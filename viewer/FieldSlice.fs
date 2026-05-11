module FieldSlice

// Field-line overlay for blocks whose `Visibility = VFieldLines`. Per
// such block we render a world-space quad on the block's `SlicePlane`;
// the fragment shader evaluates the block's MathIR SDF (compiled to
// WGSL via `Server.Lang.MathIrWgsl`) and draws iso-contour lines plus a
// thick zero-line, alpha-blended over the surface render and writing
// `frag_depth` so sketches behind the slice z-test correctly.
//
// One pipeline per *set* of visible field-line block IDs — recompiled
// whenever a block enters/leaves the set or the underlying IR changes.
// The pipeline's fragment shader dispatches on a per-vertex slice index
// to one of N `block_<id>(p)` entry functions emitted by `MathIrWgsl`.

open Fable.Core
open Fable.Core.JsInterop
open Browser.Types
open Server
open Server.Lang
open WebGPU

// ── Buffer layout ──────────────────────────────────────────────────────

// Each slice quad emits 6 vertices (two triangles). Per vertex: pos(3) +
// info(4). info.x = slice index used by the fragment shader to switch
// on block; remaining lanes reserved.
let private FLOATS_PER_VERT = 7
let private VERTS_PER_SLICE = 6
let private MIN_STORAGE_BYTES = 32

// ── Public type ────────────────────────────────────────────────────────

/// One block worth of slice input. `EntryName` is the WGSL function name
/// the fragment shader calls (`block_<blockId>`).
type SliceInput =
    { BlockId:   Notebook.BlockId
      EntryName: string
      Expr:      MathIr.Expr
      Plane:     Notebook.SlicePlane }

type FieldSlice =
    private
        { Scene: Scene.Scene
          PipelineLayout: IGPUPipelineLayout
          SlotBindGroupLayout: IGPUBindGroupLayout
          mutable Pipeline: IGPURenderPipeline option
          /// Cache key: the WGSL source we last built a pipeline for. If
          /// the same source comes back next frame we keep the pipeline.
          mutable PipelineWgsl: string option
          mutable SlotBuffer: IGPUBuffer
          mutable SlotBindGroup: IGPUBindGroup
          mutable SlotCapacityFloats: int
          mutable VertexBuffer: IGPUBuffer option
          mutable VertexCount: int
          mutable VertexCapacityFloats: int }

// ── GPU resource helpers ───────────────────────────────────────────────

let private storageBuffer (device: IGPUDevice) (byteSize: int) : IGPUBuffer =
    device.createBuffer
        { size = max byteSize MIN_STORAGE_BYTES
          usage = GPUBufferUsage.Storage ||| GPUBufferUsage.CopyDst }

let private vertexBuffer (device: IGPUDevice) (byteSize: int) : IGPUBuffer =
    device.createBuffer
        { size = max byteSize 32
          usage = GPUBufferUsage.Vertex ||| GPUBufferUsage.CopyDst }

let private slotLayout (device: IGPUDevice) : IGPUBindGroupLayout =
    device.createBindGroupLayout
        { entries =
            [| box
                {| binding = 0
                   visibility = GPUShaderStage.Fragment
                   buffer = {| ``type`` = "read-only-storage" |} |} |] }

let private makeSlotBindGroup
        (device: IGPUDevice)
        (layout: IGPUBindGroupLayout) (buffer: IGPUBuffer) : IGPUBindGroup =
    device.createBindGroup
        { layout = layout
          entries = [| { binding = 0; resource = box { buffer = buffer } } |] }

let private nextPowerOfTwo (v: int) : int =
    let mutable n = max 16 v
    let mutable r = 1
    while r < n do r <- r * 2
    r

let private ensureSlotCapacity (fs: FieldSlice) (neededFloats: int) =
    if neededFloats <= fs.SlotCapacityFloats then () else
    let cap = nextPowerOfTwo neededFloats
    fs.SlotBuffer <- storageBuffer fs.Scene.Device (cap * 4)
    fs.SlotBindGroup <-
        makeSlotBindGroup fs.Scene.Device fs.SlotBindGroupLayout fs.SlotBuffer
    fs.SlotCapacityFloats <- cap

