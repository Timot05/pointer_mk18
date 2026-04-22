// Sketch-entity pick compute shader.
//
// Dispatched once per visible sketch. Each workgroup fires 25 threads
// — one per pixel in a 5×5 grid around the cursor. Each thread:
//   1. Builds an orthographic world ray from pixel → world.
//   2. Intersects the ray with the sketch plane (derived from
//      `frame.x_axis` × `frame.y_axis`).
//   3. Converts the hit point into sketch-local 2D coords.
//   4. SDF-tests the point against every entry in the sketch's
//      `points` / `lines` / `circles` / `loops` / `labels` storage
//      arrays, keeping the best hit by (priority, score).
//   5. Writes its best hit into `samples[pick.sample_base + index]`.
//
// Priorities (lower = wins over higher, matches `Pickable.priority`):
//   0 = point
//   1 = line / circle / arc
//   2 = dim label
//   3 = frame (handled in a separate pass)
//   4 = loop
//
// Kinds are informational only — the CPU dedup looks pick ids up in
// `state.Compiled.Pickables` to recover the actual target kind.

struct Camera {
  eye: vec3<f32>, _p0: f32,
  forward: vec3<f32>, _p1: f32,
  right: vec3<f32>, view_half_h: f32,
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

@group(0) @binding(0) var<uniform> cam: Camera;
@group(0) @binding(1) var<uniform> frame: SketchFrame;
@group(0) @binding(2) var<uniform> pick: PickState;
@group(0) @binding(3) var<storage, read> points: array<vec4<f32>>;
@group(0) @binding(4) var<storage, read> lines: array<vec4<f32>>;
@group(0) @binding(5) var<storage, read> circles: array<vec4<f32>>;
@group(0) @binding(6) var<storage, read> loops: array<vec4<f32>>;
@group(0) @binding(7) var<storage, read> labels: array<vec4<f32>>;
@group(0) @binding(8) var<storage, read_write> samples: array<PickSample>;

/// Intersect an orthographic world ray from pixel with the sketch
/// plane. Returns (sketch_x, sketch_y, t); t = 1e9 on miss.
fn pixel_to_sketch(pixel: vec2<f32>) -> vec3<f32> {
  let uv = pixel / max(pick.viewport, vec2<f32>(1.0, 1.0));
  let ndc_x = uv.x * 2.0 - 1.0;
  let ndc_y = 1.0 - uv.y * 2.0;
  let origin = cam.eye
             + ndc_x * cam.aspect * cam.view_half_h * cam.right
             + ndc_y * cam.view_half_h * cam.up;
  let dir = cam.forward;
  let normal = cross(frame.x_axis.xyz, frame.y_axis.xyz);
  let denom = dot(normal, dir);
  if (abs(denom) < 1e-6) { return vec3<f32>(1e9, 1e9, 1e9); }
  let t = dot(normal, frame.pos.xyz - origin) / denom;
  if (t < 0.0) { return vec3<f32>(1e9, 1e9, 1e9); }
  let world = origin + t * dir;
  let rel = world - frame.pos.xyz;
  return vec3<f32>(dot(rel, frame.x_axis.xyz), dot(rel, frame.y_axis.xyz), t);
}

/// World-units-per-pixel on the sketch plane (ortho → independent of t).
fn world_per_px() -> f32 {
  return (2.0 * cam.view_half_h) / max(pick.viewport.y, 1.0);
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

fn pick_better(priority: u32, score: f32, bestPriority: u32, bestScore: f32) -> bool {
  return priority < bestPriority || (priority == bestPriority && score < bestScore);
}

@compute @workgroup_size(25)
fn cs_main(@builtin(local_invocation_index) index: u32) {
  let gx = i32(index % PICK_GRID) - 2;
  let gy = i32(index / PICK_GRID) - 2;
  let pixel = pick.mouse + vec2<f32>(f32(gx), f32(gy));
  let hit = pixel_to_sketch(pixel);

  var best_id: u32 = NO_HIT;
  var best_kind: u32 = 0u;
  var best_priority: u32 = 999u;
  var best_score: f32 = 1e9;

  if (hit.z < 1e8) {
    let p = hit.xy;
    let wpp = world_per_px();

    // ── Points (priority 0) ──
    // Each vec4: (x, y, radius_px, pickId).
    // Radius 0 entries are unused / zero-init slots; skip so they don't
    // produce phantom pickId=0 hits at the sketch origin.
    let pointCount = arrayLength(&points);
    for (var i: u32 = 0u; i < pointCount; i = i + 1u) {
      let e = points[i];
      if (e.z <= 0.0) { continue; }
      let d = length(p - e.xy) / max(wpp, 1e-6);
      if (d <= e.z && pick_better(0u, d, best_priority, best_score)) {
        best_priority = 0u;
        best_score = d;
        best_id = u32(e.w + 0.5);
        best_kind = 1u;
      }
    }

    // ── Lines (priority 1) ──
    // Pairs of vec4: [0]=(x1,y1,x2,y2), [1]=(thickness_px, pickId, _, _).
    // Zero-length segments with thickness=0 are zero-init slots; skip.
    let lineCount = arrayLength(&lines) / 2u;
    for (var i: u32 = 0u; i < lineCount; i = i + 1u) {
      let geom = lines[i * 2u];
      let info = lines[i * 2u + 1u];
      if (info.x <= 0.0) { continue; }
      let d = sdf_segment(p, geom.xy, geom.zw) / max(wpp, 1e-6);
      if (d <= info.x && pick_better(1u, d, best_priority, best_score)) {
        best_priority = 1u;
        best_score = d;
        best_id = u32(info.y + 0.5);
        best_kind = 2u;
      }
    }

    // ── Circles (priority 1) ──
    // Pairs of vec4: [0]=(cx, cy, radius_world, thickness_px), [1]=(pickId, _, _, _).
    // Thickness=0 marks unused zero-init slots; skip.
    let circleCount = arrayLength(&circles) / 2u;
    for (var i: u32 = 0u; i < circleCount; i = i + 1u) {
      let geom = circles[i * 2u];
      let info = circles[i * 2u + 1u];
      if (geom.w <= 0.0) { continue; }
      let d = abs(length(p - geom.xy) - geom.z) / max(wpp, 1e-6);
      if (d <= geom.w && pick_better(1u, d, best_priority, best_score)) {
        best_priority = 1u;
        best_score = d;
        best_id = u32(info.x + 0.5);
        best_kind = 3u;
      }
    }

    // ── Dim labels (priority 2) ──
    // Pairs of vec4: [0]=(anchor_x, anchor_y, min_dx_px, min_dy_px),
    //                [1]=(max_dx_px, max_dy_px, pickId, _).
    // The rect is placed in screen space around the anchor (world)
    // with min/max offsets in pixels; we convert to world using wpp.
    // Degenerate (zero-area) rects mark unused zero-init slots; skip.
    let labelCount = arrayLength(&labels) / 2u;
    for (var i: u32 = 0u; i < labelCount; i = i + 1u) {
      let r0 = labels[i * 2u];
      let r1 = labels[i * 2u + 1u];
      if (r0.z == r1.x && r0.w == r1.y) { continue; }
      let minp = r0.xy + vec2<f32>(r0.z, -r0.w) * wpp;
      let maxp = r0.xy + vec2<f32>(r1.x, -r1.y) * wpp;
      let lo = min(minp, maxp);
      let hi = max(minp, maxp);
      if (all(p >= lo) && all(p <= hi)) {
        let center = (lo + hi) * 0.5;
        let d = 20.0 + length(p - center) / max(wpp, 1e-6);
        if (pick_better(2u, d, best_priority, best_score)) {
          best_priority = 2u;
          best_score = d;
          best_id = u32(r1.z + 0.5);
          best_kind = 6u;
        }
      }
    }

    // ── Loops (priority 4 — lowest) ──
    // Pairs of vec4: [0]=(ax, ay, bx, by), [1]=(cx, cy, pickId, _).
    // Degenerate / zero-area triangles mark unused zero-init slots; skip
    // so the origin-triangle doesn't claim every pick.
    let loopCount = arrayLength(&loops) / 2u;
    for (var i: u32 = 0u; i < loopCount; i = i + 1u) {
      let t0 = loops[i * 2u];
      let t1 = loops[i * 2u + 1u];
      let ab = t0.zw - t0.xy;
      let ac = t1.xy - t0.xy;
      let area2 = abs(ab.x * ac.y - ab.y * ac.x);
      if (area2 < 1e-8) { continue; }
      if (point_in_triangle(p, t0.xy, t0.zw, t1.xy)) {
        let centroid = (t0.xy + t0.zw + t1.xy) / 3.0;
        let d = 50.0 + length(p - centroid);
        if (pick_better(4u, d, best_priority, best_score)) {
          best_priority = 4u;
          best_score = d;
          best_id = u32(t1.z + 0.5);
          best_kind = 5u;
        }
      }
    }
  }

  let out = pick.sample_base + index;
  samples[out].id = best_id;
  samples[out].kind = best_kind;
  samples[out].score = best_score;
  samples[out]._pad = 0.0;
}
