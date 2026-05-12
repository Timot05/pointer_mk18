const std = @import("std");
const math = std.math;
const math_ir = @import("math_ir.zig");

const Axis = math_ir.Axis;
const Plane = math_ir.Plane;
const Unary = math_ir.Unary;
const Binary = math_ir.Binary;
const Expr = math_ir.Expr;
const Vec2 = math_ir.Vec2;
const Vec3 = math_ir.Vec3;
const Interval = math_ir.Interval;
const Box3 = math_ir.Box3;
const MathIR = math_ir.MathIR;
const Intrinsic = math_ir.Intrinsic;
const FoldOp = math_ir.FoldOp;
const Node = math_ir.Node;

pub fn interval(lo: f64, hi: f64) Interval {
    return if (lo <= hi) .{ .lo = lo, .hi = hi } else .{ .lo = hi, .hi = lo };
}

pub fn singleton(value: f64) Interval {
    return .{ .lo = value, .hi = value };
}

pub fn unknown() Interval {
    return .{ .lo = -1.0e30, .hi = 1.0e30 };
}

pub fn box3(xi: Interval, yi: Interval, zi: Interval) Box3 {
    return .{ .xi = xi, .yi = yi, .zi = zi };
}

fn axisPoint(p: Vec3, axis: i32) f64 {
    return switch (@as(Axis, @enumFromInt(axis))) {
        .x => p.x,
        .y => p.y,
        .z => p.z,
    };
}

pub fn axisInterval(box: Box3, axis: i32) Interval {
    return switch (@as(Axis, @enumFromInt(axis))) {
        .x => box.xi,
        .y => box.yi,
        .z => box.zi,
    };
}

fn absFloat(v: f64) f64 {
    return if (v < 0.0) -v else v;
}

fn min(a: f64, b: f64) f64 {
    return if (a < b) a else b;
}
fn max(a: f64, b: f64) f64 {
    return if (a > b) a else b;
}
fn clamp(lo: f64, hi: f64, value: f64) f64 {
    return min(hi, max(lo, value));
}

fn v2(x_: f64, y_: f64) Vec2 {
    return .{ .x = x_, .y = y_ };
}
fn v2Sub(a: Vec2, b: Vec2) Vec2 {
    return v2(a.x - b.x, a.y - b.y);
}
fn v2Scale(a: Vec2, s: f64) Vec2 {
    return v2(a.x * s, a.y * s);
}
fn v2Dot(a: Vec2, b: Vec2) f64 {
    return a.x * b.x + a.y * b.y;
}
fn v2Cross(a: Vec2, b: Vec2) f64 {
    return a.x * b.y - a.y * b.x;
}
fn v2Len(a: Vec2) f64 {
    return @sqrt(v2Dot(a, a));
}

pub fn slotValue(slots: []const f64, id: i32) f64 {
    return slots[@intCast(id)];
}

fn planePoint(p: Vec3, plane: Plane) Vec2 {
    return switch (plane) {
        .xy => v2(p.x, p.y),
        .xz => v2(p.x, p.z),
        .yz => v2(p.y, p.z),
    };
}

fn intervalRadius(a: Interval) f64 {
    return (a.hi - a.lo) * 0.5;
}

pub fn boxCenter(box: Box3) Vec3 {
    return .{
        .x = (box.xi.lo + box.xi.hi) * 0.5,
        .y = (box.yi.lo + box.yi.hi) * 0.5,
        .z = (box.zi.lo + box.zi.hi) * 0.5,
    };
}

pub fn planeBoxRadius(box: Box3, plane: Plane) f64 {
    return switch (plane) {
        .xy => @sqrt(intervalRadius(box.xi) * intervalRadius(box.xi) + intervalRadius(box.yi) * intervalRadius(box.yi)),
        .xz => @sqrt(intervalRadius(box.xi) * intervalRadius(box.xi) + intervalRadius(box.zi) * intervalRadius(box.zi)),
        .yz => @sqrt(intervalRadius(box.yi) * intervalRadius(box.yi) + intervalRadius(box.zi) * intervalRadius(box.zi)),
    };
}

fn remEuclid(a: f64, b: f64) f64 {
    var r = @mod(a, b);
    if (r < 0.0) r += absFloat(b);
    return r;
}

