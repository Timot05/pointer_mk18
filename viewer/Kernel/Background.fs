module Kernel.Background

// Web-Worker pool that drives the Zig kernel in parallel. Each worker
// owns its own WASM instance + copy of the scene IR; main thread hands
// out tiles to free workers and uploads returned g-buffers into the
// rgba32float texture. The fragment shader does shading + perspective
// depth in `Background.wgsl` so sketch geometry z-tests against the
// field surface.
//
// Scheduling model (per level):
//   1. Tiles array = full tile grid of the current canvas.
//   2. Each free worker is immediately sent the next tile from the
//      queue.
//   3. On a worker response we upload into the `pending` texture, mark
//      the worker free, and push the next tile.
//   4. When all tiles of the current level have landed, swap pending
//      and display (ping-pong) so the shader reads from a fully
//      consistent frame. Advance level.
//   5. Camera change bumps `Epoch` — in-flight stale responses are
//      discarded before upload.

open Fable.Core
open Fable.Core.JsInterop
open Browser.Types
open Browser
open Server
open WebGPU

[<Emit("navigator.hardwareConcurrency || 4")>]
let private hardwareConcurrency : int = jsNative

[<Emit("performance.now()")>]
let private performanceNow () : float = jsNative

/// Workers to spawn. Leave at least one core free for the main thread.
let private workerCount () : int =
    max 1 (min 4 (hardwareConcurrency - 1))

/// Max pixel size the kernel can handle per tile (Zig's MAX_W/MAX_H).
let private MAX_TILE = 1024
/// Coarsest refinement level we ever start from.
let private START_LEVEL = 3
/// How long the camera must stay still before we start climbing above
/// `START_LEVEL`. Snappy motion stays pinned to the coarsest level so
/// the display swaps every few ms; refinement only kicks in when the
/// user has clearly stopped.
let private MOTION_QUIET_MS = 50.0

// ── Worker bindings ────────────────────────────────────────────────────

[<Emit("new Worker(new URL($0, import.meta.url), { type: 'module' })")>]
let private createWorker (path: string) : obj = jsNative

[<Emit("$0.addEventListener('message', $1)")>]
let private onMessage (w: obj) (h: obj -> unit) : unit = jsNative

[<Emit("$0.addEventListener('error', $1)")>]
let private onError (w: obj) (h: obj -> unit) : unit = jsNative

[<Emit("$0.postMessage($1)")>]
let private post (w: obj) (msg: obj) : unit = jsNative

[<Emit("$0.terminate()")>]
let private terminate (w: obj) : unit = jsNative

[<Emit("new Float32Array($0)")>]
let private f32OfBuffer (buf: obj) : obj = jsNative

[<Emit("new Uint32Array($0)")>]
let private u32OfBuffer (buf: obj) : obj = jsNative

[<Emit("$0.queue.writeTexture($1, $2, $3, $4)")>]
let private writeTexture
        (device: IGPUDevice)
        (destination: obj) (data: obj) (dataLayout: obj) (size: obj) : unit = jsNative

// ── Types ──────────────────────────────────────────────────────────────

type private Tile =
    { TileX: int
      TileY: int
      TileW: int
      TileH: int }

type private WorkerSlot =
    { mutable Handle: obj
      mutable Ready: bool         // `ready` received
      mutable Busy: bool           // tile in flight
      mutable CurrentLevel: int }  // level of the in-flight tile

