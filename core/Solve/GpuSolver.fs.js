import { Record } from "../../ui/fable_modules/fable-library-js.4.29.0/Types.js";
import { record_type, array_type, float32_type } from "../../ui/fable_modules/fable-library-js.4.29.0/Reflection.js";
import { max } from "../../ui/fable_modules/fable-library-js.4.29.0/Double.js";
import { PromiseBuilder__Delay_62FBFDE1, PromiseBuilder__Run_212F1D4B } from "../../ui/fable_modules/Fable.Promise.3.2.0/Promise.fs.js";
import { promise } from "../../ui/fable_modules/Fable.Promise.3.2.0/PromiseImpl.fs.js";
import { GPUMapMode_Read, GPUBindGroupDescriptor, GPUBindGroupEntry, GPUBufferBinding, GPUComputePipelineDescriptor, GPUProgrammableStage, GPUPipelineLayoutDescriptor, GPUBindGroupLayoutDescriptor, GPUBindGroupLayoutEntry, GPUBufferBindingLayout, GPUShaderStage_Compute, GPUShaderModuleDescriptor, GPUBufferUsage_MapRead, GPUBufferUsage_CopySrc, GPUBufferUsage_Storage, GPUBufferDescriptor, GPUBufferUsage_CopyDst, GPUBufferUsage_Uniform, WebGpu_gpu } from "./WebGpu.fs.js";
import { GpuGraph_packGraph } from "./GpuGraph.fs.js";
import { take, map } from "../../ui/fable_modules/fable-library-js.4.29.0/Array.js";
import { printf, toFail } from "../../ui/fable_modules/fable-library-js.4.29.0/String.js";

export class GpuEvaluation extends Record {
    constructor(Values, Jac) {
        super();
        this.Values = Values;
        this.Jac = Jac;
    }
}

export function GpuEvaluation_$reflection() {
    return record_type("Server.GpuEvaluation", [], GpuEvaluation, () => [["Values", array_type(float32_type)], ["Jac", array_type(float32_type)]]);
}

