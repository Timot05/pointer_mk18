const std = @import("std");
const math = std.math;
const math_ir = @import("math_ir.zig");
const reg = @import("math_reg_tape.zig");
const eval = @import("math_eval.zig");

const Vec3 = math_ir.Vec3;
const MathIR = math_ir.MathIR;
const Unary = math_ir.Unary;
const Binary = math_ir.Binary;
const Intrinsic = math_ir.Intrinsic;
const max_nodes = math_ir.max_nodes;
const RegTape = reg.RegTape;
const Op = reg.Op;

// Forward-mode autodiff packed into a v128: lane 0 is the value, lanes 1..3
// are ∂/∂x, ∂/∂y, ∂/∂z. Linear ops (add/sub/neg) collapse to a single SIMD
// instruction; product/quotient/sqrt/square need a small lane-0 patch since
// the broadcast trick would double-count the value.
//
// This pairs with `decodeRegEvalF32`, an f32-only value evaluator that
// matches the Grad evaluator's lane-0 path bit-for-bit. A renderer can
// march in f32 and call `decodeRegEvalGrad` once at each hit pixel — the
// hit-detection value and the gradient's value lane will agree exactly.

pub const Grad = @Vector(4, f32);

pub inline fn gConst(v: f32) Grad {
    return .{ v, 0, 0, 0 };
}

// ── Scalar f32 helpers (mirror math_eval.evalUnaryPoint / evalBinaryPoint).
//
// Round-tripping through the existing f64 helpers keeps the f32 path
// bit-precision-aligned with the f64 reference (modulo the inevitable
// final cast). All consumers eat the cost — intrinsics already round-trip
// via @floatCast — and there's no per-op-kind reimplementation to drift.

inline fn unaryF32(op: Unary, a: f32) f32 {
    return @floatCast(eval.evalUnaryPoint(op, @floatCast(a)));
}

inline fn binaryF32(op: Binary, a: f32, b: f32) f32 {
    return @floatCast(eval.evalBinaryPoint(op, @floatCast(a), @floatCast(b)));
}

inline fn intrinsicF32(ir: *const MathIR, intrinsic: Intrinsic, slots: []const f64, x: f32, y: f32, z: f32) f32 {
    const p = Vec3{ .x = @floatCast(x), .y = @floatCast(y), .z = @floatCast(z) };
    return @floatCast(eval.evalIntrinsicPoint(ir, intrinsic, slots, p));
}

// ── Grad ops ──────────────────────────────────────────────────────────────

inline fn gAdd(a: Grad, b: Grad) Grad {
    return a + b;
}

inline fn gSub(a: Grad, b: Grad) Grad {
    return a - b;
}

inline fn gNeg(a: Grad) Grad {
    return -a;
}

inline fn gAbs(a: Grad) Grad {
    if (a[0] > 0) return a;
    if (a[0] < 0) return -a;
    return @splat(0);
}

inline fn gSquare(a: Grad) Grad {
    // d(v²) = 2v · dv
    const k: Grad = @splat(2.0 * a[0]);
    var r = k * a;
    r[0] = a[0] * a[0];
    return r;
}

inline fn gSqrt(a: Grad) Grad {
    // d(√v) = (0.5/√v) · dv ; undefined at v ≤ 0
    if (a[0] <= 0) return @splat(0);
    const sv = @sqrt(a[0]);
    const k: Grad = @splat(0.5 / sv);
    var r = a * k;
    r[0] = sv;
    return r;
}

inline fn gRecip(a: Grad) Grad {
    // d(1/v) = -(1/v²) · dv
    if (a[0] == 0) return @splat(0);
    const r0 = 1.0 / a[0];
    const k: Grad = @splat(-r0 * r0);
    var r = a * k;
    r[0] = r0;
    return r;
}

inline fn gSin(a: Grad) Grad {
    const c = @cos(a[0]);
    const k: Grad = @splat(c);
    var r = k * a;
    r[0] = @sin(a[0]);
    return r;
}

inline fn gCos(a: Grad) Grad {
    const s = @sin(a[0]);
    const k: Grad = @splat(-s);
    var r = k * a;
    r[0] = @cos(a[0]);
    return r;
}

