const std = @import("std");

// Field IR — a high-level SDF tree, lowered to a tape by `lower.zig`.
//
// Mirrors the shape of the F# FieldIR in pointer_mk18/core/FieldIR.fs so
// a host can serialize its tree and hand it to Zig. Differences:
//
//   * No slot table. Every numeric parameter is a plain f32 inlined on
//     the node. The host resolves slots before sending.
//
//   * Tree nodes and sketch 2D primitives live in caller-owned slab
//     storage; `FieldBuilder` allocates into those slabs and returns
//     indices (`FieldNodeRef` / `SketchPrimRef`).

pub const FieldNodeRef = u32;
pub const SketchPrimRef = u32;

pub const Axis = enum(u8) { x, y, z };
pub const BooleanOp = enum(u8) { union_, subtract, intersect };
pub const UnaryOp = enum(u8) { thicken, shell };

pub const Primitive = union(enum) {
    sphere: struct { radius: f32 },
    cylinder: struct { radius: f32, height: f32 },
    box: struct { width: f32, height: f32, depth: f32 },
    half_plane: struct { axis: Axis, offset: f32, flip: bool },
};

pub const SketchPrimitive2d = union(enum) {
    line_segment: struct { start: [2]f32, end: [2]f32 },
    circle: struct { center: [2]f32, radius: f32 },
    arc_center: struct {
        start: [2]f32,
        end: [2]f32,
        center: [2]f32,
        clockwise: bool,
    },
};

pub const FieldNode = union(enum) {
    primitive: Primitive,
    translate: struct {
        x: f32,
        y: f32,
        z: f32,
        child: FieldNodeRef,
    },
    rotate: struct {
        ax: f32,
        ay: f32,
        az: f32,
        angle: f32,
        child: FieldNodeRef,
    },
    boolean: struct {
        op: BooleanOp,
        radius: f32,
        a: FieldNodeRef,
        b: FieldNodeRef,
    },
    unary: struct {
        op: UnaryOp,
        value: f32,
        child: FieldNodeRef,
    },
    sketch: struct {
        prims_first: SketchPrimRef,
        prims_len: u32,
        closed: bool,
        flip: bool,
    },
};

pub const FieldTree = struct {
    nodes: []const FieldNode,
    sketch_prims: []const SketchPrimitive2d,
    root: FieldNodeRef,
};

