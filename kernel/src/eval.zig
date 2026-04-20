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
