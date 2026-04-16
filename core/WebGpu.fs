namespace Server

open Fable.Core
open Fable.Core.JsInterop

// Typed WebGPU bindings — compute-shader subset only.
// This stays intentionally small: just enough for buffer-backed compute
// dispatch and readback, which is the only capability the solver needs.

[<RequireQualifiedAccess>]
module GPUBufferUsage =
    let MapRead = 0x0001
    let CopySrc = 0x0004
    let CopyDst = 0x0008
    let Uniform = 0x0040
    let Storage = 0x0080

[<RequireQualifiedAccess>]
module GPUMapMode =
    let Read = 0x0001
    let Write = 0x0002

[<RequireQualifiedAccess>]
module GPUShaderStage =
    let Compute = 0x4

type [<AllowNullLiteral>] IGPUShaderModule = interface end
type [<AllowNullLiteral>] IGPUPipelineLayout = interface end
type [<AllowNullLiteral>] IGPUBindGroupLayout = interface end
type [<AllowNullLiteral>] IGPUBindGroup = interface end
type [<AllowNullLiteral>] IGPUCommandBuffer = interface end

type [<AllowNullLiteral>] IGPUBuffer =
    abstract mapAsync: mode: int -> JS.Promise<unit>
    abstract getMappedRange: unit -> JS.ArrayBuffer
    abstract unmap: unit -> unit

type [<AllowNullLiteral>] IGPUComputePipeline =
    abstract getBindGroupLayout: index: int -> IGPUBindGroupLayout

type [<AllowNullLiteral>] IGPUComputePassEncoder =
    abstract setPipeline: pipeline: IGPUComputePipeline -> unit
    abstract setBindGroup: index: int * bindGroup: IGPUBindGroup -> unit
    abstract dispatchWorkgroups: x: int * y: int * z: int -> unit

    [<Emit("$0.end()")>]
    abstract endPass: unit -> unit

type [<AllowNullLiteral>] IGPUCommandEncoder =
    abstract beginComputePass: unit -> IGPUComputePassEncoder

    abstract copyBufferToBuffer:
        src: IGPUBuffer * srcOffset: int * dst: IGPUBuffer * dstOffset: int * size: int -> unit

    abstract finish: unit -> IGPUCommandBuffer

type [<AllowNullLiteral>] IGPUQueue =
    abstract writeBuffer: buffer: IGPUBuffer * offset: int * data: float32[] -> unit
    abstract writeBuffer: buffer: IGPUBuffer * offset: int * data: uint32[] -> unit
    abstract submit: commandBuffers: IGPUCommandBuffer[] -> unit

type GPUBufferDescriptor = { size: int; usage: int }
type GPUShaderModuleDescriptor = { code: string }
type GPUProgrammableStage = { ``module``: IGPUShaderModule; entryPoint: string }
type GPUBufferBindingLayout = { ``type``: string }
type GPUBindGroupLayoutEntry = { binding: int; visibility: int; buffer: GPUBufferBindingLayout }
type GPUBindGroupLayoutDescriptor = { entries: GPUBindGroupLayoutEntry[] }
type GPUPipelineLayoutDescriptor = { bindGroupLayouts: IGPUBindGroupLayout[] }

type GPUComputePipelineDescriptor =
    { layout: U2<string, IGPUPipelineLayout>
      compute: GPUProgrammableStage }

type GPUBufferBinding = { buffer: IGPUBuffer }
type GPUBindGroupEntry = { binding: int; resource: GPUBufferBinding }

type GPUBindGroupDescriptor =
    { layout: IGPUBindGroupLayout
      entries: GPUBindGroupEntry[] }

type [<AllowNullLiteral>] IGPUDevice =
    abstract queue: IGPUQueue
    abstract createBuffer: descriptor: GPUBufferDescriptor -> IGPUBuffer
    abstract createShaderModule: descriptor: GPUShaderModuleDescriptor -> IGPUShaderModule
    abstract createBindGroupLayout: descriptor: GPUBindGroupLayoutDescriptor -> IGPUBindGroupLayout
    abstract createPipelineLayout: descriptor: GPUPipelineLayoutDescriptor -> IGPUPipelineLayout
    abstract createComputePipeline: descriptor: GPUComputePipelineDescriptor -> IGPUComputePipeline
    abstract createBindGroup: descriptor: GPUBindGroupDescriptor -> IGPUBindGroup
    abstract createCommandEncoder: unit -> IGPUCommandEncoder
    abstract destroy: unit -> unit

type [<AllowNullLiteral>] IGPUAdapter =
    abstract requestDevice: unit -> JS.Promise<IGPUDevice>

type [<AllowNullLiteral>] IGPU =
    abstract requestAdapter: unit -> JS.Promise<IGPUAdapter>

module WebGpu =

    [<Emit("navigator.gpu ?? null")>]
    let private gpuRaw: IGPU = jsNative

    let gpu () : IGPU option =
        if isNull gpuRaw then None else Some gpuRaw

    [<Emit("new Float32Array($0).slice()")>]
    let f32ArrayOf (buffer: JS.ArrayBuffer) : float32[] = jsNative
