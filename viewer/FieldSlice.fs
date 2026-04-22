module FieldSlice

// Field iso-line overlay. Renders a quad in world space for every
// enabled slice (one per `FieldSliceView` in `ViewerState.FieldSlices`),
// evaluating its surface's SDF per pixel and shading contour lines at
// unit intervals plus a thick zero-line. Writes `frag_depth` through the
// same ortho projection as every other pipeline, so slices z-test
// against the field surface and sketches.
//
// Runs in both viewer modes (Interval kernel and Raymarch) — the slice
// overlay is independent of which background renderer is active.

open Fable.Core
open Fable.Core.JsInterop
open Browser.Types
open Server
open WebGPU

// Each vertex = pos(3) + info(4). info.x carries the surface index the
// shader evaluates; remaining lanes are reserved.
let private FLOATS_PER_VERT = 7
let private VERTS_PER_SLICE = 6 // two triangles forming one quad
let private MIN_STORAGE_BYTES = 32

type FieldSlice =
    private
        { Scene: Scene.Scene
          PipelineLayout: IGPUPipelineLayout
          SlotBindGroupLayout: IGPUBindGroupLayout
          mutable Pipeline: IGPURenderPipeline option
          mutable PipelineWgsl: string option
          mutable SlotBuffer: IGPUBuffer
          mutable SlotBindGroup: IGPUBindGroup
          mutable SlotCapacityFloats: int
          mutable VertexBuffer: IGPUBuffer option
          mutable VertexCount: int
          mutable VertexCapacityFloats: int }

// ── Buffers / bind groups ──────────────────────────────────────────────

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

// ── Pipeline ───────────────────────────────────────────────────────────

let private buildPipeline
        (scene: Scene.Scene)
        (layout: IGPUPipelineLayout)
        (wgsl: string) : IGPURenderPipeline =
    let shader = scene.Device.createShaderModule { code = wgsl }
    let vertexLayout =
        {| arrayStride = FLOATS_PER_VERT * 4
           stepMode = "vertex"
           attributes =
            [| {| shaderLocation = 0; offset = 0; format = "float32x3" |}
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
               // Write depth so overlay geometry z-tests correctly; the
               // slice surface itself is usually cheaper to see on top of
               // the field, but sketch lines *behind* it should still be
               // hidden, which requires a real depth comparison.
               depthStencil =
                {| format = "depth24plus"
                   depthWriteEnabled = true
                   depthCompare = "less" |} |})

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

// Emit 6 vertices (two triangles) for one slice quad. Vertex positions
// are world-space corners of the plane: origin ± extent along PlaneX/Y.
// The extent is camera-driven so the plane always covers the viewport
// with slack — the shader's distance fade (scaled to `view_half_h`)
// takes over well inside the quad so its edge is never visible.
let private appendQuad (out: ResizeArray<float32>) (viewHalfH: float) (slice: FieldSliceView) =
    let s = float32 (max 40.0 (viewHalfH * 12.0))
    let o = slice.PlaneOrigin
    let ux = slice.PlaneX
    let uy = slice.PlaneY
    // corner(u, v) in local plane coords → world position.
    let corner (u: float32) (v: float32) : struct (float32 * float32 * float32) =
        struct (float32 o.X + u * s * float32 ux.X + v * s * float32 uy.X,
                float32 o.Y + u * s * float32 ux.Y + v * s * float32 uy.Y,
                float32 o.Z + u * s * float32 ux.Z + v * s * float32 uy.Z)
    let info0 = float32 slice.SurfaceIndex
    let push (struct (x, y, z): struct (float32 * float32 * float32)) =
        out.Add x
        out.Add y
        out.Add z
        out.Add info0
        out.Add 0.0f
        out.Add 0.0f
        out.Add 0.0f
    // Triangle 1: (-1, -1) → (+1, -1) → (+1, +1)
    push (corner -1.0f -1.0f)
    push (corner  1.0f -1.0f)
    push (corner  1.0f  1.0f)
    // Triangle 2: (-1, -1) → (+1, +1) → (-1, +1)
    push (corner -1.0f -1.0f)
    push (corner  1.0f  1.0f)
    push (corner -1.0f  1.0f)

let update (fs: FieldSlice) (state: EditorState) (viewerState: ViewerState) =
    // Rebuild pipeline if the set of surfaces changed (shader switches
    // on surface index, so adding/removing a surface changes the source).
    let wgsl = GpuFieldSlice.combinedFieldSliceWgsl state.Compiled.Surfaces
    if wgsl <> fs.PipelineWgsl then
        fs.PipelineWgsl <- wgsl
        fs.Pipeline <-
            match wgsl with
            | Some src -> Some (buildPipeline fs.Scene fs.PipelineLayout src)
            | None -> None

    // Re-upload slot values every frame (slider drags swap SlotValues).
    if state.SlotValues.Length > 0 then
        let data = state.SlotValues |> Array.map float32
        ensureSlotCapacity fs data.Length
        WebGPU.writeFloat32 fs.Scene.Device.queue fs.SlotBuffer 0 data

    // Rebuild the vertex buffer whenever slice set changes. Cheap enough
    // to do every frame (usually ≤ a handful of slices). Quad size
    // tracks the current zoom level so the plane effectively spans
    // the viewport at any scale.
    let slices = viewerState.FieldSlices
    if slices.IsEmpty then
        fs.VertexCount <- 0
    else
        let viewHalfH = Camera.viewHalfH fs.Scene.Camera
        let verts = ResizeArray<float32>()
        for slice in slices do appendQuad verts viewHalfH slice
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
