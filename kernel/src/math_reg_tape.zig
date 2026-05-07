const std = @import("std");
const math = std.math;
const math_ir = @import("math_ir.zig");
const eval = @import("math_eval.zig");

const Axis = math_ir.Axis;
const Expr = math_ir.Expr;
const Vec3 = math_ir.Vec3;
const MathIR = math_ir.MathIR;
const Unary = math_ir.Unary;
const Binary = math_ir.Binary;
const Interval = math_ir.Interval;
const Box3 = math_ir.Box3;
const max_tape_words = math_ir.max_tape_words;
const max_immediates = math_ir.max_immediates;
const max_nodes = math_ir.max_nodes;

pub const Op = enum(u8) {
    load_x,
    load_y,
    load_z,
    load_slot,
    load_const,
    unary,
    binary,
    enter_remap_axes,
    enter_remap_affine,
    exit_remap,
    intrinsic,
    copy_slot,
    return_,
};

pub const Choice = enum(u8) { none, left, right, both };

pub const RegTape = struct {
    opcodes: [max_tape_words]u8 = undefined,
    dst: [max_tape_words]u16 = undefined,
    src_a: [max_tape_words]u16 = undefined,
    src_b: [max_tape_words]u16 = undefined,
    src_c: [max_tape_words]u16 = undefined,
    aux: [max_tape_words]i32 = undefined,
    instruction_count: usize = 0,

    immediates: [max_immediates]f64 = undefined,
    immediate_count: usize = 0,

    slot_count: usize = 0,

    fn emit(self: *RegTape, op: Op, dst_v: u16, a: u16, b: u16, c: u16, auxv: i32) !void {
        if (self.instruction_count >= max_tape_words) return error.TapeCapacity;
        self.opcodes[self.instruction_count] = @intFromEnum(op);
        self.dst[self.instruction_count] = dst_v;
        self.src_a[self.instruction_count] = a;
        self.src_b[self.instruction_count] = b;
        self.src_c[self.instruction_count] = c;
        self.aux[self.instruction_count] = auxv;
        self.instruction_count += 1;
    }

    fn immediate(self: *RegTape, value: f64) !i32 {
        var i: usize = 0;
        while (i < self.immediate_count) : (i += 1) {
            if (self.immediates[i] == value) return @intCast(i);
        }
        if (self.immediate_count >= max_immediates) return error.ImmediateCapacity;
        const id = self.immediate_count;
        self.immediates[id] = value;
        self.immediate_count += 1;
        return @intCast(id);
    }
};

pub fn compileToRegTape(ir: *const MathIR, root: Expr) !RegTape {
    var tape = RegTape{};
    tape.slot_count = ir.node_count;
    var visited = [_]bool{false} ** max_nodes;
    try encodeNode(&tape, ir, @intCast(root.id), &visited);
    try tape.emit(.return_, 0, @intCast(root.id), 0, 0, 0);
    return tape;
}

const EncodeError = error{ TapeCapacity, ImmediateCapacity };

fn encodeBody(tape: *RegTape, ir: *const MathIR, node_id: u16, visited: *[max_nodes]bool) EncodeError!void {
    var saved: [max_nodes]bool = undefined;
    @memcpy(&saved, visited);
    @memset(visited, false);
    try encodeNode(tape, ir, node_id, visited);
    @memcpy(visited, &saved);
}