let private ensureVertexCapacity (fs: FieldSlice) (neededFloats: int) =
    if neededFloats <= fs.VertexCapacityFloats then () else
    let cap = nextPowerOfTwo neededFloats
    fs.VertexBuffer <- Some (vertexBuffer fs.Scene.Device (cap * 4))
    fs.VertexCapacityFloats <- cap

// ── Shader generation ──────────────────────────────────────────────────

/// Build the per-block entry-name. Encodes the BlockId so multiple
/// blocks with the same name still get distinct functions.
let private entryNameOf (id: Notebook.BlockId) : string = sprintf "block_%d" id

/// Build the combined WGSL for the current set of slices. Pulls per-
/// block evaluators from `MathIrWgsl.emitMany`, glues on a fixed
/// camera + dispatch + fragment shell.
let private buildWgsl (ir: MathIr.MathIR) (inputs: SliceInput list) : string =
    let sb = System.Text.StringBuilder()

    sb.AppendLine "struct Camera {" |> ignore
    sb.AppendLine "  eye: vec3<f32>, _p0: f32," |> ignore
    sb.AppendLine "  forward: vec3<f32>, _p1: f32," |> ignore
    sb.AppendLine "  right: vec3<f32>, view_half_h: f32," |> ignore
    sb.AppendLine "  up: vec3<f32>, aspect: f32," |> ignore
    sb.AppendLine "}" |> ignore
    sb.AppendLine "struct Slots { v: array<f32> }" |> ignore
    sb.AppendLine "@group(0) @binding(0) var<uniform> cam: Camera;" |> ignore
    sb.AppendLine "@group(1) @binding(0) var<storage, read> slots: Slots;" |> ignore

    let entries =
        inputs
        |> List.map (fun s -> entryNameOf s.BlockId, s.Expr)
    sb.AppendLine (MathIrWgsl.emitMany ir entries) |> ignore

    // Orthographic projection mirroring `Background.wgsl`. Matches the
    // sketch overlay pipelines so the slice's `frag_depth` z-tests cleanly
    // against everything else.
    sb.AppendLine "fn project_world(pos: vec3<f32>) -> vec4<f32> {" |> ignore
    sb.AppendLine "  let f = cam.forward;" |> ignore
    sb.AppendLine "  let r = cam.right;" |> ignore
    sb.AppendLine "  let u = cam.up;" |> ignore
    sb.AppendLine "  let view = mat4x4<f32>(" |> ignore
    sb.AppendLine "    vec4<f32>(r.x, u.x, -f.x, 0.0)," |> ignore
    sb.AppendLine "    vec4<f32>(r.y, u.y, -f.y, 0.0)," |> ignore
    sb.AppendLine "    vec4<f32>(r.z, u.z, -f.z, 0.0)," |> ignore
    sb.AppendLine "    vec4<f32>(-dot(r, cam.eye), -dot(u, cam.eye), dot(f, cam.eye), 1.0)," |> ignore
    sb.AppendLine "  );" |> ignore
    sb.AppendLine "  let near = 0.001;" |> ignore
    sb.AppendLine "  let far = 1000.0;" |> ignore
    sb.AppendLine "  let h = cam.view_half_h;" |> ignore
    sb.AppendLine "  let w = cam.aspect * h;" |> ignore
    sb.AppendLine "  let proj = mat4x4<f32>(" |> ignore
    sb.AppendLine "    vec4<f32>(1.0 / w, 0.0, 0.0, 0.0)," |> ignore
    sb.AppendLine "    vec4<f32>(0.0, 1.0 / h, 0.0, 0.0)," |> ignore
    sb.AppendLine "    vec4<f32>(0.0, 0.0, -1.0 / (far - near), 0.0)," |> ignore
    sb.AppendLine "    vec4<f32>(0.0, 0.0, -near / (far - near), 1.0)," |> ignore
    sb.AppendLine "  );" |> ignore
    sb.AppendLine "  return proj * view * vec4<f32>(pos, 1.0);" |> ignore
    sb.AppendLine "}" |> ignore

    sb.AppendLine "struct VIn { @location(0) pos: vec3<f32>, @location(1) info: vec4<f32> };" |> ignore
    sb.AppendLine "struct VOut {" |> ignore
    sb.AppendLine "  @builtin(position) clip: vec4<f32>," |> ignore
    sb.AppendLine "  @location(0) world_pos: vec3<f32>," |> ignore
    sb.AppendLine "  @location(1) info: vec4<f32>," |> ignore
    sb.AppendLine "};" |> ignore
    sb.AppendLine "@vertex fn vs_main(v: VIn) -> VOut {" |> ignore
    sb.AppendLine "  var o: VOut;" |> ignore
    sb.AppendLine "  o.clip = project_world(v.pos);" |> ignore
    sb.AppendLine "  o.world_pos = v.pos;" |> ignore
    sb.AppendLine "  o.info = v.info;" |> ignore
    sb.AppendLine "  return o;" |> ignore
    sb.AppendLine "}" |> ignore

    sb.AppendLine "struct FOut {" |> ignore
    sb.AppendLine "  @location(0) color: vec4<f32>," |> ignore
    sb.AppendLine "  @builtin(frag_depth) depth: f32," |> ignore
    sb.AppendLine "}" |> ignore
    sb.AppendLine "@fragment fn fs_main(f: VOut) -> FOut {" |> ignore
    sb.AppendLine "  let slice_idx = i32(f.info.x + 0.5);" |> ignore
    sb.AppendLine "  let p = f.world_pos;" |> ignore
    sb.AppendLine "  var d: f32;" |> ignore
    sb.AppendLine "  switch slice_idx {" |> ignore
    inputs |> List.iteri (fun i s ->
        sb.AppendLine (sprintf "    case %d: { d = %s(p); }" i (entryNameOf s.BlockId)) |> ignore)
    sb.AppendLine "    default: { d = 1e10; }" |> ignore
    sb.AppendLine "  }" |> ignore
    // Iso-contour shading — port of `core/Solve/GpuFieldSlice.fs` on main.
    // `fwidth` makes line width screen-space-uniform; the distance fade
    // keeps far-away iso-lines from cluttering the view.
    // Adaptive field-line spacing. `fwidth(d)` estimates field units per
    // pixel; as the camera zooms in this drops, so quantizing the target
    // spacing by powers of two reveals denser contours at stable LOD steps.
    sb.AppendLine "  let target_px: f32 = 36.0;" |> ignore
    sb.AppendLine "  let value_per_px = max(fwidth(d), 1.0e-6);" |> ignore
    sb.AppendLine "  let target_spacing = clamp(value_per_px * target_px, 0.03125, 8.0);" |> ignore
    sb.AppendLine "  let iso_spacing: f32 = exp2(floor(log2(target_spacing)));" |> ignore
    sb.AppendLine "  let abs_d = abs(d);" |> ignore
    sb.AppendLine "  let dd = max(value_per_px, 0.0001);" |> ignore
    sb.AppendLine "  let zero_width = max(dd * 1.6, 0.012);" |> ignore
    sb.AppendLine "  let iso_width = max(dd * 1.25, 0.01);" |> ignore
    sb.AppendLine "  let iso_frac = abs(fract(abs_d / iso_spacing + 0.5) - 0.5) * 2.0 * iso_spacing;" |> ignore
    sb.AppendLine "  let iso_line = 1.0 - smoothstep(0.0, iso_width, iso_frac);" |> ignore
    sb.AppendLine "  let zero_line = 1.0 - smoothstep(0.0, zero_width, abs_d);" |> ignore
    sb.AppendLine "  let fade_start = iso_spacing * 4.0;" |> ignore
    sb.AppendLine "  let fade_end = iso_spacing * 10.0;" |> ignore
    sb.AppendLine "  let dist_fade = 1.0 - smoothstep(fade_start, fade_end, abs_d);" |> ignore
    sb.AppendLine "  let final_alpha = max(iso_line * 0.42 * dist_fade, zero_line);" |> ignore
    sb.AppendLine "  if (final_alpha < 0.005) { discard; }" |> ignore
    sb.AppendLine "  let neg_col = vec3<f32>(1.0, 0.35, 0.25);" |> ignore
    sb.AppendLine "  let pos_col = vec3<f32>(0.25, 0.55, 1.0);" |> ignore
    sb.AppendLine "  let base_col = select(pos_col, neg_col, d < 0.0);" |> ignore
    sb.AppendLine "  let col = mix(base_col, vec3<f32>(1.0), max(zero_line, iso_line * 0.22));" |> ignore
    sb.AppendLine "  var out: FOut;" |> ignore
    sb.AppendLine "  out.color = vec4<f32>(col, final_alpha);" |> ignore
    sb.AppendLine "  out.depth = f.clip.z / f.clip.w;" |> ignore
    sb.AppendLine "  return out;" |> ignore
    sb.AppendLine "}" |> ignore

    sb.ToString()

