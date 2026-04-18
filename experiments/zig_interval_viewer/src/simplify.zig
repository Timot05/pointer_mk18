const std = @import("std");
const tape_mod = @import("tape.zig");
const interval_mod = @import("interval.zig");

// Each original slot resolves to either a known compile-time constant or a
// slot in the *new* tape being built. Choice-resolution collapses min/max
// transitively — if a min's picked operand is itself a constant, the min's
// value is that constant, no op emitted.
pub const Value = union(enum) {
    constant: f32,
    slot: u32,
};

const Emitter = struct {
    out_ops: []tape_mod.Instruction,
    out_consts: []f32,
    op_count: u32 = 0,
    const_count: u32 = 0,

    fn emit(self: *Emitter, op: tape_mod.Op, a: u32, b: u32) u32 {
        const s = self.op_count;
        self.out_ops[s] = .{ .op = op, .a = a, .b = b };
        self.op_count += 1;
        return s;
    }

    fn emitConst(self: *Emitter, c: f32) u32 {
        const ci = self.const_count;
        self.out_consts[ci] = c;
        self.const_count += 1;
        return self.emit(.constant, ci, 0);
    }

    // Force a Value into a concrete slot. Slots are returned as-is;
    // Constants materialize a fresh `constant` op.
    fn realize(self: *Emitter, v: Value) u32 {
        return switch (v) {
            .slot => |s| s,
            .constant => |c| self.emitConst(c),
        };
    }
};

fn unaryFold(
    v: Value,
    op: tape_mod.Op,
    comptime foldFn: fn (f32) f32,
    em: *Emitter,
) Value {
    return switch (v) {
        .constant => |c| .{ .constant = foldFn(c) },
        .slot => |s| .{ .slot = em.emit(op, s, 0) },
    };
}

fn addFold(va: Value, vb: Value, em: *Emitter) Value {
    switch (va) {
        .constant => |ac| switch (vb) {
            .constant => |bc| return .{ .constant = ac + bc },
            .slot => |bs| {
                if (ac == 0) return .{ .slot = bs };
                return .{ .slot = em.emit(.add, em.emitConst(ac), bs) };
            },
        },
        .slot => |as| switch (vb) {
            .constant => |bc| {
                if (bc == 0) return .{ .slot = as };
                return .{ .slot = em.emit(.add, as, em.emitConst(bc)) };
            },
            .slot => |bs| return .{ .slot = em.emit(.add, as, bs) },
        },
    }
}

fn subFold(va: Value, vb: Value, em: *Emitter) Value {
    switch (va) {
        .constant => |ac| switch (vb) {
            .constant => |bc| return .{ .constant = ac - bc },
            .slot => |bs| {
                if (ac == 0) return .{ .slot = em.emit(.neg, bs, 0) };
                return .{ .slot = em.emit(.sub, em.emitConst(ac), bs) };
            },
        },
        .slot => |as| switch (vb) {
            .constant => |bc| {
                if (bc == 0) return .{ .slot = as };
                return .{ .slot = em.emit(.sub, as, em.emitConst(bc)) };
            },
            .slot => |bs| return .{ .slot = em.emit(.sub, as, bs) },
        },
    }
}

fn mulFold(va: Value, vb: Value, em: *Emitter) Value {
    switch (va) {
        .constant => |ac| switch (vb) {
            .constant => |bc| return .{ .constant = ac * bc },
            .slot => |bs| {
                if (ac == 0) return .{ .constant = 0 };
                if (ac == 1) return .{ .slot = bs };
                if (ac == -1) return .{ .slot = em.emit(.neg, bs, 0) };
                return .{ .slot = em.emit(.mul, em.emitConst(ac), bs) };
            },
        },
        .slot => |as| switch (vb) {
            .constant => |bc| {
                if (bc == 0) return .{ .constant = 0 };
                if (bc == 1) return .{ .slot = as };
                if (bc == -1) return .{ .slot = em.emit(.neg, as, 0) };
                return .{ .slot = em.emit(.mul, as, em.emitConst(bc)) };
            },
            .slot => |bs| return .{ .slot = em.emit(.mul, as, bs) },
        },
    }
}