fn encodeNode(tape: *RegTape, ir: *const MathIR, node_id: u16, visited: *[max_nodes]bool) EncodeError!void {
    if (visited[node_id]) return;
    visited[node_id] = true;
    const node = ir.nodes[node_id];
    switch (node.kind) {
        .var_ => {
            const ax: Axis = @enumFromInt(node.op);
            const op: Op = switch (ax) {
                .x => .load_x,
                .y => .load_y,
                .z => .load_z,
            };
            try tape.emit(op, node_id, 0, 0, 0, 0);
        },
        .slot => {
            try tape.emit(.load_slot, node_id, 0, 0, 0, node.op);
        },
        .const_ => {
            const imm_id = try tape.immediate(node.value);
            try tape.emit(.load_const, node_id, 0, 0, 0, imm_id);
        },
        .unary => {
            try encodeNode(tape, ir, @intCast(node.a), visited);
            try tape.emit(.unary, node_id, @intCast(node.a), 0, 0, node.op);
        },
        .binary => {
            try encodeNode(tape, ir, @intCast(node.a), visited);
            try encodeNode(tape, ir, @intCast(node.b), visited);
            try tape.emit(.binary, node_id, @intCast(node.a), @intCast(node.b), 0, node.op);
        },
        .remap_axes => {
            try encodeNode(tape, ir, @intCast(node.b), visited);
            try encodeNode(tape, ir, @intCast(node.c), visited);
            try encodeNode(tape, ir, @intCast(node.d), visited);
            try tape.emit(.enter_remap_axes, 0, @intCast(node.b), @intCast(node.c), @intCast(node.d), 0);
            try encodeBody(tape, ir, @intCast(node.a), visited);
            try tape.emit(.exit_remap, node_id, @intCast(node.a), 0, 0, 0);
        },
        .remap_affine => {
            const a = ir.affines[@intCast(node.b)];
            try encodeNode(tape, ir, @intCast(a.m00.id), visited);
            try encodeNode(tape, ir, @intCast(a.m01.id), visited);
            try encodeNode(tape, ir, @intCast(a.m02.id), visited);
            try encodeNode(tape, ir, @intCast(a.m03.id), visited);
            try encodeNode(tape, ir, @intCast(a.m10.id), visited);
            try encodeNode(tape, ir, @intCast(a.m11.id), visited);
            try encodeNode(tape, ir, @intCast(a.m12.id), visited);
            try encodeNode(tape, ir, @intCast(a.m13.id), visited);
            try encodeNode(tape, ir, @intCast(a.m20.id), visited);
            try encodeNode(tape, ir, @intCast(a.m21.id), visited);
            try encodeNode(tape, ir, @intCast(a.m22.id), visited);
            try encodeNode(tape, ir, @intCast(a.m23.id), visited);
            try tape.emit(.enter_remap_affine, 0, 0, 0, 0, node.b);
            try encodeBody(tape, ir, @intCast(node.a), visited);
            try tape.emit(.exit_remap, node_id, @intCast(node.a), 0, 0, 0);
        },
        .intrinsic => {
            try tape.emit(.intrinsic, node_id, 0, 0, 0, node.a);
        },
    }
}

fn intervalMin(a: Interval, b: Interval) Interval {
    return eval.interval(if (a.lo < b.lo) a.lo else b.lo, if (a.hi < b.hi) a.hi else b.hi);
}

fn intervalMax(a: Interval, b: Interval) Interval {
    return eval.interval(if (a.lo > b.lo) a.lo else b.lo, if (a.hi > b.hi) a.hi else b.hi);
}

fn intervalSqrt(a: Interval) Interval {
    const lo = if (a.lo < 0.0) 0.0 else a.lo;
    const hi = if (a.hi < 0.0) 0.0 else a.hi;
    return eval.interval(@sqrt(lo), @sqrt(hi));
}

const FoldedOp = struct { op: Op, dst: u16, a: u16, b: u16, c: u16, aux: i32 };