type Background =
    private
        { Scene: Scene.Scene
          Workers: WorkerSlot[]
          FieldCameraBuffer: IGPUBuffer
          // Compiled WebAssembly.Module cached on the main thread so
          // workers (including respawns) instantiate instantly.
          WasmModule: obj
          // Cached IR bytes for workers that come online later. Null
          // until the editor ships us a real field.
          mutable IrBytes: obj
          mutable HasIr: bool
          // Ping-pong textures: shader always samples `Display`; workers
          // always write into `Pending`. On level complete we swap.
          mutable DisplayTexture: IGPUTexture
          mutable DisplayView: IGPUTextureView
          mutable DisplayBindGroup: IGPUBindGroup
          mutable PendingTexture: IGPUTexture
          mutable PendingView: IGPUTextureView
          mutable PendingBindGroup: IGPUBindGroup
          // Parallel palette-idx textures (r32uint), rotated together
          // with the gbuffer textures so the displayed bind group always
          // points at consistent gbuffer + palette pairs.
          mutable DisplayPaletteTexture: IGPUTexture
          mutable DisplayPaletteView: IGPUTextureView
          mutable PendingPaletteTexture: IGPUTexture
          mutable PendingPaletteView: IGPUTextureView
          // False until the first level completes — no draw is issued
          // before that so we don't sample uninitialized texels.
          mutable HasDisplay: bool
          // Set when `resize` was called with old Display kept at its
          // pre-resize size. After the next swap, the now-Pending
          // texture is at that old size; recreate it at the new size
          // so subsequent worker uploads land in a correctly-sized
          // target.
          mutable PendingNeedsResize: bool
          mutable Width: int
          mutable Height: int
          mutable Tiles: Tile[]
          mutable Level: int
          mutable MaxLevel: int
          // Per-level bookkeeping.
          mutable TilesEmitted: int
          mutable TilesRendered: int
          // Counts tiles whose response included a non-null g-buffer. If
          // this is still 0 when the level "completes", the IR is in a
          // bad state (e.g. lower failed); we skip the swap so the last
          // good display keeps showing instead of an uninitialised one.
          mutable TilesUploaded: int
          // Bumped on any camera/size change. Stale worker responses
          // carry an old epoch and get discarded.
          mutable Epoch: int
          mutable LastCameraKey: float
          // Timestamp (performance.now) of the last camera change. Used
          // to decide when to stop re-rendering level 3 and advance.
          mutable LastCameraChangeMs: float
          // Set whenever something visibly changed (level advanced, new
          // tiles dispatched, display swapped). The viewer's RAF loop
          // reads + clears this via `consumeDisplayDirty` to know it
          // should redraw. Without this, the loop only redraws on its
          // own dirty signals (camera, store, etc.) and misses async
          // background updates that happen during a quiet window.
          mutable DisplayDirty: bool }

// ── Helpers ────────────────────────────────────────────────────────────

let private makeTiles (w: int) (h: int) : Tile[] =
    let nx = (w + MAX_TILE - 1) / MAX_TILE
    let ny = (h + MAX_TILE - 1) / MAX_TILE
    [|
        for ty in 0 .. ny - 1 do
            for tx in 0 .. nx - 1 do
                let x = tx * MAX_TILE
                let y = ty * MAX_TILE
                yield
                    { TileX = x; TileY = y
                      TileW = min MAX_TILE (w - x)
                      TileH = min MAX_TILE (h - y) }
    |]

let private createTexture (device: IGPUDevice) (w: int) (h: int) : IGPUTexture =
    device.createTexture
        { size = { width = w; height = h; depthOrArrayLayers = 1 }
          format = "rgba32float"
          usage =
            GPUTextureUsage.TextureBinding
            ||| GPUTextureUsage.CopyDst }

let private createPaletteTexture (device: IGPUDevice) (w: int) (h: int) : IGPUTexture =
    device.createTexture
        { size = { width = w; height = h; depthOrArrayLayers = 1 }
          format = "r32uint"
          usage =
            GPUTextureUsage.TextureBinding
            ||| GPUTextureUsage.CopyDst }

let private buildBindGroup
        (device: IGPUDevice)
        (layout: IGPUBindGroupLayout)
        (view: IGPUTextureView)
        (sampler: IGPUSampler)
        (fieldBuffer: IGPUBuffer)
        (paletteView: IGPUTextureView) : IGPUBindGroup =
    device.createBindGroup
        { layout = layout
          entries =
            [| { binding = 0; resource = box view }
               { binding = 1; resource = box sampler }
               { binding = 2; resource = box { buffer = fieldBuffer } }
               { binding = 3; resource = box paletteView } |] }

