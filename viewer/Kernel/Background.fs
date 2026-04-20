module Kernel.Background

// Background field renderer. Drives the Zig WASM kernel, uploads its
// per-pixel G-buffer (normal.xyz, wcz) into a rgba32float texture, and
// draws a full-screen triangle at the start of the main viewer's color
// pass. The fragment shader reconstructs the hit's world position and
// writes `frag_depth` through the viewer's perspective camera, so sketch
// overlay geometry z-tests correctly against the field surface.
//
// Scheduling: progressive refinement by level, tile-by-tile within each
// level. On camera change, reset to (level = START_LEVEL, tile = 0). Each
// RAF tick renders one tile and uploads that sub-rect into the GPU
// texture.

open Fable.Core
open Fable.Core.JsInterop
open Browser.Types
open Server
open WebGPU

// ── Tile scheduling constants ──────────────────────────────────────────

/// Max pixel size the kernel can handle per tile (Zig's MAX_W/MAX_H).
let private MAX_TILE = 1024
/// Coarsest level we ever start from. Higher = less blocky first frame.
let private START_LEVEL = 3

// ── Types ──────────────────────────────────────────────────────────────

type private Tile =
    { TileX: int
      TileY: int
      TileW: int
      TileH: int }

/// Progressive render schedule: coarse-to-fine levels, full tile grid at
/// each level. Reset to the start when the camera moves.
type private Schedule =
    { mutable Level: int
      mutable TileIdx: int
      mutable Dirty: bool }

