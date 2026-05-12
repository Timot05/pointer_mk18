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

// Primitive (line_segment / circle / bezier_*) via central differences in
// the LOCAL axis frame, chain-ruled through the outer-axis Grads. Six extra
// `evalPoint` calls per primitive; cheap enough not to matter at the sizes
// from-sketch produces, and the result is correctly shaded (without this,
// the gradient is zero and `cpu_render` falls back to the (0, 1, 0) normal,
// which renders any from-sketch surface as flat-lit brown).
fn gPrimitive(ir: *const MathIR, node_id: i32, slots: []const f64, axis: [3]Grad) Grad {
    const cx = axis[0][0];
    const cy = axis[1][0];
    const cz = axis[2][0];
    const expr_: math_ir.Expr = .{ .id = node_id };
    const ePoint = struct {
        fn f(irp: *const MathIR, e: math_ir.Expr, sl: []const f64, px: f32, py: f32, pz: f32) f32 {
            return @floatCast(eval.evalPoint(irp, e, sl, .{ .x = px, .y = py, .z = pz }));
        }
    }.f;

    const v0 = ePoint(ir, expr_, slots, cx, cy, cz);

    const h: f32 = 1.0e-3;
    const inv_2h: f32 = 1.0 / (2.0 * h);
    const dcx = (ePoint(ir, expr_, slots, cx + h, cy, cz) - ePoint(ir, expr_, slots, cx - h, cy, cz)) * inv_2h;
    const dcy = (ePoint(ir, expr_, slots, cx, cy + h, cz) - ePoint(ir, expr_, slots, cx, cy - h, cz)) * inv_2h;
    const dcz = (ePoint(ir, expr_, slots, cx, cy, cz + h) - ePoint(ir, expr_, slots, cx, cy, cz - h)) * inv_2h;

    return Grad{
        v0,
        dcx * axis[0][1] + dcy * axis[1][1] + dcz * axis[2][1],
        dcx * axis[0][2] + dcy * axis[1][2] + dcz * axis[2][2],
        dcx * axis[0][3] + dcy * axis[1][3] + dcz * axis[2][3],
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
            .primitive => {
                const pp = Vec3{
                    .x = @floatCast(axes[ap - 1][0]),
                    .y = @floatCast(axes[ap - 1][1]),
                    .z = @floatCast(axes[ap - 1][2]),
                };
                values[dst] = @floatCast(eval.evalPoint(ir, .{ .id = @intCast(dst) }, slots, pp));
            },
            .fold => {
                const start: usize = @intCast(aux);
                const count: usize = @intCast(a);
                if (count == 0) {
                    values[dst] = 0;
                } else {
                    const fop: math_ir.FoldOp = @enumFromInt(b);
                    var acc = values[@intCast(ir.node_refs[start])];
                    var i: usize = 1;
                    while (i < count) : (i += 1) {
                        const v = values[@intCast(ir.node_refs[start + i])];
                        acc = switch (fop) {
                            .min => if (acc < v) acc else v,
                            .max => if (acc > v) acc else v,
                            .sum => acc + v,
                        };
                    }
                    values[dst] = acc;
                }
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
            .primitive => {
                values[dst] = gPrimitive(ir, @intCast(dst), slots, axes[ap - 1]);
            },
            .fold => {
                const start: usize = @intCast(aux);
                const count: usize = @intCast(a);
                if (count == 0) {
                    values[dst] = gConst(0);
                } else {
                    const fop: math_ir.FoldOp = @enumFromInt(b);
                    var acc = values[@intCast(ir.node_refs[start])];
                    var i: usize = 1;
                    while (i < count) : (i += 1) {
                        const v = values[@intCast(ir.node_refs[start + i])];
                        acc = switch (fop) {
                            .min => gMin(acc, v),
                            .max => gMax(acc, v),
                            .sum => gAdd(acc, v),
                        };
                    }
                    values[dst] = acc;
                }
            },
            .copy_slot => values[dst] = values[a],
            .return_ => return values[a],
        }
    }
    return @splat(0);
}

