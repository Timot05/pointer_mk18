// Frame-origin pick compute shader.
//
// Single dispatch, 25 threads in a 5×5 grid around the cursor. Each
// thread:
//   1. Projects every frame's world-space origin into screen space.
//   2. Measures pixel distance from its grid pixel to each projected
//      origin; if within the hit radius, records as a candidate.
//   3. Keeps the closest frame (priority 3), writing into the shared
//      `samples` buffer at `pick.sample_base + index`.
//
// Runs after `PickCompute.wgsl` but reads/writes the same samples
// buffer; `sample_base` places its 25 slots past whatever the
// sketch-entity pass wrote.

struct Camera {
  eye: vec3<f32>, _p0: f32,
  forward: vec3<f32>, _p1: f32,
  right: vec3<f32>, view_half_h: f32,
  up: vec3<f32>, aspect: f32,
};

struct PickState {
  viewport: vec2<f32>,
  mouse: vec2<f32>,
  sample_base: u32,
  _pad0: u32,
  _pad1: vec2<u32>,
};

struct PickSample {
  id: u32,
  kind: u32,
  score: f32,
  _pad: f32,
};

const PICK_GRID: u32 = 5u;
const NO_HIT: u32 = 0xFFFFFFFFu;
/// Frame origins have an implicit ~20-pixel hit region — matches the
/// fat gizmo cluster size.
const HIT_RADIUS_PX: f32 = 20.0;

@group(0) @binding(0) var<uniform> cam: Camera;
@group(0) @binding(1) var<uniform> pick: PickState;
@group(0) @binding(2) var<storage, read> origins: array<vec4<f32>>;
@group(0) @binding(3) var<storage, read_write> samples: array<PickSample>;

/// Orthographic world-to-screen projection. Returns (px, py, depth);
/// depth ≤ 0 means behind the camera (still picked — ortho has no
/// near-plane cull here, but the caller can threshold if needed).
fn project_world(pos: vec3<f32>) -> vec3<f32> {
  let rel = pos - cam.eye;
  let u = dot(rel, cam.right) / max(cam.aspect * cam.view_half_h, 1e-6);
  let v = dot(rel, cam.up) / max(cam.view_half_h, 1e-6);
  let z = dot(rel, cam.forward);
  let ndc_x = (u + 1.0) * 0.5;
  let ndc_y = (1.0 - v) * 0.5;
  return vec3<f32>(ndc_x * pick.viewport.x, ndc_y * pick.viewport.y, z);
}

@compute @workgroup_size(25)
fn cs_main(@builtin(local_invocation_index) index: u32) {
  let gx = i32(index % PICK_GRID) - 2;
  let gy = i32(index / PICK_GRID) - 2;
  let pixel = pick.mouse + vec2<f32>(f32(gx), f32(gy));

  var best_id: u32 = NO_HIT;
  var best_score: f32 = 1e9;

  let originCount = arrayLength(&origins);
  for (var i: u32 = 0u; i < originCount; i = i + 1u) {
    let raw = origins[i];
    let screen = project_world(raw.xyz);
    let d = length(pixel - screen.xy);
    if (d <= HIT_RADIUS_PX && d < best_score) {
      best_score = d;
      best_id = u32(raw.w + 0.5);
    }
  }

  let out = pick.sample_base + index;
  samples[out].id = best_id;
  samples[out].kind = 4u;
  samples[out].score = best_score;
  samples[out]._pad = 0.0;
}
