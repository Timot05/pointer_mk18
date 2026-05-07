const std = @import("std");
const m = @import("math_domain.zig");

pub const Out = struct {
    io: std.Io,
    file: std.Io.File,

    pub fn writeAll(self: Out, slice: []const u8) !void {
        try self.file.writeStreamingAll(self.io, slice);
    }

    pub fn print(self: Out, comptime fmt: []const u8, args: anytype) !void {
        var buf: [1024]u8 = undefined;
        const slice = try std.fmt.bufPrint(&buf, fmt, args);
        try self.writeAll(slice);
    }
};

fn axisName(a: m.Axis) []const u8 {
    return switch (a) {
        .x => "x",
        .y => "y",
        .z => "z",
    };
}

fn unaryName(u: m.Unary) []const u8 {
    return switch (u) {
        .neg => "neg",
        .abs => "abs",
        .recip => "recip",
        .square => "square",
        .sqrt => "sqrt",
        .floor => "floor",
        .ceil => "ceil",
        .round => "round",
        .sin => "sin",
        .cos => "cos",
        .tan => "tan",
        .asin => "asin",
        .acos => "acos",
        .atan => "atan",
        .exp => "exp",
        .ln => "ln",
        .not => "not",
    };
}

fn binaryName(b: m.Binary) []const u8 {
    return switch (b) {
        .add => "add",
        .sub => "sub",
        .mul => "mul",
        .div => "div",
        .atan2 => "atan2",
        .min => "min",
        .max => "max",
        .pow => "pow",
        .compare => "compare",
        .mod => "mod",
        .and_ => "and",
        .or_ => "or",
    };
}

fn intrinsicName(k: m.IntrinsicKind) []const u8 {
    return switch (k) {
        .sketch_distance => "sketch_distance",
        .sketch_path => "sketch_path",
        .curve_distance_along => "curve_dist_along",
    };
}

fn primitiveName(k: m.PrimitiveKind) []const u8 {
    return switch (k) {
        .line_segment => "line",
        .bezier_quadratic => "bez_q",
        .bezier_cubic => "bez_c",
        .circle => "circle",
        .naca4 => "naca4",
        .arc_center => "arc",
    };
}

fn planeName(p: m.Plane) []const u8 {
    return switch (p) {
        .xy => "xy",
        .xz => "xz",
        .yz => "yz",
    };
}

pub fn writeDot(out: Out, ir: *const m.MathIR, root: m.Expr) !void {
    try out.writeAll("digraph IR {\n");
    try out.writeAll("  rankdir=BT;\n");
    try out.writeAll("  node [fontname=\"Menlo\", fontsize=10];\n");
    try out.writeAll("  edge [fontname=\"Menlo\", fontsize=9];\n");
    try out.writeAll("  compound=true;\n\n");

    const root_id: usize = @intCast(root.id);

    // Nodes
    var i: usize = 0;
    while (i < ir.node_count) : (i += 1) {
        try writeNode(out, ir, i, ir.nodes[i], root_id);
    }
    try out.writeAll("\n");

    // Edges between nodes
    i = 0;
    while (i < ir.node_count) : (i += 1) {
        try writeEdges(out, i, ir.nodes[i]);
    }
    try out.writeAll("\n");

    // Affine clusters
    var ai: usize = 0;
    while (ai < ir.affine_count) : (ai += 1) {
        try writeAffineCluster(out, ai, ir.affines[ai]);
    }

    // Intrinsic clusters
    var ii: usize = 0;
    while (ii < ir.intrinsic_count) : (ii += 1) {
        try writeIntrinsicCluster(out, ii, ir.intrinsics[ii], ir);
    }

    try out.writeAll("}\n");
}

