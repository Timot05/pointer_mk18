module PickCompute

// Compute-shader picking. Replaces the raster pick pass with a 5×5
// grid-sampled ray-march on the GPU that tests the cursor's
// neighborhood against every sketch entity + frame origin using SDF
// distance functions. Returns a list of candidate pickables ordered by
// priority so the CPU can pick the right one even when multiple shapes
// overlap under the cursor. Ported from the pre-F# TS viewer (commit
// c2434f3) with adjustments for the orthographic camera.
//
// Layout:
//   * One compute dispatch per visible sketch (25 threads each, one
//     per pixel in the 5×5 grid). Each thread writes into a distinct
//     slot in the shared `samples` storage buffer using a per-dispatch
//     `sample_base` offset from the `PickState` uniform.
//   * One additional dispatch for world-space frame origins.
//   * `PickState` uses dynamic-offset binding so per-dispatch writes
//     don't stomp each other (same pattern as the frame uniforms).
//
// Geometry feeds:
//   * points   — every `PickPoint` in every sketch's pickables.
//   * lines    — every `PickLine` (and — later — arc samples).
//   * circles / loops / labels — reserved, currently uploaded empty.
//   * origins  — every `PickFrameOrigin` in world space.

open Fable.Core
open Fable.Core.JsInterop
open Server
open WebGPU

// ── Constants ────────────────────────────────────────────────────────

let private PICK_GRID = 5
let SAMPLES_PER_DISPATCH = PICK_GRID * PICK_GRID
/// Max simultaneous dispatches we pre-allocate sample-buffer slots for.
/// One per visible sketch + one for frames + one for the selected
/// translate gizmo. 64 leaves plenty of room while keeping readback tiny.
let private MAX_DISPATCHES = 64
/// Per-pick-state slot size — 256-byte alignment is the WebGPU min for
/// dynamic uniform offsets.
let private PICK_STATE_STRIDE = 256
let private NO_HIT : uint32 = 0xFFFFFFFFu
/// Screen-pixel radius used for point picks on the sketch plane. Must
/// match the "fat pick disc" behavior of the old raster pick (28 px).
let private POINT_PICK_RADIUS_PX = 28.0f
/// Screen-pixel threshold for line / circle / arc picks. The compute
/// shader's line SDF is measured in pixels (world distance ÷ world-
/// per-pixel), and this is the max distance at which a segment counts
/// as a hit. Fat enough that picking 1-pixel lines is easy, narrow
/// enough that nearby geometry doesn't steal the click.
let private LINE_PICK_THICKNESS_PX = 14.0f

// ── State record ─────────────────────────────────────────────────────

/// One sketch's set of geometry storage buffers. Allocated lazily the
/// first time a sketch is seen; subsequent frames reuse and grow.
type private SketchBuffers =
    { mutable Points: IGPUBuffer
      mutable PointsBytes: int
      mutable Lines: IGPUBuffer
      mutable LinesBytes: int
      mutable Circles: IGPUBuffer
      mutable CirclesBytes: int
      mutable Loops: IGPUBuffer
      mutable LoopsBytes: int
      mutable Labels: IGPUBuffer
      mutable LabelsBytes: int
      mutable BindGroup: IGPUBindGroup option }

type PickCompute =
    private
        { Scene: Scene.Scene
          // ── Pipelines + layouts ──
          SketchPipeline: IGPUComputePipeline
          FramePipeline: IGPUComputePipeline
          GizmoPipeline: IGPUComputePipeline
          SketchBindGroupLayout: IGPUBindGroupLayout
          FrameBindGroupLayout: IGPUBindGroupLayout
          GizmoBindGroupLayout: IGPUBindGroupLayout
          // ── Uniforms ──
          PickStateBuffer: IGPUBuffer
          // ── Per-sketch geometry ──
          SketchBuffers: System.Collections.Generic.Dictionary<string, SketchBuffers>
          // ── Frame geometry (global) ──
          mutable FrameOrigins: IGPUBuffer
          mutable FrameOriginsBytes: int
          mutable FrameBindGroup: IGPUBindGroup option
          // ── Selected translate gizmo geometry (ephemeral) ──
          mutable GizmoAxes: IGPUBuffer
          mutable GizmoAxesBytes: int
          mutable GizmoPlanes: IGPUBuffer
          mutable GizmoPlanesBytes: int
          mutable GizmoBindGroup: IGPUBindGroup option
          // ── Samples output + staging readback ──
          SamplesBuffer: IGPUBuffer
          SamplesStaging: IGPUBuffer
          SamplesBytes: int
          // ── Per-frame state ──
          mutable LastSketchOrder: (string * int) list
          mutable LastFrameDispatchIndex: int option
          mutable LastGizmoDispatchIndex: int option
          mutable InFlight: bool }

// ── Buffer helpers ───────────────────────────────────────────────────

let private emptyStorage (device: IGPUDevice) : IGPUBuffer =
    device.createBuffer
        { size = 16
          usage = GPUBufferUsage.Storage ||| GPUBufferUsage.CopyDst }

let private uniformBuffer (device: IGPUDevice) (size: int) : IGPUBuffer =
    device.createBuffer
        { size = size
          usage = GPUBufferUsage.Uniform ||| GPUBufferUsage.CopyDst }

