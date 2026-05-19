module ImagePlane

// Reference-image planes: render a flat textured quad in 3D space
// for each `ImageBody` block. The image is fetched from the block's
// URL and cached as a GPU texture, keyed by `BlockId`. URL changes
// trigger a refetch; block deletion frees the texture.
//
// Picks no part of the SDF math — purely visual overlay for CAD
// blueprint use. Drawn after the background pass and before the
// field-slice overlay, with depth-test enabled so SDF bodies
// occlude the quad where they intersect.

open Fable.Core
open Fable.Core.JsInterop
open System.Collections.Generic
open Browser.Types
open Server
open Server.Lang
open WebGPU

// ── GPU resource layout ────────────────────────────────────────────────

/// Per-quad uniforms: origin (vec4) + x_axis (vec4) + y_axis (vec4)
/// + half_width, half_height, opacity, pad (4 floats). 64 bytes total.
let private QUAD_BUFFER_BYTES = 64

/// Single 1×1 transparent pixel used as a placeholder texture while
/// a real image is still loading. Lets us build the bind group up
/// front and avoid an "if-texture-Some" branch in the draw loop.
let private TRANSPARENT_PIXEL : byte[] = [| 0uy; 0uy; 0uy; 0uy |]

// ── Cache entry ────────────────────────────────────────────────────────

type private ImageEntry =
    { mutable Url: string
      // The actual loaded texture once the fetch completes. `None`
      // while a request is in flight or the URL is empty.
      mutable Texture: IGPUTexture option
      // The bind group binds the quad buffer + the currently bound
      // texture. Recreated whenever `Texture` changes.
      mutable BindGroup: IGPUBindGroup
      // Per-block 64-byte uniform buffer holding the quad placement.
      QuadBuffer: IGPUBuffer
      // Generation counter — bumped each time we kick off a fetch
      // so a late callback for a stale URL is discarded.
      mutable Generation: int
      mutable Ready: bool }

// ── Public type ────────────────────────────────────────────────────────

type ImagePlaneRenderer =
    private
        { Scene: Scene.Scene
          Pipeline: IGPURenderPipeline
          QuadBindGroupLayout: IGPUBindGroupLayout
          Sampler: IGPUSampler
          PlaceholderTexture: IGPUTexture
          PlaceholderView: IGPUTextureView
          Entries: Dictionary<Notebook.BlockId, ImageEntry> }

// ── Plane → world axes ─────────────────────────────────────────────────

let private planeAxes (plane: SketchPlane) : Vec3 * Vec3 =
    // Local X / Y of the sketch plane, expressed in world coords.
    // Matches the `SlicePlane.AxisX` / `AxisY` convention so XY-plane
    // sketches sit on the world XY plane with their local X = world X.
    match plane with
    | XY ->
        { X = 1.0; Y = 0.0; Z = 0.0 },
        { X = 0.0; Y = 1.0; Z = 0.0 }
    | XZ ->
        { X = 1.0; Y = 0.0; Z = 0.0 },
        { X = 0.0; Y = 0.0; Z = 1.0 }
    | YZ ->
        { X = 0.0; Y = 1.0; Z = 0.0 },
        { X = 0.0; Y = 0.0; Z = 1.0 }

// ── GPU resource helpers ───────────────────────────────────────────────

let private uniformBuffer (device: IGPUDevice) (size: int) : IGPUBuffer =
    device.createBuffer
        { size = size
          usage = GPUBufferUsage.Uniform ||| GPUBufferUsage.CopyDst }

let private alphaBlend () =
    {| color = {| srcFactor = "src-alpha"; dstFactor = "one-minus-src-alpha"; operation = "add" |}
       alpha = {| srcFactor = "one"; dstFactor = "one-minus-src-alpha"; operation = "add" |} |}

let private makeBindGroup
        (device: IGPUDevice)
        (layout: IGPUBindGroupLayout)
        (buffer: IGPUBuffer)
        (view: IGPUTextureView)
        (sampler: IGPUSampler) : IGPUBindGroup =
    device.createBindGroup
        { layout = layout
          entries =
            [| { binding = 0; resource = box { buffer = buffer } }
               { binding = 1; resource = box view }
               { binding = 2; resource = box sampler } |] }

[<Emit("(new Uint8Array($0)).buffer")>]
let private bytesToArrayBuffer (data: byte[]) : obj = jsNative

[<Emit("$0.queue.writeTexture({ texture: $1 }, $2, { bytesPerRow: $3 }, [$4, $5])")>]
let private writeTexture
    (device: IGPUDevice)
    (texture: IGPUTexture)
    (data: obj)
    (bytesPerRow: int)
    (width: int) (height: int) : unit = jsNative