// ── Batched (4-hit) forward-mode Grad ─────────────────────────────────────
//
// `Grad4` holds 4 hits' (value, ∂/∂x, ∂/∂y, ∂/∂z) — each field is an F4
// where lane `l` corresponds to hit `l`. Per-op rules apply across lanes
// in SIMD-4, so one tape walk produces 4 hits' analytical gradients.
// Used by the per-pixel leaf hot loop in `cpu_render.zig`, where each
// SIMD-4 z-scan group can yield up to 4 hits — this evaluator amortises
// the per-tape-walk overhead 4× over independent `decodeRegEvalGrad`
// calls.
//
// Lanes are independent: the result for lane `l` is identical to a
// single-point `decodeRegEvalGrad` at lane `l`'s (x[l], y[l], z[l]). The
// caller is free to leave invalid lanes undefined; the evaluator never
// faults on them as long as the tape doesn't divide by exactly zero or
// take √ of a strictly-negative scalar in the value lane. (`fSqrt` /
// `fDiv` mask such lanes to zero, matching the scalar grad behaviour.)

pub const F4g = @Vector(4, f32);

pub const Grad4 = struct {
    v: F4g,
    dx: F4g,
    dy: F4g,
    dz: F4g,
};

inline fn g4Zero() F4g {
    return @splat(0);
}
inline fn g4One() F4g {
    return @splat(1);
}
inline fn g4Splat(s: f32) F4g {
    return @splat(s);
}
inline fn g4Const(s: f32) Grad4 {
    return .{ .v = @splat(s), .dx = g4Zero(), .dy = g4Zero(), .dz = g4Zero() };
}

