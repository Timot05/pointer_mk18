module Raymarch

// Block-probe raymarch viewer. Same orthographic camera as every other
// viewer shader; the field SDF is generated as WGSL (see
// `GpuIsosurface.combinedRaymarchShaders`) and rendered in three passes
// per frame:
//
//   1. front_walk   (compute) — per 8×8 block, sphere-trace the centre
//      ray from tNear forward. Once inside the threshold tube, switch to
//      fixed diagonal steps so we keep sampling through the surface and
//      find the last hit too. Writes both tStart and tEnd in one pass,
//      replacing what used to be separate front + back probes.
//   2. analysis     (compute) — for every block that hit, build the world
//      AABB of the block × [tStart, tEnd], evaluate each surface's
//      interval-SDF over it, and write a u32 alive-mask.
//   3. fragment      (render)  — per pixel, look up its block's tStart /
//      tEnd / mask, discard blocks that missed entirely, otherwise sphere-
//      trace from tStart while iterating only alive surfaces.
//
// Lifecycle:
//   * Shader topology (add / remove / reorder surfaces) rebuilds both
//     pipelines.
//   * Slot values re-upload to the shared slot storage buffer.
//   * Per-surface state re-uploads to the surface storage buffer.
//   * Canvas resize reallocates the per-block storage buffers.
//   * Cfg uniform is rewritten every frame (canvas size + block counts).
//
// Integration: `encodeCompute` must run *before* `beginRenderPass` so
// the fragment's read-only views see fully-written probe + mask data.
// `draw` binds the same bindings (group 0..3) and runs the masked trace.

open Fable.Core
open Fable.Core.JsInterop
open Browser.Types
open Server
open WebGPU

[<Emit("$0.queue.writeBuffer($1, $2, $3)")>]
let private writeBuffer (device: IGPUDevice) (buf: IGPUBuffer) (offset: int) (data: obj) : unit = jsNative

/// Each surface contributes 8 floats: colorOpacity (r,g,b,a) + isoEnabled
/// (isoValue, enabled?, 0, 0). Must match the `SurfaceState` WGSL struct.
let private FLOATS_PER_SURFACE = 8
/// Storage buffers have a minimum size > 0. Any empty scene still needs
/// a real allocation — pad to one surface worth of bytes.
let private MIN_STORAGE_BYTES = 32
/// Screen partitioning. 8 matches the GPU-experiment sweet spot (single
/// 8 + simplify front+back); the compute passes use @workgroup_size(8,8)
/// so this is also the workgroup tile size.
let private BLOCK_SIZE = 8
/// Near / far plane used for probe and per-pixel trace along `cam.forward`.
let private T_NEAR = 0.01f
let private T_FAR = 1000.0f
/// Iteration caps. Probes are coarse; pixel trace carries the fine work.
let private MAX_PROBE_STEPS = 64u
let private MAX_PIXEL_STEPS = 192u

/// Bytes per block for front / back depth buffers (f32).
let private DEPTH_BYTES_PER_BLOCK = 4
/// Bytes per block for mask buffer (u32).
let private MASK_BYTES_PER_BLOCK = 4

