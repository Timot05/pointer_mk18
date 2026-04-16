import type { Graph } from "./graph";
import { slotToNodeIndex } from "./graph";

const OP_CODE: Record<string, number> = {
  Constant: 0, Param: 1, Neg: 2, Sin: 3, Cos: 4, Sqrt: 5,
  Add: 6, Sub: 7, Mul: 8, Div: 9, Atan2: 10,
};

const WGSL = `
struct Meta {
  nNodes: u32,
  nParams: u32,
  nVars: u32,
  nRes: u32,
};

@group(0) @binding(0) var<uniform> shape: Meta;
@group(0) @binding(1) var<storage, read> nodes: array<u32>;
@group(0) @binding(2) var<storage, read> consts: array<f32>;
@group(0) @binding(3) var<storage, read> paramsBuf: array<f32>;
@group(0) @binding(4) var<storage, read_write> values: array<f32>;
@group(0) @binding(5) var<storage, read> outputs: array<u32>;
@group(0) @binding(6) var<storage, read> varSlotNode: array<u32>;
@group(0) @binding(7) var<storage, read_write> adj: array<f32>;
@group(0) @binding(8) var<storage, read_write> jac: array<f32>;

@compute @workgroup_size(1)
fn forward(@builtin(global_invocation_id) gid: vec3u) {
  let b = gid.x;
  let vBase = b * shape.nNodes;
  let pBase = b * shape.nParams;
  for (var i: u32 = 0u; i < shape.nNodes; i = i + 1u) {
    let o = nodes[i * 4u + 0u];
    let a = nodes[i * 4u + 1u];
    let c = nodes[i * 4u + 2u];
    let aux = nodes[i * 4u + 3u];
    var v: f32 = 0.0;
    switch (o) {
      case 0u: { v = consts[aux]; }
      case 1u: { v = paramsBuf[pBase + aux]; }
      case 2u: { v = -values[vBase + a]; }
      case 3u: { v = sin(values[vBase + a]); }
      case 4u: { v = cos(values[vBase + a]); }
      case 5u: { v = sqrt(values[vBase + a]); }
      case 6u: { v = values[vBase + a] + values[vBase + c]; }
      case 7u: { v = values[vBase + a] - values[vBase + c]; }
      case 8u: { v = values[vBase + a] * values[vBase + c]; }
      case 9u: { v = values[vBase + a] / values[vBase + c]; }
      case 10u: { v = atan2(values[vBase + a], values[vBase + c]); }
      default: { v = 0.0; }
    }
    values[vBase + i] = v;
  }
}

@compute @workgroup_size(1)
fn reverse(@builtin(global_invocation_id) gid: vec3u) {
  let b = gid.x;
  let r = gid.y;
  let vBase = b * shape.nNodes;
  let aBase = (b * shape.nRes + r) * shape.nNodes;
  let jBase = (b * shape.nRes + r) * shape.nVars;

  for (var i: u32 = 0u; i < shape.nNodes; i = i + 1u) {
    adj[aBase + i] = 0.0;
  }
  adj[aBase + outputs[r]] = 1.0;

  for (var ii: u32 = 0u; ii < shape.nNodes; ii = ii + 1u) {
    let i = shape.nNodes - 1u - ii;
    let ai = adj[aBase + i];
    if (ai == 0.0) { continue; }
    let o = nodes[i * 4u + 0u];
    let a = nodes[i * 4u + 1u];
    let c = nodes[i * 4u + 2u];
    switch (o) {
      case 0u, 1u: {}
      case 2u: { adj[aBase + a] = adj[aBase + a] - ai; }
      case 3u: { adj[aBase + a] = adj[aBase + a] + ai * cos(values[vBase + a]); }
      case 4u: { adj[aBase + a] = adj[aBase + a] - ai * sin(values[vBase + a]); }
      case 5u: {
        let v = values[vBase + a];
        if (v > 0.0) { adj[aBase + a] = adj[aBase + a] + ai / (2.0 * sqrt(v)); }
      }
      case 6u: {
        adj[aBase + a] = adj[aBase + a] + ai;
        adj[aBase + c] = adj[aBase + c] + ai;
      }
      case 7u: {
        adj[aBase + a] = adj[aBase + a] + ai;
        adj[aBase + c] = adj[aBase + c] - ai;
      }
      case 8u: {
        let av = values[vBase + a];
        let bv = values[vBase + c];
        adj[aBase + a] = adj[aBase + a] + ai * bv;
        adj[aBase + c] = adj[aBase + c] + ai * av;
      }
      case 9u: {
        let av = values[vBase + a];
        let bv = values[vBase + c];
        adj[aBase + a] = adj[aBase + a] + ai / bv;
        adj[aBase + c] = adj[aBase + c] - ai * av / (bv * bv);
      }
      case 10u: {
        let yv = values[vBase + a];
        let xv = values[vBase + c];
        let d = xv * xv + yv * yv;
        if (d > 0.0) {
          adj[aBase + a] = adj[aBase + a] + ai * xv / d;
          adj[aBase + c] = adj[aBase + c] - ai * yv / d;
        }
      }
      default: {}
    }
  }

  for (var k: u32 = 0u; k < shape.nVars; k = k + 1u) {
    jac[jBase + k] = adj[aBase + varSlotNode[k]];
  }
}
`;

