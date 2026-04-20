import { printf, toText } from "../../ui/fable_modules/fable-library-js.4.29.0/String.js";
import { toString, Record } from "../../ui/fable_modules/fable-library-js.4.29.0/Types.js";
import { record_type, array_type, string_type, int32_type } from "../../ui/fable_modules/fable-library-js.4.29.0/Reflection.js";
import { StringBuilder__AppendLine_Z721C83C5, StringBuilder_$ctor } from "../../ui/fable_modules/fable-library-js.4.29.0/System.Text.js";
import { mapIndexed, iterateIndexed } from "../../ui/fable_modules/fable-library-js.4.29.0/List.js";
import { disposeSafe, getEnumerator } from "../../ui/fable_modules/fable-library-js.4.29.0/Util.js";

function ff(v) {
    if (Math.abs(v) < 1E-12) {
        return "0.0";
    }
    else {
        return toText(printf("%.10f"))(v);
    }
}

function slotExpr(slot) {
    return `slots.v[${slot}]`;
}

export class CodegenCtx extends Record {
    constructor(NextId, Declarations) {
        super();
        this.NextId = (NextId | 0);
        this.Declarations = Declarations;
    }
}

export function CodegenCtx_$reflection() {
    return record_type("Server.GpuSdf.CodegenCtx", [], CodegenCtx, () => [["NextId", int32_type], ["Declarations", array_type(string_type)]]);
}

function createCtx() {
    return new CodegenCtx(0, []);
}

function freshId(ctx) {
    const id = ctx.NextId | 0;
    ctx.NextId = ((id + 1) | 0);
    return id | 0;
}

function sketchPointExpr(pt) {
    return `vec2<f32>(${slotExpr(pt.XSlot)}, ${slotExpr(pt.YSlot)})`;
}

