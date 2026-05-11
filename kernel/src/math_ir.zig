pub const Axis = enum(i32) { x, y, z };
pub const Plane = enum(i32) { xy, xz, yz };

pub const Unary = enum(i32) {
    neg,
    abs,
    recip,
    square,
    sqrt,
    floor,
    ceil,
    round,
    sin,
    cos,
    tan,
    asin,
    acos,
    atan,
    exp,
    ln,
    not,
};

pub const Binary = enum(i32) {
    add,
    sub,
    mul,
    div,
    atan2,
    min,
    max,
    pow,
    compare,
    mod,
    and_,
    or_,
};

pub const NodeKind = enum(i32) {
    var_,
    slot,
    const_,
    unary,
    binary,
    remap_axes,
    remap_affine,
    intrinsic,
};

pub const PrimitiveKind = enum(i32) {
    line_segment,
    bezier_quadratic,
    bezier_cubic,
    circle,
    naca4,
    arc_center,
};

pub const IntrinsicKind = enum(i32) {
    sketch_distance,
    sketch_path,
    curve_distance_along,
};

pub const Expr = struct { id: i32 };
pub const Vec2 = struct { x: f64, y: f64 };
pub const Vec3 = struct { x: f64, y: f64, z: f64 };
pub const SlotPoint2 = struct { x: i32, y: i32 };
pub const Interval = struct { lo: f64, hi: f64 };
pub const Box3 = struct { xi: Interval, yi: Interval, zi: Interval };
pub const Cube = struct { center: Vec3, half_size: f64 };
pub const Node = struct {
    kind: NodeKind,
    op: i32 = 0,
    a: i32 = -1,
    b: i32 = -1,
    c: i32 = -1,
    d: i32 = -1,
    value: f64 = 0.0,
};

pub const Affine3 = struct {
    m00: Expr,
    m01: Expr,
    m02: Expr,
    m03: Expr,
    m10: Expr,
    m11: Expr,
    m12: Expr,
    m13: Expr,
    m20: Expr,
    m21: Expr,
    m22: Expr,
    m23: Expr,
};

pub const SketchPrimitive = struct {
    kind: PrimitiveKind,
    p0: SlotPoint2 = .{ .x = -1, .y = -1 },
    p1: SlotPoint2 = .{ .x = -1, .y = -1 },
    p2: SlotPoint2 = .{ .x = -1, .y = -1 },
    p3: SlotPoint2 = .{ .x = -1, .y = -1 },
    radius: i32 = -1,
    chord: i32 = -1,
    max_camber: i32 = -1,
    camber_pos: i32 = -1,
    thickness: i32 = -1,
    clockwise: bool = false,
};

pub const Intrinsic = struct {
    kind: IntrinsicKind,
    plane: Plane = .xy,
    primitive_start: i32 = -1,
    primitive_count: i32 = 0,
    closed: bool = false,
    flip: bool = false,
    ox: i32 = -1,
    oy: i32 = -1,
    oz: i32 = -1,
    ax: i32 = -1,
    ay: i32 = -1,
    az: i32 = -1,
};

pub const max_nodes = 4096;
pub const max_affines = 256;
pub const max_intrinsics = 512;
pub const max_primitives = 2048;
pub const max_tape_words = 4096;
pub const max_immediates = 512;

