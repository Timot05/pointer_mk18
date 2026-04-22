namespace Server

open System.Text

module GpuSdf =

    let private ff (v: float) =
        if abs v < 1e-12 then "0.0" else sprintf "%.10f" v

    let private slotExpr (slot: Slot) = $"slots.v[{slot}]"

    [<Literal>]
    let WGSL_HELPERS = """
fn seg_dist(p: vec2<f32>, a: vec2<f32>, b: vec2<f32>) -> f32 {
  let e = b - a;
  let w = p - a;
  let l = dot(e, e) + 1e-20;
  let t = clamp(dot(w, e) / l, 0.0, 1.0);
  let d = w - e * t;
  return length(d);
}

fn ray_dist(p: vec2<f32>, a: vec2<f32>, b: vec2<f32>) -> f32 {
  let e = b - a;
  let w = p - a;
  let l = dot(e, e) + 1e-20;
  let t = max(dot(w, e) / l, 0.0);
  let d = w - e * t;
  return length(d);
}

fn head_ray_dist(p: vec2<f32>, a: vec2<f32>, b: vec2<f32>) -> f32 {
  let e = b - a;
  let w = p - a;
  let l = dot(e, e) + 1e-20;
  let t = min(dot(w, e) / l, 1.0);
  let d = w - e * t;
  return length(d);
}

fn line_dist(p: vec2<f32>, a: vec2<f32>, b: vec2<f32>) -> f32 {
  let e = b - a;
  let w = p - a;
  let l = dot(e, e) + 1e-20;
  let t = dot(w, e) / l;
  let d = w - e * t;
  return length(d);
}

fn circle_curve_dist(p: vec2<f32>, center: vec2<f32>, radius: f32) -> f32 {
  return abs(length(p - center) - radius);
}

fn atan2_compat(y: f32, x: f32) -> f32 {
  if (abs(x) < 1e-7) {
    if (y > 0.0) { return 1.57079632679; }
    if (y < 0.0) { return -1.57079632679; }
    return 0.0;
  }
  let a = atan(y / x);
  if (x > 0.0) { return a; }
  if (y >= 0.0) { return a + 3.14159265359; }
  return a - 3.14159265359;
}

fn positive_angle_delta(start: f32, end: f32) -> f32 {
  let tau = 6.28318530718;
  var d = end - start;
  while (d < 0.0) { d += tau; }
  while (d >= tau) { d -= tau; }
  return d;
}

fn arc_contains_angle(start: f32, end: f32, query: f32, clockwise: bool) -> bool {
  if (clockwise) {
    return positive_angle_delta(end, query) <= positive_angle_delta(end, start);
  }
  return positive_angle_delta(start, query) <= positive_angle_delta(start, end);
}

fn arc_curve_dist(
  p: vec2<f32>,
  start: vec2<f32>,
  end: vec2<f32>,
  center: vec2<f32>,
  clockwise: bool,
) -> f32 {
  let radius = length(start - center);
  if (radius < 1e-6) { return seg_dist(p, start, end); }
  let query = p - center;
  let start_angle = atan2_compat(start.y - center.y, start.x - center.x);
  let end_angle = atan2_compat(end.y - center.y, end.x - center.x);
  let query_angle = atan2_compat(query.y, query.x);
  if (arc_contains_angle(start_angle, end_angle, query_angle, clockwise)) {
    return abs(length(query) - radius);
  }
  return min(length(p - start), length(p - end));
}

fn ray_cross_line_segment(p: vec2<f32>, a: vec2<f32>, b: vec2<f32>) -> i32 {
  let a_above = a.y > p.y;
  let b_above = b.y > p.y;
  if (a_above == b_above) { return 0; }
  let t = (p.y - a.y) / (b.y - a.y);
  let x = a.x + t * (b.x - a.x);
  if (x > p.x) { return 1; }
  return 0;
}

fn ray_cross_circle(p: vec2<f32>, center: vec2<f32>, radius: f32) -> i32 {
  let dy = p.y - center.y;
  let disc = radius * radius - dy * dy;
  if (disc <= 1e-7) { return 0; }
  let h = sqrt(disc);
  var count = 0;
  if (center.x - h > p.x) { count += 1; }
  if (center.x + h > p.x) { count += 1; }
  return count;
}

fn ray_cross_arc(
  p: vec2<f32>,
  start: vec2<f32>,
  end: vec2<f32>,
  center: vec2<f32>,
  clockwise: bool,
) -> i32 {
  let radius = length(start - center);
  if (radius < 1e-6) { return 0; }
  let dy = p.y - center.y;
  let disc = radius * radius - dy * dy;
  if (disc <= 1e-7) { return 0; }
  let start_angle = atan2_compat(start.y - center.y, start.x - center.x);
  let end_angle = atan2_compat(end.y - center.y, end.x - center.x);
  let h = sqrt(disc);
  var count = 0;
  let x0 = center.x - h;
  if (x0 > p.x) {
    let angle0 = atan2_compat(p.y - center.y, x0 - center.x);
    if (arc_contains_angle(start_angle, end_angle, angle0, clockwise)
      && (abs(x0 - end.x) > 1e-5 || abs(p.y - end.y) > 1e-5)) {
      count += 1;
    }
  }
  let x1 = center.x + h;
  if (x1 > p.x) {
    let angle1 = atan2_compat(p.y - center.y, x1 - center.x);
    if (arc_contains_angle(start_angle, end_angle, angle1, clockwise)
      && (abs(x1 - end.x) > 1e-5 || abs(p.y - end.y) > 1e-5)) {
      count += 1;
    }
  }
  return count;
}

fn smooth_min(a: f32, b: f32, k: f32) -> f32 {
  if (k <= 1e-6) { return min(a, b); }
  let h = max(k - abs(a - b), 0.0) / k;
  return min(a, b) - h * h * h * k / 6.0;
}

fn sdf_sphere(p: vec3<f32>, r: f32) -> f32 {
  return length(p) - r;
}

fn sdf_cylinder(p: vec3<f32>, r: f32, h: f32) -> f32 {
  let d_radial = length(p.xy) - r;
  let d_axial = abs(p.z) - h * 0.5;
  if (d_radial > 0.0 && d_axial > 0.0) {
    return sqrt(d_radial * d_radial + d_axial * d_axial);
  }
  return max(d_radial, d_axial);
}

fn sdf_box(p: vec3<f32>, half_size: vec3<f32>) -> f32 {
  let q = abs(p) - half_size;
  let outside = length(max(q, vec3<f32>(0.0)));
  let inside = min(max(q.x, max(q.y, q.z)), 0.0);
  return outside + inside;
}

fn rotate_axis_angle_inv(p: vec3<f32>, axis: vec3<f32>, angle: f32) -> vec3<f32> {
  let len_axis = length(axis);
  if (len_axis <= 1e-6) { return p; }
  let u = axis / len_axis;
  let a = -angle;
  let c = cos(a);
  let s = sin(a);
  return p * c + cross(u, p) * s + u * dot(u, p) * (1.0 - c);
}
"""

    type CodegenCtx =
        { mutable NextId: int
          Declarations: ResizeArray<string> }

    let private createCtx () =
        { NextId = 0
          Declarations = ResizeArray() }

    let private freshId (ctx: CodegenCtx) =
        let id = ctx.NextId
        ctx.NextId <- id + 1
        id

    let private sketchPointExpr (pt: SlotPt2) =
        $"vec2<f32>({slotExpr pt.XSlot}, {slotExpr pt.YSlot})"

    let rec private codegenNode (node: FieldNode) (ctx: CodegenCtx) (pExpr: string) : string =
        match node with
        | FPrimitive prim ->
            match prim with
            | PrimSphere radius ->
                $"sdf_sphere({pExpr}, {slotExpr radius})"
            | PrimCylinder(radius, height) ->
                $"sdf_cylinder({pExpr}, {slotExpr radius}, {slotExpr height})"
            | PrimBox(width, height, depth) ->
                $"sdf_box({pExpr}, vec3<f32>({slotExpr width} * 0.5, {slotExpr height} * 0.5, {slotExpr depth} * 0.5))"
            | PrimHalfPlane(axis, offset, flip) ->
                let comp =
                    match axis with
                    | "X" -> "x"
                    | "Y" -> "y"
                    | _ -> "z"
                let sign = if flip then "-1.0" else "1.0"
                $"((({pExpr}).{comp} - {slotExpr offset}) * {sign})"
        | FTranslate(x, y, z, child) ->
            let childP = $"({pExpr} - vec3<f32>({slotExpr x}, {slotExpr y}, {slotExpr z}))"
            codegenNode child ctx childP
        | FRotate(ax, ay, az, angle, child) ->
            let childP = $"rotate_axis_angle_inv({pExpr}, vec3<f32>({slotExpr ax}, {slotExpr ay}, {slotExpr az}), {slotExpr angle})"
            codegenNode child ctx childP
        | FBoolean(op, radius, a, b) ->
            let ea = codegenNode a ctx pExpr
            let eb = codegenNode b ctx pExpr
            let k = slotExpr radius
            match op with
            | BoolUnion -> $"smooth_min({ea}, {eb}, {k})"
            | BoolIntersect -> $"(-smooth_min(-({ea}), -({eb}), {k}))"
            | BoolSubtract -> $"(-smooth_min(-({ea}), {eb}, {k}))"
        | FFieldOp(op, value, child) ->
            let childExpr = codegenNode child ctx pExpr
            match op with
            | OpThicken -> $"(({childExpr}) - {slotExpr value})"
            | OpShell -> $"max(({childExpr}), -(({childExpr}) + {slotExpr value}))"
        | FSketch sketch ->
            let fnName = $"sketch_{freshId ctx}"
            let sb = StringBuilder()
            sb.AppendLine($"fn {fnName}(p: vec3<f32>) -> f32 {{") |> ignore
            sb.AppendLine("  let lp = p.xy;") |> ignore
            sb.AppendLine("  var min_d: f32 = 1e10;") |> ignore

            sketch.Primitives
            |> List.iteri (fun i prim ->
                match prim with
                | SpLineSegment(startP, endP) ->
                    sb.AppendLine($"  let l{i}_a = {sketchPointExpr startP};") |> ignore
                    sb.AppendLine($"  let l{i}_b = {sketchPointExpr endP};") |> ignore
                    sb.AppendLine($"  min_d = min(min_d, seg_dist(lp, l{i}_a, l{i}_b));") |> ignore
                | SpCircle(center, radiusSlot) ->
                    sb.AppendLine($"  let c{i}_center = {sketchPointExpr center};") |> ignore
                    sb.AppendLine($"  min_d = min(min_d, circle_curve_dist(lp, c{i}_center, {slotExpr radiusSlot}));") |> ignore
                | SpArcCenter(startP, endP, center, clockwise) ->
                    let cw = if clockwise then "true" else "false"
                    sb.AppendLine($"  let a{i}_start = {sketchPointExpr startP};") |> ignore
                    sb.AppendLine($"  let a{i}_end = {sketchPointExpr endP};") |> ignore
                    sb.AppendLine($"  let a{i}_center = {sketchPointExpr center};") |> ignore
                    sb.AppendLine($"  min_d = min(min_d, arc_curve_dist(lp, a{i}_start, a{i}_end, a{i}_center, {cw}));") |> ignore)

            if sketch.Closed then
                sb.AppendLine("  var crossings: i32 = 0;") |> ignore
                sketch.Primitives
                |> List.iteri (fun i prim ->
                    match prim with
                    | SpLineSegment _ ->
                        sb.AppendLine($"  crossings += ray_cross_line_segment(lp, l{i}_a, l{i}_b);") |> ignore
                    | SpCircle(_, radiusSlot) ->
                        sb.AppendLine($"  crossings += ray_cross_circle(lp, c{i}_center, {slotExpr radiusSlot});") |> ignore
                    | SpArcCenter(_, _, _, clockwise) ->
                        let cw = if clockwise then "true" else "false"
                        sb.AppendLine($"  crossings += ray_cross_arc(lp, a{i}_start, a{i}_end, a{i}_center, {cw});") |> ignore)
                if sketch.Flip then
                    sb.AppendLine("  if ((crossings & 1) != 0) { return min_d; }") |> ignore
                    sb.AppendLine("  return -min_d;") |> ignore
                else
                    sb.AppendLine("  if ((crossings & 1) != 0) { return -min_d; }") |> ignore
                    sb.AppendLine("  return min_d;") |> ignore
            else
                sb.AppendLine("  return min_d;") |> ignore

            sb.AppendLine("}") |> ignore
            ctx.Declarations.Add(sb.ToString())
            $"{fnName}({pExpr})"

    let generateEvalFunctions (surfaces: FieldSurface list) =
        let ctx = createCtx ()
        let evals =
            surfaces
            |> List.mapi (fun i surface ->
                let expr = codegenNode surface.Field ctx "p"
                i, expr)
        let sb = StringBuilder()
        sb.AppendLine(WGSL_HELPERS) |> ignore
        for decl in ctx.Declarations do
            sb.AppendLine(decl) |> ignore
        for (i, expr) in evals do
            sb.AppendLine($"fn eval_sdf_{i}(p: vec3<f32>) -> f32 {{") |> ignore
            sb.AppendLine($"  return {expr};") |> ignore
            sb.AppendLine("}") |> ignore
        sb.ToString()

    // ── Interval-arithmetic WGSL codegen ────────────────────────────────────
    //
    // Produces per-surface
    // `interval_sdf_{i}(box) -> Intv` — a conservative [lo, hi] bound on the
    // surface's SDF over an axis-aligned box of input intervals.
    //
    // Block-probe raymarching calls this per screen-block to determine which
    // surfaces are alive in the block (isovalue ∈ [lo, hi]); dead surfaces
    // are skipped in the per-pixel sphere-trace.
    //
    // Soundness: unsupported constructs (FRotate, FSketch) return the
    // "unknown" interval [-INF, +INF], which keeps masking sound but gives
    // up any pruning for blocks containing them.
    [<Literal>]
    let WGSL_INTERVAL_HELPERS = """
struct Intv { lo: f32, hi: f32 };
struct IntvBox { xi: Intv, yi: Intv, zi: Intv };

const IV_BIG: f32 = 1.0e30;

fn iv_single(v: f32) -> Intv { return Intv(v, v); }
fn iv_unknown() -> Intv { return Intv(-IV_BIG, IV_BIG); }

fn iv_add(a: Intv, b: Intv) -> Intv { return Intv(a.lo + b.lo, a.hi + b.hi); }
fn iv_sub(a: Intv, b: Intv) -> Intv { return Intv(a.lo - b.hi, a.hi - b.lo); }
fn iv_neg(a: Intv) -> Intv { return Intv(-a.hi, -a.lo); }
fn iv_sub_scalar(a: Intv, s: f32) -> Intv { return Intv(a.lo - s, a.hi - s); }

fn iv_abs(a: Intv) -> Intv {
  if (a.lo >= 0.0) { return a; }
  if (a.hi <= 0.0) { return Intv(-a.hi, -a.lo); }
  return Intv(0.0, max(-a.lo, a.hi));
}

fn iv_square(a: Intv) -> Intv {
  if (a.lo >= 0.0) { return Intv(a.lo * a.lo, a.hi * a.hi); }
  if (a.hi <= 0.0) { return Intv(a.hi * a.hi, a.lo * a.lo); }
  return Intv(0.0, max(a.lo * a.lo, a.hi * a.hi));
}

fn iv_sqrt(a: Intv) -> Intv {
  return Intv(sqrt(max(a.lo, 0.0)), sqrt(max(a.hi, 0.0)));
}

fn iv_imin(a: Intv, b: Intv) -> Intv { return Intv(min(a.lo, b.lo), min(a.hi, b.hi)); }
fn iv_imax(a: Intv, b: Intv) -> Intv { return Intv(max(a.lo, b.lo), max(a.hi, b.hi)); }

// smooth_min widening: smooth_min(a,b,k) ∈ [min(a,b) - k/6, min(a,b)].
fn iv_smooth_min(a: Intv, b: Intv, k: f32) -> Intv {
  let sharp = iv_imin(a, b);
  if (k <= 0.0) { return sharp; }
  return Intv(sharp.lo - k / 6.0, sharp.hi);
}

fn ivbox_sub(b: IntvBox, d: vec3<f32>) -> IntvBox {
  return IntvBox(
    Intv(b.xi.lo - d.x, b.xi.hi - d.x),
    Intv(b.yi.lo - d.y, b.yi.hi - d.y),
    Intv(b.zi.lo - d.z, b.zi.hi - d.z)
  );
}

// ── Per-primitive interval bounds ──────────────────────────────────────

fn interval_sphere(b: IntvBox, r: f32) -> Intv {
  let sum_sq = iv_add(iv_add(iv_square(b.xi), iv_square(b.yi)), iv_square(b.zi));
  return iv_sub_scalar(iv_sqrt(sum_sq), r);
}

fn interval_box(b: IntvBox, hx: f32, hy: f32, hz: f32) -> Intv {
  let qx = iv_sub_scalar(iv_abs(b.xi), hx);
  let qy = iv_sub_scalar(iv_abs(b.yi), hy);
  let qz = iv_sub_scalar(iv_abs(b.zi), hz);
  let zero = iv_single(0.0);
  let mx = iv_imax(qx, zero);
  let my = iv_imax(qy, zero);
  let mz = iv_imax(qz, zero);
  let outside = iv_sqrt(iv_add(iv_add(iv_square(mx), iv_square(my)), iv_square(mz)));
  let inside = iv_imin(iv_imax(qx, iv_imax(qy, qz)), zero);
  return iv_add(outside, inside);
}

fn interval_cylinder(b: IntvBox, r: f32, half_h: f32) -> Intv {
  // Axial along Z; radial = length(p.xy). Matches sdf_cylinder.
  let d_radial = iv_sub_scalar(iv_sqrt(iv_add(iv_square(b.xi), iv_square(b.yi))), r);
  let d_axial = iv_sub_scalar(iv_abs(b.zi), half_h);
  let branch_out = iv_sqrt(iv_add(iv_square(d_radial), iv_square(d_axial)));
  let branch_in = iv_imax(d_radial, d_axial);
  // Definitely outside → branch_out; definitely on an axis → branch_in;
  // ambiguous → hull of both (loose but sound).
  if (d_radial.lo > 0.0 && d_axial.lo > 0.0) { return branch_out; }
  if (d_radial.hi <= 0.0 || d_axial.hi <= 0.0) { return branch_in; }
  return Intv(min(branch_out.lo, branch_in.lo), max(branch_out.hi, branch_in.hi));
}

fn interval_halfplane_x(b: IntvBox, off: f32, flip: i32) -> Intv {
  let raw = iv_sub_scalar(b.xi, off);
  if (flip != 0) { return iv_neg(raw); }
  return raw;
}
fn interval_halfplane_y(b: IntvBox, off: f32, flip: i32) -> Intv {
  let raw = iv_sub_scalar(b.yi, off);
  if (flip != 0) { return iv_neg(raw); }
  return raw;
}
fn interval_halfplane_z(b: IntvBox, off: f32, flip: i32) -> Intv {
  let raw = iv_sub_scalar(b.zi, off);
  if (flip != 0) { return iv_neg(raw); }
  return raw;
}
"""

    // Emits a WGSL expression that computes the interval bound of `node`
    // over the input box expression `boxExpr`. Rotations and sketches punt
    // to `iv_unknown()` — same as the CPU evaluator.
    let rec private codegenIntervalNode (node: FieldNode) (boxExpr: string) : string =
        match node with
        | FPrimitive prim ->
            match prim with
            | PrimSphere radius ->
                $"interval_sphere({boxExpr}, {slotExpr radius})"
            | PrimCylinder(radius, height) ->
                $"interval_cylinder({boxExpr}, {slotExpr radius}, {slotExpr height} * 0.5)"
            | PrimBox(width, height, depth) ->
                $"interval_box({boxExpr}, {slotExpr width} * 0.5, {slotExpr height} * 0.5, {slotExpr depth} * 0.5)"
            | PrimHalfPlane(axis, offset, flip) ->
                let fn =
                    match axis with
                    | "X" -> "interval_halfplane_x"
                    | "Y" -> "interval_halfplane_y"
                    | _ -> "interval_halfplane_z"
                let flipI = if flip then "1" else "0"
                $"{fn}({boxExpr}, {slotExpr offset}, {flipI})"
        | FTranslate(x, y, z, child) ->
            let childBox =
                $"ivbox_sub({boxExpr}, vec3<f32>({slotExpr x}, {slotExpr y}, {slotExpr z}))"
            codegenIntervalNode child childBox
        | FRotate _ -> "iv_unknown()"
        | FBoolean(op, radius, a, b) ->
            let ea = codegenIntervalNode a boxExpr
            let eb = codegenIntervalNode b boxExpr
            let k = slotExpr radius
            match op with
            | BoolUnion -> $"iv_smooth_min({ea}, {eb}, {k})"
            | BoolIntersect -> $"iv_neg(iv_smooth_min(iv_neg({ea}), iv_neg({eb}), {k}))"
            | BoolSubtract -> $"iv_neg(iv_smooth_min(iv_neg({ea}), {eb}, {k}))"
        | FFieldOp(op, value, child) ->
            let ec = codegenIntervalNode child boxExpr
            match op with
            | OpThicken -> $"iv_sub_scalar({ec}, {slotExpr value})"
            | OpShell ->
                // max(child, -(child + v)) — child is duplicated in WGSL.
                $"iv_imax({ec}, iv_neg(iv_add({ec}, iv_single({slotExpr value}))))"
        | FSketch _ -> "iv_unknown()"

    let generateIntervalFunctions (surfaces: FieldSurface list) =
        let sb = StringBuilder()
        sb.AppendLine(WGSL_INTERVAL_HELPERS) |> ignore
        surfaces
        |> List.iteri (fun i surface ->
            let expr = codegenIntervalNode surface.Field "box"
            sb.AppendLine($"fn interval_sdf_{i}(box: IntvBox) -> Intv {{") |> ignore
            sb.AppendLine($"  return {expr};") |> ignore
            sb.AppendLine("}") |> ignore)
        sb.ToString()