type Raymarch =
    private
        { Scene: Scene.Scene

          // Bind group layouts. Slot / surface layouts cover both
          // Fragment and Compute visibility so one bind group serves
          // both pipelines. Block buffers need two layouts because the
          // same buffers are RW from compute and read-only from fragment.
          CameraComputeLayout: IGPUBindGroupLayout
          SlotLayout: IGPUBindGroupLayout
          SurfaceLayout: IGPUBindGroupLayout
          BlockComputeLayout: IGPUBindGroupLayout
          BlockFragmentLayout: IGPUBindGroupLayout
          RenderPipelineLayout: IGPUPipelineLayout
          ComputePipelineLayout: IGPUPipelineLayout
          CameraComputeBindGroup: IGPUBindGroup

          // Slot buffer grows with the slot table. Shared between render
          // and compute pipelines.
          mutable SlotBuffer: IGPUBuffer
          mutable SlotBindGroup: IGPUBindGroup
          mutable SlotCapacityFloats: int
          mutable SurfaceBuffer: IGPUBuffer
          mutable SurfaceBindGroup: IGPUBindGroup
          mutable SurfaceCapacityCount: int

          // Pipelines — rebuilt on shader-source change (topology change).
          // All three go `None` when no surfaces are visible so `draw`
          // and `encodeCompute` become no-ops. The "front walk" pipeline
          // does front probe + forward walk in one pass, writing both
          // frontDepths and backDepths.
          mutable Pipeline: IGPURenderPipeline option
          mutable FrontWalkPipeline: IGPUComputePipeline option
          mutable AnalysisPipeline: IGPUComputePipeline option
          mutable PipelineWgsl: (string * string) option

          // Per-block storage. Reallocated on canvas resize; Cfg is
          // rewritten every frame. Mask encoded as one u32 per block —
          // viewer scenes rarely have >32 surfaces, which is all a u32
          // can represent.
          CfgBuffer: IGPUBuffer
          mutable FrontDepthBuffer: IGPUBuffer
          mutable BackDepthBuffer: IGPUBuffer
          mutable MaskBuffer: IGPUBuffer
          mutable BlockComputeBindGroup: IGPUBindGroup
          mutable BlockFragmentBindGroup: IGPUBindGroup
          mutable BlocksX: int
          mutable BlocksY: int }

// ── Buffers + bind groups ──────────────────────────────────────────────

let private storageBuffer (device: IGPUDevice) (byteSize: int) : IGPUBuffer =
    device.createBuffer
        { size = max byteSize MIN_STORAGE_BYTES
          usage = GPUBufferUsage.Storage ||| GPUBufferUsage.CopyDst }

let private uniformBuffer (device: IGPUDevice) (byteSize: int) : IGPUBuffer =
    device.createBuffer
        { size = byteSize
          usage = GPUBufferUsage.Uniform ||| GPUBufferUsage.CopyDst }

// ── Bind group layouts ─────────────────────────────────────────────────

let private cameraComputeLayout (device: IGPUDevice) : IGPUBindGroupLayout =
    device.createBindGroupLayout
        { entries =
            [| box
                {| binding = 0
                   visibility = GPUShaderStage.Compute
                   buffer = {| ``type`` = "uniform" |} |} |] }

// Slot / surface layouts are shared by compute and fragment — the
// same bind group binds to both pipelines, which only works if
// visibility includes both stages.
let private sharedStorageLayout (device: IGPUDevice) : IGPUBindGroupLayout =
    device.createBindGroupLayout
        { entries =
            [| box
                {| binding = 0
                   visibility = GPUShaderStage.Fragment ||| GPUShaderStage.Compute
                   buffer = {| ``type`` = "read-only-storage" |} |} |] }

let private blockComputeLayout (device: IGPUDevice) : IGPUBindGroupLayout =
    device.createBindGroupLayout
        { entries =
            [| box
                {| binding = 0
                   visibility = GPUShaderStage.Compute
                   buffer = {| ``type`` = "uniform" |} |}
               box
                {| binding = 1
                   visibility = GPUShaderStage.Compute
                   buffer = {| ``type`` = "storage" |} |}
               box
                {| binding = 2
                   visibility = GPUShaderStage.Compute
                   buffer = {| ``type`` = "storage" |} |}
               box
                {| binding = 3
                   visibility = GPUShaderStage.Compute
                   buffer = {| ``type`` = "storage" |} |} |] }

let private blockFragmentLayout (device: IGPUDevice) : IGPUBindGroupLayout =
    device.createBindGroupLayout
        { entries =
            [| box
                {| binding = 0
                   visibility = GPUShaderStage.Fragment
                   buffer = {| ``type`` = "uniform" |} |}
               box
                {| binding = 1
                   visibility = GPUShaderStage.Fragment
                   buffer = {| ``type`` = "read-only-storage" |} |}
               box
                {| binding = 2
                   visibility = GPUShaderStage.Fragment
                   buffer = {| ``type`` = "read-only-storage" |} |}
               box
                {| binding = 3
                   visibility = GPUShaderStage.Fragment
                   buffer = {| ``type`` = "read-only-storage" |} |} |] }

