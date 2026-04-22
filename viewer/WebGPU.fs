module WebGPU

// ----------------------------------------------------------------------------
// Typed WebGPU bindings for the F# viewer.
//
// Design:
// * Opaque handles are AllowNullLiteral interfaces — Fable erases them to
//   plain property/method access at the JS layer.
// * Simple descriptor records are modelled as F# records (fields match the
//   JS shape exactly). Fable compiles them to plain objects.
// * Complex descriptors with many optional fields (pipelines with blend
//   state, render passes with multiple attachments) take `obj` and callers
//   use anonymous records `{| ... |}` at the construction site. This matches
//   JS WebGPU idiom and avoids an explosion of record variants.
// * Emit helpers are provided for awkward patterns (multi-attachment passes,
//   TypedArray construction).
// ----------------------------------------------------------------------------

open Fable.Core
open Fable.Core.JsInterop

// ── Flag constants ──────────────────────────────────────────────────────

[<RequireQualifiedAccess>]
module GPUBufferUsage =
    let MapRead = 0x0001
    let MapWrite = 0x0002
    let CopySrc = 0x0004
    let CopyDst = 0x0008
    let Index = 0x0010
    let Vertex = 0x0020
    let Uniform = 0x0040
    let Storage = 0x0080
    let Indirect = 0x0100
    let QueryResolve = 0x0200

[<RequireQualifiedAccess>]
module GPUShaderStage =
    let Vertex = 0x1
    let Fragment = 0x2
    let Compute = 0x4

[<RequireQualifiedAccess>]
module GPUTextureUsage =
    let CopySrc = 0x01
    let CopyDst = 0x02
    let TextureBinding = 0x04
    let StorageBinding = 0x08
    let RenderAttachment = 0x10

[<RequireQualifiedAccess>]
module GPUMapMode =
    let Read = 0x0001
    let Write = 0x0002

// ── Opaque handles ──────────────────────────────────────────────────────

type [<AllowNullLiteral>] IGPUShaderModule = interface end
type [<AllowNullLiteral>] IGPUPipelineLayout = interface end
type [<AllowNullLiteral>] IGPUBindGroupLayout = interface end
type [<AllowNullLiteral>] IGPUBindGroup = interface end
type [<AllowNullLiteral>] IGPUCommandBuffer = interface end
type [<AllowNullLiteral>] IGPUSampler = interface end
type [<AllowNullLiteral>] IGPUTextureView = interface end

type [<AllowNullLiteral>] IGPUBuffer =
    abstract mapAsync: mode: int -> JS.Promise<unit>
    abstract getMappedRange: unit -> JS.ArrayBuffer
    abstract unmap: unit -> unit
    abstract destroy: unit -> unit
    abstract size: int

type [<AllowNullLiteral>] IGPUTexture =
    abstract createView: unit -> IGPUTextureView
    abstract destroy: unit -> unit
    abstract width: int
    abstract height: int

type [<AllowNullLiteral>] IGPURenderPipeline =
    abstract getBindGroupLayout: index: int -> IGPUBindGroupLayout

type [<AllowNullLiteral>] IGPUComputePipeline =
    abstract getBindGroupLayout: index: int -> IGPUBindGroupLayout

// ── Pass / command encoders ─────────────────────────────────────────────

type [<AllowNullLiteral>] IGPURenderPassEncoder =
    abstract setPipeline: pipeline: IGPURenderPipeline -> unit
    abstract setBindGroup: index: int * bindGroup: IGPUBindGroup -> unit
    abstract setVertexBuffer: slot: int * buffer: IGPUBuffer -> unit
    abstract setIndexBuffer: buffer: IGPUBuffer * indexFormat: string -> unit
    abstract draw: vertexCount: int -> unit
    abstract drawIndexed: indexCount: int -> unit
    abstract setScissorRect: x: int * y: int * width: int * height: int -> unit
    abstract setViewport: x: float * y: float * w: float * h: float * minDepth: float * maxDepth: float -> unit

    [<Emit("$0.end()")>]
    abstract endPass: unit -> unit

    [<Emit("$0.draw($1, $2)")>]
    abstract drawInstanced: vertexCount: int * instanceCount: int -> unit

