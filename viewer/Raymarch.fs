module Raymarch

// Optional raymarching viewer. An alternative to the Zig WASM kernel
// background — here the field SDF is generated as WGSL (see
// `GpuIsosurface.combinedIsosurfaceWgsl`) and sphere-traced on the GPU
// in a single full-screen draw.
//
// Lifecycle:
//   * Shader topology (add/remove/reorder surfaces) rebuilds the pipeline.
//   * Slot values (slider drags) re-upload to the slot storage buffer.
//   * Per-surface state (color, iso-enabled) re-uploads to the surface
//     storage buffer. Both buffers grow on demand.
//
// Integration: draws before sketches, writes `frag_depth`, so overlay
// geometry z-tests cleanly against the surface.

open Fable.Core
open Fable.Core.JsInterop
open Browser.Types
open Server
open WebGPU

[<Emit("$0.queue.writeBuffer($1, $2, $3)")>]
let private writeBuffer (device: IGPUDevice) (buf: IGPUBuffer) (offset: int) (data: obj) : unit = jsNative

/// Each surface contributes 8 floats: colorOpacity (r,g,b,a) + isoEnabled
/// (isoValue, enabled?, 0, 0). Must match the `SurfaceState` WGSL struct
/// emitted by `GpuIsosurface`.
let private FLOATS_PER_SURFACE = 8
/// Storage buffers have a minimum size > 0. Any empty scene still needs
/// a real allocation — pad to one surface worth of bytes.
let private MIN_STORAGE_BYTES = 32

type Raymarch =
    private
        { Scene: Scene.Scene
          PipelineLayout: IGPUPipelineLayout
          SlotBindGroupLayout: IGPUBindGroupLayout
          SurfaceBindGroupLayout: IGPUBindGroupLayout
          // Mutable pipeline — rebuilt on shader-source change (topology
          // change). None when no surfaces are visible.
          mutable Pipeline: IGPURenderPipeline option
          mutable PipelineWgsl: string option
          // Slot buffer grows as the slot table grows. Capacity in floats.
          mutable SlotBuffer: IGPUBuffer
          mutable SlotBindGroup: IGPUBindGroup
          mutable SlotCapacityFloats: int
          // Surface buffer grows as more surfaces enable their iso toggle.
          mutable SurfaceBuffer: IGPUBuffer
          mutable SurfaceBindGroup: IGPUBindGroup
          mutable SurfaceCapacityCount: int }

// ── Buffers + bind groups ──────────────────────────────────────────────

let private storageBuffer (device: IGPUDevice) (byteSize: int) : IGPUBuffer =
    device.createBuffer
        { size = max byteSize MIN_STORAGE_BYTES
          usage = GPUBufferUsage.Storage ||| GPUBufferUsage.CopyDst }

let private slotLayout (device: IGPUDevice) : IGPUBindGroupLayout =
    device.createBindGroupLayout
        { entries =
            [| box
                {| binding = 0
                   visibility = GPUShaderStage.Fragment
                   buffer = {| ``type`` = "read-only-storage" |} |} |] }

let private surfaceLayout (device: IGPUDevice) : IGPUBindGroupLayout =
    device.createBindGroupLayout
        { entries =
            [| box
                {| binding = 0
                   visibility = GPUShaderStage.Fragment
                   buffer = {| ``type`` = "read-only-storage" |} |} |] }

let private makeBindGroup
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

// ── Public API ─────────────────────────────────────────────────────────

let create (scene: Scene.Scene) : Raymarch =
    let slotBindLayout = slotLayout scene.Device
    let surfaceBindLayout = surfaceLayout scene.Device
    let pipelineLayout =
        scene.Device.createPipelineLayout
            { bindGroupLayouts =
                [| scene.CameraBindGroupLayout
                   slotBindLayout
                   surfaceBindLayout |] }
    let slotBuffer = storageBuffer scene.Device MIN_STORAGE_BYTES
    let surfaceBuffer = storageBuffer scene.Device MIN_STORAGE_BYTES
    { Scene = scene
      PipelineLayout = pipelineLayout
      SlotBindGroupLayout = slotBindLayout
      SurfaceBindGroupLayout = surfaceBindLayout
      Pipeline = None
      PipelineWgsl = None
      SlotBuffer = slotBuffer
      SlotBindGroup = makeBindGroup scene.Device slotBindLayout slotBuffer
      SlotCapacityFloats = MIN_STORAGE_BYTES / 4
      SurfaceBuffer = surfaceBuffer
      SurfaceBindGroup = makeBindGroup scene.Device surfaceBindLayout surfaceBuffer
      SurfaceCapacityCount = MIN_STORAGE_BYTES / (FLOATS_PER_SURFACE * 4) }

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
    rm.SlotBindGroup <-
        makeBindGroup rm.Scene.Device rm.SlotBindGroupLayout rm.SlotBuffer
    rm.SlotCapacityFloats <- cap

