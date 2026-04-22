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

/// Dictionary of slots keyed by sketch id. One slot per sketch per
/// category — required because `queue.writeBuffer` is serialised
/// against `submit()` rather than interleaved with recorded commands.
/// Sharing one slot across multiple sketches causes the last sketch's
/// `writeBuffer` to overwrite everyone else's vertex data, making every
/// sketch render with the last sketch's geometry.
type PerSketchSlots = System.Collections.Generic.Dictionary<string, Slot>

let createPerSketchSlots () : PerSketchSlots = System.Collections.Generic.Dictionary()

/// Idempotent lookup that allocates a fresh `Slot` the first time a
/// given sketch id is seen. Buffers created here live for the rest of
/// the session (there's no eviction yet — deleting a sketch leaves its
/// slot in the map until the whole pool is destroyed).
let getSketchSlot (ps: PerSketchSlots) (id: string) : Slot =
    match ps.TryGetValue id with
    | true, slot -> slot
    | false, _ ->
        let slot = createSlot ()
        ps.[id] <- slot
        slot

/// Bag of every slot used by the viewer. Per-sketch categories live in
/// `PerSketchSlots` dictionaries; globally-scoped categories stay as
/// single `Slot`s.
type Slots =
    { Grid: PerSketchSlots
      LoopFill: PerSketchSlots
      Gizmo: PerSketchSlots
      ConstraintLine: PerSketchSlots
      SketchLine: PerSketchSlots
      SketchPoint: PerSketchSlots
      Label: PerSketchSlots
      ToolPreviewLine: PerSketchSlots
      ToolPreviewPoint: PerSketchSlots
      PlacementPreviewLine: PerSketchSlots
      PlacementPreviewLabel: PerSketchSlots
      FrameOriginPoint: Slot
      FrameGizmo: Slot }

let createSlots () : Slots =
    { Grid = createPerSketchSlots ()
      LoopFill = createPerSketchSlots ()
      Gizmo = createPerSketchSlots ()
      ConstraintLine = createPerSketchSlots ()
      SketchLine = createPerSketchSlots ()
      SketchPoint = createPerSketchSlots ()
      Label = createPerSketchSlots ()
      ToolPreviewLine = createPerSketchSlots ()
      ToolPreviewPoint = createPerSketchSlots ()
      PlacementPreviewLine = createPerSketchSlots ()
      PlacementPreviewLabel = createPerSketchSlots ()
      FrameOriginPoint = createSlot ()
      FrameGizmo = createSlot () }
