module Scene

// All persistent GPU resources bundled into one record. The mount function
// builds a `Scene`, hands it to `Input`, `Render`, and `DimensionEditor`.

open Fable.Core
open Fable.Core.JsInterop
open Browser.Types
open Server
open WebGPU
open MsdfAtlas

type Scene =
    { // Canvas + renderer
      Device: IGPUDevice
      Ctx: IGPUCanvasContext
      Canvas: HTMLCanvasElement
      Dpr: float
      Format: string

      // Textures (mutable — recreated on resize)
      mutable DepthTex: IGPUTexture
      mutable PickTex: IGPUTexture

      // Uniform buffers + bind groups
      CameraBuffer: IGPUBuffer
      CameraBindGroup: IGPUBindGroup
      FrameBuffer: IGPUBuffer
      FrameBindGroup: IGPUBindGroup
      ViewportBuffer: IGPUBuffer
      ViewportBindGroup: IGPUBindGroup
      LabelUniformBuffer: IGPUBuffer
      LabelBindGroup: IGPUBindGroup

      // Pipelines
      LinePipeline: IGPURenderPipeline
      TriPipeline: IGPURenderPipeline
      PointPipeline: IGPURenderPipeline
      PointPickPipeline: IGPURenderPipeline
      LoopPickPipeline: IGPURenderPipeline
      LinePickPipeline: IGPURenderPipeline
      GizmoPipeline: IGPURenderPipeline
      WorldPointPipeline: IGPURenderPipeline
      WorldPointPickPipeline: IGPURenderPipeline
      LabelPipeline: IGPURenderPipeline
      BackgroundPipeline: IGPURenderPipeline

      // Background blit (kernel pixel-buffer → full-screen triangle).
      BackgroundBindGroupLayout: IGPUBindGroupLayout
      BackgroundSampler: IGPUSampler

      // Static geometry
      PointQuadBuffer: IGPUBuffer
      LinePickCornerBuffer: IGPUBuffer

      // Pick readback
      PickReadBuffer: IGPUBuffer

      // Font atlas
      Atlas: MsdfAtlas
      FontMetrics: FontMetrics

      // Buffer pool
      Pool: BufferPool.Pool
      Slots: BufferPool.Slots

      // Camera
      Camera: Camera.CameraState }

let private alphaBlend () =
    {| color = {| srcFactor = "src-alpha"; dstFactor = "one-minus-src-alpha"; operation = "add" |}
       alpha = {| srcFactor = "one"; dstFactor = "one-minus-src-alpha"; operation = "add" |} |}

let private uniformBuffer (device: IGPUDevice) (size: int) : IGPUBuffer =
    device.createBuffer
        { size = size
          usage = GPUBufferUsage.Uniform ||| GPUBufferUsage.CopyDst }

let private staticVertexBuffer (device: IGPUDevice) (data: float32[]) : IGPUBuffer =
    let buf =
        device.createBuffer
            { size = data.Length * 4
              usage = GPUBufferUsage.Vertex ||| GPUBufferUsage.CopyDst }
    WebGPU.writeFloat32 device.queue buf 0 data
    buf

let private vertexOnlyLayout (device: IGPUDevice) : IGPUBindGroupLayout =
    device.createBindGroupLayout
        { entries =
            [| box
                {| binding = 0
                   visibility = GPUShaderStage.Vertex
                   buffer = {| ``type`` = "uniform" |} |} |] }

