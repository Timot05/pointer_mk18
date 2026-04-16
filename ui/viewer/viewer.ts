import { type ActionSketch, type JsonRigidTransform, type Pickable, type RenderEntity, type SelectionTarget, type SketchLoop, type ViewerModel, type ViewerSketch, type ViewerState } from "./api";
import { ACCENT, ACCENT_SOFT, AXIS, DIM_COLOR, DIM_HOVER, FIXED_COLOR, GRID_MAJOR, GRID_MINOR, LOOP_FILL, PAGE_BG, SKETCH_LINE, SKETCH_POINT } from "./colors";
import { HALF_FOV, orbit, pan, viewBasis, zoomTowardsPointer, type CameraState } from "./camera";
import { loadFontMetrics, loadMsdfAtlas, type FontMetrics } from "./msdf-atlas";
import { add2, add3, cross3, dot2, dot3, len2, norm2, norm3, perp, scale2, scale3, sub2, sub3, type Vec2, type Vec3 } from "./math";
import { createIsosurfacePipeline } from "./pipeline-isosurface";
import { createFieldSlicePipeline } from "./pipeline-field-slice";
import { createMsdfLabelPipeline, writeLabelUniform } from "./pipeline-msdf-label";
import {
  dispatchEditor,
  selectViewerModel,
  selectViewerState,
  selectionCandidatesFromJs,
  subscribeViewerModel,
  subscribeViewerState,
} from "../src/viewer-bridge";
import {
  beginConstraintLabelDrag,
  beginPointDrag,
  cancelSketchDrag,
  cancelEditingDimension,
  commitEditingDimension,
  finishSketchDrag,
  setConstraintPlacementCursor,
  startEditingDimension,
  updateSketchDrag,
  viewerDimensionClickTarget,
  viewerHover,
  viewerPick,
  viewerPlaceConstraint,
  viewerToolClick,
} from "../src/viewer-bridge";
import { ofArray as listOfArray } from "../src-gen/fable_modules/fable-library-js.4.24.0/List.js";

type PickKind = "point" | "line" | "circle" | "arc" | "loop" | "dimension";

interface LineVertex {
  x: number;
  y: number;
  color: readonly number[];
}

interface PointInstance {
  x: number;
  y: number;
  radiusPx: number;
  color: readonly number[];
}

interface PickPoint {
  x: number;
  y: number;
  radiusPx: number;
  pickId: number;
}

interface PickSegment {
  a: Vec2;
  b: Vec2;
  strokePx: number;
  pickId: number;
  kind: number;
}

interface PickCircle {
  center: Vec2;
  radius: number;
  strokePx: number;
  pickId: number;
}

interface PickLoopTriangle {
  a: Vec2;
  b: Vec2;
  c: Vec2;
  pickId: number;
}

interface PickLabelRect {
  anchor: Vec2;
  minPx: Vec2;
  maxPx: Vec2;
  pickId: number;
}

interface RenderBuffers {
  triData: Float32Array;
  lineData: Float32Array;
  pointData: Float32Array;
  highlightLineData: Float32Array;
  highlightPointData: Float32Array;
  pickPoints: Float32Array;
  pickLines: Float32Array;
  pickCircles: Float32Array;
  pickLoops: Float32Array;
  pickLabels: Float32Array;
  labelData: Float32Array;
  highlightLabelData: Float32Array;
}

interface RenderSketch {
  sketchId: string;
  frame: SketchFrame;
  buffers: RenderBuffers;
  loops: ResolvedLoopGeometry[];
  dimensionAnchors: Map<number, Vec2>;
}

interface ConstraintLabel {
  text: string;
  anchor: Vec2;
  pickId: number | null;
  hovered: boolean;
}

interface ResolvedLoopGeometry {
  id: string;
  pickId: number | null;
  boundary: Vec2[];
}

interface PickCandidateHit {
  pickId: number;
  kind: PickKind;
  score: number;
  sketchId: string;
}

interface DragState {
  pointerId: number;
  sketchId: string;
  xPath: string;
  yPath: string;
  target: Vec2;
  kind: "point" | "label";
  pointId?: string;
  constraintIndex?: number;
}

interface PendingPrimaryState {
  pointerId: number;
  start: { x: number; y: number };
  target: NonNullable<ReturnType<ViewerApp["resolveDragTarget"]>>;
}

interface SketchFrame {
  position: Vec3;
  xAxis: Vec3;
  yAxis: Vec3;
  zAxis: Vec3;
}

interface GpuContext {
  device: GPUDevice;
  context: GPUCanvasContext;
  format: GPUTextureFormat;
  cameraLayout: GPUBindGroupLayout;
  cameraBuffer: GPUBuffer;
  cameraBindGroup: GPUBindGroup;
  gizmoPipeline: GPURenderPipeline;
  triPipeline: GPURenderPipeline;
  linePipeline: GPURenderPipeline;
  pointPipeline: GPURenderPipeline;
  sketchFrameLayout: GPUBindGroupLayout;
  frameBuffer: GPUBuffer;
  frameBindGroup: GPUBindGroup;
  viewportBuffer: GPUBuffer;
  viewportBindGroup: GPUBindGroup;
  pointQuadBuffer: GPUBuffer;
  labelPipeline: GPURenderPipeline | null;
  labelBindGroup: GPUBindGroup | null;
  labelUniformBuffer: GPUBuffer | null;
  fieldSlotBindGroupLayout: GPUBindGroupLayout;
  fieldSurfaceBindGroupLayout: GPUBindGroupLayout;
  pickPipeline: GPUComputePipeline;
  pickBindGroupLayout: GPUBindGroupLayout;
  framePickPipeline: GPUComputePipeline;
  framePickBindGroupLayout: GPUBindGroupLayout;
  pickStateBuffer: GPUBuffer;
  pickResultBuffer: GPUBuffer;
}

interface FieldPipelineState {
  pipeline: GPURenderPipeline;
  slotBuffer: GPUBuffer;
  slotCapacity: number;
  slotBindGroup: GPUBindGroup;
  surfaceBuffer: GPUBuffer;
  surfaceBindGroup: GPUBindGroup;
}

interface FieldSlicePipelineState {
  pipeline: GPURenderPipeline;
  vertexBuffer: GPUBuffer;
  vertexCount: number;
}

export interface ViewerStartOptions {
}

const NO_HIT_ID = 0xffffffff;
const PICK_GRID = 5;
const PICK_SAMPLES = PICK_GRID * PICK_GRID;
const LINE_SHADER = `
struct Camera {
  eye: vec3<f32>, _p0: f32,
  forward: vec3<f32>, _p1: f32,
  right: vec3<f32>, _p2: f32,
  up: vec3<f32>, aspect: f32,
};

struct SketchFrame {
  pos: vec4<f32>,
  x_axis: vec4<f32>,
  y_axis: vec4<f32>,
};

@group(0) @binding(0) var<uniform> cam: Camera;
@group(1) @binding(0) var<uniform> frame: SketchFrame;

struct VsIn {
  @location(0) position_2d: vec2<f32>,
  @location(1) color: vec4<f32>,
};

struct VsOut {
  @builtin(position) clip_pos: vec4<f32>,
  @location(0) color: vec4<f32>,
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
  let t = tan(${HALF_FOV});
  let proj = mat4x4<f32>(
    vec4<f32>(1.0 / (cam.aspect * t), 0.0, 0.0, 0.0),
    vec4<f32>(0.0, 1.0 / t, 0.0, 0.0),
    vec4<f32>(0.0, 0.0, -(far + near) / (far - near), -1.0),
    vec4<f32>(0.0, 0.0, -2.0 * far * near / (far - near), 0.0),
  );
  return proj * view * vec4<f32>(pos, 1.0);
}

@vertex
fn vs_main(input: VsIn) -> VsOut {
  let p = input.position_2d;
  let world = frame.pos.xyz + p.x * frame.x_axis.xyz + p.y * frame.y_axis.xyz;
  var out: VsOut;
  out.clip_pos = project_world(world);
  out.color = input.color;
  return out;
}

@fragment
fn fs_main(input: VsOut) -> @location(0) vec4<f32> {
  return input.color;
}
`;

const GIZMO_SHADER = `
struct Camera {
  eye: vec3<f32>, _p0: f32,
  forward: vec3<f32>, _p1: f32,
  right: vec3<f32>, _p2: f32,
  up: vec3<f32>, aspect: f32,
};

struct Viewport {
  size: vec2<f32>,
  _pad: vec2<f32>,
};

@group(0) @binding(0) var<uniform> cam: Camera;
@group(1) @binding(0) var<uniform> viewport: Viewport;

struct VsIn {
  @location(0) origin: vec3<f32>,
  @location(1) axis: vec3<f32>,
  @location(2) axis_px: f32,
  @location(3) endpoint: f32,
  @location(4) color: vec4<f32>,
};

struct VsOut {
  @builtin(position) clip_pos: vec4<f32>,
  @location(0) color: vec4<f32>,
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
  let t = tan(${HALF_FOV});
  let proj = mat4x4<f32>(
    vec4<f32>(1.0 / (cam.aspect * t), 0.0, 0.0, 0.0),
    vec4<f32>(0.0, 1.0 / t, 0.0, 0.0),
    vec4<f32>(0.0, 0.0, -(far + near) / (far - near), -1.0),
    vec4<f32>(0.0, 0.0, -2.0 * far * near / (far - near), 0.0),
  );
  return proj * view * vec4<f32>(pos, 1.0);
}

@vertex
fn vs_main(input: VsIn) -> VsOut {
  let depth = max(abs(dot(input.origin - cam.eye, cam.forward)), 1e-3);
  let world_per_px = (2.0 * depth * tan(${HALF_FOV})) / max(viewport.size.y, 1.0);
  let world = input.origin + input.axis * (input.axis_px * world_per_px * input.endpoint);
  var out: VsOut;
  out.clip_pos = project_world(world);
  out.color = input.color;
  return out;
}

@fragment
fn fs_main(input: VsOut) -> @location(0) vec4<f32> {
  return input.color;
}
`;

const POINT_SHADER = `
struct Camera {
  eye: vec3<f32>, _p0: f32,
  forward: vec3<f32>, _p1: f32,
  right: vec3<f32>, _p2: f32,
  up: vec3<f32>, aspect: f32,
};

struct SketchFrame {
  pos: vec4<f32>,
  x_axis: vec4<f32>,
  y_axis: vec4<f32>,
};

struct Viewport {
  size: vec2<f32>,
  _pad: vec2<f32>,
};

@group(0) @binding(0) var<uniform> cam: Camera;
@group(1) @binding(0) var<uniform> frame: SketchFrame;
@group(2) @binding(0) var<uniform> viewport: Viewport;

struct QuadIn {
  @location(0) corner: vec2<f32>,
};

struct InstanceIn {
  @location(1) center_2d: vec2<f32>,
  @location(2) radius_px: f32,
  @location(3) color: vec4<f32>,
};

struct VsOut {
  @builtin(position) clip_pos: vec4<f32>,
  @location(0) color: vec4<f32>,
  @location(1) local_pos: vec2<f32>,
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
  let t = tan(${HALF_FOV});
  let proj = mat4x4<f32>(
    vec4<f32>(1.0 / (cam.aspect * t), 0.0, 0.0, 0.0),
    vec4<f32>(0.0, 1.0 / t, 0.0, 0.0),
    vec4<f32>(0.0, 0.0, -(far + near) / (far - near), -1.0),
    vec4<f32>(0.0, 0.0, -2.0 * far * near / (far - near), 0.0),
  );
  return proj * view * vec4<f32>(pos, 1.0);
}

@vertex
fn vs_main(quad: QuadIn, instance: InstanceIn) -> VsOut {
  let world = frame.pos.xyz + instance.center_2d.x * frame.x_axis.xyz + instance.center_2d.y * frame.y_axis.xyz;
  let center_clip = project_world(world);
  let size = max(viewport.size, vec2<f32>(1.0, 1.0));
  let offset_ndc = vec2<f32>(
    quad.corner.x * instance.radius_px * 2.0 / size.x,
    quad.corner.y * instance.radius_px * 2.0 / size.y
  );
  var out: VsOut;
  out.clip_pos = vec4<f32>(center_clip.xy + offset_ndc * center_clip.w, center_clip.z, center_clip.w);
  out.color = instance.color;
  out.local_pos = quad.corner;
  return out;
}

@fragment
fn fs_main(input: VsOut) -> @location(0) vec4<f32> {
  if (dot(input.local_pos, input.local_pos) > 1.0) {
    discard;
  }
  return input.color;
}
`;

const PICK_SHADER = `
const HALF_FOV: f32 = ${HALF_FOV};
const PICK_GRID: u32 = ${PICK_GRID}u;
const NO_HIT: u32 = ${NO_HIT_ID}u;

struct Camera {
  eye: vec3<f32>, _p0: f32,
  forward: vec3<f32>, _p1: f32,
  right: vec3<f32>, _p2: f32,
  up: vec3<f32>, aspect: f32,
};

struct SketchFrame {
  pos: vec4<f32>,
  x_axis: vec4<f32>,
  y_axis: vec4<f32>,
};

struct PickState {
  viewport: vec2<f32>,
  mouse: vec2<f32>,
};

struct PickSample {
  id: u32,
  kind: u32,
  score: f32,
  _pad: f32,
};

@group(0) @binding(0) var<uniform> cam: Camera;
@group(0) @binding(1) var<uniform> frame: SketchFrame;
@group(0) @binding(2) var<uniform> pick: PickState;
@group(0) @binding(3) var<storage, read> points: array<vec4<f32>>;
@group(0) @binding(4) var<storage, read> lines: array<vec4<f32>>;
@group(0) @binding(5) var<storage, read> circles: array<vec4<f32>>;
@group(0) @binding(6) var<storage, read> loops: array<vec4<f32>>;
@group(0) @binding(7) var<storage, read> labels: array<vec4<f32>>;
@group(0) @binding(8) var<storage, read_write> samples: array<PickSample, ${PICK_SAMPLES}>;

fn make_ray(pixel: vec2<f32>) -> vec3<f32> {
  let uv = pixel / max(pick.viewport, vec2<f32>(1.0, 1.0));
  let ndc = vec2<f32>(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
  return normalize(cam.forward + cam.right * (ndc.x * cam.aspect * tan(HALF_FOV)) + cam.up * (ndc.y * tan(HALF_FOV)));
}

fn frame_hit(dir: vec3<f32>) -> vec3<f32> {
  let normal = cross(frame.x_axis.xyz, frame.y_axis.xyz);
  let denom = dot(normal, dir);
  if (abs(denom) < 1e-6) { return vec3<f32>(1e9, 1e9, 0.0); }
  let t = dot(normal, frame.pos.xyz - cam.eye) / denom;
  if (t <= 0.0) { return vec3<f32>(1e9, 1e9, 0.0); }
  let hit_world = cam.eye + dir * t;
  let local = hit_world - frame.pos.xyz;
  return vec3<f32>(dot(local, frame.x_axis.xyz), dot(local, frame.y_axis.xyz), t);
}

fn world_per_px(t: f32) -> f32 {
  return (2.0 * t * tan(HALF_FOV)) / max(pick.viewport.y, 1.0);
}

fn sdf_segment(p: vec2<f32>, a: vec2<f32>, b: vec2<f32>) -> f32 {
  let ab = b - a;
  let denom = max(dot(ab, ab), 1e-8);
  let h = clamp(dot(p - a, ab) / denom, 0.0, 1.0);
  return length((a + ab * h) - p);
}

fn point_in_triangle(p: vec2<f32>, a: vec2<f32>, b: vec2<f32>, c: vec2<f32>) -> bool {
  let v0 = c - a;
  let v1 = b - a;
  let v2 = p - a;
  let dot00 = dot(v0, v0);
  let dot01 = dot(v0, v1);
  let dot02 = dot(v0, v2);
  let dot11 = dot(v1, v1);
  let dot12 = dot(v1, v2);
  let inv = 1.0 / max(dot00 * dot11 - dot01 * dot01, 1e-8);
  let u = (dot11 * dot02 - dot01 * dot12) * inv;
  let v = (dot00 * dot12 - dot01 * dot02) * inv;
  return u >= 0.0 && v >= 0.0 && (u + v) <= 1.0;
}

fn label_minmax(rect0: vec4<f32>, rect1: vec4<f32>, wpp: f32) -> mat2x2<f32> {
  let proj_fx = frame.x_axis.xyz - dot(frame.x_axis.xyz, cam.forward) * cam.forward;
  let proj_fy = frame.y_axis.xyz - dot(frame.y_axis.xyz, cam.forward) * cam.forward;
  let x_sign = select(-1.0, 1.0, dot(proj_fx, cam.right) > 0.0);
  let y_sign = select(-1.0, 1.0, dot(proj_fy, cam.up) > 0.0);
  let p0 = rect0.xy + vec2<f32>(rect0.z * x_sign, -rect0.w * y_sign) * wpp;
  let p1 = rect0.xy + vec2<f32>(rect1.x * x_sign, -rect1.y * y_sign) * wpp;
  return mat2x2<f32>(min(p0, p1), max(p0, p1));
}

@compute @workgroup_size(${PICK_SAMPLES})
fn cs_main(@builtin(local_invocation_index) index: u32) {
  let gx = i32(index % PICK_GRID) - 2;
  let gy = i32(index / PICK_GRID) - 2;
  let pixel = pick.mouse + vec2<f32>(f32(gx), f32(gy));
  let dir = make_ray(pixel);

  var bestId: u32 = NO_HIT;
  var bestKind: u32 = 0u;
  var bestScore: f32 = 1e9;

  let hit = frame_hit(dir);
  let planeT = hit.z;
  if (planeT > 0.0 && planeT < 1e8) {
    let p = hit.xy;
    let wpp = world_per_px(planeT);

    let pointCount = arrayLength(&points);
    for (var i: u32 = 0u; i < pointCount; i = i + 1u) {
      let raw = points[i];
      let score = length(p - raw.xy) / max(wpp, 1e-6);
      if (score <= raw.z && score < bestScore) {
        bestScore = score;
        bestId = u32(raw.w + 0.5);
        bestKind = 1u;
      }
    }

    let lineCount = arrayLength(&lines) / 2u;
    for (var i: u32 = 0u; i < lineCount; i = i + 1u) {
      let geom = lines[i * 2u];
      let info = lines[i * 2u + 1u];
      let score = sdf_segment(p, geom.xy, geom.zw) / max(wpp, 1e-6);
      if (score <= info.x && score < bestScore) {
        bestScore = score;
        bestId = u32(info.y + 0.5);
        bestKind = u32(info.z + 0.5);
      }
    }

    let circleCount = arrayLength(&circles) / 2u;
    for (var i: u32 = 0u; i < circleCount; i = i + 1u) {
      let geom = circles[i * 2u];
      let info = circles[i * 2u + 1u];
      let score = abs(length(p - geom.xy) - geom.z) / max(wpp, 1e-6);
      if (score <= geom.w && score < bestScore) {
        bestScore = score;
        bestId = u32(info.x + 0.5);
        bestKind = 3u;
      }
    }

    let loopCount = arrayLength(&loops) / 2u;
    for (var i: u32 = 0u; i < loopCount; i = i + 1u) {
      let tri0 = loops[i * 2u];
      let tri1 = loops[i * 2u + 1u];
      if (point_in_triangle(p, tri0.xy, tri0.zw, tri1.xy)) {
        let centroid = (tri0.xy + tri0.zw + tri1.xy) / 3.0;
        let score = 50.0 + length(p - centroid);
        if (score < bestScore) {
          bestScore = score;
          bestId = u32(tri1.z + 0.5);
          bestKind = 5u;
        }
      }
    }

    let labelCount = arrayLength(&labels) / 2u;
    for (var i: u32 = 0u; i < labelCount; i = i + 1u) {
      let rect0 = labels[i * 2u];
      let rect1 = labels[i * 2u + 1u];
      let mm = label_minmax(rect0, rect1, wpp);
      let minp = mm[0];
      let maxp = mm[1];
      if (all(p >= minp) && all(p <= maxp)) {
        let center = (minp + maxp) * 0.5;
        let score = 20.0 + length(p - center) / max(wpp, 1e-6);
        if (score < bestScore) {
          bestScore = score;
          bestId = u32(rect1.z + 0.5);
          bestKind = 6u;
        }
      }
    }
  }

  samples[index].id = bestId;
  samples[index].kind = bestKind;
  samples[index].score = bestScore;
}
`;