let private makeSharedBindGroup
        (device: IGPUDevice)
        (layout: IGPUBindGroupLayout) (buffer: IGPUBuffer) : IGPUBindGroup =
    device.createBindGroup
        { layout = layout
          entries = [| { binding = 0; resource = box { buffer = buffer } } |] }

let private makeBlockBindGroup
        (device: IGPUDevice)
        (layout: IGPUBindGroupLayout)
        (cfg: IGPUBuffer) (front: IGPUBuffer) (back: IGPUBuffer) (masks: IGPUBuffer)
        : IGPUBindGroup =
    device.createBindGroup
        { layout = layout
          entries =
            [| { binding = 0; resource = box { buffer = cfg } }
               { binding = 1; resource = box { buffer = front } }
               { binding = 2; resource = box { buffer = back } }
               { binding = 3; resource = box { buffer = masks } } |] }

// ── Pipelines ──────────────────────────────────────────────────────────

let private buildRenderPipeline
        (scene: Scene.Scene)
        (layout: IGPUPipelineLayout)
        (wgsl: string) : IGPURenderPipeline =
    let shader = scene.Device.createShaderModule { code = wgsl }
    scene.Device.createRenderPipeline
        (box
            {| layout = layout
               vertex = {| ``module`` = shader; entryPoint = "vs_main" |}
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
               depthStencil =
                {| format = "depth24plus"
                   depthWriteEnabled = true
                   depthCompare = "less" |} |})

let private buildComputePipeline
        (scene: Scene.Scene)
        (layout: IGPUPipelineLayout)
        (shader: IGPUShaderModule)
        (entryPoint: string) : IGPUComputePipeline =
    scene.Device.createComputePipeline
        (box
            {| layout = layout
               compute = {| ``module`` = shader; entryPoint = entryPoint |} |})

// ── Public API ─────────────────────────────────────────────────────────

let create (scene: Scene.Scene) : Raymarch =
    let camComputeLayout = cameraComputeLayout scene.Device
    let slotLayout = sharedStorageLayout scene.Device
    let surfaceLayout = sharedStorageLayout scene.Device
    let blockComp = blockComputeLayout scene.Device
    let blockFrag = blockFragmentLayout scene.Device

    // Render pipeline reuses Scene's camera bind-group (V|F visibility);
    // compute pipelines need a Compute-visible layout over the same
    // buffer.
    let renderPipelineLayout =
        scene.Device.createPipelineLayout
            { bindGroupLayouts =
                [| scene.CameraBindGroupLayout
                   slotLayout
                   surfaceLayout
                   blockFrag |] }
    let computePipelineLayout =
        scene.Device.createPipelineLayout
            { bindGroupLayouts =
                [| camComputeLayout
                   slotLayout
                   surfaceLayout
                   blockComp |] }

    let camComputeBindGroup =
        scene.Device.createBindGroup
            { layout = camComputeLayout
              entries = [| { binding = 0; resource = box { buffer = scene.CameraBuffer } } |] }

    let slotBuffer = storageBuffer scene.Device MIN_STORAGE_BYTES
    let surfaceBuffer = storageBuffer scene.Device MIN_STORAGE_BYTES

    // Cfg is 12 × 4 = 48 bytes (matches `struct Cfg` in the shaders).
    let cfgBuffer = uniformBuffer scene.Device 48

    let frontBuffer = storageBuffer scene.Device (MIN_STORAGE_BYTES)
    let backBuffer = storageBuffer scene.Device (MIN_STORAGE_BYTES)
    let maskBuffer = storageBuffer scene.Device (MIN_STORAGE_BYTES)

    { Scene = scene
      CameraComputeLayout = camComputeLayout
      SlotLayout = slotLayout
      SurfaceLayout = surfaceLayout
      BlockComputeLayout = blockComp
      BlockFragmentLayout = blockFrag
      RenderPipelineLayout = renderPipelineLayout
      ComputePipelineLayout = computePipelineLayout
      CameraComputeBindGroup = camComputeBindGroup

      Pipeline = None
      FrontWalkPipeline = None
      AnalysisPipeline = None
      PipelineWgsl = None

      SlotBuffer = slotBuffer
      SlotBindGroup = makeSharedBindGroup scene.Device slotLayout slotBuffer
      SlotCapacityFloats = MIN_STORAGE_BYTES / 4
      SurfaceBuffer = surfaceBuffer
      SurfaceBindGroup = makeSharedBindGroup scene.Device surfaceLayout surfaceBuffer
      SurfaceCapacityCount = MIN_STORAGE_BYTES / (FLOATS_PER_SURFACE * 4)

      CfgBuffer = cfgBuffer
      FrontDepthBuffer = frontBuffer
      BackDepthBuffer = backBuffer
      MaskBuffer = maskBuffer
      BlockComputeBindGroup =
        makeBlockBindGroup scene.Device blockComp cfgBuffer frontBuffer backBuffer maskBuffer
      BlockFragmentBindGroup =
        makeBlockBindGroup scene.Device blockFrag cfgBuffer frontBuffer backBuffer maskBuffer
      BlocksX = 0
      BlocksY = 0 }