inline fn g4Neg(a: Grad4) Grad4 {
    return .{ .v = -a.v, .dx = -a.dx, .dy = -a.dy, .dz = -a.dz };
}
inline fn g4Add(a: Grad4, b: Grad4) Grad4 {
    return .{ .v = a.v + b.v, .dx = a.dx + b.dx, .dy = a.dy + b.dy, .dz = a.dz + b.dz };
}
inline fn g4Sub(a: Grad4, b: Grad4) Grad4 {
    return .{ .v = a.v - b.v, .dx = a.dx - b.dx, .dy = a.dy - b.dy, .dz = a.dz - b.dz };
}
inline fn g4Mul(a: Grad4, b: Grad4) Grad4 {
    return .{
        .v = a.v * b.v,
        .dx = a.v * b.dx + b.v * a.dx,
        .dy = a.v * b.dy + b.v * a.dy,
        .dz = a.v * b.dz + b.v * a.dz,
    };
}
inline fn g4Div(a: Grad4, b: Grad4) Grad4 {
    const zero = g4Zero();
    const nonzero: @Vector(4, bool) = b.v != zero;
    const safe_b = @select(f32, nonzero, b.v, g4One());
    const inv = g4One() / safe_b;
    const inv2 = inv * inv;
    return .{
        .v = @select(f32, nonzero, a.v * inv, zero),
        .dx = @select(f32, nonzero, (a.dx * b.v - a.v * b.dx) * inv2, zero),
        .dy = @select(f32, nonzero, (a.dy * b.v - a.v * b.dy) * inv2, zero),
        .dz = @select(f32, nonzero, (a.dz * b.v - a.v * b.dz) * inv2, zero),
    };
}
inline fn g4Abs(a: Grad4) Grad4 {
    const zero = g4Zero();
    const pos: @Vector(4, bool) = a.v > zero;
    const neg: @Vector(4, bool) = a.v < zero;
    return .{
        .v = @select(f32, pos, a.v, @select(f32, neg, -a.v, zero)),
        .dx = @select(f32, pos, a.dx, @select(f32, neg, -a.dx, zero)),
        .dy = @select(f32, pos, a.dy, @select(f32, neg, -a.dy, zero)),
        .dz = @select(f32, pos, a.dz, @select(f32, neg, -a.dz, zero)),
    };
}
inline fn g4Sqrt(a: Grad4) Grad4 {
    const zero = g4Zero();
    const pos: @Vector(4, bool) = a.v > zero;
    const sv = @sqrt(@max(a.v, zero));
    const safe_sv = @select(f32, pos, sv, g4One());
    const k = g4Splat(0.5) / safe_sv;
    return .{
        .v = @select(f32, pos, sv, zero),
        .dx = @select(f32, pos, a.dx * k, zero),
        .dy = @select(f32, pos, a.dy * k, zero),
        .dz = @select(f32, pos, a.dz * k, zero),
    };
}
inline fn g4Square(a: Grad4) Grad4 {
    const two_v = g4Splat(2.0) * a.v;
    return .{
        .v = a.v * a.v,
        .dx = two_v * a.dx,
        .dy = two_v * a.dy,
        .dz = two_v * a.dz,
    };
}
inline fn g4Min(a: Grad4, b: Grad4) Grad4 {
    const a_wins: @Vector(4, bool) = a.v <= b.v;
    return .{
        .v = @select(f32, a_wins, a.v, b.v),
        .dx = @select(f32, a_wins, a.dx, b.dx),
        .dy = @select(f32, a_wins, a.dy, b.dy),
        .dz = @select(f32, a_wins, a.dz, b.dz),
    };
}
inline fn g4Max(a: Grad4, b: Grad4) Grad4 {
    const a_wins: @Vector(4, bool) = a.v >= b.v;
    return .{
        .v = @select(f32, a_wins, a.v, b.v),
        .dx = @select(f32, a_wins, a.dx, b.dx),
        .dy = @select(f32, a_wins, a.dy, b.dy),
        .dz = @select(f32, a_wins, a.dz, b.dz),
    };
}
inline fn g4Atan2(a: Grad4, b: Grad4) Grad4 {
    // Value lane is per-lane scalar atan2; partials vectorise.
    const y_arr: [4]f32 = a.v;
    const x_arr: [4]f32 = b.v;
    var v_arr: [4]f32 = undefined;
    inline for (0..4) |i| v_arr[i] = math.atan2(y_arr[i], x_arr[i]);
    const v_out: F4g = v_arr;

    const zero = g4Zero();
    const r2 = b.v * b.v + a.v * a.v;
    const nz: @Vector(4, bool) = r2 != zero;
    const safe_r2 = @select(f32, nz, r2, g4One());
    const inv = g4One() / safe_r2;
    return .{
        .v = v_out,
        .dx = @select(f32, nz, (b.v * a.dx - a.v * b.dx) * inv, zero),
        .dy = @select(f32, nz, (b.v * a.dy - a.v * b.dy) * inv, zero),
        .dz = @select(f32, nz, (b.v * a.dz - a.v * b.dz) * inv, zero),
    };
}

// Per-lane scalar fallback for ops whose batched form isn't worth
// implementing (transcendentals, comparisons, intrinsic ops). The
// caller pays a 4× overhead on these versus the native batched ops, but
// they're rare in lowered SDF tapes.
fn g4UnaryPerLane(op: Unary, a: Grad4) Grad4 {
    var out: Grad4 = undefined;
    const av: [4]f32 = a.v;
    const adx: [4]f32 = a.dx;
    const ady: [4]f32 = a.dy;
    const adz: [4]f32 = a.dz;
    var v_arr: [4]f32 = undefined;
    var dx_arr: [4]f32 = undefined;
    var dy_arr: [4]f32 = undefined;
    var dz_arr: [4]f32 = undefined;
    inline for (0..4) |i| {
        const lane: Grad = .{ av[i], adx[i], ady[i], adz[i] };
        const r = gUnary(op, lane);
        v_arr[i] = r[0];
        dx_arr[i] = r[1];
        dy_arr[i] = r[2];
        dz_arr[i] = r[3];
    }
    out.v = v_arr;
    out.dx = dx_arr;
    out.dy = dy_arr;
    out.dz = dz_arr;
    return out;
}

