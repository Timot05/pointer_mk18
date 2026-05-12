// SIMD-4 bulk evaluator: one tape walk evaluates the SDF at four pixels at
// once. Used by the leaf-tile hot loop in `cpu_render.zig`.
//
// Each tape slot holds an F4 (= @Vector(4, f32)); linear ops map to single
// v128 instructions, lane-conditional ops (min/max/abs/sqrt) use @select
// masks, and the fully-irregular ops (sin/cos/atan2/pow/mod) fall back to
// per-lane scalars routed through `math_eval` to stay bit-precision-aligned.
//
// This evaluator does NOT compute partials. For analytical normals at hit
// points, use `decodeRegEvalGrad` from `math_grad.zig`.

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

pub const F4 = @Vector(4, f32);

inline fn splat(v: f32) F4 {
    return @splat(v);
}

// ── Per-lane fallback (scalar eval one lane at a time, route through f64) ─

fn unaryPerLane(op: Unary, a: F4) F4 {
    var out: [4]f32 = undefined;
    const av: [4]f32 = a;
    inline for (0..4) |i| {
        out[i] = @floatCast(eval.evalUnaryPoint(op, @floatCast(av[i])));
    }
    return out;
}

fn binaryPerLane(op: Binary, a: F4, b: F4) F4 {
    var out: [4]f32 = undefined;
    const av: [4]f32 = a;
    const bv: [4]f32 = b;
    inline for (0..4) |i| {
        out[i] = @floatCast(eval.evalBinaryPoint(op, @floatCast(av[i]), @floatCast(bv[i])));
    }
    return out;
}

/// SIMD-4 line-segment unsigned distance. Reads endpoint child values
/// directly out of the tape's `values` array — they were SIMD-evaluated
/// earlier in the tape walk, so the four lanes already carry the right
/// per-lane coords for free. ~12 SIMD ops vs four scalar `evalPoint` tree
/// walks (the previous per-lane fallback).
inline fn primLineSegmentF4(ir: *const MathIR, ref_start: usize, values: []const F4, qx: F4, qy: F4) F4 {
    const p0x = values[@intCast(ir.node_refs[ref_start + 0])];
    const p0y = values[@intCast(ir.node_refs[ref_start + 1])];
    const p1x = values[@intCast(ir.node_refs[ref_start + 2])];
    const p1y = values[@intCast(ir.node_refs[ref_start + 3])];
    const ex = p1x - p0x;
    const ey = p1y - p0y;
    const wx = qx - p0x;
    const wy = qy - p0y;
    const dot_we = wx * ex + wy * ey;
    const dot_ee = ex * ex + ey * ey + splat(1.0e-20);
    const one: F4 = @splat(1.0);
    const zero: F4 = @splat(0.0);
    const t = @max(zero, @min(one, dot_we / dot_ee));
    const dx = wx - t * ex;
    const dy = wy - t * ey;
    return fSqrt(dx * dx + dy * dy);
}

/// SIMD-4 circle (curve) unsigned distance: `||p - c| - r|`.
inline fn primCircleF4(ir: *const MathIR, ref_start: usize, values: []const F4, qx: F4, qy: F4) F4 {
    const cx = values[@intCast(ir.node_refs[ref_start + 0])];
    const cy = values[@intCast(ir.node_refs[ref_start + 1])];
    const r = values[@intCast(ir.node_refs[ref_start + 2])];
    const dx = qx - cx;
    const dy = qy - cy;
    const d = fSqrt(dx * dx + dy * dy);
    return fAbs(d - r);
}

inline fn planeAxesF4(ax: [3]F4, plane: i32) [2]F4 {
    return switch (plane) {
        0 => .{ ax[0], ax[1] }, // XY
        1 => .{ ax[0], ax[2] }, // XZ
        2 => .{ ax[1], ax[2] }, // YZ
        else => .{ ax[0], ax[1] },
    };
}

fn primitivePerLane(ir: *const MathIR, node_id: i32, slots: []const f64, ax: [3]F4) F4 {
    const xs: [4]f32 = ax[0];
    const ys: [4]f32 = ax[1];
    const zs: [4]f32 = ax[2];
    var vs: [4]f32 = undefined;
    inline for (0..4) |k| {
        const pp = Vec3{
            .x = @floatCast(xs[k]),
            .y = @floatCast(ys[k]),
            .z = @floatCast(zs[k]),
        };
        vs[k] = @floatCast(eval.evalPoint(ir, .{ .id = node_id }, slots, pp));
    }
    return @as(@Vector(4, f32), vs);
}

fn intrinsicPerLane(ir: *const MathIR, intrinsic: Intrinsic, slots: []const f64, x: F4, y: F4, z: F4) F4 {
    var out: [4]f32 = undefined;
    const xv: [4]f32 = x;
    const yv: [4]f32 = y;
    const zv: [4]f32 = z;
    inline for (0..4) |i| {
        const p = Vec3{ .x = @floatCast(xv[i]), .y = @floatCast(yv[i]), .z = @floatCast(zv[i]) };
        out[i] = @floatCast(eval.evalIntrinsicPoint(ir, intrinsic, slots, p));
    }
    return out;
}

