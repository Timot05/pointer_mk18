module BufferPool

// Grow-only GPU vertex buffer pool. Each draw-site owns a `Slot`; the pool
// allocates a buffer on first upload and grows it when data no longer fits.
// Replaced buffers are queued and destroyed once the GPU has had enough
// frames to drain in-flight command buffers referencing them.

open Fable.Core.JsInterop
open Server
open WebGPU

/// One managed buffer + its current capacity in bytes.
type Slot =
    { mutable Buffer: IGPUBuffer option
      mutable CapBytes: int }

/// Pool shared by every slot. Tracks submitted-frame count so old buffers
/// can be destroyed safely after the GPU has moved past them.
type Pool =
    { Device: IGPUDevice
      mutable SubmittedFrameCount: int
      Retired: ResizeArray<IGPUBuffer * int> }

let createPool (device: IGPUDevice) : Pool =
    { Device = device
      SubmittedFrameCount = 0
      Retired = ResizeArray<IGPUBuffer * int>() }

let createSlot () : Slot =
    { Buffer = None; CapBytes = 0 }

/// Schedule destruction after a few frames — WebGPU command buffers may
/// still reference the buffer at submit time.
let scheduleDestroy (pool: Pool) (buffer: IGPUBuffer) =
    pool.Retired.Add(buffer, pool.SubmittedFrameCount + 8)

/// Destroy any retired buffers whose "safe after" frame count has passed.
let flushRetired (pool: Pool) =
    let mutable write = 0
    for i in 0 .. pool.Retired.Count - 1 do
        let (buffer, retireAfter) = pool.Retired.[i]
        if retireAfter <= pool.SubmittedFrameCount then
            buffer.destroy()
        else
            pool.Retired.[write] <- pool.Retired.[i]
            write <- write + 1
    while pool.Retired.Count > write do
        pool.Retired.RemoveAt(pool.Retired.Count - 1)

/// Mark that one more command buffer has been submitted — callers do this
/// exactly once per render frame, right after `device.queue.submit`.
let markFrameSubmitted (pool: Pool) =
    pool.SubmittedFrameCount <- pool.SubmittedFrameCount + 1

/// Upload float32 vertex data into the slot's buffer, growing it first if
/// the data doesn't fit. Returns the (possibly reused) GPU buffer.
let upload (pool: Pool) (slot: Slot) (data: float32[]) : IGPUBuffer =
    let bytes = data.Length * 4
    if slot.CapBytes < bytes then
        slot.Buffer |> Option.iter (scheduleDestroy pool)
        let newCap = max 1024 (max bytes (slot.CapBytes * 2))
        slot.CapBytes <- newCap
        slot.Buffer <-
            Some (pool.Device.createBuffer
                    { size = newCap
                      usage = GPUBufferUsage.Vertex ||| GPUBufferUsage.CopyDst })
    match slot.Buffer with
    | Some buf ->
        WebGPU.writeFloat32 pool.Device.queue buf 0 data
        buf
    | None -> failwith "unreachable"

/// Bag of every slot used by the viewer. One-shot record so the render
/// loop can reach each buffer by name.
type Slots =
    { Grid: Slot
      LoopFill: Slot
      Gizmo: Slot
      ConstraintLine: Slot
      SketchLine: Slot
      SketchPoint: Slot
      Label: Slot
      LoopPick: Slot
      LinePick: Slot
      PointPick: Slot
      DimPick: Slot
      ToolPreviewLine: Slot
      ToolPreviewPoint: Slot
      PlacementPreviewLine: Slot
      PlacementPreviewLabel: Slot
      FrameOriginPoint: Slot
      FrameOriginPick: Slot
      FrameAxisPick: Slot
      FrameGizmo: Slot }

let createSlots () : Slots =
    { Grid = createSlot ()
      LoopFill = createSlot ()
      Gizmo = createSlot ()
      ConstraintLine = createSlot ()
      SketchLine = createSlot ()
      SketchPoint = createSlot ()
      Label = createSlot ()
      LoopPick = createSlot ()
      LinePick = createSlot ()
      PointPick = createSlot ()
      DimPick = createSlot ()
      ToolPreviewLine = createSlot ()
      ToolPreviewPoint = createSlot ()
      PlacementPreviewLine = createSlot ()
      PlacementPreviewLabel = createSlot ()
      FrameOriginPoint = createSlot ()
      FrameOriginPick = createSlot ()
      FrameAxisPick = createSlot ()
      FrameGizmo = createSlot () }