type [<AllowNullLiteral>] IGPUComputePassEncoder =
    abstract setPipeline: pipeline: IGPUComputePipeline -> unit
    abstract setBindGroup: index: int * bindGroup: IGPUBindGroup -> unit
    abstract dispatchWorkgroups: x: int * y: int * z: int -> unit

    [<Emit("$0.end()")>]
    abstract endPass: unit -> unit

type [<AllowNullLiteral>] IGPUCommandEncoder =
    abstract beginRenderPass: descriptor: obj -> IGPURenderPassEncoder
    abstract beginComputePass: unit -> IGPUComputePassEncoder
    abstract copyBufferToBuffer:
        src: IGPUBuffer * srcOffset: int *
        dst: IGPUBuffer * dstOffset: int *
        size: int -> unit
    abstract finish: unit -> IGPUCommandBuffer

    // copyTextureToBuffer takes nested descriptors; easier via Emit helper
    // at the bottom of this module.

// ── Queue ───────────────────────────────────────────────────────────────

type [<AllowNullLiteral>] IGPUQueue =
    abstract submit: commandBuffers: IGPUCommandBuffer[] -> unit

// queue.writeBuffer has several overloads (typed array vs ArrayBuffer vs
// offset+size variants). Keep the interface minimal and expose the common
// typed-array variants via Emit helpers at the bottom of this file.

// ── Simple descriptor records ───────────────────────────────────────────

type GPUBufferDescriptor = { size: int; usage: int }
type GPUShaderModuleDescriptor = { code: string }

type GPUProgrammableStage =
    { ``module``: IGPUShaderModule
      entryPoint: string }

type GPUVertexAttribute =
    { shaderLocation: int
      offset: int
      format: string }

type GPUVertexBufferLayout =
    { arrayStride: int
      stepMode: string
      attributes: GPUVertexAttribute[] }

type GPUPrimitiveState = { topology: string }

type GPUDepthStencilState =
    { format: string
      depthWriteEnabled: bool
      depthCompare: string }

// ── Bind group layout entries ───────────────────────────────────────────
//
// A layout entry takes one of `{ buffer: ... }`, `{ texture: ... }`, or
// `{ sampler: ... }` depending on the binding type. We model each variant
// as its own record and type the layout descriptor's `entries` as `obj[]`
// so call sites can mix variants.

type GPUBufferBindingLayout = { ``type``: string }

type GPUBufferBindGroupLayoutEntry =
    { binding: int
      visibility: int
      buffer: GPUBufferBindingLayout }

type GPUTextureBindingLayout =
    { sampleType: string
      viewDimension: string }

type GPUTextureBindGroupLayoutEntry =
    { binding: int
      visibility: int
      texture: GPUTextureBindingLayout }

type GPUSamplerBindingLayout = { ``type``: string }

type GPUSamplerBindGroupLayoutEntry =
    { binding: int
      visibility: int
      sampler: GPUSamplerBindingLayout }

type GPUBindGroupLayoutDescriptor = { entries: obj[] }

type GPUPipelineLayoutDescriptor = { bindGroupLayouts: IGPUBindGroupLayout[] }

// ── Bind group entries ──────────────────────────────────────────────────
//
// Resource is union-shaped: `{ buffer }`, or a raw texture view, or a raw
// sampler. Use `obj` for the resource field and construct at call site.

type GPUBufferBinding = { buffer: IGPUBuffer }
type GPUBindGroupEntry = { binding: int; resource: obj }
type GPUBindGroupDescriptor =
    { layout: IGPUBindGroupLayout
      entries: GPUBindGroupEntry[] }

// ── Texture / sampler descriptors ───────────────────────────────────────

type GPUExtent3D = { width: int; height: int; depthOrArrayLayers: int }