let private newStorage (device: IGPUDevice) (bytes: int) : IGPUBuffer =
    device.createBuffer
        { size = max 16 bytes
          usage = GPUBufferUsage.Storage ||| GPUBufferUsage.CopyDst }

let private ensureStorage (device: IGPUDevice) (buf: IGPUBuffer) (currentBytes: int) (needBytes: int) : struct (IGPUBuffer * int * bool) =
    if currentBytes >= max 16 needBytes then struct (buf, currentBytes, false)
    else
        let grown = max 256 (max needBytes (currentBytes * 2))
        let next = newStorage device grown
        struct (next, grown, true)

let private writeF32 (scene: Scene.Scene) (buf: IGPUBuffer) (data: float32[]) =
    if data.Length > 0 then
        WebGPU.writeFloat32 scene.Device.queue buf 0 data

// ── Bind group layouts ───────────────────────────────────────────────

let private sketchLayout (device: IGPUDevice) : IGPUBindGroupLayout =
    device.createBindGroupLayout
        { entries =
            [| box
                {| binding = 0
                   visibility = GPUShaderStage.Compute
                   buffer = {| ``type`` = "uniform" |} |}
               box
                {| binding = 1
                   visibility = GPUShaderStage.Compute
                   buffer = {| ``type`` = "uniform"; hasDynamicOffset = true |} |}
               box
                {| binding = 2
                   visibility = GPUShaderStage.Compute
                   buffer = {| ``type`` = "uniform"; hasDynamicOffset = true |} |}
               box
                {| binding = 3
                   visibility = GPUShaderStage.Compute
                   buffer = {| ``type`` = "read-only-storage" |} |}
               box
                {| binding = 4
                   visibility = GPUShaderStage.Compute
                   buffer = {| ``type`` = "read-only-storage" |} |}
               box
                {| binding = 5
                   visibility = GPUShaderStage.Compute
                   buffer = {| ``type`` = "read-only-storage" |} |}
               box
                {| binding = 6
                   visibility = GPUShaderStage.Compute
                   buffer = {| ``type`` = "read-only-storage" |} |}
               box
                {| binding = 7
                   visibility = GPUShaderStage.Compute
                   buffer = {| ``type`` = "read-only-storage" |} |}
               box
                {| binding = 8
                   visibility = GPUShaderStage.Compute
                   buffer = {| ``type`` = "storage" |} |} |] }

let private frameLayout (device: IGPUDevice) : IGPUBindGroupLayout =
    device.createBindGroupLayout
        { entries =
            [| box
                {| binding = 0
                   visibility = GPUShaderStage.Compute
                   buffer = {| ``type`` = "uniform" |} |}
               box
                {| binding = 1
                   visibility = GPUShaderStage.Compute
                   buffer = {| ``type`` = "uniform"; hasDynamicOffset = true |} |}
               box
                {| binding = 2
                   visibility = GPUShaderStage.Compute
                   buffer = {| ``type`` = "read-only-storage" |} |}
               box
                {| binding = 3
                   visibility = GPUShaderStage.Compute
                   buffer = {| ``type`` = "storage" |} |} |] }

let private gizmoLayout (device: IGPUDevice) : IGPUBindGroupLayout =
    device.createBindGroupLayout
        { entries =
            [| box
                {| binding = 0
                   visibility = GPUShaderStage.Compute
                   buffer = {| ``type`` = "uniform" |} |}
               box
                {| binding = 1
                   visibility = GPUShaderStage.Compute
                   buffer = {| ``type`` = "uniform"; hasDynamicOffset = true |} |}
               box
                {| binding = 2
                   visibility = GPUShaderStage.Compute
                   buffer = {| ``type`` = "read-only-storage" |} |}
               box
                {| binding = 3
                   visibility = GPUShaderStage.Compute
                   buffer = {| ``type`` = "read-only-storage" |} |}
               box
                {| binding = 4
                   visibility = GPUShaderStage.Compute
                   buffer = {| ``type`` = "storage" |} |} |] }

// ── Construction ─────────────────────────────────────────────────────

/// Pipeline layouts bind the `PickState` buffer at binding-2 (sketch)
/// / binding-1 (frame) with a dynamic offset sized to match one 256-B
/// slot. `size = 48` covers the 7 u32/f32 fields + padding.
let private bindingResourceWithSize (buffer: IGPUBuffer) (size: int) : obj =
    box {| buffer = buffer; offset = 0; size = size |}