// ── Lane-parallel ops ─────────────────────────────────────────────────────

inline fn fAbs(a: F4) F4 {
    const zero: F4 = @splat(0);
    const pos: @Vector(4, bool) = a > zero;
    const neg: @Vector(4, bool) = a < zero;
    return @select(f32, pos, a, @select(f32, neg, -a, zero));
}

inline fn fSqrt(a: F4) F4 {
    // Lanes with v ≤ 0 return 0 (matches f64 path's behavior of NaN→0 at the
    // call site since the renderer treats undefined lanes as misses).
    const zero: F4 = @splat(0);
    const pos: @Vector(4, bool) = a > zero;
    return @select(f32, pos, @sqrt(@max(a, zero)), zero);
}

inline fn fRecip(a: F4) F4 {
    const zero: F4 = @splat(0);
    const nonzero: @Vector(4, bool) = a != zero;
    const safe = @select(f32, nonzero, a, @as(F4, @splat(1)));
    const r = @as(F4, @splat(1)) / safe;
    return @select(f32, nonzero, r, zero);
}

inline fn fSquare(a: F4) F4 {
    return a * a;
}

inline fn fNeg(a: F4) F4 {
    return -a;
}

inline fn fMin(a: F4, b: F4) F4 {
    return @min(a, b);
}

inline fn fMax(a: F4, b: F4) F4 {
    return @max(a, b);
}

fn unaryF4(op: Unary, a: F4) F4 {
    return switch (op) {
        .neg => fNeg(a),
        .abs => fAbs(a),
        .recip => fRecip(a),
        .square => fSquare(a),
        .sqrt => fSqrt(a),
        // Everything else falls back to per-lane scalars via f64.
        .floor, .ceil, .round, .sin, .cos, .tan, .asin, .acos, .atan, .exp, .ln, .not => unaryPerLane(op, a),
    };
}

fn binaryF4(op: Binary, a: F4, b: F4) F4 {
    return switch (op) {
        .add => a + b,
        .sub => a - b,
        .mul => a * b,
        .div => blk: {
            const zero: F4 = @splat(0);
            const nonzero: @Vector(4, bool) = b != zero;
            const safe = @select(f32, nonzero, b, @as(F4, @splat(1)));
            break :blk @select(f32, nonzero, a / safe, zero);
        },
        .min => fMin(a, b),
        .max => fMax(a, b),
        // atan2/pow/compare/mod/and_/or_: uncommon enough that the per-lane
        // f64 fallback is fine. Compare/and_/or_/mod are non-smooth anyway.
        .atan2, .pow, .compare, .mod, .and_, .or_ => binaryPerLane(op, a, b),
    };
}

// ── Driver ────────────────────────────────────────────────────────────────

pub fn decodeRegEvalF4(
    tape: *const RegTape,
    ir: *const MathIR,
    slots: []const f64,
    x: F4,
    y: F4,
    z: F4,
    values: []F4,
) F4 {
    var axes: [64][3]F4 = undefined;
    axes[0] = .{ x, y, z };
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
            .load_slot => values[dst] = splat(@floatCast(eval.slotValue(slots, aux))),
            .load_const => values[dst] = splat(@floatCast(tape.immediates[@intCast(aux)])),
            .unary => values[dst] = unaryF4(@enumFromInt(aux), values[a]),
            .binary => values[dst] = binaryF4(@enumFromInt(aux), values[a], values[b]),
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
                values[dst] = intrinsicPerLane(ir, intrinsic, slots, axes[ap - 1][0], axes[ap - 1][1], axes[ap - 1][2]);
            },
            .primitive => {
                // Fast SIMD path for the two common kinds (line_segment,
                // circle). Bezier/arc fall back to per-lane scalar — their
                // evaluators do iterative work (Newton, etc.) that doesn't
                // vectorise cleanly. The fast path is the per-pixel hot
                // loop for from-sketch fields; with the scalar fallback in
                // place for every primitive, each F4 eval did four scalar
                // tree walks per primitive, swamping the leaf scan.
                const node = ir.nodes[@intCast(dst)];
                const plane = @divTrunc(node.op, 2);
                const q = planeAxesF4(axes[ap - 1], plane);
                const ref_start = @as(usize, @intCast(node.a));
                values[dst] = switch (node.kind) {
                    .line_segment => primLineSegmentF4(ir, ref_start, values, q[0], q[1]),
                    .circle => primCircleF4(ir, ref_start, values, q[0], q[1]),
                    else => primitivePerLane(ir, @intCast(dst), slots, axes[ap - 1]),
                };
            },
            .fold => {
                const start: usize = @intCast(aux);
                const count: usize = @intCast(a);
                if (count == 0) {
                    values[dst] = @splat(0);
                } else {
                    const fop: math_ir.FoldOp = @enumFromInt(b);
                    var acc = values[@intCast(ir.node_refs[start])];
                    var i: usize = 1;
                    while (i < count) : (i += 1) {
                        const v = values[@intCast(ir.node_refs[start + i])];
                        acc = switch (fop) {
                            .min => @min(acc, v),
                            .max => @max(acc, v),
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
    return @splat(0);
}