let private createPlaceholderTexture (device: IGPUDevice) : IGPUTexture =
    let tex =
        device.createTexture
            { size = { width = 1; height = 1; depthOrArrayLayers = 1 }
              format = "rgba8unorm"
              usage = GPUTextureUsage.TextureBinding ||| GPUTextureUsage.CopyDst }
    writeTexture device tex (bytesToArrayBuffer TRANSPARENT_PIXEL) 256 1 1
    tex

// ── Public API ─────────────────────────────────────────────────────────

let create (scene: Scene.Scene) : ImagePlaneRenderer =
    let device = scene.Device

    let quadBindGroupLayout =
        device.createBindGroupLayout
            { entries =
                [| box
                    {| binding = 0
                       visibility = GPUShaderStage.Vertex ||| GPUShaderStage.Fragment
                       buffer = {| ``type`` = "uniform" |} |}
                   box
                    {| binding = 1
                       visibility = GPUShaderStage.Fragment
                       texture = {| sampleType = "float"; viewDimension = "2d" |} |}
                   box
                    {| binding = 2
                       visibility = GPUShaderStage.Fragment
                       sampler = {| ``type`` = "filtering" |} |} |] }

    let pipelineLayout =
        device.createPipelineLayout
            { bindGroupLayouts = [| scene.CameraBindGroupLayout; quadBindGroupLayout |] }

    let shader = device.createShaderModule { code = Shaders.imagePlane }

    let pipeline =
        device.createRenderPipeline
            (box
                {| layout = pipelineLayout
                   vertex =
                    {| ``module`` = shader
                       entryPoint = "vs" |}
                   fragment =
                    {| ``module`` = shader
                       entryPoint = "fs"
                       targets = [| {| format = scene.Format; blend = alphaBlend () |} |] |}
                   primitive = {| topology = "triangle-list" |}
                   depthStencil =
                    {| format = "depth24plus"; depthWriteEnabled = false; depthCompare = "less" |} |})

    let sampler =
        device.createSampler
            (box
                {| magFilter = "linear"
                   minFilter = "linear"
                   addressModeU = "clamp-to-edge"
                   addressModeV = "clamp-to-edge" |})

    let placeholderTex = createPlaceholderTexture device
    let placeholderView = placeholderTex.createView ()

    { Scene = scene
      Pipeline = pipeline
      QuadBindGroupLayout = quadBindGroupLayout
      Sampler = sampler
      PlaceholderTexture = placeholderTex
      PlaceholderView = placeholderView
      Entries = Dictionary() }

// ── Fetch + texture lifecycle ──────────────────────────────────────────

let private kickFetch
        (renderer: ImagePlaneRenderer)
        (blockId: Notebook.BlockId)
        (entry: ImageEntry)
        (url: string) : unit =
    if System.String.IsNullOrWhiteSpace url then
        // Empty URL — clear texture, fall back to placeholder.
        match entry.Texture with
        | Some t -> t.destroy ()
        | None -> ()
        entry.Texture <- None
        entry.Ready <- false
        entry.BindGroup <-
            makeBindGroup
                renderer.Scene.Device renderer.QuadBindGroupLayout
                entry.QuadBuffer renderer.PlaceholderView renderer.Sampler
    else
        entry.Generation <- entry.Generation + 1
        let gen = entry.Generation
        fetchImageBitmap url
        |> Promise.iter (fun image ->
            // Discard if a newer fetch superseded this one, or if
            // the block was removed mid-flight.
            if entry.Generation <> gen then () else
            let w = imageWidth image
            let h = imageHeight image
            if w <= 0 || h <= 0 then () else
            let tex =
                renderer.Scene.Device.createTexture
                    { size = { width = w; height = h; depthOrArrayLayers = 1 }
                      format = "rgba8unorm"
                      usage =
                          GPUTextureUsage.TextureBinding
                          ||| GPUTextureUsage.CopyDst
                          ||| GPUTextureUsage.RenderAttachment }
            copyImageBitmapToTexture renderer.Scene.Device image tex w h
            // Replace any prior texture.
            match entry.Texture with
            | Some t -> t.destroy ()
            | None -> ()
            entry.Texture <- Some tex
            entry.Ready <- true
            entry.BindGroup <-
                makeBindGroup
                    renderer.Scene.Device renderer.QuadBindGroupLayout
                    entry.QuadBuffer (tex.createView ()) renderer.Sampler)