fn writeNode(out: Out, ir: *const m.MathIR, id: usize, node: m.Node, root_id: usize) !void {
    const peri: u32 = if (id == root_id) 2 else 1;
    switch (node.kind) {
        .var_ => {
            const ax: m.Axis = @enumFromInt(node.op);
            try out.print("  n{d} [label=\"#{d} var {s}\", shape=ellipse, style=filled, fillcolor=\"#dddddd\", peripheries={d}];\n", .{ id, id, axisName(ax), peri });
        },
        .slot => {
            try out.print("  n{d} [label=\"#{d} slot[{d}]\", shape=ellipse, style=filled, fillcolor=\"#cccccc\", peripheries={d}];\n", .{ id, id, node.op, peri });
        },
        .const_ => {
            try out.print("  n{d} [label=\"#{d} const {d:.4}\", shape=ellipse, style=filled, fillcolor=\"#eeeeee\", peripheries={d}];\n", .{ id, id, node.value, peri });
        },
        .unary => {
            const u: m.Unary = @enumFromInt(node.op);
            try out.print("  n{d} [label=\"#{d} {s}\", shape=box, style=filled, fillcolor=\"#cce5ff\", peripheries={d}];\n", .{ id, id, unaryName(u), peri });
        },
        .binary => {
            const b: m.Binary = @enumFromInt(node.op);
            try out.print("  n{d} [label=\"#{d} {s}\", shape=box, style=filled, fillcolor=\"#bbe9ff\", peripheries={d}];\n", .{ id, id, binaryName(b), peri });
        },
        .remap_axes => {
            try out.print("  n{d} [label=\"#{d} remap_axes\", shape=box, style=\"filled,bold\", fillcolor=\"#fff2b3\", peripheries={d}];\n", .{ id, id, peri });
        },
        .remap_affine => {
            try out.print("  n{d} [label=\"#{d} remap_affine\\nA={d}\", shape=box, style=\"filled,bold\", fillcolor=\"#ffd966\", peripheries={d}];\n", .{ id, id, node.b, peri });
        },
        .intrinsic => {
            const intrinsic = ir.intrinsics[@intCast(node.a)];
            try out.print("  n{d} [label=\"#{d} {s}\\nI={d}\", shape=oval, style=filled, fillcolor=\"#c8e6c9\", peripheries={d}];\n", .{ id, id, intrinsicName(intrinsic.kind), node.a, peri });
        },
    }
}

fn writeEdges(out: Out, id: usize, node: m.Node) !void {
    switch (node.kind) {
        .var_, .slot, .const_ => {},
        .unary => try out.print("  n{d} -> n{d};\n", .{ id, node.a }),
        .binary => {
            try out.print("  n{d} -> n{d} [label=\"a\"];\n", .{ id, node.a });
            try out.print("  n{d} -> n{d} [label=\"b\"];\n", .{ id, node.b });
        },
        .remap_axes => {
            try out.print("  n{d} -> n{d} [label=\"target\"];\n", .{ id, node.a });
            try out.print("  n{d} -> n{d} [label=\"x'\"];\n", .{ id, node.b });
            try out.print("  n{d} -> n{d} [label=\"y'\"];\n", .{ id, node.c });
            try out.print("  n{d} -> n{d} [label=\"z'\"];\n", .{ id, node.d });
        },
        .remap_affine => {
            try out.print("  n{d} -> n{d} [label=\"target\"];\n", .{ id, node.a });
            try out.print("  n{d} -> aff{d} [style=dashed, color=goldenrod, label=\"affine\"];\n", .{ id, node.b });
        },
        .intrinsic => try out.print("  n{d} -> intr{d} [style=dashed, color=darkgreen, label=\"intrinsic\"];\n", .{ id, node.a }),
    }
}

fn writeAffineCluster(out: Out, id: usize, a: m.Affine3) !void {
    try out.print("  subgraph cluster_aff{d} {{\n", .{id});
    try out.print("    label=\"affine #{d}\"; style=dashed; color=goldenrod; bgcolor=\"#fffaf0\";\n", .{id});
    try out.print(
        "    aff{d} [shape=record, label=\"{{m00=#{d}|m01=#{d}|m02=#{d}|m03=#{d}}}|{{m10=#{d}|m11=#{d}|m12=#{d}|m13=#{d}}}|{{m20=#{d}|m21=#{d}|m22=#{d}|m23=#{d}}}\", fontsize=8];\n",
        .{
            id,
            a.m00.id, a.m01.id, a.m02.id, a.m03.id,
            a.m10.id, a.m11.id, a.m12.id, a.m13.id,
            a.m20.id, a.m21.id, a.m22.id, a.m23.id,
        },
    );
    try out.writeAll("  }\n");

    const ids = [_]i32{
        a.m00.id, a.m01.id, a.m02.id, a.m03.id,
        a.m10.id, a.m11.id, a.m12.id, a.m13.id,
        a.m20.id, a.m21.id, a.m22.id, a.m23.id,
    };
    var seen: [12]i32 = undefined;
    var seen_count: usize = 0;
    for (ids) |xid| {
        var dup = false;
        for (seen[0..seen_count]) |s| {
            if (s == xid) {
                dup = true;
                break;
            }
        }
        if (!dup) {
            seen[seen_count] = xid;
            seen_count += 1;
            try out.print("  aff{d} -> n{d} [style=dotted, color=\"#cccccc\"];\n", .{ id, xid });
        }
    }
}

