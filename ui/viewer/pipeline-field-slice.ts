export function createFieldSlicePipeline(
  device: GPUDevice,
  format: GPUTextureFormat,
  shaderSource: string,
  cameraBindGroupLayout: GPUBindGroupLayout,
  slotBindGroupLayout: GPUBindGroupLayout,
): GPURenderPipeline {
  const module = device.createShaderModule({ label: "field_slice_shader", code: shaderSource });
  return device.createRenderPipeline({
    label: "field_slice_pipeline",
    layout: device.createPipelineLayout({
      bindGroupLayouts: [cameraBindGroupLayout, slotBindGroupLayout],
    }),
    vertex: {
      module,
      entryPoint: "vs_main",
      buffers: [{
        arrayStride: 7 * 4,
        stepMode: "vertex",
        attributes: [
          { shaderLocation: 0, offset: 0, format: "float32x3" },
          { shaderLocation: 1, offset: 3 * 4, format: "float32x4" },
        ],
      }],
    },
    fragment: {
      module,
      entryPoint: "fs_main",
      targets: [{
        format,
        blend: {
          color: { srcFactor: "src-alpha", dstFactor: "one-minus-src-alpha", operation: "add" },
          alpha: { srcFactor: "one", dstFactor: "one-minus-src-alpha", operation: "add" },
        },
      }],
    },
    primitive: { topology: "triangle-list" },
  });
}