const GpuSolver_wgsl = "\nstruct Meta {\n  nNodes: u32,\n  nParams: u32,\n  nVars: u32,\n  nRes: u32,\n};\n\n@group(0) @binding(0) var<uniform> shape: Meta;\n@group(0) @binding(1) var<storage, read> nodes: array<u32>;\n@group(0) @binding(2) var<storage, read> consts: array<f32>;\n@group(0) @binding(3) var<storage, read> paramsBuf: array<f32>;\n@group(0) @binding(4) var<storage, read_write> values: array<f32>;\n@group(0) @binding(5) var<storage, read> outputs: array<u32>;\n@group(0) @binding(6) var<storage, read> varSlotNode: array<u32>;\n@group(0) @binding(7) var<storage, read_write> adj: array<f32>;\n@group(0) @binding(8) var<storage, read_write> jac: array<f32>;\n\n@compute @workgroup_size(1)\nfn forward(@builtin(global_invocation_id) gid: vec3u) {\n  let b = gid.x;\n  let vBase = b * shape.nNodes;\n  let pBase = b * shape.nParams;\n  for (var i: u32 = 0u; i < shape.nNodes; i = i + 1u) {\n    let o = nodes[i * 4u + 0u];\n    let a = nodes[i * 4u + 1u];\n    let c = nodes[i * 4u + 2u];\n    let aux = nodes[i * 4u + 3u];\n    var v: f32 = 0.0;\n    switch (o) {\n      case 0u: { v = consts[aux]; }\n      case 1u: { v = paramsBuf[pBase + aux]; }\n      case 2u: { v = -values[vBase + a]; }\n      case 3u: { v = sin(values[vBase + a]); }\n      case 4u: { v = cos(values[vBase + a]); }\n      case 5u: { v = sqrt(values[vBase + a]); }\n      case 6u: { v = values[vBase + a] + values[vBase + c]; }\n      case 7u: { v = values[vBase + a] - values[vBase + c]; }\n      case 8u: { v = values[vBase + a] * values[vBase + c]; }\n      case 9u: { v = values[vBase + a] / values[vBase + c]; }\n      case 10u: { v = atan2(values[vBase + a], values[vBase + c]); }\n      default: { v = 0.0; }\n    }\n    values[vBase + i] = v;\n  }\n}\n\n@compute @workgroup_size(1)\nfn reverse(@builtin(global_invocation_id) gid: vec3u) {\n  let b = gid.x;\n  let r = gid.y;\n  let vBase = b * shape.nNodes;\n  let aBase = (b * shape.nRes + r) * shape.nNodes;\n  let jBase = (b * shape.nRes + r) * shape.nVars;\n\n  for (var i: u32 = 0u; i < shape.nNodes; i = i + 1u) {\n    adj[aBase + i] = 0.0;\n  }\n  adj[aBase + outputs[r]] = 1.0;\n\n  for (var ii: u32 = 0u; ii < shape.nNodes; ii = ii + 1u) {\n    let i = shape.nNodes - 1u - ii;\n    let ai = adj[aBase + i];\n    if (ai == 0.0) { continue; }\n    let o = nodes[i * 4u + 0u];\n    let a = nodes[i * 4u + 1u];\n    let c = nodes[i * 4u + 2u];\n    switch (o) {\n      case 0u, 1u: {}\n      case 2u: { adj[aBase + a] = adj[aBase + a] - ai; }\n      case 3u: { adj[aBase + a] = adj[aBase + a] + ai * cos(values[vBase + a]); }\n      case 4u: { adj[aBase + a] = adj[aBase + a] - ai * sin(values[vBase + a]); }\n      case 5u: {\n        let v = values[vBase + a];\n        if (v > 0.0) { adj[aBase + a] = adj[aBase + a] + ai / (2.0 * sqrt(v)); }\n      }\n      case 6u: {\n        adj[aBase + a] = adj[aBase + a] + ai;\n        adj[aBase + c] = adj[aBase + c] + ai;\n      }\n      case 7u: {\n        adj[aBase + a] = adj[aBase + a] + ai;\n        adj[aBase + c] = adj[aBase + c] - ai;\n      }\n      case 8u: {\n        let av = values[vBase + a];\n        let bv = values[vBase + c];\n        adj[aBase + a] = adj[aBase + a] + ai * bv;\n        adj[aBase + c] = adj[aBase + c] + ai * av;\n      }\n      case 9u: {\n        let av = values[vBase + a];\n        let bv = values[vBase + c];\n        adj[aBase + a] = adj[aBase + a] + ai / bv;\n        adj[aBase + c] = adj[aBase + c] - ai * av / (bv * bv);\n      }\n      case 10u: {\n        let yv = values[vBase + a];\n        let xv = values[vBase + c];\n        let d = xv * xv + yv * yv;\n        if (d > 0.0) {\n          adj[aBase + a] = adj[aBase + a] + ai * xv / d;\n          adj[aBase + c] = adj[aBase + c] - ai * yv / d;\n        }\n      }\n      default: {}\n    }\n  }\n\n  for (var k: u32 = 0u; k < shape.nVars; k = k + 1u) {\n    jac[jBase + k] = adj[aBase + varSlotNode[k]];\n  }\n}\n";

function GpuSolver_floatBytes(count) {
    return max(count * 4, 4);
}

function GpuSolver_uintBytes(count) {
    return max(count * 4, 4);
}

