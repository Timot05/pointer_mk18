namespace Server

open System.Text

// Emits the full-screen-triangle raymarch shader used by the `Raymarch`
// viewer mode. The shader:
//
//   * Reads the same Camera uniform layout every other viewer shader uses
//     (eye, forward, right, up, view_half_h, aspect), so ray generation
//     matches the orthographic projection used elsewhere — parallel rays
//     whose origin varies with screen UV, direction fixed to `forward`.
//   * Sphere-marches each surface; keeps the nearest hit (by `t`).
//   * Writes `@builtin(frag_depth)` by projecting the hit point through
//     the same ortho matrix the color pipelines use, so sketch overlays
//     z-test correctly against the field surface.
//
// Topology changes (surfaces added / removed / reordered) invalidate the
// shader source — call sites detect this and rebuild the pipeline.

module GpuIsosurface =

    let private ff (v: float) =
        if abs v < 1e-12 then "0.0" else sprintf "%.10f" v

    let combinedIsosurfaceWgsl (surfaces: FieldSurface list) =
        if surfaces.IsEmpty then None else
        let sb = StringBuilder()

        // ── Uniforms + storage buffers ─────────────────────────────────
        sb.AppendLine("struct Camera {") |> ignore
        sb.AppendLine("  eye: vec3<f32>, _p0: f32,") |> ignore
        sb.AppendLine("  forward: vec3<f32>, _p1: f32,") |> ignore
        sb.AppendLine("  right: vec3<f32>, view_half_h: f32,") |> ignore
        sb.AppendLine("  up: vec3<f32>, aspect: f32,") |> ignore
        sb.AppendLine("}") |> ignore
        sb.AppendLine("struct Slots { v: array<f32>, }") |> ignore
        sb.AppendLine("struct SurfaceState { colorOpacity: vec4<f32>, isoEnabled: vec4<f32>, }") |> ignore
        sb.AppendLine("@group(0) @binding(0) var<uniform> cam: Camera;") |> ignore
        sb.AppendLine("@group(1) @binding(0) var<storage, read> slots: Slots;") |> ignore
        sb.AppendLine("@group(2) @binding(0) var<storage, read> surfaceStates: array<SurfaceState>;") |> ignore

        // ── Per-surface SDF evaluators (reuses existing codegen) ───────
        sb.AppendLine(GpuSdf.generateEvalFunctions surfaces) |> ignore

        // ── Central-difference normal per surface ──────────────────────
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

        // ── Same orthographic projection the color pipelines use ──────
        // Keep this in sync with viewer/Shaders/*.wgsl `project_world`.
        sb.AppendLine("fn project_world(pos: vec3<f32>) -> vec4<f32> {") |> ignore
        sb.AppendLine("  let f = cam.forward;") |> ignore
        sb.AppendLine("  let r = cam.right;") |> ignore
        sb.AppendLine("  let u = cam.up;") |> ignore
        sb.AppendLine("  let view = mat4x4<f32>(") |> ignore
        sb.AppendLine("    vec4<f32>(r.x, u.x, -f.x, 0.0),") |> ignore
        sb.AppendLine("    vec4<f32>(r.y, u.y, -f.y, 0.0),") |> ignore
        sb.AppendLine("    vec4<f32>(r.z, u.z, -f.z, 0.0),") |> ignore
        sb.AppendLine("    vec4<f32>(-dot(r, cam.eye), -dot(u, cam.eye), dot(f, cam.eye), 1.0),") |> ignore
        sb.AppendLine("  );") |> ignore
        sb.AppendLine("  let near = 0.001;") |> ignore
        sb.AppendLine("  let far = 1000.0;") |> ignore
        sb.AppendLine("  let h = cam.view_half_h;") |> ignore
        sb.AppendLine("  let w = cam.aspect * h;") |> ignore
        sb.AppendLine("  let proj = mat4x4<f32>(") |> ignore
        sb.AppendLine("    vec4<f32>(1.0 / w, 0.0, 0.0, 0.0),") |> ignore
        sb.AppendLine("    vec4<f32>(0.0, 1.0 / h, 0.0, 0.0),") |> ignore
        sb.AppendLine("    vec4<f32>(0.0, 0.0, -1.0 / (far - near), 0.0),") |> ignore
        sb.AppendLine("    vec4<f32>(0.0, 0.0, -near / (far - near), 1.0),") |> ignore
        sb.AppendLine("  );") |> ignore
        sb.AppendLine("  return proj * view * vec4<f32>(pos, 1.0);") |> ignore
        sb.AppendLine("}") |> ignore

        // ── Full-screen triangle ──────────────────────────────────────
        sb.AppendLine("struct VOut { @builtin(position) clip: vec4<f32>, @location(0) uv: vec2<f32> };") |> ignore
        sb.AppendLine("@vertex fn vs_main(@builtin(vertex_index) idx: u32) -> VOut {") |> ignore
        sb.AppendLine("  var o: VOut;") |> ignore
        sb.AppendLine("  var pos = array<vec2<f32>, 3>(") |> ignore
        sb.AppendLine("    vec2<f32>(-1.0, -1.0),") |> ignore
        sb.AppendLine("    vec2<f32>(3.0, -1.0),") |> ignore
        sb.AppendLine("    vec2<f32>(-1.0, 3.0)") |> ignore
        sb.AppendLine("  );") |> ignore
        sb.AppendLine("  let p = pos[idx];") |> ignore
        sb.AppendLine("  o.clip = vec4<f32>(p, 0.0, 1.0);") |> ignore
        sb.AppendLine("  o.uv = p;") |> ignore
        sb.AppendLine("  return o;") |> ignore
        sb.AppendLine("}") |> ignore

        // ── Fragment: orthographic sphere-tracer + depth write ────────
        sb.AppendLine("struct FOut {") |> ignore
        sb.AppendLine("  @location(0) color: vec4<f32>,") |> ignore
        sb.AppendLine("  @builtin(frag_depth) depth: f32,") |> ignore
        sb.AppendLine("}") |> ignore
        sb.AppendLine("@fragment fn fs_main(f: VOut) -> FOut {") |> ignore
        // Orthographic ray: origin slides over a plane through the eye,
        // direction is fixed. Matches `Camera.screenToRay` in F#.
        sb.AppendLine("  let ro = cam.eye") |> ignore
        sb.AppendLine("    + f.uv.x * cam.aspect * cam.view_half_h * cam.right") |> ignore
        sb.AppendLine("    + f.uv.y * cam.view_half_h * cam.up;") |> ignore
        sb.AppendLine("  let rd = cam.forward;") |> ignore
        sb.AppendLine("  var t: f32 = 0.01;") |> ignore
        sb.AppendLine("  let max_t: f32 = 1000.0;") |> ignore
        sb.AppendLine("  for (var i: i32 = 0; i < 192; i++) {") |> ignore
        sb.AppendLine("    let p = ro + rd * t;") |> ignore
        sb.AppendLine("    var min_d: f32 = 1e10;") |> ignore
        sb.AppendLine("    var hit_id: i32 = -1;") |> ignore
        surfaces |> List.iteri (fun i _ ->
            sb.AppendLine("    {") |> ignore
            sb.AppendLine($"      let st = surfaceStates[{i}];") |> ignore
            sb.AppendLine("      if (st.isoEnabled.y >= 0.5) {") |> ignore
            sb.AppendLine($"        let d = eval_sdf_{i}(p);") |> ignore
            sb.AppendLine("        let dist_to_iso = abs(d - st.isoEnabled.x);") |> ignore
            sb.AppendLine($"        if (dist_to_iso < {ff 0.01}) {{ hit_id = {i}; }}") |> ignore
            sb.AppendLine("        if (dist_to_iso < min_d) { min_d = dist_to_iso; }") |> ignore
            sb.AppendLine("      }") |> ignore
            sb.AppendLine("    }") |> ignore)
        sb.AppendLine("    if (hit_id >= 0) {") |> ignore
        sb.AppendLine("      let st = surfaceStates[u32(hit_id)];") |> ignore
        sb.AppendLine("      var col = st.colorOpacity.xyz;") |> ignore
        sb.AppendLine("      let alpha = st.colorOpacity.w;") |> ignore
        sb.AppendLine("      let n = estimate_normal(p, hit_id);") |> ignore
        sb.AppendLine("      let key_dir = normalize(vec3<f32>(0.4, 0.3, 0.8));") |> ignore
        sb.AppendLine("      let fill_dir = normalize(vec3<f32>(-0.5, -0.4, 0.3));") |> ignore
        sb.AppendLine("      let key = max(dot(n, key_dir), 0.0) * 0.5;") |> ignore
        sb.AppendLine("      let fill = max(dot(n, fill_dir), 0.0) * 0.2;") |> ignore
        sb.AppendLine("      let ambient = 0.45;") |> ignore
        sb.AppendLine("      col = col * (ambient + key + fill);") |> ignore
        sb.AppendLine("      let clip = project_world(p);") |> ignore
        sb.AppendLine("      var out: FOut;") |> ignore
        sb.AppendLine("      out.color = vec4<f32>(col, alpha);") |> ignore
        sb.AppendLine("      out.depth = clip.z / clip.w;") |> ignore
        sb.AppendLine("      return out;") |> ignore
        sb.AppendLine("    }") |> ignore
        sb.AppendLine("    let step_size = max(min_d, 0.01);") |> ignore
        sb.AppendLine("    t += step_size;") |> ignore
        sb.AppendLine("    if (t > max_t) { break; }") |> ignore
        sb.AppendLine("  }") |> ignore
        // Miss: discard so the clear color shows through and depth stays
        // at its cleared value — sketches still z-test cleanly against
        // 1.0. WGSL still needs a return after `discard` because the
        // function's declared return type is `FOut`.
        sb.AppendLine("  discard;") |> ignore
        sb.AppendLine("  var miss: FOut;") |> ignore
        sb.AppendLine("  miss.color = vec4<f32>(0.0, 0.0, 0.0, 0.0);") |> ignore
        sb.AppendLine("  miss.depth = 1.0;") |> ignore
        sb.AppendLine("  return miss;") |> ignore
        sb.AppendLine("}") |> ignore

        Some(sb.ToString())