pub fn evalUnaryPoint(op: Unary, a: f64) f64 {
    return switch (op) {
        .neg => -a,
        .abs => absFloat(a),
        .recip => 1.0 / a,
        .square => a * a,
        .sqrt => @sqrt(a),
        .floor => @floor(a),
        .ceil => @ceil(a),
        .round => @floor(a + 0.5),
        .sin => @sin(a),
        .cos => @cos(a),
        .tan => @tan(a),
        .asin => math.asin(a),
        .acos => math.acos(a),
        .atan => math.atan(a),
        .exp => @exp(a),
        .ln => @log(a),
        .not => if (a == 0.0) 1.0 else 0.0,
    };
}

pub fn evalBinaryPoint(op: Binary, a: f64, b: f64) f64 {
    return switch (op) {
        .add => a + b,
        .sub => a - b,
        .mul => a * b,
        .div => a / b,
        .atan2 => math.atan2(a, b),
        .min => min(a, b),
        .max => max(a, b),
        .pow => math.pow(f64, a, b),
        .compare => if (a < b) -1.0 else if (a == b) 0.0 else 1.0,
        .mod => remEuclid(a, b),
        .and_ => if (a == 0.0) a else b,
        .or_ => if (a != 0.0) a else b,
    };
}

pub fn evalPoint(ir: *const MathIR, root: Expr, slots: []const f64, p: Vec3) f64 {
    const node = ir.nodes[@intCast(root.id)];
    return switch (node.kind) {
        .var_ => axisPoint(p, node.op),
        .slot => slotValue(slots, node.op),
        .const_ => node.value,
        .unary => evalUnaryPoint(@enumFromInt(node.op), evalPoint(ir, .{ .id = node.a }, slots, p)),
        .binary => evalBinaryPoint(@enumFromInt(node.op), evalPoint(ir, .{ .id = node.a }, slots, p), evalPoint(ir, .{ .id = node.b }, slots, p)),
        .remap_axes => evalPoint(ir, .{ .id = node.a }, slots, .{
            .x = evalPoint(ir, .{ .id = node.b }, slots, p),
            .y = evalPoint(ir, .{ .id = node.c }, slots, p),
            .z = evalPoint(ir, .{ .id = node.d }, slots, p),
        }),
        .remap_affine => blk: {
            const a = ir.affines[@intCast(node.b)];
            break :blk evalPoint(ir, .{ .id = node.a }, slots, .{
                .x = evalPoint(ir, a.m00, slots, p) * p.x + evalPoint(ir, a.m01, slots, p) * p.y + evalPoint(ir, a.m02, slots, p) * p.z + evalPoint(ir, a.m03, slots, p),
                .y = evalPoint(ir, a.m10, slots, p) * p.x + evalPoint(ir, a.m11, slots, p) * p.y + evalPoint(ir, a.m12, slots, p) * p.z + evalPoint(ir, a.m13, slots, p),
                .z = evalPoint(ir, a.m20, slots, p) * p.x + evalPoint(ir, a.m21, slots, p) * p.y + evalPoint(ir, a.m22, slots, p) * p.z + evalPoint(ir, a.m23, slots, p),
            });
        },
        .intrinsic => evalIntrinsicPoint(ir, ir.intrinsics[@intCast(node.a)], slots, p),
        .fold => foldPoint(ir, node, slots, p),
        .line_segment => evalLineSegmentPoint(ir, node, slots, p),
        .circle => evalCirclePoint(ir, node, slots, p),
        .bezier_quadratic => evalBezierQuadraticPoint(ir, node, slots, p),
        // BezierCubic / ArcCenter: unimplemented in the legacy primitive
        // evaluator (returns NaN there). Keep the same behaviour for now;
        // a real implementation can drop in alongside the others.
        .bezier_cubic, .arc_center => math.nan(f64),
    };
}

fn foldPoint(ir: *const MathIR, node: Node, slots: []const f64, p: Vec3) f64 {
    const start: usize = @intCast(node.a);
    const count: usize = @intCast(node.b);
    if (count == 0) return 0.0;
    const op: FoldOp = @enumFromInt(node.op);
    var acc = evalPoint(ir, .{ .id = ir.node_refs[start] }, slots, p);
    var i: usize = 1;
    while (i < count) : (i += 1) {
        const v = evalPoint(ir, .{ .id = ir.node_refs[start + i] }, slots, p);
        acc = switch (op) {
            .min => min(acc, v),
            .max => max(acc, v),
            .sum => acc + v,
        };
    }
    return acc;
}

