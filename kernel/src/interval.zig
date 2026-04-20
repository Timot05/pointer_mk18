const std = @import("std");
const tape_mod = @import("tape.zig");

// Bitfield layout: Both == Left | Right.
pub const Choice = enum(u8) {
    left = 1,
    right = 2,
    both = 3,
};

pub const Interval = struct {
    lo: f32,
    hi: f32,
};

pub fn iConst(v: f32) Interval {
    return .{ .lo = v, .hi = v };
}

pub fn iAdd(a: Interval, b: Interval) Interval {
    return .{ .lo = a.lo + b.lo, .hi = a.hi + b.hi };
}

pub fn iSub(a: Interval, b: Interval) Interval {
    return .{ .lo = a.lo - b.hi, .hi = a.hi - b.lo };
}

pub fn iNeg(a: Interval) Interval {
    return .{ .lo = -a.hi, .hi = -a.lo };
}

pub fn iAbs(a: Interval) Interval {
    if (a.lo >= 0) return a;
    if (a.hi <= 0) return iNeg(a);
    return .{ .lo = 0, .hi = @max(-a.lo, a.hi) };
}

pub fn iSqrt(a: Interval) Interval {
    const lo: f32 = if (a.lo < 0) 0 else a.lo;
    const hi: f32 = if (a.hi < 0) 0 else a.hi;
    return .{ .lo = @sqrt(lo), .hi = @sqrt(hi) };
}

pub fn iSquare(a: Interval) Interval {
    if (a.lo >= 0) return .{ .lo = a.lo * a.lo, .hi = a.hi * a.hi };
    if (a.hi <= 0) return .{ .lo = a.hi * a.hi, .hi = a.lo * a.lo };
    return .{ .lo = 0, .hi = @max(a.lo * a.lo, a.hi * a.hi) };
}

pub fn iMul(a: Interval, b: Interval) Interval {
    const p1 = a.lo * b.lo;
    const p2 = a.lo * b.hi;
    const p3 = a.hi * b.lo;
    const p4 = a.hi * b.hi;
    return .{
        .lo = @min(@min(p1, p2), @min(p3, p4)),
        .hi = @max(@max(p1, p2), @max(p3, p4)),
    };
}

pub fn iDiv(a: Interval, b: Interval) Interval {
    if (b.lo <= 0 and b.hi >= 0) {
        return .{ .lo = -std.math.inf(f32), .hi = std.math.inf(f32) };
    }
    const inv: Interval = .{ .lo = 1.0 / b.hi, .hi = 1.0 / b.lo };
    return iMul(a, inv);
}

pub const MinMaxResult = struct { interval: Interval, choice: Choice };

pub fn iMin(a: Interval, b: Interval) MinMaxResult {
    const r: Interval = .{ .lo = @min(a.lo, b.lo), .hi = @min(a.hi, b.hi) };
    const c: Choice = if (a.hi <= b.lo) .left else if (b.hi <= a.lo) .right else .both;
    return .{ .interval = r, .choice = c };
}

pub fn iMax(a: Interval, b: Interval) MinMaxResult {
    const r: Interval = .{ .lo = @max(a.lo, b.lo), .hi = @max(a.hi, b.hi) };
    const c: Choice = if (a.lo >= b.hi) .left else if (b.lo >= a.hi) .right else .both;
    return .{ .interval = r, .choice = c };
}

// Interval bound for atan2(y, x). atan2 is discontinuous along the negative-x
// half-axis (jump from ±π) and undefined at the origin. When the input box
// stays strictly in the right half-plane (x > 0) we can return a tighter
// bound via corner evaluations; otherwise fall back to the full [-π, π].
pub fn iAtan2(y: Interval, x: Interval) Interval {
    const pi: f32 = std.math.pi;
    if (x.lo > 0) {
        const c1 = std.math.atan2(y.lo, x.lo);
        const c2 = std.math.atan2(y.lo, x.hi);
        const c3 = std.math.atan2(y.hi, x.lo);
        const c4 = std.math.atan2(y.hi, x.hi);
        return .{
            .lo = @min(@min(c1, c2), @min(c3, c4)),
            .hi = @max(@max(c1, c2), @max(c3, c4)),
        };
    }
    return .{ .lo = -pi, .hi = pi };
}

pub const EvalResult = struct {
    result: Interval,
    has_any_pruneable: bool,
};