// Grow buffers in powers of two so resize is amortized O(1).
let private nextPowerOfTwo (v: int) : int =
    let mutable n = max 16 v
    let mutable r = 1
    while r < n do r <- r * 2
    r

let private ensureSlotCapacity (rm: Raymarch) (neededFloats: int) =
    if neededFloats <= rm.SlotCapacityFloats then () else
    let cap = nextPowerOfTwo neededFloats
    rm.SlotBuffer <- storageBuffer rm.Scene.Device (cap * 4)
    rm.SlotBindGroup <- makeSharedBindGroup rm.Scene.Device rm.SlotLayout rm.SlotBuffer
    rm.SlotCapacityFloats <- cap

let private ensureSurfaceCapacity (rm: Raymarch) (neededCount: int) =
    if neededCount <= rm.SurfaceCapacityCount then () else
    let cap = nextPowerOfTwo neededCount
    rm.SurfaceBuffer <- storageBuffer rm.Scene.Device (cap * FLOATS_PER_SURFACE * 4)
    rm.SurfaceBindGroup <-
        makeSharedBindGroup rm.Scene.Device rm.SurfaceLayout rm.SurfaceBuffer
    rm.SurfaceCapacityCount <- cap

/// Reallocate per-block storage when the canvas grid changes. Both
/// compute + fragment bind groups get rebuilt against the new buffers.
let private ensureBlockCapacity (rm: Raymarch) (blocksX: int) (blocksY: int) =
    if blocksX = rm.BlocksX && blocksY = rm.BlocksY then () else
    let totalBlocks = max 1 (blocksX * blocksY)
    rm.FrontDepthBuffer <-
        storageBuffer rm.Scene.Device (totalBlocks * DEPTH_BYTES_PER_BLOCK)
    rm.BackDepthBuffer <-
        storageBuffer rm.Scene.Device (totalBlocks * DEPTH_BYTES_PER_BLOCK)
    rm.MaskBuffer <-
        storageBuffer rm.Scene.Device (totalBlocks * MASK_BYTES_PER_BLOCK)
    rm.BlockComputeBindGroup <-
        makeBlockBindGroup rm.Scene.Device rm.BlockComputeLayout
            rm.CfgBuffer rm.FrontDepthBuffer rm.BackDepthBuffer rm.MaskBuffer
    rm.BlockFragmentBindGroup <-
        makeBlockBindGroup rm.Scene.Device rm.BlockFragmentLayout
            rm.CfgBuffer rm.FrontDepthBuffer rm.BackDepthBuffer rm.MaskBuffer
    rm.BlocksX <- blocksX
    rm.BlocksY <- blocksY

// ── Per-frame upload ──────────────────────────────────────────────────

/// Maximum surfaces the per-block alive mask (u32) can track. Surfaces
/// beyond this limit are rendered without mask pruning — the shader
/// simply drops them from its branch list, so they won't render.
let private MAX_SURFACES = 32

