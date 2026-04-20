const std = @import("std");
const tape_mod = @import("tape.zig");

// Forward-mode autodiff, packed as a v128: lane 0 is the value, lanes 1..3 are
// ∂/∂x, ∂/∂y, ∂/∂z. Packing as @Vector lets add/sub/neg/linear-scale collapse
// to single WASM v128 instructions. Product and quotient rules need a tiny
// fixup for lane 0 since the value is v_a*v_b, not 2*v_a*v_b.

pub const Grad = @Vector(4, f32);

pub inline fn gConst(c: f32) Grad {
    return .{ c, 0, 0, 0 };
}

inline fn gNeg(a: Grad) Grad {
    return -a;
}

inline fn gAbs(a: Grad) Grad {
    if (a[0] > 0) return a;
    if (a[0] < 0) return -a;
    return @splat(0);
}

inline fn gSqrt(a: Grad) Grad {
    if (a[0] <= 0) return @splat(0);
    const sv = @sqrt(a[0]);
    // result.v  = sqrt(v)
    // result.d* = d* * (0.5 / sqrt(v))
    const k: Grad = @splat(0.5 / sv);
    var r = a * k;
    r[0] = sv;
    return r;
}

inline fn gSquare(a: Grad) Grad {
    // result.v  = v*v
    // result.d* = 2v * d*
    const two_v: Grad = @splat(2.0 * a[0]);
    var r = two_v * a;
    r[0] = a[0] * a[0];
    return r;
}

inline fn gAdd(a: Grad, b: Grad) Grad {
    return a + b;
}

inline fn gSub(a: Grad, b: Grad) Grad {
    return a - b;
}

inline fn gMul(a: Grad, b: Grad) Grad {
    // Product rule: d(ab) = a·db + b·da.
    // broadcast(v_a)*b + broadcast(v_b)*a gives the correct derivatives but
    // double-counts the value; overwrite lane 0 with v_a*v_b.
    const va: Grad = @splat(a[0]);
    const vb: Grad = @splat(b[0]);
    var r = va * b + vb * a;
    r[0] = a[0] * b[0];
    return r;
}

inline fn gDiv(a: Grad, b: Grad) Grad {
    if (b[0] == 0) return @splat(0);
    const inv = 1.0 / b[0];
    const inv2: Grad = @splat(inv * inv);
    // Quotient rule: d(a/b) = (da·b - a·db) / b^2
    const va: Grad = @splat(a[0]);
    const vb: Grad = @splat(b[0]);
    var r = (vb * a - va * b) * inv2;
    r[0] = a[0] * inv;
    return r;
}

inline fn gMin(a: Grad, b: Grad) Grad {
    return if (a[0] <= b[0]) a else b;
}

inline fn gMax(a: Grad, b: Grad) Grad {
    return if (a[0] >= b[0]) a else b;
}

// atan2(y, x): value = atan2(y, x); derivatives = (x·dy − y·dx) / (x² + y²).
// Convention (matches tape op): a = y (first arg), b = x.
inline fn gAtan2(a: Grad, b: Grad) Grad {
    const y = a[0];
    const x = b[0];
    const r2 = x * x + y * y;
    if (r2 == 0) return @splat(0);
    const inv: Grad = @splat(1.0 / r2);
    const xs: Grad = @splat(x);
    const ys: Grad = @splat(y);
    var r = (xs * a - ys * b) * inv;
    r[0] = std.math.atan2(y, x);
    return r;
}

pub fn evalGrad(
    tape: *const tape_mod.Tape,
    x: f32,
    y: f32,
    z: f32,
    slots: []Grad,
) Grad {
    for (tape.ops, 0..) |ins, i| {
        slots[i] = switch (ins.op) {
            .input_x => Grad{ x, 1, 0, 0 },
            .input_y => Grad{ y, 0, 1, 0 },
            .input_z => Grad{ z, 0, 0, 1 },
            .constant => gConst(tape.constants[ins.a]),
            .neg => gNeg(slots[ins.a]),
            .abs => gAbs(slots[ins.a]),
            .sqrt => gSqrt(slots[ins.a]),
            .square => gSquare(slots[ins.a]),
            .add => gAdd(slots[ins.a], slots[ins.b]),
            .sub => gSub(slots[ins.a], slots[ins.b]),
            .mul => gMul(slots[ins.a], slots[ins.b]),
            .div => gDiv(slots[ins.a], slots[ins.b]),
            .min => gMin(slots[ins.a], slots[ins.b]),
            .max => gMax(slots[ins.a], slots[ins.b]),
            .atan2 => gAtan2(slots[ins.a], slots[ins.b]),
        };
    }
    return slots[tape.output_slot];
}

// ── Batched (SoA) gradient, 4 pixels per tape walk ────────────────────────
//
// Layout: the four fields are each a 4-lane vector across pixels. So v[l] is
// the value at pixel lane l, dx[l] is its partial, etc. This lays out in
// memory as [v0 v1 v2 v3][dx0 dx1 dx2 dx3]…, maximizing SIMD throughput per
// op (every fn below is straight-line vector arithmetic on v128s).