let create (scene: Scene.Scene) : PickCompute =
    let device = scene.Device
    let sketchBindLayout = sketchLayout device
    let frameBindLayout = frameLayout device
    let gizmoBindLayout = gizmoLayout device

    let sketchPipelineLayout =
        device.createPipelineLayout
            { bindGroupLayouts = [| sketchBindLayout |] }
    let framePipelineLayout =
        device.createPipelineLayout
            { bindGroupLayouts = [| frameBindLayout |] }
    let gizmoPipelineLayout =
        device.createPipelineLayout
            { bindGroupLayouts = [| gizmoBindLayout |] }

    let sketchShader =
        device.createShaderModule { code = Shaders.pickCompute }
    let frameShader =
        device.createShaderModule { code = Shaders.framePickCompute }
    let gizmoShader =
        device.createShaderModule { code = Shaders.gizmoPickCompute }

    let sketchPipeline =
        device.createComputePipeline
            (box
                {| layout = sketchPipelineLayout
                   compute = {| ``module`` = sketchShader; entryPoint = "cs_main" |} |})
    let framePipeline =
        device.createComputePipeline
            (box
                {| layout = framePipelineLayout
                   compute = {| ``module`` = frameShader; entryPoint = "cs_main" |} |})
    let gizmoPipeline =
        device.createComputePipeline
            (box
                {| layout = gizmoPipelineLayout
                   compute = {| ``module`` = gizmoShader; entryPoint = "cs_main" |} |})

    let pickStateBuffer = uniformBuffer device (PICK_STATE_STRIDE * MAX_DISPATCHES)
    let samplesBytes = MAX_DISPATCHES * SAMPLES_PER_DISPATCH * 16
    let samplesBuffer =
        device.createBuffer
            { size = samplesBytes
              usage =
                GPUBufferUsage.Storage
                ||| GPUBufferUsage.CopySrc
                ||| GPUBufferUsage.CopyDst }
    let samplesStaging =
        device.createBuffer
            { size = samplesBytes
              usage = GPUBufferUsage.CopyDst ||| GPUBufferUsage.MapRead }

    { Scene = scene
      SketchPipeline = sketchPipeline
      FramePipeline = framePipeline
      GizmoPipeline = gizmoPipeline
      SketchBindGroupLayout = sketchBindLayout
      FrameBindGroupLayout = frameBindLayout
      GizmoBindGroupLayout = gizmoBindLayout
      PickStateBuffer = pickStateBuffer
      SketchBuffers = System.Collections.Generic.Dictionary()
      FrameOrigins = emptyStorage device
      FrameOriginsBytes = 16
      FrameBindGroup = None
      GizmoAxes = emptyStorage device
      GizmoAxesBytes = 16
      GizmoPlanes = emptyStorage device
      GizmoPlanesBytes = 16
      GizmoBindGroup = None
      SamplesBuffer = samplesBuffer
      SamplesStaging = samplesStaging
      SamplesBytes = samplesBytes
      LastSketchOrder = []
      LastFrameDispatchIndex = None
      LastGizmoDispatchIndex = None
      InFlight = false }

// ── Per-sketch buffer management ─────────────────────────────────────

let private getOrCreateSketchBuffers (pc: PickCompute) (sketchId: string) : SketchBuffers =
    match pc.SketchBuffers.TryGetValue sketchId with
    | true, existing -> existing
    | false, _ ->
        let d = pc.Scene.Device
        let s =
            { Points = emptyStorage d; PointsBytes = 16
              Lines = emptyStorage d; LinesBytes = 16
              Circles = emptyStorage d; CirclesBytes = 16
              Loops = emptyStorage d; LoopsBytes = 16
              Labels = emptyStorage d; LabelsBytes = 16
              BindGroup = None }
        pc.SketchBuffers.[sketchId] <- s
        s

/// Upload `data` into `slot`, growing the buffer if needed. Returns
/// whether the buffer was reallocated (so the bind group must be
/// rebuilt).
let private uploadMaybeGrow (scene: Scene.Scene) (data: float32[])
        (buf: IGPUBuffer) (bytes: int) : struct (IGPUBuffer * int * bool) =
    let needed = data.Length * 4
    let struct (next, newBytes, grew) = ensureStorage scene.Device buf bytes needed
    writeF32 scene next data
    struct (next, newBytes, grew)

/// Like `uploadMaybeGrow` but also writes a repeating 4-float sentinel
/// pattern over the unused tail of the buffer. Used for frame origins
/// where zero-init would look like a real pickable at the world origin.
let private uploadWithSentinelPad
        (scene: Scene.Scene) (data: float32[])
        (sentinel: float32[])
        (buf: IGPUBuffer) (bytes: int) : struct (IGPUBuffer * int * bool) =
    let needed = data.Length * 4
    let struct (next, newBytes, grew) = ensureStorage scene.Device buf bytes needed
    writeF32 scene next data
    let tailBytes = newBytes - needed
    if tailBytes >= sentinel.Length * 4 then
        let tailFloats = tailBytes / 4
        let pad =
            Array.init tailFloats (fun i -> sentinel.[i % sentinel.Length])
        WebGPU.writeFloat32 scene.Device.queue next needed pad
    struct (next, newBytes, grew)

/// Frame-origin sentinel: a position so far off any realistic camera
/// that `project_world` lands outside the 20-pixel hit radius. Keeps
/// empty / oversized origin buffers from producing phantom pickId=0
/// hits at the world origin.
let private FRAME_ORIGIN_SENTINEL : float32[] =
    [| 1.0e30f; 1.0e30f; 1.0e30f; 0.0f |]

/// Gizmo-axis sentinel: one complete 2-vec4 axis entry far outside
/// the camera. Pick id is irrelevant because it should never be hit.
let private GIZMO_AXIS_SENTINEL : float32[] =
    [| 1.0e30f; 1.0e30f; 1.0e30f; 1.0f
       1.0f; 0.0f; 0.0f; 0.0f |]

/// Gizmo-plane sentinel: one complete 3-vec4 plane entry far outside
/// the camera. Keeps oversized storage buffers from exposing stale
/// plane handles after growth.
let private GIZMO_PLANE_SENTINEL : float32[] =
    [| 1.0e30f; 1.0e30f; 1.0e30f; 0.0f
       1.0f; 0.0f; 0.0f; 16.0f
       0.0f; 1.0f; 0.0f; 44.0f |]

