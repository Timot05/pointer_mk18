namespace Server

open System.Text

// Emits the block-probe raymarch shaders used by the `Raymarch` viewer
// mode. Two shader modules come out of one call:
//
//   1. Compute module — three entry points:
//        * front_probe_main  sphere-traces from tNear forward for each 8×8
//          screen block; writes per-block tStart (or tFar on miss)
//        * back_probe_main   sphere-traces from tFar backward; writes tEnd
//        * analysis_main     for every block that hit, computes the world
//          AABB of the block × [tStart, tEnd], evaluates each surface's
//          interval SDF over that box, and builds a u32 alive-mask (bit i
//          set iff surface i's interval could contain its isovalue).
//
//   2. Fragment module — vs_main + fs_main. fs_main reads per-block
//      tStart/tEnd and alive-mask, discards missed blocks, then runs the
//      per-pixel sphere trace starting at tStart and iterating only alive
//      surfaces.
//
// The split is forced by WGSL storage-buffer access rules: the front /
// back / mask buffers need `read_write` in compute and `read` in
// fragment, which can't coexist in a single module.

module GpuIsosurface =

    let private ff (v: float) =
        if abs v < 1e-12 then "0.0" else sprintf "%.10f" v

    type RaymarchShaders =
        { ComputeWgsl: string
          FragmentWgsl: string
          SurfaceCount: int }

    // ── WGSL source snippets (module-level to avoid F# offside-rule
    //     issues with triple-quoted strings inside function bodies). ─────

    let private SHARED_STRUCTS = """
struct Camera {
  eye: vec3<f32>, _p0: f32,
  forward: vec3<f32>, _p1: f32,
  right: vec3<f32>, view_half_h: f32,
  up: vec3<f32>, aspect: f32,
}
struct Slots { v: array<f32>, }
struct SurfaceState { colorOpacity: vec4<f32>, isoEnabled: vec4<f32>, }
struct Cfg {
  canvas_w: f32, canvas_h: f32,
  block_size: u32, blocks_x: u32,
  blocks_y: u32, max_probe_steps: u32,
  max_pixel_steps: u32, _pad0: u32,
  t_near: f32, t_far: f32,
  threshold: f32, _pad1: f32,
}
"""

    let private COMPUTE_BINDINGS = """
@group(0) @binding(0) var<uniform> cam: Camera;
@group(1) @binding(0) var<storage, read> slots: Slots;
@group(2) @binding(0) var<storage, read> surfaceStates: array<SurfaceState>;
@group(3) @binding(0) var<uniform> cfg: Cfg;
@group(3) @binding(1) var<storage, read_write> frontDepths: array<f32>;
@group(3) @binding(2) var<storage, read_write> backDepths: array<f32>;
@group(3) @binding(3) var<storage, read_write> masks: array<u32>;
// Scene-wide alive mask — one u32 produced by `global_analyze_main`
// over the full frustum AABB. Read by `front_walk_main` to skip
// surfaces that can't touch the scene, and by `analysis_main` to avoid
// evaluating their per-block interval at all.
@group(3) @binding(4) var<storage, read_write> globalMask: array<u32>;
"""

    let private FRAGMENT_BINDINGS = """
@group(0) @binding(0) var<uniform> cam: Camera;
@group(1) @binding(0) var<storage, read> slots: Slots;
@group(2) @binding(0) var<storage, read> surfaceStates: array<SurfaceState>;
@group(3) @binding(0) var<uniform> cfg: Cfg;
@group(3) @binding(1) var<storage, read> frontDepths: array<f32>;
@group(3) @binding(2) var<storage, read> backDepths: array<f32>;
@group(3) @binding(3) var<storage, read> masks: array<u32>;
"""

    /// Common block-space helpers used by both probe + analysis.
    /// aabb_axis tightens the world-AABB to the parallelepiped swept by
    /// the block's rays over [tStart, tEnd]; each world-axis component
    /// is linear in (u, v, t) so we handle the coefficient's sign by
    /// min/max of the endpoints.
    let private BLOCK_HELPERS = """
fn block_center_uv(bx: u32, by: u32) -> vec2<f32> {
  let px = f32(bx * cfg.block_size) + 0.5 * f32(cfg.block_size);
  let py = f32(by * cfg.block_size) + 0.5 * f32(cfg.block_size);
  let u = (px / cfg.canvas_w) * 2.0 - 1.0;
  let v = 1.0 - (py / cfg.canvas_h) * 2.0;
  return vec2<f32>(u, v);
}

fn world_ray_origin(uv: vec2<f32>) -> vec3<f32> {
  return cam.eye
    + uv.x * cam.aspect * cam.view_half_h * cam.right
    + uv.y * cam.view_half_h * cam.up;
}

fn aabb_axis(
    u_lo: f32, u_hi: f32, u_c: f32,
    v_lo: f32, v_hi: f32, v_c: f32,
    t_lo: f32, t_hi: f32, t_c: f32,
    base: f32) -> Intv {
  let u0 = u_lo * u_c; let u1 = u_hi * u_c;
  let v0 = v_lo * v_c; let v1 = v_hi * v_c;
  let t0 = t_lo * t_c; let t1 = t_hi * t_c;
  let lo = base + min(u0, u1) + min(v0, v1) + min(t0, t1);
  let hi = base + max(u0, u1) + max(v0, v1) + max(t0, t1);
  return Intv(lo, hi);
}
"""

    /// Front + walk: one forward pass that finds both the earliest and
    /// latest t a block could hit. Sphere-traces through empty space
    /// (step = scene_dist), then once inside the threshold tube switches
    /// to fixed diagonal steps (step = 2 × threshold) so we keep sampling
    /// through surfaces instead of decelerating to a standstill at a
    /// zero-crossing. Tracks first and last t where scene_dist <
    /// threshold; the last-hit t gives a much tighter tEnd than a
    /// separate back probe would, in a single dispatch.
    let private FRONT_WALK_WGSL = """
@compute @workgroup_size(8, 8, 1)
fn front_walk_main(@builtin(global_invocation_id) gid: vec3<u32>) {
  if (gid.x >= cfg.blocks_x || gid.y >= cfg.blocks_y) { return; }
  let uv = block_center_uv(gid.x, gid.y);
  let ro = world_ray_origin(uv);
  let rd = cam.forward;
  let walk_step = 2.0 * cfg.threshold;
  let gm = globalMask[0];

  var t: f32 = cfg.t_near;
  var found: bool = false;
  var first_hit: f32 = cfg.t_far;
  var last_hit: f32 = cfg.t_far;

  for (var i: u32 = 0u; i < cfg.max_probe_steps; i = i + 1u) {
    if (t >= cfg.t_far) { break; }
    let d = scene_dist(ro + rd * t, gm);
    if (d < cfg.threshold) {
      if (!found) { first_hit = t; found = true; }
      last_hit = t;
      t = t + walk_step;
    } else {
      t = t + max(d, 1e-4);
    }
  }

  let idx = gid.y * cfg.blocks_x + gid.x;
  if (found) {
    frontDepths[idx] = max(cfg.t_near, first_hit - cfg.threshold);
    backDepths[idx]  = min(cfg.t_far, last_hit + cfg.threshold);
  } else {
    frontDepths[idx] = cfg.t_far;
    backDepths[idx]  = cfg.t_far;
  }
}
"""

    /// Global analysis: one thread, one interval evaluation over the
    /// whole frustum. Sets a bit per surface whose isovalue is inside
    /// its whole-scene interval. Everything downstream skips surfaces
    /// with their bit cleared — both the probe's per-step `scene_dist`
    /// and the per-block `analysis_main`.
    let private GLOBAL_ANALYSIS_PRELUDE_WGSL = """
@compute @workgroup_size(1, 1, 1)
fn global_analyze_main(@builtin(global_invocation_id) gid: vec3<u32>) {
  if (gid.x != 0u || gid.y != 0u) { return; }

  let u_lo = -1.0; let u_hi = 1.0;
  let v_lo = -1.0; let v_hi = 1.0;
  let t_lo = cfg.t_near; let t_hi = cfg.t_far;

  let A = cam.aspect * cam.view_half_h;
  let B = cam.view_half_h;
  let xi = aabb_axis(u_lo, u_hi, A * cam.right.x,
                     v_lo, v_hi, B * cam.up.x,
                     t_lo, t_hi, cam.forward.x,
                     cam.eye.x);
  let yi = aabb_axis(u_lo, u_hi, A * cam.right.y,
                     v_lo, v_hi, B * cam.up.y,
                     t_lo, t_hi, cam.forward.y,
                     cam.eye.y);
  let zi = aabb_axis(u_lo, u_hi, A * cam.right.z,
                     v_lo, v_hi, B * cam.up.z,
                     t_lo, t_hi, cam.forward.z,
                     cam.eye.z);
  let ibox = IntvBox(xi, yi, zi);

  var mask: u32 = 0u;
"""

    /// Analysis: for each block whose front probe hit something, build
    /// the world AABB of the block region × [tStart, tEnd] and run
    /// interval-SDF evaluation per surface. A surface is alive iff its
    /// interval contains its isovalue — meaning the isosurface could
    /// pass through the block.
    ///
    /// Per-surface mask bit setting code is emitted after this prelude.
    let private ANALYSIS_PRELUDE_WGSL = """
@compute @workgroup_size(8, 8, 1)
fn analysis_main(@builtin(global_invocation_id) gid: vec3<u32>) {
  if (gid.x >= cfg.blocks_x || gid.y >= cfg.blocks_y) { return; }
  let idx = gid.y * cfg.blocks_x + gid.x;
  let tStart = frontDepths[idx];
  let tEnd   = backDepths[idx];
  let gm = globalMask[0];

  if (tStart >= cfg.t_far || gm == 0u) {
    masks[idx] = 0u;
    return;
  }

  let px_lo = f32(gid.x * cfg.block_size);
  let px_hi = px_lo + f32(cfg.block_size);
  let py_lo = f32(gid.y * cfg.block_size);
  let py_hi = py_lo + f32(cfg.block_size);
  let u_lo = (px_lo / cfg.canvas_w) * 2.0 - 1.0;
  let u_hi = (px_hi / cfg.canvas_w) * 2.0 - 1.0;
  let v_hi = 1.0 - (py_lo / cfg.canvas_h) * 2.0;
  let v_lo = 1.0 - (py_hi / cfg.canvas_h) * 2.0;
  let t_lo = tStart;
  let t_hi = max(t_lo, tEnd);

  let A = cam.aspect * cam.view_half_h;
  let B = cam.view_half_h;
  let xi = aabb_axis(u_lo, u_hi, A * cam.right.x,
                     v_lo, v_hi, B * cam.up.x,
                     t_lo, t_hi, cam.forward.x,
                     cam.eye.x);
  let yi = aabb_axis(u_lo, u_hi, A * cam.right.y,
                     v_lo, v_hi, B * cam.up.y,
                     t_lo, t_hi, cam.forward.y,
                     cam.eye.y);
  let zi = aabb_axis(u_lo, u_hi, A * cam.right.z,
                     v_lo, v_hi, B * cam.up.z,
                     t_lo, t_hi, cam.forward.z,
                     cam.eye.z);
  let ibox = IntvBox(xi, yi, zi);

  var mask: u32 = 0u;
"""

    let private FRAGMENT_PROJECT_AND_VS_WGSL = """
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
  let h = cam.view_half_h;
  let w = cam.aspect * h;
  let proj = mat4x4<f32>(
    vec4<f32>(1.0 / w, 0.0, 0.0, 0.0),
    vec4<f32>(0.0, 1.0 / h, 0.0, 0.0),
    vec4<f32>(0.0, 0.0, -1.0 / (far - near), 0.0),
    vec4<f32>(0.0, 0.0, -near / (far - near), 1.0),
  );
  return proj * view * vec4<f32>(pos, 1.0);
}

struct VOut { @builtin(position) clip: vec4<f32>, @location(0) uv: vec2<f32> };

@vertex fn vs_main(@builtin(vertex_index) idx: u32) -> VOut {
  var o: VOut;
  var pos = array<vec2<f32>, 3>(
    vec2<f32>(-1.0, -1.0),
    vec2<f32>(3.0, -1.0),
    vec2<f32>(-1.0, 3.0)
  );
  let p = pos[idx];
  o.clip = vec4<f32>(p, 0.0, 1.0);
  o.uv = p;
  return o;
}

struct FOut {
  @location(0) color: vec4<f32>,
  @builtin(frag_depth) depth: f32,
}
"""

    let private FRAGMENT_PRELUDE_WGSL = """
@fragment fn fs_main(f: VOut) -> FOut {
  let pixel = vec2<u32>(f.clip.xy);
  let bx = pixel.x / cfg.block_size;
  let by = pixel.y / cfg.block_size;
  let blockIdx = by * cfg.blocks_x + bx;
  let tStart = frontDepths[blockIdx];
  let tEnd = backDepths[blockIdx];
  let mask = masks[blockIdx];

  if (tStart >= cfg.t_far || mask == 0u) {
    discard;
    var miss: FOut;
    miss.color = vec4<f32>(0.0, 0.0, 0.0, 0.0);
    miss.depth = 1.0;
    return miss;
  }

  let ro = cam.eye
    + f.uv.x * cam.aspect * cam.view_half_h * cam.right
    + f.uv.y * cam.view_half_h * cam.up;
  let rd = cam.forward;
  var t: f32 = tStart;
  let t_limit = tEnd;
  for (var i: u32 = 0u; i < cfg.max_pixel_steps; i = i + 1u) {
    let p = ro + rd * t;
    var min_d: f32 = 1e10;
    var hit_id: i32 = -1;
"""

    let private FRAGMENT_HIT_AND_EPILOGUE_WGSL = """
    if (hit_id >= 0) {
      let st = surfaceStates[u32(hit_id)];
      var col = st.colorOpacity.xyz;
      let alpha = st.colorOpacity.w;
      let n = estimate_normal(p, hit_id);
      let key_dir = normalize(vec3<f32>(0.4, 0.3, 0.8));
      let fill_dir = normalize(vec3<f32>(-0.5, -0.4, 0.3));
      let key = max(dot(n, key_dir), 0.0) * 0.5;
      let fill = max(dot(n, fill_dir), 0.0) * 0.2;
      let ambient = 0.45;
      col = col * (ambient + key + fill);
      let clip = project_world(p);
      var out: FOut;
      out.color = vec4<f32>(col, alpha);
      out.depth = clip.z / clip.w;
      return out;
    }
    t = t + max(min_d, 0.01);
    if (t > t_limit) { break; }
  }
  discard;
  // WGSL requires a return after `discard` since the function's
  // declared return type is FOut. The values are irrelevant because
  // the fragment is already discarded.
  var miss: FOut;
  miss.color = vec4<f32>(0.0, 0.0, 0.0, 0.0);
  miss.depth = 1.0;
  return miss;
}
"""

    // ── Per-surface snippets ────────────────────────────────────────────

    /// Emit a per-surface `scene_dist(p, gm)` helper returning
    /// min_i |eval_sdf_i(p) - iso_i| across all enabled surfaces, but
    /// only those whose bit is set in `gm` — the scene-wide alive mask
    /// produced by `global_analyze_main`. Surfaces ruled out by the
    /// whole-frustum interval pass cost nothing beyond a bit test.
    let private emitSceneDist (sb: StringBuilder) (surfaces: FieldSurface list) =
        sb.AppendLine("fn scene_dist(p: vec3<f32>, gm: u32) -> f32 {") |> ignore
        sb.AppendLine("  var md: f32 = 1.0e10;") |> ignore
        surfaces |> List.iteri (fun i _ ->
            sb.AppendLine("  {") |> ignore
            sb.AppendLine($"    if ((gm & (1u << {i}u)) != 0u) {{") |> ignore
            sb.AppendLine($"      let st = surfaceStates[{i}];") |> ignore
            sb.AppendLine("      if (st.isoEnabled.y >= 0.5) {") |> ignore
            sb.AppendLine($"        let d = abs(eval_sdf_{i}(p) - st.isoEnabled.x);") |> ignore
            sb.AppendLine("        if (d < md) { md = d; }") |> ignore
            sb.AppendLine("      }") |> ignore
            sb.AppendLine("    }") |> ignore
            sb.AppendLine("  }") |> ignore)
        sb.AppendLine("  return md;") |> ignore
        sb.AppendLine("}") |> ignore

    /// Per-surface mask-setting body. A surface is alive iff its
    /// interval [lo, hi] contains its isovalue — the isosurface could
    /// pass through the box. `gate` is either `None` (unconditional,
    /// used by the whole-frustum global pass) or a WGSL u32 expression
    /// that has to be non-zero for the surface to be considered.
    let private emitMaskFromInterval
            (sb: StringBuilder) (surfaces: FieldSurface list) (gate: string option) =
        surfaces |> List.iteri (fun i _ ->
            sb.AppendLine("  {") |> ignore
            match gate with
            | Some cond ->
                sb.AppendLine($"    if (({cond} & (1u << {i}u)) != 0u) {{") |> ignore
            | None ->
                sb.AppendLine("    if (true) {") |> ignore
            sb.AppendLine($"      let st = surfaceStates[{i}];") |> ignore
            sb.AppendLine("      if (st.isoEnabled.y >= 0.5) {") |> ignore
            sb.AppendLine($"        let iv = interval_sdf_{i}(ibox);") |> ignore
            sb.AppendLine("        let iso = st.isoEnabled.x;") |> ignore
            sb.AppendLine("        if (iv.lo <= iso && iso <= iv.hi) {") |> ignore
            sb.AppendLine($"          mask = mask | (1u << {i}u);") |> ignore
            sb.AppendLine("        }") |> ignore
            sb.AppendLine("      }") |> ignore
            sb.AppendLine("    }") |> ignore
            sb.AppendLine("  }") |> ignore)

    /// Per-surface branch in the fragment's per-pixel trace. Gated by
    /// the alive-mask bit so masked-out surfaces cost nothing beyond a
    /// bit test.
    let private emitFragmentSurface (sb: StringBuilder) (i: int) =
        sb.AppendLine("    {") |> ignore
        sb.AppendLine($"      if ((mask & (1u << {i}u)) != 0u) {{") |> ignore
        sb.AppendLine($"        let st = surfaceStates[{i}];") |> ignore
        sb.AppendLine($"        let d = eval_sdf_{i}(p);") |> ignore
        sb.AppendLine("        let dist_to_iso = abs(d - st.isoEnabled.x);") |> ignore
        sb.AppendLine($"        if (dist_to_iso < {ff 0.01}) {{ hit_id = {i}; }}") |> ignore
        sb.AppendLine("        if (dist_to_iso < min_d) { min_d = dist_to_iso; }") |> ignore
        sb.AppendLine("      }") |> ignore
        sb.AppendLine("    }") |> ignore

    /// Normal: central-difference per surface.
    let private emitEstimateNormal (sb: StringBuilder) (surfaces: FieldSurface list) =
        sb.AppendLine("fn estimate_normal(p: vec3<f32>, surface_id: i32) -> vec3<f32> {") |> ignore
        sb.AppendLine("  let e = 0.01;") |> ignore
        sb.AppendLine("  var n: vec3<f32>;") |> ignore
        sb.AppendLine("  switch surface_id {") |> ignore
        surfaces |> List.iteri (fun i _ ->
            sb.AppendLine($"    case {i}: {{") |> ignore
            sb.AppendLine($"      n = vec3<f32>(") |> ignore
            sb.AppendLine($"        eval_sdf_{i}(p + vec3<f32>(e, 0.0, 0.0)) - eval_sdf_{i}(p - vec3<f32>(e, 0.0, 0.0)),") |> ignore
            sb.AppendLine($"        eval_sdf_{i}(p + vec3<f32>(0.0, e, 0.0)) - eval_sdf_{i}(p - vec3<f32>(0.0, e, 0.0)),") |> ignore
            sb.AppendLine($"        eval_sdf_{i}(p + vec3<f32>(0.0, 0.0, e)) - eval_sdf_{i}(p - vec3<f32>(0.0, 0.0, e))") |> ignore
            sb.AppendLine("      );") |> ignore
            sb.AppendLine("    }") |> ignore)
        sb.AppendLine("    default: { n = vec3<f32>(0.0, 0.0, 1.0); }") |> ignore
        sb.AppendLine("  }") |> ignore
        sb.AppendLine("  return normalize(n);") |> ignore
        sb.AppendLine("}") |> ignore

    // ── Compute shader ──────────────────────────────────────────────────

    let private computeWgsl (surfaces: FieldSurface list) =
        let sb = StringBuilder()
        sb.Append(SHARED_STRUCTS) |> ignore
        sb.Append(COMPUTE_BINDINGS) |> ignore
        sb.AppendLine(GpuSdf.generateEvalFunctions surfaces) |> ignore
        sb.AppendLine(GpuSdf.generateIntervalFunctions surfaces) |> ignore
        sb.Append(BLOCK_HELPERS) |> ignore
        emitSceneDist sb surfaces
        // 1. Whole-frustum alive-mask pre-pass.
        sb.Append(GLOBAL_ANALYSIS_PRELUDE_WGSL) |> ignore
        emitMaskFromInterval sb surfaces None
        sb.AppendLine("  globalMask[0] = mask;") |> ignore
        sb.AppendLine("}") |> ignore
        // 2. Front + walk probe (uses globalMask to skip dead surfaces).
        sb.Append(FRONT_WALK_WGSL) |> ignore
        // 3. Per-block analysis (further tightens by per-block AABB).
        sb.Append(ANALYSIS_PRELUDE_WGSL) |> ignore
        emitMaskFromInterval sb surfaces (Some "gm")
        sb.AppendLine("  masks[idx] = mask;") |> ignore
        sb.AppendLine("}") |> ignore
        sb.ToString()

    // ── Fragment shader ─────────────────────────────────────────────────

    let private fragmentWgsl (surfaces: FieldSurface list) =
        let sb = StringBuilder()
        sb.Append(SHARED_STRUCTS) |> ignore
        sb.Append(FRAGMENT_BINDINGS) |> ignore
        sb.AppendLine(GpuSdf.generateEvalFunctions surfaces) |> ignore
        emitEstimateNormal sb surfaces
        sb.Append(FRAGMENT_PROJECT_AND_VS_WGSL) |> ignore
        sb.Append(FRAGMENT_PRELUDE_WGSL) |> ignore
        surfaces |> List.iteri (fun i _ -> emitFragmentSurface sb i)
        sb.Append(FRAGMENT_HIT_AND_EPILOGUE_WGSL) |> ignore
        sb.ToString()

    /// Generate both shader sources for the block-probe raymarch. Returns
    /// None when there are no surfaces to render — the caller drops its
    /// pipelines and no-ops in draw.
    let combinedRaymarchShaders (surfaces: FieldSurface list) : RaymarchShaders option =
        if surfaces.IsEmpty then None
        else
            Some
                { ComputeWgsl = computeWgsl surfaces
                  FragmentWgsl = fragmentWgsl surfaces
                  SurfaceCount = surfaces.Length }