fn segDist(p: Vec2, a: Vec2, b: Vec2) f64 {
    const e = v2Sub(b, a);
    const w = v2Sub(p, a);
    const t = clamp(0.0, 1.0, v2Dot(w, e) / (v2Dot(e, e) + 1.0e-20));
    return v2Len(v2Sub(w, v2Scale(e, t)));
}

fn circleCurveDist(p: Vec2, center: Vec2, radius: f64) f64 {
    return absFloat(v2Len(v2Sub(p, center)) - radius);
}

fn v2Add(a: Vec2, b: Vec2) Vec2 {
    return v2(a.x + b.x, a.y + b.y);
}

fn safeDenominator(value: f64, eps: f64) f64 {
    const e = max(absFloat(eps), 1.0e-12);
    if (absFloat(value) >= e) return value;
    return if (value < 0.0) -e else e;
}

fn bezierQuadraticPoint(t: f64, p0: Vec2, p1: Vec2, p2: Vec2) Vec2 {
    const u = 1.0 - t;
    return v2Add(v2Add(v2Scale(p0, u * u), v2Scale(p1, 2.0 * u * t)), v2Scale(p2, t * t));
}

fn bezierQuadraticD1(t: f64, p0: Vec2, p1: Vec2, p2: Vec2) Vec2 {
    return v2Add(v2Scale(v2Sub(p1, p0), 2.0 * (1.0 - t)), v2Scale(v2Sub(p2, p1), 2.0 * t));
}

fn bezierQuadraticD2(p0: Vec2, p1: Vec2, p2: Vec2) Vec2 {
    return v2Scale(v2Add(v2Sub(p2, v2Scale(p1, 2.0)), p0), 2.0);
}

fn quadraticCurveDist(p: Vec2, p0: Vec2, p1: Vec2, p2: Vec2) f64 {
    var best = min(v2Len(v2Sub(p0, p)), v2Len(v2Sub(p2, p)));
    var seed_i: usize = 0;
    while (seed_i < 5) : (seed_i += 1) {
        var t = @as(f64, @floatFromInt(seed_i)) * 0.25;
        var i: usize = 0;
        while (i < 8) : (i += 1) {
            const c = bezierQuadraticPoint(t, p0, p1, p2);
            const d1 = bezierQuadraticD1(t, p0, p1, p2);
            const d2 = bezierQuadraticD2(p0, p1, p2);
            const v = v2Sub(c, p);
            const g = v2Dot(v, d1);
            const gp = v2Dot(d1, d1) + v2Dot(v, d2);
            t = clamp(0.0, 1.0, t - g / safeDenominator(gp, 1.0e-9));
        }
        best = min(best, v2Len(v2Sub(bezierQuadraticPoint(t, p0, p1, p2), p)));
    }
    return best;
}

// ── Primitive-as-node evaluators ─────────────────────────────────────────
//
// `LineSegment`/`Circle`/etc. NodeKinds carry their geometry as child node
// refs in `ir.node_refs[a..a+b]`; the plane is `op / 2`; primitive-specific
// flags (e.g. clockwise) live in `op & 1`. These helpers extract the coords
// by `evalPoint`-ing each child and call the 2D distance math.

inline fn primitivePlane(node: Node) Plane {
    return @enumFromInt(@divTrunc(node.op, 2));
}

inline fn primitiveChild(ir: *const MathIR, node: Node, k: usize, slots: []const f64, p: Vec3) f64 {
    return evalPoint(ir, .{ .id = ir.node_refs[@intCast(node.a + @as(i32, @intCast(k)))] }, slots, p);
}

pub fn evalLineSegmentPoint(ir: *const MathIR, node: Node, slots: []const f64, p: Vec3) f64 {
    const q = planePoint(p, primitivePlane(node));
    const p0 = v2(primitiveChild(ir, node, 0, slots, p), primitiveChild(ir, node, 1, slots, p));
    const p1 = v2(primitiveChild(ir, node, 2, slots, p), primitiveChild(ir, node, 3, slots, p));
    return segDist(q, p0, p1);
}

pub fn evalCirclePoint(ir: *const MathIR, node: Node, slots: []const f64, p: Vec3) f64 {
    const q = planePoint(p, primitivePlane(node));
    const c = v2(primitiveChild(ir, node, 0, slots, p), primitiveChild(ir, node, 1, slots, p));
    const r = primitiveChild(ir, node, 2, slots, p);
    return circleCurveDist(q, c, r);
}