let private cameraKey (camera: Camera.CameraState) : float =
    camera.Azimuth * 1e6
    + camera.Elevation * 1e3
    + camera.Distance * 1e-2
    + camera.Target.X + camera.Target.Y * 0.1 + camera.Target.Z * 0.01

let private viewHalfV (camera: Camera.CameraState) : float =
    camera.Distance * tan Camera.HALF_FOV

let private cameraValues (camera: Camera.CameraState) : float32[] =
    let b = Camera.basis camera
    let t = camera.Target
    // basis_z = -Forward to match the kernel's "+wcz = closer" convention.
    [| float32 t.X;          float32 t.Y;          float32 t.Z
       float32 b.Right.X;    float32 b.Right.Y;    float32 b.Right.Z
       float32 b.Up.X;       float32 b.Up.Y;       float32 b.Up.Z
       float32 -b.Forward.X; float32 -b.Forward.Y; float32 -b.Forward.Z |]

let private writeFieldCamera
        (device: IGPUDevice) (buffer: IGPUBuffer)
        (camera: Camera.CameraState)
        (viewHalfW: float) (viewHalfH: float) =
    let b = Camera.basis camera
    let t = camera.Target
    let data : float32[] =
        [| float32 t.X;          float32 t.Y;          float32 t.Z;          0.0f
           float32 b.Right.X;    float32 b.Right.Y;    float32 b.Right.Z;    0.0f
           float32 b.Up.X;       float32 b.Up.Y;       float32 b.Up.Z;       0.0f
           float32 -b.Forward.X; float32 -b.Forward.Y; float32 -b.Forward.Z; 0.0f
           float32 viewHalfW;    float32 viewHalfH;    0.0f;                 0.0f |]
    WebGPU.writeFloat32 device.queue buffer 0 data

// ── Dispatch ───────────────────────────────────────────────────────────

/// Send one tile to a free worker; no-op if none are free.
let private dispatch (bg: Background) (slot: WorkerSlot) =
    if slot.Busy || not slot.Ready then () else
    if not bg.HasIr then () else
    if bg.Level > bg.MaxLevel then () else
    if bg.TilesEmitted >= bg.Tiles.Length then () else
    let tile = bg.Tiles.[bg.TilesEmitted]
    bg.TilesEmitted <- bg.TilesEmitted + 1
    slot.Busy <- true
    slot.CurrentLevel <- bg.Level
    let aspect = float bg.Width / max (float bg.Height) 1.0
    let vhh = viewHalfV bg.Scene.Camera
    let vhw = vhh * aspect
    post slot.Handle
        {| kind = "render"
           epoch = bg.Epoch
           level = bg.Level
           tileX = tile.TileX
           tileY = tile.TileY
           tileW = tile.TileW
           tileH = tile.TileH
           fullW = bg.Width
           fullH = bg.Height
           viewHalfW = vhw
           viewHalfH = vhh
           half = vhh |}

let private dispatchAll (bg: Background) =
    for slot in bg.Workers do dispatch bg slot

/// Returns true if the tile carried a g-buffer; false for empty responses
/// (e.g. kernel has no scene loaded because lowering failed). Callers use
/// the result to decide whether the level accumulated any real pixels.
let private uploadRenderedTile (bg: Background) (data: obj) : bool =
    let tileX : int = data?tileX
    let tileY : int = data?tileY
    let tileW : int = data?tileW
    let tileH : int = data?tileH
    let buffer : obj = data?buffer
    let paletteBuffer : obj = data?paletteBuffer
    if isNull buffer then false else
    let view = f32OfBuffer buffer
    let destination =
        {| texture = bg.PendingTexture
           origin = {| x = tileX; y = tileY; z = 0 |} |}
    let dataLayout =
        {| offset = 0
           bytesPerRow = tileW * 16
           rowsPerImage = tileH |}
    let size =
        {| width = tileW
           height = tileH
           depthOrArrayLayers = 1 |}
    writeTexture bg.Scene.Device (box destination) view (box dataLayout) (box size)

    // Palette idx companion (one u32 per pixel = 4 bytes/row stride * width).
    if not (isNull paletteBuffer) then
        let paletteView = u32OfBuffer paletteBuffer
        let paletteDestination =
            {| texture = bg.PendingPaletteTexture
               origin = {| x = tileX; y = tileY; z = 0 |} |}
        let paletteLayout =
            {| offset = 0
               bytesPerRow = tileW * 4
               rowsPerImage = tileH |}
        writeTexture
            bg.Scene.Device (box paletteDestination) paletteView
            (box paletteLayout) (box size)
    true