fn g4BinaryPerLane(op: Binary, a: Grad4, b: Grad4) Grad4 {
    var out: Grad4 = undefined;
    const av: [4]f32 = a.v;
    const adx: [4]f32 = a.dx;
    const ady: [4]f32 = a.dy;
    const adz: [4]f32 = a.dz;
    const bv: [4]f32 = b.v;
    const bdx: [4]f32 = b.dx;
    const bdy: [4]f32 = b.dy;
    const bdz: [4]f32 = b.dz;
    var v_arr: [4]f32 = undefined;
    var dx_arr: [4]f32 = undefined;
    var dy_arr: [4]f32 = undefined;
    var dz_arr: [4]f32 = undefined;
    inline for (0..4) |i| {
        const la: Grad = .{ av[i], adx[i], ady[i], adz[i] };
        const lb: Grad = .{ bv[i], bdx[i], bdy[i], bdz[i] };
        const r = gBinary(op, la, lb);
        v_arr[i] = r[0];
        dx_arr[i] = r[1];
        dy_arr[i] = r[2];
        dz_arr[i] = r[3];
    }
    out.v = v_arr;
    out.dx = dx_arr;
    out.dy = dy_arr;
    out.dz = dz_arr;
    return out;
}

fn g4Unary(op: Unary, a: Grad4) Grad4 {
    return switch (op) {
        .neg => g4Neg(a),
        .abs => g4Abs(a),
        .sqrt => g4Sqrt(a),
        .square => g4Square(a),
        else => g4UnaryPerLane(op, a),
    };
}

fn g4Binary(op: Binary, a: Grad4, b: Grad4) Grad4 {
    return switch (op) {
        .add => g4Add(a, b),
        .sub => g4Sub(a, b),
        .mul => g4Mul(a, b),
        .div => g4Div(a, b),
        .min => g4Min(a, b),
        .max => g4Max(a, b),
        .atan2 => g4Atan2(a, b),
        else => g4BinaryPerLane(op, a, b),
    };
}

fn g4Primitive(ir: *const MathIR, node_id: i32, slots: []const f64, axis: [3]Grad4) Grad4 {
    // Per-lane fallback to scalar `gPrimitive`. Same chain-rule via outer
    // axes as `g4Intrinsic`; the central-diff overhead is per-lane but
    // amortised over 4 hits per tape walk.
    var out: Grad4 = undefined;
    const xv: [4]f32 = axis[0].v;
    const yv: [4]f32 = axis[1].v;
    const zv: [4]f32 = axis[2].v;
    var v_arr: [4]f32 = undefined;
    var dx_arr: [4]f32 = undefined;
    var dy_arr: [4]f32 = undefined;
    var dz_arr: [4]f32 = undefined;
    inline for (0..4) |i| {
        const ax_lane: [3]Grad = .{
            .{ xv[i], axis[0].dx[i], axis[0].dy[i], axis[0].dz[i] },
            .{ yv[i], axis[1].dx[i], axis[1].dy[i], axis[1].dz[i] },
            .{ zv[i], axis[2].dx[i], axis[2].dy[i], axis[2].dz[i] },
        };
        const r = gPrimitive(ir, node_id, slots, ax_lane);
        v_arr[i] = r[0];
        dx_arr[i] = r[1];
        dy_arr[i] = r[2];
        dz_arr[i] = r[3];
    }
    out.v = v_arr;
    out.dx = dx_arr;
    out.dy = dy_arr;
    out.dz = dz_arr;
    return out;
}

