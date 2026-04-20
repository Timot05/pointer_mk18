import { Record } from "../ui/fable_modules/fable-library-js.4.29.0/Types.js";
import { obj_type, bool_type, array_type, class_type, string_type, record_type, int32_type } from "../ui/fable_modules/fable-library-js.4.29.0/Reflection.js";

export const GPUBufferUsage_MapRead = 1;

export const GPUBufferUsage_MapWrite = 2;

export const GPUBufferUsage_CopySrc = 4;

export const GPUBufferUsage_CopyDst = 8;

export const GPUBufferUsage_Index = 16;

export const GPUBufferUsage_Vertex = 32;

export const GPUBufferUsage_Uniform = 64;

export const GPUBufferUsage_Storage = 128;

export const GPUBufferUsage_Indirect = 256;

export const GPUBufferUsage_QueryResolve = 512;

export const GPUShaderStage_Vertex = 1;

export const GPUShaderStage_Fragment = 2;

export const GPUShaderStage_Compute = 4;

export const GPUTextureUsage_CopySrc = 1;

export const GPUTextureUsage_CopyDst = 2;

export const GPUTextureUsage_TextureBinding = 4;

export const GPUTextureUsage_StorageBinding = 8;

export const GPUTextureUsage_RenderAttachment = 16;

export const GPUMapMode_Read = 1;

export const GPUMapMode_Write = 2;

export class GPUBufferDescriptor extends Record {
    constructor(size, usage) {
        super();
        this.size = (size | 0);
        this.usage = (usage | 0);
    }
}

export function GPUBufferDescriptor_$reflection() {
    return record_type("WebGPU.GPUBufferDescriptor", [], GPUBufferDescriptor, () => [["size", int32_type], ["usage", int32_type]]);
}

export class GPUShaderModuleDescriptor extends Record {
    constructor(code) {
        super();
        this.code = code;
    }
}

export function GPUShaderModuleDescriptor_$reflection() {
    return record_type("WebGPU.GPUShaderModuleDescriptor", [], GPUShaderModuleDescriptor, () => [["code", string_type]]);
}

export class GPUProgrammableStage extends Record {
    constructor(module, entryPoint) {
        super();
        this.module = module;
        this.entryPoint = entryPoint;
    }
}

export function GPUProgrammableStage_$reflection() {
    return record_type("WebGPU.GPUProgrammableStage", [], GPUProgrammableStage, () => [["module", class_type("WebGPU.IGPUShaderModule")], ["entryPoint", string_type]]);
}

export class GPUVertexAttribute extends Record {
    constructor(shaderLocation, offset, format) {
        super();
        this.shaderLocation = (shaderLocation | 0);
        this.offset = (offset | 0);
        this.format = format;
    }
}

export function GPUVertexAttribute_$reflection() {
    return record_type("WebGPU.GPUVertexAttribute", [], GPUVertexAttribute, () => [["shaderLocation", int32_type], ["offset", int32_type], ["format", string_type]]);
}

export class GPUVertexBufferLayout extends Record {
    constructor(arrayStride, stepMode, attributes) {
        super();
        this.arrayStride = (arrayStride | 0);
        this.stepMode = stepMode;
        this.attributes = attributes;
    }
}

export function GPUVertexBufferLayout_$reflection() {
    return record_type("WebGPU.GPUVertexBufferLayout", [], GPUVertexBufferLayout, () => [["arrayStride", int32_type], ["stepMode", string_type], ["attributes", array_type(GPUVertexAttribute_$reflection())]]);
}

export class GPUPrimitiveState extends Record {
    constructor(topology) {
        super();
        this.topology = topology;
    }
}

export function GPUPrimitiveState_$reflection() {
    return record_type("WebGPU.GPUPrimitiveState", [], GPUPrimitiveState, () => [["topology", string_type]]);
}

export class GPUDepthStencilState extends Record {
    constructor(format, depthWriteEnabled, depthCompare) {
        super();
        this.format = format;
        this.depthWriteEnabled = depthWriteEnabled;
        this.depthCompare = depthCompare;
    }
}

export function GPUDepthStencilState_$reflection() {
    return record_type("WebGPU.GPUDepthStencilState", [], GPUDepthStencilState, () => [["format", string_type], ["depthWriteEnabled", bool_type], ["depthCompare", string_type]]);
}

export class GPUBufferBindingLayout extends Record {
    constructor(type) {
        super();
        this.type = type;
    }
}

export function GPUBufferBindingLayout_$reflection() {
    return record_type("WebGPU.GPUBufferBindingLayout", [], GPUBufferBindingLayout, () => [["type", string_type]]);
}

export class GPUBufferBindGroupLayoutEntry extends Record {
    constructor(binding, visibility, buffer) {
        super();
        this.binding = (binding | 0);
        this.visibility = (visibility | 0);
        this.buffer = buffer;
    }
}

export function GPUBufferBindGroupLayoutEntry_$reflection() {
    return record_type("WebGPU.GPUBufferBindGroupLayoutEntry", [], GPUBufferBindGroupLayoutEntry, () => [["binding", int32_type], ["visibility", int32_type], ["buffer", GPUBufferBindingLayout_$reflection()]]);
}

export class GPUTextureBindingLayout extends Record {
    constructor(sampleType, viewDimension) {
        super();
        this.sampleType = sampleType;
        this.viewDimension = viewDimension;
    }
}

