namespace Server

open Fable.Core
open Fable.Core.JsInterop

type GpuEvaluation =
    { Values: float32[]
      Jac: float32[] }

type IGpuSolver =
    abstract Graph: Graph
    abstract Evaluate: paramsBatched: float32[] * batch: int -> JS.Promise<GpuEvaluation>
    abstract Destroy: unit -> unit

module GpuSolver =

    let private wgsl = """
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
"""

    let private floatBytes count = max (count * 4) 4
    let private uintBytes count = max (count * 4) 4

    let createGpuSolver (graph: Graph) (maxBatch: int) : JS.Promise<IGpuSolver> =
        promise {
            let! adapter =
                match WebGpu.gpu () with
                | Some gpu -> gpu.requestAdapter ()
                | None -> failwith "WebGPU not available"

            if isNull adapter then
                failwith "no GPU adapter"

            let! device = adapter.requestDevice ()
            let packed = GpuGraph.packGraph graph
            let nNodes = graph.Nodes.Length
            let nParams = graph.Params.Length
            let nVars = graph.VarSlots.Length
            let nRes = graph.Outputs.Length

            let shapeBuf =
                device.createBuffer
                    { size = 16
                      usage = GPUBufferUsage.Uniform ||| GPUBufferUsage.CopyDst }

            device.queue.writeBuffer(shapeBuf, 0, [| uint32 nNodes; uint32 nParams; uint32 nVars; uint32 nRes |])

            let nodesBuf =
                device.createBuffer
                    { size = uintBytes packed.PackedNodes.Length
                      usage = GPUBufferUsage.Storage ||| GPUBufferUsage.CopyDst }

            if packed.PackedNodes.Length > 0 then
                device.queue.writeBuffer(nodesBuf, 0, packed.PackedNodes)

            let constsBuf =
                device.createBuffer
                    { size = floatBytes packed.Consts.Length
                      usage = GPUBufferUsage.Storage ||| GPUBufferUsage.CopyDst }

            if packed.Consts.Length > 0 then
                device.queue.writeBuffer(constsBuf, 0, packed.Consts)

            let outputsData = graph.Outputs |> Array.map uint32
            let outputsBuf =
                device.createBuffer
                    { size = uintBytes outputsData.Length
                      usage = GPUBufferUsage.Storage ||| GPUBufferUsage.CopyDst }

            if outputsData.Length > 0 then
                device.queue.writeBuffer(outputsBuf, 0, outputsData)

            let varSlotNodeBuf =
                device.createBuffer
                    { size = uintBytes packed.VarSlotNodes.Length
                      usage = GPUBufferUsage.Storage ||| GPUBufferUsage.CopyDst }

            if packed.VarSlotNodes.Length > 0 then
                device.queue.writeBuffer(varSlotNodeBuf, 0, packed.VarSlotNodes)

            let paramsSize = floatBytes (maxBatch * nParams)
            let valuesSize = floatBytes (maxBatch * nNodes)
            let adjSize = floatBytes (maxBatch * nRes * nNodes)
            let jacSize = floatBytes (maxBatch * nRes * nVars)

            let paramsBuf = device.createBuffer { size = paramsSize; usage = GPUBufferUsage.Storage ||| GPUBufferUsage.CopyDst }
            let valuesBuf = device.createBuffer { size = valuesSize; usage = GPUBufferUsage.Storage ||| GPUBufferUsage.CopySrc }
            let adjBuf = device.createBuffer { size = adjSize; usage = GPUBufferUsage.Storage }
            let jacBuf = device.createBuffer { size = jacSize; usage = GPUBufferUsage.Storage ||| GPUBufferUsage.CopySrc }
            let valuesStaging = device.createBuffer { size = valuesSize; usage = GPUBufferUsage.CopyDst ||| GPUBufferUsage.MapRead }
            let jacStaging = device.createBuffer { size = jacSize; usage = GPUBufferUsage.CopyDst ||| GPUBufferUsage.MapRead }

            let shader = device.createShaderModule { code = wgsl }

            let bindGroupLayout =
                device.createBindGroupLayout
                    { entries =
                        [| { binding = 0; visibility = GPUShaderStage.Compute; buffer = { ``type`` = "uniform" } }
                           { binding = 1; visibility = GPUShaderStage.Compute; buffer = { ``type`` = "read-only-storage" } }
                           { binding = 2; visibility = GPUShaderStage.Compute; buffer = { ``type`` = "read-only-storage" } }
                           { binding = 3; visibility = GPUShaderStage.Compute; buffer = { ``type`` = "read-only-storage" } }
                           { binding = 4; visibility = GPUShaderStage.Compute; buffer = { ``type`` = "storage" } }
                           { binding = 5; visibility = GPUShaderStage.Compute; buffer = { ``type`` = "read-only-storage" } }
                           { binding = 6; visibility = GPUShaderStage.Compute; buffer = { ``type`` = "read-only-storage" } }
                           { binding = 7; visibility = GPUShaderStage.Compute; buffer = { ``type`` = "storage" } }
                           { binding = 8; visibility = GPUShaderStage.Compute; buffer = { ``type`` = "storage" } } |] }

            let pipelineLayout = device.createPipelineLayout { bindGroupLayouts = [| bindGroupLayout |] }
            let forwardPipeline = device.createComputePipeline { layout = U2.Case2 pipelineLayout; compute = { ``module`` = shader; entryPoint = "forward" } }
            let reversePipeline = device.createComputePipeline { layout = U2.Case2 pipelineLayout; compute = { ``module`` = shader; entryPoint = "reverse" } }

            let bindGroup =
                device.createBindGroup
                    { layout = bindGroupLayout
                      entries =
                        [| { binding = 0; resource = { buffer = shapeBuf } }
                           { binding = 1; resource = { buffer = nodesBuf } }
                           { binding = 2; resource = { buffer = constsBuf } }
                           { binding = 3; resource = { buffer = paramsBuf } }
                           { binding = 4; resource = { buffer = valuesBuf } }
                           { binding = 5; resource = { buffer = outputsBuf } }
                           { binding = 6; resource = { buffer = varSlotNodeBuf } }
                           { binding = 7; resource = { buffer = adjBuf } }
                           { binding = 8; resource = { buffer = jacBuf } } |] }

            let solver =
                { new IGpuSolver with
                    member _.Graph = graph

                    member _.Evaluate(paramsBatched, batch) =
                        promise {
                            if batch > maxBatch then
                                failwithf "Batch %d exceeds solver capacity %d" batch maxBatch

                            device.queue.writeBuffer(paramsBuf, 0, paramsBatched)
                            let encoder = device.createCommandEncoder ()

                            let forwardPass = encoder.beginComputePass ()
                            forwardPass.setPipeline forwardPipeline
                            forwardPass.setBindGroup(0, bindGroup)
                            forwardPass.dispatchWorkgroups(batch, 1, 1)
                            forwardPass.endPass ()

                            let reversePass = encoder.beginComputePass ()
                            reversePass.setPipeline reversePipeline
                            reversePass.setBindGroup(0, bindGroup)
                            reversePass.dispatchWorkgroups(batch, max nRes 1, 1)
                            reversePass.endPass ()

                            let valueCount = batch * nNodes
                            let jacCount = batch * nRes * nVars
                            let valueBytes = valueCount * 4
                            let jacBytes = jacCount * 4

                            if valueBytes > 0 then
                                encoder.copyBufferToBuffer(valuesBuf, 0, valuesStaging, 0, valueBytes)

                            if jacBytes > 0 then
                                encoder.copyBufferToBuffer(jacBuf, 0, jacStaging, 0, jacBytes)

                            device.queue.submit [| encoder.finish () |]

                            let! values =
                                promise {
                                    if valueBytes <= 0 then
                                        return [||]
                                    else
                                        let! _ = valuesStaging.mapAsync GPUMapMode.Read
                                        let mapped = WebGpu.f32ArrayOf (valuesStaging.getMappedRange ())
                                        valuesStaging.unmap ()
                                        return mapped |> Array.take valueCount
                                }

                            let! jac =
                                promise {
                                    if jacBytes <= 0 then
                                        return [||]
                                    else
                                        let! _ = jacStaging.mapAsync GPUMapMode.Read
                                        let mapped = WebGpu.f32ArrayOf (jacStaging.getMappedRange ())
                                        jacStaging.unmap ()
                                        return mapped |> Array.take jacCount
                                }

                            return { Values = values; Jac = jac }
                        }

                    member _.Destroy() =
                        try
                            valuesStaging.unmap ()
                        with _ ->
                            ()

                        try
                            jacStaging.unmap ()
                        with _ ->
                            ()

                        device.destroy () }

            return solver
        }