pub const FieldBuilder = struct {
    nodes: []FieldNode,
    node_count: u32 = 0,
    sketch_prims: []SketchPrimitive2d,
    sketch_prim_count: u32 = 0,

    pub fn init(
        nodes: []FieldNode,
        sketch_prims: []SketchPrimitive2d,
    ) FieldBuilder {
        return .{ .nodes = nodes, .sketch_prims = sketch_prims };
    }

    pub fn finalize(self: *FieldBuilder, root: FieldNodeRef) FieldTree {
        return .{
            .nodes = self.nodes[0..self.node_count],
            .sketch_prims = self.sketch_prims[0..self.sketch_prim_count],
            .root = root,
        };
    }

    fn pushNode(self: *FieldBuilder, n: FieldNode) FieldNodeRef {
        const idx = self.node_count;
        self.nodes[idx] = n;
        self.node_count += 1;
        return idx;
    }

    fn pushSketchPrim(self: *FieldBuilder, p: SketchPrimitive2d) SketchPrimRef {
        const idx = self.sketch_prim_count;
        self.sketch_prims[idx] = p;
        self.sketch_prim_count += 1;
        return idx;
    }

    // ── Primitive constructors ──────────────────────────────────────

    pub fn sphere(self: *FieldBuilder, radius: f32) FieldNodeRef {
        return self.pushNode(.{ .primitive = .{ .sphere = .{ .radius = radius } } });
    }

    pub fn cylinder(self: *FieldBuilder, radius: f32, height: f32) FieldNodeRef {
        return self.pushNode(.{ .primitive = .{ .cylinder = .{ .radius = radius, .height = height } } });
    }

    pub fn box(self: *FieldBuilder, w: f32, h: f32, d: f32) FieldNodeRef {
        return self.pushNode(.{ .primitive = .{ .box = .{ .width = w, .height = h, .depth = d } } });
    }

    pub fn halfPlane(self: *FieldBuilder, axis: Axis, offset: f32, flip: bool) FieldNodeRef {
        return self.pushNode(.{ .primitive = .{ .half_plane = .{ .axis = axis, .offset = offset, .flip = flip } } });
    }

    // ── Transforms ──────────────────────────────────────────────────

    pub fn translate(self: *FieldBuilder, x: f32, y: f32, z: f32, child: FieldNodeRef) FieldNodeRef {
        return self.pushNode(.{ .translate = .{ .x = x, .y = y, .z = z, .child = child } });
    }

    pub fn rotate(self: *FieldBuilder, ax: f32, ay: f32, az: f32, angle: f32, child: FieldNodeRef) FieldNodeRef {
        return self.pushNode(.{ .rotate = .{ .ax = ax, .ay = ay, .az = az, .angle = angle, .child = child } });
    }

    // ── Booleans ────────────────────────────────────────────────────
    // `radius` is the smooth-blend distance; pass 0 for a hard op.

    pub fn union_(self: *FieldBuilder, a: FieldNodeRef, b: FieldNodeRef, radius: f32) FieldNodeRef {
        return self.pushNode(.{ .boolean = .{ .op = .union_, .radius = radius, .a = a, .b = b } });
    }

    pub fn subtract(self: *FieldBuilder, a: FieldNodeRef, b: FieldNodeRef, radius: f32) FieldNodeRef {
        return self.pushNode(.{ .boolean = .{ .op = .subtract, .radius = radius, .a = a, .b = b } });
    }

    pub fn intersect(self: *FieldBuilder, a: FieldNodeRef, b: FieldNodeRef, radius: f32) FieldNodeRef {
        return self.pushNode(.{ .boolean = .{ .op = .intersect, .radius = radius, .a = a, .b = b } });
    }

    // ── Unary field ops ─────────────────────────────────────────────

    pub fn thicken(self: *FieldBuilder, amount: f32, child: FieldNodeRef) FieldNodeRef {
        return self.pushNode(.{ .unary = .{ .op = .thicken, .value = amount, .child = child } });
    }

    pub fn shell(self: *FieldBuilder, thickness: f32, child: FieldNodeRef) FieldNodeRef {
        return self.pushNode(.{ .unary = .{ .op = .shell, .value = thickness, .child = child } });
    }

    // ── Sketches ────────────────────────────────────────────────────
    // Build a sketch by pushing 2D primitives via the sketch-prim
    // helpers, then calling `sketch` with the indices.

    pub fn sketchLine(self: *FieldBuilder, start: [2]f32, end: [2]f32) SketchPrimRef {
        return self.pushSketchPrim(.{ .line_segment = .{ .start = start, .end = end } });
    }

    pub fn sketchCircle(self: *FieldBuilder, center: [2]f32, radius: f32) SketchPrimRef {
        return self.pushSketchPrim(.{ .circle = .{ .center = center, .radius = radius } });
    }

    pub fn sketchArc(
        self: *FieldBuilder,
        start: [2]f32,
        end: [2]f32,
        center: [2]f32,
        clockwise: bool,
    ) SketchPrimRef {
        return self.pushSketchPrim(.{ .arc_center = .{
            .start = start,
            .end = end,
            .center = center,
            .clockwise = clockwise,
        } });
    }

    // Group the most recently pushed N sketch primitives into a node.
    // Call the prim helpers first, note the returned indices, then call
    // this with the range [first, first+len).
    pub fn sketch(
        self: *FieldBuilder,
        prims_first: SketchPrimRef,
        prims_len: u32,
        closed: bool,
        flip: bool,
    ) FieldNodeRef {
        return self.pushNode(.{ .sketch = .{
            .prims_first = prims_first,
            .prims_len = prims_len,
            .closed = closed,
            .flip = flip,
        } });
    }
};