/// `state.Compiled.Surfaces` lists one FieldSurface per visible DocAction
/// that compiles to a field — often 100+. Only surfaces marked
/// Display.Enabled actually render. Filter to the enabled set up front so
/// both the WGSL codegen and the surfaceStates buffer stay tight (and
/// the mask bit index per surface stays under 32).
let private enabledSurfacesAndState (state: EditorState)
        : FieldSurface list * float32[] =
    let displayById =
        state.Doc.Actions
        |> List.choose (fun a ->
            match a.Display with
            | Some d when a.Visible && d.Enabled -> Some (a.Id, d)
            | _ -> None)
        |> Map.ofList
    let enabled =
        state.Compiled.Surfaces
        |> List.choose (fun s ->
            Map.tryFind s.ActionId displayById
            |> Option.map (fun d -> s, d))
        |> List.truncate MAX_SURFACES
    let filteredSurfaces = enabled |> List.map fst
    let state =
        enabled
        |> List.mapi (fun i (_, d) ->
            let baseIdx = i * FLOATS_PER_SURFACE
            [| float32 d.Color.[0]; float32 d.Color.[1]; float32 d.Color.[2]; float32 d.Opacity
               float32 d.IsoValue; 1.0f; 0.0f; 0.0f |])
        |> Array.concat
    filteredSurfaces, state

let private slotFloat32Array (state: EditorState) : float32[] =
    state.SlotValues |> Array.map float32

/// Ceiling division — canvas dimensions may not divide evenly by
/// BLOCK_SIZE; the last row / column of blocks is partial and the
/// compute pass early-outs for pixels outside the canvas.
let private ceilDiv a b = (a + b - 1) / b

/// Probe threshold = half the 2D world-space diagonal of one block.
/// Orthographic parallel rays inside a block are at most this distance
/// from the centre ray, so any surface the block could hit must come
/// within `threshold` of the centre ray as well.
let private probeThreshold (canvasW: float) (canvasH: float) (aspect: float) (viewHalfH: float) =
    let blockWorldW = 2.0 * float BLOCK_SIZE / canvasW * aspect * viewHalfH
    let blockWorldH = 2.0 * float BLOCK_SIZE / canvasH * viewHalfH
    0.5 * sqrt (blockWorldW * blockWorldW + blockWorldH * blockWorldH)

let private writeCfg
        (rm: Raymarch)
        (canvasW: int) (canvasH: int)
        (blocksX: int) (blocksY: int)
        (threshold: float32) =
    // Layout mirrors `struct Cfg` — 12 × 4 = 48 bytes. `Float32Array`
    // covers the whole thing; the u32 fields alias into it via
    // `bitcast<u32>` on the shader side if needed, but the host writes
    // raw bit-patterns here using Uint32 / Float32 arrays.
    // Using Float32 storage for all; u32 fields reinterpreted via
    // Float32Array(new Uint32Array(...)) trick isn't available directly,
    // so we write the u32 fields via a separate writeUint32 call at the
    // right offsets.
    let fdata =
        [| float32 canvasW; float32 canvasH
           0.0f; 0.0f     // block_size, blocks_x — overwritten below
           0.0f; 0.0f     // blocks_y, max_probe_steps — overwritten below
           0.0f; 0.0f     // max_pixel_steps, pad0 — overwritten below
           float32 T_NEAR; float32 T_FAR
           threshold; 0.0f |]
    WebGPU.writeFloat32 rm.Scene.Device.queue rm.CfgBuffer 0 fdata
    let udata =
        [| uint32 BLOCK_SIZE; uint32 blocksX
           uint32 blocksY; MAX_PROBE_STEPS
           MAX_PIXEL_STEPS; 0u |]
    // Offset 8 bytes = past canvas_w / canvas_h (2 × f32).
    WebGPU.writeUint32 rm.Scene.Device.queue rm.CfgBuffer 8 udata