/// Build the full scene. Called once from `Viewer.mount`.
let create
        (device: IGPUDevice)
        (ctx: IGPUCanvasContext)
        (canvas: HTMLCanvasElement)
        (dpr: float)
        (format: string)
        (atlas: MsdfAtlas)
        (fontMetrics: FontMetrics)
        : Scene =

    // ── Pick readback buffer (256 B = WebGPU's min bytesPerRow alignment).
    let pickReadBuffer =
        device.createBuffer
            { size = 256
              usage = GPUBufferUsage.CopyDst ||| GPUBufferUsage.MapRead }

    // ── Camera / frame / viewport / label uniforms ────────────────────
    let cameraBuffer = uniformBuffer device 64
    let cameraBindGroupLayout =
        device.createBindGroupLayout
            { entries =
                [| box
                    {| binding = 0
                       visibility = GPUShaderStage.Vertex ||| GPUShaderStage.Fragment
                       buffer = {| ``type`` = "uniform" |} |} |] }
    let cameraBindGroup =
        device.createBindGroup
            { layout = cameraBindGroupLayout
              entries = [| { binding = 0; resource = box { buffer = cameraBuffer } } |] }

    let frameBuffer = uniformBuffer device 64
    let frameBindGroupLayout = vertexOnlyLayout device
    let frameBindGroup =
        device.createBindGroup
            { layout = frameBindGroupLayout
              entries = [| { binding = 0; resource = box { buffer = frameBuffer } } |] }

    let viewportBuffer = uniformBuffer device 16
    let viewportBindGroupLayout = vertexOnlyLayout device
    let viewportBindGroup =
        device.createBindGroup
            { layout = viewportBindGroupLayout
              entries = [| { binding = 0; resource = box { buffer = viewportBuffer } } |] }

    let labelUniformBuffer = uniformBuffer device 64
    let labelBindGroupLayout =
        device.createBindGroupLayout
            { entries =
                [| box
                    {| binding = 0
                       visibility = GPUShaderStage.Vertex
                       buffer = {| ``type`` = "uniform" |} |}
                   box
                    {| binding = 1
                       visibility = GPUShaderStage.Fragment
                       texture = {| sampleType = "float"; viewDimension = "2d" |} |}
                   box
                    {| binding = 2
                       visibility = GPUShaderStage.Fragment
                       sampler = {| ``type`` = "filtering" |} |} |] }
    let labelBindGroup =
        device.createBindGroup
            { layout = labelBindGroupLayout
              entries =
                [| { binding = 0; resource = box { buffer = labelUniformBuffer } }
                   { binding = 1; resource = box (atlas.Texture.createView()) }
                   { binding = 2; resource = box atlas.Sampler } |] }

    // ── Static vertex geometry ────────────────────────────────────────
    // Billboard quad corners for the instanced point pipelines.
    let pointQuadBuffer =
        staticVertexBuffer device
            [| -1.0f; -1.0f; 1.0f; -1.0f; -1.0f; 1.0f
               1.0f; -1.0f;  1.0f;  1.0f; -1.0f; 1.0f |]

    // Thick-line pick extrusion corners (t, s).
    let linePickCornerBuffer =
        staticVertexBuffer device
            [| 0.0f; -1.0f; 1.0f; -1.0f; 0.0f; 1.0f
               1.0f; -1.0f;  1.0f;  1.0f; 0.0f; 1.0f |]

    // ── Shaders ───────────────────────────────────────────────────────
    let lineShader = device.createShaderModule { code = Shaders.line }
    let pointShader = device.createShaderModule { code = Shaders.point }
    let loopPickShader = device.createShaderModule { code = Shaders.loopPick }
    let linePickShader = device.createShaderModule { code = Shaders.linePick }
    let pointPickShader = device.createShaderModule { code = Shaders.pointPick }
    let gizmoShader = device.createShaderModule { code = Shaders.gizmo }
    let worldPointShader = device.createShaderModule { code = Shaders.worldPoint }
    let worldPointPickShader = device.createShaderModule { code = Shaders.worldPointPick }
    let labelShader = device.createShaderModule { code = Shaders.label }

    // ── Pipeline layouts ──────────────────────────────────────────────
    let camFrameLayout =
        device.createPipelineLayout
            { bindGroupLayouts = [| cameraBindGroupLayout; frameBindGroupLayout |] }

    let camFrameViewportLayout =
        device.createPipelineLayout
            { bindGroupLayouts =
                [| cameraBindGroupLayout; frameBindGroupLayout; viewportBindGroupLayout |] }

    let camViewportLayout =
        device.createPipelineLayout
            { bindGroupLayouts = [| cameraBindGroupLayout; viewportBindGroupLayout |] }

    let camLabelLayout =
        device.createPipelineLayout
            { bindGroupLayouts = [| cameraBindGroupLayout; labelBindGroupLayout |] }

    // ── Line / tri (loop fill) pipelines ─────────────────────────────
    let lineVertexBuffers =
        [| {| arrayStride = 6 * 4
              stepMode = "vertex"
              attributes =
                [| {| shaderLocation = 0; offset = 0; format = "float32x2" |}
                   {| shaderLocation = 1; offset = 8; format = "float32x4" |} |] |} |]

    let linePipeline =
        device.createRenderPipeline
            (box
                {| layout = camFrameLayout
                   vertex =
                    {| ``module`` = lineShader
                       entryPoint = "vs"
                       buffers = lineVertexBuffers |}
                   fragment =
                    {| ``module`` = lineShader
                       entryPoint = "fs"
                       targets = [| {| format = format; blend = alphaBlend () |} |] |}
                   primitive = {| topology = "line-list" |}
                   depthStencil =
                    {| format = "depth24plus"; depthWriteEnabled = false; depthCompare = "less" |} |})

    let triPipeline =
        device.createRenderPipeline
            (box
                {| layout = camFrameLayout
                   vertex =
                    {| ``module`` = lineShader
                       entryPoint = "vs"
                       buffers = lineVertexBuffers |}
                   fragment =
                    {| ``module`` = lineShader
                       entryPoint = "fs"
                       targets = [| {| format = format; blend = alphaBlend () |} |] |}
                   primitive = {| topology = "triangle-list" |}
                   depthStencil =
                    {| format = "depth24plus"; depthWriteEnabled = false; depthCompare = "less" |} |})

    // ── Point pipelines ──────────────────────────────────────────────
    // Vertex slot 0 = quad corners. Slot 1 = per-point instance data.
    let pointColorInstanceLayout =
        {| arrayStride = 7 * 4
           stepMode = "instance"
           attributes =
            [| {| shaderLocation = 1; offset = 0;  format = "float32x2" |}
               {| shaderLocation = 2; offset = 8;  format = "float32" |}
               {| shaderLocation = 3; offset = 12; format = "float32x4" |} |] |}

    let pointPickInstanceLayout =
        {| arrayStride = 4 * 4
           stepMode = "instance"
           attributes =
            [| {| shaderLocation = 1; offset = 0;  format = "float32x2" |}
               {| shaderLocation = 2; offset = 8;  format = "float32" |}
               {| shaderLocation = 3; offset = 12; format = "float32" |} |] |}

    let quadCornerLayout =
        {| arrayStride = 2 * 4
           stepMode = "vertex"
           attributes =
            [| {| shaderLocation = 0; offset = 0; format = "float32x2" |} |] |}

    let pointPipeline =
        device.createRenderPipeline
            (box
                {| layout = camFrameViewportLayout
                   vertex =
                    {| ``module`` = pointShader
                       entryPoint = "vs"
                       buffers = [| box quadCornerLayout; box pointColorInstanceLayout |] |}
                   fragment =
                    {| ``module`` = pointShader
                       entryPoint = "fs"
                       targets = [| {| format = format; blend = alphaBlend () |} |] |}
                   primitive = {| topology = "triangle-list" |}
                   depthStencil =
                    {| format = "depth24plus"; depthWriteEnabled = false; depthCompare = "less" |} |})

    let pointPickPipeline =
        device.createRenderPipeline
            (box
                {| layout = camFrameViewportLayout
                   vertex =
                    {| ``module`` = pointPickShader
                       entryPoint = "vs"
                       buffers = [| box quadCornerLayout; box pointPickInstanceLayout |] |}
                   fragment =
                    {| ``module`` = pointPickShader
                       entryPoint = "fs"
                       targets = [| {| format = "r32uint" |} |] |}
                   primitive = {| topology = "triangle-list" |}
                   depthStencil =
                    {| format = "depth24plus"; depthWriteEnabled = true; depthCompare = "less" |} |})

    // ── Loop pick (triangulated loop fills → r32uint) ────────────────
    let loopPickPipeline =
        device.createRenderPipeline
            (box
                {| layout = camFrameLayout
                   vertex =
                    {| ``module`` = loopPickShader
                       entryPoint = "vs"
                       buffers =
                        [| {| arrayStride = 3 * 4
                              stepMode = "vertex"
                              attributes =
                                [| {| shaderLocation = 0; offset = 0; format = "float32x2" |}
                                   {| shaderLocation = 1; offset = 8; format = "float32" |} |] |} |] |}
                   fragment =
                    {| ``module`` = loopPickShader
                       entryPoint = "fs"
                       targets = [| {| format = "r32uint" |} |] |}
                   primitive = {| topology = "triangle-list" |}
                   depthStencil =
                    {| format = "depth24plus"; depthWriteEnabled = true; depthCompare = "less" |} |})

    // ── Line pick (thick extruded segments) ──────────────────────────
    let linePickPipeline =
        device.createRenderPipeline
            (box
                {| layout = camFrameLayout
                   vertex =
                    {| ``module`` = linePickShader
                       entryPoint = "vs"
                       buffers =
                        [| {| arrayStride = 2 * 4
                              stepMode = "vertex"
                              attributes =
                                [| {| shaderLocation = 0; offset = 0; format = "float32x2" |} |] |}
                           {| arrayStride = 5 * 4
                              stepMode = "instance"
                              attributes =
                                [| {| shaderLocation = 1; offset = 0;  format = "float32x2" |}
                                   {| shaderLocation = 2; offset = 8;  format = "float32x2" |}
                                   {| shaderLocation = 3; offset = 16; format = "float32" |} |] |} |] |}
                   fragment =
                    {| ``module`` = linePickShader
                       entryPoint = "fs"
                       targets = [| {| format = "r32uint" |} |] |}
                   primitive = {| topology = "triangle-list" |}
                   depthStencil =
                    {| format = "depth24plus"; depthWriteEnabled = true; depthCompare = "less" |} |})

    // ── Gizmo (screen-scaled axis lines, line-list) ──────────────────
    let gizmoPipeline =
        device.createRenderPipeline
            (box
                {| layout = camViewportLayout
                   vertex =
                    {| ``module`` = gizmoShader
                       entryPoint = "vs"
                       buffers =
                        [| {| arrayStride = 12 * 4
                              stepMode = "vertex"
                              attributes =
                                [| {| shaderLocation = 0; offset = 0;  format = "float32x3" |}
                                   {| shaderLocation = 1; offset = 12; format = "float32x3" |}
                                   {| shaderLocation = 2; offset = 24; format = "float32" |}
                                   {| shaderLocation = 3; offset = 28; format = "float32" |}
                                   {| shaderLocation = 4; offset = 32; format = "float32x4" |} |] |} |] |}
                   fragment =
                    {| ``module`` = gizmoShader
                       entryPoint = "fs"
                       targets = [| {| format = format; blend = alphaBlend () |} |] |}
                   primitive = {| topology = "line-list" |}
                   depthStencil =
                    {| format = "depth24plus"; depthWriteEnabled = false; depthCompare = "less" |} |})

    // ── World-space point (no frame uniform) + pick variant ──────────
    let worldPointPipeline =
        device.createRenderPipeline
            (box
                {| layout = camViewportLayout
                   vertex =
                    {| ``module`` = worldPointShader
                       entryPoint = "vs"
                       buffers =
                        [| {| arrayStride = 2 * 4
                              stepMode = "vertex"
                              attributes =
                                [| {| shaderLocation = 0; offset = 0; format = "float32x2" |} |] |}
                           {| arrayStride = 8 * 4
                              stepMode = "instance"
                              attributes =
                                [| {| shaderLocation = 1; offset = 0;  format = "float32x3" |}
                                   {| shaderLocation = 2; offset = 12; format = "float32" |}
                                   {| shaderLocation = 3; offset = 16; format = "float32x4" |} |] |} |] |}
                   fragment =
                    {| ``module`` = worldPointShader
                       entryPoint = "fs"
                       targets = [| {| format = format; blend = alphaBlend () |} |] |}
                   primitive = {| topology = "triangle-list" |}
                   depthStencil =
                    {| format = "depth24plus"; depthWriteEnabled = false; depthCompare = "less" |} |})

    let worldPointPickPipeline =
        device.createRenderPipeline
            (box
                {| layout = camViewportLayout
                   vertex =
                    {| ``module`` = worldPointPickShader
                       entryPoint = "vs"
                       buffers =
                        [| {| arrayStride = 2 * 4
                              stepMode = "vertex"
                              attributes =
                                [| {| shaderLocation = 0; offset = 0; format = "float32x2" |} |] |}
                           {| arrayStride = 5 * 4
                              stepMode = "instance"
                              attributes =
                                [| {| shaderLocation = 1; offset = 0;  format = "float32x3" |}
                                   {| shaderLocation = 2; offset = 12; format = "float32" |}
                                   {| shaderLocation = 3; offset = 16; format = "float32" |} |] |} |] |}
                   fragment =
                    {| ``module`` = worldPointPickShader
                       entryPoint = "fs"
                       targets = [| {| format = "r32uint" |} |] |}
                   primitive = {| topology = "triangle-list" |}
                   depthStencil =
                    {| format = "depth24plus"; depthWriteEnabled = true; depthCompare = "less" |} |})

    // ── MSDF label pipeline ──────────────────────────────────────────
    let labelPipeline =
        device.createRenderPipeline
            (box
                {| layout = camLabelLayout
                   vertex =
                    {| ``module`` = labelShader
                       entryPoint = "vs"
                       buffers =
                        [| {| arrayStride = 10 * 4
                              stepMode = "vertex"
                              attributes =
                                [| {| shaderLocation = 0; offset = 0;  format = "float32x2" |}
                                   {| shaderLocation = 1; offset = 8;  format = "float32x2" |}
                                   {| shaderLocation = 2; offset = 16; format = "float32x2" |}
                                   {| shaderLocation = 3; offset = 24; format = "float32x4" |} |] |} |] |}
                   fragment =
                    {| ``module`` = labelShader
                       entryPoint = "fs"
                       targets = [| {| format = format; blend = alphaBlend () |} |] |}
                   primitive = {| topology = "triangle-list" |}
                   depthStencil =
                    {| format = "depth24plus"; depthWriteEnabled = false; depthCompare = "less" |} |})

    // ── Background: field G-buffer → shaded + depth-writing draw ─────
    // Samples a rgba32float texture (normal.xyz, wcz), reconstructs the
    // hit's world position, writes frag_depth via the viewer's camera so
    // sketches z-test against the field surface. Sampler is non-filtering
    // because rgba32float filtering needs the `float32-filterable` feature.
    let backgroundShader = device.createShaderModule { code = Shaders.background }

    let backgroundBindGroupLayout =
        device.createBindGroupLayout
            { entries =
                [| box
                    {| binding = 0
                       visibility = GPUShaderStage.Fragment
                       texture =
                        {| sampleType = "unfilterable-float"; viewDimension = "2d" |} |}
                   box
                    {| binding = 1
                       visibility = GPUShaderStage.Fragment
                       sampler = {| ``type`` = "non-filtering" |} |}
                   box
                    {| binding = 2
                       visibility = GPUShaderStage.Fragment
                       buffer = {| ``type`` = "uniform" |} |} |] }

    let backgroundSampler =
        device.createSampler
            (box
                {| magFilter = "nearest"; minFilter = "nearest"
                   addressModeU = "clamp-to-edge"; addressModeV = "clamp-to-edge" |})

    let backgroundPipelineLayout =
        device.createPipelineLayout
            { bindGroupLayouts = [| backgroundBindGroupLayout; cameraBindGroupLayout |] }

    let backgroundPipeline =
        device.createRenderPipeline
            (box
                {| layout = backgroundPipelineLayout
                   vertex = {| ``module`` = backgroundShader; entryPoint = "vs" |}
                   fragment =
                    {| ``module`` = backgroundShader
                       entryPoint = "fs"
                       targets = [| {| format = format |} |] |}
                   primitive = {| topology = "triangle-list" |}
                   depthStencil =
                    {| format = "depth24plus"; depthWriteEnabled = true; depthCompare = "less" |} |})

    { Device = device
      Ctx = ctx
      Canvas = canvas
      Dpr = dpr
      Format = format

      DepthTex = Unchecked.defaultof<_>
      PickTex = Unchecked.defaultof<_>

      CameraBuffer = cameraBuffer
      CameraBindGroup = cameraBindGroup
      FrameBuffer = frameBuffer
      FrameBindGroup = frameBindGroup
      ViewportBuffer = viewportBuffer
      ViewportBindGroup = viewportBindGroup
      LabelUniformBuffer = labelUniformBuffer
      LabelBindGroup = labelBindGroup

      LinePipeline = linePipeline
      TriPipeline = triPipeline
      PointPipeline = pointPipeline
      PointPickPipeline = pointPickPipeline
      LoopPickPipeline = loopPickPipeline
      LinePickPipeline = linePickPipeline
      GizmoPipeline = gizmoPipeline
      WorldPointPipeline = worldPointPipeline
      WorldPointPickPipeline = worldPointPickPipeline
      LabelPipeline = labelPipeline
      BackgroundPipeline = backgroundPipeline

      BackgroundBindGroupLayout = backgroundBindGroupLayout
      BackgroundSampler = backgroundSampler

      PointQuadBuffer = pointQuadBuffer
      LinePickCornerBuffer = linePickCornerBuffer

      PickReadBuffer = pickReadBuffer

      Atlas = atlas
      FontMetrics = fontMetrics

      Pool = BufferPool.createPool device
      Slots = BufferPool.createSlots ()

      Camera = Camera.create () }

/// Recreate depth + pick textures at the current canvas size. Call from the
/// ResizeObserver callback and from mount.
let remakeAttachments (scene: Scene) =
    let w : int = scene.Canvas?width
    let h : int = scene.Canvas?height
    if w > 0 && h > 0 then
        if not (isNull (box scene.DepthTex)) then scene.DepthTex.destroy()
        scene.DepthTex <-
            scene.Device.createTexture
                { size = { width = w; height = h; depthOrArrayLayers = 1 }
                  format = "depth24plus"
                  usage = GPUTextureUsage.RenderAttachment }
        if not (isNull (box scene.PickTex)) then scene.PickTex.destroy()
        scene.PickTex <-
            scene.Device.createTexture
                { size = { width = w; height = h; depthOrArrayLayers = 1 }
                  format = "r32uint"
                  usage = GPUTextureUsage.RenderAttachment ||| GPUTextureUsage.CopySrc }