// ── Geometry array builders ──────────────────────────────────────────
//
// Each builder converts the existing raster-pick buffer format emitted
// by `SketchOverlayRender` into the 2-vec4 interleaved format the
// compute shader expects. Keeps the authoring logic (slot resolution,
// arc sampling, loop triangulation) in one place and leaves this
// module focused on GPU orchestration.

/// `buildSketchPointPickBuffer` already returns (cx, cy, radius_px,
/// pickId) per instance — an exact match for the compute shader's
/// `points` layout.
let private buildPointsArray
        (sketchId: string)
        (entities: RenderEntity list)
        (slotLookup: Map<SlotRef, Slot>)
        (paramValues: float[])
        (pickables: Pickable list) : float32[] =
    SketchOverlayRender.buildSketchPointPickBuffer
        sketchId entities slotLookup paramValues pickables

/// `buildSketchPickLineBuffer` flattens lines, circles and arcs into
/// `(x1, y1, x2, y2, pickId)` segments. Expand each into the shader's
/// `[geom, info]` pair.
let private buildLinesArray
        (sketchId: string)
        (entities: RenderEntity list)
        (slotLookup: Map<SlotRef, Slot>)
        (paramValues: float[])
        (pickables: Pickable list) : float32[] =
    let src =
        SketchOverlayRender.buildSketchPickLineBuffer
            sketchId entities slotLookup paramValues pickables
    let count = src.Length / 5
    let out = Array.zeroCreate (count * 8)
    for i in 0 .. count - 1 do
        let s = i * 5
        let d = i * 8
        out.[d]     <- src.[s]
        out.[d + 1] <- src.[s + 1]
        out.[d + 2] <- src.[s + 2]
        out.[d + 3] <- src.[s + 3]
        out.[d + 4] <- LINE_PICK_THICKNESS_PX
        out.[d + 5] <- src.[s + 4]
        out.[d + 6] <- 0.0f
        out.[d + 7] <- 0.0f
    out

/// `buildSketchLoopPickBuffer` returns triangles as 3 × (x, y, pickId)
/// vertices = 9 floats per triangle. Flatten to the shader's 2-vec4 per
/// triangle: `[0]=(ax, ay, bx, by), [1]=(cx, cy, pickId, 0)`.
let private buildLoopsArray
        (sketchId: string)
        (sketch: ActionSketch)
        (loops: SketchLoopView list)
        (slotLookup: Map<SlotRef, Slot>)
        (paramValues: float[])
        (pickables: Pickable list) : float32[] =
    let src =
        SketchOverlayRender.buildSketchLoopPickBuffer
            sketchId sketch loops slotLookup paramValues pickables
    let count = src.Length / 9
    let out = Array.zeroCreate (count * 8)
    for i in 0 .. count - 1 do
        let s = i * 9
        let d = i * 8
        out.[d]     <- src.[s]
        out.[d + 1] <- src.[s + 1]
        out.[d + 2] <- src.[s + 3]
        out.[d + 3] <- src.[s + 4]
        out.[d + 4] <- src.[s + 6]
        out.[d + 5] <- src.[s + 7]
        out.[d + 6] <- src.[s + 2]
        out.[d + 7] <- 0.0f
    out

/// `buildSketchDimensionPickBuffer` returns (anchor_x, anchor_y,
/// radius_px, pickId) — a fat-point around the label anchor. Widen to
/// a ±radius_px square in the shader's label format:
/// `[0]=(ax, ay, -r, r), [1]=(r, -r, pickId, 0)`.
let private buildLabelsArray
        (sketchId: string)
        (sketch: ActionSketch)
        (slotLookup: Map<SlotRef, Slot>)
        (paramValues: float[])
        (pickables: Pickable list) : float32[] =
    let src =
        SketchOverlayRender.buildSketchDimensionPickBuffer
            sketchId sketch slotLookup paramValues pickables
    let count = src.Length / 4
    let out = Array.zeroCreate (count * 8)
    for i in 0 .. count - 1 do
        let s = i * 4
        let d = i * 8
        let r = src.[s + 2]
        out.[d]     <- src.[s]
        out.[d + 1] <- src.[s + 1]
        out.[d + 2] <- -r
        out.[d + 3] <- r
        out.[d + 4] <- r
        out.[d + 5] <- -r
        out.[d + 6] <- src.[s + 3]
        out.[d + 7] <- 0.0f
    out