pub fn evalInterval(
    tape: *const tape_mod.Tape,
    x: Interval,
    y: Interval,
    z: Interval,
    slots: []Interval,
    trace: []Choice,
) EvalResult {
    var choice_idx: u32 = 0;
    var any_pruneable = false;
    for (tape.ops, 0..) |ins, i| {
        const r: Interval = switch (ins.op) {
            .input_x => x,
            .input_y => y,
            .input_z => z,
            .constant => iConst(tape.constants[ins.a]),
            .neg => iNeg(slots[ins.a]),
            .abs => iAbs(slots[ins.a]),
            .sqrt => iSqrt(slots[ins.a]),
            .square => iSquare(slots[ins.a]),
            .add => iAdd(slots[ins.a], slots[ins.b]),
            .sub => iSub(slots[ins.a], slots[ins.b]),
            .mul => iMul(slots[ins.a], slots[ins.b]),
            .div => iDiv(slots[ins.a], slots[ins.b]),
            .min => blk: {
                const mr = iMin(slots[ins.a], slots[ins.b]);
                trace[choice_idx] = mr.choice;
                choice_idx += 1;
                if (mr.choice != .both) any_pruneable = true;
                break :blk mr.interval;
            },
            .max => blk: {
                const mr = iMax(slots[ins.a], slots[ins.b]);
                trace[choice_idx] = mr.choice;
                choice_idx += 1;
                if (mr.choice != .both) any_pruneable = true;
                break :blk mr.interval;
            },
            .atan2 => iAtan2(slots[ins.a], slots[ins.b]),
        };
        slots[i] = r;
    }
    return .{ .result = slots[tape.output_slot], .has_any_pruneable = any_pruneable };
}

fn expectIntervalEq(actual: Interval, expected_lo: f32, expected_hi: f32) !void {
    try std.testing.expectEqual(expected_lo, actual.lo);
    try std.testing.expectEqual(expected_hi, actual.hi);
}

test "iSub flips subtrahend endpoints" {
    const r = iSub(.{ .lo = 1.0, .hi = 2.0 }, .{ .lo = 3.0, .hi = 4.0 });
    try expectIntervalEq(r, -3.0, -1.0);
}

test "iAbs over interval crossing zero starts at zero" {
    const r = iAbs(.{ .lo = -2.5, .hi = 3.0 });
    try expectIntervalEq(r, 0.0, 3.0);
}

test "iSquare over interval crossing zero starts at zero" {
    const r = iSquare(.{ .lo = -3.0, .hi = 2.0 });
    try expectIntervalEq(r, 0.0, 9.0);
}

test "iDiv returns infinite interval when divisor crosses zero" {
    const r = iDiv(.{ .lo = 1.0, .hi = 2.0 }, .{ .lo = -1.0, .hi = 1.0 });
    try std.testing.expect(std.math.isInf(r.lo));
    try std.testing.expect(std.math.isInf(r.hi));
    try std.testing.expect(r.lo < 0.0);
    try std.testing.expect(r.hi > 0.0);
}

test "iMin and iMax report deterministic choice when intervals do not overlap" {
    const a: Interval = .{ .lo = -4.0, .hi = -2.0 };
    const b: Interval = .{ .lo = 1.0, .hi = 3.0 };

    const mn = iMin(a, b);
    try expectIntervalEq(mn.interval, -4.0, -2.0);
    try std.testing.expectEqual(Choice.left, mn.choice);

    const mx = iMax(a, b);
    try expectIntervalEq(mx.interval, 1.0, 3.0);
    try std.testing.expectEqual(Choice.right, mx.choice);
}

test "evalInterval on sphere matches expected interval at origin-centered box" {
    var ops: [16]tape_mod.Instruction = undefined;
    var consts: [4]f32 = undefined;
    var builder = tape_mod.TapeBuilder.init(&ops, &consts);

    const x = builder.inputX();
    const y = builder.inputY();
    const z = builder.inputZ();
    const sum_sq = builder.add(
        builder.add(builder.square(x), builder.square(y)),
        builder.square(z),
    );
    const radius = builder.constant(1.0);
    const out = builder.sub(builder.sqrtOp(sum_sq), radius);
    const tape = builder.finalize(out);

    var slots: [16]Interval = undefined;
    var trace: [4]Choice = undefined;
    const r = evalInterval(
        &tape,
        .{ .lo = -0.5, .hi = 0.5 },
        .{ .lo = -0.5, .hi = 0.5 },
        .{ .lo = -0.5, .hi = 0.5 },
        &slots,
        &trace,
    );

    try std.testing.expectEqual(false, r.has_any_pruneable);
    try std.testing.expectEqual(-1.0, r.result.lo);
    try std.testing.expectApproxEqAbs(@as(f32, @sqrt(0.75) - 1.0), r.result.hi, 1e-6);
}

test "evalInterval records pruneable min choice" {
    var ops: [8]tape_mod.Instruction = undefined;
    var consts: [4]f32 = undefined;
    var builder = tape_mod.TapeBuilder.init(&ops, &consts);

    const left = builder.constant(-2.0);
    const right = builder.constant(3.0);
    const out = builder.minOp(left, right);
    const tape = builder.finalize(out);

    var slots: [8]Interval = undefined;
    var trace: [4]Choice = undefined;
    const r = evalInterval(
        &tape,
        .{ .lo = 0.0, .hi = 0.0 },
        .{ .lo = 0.0, .hi = 0.0 },
        .{ .lo = 0.0, .hi = 0.0 },
        &slots,
        &trace,
    );

    try expectIntervalEq(r.result, -2.0, -2.0);
    try std.testing.expectEqual(true, r.has_any_pruneable);
    try std.testing.expectEqual(Choice.left, trace[0]);
}
