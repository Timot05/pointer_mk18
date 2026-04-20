namespace Server

open System.Text

// Field iso-line overlay: renders a world-space quad per enabled slice,
// evaluating the chosen surface's SDF at each pixel and drawing contour
// lines where `|d|` crosses integer multiples of `iso_spacing`, plus a
// thicker zero-line. Orthographic projection + `@builtin(frag_depth)`
// so slices z-test correctly against the field surface and sketches.

module GpuFieldSlice =

    let combinedFieldSliceWgsl (surfaces: FieldSurface list) =
        if surfaces.IsEmpty then None else
        let sb = StringBuilder()

        // ── Uniforms + storage ────────────────────────────────────────
        sb.AppendLine("struct Camera {") |> ignore
        sb.AppendLine("  eye: vec3<f32>, _p0: f32,") |> ignore
        sb.AppendLine("  forward: vec3<f32>, _p1: f32,") |> ignore
        sb.AppendLine("  right: vec3<f32>, view_half_h: f32,") |> ignore
        sb.AppendLine("  up: vec3<f32>, aspect: f32,") |> ignore
        sb.AppendLine("}") |> ignore
        sb.AppendLine("struct Slots { v: array<f32>, }") |> ignore
        sb.AppendLine("@group(0) @binding(0) var<uniform> cam: Camera;") |> ignore
        sb.AppendLine("@group(1) @binding(0) var<storage, read> slots: Slots;") |> ignore

        // ── Per-surface SDF evaluators ────────────────────────────────
        sb.AppendLine(GpuSdf.generateEvalFunctions surfaces) |> ignore

        // ── Orthographic projection (kept in sync with other shaders) ─
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

        // ── Vertex: world-space quad → clip space ─────────────────────
        sb.AppendLine("struct VIn { @location(0) pos: vec3<f32>, @location(1) info: vec4<f32> };") |> ignore
        sb.AppendLine("struct VOut {") |> ignore
        sb.AppendLine("  @builtin(position) clip: vec4<f32>,") |> ignore
        sb.AppendLine("  @location(0) world_pos: vec3<f32>,") |> ignore
        sb.AppendLine("  @location(1) info: vec4<f32>,") |> ignore
        sb.AppendLine("};") |> ignore
        sb.AppendLine("@vertex fn vs_main(v: VIn) -> VOut {") |> ignore
        sb.AppendLine("  var o: VOut;") |> ignore
        sb.AppendLine("  o.clip = project_world(v.pos);") |> ignore
        sb.AppendLine("  o.world_pos = v.pos;") |> ignore
        sb.AppendLine("  o.info = v.info;") |> ignore
        sb.AppendLine("  return o;") |> ignore
        sb.AppendLine("}") |> ignore

        // ── Fragment: iso-lines + zero-line + depth ───────────────────
        sb.AppendLine("struct FOut {") |> ignore
        sb.AppendLine("  @location(0) color: vec4<f32>,") |> ignore
        sb.AppendLine("  @builtin(frag_depth) depth: f32,") |> ignore
        sb.AppendLine("}") |> ignore
        sb.AppendLine("@fragment fn fs_main(f: VOut) -> FOut {") |> ignore
        sb.AppendLine("  let slice_idx = i32(f.info.x + 0.5);") |> ignore
        sb.AppendLine("  let p = f.world_pos;") |> ignore
        sb.AppendLine("  var d: f32;") |> ignore
        sb.AppendLine("  switch slice_idx {") |> ignore
        surfaces |> List.iteri (fun i _ ->
            sb.AppendLine($"    case {i}: {{ d = eval_sdf_{i}(p); }}") |> ignore)
        sb.AppendLine("    default: { d = 1e10; }") |> ignore
        sb.AppendLine("  }") |> ignore
        sb.AppendLine("  let iso_spacing: f32 = 1.0;") |> ignore
        sb.AppendLine("  let abs_d = abs(d);") |> ignore
        sb.AppendLine("  let dd = max(fwidth(d), 0.0001);") |> ignore
        sb.AppendLine("  let zero_width = max(dd * 1.6, 0.012);") |> ignore
        sb.AppendLine("  let iso_width = max(dd * 1.25, 0.01);") |> ignore
        sb.AppendLine("  let iso_frac = abs(fract(abs_d / iso_spacing + 0.5) - 0.5) * 2.0 * iso_spacing;") |> ignore
        sb.AppendLine("  let iso_line = 1.0 - smoothstep(0.0, iso_width, iso_frac);") |> ignore
        sb.AppendLine("  let zero_line = 1.0 - smoothstep(0.0, zero_width, abs_d);") |> ignore
        sb.AppendLine("  let fade_start = iso_spacing * 6.0;") |> ignore
        sb.AppendLine("  let fade_end = iso_spacing * 18.0;") |> ignore
        sb.AppendLine("  let dist_fade = 1.0 - smoothstep(fade_start, fade_end, abs_d);") |> ignore
        sb.AppendLine("  let final_alpha = max(iso_line * 0.42 * dist_fade, zero_line);") |> ignore
        sb.AppendLine("  if (final_alpha < 0.005) { discard; }") |> ignore
        sb.AppendLine("  let neg_col = vec3<f32>(1.0, 0.35, 0.25);") |> ignore
        sb.AppendLine("  let pos_col = vec3<f32>(0.25, 0.55, 1.0);") |> ignore
        sb.AppendLine("  let base_col = select(pos_col, neg_col, d < 0.0);") |> ignore
        sb.AppendLine("  let col = mix(base_col, vec3<f32>(1.0), max(zero_line, iso_line * 0.22));") |> ignore
        sb.AppendLine("  var out: FOut;") |> ignore
        sb.AppendLine("  out.color = vec4<f32>(col, final_alpha);") |> ignore
        sb.AppendLine("  out.depth = f.clip.z / f.clip.w;") |> ignore
        sb.AppendLine("  return out;") |> ignore
        sb.AppendLine("}") |> ignore

        Some(sb.ToString())