/// Rebuild pipelines (on topology change) and upload per-frame data
/// (slots, surfaces, Cfg). Safe to call every frame.
let update (rm: Raymarch) (state: EditorState) =
    let enabledSurfaces, surfData = enabledSurfacesAndState state
    let shaders = GpuIsosurface.combinedRaymarchShaders enabledSurfaces
    let wgslPair = shaders |> Option.map (fun s -> s.ComputeWgsl, s.FragmentWgsl)
    if wgslPair <> rm.PipelineWgsl then
        rm.PipelineWgsl <- wgslPair
        match wgslPair with
        | Some (computeSrc, fragmentSrc) ->
            let computeModule =
                rm.Scene.Device.createShaderModule { code = computeSrc }
            rm.FrontWalkPipeline <-
                Some (buildComputePipeline rm.Scene rm.ComputePipelineLayout computeModule "front_walk_main")
            rm.AnalysisPipeline <-
                Some (buildComputePipeline rm.Scene rm.ComputePipelineLayout computeModule "analysis_main")
            rm.Pipeline <-
                Some (buildRenderPipeline rm.Scene rm.RenderPipelineLayout fragmentSrc)
        | None ->
            rm.Pipeline <- None
            rm.FrontWalkPipeline <- None
            rm.AnalysisPipeline <- None

    let slotData = slotFloat32Array state
    if slotData.Length > 0 then
        ensureSlotCapacity rm slotData.Length
        WebGPU.writeFloat32 rm.Scene.Device.queue rm.SlotBuffer 0 slotData

    if surfData.Length > 0 then
        ensureSurfaceCapacity rm (surfData.Length / FLOATS_PER_SURFACE)
        WebGPU.writeFloat32 rm.Scene.Device.queue rm.SurfaceBuffer 0 surfData

    // Canvas + block layout. `canvas.width` is the framebuffer pixel
    // count (dpr-scaled) matching `@builtin(position)` in the fragment.
    let canvasW : int = rm.Scene.Canvas?width
    let canvasH : int = rm.Scene.Canvas?height
    if canvasW > 0 && canvasH > 0 then
        let blocksX = ceilDiv canvasW BLOCK_SIZE
        let blocksY = ceilDiv canvasH BLOCK_SIZE
        ensureBlockCapacity rm blocksX blocksY

        let aspect = float canvasW / max (float canvasH) 1.0
        let viewHalfH = Camera.viewHalfH rm.Scene.Camera
        let threshold = float32 (probeThreshold (float canvasW) (float canvasH) aspect viewHalfH)
        writeCfg rm canvasW canvasH blocksX blocksY threshold

/// Encode the two compute passes (front+walk, analysis). Must run
/// BEFORE `beginRenderPass` — the fragment reads the buffers these
/// passes write.
let encodeCompute (rm: Raymarch) (encoder: IGPUCommandEncoder) =
    match rm.FrontWalkPipeline, rm.AnalysisPipeline with
    | Some frontWalkPipe, Some analysisPipe
        when rm.BlocksX > 0 && rm.BlocksY > 0 ->
        let dispatchX = ceilDiv rm.BlocksX BLOCK_SIZE
        let dispatchY = ceilDiv rm.BlocksY BLOCK_SIZE
        let pass = encoder.beginComputePass()
        pass.setBindGroup(0, rm.CameraComputeBindGroup)
        pass.setBindGroup(1, rm.SlotBindGroup)
        pass.setBindGroup(2, rm.SurfaceBindGroup)
        pass.setBindGroup(3, rm.BlockComputeBindGroup)
        pass.setPipeline frontWalkPipe
        pass.dispatchWorkgroups(dispatchX, dispatchY, 1)
        pass.setPipeline analysisPipe
        pass.dispatchWorkgroups(dispatchX, dispatchY, 1)
        pass.endPass()
    | _ -> ()

let draw (rm: Raymarch) (pass: IGPURenderPassEncoder) =
    match rm.Pipeline with
    | None -> ()
    | Some pipe ->
        pass.setPipeline pipe
        pass.setBindGroup(0, rm.Scene.CameraBindGroup)
        pass.setBindGroup(1, rm.SlotBindGroup)
        pass.setBindGroup(2, rm.SurfaceBindGroup)
        pass.setBindGroup(3, rm.BlockFragmentBindGroup)
        pass.draw 3