fn writeIntrinsicCluster(out: Out, id: usize, intrinsic: m.Intrinsic, ir: *const m.MathIR) !void {
    try out.print("  subgraph cluster_intr{d} {{\n", .{id});
    try out.print("    label=\"intrinsic #{d} {s} plane={s}\"; style=dashed; color=darkgreen; bgcolor=\"#f0fff0\";\n", .{ id, intrinsicName(intrinsic.kind), planeName(intrinsic.plane) });
    try out.print(
        "    intr{d} [shape=record, label=\"{{kind={s}|plane={s}|prim_count={d}|closed={any}|flip={any}}}\", fontsize=8];\n",
        .{ id, intrinsicName(intrinsic.kind), planeName(intrinsic.plane), intrinsic.primitive_count, intrinsic.closed, intrinsic.flip },
    );

    var pi: i32 = 0;
    while (pi < intrinsic.primitive_count) : (pi += 1) {
        const prim_id: i32 = intrinsic.primitive_start + pi;
        const prim = ir.primitives[@intCast(prim_id)];
        try out.print(
            "    intr{d}_p{d} [shape=note, fontsize=8, label=\"{s} #{d}\\np0=s{d},s{d} p1=s{d},s{d} p2=s{d},s{d} r=s{d}\"];\n",
            .{ id, prim_id, primitiveName(prim.kind), prim_id, prim.p0.x, prim.p0.y, prim.p1.x, prim.p1.y, prim.p2.x, prim.p2.y, prim.radius },
        );
    }
    try out.writeAll("  }\n");
}

fn writeIndent(out: Out, depth: usize) !void {
    var i: usize = 0;
    while (i < depth) : (i += 1) try out.writeAll("  ");
}

pub fn writeTapeComments(out: Out, tape: *const m.RegTape, ir: *const m.MathIR) !void {
    try out.print("// RegTape: {d} instructions, {d} immediates, {d} slots\n", .{ tape.instruction_count, tape.immediate_count, tape.slot_count });
    var ip: usize = 0;
    var indent: usize = 0;
    while (ip < tape.instruction_count) : (ip += 1) {
        const op: m.RegOp = @enumFromInt(tape.opcodes[ip]);
        const dst = tape.dst[ip];
        const a = tape.src_a[ip];
        const b = tape.src_b[ip];
        const c = tape.src_c[ip];
        const aux = tape.aux[ip];

        if (op == .exit_remap and indent > 0) indent -= 1;

        try out.writeAll("// ");
        try writeIndent(out, indent);
        try out.print("{d}: ", .{ip});

        switch (op) {
            .load_x => try out.print("s{d} = load_x\n", .{dst}),
            .load_y => try out.print("s{d} = load_y\n", .{dst}),
            .load_z => try out.print("s{d} = load_z\n", .{dst}),
            .load_slot => try out.print("s{d} = load_slot[{d}]\n", .{ dst, aux }),
            .copy_slot => try out.print("s{d} = copy s{d}\n", .{ dst, a }),
            .load_const => try out.print("s{d} = const #{d} = {d:.6}\n", .{ dst, aux, tape.immediates[@intCast(aux)] }),
            .unary => try out.print("s{d} = {s}(s{d})\n", .{ dst, unaryName(@enumFromInt(aux)), a }),
            .binary => try out.print("s{d} = {s}(s{d}, s{d})\n", .{ dst, binaryName(@enumFromInt(aux)), a, b }),
            .enter_remap_axes => {
                try out.print("enter_remap_axes (x'=s{d}, y'=s{d}, z'=s{d})\n", .{ a, b, c });
                indent += 1;
            },
            .enter_remap_affine => {
                try out.print("enter_remap_affine A={d}\n", .{aux});
                indent += 1;
            },
            .exit_remap => try out.print("s{d} = exit_remap (body=s{d})\n", .{ dst, a }),
            .intrinsic => {
                const intrinsic = ir.intrinsics[@intCast(aux)];
                try out.print("s{d} = intrinsic[{d}] {s} plane={s}\n", .{ dst, aux, intrinsicName(intrinsic.kind), planeName(intrinsic.plane) });
            },
            .return_ => try out.print("return s{d}\n", .{a}),
        }
    }
    try out.writeAll("//\n");
}