/// Build (x, y, pickId) for every frame origin. One vec4 per frame:
/// (x, y, z, pickId). The compute shader projects each through the
/// current camera uniform.
let private buildWorldPointPickArray
        (frames: FrameView list)
        (rotateCtx: RotateGizmo.Context option)
        (pickables: Pickable list)
        (worldPerPx: float) : float32[] =
    let idByFrame =
        pickables
        |> List.choose (fun p ->
            match p with
            | PickFrameOrigin(pid, fid) -> Some (fid, pid)
            | _ -> None)
        |> Map.ofList
    let out = ResizeArray<float32>()
    for f in frames do
        match Map.tryFind f.Id idByFrame with
        | Some pid ->
            let pos = f.Transform.Trans
            out.Add(float32 pos.X)
            out.Add(float32 pos.Y)
            out.Add(float32 pos.Z)
            out.Add(float32 pid)
        | None -> ()
    match rotateCtx with
    | Some ctx ->
        let pickIdFor handle =
            pickables
            |> List.tryPick (function
                | PickGizmoHandle(pid, aid, h) when aid = ctx.ActionId && h = handle -> Some pid
                | _ -> None)
        let axisEnd =
            ctx.Origin + (Editor.rotateAxisHandlePx * worldPerPx) * ctx.AxisWorld
        let angleEnd =
            ctx.Origin
            + (Editor.rotateAngleHandlePx * worldPerPx)
              * (((cos ctx.Angle) * ctx.BasisU + (sin ctx.Angle) * ctx.BasisV).Normalized)
        match pickIdFor GRotateAxis with
        | Some pid ->
            out.Add(float32 axisEnd.X)
            out.Add(float32 axisEnd.Y)
            out.Add(float32 axisEnd.Z)
            out.Add(float32 pid)
        | None -> ()
        match pickIdFor GRotateAngle with
        | Some pid ->
            out.Add(float32 angleEnd.X)
            out.Add(float32 angleEnd.Y)
            out.Add(float32 angleEnd.Z)
            out.Add(float32 pid)
        | None -> ()
    | None -> ()
    out.ToArray()

let private axisVecs (ctx: TranslateGizmo.Context) : Vec3[] =
    [| ctx.AxisX; ctx.AxisY; ctx.AxisZ |]

let private planeVecs (ctx: TranslateGizmo.Context) : (Vec3 * Vec3)[] =
    [| ctx.AxisX, ctx.AxisY
       ctx.AxisY, ctx.AxisZ
       ctx.AxisX, ctx.AxisZ |]

/// Axis pick layout: 2 vec4 per axis:
///   (anchor.xyz, length_px), (dir.xyz, pickId)
let private buildGizmoAxesArray
        (translateCtx: TranslateGizmo.Context option)
        (halfPlaneCtx: HalfPlaneGizmo.Context option)
        (pickables: Pickable list)
        (worldPerPx: float) : float32[] =
    let out = ResizeArray<float32>()
    match translateCtx with
    | Some ctx ->
        let pickIdByAxis =
            pickables
            |> List.choose (function
                | PickGizmoHandle(pid, aid, GAxis axis) when aid = ctx.ActionId -> Some(axis, pid)
                | _ -> None)
            |> Map.ofList
        for (axisIdx, axis) in axisVecs ctx |> Array.indexed do
            match Map.tryFind axisIdx pickIdByAxis with
            | Some pid ->
                out.Add(float32 ctx.Origin.X)
                out.Add(float32 ctx.Origin.Y)
                out.Add(float32 ctx.Origin.Z)
                out.Add(TranslateGizmo.AXIS_LENGTH_PX + TranslateGizmo.ARROW_LENGTH_PX)
                out.Add(float32 axis.X)
                out.Add(float32 axis.Y)
                out.Add(float32 axis.Z)
                out.Add(float32 pid)
            | None -> ()
    | None -> ()
    match halfPlaneCtx with
    | Some ctx ->
        let pickIdFor handle =
            pickables
            |> List.tryPick (function
                | PickGizmoHandle(pid, aid, h) when aid = ctx.ActionId && h = handle -> Some pid
                | _ -> None)
        for axisIdx in 0 .. 2 do
            match pickIdFor (GHalfPlaneAxis axisIdx) with
            | Some pid ->
                let dir = HalfPlaneGizmo.localAxis axisIdx
                out.Add(0.0f)
                out.Add(0.0f)
                out.Add(0.0f)
                out.Add(HalfPlaneGizmo.AXIS_LENGTH_PX)
                out.Add(float32 dir.X)
                out.Add(float32 dir.Y)
                out.Add(float32 dir.Z)
                out.Add(float32 pid)
            | None -> ()
        match pickIdFor GHalfPlaneOffset with
        | Some pid ->
            let sign = if ctx.Offset < 0.0 then -1.0 else 1.0
            let dir = sign * ctx.AxisDir
            let anchor = HalfPlaneGizmo.offsetAnchor ctx worldPerPx
            let lengthPx = HalfPlaneGizmo.displayedOffsetPx ctx worldPerPx + HalfPlaneGizmo.ARROW_LENGTH_PX
            out.Add(float32 anchor.X)
            out.Add(float32 anchor.Y)
            out.Add(float32 anchor.Z)
            out.Add(lengthPx)
            out.Add(float32 dir.X)
            out.Add(float32 dir.Y)
            out.Add(float32 dir.Z)
            out.Add(float32 pid)
        | None -> ()
    | None -> ()
    out.ToArray()