let private ensureEntry
        (renderer: ImagePlaneRenderer)
        (blockId: Notebook.BlockId)
        (url: string) : ImageEntry =
    match renderer.Entries.TryGetValue blockId with
    | true, existing ->
        if existing.Url <> url then
            existing.Url <- url
            kickFetch renderer blockId existing url
        existing
    | _ ->
        let quadBuffer = uniformBuffer renderer.Scene.Device QUAD_BUFFER_BYTES
        let entry =
            { Url = url
              Texture = None
              QuadBuffer = quadBuffer
              BindGroup =
                  makeBindGroup
                      renderer.Scene.Device renderer.QuadBindGroupLayout
                      quadBuffer renderer.PlaceholderView renderer.Sampler
              Generation = 0
              Ready = false }
        renderer.Entries.[blockId] <- entry
        if not (System.String.IsNullOrWhiteSpace url) then
            kickFetch renderer blockId entry url
        entry

let private evictMissing
        (renderer: ImagePlaneRenderer)
        (live: Set<Notebook.BlockId>) : unit =
    let dead = ResizeArray<Notebook.BlockId>()
    for kv in renderer.Entries do
        if not (Set.contains kv.Key live) then dead.Add kv.Key
    for id in dead do
        let entry = renderer.Entries.[id]
        match entry.Texture with
        | Some t -> t.destroy ()
        | None -> ()
        renderer.Entries.Remove id |> ignore

let private writeQuadUniforms
        (renderer: ImagePlaneRenderer)
        (entry: ImageEntry)
        (data: Notebook.ImageData) : unit =
    let xAxisRaw, yAxisRaw = planeAxes data.Plane
    // Rotation is stored in degrees, applied as an in-plane CCW
    // rotation about the origin. Pre-rotate the plane basis here so
    // the shader stays a plain projection.
    let theta = data.Rotation * System.Math.PI / 180.0
    let c = cos theta
    let s = sin theta
    let xAxis =
        { X = c * xAxisRaw.X + s * yAxisRaw.X
          Y = c * xAxisRaw.Y + s * yAxisRaw.Y
          Z = c * xAxisRaw.Z + s * yAxisRaw.Z }
    let yAxis =
        { X = -s * xAxisRaw.X + c * yAxisRaw.X
          Y = -s * xAxisRaw.Y + c * yAxisRaw.Y
          Z = -s * xAxisRaw.Z + c * yAxisRaw.Z }
    let buf : float32[] = Array.zeroCreate (QUAD_BUFFER_BYTES / 4)
    // origin (vec4 — vec3 + pad)
    buf.[0]  <- float32 data.Origin.X
    buf.[1]  <- float32 data.Origin.Y
    buf.[2]  <- float32 data.Origin.Z
    buf.[3]  <- 0.0f
    // x_axis (vec4)
    buf.[4]  <- float32 xAxis.X
    buf.[5]  <- float32 xAxis.Y
    buf.[6]  <- float32 xAxis.Z
    buf.[7]  <- 0.0f
    // y_axis (vec4)
    buf.[8]  <- float32 yAxis.X
    buf.[9]  <- float32 yAxis.Y
    buf.[10] <- float32 yAxis.Z
    buf.[11] <- 0.0f
    // half_width, half_height, opacity, pad
    buf.[12] <- float32 (data.Width  * 0.5)
    buf.[13] <- float32 (data.Height * 0.5)
    buf.[14] <- float32 data.Opacity
    buf.[15] <- 0.0f
    WebGPU.writeFloat32 renderer.Scene.Device.queue entry.QuadBuffer 0 buf

/// Walk the current notebook, refresh per-block quad uniforms, kick
/// off fetches for new/changed URLs, evict cache entries for removed
/// blocks. Cheap when the image-block set is stable.
let update (renderer: ImagePlaneRenderer) (doc: Server.Document) : unit =
    let live = ResizeArray<Notebook.BlockId>()
    for block in doc.Blocks do
        match block.Body with
        | Notebook.ImageBody data when block.Visibility = Notebook.VIsosurface ->
            let entry = ensureEntry renderer block.Id data.Url
            writeQuadUniforms renderer entry data
            live.Add block.Id
        | _ -> ()
    evictMissing renderer (Set.ofSeq live)

/// Issue one draw per ready image block onto the supplied render
/// pass. Must be called inside a render pass that has the camera
/// bind group bound at group 0.
let draw
        (renderer: ImagePlaneRenderer)
        (pass: IGPURenderPassEncoder)
        (doc: Server.Document) : unit =
    let mutable bound = false
    for block in doc.Blocks do
        match block.Body with
        | Notebook.ImageBody _ when block.Visibility = Notebook.VIsosurface ->
            match renderer.Entries.TryGetValue block.Id with
            | true, entry when entry.Ready ->
                if not bound then
                    pass.setPipeline renderer.Pipeline
                    pass.setBindGroup (0, renderer.Scene.CameraBindGroup)
                    bound <- true
                pass.setBindGroup (1, entry.BindGroup)
                pass.draw 6
            | _ -> ()
        | _ -> ()
