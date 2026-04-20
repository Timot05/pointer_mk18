import { printf, toText } from "../../ui/fable_modules/fable-library-js.4.29.0/String.js";
import { iterateIndexed, isEmpty } from "../../ui/fable_modules/fable-library-js.4.29.0/List.js";
import { StringBuilder__AppendLine_Z721C83C5, StringBuilder_$ctor } from "../../ui/fable_modules/fable-library-js.4.29.0/System.Text.js";
import { generateEvalFunctions } from "./GpuSdf.fs.js";
import { toString } from "../../ui/fable_modules/fable-library-js.4.29.0/Types.js";

function ff(v) {
    if (Math.abs(v) < 1E-12) {
        return "0.0";
    }
    else {
        return toText(printf("%.10f"))(v);
    }
}

export function combinedIsosurfaceWgsl(surfaces) {
    if (isEmpty(surfaces)) {
        return undefined;
    }
    else {
        const sb = StringBuilder_$ctor();
        StringBuilder__AppendLine_Z721C83C5(sb, "struct Camera {");
        StringBuilder__AppendLine_Z721C83C5(sb, "  eye: vec3<f32>, _p0: f32,");
        StringBuilder__AppendLine_Z721C83C5(sb, "  forward: vec3<f32>, _p1: f32,");
        StringBuilder__AppendLine_Z721C83C5(sb, "  right: vec3<f32>, _p2: f32,");
        StringBuilder__AppendLine_Z721C83C5(sb, "  up: vec3<f32>, aspect: f32,");
        StringBuilder__AppendLine_Z721C83C5(sb, "}");
        StringBuilder__AppendLine_Z721C83C5(sb, "struct Slots { v: array<f32>, }");
        StringBuilder__AppendLine_Z721C83C5(sb, "struct SurfaceState { colorOpacity: vec4<f32>, isoEnabled: vec4<f32>, }");
        StringBuilder__AppendLine_Z721C83C5(sb, "@group(0) @binding(0) var<uniform> cam: Camera;");
        StringBuilder__AppendLine_Z721C83C5(sb, "@group(1) @binding(0) var<storage, read> slots: Slots;");
        StringBuilder__AppendLine_Z721C83C5(sb, "@group(2) @binding(0) var<storage, read> surfaceStates: array<SurfaceState>;");
        StringBuilder__AppendLine_Z721C83C5(sb, generateEvalFunctions(surfaces));
        StringBuilder__AppendLine_Z721C83C5(sb, "fn estimate_normal(p: vec3<f32>, surface_id: i32) -> vec3<f32> {");
        StringBuilder__AppendLine_Z721C83C5(sb, "  let e = 0.01;");
        StringBuilder__AppendLine_Z721C83C5(sb, "  var n: vec3<f32>;");
        StringBuilder__AppendLine_Z721C83C5(sb, "  switch surface_id {");
        iterateIndexed((i, _arg) => {
            StringBuilder__AppendLine_Z721C83C5(sb, `    case ${i}: {`);
            StringBuilder__AppendLine_Z721C83C5(sb, "      n = vec3<f32>(");
            StringBuilder__AppendLine_Z721C83C5(sb, `        eval_sdf_${i}(p + vec3<f32>(e, 0.0, 0.0)) - eval_sdf_${i}(p - vec3<f32>(e, 0.0, 0.0)),`);
            StringBuilder__AppendLine_Z721C83C5(sb, `        eval_sdf_${i}(p + vec3<f32>(0.0, e, 0.0)) - eval_sdf_${i}(p - vec3<f32>(0.0, e, 0.0)),`);
            StringBuilder__AppendLine_Z721C83C5(sb, `        eval_sdf_${i}(p + vec3<f32>(0.0, 0.0, e)) - eval_sdf_${i}(p - vec3<f32>(0.0, 0.0, e))`);
            StringBuilder__AppendLine_Z721C83C5(sb, "      );");
            StringBuilder__AppendLine_Z721C83C5(sb, "    }");
        }, surfaces);
        StringBuilder__AppendLine_Z721C83C5(sb, "    default: { n = vec3<f32>(0.0, 0.0, 1.0); }");
        StringBuilder__AppendLine_Z721C83C5(sb, "  }");
        StringBuilder__AppendLine_Z721C83C5(sb, "  return normalize(n);");
        StringBuilder__AppendLine_Z721C83C5(sb, "}");
        StringBuilder__AppendLine_Z721C83C5(sb, "struct VOut { @builtin(position) clip: vec4<f32>, @location(0) uv: vec2<f32> };");
        StringBuilder__AppendLine_Z721C83C5(sb, "@vertex fn vs_main(@builtin(vertex_index) idx: u32) -> VOut {");
        StringBuilder__AppendLine_Z721C83C5(sb, "  var o: VOut;");
        StringBuilder__AppendLine_Z721C83C5(sb, "  var pos = array<vec2<f32>, 3>(");
        StringBuilder__AppendLine_Z721C83C5(sb, "    vec2<f32>(-1.0, -1.0),");
        StringBuilder__AppendLine_Z721C83C5(sb, "    vec2<f32>(3.0, -1.0),");
        StringBuilder__AppendLine_Z721C83C5(sb, "    vec2<f32>(-1.0, 3.0)");
        StringBuilder__AppendLine_Z721C83C5(sb, "  );");
        StringBuilder__AppendLine_Z721C83C5(sb, "  let p = pos[idx];");
        StringBuilder__AppendLine_Z721C83C5(sb, "  o.clip = vec4<f32>(p, 0.0, 1.0);");
        StringBuilder__AppendLine_Z721C83C5(sb, "  o.uv = p;");
        StringBuilder__AppendLine_Z721C83C5(sb, "  return o;");
        StringBuilder__AppendLine_Z721C83C5(sb, "}");
        StringBuilder__AppendLine_Z721C83C5(sb, "@fragment fn fs_main(f: VOut) -> @location(0) vec4<f32> {");
        StringBuilder__AppendLine_Z721C83C5(sb, "  let half_fov = 0.3927;");
        StringBuilder__AppendLine_Z721C83C5(sb, "  let tan_fov = tan(half_fov);");
        StringBuilder__AppendLine_Z721C83C5(sb, "  let rd = normalize(cam.forward + f.uv.x * cam.aspect * tan_fov * cam.right + f.uv.y * tan_fov * cam.up);");
        StringBuilder__AppendLine_Z721C83C5(sb, "  var t: f32 = 0.01;");
        StringBuilder__AppendLine_Z721C83C5(sb, "  let max_t: f32 = 500.0;");
        StringBuilder__AppendLine_Z721C83C5(sb, "  for (var i: i32 = 0; i < 192; i++) {");
        StringBuilder__AppendLine_Z721C83C5(sb, "    let p = cam.eye + rd * t;");
        StringBuilder__AppendLine_Z721C83C5(sb, "    var min_d: f32 = 1e10;");
        StringBuilder__AppendLine_Z721C83C5(sb, "    var hit_id: i32 = -1;");
        iterateIndexed((i_1, _arg_1) => {
            StringBuilder__AppendLine_Z721C83C5(sb, "    {");
            StringBuilder__AppendLine_Z721C83C5(sb, `      let st = surfaceStates[${i_1}];`);
            StringBuilder__AppendLine_Z721C83C5(sb, "      if (st.isoEnabled.y >= 0.5) {");
            StringBuilder__AppendLine_Z721C83C5(sb, `        let d = eval_sdf_${i_1}(p);`);
            StringBuilder__AppendLine_Z721C83C5(sb, "        let dist_to_iso = abs(d - st.isoEnabled.x);");
            StringBuilder__AppendLine_Z721C83C5(sb, `        if (dist_to_iso < ${ff(0.01)}) { hit_id = ${i_1}; }`);
            StringBuilder__AppendLine_Z721C83C5(sb, "        if (dist_to_iso < min_d) { min_d = dist_to_iso; }");
            StringBuilder__AppendLine_Z721C83C5(sb, "      }");
            StringBuilder__AppendLine_Z721C83C5(sb, "    }");
        }, surfaces);
        StringBuilder__AppendLine_Z721C83C5(sb, "    if (hit_id >= 0) {");
        StringBuilder__AppendLine_Z721C83C5(sb, "      let st = surfaceStates[u32(hit_id)];");
        StringBuilder__AppendLine_Z721C83C5(sb, "      var col = st.colorOpacity.xyz;");
        StringBuilder__AppendLine_Z721C83C5(sb, "      let alpha = st.colorOpacity.w;");
        StringBuilder__AppendLine_Z721C83C5(sb, "      let n = estimate_normal(p, hit_id);");
        StringBuilder__AppendLine_Z721C83C5(sb, "      let key_dir = normalize(vec3<f32>(0.4, 0.3, 0.8));");
        StringBuilder__AppendLine_Z721C83C5(sb, "      let fill_dir = normalize(vec3<f32>(-0.5, -0.4, 0.3));");
        StringBuilder__AppendLine_Z721C83C5(sb, "      let key = max(dot(n, key_dir), 0.0) * 0.5;");
        StringBuilder__AppendLine_Z721C83C5(sb, "      let fill = max(dot(n, fill_dir), 0.0) * 0.2;");
        StringBuilder__AppendLine_Z721C83C5(sb, "      let ambient = 0.45;");
        StringBuilder__AppendLine_Z721C83C5(sb, "      col = col * (ambient + key + fill);");
        StringBuilder__AppendLine_Z721C83C5(sb, "      return vec4<f32>(col, alpha);");
        StringBuilder__AppendLine_Z721C83C5(sb, "    }");
        StringBuilder__AppendLine_Z721C83C5(sb, "    let step_size = max(min_d, 0.01);");
        StringBuilder__AppendLine_Z721C83C5(sb, "    t += step_size;");
        StringBuilder__AppendLine_Z721C83C5(sb, "    if (t > max_t) { break; }");
        StringBuilder__AppendLine_Z721C83C5(sb, "  }");
        StringBuilder__AppendLine_Z721C83C5(sb, "  return vec4<f32>(0.0, 0.0, 0.0, 0.0);");
        StringBuilder__AppendLine_Z721C83C5(sb, "}");
        return toString(sb);
    }
}