type Background =
    private
        { Scene: Scene.Scene
          Exports: Wasm.Exports
          mutable Width: int
          mutable Height: int
          mutable Texture: IGPUTexture
          mutable TextureView: IGPUTextureView
          mutable BindGroup: IGPUBindGroup
          FieldCameraBuffer: IGPUBuffer
          mutable Tiles: Tile[]
          mutable MaxLevel: int
          Schedule: Schedule
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
                    { TileX = x
                      TileY = y
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

let private createBindGroup
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

/// Stable key for "did the camera change?" detection — resets the
/// progressive schedule whenever it does.
let private cameraKey (camera: Camera.CameraState) : float =
    camera.Azimuth * 1e6
    + camera.Elevation * 1e3
    + camera.Distance * 1e-2
    + camera.Target.X + camera.Target.Y * 0.1 + camera.Target.Z * 0.01

/// Copy camera basis to the kernel's camera buffer + invoke set_camera.
/// The kernel's "eye" is the centre of an orthographic slab — we want it
/// on the look-at target so the scene sits inside the slab.
let private pushCamera (x: Wasm.Exports) (camera: Camera.CameraState) =
    let b = Camera.basis camera
    let t = camera.Target
    let values : float32[] =
        [| float32 t.X;         float32 t.Y;         float32 t.Z
           float32 b.Right.X;   float32 b.Right.Y;   float32 b.Right.Z
           float32 b.Up.X;      float32 b.Up.Y;      float32 b.Up.Z
           float32 b.Forward.X; float32 b.Forward.Y; float32 b.Forward.Z |]
    Wasm.setCamera x values |> ignore

/// Write the field-camera uniform (used by the shader to reconstruct
/// world positions from uv + wcz and drive `frag_depth`).
let private writeFieldCamera
        (device: IGPUDevice) (buffer: IGPUBuffer)
        (camera: Camera.CameraState)
        (viewHalfW: float) (viewHalfH: float) =
    let b = Camera.basis camera
    let t = camera.Target
    // 5 × vec4<f32> = 80 bytes. std140-style padding (trailing `_pad`).
    let data : float32[] =
        [| float32 t.X;         float32 t.Y;         float32 t.Z;         0.0f
           float32 b.Right.X;   float32 b.Right.Y;   float32 b.Right.Z;   0.0f
           float32 b.Up.X;      float32 b.Up.Y;      float32 b.Up.Z;      0.0f
           float32 b.Forward.X; float32 b.Forward.Y; float32 b.Forward.Z; 0.0f
           float32 viewHalfW;   float32 viewHalfH;   0.0f;                0.0f |]
    WebGPU.writeFloat32 device.queue buffer 0 data

/// View half-extent (vertical) derived from the main viewer's camera.
/// Matches a perspective frustum's size at the target's distance so
/// zooming naturally shrinks / grows the field render.
let private viewHalfV (camera: Camera.CameraState) : float =
    camera.Distance * tan Camera.HALF_FOV

// ── Demo scene (TODO: pull from state.Compiled field actions) ──────────

let private buildDemoIr () : obj =
    // Sized to sit comfortably inside the main viewer's default camera
    // frustum (target = origin, distance = ~80 → view half ≈ 33).
    let ir = IrCodec.create ()
    let outer = IrCodec.sphere ir 18.0
    let cut = IrCodec.translate ir 10.0 0.0 10.0 (IrCodec.sphere ir 11.0)
    let root = IrCodec.subtract ir outer cut
    IrCodec.serialize ir root

// ── Public API ─────────────────────────────────────────────────────────

/// Async-create a Background bound to the given scene's canvas size. Loads
/// the kernel WASM, uploads a demo scene, allocates the initial GPU
/// texture + uniform buffer + bind group.
let create (scene: Scene.Scene) : JS.Promise<Background> =
    promise {
        let! exports = Wasm.load "/kernel/viewer.wasm"
        let code = Wasm.uploadIr exports (buildDemoIr ())
        if code <> 0 then failwithf "ir_upload failed: %d" code
        let maxLevel = exports.max_render_level ()

        let w = max 1 (int scene.Canvas.width)
        let h = max 1 (int scene.Canvas.height)
        let texture = createTexture scene.Device w h
        let view = texture.createView ()
        let fieldBuffer =
            scene.Device.createBuffer
                { size = 80  // 5 × vec4<f32>
                  usage = GPUBufferUsage.Uniform ||| GPUBufferUsage.CopyDst }
        let bindGroup =
            createBindGroup scene.Device scene.BackgroundBindGroupLayout
                view scene.BackgroundSampler fieldBuffer
        return
            { Scene = scene
              Exports = exports
              Width = w
              Height = h
              Texture = texture
              TextureView = view
              BindGroup = bindGroup
              FieldCameraBuffer = fieldBuffer
              Tiles = makeTiles w h
              MaxLevel = maxLevel
              Schedule = { Level = START_LEVEL; TileIdx = 0; Dirty = true }
              LastCameraKey = nan }
    }

/// Recreate the GPU texture + tile grid at the new canvas size. Call from
/// the resize path.
let resize (bg: Background) (w: int) (h: int) =
    if w = bg.Width && h = bg.Height then () else
    bg.Texture.destroy ()
    bg.Width <- w
    bg.Height <- h
    bg.Texture <- createTexture bg.Scene.Device w h
    bg.TextureView <- bg.Texture.createView ()
    bg.BindGroup <-
        createBindGroup bg.Scene.Device bg.Scene.BackgroundBindGroupLayout
            bg.TextureView bg.Scene.BackgroundSampler bg.FieldCameraBuffer
    bg.Tiles <- makeTiles w h
    bg.Schedule.Level <- START_LEVEL
    bg.Schedule.TileIdx <- 0
    bg.Schedule.Dirty <- true

[<Emit("$0.queue.writeTexture($1, $2, $3, $4)")>]
let private writeTexture
        (device: IGPUDevice)
        (destination: obj) (data: obj) (dataLayout: obj) (size: obj) : unit = jsNative

/// Advance the render schedule by one step: sync camera if dirty, render
/// one tile at the current level, upload to the GPU texture. Intended to
/// be called once per RAF.
let update (bg: Background) =
    let aspect = float bg.Width / max (float bg.Height) 1.0
    let viewHalfH = viewHalfV bg.Scene.Camera
    let viewHalfW = viewHalfH * aspect
    // Slab depth matches view half → isotropic view cube sized with zoom.
    let half = viewHalfH

    // Camera-change detection — on change, restart at the coarsest level
    // and republish the field-camera uniform for the shader.
    let key = cameraKey bg.Scene.Camera
    if key <> bg.LastCameraKey then
        bg.LastCameraKey <- key
        pushCamera bg.Exports bg.Scene.Camera
        writeFieldCamera bg.Scene.Device bg.FieldCameraBuffer
            bg.Scene.Camera viewHalfW viewHalfH
        bg.Schedule.Level <- START_LEVEL
        bg.Schedule.TileIdx <- 0
        bg.Schedule.Dirty <- true

    if bg.Schedule.Dirty then bg.Schedule.Dirty <- false

    // Stop advancing when we've reached the finest level on every tile.
    if bg.Schedule.Level > bg.MaxLevel then () else

    if bg.Tiles.Length = 0 then () else

    let tile = bg.Tiles.[bg.Schedule.TileIdx]
    let written =
        bg.Exports.render_voxels
            (tile.TileW, tile.TileH,
             bg.Width, bg.Height,
             tile.TileX, tile.TileY,
             viewHalfW, viewHalfH,
             half, bg.Schedule.Level)
    if written > 0 then
        // Kernel wrote tile.TileW * tile.TileH rgba32float pixels into
        // `gbuffer` starting at offset 0. Upload into the matching sub-
        // rect of the GPU texture.
        let src = Wasm.gbufferView bg.Exports tile.TileW tile.TileH
        let destination =
            {| texture = bg.Texture
               origin = {| x = tile.TileX; y = tile.TileY; z = 0 |} |}
        let dataLayout =
            {| offset = 0
               bytesPerRow = tile.TileW * 16  // 4 × f32
               rowsPerImage = tile.TileH |}
        let size =
            {| width = tile.TileW
               height = tile.TileH
               depthOrArrayLayers = 1 |}
        writeTexture bg.Scene.Device (box destination) src (box dataLayout) (box size)

    // Advance: tile → next tile; when the level is complete, step to the
    // next finer level.
    bg.Schedule.TileIdx <- bg.Schedule.TileIdx + 1
    if bg.Schedule.TileIdx >= bg.Tiles.Length then
        bg.Schedule.TileIdx <- 0
        bg.Schedule.Level <- bg.Schedule.Level + 1

/// Draw the background into the given color pass. Called at the start of
/// the color pass so sketch geometry depth-tests against the field.
let draw (bg: Background) (pass: IGPURenderPassEncoder) =
    pass.setPipeline bg.Scene.BackgroundPipeline
    pass.setBindGroup(0, bg.BindGroup)
    pass.setBindGroup(1, bg.Scene.CameraBindGroup)
    pass.draw 3