pub fn evalBezierQuadraticPoint(ir: *const MathIR, node: Node, slots: []const f64, p: Vec3) f64 {
    const q = planePoint(p, primitivePlane(node));
    const p0 = v2(primitiveChild(ir, node, 0, slots, p), primitiveChild(ir, node, 1, slots, p));
    const p1 = v2(primitiveChild(ir, node, 2, slots, p), primitiveChild(ir, node, 3, slots, p));
    const p2 = v2(primitiveChild(ir, node, 4, slots, p), primitiveChild(ir, node, 5, slots, p));
    return quadraticCurveDist(q, p0, p1, p2);
}

const AxisHit = struct {
    found: bool = false,
    s: f64 = 0.0,
    score: f64 = 0.0,
};

fn offerAxisHit(best: *AxisHit, s: f64, score: f64) void {
    if (!math.isFinite(s) or !math.isFinite(score)) return;
    if (!best.found or score < best.score) {
        best.* = .{ .found = true, .s = s, .score = score };
    }
}

fn offerCurvePointFallback(best: *AxisHit, q: Vec2, dir: Vec2, point: Vec2) void {
    const delta = v2Sub(point, q);
    const s = v2Dot(delta, dir);
    const perpendicular = absFloat(v2Cross(delta, dir));
    offerAxisHit(best, s, perpendicular);
}

fn endpoint_axis_fallback(q: Vec2, dir: Vec2, a: Vec2, b: Vec2) f64 {
    var best = AxisHit{};
    offerCurvePointFallback(&best, q, dir, a);
    offerCurvePointFallback(&best, q, dir, b);
    return if (best.found) best.s else math.nan(f64);
}

fn solveQuadratic(a: f64, b: f64, c: f64, roots: *[2]f64) usize {
    const eps = 1.0e-12;
    if (absFloat(a) < eps) {
        if (absFloat(b) < eps) return 0;
        roots[0] = -c / b;
        return 1;
    }
    const disc = b * b - 4.0 * a * c;
    if (disc < -eps) return 0;
    if (absFloat(disc) <= eps) {
        roots[0] = -b / (2.0 * a);
        return 1;
    }
    const sqrt_disc = @sqrt(disc);
    roots[0] = (-b - sqrt_disc) / (2.0 * a);
    roots[1] = (-b + sqrt_disc) / (2.0 * a);
    return 2;
}

fn curveDistanceAlongLineSegment(q: Vec2, dir: Vec2, a: Vec2, b: Vec2) f64 {
    const e = v2Sub(b, a);
    const den = v2Cross(dir, e);
    if (absFloat(den) >= 1.0e-12) {
        const aq = v2Sub(a, q);
        return v2Cross(aq, e) / den;
    }
    return endpoint_axis_fallback(q, dir, a, b);
}

fn curveDistanceAlongCircle(q: Vec2, dir: Vec2, center: Vec2, radius: f64) f64 {
    var best = AxisHit{};
    const m = v2Sub(q, center);
    const b = 2.0 * v2Dot(m, dir);
    const c = v2Dot(m, m) - radius * radius;
    var roots: [2]f64 = undefined;
    const count = solveQuadratic(1.0, b, c, &roots);
    var i: usize = 0;
    while (i < count) : (i += 1) {
        offerAxisHit(&best, roots[i], absFloat(roots[i]));
    }
    if (!best.found) {
        // No axis-line intersection. Return the signed axis offset to the
        // closest approach to the circle's center instead of poisoning the
        // field with NaN.
        const s_center = v2Dot(v2Sub(center, q), dir);
        offerAxisHit(&best, s_center, absFloat(v2Cross(v2Sub(center, q), dir)) - radius);
    }
    return if (best.found) best.s else math.nan(f64);
}

