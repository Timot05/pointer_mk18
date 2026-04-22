const std = @import("std");
const tape_mod = @import("tape.zig");

pub fn evalScalar(
    tape: *const tape_mod.Tape,
    x: f32,
    y: f32,
    z: f32,
    slots: []f32,
) f32 {
    for (tape.ops, 0..) |ins, i| {
        slots[i] = switch (ins.op) {
            .input_x => x,
            .input_y => y,
            .input_z => z,
            .constant => tape.constants[ins.a],
            .neg => -slots[ins.a],
            .abs => @abs(slots[ins.a]),
            .sqrt => @sqrt(slots[ins.a]),
            .square => slots[ins.a] * slots[ins.a],
            .add => slots[ins.a] + slots[ins.b],
            .sub => slots[ins.a] - slots[ins.b],
            .mul => slots[ins.a] * slots[ins.b],
            .div => slots[ins.a] / slots[ins.b],
            .min => @min(slots[ins.a], slots[ins.b]),
            .max => @max(slots[ins.a], slots[ins.b]),
            .atan2 => std.math.atan2(slots[ins.a], slots[ins.b]),
        };
    }
    return slots[tape.output_slot];
}

pub const F4 = @Vector(4, f32);

// Batched scalar eval: interprets the tape once across 4 independent sample
// points (4 lanes of f32). One tape walk yields four SDF values. Used in the
// voxel leaf-sampling hot path, where adjacent x-pixels in a row share wcy,
// wcz and differ only in wcx.
pub fn evalScalar4(
    tape: *const tape_mod.Tape,
    x: F4,
    y: F4,
    z: F4,
    slots: []F4,
) F4 {
    for (tape.ops, 0..) |ins, i| {
        slots[i] = switch (ins.op) {
            .input_x => x,
            .input_y => y,
            .input_z => z,
            .constant => @splat(tape.constants[ins.a]),
            .neg => -slots[ins.a],
            .abs => @abs(slots[ins.a]),
            .sqrt => @sqrt(slots[ins.a]),
            .square => slots[ins.a] * slots[ins.a],
            .add => slots[ins.a] + slots[ins.b],
            .sub => slots[ins.a] - slots[ins.b],
            .mul => slots[ins.a] * slots[ins.b],
            .div => slots[ins.a] / slots[ins.b],
            .min => @min(slots[ins.a], slots[ins.b]),
            .max => @max(slots[ins.a], slots[ins.b]),
            .atan2 => atan2_v4(slots[ins.a], slots[ins.b]),
        };
    }
    return slots[tape.output_slot];
}

inline fn atan2_v4(y: F4, x: F4) F4 {
    return .{
        std.math.atan2(y[0], x[0]),
        std.math.atan2(y[1], x[1]),
        std.math.atan2(y[2], x[2]),
        std.math.atan2(y[3], x[3]),
    };
}