inline fn gTan(a: Grad) Grad {
    const c = @cos(a[0]);
    if (c == 0) return @splat(0);
    const sec2 = 1.0 / (c * c);
    const k: Grad = @splat(sec2);
    var r = k * a;
    r[0] = @tan(a[0]);
    return r;
}

inline fn gAsin(a: Grad) Grad {
    const v = a[0];
    const denom = @sqrt(@max(0.0, 1.0 - v * v));
    if (denom == 0) return Grad{ math.asin(v), 0, 0, 0 };
    const k: Grad = @splat(1.0 / denom);
    var r = k * a;
    r[0] = math.asin(v);
    return r;
}

inline fn gAcos(a: Grad) Grad {
    const v = a[0];
    const denom = @sqrt(@max(0.0, 1.0 - v * v));
    if (denom == 0) return Grad{ math.acos(v), 0, 0, 0 };
    const k: Grad = @splat(-1.0 / denom);
    var r = k * a;
    r[0] = math.acos(v);
    return r;
}

inline fn gAtan(a: Grad) Grad {
    const k: Grad = @splat(1.0 / (1.0 + a[0] * a[0]));
    var r = k * a;
    r[0] = math.atan(a[0]);
    return r;
}

inline fn gExp(a: Grad) Grad {
    const e = @exp(a[0]);
    const k: Grad = @splat(e);
    var r = k * a;
    r[0] = e;
    return r;
}

inline fn gLn(a: Grad) Grad {
    if (a[0] <= 0) return Grad{ @log(a[0]), 0, 0, 0 };
    const k: Grad = @splat(1.0 / a[0]);
    var r = k * a;
    r[0] = @log(a[0]);
    return r;
}

// Non-smooth/step ops: derivative is 0 a.e. — return zero gradient.
inline fn gFlat(op: Unary, a: Grad) Grad {
    return Grad{ unaryF32(op, a[0]), 0, 0, 0 };
}

fn gUnary(op: Unary, a: Grad) Grad {
    return switch (op) {
        .neg => gNeg(a),
        .abs => gAbs(a),
        .recip => gRecip(a),
        .square => gSquare(a),
        .sqrt => gSqrt(a),
        .sin => gSin(a),
        .cos => gCos(a),
        .tan => gTan(a),
        .asin => gAsin(a),
        .acos => gAcos(a),
        .atan => gAtan(a),
        .exp => gExp(a),
        .ln => gLn(a),
        // step / piecewise-constant: derivative is 0 a.e.
        .floor, .ceil, .round, .not => gFlat(op, a),
    };
}

inline fn gMul(a: Grad, b: Grad) Grad {
    // Product rule: d(ab) = a·db + b·da. The broadcast trick double-counts
    // the value lane; patch r[0] = a·b after the v128 op.
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
    const va: Grad = @splat(a[0]);
    const vb: Grad = @splat(b[0]);
    var r = (vb * a - va * b) * inv2;
    r[0] = a[0] * inv;
    return r;
}

inline fn gAtan2(a: Grad, b: Grad) Grad {
    // d atan2(a, b) = (b·da - a·db) / (a² + b²)
    const va = a[0];
    const vb = b[0];
    const r2 = va * va + vb * vb;
    if (r2 == 0) return @splat(0);
    const inv: Grad = @splat(1.0 / r2);
    const ka: Grad = @splat(va);
    const kb: Grad = @splat(vb);
    var r = (kb * a - ka * b) * inv;
    r[0] = math.atan2(va, vb);
    return r;
}

inline fn gMin(a: Grad, b: Grad) Grad {
    return if (a[0] <= b[0]) a else b;
}

inline fn gMax(a: Grad, b: Grad) Grad {
    return if (a[0] >= b[0]) a else b;
}

inline fn gPow(a: Grad, b: Grad) Grad {
    // d(a^b) = a^b · (b/a · da + ln(a) · db). Undefined at a ≤ 0.
    const va = a[0];
    const vb = b[0];
    if (va <= 0) return Grad{ math.pow(f32, va, vb), 0, 0, 0 };
    const v_out = math.pow(f32, va, vb);
    const k_a: Grad = @splat(v_out * vb / va);
    const k_b: Grad = @splat(v_out * @log(va));
    var r = k_a * a + k_b * b;
    r[0] = v_out;
    return r;
}

