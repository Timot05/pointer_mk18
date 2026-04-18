const std = @import("std");
const tape_mod = @import("tape.zig");

// Matches fidget's bitfield layout: Both == Left | Right.
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
