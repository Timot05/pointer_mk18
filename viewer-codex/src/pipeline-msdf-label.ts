import { HALF_FOV } from "./camera";
import type { MsdfAtlas } from "./msdf-atlas";

const LABEL_WGSL = `
struct Camera {
  eye: vec3<f32>, _p0: f32,
  forward: vec3<f32>, _p1: f32,
  right: vec3<f32>, _p2: f32,
  up: vec3<f32>, aspect: f32,
};

struct LabelUniforms {
  viewport: vec4<f32>,
  frame_pos: vec4<f32>,
  frame_x: vec4<f32>,
  frame_y: vec4<f32>,
};

const ATLAS_PX_RANGE: f32 = 4.0;
const ATLAS_SIZE: f32 = 256.0;
const HALF_FOV: f32 = ${HALF_FOV};

@group(0) @binding(0) var<uniform> cam: Camera;
@group(1) @binding(0) var<uniform> label: LabelUniforms;
@group(1) @binding(1) var atlas: texture_2d<f32>;
@group(1) @binding(2) var atlas_sampler: sampler;

struct VsIn {
  @location(0) anchor_2d: vec2<f32>,
  @location(1) offset_px: vec2<f32>,
  @location(2) uv: vec2<f32>,
  @location(3) color: vec4<f32>,
};

struct VsOut {
  @builtin(position) clip_pos: vec4<f32>,
  @location(0) uv: vec2<f32>,
  @location(1) color: vec4<f32>,
};

fn project_world(pos: vec3<f32>) -> vec4<f32> {
  let f = cam.forward;
  let r = cam.right;
  let u = cam.up;
  let view = mat4x4<f32>(
    vec4<f32>(r.x, u.x, -f.x, 0.0),
    vec4<f32>(r.y, u.y, -f.y, 0.0),
    vec4<f32>(r.z, u.z, -f.z, 0.0),
    vec4<f32>(-dot(r, cam.eye), -dot(u, cam.eye), dot(f, cam.eye), 1.0),
  );
  let near = 0.001;
  let far = 1000.0;
  let t = tan(HALF_FOV);
  let proj = mat4x4<f32>(
    vec4<f32>(1.0 / (cam.aspect * t), 0.0, 0.0, 0.0),
    vec4<f32>(0.0, 1.0 / t, 0.0, 0.0),
    vec4<f32>(0.0, 0.0, -(far + near) / (far - near), -1.0),
    vec4<f32>(0.0, 0.0, -2.0 * far * near / (far - near), 0.0),
  );
  return proj * view * vec4<f32>(pos, 1.0);
}

@vertex
fn vs_label(input: VsIn) -> VsOut {
  let frame_pos = label.frame_pos.xyz;
  let frame_x = label.frame_x.xyz;
  let frame_y = label.frame_y.xyz;

  let anchor_world = frame_pos + input.anchor_2d.x * frame_x + input.anchor_2d.y * frame_y;
  let cam_to_anchor = anchor_world - cam.eye;
  let view_depth = abs(dot(cam_to_anchor, cam.forward));
  let world_per_px = 2.0 * view_depth * tan(HALF_FOV) / label.viewport.y;

  let proj_fx = frame_x - dot(frame_x, cam.forward) * cam.forward;
  let proj_fy = frame_y - dot(frame_y, cam.forward) * cam.forward;
  let x_sign = select(-1.0, 1.0, dot(proj_fx, cam.right) > 0.0);
  let y_sign = select(-1.0, 1.0, dot(proj_fy, cam.up) > 0.0);
  let plane_offset_2d = vec2<f32>(
    input.offset_px.x * x_sign,
    -input.offset_px.y * y_sign
  ) * world_per_px;
  let plane_offset_world = plane_offset_2d.x * frame_x + plane_offset_2d.y * frame_y;

  var out: VsOut;
  out.clip_pos = project_world(anchor_world + plane_offset_world);
  out.uv = input.uv;
  out.color = input.color;
  return out;
}

fn median3(r: f32, g: f32, b: f32) -> f32 {
  return max(min(r, g), min(max(r, g), b));
}

@fragment
fn fs_label(input: VsOut) -> @location(0) vec4<f32> {
  let msd = textureSample(atlas, atlas_sampler, input.uv).rgb;
  let sd = median3(msd.r, msd.g, msd.b);
  let unit_range = ATLAS_PX_RANGE / ATLAS_SIZE;
  let uv_deriv = fwidth(input.uv);
  let screen_px_range = max(0.5 * (unit_range / uv_deriv.x + unit_range / uv_deriv.y), 1.0);
  let screen_px_distance = screen_px_range * (sd - 0.5);
  let alpha = clamp(screen_px_distance + 0.5, 0.0, 1.0);
  return vec4<f32>(input.color.rgb, input.color.a * alpha);
}
`;

