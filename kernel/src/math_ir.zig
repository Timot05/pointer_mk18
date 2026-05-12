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
    fold,
    // Sketch primitives as first-class subtree nodes. Each one's geometry
    // inputs are stored as child node refs in `NodeRefs[a..a+b]`. The
    // plane is packed in `op`: `op / 2` = plane (0=xy, 1=xz, 2=yz).
    // `arc_center` packs `clockwise` as the low bit of `op`.
    line_segment,
    circle,
    bezier_quadratic,
    bezier_cubic,
    arc_center,
};

/// Variadic-fold operator. Order must match `MathIr.FoldOp` on the F# side.
pub const FoldOp = enum(i32) {
    min,
    max,
    sum,
};

/// Kernel-level specialised evaluators. Packaging-only intrinsics
/// (formerly `sketch_distance` / `sketch_path`) were lowered to fold +
/// per-primitive subtree nodes; only `curve_distance_along` stays
/// intrinsic because its signed-distance-along-an-axis math is
/// genuinely specialised. Numeric values preserved for wire-format
/// compatibility: `sketch_distance=0`, `sketch_path=1`, `curve_distance_along=2`.
pub const IntrinsicKind = enum(i32) {
    curve_distance_along = 2,
};

pub const Expr = struct { id: i32 };
pub const Vec2 = struct { x: f64, y: f64 };
pub const Vec3 = struct { x: f64, y: f64, z: f64 };
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

/// `CurveDistanceAlong`'s primitive children live as a window in
/// `node_refs[primitive_start..primitive_start+primitive_count]`. The
/// names retain "primitive" for wire-format and historical continuity,
/// but they always reference primitive subtree node ids now — not rows
/// in the (retired) `Primitives` side table.
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
pub const max_node_refs = 8192;
pub const max_tape_words = 4096;
pub const max_immediates = 512;

pub const MathIR = struct {
    nodes: [max_nodes]Node = undefined,
    node_count: usize = 0,
    affines: [max_affines]Affine3 = undefined,
    affine_count: usize = 0,
    intrinsics: [max_intrinsics]Intrinsic = undefined,
    intrinsic_count: usize = 0,
    /// Packed child-id array. Used by Fold (`a`=start, `b`=count), the
    /// sketch-primitive node kinds, and `CurveDistanceAlong`'s
    /// primitive list (via `Intrinsic.primitive_start`/`primitive_count`).
    /// Replaces the legacy `primitives` side table.
    node_refs: [max_node_refs]i32 = undefined,
    node_ref_count: usize = 0,

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
    // The slot-indexed `point2` / `lineSegment` / `bezierQuadratic` /
    // `circle` builders and the legacy `sketchDistance` / `sketchPath`
    // intrinsics were retired in Phase 3. Use the `*N` builders below
    // for sketch primitives and `fold(.min, …)` for "min over a curve
    // list" — both emit pure subtree nodes.
    /// Variadic fold over `children`. Returns a `Fold` node whose `a`
    /// field is the start index of the children in `node_refs` and `b`
    /// is the count. `op` selects the fold operator (min/max/sum).
    pub fn fold(self: *MathIR, op: FoldOp, children: []const Expr) !Expr {
        if (self.node_ref_count + children.len > max_node_refs) return error.NodeRefCapacity;
        const start = self.node_ref_count;
        for (children) |child| {
            self.node_refs[self.node_ref_count] = child.id;
            self.node_ref_count += 1;
        }
        return self.node(.{ .kind = .fold, .op = @intFromEnum(op), .a = @intCast(start), .b = @intCast(children.len) });
    }

    /// Internal helper for primitive-as-node builders. Packs `children`
    /// into `node_refs` and pushes a node of `kind` with `op` carrying
    /// the plane (and any extra flag bits).
    fn primitiveNode(self: *MathIR, kind: NodeKind, plane: Plane, op_extra: i32, children: []const Expr) !Expr {
        if (self.node_ref_count + children.len > max_node_refs) return error.NodeRefCapacity;
        const start = self.node_ref_count;
        for (children) |child| {
            self.node_refs[self.node_ref_count] = child.id;
            self.node_ref_count += 1;
        }
        return self.node(.{
            .kind = kind,
            .op = @as(i32, @intFromEnum(plane)) * 2 + op_extra,
            .a = @intCast(start),
            .b = @intCast(children.len),
        });
    }

    pub fn lineSegmentN(self: *MathIR, plane: Plane, p0x: Expr, p0y: Expr, p1x: Expr, p1y: Expr) !Expr {
        return self.primitiveNode(.line_segment, plane, 0, &.{ p0x, p0y, p1x, p1y });
    }

    pub fn circleN(self: *MathIR, plane: Plane, cx: Expr, cy: Expr, r: Expr) !Expr {
        return self.primitiveNode(.circle, plane, 0, &.{ cx, cy, r });
    }

    pub fn bezierQuadraticN(self: *MathIR, plane: Plane, p0x: Expr, p0y: Expr, p1x: Expr, p1y: Expr, p2x: Expr, p2y: Expr) !Expr {
        return self.primitiveNode(.bezier_quadratic, plane, 0, &.{ p0x, p0y, p1x, p1y, p2x, p2y });
    }

    pub fn bezierCubicN(self: *MathIR, plane: Plane, p0x: Expr, p0y: Expr, p1x: Expr, p1y: Expr, p2x: Expr, p2y: Expr, p3x: Expr, p3y: Expr) !Expr {
        return self.primitiveNode(.bezier_cubic, plane, 0, &.{ p0x, p0y, p1x, p1y, p2x, p2y, p3x, p3y });
    }

    pub fn arcCenterN(self: *MathIR, plane: Plane, sx: Expr, sy: Expr, ex: Expr, ey: Expr, cx: Expr, cy: Expr, clockwise: bool) !Expr {
        const extra: i32 = if (clockwise) 1 else 0;
        return self.primitiveNode(.arc_center, plane, extra, &.{ sx, sy, ex, ey, cx, cy });
    }

    pub fn curveDistanceAlong(self: *MathIR, plane: Plane, primitives: []const Expr, ax: Expr, ay: Expr, az: Expr, flip: bool) !Expr {
        if (self.intrinsic_count >= max_intrinsics) return error.IntrinsicCapacity;
        if (self.node_ref_count + primitives.len > max_node_refs) return error.NodeRefCapacity;
        const ref_start = self.node_ref_count;
        for (primitives) |p| {
            self.node_refs[self.node_ref_count] = p.id;
            self.node_ref_count += 1;
        }
        const id = self.intrinsic_count;
        self.intrinsics[id] = .{
            .kind = .curve_distance_along,
            .plane = plane,
            .primitive_start = @intCast(ref_start),
            .primitive_count = @intCast(primitives.len),
            .flip = flip,
            .ax = ax.id,
            .ay = ay.id,
            .az = az.id,
        };
        self.intrinsic_count += 1;
        return self.node(.{ .kind = .intrinsic, .a = @intCast(id) });
    }
};