inline fn gFlatBinary(op: Binary, a: Grad, b: Grad) Grad {
    return Grad{ binaryF32(op, a[0], b[0]), 0, 0, 0 };
}

fn gBinary(op: Binary, a: Grad, b: Grad) Grad {
    return switch (op) {
        .add => gAdd(a, b),
        .sub => gSub(a, b),
        .mul => gMul(a, b),
        .div => gDiv(a, b),
        .atan2 => gAtan2(a, b),
        .min => gMin(a, b),
        .max => gMax(a, b),
        .pow => gPow(a, b),
        .compare, .mod, .and_, .or_ => gFlatBinary(op, a, b),
    };
}

// Intrinsic via central differences in the LOCAL axis frame, then chain-rule
// through the outer-axis Grads. Six extra intrinsic evals per call; intrinsics
// are already heavy ops so the relative overhead is small.
fn gIntrinsic(ir: *const MathIR, intrinsic: Intrinsic, slots: []const f64, axis: [3]Grad) Grad {
    const cx = axis[0][0];
    const cy = axis[1][0];
    const cz = axis[2][0];
    const v0 = intrinsicF32(ir, intrinsic, slots, cx, cy, cz);

    const h: f32 = 1.0e-3;
    const inv_2h: f32 = 1.0 / (2.0 * h);
    const dcx = (intrinsicF32(ir, intrinsic, slots, cx + h, cy, cz) - intrinsicF32(ir, intrinsic, slots, cx - h, cy, cz)) * inv_2h;
    const dcy = (intrinsicF32(ir, intrinsic, slots, cx, cy + h, cz) - intrinsicF32(ir, intrinsic, slots, cx, cy - h, cz)) * inv_2h;
    const dcz = (intrinsicF32(ir, intrinsic, slots, cx, cy, cz + h) - intrinsicF32(ir, intrinsic, slots, cx, cy, cz - h)) * inv_2h;

    return Grad{
        v0,
        dcx * axis[0][1] + dcy * axis[1][1] + dcz * axis[2][1],
        dcx * axis[0][2] + dcy * axis[1][2] + dcz * axis[2][2],
        dcx * axis[0][3] + dcy * axis[1][3] + dcz * axis[2][3],
    };
}

// ── Drivers ───────────────────────────────────────────────────────────────

pub fn decodeRegEvalF32(tape: *const RegTape, ir: *const MathIR, slots: []const f64, p: Vec3) f32 {
    var values: [max_nodes]f32 = undefined;
    var axes: [64][3]f32 = undefined;
    axes[0] = .{ @floatCast(p.x), @floatCast(p.y), @floatCast(p.z) };
    var ap: usize = 1;

    var ip: usize = 0;
    while (ip < tape.instruction_count) : (ip += 1) {
        const op: Op = @enumFromInt(tape.opcodes[ip]);
        const dst = tape.dst[ip];
        const a = tape.src_a[ip];
        const b = tape.src_b[ip];
        const c = tape.src_c[ip];
        const aux = tape.aux[ip];

        switch (op) {
            .load_x => values[dst] = axes[ap - 1][0],
            .load_y => values[dst] = axes[ap - 1][1],
            .load_z => values[dst] = axes[ap - 1][2],
            .load_slot => values[dst] = @floatCast(eval.slotValue(slots, aux)),
            .load_const => values[dst] = @floatCast(tape.immediates[@intCast(aux)]),
            .unary => values[dst] = unaryF32(@enumFromInt(aux), values[a]),
            .binary => values[dst] = binaryF32(@enumFromInt(aux), values[a], values[b]),
            .enter_remap_axes => {
                axes[ap] = .{ values[a], values[b], values[c] };
                ap += 1;
            },
            .enter_remap_affine => {
                const af = ir.affines[@intCast(aux)];
                const cur = axes[ap - 1];
                const m00 = values[@intCast(af.m00.id)];
                const m01 = values[@intCast(af.m01.id)];
                const m02 = values[@intCast(af.m02.id)];
                const m03 = values[@intCast(af.m03.id)];
                const m10 = values[@intCast(af.m10.id)];
                const m11 = values[@intCast(af.m11.id)];
                const m12 = values[@intCast(af.m12.id)];
                const m13 = values[@intCast(af.m13.id)];
                const m20 = values[@intCast(af.m20.id)];
                const m21 = values[@intCast(af.m21.id)];
                const m22 = values[@intCast(af.m22.id)];
                const m23 = values[@intCast(af.m23.id)];
                axes[ap] = .{
                    m00 * cur[0] + m01 * cur[1] + m02 * cur[2] + m03,
                    m10 * cur[0] + m11 * cur[1] + m12 * cur[2] + m13,
                    m20 * cur[0] + m21 * cur[1] + m22 * cur[2] + m23,
                };
                ap += 1;
            },
            .exit_remap => {
                values[dst] = values[a];
                ap -= 1;
            },
            .intrinsic => {
                const intrinsic = ir.intrinsics[@intCast(aux)];
                values[dst] = intrinsicF32(ir, intrinsic, slots, axes[ap - 1][0], axes[ap - 1][1], axes[ap - 1][2]);
            },
            .copy_slot => values[dst] = values[a],
            .return_ => return values[a],
        }
    }
    return 0;
}