/// Swap pending ↔ display. Pending's contents are now complete and
/// consistent; the old display texture becomes the next pending (its
/// stale content will be fully overwritten by the next level's tiles).
///
/// If `PendingNeedsResize` is set, the old Display we're rotating into
/// the Pending slot is at the pre-resize size — destroy it and create
/// a fresh Pending at the current size before the next dispatch tries
/// to write tile coords past the old bounds.
let private swapDisplay (bg: Background) =
    let t = bg.DisplayTexture
    let v = bg.DisplayView
    let g = bg.DisplayBindGroup
    let pt = bg.DisplayPaletteTexture
    let pv = bg.DisplayPaletteView
    bg.DisplayTexture <- bg.PendingTexture
    bg.DisplayView <- bg.PendingView
    bg.DisplayBindGroup <- bg.PendingBindGroup
    bg.DisplayPaletteTexture <- bg.PendingPaletteTexture
    bg.DisplayPaletteView <- bg.PendingPaletteView
    if bg.PendingNeedsResize then
        t.destroy ()
        pt.destroy ()
        bg.PendingTexture <- createTexture bg.Scene.Device bg.Width bg.Height
        bg.PendingView <- bg.PendingTexture.createView ()
        bg.PendingPaletteTexture <- createPaletteTexture bg.Scene.Device bg.Width bg.Height
        bg.PendingPaletteView <- bg.PendingPaletteTexture.createView ()
        bg.PendingBindGroup <-
            buildBindGroup bg.Scene.Device bg.Scene.BackgroundBindGroupLayout
                bg.PendingView bg.Scene.BackgroundSampler bg.FieldCameraBuffer
                bg.PendingPaletteView
        bg.PendingNeedsResize <- false
    else
        bg.PendingTexture <- t
        bg.PendingView <- v
        bg.PendingBindGroup <- g
        bg.PendingPaletteTexture <- pt
        bg.PendingPaletteView <- pv
    bg.HasDisplay <- true
    bg.DisplayDirty <- true