pub fn simplifyTape(orig: *const RegTape, ir: *const MathIR, trace: []const Choice, out: *RegTape) !void {
    std.debug.assert(trace.len >= orig.instruction_count);

    var folded: [max_tape_words]FoldedOp = undefined;
    var fold_count: usize = 0;

    var ip: usize = 0;
    while (ip < orig.instruction_count) : (ip += 1) {
        const op: Op = @enumFromInt(orig.opcodes[ip]);
        const dst = orig.dst[ip];
        const a = orig.src_a[ip];
        const b = orig.src_b[ip];
        const c = orig.src_c[ip];
        const aux = orig.aux[ip];

        if (op == .binary) {
            const bin: Binary = @enumFromInt(aux);
            if (bin == .min or bin == .max) {
                switch (trace[ip]) {
                    .left => {
                        folded[fold_count] = .{ .op = .copy_slot, .dst = dst, .a = a, .b = 0, .c = 0, .aux = 0 };
                        fold_count += 1;
                        continue;
                    },
                    .right => {
                        folded[fold_count] = .{ .op = .copy_slot, .dst = dst, .a = b, .b = 0, .c = 0, .aux = 0 };
                        fold_count += 1;
                        continue;
                    },
                    .both, .none => {},
                }
            }
        }
        folded[fold_count] = .{ .op = op, .dst = dst, .a = a, .b = b, .c = c, .aux = aux };
        fold_count += 1;
    }

    var live: [max_nodes]bool = undefined;
    var keep: [max_tape_words]bool = undefined;
    @memset(live[0..orig.slot_count], false);
    @memset(keep[0..orig.instruction_count], false);

    var return_ip: ?usize = null;
    var i: usize = fold_count;
    while (i > 0) {
        i -= 1;
        if (folded[i].op == .return_) {
            return_ip = i;
            live[folded[i].a] = true;
            keep[i] = true;
            break;
        }
    }
    if (return_ip == null) return error.MissingReturn;

    var scope_active: [64]bool = [_]bool{false} ** 64;
    var depth: usize = 0;

    var j: usize = return_ip.?;
    while (j > 0) {
        j -= 1;
        const f = folded[j];
        switch (f.op) {
            .exit_remap => {
                if (depth >= 64) return error.RemapDepth;
                scope_active[depth] = live[f.dst];
                depth += 1;
                if (live[f.dst]) {
                    keep[j] = true;
                    live[f.a] = true;
                }
            },
            .enter_remap_axes => {
                if (depth == 0) return error.UnbalancedRemap;
                depth -= 1;
                if (scope_active[depth]) {
                    keep[j] = true;
                    live[f.a] = true;
                    live[f.b] = true;
                    live[f.c] = true;
                }
            },
            .enter_remap_affine => {
                if (depth == 0) return error.UnbalancedRemap;
                depth -= 1;
                if (scope_active[depth]) {
                    keep[j] = true;
                    const af = ir.affines[@intCast(f.aux)];
                    live[@intCast(af.m00.id)] = true;
                    live[@intCast(af.m01.id)] = true;
                    live[@intCast(af.m02.id)] = true;
                    live[@intCast(af.m03.id)] = true;
                    live[@intCast(af.m10.id)] = true;
                    live[@intCast(af.m11.id)] = true;
                    live[@intCast(af.m12.id)] = true;
                    live[@intCast(af.m13.id)] = true;
                    live[@intCast(af.m20.id)] = true;
                    live[@intCast(af.m21.id)] = true;
                    live[@intCast(af.m22.id)] = true;
                    live[@intCast(af.m23.id)] = true;
                }
            },
            .copy_slot, .unary => {
                if (live[f.dst]) {
                    keep[j] = true;
                    live[f.a] = true;
                }
            },
            .binary => {
                if (live[f.dst]) {
                    keep[j] = true;
                    live[f.a] = true;
                    live[f.b] = true;
                }
            },
            .load_x, .load_y, .load_z, .load_slot, .load_const, .intrinsic => {
                if (live[f.dst]) keep[j] = true;
            },
            .return_ => {},
        }
    }
    if (depth != 0) return error.UnbalancedRemap;

    out.instruction_count = 0;
    out.immediate_count = orig.immediate_count;
    @memcpy(out.immediates[0..orig.immediate_count], orig.immediates[0..orig.immediate_count]);
    out.slot_count = orig.slot_count;

    var w: usize = 0;
    while (w < fold_count) : (w += 1) {
        if (!keep[w]) continue;
        const f = folded[w];
        try out.emit(f.op, f.dst, f.a, f.b, f.c, f.aux);
    }
}

pub fn decodeRegEvalInterval(tape: *const RegTape, ir: *const MathIR, slots: []const f64, box: Box3) Interval {
    var values: [max_nodes]Interval = undefined;
    var axes: [64]Box3 = undefined;
    axes[0] = box;
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
            .load_x => values[dst] = eval.axisInterval(axes[ap - 1], 0),
            .load_y => values[dst] = eval.axisInterval(axes[ap - 1], 1),
            .load_z => values[dst] = eval.axisInterval(axes[ap - 1], 2),
            .load_slot => values[dst] = eval.singleton(eval.slotValue(slots, aux)),
            .load_const => values[dst] = eval.singleton(tape.immediates[@intCast(aux)]),
            .unary => {
                const av = values[a];
                const u: Unary = @enumFromInt(aux);
                values[dst] = switch (u) {
                    .neg => eval.ineg(av),
                    .abs => eval.iabs(av),
                    .recip => eval.idiv(eval.singleton(1.0), av),
                    .square => eval.isquare(av),
                    .sqrt => intervalSqrt(av),
                    else => eval.unknown(),
                };
            },
            .binary => {
                const av = values[a];
                const bv = values[b];
                const bin: Binary = @enumFromInt(aux);
                values[dst] = switch (bin) {
                    .add => eval.iadd(av, bv),
                    .sub => eval.isub(av, bv),
                    .mul => eval.imul(av, bv),
                    .div => eval.idiv(av, bv),
                    .min => intervalMin(av, bv),
                    .max => intervalMax(av, bv),
                    else => eval.unknown(),
                };
            },
            .enter_remap_axes => {
                axes[ap] = .{ .xi = values[a], .yi = values[b], .zi = values[c] };
                ap += 1;
            },
            .enter_remap_affine => {
                const af = ir.affines[@intCast(aux)];
                const xi = axes[ap - 1].xi;
                const yi = axes[ap - 1].yi;
                const zi = axes[ap - 1].zi;
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
                const new_xi = eval.iadd(eval.iadd(eval.iadd(eval.imul(m00, xi), eval.imul(m01, yi)), eval.imul(m02, zi)), m03);
                const new_yi = eval.iadd(eval.iadd(eval.iadd(eval.imul(m10, xi), eval.imul(m11, yi)), eval.imul(m12, zi)), m13);
                const new_zi = eval.iadd(eval.iadd(eval.iadd(eval.imul(m20, xi), eval.imul(m21, yi)), eval.imul(m22, zi)), m23);
                axes[ap] = .{ .xi = new_xi, .yi = new_yi, .zi = new_zi };
                ap += 1;
            },
            .exit_remap => {
                values[dst] = values[a];
                ap -= 1;
            },
            .intrinsic => {
                const intrinsic = ir.intrinsics[@intCast(aux)];
                if (intrinsic.kind == .curve_distance_along) {
                    values[dst] = eval.unknown();
                } else {
                    const center = eval.boxCenter(axes[ap - 1]);
                    const value = eval.evalIntrinsicPoint(ir, intrinsic, slots, center);
                    const radius = eval.planeBoxRadius(axes[ap - 1], intrinsic.plane);
                    values[dst] = eval.interval(value - radius, value + radius);
                }
            },
            .copy_slot => values[dst] = values[a],
            .return_ => return values[a],
        }
    }
    return eval.unknown();
}