function codegenNode(node_mut, ctx_mut, pExpr_mut) {
    codegenNode:
    while (true) {
        const node = node_mut, ctx = ctx_mut, pExpr = pExpr_mut;
        switch (node.tag) {
            case 1: {
                node_mut = node.fields[3];
                ctx_mut = ctx;
                pExpr_mut = (`(${pExpr} - vec3<f32>(${slotExpr(node.fields[0])}, ${slotExpr(node.fields[1])}, ${slotExpr(node.fields[2])}))`);
                continue codegenNode;
            }
            case 2: {
                node_mut = node.fields[4];
                ctx_mut = ctx;
                pExpr_mut = (`rotate_axis_angle_inv(${pExpr}, vec3<f32>(${slotExpr(node.fields[0])}, ${slotExpr(node.fields[1])}, ${slotExpr(node.fields[2])}), ${slotExpr(node.fields[3])})`);
                continue codegenNode;
            }
            case 3: {
                const op = node.fields[0];
                const ea = codegenNode(node.fields[2], ctx, pExpr);
                const eb = codegenNode(node.fields[3], ctx, pExpr);
                const k = slotExpr(node.fields[1]);
                switch (op.tag) {
                    case 2:
                        return `(-smooth_min(-(${ea}), -(${eb}), ${k}))`;
                    case 1:
                        return `(-smooth_min(-(${ea}), ${eb}, ${k}))`;
                    default:
                        return `smooth_min(${ea}, ${eb}, ${k})`;
                }
            }
            case 4: {
                const value = node.fields[1] | 0;
                const childExpr = codegenNode(node.fields[2], ctx, pExpr);
                if (node.fields[0].tag === 1) {
                    return `max((${childExpr}), -((${childExpr}) + ${slotExpr(value)}))`;
                }
                else {
                    return `((${childExpr}) - ${slotExpr(value)})`;
                }
            }
            case 5: {
                const sketch = node.fields[0];
                const fnName = `sketch_${freshId(ctx)}`;
                const sb = StringBuilder_$ctor();
                StringBuilder__AppendLine_Z721C83C5(sb, `fn ${fnName}(p: vec3<f32>) -> f32 {`);
                StringBuilder__AppendLine_Z721C83C5(sb, "  let lp = p.xy;");
                StringBuilder__AppendLine_Z721C83C5(sb, "  var min_d: f32 = 1e10;");
                iterateIndexed((i, prim_1) => {
                    switch (prim_1.tag) {
                        case 1: {
                            StringBuilder__AppendLine_Z721C83C5(sb, `  let c${i}_center = ${sketchPointExpr(prim_1.fields[0])};`);
                            StringBuilder__AppendLine_Z721C83C5(sb, `  min_d = min(min_d, circle_curve_dist(lp, c${i}_center, ${slotExpr(prim_1.fields[1])}));`);
                            break;
                        }
                        case 2: {
                            const cw = prim_1.fields[3] ? "true" : "false";
                            StringBuilder__AppendLine_Z721C83C5(sb, `  let a${i}_start = ${sketchPointExpr(prim_1.fields[0])};`);
                            StringBuilder__AppendLine_Z721C83C5(sb, `  let a${i}_end = ${sketchPointExpr(prim_1.fields[1])};`);
                            StringBuilder__AppendLine_Z721C83C5(sb, `  let a${i}_center = ${sketchPointExpr(prim_1.fields[2])};`);
                            StringBuilder__AppendLine_Z721C83C5(sb, `  min_d = min(min_d, arc_curve_dist(lp, a${i}_start, a${i}_end, a${i}_center, ${cw}));`);
                            break;
                        }
                        default: {
                            StringBuilder__AppendLine_Z721C83C5(sb, `  let l${i}_a = ${sketchPointExpr(prim_1.fields[0])};`);
                            StringBuilder__AppendLine_Z721C83C5(sb, `  let l${i}_b = ${sketchPointExpr(prim_1.fields[1])};`);
                            StringBuilder__AppendLine_Z721C83C5(sb, `  min_d = min(min_d, seg_dist(lp, l${i}_a, l${i}_b));`);
                        }
                    }
                }, sketch.Primitives);
                if (sketch.Closed) {
                    StringBuilder__AppendLine_Z721C83C5(sb, "  var crossings: i32 = 0;");
                    iterateIndexed((i_1, prim_2) => {
                        switch (prim_2.tag) {
                            case 1: {
                                StringBuilder__AppendLine_Z721C83C5(sb, `  crossings += ray_cross_circle(lp, c${i_1}_center, ${slotExpr(prim_2.fields[1])});`);
                                break;
                            }
                            case 2: {
                                StringBuilder__AppendLine_Z721C83C5(sb, `  crossings += ray_cross_arc(lp, a${i_1}_start, a${i_1}_end, a${i_1}_center, ${prim_2.fields[3] ? "true" : "false"});`);
                                break;
                            }
                            default:
                                StringBuilder__AppendLine_Z721C83C5(sb, `  crossings += ray_cross_line_segment(lp, l${i_1}_a, l${i_1}_b);`);
                        }
                    }, sketch.Primitives);
                    if (sketch.Flip) {
                        StringBuilder__AppendLine_Z721C83C5(sb, "  if ((crossings & 1) != 0) { return min_d; }");
                        StringBuilder__AppendLine_Z721C83C5(sb, "  return -min_d;");
                    }
                    else {
                        StringBuilder__AppendLine_Z721C83C5(sb, "  if ((crossings & 1) != 0) { return -min_d; }");
                        StringBuilder__AppendLine_Z721C83C5(sb, "  return min_d;");
                    }
                }
                else {
                    StringBuilder__AppendLine_Z721C83C5(sb, "  return min_d;");
                }
                StringBuilder__AppendLine_Z721C83C5(sb, "}");
                void (ctx.Declarations.push(toString(sb)));
                return `${fnName}(${pExpr})`;
            }
            default: {
                const prim = node.fields[0];
                switch (prim.tag) {
                    case 1:
                        return `sdf_cylinder(${pExpr}, ${slotExpr(prim.fields[0])}, ${slotExpr(prim.fields[1])})`;
                    case 2:
                        return `sdf_box(${pExpr}, vec3<f32>(${slotExpr(prim.fields[0])} * 0.5, ${slotExpr(prim.fields[1])} * 0.5, ${slotExpr(prim.fields[2])} * 0.5))`;
                    case 3: {
                        const axis = prim.fields[0];
                        const sign = prim.fields[2] ? "-1.0" : "1.0";
                        return `(((${pExpr}).${(axis === "X") ? "x" : ((axis === "Y") ? "y" : "z")} - ${slotExpr(prim.fields[1])}) * ${sign})`;
                    }
                    default:
                        return `sdf_sphere(${pExpr}, ${slotExpr(prim.fields[0])})`;
                }
            }
        }
        break;
    }
}