// ── Pipeline build ─────────────────────────────────────────────────────

let private buildPipeline
        (scene: Scene.Scene)
        (layout: IGPUPipelineLayout)
        (wgsl: string) : IGPURenderPipeline =
    let shader = scene.Device.createShaderModule { code = wgsl }
    let vertexLayout =
        {| arrayStride = FLOATS_PER_VERT * 4
           stepMode = "vertex"
           attributes =
            [| {| shaderLocation = 0; offset = 0;  format = "float32x3" |}
               {| shaderLocation = 1; offset = 12; format = "float32x4" |} |] |}
    scene.Device.createRenderPipeline
        (box
            {| layout = layout
               vertex =
                {| ``module`` = shader
                   entryPoint = "vs_main"
                   buffers = [| box vertexLayout |] |}
               fragment =
                {| ``module`` = shader
                   entryPoint = "fs_main"
                   targets =
                    [| {| format = scene.Format
                          blend =
                           {| color =
                               {| srcFactor = "src-alpha"
                                  dstFactor = "one-minus-src-alpha"
                                  operation = "add" |}
                              alpha =
                               {| srcFactor = "one"
                                  dstFactor = "one-minus-src-alpha"
                                  operation = "add" |} |} |} |] |}
               primitive = {| topology = "triangle-list" |}
               // Depth-write on. Overlays in front of the slice cover its
               // pixels; geometry behind it z-fails against the contour
               // surface. Slice ↔ slice ordering is z-buffer-correct.
               depthStencil =
                {| format = "depth24plus"
                   depthWriteEnabled = true
                   depthCompare = "less" |} |})