type GPUTextureDescriptor =
    { size: GPUExtent3D
      format: string
      usage: int }

// Sampler descriptors vary a lot; accept `obj` from callers.

// ── Canvas context ──────────────────────────────────────────────────────

type GPUCanvasConfiguration =
    { device: obj
      format: string
      alphaMode: string }

type [<AllowNullLiteral>] IGPUCanvasContext =
    abstract configure: config: GPUCanvasConfiguration -> unit
    abstract getCurrentTexture: unit -> IGPUTexture

// ── Device ──────────────────────────────────────────────────────────────
//
// Pipeline + pipeline-layout creation signatures take `obj` because the
// descriptor shape varies (blend state, depth state, multiple color
// targets, etc.). Callers use anonymous records.

type [<AllowNullLiteral>] IGPUDevice =
    abstract queue: IGPUQueue
    abstract createBuffer: descriptor: GPUBufferDescriptor -> IGPUBuffer
    abstract createShaderModule: descriptor: GPUShaderModuleDescriptor -> IGPUShaderModule
    abstract createBindGroupLayout: descriptor: GPUBindGroupLayoutDescriptor -> IGPUBindGroupLayout
    abstract createPipelineLayout: descriptor: GPUPipelineLayoutDescriptor -> IGPUPipelineLayout
    abstract createRenderPipeline: descriptor: obj -> IGPURenderPipeline
    abstract createComputePipeline: descriptor: obj -> IGPUComputePipeline
    abstract createBindGroup: descriptor: GPUBindGroupDescriptor -> IGPUBindGroup
    abstract createTexture: descriptor: GPUTextureDescriptor -> IGPUTexture
    abstract createSampler: descriptor: obj -> IGPUSampler
    abstract createCommandEncoder: unit -> IGPUCommandEncoder
    abstract destroy: unit -> unit

// ── Adapter + navigator ─────────────────────────────────────────────────

type [<AllowNullLiteral>] IGPUAdapter =
    abstract requestDevice: unit -> JS.Promise<IGPUDevice>

type [<AllowNullLiteral>] IGPU =
    abstract requestAdapter: unit -> JS.Promise<IGPUAdapter>
    abstract getPreferredCanvasFormat: unit -> string

[<Emit("navigator.gpu ?? null")>]
let private gpuRaw: IGPU = jsNative

let gpu () : IGPU option =
    if isNull gpuRaw then None else Some gpuRaw

// ── Helpers for awkward-to-type constructs ──────────────────────────────

/// canvas.getContext('webgpu') — returns null if WebGPU isn't available.
[<Emit("$0.getContext('webgpu')")>]
let getWebgpuContext (canvas: obj) : IGPUCanvasContext = jsNative

/// writeBuffer with Float32 data (wraps in Float32Array view).
[<Emit("$0.writeBuffer($1, $2, new Float32Array($3))")>]
let writeFloat32 (queue: IGPUQueue) (buffer: IGPUBuffer) (offset: int) (data: float32[]) : unit = jsNative

/// writeBuffer with Uint32 data.
[<Emit("$0.writeBuffer($1, $2, new Uint32Array($3))")>]
let writeUint32 (queue: IGPUQueue) (buffer: IGPUBuffer) (offset: int) (data: uint32[]) : unit = jsNative

/// writeBuffer passing a Float32Array (or other typed array) directly —
/// used when data is already a typed array (e.g. from a worker message).
[<Emit("$0.writeBuffer($1, $2, $3)")>]
let writeBufferRaw (queue: IGPUQueue) (buffer: IGPUBuffer) (offset: int) (data: obj) : unit = jsNative

/// Convert an F# float32[] to a real Float32Array, exposing `.buffer` for
/// structured-clone transfer.
[<Emit("new Float32Array($0)")>]
let toFloat32Array (arr: float32[]) : obj = jsNative

[<Emit("new Uint32Array($0)")>]
let toUint32Array (arr: uint32[]) : obj = jsNative