/// Plane pick layout: 3 vec4 per plane:
///   (origin.xyz, pickId), (axisU.xyz, near_px), (axisV.xyz, far_px)
let private buildGizmoPlanesArray (ctx: TranslateGizmo.Context) (pickables: Pickable list) : float32[] =
    let pickIdByPlane =
        pickables
        |> List.choose (function
            | PickGizmoHandle(pid, aid, GPlane plane) when aid = ctx.ActionId -> Some(plane, pid)
            | _ -> None)
        |> Map.ofList
    let out = ResizeArray<float32>()
    for (planeIdx, (u, v)) in planeVecs ctx |> Array.indexed do
        match Map.tryFind planeIdx pickIdByPlane with
        | Some pid ->
            out.Add(float32 ctx.Origin.X)
            out.Add(float32 ctx.Origin.Y)
            out.Add(float32 ctx.Origin.Z)
            out.Add(float32 pid)
            out.Add(float32 u.X)
            out.Add(float32 u.Y)
            out.Add(float32 u.Z)
            out.Add(TranslateGizmo.PLANE_NEAR_PX)
            out.Add(float32 v.X)
            out.Add(float32 v.Y)
            out.Add(float32 v.Z)
            out.Add(TranslateGizmo.PLANE_FAR_PX)
        | None -> ()
    out.ToArray()

// ── Bind-group rebuild ───────────────────────────────────────────────

/// The PickState binding uses a dynamic offset, so its view window must
/// be explicitly sized to one slot. 48 bytes covers viewport (8) +
/// mouse (8) + sample_base (4) + _pad0 (4) + _pad1 (8) = 32; bump to 64
/// for headroom and 16-byte struct alignment.
let private PICK_STATE_VIEW_BYTES = 64

let private rebuildSketchBindGroup (pc: PickCompute) (sb: SketchBuffers) : IGPUBindGroup =
    pc.Scene.Device.createBindGroup
        { layout = pc.SketchBindGroupLayout
          entries =
            [| { binding = 0; resource = box { buffer = pc.Scene.CameraBuffer } }
               { binding = 1
                 resource = bindingResourceWithSize pc.Scene.FrameBuffer Scene.FRAME_SLOT_BYTES }
               { binding = 2
                 resource = bindingResourceWithSize pc.PickStateBuffer PICK_STATE_VIEW_BYTES }
               { binding = 3; resource = box { buffer = sb.Points } }
               { binding = 4; resource = box { buffer = sb.Lines } }
               { binding = 5; resource = box { buffer = sb.Circles } }
               { binding = 6; resource = box { buffer = sb.Loops } }
               { binding = 7; resource = box { buffer = sb.Labels } }
               { binding = 8; resource = box { buffer = pc.SamplesBuffer } } |] }

let private rebuildFrameBindGroup (pc: PickCompute) : IGPUBindGroup =
    pc.Scene.Device.createBindGroup
        { layout = pc.FrameBindGroupLayout
          entries =
            [| { binding = 0; resource = box { buffer = pc.Scene.CameraBuffer } }
               { binding = 1
                 resource = bindingResourceWithSize pc.PickStateBuffer PICK_STATE_VIEW_BYTES }
               { binding = 2; resource = box { buffer = pc.FrameOrigins } }
               { binding = 3; resource = box { buffer = pc.SamplesBuffer } } |] }

let private rebuildGizmoBindGroup (pc: PickCompute) : IGPUBindGroup =
    pc.Scene.Device.createBindGroup
        { layout = pc.GizmoBindGroupLayout
          entries =
            [| { binding = 0; resource = box { buffer = pc.Scene.CameraBuffer } }
               { binding = 1
                 resource = bindingResourceWithSize pc.PickStateBuffer PICK_STATE_VIEW_BYTES }
               { binding = 2; resource = box { buffer = pc.GizmoAxes } }
               { binding = 3; resource = box { buffer = pc.GizmoPlanes } }
               { binding = 4; resource = box { buffer = pc.SamplesBuffer } } |] }

// ── Per-frame update ─────────────────────────────────────────────────

