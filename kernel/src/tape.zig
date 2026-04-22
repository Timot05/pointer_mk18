const std = @import("std");

pub const NodeRef = u32;

pub const Op = enum(u8) {
    input_x,
    input_y,
    input_z,
    constant,
    neg,
    abs,
    sqrt,
    square,
    add,
    sub,
    mul,
    div,
    min,
    max,
    atan2,
};

pub fn isChoiceOp(op: Op) bool {
    return op == .min or op == .max;
}

pub const Instruction = struct {
    op: Op,
    a: u32,
    b: u32,
};

pub const Tape = struct {
    ops: []Instruction,
    constants: []f32,
    choice_count: u32,
    output_slot: u32,
};

pub const TapeBuilder = struct {
    ops: []Instruction,
    op_count: u32 = 0,
    constants: []f32,
    const_count: u32 = 0,
    choice_count: u32 = 0,

    pub fn init(ops: []Instruction, constants: []f32) TapeBuilder {
        return .{ .ops = ops, .constants = constants };
    }

    pub fn finalize(self: *TapeBuilder, output_ref: NodeRef) Tape {
        return .{
            .ops = self.ops[0..self.op_count],
            .constants = self.constants[0..self.const_count],
            .choice_count = self.choice_count,
            .output_slot = output_ref,
        };
    }

    fn push(self: *TapeBuilder, ins: Instruction) NodeRef {
        // OOB here means the tape outgrew its pre-allocated slab. Overflow is
        // silent in ReleaseFast without this guard — it stomps whatever module
        // state follows `ops` in memory and we eventually crash somewhere
        // unrelated. Assert so the failure mode is legible.
        std.debug.assert(self.op_count < self.ops.len);
        const idx = self.op_count;
        self.ops[idx] = ins;
        self.op_count += 1;
        if (isChoiceOp(ins.op)) self.choice_count += 1;
        return idx;
    }

    pub fn inputX(self: *TapeBuilder) NodeRef {
        return self.push(.{ .op = .input_x, .a = 0, .b = 0 });
    }
    pub fn inputY(self: *TapeBuilder) NodeRef {
        return self.push(.{ .op = .input_y, .a = 0, .b = 0 });
    }
    pub fn inputZ(self: *TapeBuilder) NodeRef {
        return self.push(.{ .op = .input_z, .a = 0, .b = 0 });
    }
    pub fn constant(self: *TapeBuilder, v: f32) NodeRef {
        std.debug.assert(self.const_count < self.constants.len);
        const ci = self.const_count;
        self.constants[ci] = v;
        self.const_count += 1;
        return self.push(.{ .op = .constant, .a = ci, .b = 0 });
    }
    pub fn neg(self: *TapeBuilder, a: NodeRef) NodeRef {
        return self.push(.{ .op = .neg, .a = a, .b = 0 });
    }
    pub fn absOp(self: *TapeBuilder, a: NodeRef) NodeRef {
        return self.push(.{ .op = .abs, .a = a, .b = 0 });
    }
    pub fn sqrtOp(self: *TapeBuilder, a: NodeRef) NodeRef {
        return self.push(.{ .op = .sqrt, .a = a, .b = 0 });
    }
    pub fn square(self: *TapeBuilder, a: NodeRef) NodeRef {
        return self.push(.{ .op = .square, .a = a, .b = 0 });
    }
    pub fn add(self: *TapeBuilder, a: NodeRef, b: NodeRef) NodeRef {
        return self.push(.{ .op = .add, .a = a, .b = b });
    }
    pub fn sub(self: *TapeBuilder, a: NodeRef, b: NodeRef) NodeRef {
        return self.push(.{ .op = .sub, .a = a, .b = b });
    }
    pub fn mul(self: *TapeBuilder, a: NodeRef, b: NodeRef) NodeRef {
        return self.push(.{ .op = .mul, .a = a, .b = b });
    }
    pub fn div(self: *TapeBuilder, a: NodeRef, b: NodeRef) NodeRef {
        return self.push(.{ .op = .div, .a = a, .b = b });
    }
    pub fn minOp(self: *TapeBuilder, a: NodeRef, b: NodeRef) NodeRef {
        return self.push(.{ .op = .min, .a = a, .b = b });
    }
    pub fn maxOp(self: *TapeBuilder, a: NodeRef, b: NodeRef) NodeRef {
        return self.push(.{ .op = .max, .a = a, .b = b });
    }
    pub fn atan2Op(self: *TapeBuilder, y: NodeRef, x: NodeRef) NodeRef {
        // Convention: a = y, b = x (matches std.math.atan2(y, x)).
        return self.push(.{ .op = .atan2, .a = y, .b = x });
    }
};