fn curveDistanceAlongQuadratic(q: Vec2, dir: Vec2, p0: Vec2, p1: Vec2, p2: Vec2) f64 {
    var best = AxisHit{};
    var tangentBest = AxisHit{};

    // C(t) = a*t^2 + b*t + c in power basis.
    const qa = v2Add(v2Sub(p2, v2Scale(p1, 2.0)), p0);
    const qb = v2Scale(v2Sub(p1, p0), 2.0);
    const qc = v2Sub(p0, q);

    var roots: [2]f64 = undefined;
    const count = solveQuadratic(v2Cross(qa, dir), v2Cross(qb, dir), v2Cross(qc, dir), &roots);
    var i: usize = 0;
    while (i < count) : (i += 1) {
        const t = roots[i];
        if (t >= -1.0e-9 and t <= 1.0 + 1.0e-9) {
            const cpt = bezierQuadraticPoint(clamp(0.0, 1.0, t), p0, p1, p2);
            const s = v2Dot(v2Sub(cpt, q), dir);
            offerAxisHit(&best, s, absFloat(s));
        } else {
            const tangentS =
                if (t < 0.0)
                    curveDistanceAlongLineSegment(q, dir, p0, v2Add(p0, v2Sub(p1, p0)))
                else
                    curveDistanceAlongLineSegment(q, dir, p2, v2Add(p2, v2Sub(p2, p1)));
            offerAxisHit(&tangentBest, tangentS, absFloat(t));
        }
    }
    if (!best.found) {
        if (tangentBest.found) {
            best = tangentBest;
        } else {
            offerCurvePointFallback(&best, q, dir, p0);
            offerCurvePointFallback(&best, q, dir, p2);
        }
    }
    return if (best.found) best.s else math.nan(f64);
}

fn planeAxis(axis: Vec3, plane: Plane) Vec2 {
    return switch (plane) {
        .xy => v2(axis.x, axis.y),
        .xz => v2(axis.x, axis.z),
        .yz => v2(axis.y, axis.z),
    };
}

/// Compute the signed axis offset for a single primitive subtree node.
/// Reads the primitive's coord children via `evalPoint` and dispatches on
/// the primitive's NodeKind. Returns NaN for primitive kinds without an
/// axis-distance implementation (bezier_cubic, arc_center today).
fn curveDistanceAlongPrimitive(
    ir: *const MathIR,
    primitive_node_id: i32,
    q: Vec2,
    dir: Vec2,
    slots: []const f64,
    p: Vec3,
) f64 {
    const node = ir.nodes[@intCast(primitive_node_id)];
    return switch (node.kind) {
        .line_segment => blk: {
            const p0 = v2(primitiveChild(ir, node, 0, slots, p), primitiveChild(ir, node, 1, slots, p));
            const p1 = v2(primitiveChild(ir, node, 2, slots, p), primitiveChild(ir, node, 3, slots, p));
            break :blk curveDistanceAlongLineSegment(q, dir, p0, p1);
        },
        .circle => blk: {
            const c = v2(primitiveChild(ir, node, 0, slots, p), primitiveChild(ir, node, 1, slots, p));
            const r = primitiveChild(ir, node, 2, slots, p);
            break :blk curveDistanceAlongCircle(q, dir, c, r);
        },
        .bezier_quadratic => blk: {
            const p0 = v2(primitiveChild(ir, node, 0, slots, p), primitiveChild(ir, node, 1, slots, p));
            const p1 = v2(primitiveChild(ir, node, 2, slots, p), primitiveChild(ir, node, 3, slots, p));
            const p2 = v2(primitiveChild(ir, node, 4, slots, p), primitiveChild(ir, node, 5, slots, p));
            break :blk curveDistanceAlongQuadratic(q, dir, p0, p1, p2);
        },
        else => math.nan(f64),
    };
}

fn curveDistanceAlong(ir: *const MathIR, intrinsic: Intrinsic, slots: []const f64, p: Vec3) f64 {
    if (intrinsic.primitive_start < 0 or intrinsic.primitive_count <= 0) return math.nan(f64);
    if (intrinsic.ax < 0 or intrinsic.ay < 0 or intrinsic.az < 0) return math.nan(f64);

    const axis3 = Vec3{
        .x = evalPoint(ir, .{ .id = intrinsic.ax }, slots, p),
        .y = evalPoint(ir, .{ .id = intrinsic.ay }, slots, p),
        .z = evalPoint(ir, .{ .id = intrinsic.az }, slots, p),
    };
    const axis2 = planeAxis(axis3, intrinsic.plane);
    const axis_len = v2Len(axis2);
    if (axis_len < 1.0e-12) return math.nan(f64);
    const dir = v2Scale(axis2, 1.0 / axis_len);
    const q = planePoint(p, intrinsic.plane);

    var best = AxisHit{};
    var i: usize = 0;
    while (i < @as(usize, @intCast(intrinsic.primitive_count))) : (i += 1) {
        const child_id = ir.node_refs[@as(usize, @intCast(intrinsic.primitive_start)) + i];
        const s = curveDistanceAlongPrimitive(ir, child_id, q, dir, slots, p);
        offerAxisHit(&best, s, absFloat(s));
    }

    if (!best.found) return math.nan(f64);
    return if (intrinsic.flip) -best.s else best.s;
}