/// Rebuild per-sketch geometry buffers + frame origin buffer from the
/// current pickables. Called every frame by the render loop after
/// geometry might have shifted.
let update (pc: PickCompute) (state: EditorState) (viewState: ViewerState) =
    let gizmoPickables =
        TranslateGizmo.ephemeralPickablesForState state
        @ RotateGizmo.ephemeralPickablesForState state
        @ HalfPlaneGizmo.ephemeralPickablesForState state
    let pickables = state.Compiled.Pickables @ gizmoPickables
    let slotLookup = state.Compiled.Slots.Index
    let paramValues = viewState.Params

    // Sketches addressed by id, for fast entity / constraint lookup
    // inside the per-sketch loop below.
    let actionSketches =
        state.Doc.Actions
        |> List.choose (fun a ->
            match a.Kind with
            | Sketch(_, _, s) -> Some (a.Id, s)
            | _ -> None)
    let blockSketches =
        state.Doc.Blocks
        |> List.choose (fun b ->
            match b.Kind with
            | Server.Lang.Notebook.SketchBlock data ->
                Some (Server.SketchAuthoring.blockSketchId b.Id, data.Sketch)
            | _ -> None)
    let sketchById = (actionSketches @ blockSketches) |> Map.ofList
    let loopsBySketch =
        viewState.SketchLoops
        |> List.map (fun sl -> sl.SketchId, sl.Loops)
        |> Map.ofList

    // Sketch draw order must match `Render.writeSketchUniforms` so the
    // dynamic frame-uniform offsets we bind below land on the right
    // sketch.
    let sketchOrder =
        viewState.SketchTransforms
        |> List.truncate Scene.FRAME_CAPACITY
        |> List.map (fun f -> f.Id)

    pc.LastSketchOrder <-
        sketchOrder
        |> List.mapi (fun i sid -> sid, i)

    for sid in sketchOrder do
        let sb = getOrCreateSketchBuffers pc sid
        let sketchOpt = Map.tryFind sid sketchById
        let loops = Map.tryFind sid loopsBySketch |> Option.defaultValue []
        let entities =
            sketchOpt |> Option.map (fun s -> s.Entities) |> Option.defaultValue []

        let points = buildPointsArray sid entities slotLookup paramValues pickables
        let lines = buildLinesArray sid entities slotLookup paramValues pickables
        let loopTris =
            match sketchOpt with
            | Some s -> buildLoopsArray sid s loops slotLookup paramValues pickables
            | None -> [||]
        let labels =
            match sketchOpt with
            | Some s -> buildLabelsArray sid s slotLookup paramValues pickables
            | None -> [||]

        let mutable changed = false
        let struct (p, pb, pGrew) = uploadMaybeGrow pc.Scene points sb.Points sb.PointsBytes
        sb.Points <- p
        sb.PointsBytes <- pb
        if pGrew then changed <- true
        let struct (l, lb, lGrew) = uploadMaybeGrow pc.Scene lines sb.Lines sb.LinesBytes
        sb.Lines <- l
        sb.LinesBytes <- lb
        if lGrew then changed <- true
        let struct (lo, lob, loGrew) = uploadMaybeGrow pc.Scene loopTris sb.Loops sb.LoopsBytes
        sb.Loops <- lo
        sb.LoopsBytes <- lob
        if loGrew then changed <- true
        let struct (lb2, lb2Bytes, lb2Grew) = uploadMaybeGrow pc.Scene labels sb.Labels sb.LabelsBytes
        sb.Labels <- lb2
        sb.LabelsBytes <- lb2Bytes
        if lb2Grew then changed <- true

        if changed || sb.BindGroup.IsNone then
            sb.BindGroup <- Some(rebuildSketchBindGroup pc sb)

    // Frame origins. Pad the tail with a far-away sentinel so grown or
    // empty buffers don't read back as a phantom frame at (0,0,0).
    let visibleFrames =
        viewState.Frames
        |> List.filter (fun f ->
            Map.tryFind f.Id viewState.Visible |> Option.defaultValue true)
    let worldPerPx = (2.0 * Camera.viewHalfH pc.Scene.Camera) / max (float pc.Scene.Canvas.height) 1.0
    let origins =
        buildWorldPointPickArray
            visibleFrames
            (RotateGizmo.contextOf state)
            pickables
            worldPerPx
    let struct (fo, foBytes, foGrew) =
        uploadWithSentinelPad pc.Scene origins FRAME_ORIGIN_SENTINEL
            pc.FrameOrigins pc.FrameOriginsBytes
    pc.FrameOrigins <- fo
    pc.FrameOriginsBytes <- foBytes
    if foGrew || pc.FrameBindGroup.IsNone then
        pc.FrameBindGroup <- Some(rebuildFrameBindGroup pc)

    pc.LastFrameDispatchIndex <-
        if origins.Length > 0 then Some sketchOrder.Length else None

    // Selected translate gizmo. Ephemeral pickables share the same id
    // map as the viewer's mousedown reducer, but are not part of the
    // compiled topology pickables.
    let gizmoAxes, gizmoPlanes =
        let translateCtx = TranslateGizmo.contextOf state
        let halfPlaneCtx = HalfPlaneGizmo.contextOf state
        let axes = buildGizmoAxesArray translateCtx halfPlaneCtx gizmoPickables worldPerPx
        let planes =
            match translateCtx with
            | Some ctx when not gizmoPickables.IsEmpty -> buildGizmoPlanesArray ctx gizmoPickables
            | _ -> [||]
        axes, planes
    let mutable gizmoChanged = false
    let struct (ga, gaBytes, gaGrew) =
        uploadWithSentinelPad pc.Scene gizmoAxes GIZMO_AXIS_SENTINEL
            pc.GizmoAxes pc.GizmoAxesBytes
    pc.GizmoAxes <- ga
    pc.GizmoAxesBytes <- gaBytes
    if gaGrew then gizmoChanged <- true
    let struct (gp, gpBytes, gpGrew) =
        uploadWithSentinelPad pc.Scene gizmoPlanes GIZMO_PLANE_SENTINEL
            pc.GizmoPlanes pc.GizmoPlanesBytes
    pc.GizmoPlanes <- gp
    pc.GizmoPlanesBytes <- gpBytes
    if gpGrew then gizmoChanged <- true
    if gizmoChanged || pc.GizmoBindGroup.IsNone then
        pc.GizmoBindGroup <- Some(rebuildGizmoBindGroup pc)

    pc.LastGizmoDispatchIndex <-
        if gizmoAxes.Length > 0 || gizmoPlanes.Length > 0 then
            Some(sketchOrder.Length + (if pc.LastFrameDispatchIndex.IsSome then 1 else 0))
        else None

// ── Pick dispatch + readback ─────────────────────────────────────────