const FRAME_PICK_SHADER = `
const HALF_FOV: f32 = ${HALF_FOV};
const PICK_GRID: u32 = ${PICK_GRID}u;
const NO_HIT: u32 = ${NO_HIT_ID}u;

struct Camera {
  eye: vec3<f32>, _p0: f32,
  forward: vec3<f32>, _p1: f32,
  right: vec3<f32>, _p2: f32,
  up: vec3<f32>, aspect: f32,
};

struct PickState {
  viewport: vec2<f32>,
  mouse: vec2<f32>,
};

struct PickSample {
  id: u32,
  kind: u32,
  score: f32,
  _pad: f32,
};

@group(0) @binding(0) var<uniform> cam: Camera;
@group(0) @binding(1) var<uniform> pick: PickState;
@group(0) @binding(2) var<storage, read> origins: array<vec4<f32>>;
@group(0) @binding(3) var<storage, read_write> samples: array<PickSample, ${PICK_SAMPLES}>;

fn project_world(pos: vec3<f32>) -> vec3<f32> {
  let rel = pos - cam.eye;
  let z = dot(rel, cam.forward);
  if (z <= 1e-6) { return vec3<f32>(1e9, 1e9, -1.0); }
  let ndc_x = dot(rel, cam.right) / (z * tan(HALF_FOV) * cam.aspect);
  let ndc_y = dot(rel, cam.up) / (z * tan(HALF_FOV));
  return vec3<f32>(
    ((ndc_x + 1.0) * 0.5) * pick.viewport.x,
    ((1.0 - ndc_y) * 0.5) * pick.viewport.y,
    z,
  );
}

fn sdf_segment(p: vec2<f32>, a: vec2<f32>, b: vec2<f32>) -> f32 {
  let ab = b - a;
  let denom = max(dot(ab, ab), 1e-8);
  let h = clamp(dot(p - a, ab) / denom, 0.0, 1.0);
  return length((a + ab * h) - p);
}

@compute @workgroup_size(${PICK_SAMPLES})
fn cs_main(@builtin(local_invocation_index) index: u32) {
  let gx = i32(index % PICK_GRID) - 2;
  let gy = i32(index / PICK_GRID) - 2;
  let pixel = pick.mouse + vec2<f32>(f32(gx), f32(gy));

  var bestId: u32 = NO_HIT;
  var bestKind: u32 = 0u;
  var bestScore: f32 = 1e9;

  let originCount = arrayLength(&origins);
  for (var i: u32 = 0u; i < originCount; i = i + 1u) {
    let raw = origins[i];
    let screen = project_world(raw.xyz);
    if (screen.z <= 0.0) { continue; }
    let score = length(pixel - screen.xy);
    if (score <= 9.0 && score < bestScore) {
      bestScore = score;
      bestId = u32(raw.w + 0.5);
      bestKind = 1u;
    }
  }
  samples[index].id = bestId;
  samples[index].kind = bestKind;
  samples[index].score = bestScore;
}
`;

export class ViewerApp {
  private static readonly DRAG_START_PX = 4;
  private readonly root: HTMLElement;
  private readonly canvas: HTMLCanvasElement;
  private gpu: GpuContext | null = null;
  private model: ViewerModel | null = null;
  private state: ViewerState | null = null;
  private fontMetrics: FontMetrics | null = null;
  private slotLookup = new Map<string, number>();
  private renderSketches: RenderSketch[] = [];
  private dimensionEditInput: HTMLInputElement | null = null;
  private dimensionEditKey: string | null = null;
  private resizeObserver: ResizeObserver | null = null;
  private fieldPipeline: FieldPipelineState | null = null;
  private fieldSlicePipeline: FieldSlicePipelineState | null = null;
  private renderQueued = false;
  private isPicking = false;
  private resetCameraPending = false;
  private pointer = { x: 0, y: 0 };
  private drag: DragState | null = null;
  private pendingPrimary: PendingPrimaryState | null = null;
  private suppressPrimaryUp = false;
  private interaction:
    | { kind: "orbit" | "pan"; x: number; y: number; pointerId: number }
    | null = null;
  private readonly camera: CameraState = {
    azimuth: 0.78,
    elevation: 0.58,
    distance: 80,
    target: [0, 0, 0],
  };

  constructor(root: HTMLElement) {
    this.root = root;
    this.root.classList.add("viewer-root");
    this.canvas = document.createElement("canvas");
    this.canvas.className = "viewer-canvas";
    root.replaceChildren(this.canvas);
  }

  async start(options: ViewerStartOptions = {}): Promise<void> {
    this.bindEvents();
    this.gpu = await this.initGpu();
    try {
      this.fontMetrics = await loadFontMetrics("/fonts/dekal.json");
    } catch (_error) {}
    await this.reloadModel(true);
    await this.reloadState();
    this.resizeObserver = new ResizeObserver(() => {
      this.resizeCanvas();
      this.queueRender();
    });
    this.resizeObserver.observe(this.canvas);
    this.resizeCanvas();
    subscribeViewerState(() => {
      void this.reloadState();
    });
    subscribeViewerModel(() => {
      void this.reloadModel(false).then(() => this.reloadState());
    });
    this.queueRender();
  }

  private async reloadModel(resetCamera: boolean): Promise<void> {
    if (this.drag) return;
    this.model = selectViewerModel() as ViewerModel;
    this.rebuildFieldPipeline();
    this.rebuildFieldSlicePipeline();
    this.slotLookup = new Map(this.model.slotIndex.map((entry) => [`${entry.actionId}:${entry.path}`, entry.slot]));
    this.rebuildRenderData();
    if (resetCamera) this.resetCameraPending = true;
    this.queueRender();
  }

  private async reloadState(): Promise<void> {
    if (this.drag) return;
    let nextState = selectViewerState() as ViewerState;
    if (this.model && nextState.params.length !== this.model.numSlots) {
      await this.reloadModel(false);
      nextState = selectViewerState() as ViewerState;
    }
    this.applyViewerState(nextState);
    this.rebuildRenderData();
    this.updateFieldBuffers();
    this.updateFieldSliceBuffers();
    if (this.resetCameraPending) {
      this.fitCamera();
      this.resetCameraPending = false;
    }
    this.queueRender();
  }

  private applyViewerState(nextState: ViewerState): void {
    this.state = nextState;
  }

  private bindEvents(): void {
    this.canvas.addEventListener("contextmenu", (event) => event.preventDefault());
    this.canvas.addEventListener("pointerdown", (event) => {
      this.pointer = this.eventPos(event);
      if (event.button === 2) {
        this.interaction = { kind: "orbit", x: event.clientX, y: event.clientY, pointerId: event.pointerId };
        this.canvas.setPointerCapture(event.pointerId);
      } else if (event.button === 1) {
        event.preventDefault();
        this.interaction = { kind: "pan", x: event.clientX, y: event.clientY, pointerId: event.pointerId };
        this.canvas.setPointerCapture(event.pointerId);
      } else if (event.button === 0) {
        void this.beginPrimaryPointer(event);
      }
    });
    this.canvas.addEventListener("pointermove", (event) => {
      this.pointer = this.eventPos(event);
      if (this.state?.sketchUi.editingDimension) return;
      if (this.drag?.pointerId === event.pointerId) {
        void this.updateDragFrame();
        return;
      }
      if (this.pendingPrimary?.pointerId === event.pointerId) {
        const dx = this.pointer.x - this.pendingPrimary.start.x;
        const dy = this.pointer.y - this.pendingPrimary.start.y;
        if ((dx * dx) + (dy * dy) >= ViewerApp.DRAG_START_PX * ViewerApp.DRAG_START_PX) {
          void this.startPendingDrag(event.pointerId);
          return;
        }
      }
      if (this.interaction) {
        const dx = event.clientX - this.interaction.x;
        const dy = event.clientY - this.interaction.y;
        this.interaction.x = event.clientX;
        this.interaction.y = event.clientY;
        if (this.interaction.kind === "orbit") orbit(this.camera, dx, dy);
        else pan(this.camera, dx, dy, this.canvas.clientHeight);
        this.queueRender();
        return;
      }
      void this.pickAtPointer(false);
    });
    this.canvas.addEventListener("pointerup", (event) => {
      if (this.state?.sketchUi.editingDimension) return;
      if (this.drag?.pointerId === event.pointerId) {
        this.pointer = this.eventPos(event);
        this.canvas.releasePointerCapture(event.pointerId);
        void this.finishDrag();
        return;
      }
      if (this.pendingPrimary?.pointerId === event.pointerId) {
        this.canvas.releasePointerCapture(event.pointerId);
        this.pendingPrimary = null;
      }
      if (this.interaction?.pointerId === event.pointerId) {
        this.canvas.releasePointerCapture(event.pointerId);
        this.interaction = null;
        return;
      }
      if (event.button === 0) {
        if (this.suppressPrimaryUp) {
          this.suppressPrimaryUp = false;
          return;
        }
        void this.pickAtPointer(event);
      }
    });
    this.canvas.addEventListener("wheel", (event) => {
      event.preventDefault();
      const rect = this.canvas.getBoundingClientRect();
      zoomTowardsPointer(
        this.camera,
        rect.width,
        rect.height,
        event.clientX - rect.left,
        event.clientY - rect.top,
        event.deltaY,
      );
      this.queueRender();
    }, { passive: false });
    this.canvas.addEventListener("dblclick", (event) => {
      if (event.button !== 0) return;
      this.pointer = this.eventPos(event);
      void this.startDimensionEditAtPointer();
    });
  }

  private eventPos(event: PointerEvent): { x: number; y: number } {
    const rect = this.canvas.getBoundingClientRect();
    return { x: event.clientX - rect.left, y: event.clientY - rect.top };
  }

  private async beginPrimaryPointer(event: PointerEvent): Promise<void> {
    if (await this.tryHandleSketchAuthoringClick()) {
      this.suppressPrimaryUp = true;
      this.queueRender();
      return;
    }
    const candidates = [...await this.pickAcrossSketches(), ...await this.pickFrameTargetsGpu()];
    dispatchEditor(viewerHover(selectionCandidatesFromJs(toPickRequest(candidates))));
    this.applyViewerState(selectViewerState() as ViewerState);
    this.rebuildRenderData();
    this.queueRender();
    if (event.shiftKey) return;
    const dragTarget = this.state.dragTarget;
    if (!dragTarget) return;

    const target = this.resolveDragTarget(dragTarget);
    if (!target) return;
    this.pendingPrimary = {
      pointerId: event.pointerId,
      start: { ...this.pointer },
      target,
    };
    this.canvas.setPointerCapture(event.pointerId);
  }

  private async startPendingDrag(pointerId: number): Promise<void> {
    const pending = this.pendingPrimary;
    if (!pending || pending.pointerId !== pointerId) return;
    const target = pending.target;
    if (!target) {
      this.pendingPrimary = null;
      return;
    }
    const local = pointerToSketchLocal(this.pointer, this.canvas, this.camera, target.frame);
    this.pendingPrimary = null;
    if (!local) return;
    this.drag = {
      pointerId,
      sketchId: target.sketchId,
      kind: target.kind,
      pointId: target.pointId,
      constraintIndex: target.constraintIndex,
      xPath: target.xPath,
      yPath: target.yPath,
      target: local,
    };
    if (target.kind === "point" && target.pointId) {
      dispatchEditor(beginPointDrag(target.sketchId, target.pointId, local[0], local[1]));
    } else if (target.kind === "label" && typeof target.constraintIndex === "number") {
      dispatchEditor(beginConstraintLabelDrag(target.sketchId, target.constraintIndex, local[0], local[1]));
    }
    this.applyViewerState(selectViewerState() as ViewerState);
    this.rebuildRenderData();
    this.queueRender();
  }

  private async startDimensionEditAtPointer(): Promise<void> {
    if (!this.state?.sketchUi.editMode || this.drag || this.interaction) return;
    const target = this.state.hoveredTarget;
    if (!target || target.case !== "TargetDimension") return;
    dispatchEditor(startEditingDimension(target.constraintIndex));
    this.applyViewerState(selectViewerState() as ViewerState);
    this.rebuildRenderData();
    this.queueRender();
  }

  private async cancelDimensionEdit(): Promise<void> {
    dispatchEditor(cancelEditingDimension);
    this.applyViewerState(selectViewerState() as ViewerState);
    this.rebuildRenderData();
    this.queueRender();
  }

  private async commitDimensionEdit(raw: string): Promise<void> {
    const value = Number.parseFloat(raw);
    if (!Number.isFinite(value)) {
      await this.cancelDimensionEdit();
      return;
    }
    dispatchEditor(commitEditingDimension(value));
    this.applyViewerState(selectViewerState() as ViewerState);
    this.rebuildRenderData();
    this.queueRender();
  }

  private authoringSketch(): { model: ViewerSketch; frame: SketchFrame } | null {
    if (!this.model || !this.state) return null;
    if (!this.state.sketchUi.editMode) return null;
    const sketchId = this.state.selectedId;
    if (!sketchId) return null;
    const model = this.model.sketches.find((candidate) => candidate.id === sketchId);
    if (!model) return null;
    return { model, frame: toSketchFrame(model.transform) };
  }

  private currentToolPreview(): { frame: SketchFrame; lineData: Float32Array; pointData: Float32Array } | null {
    const authored = this.authoringSketch();
    if (!authored || !this.state) return null;
    const tool = this.state.sketchUi.tool;
    if (!tool || tool === "none") return null;
    const points = this.state.sketchUi.toolPoints.map((point) => [point.x, point.y] as Vec2);
    const cursor = pointerToSketchLocal(this.pointer, this.canvas, this.camera, authored.frame);
    return buildToolPreviewBuffers(tool, points, cursor, authored.frame);
  }

  private currentConstraintPreview(): { frame: SketchFrame; lineData: Float32Array; labelData: Float32Array } | null {
    const authored = this.authoringSketch();
    const pending = this.state?.sketchUi.pendingConstraintPlacement;
    if (!authored || !pending || pending.sketchId !== authored.model.id) return null;
    const cursor = pointerToSketchLocal(this.pointer, this.canvas, this.camera, authored.frame);
    if (!cursor) return null;
    const effectiveParams = new Float32Array(this.state?.params ?? []);

    const entityMap = new Map(authored.model.sketch.entities.map((entity) => [entity.id, entity]));
    const pointMap = new Map<string, Vec2>();
    const framePointMap = new Map<string, Vec2>(
      this.state!.frames.map((frame) => [frame.id, projectWorldToSketchLocal(toSketchFrame(frame.transform).position, authored.frame)] as const),
    );
    const resolveValue = (path: string, fallback: number) =>
      slotValue(effectiveParams, this.slotLookup, authored.model.id, path, fallback);

    for (const entity of authored.model.sketch.entities) {
      if (entity.case === "REPoint") {
        pointMap.set(entity.id, [
          resolveValue(`sketch.entity.${entity.id}.x`, entity.x),
          resolveValue(`sketch.entity.${entity.id}.y`, entity.y),
        ]);
      }
    }

    const lineVertices: LineVertex[] = [];
    const labels: ConstraintLabel[] = [];
    pushConstraintGeometry(
      lineVertices,
      [],
      labels,
      [],
      new Map<number, Vec2>(),
      pointMap,
      entityMap,
      framePointMap,
      resolveValue,
      () => cursor,
      pending.constraint,
      -1,
      null,
      null,
      [],
      authored.model.id,
      true,
    );

    return {
      frame: authored.frame,
      lineData: flattenLines(lineVertices),
      labelData: this.fontMetrics ? buildLabelVertices(labels, this.fontMetrics) : new Float32Array(),
    };
  }

