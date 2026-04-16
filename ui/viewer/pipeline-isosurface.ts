export function createIsosurfacePipeline(
  device: GPUDevice,
  format: GPUTextureFormat,
  shaderSource: string,
  cameraBindGroupLayout: GPUBindGroupLayout,
  slotBindGroupLayout: GPUBindGroupLayout,
  surfaceBindGroupLayout: GPUBindGroupLayout,
): GPURenderPipeline {
  const module = device.createShaderModule({ label: "field_isosurface_shader", code: shaderSource });
  return device.createRenderPipeline({
    label: "field_isosurface_pipeline",
    layout: device.createPipelineLayout({
      bindGroupLayouts: [cameraBindGroupLayout, slotBindGroupLayout, surfaceBindGroupLayout],
    }),
    vertex: {
      module,
      entryPoint: "vs_main",
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
