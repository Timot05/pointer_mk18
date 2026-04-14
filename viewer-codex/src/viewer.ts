import { getViewerModel, getViewerState, patchActionParam, postViewerPick, type ActionSketch, type JsonRigidTransform, type Pickable, type RenderEntity, type SketchLoop, type ViewerFrame, type ViewerModel, type ViewerSketch, type ViewerState } from "./api";
import { ACCENT, ACCENT_SOFT, AXIS, DIM_COLOR, DIM_HOVER, FIXED_COLOR, GRID_MAJOR, GRID_MINOR, LOOP_FILL, PAGE_BG, SKETCH_LINE, SKETCH_POINT } from "./colors";
import { HALF_FOV, orbit, pan, viewBasis, zoom, type CameraState } from "./camera";
import type { Graph } from "./graph";
import { solveGraphWithGpu, type SolverPin } from "./gpu-lm-solver";
import { createGpuSolver, type GpuSolver } from "./gpu-solver";
import { loadFontMetrics, loadMsdfAtlas, type FontMetrics } from "./msdf-atlas";
import { add2, add3, cross3, dot2, dot3, len2, norm2, norm3, perp, scale2, scale3, sub2, sub3, type Vec2, type Vec3 } from "./math";
import { createMsdfLabelPipeline, writeLabelUniform } from "./pipeline-msdf-label";

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
  pickPoints: Float32Array;
  pickLines: Float32Array;
  pickCircles: Float32Array;
  pickLoops: Float32Array;
  pickLabels: Float32Array;
  labelData: Float32Array;
}

interface RenderSketch {
  sketchId: string;
  frame: SketchFrame;
  buffers: RenderBuffers;
  loops: ResolvedLoopGeometry[];
}

interface ConstraintLabel {
  text: string;
  anchor: Vec2;
  pickId: number | null;
  hovered: boolean;
}

interface SketchSolverBinding {
  graph: Graph;
  solver: GpuSolver;
  localByPath: Map<string, number>;
  localToGlobal: number[];
  varIndexByLocal: Map<number, number>;
}

interface ResolvedLoopGeometry {
  id: string;
  pickId: number | null;
  boundary: Vec2[];
}

interface HoverHit {
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
  pickPipeline: GPUComputePipeline;
  pickBindGroupLayout: GPUBindGroupLayout;
  pickStateBuffer: GPUBuffer;
  pickResultBuffer: GPUBuffer;
}

export interface ViewerStartOptions {
  polling?: boolean;
  onDocumentDirty?: () => void;
  subscribeViewerStateDirty?: (listener: () => void) => () => void;
  subscribeViewerModelDirty?: (listener: () => void) => () => void;
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

export class ViewerApp {
  private readonly root: HTMLElement;
  private readonly canvas: HTMLCanvasElement;
  private readonly statusEl: HTMLElement;
  private readonly metaEl: HTMLElement;
  private readonly hoverEl: HTMLElement;
  private readonly errorEl: HTMLElement;
  private gpu: GpuContext | null = null;
  private model: ViewerModel | null = null;
  private state: ViewerState | null = null;
  private fontMetrics: FontMetrics | null = null;
  private slotLookup = new Map<string, number>();
  private solverBindings = new Map<string, SketchSolverBinding>();
  private solvedSketchParams = new Map<string, Float32Array>();
  private renderSketches: RenderSketch[] = [];
  private selectedActionId: string | null = null;
  private hovered: HoverHit | null = null;
  private resizeObserver: ResizeObserver | null = null;
  private stateTimer: number | null = null;
  private modelTimer: number | null = null;
  private startOptions: ViewerStartOptions = {};
  private renderQueued = false;
  private isPicking = false;
  private solveInFlight: Promise<void> | null = null;
  private solveQueued = false;
  private resetCameraPending = false;
  private pointer = { x: 0, y: 0 };
  private drag: DragState | null = null;
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
    this.canvas = document.createElement("canvas");
    this.canvas.className = "viewer-canvas";

    this.statusEl = document.createElement("div");
    this.metaEl = document.createElement("div");
    this.hoverEl = document.createElement("div");
    this.errorEl = document.createElement("div");
    this.errorEl.className = "status-row is-error";
    const shell = document.createElement("div");
    shell.className = "viewer-shell";
    shell.innerHTML = `<div class="viewer-main"></div>`;
    this.metaEl.className = "viewer-meta mono";

    const main = shell.querySelector(".viewer-main");
    if (!main) throw new Error("Missing viewer main");
    main.appendChild(this.canvas);