  private async tryHandleSketchAuthoringClick(): Promise<boolean> {
    if (!this.authoringSketch()) {
      return false;
    }
    const authored = this.authoringSketch();
    if (!authored) return false;
    const local = pointerToSketchLocal(this.pointer, this.canvas, this.camera, authored.frame);
    if (!local) return false;

    const placement = this.state?.sketchUi.constraintPlacementMode;
    if (placement) {
      const candidates = [...await this.pickAcrossSketches(), ...await this.pickFrameTargetsGpu()];
      const placementCursor = this.currentPlacementCursor();
      if (placementCursor) {
        dispatchEditor(
          setConstraintPlacementCursor(
            placementCursor ? [placementCursor.sketchId, { X: placementCursor.x, Y: placementCursor.y }] : undefined,
          ),
        );
        this.applyViewerState(selectViewerState() as ViewerState);
      }
      dispatchEditor(viewerHover(selectionCandidatesFromJs(toPickRequest(candidates))));
      this.applyViewerState(selectViewerState() as ViewerState);
      const hovered = this.state?.hoveredTarget;
      if (hovered && hovered.case !== "TargetDimension" && hovered.case !== "TargetLoop" && hovered.case !== "TargetSurface") {
        dispatchEditor(viewerDimensionClickTarget);
        this.applyViewerState(selectViewerState() as ViewerState);
        this.rebuildRenderData();
        this.queueRender();
        return true;
      }
      if (!this.state?.sketchUi.pendingConstraintPlacement) {
        return false;
      }
      dispatchEditor(viewerPlaceConstraint(local[0], local[1]));
      this.applyViewerState(selectViewerState() as ViewerState);
      await this.reloadModel(false);
      return true;
    }

    const tool = this.state?.sketchUi.tool ?? "none";
    if (tool === "none") {
      return false;
    }
    dispatchEditor(viewerToolClick(local[0], local[1]));
    this.applyViewerState(selectViewerState() as ViewerState);
    await this.reloadModel(false);
    return true;
  }

  private updateDragTarget(): void {
    if (!this.drag) return;
    const sketch = this.renderSketches.find((candidate) => candidate.sketchId === this.drag!.sketchId);
    if (!sketch) return;
    const local = pointerToSketchLocal(this.pointer, this.canvas, this.camera, sketch.frame);
    if (!local) return;
    this.drag.target = local;
  }

  private async finishDrag(): Promise<void> {
    const drag = this.drag;
    if (!drag) return;
    this.updateDragTarget();
    this.drag = null;
    dispatchEditor(finishSketchDrag);
    this.applyViewerState(selectViewerState() as ViewerState);
    await this.reloadState();
    this.rebuildRenderData();
    this.queueRender();
  }

  private async updateDragFrame(): Promise<void> {
    this.updateDragTarget();
    if (this.drag) {
      dispatchEditor(updateSketchDrag(this.drag.target[0], this.drag.target[1]));
    } else {
      dispatchEditor(cancelSketchDrag);
    }
    this.applyViewerState(selectViewerState() as ViewerState);
    this.rebuildRenderData();
    this.queueRender();
  }

  private resolveDragTarget(target: SelectionTarget): { kind: "point" | "label"; sketchId: string; pointId?: string; constraintIndex?: number; xPath: string; yPath: string; frame: SketchFrame } | null {
    switch (target.case) {
      case "TargetPoint": {
        const sketch = this.renderSketches.find((candidate) => candidate.sketchId === target.sketchId);
        if (!sketch) return null;
        return {
          kind: "point",
          sketchId: target.sketchId,
          pointId: target.entityId,
          xPath: `sketch.entity.${target.entityId}.x`,
          yPath: `sketch.entity.${target.entityId}.y`,
          frame: sketch.frame,
        };
      }
      case "TargetDimension": {
        const sketch = this.renderSketches.find((candidate) => candidate.sketchId === target.sketchId);
        if (!sketch) return null;
        return {
          kind: "label",
          sketchId: target.sketchId,
          constraintIndex: target.constraintIndex,
          xPath: `sketch.constraint.${target.constraintIndex}.labelPosition.x`,
          yPath: `sketch.constraint.${target.constraintIndex}.labelPosition.y`,
          frame: sketch.frame,
        };
      }
      default:
        return null;
    }
  }

  private fitCamera(): void {
    if (!this.model || !this.state) return;
    const frames = this.activeFrameList();
    if (this.model.sketches.length === 0 && frames.length === 0) return;
    let worldMin: Vec3 = [Infinity, Infinity, Infinity];
    let worldMax: Vec3 = [-Infinity, -Infinity, -Infinity];
    for (const frame of frames) {
      if (!this.isVisible(frame.id)) continue;
      const t = toSketchFrame(frame.transform);
      const p = t.position;
      worldMin = [Math.min(worldMin[0], p[0]), Math.min(worldMin[1], p[1]), Math.min(worldMin[2], p[2])];
      worldMax = [Math.max(worldMax[0], p[0]), Math.max(worldMax[1], p[1]), Math.max(worldMax[2], p[2])];
    }
    for (const sketch of this.model.sketches) {
      if (!this.isVisible(sketch.id)) continue;
      const frame = toSketchFrame(sketch.transform);
      const points = sketch.sketch.entities.filter((entity): entity is Extract<RenderEntity, { case: "REPoint" }> => entity.case === "REPoint");
      for (const p of points) {
        const local: Vec2 = [p.x, p.y];
        const world = liftPoint(frame, local);
        worldMin = [Math.min(worldMin[0], world[0]), Math.min(worldMin[1], world[1]), Math.min(worldMin[2], world[2])];
        worldMax = [Math.max(worldMax[0], world[0]), Math.max(worldMax[1], world[1]), Math.max(worldMax[2], world[2])];
      }
    }
    if (!Number.isFinite(worldMin[0])) return;
    const center: Vec3 = [
      (worldMin[0] + worldMax[0]) * 0.5,
      (worldMin[1] + worldMax[1]) * 0.5,
      (worldMin[2] + worldMax[2]) * 0.5,
    ];
    const radius = Math.hypot(worldMax[0] - worldMin[0], worldMax[1] - worldMin[1], worldMax[2] - worldMin[2]) * 0.5;
    this.camera.target = center;
    this.camera.distance = Math.max(40, radius * 2.4);
  }

  private activeFrameList(): Array<{ id: string; transform: JsonRigidTransform }> {
    if (!this.state) return [];
    return this.state.sketchUi.editMode ? this.state.sketchEditFrames : this.state.frames;
  }

  private rebuildRenderData(): void {
    if (!this.model || !this.state) return;
    const effectiveParams = new Float32Array(this.state?.params ?? []);
    this.renderSketches = this.model.sketches
      .filter((sketch) => this.isVisible(sketch.id))
      .map((sketch) => {
        const frame = toSketchFrame(sketch.transform);
        const built = buildSketchBuffers(
          sketch,
          this.model!.pickables,
          this.slotLookup,
          effectiveParams,
          this.state!.frames,
          this.state!.highlightedTarget,
          this.state.highlightedTargets,
          this.fontMetrics,
          this.drag,
          frame,
          (constraintIndex) => this.constraintLabelPosition(sketch.id, constraintIndex),
          this.state!.visibleDimensionSketchIds.includes(sketch.id),
          this.state!.sketchUi.editMode && this.state!.selectedId === sketch.id,
        );
        return {
          sketchId: sketch.id,
          frame,
          buffers: built.buffers,
          loops: built.loops,
          dimensionAnchors: built.dimensionAnchors,
        };
      });
  }

  private isVisible(actionId: string): boolean {
    return this.state?.visible[actionId] ?? true;
  }

  private constraintLabelPosition(sketchId: string, constraintIndex: number): Vec2 | null {
    const hit = this.state?.constraintLabelPositions.find((candidate) =>
      candidate.sketchId === sketchId && candidate.constraintIndex === constraintIndex);
    return hit ? [hit.position.x, hit.position.y] : null;
  }


  private resizeCanvas(): void {
    const dpr = Math.max(window.devicePixelRatio || 1, 1);
    const width = Math.max(1, Math.floor(this.canvas.clientWidth * dpr));
    const height = Math.max(1, Math.floor(this.canvas.clientHeight * dpr));
    if (this.canvas.width !== width || this.canvas.height !== height) {
      this.canvas.width = width;
      this.canvas.height = height;
      this.gpu?.context.configure({
        device: this.gpu.device,
        format: this.gpu.format,
        alphaMode: "opaque",
      });
    }
  }

  private queueRender(): void {
    if (this.renderQueued) return;
    this.renderQueued = true;
    requestAnimationFrame(() => {
      this.renderQueued = false;
      this.render();
    });
  }

  private render(): void {
    if (!this.gpu) return;
    const { device, context, cameraBuffer, cameraBindGroup, gizmoPipeline, triPipeline, linePipeline, pointPipeline, frameBuffer, frameBindGroup, viewportBuffer, viewportBindGroup, pointQuadBuffer, labelPipeline, labelBindGroup, labelUniformBuffer } = this.gpu;
    this.resizeCanvas();

    const width = this.canvas.width;
    const height = this.canvas.height;
    const aspect = width / Math.max(height, 1);
    const { eye, forward, right, up } = viewBasis(this.camera);
    const cameraData = new Float32Array(16);
    cameraData.set(eye, 0);
    cameraData.set(forward, 4);
    cameraData.set(right, 8);
    cameraData.set(up, 12);
    cameraData[15] = aspect;
    device.queue.writeBuffer(cameraBuffer, 0, cameraData);
    device.queue.writeBuffer(viewportBuffer, 0, new Float32Array([width, height, 0, 0]));

    const encoder = device.createCommandEncoder();
    const textureView = context.getCurrentTexture().createView();
    const pass = encoder.beginRenderPass({
      colorAttachments: [{
        view: textureView,
        clearValue: hexToGpuColor(PAGE_BG),
        loadOp: "clear",
        storeOp: "store",
      }],
    });
    pass.setBindGroup(0, cameraBindGroup);

    if (this.fieldPipeline) {
      this.updateFieldBuffers();
      pass.setPipeline(this.fieldPipeline.pipeline);
      pass.setBindGroup(1, this.fieldPipeline.slotBindGroup);
      pass.setBindGroup(2, this.fieldPipeline.surfaceBindGroup);
      pass.draw(3);
    }

    if (this.fieldSlicePipeline && this.fieldPipeline && this.fieldSlicePipeline.vertexCount > 0) {
      pass.setPipeline(this.fieldSlicePipeline.pipeline);
      pass.setBindGroup(1, this.fieldPipeline.slotBindGroup);
      pass.setVertexBuffer(0, this.fieldSlicePipeline.vertexBuffer);
      pass.draw(this.fieldSlicePipeline.vertexCount);
    }

    const frameLineData = this.state
      ? buildFrameLineData(
          this.activeFrameList().filter((frame) => this.isVisible(frame.id)),
          this.state.hoveredTarget,
          this.state.selectedTargets,
          this.state.selectedId,
        )
      : new Float32Array();
    if (frameLineData.length > 0) {
      const gizmoBuffer = device.createBuffer({
        size: frameLineData.byteLength,
        usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST,
      });
      device.queue.writeBuffer(gizmoBuffer, 0, frameLineData);
      pass.setPipeline(gizmoPipeline);
      pass.setBindGroup(1, viewportBindGroup);
      pass.setVertexBuffer(0, gizmoBuffer);
      pass.draw(frameLineData.length / 12);
    }

    for (const sketch of this.renderSketches) {
      const frameData = new Float32Array(12);
      frameData.set(sketch.frame.position, 0);
      frameData.set(sketch.frame.xAxis, 4);
      frameData.set(sketch.frame.yAxis, 8);
      device.queue.writeBuffer(frameBuffer, 0, frameData);
      pass.setBindGroup(1, frameBindGroup);

      if (sketch.buffers.triData.length > 0) {
        const triBuffer = device.createBuffer({
          size: sketch.buffers.triData.byteLength,
          usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST,
        });
        device.queue.writeBuffer(triBuffer, 0, sketch.buffers.triData);
        pass.setPipeline(triPipeline);
        pass.setVertexBuffer(0, triBuffer);
        pass.draw(sketch.buffers.triData.length / 6);
      }

      if (sketch.buffers.lineData.length > 0) {
        const lineBuffer = device.createBuffer({
          size: sketch.buffers.lineData.byteLength,
          usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST,
        });
        device.queue.writeBuffer(lineBuffer, 0, sketch.buffers.lineData);
        pass.setPipeline(linePipeline);
        pass.setVertexBuffer(0, lineBuffer);
        pass.draw(sketch.buffers.lineData.length / 6);
      }

      if (sketch.buffers.pointData.length > 0) {
        const pointBuffer = device.createBuffer({
          size: sketch.buffers.pointData.byteLength,
          usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST,
        });
        device.queue.writeBuffer(pointBuffer, 0, sketch.buffers.pointData);
        pass.setPipeline(pointPipeline);
        pass.setBindGroup(2, viewportBindGroup);
        pass.setVertexBuffer(0, pointQuadBuffer);
        pass.setVertexBuffer(1, pointBuffer);
        pass.draw(6, sketch.buffers.pointData.length / 7);
      }

      if (labelPipeline && labelBindGroup && labelUniformBuffer && sketch.buffers.labelData.length > 0) {
        const labelBuffer = device.createBuffer({
          size: sketch.buffers.labelData.byteLength,
          usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST,
        });
        device.queue.writeBuffer(labelBuffer, 0, sketch.buffers.labelData);
        writeLabelUniform(
          device,
          labelUniformBuffer,
          [Math.max(1, this.canvas.clientWidth), Math.max(1, this.canvas.clientHeight)],
          sketch.frame,
        );
        pass.setPipeline(labelPipeline);
        pass.setBindGroup(1, labelBindGroup);
        pass.setVertexBuffer(0, labelBuffer);
        pass.draw(sketch.buffers.labelData.length / 10);
      }

      if (sketch.buffers.highlightLineData.length > 0) {
        const lineBuffer = device.createBuffer({
          size: sketch.buffers.highlightLineData.byteLength,
          usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST,
        });
        device.queue.writeBuffer(lineBuffer, 0, sketch.buffers.highlightLineData);
        pass.setPipeline(linePipeline);
        pass.setBindGroup(1, frameBindGroup);
        pass.setVertexBuffer(0, lineBuffer);
        pass.draw(sketch.buffers.highlightLineData.length / 6);
      }

      if (sketch.buffers.highlightPointData.length > 0) {
        const pointBuffer = device.createBuffer({
          size: sketch.buffers.highlightPointData.byteLength,
          usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST,
        });
        device.queue.writeBuffer(pointBuffer, 0, sketch.buffers.highlightPointData);
        pass.setPipeline(pointPipeline);
        pass.setBindGroup(1, frameBindGroup);
        pass.setBindGroup(2, viewportBindGroup);
        pass.setVertexBuffer(0, pointQuadBuffer);
        pass.setVertexBuffer(1, pointBuffer);
        pass.draw(6, sketch.buffers.highlightPointData.length / 7);
      }

      if (labelPipeline && labelBindGroup && labelUniformBuffer && sketch.buffers.highlightLabelData.length > 0) {
        const labelBuffer = device.createBuffer({
          size: sketch.buffers.highlightLabelData.byteLength,
          usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST,
        });
        device.queue.writeBuffer(labelBuffer, 0, sketch.buffers.highlightLabelData);
        writeLabelUniform(
          device,
          labelUniformBuffer,
          [Math.max(1, this.canvas.clientWidth), Math.max(1, this.canvas.clientHeight)],
          sketch.frame,
        );
        pass.setPipeline(labelPipeline);
        pass.setBindGroup(1, labelBindGroup);
        pass.setVertexBuffer(0, labelBuffer);
        pass.draw(sketch.buffers.highlightLabelData.length / 10);
      }
    }

    const preview = this.currentToolPreview();
    if (preview) {
      const frameData = new Float32Array(12);
      frameData.set(preview.frame.position, 0);
      frameData.set(preview.frame.xAxis, 4);
      frameData.set(preview.frame.yAxis, 8);
      device.queue.writeBuffer(frameBuffer, 0, frameData);
      pass.setBindGroup(1, frameBindGroup);

      if (preview.lineData.length > 0) {
        const lineBuffer = device.createBuffer({
          size: preview.lineData.byteLength,
          usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST,
        });
        device.queue.writeBuffer(lineBuffer, 0, preview.lineData);
        pass.setPipeline(linePipeline);
        pass.setVertexBuffer(0, lineBuffer);
        pass.draw(preview.lineData.length / 6);
      }

      if (preview.pointData.length > 0) {
        const pointBuffer = device.createBuffer({
          size: preview.pointData.byteLength,
          usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST,
        });
        device.queue.writeBuffer(pointBuffer, 0, preview.pointData);
        pass.setPipeline(pointPipeline);
        pass.setBindGroup(2, viewportBindGroup);
        pass.setVertexBuffer(0, pointQuadBuffer);
        pass.setVertexBuffer(1, pointBuffer);
        pass.draw(6, preview.pointData.length / 7);
      }
    }

    const constraintPreview = this.currentConstraintPreview();
    if (constraintPreview) {
      const frameData = new Float32Array(12);
      frameData.set(constraintPreview.frame.position, 0);
      frameData.set(constraintPreview.frame.xAxis, 4);
      frameData.set(constraintPreview.frame.yAxis, 8);
      device.queue.writeBuffer(frameBuffer, 0, frameData);
      pass.setBindGroup(1, frameBindGroup);
      if (constraintPreview.lineData.length > 0) {
        const lineBuffer = device.createBuffer({
          size: constraintPreview.lineData.byteLength,
          usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST,
        });
        device.queue.writeBuffer(lineBuffer, 0, constraintPreview.lineData);
        pass.setPipeline(linePipeline);
        pass.setVertexBuffer(0, lineBuffer);
        pass.draw(constraintPreview.lineData.length / 6);
      }
      if (labelPipeline && labelBindGroup && labelUniformBuffer && constraintPreview.labelData.length > 0) {
        const labelBuffer = device.createBuffer({
          size: constraintPreview.labelData.byteLength,
          usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST,
        });
        device.queue.writeBuffer(labelBuffer, 0, constraintPreview.labelData);
        writeLabelUniform(
          device,
          labelUniformBuffer,
          [Math.max(1, this.canvas.clientWidth), Math.max(1, this.canvas.clientHeight)],
          constraintPreview.frame,
        );
        pass.setPipeline(labelPipeline);
        pass.setBindGroup(1, labelBindGroup);
        pass.setVertexBuffer(0, labelBuffer);
        pass.draw(constraintPreview.labelData.length / 10);
      }
    }

    pass.end();
    device.queue.submit([encoder.finish()]);
    this.syncDimensionEditor();
  }

