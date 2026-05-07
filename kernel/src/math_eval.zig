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
const SlotPoint2 = math_ir.SlotPoint2;
const Interval = math_ir.Interval;
const Box3 = math_ir.Box3;
const MathIR = math_ir.MathIR;
const SketchPrimitive = math_ir.SketchPrimitive;
const Intrinsic = math_ir.Intrinsic;

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
fn v2Len(a: Vec2) f64 {
    return @sqrt(v2Dot(a, a));
}

pub fn slotValue(slots: []const f64, id: i32) f64 {
    return slots[@intCast(id)];
}

fn slotPoint2(slots: []const f64, p: SlotPoint2) Vec2 {
    return v2(slotValue(slots, p.x), slotValue(slots, p.y));
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
    };
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

pub fn evalSketchDistance(primitive: SketchPrimitive, slots: []const f64, p: Vec3, plane: Plane) f64 {
    const q = planePoint(p, plane);
    return switch (primitive.kind) {
        .line_segment => segDist(q, slotPoint2(slots, primitive.p0), slotPoint2(slots, primitive.p1)),
        .bezier_quadratic => quadraticCurveDist(q, slotPoint2(slots, primitive.p0), slotPoint2(slots, primitive.p1), slotPoint2(slots, primitive.p2)),
        .circle => circleCurveDist(q, slotPoint2(slots, primitive.p0), slotValue(slots, primitive.radius)),
        else => math.nan(f64),
    };
}

fn segmentWindingAngle(p: Vec2, a: Vec2, b: Vec2) f64 {
    const v0x = a.x - p.x;
    const v0y = a.y - p.y;
    const v1x = b.x - p.x;
    const v1y = b.y - p.y;
    return math.atan2(v0x * v1y - v0y * v1x, v0x * v1x + v0y * v1y);
}

fn primitiveWindingAngle(primitive: SketchPrimitive, slots: []const f64, q: Vec2) f64 {
    return switch (primitive.kind) {
        .line_segment => segmentWindingAngle(q, slotPoint2(slots, primitive.p0), slotPoint2(slots, primitive.p1)),
        .bezier_quadratic => blk: {
            const p0 = slotPoint2(slots, primitive.p0);
            const p1 = slotPoint2(slots, primitive.p1);
            const p2 = slotPoint2(slots, primitive.p2);
            var total: f64 = 0.0;
            var prev = bezierQuadraticPoint(0.0, p0, p1, p2);
            var i: usize = 1;
            while (i <= 16) : (i += 1) {
                const curr = bezierQuadraticPoint(@as(f64, @floatFromInt(i)) / 16.0, p0, p1, p2);
                total += segmentWindingAngle(q, prev, curr);
                prev = curr;
            }
            break :blk total;
        },
        else => 0.0,
    };
}

fn sketchPathDist(ir: *const MathIR, intrinsic: Intrinsic, slots: []const f64, p: Vec3) f64 {
    const q = planePoint(p, intrinsic.plane);
    var unsigned: f64 = 1.0e30;
    var winding: f64 = 0.0;
    var i: usize = 0;
    while (i < @as(usize, @intCast(intrinsic.primitive_count))) : (i += 1) {
        const primitive = ir.primitives[@as(usize, @intCast(intrinsic.primitive_start)) + i];
        unsigned = min(unsigned, evalSketchDistance(primitive, slots, p, intrinsic.plane));
        winding += primitiveWindingAngle(primitive, slots, q);
    }
    var signed = unsigned;
    if (intrinsic.closed) {
        if (intrinsic.primitive_count == 1 and ir.primitives[@intCast(intrinsic.primitive_start)].kind == .circle) {
            const c = ir.primitives[@intCast(intrinsic.primitive_start)];
            signed = v2Len(v2Sub(q, slotPoint2(slots, c.p0))) - slotValue(slots, c.radius);
        } else if (absFloat(winding) > math.pi) {
            signed = -unsigned;
        }
    }
    return if (intrinsic.flip) -signed else signed;
}

pub fn evalIntrinsicPoint(ir: *const MathIR, intrinsic: Intrinsic, slots: []const f64, p: Vec3) f64 {
    return switch (intrinsic.kind) {
        .sketch_distance => evalSketchDistance(ir.primitives[@intCast(intrinsic.primitive_start)], slots, p, intrinsic.plane),
        .sketch_path => sketchPathDist(ir, intrinsic, slots, p),
        .curve_distance_along => math.nan(f64),
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