let private handleResponse (bg: Background) (slot: WorkerSlot) (data: obj) =
    let kind : string = data?kind
    match kind with
    | "ready" ->
        slot.Ready <- true
        // First worker to finish init sets the actual MaxLevel; later
        // workers running the same wasm report the same value.
        let reportedMax : int = data?maxLevel
        if reportedMax > 0 && reportedMax <> bg.MaxLevel then
            bg.MaxLevel <- reportedMax
        // IR might not be available yet at spawn time; `updateIr` will
        // push it directly once the editor compiles a field.
        if bg.HasIr then
            post slot.Handle {| kind = "ir"; bytes = bg.IrBytes |}
            post slot.Handle
                {| kind = "camera"; values = cameraValues bg.Scene.Camera |}
    | "ir-done" ->
        // The kernel returns a status code from `ir_upload` (0 = ok, 1
        // = bad magic, 7 = truncated, etc.). On failure the scene
        // never renders — surface to the console so a blank canvas
        // doesn't pass silently.
        let code : int = data?code
        if code <> 0 then
            console.warn (sprintf "kernel ir_upload failed: code=%d" code)
        // Worker is now fully initialized. Kick off the first tile.
        dispatch bg slot
    | "rendered" ->
        slot.Busy <- false
        let epoch : int = data?epoch
        if epoch = bg.Epoch then
            if uploadRenderedTile bg data then
                bg.TilesUploaded <- bg.TilesUploaded + 1
            bg.TilesRendered <- bg.TilesRendered + 1
            if bg.TilesRendered >= bg.Tiles.Length then
                // Only swap if at least one tile actually carried data.
                // Zero uploads means the kernel silently failed (e.g.
                // unsupported IR) — keep the previous display instead of
                // revealing an uninitialised pending texture.
                if bg.TilesUploaded > 0 then
                    swapDisplay bg
                // Only climb to the next refinement level when the
                // camera has been still for a bit. During motion we
                // stay pinned at START_LEVEL so each frame can complete
                // a fast coarse render; refinement is a quiet-window
                // luxury.
                let stable =
                    performanceNow () - bg.LastCameraChangeMs > MOTION_QUIET_MS
                if stable && bg.Level < bg.MaxLevel then
                    bg.Level <- bg.Level + 1
                    bg.TilesEmitted <- 0
                    bg.TilesRendered <- 0
                    bg.TilesUploaded <- 0
                // else: leave tile counters at their max so dispatch is
                // a no-op until either the camera moves (update() resets
                // everything) or stability is detected in update() and
                // the next level gets kicked off there.
        // Keep the worker busy with the next tile (either the current
        // level's remainder, or the next level's first tile).
        dispatch bg slot
    | _ -> ()

/// Spawn a fresh worker for `slot`, wire up handlers, and kick off init.
/// The worker replies "ready" → we send IR + camera → "ir-done" → dispatch.
let private initSlot (bg: Background) (slot: WorkerSlot) =
    slot.Handle <- createWorker "./Worker.js"
    slot.Ready <- false
    slot.Busy <- false
    slot.CurrentLevel <- 0
    onMessage slot.Handle (fun ev -> handleResponse bg slot (ev?data))
    onError slot.Handle (fun ev ->
        console.error ("kernel worker error", ev)
        slot.Busy <- false)
    post slot.Handle {| kind = "init"; wasmModule = bg.WasmModule |}

// ── Public API ─────────────────────────────────────────────────────────

/// Spawn the worker pool and allocate GPU resources. Workers load WASM
/// asynchronously; tiles start flowing as soon as each one is ready.
let create (scene: Scene.Scene) : JS.Promise<Background> =
    promise {
        // Compile once on the main thread; structured-clone the module
        // to every worker (and every respawn) to skip fetch + compile.
        let! wasmModule = Wasm.compile "/kernel/viewer.wasm"

        let w = max 1 (int scene.Canvas.width)
        let h = max 1 (int scene.Canvas.height)
        let displayTexture = createTexture scene.Device w h
        let displayView = displayTexture.createView ()
        let pendingTexture = createTexture scene.Device w h
        let pendingView = pendingTexture.createView ()
        let displayPaletteTexture = createPaletteTexture scene.Device w h
        let displayPaletteView = displayPaletteTexture.createView ()
        let pendingPaletteTexture = createPaletteTexture scene.Device w h
        let pendingPaletteView = pendingPaletteTexture.createView ()
        let fieldBuffer =
            scene.Device.createBuffer
                { size = 80
                  usage = GPUBufferUsage.Uniform ||| GPUBufferUsage.CopyDst }
        let displayBindGroup =
            buildBindGroup scene.Device scene.BackgroundBindGroupLayout
                displayView scene.BackgroundSampler fieldBuffer displayPaletteView
        let pendingBindGroup =
            buildBindGroup scene.Device scene.BackgroundBindGroupLayout
                pendingView scene.BackgroundSampler fieldBuffer pendingPaletteView

        // Reasonable placeholder: the kernel's max_render_level is 7
        // (PER_PIXEL_LEVEL). Worker echoes it back once ready, but we
        // don't want to block on that — use the known constant.
        let maxLevel = 7

        // Placeholder slots — `initSlot` below replaces .Handle and
        // wires up message handlers once `bg` exists.
        let workers : WorkerSlot[] =
            Array.init (workerCount ()) (fun _ ->
                { Handle = null; Ready = false; Busy = false; CurrentLevel = 0 })

        let aspect = float w / max (float h) 1.0
        let vhh = viewHalfV scene.Camera
        let vhw = vhh * aspect
        writeFieldCamera scene.Device fieldBuffer scene.Camera vhw vhh

        let bg : Background =
            { Scene = scene
              Workers = workers
              FieldCameraBuffer = fieldBuffer
              WasmModule = wasmModule
              IrBytes = null
              HasIr = false
              DisplayTexture = displayTexture
              DisplayView = displayView
              DisplayBindGroup = displayBindGroup
              PendingTexture = pendingTexture
              PendingView = pendingView
              PendingBindGroup = pendingBindGroup
              DisplayPaletteTexture = displayPaletteTexture
              DisplayPaletteView = displayPaletteView
              PendingPaletteTexture = pendingPaletteTexture
              PendingPaletteView = pendingPaletteView
              HasDisplay = false
              PendingNeedsResize = false
              Width = w
              Height = h
              Tiles = makeTiles w h
              Level = START_LEVEL
              MaxLevel = maxLevel
              TilesEmitted = 0
              TilesRendered = 0
              TilesUploaded = 0
              Epoch = 0
              LastCameraKey = cameraKey scene.Camera
              LastCameraChangeMs = performanceNow ()
              DisplayDirty = false }

        for slot in workers do initSlot bg slot

        return bg
    }