  private syncDimensionEditor(): void {
    const editing = this.state?.sketchUi.editingDimension;
    if (!editing) {
      this.dimensionEditInput?.remove();
      this.dimensionEditInput = null;
      this.dimensionEditKey = null;
      return;
    }
    const sketch = this.renderSketches.find((candidate) => candidate.sketchId === editing.sketchId);
    const anchor = sketch?.dimensionAnchors.get(editing.constraintIndex);
    if (!sketch || !anchor) {
      this.dimensionEditInput?.remove();
      this.dimensionEditInput = null;
      this.dimensionEditKey = null;
      return;
    }
    const screen = projectSketchLocalToScreen(anchor, sketch.frame, this.canvas, this.camera);
    if (!screen) {
      if (this.dimensionEditInput) this.dimensionEditInput.style.display = "none";
      return;
    }

    const key = `${editing.sketchId}:${editing.constraintIndex}`;
    let input = this.dimensionEditInput;
    if (!input || this.dimensionEditKey !== key) {
      input?.remove();
      input = document.createElement("input");
      let closing = false;
      input.type = "number";
      input.step = "any";
      input.className = "viewer-dimension-edit";
      input.addEventListener("pointerdown", (event) => event.stopPropagation());
      input.addEventListener("dblclick", (event) => event.stopPropagation());
      input.addEventListener("keydown", (event) => {
        event.stopPropagation();
        if (event.key === "Enter") {
          event.preventDefault();
          closing = true;
          void this.commitDimensionEdit(input!.value);
        } else if (event.key === "Escape") {
          event.preventDefault();
          closing = true;
          void this.cancelDimensionEdit();
        }
      });
      input.addEventListener("blur", () => {
        if (closing) return;
        requestAnimationFrame(() => {
          if (this.state?.sketchUi.editingDimension && this.dimensionEditInput === input) {
            input.focus();
            input.select();
          }
        });
      });
      this.root.appendChild(input);
      this.dimensionEditInput = input;
      this.dimensionEditKey = key;
      input.value = String(editing.value);
      requestAnimationFrame(() => {
        input!.focus();
        input!.select();
      });
    } else if ((this.root.getRootNode() instanceof ShadowRoot ? this.root.getRootNode().activeElement : document.activeElement) !== input) {
      input.value = String(editing.value);
    }

    input.style.display = "";
    input.style.left = `${screen.x}px`;
    input.style.top = `${screen.y}px`;
  }

  private async pickAtPointer(event?: PointerEvent): Promise<void> {
    if (!this.gpu || this.isPicking || this.renderSketches.length === 0) return;
    this.isPicking = true;
    try {
      const placementCursor = this.currentPlacementCursor();
      if (!event && placementCursor) {
        dispatchEditor(
          setConstraintPlacementCursor(
            placementCursor ? [placementCursor.sketchId, { X: placementCursor.x, Y: placementCursor.y }] : undefined,
          ),
        );
        this.applyViewerState(selectViewerState() as ViewerState);
      }
      const candidates = [...await this.pickAcrossSketches(), ...await this.pickFrameTargetsGpu()];
      if (candidates.length === 0) {
        if (this.state) {
          this.state = {
            ...this.state,
            hoveredTarget: null,
            highlightedTarget: null,
            dragTarget: null,
          };
          this.rebuildRenderData();
          this.queueRender();
        }
        return;
      }
      if (event) {
        const intent = event.shiftKey ? "toggle" : "replace";
        dispatchEditor(viewerPick(intent, selectionCandidatesFromJs(toPickRequest(candidates))));
        this.applyViewerState(selectViewerState() as ViewerState);
      } else {
        dispatchEditor(viewerHover(selectionCandidatesFromJs(toPickRequest(candidates))));
        this.applyViewerState(selectViewerState() as ViewerState);
      }
      this.rebuildRenderData();
      this.queueRender();
    } finally {
      this.isPicking = false;
    }
  }

  private async pickAcrossSketches(): Promise<PickCandidateHit[]> {
    if (!this.gpu) return [];
    const hits: PickCandidateHit[] = [];
    for (const sketch of this.renderSketches) {
      hits.push(...(await this.runPickPass(sketch)));
    }
    const deduped = new Map<string, PickCandidateHit>();
    for (const hit of hits) {
      const key = `${hit.pickId}:${hit.kind}:${hit.sketchId}`;
      const current = deduped.get(key);
      if (!current || hit.score < current.score) deduped.set(key, hit);
    }
    return [...deduped.values()];
  }

  private async pickFrameTargetsGpu(): Promise<PickCandidateHit[]> {
    if (!this.gpu || !this.state?.sketchUi.editMode) return [];
    const frames = this.state.sketchEditFrames.filter((frame) => this.isVisible(frame.id));
    if (frames.length === 0) return [];
    const pickables = this.model?.pickables ?? [];
    const { device, cameraBuffer, framePickPipeline, framePickBindGroupLayout, pickStateBuffer, pickResultBuffer } = this.gpu;
    const dprX = this.canvas.width / Math.max(this.canvas.clientWidth, 1);
    const dprY = this.canvas.height / Math.max(this.canvas.clientHeight, 1);
    device.queue.writeBuffer(
      pickStateBuffer,
      0,
      new Float32Array([this.canvas.width, this.canvas.height, this.pointer.x * dprX, this.pointer.y * dprY]),
    );

    const originEntries = new Float32Array(frames.length * 4);
    let oo = 0;
    const pickIdFor = (frameId: string) =>
      pickables.find((candidate) => candidate.case === "PickFrameOrigin" && candidate.frameId === frameId)?.pickId;
    for (let frameIndex = 0; frameIndex < frames.length; frameIndex++) {
      const frame = frames[frameIndex];
      const t = toSketchFrame(frame.transform);
      const originId = pickIdFor(frame.id);
      if (originId == null) continue;
      originEntries[oo++] = t.position[0];
      originEntries[oo++] = t.position[1];
      originEntries[oo++] = t.position[2];
      originEntries[oo++] = originId;
    }

    const originBuffer = device.createBuffer({
      size: Math.max(16, originEntries.byteLength),
      usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST,
    });
    device.queue.writeBuffer(originBuffer, 0, originEntries);
    const bindGroup = device.createBindGroup({
      layout: framePickBindGroupLayout,
      entries: [
        { binding: 0, resource: { buffer: cameraBuffer } },
        { binding: 1, resource: { buffer: pickStateBuffer } },
        { binding: 2, resource: { buffer: originBuffer } },
        { binding: 3, resource: { buffer: pickResultBuffer } },
      ],
    });
    const encoder = device.createCommandEncoder();
    const pass = encoder.beginComputePass();
    pass.setPipeline(framePickPipeline);
    pass.setBindGroup(0, bindGroup);
    pass.dispatchWorkgroups(1);
    pass.end();
    const readBuffer = device.createBuffer({
      size: PICK_SAMPLES * 16,
      usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ,
    });
    encoder.copyBufferToBuffer(pickResultBuffer, 0, readBuffer, 0, PICK_SAMPLES * 16);
    device.queue.submit([encoder.finish()]);
    await readBuffer.mapAsync(GPUMapMode.READ);
    const copy = readBuffer.getMappedRange().slice(0);
    readBuffer.unmap();
    readBuffer.destroy();
    originBuffer.destroy();
    const view = new DataView(copy);
    const hits = new Map<number, PickCandidateHit>();
    for (let i = 0; i < PICK_SAMPLES; i++) {
      const base = i * 16;
      const id = view.getUint32(base, true);
      const score = view.getFloat32(base + 8, true);
      if (id === NO_HIT_ID || !Number.isFinite(score)) continue;
      const current = hits.get(id);
      if (!current || score < current.score) {
        hits.set(id, {
          pickId: id,
          kind: "point",
          score,
          sketchId: "",
        });
      }
    }
    return [...hits.values()];
  }

  private async runPickPass(sketch: RenderSketch): Promise<PickCandidateHit[]> {
    if (!this.gpu) return [];
    const { device, cameraBuffer, frameBuffer, pickPipeline, pickBindGroupLayout, pickStateBuffer, pickResultBuffer } = this.gpu;

    const frameData = new Float32Array(12);
    frameData.set(sketch.frame.position, 0);
    frameData.set(sketch.frame.xAxis, 4);
    frameData.set(sketch.frame.yAxis, 8);
    device.queue.writeBuffer(frameBuffer, 0, frameData);
    const dprX = this.canvas.width / Math.max(this.canvas.clientWidth, 1);
    const dprY = this.canvas.height / Math.max(this.canvas.clientHeight, 1);
    device.queue.writeBuffer(
      pickStateBuffer,
      0,
      new Float32Array([this.canvas.width, this.canvas.height, this.pointer.x * dprX, this.pointer.y * dprY]),
    );

    const pointBuffer = device.createBuffer({
      size: Math.max(16, sketch.buffers.pickPoints.byteLength),
      usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST,
    });
    device.queue.writeBuffer(pointBuffer, 0, sketch.buffers.pickPoints);

    const lineBuffer = device.createBuffer({
      size: Math.max(16, sketch.buffers.pickLines.byteLength),
      usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST,
    });
    device.queue.writeBuffer(lineBuffer, 0, sketch.buffers.pickLines);

    const circleBuffer = device.createBuffer({
      size: Math.max(16, sketch.buffers.pickCircles.byteLength),
      usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST,
    });
    device.queue.writeBuffer(circleBuffer, 0, sketch.buffers.pickCircles);

    const loopBuffer = device.createBuffer({
      size: Math.max(16, sketch.buffers.pickLoops.byteLength),
      usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST,
    });
    device.queue.writeBuffer(loopBuffer, 0, sketch.buffers.pickLoops);

    const labelBuffer = device.createBuffer({
      size: Math.max(16, sketch.buffers.pickLabels.byteLength),
      usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST,
    });
    device.queue.writeBuffer(labelBuffer, 0, sketch.buffers.pickLabels);

    const bindGroup = device.createBindGroup({
      layout: pickBindGroupLayout,
      entries: [
        { binding: 0, resource: { buffer: cameraBuffer } },
        { binding: 1, resource: { buffer: frameBuffer } },
        { binding: 2, resource: { buffer: pickStateBuffer } },
        { binding: 3, resource: { buffer: pointBuffer } },
        { binding: 4, resource: { buffer: lineBuffer } },
        { binding: 5, resource: { buffer: circleBuffer } },
        { binding: 6, resource: { buffer: loopBuffer } },
        { binding: 7, resource: { buffer: labelBuffer } },
        { binding: 8, resource: { buffer: pickResultBuffer } },
      ],
    });

    const encoder = device.createCommandEncoder();
    const pass = encoder.beginComputePass();
    pass.setPipeline(pickPipeline);
    pass.setBindGroup(0, bindGroup);
    pass.dispatchWorkgroups(1);
    pass.end();
    const pickReadBuffer = device.createBuffer({
      size: PICK_SAMPLES * 16,
      usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ,
    });
    encoder.copyBufferToBuffer(pickResultBuffer, 0, pickReadBuffer, 0, PICK_SAMPLES * 16);
    device.queue.submit([encoder.finish()]);

    await pickReadBuffer.mapAsync(GPUMapMode.READ);
    const copy = pickReadBuffer.getMappedRange().slice(0);
    pickReadBuffer.unmap();
    pickReadBuffer.destroy();
    const view = new DataView(copy);

    const hits = new Map<number, PickCandidateHit>();
    for (let i = 0; i < PICK_SAMPLES; i++) {
      const base = i * 16;
      const id = view.getUint32(base, true);
      const kind = view.getUint32(base + 4, true);
      const score = view.getFloat32(base + 8, true);
      if (id === NO_HIT_ID || !Number.isFinite(score)) continue;
      const resolved = pickKind(kind);
      if (!resolved) continue;
      const current = hits.get(id);
      if (!current || score < current.score) {
        hits.set(id, { pickId: id, kind: resolved, score, sketchId: sketch.sketchId });
      }
    }
    return [...hits.values()];
  }

  private currentPlacementCursor(): { sketchId: string; x: number; y: number } | null {
    const placement = this.state?.sketchUi.constraintPlacementMode;
    const authored = this.authoringSketch();
    if (!placement || !authored) return null;
    const local = pointerToSketchLocal(this.pointer, this.canvas, this.camera, authored.frame);
    if (!local) return null;
    return { sketchId: authored.model.id, x: local[0], y: local[1] };
  }

  private rebuildFieldPipeline(): void {
    this.fieldPipeline?.slotBuffer.destroy();
    this.fieldPipeline?.surfaceBuffer.destroy();
    this.fieldPipeline = null;
    if (!this.gpu || !this.model?.fieldWgsl) return;
    const { device, format, cameraLayout, fieldSlotBindGroupLayout, fieldSurfaceBindGroupLayout } = this.gpu;
    const pipeline = createIsosurfacePipeline(
      device,
      format,
      this.model.fieldWgsl,
      cameraLayout,
      fieldSlotBindGroupLayout,
      fieldSurfaceBindGroupLayout,
    );
    const slotCapacity = Math.max(this.model.numSlots, 1);
    const slotBuffer = device.createBuffer({
      size: Math.max(16, slotCapacity * 4),
      usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST,
    });
    const slotBindGroup = device.createBindGroup({
      layout: fieldSlotBindGroupLayout,
      entries: [{ binding: 0, resource: { buffer: slotBuffer } }],
    });
    const surfaceBuffer = device.createBuffer({
      size: Math.max(32, Math.max(this.model.fieldSurfaceActionIds.length, 1) * 32),
      usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST,
    });
    const surfaceBindGroup = device.createBindGroup({
      layout: fieldSurfaceBindGroupLayout,
      entries: [{ binding: 0, resource: { buffer: surfaceBuffer } }],
    });
    this.fieldPipeline = { pipeline, slotBuffer, slotCapacity, slotBindGroup, surfaceBuffer, surfaceBindGroup };
    if (this.state && this.state.params.length <= slotCapacity) {
      this.updateFieldBuffers();
    }
  }

  private rebuildFieldSlicePipeline(): void {
    this.fieldSlicePipeline?.vertexBuffer.destroy();
    this.fieldSlicePipeline = null;
    if (!this.gpu || !this.model?.fieldSliceWgsl) return;
    const { device, format, cameraLayout, fieldSlotBindGroupLayout } = this.gpu;
    const pipeline = createFieldSlicePipeline(
      device,
      format,
      this.model.fieldSliceWgsl,
      cameraLayout,
      fieldSlotBindGroupLayout,
    );
    const vertexData = buildFieldSliceVertexData(this.state?.fieldSlices ?? [], this.camera, this.canvas);
    const vertexBuffer = device.createBuffer({
      size: Math.max(16, vertexData.byteLength),
      usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST,
    });
    if (vertexData.byteLength > 0) {
      device.queue.writeBuffer(vertexBuffer, 0, vertexData);
    }
    this.fieldSlicePipeline = {
      pipeline,
      vertexBuffer,
      vertexCount: vertexData.length / 7,
    };
  }

  private updateFieldBuffers(): void {
    if (!this.gpu || !this.model || !this.state || !this.fieldPipeline) return;
    const { device } = this.gpu;
    const effectiveParams = new Float32Array(this.state?.params ?? []);
    if (effectiveParams.length > this.fieldPipeline.slotCapacity) {
      this.rebuildFieldPipeline();
      return;
    }
    const { slotBuffer, surfaceBuffer } = this.fieldPipeline;
    device.queue.writeBuffer(slotBuffer, 0, effectiveParams);

    const surfaceState = new Float32Array(Math.max(this.model.fieldSurfaceActionIds.length, 1) * 8);
    this.model.fieldSurfaceActionIds.forEach((actionId, index) => {
      const base = index * 8;
      const entry = this.state!.display[actionId] as { display?: { enabled?: boolean; color?: number[]; opacity?: number; isoValue?: number } } | undefined;
      const raw = entry?.display;
      const color = raw?.color ?? [0.522, 0.682, 0.784];
      const opacity = raw?.opacity ?? 0.9;
      const isoValue = raw?.isoValue ?? 0.0;
      const enabled = (this.state!.visible[actionId] ?? true) && (raw?.enabled ?? false);
      surfaceState[base + 0] = color[0] ?? 0.522;
      surfaceState[base + 1] = color[1] ?? 0.682;
      surfaceState[base + 2] = color[2] ?? 0.784;
      surfaceState[base + 3] = opacity;
      surfaceState[base + 4] = isoValue;
      surfaceState[base + 5] = enabled ? 1 : 0;
      surfaceState[base + 6] = 0;
      surfaceState[base + 7] = 0;
    });
    device.queue.writeBuffer(surfaceBuffer, 0, surfaceState);
  }

  private updateFieldSliceBuffers(): void {
    if (!this.gpu || !this.fieldSlicePipeline) return;
    const vertexData = buildFieldSliceVertexData(this.state?.fieldSlices ?? [], this.camera, this.canvas);
    this.fieldSlicePipeline.vertexBuffer.destroy();
    const vertexBuffer = this.gpu.device.createBuffer({
      size: Math.max(16, vertexData.byteLength),
      usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST,
    });
    if (vertexData.byteLength > 0) {
      this.gpu.device.queue.writeBuffer(vertexBuffer, 0, vertexData);
    }
    this.fieldSlicePipeline = {
      ...this.fieldSlicePipeline,
      vertexBuffer,
      vertexCount: vertexData.length / 7,
    };
  }