pub fn evalIntrinsicPoint(ir: *const MathIR, intrinsic: Intrinsic, slots: []const f64, p: Vec3) f64 {
    return switch (intrinsic.kind) {
        .curve_distance_along => curveDistanceAlong(ir, intrinsic, slots, p),
    };
}

pub fn iadd(a: Interval, b: Interval) Interval {
    return interval(a.lo + b.lo, a.hi + b.hi);
}
pub fn isub(a: Interval, b: Interval) Interval {
    return interval(a.lo - b.hi, a.hi - b.lo);
}
pub fn ineg(a: Interval) Interval {
    return interval(-a.hi, -a.lo);
}
pub fn iabs(a: Interval) Interval {
    if (a.lo >= 0.0) return a;
    if (a.hi <= 0.0) return interval(-a.hi, -a.lo);
    return interval(0.0, max(-a.lo, a.hi));
}
pub fn isquare(a: Interval) Interval {
    if (a.lo >= 0.0) return interval(a.lo * a.lo, a.hi * a.hi);
    if (a.hi <= 0.0) return interval(a.hi * a.hi, a.lo * a.lo);
    return interval(0.0, max(a.lo * a.lo, a.hi * a.hi));
}
pub fn imul(a: Interval, b: Interval) Interval {
    const v0 = a.lo * b.lo;
    const v1 = a.lo * b.hi;
    const v2_ = a.hi * b.lo;
    const v3 = a.hi * b.hi;
    return interval(min(min(v0, v1), min(v2_, v3)), max(max(v0, v1), max(v2_, v3)));
}
pub fn idiv(a: Interval, b: Interval) Interval {
    if (b.lo <= 0.0 and b.hi >= 0.0) return unknown();
    return imul(a, interval(1.0 / b.hi, 1.0 / b.lo));
}