export function generateEvalFunctions(surfaces) {
    const ctx = createCtx();
    const evals = mapIndexed((i, surface) => [i, codegenNode(surface.Field, ctx, "p")], surfaces);
    const sb = StringBuilder_$ctor();
    StringBuilder__AppendLine_Z721C83C5(sb, "\nfn seg_dist(p: vec2<f32>, a: vec2<f32>, b: vec2<f32>) -> f32 {\n  let e = b - a;\n  let w = p - a;\n  let l = dot(e, e) + 1e-20;\n  let t = clamp(dot(w, e) / l, 0.0, 1.0);\n  let d = w - e * t;\n  return length(d);\n}\n\nfn ray_dist(p: vec2<f32>, a: vec2<f32>, b: vec2<f32>) -> f32 {\n  let e = b - a;\n  let w = p - a;\n  let l = dot(e, e) + 1e-20;\n  let t = max(dot(w, e) / l, 0.0);\n  let d = w - e * t;\n  return length(d);\n}\n\nfn head_ray_dist(p: vec2<f32>, a: vec2<f32>, b: vec2<f32>) -> f32 {\n  let e = b - a;\n  let w = p - a;\n  let l = dot(e, e) + 1e-20;\n  let t = min(dot(w, e) / l, 1.0);\n  let d = w - e * t;\n  return length(d);\n}\n\nfn line_dist(p: vec2<f32>, a: vec2<f32>, b: vec2<f32>) -> f32 {\n  let e = b - a;\n  let w = p - a;\n  let l = dot(e, e) + 1e-20;\n  let t = dot(w, e) / l;\n  let d = w - e * t;\n  return length(d);\n}\n\nfn circle_curve_dist(p: vec2<f32>, center: vec2<f32>, radius: f32) -> f32 {\n  return abs(length(p - center) - radius);\n}\n\nfn atan2_compat(y: f32, x: f32) -> f32 {\n  if (abs(x) < 1e-7) {\n    if (y > 0.0) { return 1.57079632679; }\n    if (y < 0.0) { return -1.57079632679; }\n    return 0.0;\n  }\n  let a = atan(y / x);\n  if (x > 0.0) { return a; }\n  if (y >= 0.0) { return a + 3.14159265359; }\n  return a - 3.14159265359;\n}\n\nfn positive_angle_delta(start: f32, end: f32) -> f32 {\n  let tau = 6.28318530718;\n  var d = end - start;\n  while (d < 0.0) { d += tau; }\n  while (d >= tau) { d -= tau; }\n  return d;\n}\n\nfn arc_contains_angle(start: f32, end: f32, query: f32, clockwise: bool) -> bool {\n  if (clockwise) {\n    return positive_angle_delta(end, query) <= positive_angle_delta(end, start);\n  }\n  return positive_angle_delta(start, query) <= positive_angle_delta(start, end);\n}\n\nfn arc_curve_dist(\n  p: vec2<f32>,\n  start: vec2<f32>,\n  end: vec2<f32>,\n  center: vec2<f32>,\n  clockwise: bool,\n) -> f32 {\n  let radius = length(start - center);\n  if (radius < 1e-6) { return seg_dist(p, start, end); }\n  let query = p - center;\n  let start_angle = atan2_compat(start.y - center.y, start.x - center.x);\n  let end_angle = atan2_compat(end.y - center.y, end.x - center.x);\n  let query_angle = atan2_compat(query.y, query.x);\n  if (arc_contains_angle(start_angle, end_angle, query_angle, clockwise)) {\n    return abs(length(query) - radius);\n  }\n  return min(length(p - start), length(p - end));\n}\n\nfn ray_cross_line_segment(p: vec2<f32>, a: vec2<f32>, b: vec2<f32>) -> i32 {\n  let a_above = a.y > p.y;\n  let b_above = b.y > p.y;\n  if (a_above == b_above) { return 0; }\n  let t = (p.y - a.y) / (b.y - a.y);\n  let x = a.x + t * (b.x - a.x);\n  if (x > p.x) { return 1; }\n  return 0;\n}\n\nfn ray_cross_circle(p: vec2<f32>, center: vec2<f32>, radius: f32) -> i32 {\n  let dy = p.y - center.y;\n  let disc = radius * radius - dy * dy;\n  if (disc <= 1e-7) { return 0; }\n  let h = sqrt(disc);\n  var count = 0;\n  if (center.x - h > p.x) { count += 1; }\n  if (center.x + h > p.x) { count += 1; }\n  return count;\n}\n\nfn ray_cross_arc(\n  p: vec2<f32>,\n  start: vec2<f32>,\n  end: vec2<f32>,\n  center: vec2<f32>,\n  clockwise: bool,\n) -> i32 {\n  let radius = length(start - center);\n  if (radius < 1e-6) { return 0; }\n  let dy = p.y - center.y;\n  let disc = radius * radius - dy * dy;\n  if (disc <= 1e-7) { return 0; }\n  let start_angle = atan2_compat(start.y - center.y, start.x - center.x);\n  let end_angle = atan2_compat(end.y - center.y, end.x - center.x);\n  let h = sqrt(disc);\n  var count = 0;\n  let x0 = center.x - h;\n  if (x0 > p.x) {\n    let angle0 = atan2_compat(p.y - center.y, x0 - center.x);\n    if (arc_contains_angle(start_angle, end_angle, angle0, clockwise)\n      && (abs(x0 - end.x) > 1e-5 || abs(p.y - end.y) > 1e-5)) {\n      count += 1;\n    }\n  }\n  let x1 = center.x + h;\n  if (x1 > p.x) {\n    let angle1 = atan2_compat(p.y - center.y, x1 - center.x);\n    if (arc_contains_angle(start_angle, end_angle, angle1, clockwise)\n      && (abs(x1 - end.x) > 1e-5 || abs(p.y - end.y) > 1e-5)) {\n      count += 1;\n    }\n  }\n  return count;\n}\n\nfn smooth_min(a: f32, b: f32, k: f32) -> f32 {\n  if (k <= 1e-6) { return min(a, b); }\n  let h = max(k - abs(a - b), 0.0) / k;\n  return min(a, b) - h * h * h * k / 6.0;\n}\n\nfn sdf_sphere(p: vec3<f32>, r: f32) -> f32 {\n  return length(p) - r;\n}\n\nfn sdf_cylinder(p: vec3<f32>, r: f32, h: f32) -> f32 {\n  let d_radial = length(p.xy) - r;\n  let d_axial = abs(p.z) - h * 0.5;\n  if (d_radial > 0.0 && d_axial > 0.0) {\n    return sqrt(d_radial * d_radial + d_axial * d_axial);\n  }\n  return max(d_radial, d_axial);\n}\n\nfn sdf_box(p: vec3<f32>, half_size: vec3<f32>) -> f32 {\n  let q = abs(p) - half_size;\n  let outside = length(max(q, vec3<f32>(0.0)));\n  let inside = min(max(q.x, max(q.y, q.z)), 0.0);\n  return outside + inside;\n}\n\nfn rotate_axis_angle_inv(p: vec3<f32>, axis: vec3<f32>, angle: f32) -> vec3<f32> {\n  let len_axis = length(axis);\n  if (len_axis <= 1e-6) { return p; }\n  let u = axis / len_axis;\n  let a = -angle;\n  let c = cos(a);\n  let s = sin(a);\n  return p * c + cross(u, p) * s + u * dot(u, p) * (1.0 - c);\n}\n");
    let enumerator = getEnumerator(ctx.Declarations);
    try {
        while (enumerator["System.Collections.IEnumerator.MoveNext"]()) {
            StringBuilder__AppendLine_Z721C83C5(sb, enumerator["System.Collections.Generic.IEnumerator`1.get_Current"]());
        }
    }
    finally {
        disposeSafe(enumerator);
    }
    const enumerator_1 = getEnumerator(evals);
    try {
        while (enumerator_1["System.Collections.IEnumerator.MoveNext"]()) {
            const forLoopVar = enumerator_1["System.Collections.Generic.IEnumerator`1.get_Current"]();
            StringBuilder__AppendLine_Z721C83C5(sb, `fn eval_sdf_${forLoopVar[0]}(p: vec3<f32>) -> f32 {`);
            StringBuilder__AppendLine_Z721C83C5(sb, `  return ${forLoopVar[1]};`);
            StringBuilder__AppendLine_Z721C83C5(sb, "}");
        }
    }
    finally {
        disposeSafe(enumerator_1);
    }
    return toString(sb);
}

