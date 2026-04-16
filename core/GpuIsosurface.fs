namespace Server

open System.Text

module GpuIsosurface =

    let private ff (v: float) =
        if abs v < 1e-12 then "0.0" else sprintf "%.10f" v

    let combinedIsosurfaceWgsl (surfaces: FieldSurface list) =
        if surfaces.IsEmpty then None else
        let sb = StringBuilder()
        sb.AppendLine("struct Camera {") |> ignore
        sb.AppendLine("  eye: vec3<f32>, _p0: f32,") |> ignore
        sb.AppendLine("  forward: vec3<f32>, _p1: f32,") |> ignore
        sb.AppendLine("  right: vec3<f32>, _p2: f32,") |> ignore
        sb.AppendLine("  up: vec3<f32>, aspect: f32,") |> ignore
        sb.AppendLine("}") |> ignore
        sb.AppendLine("struct Slots { v: array<f32>, }") |> ignore
        sb.AppendLine("struct SurfaceState { colorOpacity: vec4<f32>, isoEnabled: vec4<f32>, }") |> ignore
        sb.AppendLine("@group(0) @binding(0) var<uniform> cam: Camera;") |> ignore
        sb.AppendLine("@group(1) @binding(0) var<storage, read> slots: Slots;") |> ignore
        sb.AppendLine("@group(2) @binding(0) var<storage, read> surfaceStates: array<SurfaceState>;") |> ignore
        sb.AppendLine(GpuSdf.generateEvalFunctions surfaces) |> ignore
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
        sb.AppendLine("@fragment fn fs_main(f: VOut) -> @location(0) vec4<f32> {") |> ignore
        sb.AppendLine("  let half_fov = 0.3927;") |> ignore
        sb.AppendLine("  let tan_fov = tan(half_fov);") |> ignore
        sb.AppendLine("  let rd = normalize(cam.forward + f.uv.x * cam.aspect * tan_fov * cam.right + f.uv.y * tan_fov * cam.up);") |> ignore
        sb.AppendLine("  var t: f32 = 0.01;") |> ignore
        sb.AppendLine("  let max_t: f32 = 500.0;") |> ignore
        sb.AppendLine("  for (var i: i32 = 0; i < 192; i++) {") |> ignore
        sb.AppendLine("    let p = cam.eye + rd * t;") |> ignore
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
        sb.AppendLine("      return vec4<f32>(col, alpha);") |> ignore
        sb.AppendLine("    }") |> ignore
        sb.AppendLine("    let step_size = max(min_d, 0.01);") |> ignore
        sb.AppendLine("    t += step_size;") |> ignore
        sb.AppendLine("    if (t > max_t) { break; }") |> ignore
        sb.AppendLine("  }") |> ignore
        sb.AppendLine("  return vec4<f32>(0.0, 0.0, 0.0, 0.0);") |> ignore
        sb.AppendLine("}") |> ignore
        Some(sb.ToString())