fn g4Intrinsic(ir: *const MathIR, intrinsic: Intrinsic, slots: []const f64, axis: [3]Grad4) Grad4 {
    // 4 lanes of (x, y, z) → 4 lanes of grad, each computed via per-lane
    // central differences (same as scalar grad).
    var out: Grad4 = undefined;
    const xv: [4]f32 = axis[0].v;
    const yv: [4]f32 = axis[1].v;
    const zv: [4]f32 = axis[2].v;
    var v_arr: [4]f32 = undefined;
    var dx_arr: [4]f32 = undefined;
    var dy_arr: [4]f32 = undefined;
    var dz_arr: [4]f32 = undefined;
    inline for (0..4) |i| {
        const ax_lane: [3]Grad = .{
            .{ xv[i], axis[0].dx[i], axis[0].dy[i], axis[0].dz[i] },
            .{ yv[i], axis[1].dx[i], axis[1].dy[i], axis[1].dz[i] },
            .{ zv[i], axis[2].dx[i], axis[2].dy[i], axis[2].dz[i] },
        };
        const r = gIntrinsic(ir, intrinsic, slots, ax_lane);
        v_arr[i] = r[0];
        dx_arr[i] = r[1];
        dy_arr[i] = r[2];
        dz_arr[i] = r[3];
    }
    out.v = v_arr;
    out.dx = dx_arr;
    out.dy = dy_arr;
    out.dz = dz_arr;
    return out;
}

pub fn decodeRegEvalGrad4(
    tape: *const RegTape,
    ir: *const MathIR,
    slots: []const f64,
    x: F4g,
    y: F4g,
    z: F4g,
    values: []Grad4,
) Grad4 {
    var axes: [64][3]Grad4 = undefined;
    const zero = g4Zero();
    const one = g4One();
    axes[0] = .{
        .{ .v = x, .dx = one, .dy = zero, .dz = zero },
        .{ .v = y, .dx = zero, .dy = one, .dz = zero },
        .{ .v = z, .dx = zero, .dy = zero, .dz = one },
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
            .load_slot => values[dst] = g4Const(@floatCast(eval.slotValue(slots, aux))),
            .load_const => values[dst] = g4Const(@floatCast(tape.immediates[@intCast(aux)])),
            .unary => values[dst] = g4Unary(@enumFromInt(aux), values[a]),
            .binary => values[dst] = g4Binary(@enumFromInt(aux), values[a], values[b]),
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
                    g4Add(g4Add(g4Add(g4Mul(m00, cur[0]), g4Mul(m01, cur[1])), g4Mul(m02, cur[2])), m03),
                    g4Add(g4Add(g4Add(g4Mul(m10, cur[0]), g4Mul(m11, cur[1])), g4Mul(m12, cur[2])), m13),
                    g4Add(g4Add(g4Add(g4Mul(m20, cur[0]), g4Mul(m21, cur[1])), g4Mul(m22, cur[2])), m23),
                };
                ap += 1;
            },
            .exit_remap => {
                values[dst] = values[a];
                ap -= 1;
            },
            .intrinsic => {
                const intrinsic = ir.intrinsics[@intCast(aux)];
                values[dst] = g4Intrinsic(ir, intrinsic, slots, axes[ap - 1]);
            },
            .primitive => {
                values[dst] = g4Primitive(ir, @intCast(dst), slots, axes[ap - 1]);
            },
            .fold => {
                const start: usize = @intCast(aux);
                const count: usize = @intCast(a);
                if (count == 0) {
                    values[dst] = .{ .v = g4Zero(), .dx = g4Zero(), .dy = g4Zero(), .dz = g4Zero() };
                } else {
                    const fop: math_ir.FoldOp = @enumFromInt(b);
                    var acc = values[@intCast(ir.node_refs[start])];
                    var i: usize = 1;
                    while (i < count) : (i += 1) {
                        const v = values[@intCast(ir.node_refs[start + i])];
                        acc = switch (fop) {
                            .min => g4Min(acc, v),
                            .max => g4Max(acc, v),
                            .sum => g4Add(acc, v),
                        };
                    }
                    values[dst] = acc;
                }
            },
            .copy_slot => values[dst] = values[a],
            .return_ => return values[a],
        }
    }
    return Grad4{ .v = g4Zero(), .dx = g4Zero(), .dy = g4Zero(), .dz = g4Zero() };
}