let resize (bg: Background) (w: int) (h: int) =
    if w = bg.Width && h = bg.Height then () else
    // Keep Display alive at its pre-resize size — the shader continues
    // sampling it (slightly stretched at non-matching resolution, since
    // UVs are normalized) so the canvas never blanks during a resolution
    // change. Only recreate Pending at the new size; on the next swap
    // we'll recreate the (now-Pending, old-size) texture too.
    bg.PendingTexture.destroy ()
    bg.PendingPaletteTexture.destroy ()
    bg.Width <- w
    bg.Height <- h
    bg.PendingTexture <- createTexture bg.Scene.Device w h
    bg.PendingView <- bg.PendingTexture.createView ()
    bg.PendingPaletteTexture <- createPaletteTexture bg.Scene.Device w h
    bg.PendingPaletteView <- bg.PendingPaletteTexture.createView ()
    bg.PendingBindGroup <-
        buildBindGroup bg.Scene.Device bg.Scene.BackgroundBindGroupLayout
            bg.PendingView bg.Scene.BackgroundSampler bg.FieldCameraBuffer
            bg.PendingPaletteView
    bg.PendingNeedsResize <- true
    bg.Tiles <- makeTiles w h
    bg.Level <- START_LEVEL
    bg.TilesEmitted <- 0
    bg.TilesRendered <- 0
    bg.TilesUploaded <- 0
    bg.Epoch <- bg.Epoch + 1
    // Any in-flight worker renders carry the old epoch and get discarded
    // on arrival; when they finish, `dispatch` picks up the new tiles.

