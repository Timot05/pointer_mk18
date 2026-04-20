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
//   3. On a worker response we upload, mark the worker free, and push
//      the next tile.
//   4. When all tiles of the current level are uploaded, advance level.
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

/// Workers to spawn. Leave at least one core free for the main thread.
let private workerCount () : int =
    max 1 (min 4 (hardwareConcurrency - 1))

/// Max pixel size the kernel can handle per tile (Zig's MAX_W/MAX_H).
let private MAX_TILE = 1024
/// Coarsest refinement level we ever start from.
let private START_LEVEL = 3

// ── Worker bindings ────────────────────────────────────────────────────

[<Emit("new Worker(new URL($0, import.meta.url), { type: 'module' })")>]
let private createWorker (path: string) : obj = jsNative

[<Emit("$0.addEventListener('message', $1)")>]
let private onMessage (w: obj) (h: obj -> unit) : unit = jsNative

[<Emit("$0.addEventListener('error', $1)")>]
let private onError (w: obj) (h: obj -> unit) : unit = jsNative

[<Emit("$0.postMessage($1)")>]
let private post (w: obj) (msg: obj) : unit = jsNative

[<Emit("new Float32Array($0)")>]
let private f32OfBuffer (buf: obj) : obj = jsNative

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
    { Handle: obj
      mutable Ready: bool    // `ready` received
      mutable Busy: bool }   // tile in flight

type Background =
    private
        { Scene: Scene.Scene
          Workers: WorkerSlot[]
          FieldCameraBuffer: IGPUBuffer
          IrBytes: obj        // cached for workers that come online later
          mutable Texture: IGPUTexture
          mutable TextureView: IGPUTextureView
          mutable BindGroup: IGPUBindGroup
          mutable Width: int
          mutable Height: int
          mutable Tiles: Tile[]
          mutable Level: int
          mutable MaxLevel: int
          // Per-level bookkeeping.
          mutable TilesEmitted: int
          mutable TilesRendered: int
          // Bumped on any camera/size change. Stale worker responses
          // carry an old epoch and get discarded.
          mutable Epoch: int
          mutable LastCameraKey: float }

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

let private buildBindGroup
        (device: IGPUDevice)
        (layout: IGPUBindGroupLayout)
        (view: IGPUTextureView)
        (sampler: IGPUSampler)
        (fieldBuffer: IGPUBuffer) : IGPUBindGroup =
    device.createBindGroup
        { layout = layout
          entries =
            [| { binding = 0; resource = box view }
               { binding = 1; resource = box sampler }
               { binding = 2; resource = box { buffer = fieldBuffer } } |] }

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

let private buildDemoIr () : obj =
    let ir = IrCodec.create ()
    let outer = IrCodec.sphere ir 18.0
    let cut = IrCodec.translate ir 10.0 0.0 10.0 (IrCodec.sphere ir 11.0)
    let root = IrCodec.subtract ir outer cut
    IrCodec.serialize ir root

// ── Dispatch ───────────────────────────────────────────────────────────

/// Send one tile to a free worker; no-op if none are free.
let private dispatch (bg: Background) (slot: WorkerSlot) =
    if slot.Busy || not slot.Ready then () else
    if bg.Level > bg.MaxLevel then () else
    if bg.TilesEmitted >= bg.Tiles.Length then () else
    let tile = bg.Tiles.[bg.TilesEmitted]
    bg.TilesEmitted <- bg.TilesEmitted + 1
    slot.Busy <- true
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