// ── Vertex buffer build ────────────────────────────────────────────────

let private appendQuad (out: ResizeArray<float32>) (viewHalfH: float) (sliceIdx: int) (plane: Notebook.SlicePlane) =
    // Match the main-branch overlay behavior: make the slice quad camera-
    // sized so the field covers the viewport and the quad edge stays hidden.
    let s = float32 (max plane.Extent (max 40.0 (viewHalfH * 12.0)))
    let o = plane.Origin
    let ux = plane.AxisX
    let uy = plane.AxisY
    // (u, v) ∈ {-1, +1} → world-space corner along the plane axes.
    let cornerX u v = float32 o.X + u * s * float32 ux.X + v * s * float32 uy.X
    let cornerY u v = float32 o.Y + u * s * float32 ux.Y + v * s * float32 uy.Y
    let cornerZ u v = float32 o.Z + u * s * float32 ux.Z + v * s * float32 uy.Z
    let info0 = float32 sliceIdx
    let push (u: float32) (v: float32) =
        out.Add (cornerX u v)
        out.Add (cornerY u v)
        out.Add (cornerZ u v)
        out.Add info0
        out.Add 0.0f
        out.Add 0.0f
        out.Add 0.0f
    // Two triangles forming the quad: (-1,-1)(+1,-1)(+1,+1) and
    // (-1,-1)(+1,+1)(-1,+1).
    push -1.0f -1.0f
    push  1.0f -1.0f
    push  1.0f  1.0f
    push -1.0f -1.0f
    push  1.0f  1.0f
    push -1.0f  1.0f