const F4 = @Vector(4, f32);

pub const GradBatch = struct {
    v: F4,
    dx: F4,
    dy: F4,
    dz: F4,
};

inline fn f4zero() F4 {
    return @splat(0);
}

inline fn f4one() F4 {
    return @splat(1);
}

pub inline fn gbConst(c: f32) GradBatch {
    return .{ .v = @splat(c), .dx = f4zero(), .dy = f4zero(), .dz = f4zero() };
}

inline fn gbNeg(a: GradBatch) GradBatch {
    return .{ .v = -a.v, .dx = -a.dx, .dy = -a.dy, .dz = -a.dz };
}

inline fn gbAbs(a: GradBatch) GradBatch {
    const zero = f4zero();
    const pos: @Vector(4, bool) = a.v > zero;
    const neg: @Vector(4, bool) = a.v < zero;
    return .{
        .v = @select(f32, pos, a.v, @select(f32, neg, -a.v, zero)),
        .dx = @select(f32, pos, a.dx, @select(f32, neg, -a.dx, zero)),
        .dy = @select(f32, pos, a.dy, @select(f32, neg, -a.dy, zero)),
        .dz = @select(f32, pos, a.dz, @select(f32, neg, -a.dz, zero)),
    };
}

inline fn gbSqrt(a: GradBatch) GradBatch {
    const zero = f4zero();
    const pos: @Vector(4, bool) = a.v > zero;
    // Clamp input so inactive lanes don't produce NaN; their results are masked
    // to zero below.
    const sv = @sqrt(@max(a.v, zero));
    const safe_sv = @select(f32, pos, sv, f4one());
    const k: F4 = @as(F4, @splat(0.5)) / safe_sv;
    return .{
        .v = @select(f32, pos, sv, zero),
        .dx = @select(f32, pos, a.dx * k, zero),
        .dy = @select(f32, pos, a.dy * k, zero),
        .dz = @select(f32, pos, a.dz * k, zero),
    };
}

inline fn gbSquare(a: GradBatch) GradBatch {
    const two_v = @as(F4, @splat(2.0)) * a.v;
    return .{
        .v = a.v * a.v,
        .dx = two_v * a.dx,
        .dy = two_v * a.dy,
        .dz = two_v * a.dz,
    };
}

inline fn gbAdd(a: GradBatch, b: GradBatch) GradBatch {
    return .{ .v = a.v + b.v, .dx = a.dx + b.dx, .dy = a.dy + b.dy, .dz = a.dz + b.dz };
}

inline fn gbSub(a: GradBatch, b: GradBatch) GradBatch {
    return .{ .v = a.v - b.v, .dx = a.dx - b.dx, .dy = a.dy - b.dy, .dz = a.dz - b.dz };
}

inline fn gbMul(a: GradBatch, b: GradBatch) GradBatch {
    return .{
        .v = a.v * b.v,
        .dx = a.v * b.dx + b.v * a.dx,
        .dy = a.v * b.dy + b.v * a.dy,
        .dz = a.v * b.dz + b.v * a.dz,
    };
}

inline fn gbDiv(a: GradBatch, b: GradBatch) GradBatch {
    const zero = f4zero();
    const nonzero: @Vector(4, bool) = b.v != zero;
    const safe_b = @select(f32, nonzero, b.v, f4one());
    const inv = @as(F4, @splat(1.0)) / safe_b;
    const inv2 = inv * inv;
    return .{
        .v = @select(f32, nonzero, a.v * inv, zero),
        .dx = @select(f32, nonzero, (a.dx * b.v - a.v * b.dx) * inv2, zero),
        .dy = @select(f32, nonzero, (a.dy * b.v - a.v * b.dy) * inv2, zero),
        .dz = @select(f32, nonzero, (a.dz * b.v - a.v * b.dz) * inv2, zero),
    };
}

inline fn gbMin(a: GradBatch, b: GradBatch) GradBatch {
    const a_wins: @Vector(4, bool) = a.v <= b.v;
    return .{
        .v = @select(f32, a_wins, a.v, b.v),
        .dx = @select(f32, a_wins, a.dx, b.dx),
        .dy = @select(f32, a_wins, a.dy, b.dy),
        .dz = @select(f32, a_wins, a.dz, b.dz),
    };
}

inline fn gbMax(a: GradBatch, b: GradBatch) GradBatch {
    const a_wins: @Vector(4, bool) = a.v >= b.v;
    return .{
        .v = @select(f32, a_wins, a.v, b.v),
        .dx = @select(f32, a_wins, a.dx, b.dx),
        .dy = @select(f32, a_wins, a.dy, b.dy),
        .dz = @select(f32, a_wins, a.dz, b.dz),
    };
}