/// Write one `PickState` slot (viewport, mouse, sample_base). The
/// sketch-frame slot for each sketch is controlled separately by the
/// caller via dynamic offset.
let private writePickState (pc: PickCompute) (dispatchIndex: int)
        (vw: float32) (vh: float32) (mx: float32) (my: float32) =
    let sampleBaseU32 = uint32 (dispatchIndex * SAMPLES_PER_DISPATCH)
    let floats = [| vw; vh; mx; my |]
    WebGPU.writeFloat32 pc.Scene.Device.queue pc.PickStateBuffer
        (dispatchIndex * PICK_STATE_STRIDE) floats
    WebGPU.writeUint32 pc.Scene.Device.queue pc.PickStateBuffer
        (dispatchIndex * PICK_STATE_STRIDE + 16)
        [| sampleBaseU32; 0u; 0u; 0u |]

/// Dispatch compute + read back. Returns a list of distinct pick
/// candidates discovered anywhere in the 5×5 window, sorted by
/// priority then score.
let pickAt (pc: PickCompute) (px: int) (py: int) : JS.Promise<PickCandidateInput list> =
    promise {
        if pc.InFlight then return []
        else
            pc.InFlight <- true
            let canvasW = float32 (pc.Scene.Canvas?width: int)
            let canvasH = float32 (pc.Scene.Canvas?height: int)

            // Write per-dispatch PickState (shared viewport + mouse, distinct sample_base).
            let sketchDispatches = pc.LastSketchOrder
            for (_sid, idx) in sketchDispatches do
                writePickState pc idx canvasW canvasH (float32 px) (float32 py)
            match pc.LastFrameDispatchIndex with
            | Some idx ->
                writePickState pc idx canvasW canvasH (float32 px) (float32 py)
            | None -> ()
            match pc.LastGizmoDispatchIndex with
            | Some idx ->
                writePickState pc idx canvasW canvasH (float32 px) (float32 py)
            | None -> ()

            let encoder = pc.Scene.Device.createCommandEncoder()

            let pass = encoder.beginComputePass()

            // ── Sketch dispatches ──
            for (sid, idx) in sketchDispatches do
                match pc.SketchBuffers.TryGetValue sid with
                | true, sb ->
                    match sb.BindGroup with
                    | Some bg ->
                        pass.setPipeline pc.SketchPipeline
                        // Dynamic offsets: binding 1 = frame (sketch i),
                        // binding 2 = pickState (dispatch idx).
                        let frameOffset = idx * Scene.FRAME_STRIDE
                        let pickStateOffset = idx * PICK_STATE_STRIDE
                        pass.setBindGroupWithOffsets(
                            0, bg, [| frameOffset; pickStateOffset |])
                        pass.dispatchWorkgroups(1, 1, 1)
                    | None -> ()
                | false, _ -> ()

            // ── Frame dispatch ──
            match pc.LastFrameDispatchIndex, pc.FrameBindGroup with
            | Some idx, Some bg ->
                pass.setPipeline pc.FramePipeline
                let pickStateOffset = idx * PICK_STATE_STRIDE
                pass.setBindGroupWithOffsets(0, bg, [| pickStateOffset |])
                pass.dispatchWorkgroups(1, 1, 1)
            | _ -> ()

            // ── Selected translate-gizmo dispatch ──
            match pc.LastGizmoDispatchIndex, pc.GizmoBindGroup with
            | Some idx, Some bg ->
                pass.setPipeline pc.GizmoPipeline
                let pickStateOffset = idx * PICK_STATE_STRIDE
                pass.setBindGroupWithOffsets(0, bg, [| pickStateOffset |])
                pass.dispatchWorkgroups(1, 1, 1)
            | _ -> ()

            pass.endPass()

            // Copy samples → staging for CPU readback.
            WebGPU.copyBufferToBuffer
                encoder pc.SamplesBuffer 0 pc.SamplesStaging 0 pc.SamplesBytes
            pc.Scene.Device.queue.submit [| encoder.finish() |]

            do! pc.SamplesStaging.mapAsync GPUMapMode.Read
            let arr = pc.SamplesStaging.getMappedRange()
            // Each sample = 4 words = id (u32), kind (u32), score (f32), _pad.
            // Read the same bytes twice — once as u32 (for id), once as f32
            // (for score) — so interleaved reinterpret is free.
            let totalSamples =
                (List.length sketchDispatches
                    + (if pc.LastFrameDispatchIndex.IsSome then 1 else 0)
                    + (if pc.LastGizmoDispatchIndex.IsSome then 1 else 0))
                * SAMPLES_PER_DISPATCH
            let rawU32 = WebGPU.readU32Range arr 0 (totalSamples * 4)
            let rawF32 = WebGPU.readF32Range arr 0 (totalSamples * 4)
            pc.SamplesStaging.unmap()
            pc.InFlight <- false

            // Dedup: for each pickId, keep the smallest score. Priority
            // ordering is left to the core's `reduceSelectionCandidates`
            // so the viewer doesn't duplicate that decision.
            let bestById = System.Collections.Generic.Dictionary<int, float32>()
            for i in 0 .. totalSamples - 1 do
                let base_ = i * 4
                let id = rawU32.[base_]
                if id <> NO_HIT then
                    let score = rawF32.[base_ + 2]
                    let pid = int id
                    match bestById.TryGetValue pid with
                    | true, existing when existing <= score -> ()
                    | _ -> bestById.[pid] <- score

            return
                bestById
                |> Seq.map (fun kv -> { PickId = kv.Key; Score = kv.Value })
                |> List.ofSeq
    }
