import { iterateIndexed, isEmpty } from "../../ui/fable_modules/fable-library-js.4.29.0/List.js";
import { StringBuilder__AppendLine_Z721C83C5, StringBuilder_$ctor } from "../../ui/fable_modules/fable-library-js.4.29.0/System.Text.js";
import { generateEvalFunctions } from "./GpuSdf.fs.js";
import { toString } from "../../ui/fable_modules/fable-library-js.4.29.0/Types.js";

export function combinedFieldSliceWgsl(surfaces) {
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
        StringBuilder__AppendLine_Z721C83C5(sb, "@group(0) @binding(0) var<uniform> cam: Camera;");
        StringBuilder__AppendLine_Z721C83C5(sb, "@group(1) @binding(0) var<storage, read> slots: Slots;");
        StringBuilder__AppendLine_Z721C83C5(sb, generateEvalFunctions(surfaces));
        StringBuilder__AppendLine_Z721C83C5(sb, "struct VIn { @location(0) pos: vec3<f32>, @location(1) info: vec4<f32> };");
        StringBuilder__AppendLine_Z721C83C5(sb, "struct VOut { @builtin(position) clip: vec4<f32>, @location(0) world_pos: vec3<f32>, @location(1) info: vec4<f32> };");
        StringBuilder__AppendLine_Z721C83C5(sb, "@vertex fn vs_main(v: VIn) -> VOut {");
        StringBuilder__AppendLine_Z721C83C5(sb, "  let f = cam.forward;");
        StringBuilder__AppendLine_Z721C83C5(sb, "  let r = cam.right;");
        StringBuilder__AppendLine_Z721C83C5(sb, "  let u = cam.up;");
        StringBuilder__AppendLine_Z721C83C5(sb, "  let view = mat4x4<f32>(");
        StringBuilder__AppendLine_Z721C83C5(sb, "    vec4<f32>(r.x, u.x, -f.x, 0.0),");
        StringBuilder__AppendLine_Z721C83C5(sb, "    vec4<f32>(r.y, u.y, -f.y, 0.0),");
        StringBuilder__AppendLine_Z721C83C5(sb, "    vec4<f32>(r.z, u.z, -f.z, 0.0),");
        StringBuilder__AppendLine_Z721C83C5(sb, "    vec4<f32>(-dot(r, cam.eye), -dot(u, cam.eye), dot(f, cam.eye), 1.0),");
        StringBuilder__AppendLine_Z721C83C5(sb, "  );");
        StringBuilder__AppendLine_Z721C83C5(sb, "  let half_fov: f32 = 0.3927;");
        StringBuilder__AppendLine_Z721C83C5(sb, "  let near: f32 = 0.001;");
        StringBuilder__AppendLine_Z721C83C5(sb, "  let far: f32 = 1000.0;");
        StringBuilder__AppendLine_Z721C83C5(sb, "  let t = tan(half_fov);");
        StringBuilder__AppendLine_Z721C83C5(sb, "  let proj = mat4x4<f32>(");
        StringBuilder__AppendLine_Z721C83C5(sb, "    vec4<f32>(1.0 / (cam.aspect * t), 0.0, 0.0, 0.0),");
        StringBuilder__AppendLine_Z721C83C5(sb, "    vec4<f32>(0.0, 1.0 / t, 0.0, 0.0),");
        StringBuilder__AppendLine_Z721C83C5(sb, "    vec4<f32>(0.0, 0.0, -(far + near) / (far - near), -1.0),");
        StringBuilder__AppendLine_Z721C83C5(sb, "    vec4<f32>(0.0, 0.0, -2.0 * far * near / (far - near), 0.0),");
        StringBuilder__AppendLine_Z721C83C5(sb, "  );");
        StringBuilder__AppendLine_Z721C83C5(sb, "  var o: VOut;");
        StringBuilder__AppendLine_Z721C83C5(sb, "  o.clip = proj * view * vec4<f32>(v.pos, 1.0);");
        StringBuilder__AppendLine_Z721C83C5(sb, "  o.world_pos = v.pos;");
        StringBuilder__AppendLine_Z721C83C5(sb, "  o.info = v.info;");
        StringBuilder__AppendLine_Z721C83C5(sb, "  return o;");
        StringBuilder__AppendLine_Z721C83C5(sb, "}");
        StringBuilder__AppendLine_Z721C83C5(sb, "@fragment fn fs_main(f: VOut) -> @location(0) vec4<f32> {");
        StringBuilder__AppendLine_Z721C83C5(sb, "  let slice_idx = i32(f.info.x + 0.5);");
        StringBuilder__AppendLine_Z721C83C5(sb, "  let p = f.world_pos;");
        StringBuilder__AppendLine_Z721C83C5(sb, "  var d: f32;");
        StringBuilder__AppendLine_Z721C83C5(sb, "  switch slice_idx {");
        iterateIndexed((i, _arg) => {
            StringBuilder__AppendLine_Z721C83C5(sb, `    case ${i}: { d = eval_sdf_${i}(p); }`);
        }, surfaces);
        StringBuilder__AppendLine_Z721C83C5(sb, "    default: { d = 1e10; }");
        StringBuilder__AppendLine_Z721C83C5(sb, "  }");
        StringBuilder__AppendLine_Z721C83C5(sb, "  let iso_spacing: f32 = 1.0;");
        StringBuilder__AppendLine_Z721C83C5(sb, "  let abs_d = abs(d);");
        StringBuilder__AppendLine_Z721C83C5(sb, "  let dd = max(fwidth(d), 0.0001);");
        StringBuilder__AppendLine_Z721C83C5(sb, "  let zero_width = max(dd * 1.6, 0.012);");
        StringBuilder__AppendLine_Z721C83C5(sb, "  let iso_width = max(dd * 1.25, 0.01);");
        StringBuilder__AppendLine_Z721C83C5(sb, "  let iso_frac = abs(fract(abs_d / iso_spacing + 0.5) - 0.5) * 2.0 * iso_spacing;");
        StringBuilder__AppendLine_Z721C83C5(sb, "  let iso_line = 1.0 - smoothstep(0.0, iso_width, iso_frac);");
        StringBuilder__AppendLine_Z721C83C5(sb, "  let zero_line = 1.0 - smoothstep(0.0, zero_width, abs_d);");
        StringBuilder__AppendLine_Z721C83C5(sb, "  let fade_start = iso_spacing * 6.0;");
        StringBuilder__AppendLine_Z721C83C5(sb, "  let fade_end = iso_spacing * 18.0;");
        StringBuilder__AppendLine_Z721C83C5(sb, "  let dist_fade = 1.0 - smoothstep(fade_start, fade_end, abs_d);");
        StringBuilder__AppendLine_Z721C83C5(sb, "  let final_alpha = max(iso_line * 0.42 * dist_fade, zero_line);");
        StringBuilder__AppendLine_Z721C83C5(sb, "  if (final_alpha < 0.005) { discard; }");
        StringBuilder__AppendLine_Z721C83C5(sb, "  let neg_col = vec3<f32>(1.0, 0.35, 0.25);");
        StringBuilder__AppendLine_Z721C83C5(sb, "  let pos_col = vec3<f32>(0.25, 0.55, 1.0);");
        StringBuilder__AppendLine_Z721C83C5(sb, "  let base_col = select(pos_col, neg_col, d < 0.0);");
        StringBuilder__AppendLine_Z721C83C5(sb, "  let col = mix(base_col, vec3<f32>(1.0), max(zero_line, iso_line * 0.22));");
        StringBuilder__AppendLine_Z721C83C5(sb, "  return vec4<f32>(col, final_alpha);");
        StringBuilder__AppendLine_Z721C83C5(sb, "}");
        return toString(sb);
    }
}