// ── Public API ─────────────────────────────────────────────────────────

let create (scene: Scene.Scene) : FieldSlice =
    let slotBindLayout = slotLayout scene.Device
    let pipelineLayout =
        scene.Device.createPipelineLayout
            { bindGroupLayouts =
                [| scene.CameraBindGroupLayout
                   slotBindLayout |] }
    let slotBuffer = storageBuffer scene.Device MIN_STORAGE_BYTES
    { Scene = scene
      PipelineLayout = pipelineLayout
      SlotBindGroupLayout = slotBindLayout
      Pipeline = None
      PipelineWgsl = None
      SlotBuffer = slotBuffer
      SlotBindGroup = makeSlotBindGroup scene.Device slotBindLayout slotBuffer
      SlotCapacityFloats = MIN_STORAGE_BYTES / 4
      VertexBuffer = None
      VertexCount = 0
      VertexCapacityFloats = 0 }

/// Refresh per-frame state. `inputs` is the set of currently-VFieldLines
/// blocks (with their MathIR expression + plane). `slotValues` is the
/// notebook's slot vector — its layout must match the slot ids the
/// emitted shader indexes.
let update
        (fs: FieldSlice)
        (ir: MathIr.MathIR option)
        (inputs: SliceInput list)
        (slotValues: float array) =
    match ir, inputs with
    | None, _ | _, [] ->
        // Nothing to render this frame — drop the pipeline so we don't
        // pay an old shader's compile cost when blocks return.
        fs.Pipeline <- None
        fs.PipelineWgsl <- None
        fs.VertexCount <- 0
    | Some ir, _ ->
        let wgsl = buildWgsl ir inputs
        if fs.PipelineWgsl <> Some wgsl then
            fs.PipelineWgsl <- Some wgsl
            fs.Pipeline <- Some (buildPipeline fs.Scene fs.PipelineLayout wgsl)

        // Re-upload slot values every frame — slider drags swap them in
        // place. A zero-length notebook compiles fine but has nothing to
        // index, so skip the upload to keep WebGPU happy.
        if slotValues.Length > 0 then
            let data = slotValues |> Array.map float32
            ensureSlotCapacity fs data.Length
            WebGPU.writeFloat32 fs.Scene.Device.queue fs.SlotBuffer 0 data

        let viewHalfH = Camera.viewHalfH fs.Scene.Camera
        let verts = ResizeArray<float32>()
        inputs |> List.iteri (fun i s -> appendQuad verts viewHalfH i s.Plane)
        let data = verts.ToArray()
        ensureVertexCapacity fs data.Length
        match fs.VertexBuffer with
        | Some buf ->
            WebGPU.writeFloat32 fs.Scene.Device.queue buf 0 data
            fs.VertexCount <- data.Length / FLOATS_PER_VERT
        | None ->
            fs.VertexCount <- 0

let draw (fs: FieldSlice) (pass: IGPURenderPassEncoder) =
    match fs.Pipeline, fs.VertexBuffer with
    | Some pipe, Some vbuf when fs.VertexCount > 0 ->
        pass.setPipeline pipe
        pass.setBindGroup(0, fs.Scene.CameraBindGroup)
        pass.setBindGroup(1, fs.SlotBindGroup)
        pass.setVertexBuffer(0, vbuf)
        pass.draw fs.VertexCount
    | _ -> ()