const LABEL_UNIFORM_SIZE = 64;

export function createMsdfLabelPipeline(
  device: GPUDevice,
  format: GPUTextureFormat,
  atlas: MsdfAtlas,
  cameraBindGroupLayout: GPUBindGroupLayout,
) {
  const shaderModule = device.createShaderModule({ label: "msdf_label_shader", code: LABEL_WGSL });

  const labelBindGroupLayout = device.createBindGroupLayout({
    label: "msdf_label_bind_group_layout",
    entries: [
      { binding: 0, visibility: GPUShaderStage.VERTEX, buffer: { type: "uniform" } },
      { binding: 1, visibility: GPUShaderStage.FRAGMENT, texture: { sampleType: "float", viewDimension: "2d" } },
      { binding: 2, visibility: GPUShaderStage.FRAGMENT, sampler: { type: "filtering" } },
    ],
  });

  const pipeline = device.createRenderPipeline({
    label: "msdf_label_pipeline",
    layout: device.createPipelineLayout({
      label: "msdf_label_pipeline_layout",
      bindGroupLayouts: [cameraBindGroupLayout, labelBindGroupLayout],
    }),
    vertex: {
      module: shaderModule,
      entryPoint: "vs_label",
      buffers: [{
        arrayStride: 10 * 4,
        stepMode: "vertex",
        attributes: [
          { shaderLocation: 0, offset: 0, format: "float32x2" },
          { shaderLocation: 1, offset: 2 * 4, format: "float32x2" },
          { shaderLocation: 2, offset: 4 * 4, format: "float32x2" },
          { shaderLocation: 3, offset: 6 * 4, format: "float32x4" },
        ],
      }],
    },
    fragment: {
      module: shaderModule,
      entryPoint: "fs_label",
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

  const uniformBuffer = device.createBuffer({
    label: "msdf_label_uniform",
    size: LABEL_UNIFORM_SIZE,
    usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
  });

  const bindGroup = device.createBindGroup({
    label: "msdf_label_bind_group",
    layout: labelBindGroupLayout,
    entries: [
      { binding: 0, resource: { buffer: uniformBuffer } },
      { binding: 1, resource: atlas.texture.createView() },
      { binding: 2, resource: atlas.sampler },
    ],
  });

  return { pipeline, bindGroup, uniformBuffer };
}

export function writeLabelUniform(
  device: GPUDevice,
  uniformBuffer: GPUBuffer,
  canvasSize: [number, number],
  frame: { position: [number, number, number]; xAxis: [number, number, number]; yAxis: [number, number, number] },
): void {
  const data = new Float32Array(16);
  data[0] = canvasSize[0];
  data[1] = canvasSize[1];
  data[2] = 1;
  data[3] = 0;
  data[4] = frame.position[0];
  data[5] = frame.position[1];
  data[6] = frame.position[2];
  data[7] = 0;
  data[8] = frame.xAxis[0];
  data[9] = frame.xAxis[1];
  data[10] = frame.xAxis[2];
  data[11] = 0;
  data[12] = frame.yAxis[0];
  data[13] = frame.yAxis[1];
  data[14] = frame.yAxis[2];
  data[15] = 0;
  device.queue.writeBuffer(uniformBuffer, 0, data);
}