function packGraph(g: Graph) {
  const packed = new Uint32Array(g.nodes.length * 4);
  const consts: number[] = [];
  for (let i = 0; i < g.nodes.length; i++) {
    const n = g.nodes[i];
    const code = OP_CODE[n.op.case];
    const a = n.inputs[0] ?? 0;
    const b = n.inputs[1] ?? 0;
    let aux = 0;
    if (n.op.case === "Constant") {
      aux = consts.length;
      consts.push(n.op.value);
    } else if (n.op.case === "Param") {
      aux = n.op.slot;
    }
    packed[i * 4 + 0] = code;
    packed[i * 4 + 1] = a;
    packed[i * 4 + 2] = b;
    packed[i * 4 + 3] = aux;
  }
  return { packed, consts: new Float32Array(consts) };
}

export interface GpuSolver {
  graph: Graph;
  evaluate(paramsBatched: Float32Array, batch: number): Promise<{ values: Float32Array; jac: Float32Array }>;
  destroy(): void;
}

export async function createGpuSolver(g: Graph, maxBatch = 1): Promise<GpuSolver> {
  if (!navigator.gpu) throw new Error("WebGPU not available");
  const adapter = await navigator.gpu.requestAdapter();
  if (!adapter) throw new Error("no GPU adapter");
  const device = await adapter.requestDevice();

  const { packed, consts } = packGraph(g);
  const nNodes = g.nodes.length;
  const nParams = g.params.length;
  const nVars = g.varSlots.length;
  const nRes = g.outputs.length;
  const slot2node = slotToNodeIndex(g);

  const shapeBuf = device.createBuffer({ size: 16, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST });
  device.queue.writeBuffer(shapeBuf, 0, new Uint32Array([nNodes, nParams, nVars, nRes]));

  const nodesBuf = device.createBuffer({ size: Math.max(packed.byteLength, 4), usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST });
  if (packed.byteLength > 0) device.queue.writeBuffer(nodesBuf, 0, packed);

  const constsBuf = device.createBuffer({ size: Math.max(consts.byteLength, 4), usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST });
  if (consts.byteLength > 0) device.queue.writeBuffer(constsBuf, 0, consts);

  const outputsBuf = device.createBuffer({ size: Math.max(nRes * 4, 4), usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST });
  if (nRes > 0) device.queue.writeBuffer(outputsBuf, 0, new Uint32Array(g.outputs));

  const varSlotNodeData = new Uint32Array(nVars);
  for (let i = 0; i < nVars; i++) varSlotNodeData[i] = slot2node[g.varSlots[i]];
  const varSlotNodeBuf = device.createBuffer({ size: Math.max(nVars * 4, 4), usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST });
  if (nVars > 0) device.queue.writeBuffer(varSlotNodeBuf, 0, varSlotNodeData);

  const paramsSize = Math.max(maxBatch * nParams * 4, 4);
  const valuesSize = Math.max(maxBatch * nNodes * 4, 4);
  const adjSize = Math.max(maxBatch * nRes * nNodes * 4, 4);
  const jacSize = Math.max(maxBatch * nRes * nVars * 4, 4);

  const paramsBuf = device.createBuffer({ size: paramsSize, usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST });
  const valuesBuf = device.createBuffer({ size: valuesSize, usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC });
  const adjBuf = device.createBuffer({ size: adjSize, usage: GPUBufferUsage.STORAGE });
  const jacBuf = device.createBuffer({ size: jacSize, usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC });
  const valuesStaging = device.createBuffer({ size: valuesSize, usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ });
  const jacStaging = device.createBuffer({ size: jacSize, usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ });

  const module = device.createShaderModule({ code: WGSL });
  const bindGroupLayout = device.createBindGroupLayout({
    entries: [
      { binding: 0, visibility: GPUShaderStage.COMPUTE, buffer: { type: "uniform" } },
      { binding: 1, visibility: GPUShaderStage.COMPUTE, buffer: { type: "read-only-storage" } },
      { binding: 2, visibility: GPUShaderStage.COMPUTE, buffer: { type: "read-only-storage" } },
      { binding: 3, visibility: GPUShaderStage.COMPUTE, buffer: { type: "read-only-storage" } },
      { binding: 4, visibility: GPUShaderStage.COMPUTE, buffer: { type: "storage" } },
      { binding: 5, visibility: GPUShaderStage.COMPUTE, buffer: { type: "read-only-storage" } },
      { binding: 6, visibility: GPUShaderStage.COMPUTE, buffer: { type: "read-only-storage" } },
      { binding: 7, visibility: GPUShaderStage.COMPUTE, buffer: { type: "storage" } },
      { binding: 8, visibility: GPUShaderStage.COMPUTE, buffer: { type: "storage" } },
    ],
  });
  const pipelineLayout = device.createPipelineLayout({ bindGroupLayouts: [bindGroupLayout] });
  const fwdPipeline = device.createComputePipeline({ layout: pipelineLayout, compute: { module, entryPoint: "forward" } });
  const revPipeline = device.createComputePipeline({ layout: pipelineLayout, compute: { module, entryPoint: "reverse" } });

  const bindGroup = device.createBindGroup({
    layout: bindGroupLayout,
    entries: [
      { binding: 0, resource: { buffer: shapeBuf } },
      { binding: 1, resource: { buffer: nodesBuf } },
      { binding: 2, resource: { buffer: constsBuf } },
      { binding: 3, resource: { buffer: paramsBuf } },
      { binding: 4, resource: { buffer: valuesBuf } },
      { binding: 5, resource: { buffer: outputsBuf } },
      { binding: 6, resource: { buffer: varSlotNodeBuf } },
      { binding: 7, resource: { buffer: adjBuf } },
      { binding: 8, resource: { buffer: jacBuf } },
    ],
  });

  let pending: Promise<unknown> = Promise.resolve();
  let destroyed = false;

  return {
    graph: g,
    async evaluate(paramsBatched: Float32Array, batch: number) {
      const run = pending.then(async () => {
        if (destroyed) throw new Error("GPU solver destroyed");
        device.queue.writeBuffer(paramsBuf, 0, paramsBatched);
        const enc = device.createCommandEncoder();
        const pass0 = enc.beginComputePass();
        pass0.setPipeline(fwdPipeline);
        pass0.setBindGroup(0, bindGroup);
        pass0.dispatchWorkgroups(batch, 1, 1);
        pass0.end();
        const pass1 = enc.beginComputePass();
        pass1.setPipeline(revPipeline);
        pass1.setBindGroup(0, bindGroup);
        pass1.dispatchWorkgroups(batch, Math.max(nRes, 1), 1);
        pass1.end();
        const vBytes = batch * nNodes * 4;
        const jBytes = batch * nRes * nVars * 4;
        if (vBytes > 0) enc.copyBufferToBuffer(valuesBuf, 0, valuesStaging, 0, vBytes);
        if (jBytes > 0) enc.copyBufferToBuffer(jacBuf, 0, jacStaging, 0, jBytes);
        device.queue.submit([enc.finish()]);

        const values = new Float32Array(batch * nNodes);
        const jac = new Float32Array(batch * nRes * nVars);
        if (vBytes > 0) {
          await valuesStaging.mapAsync(GPUMapMode.READ, 0, vBytes);
          values.set(new Float32Array(valuesStaging.getMappedRange(0, vBytes).slice(0)));
          valuesStaging.unmap();
        }
        if (jBytes > 0) {
          await jacStaging.mapAsync(GPUMapMode.READ, 0, jBytes);
          jac.set(new Float32Array(jacStaging.getMappedRange(0, jBytes).slice(0)));
          jacStaging.unmap();
        }
        return { values, jac };
      });
      pending = run.catch(() => undefined);
      return run;
    },
    destroy() {
      destroyed = true;
      try {
        valuesStaging.unmap();
      } catch {}
      try {
        jacStaging.unmap();
      } catch {}
      device.destroy();
    },
  };
}
