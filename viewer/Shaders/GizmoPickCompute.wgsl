// Translate-gizmo pick compute shader.
//
// Third compute dispatch of the pick pass (after sketch entities and
// frame origins). 25 threads, 5×5 pixel window around the cursor.
// Tests axis-segments and plane-quads against the pixel and writes
// the best hit into the shared `samples` buffer.
//
// Axis buffer layout — 2 × vec4 per axis:
//   vec4[0] = (anchor.xyz, length_px)
//   vec4[1] = (dir.xyz,    pickId as f32)
//
// Plane buffer layout — 3 × vec4 per plane:
//   vec4[0] = (origin.xyz, pickId as f32)
//   vec4[1] = (axisU.xyz,  near_px)
//   vec4[2] = (axisV.xyz,  far_px)
//
// Both buffers are small (≤ 6 entries each — 3 axes + 3 planes for the
// currently-selected translate). Empty when no translate is selected.

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
const AXIS_THRESHOLD_PX: f32 = 10.0;
const PLANE_PADDING_PX: f32 = 3.0;

@group(0) @binding(0) var<uniform> cam: Camera;
@group(0) @binding(1) var<uniform> pick: PickState;
@group(0) @binding(2) var<storage, read> axes: array<vec4<f32>>;
@group(0) @binding(3) var<storage, read> planes: array<vec4<f32>>;
@group(0) @binding(4) var<storage, read_write> samples: array<PickSample>;

fn project_world(pos: vec3<f32>) -> vec2<f32> {
  let rel = pos - cam.eye;
  let u = dot(rel, cam.right) / max(cam.aspect * cam.view_half_h, 1e-6);
  let v = dot(rel, cam.up) / max(cam.view_half_h, 1e-6);
  let ndc_x = (u + 1.0) * 0.5;
  let ndc_y = (1.0 - v) * 0.5;
  return vec2<f32>(ndc_x * pick.viewport.x, ndc_y * pick.viewport.y);
}

fn distance_to_segment(p: vec2<f32>, a: vec2<f32>, b: vec2<f32>) -> f32 {
  let ab = b - a;
  let len2 = dot(ab, ab);
  if (len2 < 1e-6) { return length(p - a); }
  let t = clamp(dot(p - a, ab) / len2, 0.0, 1.0);
  let q = a + t * ab;
  return length(p - q);
}

fn side(a: vec2<f32>, b: vec2<f32>, p: vec2<f32>) -> f32 {
  // Signed cross product, (b - a) × (p - a). Sign flips across the line AB.
  let ab = b - a;
  let ap = p - a;
  return ab.x * ap.y - ab.y * ap.x;
}

fn point_in_quad(p: vec2<f32>, c0: vec2<f32>, c1: vec2<f32>, c2: vec2<f32>, c3: vec2<f32>) -> bool {
  let s0 = side(c0, c1, p);
  let s1 = side(c1, c2, p);
  let s2 = side(c2, c3, p);
  let s3 = side(c3, c0, p);
  let d = PLANE_PADDING_PX;
  let all_pos = s0 >= -d && s1 >= -d && s2 >= -d && s3 >= -d;
  let all_neg = s0 <=  d && s1 <=  d && s2 <=  d && s3 <=  d;
  return all_pos || all_neg;
}

@compute @workgroup_size(25)
fn cs_main(@builtin(local_invocation_index) index: u32) {
  let gx = i32(index % PICK_GRID) - 2;
  let gy = i32(index / PICK_GRID) - 2;
  let pixel = pick.mouse + vec2<f32>(f32(gx), f32(gy));
  let wpp = (2.0 * cam.view_half_h) / max(pick.viewport.y, 1.0);

  var best_id: u32 = NO_HIT;
  var best_score: f32 = 1e9;

  // Axes (2 vec4 per entry).
  let axisPairs = arrayLength(&axes) / 2u;
  for (var i: u32 = 0u; i < axisPairs; i = i + 1u) {
    let v0 = axes[i * 2u + 0u];
    let v1 = axes[i * 2u + 1u];
    let anchor = v0.xyz;
    let length_px = v0.w;
    let dir = v1.xyz;
    let pickId = u32(v1.w + 0.5);
    let tip = anchor + dir * (length_px * wpp);
    let p0 = project_world(anchor);
    let p1 = project_world(tip);
    let d = distance_to_segment(pixel, p0, p1);
    if (d <= AXIS_THRESHOLD_PX && d < best_score) {
      best_score = d;
      best_id = pickId;
    }
  }

  // Planes (3 vec4 per entry).
  let planeTriples = arrayLength(&planes) / 3u;
  for (var i: u32 = 0u; i < planeTriples; i = i + 1u) {
    let v0 = planes[i * 3u + 0u];
    let v1 = planes[i * 3u + 1u];
    let v2 = planes[i * 3u + 2u];
    let origin = v0.xyz;
    let pickId = u32(v0.w + 0.5);
    let axisU = v1.xyz;
    let near_px = v1.w;
    let axisV = v2.xyz;
    let far_px = v2.w;

    let c00 = origin + axisU * (near_px * wpp) + axisV * (near_px * wpp);
    let c10 = origin + axisU * (far_px  * wpp) + axisV * (near_px * wpp);
    let c11 = origin + axisU * (far_px  * wpp) + axisV * (far_px  * wpp);
    let c01 = origin + axisU * (near_px * wpp) + axisV * (far_px  * wpp);

    let p00 = project_world(c00);
    let p10 = project_world(c10);
    let p11 = project_world(c11);
    let p01 = project_world(c01);

    if (point_in_quad(pixel, p00, p10, p11, p01)) {
      // Score = distance to the projected center. Higher-base than a
      // tight axis hit so axes win ties via the GPU pass; the core's
      // `selectionPriority` also ranks handles (axes = planes here —
      // both TargetGizmoHandle — but the lower score makes axes win).
      let center = (p00 + p10 + p11 + p01) * 0.25;
      let d = length(pixel - center);
      if (d < best_score) {
        best_score = d;
        best_id = pickId;
      }
    }
  }

  let out = pick.sample_base + index;
  samples[out].id = best_id;
  samples[out].kind = 5u;
  samples[out].score = best_score;
  samples[out]._pad = 0.0;
}