[<Emit("$0.buffer")>]
let typedArrayBuffer (typedArr: obj) : obj = jsNative

/// Copy a mapped ArrayBuffer as a Float32Array.
[<Emit("new Float32Array($0).slice()")>]
let f32ArrayOf (buffer: JS.ArrayBuffer) : float32[] = jsNative

[<Emit("new Uint32Array($0).slice()")>]
let u32ArrayOf (buffer: JS.ArrayBuffer) : uint32[] = jsNative

/// window.requestAnimationFrame wrapper returning the handle.
[<Emit("window.requestAnimationFrame($0)")>]
let requestAnimationFrame (callback: float -> unit) : int = jsNative

/// Cancel a scheduled animation frame.
[<Emit("window.cancelAnimationFrame($0)")>]
let cancelAnimationFrame (handle: int) : unit = jsNative

/// High-resolution monotonic timestamp (ms, sub-millisecond precision).
[<Emit("performance.now()")>]
let performanceNow () : float = jsNative

/// Copy an ImageBitmap into a GPU texture. Used for the MSDF atlas.
[<Emit("$0.queue.copyExternalImageToTexture({ source: $1 }, { texture: $2 }, [$3, $4])")>]
let copyImageBitmapToTexture
    (device: IGPUDevice)
    (image: obj)
    (texture: IGPUTexture)
    (width: int) (height: int) : unit = jsNative

/// Fetch a URL and produce an ImageBitmap (Promise).
[<Emit("fetch($0).then(r => r.blob()).then(b => createImageBitmap(b, { colorSpaceConversion: 'none' }))")>]
let fetchImageBitmap (url: string) : JS.Promise<obj> = jsNative

/// Fetch a URL and parse the body as JSON.
[<Emit("fetch($0).then(r => r.json())")>]
let fetchJson (url: string) : JS.Promise<obj> = jsNative

/// Read an image's .width / .height.
[<Emit("$0.width")>]
let imageWidth (image: obj) : int = jsNative
[<Emit("$0.height")>]
let imageHeight (image: obj) : int = jsNative

/// Copy a 1×1 pixel from a 2D texture into a buffer at offset 0.
/// bytesPerRow is set to 256, the minimum alignment WebGPU requires.
[<Emit("""$0.copyTextureToBuffer(
    { texture: $1, origin: { x: $2, y: $3, z: 0 }, mipLevel: 0 },
    { buffer: $4, bytesPerRow: 256 },
    { width: 1, height: 1, depthOrArrayLayers: 1 }
)""")>]
let copyTextureToBuffer1x1
    (encoder: IGPUCommandEncoder)
    (texture: IGPUTexture)
    (x: int) (y: int)
    (buffer: IGPUBuffer) : unit = jsNative

/// Read the first u32 from a mapped buffer's range.
[<Emit("new Uint32Array($0)[0] >>> 0")>]
let readFirstU32 (buffer: JS.ArrayBuffer) : uint32 = jsNative

/// Begin a simple single-target render pass with a depth attachment and a
/// clear color. Use for the common case; for multi-attachment passes
/// (e.g. the pick pass that also writes to an r32uint target) construct
/// the descriptor via `{| ... |}` and call `encoder.beginRenderPass`
/// directly.
[<Emit("""$0.beginRenderPass({
    colorAttachments: [{
        view: $1,
        loadOp: 'clear',
        storeOp: 'store',
        clearValue: { r: $2, g: $3, b: $4, a: 1.0 }
    }],
    depthStencilAttachment: {
        view: $5,
        depthLoadOp: 'clear',
        depthStoreOp: 'store',
        depthClearValue: 1.0
    }
})""")>]
let beginRenderPassClearColor
    (encoder: IGPUCommandEncoder)
    (colorView: IGPUTextureView)
    (r: float)
    (g: float)
    (b: float)
    (depthView: IGPUTextureView)
    : IGPURenderPassEncoder = jsNative