fn divFold(va: Value, vb: Value, em: *Emitter) Value {
    switch (va) {
        .constant => |ac| switch (vb) {
            .constant => |bc| {
                if (bc == 0) {
                    // Preserve NaN/inf semantics of the unfolded op; emit a real
                    // div so run-time behaviour matches the original tape.
                    return .{ .slot = em.emit(.div, em.emitConst(ac), em.emitConst(bc)) };
                }
                return .{ .constant = ac / bc };
            },
            .slot => |bs| {
                if (ac == 0) return .{ .constant = 0 };
                return .{ .slot = em.emit(.div, em.emitConst(ac), bs) };
            },
        },
        .slot => |as| switch (vb) {
            .constant => |bc| {
                if (bc == 1) return .{ .slot = as };
                if (bc == -1) return .{ .slot = em.emit(.neg, as, 0) };
                if (bc == 0) {
                    return .{ .slot = em.emit(.div, as, em.emitConst(bc)) };
                }
                // Prefer mul-by-reciprocal: cheaper runtime, same result for finite c.
                return .{ .slot = em.emit(.mul, as, em.emitConst(1.0 / bc)) };
            },
            .slot => |bs| return .{ .slot = em.emit(.div, as, bs) },
        },
    }
}

fn minFold(va: Value, vb: Value, em: *Emitter) Value {
    switch (va) {
        .constant => |ac| switch (vb) {
            .constant => |bc| return .{ .constant = @min(ac, bc) },
            .slot => |bs| return .{ .slot = em.emit(.min, em.emitConst(ac), bs) },
        },
        .slot => |as| switch (vb) {
            .constant => |bc| return .{ .slot = em.emit(.min, as, em.emitConst(bc)) },
            .slot => |bs| return .{ .slot = em.emit(.min, as, bs) },
        },
    }
}

fn maxFold(va: Value, vb: Value, em: *Emitter) Value {
    switch (va) {
        .constant => |ac| switch (vb) {
            .constant => |bc| return .{ .constant = @max(ac, bc) },
            .slot => |bs| return .{ .slot = em.emit(.max, em.emitConst(ac), bs) },
        },
        .slot => |as| switch (vb) {
            .constant => |bc| return .{ .slot = em.emit(.max, as, em.emitConst(bc)) },
            .slot => |bs| return .{ .slot = em.emit(.max, as, bs) },
        },
    }
}

fn atan2Fold(va: Value, vb: Value, em: *Emitter) Value {
    switch (va) {
        .constant => |ac| switch (vb) {
            .constant => |bc| return .{ .constant = std.math.atan2(ac, bc) },
            .slot => |bs| return .{ .slot = em.emit(.atan2, em.emitConst(ac), bs) },
        },
        .slot => |as| switch (vb) {
            .constant => |bc| return .{ .slot = em.emit(.atan2, as, em.emitConst(bc)) },
            .slot => |bs| return .{ .slot = em.emit(.atan2, as, bs) },
        },
    }
}

fn foldNeg(c: f32) f32 { return -c; }
fn foldAbs(c: f32) f32 { return @abs(c); }
fn foldSqrt(c: f32) f32 { return @sqrt(@max(c, 0)); }
fn foldSquare(c: f32) f32 { return c * c; }