/// Per-RAF entry point. Detects camera change, updates uniforms, tops up
/// workers. Most of the dispatch work happens inside response handlers,
/// so this is mostly idempotent.
let update (bg: Background) =
    let aspect = float bg.Width / max (float bg.Height) 1.0
    let vhh = viewHalfV bg.Scene.Camera
    let vhw = vhh * aspect

    let key = cameraKey bg.Scene.Camera
    if key <> bg.LastCameraKey then
        bg.LastCameraKey <- key
        bg.LastCameraChangeMs <- performanceNow ()
        bg.Epoch <- bg.Epoch + 1
        bg.Level <- START_LEVEL
        bg.TilesEmitted <- 0
        bg.TilesRendered <- 0
        writeFieldCamera bg.Scene.Device bg.FieldCameraBuffer
            bg.Scene.Camera vhw vhh
        // Kill workers stuck on a slow high-detail tile so the coarse
        // level can start dispatching now instead of after they finish.
        // Level-3 tiles are fast (~ms) — we let those complete naturally
        // to avoid thrashing on continuous camera motion. Respawn is
        // cheap because the compiled WASM module is shared from main.
        let camVals = cameraValues bg.Scene.Camera
        for slot in bg.Workers do
            if slot.Busy && slot.CurrentLevel > START_LEVEL then
                terminate slot.Handle
                initSlot bg slot
            elif slot.Ready then
                post slot.Handle {| kind = "camera"; values = camVals |}
    elif bg.Level < bg.MaxLevel
         && bg.TilesRendered >= bg.Tiles.Length
         && performanceNow () - bg.LastCameraChangeMs > MOTION_QUIET_MS then
        // Quiet transition: camera stopped moving, current level has
        // finished, and we're not yet at max detail → kick off the next
        // refinement level. This handles the case where handleResponse
        // saw motion and declined to advance; nothing else would have
        // restarted dispatch.
        bg.Level <- bg.Level + 1
        bg.TilesEmitted <- 0
        bg.TilesRendered <- 0
        // The promotion itself doesn't paint yet (workers haven't run),
        // but it means new tiles are about to flow. Mark dirty so the
        // viewer keeps RAF-ticking through the worker round-trip; the
        // next swapDisplay will dirty again with the actual new pixels.
        bg.DisplayDirty <- true

    // Keep the pool saturated — cheap when everyone's already busy.
    dispatchAll bg

/// Read-and-clear the dirty flag. Returns true once for every visible
/// state change (display swap or pending level promotion) since the last
/// call. The viewer's RAF loop ORs this into its dirty calculation so
/// async background work paints without depending on user input.
let consumeDisplayDirty (bg: Background) : bool =
    if bg.DisplayDirty then
        bg.DisplayDirty <- false
        true
    else
        false

let draw (bg: Background) (pass: IGPURenderPassEncoder) =
    if not bg.HasDisplay then () else
    pass.setPipeline bg.Scene.BackgroundPipeline
    pass.setBindGroup(0, bg.DisplayBindGroup)
    pass.setBindGroup(1, bg.Scene.CameraBindGroup)
    pass.draw 3

/// Editor sends new IR bytes (topology or slot-value change). Cache for
/// late-joining workers, broadcast to the ready ones, and restart the
/// refinement pyramid so the next frame shows the updated field.
let updateIr (bg: Background) (bytes: obj) =
    bg.IrBytes <- bytes
    bg.HasIr <- true
    bg.Epoch <- bg.Epoch + 1
    bg.Level <- START_LEVEL
    bg.TilesEmitted <- 0
    bg.TilesRendered <- 0
    bg.TilesUploaded <- 0
    // Treat an IR change like a camera change: pin at level 3 briefly so
    // a fast coarse frame lands before refinement resumes.
    bg.LastCameraChangeMs <- performanceNow ()
    let camVals = cameraValues bg.Scene.Camera
    for slot in bg.Workers do
        if slot.Busy then
            // In-flight tile would land on the old IR. Respawn — the new
            // worker picks up the fresh IR through its ready handshake.
            terminate slot.Handle
            initSlot bg slot
        elif slot.Ready then
            post slot.Handle {| kind = "ir"; bytes = bytes |}
            post slot.Handle {| kind = "camera"; values = camVals |}
        // else: still booting; "ready" handler will read bg.IrBytes.

/// Editor reports there's nothing to render (no surfaces, or all empty).
/// Blank the display and stop workers so we don't keep chewing on the
/// previous field.
let clear (bg: Background) =
    bg.HasIr <- false
    bg.IrBytes <- null
    bg.HasDisplay <- false
    bg.Epoch <- bg.Epoch + 1
    bg.Level <- START_LEVEL
    bg.TilesEmitted <- 0
    bg.TilesRendered <- 0
    bg.TilesUploaded <- 0
    for slot in bg.Workers do
        if slot.Busy then
            terminate slot.Handle
            initSlot bg slot