export function GPUTextureBindingLayout_$reflection() {
    return record_type("WebGPU.GPUTextureBindingLayout", [], GPUTextureBindingLayout, () => [["sampleType", string_type], ["viewDimension", string_type]]);
}

export class GPUTextureBindGroupLayoutEntry extends Record {
    constructor(binding, visibility, texture) {
        super();
        this.binding = (binding | 0);
        this.visibility = (visibility | 0);
        this.texture = texture;
    }
}

export function GPUTextureBindGroupLayoutEntry_$reflection() {
    return record_type("WebGPU.GPUTextureBindGroupLayoutEntry", [], GPUTextureBindGroupLayoutEntry, () => [["binding", int32_type], ["visibility", int32_type], ["texture", GPUTextureBindingLayout_$reflection()]]);
}

export class GPUSamplerBindingLayout extends Record {
    constructor(type) {
        super();
        this.type = type;
    }
}

export function GPUSamplerBindingLayout_$reflection() {
    return record_type("WebGPU.GPUSamplerBindingLayout", [], GPUSamplerBindingLayout, () => [["type", string_type]]);
}

export class GPUSamplerBindGroupLayoutEntry extends Record {
    constructor(binding, visibility, sampler) {
        super();
        this.binding = (binding | 0);
        this.visibility = (visibility | 0);
        this.sampler = sampler;
    }
}

export function GPUSamplerBindGroupLayoutEntry_$reflection() {
    return record_type("WebGPU.GPUSamplerBindGroupLayoutEntry", [], GPUSamplerBindGroupLayoutEntry, () => [["binding", int32_type], ["visibility", int32_type], ["sampler", GPUSamplerBindingLayout_$reflection()]]);
}

export class GPUBindGroupLayoutDescriptor extends Record {
    constructor(entries) {
        super();
        this.entries = entries;
    }
}

export function GPUBindGroupLayoutDescriptor_$reflection() {
    return record_type("WebGPU.GPUBindGroupLayoutDescriptor", [], GPUBindGroupLayoutDescriptor, () => [["entries", array_type(obj_type)]]);
}

export class GPUPipelineLayoutDescriptor extends Record {
    constructor(bindGroupLayouts) {
        super();
        this.bindGroupLayouts = bindGroupLayouts;
    }
}

export function GPUPipelineLayoutDescriptor_$reflection() {
    return record_type("WebGPU.GPUPipelineLayoutDescriptor", [], GPUPipelineLayoutDescriptor, () => [["bindGroupLayouts", array_type(class_type("WebGPU.IGPUBindGroupLayout"))]]);
}

export class GPUBufferBinding extends Record {
    constructor(buffer) {
        super();
        this.buffer = buffer;
    }
}

export function GPUBufferBinding_$reflection() {
    return record_type("WebGPU.GPUBufferBinding", [], GPUBufferBinding, () => [["buffer", class_type("WebGPU.IGPUBuffer")]]);
}

export class GPUBindGroupEntry extends Record {
    constructor(binding, resource) {
        super();
        this.binding = (binding | 0);
        this.resource = resource;
    }
}

export function GPUBindGroupEntry_$reflection() {
    return record_type("WebGPU.GPUBindGroupEntry", [], GPUBindGroupEntry, () => [["binding", int32_type], ["resource", obj_type]]);
}

export class GPUBindGroupDescriptor extends Record {
    constructor(layout, entries) {
        super();
        this.layout = layout;
        this.entries = entries;
    }
}

export function GPUBindGroupDescriptor_$reflection() {
    return record_type("WebGPU.GPUBindGroupDescriptor", [], GPUBindGroupDescriptor, () => [["layout", class_type("WebGPU.IGPUBindGroupLayout")], ["entries", array_type(GPUBindGroupEntry_$reflection())]]);
}

export class GPUExtent3D extends Record {
    constructor(width, height, depthOrArrayLayers) {
        super();
        this.width = (width | 0);
        this.height = (height | 0);
        this.depthOrArrayLayers = (depthOrArrayLayers | 0);
    }
}

export function GPUExtent3D_$reflection() {
    return record_type("WebGPU.GPUExtent3D", [], GPUExtent3D, () => [["width", int32_type], ["height", int32_type], ["depthOrArrayLayers", int32_type]]);
}

export class GPUTextureDescriptor extends Record {
    constructor(size, format, usage) {
        super();
        this.size = size;
        this.format = format;
        this.usage = (usage | 0);
    }
}

export function GPUTextureDescriptor_$reflection() {
    return record_type("WebGPU.GPUTextureDescriptor", [], GPUTextureDescriptor, () => [["size", GPUExtent3D_$reflection()], ["format", string_type], ["usage", int32_type]]);
}

export class GPUCanvasConfiguration extends Record {
    constructor(device, format, alphaMode) {
        super();
        this.device = device;
        this.format = format;
        this.alphaMode = alphaMode;
    }
}

export function GPUCanvasConfiguration_$reflection() {
    return record_type("WebGPU.GPUCanvasConfiguration", [], GPUCanvasConfiguration, () => [["device", obj_type], ["format", string_type], ["alphaMode", string_type]]);
}

export function gpu() {
    if ((navigator.gpu ?? null) == null) {
        return undefined;
    }
    else {
        return navigator.gpu ?? null;
    }
}