  private async initGpu(): Promise<GpuContext> {
    if (!navigator.gpu) throw new Error("WebGPU unavailable");
    const adapter = await navigator.gpu.requestAdapter();
    if (!adapter) throw new Error("No GPU adapter");
    const device = await adapter.requestDevice();
    const context = this.canvas.getContext("webgpu");
    if (!context) throw new Error("Missing WebGPU canvas context");
    const format = navigator.gpu.getPreferredCanvasFormat();
    context.configure({ device, format, alphaMode: "opaque" });

    const cameraLayout = device.createBindGroupLayout({
      entries: [{ binding: 0, visibility: GPUShaderStage.VERTEX | GPUShaderStage.FRAGMENT | GPUShaderStage.COMPUTE, buffer: { type: "uniform" } }],
    });
    const sketchFrameLayout = device.createBindGroupLayout({
      entries: [{ binding: 0, visibility: GPUShaderStage.VERTEX | GPUShaderStage.COMPUTE, buffer: { type: "uniform" } }],
    });
    const viewportLayout = device.createBindGroupLayout({
      entries: [{ binding: 0, visibility: GPUShaderStage.VERTEX, buffer: { type: "uniform" } }],
    });

    const cameraBuffer = device.createBuffer({ size: 64, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST });
    const frameBuffer = device.createBuffer({ size: 48, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST });
    const viewportBuffer = device.createBuffer({ size: 16, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST });
    const cameraBindGroup = device.createBindGroup({ layout: cameraLayout, entries: [{ binding: 0, resource: { buffer: cameraBuffer } }] });
    const frameBindGroup = device.createBindGroup({ layout: sketchFrameLayout, entries: [{ binding: 0, resource: { buffer: frameBuffer } }] });
    const viewportBindGroup = device.createBindGroup({ layout: viewportLayout, entries: [{ binding: 0, resource: { buffer: viewportBuffer } }] });

    const gizmoModule = device.createShaderModule({ code: GIZMO_SHADER });
    const gizmoPipeline = device.createRenderPipeline({
      layout: device.createPipelineLayout({ bindGroupLayouts: [cameraLayout, viewportLayout] }),
      vertex: {
        module: gizmoModule,
        entryPoint: "vs_main",
        buffers: [{
          arrayStride: 48,
          attributes: [
            { shaderLocation: 0, offset: 0, format: "float32x3" },
            { shaderLocation: 1, offset: 12, format: "float32x3" },
            { shaderLocation: 2, offset: 24, format: "float32" },
            { shaderLocation: 3, offset: 28, format: "float32" },
            { shaderLocation: 4, offset: 32, format: "float32x4" },
          ],
        }],
      },
      fragment: {
        module: gizmoModule,
        entryPoint: "fs_main",
        targets: [{
          format,
          blend: {
            color: { srcFactor: "src-alpha", dstFactor: "one-minus-src-alpha", operation: "add" },
            alpha: { srcFactor: "one", dstFactor: "one-minus-src-alpha", operation: "add" },
          },
        }],
      },
      primitive: { topology: "line-list" },
    });

    const lineModule = device.createShaderModule({ code: LINE_SHADER });
    const triPipeline = device.createRenderPipeline({
      layout: device.createPipelineLayout({ bindGroupLayouts: [cameraLayout, sketchFrameLayout] }),
      vertex: {
        module: lineModule,
        entryPoint: "vs_main",
        buffers: [{
          arrayStride: 24,
          attributes: [
            { shaderLocation: 0, offset: 0, format: "float32x2" },
            { shaderLocation: 1, offset: 8, format: "float32x4" },
          ],
        }],
      },
      fragment: {
        module: lineModule,
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
    const linePipeline = device.createRenderPipeline({
      layout: device.createPipelineLayout({ bindGroupLayouts: [cameraLayout, sketchFrameLayout] }),
      vertex: {
        module: lineModule,
        entryPoint: "vs_main",
        buffers: [{
          arrayStride: 24,
          attributes: [
            { shaderLocation: 0, offset: 0, format: "float32x2" },
            { shaderLocation: 1, offset: 8, format: "float32x4" },
          ],
        }],
      },
      fragment: {
        module: lineModule,
        entryPoint: "fs_main",
        targets: [{
          format,
          blend: {
            color: { srcFactor: "src-alpha", dstFactor: "one-minus-src-alpha", operation: "add" },
            alpha: { srcFactor: "one", dstFactor: "one-minus-src-alpha", operation: "add" },
          },
        }],
      },
      primitive: { topology: "line-list" },
    });

    const pointModule = device.createShaderModule({ code: POINT_SHADER });
    const pointPipeline = device.createRenderPipeline({
      layout: device.createPipelineLayout({ bindGroupLayouts: [cameraLayout, sketchFrameLayout, viewportLayout] }),
      vertex: {
        module: pointModule,
        entryPoint: "vs_main",
        buffers: [
          {
            arrayStride: 8,
            stepMode: "vertex",
            attributes: [{ shaderLocation: 0, offset: 0, format: "float32x2" }],
          },
          {
            arrayStride: 28,
            stepMode: "instance",
            attributes: [
              { shaderLocation: 1, offset: 0, format: "float32x2" },
              { shaderLocation: 2, offset: 8, format: "float32" },
              { shaderLocation: 3, offset: 12, format: "float32x4" },
            ],
          },
        ],
      },
      fragment: {
        module: pointModule,
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

    const pointQuadBuffer = device.createBuffer({
      size: 6 * 2 * 4,
      usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST,
    });
    device.queue.writeBuffer(pointQuadBuffer, 0, new Float32Array([
      -1, -1,  1, -1,  1,  1,
      -1, -1,  1,  1, -1,  1,
    ]));

    let labelPipeline: GPURenderPipeline | null = null;
    let labelBindGroup: GPUBindGroup | null = null;
    let labelUniformBuffer: GPUBuffer | null = null;
    try {
      const atlas = await loadMsdfAtlas(device, "/fonts/dekal.png");
      const labelBundle = createMsdfLabelPipeline(device, format, atlas, cameraLayout);
      labelPipeline = labelBundle.pipeline;
      labelBindGroup = labelBundle.bindGroup;
      labelUniformBuffer = labelBundle.uniformBuffer;
    } catch {
      labelPipeline = null;
      labelBindGroup = null;
      labelUniformBuffer = null;
    }

    const fieldSlotBindGroupLayout = device.createBindGroupLayout({
      entries: [
        { binding: 0, visibility: GPUShaderStage.FRAGMENT, buffer: { type: "read-only-storage" } },
      ],
    });
    const fieldSurfaceBindGroupLayout = device.createBindGroupLayout({
      entries: [
        { binding: 0, visibility: GPUShaderStage.FRAGMENT, buffer: { type: "read-only-storage" } },
      ],
    });

    const pickBindGroupLayout = device.createBindGroupLayout({
      entries: [
        { binding: 0, visibility: GPUShaderStage.COMPUTE, buffer: { type: "uniform" } },
        { binding: 1, visibility: GPUShaderStage.COMPUTE, buffer: { type: "uniform" } },
        { binding: 2, visibility: GPUShaderStage.COMPUTE, buffer: { type: "uniform" } },
        { binding: 3, visibility: GPUShaderStage.COMPUTE, buffer: { type: "read-only-storage" } },
        { binding: 4, visibility: GPUShaderStage.COMPUTE, buffer: { type: "read-only-storage" } },
        { binding: 5, visibility: GPUShaderStage.COMPUTE, buffer: { type: "read-only-storage" } },
        { binding: 6, visibility: GPUShaderStage.COMPUTE, buffer: { type: "read-only-storage" } },
        { binding: 7, visibility: GPUShaderStage.COMPUTE, buffer: { type: "read-only-storage" } },
        { binding: 8, visibility: GPUShaderStage.COMPUTE, buffer: { type: "storage" } },
      ],
    });
    const pickPipeline = device.createComputePipeline({
      layout: device.createPipelineLayout({ bindGroupLayouts: [pickBindGroupLayout] }),
      compute: { module: device.createShaderModule({ code: PICK_SHADER }), entryPoint: "cs_main" },
    });
    const framePickBindGroupLayout = device.createBindGroupLayout({
      entries: [
        { binding: 0, visibility: GPUShaderStage.COMPUTE, buffer: { type: "uniform" } },
        { binding: 1, visibility: GPUShaderStage.COMPUTE, buffer: { type: "uniform" } },
        { binding: 2, visibility: GPUShaderStage.COMPUTE, buffer: { type: "read-only-storage" } },
        { binding: 3, visibility: GPUShaderStage.COMPUTE, buffer: { type: "storage" } },
      ],
    });
    const framePickPipeline = device.createComputePipeline({
      layout: device.createPipelineLayout({ bindGroupLayouts: [framePickBindGroupLayout] }),
      compute: { module: device.createShaderModule({ code: FRAME_PICK_SHADER }), entryPoint: "cs_main" },
    });
    const pickStateBuffer = device.createBuffer({ size: 16, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST });
    const pickResultBuffer = device.createBuffer({ size: PICK_SAMPLES * 16, usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC });
    return {
      device,
      context,
      format,
      cameraLayout,
      cameraBuffer,
      cameraBindGroup,
      gizmoPipeline,
      triPipeline,
      linePipeline,
      pointPipeline,
      sketchFrameLayout,
      frameBuffer,
      frameBindGroup,
      viewportBuffer,
      viewportBindGroup,
      pointQuadBuffer,
      labelPipeline,
      labelBindGroup,
      labelUniformBuffer,
      fieldSlotBindGroupLayout,
      fieldSurfaceBindGroupLayout,
      pickPipeline,
      pickBindGroupLayout,
      framePickPipeline,
      framePickBindGroupLayout,
      pickStateBuffer,
      pickResultBuffer,
    };
  }

}

function buildSketchBuffers(
  viewerSketch: ViewerSketch,
  pickables: Pickable[],
  slotLookup: Map<string, number>,
  params: number[],
  frames: Array<{ id: string; transform: JsonRigidTransform }>,
  hoveredTarget: SelectionTarget | null,
  selectedTargets: SelectionTarget[],
  fontMetrics: FontMetrics | null,
  drag?: DragState | null,
  sketchFrame?: SketchFrame,
  stateLabelPosition?: (constraintIndex: number) => Vec2 | null,
  showDimensions?: boolean,
  showGrid?: boolean,
): { buffers: RenderBuffers; loops: ResolvedLoopGeometry[]; dimensionAnchors: Map<number, Vec2> } {
  const entityMap = new Map(viewerSketch.sketch.entities.map((entity) => [entity.id, entity]));
  const pointMap = new Map<string, Vec2>();
  const framePointMap =
    sketchFrame
      ? new Map<string, Vec2>(
          frames.map((frame) => [frame.id, projectWorldToSketchLocal(toSketchFrame(frame.transform).position, sketchFrame)] as const),
        )
      : new Map<string, Vec2>();
  const triVertices: LineVertex[] = [];
  const lineVertices: LineVertex[] = [];
  const highlightLineVertices: LineVertex[] = [];
  const pointInstances: PointInstance[] = [];
  const highlightPointInstances: PointInstance[] = [];
  const pickPoints: PickPoint[] = [];
  const pickSegments: PickSegment[] = [];
  const pickCircles: PickCircle[] = [];
  const pickLoopTriangles: PickLoopTriangle[] = [];
  const pickMap = buildPickIndex(pickables, viewerSketch.id);
  const labels: ConstraintLabel[] = [];
  const highlightLabels: ConstraintLabel[] = [];
  const dimensionAnchors = new Map<number, Vec2>();
  const isHovered = (kind: PickKind, id: string | number): boolean =>
    selectionMatches(hoveredTarget, viewerSketch.id, kind, id);
  const isSelected = (kind: PickKind, id: string | number): boolean =>
    selectionMatchesAny(selectedTargets, viewerSketch.id, kind, id);
  const resolveValue = (path: string, fallback: number) => slotValue(params, slotLookup, viewerSketch.id, path, fallback);
  const resolveLabelAnchor = (index: number, fallback: Vec2): Vec2 => {
    if (drag?.kind === "label" && drag.sketchId === viewerSketch.id && drag.constraintIndex === index) return drag.target;
    const live = stateLabelPosition?.(index);
    if (live) return live;
    return fallback;
  };

  for (const entity of viewerSketch.sketch.entities) {
    if (entity.case === "REPoint") {
      const resolved: Vec2 = [
        resolveValue(`sketch.entity.${entity.id}.x`, entity.x),
        resolveValue(`sketch.entity.${entity.id}.y`, entity.y),
      ];
      pointMap.set(entity.id, resolved);
    }
  }

  const resolvedLoops = viewerSketch.loops
    .map((loop) => resolveLoopGeometry(loop, entityMap, pointMap, resolveValue))
    .filter((loop): loop is ResolvedLoopGeometry => loop !== null);
  for (const loop of resolvedLoops) {
    loop.pickId = pickMap.get(`loop:${loop.id}`) ?? null;
    const activeLoop = isHovered("loop", loop.id) || isSelected("loop", loop.id);
    pushLoopFill(triVertices, pickLoopTriangles, loop.boundary, activeLoop ? ACCENT_SOFT : LOOP_FILL, loop.pickId);
  }

  const [minPoint, maxPoint] = computeBounds(viewerSketch.sketch, pointMap, resolveValue);
  if (showGrid) {
    pushGrid(lineVertices, minPoint, maxPoint);
  }

  for (const entity of viewerSketch.sketch.entities) {
    if (entity.case === "REPoint") {
      const p = pointMap.get(entity.id);
      if (!p) continue;
      const activePoint = isHovered("point", entity.id) || isSelected("point", entity.id);
      pointInstances.push({ x: p[0], y: p[1], radiusPx: 5.0, color: SKETCH_POINT });
      if (activePoint) {
        highlightPointInstances.push({ x: p[0], y: p[1], radiusPx: 7.5, color: ACCENT });
      }
      const pickId = pickMap.get(`point:${entity.id}`);
      if (pickId != null) pickPoints.push({ x: p[0], y: p[1], radiusPx: 10, pickId });
      continue;
    }

    if (entity.case === "RELine") {
      const a = pointMap.get(entity.startId);
      const b = pointMap.get(entity.endId);
      if (!a || !b) continue;
      const pickId = pickMap.get(`line:${entity.id}`);
      const activeLine = isHovered("line", entity.id) || isSelected("line", entity.id);
      lineVertices.push({ x: a[0], y: a[1], color: SKETCH_LINE }, { x: b[0], y: b[1], color: SKETCH_LINE });
      if (activeLine) {
        highlightLineVertices.push({ x: a[0], y: a[1], color: ACCENT }, { x: b[0], y: b[1], color: ACCENT });
      }
      if (pickId != null) pickSegments.push({ a, b, strokePx: 8, pickId, kind: 2 });
      continue;
    }

    if (entity.case === "RECircle") {
      const c = pointMap.get(entity.center);
      if (!c) continue;
      const radius = resolveValue(`sketch.entity.${entity.id}.radius`, entity.radius);
      const pickId = pickMap.get(`circle:${entity.id}`);
      const activeCircle = isHovered("circle", entity.id) || isSelected("circle", entity.id);
      pushCircle(lineVertices, c, radius, SKETCH_LINE);
      if (activeCircle) pushCircle(highlightLineVertices, c, radius, ACCENT);
      if (pickId != null) pickCircles.push({ center: c, radius, strokePx: 8, pickId });
      continue;
    }

    if (entity.case === "REArc" && entity.data.case === "ArcCenter") {
      const s = pointMap.get(entity.startId);
      const e = pointMap.get(entity.endId);
      const c = pointMap.get(entity.data.center);
      if (!s || !e || !c) continue;
      const pickId = pickMap.get(`arc:${entity.id}`);
      const activeArc = isHovered("arc", entity.id) || isSelected("arc", entity.id);
      pushArc(lineVertices, pickSegments, s, e, c, entity.data.clockwise, SKETCH_LINE, pickId);
      if (activeArc) pushArc(highlightLineVertices, undefined, s, e, c, entity.data.clockwise, ACCENT, null);
    }
  }

  viewerSketch.sketch.constraints.forEach((constraint, index) => {
    pushConstraintGeometry(
      lineVertices,
      highlightLineVertices,
      labels,
      highlightLabels,
      dimensionAnchors,
      pointMap,
      entityMap,
      framePointMap,
      resolveValue,
      resolveLabelAnchor,
      constraint,
      index,
      pickMap.get(`dimension:${index}`) ?? null,
      hoveredTarget,
      selectedTargets,
      viewerSketch.id,
      showDimensions ?? false,
    );
  });

  return {
    buffers: {
      triData: flattenLines(triVertices),
      lineData: flattenLines(lineVertices),
      pointData: flattenPoints(pointInstances),
      highlightLineData: flattenLines(highlightLineVertices),
      highlightPointData: flattenPoints(highlightPointInstances),
      pickPoints: flattenPickPoints(pickPoints),
      pickLines: flattenPickSegments(pickSegments),
      pickCircles: flattenPickCircles(pickCircles),
      pickLoops: flattenPickLoopTriangles(pickLoopTriangles),
      pickLabels: fontMetrics ? flattenPickLabelRects(buildLabelPickRects(labels, fontMetrics)) : new Float32Array(),
      labelData: fontMetrics ? buildLabelVertices(labels, fontMetrics) : new Float32Array(),
      highlightLabelData: fontMetrics ? buildLabelVertices(highlightLabels, fontMetrics) : new Float32Array(),
    },
    loops: resolvedLoops,
    dimensionAnchors,
  };
}

function pushConstraintGeometry(
  lines: LineVertex[],
  highlightLines: LineVertex[],
  labels: ConstraintLabel[],
  highlightLabels: ConstraintLabel[],
  dimensionAnchors: Map<number, Vec2>,
  pointMap: Map<string, Vec2>,
  entityMap: Map<string, RenderEntity>,
  framePointMap: Map<string, Vec2>,
  resolveValue: (path: string, fallback: number) => number,
  resolveLabelAnchor: (index: number, fallback: Vec2) => Vec2,
  constraint: ActionSketch["constraints"][number],
  constraintIndex: number,
  pickId: number | null,
  hoveredTarget: SelectionTarget | null,
  selectedTargets: SelectionTarget[],
  sketchId: string,
  showDimensions: boolean,
): void {
  const activeDimension =
    selectionMatches(hoveredTarget, sketchId, "dimension", constraintIndex) ||
    selectionMatchesAny(selectedTargets, sketchId, "dimension", constraintIndex);
  switch (constraint.case) {
    case "Fixed": {
      const p = pointMap.get(constraint.point);
      if (!p) return;
      const dx: Vec2 = [0.75, 0];
      const dy: Vec2 = [0, 0.75];
      lines.push({ x: p[0] - dx[0], y: p[1] - dy[1], color: FIXED_COLOR }, { x: p[0] + dx[0], y: p[1] + dy[1], color: FIXED_COLOR });
      lines.push({ x: p[0] - dx[0], y: p[1] + dy[1], color: FIXED_COLOR }, { x: p[0] + dx[0], y: p[1] - dy[1], color: FIXED_COLOR });
      return;
    }
    case "Horizontal":
    case "Vertical": {
      const a = pointMap.get(constraint.a);
      const b = pointMap.get(constraint.b);
      if (!a || !b) return;
      const mid = scale2(add2(a, b), 0.5);
      if (constraint.case === "Horizontal") {
        lines.push({ x: mid[0] - 0.8, y: mid[1], color: DIM_COLOR }, { x: mid[0] + 0.8, y: mid[1], color: DIM_COLOR });
        if (activeDimension) {
          highlightLines.push({ x: mid[0] - 0.8, y: mid[1], color: DIM_HOVER }, { x: mid[0] + 0.8, y: mid[1], color: DIM_HOVER });
        }
      } else {
        lines.push({ x: mid[0], y: mid[1] - 0.8, color: DIM_COLOR }, { x: mid[0], y: mid[1] + 0.8, color: DIM_COLOR });
        if (activeDimension) {
          highlightLines.push({ x: mid[0], y: mid[1] - 0.8, color: DIM_HOVER }, { x: mid[0], y: mid[1] + 0.8, color: DIM_HOVER });
        }
      }
      return;
    }
    case "Distance": {
      if (!showDimensions) return;
      const a = pointMap.get(constraint.a);
      const b = pointMap.get(constraint.b);
      if (!a || !b) return;
      const anchor = pushPointDistanceGeometry(lines, highlightLines, dimensionAnchors, constraintIndex, a, b, resolveLabelAnchor, activeDimension);
      labels.push({
        text: formatNumber(resolveValue(`sketch.constraint.${constraintIndex}.distance`, constraint.distance)),
        anchor,
        pickId,
        hovered: false,
      });
      if (activeDimension) highlightLabels.push({ text: formatNumber(resolveValue(`sketch.constraint.${constraintIndex}.distance`, constraint.distance)), anchor, pickId, hovered: true });
      return;
    }
    case "FrameDistance": {
      if (!showDimensions || constraint.part !== "origin") return;
      const a = pointMap.get(constraint.point);
      const b = framePointMap.get(constraint.frame);
      if (!a || !b) return;
      const anchor = pushPointDistanceGeometry(lines, highlightLines, dimensionAnchors, constraintIndex, a, b, resolveLabelAnchor, activeDimension);
      labels.push({
        text: formatNumber(resolveValue(`sketch.constraint.${constraintIndex}.distance`, constraint.distance)),
        anchor,
        pickId,
        hovered: false,
      });
      if (activeDimension) highlightLabels.push({ text: formatNumber(resolveValue(`sketch.constraint.${constraintIndex}.distance`, constraint.distance)), anchor, pickId, hovered: true });
      return;
    }
    case "LineDistance": {
      if (!showDimensions) return;
      const aStart = pointMap.get(constraint.aStart);
      const aEnd = pointMap.get(constraint.aEnd);
      const bStart = pointMap.get(constraint.bStart);
      const bEnd = pointMap.get(constraint.bEnd);
      if (!aStart || !aEnd || !bStart || !bEnd) return;
      const midA = scale2(add2(aStart, aEnd), 0.5);
      const midB = scale2(add2(bStart, bEnd), 0.5);
      const fallbackAnchor = scale2(add2(midA, midB), 0.5);
      const anchor = resolveLabelAnchor(constraintIndex, fallbackAnchor);
      dimensionAnchors.set(constraintIndex, anchor);
      const pa = projectPointToInfiniteLine(anchor, aStart, aEnd);
      const pb = projectPointToInfiniteLine(anchor, bStart, bEnd);
      const mid = scale2(add2(pa, pb), 0.5);
      lines.push({ x: pa[0], y: pa[1], color: DIM_COLOR }, { x: pb[0], y: pb[1], color: DIM_COLOR });
      lines.push({ x: mid[0], y: mid[1], color: DIM_COLOR }, { x: anchor[0], y: anchor[1], color: DIM_COLOR });
      if (activeDimension) {
        highlightLines.push({ x: pa[0], y: pa[1], color: DIM_HOVER }, { x: pb[0], y: pb[1], color: DIM_HOVER });
        highlightLines.push({ x: mid[0], y: mid[1], color: DIM_HOVER }, { x: anchor[0], y: anchor[1], color: DIM_HOVER });
      }
      labels.push({
        text: formatNumber(resolveValue(`sketch.constraint.${constraintIndex}.distance`, constraint.distance)),
        anchor,
        pickId,
        hovered: false,
      });
      if (activeDimension) {
        highlightLabels.push({
          text: formatNumber(resolveValue(`sketch.constraint.${constraintIndex}.distance`, constraint.distance)),
          anchor,
          pickId,
          hovered: true,
        });
      }
      return;
    }
    case "FrameLineDistance": {
      if (!showDimensions || constraint.part !== "origin") return;
      const aStart = pointMap.get(constraint.aStart);
      const aEnd = pointMap.get(constraint.aEnd);
      const framePoint = framePointMap.get(constraint.frame);
      if (!aStart || !aEnd || !framePoint) return;
      const projected = projectPointToInfiniteLine(framePoint, aStart, aEnd);
      const anchor = pushPointDistanceGeometry(lines, highlightLines, dimensionAnchors, constraintIndex, projected, framePoint, resolveLabelAnchor, activeDimension);
      labels.push({
        text: formatNumber(resolveValue(`sketch.constraint.${constraintIndex}.distance`, constraint.distance)),
        anchor,
        pickId,
        hovered: false,
      });
      if (activeDimension) {
        highlightLabels.push({
          text: formatNumber(resolveValue(`sketch.constraint.${constraintIndex}.distance`, constraint.distance)),
          anchor,
          pickId,
          hovered: true,
        });
      }
      return;
    }
    case "CircleDiameter": {
      if (!showDimensions) return;
      const center = pointMap.get(constraint.center);
      const circle = entityMap.get(constraint.circle);
      if (!center || !circle) return;
      let radius: number | null = null;
      if (circle.case === "RECircle") {
        radius = resolveValue(`sketch.entity.${circle.id}.radius`, circle.radius);
      } else if (circle.case === "REArc" && circle.data.case === "ArcCenter") {
        const start = pointMap.get(circle.startId);
        if (start) radius = len2(sub2(start, center));
      }
      if (radius == null) return;
      const fallbackAnchor: Vec2 = [center[0], center[1] + radius + 2.4];
      const anchor = resolveLabelAnchor(constraintIndex, fallbackAnchor);
      dimensionAnchors.set(constraintIndex, anchor);
      const axis = norm2(sub2(anchor, center));
      const dir = len2(axis) < 1e-6 ? ([0, 1] as Vec2) : axis;
      lines.push(
        { x: center[0] - dir[0] * radius, y: center[1] - dir[1] * radius, color: DIM_COLOR },
        { x: center[0] + dir[0] * radius, y: center[1] + dir[1] * radius, color: DIM_COLOR },
      );
      if (activeDimension) {
        highlightLines.push(
          { x: center[0] - dir[0] * radius, y: center[1] - dir[1] * radius, color: DIM_HOVER },
          { x: center[0] + dir[0] * radius, y: center[1] + dir[1] * radius, color: DIM_HOVER },
        );
      }
      labels.push({
        text: `⌀ ${formatNumber(resolveValue(`sketch.constraint.${constraintIndex}.diameter`, constraint.diameter))}`,
        anchor,
        pickId,
        hovered: false,
      });
      if (activeDimension) highlightLabels.push({ text: `⌀ ${formatNumber(resolveValue(`sketch.constraint.${constraintIndex}.diameter`, constraint.diameter))}`, anchor, pickId, hovered: true });
      return;
    }
    case "Angle": {
      if (!showDimensions) return;
      const a0 = pointMap.get(constraint.aStart);
      const a1 = pointMap.get(constraint.aEnd);
      const b0 = pointMap.get(constraint.bStart);
      const b1 = pointMap.get(constraint.bEnd);
      if (!a0 || !a1 || !b0 || !b1) return;
      const resolved = resolveAngleGeometry(a0, a1, b0, b1, constraint.aReverse, constraint.bReverse, constraint.ccwFromAToB);
      if (!resolved) return;
      const { vertex, rayA, rayB, midDir } = resolved;
      const fallback = add2(vertex, scale2(midDir, 4.4));
      const anchor = resolveLabelAnchor(constraintIndex, fallback);
      dimensionAnchors.set(constraintIndex, anchor);
      const anchorVec = sub2(anchor, vertex);
      const anchorRadius = len2(anchorVec);
      const anchorAngle = Math.atan2(anchorVec[1], anchorVec[0]);
      const startAngle = Math.atan2(rayA[1], rayA[0]);
      const endAngle = Math.atan2(rayB[1], rayB[0]);
      const arcSweep = Math.abs(deltaAlongSweep(startAngle, endAngle, constraint.ccwFromAToB));
      const anchorSweep = Math.abs(deltaAlongSweep(startAngle, anchorAngle, constraint.ccwFromAToB));
      const anchorInsideSector = anchorSweep <= arcSweep + 1e-6;
      const r = anchorInsideSector ? anchorRadius - 0.8 : anchorRadius;
      if (r <= 1e-6) return;
      const extendAfterEnd = Math.abs(deltaAlongSweep(endAngle, anchorAngle, constraint.ccwFromAToB));
      const extendBeforeStart = Math.abs(deltaAlongSweep(anchorAngle, startAngle, constraint.ccwFromAToB));
      const extendStart = !anchorInsideSector && extendBeforeStart < extendAfterEnd;
      const arcStartAngle = extendStart ? anchorAngle : startAngle;
      const arcEndAngle = !anchorInsideSector && !extendStart ? anchorAngle : endAngle;
      lines.push({ x: vertex[0], y: vertex[1], color: DIM_COLOR }, { x: vertex[0] + rayA[0] * r, y: vertex[1] + rayA[1] * r, color: DIM_COLOR });
      lines.push({ x: vertex[0], y: vertex[1], color: DIM_COLOR }, { x: vertex[0] + rayB[0] * r, y: vertex[1] + rayB[1] * r, color: DIM_COLOR });
      pushAngleArc(lines, vertex, rayA, rayB, r, constraint.ccwFromAToB, DIM_COLOR, arcStartAngle, arcEndAngle);
      if (activeDimension) {
        highlightLines.push({ x: vertex[0], y: vertex[1], color: DIM_HOVER }, { x: vertex[0] + rayA[0] * r, y: vertex[1] + rayA[1] * r, color: DIM_HOVER });
        highlightLines.push({ x: vertex[0], y: vertex[1], color: DIM_HOVER }, { x: vertex[0] + rayB[0] * r, y: vertex[1] + rayB[1] * r, color: DIM_HOVER });
        pushAngleArc(highlightLines, vertex, rayA, rayB, r, constraint.ccwFromAToB, DIM_HOVER, arcStartAngle, arcEndAngle);
      }
      labels.push({
        text: `${formatNumber(resolveValue(`sketch.constraint.${constraintIndex}.angle`, constraint.angle))}`,
        anchor,
        pickId,
        hovered: false,
      });
      if (activeDimension) highlightLabels.push({ text: `${formatNumber(resolveValue(`sketch.constraint.${constraintIndex}.angle`, constraint.angle))}`, anchor, pickId, hovered: true });
      return;
    }
    default:
      return;
  }
}

function pushPointDistanceGeometry(
  lines: LineVertex[],
  highlightLines: LineVertex[],
  dimensionAnchors: Map<number, Vec2>,
  constraintIndex: number,
  a: Vec2,
  b: Vec2,
  resolveLabelAnchor: (index: number, fallback: Vec2) => Vec2,
  activeDimension: boolean,
): Vec2 {
  const dir = norm2(sub2(b, a));
  const n = perp(dir);
  const mid = scale2(add2(a, b), 0.5);
  const fallbackAnchor = add2(mid, scale2(n, 1.8));
  const anchor = resolveLabelAnchor(constraintIndex, fallbackAnchor);
  dimensionAnchors.set(constraintIndex, anchor);
  const offsetAmount = dot2(sub2(anchor, mid), n);
  const off = scale2(n, Math.abs(offsetAmount) < 0.5 ? 1.8 : offsetAmount);
  const aa = add2(a, off);
  const bb = add2(b, off);
  const axis = norm2(sub2(bb, aa));
  const projected = add2(aa, scale2(axis, dot2(sub2(anchor, aa), axis)));
  const extentA = dot2(sub2(projected, aa), axis);
  const extentB = dot2(sub2(projected, bb), axis);
  lines.push({ x: a[0], y: a[1], color: DIM_COLOR }, { x: aa[0], y: aa[1], color: DIM_COLOR });
  lines.push({ x: b[0], y: b[1], color: DIM_COLOR }, { x: bb[0], y: bb[1], color: DIM_COLOR });
  lines.push({ x: aa[0], y: aa[1], color: DIM_COLOR }, { x: bb[0], y: bb[1], color: DIM_COLOR });
  if (extentA < 0) {
    lines.push({ x: projected[0], y: projected[1], color: DIM_COLOR }, { x: aa[0], y: aa[1], color: DIM_COLOR });
  } else if (extentB > 0) {
    lines.push({ x: bb[0], y: bb[1], color: DIM_COLOR }, { x: projected[0], y: projected[1], color: DIM_COLOR });
  }
  lines.push({ x: projected[0], y: projected[1], color: DIM_COLOR }, { x: anchor[0], y: anchor[1], color: DIM_COLOR });
  if (activeDimension) {
    highlightLines.push({ x: a[0], y: a[1], color: DIM_HOVER }, { x: aa[0], y: aa[1], color: DIM_HOVER });
    highlightLines.push({ x: b[0], y: b[1], color: DIM_HOVER }, { x: bb[0], y: bb[1], color: DIM_HOVER });
    highlightLines.push({ x: aa[0], y: aa[1], color: DIM_HOVER }, { x: bb[0], y: bb[1], color: DIM_HOVER });
    if (extentA < 0) {
      highlightLines.push({ x: projected[0], y: projected[1], color: DIM_HOVER }, { x: aa[0], y: aa[1], color: DIM_HOVER });
    } else if (extentB > 0) {
      highlightLines.push({ x: bb[0], y: bb[1], color: DIM_HOVER }, { x: projected[0], y: projected[1], color: DIM_HOVER });
    }
    highlightLines.push({ x: projected[0], y: projected[1], color: DIM_HOVER }, { x: anchor[0], y: anchor[1], color: DIM_HOVER });
  }
  return anchor;
}

function computeBounds(
  sketch: ActionSketch,
  pointMap: Map<string, Vec2>,
  resolveValue: (path: string, fallback: number) => number,
): [Vec2, Vec2] {
  let min: Vec2 = [Infinity, Infinity];
  let max: Vec2 = [-Infinity, -Infinity];
  for (const entity of sketch.entities) {
    if (entity.case === "REPoint") {
      const p = pointMap.get(entity.id);
      if (!p) continue;
      min = [Math.min(min[0], p[0]), Math.min(min[1], p[1])];
      max = [Math.max(max[0], p[0]), Math.max(max[1], p[1])];
    }
    if (entity.case === "RECircle") {
      const c = pointMap.get(entity.center);
      if (!c) continue;
      const radius = resolveValue(`sketch.entity.${entity.id}.radius`, entity.radius);
      min = [Math.min(min[0], c[0] - radius), Math.min(min[1], c[1] - radius)];
      max = [Math.max(max[0], c[0] + radius), Math.max(max[1], c[1] + radius)];
    }
  }
  if (!Number.isFinite(min[0])) return [[-10, -10], [10, 10]];
  return [min, max];
}

function projectPointToInfiniteLine(point: Vec2, a: Vec2, b: Vec2): Vec2 {
  const ab = sub2(b, a);
  const denom = dot2(ab, ab);
  if (denom < 1e-9) return a;
  const t = dot2(sub2(point, a), ab) / denom;
  return add2(a, scale2(ab, t));
}

function resolveAngleGeometry(
  aStart: Vec2,
  aEnd: Vec2,
  bStart: Vec2,
  bEnd: Vec2,
  aReverse: boolean,
  bReverse: boolean,
  ccw: boolean,
): { vertex: Vec2; rayA: Vec2; rayB: Vec2; midDir: Vec2 } | null {
  const aVertex = aReverse ? aEnd : aStart;
  const bVertex = bReverse ? bEnd : bStart;
  const rayA = norm2(aReverse ? sub2(aStart, aEnd) : sub2(aEnd, aStart));
  const rayB = norm2(bReverse ? sub2(bStart, bEnd) : sub2(bEnd, bStart));
  if (len2(rayA) < 1e-6 || len2(rayB) < 1e-6) return null;
  const vertex = len2(sub2(aVertex, bVertex)) < 1e-4 ? aVertex : lineIntersection(aVertex, rayA, bVertex, rayB) ?? aVertex;
  const angleA = Math.atan2(rayA[1], rayA[0]);
  const angleB = Math.atan2(rayB[1], rayB[0]);
  let sweep = angleB - angleA;
  if (ccw) {
    while (sweep < 0) sweep += Math.PI * 2;
  } else {
    while (sweep > 0) sweep -= Math.PI * 2;
  }
  const midAngle = angleA + sweep * 0.5;
  return { vertex, rayA, rayB, midDir: [Math.cos(midAngle), Math.sin(midAngle)] };
}

function lineIntersection(originA: Vec2, dirA: Vec2, originB: Vec2, dirB: Vec2): Vec2 | null {
  const denom = dirA[0] * dirB[1] - dirA[1] * dirB[0];
  if (Math.abs(denom) < 1e-6) return null;
  const delta = sub2(originB, originA);
  const t = (delta[0] * dirB[1] - delta[1] * dirB[0]) / denom;
  return add2(originA, scale2(dirA, t));
}

function normalizedSweep(startAngle: number, endAngle: number, ccw: boolean): number {
  let sweep = endAngle - startAngle;
  if (ccw) {
    while (sweep < 0) sweep += Math.PI * 2;
  } else {
    while (sweep > 0) sweep -= Math.PI * 2;
  }
  return sweep;
}

function deltaAlongSweep(startAngle: number, targetAngle: number, ccw: boolean): number {
  return normalizedSweep(startAngle, targetAngle, ccw);
}

function pushAngleArc(
  lines: LineVertex[],
  vertex: Vec2,
  rayA: Vec2,
  rayB: Vec2,
  radius: number,
  ccw: boolean,
  color: readonly number[],
  startOverrideAngle?: number,
  endOverrideAngle?: number,
): void {
  const baseStartAngle = startOverrideAngle ?? Math.atan2(rayA[1], rayA[0]);
  const baseEndAngle = endOverrideAngle ?? Math.atan2(rayB[1], rayB[0]);
  const sweep = normalizedSweep(baseStartAngle, baseEndAngle, ccw);
  const segments = Math.max(12, Math.ceil(Math.abs(sweep) * 12 / Math.PI));
  let prev: Vec2 = [vertex[0] + Math.cos(baseStartAngle) * radius, vertex[1] + Math.sin(baseStartAngle) * radius];
  for (let i = 1; i <= segments; i++) {
    const t = i / segments;
    const angle = baseStartAngle + sweep * t;
    const next: Vec2 = [vertex[0] + Math.cos(angle) * radius, vertex[1] + Math.sin(angle) * radius];
    lines.push({ x: prev[0], y: prev[1], color }, { x: next[0], y: next[1], color });
    prev = next;
  }
}

function pushGrid(lines: LineVertex[], min: Vec2, max: Vec2): void {
  const pad = 10;
  const reach = Math.max(
    10,
    Math.ceil((Math.max(Math.abs(min[0]), Math.abs(max[0]), Math.abs(min[1]), Math.abs(max[1])) + pad) / 5) * 5,
  );
  for (let x = -reach; x <= reach; x += 5) {
    lines.push({ x, y: -reach, color: GRID_MAJOR }, { x, y: reach, color: GRID_MAJOR });
  }
  for (let y = -reach; y <= reach; y += 5) {
    lines.push({ x: -reach, y, color: GRID_MAJOR }, { x: reach, y, color: GRID_MAJOR });
  }
  lines.push({ x: -reach, y: 0, color: AXIS }, { x: reach, y: 0, color: AXIS });
  lines.push({ x: 0, y: -reach, color: AXIS }, { x: 0, y: reach, color: AXIS });
}

function resolveLoopGeometry(
  loop: SketchLoop,
  entityMap: Map<string, RenderEntity>,
  pointMap: Map<string, Vec2>,
  resolveValue: (path: string, fallback: number) => number,
): ResolvedLoopGeometry | null {
  if (loop.entityIds.length === 1) {
    const entity = entityMap.get(loop.entityIds[0]);
    if (!entity || entity.case !== "RECircle") return null;
    const center = pointMap.get(entity.center);
    if (!center) return null;
    const radius = resolveValue(`sketch.entity.${entity.id}.radius`, entity.radius);
    return { id: loop.id, pickId: null, boundary: sampleCircleBoundary(center, radius, 48) };
  }

  const edges = loop.entityIds.map((id) => entityMap.get(id)).map((entity) => entity ? makeLoopEdge(entity, pointMap) : null);
  if (edges.some((edge) => edge == null)) return null;

  const forwardEdges = edges as Array<{ forward: Vec2[] }>;
  const ordered: Vec2[] = [...forwardEdges[0].forward];
  const used = new Set<number>([0]);
  let tail = ordered[ordered.length - 1];

  while (used.size < forwardEdges.length) {
    let found = false;
    for (let i = 0; i < forwardEdges.length; i++) {
      if (used.has(i)) continue;
      const edge = forwardEdges[i];
      const startsAtTail = near2(edge.forward[0], tail);
      const endsAtTail = near2(edge.forward[edge.forward.length - 1], tail);
      if (!startsAtTail && !endsAtTail) continue;
      const segment = startsAtTail ? edge.forward : [...edge.forward].reverse();
      ordered.push(...segment.slice(1));
      tail = segment[segment.length - 1];
      used.add(i);
      found = true;
      break;
    }
    if (!found) return null;
  }

  if (!near2(ordered[0], ordered[ordered.length - 1])) ordered.push(ordered[0]);
  return ordered.length >= 4 ? { id: loop.id, pickId: null, boundary: ordered } : null;
}

function makeLoopEdge(entity: RenderEntity, pointMap: Map<string, Vec2>): { forward: Vec2[] } | null {
  if (entity.case === "RELine") {
    const start = pointMap.get(entity.startId);
    const end = pointMap.get(entity.endId);
    return start && end ? { forward: [start, end] } : null;
  }
  if (entity.case === "REArc" && entity.data.case === "ArcCenter") {
    const start = pointMap.get(entity.startId);
    const end = pointMap.get(entity.endId);
    const center = pointMap.get(entity.data.center);
    return start && end && center ? { forward: sampleArcBoundary(start, end, center, entity.data.clockwise, 48) } : null;
  }
  return null;
}

function sampleCircleBoundary(center: Vec2, radius: number, segments: number): Vec2[] {
  const points: Vec2[] = [];
  for (let i = 0; i <= segments; i++) {
    const angle = (i / segments) * Math.PI * 2;
    points.push([center[0] + Math.cos(angle) * radius, center[1] + Math.sin(angle) * radius]);
  }
  return points;
}

function sampleArcBoundary(start: Vec2, end: Vec2, center: Vec2, clockwise: boolean, segments: number): Vec2[] {
  const startAngle = Math.atan2(start[1] - center[1], start[0] - center[0]);
  const endAngle = Math.atan2(end[1] - center[1], end[0] - center[0]);
  const radius = len2(sub2(start, center));
  let sweep = endAngle - startAngle;
  if (clockwise && sweep > 0) sweep -= Math.PI * 2;
  if (!clockwise && sweep < 0) sweep += Math.PI * 2;
  const points: Vec2[] = [];
  for (let i = 0; i <= segments; i++) {
    const t = i / segments;
    const angle = startAngle + sweep * t;
    points.push([center[0] + Math.cos(angle) * radius, center[1] + Math.sin(angle) * radius]);
  }
  return points;
}

function pushLoopFill(
  vertices: LineVertex[],
  pickTriangles: PickLoopTriangle[],
  boundary: Vec2[],
  color: readonly number[],
  pickId: number | null,
): void {
  const polygon = boundary.slice(0, -1);
  for (const [a, b, c] of triangulatePolygon(polygon)) {
    vertices.push(
      { x: a[0], y: a[1], color },
      { x: b[0], y: b[1], color },
      { x: c[0], y: c[1], color },
    );
    if (pickId != null) pickTriangles.push({ a, b, c, pickId });
  }
}

function triangulatePolygon(points: Vec2[]): [Vec2, Vec2, Vec2][] {
  const triangles: [Vec2, Vec2, Vec2][] = [];
  if (points.length < 3) return triangles;
  const winding = polygonSignedArea(points) >= 0 ? 1 : -1;
  const indices = points.map((_, i) => i);
  let guard = 0;
  while (indices.length > 2 && guard < points.length * points.length) {
    let clipped = false;
    for (let i = 0; i < indices.length; i++) {
      const i0 = indices[(i + indices.length - 1) % indices.length];
      const i1 = indices[i];
      const i2 = indices[(i + 1) % indices.length];
      const a = points[i0];
      const b = points[i1];
      const c = points[i2];
      if (!isEar(a, b, c, points, indices, winding)) continue;
      triangles.push(winding > 0 ? [a, b, c] : [a, c, b]);
      indices.splice(i, 1);
      clipped = true;
      break;
    }
    if (!clipped) break;
    guard++;
  }
  return triangles;
}

function isEar(a: Vec2, b: Vec2, c: Vec2, points: Vec2[], indices: number[], winding: number): boolean {
  const turn = cross2(sub2(b, a), sub2(c, b));
  if (winding > 0 ? turn <= 1e-6 : turn >= -1e-6) return false;
  for (const index of indices) {
    const p = points[index];
    if (same2(p, a) || same2(p, b) || same2(p, c)) continue;
    if (pointInTriangle(p, a, b, c)) return false;
  }
  return true;
}

function pointInTriangle(p: Vec2, a: Vec2, b: Vec2, c: Vec2): boolean {
  const s1 = cross2(sub2(b, a), sub2(p, a));
  const s2 = cross2(sub2(c, b), sub2(p, b));
  const s3 = cross2(sub2(a, c), sub2(p, c));
  const hasNeg = s1 < -1e-6 || s2 < -1e-6 || s3 < -1e-6;
  const hasPos = s1 > 1e-6 || s2 > 1e-6 || s3 > 1e-6;
  return !(hasNeg && hasPos);
}

function polygonSignedArea(points: Vec2[]): number {
  let area = 0;
  for (let i = 0; i < points.length; i++) {
    const a = points[i];
    const b = points[(i + 1) % points.length];
    area += a[0] * b[1] - b[0] * a[1];
  }
  return area * 0.5;
}

function cross2(a: Vec2, b: Vec2): number {
  return a[0] * b[1] - a[1] * b[0];
}

function near2(a: Vec2, b: Vec2): boolean {
  return len2(sub2(a, b)) < 1e-3;
}

function same2(a: Vec2, b: Vec2): boolean {
  return near2(a, b);
}

function pushCircle(lines: LineVertex[], center: Vec2, radius: number, color: readonly number[]): void {
  const segments = 64;
  for (let i = 0; i < segments; i++) {
    const a0 = (i / segments) * Math.PI * 2;
    const a1 = ((i + 1) / segments) * Math.PI * 2;
    lines.push(
      { x: center[0] + Math.cos(a0) * radius, y: center[1] + Math.sin(a0) * radius, color },
      { x: center[0] + Math.cos(a1) * radius, y: center[1] + Math.sin(a1) * radius, color },
    );
  }
}

function pushArc(
  lines: LineVertex[],
  pickSegments: PickSegment[],
  start: Vec2,
  end: Vec2,
  center: Vec2,
  clockwise: boolean,
  color: readonly number[],
  pickId: number | undefined,
): void {
  const startAngle = Math.atan2(start[1] - center[1], start[0] - center[0]);
  const endAngle = Math.atan2(end[1] - center[1], end[0] - center[0]);
  const radius = len2(sub2(start, center));
  let sweep = endAngle - startAngle;
  if (clockwise && sweep > 0) sweep -= Math.PI * 2;
  if (!clockwise && sweep < 0) sweep += Math.PI * 2;
  const segments = 48;
  let prev = start;
  for (let i = 1; i <= segments; i++) {
    const t = i / segments;
    const angle = startAngle + sweep * t;
    const next: Vec2 = [center[0] + Math.cos(angle) * radius, center[1] + Math.sin(angle) * radius];
    lines.push({ x: prev[0], y: prev[1], color }, { x: next[0], y: next[1], color });
    if (pickId != null) pickSegments.push({ a: prev, b: next, strokePx: 8, pickId, kind: 4 });
    prev = next;
  }
}

function buildToolPreviewBuffers(
  tool: string,
  toolPoints: Vec2[],
  cursor: Vec2 | null,
  _frame: SketchFrame,
): { frame: SketchFrame; lineData: Float32Array; pointData: Float32Array } | null {
  const lineVertices: LineVertex[] = [];
  const pointInstances: PointInstance[] = [];
  const previewLine = [ACCENT[0], ACCENT[1], ACCENT[2], 0.72] as const;
  const previewPoint = [ACCENT[0], ACCENT[1], ACCENT[2], 0.92] as const;

  switch (tool) {
    case "line":
      for (const point of (cursor ? [...toolPoints, cursor] : toolPoints)) {
        pointInstances.push({ x: point[0], y: point[1], radiusPx: 5.5, color: previewPoint });
      }
      if (toolPoints.length >= 1 && cursor) {
        lineVertices.push({ x: toolPoints[0][0], y: toolPoints[0][1], color: previewLine }, { x: cursor[0], y: cursor[1], color: previewLine });
      }
      break;
    case "rectangle":
      for (const point of (cursor ? [...toolPoints, cursor] : toolPoints)) {
        pointInstances.push({ x: point[0], y: point[1], radiusPx: 5.5, color: previewPoint });
      }
      if (toolPoints.length >= 1 && cursor) {
        const [x0, y0] = toolPoints[0];
        const [x1, y1] = cursor;
        const corners: Vec2[] = [[x0, y0], [x1, y0], [x1, y1], [x0, y1]];
        for (let i = 0; i < 4; i++) {
          const a = corners[i];
          const b = corners[(i + 1) % 4];
          lineVertices.push({ x: a[0], y: a[1], color: previewLine }, { x: b[0], y: b[1], color: previewLine });
        }
      }
      break;
    case "roundedRectangle":
      for (const point of (cursor ? [...toolPoints, cursor] : toolPoints)) {
        pointInstances.push({ x: point[0], y: point[1], radiusPx: 5.5, color: previewPoint });
      }
      if (toolPoints.length >= 1 && cursor) {
        const [x0, y0] = toolPoints[0];
        const [x1, y1] = cursor;
        const minX = Math.min(x0, x1);
        const maxX = Math.max(x0, x1);
        const minY = Math.min(y0, y1);
        const maxY = Math.max(y0, y1);
        const width = maxX - minX;
        const height = maxY - minY;
        if (width > 1e-9 && height > 1e-9) {
          const radius = roundedRectRadius(minX, maxX, minY, maxY);
          if (radius <= 1e-6) {
            const corners: Vec2[] = [[x0, y0], [x1, y0], [x1, y1], [x0, y1]];
            for (let i = 0; i < 4; i++) {
              const a = corners[i];
              const b = corners[(i + 1) % 4];
              lineVertices.push({ x: a[0], y: a[1], color: previewLine }, { x: b[0], y: b[1], color: previewLine });
            }
          } else {
            const topLeftStart: Vec2 = [minX + radius, maxY];
            const topRightStart: Vec2 = [maxX - radius, maxY];
            const rightTopStart: Vec2 = [maxX, maxY - radius];
            const rightBottomStart: Vec2 = [maxX, minY + radius];
            const bottomRightStart: Vec2 = [maxX - radius, minY];
            const bottomLeftStart: Vec2 = [minX + radius, minY];
            const leftBottomStart: Vec2 = [minX, minY + radius];
            const leftTopStart: Vec2 = [minX, maxY - radius];
            const tlCenter: Vec2 = [minX + radius, maxY - radius];
            const trCenter: Vec2 = [maxX - radius, maxY - radius];
            const brCenter: Vec2 = [maxX - radius, minY + radius];
            const blCenter: Vec2 = [minX + radius, minY + radius];

            const lineSegments: Array<[Vec2, Vec2]> = [
              [topLeftStart, topRightStart],
              [rightTopStart, rightBottomStart],
              [bottomRightStart, bottomLeftStart],
              [leftBottomStart, leftTopStart],
            ];
            for (const [a, b] of lineSegments) {
              lineVertices.push({ x: a[0], y: a[1], color: previewLine }, { x: b[0], y: b[1], color: previewLine });
            }
            pushArc(lineVertices, [], topRightStart, rightTopStart, trCenter, true, previewLine, undefined);
            pushArc(lineVertices, [], rightBottomStart, bottomRightStart, brCenter, true, previewLine, undefined);
            pushArc(lineVertices, [], bottomLeftStart, leftBottomStart, blCenter, true, previewLine, undefined);
            pushArc(lineVertices, [], leftTopStart, topLeftStart, tlCenter, true, previewLine, undefined);
          }
        }
      }
      break;
    case "circle":
      for (const point of (cursor ? [...toolPoints, cursor] : toolPoints)) {
        pointInstances.push({ x: point[0], y: point[1], radiusPx: 5.5, color: previewPoint });
      }
      if (toolPoints.length >= 1 && cursor) {
        const radius = Math.max(1e-6, len2(sub2(cursor, toolPoints[0])));
        pushCircle(lineVertices, toolPoints[0], radius, previewLine);
      }
      break;
    case "arc":
      for (const point of toolPoints) {
        pointInstances.push({ x: point[0], y: point[1], radiusPx: 5.5, color: previewPoint });
      }
      if (toolPoints.length >= 2 && cursor) {
        const projectedCursor = projectPointToCircle(toolPoints[0], toolPoints[1], cursor);
        pointInstances.push({ x: projectedCursor[0], y: projectedCursor[1], radiusPx: 5.5, color: previewPoint });
        const clockwise = cross2(sub2(toolPoints[1], toolPoints[0]), sub2(cursor, toolPoints[0])) < 0;
        pushArc(lineVertices, [], toolPoints[1], projectedCursor, toolPoints[0], clockwise, previewLine, undefined);
      } else if (toolPoints.length >= 1 && cursor) {
        pointInstances.push({ x: cursor[0], y: cursor[1], radiusPx: 5.5, color: previewPoint });
        lineVertices.push({ x: toolPoints[0][0], y: toolPoints[0][1], color: previewLine }, { x: cursor[0], y: cursor[1], color: previewLine });
      }
      break;
  }

  if (lineVertices.length === 0 && pointInstances.length === 0) return null;
  return {
    frame: _frame,
    lineData: flattenLines(lineVertices),
    pointData: flattenPoints(pointInstances),
  };
}

function flattenLines(lines: LineVertex[]): Float32Array {
  const data = new Float32Array(lines.length * 6);
  let o = 0;
  for (const line of lines) {
    data[o++] = line.x;
    data[o++] = line.y;
    data[o++] = line.color[0];
    data[o++] = line.color[1];
    data[o++] = line.color[2];
    data[o++] = line.color[3];
  }
  return data;
}

function flattenPoints(points: PointInstance[]): Float32Array {
  const data = new Float32Array(points.length * 7);
  let o = 0;
  for (const point of points) {
    data[o++] = point.x;
    data[o++] = point.y;
    data[o++] = point.radiusPx;
    data[o++] = point.color[0];
    data[o++] = point.color[1];
    data[o++] = point.color[2];
    data[o++] = point.color[3];
  }
  return data;
}

function flattenPickPoints(points: PickPoint[]): Float32Array {
  const data = new Float32Array(points.length * 4);
  let o = 0;
  for (const point of points) {
    data[o++] = point.x;
    data[o++] = point.y;
    data[o++] = point.radiusPx;
    data[o++] = point.pickId;
  }
  return data;
}

function flattenPickSegments(segments: PickSegment[]): Float32Array {
  const data = new Float32Array(segments.length * 8);
  let o = 0;
  for (const segment of segments) {
    data[o++] = segment.a[0];
    data[o++] = segment.a[1];
    data[o++] = segment.b[0];
    data[o++] = segment.b[1];
    data[o++] = segment.strokePx;
    data[o++] = segment.pickId;
    data[o++] = segment.kind;
    data[o++] = 0;
  }
  return data;
}

function flattenPickCircles(circles: PickCircle[]): Float32Array {
  const data = new Float32Array(circles.length * 8);
  let o = 0;
  for (const circle of circles) {
    data[o++] = circle.center[0];
    data[o++] = circle.center[1];
    data[o++] = circle.radius;
    data[o++] = circle.strokePx;
    data[o++] = circle.pickId;
    data[o++] = 0;
    data[o++] = 0;
    data[o++] = 0;
  }
  return data;
}

function flattenPickLoopTriangles(triangles: PickLoopTriangle[]): Float32Array {
  const data = new Float32Array(triangles.length * 8);
  let o = 0;
  for (const tri of triangles) {
    data[o++] = tri.a[0];
    data[o++] = tri.a[1];
    data[o++] = tri.b[0];
    data[o++] = tri.b[1];
    data[o++] = tri.c[0];
    data[o++] = tri.c[1];
    data[o++] = tri.pickId;
    data[o++] = 0;
  }
  return data;
}

function buildLabelPickRects(labels: ConstraintLabel[], font: FontMetrics): PickLabelRect[] {
  const rects: PickLabelRect[] = [];
  for (const label of labels) {
    if (label.pickId == null) continue;
    const bounds = measureTextBounds(label.text, font);
    rects.push({
      anchor: label.anchor,
      minPx: [bounds.minX - 4, bounds.minY - 3],
      maxPx: [bounds.maxX + 4, bounds.maxY + 3],
      pickId: label.pickId,
    });
  }
  return rects;
}

function flattenPickLabelRects(rects: PickLabelRect[]): Float32Array {
  const data = new Float32Array(rects.length * 8);
  let o = 0;
  for (const rect of rects) {
    data[o++] = rect.anchor[0];
    data[o++] = rect.anchor[1];
    data[o++] = rect.minPx[0];
    data[o++] = rect.minPx[1];
    data[o++] = rect.maxPx[0];
    data[o++] = rect.maxPx[1];
    data[o++] = rect.pickId;
    data[o++] = 0;
  }
  return data;
}

function buildPickIndex(pickables: Pickable[], sketchId: string): Map<string, number> {
  const map = new Map<string, number>();
  for (const pickable of pickables) {
    if ((pickable as { sketchId?: string }).sketchId !== sketchId) continue;
    if (pickable.case === "PickLoop" && typeof pickable.loopId === "string") {
      map.set(`loop:${pickable.loopId}`, pickable.pickId);
      continue;
    }
    if (pickable.case === "PickDimension" && typeof pickable.constraintIndex === "number") {
      map.set(`dimension:${pickable.constraintIndex}`, pickable.pickId);
      continue;
    }
    if ("entityId" in pickable && typeof pickable.entityId === "string") {
      let prefix: string | null = null;
      switch (pickable.case) {
        case "PickPoint": prefix = "point"; break;
        case "PickLine": prefix = "line"; break;
        case "PickCircle": prefix = "circle"; break;
        case "PickArc": prefix = "arc"; break;
      }
      if (prefix) map.set(`${prefix}:${pickable.entityId}`, pickable.pickId);
    }
  }
  return map;
}

function toPickRequest(candidates: PickCandidateHit[]): Array<{ pickId: number; score: number }> {
  return candidates.map((candidate) => ({ pickId: candidate.pickId, score: candidate.score }));
}

function selectionMatches(
  target: SelectionTarget | null,
  sketchId: string,
  kind: PickKind,
  id: string | number,
): boolean {
  if (!target) return false;
  switch (target.case) {
    case "TargetPoint":
      return kind === "point" && target.sketchId === sketchId && target.entityId === id;
    case "TargetLine":
      return kind === "line" && target.sketchId === sketchId && target.entityId === id;
    case "TargetCircle":
      return kind === "circle" && target.sketchId === sketchId && target.entityId === id;
    case "TargetArc":
      return kind === "arc" && target.sketchId === sketchId && target.entityId === id;
    case "TargetLoop":
      return kind === "loop" && target.sketchId === sketchId && target.loopId === id;
    case "TargetDimension":
      return kind === "dimension" && target.sketchId === sketchId && target.constraintIndex === id;
    default:
      return false;
  }
}

function selectionMatchesAny(
  targets: SelectionTarget[],
  sketchId: string,
  kind: PickKind,
  id: string | number,
): boolean {
  return targets.some((target) => selectionMatches(target, sketchId, kind, id));
}

function buildFrameLineData(
  frames: Array<{ id: string; transform: JsonRigidTransform }>,
  hoveredTarget: SelectionTarget | null,
  selectedTargets: SelectionTarget[],
  selectedActionId: string | null,
): Float32Array {
  const data: number[] = [];
  for (const frame of frames) {
    const t = toSketchFrame(frame.transform);
    const axisPx = frame.id === "origin" ? 64 : 52;
    const frameActive =
      frameTargetMatches(hoveredTarget, frame.id) ||
      selectedTargets.some((target) => frameTargetMatches(target, frame.id));
    const frameSelected = selectedActionId === frame.id;
    const axisColor = (base: readonly number[]): readonly number[] => {
      const alpha = frameActive || frameSelected ? 1.0 : 0.88;
      return frameActive ? [ACCENT[0], ACCENT[1], ACCENT[2], alpha] : [base[0], base[1], base[2], alpha];
    };
    pushFrameAxis(data, t.position, t.xAxis, axisPx, axisColor([0.88, 0.42, 0.42, 1]));
    pushFrameAxis(data, t.position, t.yAxis, axisPx, axisColor([0.48, 0.78, 0.54, 1]));
    pushFrameAxis(data, t.position, t.zAxis, axisPx, axisColor([0.45, 0.56, 0.92, 1]));
  }
  return new Float32Array(data);
}

function buildFieldSliceVertexData(slices: Array<{
  surfaceIndex: number;
  planeOrigin: { x: number; y: number; z: number };
  planeX: { x: number; y: number; z: number };
  planeY: { x: number; y: number; z: number };
  extent: number;
}>, camera: CameraState, canvas: HTMLCanvasElement): Float32Array {
  const verts: number[] = [];
  for (const slice of slices) {
    const o: Vec3 = [slice.planeOrigin.x, slice.planeOrigin.y, slice.planeOrigin.z];
    const x: Vec3 = [slice.planeX.x, slice.planeX.y, slice.planeX.z];
    const y: Vec3 = [slice.planeY.x, slice.planeY.y, slice.planeY.z];
    const e = adaptiveFieldSliceExtent(o, x, y, slice.extent, camera, canvas);
    const p00 = sub3(sub3(o, scale3(x, e)), scale3(y, e));
    const p10 = add3(sub3(o, scale3(y, e)), scale3(x, e));
    const p01 = add3(sub3(o, scale3(x, e)), scale3(y, e));
    const p11 = add3(add3(o, scale3(x, e)), scale3(y, e));
    const push = (p: Vec3, u: number, v: number) => verts.push(p[0], p[1], p[2], slice.surfaceIndex, u, v, 0);
    push(p00, -1, -1); push(p10, 1, -1); push(p01, -1, 1);
    push(p10, 1, -1); push(p11, 1, 1); push(p01, -1, 1);
  }
  return new Float32Array(verts);
}

function adaptiveFieldSliceExtent(
  origin: Vec3,
  planeX: Vec3,
  planeY: Vec3,
  baseExtent: number,
  camera: CameraState,
  canvas: HTMLCanvasElement,
): number {
  const xAxis = norm3(planeX);
  const yAxis = norm3(planeY);
  const normal = norm3(cross3(xAxis, yAxis));
  const { eye, forward, right, up } = viewBasis(camera);
  const aspect = canvas.width / Math.max(canvas.height, 1);
  const tan = Math.tan(HALF_FOV);

  let maxAbs = 0;
  const corners: Vec2[] = [[-1, -1], [1, -1], [-1, 1], [1, 1]];
  for (const [nx, ny] of corners) {
    const rd = norm3(add3(forward, add3(scale3(right, nx * aspect * tan), scale3(up, ny * tan))));
    const denom = dot3(rd, normal);
    if (Math.abs(denom) < 1e-5) continue;
    const t = dot3(sub3(origin, eye), normal) / denom;
    if (t <= 0) continue;
    const hit = add3(eye, scale3(rd, t));
    const rel = sub3(hit, origin);
    maxAbs = Math.max(maxAbs, Math.abs(dot3(rel, xAxis)), Math.abs(dot3(rel, yAxis)));
  }

  if (maxAbs > 0) {
    return Math.max(baseExtent, maxAbs * 1.1);
  }

  const centerDist = Math.max(0.1, len3(sub3(origin, eye)));
  const facing = Math.max(0.2, Math.abs(dot3(forward, normal)));
  const fallback = centerDist * tan * Math.max(1, aspect) / facing;
  return Math.max(baseExtent, fallback * 1.2);
}

function frameTargetMatches(target: SelectionTarget | null, frameId: string): boolean {
  if (!target) return false;
  switch (target.case) {
    case "TargetFrameOrigin":
      return target.frameId === frameId;
    default:
      return false;
  }
}

function pushFrameAxis(target: number[], origin: Vec3, axis: Vec3, axisPx: number, color: readonly number[]): void {
  target.push(origin[0], origin[1], origin[2], axis[0], axis[1], axis[2], axisPx, 0, color[0], color[1], color[2], color[3]);
  target.push(origin[0], origin[1], origin[2], axis[0], axis[1], axis[2], axisPx, 1, color[0], color[1], color[2], color[3]);
}

function slotValue(params: number[], slotLookup: Map<string, number>, actionId: string, path: string, fallback: number): number {
  const slot = slotLookup.get(`${actionId}:${path}`);
  return slot == null ? fallback : (params[slot] ?? fallback);
}

function toSketchFrame(t: JsonRigidTransform): SketchFrame {
  const xAxis = rotateByQuat(t.rot, [1, 0, 0]);
  const yAxis = rotateByQuat(t.rot, [0, 1, 0]);
  const zAxis = rotateByQuat(t.rot, [0, 0, 1]);
  return {
    position: [t.trans.x, t.trans.y, t.trans.z],
    xAxis,
    yAxis,
    zAxis,
  };
}

function liftPoint(frame: SketchFrame, p: Vec2): Vec3 {
  return add3(frame.position, add3(scale3(frame.xAxis, p[0]), scale3(frame.yAxis, p[1])));
}

function projectWorldToSketchLocal(world: Vec3, frame: SketchFrame): Vec2 {
  const local = sub3(world, frame.position);
  return [dot3(local, frame.xAxis), dot3(local, frame.yAxis)];
}

function projectSketchLocalToScreen(
  p: Vec2,
  frame: SketchFrame,
  canvas: HTMLCanvasElement,
  camera: CameraState,
): { x: number; y: number } | null {
  const world = liftPoint(frame, p);
  return projectWorldToScreen(world, canvas, camera);
}

function projectWorldToScreen(
  world: Vec3,
  canvas: HTMLCanvasElement,
  camera: CameraState,
): { x: number; y: number } | null {
  const width = Math.max(canvas.clientWidth, 1);
  const height = Math.max(canvas.clientHeight, 1);
  const aspect = width / height;
  const { eye, forward, right, up } = viewBasis(camera);
  const rel = sub3(world, eye);
  const z = dot3(rel, forward);
  if (z <= 1e-6) return null;
  const tan = Math.tan(HALF_FOV);
  const ndcX = dot3(rel, right) / (z * tan * aspect);
  const ndcY = dot3(rel, up) / (z * tan);
  return {
    x: ((ndcX + 1) * 0.5) * width,
    y: ((1 - ndcY) * 0.5) * height,
  };
}

function pointerToSketchLocal(
  pointer: { x: number; y: number },
  canvas: HTMLCanvasElement,
  camera: CameraState,
  frame: SketchFrame,
): Vec2 | null {
  const width = Math.max(canvas.clientWidth, 1);
  const height = Math.max(canvas.clientHeight, 1);
  const aspect = width / height;
  const ndcX = (pointer.x / width) * 2 - 1;
  const ndcY = 1 - (pointer.y / height) * 2;
  const { eye, forward, right, up } = viewBasis(camera);
  const tan = Math.tan(HALF_FOV);
  const dir = norm3(add3(forward, add3(scale3(right, ndcX * aspect * tan), scale3(up, ndcY * tan))));
  const normal = cross3(frame.xAxis, frame.yAxis);
  const denom = dot3(normal, dir);
  if (Math.abs(denom) < 1e-6) return null;
  const t = dot3(normal, sub3(frame.position, eye)) / denom;
  if (t <= 0) return null;
  const hit = add3(eye, scale3(dir, t));
  const local = sub3(hit, frame.position);
  return [dot3(local, frame.xAxis), dot3(local, frame.yAxis)];
}

function projectPointToCircle(center: Vec2, start: Vec2, point: Vec2): Vec2 {
  const radius = Math.max(1e-6, len2(sub2(start, center)));
  const dir = sub2(point, center);
  const dirLen = len2(dir);
  if (dirLen <= 1e-6) return [center[0] + radius, center[1]];
  const scale = radius / dirLen;
  return [center[0] + dir[0] * scale, center[1] + dir[1] * scale];
}

function roundedRectRadius(minX: number, maxX: number, minY: number, maxY: number): number {
  const width = maxX - minX;
  const height = maxY - minY;
  return Math.min(Math.min(Math.max(Math.min(width, height) * 0.2, 0.002), width * 0.5 - 1e-6), height * 0.5 - 1e-6);
}

function rotateByQuat(q: JsonRigidTransform["rot"], v: Vec3): Vec3 {
  const tx = 2.0 * (q.y * v[2] - q.z * v[1]);
  const ty = 2.0 * (q.z * v[0] - q.x * v[2]);
  const tz = 2.0 * (q.x * v[1] - q.y * v[0]);
  return [
    v[0] + q.w * tx + (q.y * tz - q.z * ty),
    v[1] + q.w * ty + (q.z * tx - q.x * tz),
    v[2] + q.w * tz + (q.x * ty - q.y * tx),
  ];
}


function pickKind(kind: number): PickKind | null {
  switch (kind) {
    case 1: return "point";
    case 2: return "line";
    case 3: return "circle";
    case 4: return "arc";
    case 5: return "loop";
    case 6: return "dimension";
    default: return null;
  }
}

function formatNumber(value: number): string {
  const rounded = Math.round(value * 10) / 10;
  return Number.isInteger(rounded) ? String(rounded) : rounded.toFixed(1);
}

function buildLabelVertices(labels: ConstraintLabel[], font: FontMetrics): Float32Array {
  const verts: number[] = [];
  for (const label of labels) {
    pushText(verts, label.text, label.anchor, label.hovered ? DIM_HOVER : DIM_COLOR, font);
  }
  return new Float32Array(verts);
}

function measureTextBounds(text: string, font: FontMetrics): { minX: number; minY: number; maxX: number; maxY: number } {
  const pxScale = 12 / font.lineHeight;
  let width = 0;
  let prevCode = -1;
  for (const ch of text) {
    const glyph = font.chars.get(ch);
    if (!glyph) continue;
    if (prevCode >= 0) width += font.kernings.get(`${prevCode}:${glyph.id}`) ?? 0;
    width += glyph.xadvance;
    prevCode = glyph.id;
  }

  let penX = -width * 0.5;
  let minX = Infinity;
  let minY = Infinity;
  let maxX = -Infinity;
  let maxY = -Infinity;
  prevCode = -1;
  for (const ch of text) {
    const glyph = font.chars.get(ch);
    if (!glyph) continue;
    if (prevCode >= 0) penX += font.kernings.get(`${prevCode}:${glyph.id}`) ?? 0;
    const x0 = (penX + glyph.xoffset) * pxScale;
    const x1 = x0 + glyph.width * pxScale;
    const y0 = (glyph.yoffset - font.base) * pxScale;
    const y1 = y0 + glyph.height * pxScale;
    minX = Math.min(minX, x0);
    minY = Math.min(minY, y0);
    maxX = Math.max(maxX, x1);
    maxY = Math.max(maxY, y1);
    penX += glyph.xadvance;
    prevCode = glyph.id;
  }
  if (!Number.isFinite(minX)) return { minX: 0, minY: -6, maxX: 0, maxY: 6 };
  return { minX, minY, maxX, maxY };
}

function pushText(out: number[], text: string, anchor: Vec2, color: readonly number[], font: FontMetrics): void {
  const pxScale = 12 / font.lineHeight;
  let width = 0;
  let prevCode = -1;
  for (const ch of text) {
    const glyph = font.chars.get(ch);
    if (!glyph) continue;
    if (prevCode >= 0) width += font.kernings.get(`${prevCode}:${glyph.id}`) ?? 0;
    width += glyph.xadvance;
    prevCode = glyph.id;
  }

  let penX = -width * 0.5;
  prevCode = -1;
  for (const ch of text) {
    const glyph = font.chars.get(ch);
    if (!glyph) continue;
    if (prevCode >= 0) penX += font.kernings.get(`${prevCode}:${glyph.id}`) ?? 0;

    const x0 = (penX + glyph.xoffset) * pxScale;
    const x1 = x0 + glyph.width * pxScale;
    const y0 = (glyph.yoffset - font.base) * pxScale;
    const y1 = y0 + glyph.height * pxScale;
    const u0 = glyph.x / font.scaleW;
    const v0 = glyph.y / font.scaleH;
    const u1 = (glyph.x + glyph.width) / font.scaleW;
    const v1 = (glyph.y + glyph.height) / font.scaleH;

    pushGlyphQuad(out, anchor, x0, y0, x1, y1, u0, v0, u1, v1, color);
    penX += glyph.xadvance;
    prevCode = glyph.id;
  }
}

function pushGlyphQuad(
  out: number[],
  anchor: Vec2,
  x0: number,
  y0: number,
  x1: number,
  y1: number,
  u0: number,
  v0: number,
  u1: number,
  v1: number,
  color: readonly number[],
): void {
  const push = (px: number, py: number, u: number, v: number) => {
    out.push(anchor[0], anchor[1], px, py, u, v, color[0], color[1], color[2], color[3]);
  };
  push(x0, y0, u0, v0);
  push(x1, y0, u1, v0);
  push(x1, y1, u1, v1);
  push(x0, y0, u0, v0);
  push(x1, y1, u1, v1);
  push(x0, y1, u0, v1);
}

function hexToGpuColor(hex: string): GPUColor {
  const value = hex.replace("#", "");
  const num = Number.parseInt(value, 16);
  return {
    r: ((num >> 16) & 255) / 255,
    g: ((num >> 8) & 255) / 255,
    b: (num & 255) / 255,
    a: 1,
  };
}
