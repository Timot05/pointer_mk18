import { Record } from "../../ui/fable_modules/fable-library-js.4.29.0/Types.js";
import { array_type, class_type, string_type, record_type, int32_type } from "../../ui/fable_modules/fable-library-js.4.29.0/Reflection.js";

export const GPUBufferUsage_MapRead = 1;

export const GPUBufferUsage_CopySrc = 4;

export const GPUBufferUsage_CopyDst = 8;

export const GPUBufferUsage_Uniform = 64;

export const GPUBufferUsage_Storage = 128;

export const GPUMapMode_Read = 1;

export const GPUMapMode_Write = 2;

export const GPUShaderStage_Compute = 4;

export class GPUBufferDescriptor extends Record {
    constructor(size, usage) {
        super();
        this.size = (size | 0);
        this.usage = (usage | 0);
    }
}

export function GPUBufferDescriptor_$reflection() {
    return record_type("Server.GPUBufferDescriptor", [], GPUBufferDescriptor, () => [["size", int32_type], ["usage", int32_type]]);
}

export class GPUShaderModuleDescriptor extends Record {
    constructor(code) {
        super();
        this.code = code;
    }
}

export function GPUShaderModuleDescriptor_$reflection() {
    return record_type("Server.GPUShaderModuleDescriptor", [], GPUShaderModuleDescriptor, () => [["code", string_type]]);
}

export class GPUProgrammableStage extends Record {
    constructor(module, entryPoint) {
        super();
        this.module = module;
        this.entryPoint = entryPoint;
    }
}

export function GPUProgrammableStage_$reflection() {
    return record_type("Server.GPUProgrammableStage", [], GPUProgrammableStage, () => [["module", class_type("Server.IGPUShaderModule")], ["entryPoint", string_type]]);
}

export class GPUBufferBindingLayout extends Record {
    constructor(type) {
        super();
        this.type = type;
    }
}

export function GPUBufferBindingLayout_$reflection() {
    return record_type("Server.GPUBufferBindingLayout", [], GPUBufferBindingLayout, () => [["type", string_type]]);
}

export class GPUBindGroupLayoutEntry extends Record {
    constructor(binding, visibility, buffer) {
        super();
        this.binding = (binding | 0);
        this.visibility = (visibility | 0);
        this.buffer = buffer;
    }
}

export function GPUBindGroupLayoutEntry_$reflection() {
    return record_type("Server.GPUBindGroupLayoutEntry", [], GPUBindGroupLayoutEntry, () => [["binding", int32_type], ["visibility", int32_type], ["buffer", GPUBufferBindingLayout_$reflection()]]);
}

export class GPUBindGroupLayoutDescriptor extends Record {
    constructor(entries) {
        super();
        this.entries = entries;
    }
}

export function GPUBindGroupLayoutDescriptor_$reflection() {
    return record_type("Server.GPUBindGroupLayoutDescriptor", [], GPUBindGroupLayoutDescriptor, () => [["entries", array_type(GPUBindGroupLayoutEntry_$reflection())]]);
}

export class GPUPipelineLayoutDescriptor extends Record {
    constructor(bindGroupLayouts) {
        super();
        this.bindGroupLayouts = bindGroupLayouts;
    }
}

export function GPUPipelineLayoutDescriptor_$reflection() {
    return record_type("Server.GPUPipelineLayoutDescriptor", [], GPUPipelineLayoutDescriptor, () => [["bindGroupLayouts", array_type(class_type("Server.IGPUBindGroupLayout"))]]);
}

export class GPUComputePipelineDescriptor extends Record {
    constructor(layout, compute) {
        super();
        this.layout = layout;
        this.compute = compute;
    }
}

export function GPUComputePipelineDescriptor_$reflection() {
    return record_type("Server.GPUComputePipelineDescriptor", [], GPUComputePipelineDescriptor, () => [["layout", class_type("Fable.Core.U2`2", [string_type, class_type("Server.IGPUPipelineLayout")])], ["compute", GPUProgrammableStage_$reflection()]]);
}

export class GPUBufferBinding extends Record {
    constructor(buffer) {
        super();
        this.buffer = buffer;
    }
}

export function GPUBufferBinding_$reflection() {
    return record_type("Server.GPUBufferBinding", [], GPUBufferBinding, () => [["buffer", class_type("Server.IGPUBuffer")]]);
}

export class GPUBindGroupEntry extends Record {
    constructor(binding, resource) {
        super();
        this.binding = (binding | 0);
        this.resource = resource;
    }
}

export function GPUBindGroupEntry_$reflection() {
    return record_type("Server.GPUBindGroupEntry", [], GPUBindGroupEntry, () => [["binding", int32_type], ["resource", GPUBufferBinding_$reflection()]]);
}

export class GPUBindGroupDescriptor extends Record {
    constructor(layout, entries) {
        super();
        this.layout = layout;
        this.entries = entries;
    }
}

export function GPUBindGroupDescriptor_$reflection() {
    return record_type("Server.GPUBindGroupDescriptor", [], GPUBindGroupDescriptor, () => [["layout", class_type("Server.IGPUBindGroupLayout")], ["entries", array_type(GPUBindGroupEntry_$reflection())]]);
}

export function WebGpu_gpu() {
    if ((navigator.gpu ?? null) == null) {
        return undefined;
    }
    else {
        return navigator.gpu ?? null;
    }
}