// Rewrites the tape by (a) resolving min/max choices via the supplied trace,
// (b) propagating known-constant values through all arithmetic ops, and
// (c) dead-code-eliminating ops whose results no longer reach the output.
//
// Scratch requirements:
//   scratch_value:    >= orig.ops.len entries
//   scratch_live:     >= out_ops.len entries (conservatively == out_ops.len)
//   scratch_new_idx:  >= out_ops.len entries
//
// The input `out_ops` / `out_constants` must be sized to hold the largest
// possible intermediate tape (constants we materialize for binary ops can
// briefly exceed the original size before DCE; 2× the original is safe).
pub fn simplify(
    orig: *const tape_mod.Tape,
    trace: []const interval_mod.Choice,
    out_ops: []tape_mod.Instruction,
    out_constants: []f32,
    scratch_value: []Value,
    scratch_live: []bool,
    scratch_new_idx: []u32,
) tape_mod.Tape {
    var em: Emitter = .{ .out_ops = out_ops, .out_consts = out_constants };
    var choice_idx: u32 = 0;

    // ── Pass 1: forward fold ──────────────────────────────────────────
    for (orig.ops, 0..) |ins, i| {
        const va: Value = if (ins.op == .input_x or ins.op == .input_y or
            ins.op == .input_z or ins.op == .constant)
            undefined
        else
            scratch_value[ins.a];
        const vb: Value = switch (ins.op) {
            .add, .sub, .mul, .div, .min, .max, .atan2 => scratch_value[ins.b],
            else => undefined,
        };

        scratch_value[i] = switch (ins.op) {
            .input_x => .{ .slot = em.emit(.input_x, 0, 0) },
            .input_y => .{ .slot = em.emit(.input_y, 0, 0) },
            .input_z => .{ .slot = em.emit(.input_z, 0, 0) },
            .constant => .{ .constant = orig.constants[ins.a] },
            .neg => unaryFold(va, .neg, foldNeg, &em),
            .abs => unaryFold(va, .abs, foldAbs, &em),
            .sqrt => unaryFold(va, .sqrt, foldSqrt, &em),
            .square => unaryFold(va, .square, foldSquare, &em),
            .add => addFold(va, vb, &em),
            .sub => subFold(va, vb, &em),
            .mul => mulFold(va, vb, &em),
            .div => divFold(va, vb, &em),
            .min => blk: {
                const c = trace[choice_idx];
                choice_idx += 1;
                break :blk switch (c) {
                    .left => va,
                    .right => vb,
                    .both => minFold(va, vb, &em),
                };
            },
            .max => blk: {
                const c = trace[choice_idx];
                choice_idx += 1;
                break :blk switch (c) {
                    .left => va,
                    .right => vb,
                    .both => maxFold(va, vb, &em),
                };
            },
            .atan2 => atan2Fold(va, vb, &em),
        };
    }

    // Pin the output.
    const output_slot = em.realize(scratch_value[orig.output_slot]);

    // ── Pass 2: DCE on the new tape (reverse liveness, forward compact) ──
    const n_out = em.op_count;
    for (0..n_out) |k| scratch_live[k] = false;
    scratch_live[output_slot] = true;
    var j: usize = n_out;
    while (j > 0) {
        j -= 1;
        if (!scratch_live[j]) continue;
        const ins = out_ops[j];
        switch (ins.op) {
            .input_x, .input_y, .input_z, .constant => {},
            .neg, .abs, .sqrt, .square => scratch_live[ins.a] = true,
            .add, .sub, .mul, .div, .min, .max, .atan2 => {
                scratch_live[ins.a] = true;
                scratch_live[ins.b] = true;
            },
        }
    }

    var k2: u32 = 0;
    var const_count_out: u32 = 0;
    var choice_count_out: u32 = 0;
    for (out_ops[0..n_out], 0..) |ins, idx| {
        if (!scratch_live[idx]) continue;
        var new_ins = ins;
        switch (ins.op) {
            .input_x, .input_y, .input_z => {},
            .constant => {
                out_constants[const_count_out] = out_constants[ins.a];
                new_ins.a = const_count_out;
                const_count_out += 1;
            },
            .neg, .abs, .sqrt, .square => {
                new_ins.a = scratch_new_idx[ins.a];
            },
            .add, .sub, .mul, .div, .min, .max, .atan2 => {
                new_ins.a = scratch_new_idx[ins.a];
                new_ins.b = scratch_new_idx[ins.b];
            },
        }
        if (ins.op == .min or ins.op == .max) choice_count_out += 1;
        out_ops[k2] = new_ins;
        scratch_new_idx[idx] = k2;
        k2 += 1;
    }

    return .{
        .ops = out_ops[0..k2],
        .constants = out_constants[0..const_count_out],
        .choice_count = choice_count_out,
        .output_slot = scratch_new_idx[output_slot],
    };
}