let private ensureSurfaceCapacity (rm: Raymarch) (neededCount: int) =
    if neededCount <= rm.SurfaceCapacityCount then () else
    let cap = nextPowerOfTwo neededCount
    rm.SurfaceBuffer <-
        storageBuffer rm.Scene.Device (cap * FLOATS_PER_SURFACE * 4)
    rm.SurfaceBindGroup <-
        makeBindGroup rm.Scene.Device rm.SurfaceBindGroupLayout rm.SurfaceBuffer
    rm.SurfaceCapacityCount <- cap

// ── Per-frame update ──────────────────────────────────────────────────

/// Pack per-surface color + iso settings for the shader. `Surfaces` in
/// `state.Compiled` is the render order; look up each action's display
/// settings and serialize to 8 floats.
let private packSurfaceState (state: EditorState) : float32[] =
    let displayById =
        state.Doc.Actions
        |> List.map (fun a ->
            let d = a.Display |> Option.defaultValue DisplaySettings.defaults
            a.Id, (a.Visible && d.Enabled, d))
        |> Map.ofList
    let n = state.Compiled.Surfaces.Length
    if n = 0 then [||] else
    let out = Array.zeroCreate (n * FLOATS_PER_SURFACE)
    state.Compiled.Surfaces
    |> List.iteri (fun i s ->
        let enabled, d =
            match Map.tryFind s.ActionId displayById with
            | Some x -> x
            | None -> false, DisplaySettings.defaults
        let baseIdx = i * FLOATS_PER_SURFACE
        out.[baseIdx + 0] <- float32 d.Color.[0]
        out.[baseIdx + 1] <- float32 d.Color.[1]
        out.[baseIdx + 2] <- float32 d.Color.[2]
        out.[baseIdx + 3] <- float32 d.Opacity
        out.[baseIdx + 4] <- float32 d.IsoValue
        out.[baseIdx + 5] <- if enabled then 1.0f else 0.0f
        out.[baseIdx + 6] <- 0.0f
        out.[baseIdx + 7] <- 0.0f)
    out

let private slotFloat32Array (state: EditorState) : float32[] =
    state.SlotValues |> Array.map float32

let update (rm: Raymarch) (state: EditorState) =
    // Rebuild shader if topology changed. `combinedIsosurfaceWgsl` returns
    // None for an empty scene — drop the pipeline so `draw` no-ops.
    let wgsl = GpuIsosurface.combinedIsosurfaceWgsl state.Compiled.Surfaces
    if wgsl <> rm.PipelineWgsl then
        rm.PipelineWgsl <- wgsl
        rm.Pipeline <-
            match wgsl with
            | Some src -> Some (buildPipeline rm.Scene rm.PipelineLayout src)
            | None -> None

    // Upload slot values (might have grown since last compile). Empty
    // slot tables still need a non-empty buffer on GPU.
    let slotData = slotFloat32Array state
    if slotData.Length > 0 then
        ensureSlotCapacity rm slotData.Length
        WebGPU.writeFloat32 rm.Scene.Device.queue rm.SlotBuffer 0 slotData

    // Upload surface state (color + enabled flag per surface).
    let surfData = packSurfaceState state
    if surfData.Length > 0 then
        ensureSurfaceCapacity rm (surfData.Length / FLOATS_PER_SURFACE)
        WebGPU.writeFloat32 rm.Scene.Device.queue rm.SurfaceBuffer 0 surfData

let draw (rm: Raymarch) (pass: IGPURenderPassEncoder) =
    match rm.Pipeline with
    | None -> ()
    | Some pipe ->
        pass.setPipeline pipe
        pass.setBindGroup(0, rm.Scene.CameraBindGroup)
        pass.setBindGroup(1, rm.SlotBindGroup)
        pass.setBindGroup(2, rm.SurfaceBindGroup)
        pass.draw 3