export function GpuSolver_createGpuSolver(graph, maxBatch) {
    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => {
        let matchValue, gpu;
        return ((matchValue = WebGpu_gpu(), (matchValue == null) ? (() => {
            throw new Error("WebGPU not available");
        })() : ((gpu = matchValue, gpu.requestAdapter())))).then((_arg) => {
            const adapter = _arg;
            return ((adapter == null) ? (((() => {
                throw new Error("no GPU adapter");
            })(), Promise.resolve())) : (Promise.resolve())).then(() => PromiseBuilder__Delay_62FBFDE1(promise, () => (adapter.requestDevice().then((_arg_1) => {
                const device = _arg_1;
                const packed = GpuGraph_packGraph(graph);
                const nNodes = graph.Nodes.length | 0;
                const nParams = graph.Params.length | 0;
                const nVars = graph.VarSlots.length | 0;
                const nRes = graph.Outputs.length | 0;
                const shapeBuf = device.createBuffer(new GPUBufferDescriptor(16, GPUBufferUsage_Uniform | GPUBufferUsage_CopyDst));
                device.queue.writeBuffer(shapeBuf, 0, new Uint32Array(new Uint32Array([nNodes >>> 0, nParams >>> 0, nVars >>> 0, nRes >>> 0])));
                const nodesBuf = device.createBuffer(new GPUBufferDescriptor(GpuSolver_uintBytes(packed.PackedNodes.length), GPUBufferUsage_Storage | GPUBufferUsage_CopyDst));
                return ((packed.PackedNodes.length > 0) ? ((device.queue.writeBuffer(nodesBuf, 0, new Uint32Array(packed.PackedNodes)), Promise.resolve())) : (Promise.resolve())).then(() => PromiseBuilder__Delay_62FBFDE1(promise, () => {
                    const constsBuf = device.createBuffer(new GPUBufferDescriptor(GpuSolver_floatBytes(packed.Consts.length), GPUBufferUsage_Storage | GPUBufferUsage_CopyDst));
                    return ((packed.Consts.length > 0) ? ((device.queue.writeBuffer(constsBuf, 0, new Float32Array(packed.Consts)), Promise.resolve())) : (Promise.resolve())).then(() => PromiseBuilder__Delay_62FBFDE1(promise, () => {
                        const outputsData = map((value) => (value >>> 0), graph.Outputs, Uint32Array);
                        const outputsBuf = device.createBuffer(new GPUBufferDescriptor(GpuSolver_uintBytes(outputsData.length), GPUBufferUsage_Storage | GPUBufferUsage_CopyDst));
                        return ((outputsData.length > 0) ? ((device.queue.writeBuffer(outputsBuf, 0, new Uint32Array(outputsData)), Promise.resolve())) : (Promise.resolve())).then(() => PromiseBuilder__Delay_62FBFDE1(promise, () => {
                            const varSlotNodeBuf = device.createBuffer(new GPUBufferDescriptor(GpuSolver_uintBytes(packed.VarSlotNodes.length), GPUBufferUsage_Storage | GPUBufferUsage_CopyDst));
                            return ((packed.VarSlotNodes.length > 0) ? ((device.queue.writeBuffer(varSlotNodeBuf, 0, new Uint32Array(packed.VarSlotNodes)), Promise.resolve())) : (Promise.resolve())).then(() => PromiseBuilder__Delay_62FBFDE1(promise, () => {
                                const paramsSize = GpuSolver_floatBytes(maxBatch * nParams) | 0;
                                const valuesSize = GpuSolver_floatBytes(maxBatch * nNodes) | 0;
                                const adjSize = GpuSolver_floatBytes((maxBatch * nRes) * nNodes) | 0;
                                const jacSize = GpuSolver_floatBytes((maxBatch * nRes) * nVars) | 0;
                                const paramsBuf = device.createBuffer(new GPUBufferDescriptor(paramsSize, GPUBufferUsage_Storage | GPUBufferUsage_CopyDst));
                                const valuesBuf = device.createBuffer(new GPUBufferDescriptor(valuesSize, GPUBufferUsage_Storage | GPUBufferUsage_CopySrc));
                                const adjBuf = device.createBuffer(new GPUBufferDescriptor(adjSize, GPUBufferUsage_Storage));
                                const jacBuf = device.createBuffer(new GPUBufferDescriptor(jacSize, GPUBufferUsage_Storage | GPUBufferUsage_CopySrc));
                                const valuesStaging = device.createBuffer(new GPUBufferDescriptor(valuesSize, GPUBufferUsage_CopyDst | GPUBufferUsage_MapRead));
                                const jacStaging = device.createBuffer(new GPUBufferDescriptor(jacSize, GPUBufferUsage_CopyDst | GPUBufferUsage_MapRead));
                                const shader = device.createShaderModule(new GPUShaderModuleDescriptor(GpuSolver_wgsl));
                                const bindGroupLayout = device.createBindGroupLayout(new GPUBindGroupLayoutDescriptor([new GPUBindGroupLayoutEntry(0, GPUShaderStage_Compute, new GPUBufferBindingLayout("uniform")), new GPUBindGroupLayoutEntry(1, GPUShaderStage_Compute, new GPUBufferBindingLayout("read-only-storage")), new GPUBindGroupLayoutEntry(2, GPUShaderStage_Compute, new GPUBufferBindingLayout("read-only-storage")), new GPUBindGroupLayoutEntry(3, GPUShaderStage_Compute, new GPUBufferBindingLayout("read-only-storage")), new GPUBindGroupLayoutEntry(4, GPUShaderStage_Compute, new GPUBufferBindingLayout("storage")), new GPUBindGroupLayoutEntry(5, GPUShaderStage_Compute, new GPUBufferBindingLayout("read-only-storage")), new GPUBindGroupLayoutEntry(6, GPUShaderStage_Compute, new GPUBufferBindingLayout("read-only-storage")), new GPUBindGroupLayoutEntry(7, GPUShaderStage_Compute, new GPUBufferBindingLayout("storage")), new GPUBindGroupLayoutEntry(8, GPUShaderStage_Compute, new GPUBufferBindingLayout("storage"))]));
                                const pipelineLayout = device.createPipelineLayout(new GPUPipelineLayoutDescriptor([bindGroupLayout]));
                                const forwardPipeline = device.createComputePipeline(new GPUComputePipelineDescriptor(pipelineLayout, new GPUProgrammableStage(shader, "forward")));
                                const reversePipeline = device.createComputePipeline(new GPUComputePipelineDescriptor(pipelineLayout, new GPUProgrammableStage(shader, "reverse")));
                                const bindGroup = device.createBindGroup(new GPUBindGroupDescriptor(bindGroupLayout, [new GPUBindGroupEntry(0, new GPUBufferBinding(shapeBuf)), new GPUBindGroupEntry(1, new GPUBufferBinding(nodesBuf)), new GPUBindGroupEntry(2, new GPUBufferBinding(constsBuf)), new GPUBindGroupEntry(3, new GPUBufferBinding(paramsBuf)), new GPUBindGroupEntry(4, new GPUBufferBinding(valuesBuf)), new GPUBindGroupEntry(5, new GPUBufferBinding(outputsBuf)), new GPUBindGroupEntry(6, new GPUBufferBinding(varSlotNodeBuf)), new GPUBindGroupEntry(7, new GPUBufferBinding(adjBuf)), new GPUBindGroupEntry(8, new GPUBufferBinding(jacBuf))]));
                                const solver = {
                                    Graph: graph,
                                    Evaluate(paramsBatched, batch) {
                                        return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => (((batch > maxBatch) ? ((toFail(printf("Batch %d exceeds solver capacity %d"))(batch)(maxBatch), Promise.resolve())) : (Promise.resolve())).then(() => PromiseBuilder__Delay_62FBFDE1(promise, () => {
                                            device.queue.writeBuffer(paramsBuf, 0, new Float32Array(paramsBatched));
                                            const encoder = device.createCommandEncoder();
                                            const forwardPass = encoder.beginComputePass();
                                            forwardPass.setPipeline(forwardPipeline);
                                            forwardPass.setBindGroup(0, bindGroup);
                                            forwardPass.dispatchWorkgroups(batch, 1, 1);
                                            forwardPass.end();
                                            const reversePass = encoder.beginComputePass();
                                            reversePass.setPipeline(reversePipeline);
                                            reversePass.setBindGroup(0, bindGroup);
                                            reversePass.dispatchWorkgroups(batch, max(nRes, 1), 1);
                                            reversePass.end();
                                            const valueCount = (batch * nNodes) | 0;
                                            const jacCount = ((batch * nRes) * nVars) | 0;
                                            const valueBytes = (valueCount * 4) | 0;
                                            const jacBytes = (jacCount * 4) | 0;
                                            return ((valueBytes > 0) ? ((encoder.copyBufferToBuffer(valuesBuf, 0, valuesStaging, 0, valueBytes), Promise.resolve())) : (Promise.resolve())).then(() => PromiseBuilder__Delay_62FBFDE1(promise, () => (((jacBytes > 0) ? ((encoder.copyBufferToBuffer(jacBuf, 0, jacStaging, 0, jacBytes), Promise.resolve())) : (Promise.resolve())).then(() => PromiseBuilder__Delay_62FBFDE1(promise, () => {
                                                device.queue.submit([encoder.finish()]);
                                                return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => ((valueBytes <= 0) ? (Promise.resolve(new Float32Array([]))) : (valuesStaging.mapAsync(GPUMapMode_Read).then(() => {
                                                    const mapped = new Float32Array(valuesStaging.getMappedRange()).slice();
                                                    valuesStaging.unmap();
                                                    return Promise.resolve(take(valueCount, mapped, Float32Array));
                                                }))))).then((_arg_3) => (PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => ((jacBytes <= 0) ? (Promise.resolve(new Float32Array([]))) : (jacStaging.mapAsync(GPUMapMode_Read).then(() => {
                                                    const mapped_1 = new Float32Array(jacStaging.getMappedRange()).slice();
                                                    jacStaging.unmap();
                                                    return Promise.resolve(take(jacCount, mapped_1, Float32Array));
                                                }))))).then((_arg_5) => (Promise.resolve(new GpuEvaluation(_arg_3, _arg_5))))));
                                            })))));
                                        })))));
                                    },
                                    Destroy() {
                                        try {
                                            valuesStaging.unmap();
                                        }
                                        catch (matchValue_1) {
                                        }
                                        try {
                                            jacStaging.unmap();
                                        }
                                        catch (matchValue_2) {
                                        }
                                        device.destroy();
                                    },
                                };
                                return Promise.resolve(solver);
                            }));
                        }));
                    }));
                }));
            }))));
        });
    }));
}