inline fn gbAtan2(a: GradBatch, b: GradBatch) GradBatch {
    // value is scalar atan2 per lane; partials are vectorised.
    const y_arr: [4]f32 = a.v;
    const x_arr: [4]f32 = b.v;
    var v_arr: [4]f32 = undefined;
    inline for (0..4) |i| v_arr[i] = std.math.atan2(y_arr[i], x_arr[i]);
    const v_out: F4 = v_arr;

    const zero = f4zero();
    const r2 = b.v * b.v + a.v * a.v;
    const nz: @Vector(4, bool) = r2 != zero;
    const safe_r2 = @select(f32, nz, r2, f4one());
    const inv = @as(F4, @splat(1.0)) / safe_r2;
    return .{
        .v = v_out,
        .dx = @select(f32, nz, (b.v * a.dx - a.v * b.dx) * inv, zero),
        .dy = @select(f32, nz, (b.v * a.dy - a.v * b.dy) * inv, zero),
        .dz = @select(f32, nz, (b.v * a.dz - a.v * b.dz) * inv, zero),
    };
}

pub fn evalGrad4(
    tape: *const tape_mod.Tape,
    x: F4,
    y: F4,
    z: F4,
    slots: []GradBatch,
) GradBatch {
    const zero = f4zero();
    const one = f4one();
    for (tape.ops, 0..) |ins, i| {
        slots[i] = switch (ins.op) {
            .input_x => .{ .v = x, .dx = one, .dy = zero, .dz = zero },
            .input_y => .{ .v = y, .dx = zero, .dy = one, .dz = zero },
            .input_z => .{ .v = z, .dx = zero, .dy = zero, .dz = one },
            .constant => gbConst(tape.constants[ins.a]),
            .neg => gbNeg(slots[ins.a]),
            .abs => gbAbs(slots[ins.a]),
            .sqrt => gbSqrt(slots[ins.a]),
            .square => gbSquare(slots[ins.a]),
            .add => gbAdd(slots[ins.a], slots[ins.b]),
            .sub => gbSub(slots[ins.a], slots[ins.b]),
            .mul => gbMul(slots[ins.a], slots[ins.b]),
            .div => gbDiv(slots[ins.a], slots[ins.b]),
            .min => gbMin(slots[ins.a], slots[ins.b]),
            .max => gbMax(slots[ins.a], slots[ins.b]),
            .atan2 => gbAtan2(slots[ins.a], slots[ins.b]),
        };
    }
    return slots[tape.output_slot];
}

fn expectGradEq(actual: Grad, expected: [4]f32) !void {
    inline for (0..4) |i| {
        try std.testing.expectApproxEqAbs(expected[i], actual[i], 1e-6);
    }
}

test "evalGrad of sphere matches analytic gradient" {
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
    const out = builder.sub(builder.sqrtOp(sum_sq), builder.constant(1.0));
    const tape = builder.finalize(out);

    var slots: [16]Grad = undefined;
    const g = evalGrad(&tape, 3.0, 4.0, 0.0, &slots);
    try expectGradEq(g, .{ 4.0, 0.6, 0.8, 0.0 });
}

test "evalGrad of translated square sum follows chain rule" {
    var ops: [16]tape_mod.Instruction = undefined;
    var consts: [8]f32 = undefined;
    var builder = tape_mod.TapeBuilder.init(&ops, &consts);

    const x = builder.inputX();
    const y = builder.inputY();
    const dx = builder.sub(x, builder.constant(2.0));
    const dy = builder.sub(y, builder.constant(-1.0));
    const out = builder.add(builder.square(dx), builder.square(dy));
    const tape = builder.finalize(out);

    var slots: [16]Grad = undefined;
    const g = evalGrad(&tape, 5.0, 3.0, 0.0, &slots);
    try expectGradEq(g, .{ 25.0, 6.0, 8.0, 0.0 });
}

test "evalGrad of atan2 returns expected partials" {
    var ops: [8]tape_mod.Instruction = undefined;
    var consts: [2]f32 = undefined;
    var builder = tape_mod.TapeBuilder.init(&ops, &consts);

    const y = builder.inputY();
    const x = builder.inputX();
    const out = builder.atan2Op(y, x);
    const tape = builder.finalize(out);

    var slots: [8]Grad = undefined;
    const g = evalGrad(&tape, 2.0, 3.0, 0.0, &slots);
    const r2 = 13.0;
    try expectGradEq(g, .{
        std.math.atan2(@as(f32, 3.0), @as(f32, 2.0)),
        -3.0 / r2,
        2.0 / r2,
        0.0,
    });
}

test "evalGrad picks active branch for min and max" {
    var ops: [16]tape_mod.Instruction = undefined;
    var consts: [4]f32 = undefined;
    var builder = tape_mod.TapeBuilder.init(&ops, &consts);

    const x = builder.inputX();
    const y = builder.inputY();
    const mn = builder.minOp(x, y);
    const mx = builder.maxOp(x, y);
    const out = builder.add(mn, mx);
    const tape = builder.finalize(out);

    var slots: [16]Grad = undefined;
    const g = evalGrad(&tape, 1.0, 3.0, 0.0, &slots);
    try expectGradEq(g, .{ 4.0, 1.0, 1.0, 0.0 });
}