pub fn decodeRegEvalIntervalWithTrace(tape: *const RegTape, ir: *const MathIR, slots: []const f64, box: Box3, trace: []Choice) Interval {
    std.debug.assert(trace.len >= tape.instruction_count);
    var values: [max_nodes]Interval = undefined;
    var axes: [64]Box3 = undefined;
    axes[0] = box;
    var ap: usize = 1;

    var ip: usize = 0;
    while (ip < tape.instruction_count) : (ip += 1) {
        trace[ip] = .none;
        const op: Op = @enumFromInt(tape.opcodes[ip]);
        const dst = tape.dst[ip];
        const a = tape.src_a[ip];
        const b = tape.src_b[ip];
        const c = tape.src_c[ip];
        const aux = tape.aux[ip];

        switch (op) {
            .load_x => values[dst] = eval.axisInterval(axes[ap - 1], 0),
            .load_y => values[dst] = eval.axisInterval(axes[ap - 1], 1),
            .load_z => values[dst] = eval.axisInterval(axes[ap - 1], 2),
            .load_slot => values[dst] = eval.singleton(eval.slotValue(slots, aux)),
            .load_const => values[dst] = eval.singleton(tape.immediates[@intCast(aux)]),
            .unary => {
                const av = values[a];
                const u: Unary = @enumFromInt(aux);
                values[dst] = switch (u) {
                    .neg => eval.ineg(av),
                    .abs => eval.iabs(av),
                    .recip => eval.idiv(eval.singleton(1.0), av),
                    .square => eval.isquare(av),
                    .sqrt => intervalSqrt(av),
                    else => eval.unknown(),
                };
            },
            .binary => {
                const av = values[a];
                const bv = values[b];
                const bin: Binary = @enumFromInt(aux);
                switch (bin) {
                    .add => values[dst] = eval.iadd(av, bv),
                    .sub => values[dst] = eval.isub(av, bv),
                    .mul => values[dst] = eval.imul(av, bv),
                    .div => values[dst] = eval.idiv(av, bv),
                    .min => {
                        if (av.hi <= bv.lo) {
                            trace[ip] = .left;
                        } else if (bv.hi <= av.lo) {
                            trace[ip] = .right;
                        } else {
                            trace[ip] = .both;
                        }
                        values[dst] = intervalMin(av, bv);
                    },
                    .max => {
                        if (av.lo >= bv.hi) {
                            trace[ip] = .left;
                        } else if (bv.lo >= av.hi) {
                            trace[ip] = .right;
                        } else {
                            trace[ip] = .both;
                        }
                        values[dst] = intervalMax(av, bv);
                    },
                    else => values[dst] = eval.unknown(),
                }
            },
            .enter_remap_axes => {
                axes[ap] = .{ .xi = values[a], .yi = values[b], .zi = values[c] };
                ap += 1;
            },
            .enter_remap_affine => {
                const af = ir.affines[@intCast(aux)];
                const xi = axes[ap - 1].xi;
                const yi = axes[ap - 1].yi;
                const zi = axes[ap - 1].zi;
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
                const new_xi = eval.iadd(eval.iadd(eval.iadd(eval.imul(m00, xi), eval.imul(m01, yi)), eval.imul(m02, zi)), m03);
                const new_yi = eval.iadd(eval.iadd(eval.iadd(eval.imul(m10, xi), eval.imul(m11, yi)), eval.imul(m12, zi)), m13);
                const new_zi = eval.iadd(eval.iadd(eval.iadd(eval.imul(m20, xi), eval.imul(m21, yi)), eval.imul(m22, zi)), m23);
                axes[ap] = .{ .xi = new_xi, .yi = new_yi, .zi = new_zi };
                ap += 1;
            },
            .exit_remap => {
                values[dst] = values[a];
                ap -= 1;
            },
            .intrinsic => {
                const intrinsic = ir.intrinsics[@intCast(aux)];
                if (intrinsic.kind == .curve_distance_along) {
                    values[dst] = eval.unknown();
                } else {
                    const center = eval.boxCenter(axes[ap - 1]);
                    const value = eval.evalIntrinsicPoint(ir, intrinsic, slots, center);
                    const radius = eval.planeBoxRadius(axes[ap - 1], intrinsic.plane);
                    values[dst] = eval.interval(value - radius, value + radius);
                }
            },
            .copy_slot => values[dst] = values[a],
            .return_ => return values[a],
        }
    }
    return eval.unknown();
}