pub fn decodeRegEvalGrad(tape: *const RegTape, ir: *const MathIR, slots: []const f64, p: Vec3) Grad {
    var values: [max_nodes]Grad = undefined;
    var axes: [64][3]Grad = undefined;
    axes[0] = .{
        Grad{ @floatCast(p.x), 1, 0, 0 },
        Grad{ @floatCast(p.y), 0, 1, 0 },
        Grad{ @floatCast(p.z), 0, 0, 1 },
    };
    var ap: usize = 1;

    var ip: usize = 0;
    while (ip < tape.instruction_count) : (ip += 1) {
        const op: Op = @enumFromInt(tape.opcodes[ip]);
        const dst = tape.dst[ip];
        const a = tape.src_a[ip];
        const b = tape.src_b[ip];
        const c = tape.src_c[ip];
        const aux = tape.aux[ip];

        switch (op) {
            .load_x => values[dst] = axes[ap - 1][0],
            .load_y => values[dst] = axes[ap - 1][1],
            .load_z => values[dst] = axes[ap - 1][2],
            .load_slot => values[dst] = gConst(@floatCast(eval.slotValue(slots, aux))),
            .load_const => values[dst] = gConst(@floatCast(tape.immediates[@intCast(aux)])),
            .unary => values[dst] = gUnary(@enumFromInt(aux), values[a]),
            .binary => values[dst] = gBinary(@enumFromInt(aux), values[a], values[b]),
            .enter_remap_axes => {
                axes[ap] = .{ values[a], values[b], values[c] };
                ap += 1;
            },
            .enter_remap_affine => {
                const af = ir.affines[@intCast(aux)];
                const cur = axes[ap - 1];
                const m00 = values[@intCast(af.m00.id)];
                const m01 = values[@intCast(af.m01.id)];
                const m02 = values[@intCast(af.m02.id)];
                const m03 = values[@intCast(af.m03.id)];
                const m10 = values[@intCast(af.m10.id)];
                const m11 = values[@intCast(af.m11.id)];
                const m12 = values[@intCast(af.m12.id)];
                const m13 = values[@intCast(af.m13.id)];
                const m20 = values[@intCast(af.m20.id)];
                const m21 = values[@intCast(af.m21.id)];
                const m22 = values[@intCast(af.m22.id)];
                const m23 = values[@intCast(af.m23.id)];
                axes[ap] = .{
                    gAdd(gAdd(gAdd(gMul(m00, cur[0]), gMul(m01, cur[1])), gMul(m02, cur[2])), m03),
                    gAdd(gAdd(gAdd(gMul(m10, cur[0]), gMul(m11, cur[1])), gMul(m12, cur[2])), m13),
                    gAdd(gAdd(gAdd(gMul(m20, cur[0]), gMul(m21, cur[1])), gMul(m22, cur[2])), m23),
                };
                ap += 1;
            },
            .exit_remap => {
                values[dst] = values[a];
                ap -= 1;
            },
            .intrinsic => {
                const intrinsic = ir.intrinsics[@intCast(aux)];
                values[dst] = gIntrinsic(ir, intrinsic, slots, axes[ap - 1]);
            },
            .copy_slot => values[dst] = values[a],
            .return_ => return values[a],
        }
    }
    return @splat(0);
}
