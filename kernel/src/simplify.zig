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

pub const Workspace = struct {
    values: []Value,
    live: []bool,
    new_idx: []u32,
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
// Workspace requirements:
//   values:   >= orig.ops.len entries
//   live:     >= out_ops.len entries (conservatively == out_ops.len)
//   new_idx:  >= out_ops.len entries
//
// The input `out_ops` / `out_constants` must be sized to hold the largest
// possible intermediate tape (constants we materialize for binary ops can
// briefly exceed the original size before DCE; 2× the original is safe).
pub fn simplify(
    orig: *const tape_mod.Tape,
    trace: []const interval_mod.Choice,
    out_ops: []tape_mod.Instruction,
    out_constants: []f32,
    workspace: Workspace,
) tape_mod.Tape {
    std.debug.assert(trace.len >= orig.choice_count);

    var em: Emitter = .{ .out_ops = out_ops, .out_consts = out_constants };
    var choice_idx: u32 = 0;

    // ── Pass 1: forward fold ──────────────────────────────────────────
    for (orig.ops, 0..) |ins, i| {
        const va: Value = if (ins.op == .input_x or ins.op == .input_y or
            ins.op == .input_z or ins.op == .constant)
            undefined
        else
            workspace.values[ins.a];
        const vb: Value = switch (ins.op) {
            .add, .sub, .mul, .div, .min, .max, .atan2 => workspace.values[ins.b],
            else => undefined,
        };

        workspace.values[i] = switch (ins.op) {
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
    const output_slot = em.realize(workspace.values[orig.output_slot]);

    // ── Pass 2: DCE on the new tape (reverse liveness, forward compact) ──
    const n_out = em.op_count;
    for (0..n_out) |k| workspace.live[k] = false;
    workspace.live[output_slot] = true;
    var j: usize = n_out;
    while (j > 0) {
        j -= 1;
        if (!workspace.live[j]) continue;
        const ins = out_ops[j];
        switch (ins.op) {
            .input_x, .input_y, .input_z, .constant => {},
            .neg, .abs, .sqrt, .square => workspace.live[ins.a] = true,
            .add, .sub, .mul, .div, .min, .max, .atan2 => {
                workspace.live[ins.a] = true;
                workspace.live[ins.b] = true;
            },
        }
    }

    var k2: u32 = 0;
    var const_count_out: u32 = 0;
    var choice_count_out: u32 = 0;
    for (out_ops[0..n_out], 0..) |ins, idx| {
        if (!workspace.live[idx]) continue;
        var new_ins = ins;
        switch (ins.op) {
            .input_x, .input_y, .input_z => {},
            .constant => {
                out_constants[const_count_out] = out_constants[ins.a];
                new_ins.a = const_count_out;
                const_count_out += 1;
            },
            .neg, .abs, .sqrt, .square => {
                new_ins.a = workspace.new_idx[ins.a];
            },
            .add, .sub, .mul, .div, .min, .max, .atan2 => {
                new_ins.a = workspace.new_idx[ins.a];
                new_ins.b = workspace.new_idx[ins.b];
            },
        }
        if (ins.op == .min or ins.op == .max) choice_count_out += 1;
        out_ops[k2] = new_ins;
        workspace.new_idx[idx] = k2;
        k2 += 1;
    }

    return .{
        .ops = out_ops[0..k2],
        .constants = out_constants[0..const_count_out],
        .choice_count = choice_count_out,
        .output_slot = workspace.new_idx[output_slot],
    };
}

fn expectConstOutput(tape: tape_mod.Tape, expected: f32) !void {
    try std.testing.expectEqual(@as(usize, 1), tape.ops.len);
    try std.testing.expectEqual(@as(usize, 1), tape.constants.len);
    try std.testing.expectEqual(tape_mod.Op.constant, tape.ops[0].op);
    try std.testing.expectEqual(@as(u32, 0), tape.output_slot);
    try std.testing.expectEqual(expected, tape.constants[0]);
}

test "simplify resolves min choice to left constant and drops unused branch" {
    var ops: [8]tape_mod.Instruction = undefined;
    var consts: [4]f32 = undefined;
    var builder = tape_mod.TapeBuilder.init(&ops, &consts);
    const left = builder.constant(-2.0);
    const right = builder.constant(3.0);
    const out = builder.minOp(left, right);
    const orig = builder.finalize(out);

    var out_ops: [16]tape_mod.Instruction = undefined;
    var out_consts: [16]f32 = undefined;
    var scratch_value: [16]Value = undefined;
    var scratch_live: [16]bool = undefined;
    var scratch_new_idx: [16]u32 = undefined;

    const simp = simplify(
        &orig,
        &[_]interval_mod.Choice{.left},
        &out_ops,
        &out_consts,
        .{
            .values = &scratch_value,
            .live = &scratch_live,
            .new_idx = &scratch_new_idx,
        },
    );

    try expectConstOutput(simp, -2.0);
}

test "simplify preserves ambiguous min and folds constant add" {
    var ops: [16]tape_mod.Instruction = undefined;
    var consts: [8]f32 = undefined;
    var builder = tape_mod.TapeBuilder.init(&ops, &consts);
    const x = builder.inputX();
    const zero = builder.constant(0.0);
    const left = builder.add(x, zero);
    const five = builder.constant(5.0);
    const out = builder.minOp(left, five);
    const orig = builder.finalize(out);

    var out_ops: [32]tape_mod.Instruction = undefined;
    var out_consts: [32]f32 = undefined;
    var scratch_value: [32]Value = undefined;
    var scratch_live: [32]bool = undefined;
    var scratch_new_idx: [32]u32 = undefined;

    const simp = simplify(
        &orig,
        &[_]interval_mod.Choice{.both},
        &out_ops,
        &out_consts,
        .{
            .values = &scratch_value,
            .live = &scratch_live,
            .new_idx = &scratch_new_idx,
        },
    );

    try std.testing.expectEqual(@as(usize, 3), simp.ops.len);
    try std.testing.expectEqual(@as(usize, 1), simp.constants.len);
    try std.testing.expectEqual(tape_mod.Op.input_x, simp.ops[0].op);
    try std.testing.expectEqual(tape_mod.Op.constant, simp.ops[1].op);
    try std.testing.expectEqual(tape_mod.Op.min, simp.ops[2].op);
    try std.testing.expectEqual(@as(u32, 0), simp.ops[2].a);
    try std.testing.expectEqual(@as(u32, 1), simp.ops[2].b);
    try std.testing.expectEqual(@as(u32, 1), simp.choice_count);
}

test "simplify eliminates dead code unrelated to output" {
    var ops: [16]tape_mod.Instruction = undefined;
    var consts: [8]f32 = undefined;
    var builder = tape_mod.TapeBuilder.init(&ops, &consts);
    const x = builder.inputX();
    _ = builder.mul(builder.constant(7.0), builder.constant(9.0));
    const out = builder.square(x);
    const orig = builder.finalize(out);

    var out_ops: [32]tape_mod.Instruction = undefined;
    var out_consts: [32]f32 = undefined;
    var scratch_value: [32]Value = undefined;
    var scratch_live: [32]bool = undefined;
    var scratch_new_idx: [32]u32 = undefined;

    const simp = simplify(
        &orig,
        &[_]interval_mod.Choice{},
        &out_ops,
        &out_consts,
        .{
            .values = &scratch_value,
            .live = &scratch_live,
            .new_idx = &scratch_new_idx,
        },
    );

    try std.testing.expectEqual(@as(usize, 2), simp.ops.len);
    try std.testing.expectEqual(tape_mod.Op.input_x, simp.ops[0].op);
    try std.testing.expectEqual(tape_mod.Op.square, simp.ops[1].op);
    try std.testing.expectEqual(@as(usize, 0), simp.constants.len);
    try std.testing.expectEqual(@as(u32, 1), simp.output_slot);
}

test "simplify turns multiply by zero into constant output" {
    var ops: [16]tape_mod.Instruction = undefined;
    var consts: [8]f32 = undefined;
    var builder = tape_mod.TapeBuilder.init(&ops, &consts);
    const x = builder.inputX();
    const out = builder.mul(x, builder.constant(0.0));
    const orig = builder.finalize(out);

    var out_ops: [32]tape_mod.Instruction = undefined;
    var out_consts: [32]f32 = undefined;
    var scratch_value: [32]Value = undefined;
    var scratch_live: [32]bool = undefined;
    var scratch_new_idx: [32]u32 = undefined;

    const simp = simplify(
        &orig,
        &[_]interval_mod.Choice{},
        &out_ops,
        &out_consts,
        .{
            .values = &scratch_value,
            .live = &scratch_live,
            .new_idx = &scratch_new_idx,
        },
    );

    try expectConstOutput(simp, 0.0);
}