pub const PointEvalScratch = struct {
    values: [max_nodes]f64 = undefined,
    axes: [64]Vec3 = undefined,
};

pub fn decodeRegEvalWith(tape: *const RegTape, ir: *const MathIR, slots: []const f64, p: Vec3, scratch: *PointEvalScratch) f64 {
    const values = &scratch.values;
    const axes = &scratch.axes;
    axes[0] = p;
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
            .load_x => values[dst] = axes[ap - 1].x,
            .load_y => values[dst] = axes[ap - 1].y,
            .load_z => values[dst] = axes[ap - 1].z,
            .load_slot => values[dst] = eval.slotValue(slots, aux),
            .load_const => values[dst] = tape.immediates[@intCast(aux)],
            .unary => values[dst] = eval.evalUnaryPoint(@enumFromInt(aux), values[a]),
            .binary => values[dst] = eval.evalBinaryPoint(@enumFromInt(aux), values[a], values[b]),
            .enter_remap_axes => {
                axes[ap] = .{ .x = values[a], .y = values[b], .z = values[c] };
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
                    .x = m00 * cur.x + m01 * cur.y + m02 * cur.z + m03,
                    .y = m10 * cur.x + m11 * cur.y + m12 * cur.z + m13,
                    .z = m20 * cur.x + m21 * cur.y + m22 * cur.z + m23,
                };
                ap += 1;
            },
            .exit_remap => {
                values[dst] = values[a];
                ap -= 1;
            },
            .intrinsic => {
                const intrinsic = ir.intrinsics[@intCast(aux)];
                values[dst] = eval.evalIntrinsicPoint(ir, intrinsic, slots, axes[ap - 1]);
            },
            .copy_slot => values[dst] = values[a],
            .return_ => return values[a],
        }
    }
    return math.nan(f64);
}

pub fn decodeRegEval(tape: *const RegTape, ir: *const MathIR, slots: []const f64, p: Vec3) f64 {
    var values: [max_nodes]f64 = undefined;
    var axes: [64]Vec3 = undefined;
    axes[0] = p;
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
            .load_x => values[dst] = axes[ap - 1].x,
            .load_y => values[dst] = axes[ap - 1].y,
            .load_z => values[dst] = axes[ap - 1].z,
            .load_slot => values[dst] = eval.slotValue(slots, aux),
            .load_const => values[dst] = tape.immediates[@intCast(aux)],
            .unary => values[dst] = eval.evalUnaryPoint(@enumFromInt(aux), values[a]),
            .binary => values[dst] = eval.evalBinaryPoint(@enumFromInt(aux), values[a], values[b]),
            .enter_remap_axes => {
                axes[ap] = .{ .x = values[a], .y = values[b], .z = values[c] };
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
                    .x = m00 * cur.x + m01 * cur.y + m02 * cur.z + m03,
                    .y = m10 * cur.x + m11 * cur.y + m12 * cur.z + m13,
                    .z = m20 * cur.x + m21 * cur.y + m22 * cur.z + m23,
                };
                ap += 1;
            },
            .exit_remap => {
                values[dst] = values[a];
                ap -= 1;
            },
            .intrinsic => {
                const intrinsic = ir.intrinsics[@intCast(aux)];
                values[dst] = eval.evalIntrinsicPoint(ir, intrinsic, slots, axes[ap - 1]);
            },
            .copy_slot => values[dst] = values[a],
            .return_ => return values[a],
        }
    }
    return math.nan(f64);
}