let private uploadRenderedTile (bg: Background) (data: obj) =
    let tileX : int = data?tileX
    let tileY : int = data?tileY
    let tileW : int = data?tileW
    let tileH : int = data?tileH
    let buffer : obj = data?buffer
    if isNull buffer then () else
    let view = f32OfBuffer buffer
    let destination =
        {| texture = bg.Texture
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

let private handleResponse (bg: Background) (slot: WorkerSlot) (data: obj) =
    let kind : string = data?kind
    match kind with
    | "ready" ->
        slot.Ready <- true
        // IR upload is the next step for this worker.
        post slot.Handle {| kind = "ir"; bytes = bg.IrBytes |}
        // Camera too, so it's ready to render.
        post slot.Handle {| kind = "camera"; values = cameraValues bg.Scene.Camera |}
    | "ir-done" ->
        // Worker is now fully initialized. Kick off the first tile.
        dispatch bg slot
    | "rendered" ->
        slot.Busy <- false
        let epoch : int = data?epoch
        if epoch = bg.Epoch then
            uploadRenderedTile bg data
            bg.TilesRendered <- bg.TilesRendered + 1
            if bg.TilesRendered >= bg.Tiles.Length then
                bg.Level <- bg.Level + 1
                bg.TilesEmitted <- 0
                bg.TilesRendered <- 0
        // Keep the worker busy with the next tile (either the current
        // level's remainder, or the next level's first tile).
        dispatch bg slot
    | _ -> ()

// ── Public API ─────────────────────────────────────────────────────────

/// Spawn the worker pool and allocate GPU resources. Workers load WASM
/// asynchronously; tiles start flowing as soon as each one is ready.
let create (scene: Scene.Scene) : JS.Promise<Background> =
    promise {
        let w = max 1 (int scene.Canvas.width)
        let h = max 1 (int scene.Canvas.height)
        let texture = createTexture scene.Device w h
        let view = texture.createView ()
        let fieldBuffer =
            scene.Device.createBuffer
                { size = 80
                  usage = GPUBufferUsage.Uniform ||| GPUBufferUsage.CopyDst }
        let bindGroup =
            buildBindGroup scene.Device scene.BackgroundBindGroupLayout
                view scene.BackgroundSampler fieldBuffer

        // Reasonable placeholder: the kernel's max_render_level is 7
        // (PER_PIXEL_LEVEL). Worker echoes it back once ready, but we
        // don't want to block on that — use the known constant.
        let maxLevel = 7

        let ir = buildDemoIr ()

        let workers : WorkerSlot[] =
            Array.init (workerCount ()) (fun _ ->
                let handle = createWorker "./Worker.js"
                { Handle = handle; Ready = false; Busy = false })

        let aspect = float w / max (float h) 1.0
        let vhh = viewHalfV scene.Camera
        let vhw = vhh * aspect
        writeFieldCamera scene.Device fieldBuffer scene.Camera vhw vhh

        let bg : Background =
            { Scene = scene
              Workers = workers
              FieldCameraBuffer = fieldBuffer
              IrBytes = ir
              Texture = texture
              TextureView = view
              BindGroup = bindGroup
              Width = w
              Height = h
              Tiles = makeTiles w h
              Level = START_LEVEL
              MaxLevel = maxLevel
              TilesEmitted = 0
              TilesRendered = 0
              Epoch = 0
              LastCameraKey = cameraKey scene.Camera }

        // Wire up message handlers (closes over bg) and kick each worker
        // into initialization.
        for slot in workers do
            onMessage slot.Handle (fun ev -> handleResponse bg slot (ev?data))
            onError slot.Handle (fun ev ->
                console.error ("kernel worker error", ev)
                slot.Busy <- false)
            post slot.Handle {| kind = "init" |}

        return bg
    }

let resize (bg: Background) (w: int) (h: int) =
    if w = bg.Width && h = bg.Height then () else
    bg.Texture.destroy ()
    bg.Width <- w
    bg.Height <- h
    bg.Texture <- createTexture bg.Scene.Device w h
    bg.TextureView <- bg.Texture.createView ()
    bg.BindGroup <-
        buildBindGroup bg.Scene.Device bg.Scene.BackgroundBindGroupLayout
            bg.TextureView bg.Scene.BackgroundSampler bg.FieldCameraBuffer
    bg.Tiles <- makeTiles w h
    bg.Level <- START_LEVEL
    bg.TilesEmitted <- 0
    bg.TilesRendered <- 0
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
        bg.Epoch <- bg.Epoch + 1
        bg.Level <- START_LEVEL
        bg.TilesEmitted <- 0
        bg.TilesRendered <- 0
        writeFieldCamera bg.Scene.Device bg.FieldCameraBuffer
            bg.Scene.Camera vhw vhh
        let camVals = cameraValues bg.Scene.Camera
        for slot in bg.Workers do
            if slot.Ready then
                post slot.Handle {| kind = "camera"; values = camVals |}

    // Keep the pool saturated — cheap when everyone's already busy.
    dispatchAll bg

let draw (bg: Background) (pass: IGPURenderPassEncoder) =
    pass.setPipeline bg.Scene.BackgroundPipeline
    pass.setBindGroup(0, bg.BindGroup)
    pass.setBindGroup(1, bg.Scene.CameraBindGroup)
    pass.draw 3