    const overlay = document.createElement("div");
    overlay.className = "viewer-overlay";
    overlay.innerHTML = `
      <section class="panel">
        <h2>Status</h2>
      </section>
      <section class="panel">
        <h2>Legend</h2>
        <div class="legend-row"><span class="swatch" style="background:${toCssColor(SKETCH_LINE)}"></span><span>sketch geometry</span></div>
        <div class="legend-row"><span class="swatch" style="background:${toCssColor(ACCENT)}"></span><span>hover / selection accent</span></div>
        <div class="legend-row"><span class="swatch" style="background:${toCssColor(DIM_COLOR)}"></span><span>constraints</span></div>
        <div class="legend-row"><span class="swatch" style="background:${toCssColor(FIXED_COLOR)}"></span><span>fixed points</span></div>
      </section>
    `;
    const statusPanel = overlay.querySelector(".panel");
    if (!statusPanel) throw new Error("Missing status panel");
    this.statusEl.className = "status-row";
    this.hoverEl.className = "status-row mono";
    statusPanel.appendChild(this.statusEl);
    statusPanel.appendChild(this.hoverEl);
    statusPanel.appendChild(this.errorEl);
    main.appendChild(overlay);

    root.appendChild(shell);
  }

  async start(options: ViewerStartOptions = {}): Promise<void> {
    this.startOptions = options;
    this.setStatus("Loading model...");
    this.bindEvents();
    this.gpu = await this.initGpu();
    try {
      this.fontMetrics = await loadFontMetrics("/fonts/dekal.json");
    } catch (error) {
      this.setStatus(`Font metrics failed: ${String(error)}`);
    }
    await this.reloadModel(true);
    await this.reloadState();
    this.resizeObserver = new ResizeObserver(() => {
      this.resizeCanvas();
      this.queueRender();
    });
    this.resizeObserver.observe(this.canvas);
    this.resizeCanvas();
    if (options.polling ?? true) {
      this.stateTimer = window.setInterval(() => { void this.reloadState(); }, 350);
      this.modelTimer = window.setInterval(() => { void this.reloadModel(false); }, 1800);
    }
    options.subscribeViewerStateDirty?.(() => {
      void this.reloadState();
    });
    options.subscribeViewerModelDirty?.(() => {
      void this.reloadModel(false).then(() => this.reloadState());
    });
    this.queueRender();
  }

  private async reloadModel(resetCamera: boolean): Promise<void> {
    if (this.drag) return;
    this.destroySolverBindings();
    this.model = await getViewerModel();
    this.slotLookup = new Map(this.model.slotIndex.map((entry) => [`${entry.actionId}:${entry.path}`, entry.slot]));
    await this.buildSolverBindings();
    this.rebuildRenderData();
    if (resetCamera) this.resetCameraPending = true;
    this.queueRender();
  }