pub const MathIR = struct {
    nodes: [max_nodes]Node = undefined,
    node_count: usize = 0,
    affines: [max_affines]Affine3 = undefined,
    affine_count: usize = 0,
    intrinsics: [max_intrinsics]Intrinsic = undefined,
    intrinsic_count: usize = 0,
    primitives: [max_primitives]SketchPrimitive = undefined,
    primitive_count: usize = 0,

    pub fn node(self: *MathIR, n: Node) !Expr {
        if (self.node_count >= max_nodes) return error.NodeCapacity;
        const id = self.node_count;
        self.nodes[id] = n;
        self.node_count += 1;
        return .{ .id = @intCast(id) };
    }

    pub fn var_(self: *MathIR, axis: Axis) !Expr {
        return self.node(.{ .kind = .var_, .op = @intFromEnum(axis) });
    }

    pub fn x(self: *MathIR) !Expr {
        return self.var_(.x);
    }
    pub fn y(self: *MathIR) !Expr {
        return self.var_(.y);
    }
    pub fn z(self: *MathIR) !Expr {
        return self.var_(.z);
    }
    pub fn slot(self: *MathIR, id: i32) !Expr {
        return self.node(.{ .kind = .slot, .op = id });
    }
    pub fn constant(self: *MathIR, value: f64) !Expr {
        return self.node(.{ .kind = .const_, .value = value });
    }
    pub fn unary(self: *MathIR, op: Unary, a: Expr) !Expr {
        return self.node(.{ .kind = .unary, .op = @intFromEnum(op), .a = a.id });
    }
    pub fn binary(self: *MathIR, op: Binary, a: Expr, b: Expr) !Expr {
        return self.node(.{ .kind = .binary, .op = @intFromEnum(op), .a = a.id, .b = b.id });
    }
    pub fn remapAxes(self: *MathIR, target: Expr, x_expr: Expr, y_expr: Expr, z_expr: Expr) !Expr {
        return self.node(.{ .kind = .remap_axes, .a = target.id, .b = x_expr.id, .c = y_expr.id, .d = z_expr.id });
    }
    pub fn affine3(self: *MathIR, affine: Affine3) !i32 {
        if (self.affine_count >= max_affines) return error.AffineCapacity;
        const id = self.affine_count;
        self.affines[id] = affine;
        self.affine_count += 1;
        return @intCast(id);
    }
    pub fn remapAffine(self: *MathIR, target: Expr, affine: i32) !Expr {
        return self.node(.{ .kind = .remap_affine, .a = target.id, .b = affine });
    }
    pub fn point2(_: *MathIR, x_slot: i32, y_slot: i32) SlotPoint2 {
        return .{ .x = x_slot, .y = y_slot };
    }
    pub fn pushPrimitive(self: *MathIR, primitive: SketchPrimitive) !i32 {
        if (self.primitive_count >= max_primitives) return error.PrimitiveCapacity;
        const id = self.primitive_count;
        self.primitives[id] = primitive;
        self.primitive_count += 1;
        return @intCast(id);
    }
    pub fn lineSegment(self: *MathIR, start: SlotPoint2, stop: SlotPoint2) !i32 {
        return self.pushPrimitive(.{ .kind = .line_segment, .p0 = start, .p1 = stop });
    }
    pub fn bezierQuadratic(self: *MathIR, p0: SlotPoint2, p1: SlotPoint2, p2: SlotPoint2) !i32 {
        return self.pushPrimitive(.{ .kind = .bezier_quadratic, .p0 = p0, .p1 = p1, .p2 = p2 });
    }
    pub fn circle(self: *MathIR, center: SlotPoint2, radius: i32) !i32 {
        return self.pushPrimitive(.{ .kind = .circle, .p0 = center, .radius = radius });
    }
    pub fn sketchDistance(self: *MathIR, plane: Plane, primitive: i32) !Expr {
        if (self.intrinsic_count >= max_intrinsics) return error.IntrinsicCapacity;
        const id = self.intrinsic_count;
        self.intrinsics[id] = .{ .kind = .sketch_distance, .plane = plane, .primitive_start = primitive, .primitive_count = 1 };
        self.intrinsic_count += 1;
        return self.node(.{ .kind = .intrinsic, .a = @intCast(id) });
    }
    pub fn sketchPath(self: *MathIR, plane: Plane, primitive_start: i32, primitive_count_: i32, closed: bool, flip: bool) !Expr {
        if (self.intrinsic_count >= max_intrinsics) return error.IntrinsicCapacity;
        const id = self.intrinsic_count;
        self.intrinsics[id] = .{ .kind = .sketch_path, .plane = plane, .primitive_start = primitive_start, .primitive_count = primitive_count_, .closed = closed, .flip = flip };
        self.intrinsic_count += 1;
        return self.node(.{ .kind = .intrinsic, .a = @intCast(id) });
    }
    pub fn curveDistanceAlong(self: *MathIR, plane: Plane, primitive_start: i32, primitive_count_: i32, ax: Expr, ay: Expr, az: Expr, flip: bool) !Expr {
        if (self.intrinsic_count >= max_intrinsics) return error.IntrinsicCapacity;
        const id = self.intrinsic_count;
        self.intrinsics[id] = .{
            .kind = .curve_distance_along,
            .plane = plane,
            .primitive_start = primitive_start,
            .primitive_count = primitive_count_,
            .flip = flip,
            .ax = ax.id,
            .ay = ay.id,
            .az = az.id,
        };
        self.intrinsic_count += 1;
        return self.node(.{ .kind = .intrinsic, .a = @intCast(id) });
    }
};

pub inline fn expr(self: *MathIR, op: Binary, a: Expr, b: Expr) !Expr {
    return self.binary(op, a, b);
}