pub fn evalInterval(ir: *const MathIR, root: Expr, slots: []const f64, box: Box3) Interval {
    const node = ir.nodes[@intCast(root.id)];
    return switch (node.kind) {
        .var_ => axisInterval(box, node.op),
        .slot => singleton(slotValue(slots, node.op)),
        .const_ => singleton(node.value),
        .unary => blk: {
            const a = evalInterval(ir, .{ .id = node.a }, slots, box);
            break :blk switch (@as(Unary, @enumFromInt(node.op))) {
                .neg => ineg(a),
                .abs => iabs(a),
                .recip => idiv(singleton(1.0), a),
                .square => isquare(a),
                .sqrt => interval(@sqrt(max(0.0, a.lo)), @sqrt(max(0.0, a.hi))),
                else => unknown(),
            };
        },
        .binary => blk: {
            const a = evalInterval(ir, .{ .id = node.a }, slots, box);
            const b = evalInterval(ir, .{ .id = node.b }, slots, box);
            break :blk switch (@as(Binary, @enumFromInt(node.op))) {
                .add => iadd(a, b),
                .sub => isub(a, b),
                .mul => imul(a, b),
                .div => idiv(a, b),
                .min => interval(min(a.lo, b.lo), min(a.hi, b.hi)),
                .max => interval(max(a.lo, b.lo), max(a.hi, b.hi)),
                // atan2 ∈ [-π, π] and compare ∈ {-1, 0, +1} regardless of
                // input intervals. Tight bounds here keep `unsigned * sign`
                // chains used by closed-sketch SDFs from blowing up to
                // `unknown` (which was making the renderer never prune).
                .atan2 => interval(-math.pi, math.pi),
                .compare => interval(-1.0, 1.0),
                else => unknown(),
            };
        },
        .remap_axes => evalInterval(ir, .{ .id = node.a }, slots, .{
            .xi = evalInterval(ir, .{ .id = node.b }, slots, box),
            .yi = evalInterval(ir, .{ .id = node.c }, slots, box),
            .zi = evalInterval(ir, .{ .id = node.d }, slots, box),
        }),
        .remap_affine => blk: {
            const a = ir.affines[@intCast(node.b)];
            const xi = box.xi;
            const yi = box.yi;
            const zi = box.zi;
            const m00 = evalInterval(ir, a.m00, slots, box);
            const m01 = evalInterval(ir, a.m01, slots, box);
            const m02 = evalInterval(ir, a.m02, slots, box);
            const m03 = evalInterval(ir, a.m03, slots, box);
            const m10 = evalInterval(ir, a.m10, slots, box);
            const m11 = evalInterval(ir, a.m11, slots, box);
            const m12 = evalInterval(ir, a.m12, slots, box);
            const m13 = evalInterval(ir, a.m13, slots, box);
            const m20 = evalInterval(ir, a.m20, slots, box);
            const m21 = evalInterval(ir, a.m21, slots, box);
            const m22 = evalInterval(ir, a.m22, slots, box);
            const m23 = evalInterval(ir, a.m23, slots, box);
            const new_xi = iadd(iadd(iadd(imul(m00, xi), imul(m01, yi)), imul(m02, zi)), m03);
            const new_yi = iadd(iadd(iadd(imul(m10, xi), imul(m11, yi)), imul(m12, zi)), m13);
            const new_zi = iadd(iadd(iadd(imul(m20, xi), imul(m21, yi)), imul(m22, zi)), m23);
            break :blk evalInterval(ir, .{ .id = node.a }, slots, .{ .xi = new_xi, .yi = new_yi, .zi = new_zi });
        },
        .intrinsic => blk: {
            const intrinsic = ir.intrinsics[@intCast(node.a)];
            if (intrinsic.kind == .curve_distance_along) break :blk unknown();
            const center = boxCenter(box);
            const value = evalIntrinsicPoint(ir, intrinsic, slots, center);
            const radius = planeBoxRadius(box, intrinsic.plane);
            break :blk interval(value - radius, value + radius);
        },
        .fold => blk: {
            const start: usize = @intCast(node.a);
            const count: usize = @intCast(node.b);
            if (count == 0) break :blk singleton(0.0);
            const op: FoldOp = @enumFromInt(node.op);
            var acc = evalInterval(ir, .{ .id = ir.node_refs[start] }, slots, box);
            var i: usize = 1;
            while (i < count) : (i += 1) {
                const v = evalInterval(ir, .{ .id = ir.node_refs[start + i] }, slots, box);
                acc = switch (op) {
                    .min => interval(min(acc.lo, v.lo), min(acc.hi, v.hi)),
                    .max => interval(max(acc.lo, v.lo), max(acc.hi, v.hi)),
                    .sum => iadd(acc, v),
                };
            }
            break :blk acc;
        },
        // Primitives use the same "value at center ± in-plane radius"
        // bound the legacy intrinsic path uses; no tighter interval is
        // available without per-primitive analysis.
        .line_segment, .circle, .bezier_quadratic, .bezier_cubic, .arc_center => blk: {
            const plane = primitivePlane(node);
            const value = evalPoint(ir, root, slots, boxCenter(box));
            const radius = planeBoxRadius(box, plane);
            break :blk interval(value - radius, value + radius);
        },
    };
}


test "interval equality" {
    const int1 = interval(1.0, 2.0);
    const int2 = interval(2.0, 1.0);
    try std.testing.expect(std.meta.eql(int1, int2));
}

test "singleton equality" {
    const int1 = singleton(1.0);
    const int2 = singleton(1.0);
    try std.testing.expect(std.meta.eql(int1, int2));
}

test "interval addition" {
    const int1 = interval(1.0, 2.0);
    const int2 = interval(2.0, 3.0);
    const result = iadd(int1, int2);
    try std.testing.expect(std.meta.eql(result, interval(3.0, 5.0)));
}

test "evalInterval evaluates binary expression over box" {
    var ir: MathIR = .{};
    const x_expr = try ir.x();
    const slot_expr = try ir.slot(0);
    const root = try ir.binary(.add, x_expr, slot_expr);

    const slots = [_]f64{2.5};
    const domain = box3(interval(-1.0, 3.0), interval(10.0, 20.0), interval(-5.0, -4.0));

    const result = evalInterval(&ir, root, &slots, domain);
    try std.testing.expectApproxEqAbs(@as(f64, 1.5), result.lo, 1.0e-12);
    try std.testing.expectApproxEqAbs(@as(f64, 5.5), result.hi, 1.0e-12);
}