  private async reloadState(): Promise<void> {
    if (this.drag) return;
    this.state = await getViewerState();
    this.errorEl.textContent = this.state.errors.length > 0
      ? `errors: ${this.state.errors[0].actionId}.${this.state.errors[0].key} ${this.state.errors[0].error}`
      : "";
    await this.solveSketches();
    this.rebuildRenderData();
    if (this.resetCameraPending) {
      this.fitCamera();
      this.resetCameraPending = false;
    }
    this.queueRender();
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
      if (this.drag?.pointerId === event.pointerId) {
        void this.updateDragFrame();
        return;
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
      if (this.drag?.pointerId === event.pointerId) {
        this.canvas.releasePointerCapture(event.pointerId);
        void this.finishDrag();
        return;
      }
      if (this.interaction?.pointerId === event.pointerId) {
        this.canvas.releasePointerCapture(event.pointerId);
        this.interaction = null;
        return;
      }
      if (event.button === 0) {
        void this.pickAtPointer(true);
      }
    });
    this.canvas.addEventListener("wheel", (event) => {
      event.preventDefault();
      zoom(this.camera, event.deltaY);
      this.queueRender();
    }, { passive: false });
  }

  private eventPos(event: PointerEvent): { x: number; y: number } {
    const rect = this.canvas.getBoundingClientRect();
    return { x: event.clientX - rect.left, y: event.clientY - rect.top };
  }

  private async beginPrimaryPointer(event: PointerEvent): Promise<void> {
    const hit = await this.pickAcrossSketches();
    this.hovered = hit;
    this.rebuildRenderData();
    this.queueRender();
    if (!hit || (hit.kind !== "point" && hit.kind !== "dimension")) return;

    const target = hit.kind === "point"
      ? this.resolveDragPoint(hit)
      : this.resolveDragDimension(hit);
    if (!target) return;
    const local = pointerToSketchLocal(this.pointer, this.canvas, this.camera, target.frame);
    if (!local) return;

    this.canvas.setPointerCapture(event.pointerId);
    this.drag = {
      pointerId: event.pointerId,
      sketchId: target.sketchId,
      kind: target.kind,
      pointId: target.pointId,
      constraintIndex: target.constraintIndex,
      xPath: target.xPath,
      yPath: target.yPath,
      target: local,
    };
    const payload = await postViewerPick(hit.pickId);
    this.selectedActionId = payload.selectedId;
    this.startOptions.onDocumentDirty?.();
    await this.solveSketches();
    this.rebuildRenderData();
    this.queueRender();
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
    this.drag = null;
    if (!drag) return;
    if (drag.kind === "label") {
      await patchActionParam(drag.sketchId, drag.xPath, drag.target[0]);
      await patchActionParam(drag.sketchId, drag.yPath, drag.target[1]);
      await this.reloadState();
      this.startOptions.onDocumentDirty?.();
      return;
    }
    const solved = this.solvedSketchParams.get(drag.sketchId);
    const binding = this.solverBindings.get(drag.sketchId);
    if (!solved || !binding) {
      await this.reloadState();
      return;
    }
    const xSlot = binding.localByPath.get(drag.xPath);
    const ySlot = binding.localByPath.get(drag.yPath);
    if (xSlot == null || ySlot == null) {
      await this.reloadState();
      return;
    }
    await patchActionParam(drag.sketchId, drag.xPath, solved[xSlot]);
    await patchActionParam(drag.sketchId, drag.yPath, solved[ySlot]);
    await this.reloadState();
    this.startOptions.onDocumentDirty?.();
  }

  private async updateDragFrame(): Promise<void> {
    this.updateDragTarget();
    await this.solveSketches();
    this.rebuildRenderData();
    this.queueRender();
  }

  private resolveDragPoint(hit: HoverHit): { sketchId: string; pointId: string; frame: SketchFrame } | null {
    if (!this.model) return null;
    const pickable = this.model.pickables.find((candidate) => candidate.pickId === hit.pickId && candidate.case === "PickPoint");
    if (!pickable?.entityId || !pickable.sketchId) return null;
    const sketch = this.renderSketches.find((candidate) => candidate.sketchId === pickable.sketchId);
    if (!sketch) return null;
    return {
      kind: "point",
      sketchId: pickable.sketchId,
      pointId: pickable.entityId,
      xPath: `sketch.entity.${pickable.entityId}.x`,
      yPath: `sketch.entity.${pickable.entityId}.y`,
      frame: sketch.frame,
    };
  }

  private resolveDragDimension(hit: HoverHit): { kind: "label"; sketchId: string; constraintIndex: number; xPath: string; yPath: string; frame: SketchFrame } | null {
    if (!this.model) return null;
    const pickable = this.model.pickables.find((candidate) => candidate.pickId === hit.pickId && candidate.case === "PickDimension");
    if (pickable?.constraintIndex == null || !pickable.sketchId) return null;
    const sketch = this.renderSketches.find((candidate) => candidate.sketchId === pickable.sketchId);
    if (!sketch) return null;
    return {
      kind: "label",
      sketchId: pickable.sketchId,
      constraintIndex: pickable.constraintIndex,
      xPath: `sketch.constraint.${pickable.constraintIndex}.labelPosition.x`,
      yPath: `sketch.constraint.${pickable.constraintIndex}.labelPosition.y`,
      frame: sketch.frame,
    };
  }

  private fitCamera(): void {
    if (!this.model || !this.state || this.model.sketches.length === 0) return;
    let worldMin: Vec3 = [Infinity, Infinity, Infinity];
    let worldMax: Vec3 = [-Infinity, -Infinity, -Infinity];
    for (const frame of this.state.frames) {
      const t = toSketchFrame(frame.transform);
      const p = t.position;
      worldMin = [Math.min(worldMin[0], p[0]), Math.min(worldMin[1], p[1]), Math.min(worldMin[2], p[2])];
      worldMax = [Math.max(worldMax[0], p[0]), Math.max(worldMax[1], p[1]), Math.max(worldMax[2], p[2])];
    }
    for (const sketch of this.model.sketches) {
      const frame = this.sketchFrameFor(sketch.id);
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

  private rebuildRenderData(): void {
    if (!this.model || !this.state) return;
    this.renderSketches = this.model.sketches.map((sketch) => {
      const frame = this.sketchFrameFor(sketch.id);
      const built = buildSketchBuffers(
        sketch,
        this.model!.pickables,
        this.slotLookup,
        this.state!.params,
        this.hovered,
        this.selectedActionId,
        this.fontMetrics,
        this.solverBindings.get(sketch.id),
        this.solvedSketchParams.get(sketch.id),
        this.drag,
      );
      return {
        sketchId: sketch.id,
        frame,
        buffers: built.buffers,
        loops: built.loops,
      };
    });
    this.metaEl.textContent = `${this.model.sketches.length} sketch${this.model.sketches.length === 1 ? "" : "es"}  ·  ${this.model.frames.length} frame${this.model.frames.length === 1 ? "" : "s"}  ·  ${this.model.numSlots} slots`;
  }

  private sketchFrameFor(sketchId: string): SketchFrame {
    const live = this.state?.sketchFrames.find((candidate) => candidate.id === sketchId);
    const fallback = this.model?.sketches.find((candidate) => candidate.id === sketchId)?.sketchFrame;
    return toSketchFrame((live ?? { transform: fallback! }).transform);
  }

  private async buildSolverBindings(): Promise<void> {
    if (!this.model) return;
    const bindings = await Promise.all(this.model.sketches.map(async (sketch) => {
      const binding = await createSketchSolverBinding(sketch, this.slotLookup);
      return [sketch.id, binding] as const;
    }));
    this.solverBindings = new Map(bindings);
    this.solvedSketchParams.clear();
  }

  private async solveSketches(): Promise<void> {
    if (this.solveInFlight) {
      this.solveQueued = true;
      await this.solveInFlight;
      return;
    }
    this.solveInFlight = this.runSolveSketches();
    try {
      await this.solveInFlight;
    } finally {
      this.solveInFlight = null;
      if (this.solveQueued) {
        this.solveQueued = false;
        await this.solveSketches();
      }
    }
  }

  private async runSolveSketches(): Promise<void> {
    if (!this.model || !this.state) return;
    const solved = new Map<string, Float32Array>();
    await Promise.all(this.model.sketches.map(async (sketch) => {
      const binding = this.solverBindings.get(sketch.id);
      if (!binding || binding.localToGlobal.length === 0) return;
      const localParams = new Float32Array(this.solvedSketchParams.get(sketch.id) ?? binding.graph.params);
      for (let i = 0; i < binding.localToGlobal.length; i++) {
        const globalSlot = binding.localToGlobal[i];
        localParams[i] = this.state!.params[globalSlot] ?? localParams[i];
      }
      const pins = this.drag?.sketchId === sketch.id ? buildDragPins(this.drag, binding) : [];
      const result = await solveGraphWithGpu(binding.graph, binding.solver, localParams, pins);
      solved.set(sketch.id, result);
    }));
    this.solvedSketchParams = solved;
  }

  private destroySolverBindings(): void {
    for (const binding of this.solverBindings.values()) binding.solver.destroy();
    this.solverBindings.clear();
    this.solvedSketchParams.clear();
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

  private setStatus(text: string): void {
    this.statusEl.textContent = text;
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

    const frameLineData = this.state ? buildFrameLineData(this.state.frames, this.selectedActionId) : new Float32Array();
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
    }

    pass.end();
    device.queue.submit([encoder.finish()]);
    this.setStatus("Orbit: right-drag · Pan: middle-drag · Pick: left-click");
  }

  private async pickAtPointer(commit: boolean): Promise<void> {
    if (!this.gpu || this.isPicking || this.renderSketches.length === 0) return;
    this.isPicking = true;
    try {
      const hit = await this.pickAcrossSketches();
      this.hovered = hit;
      this.hoverEl.textContent = hit ? `${hit.sketchId} · ${hit.kind} · pick ${hit.pickId}` : "no hover target";
      this.rebuildRenderData();
      this.queueRender();
      if (commit && hit) {
        const payload = await postViewerPick(hit.pickId);
        this.selectedActionId = payload.selectedId;
        this.startOptions.onDocumentDirty?.();
        this.rebuildRenderData();
        this.queueRender();
      }
    } finally {
      this.isPicking = false;
    }
  }

  private async pickAcrossSketches(): Promise<HoverHit | null> {
    if (!this.gpu) return null;
    let best: HoverHit | null = null;
    for (const sketch of this.renderSketches) {
      const hit = await this.runPickPass(sketch);
      if (!hit) continue;
      if (!best || hit.score < best.score) best = hit;
    }
    return best;
  }

  private async runPickPass(sketch: RenderSketch): Promise<HoverHit | null> {
    if (!this.gpu) return null;
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

    let best: HoverHit | null = null;
    for (let i = 0; i < PICK_SAMPLES; i++) {
      const base = i * 16;
      const id = view.getUint32(base, true);
      const kind = view.getUint32(base + 4, true);
      const score = view.getFloat32(base + 8, true);
      if (id === NO_HIT_ID || !Number.isFinite(score)) continue;
      const resolved = pickKind(kind);
      if (!resolved) continue;
      if (!best || score < best.score) {
        best = { pickId: id, kind: resolved, score, sketchId: sketch.sketchId };
      }
    }
    return best;
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
      entries: [{ binding: 0, visibility: GPUShaderStage.VERTEX | GPUShaderStage.COMPUTE, buffer: { type: "uniform" } }],
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
    const pickStateBuffer = device.createBuffer({ size: 16, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST });
    const pickResultBuffer = device.createBuffer({ size: PICK_SAMPLES * 16, usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC });
    return {
      device,
      context,
      format,
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
      pickPipeline,
      pickBindGroupLayout,
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
  hovered: HoverHit | null,
  selectedActionId: string | null,
  fontMetrics: FontMetrics | null,
  solverBinding?: SketchSolverBinding,
  solvedLocal?: Float32Array,
  drag?: DragState | null,
): { buffers: RenderBuffers; loops: ResolvedLoopGeometry[] } {
  const entityMap = new Map(viewerSketch.sketch.entities.map((entity) => [entity.id, entity]));
  const pointMap = new Map<string, Vec2>();
  const triVertices: LineVertex[] = [];
  const lineVertices: LineVertex[] = [];
  const pointInstances: PointInstance[] = [];
  const pickPoints: PickPoint[] = [];
  const pickSegments: PickSegment[] = [];
  const pickCircles: PickCircle[] = [];
  const pickLoopTriangles: PickLoopTriangle[] = [];
  const pickMap = buildPickIndex(pickables, viewerSketch.id);
  const sketchHovered = hovered?.sketchId === viewerSketch.id ? hovered : null;
  const labels: ConstraintLabel[] = [];
  const resolveValue = (path: string, fallback: number) =>
    resolvedSketchValue(solverBinding, solvedLocal, params, slotLookup, viewerSketch.id, path, fallback);
  const resolveLabelAnchor = (index: number, fallback: Vec2, hasExplicit: boolean): Vec2 => {
    if (drag?.kind === "label" && drag.sketchId === viewerSketch.id && drag.constraintIndex === index) return drag.target;
    if (!hasExplicit) return fallback;
    return [
      resolveValue(`sketch.constraint.${index}.labelPosition.x`, fallback[0]),
      resolveValue(`sketch.constraint.${index}.labelPosition.y`, fallback[1]),
    ];
  };

  for (const entity of viewerSketch.sketch.entities) {
    if (entity.case === "REPoint") {
      pointMap.set(entity.id, [
        resolveValue(`sketch.entity.${entity.id}.x`, entity.x),
        resolveValue(`sketch.entity.${entity.id}.y`, entity.y),
      ]);
    }
  }

  const resolvedLoops = viewerSketch.loops
    .map((loop) => resolveLoopGeometry(loop, entityMap, pointMap, resolveValue))
    .filter((loop): loop is ResolvedLoopGeometry => loop !== null);
  for (const loop of resolvedLoops) {
    loop.pickId = pickMap.get(`loop:${loop.id}`) ?? null;
    const hoveredLoop = sketchHovered?.pickId === loop.pickId;
    pushLoopFill(triVertices, pickLoopTriangles, loop.boundary, hoveredLoop ? ACCENT_SOFT : LOOP_FILL, loop.pickId);
  }

  const [minPoint, maxPoint] = computeBounds(viewerSketch.sketch, pointMap, resolveValue);
  pushGrid(lineVertices, minPoint, maxPoint);

  for (const entity of viewerSketch.sketch.entities) {
    if (entity.case === "REPoint") {
      const p = pointMap.get(entity.id);
      if (!p) continue;
      const hoveredPoint = sketchHovered?.pickId === pickMap.get(`point:${entity.id}`);
      const color = hoveredPoint ? ACCENT : SKETCH_POINT;
      pointInstances.push({ x: p[0], y: p[1], radiusPx: hoveredPoint ? 6.5 : 5.0, color });
      const pickId = pickMap.get(`point:${entity.id}`);
      if (pickId != null) pickPoints.push({ x: p[0], y: p[1], radiusPx: 10, pickId });
      continue;
    }

    if (entity.case === "RELine") {
      const a = pointMap.get(entity.startId);
      const b = pointMap.get(entity.endId);
      if (!a || !b) continue;
      const pickId = pickMap.get(`line:${entity.id}`);
      const hoveredLine = sketchHovered?.pickId === pickId;
      const color = hoveredLine ? ACCENT : SKETCH_LINE;
      lineVertices.push({ x: a[0], y: a[1], color }, { x: b[0], y: b[1], color });
      if (pickId != null) pickSegments.push({ a, b, strokePx: 8, pickId, kind: 2 });
      continue;
    }

    if (entity.case === "RECircle") {
      const c = pointMap.get(entity.center);
      if (!c) continue;
      const radius = resolveValue(`sketch.entity.${entity.id}.radius`, entity.radius);
      const pickId = pickMap.get(`circle:${entity.id}`);
      const hoveredCircle = sketchHovered?.pickId === pickId;
      const color = hoveredCircle ? ACCENT : SKETCH_LINE;
      pushCircle(lineVertices, c, radius, color);
      if (pickId != null) pickCircles.push({ center: c, radius, strokePx: 8, pickId });
      continue;
    }

    if (entity.case === "REArc" && entity.data.case === "ArcCenter") {
      const s = pointMap.get(entity.startId);
      const e = pointMap.get(entity.endId);
      const c = pointMap.get(entity.data.center);
      if (!s || !e || !c) continue;
      const pickId = pickMap.get(`arc:${entity.id}`);
      const hoveredArc = sketchHovered?.pickId === pickId;
      const color = hoveredArc ? ACCENT : SKETCH_LINE;
      pushArc(lineVertices, pickSegments, s, e, c, entity.data.clockwise, color, pickId);
    }
  }

  viewerSketch.sketch.constraints.forEach((constraint, index) => {
    pushConstraintGeometry(
      lineVertices,
      labels,
      pointMap,
      entityMap,
      resolveValue,
      resolveLabelAnchor,
      constraint,
      index,
      pickMap.get(`dimension:${index}`) ?? null,
      sketchHovered,
    );
  });

  return {
    buffers: {
      triData: flattenLines(triVertices),
      lineData: flattenLines(lineVertices),
      pointData: flattenPoints(pointInstances),
      pickPoints: flattenPickPoints(pickPoints),
      pickLines: flattenPickSegments(pickSegments),
      pickCircles: flattenPickCircles(pickCircles),
      pickLoops: flattenPickLoopTriangles(pickLoopTriangles),
      pickLabels: fontMetrics ? flattenPickLabelRects(buildLabelPickRects(labels, fontMetrics)) : new Float32Array(),
      labelData: fontMetrics ? buildLabelVertices(labels, fontMetrics) : new Float32Array(),
    },
    loops: resolvedLoops,
  };
}

function pushConstraintGeometry(
  lines: LineVertex[],
  labels: ConstraintLabel[],
  pointMap: Map<string, Vec2>,
  entityMap: Map<string, RenderEntity>,
  resolveValue: (path: string, fallback: number) => number,
  resolveLabelAnchor: (index: number, fallback: Vec2, hasExplicit: boolean) => Vec2,
  constraint: ActionSketch["constraints"][number],
  constraintIndex: number,
  pickId: number | null,
  hovered: HoverHit | null,
): void {
  const color = hovered?.kind === "dimension" ? DIM_HOVER : DIM_COLOR;
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
        lines.push({ x: mid[0] - 0.8, y: mid[1], color }, { x: mid[0] + 0.8, y: mid[1], color });
      } else {
        lines.push({ x: mid[0], y: mid[1] - 0.8, color }, { x: mid[0], y: mid[1] + 0.8, color });
      }
      return;
    }
    case "Distance": {
      const a = pointMap.get(constraint.a);
      const b = pointMap.get(constraint.b);
      if (!a || !b) return;
      const dir = norm2(sub2(b, a));
      const n = perp(dir);
      const mid = scale2(add2(a, b), 0.5);
      const fallbackAnchor = add2(mid, scale2(n, 1.8));
      const anchor = resolveLabelAnchor(constraintIndex, fallbackAnchor, constraint.labelPosition != null);
      const offsetAmount = dot2(sub2(anchor, mid), n);
      const off = scale2(n, Math.abs(offsetAmount) < 0.5 ? 1.8 : offsetAmount);
      const aa = add2(a, off);
      const bb = add2(b, off);
      lines.push({ x: a[0], y: a[1], color }, { x: aa[0], y: aa[1], color });
      lines.push({ x: b[0], y: b[1], color }, { x: bb[0], y: bb[1], color });
      lines.push({ x: aa[0], y: aa[1], color }, { x: bb[0], y: bb[1], color });
      labels.push({
        text: formatNumber(constraint.distance),
        anchor,
        pickId,
        hovered: hovered?.kind === "dimension",
      });
      return;
    }
    case "CircleDiameter": {
      const center = pointMap.get(constraint.center);
      const circle = entityMap.get(constraint.circle);
      if (!center || !circle || circle.case !== "RECircle") return;
      const radius = resolveValue(`sketch.entity.${circle.id}.radius`, circle.radius);
      const fallbackAnchor: Vec2 = [center[0], center[1] + radius + 2.4];
      const anchor = resolveLabelAnchor(constraintIndex, fallbackAnchor, constraint.labelPosition != null);
      const axis = norm2(sub2(anchor, center));
      const dir = len2(axis) < 1e-6 ? ([0, 1] as Vec2) : axis;
      lines.push(
        { x: center[0] - dir[0] * radius, y: center[1] - dir[1] * radius, color },
        { x: center[0] + dir[0] * radius, y: center[1] + dir[1] * radius, color },
      );
      labels.push({
        text: `⌀ ${formatNumber(constraint.diameter)}`,
        anchor,
        pickId,
        hovered: hovered?.kind === "dimension",
      });
      return;
    }
    case "Angle": {
      const a0 = pointMap.get(constraint.aStart);
      const a1 = pointMap.get(constraint.aEnd);
      const b0 = pointMap.get(constraint.bStart);
      const b1 = pointMap.get(constraint.bEnd);
      if (!a0 || !a1 || !b0 || !b1) return;
      const vertex = a0;
      const ra = norm2(sub2(a1, a0));
      const rb = norm2(sub2(b1, b0));
      const fallback = len2(norm2(add2(ra, rb))) < 1e-6 ? [vertex[0] + 2.6, vertex[1] + 2.6] : add2(vertex, scale2(norm2(add2(ra, rb)), 4.4));
      const anchor = resolveLabelAnchor(constraintIndex, fallback, constraint.labelPosition != null);
      const r = Math.max(2.4, len2(sub2(anchor, vertex)) - 0.8);
      lines.push({ x: vertex[0], y: vertex[1], color }, { x: vertex[0] + ra[0] * r, y: vertex[1] + ra[1] * r, color });
      lines.push({ x: vertex[0], y: vertex[1], color }, { x: vertex[0] + rb[0] * r, y: vertex[1] + rb[1] * r, color });
      labels.push({
        text: `${formatNumber(constraint.angleDegrees)}°`,
        anchor,
        pickId,
        hovered: hovered?.kind === "dimension",
      });
      return;
    }
    default:
      return;
  }
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

function pushGrid(lines: LineVertex[], min: Vec2, max: Vec2): void {
  const pad = 8;
  const loX = Math.floor((min[0] - pad) / 5) * 5;
  const hiX = Math.ceil((max[0] + pad) / 5) * 5;
  const loY = Math.floor((min[1] - pad) / 5) * 5;
  const hiY = Math.ceil((max[1] + pad) / 5) * 5;
  for (let x = loX; x <= hiX; x += 1) {
    const color = x % 5 === 0 ? GRID_MAJOR : GRID_MINOR;
    lines.push({ x, y: loY, color }, { x, y: hiY, color });
  }
  for (let y = loY; y <= hiY; y += 1) {
    const color = y % 5 === 0 ? GRID_MAJOR : GRID_MINOR;
    lines.push({ x: loX, y, color }, { x: hiX, y, color });
  }
  lines.push({ x: loX, y: 0, color: AXIS }, { x: hiX, y: 0, color: AXIS });
  lines.push({ x: 0, y: loY, color: AXIS }, { x: 0, y: hiY, color: AXIS });
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

function buildFrameLineData(frames: ViewerFrame[], selectedActionId: string | null): Float32Array {
  const data: number[] = [];
  for (const frame of frames) {
    const t = toSketchFrame(frame.transform);
    const axisPx = frame.id === "origin" ? 64 : 52;
    const alpha = selectedActionId === frame.id ? 1.0 : 0.88;
    pushFrameAxis(data, t.position, t.xAxis, axisPx, [0.88, 0.42, 0.42, alpha]);
    pushFrameAxis(data, t.position, t.yAxis, axisPx, [0.48, 0.78, 0.54, alpha]);
    pushFrameAxis(data, t.position, t.zAxis, axisPx, [0.45, 0.56, 0.92, alpha]);
  }
  return new Float32Array(data);
}

function pushFrameAxis(target: number[], origin: Vec3, axis: Vec3, axisPx: number, color: readonly number[]): void {
  target.push(origin[0], origin[1], origin[2], axis[0], axis[1], axis[2], axisPx, 0, color[0], color[1], color[2], color[3]);
  target.push(origin[0], origin[1], origin[2], axis[0], axis[1], axis[2], axisPx, 1, color[0], color[1], color[2], color[3]);
}

function slotValue(params: number[], slotLookup: Map<string, number>, actionId: string, path: string, fallback: number): number {
  const slot = slotLookup.get(`${actionId}:${path}`);
  return slot == null ? fallback : (params[slot] ?? fallback);
}

function resolvedSketchValue(
  solverBinding: SketchSolverBinding | undefined,
  solvedLocal: Float32Array | undefined,
  params: number[],
  slotLookup: Map<string, number>,
  actionId: string,
  path: string,
  fallback: number,
): number {
  const localSlot = solverBinding?.localByPath.get(path);
  if (localSlot != null && solvedLocal && localSlot < solvedLocal.length) return solvedLocal[localSlot];
  return slotValue(params, slotLookup, actionId, path, fallback);
}

async function createSketchSolverBinding(
  sketch: ViewerSketch,
  slotLookup: Map<string, number>,
): Promise<SketchSolverBinding> {
  const localByPath = new Map<string, number>();
  let localSlot = 0;
  for (const entity of sketch.sketch.entities) {
    switch (entity.case) {
      case "REPoint":
        localByPath.set(`sketch.entity.${entity.id}.x`, localSlot++);
        localByPath.set(`sketch.entity.${entity.id}.y`, localSlot++);
        break;
      case "RECircle":
        localByPath.set(`sketch.entity.${entity.id}.radius`, localSlot++);
        break;
      case "REArc":
        if (entity.data.case === "ArcThreePoint") {
          localByPath.set(`sketch.entity.${entity.id}.throughX`, localSlot++);
          localByPath.set(`sketch.entity.${entity.id}.throughY`, localSlot++);
        }
        break;
      default:
        break;
    }
  }

  const localToGlobal = new Array<number>(localByPath.size);
  for (const [path, local] of localByPath) {
    const globalSlot = slotLookup.get(`${sketch.id}:${path}`);
    if (globalSlot == null) {
      throw new Error(`Missing slot for ${sketch.id}:${path}`);
    }
    localToGlobal[local] = globalSlot;
  }

  const solver = await createGpuSolver(sketch.graph, 1);
  const varIndexByLocal = new Map<number, number>();
  for (let i = 0; i < sketch.graph.varSlots.length; i++) {
    varIndexByLocal.set(sketch.graph.varSlots[i], i);
  }
  return {
    graph: sketch.graph,
    solver,
    localByPath,
    localToGlobal,
    varIndexByLocal,
  };
}

function buildDragPins(drag: DragState, binding: SketchSolverBinding): SolverPin[] {
  const xLocal = binding.localByPath.get(drag.xPath);
  const yLocal = binding.localByPath.get(drag.yPath);
  if (xLocal == null || yLocal == null) return [];
  const xVar = binding.varIndexByLocal.get(xLocal);
  const yVar = binding.varIndexByLocal.get(yLocal);
  if (xVar == null || yVar == null) return [];
  const weight = 20;
  return [
    { localSlot: xLocal, varIndex: xVar, target: drag.target[0], weight },
    { localSlot: yLocal, varIndex: yVar, target: drag.target[1], weight },
  ];
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

function toCssColor(color: readonly number[]): string {
  return `rgba(${Math.round(color[0] * 255)}, ${Math.round(color[1] * 255)}, ${Math.round(color[2] * 255)}, ${color[3]})`;
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