// Bulk float slice eval: one tape walk over `n` independent points.
//
// Loop structure is inverted from `evalScalar4`: the outer loop is over ops,
// the inner loops are tight strided scalar kernels over `n` points. Per-op
// fixed costs (switch dispatch, slot address math) fire once per tape walk
// instead of once per SIMD quad, and LLVM auto-vectorizes the inner loops to
// WASM v128 under simd128. Net effect: dispatch overhead amortizes over
// hundreds of points per tape traversal instead of four.
//
// Slot layout is row-major SoA: slots[slot_idx * stride .. slot_idx * stride + n]
// is the per-point value array for `slot_idx`. Caller owns the backing buffer;
// it must have slots.len >= tape.ops.len * stride and stride >= n.
pub fn evalFloatSlice(
    tape: *const tape_mod.Tape,
    xs: []const f32,
    ys: []const f32,
    zs: []const f32,
    out: []f32,
    slots: []f32,
    stride: usize,
) void {
    const n = xs.len;
    std.debug.assert(ys.len == n);
    std.debug.assert(zs.len == n);
    std.debug.assert(out.len >= n);
    std.debug.assert(stride >= n);
    std.debug.assert(slots.len >= tape.ops.len * stride);

    for (tape.ops, 0..) |ins, i| {
        const dst_base = i * stride;
        const dst = slots[dst_base .. dst_base + n];
        switch (ins.op) {
            .input_x => @memcpy(dst, xs),
            .input_y => @memcpy(dst, ys),
            .input_z => @memcpy(dst, zs),
            .constant => {
                const c = tape.constants[ins.a];
                for (dst) |*d| d.* = c;
            },
            .neg => {
                const a = slots[ins.a * stride .. ins.a * stride + n];
                for (dst, a) |*d, va| d.* = -va;
            },
            .abs => {
                const a = slots[ins.a * stride .. ins.a * stride + n];
                for (dst, a) |*d, va| d.* = @abs(va);
            },
            .sqrt => {
                const a = slots[ins.a * stride .. ins.a * stride + n];
                for (dst, a) |*d, va| d.* = @sqrt(va);
            },
            .square => {
                const a = slots[ins.a * stride .. ins.a * stride + n];
                for (dst, a) |*d, va| d.* = va * va;
            },
            .add => {
                const a = slots[ins.a * stride .. ins.a * stride + n];
                const b = slots[ins.b * stride .. ins.b * stride + n];
                for (dst, a, b) |*d, va, vb| d.* = va + vb;
            },
            .sub => {
                const a = slots[ins.a * stride .. ins.a * stride + n];
                const b = slots[ins.b * stride .. ins.b * stride + n];
                for (dst, a, b) |*d, va, vb| d.* = va - vb;
            },
            .mul => {
                const a = slots[ins.a * stride .. ins.a * stride + n];
                const b = slots[ins.b * stride .. ins.b * stride + n];
                for (dst, a, b) |*d, va, vb| d.* = va * vb;
            },
            .div => {
                const a = slots[ins.a * stride .. ins.a * stride + n];
                const b = slots[ins.b * stride .. ins.b * stride + n];
                for (dst, a, b) |*d, va, vb| d.* = va / vb;
            },
            .min => {
                const a = slots[ins.a * stride .. ins.a * stride + n];
                const b = slots[ins.b * stride .. ins.b * stride + n];
                for (dst, a, b) |*d, va, vb| d.* = @min(va, vb);
            },
            .max => {
                const a = slots[ins.a * stride .. ins.a * stride + n];
                const b = slots[ins.b * stride .. ins.b * stride + n];
                for (dst, a, b) |*d, va, vb| d.* = @max(va, vb);
            },
            .atan2 => {
                const a = slots[ins.a * stride .. ins.a * stride + n];
                const b = slots[ins.b * stride .. ins.b * stride + n];
                for (dst, a, b) |*d, va, vb| d.* = std.math.atan2(va, vb);
            },
        }
    }
    const out_base = tape.output_slot * stride;
    @memcpy(out[0..n], slots[out_base .. out_base + n]);
}

test "evalFloatSlice matches evalScalar for sphere SDF" {
    var ops: [16]tape_mod.Instruction = undefined;
    var consts: [4]f32 = undefined;
    var b = tape_mod.TapeBuilder.init(&ops, &consts);
    const x = b.inputX();
    const y = b.inputY();
    const z = b.inputZ();
    const sum = b.add(b.add(b.square(x), b.square(y)), b.square(z));
    const r = b.constant(3.0);
    const out = b.sub(b.sqrtOp(sum), r);
    const tape = b.finalize(out);

    const xs = [_]f32{ 0.0, 3.0, 5.0, -4.0 };
    const ys = [_]f32{ 0.0, 0.0, 0.0, 3.0 };
    const zs = [_]f32{ 0.0, 0.0, 0.0, 0.0 };
    var sdf: [4]f32 = undefined;
    var slot_buf: [16 * 4]f32 = undefined;
    evalFloatSlice(&tape, &xs, &ys, &zs, &sdf, &slot_buf, 4);

    var slots: [16]f32 = undefined;
    inline for (0..4) |i| {
        const expected = evalScalar(&tape, xs[i], ys[i], zs[i], &slots);
        try std.testing.expectApproxEqAbs(expected, sdf[i], 1e-5);
    }
}
