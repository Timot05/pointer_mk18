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
