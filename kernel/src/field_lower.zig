// Lower mk18's high-level Field IR (sphere/cylinder/box/halfPlane,
// translate/rotate, smooth booleans, thicken/shell, sketch with
// line/circle/arc) into mk21's low-level `MathIR` expression DAG.
//
// Direct port of `pointer_mk18/kernel/src/lower.zig`, retargeted from
// mk18's `TapeBuilder` (NodeRef) to mk21's `MathIR` (`Expr`). Same
// algorithm, same numeric constants, same smooth-min formula
// (iquilezles polynomial with k = world distance).
//
// Camera wrapping is NOT this file's concern — callers compose with
// `m.wrapWithCameraFrame` and `m.MutableCamera.bind` separately.

const std = @import("std");
const m = @import("math_domain.zig");
const scene_decode = @import("scene_decode.zig");

const ParsedScene = scene_decode.ParsedScene;
const Node = scene_decode.Node;
const Primitive = scene_decode.Primitive;
const SketchPrimitive2d = scene_decode.SketchPrimitive2d;
const Axis = scene_decode.Axis;
const BooleanOp = scene_decode.BooleanOp;
const UnaryOp = scene_decode.UnaryOp;
const NodeRef = scene_decode.NodeRef;

pub const Error = error{
    InvalidRoot,
    InvalidChildRef,
    InvalidSketchRange,
    EmptySketch,
    NodeCapacity,
    AffineCapacity,
    IntrinsicCapacity,
    PrimitiveCapacity,
};

const Coords = struct {
    x: m.Expr,
    y: m.Expr,
    z: m.Expr,
};

// ── Local builder shorthands ────────────────────────────────────────────
// Cuts the `try ir.binary(.add, a, b)` boilerplate down to `try add(ir, a, b)`.

inline fn add(ir: *m.MathIR, a: m.Expr, b: m.Expr) !m.Expr {
    return ir.binary(.add, a, b);
}
inline fn sub_(ir: *m.MathIR, a: m.Expr, b: m.Expr) !m.Expr {
    return ir.binary(.sub, a, b);
}
inline fn mul(ir: *m.MathIR, a: m.Expr, b: m.Expr) !m.Expr {
    return ir.binary(.mul, a, b);
}
inline fn divv(ir: *m.MathIR, a: m.Expr, b: m.Expr) !m.Expr {
    return ir.binary(.div, a, b);
}
inline fn minOp(ir: *m.MathIR, a: m.Expr, b: m.Expr) !m.Expr {
    return ir.binary(.min, a, b);
}
inline fn maxOp(ir: *m.MathIR, a: m.Expr, b: m.Expr) !m.Expr {
    return ir.binary(.max, a, b);
}
inline fn atan2Op(ir: *m.MathIR, a: m.Expr, b: m.Expr) !m.Expr {
    return ir.binary(.atan2, a, b);
}
inline fn neg(ir: *m.MathIR, a: m.Expr) !m.Expr {
    return ir.unary(.neg, a);
}
inline fn absOp(ir: *m.MathIR, a: m.Expr) !m.Expr {
    return ir.unary(.abs, a);
}
inline fn sqrtOp(ir: *m.MathIR, a: m.Expr) !m.Expr {
    return ir.unary(.sqrt, a);
}
inline fn square(ir: *m.MathIR, a: m.Expr) !m.Expr {
    return ir.unary(.square, a);
}
inline fn k(ir: *m.MathIR, v: f32) !m.Expr {
    return ir.constant(@as(f64, v));
}
inline fn kd(ir: *m.MathIR, v: f64) !m.Expr {
    return ir.constant(v);
}

// ── Public entry ────────────────────────────────────────────────────────

pub fn lower(scene: *const ParsedScene, ir: *m.MathIR) Error!m.Expr {
    if (scene.root >= scene.nodes.len) return Error.InvalidRoot;
    const coords: Coords = .{
        .x = try ir.x(),
        .y = try ir.y(),
        .z = try ir.z(),
    };
    return lowerNode(scene, ir, scene.root, coords);
}

fn lowerNode(scene: *const ParsedScene, ir: *m.MathIR, ref: NodeRef, coords: Coords) Error!m.Expr {
    if (ref >= scene.nodes.len) return Error.InvalidChildRef;
    return switch (scene.nodes[ref]) {
        .primitive => |prim| lowerPrimitive(ir, coords, prim),
        .translate => |tr| blk: {
            const child_coords: Coords = .{
                .x = try sub_(ir, coords.x, try k(ir, tr.x)),
                .y = try sub_(ir, coords.y, try k(ir, tr.y)),
                .z = try sub_(ir, coords.z, try k(ir, tr.z)),
            };
            break :blk try lowerNode(scene, ir, tr.child, child_coords);
        },
        .rotate => |rot| blk: {
            const child_coords = try rotateAxisAngleInv(ir, coords, rot.ax, rot.ay, rot.az, rot.angle);
            break :blk try lowerNode(scene, ir, rot.child, child_coords);
        },
        .boolean => |bn| blk: {
            const a = try lowerNode(scene, ir, bn.a, coords);
            const b = try lowerNode(scene, ir, bn.b, coords);
            break :blk try lowerBoolean(ir, bn.op, bn.radius, a, b);
        },
        .unary => |un| blk: {
            const child = try lowerNode(scene, ir, un.child, coords);
            break :blk try lowerUnary(ir, un.op, un.value, child);
        },
        .sketch => |sk| try lowerSketch(scene, ir, coords, sk.prims_first, sk.prims_len, sk.closed, sk.flip),
    };
}

fn lowerPrimitive(ir: *m.MathIR, coords: Coords, prim: Primitive) Error!m.Expr {
    return switch (prim) {
        .sphere => |s| sdfSphere(ir, coords.x, coords.y, coords.z, s.radius),
        .cylinder => |c| sdfCylinder(ir, coords.x, coords.y, coords.z, c.radius, c.height),
        .box => |b| sdfBox(ir, coords.x, coords.y, coords.z, b.width, b.height, b.depth),
        .half_plane => |hp| sdfHalfPlane(ir, coords, hp.axis, hp.offset, hp.flip),
    };
}

fn lowerBoolean(ir: *m.MathIR, op: BooleanOp, radius: f32, a: m.Expr, b: m.Expr) Error!m.Expr {
    return switch (op) {
        .union_ => smoothMin(ir, a, b, radius),
        .intersect => neg(ir, try smoothMin(ir, try neg(ir, a), try neg(ir, b), radius)),
        .subtract => neg(ir, try smoothMin(ir, try neg(ir, a), b, radius)),
    };
}

fn lowerUnary(ir: *m.MathIR, op: UnaryOp, value: f32, child: m.Expr) Error!m.Expr {
    return switch (op) {
        .thicken => sub_(ir, child, try k(ir, value)),
        .shell => maxOp(ir, child, try neg(ir, try add(ir, child, try k(ir, value)))),
    };
}

// ── Sketch ──────────────────────────────────────────────────────────────

fn lowerSketch(
    scene: *const ParsedScene,
    ir: *m.MathIR,
    coords: Coords,
    first: u32,
    len: u32,
    closed: bool,
    flip: bool,
) Error!m.Expr {
    const end = @as(u64, first) + @as(u64, len);
    if (end > scene.prims.len) return Error.InvalidSketchRange;
    if (len == 0) return Error.EmptySketch;

    const prims = scene.prims[first..@intCast(end)];

    var min_d = try lowerOpenSketchPrim(ir, coords.x, coords.y, prims[0]);
    for (prims[1..]) |prim| {
        const d = try lowerOpenSketchPrim(ir, coords.x, coords.y, prim);
        min_d = try minOp(ir, min_d, d);
    }

    if (!closed) return min_d;

    if (try tryLowerConvexPolygon(ir, coords.x, coords.y, prims, flip)) |d| {
        return d;
    }

    var sign = try kd(ir, 1.0);
    for (prims) |prim| {
        const f = try crossingSignFactor(ir, coords.x, coords.y, prim);
        sign = try mul(ir, sign, f);
    }
    var signed = try mul(ir, sign, min_d);
    if (flip) signed = try neg(ir, signed);
    return signed;
}

const MAX_CONVEX_VERTS: usize = 64;

fn tryLowerConvexPolygon(
    ir: *m.MathIR,
    x: m.Expr,
    y: m.Expr,
    prims: []const SketchPrimitive2d,
    flip: bool,
) Error!?m.Expr {
    if (prims.len < 3 or prims.len > MAX_CONVEX_VERTS) return null;

    var verts: [MAX_CONVEX_VERTS][2]f32 = undefined;
    for (prims, 0..) |p, i| {
        const seg = switch (p) {
            .line_segment => |s| s,
            else => return null,
        };
        verts[i] = seg.start;
        const next = prims[(i + 1) % prims.len];
        const next_start = switch (next) {
            .line_segment => |s| s.start,
            else => return null,
        };
        if (@abs(seg.end[0] - next_start[0]) > 1e-4 or
            @abs(seg.end[1] - next_start[1]) > 1e-4) return null;
    }

    const n = prims.len;

    var area2: f32 = 0;
    var i: usize = 0;
    while (i < n) : (i += 1) {
        const a = verts[i];
        const b = verts[(i + 1) % n];
        area2 += a[0] * b[1] - b[0] * a[1];
    }
    if (@abs(area2) < 1e-10) return null;

    const orient: f32 = if (area2 > 0) 1.0 else -1.0;
    i = 0;
    while (i < n) : (i += 1) {
        const a = verts[i];
        const b = verts[(i + 1) % n];
        const c = verts[(i + 2) % n];
        const cz = (b[0] - a[0]) * (c[1] - b[1]) - (b[1] - a[1]) * (c[0] - b[0]);
        if (cz * orient < -1e-6) return null;
    }

    var result: ?m.Expr = null;
    i = 0;
    while (i < n) : (i += 1) {
        const a = verts[i];
        const b = verts[(i + 1) % n];
        const dx = b[0] - a[0];
        const dy = b[1] - a[1];
        const len_e = @sqrt(dx * dx + dy * dy);
        if (len_e < 1e-9) continue;
        const nx = (orient * dy) / len_e;
        const ny = (-orient * dx) / len_e;
        const rel_x = try sub_(ir, x, try k(ir, a[0]));
        const rel_y = try sub_(ir, y, try k(ir, a[1]));
        const d = try add(
            ir,
            try mul(ir, rel_x, try k(ir, nx)),
            try mul(ir, rel_y, try k(ir, ny)),
        );
        result = if (result) |r| try maxOp(ir, r, d) else d;
    }

    if (result) |r| {
        return if (flip) try neg(ir, r) else r;
    }
    return null;
}

fn lowerOpenSketchPrim(ir: *m.MathIR, x: m.Expr, y: m.Expr, prim: SketchPrimitive2d) Error!m.Expr {
    return switch (prim) {
        .line_segment => |seg| sdfLineSegment2d(ir, x, y, seg.start, seg.end),
        .circle => |c| sdfCircleCurve2d(ir, x, y, c.center, c.radius),
        .arc_center => |arc| sdfArcCurve2d(ir, x, y, arc.start, arc.end, arc.center, arc.clockwise),
    };
}

// ── Closed-polygon sign via horizontal-ray crossings ────────────────────

const STEP_SHARPNESS: f32 = 1.0e6;

fn step(ir: *m.MathIR, v: m.Expr) Error!m.Expr {
    const ramp = try add(ir, try mul(ir, try k(ir, STEP_SHARPNESS), v), try k(ir, 0.5));
    return minOp(ir, try k(ir, 1.0), try maxOp(ir, try k(ir, 0.0), ramp));
}

fn crossingFactor(ir: *m.MathIR, c: m.Expr) Error!m.Expr {
    return sub_(ir, try k(ir, 1.0), try mul(ir, try k(ir, 2.0), c));
}

fn crossingSignFactor(ir: *m.MathIR, x: m.Expr, y: m.Expr, prim: SketchPrimitive2d) Error!m.Expr {
    return switch (prim) {
        .line_segment => |seg| lineCrossingFactor(ir, x, y, seg.start, seg.end),
        .circle => |c| circleCrossingFactor(ir, x, y, c.center, c.radius),
        .arc_center => |arc| arcCrossingFactor(ir, x, y, arc.start, arc.end, arc.center, arc.clockwise),
    };
}

fn lineCrossingFactor(ir: *m.MathIR, x: m.Expr, y: m.Expr, a: [2]f32, b: [2]f32) Error!m.Expr {
    const ax = try k(ir, a[0]);
    const ay = try k(ir, a[1]);
    const bx = try k(ir, b[0]);
    const by = try k(ir, b[1]);

    const s1 = try sub_(ir, ay, y);
    const s2 = try sub_(ir, by, y);
    const straddles = try step(ir, try neg(ir, try mul(ir, s1, s2)));

    const ax_minus_px = try sub_(ir, ax, x);
    const bx_minus_px = try sub_(ir, bx, x);
    const cross_pab = try sub_(
        ir,
        try mul(ir, ax_minus_px, s2),
        try mul(ir, bx_minus_px, s1),
    );
    const dy = try sub_(ir, s2, s1);
    const to_right = try step(ir, try mul(ir, cross_pab, dy));

    const crossing = try mul(ir, straddles, to_right);
    return crossingFactor(ir, crossing);
}

fn circleCrossingFactor(ir: *m.MathIR, x: m.Expr, y: m.Expr, center: [2]f32, radius: f32) Error!m.Expr {
    const cx = try k(ir, center[0]);
    const cy = try k(ir, center[1]);
    const r2 = try k(ir, radius * radius);

    const dy = try sub_(ir, y, cy);
    const disc = try sub_(ir, r2, try square(ir, dy));
    const has = try step(ir, disc);

    const disc_safe = try maxOp(ir, disc, try k(ir, 0.0));
    const h = try sqrtOp(ir, disc_safe);

    const x_left = try sub_(ir, cx, h);
    const x_right = try add(ir, cx, h);
    const c_left = try mul(ir, has, try step(ir, try sub_(ir, x_left, x)));
    const c_right = try mul(ir, has, try step(ir, try sub_(ir, x_right, x)));

    return mul(
        ir,
        try crossingFactor(ir, c_left),
        try crossingFactor(ir, c_right),
    );
}

fn arcCrossingFactor(
    ir: *m.MathIR,
    x: m.Expr,
    y: m.Expr,
    start: [2]f32,
    end_: [2]f32,
    center: [2]f32,
    clockwise: bool,
) Error!m.Expr {
    const sx = start[0] - center[0];
    const sy = start[1] - center[1];
    const ex = end_[0] - center[0];
    const ey = end_[1] - center[1];
    const radius = @sqrt(sx * sx + sy * sy);
    if (radius < 1e-6) {
        return lineCrossingFactor(ir, x, y, start, end_);
    }

    const start_angle = std.math.atan2(sy, sx);
    const end_angle = std.math.atan2(ey, ex);
    const sweep = if (clockwise)
        -positiveAngleDeltaConst(end_angle, start_angle)
    else
        positiveAngleDeltaConst(start_angle, end_angle);
    const center_angle = start_angle + sweep * 0.5;
    const half_angle = @abs(sweep) * 0.5;
    const cv_x = @cos(center_angle);
    const cv_y = @sin(center_angle);

    const cx = try k(ir, center[0]);
    const cy = try k(ir, center[1]);
    const r2 = try k(ir, radius * radius);

    const dy = try sub_(ir, y, cy);
    const disc = try sub_(ir, r2, try square(ir, dy));
    const has = try step(ir, disc);

    const disc_safe = try maxOp(ir, disc, try k(ir, 0.0));
    const h = try sqrtOp(ir, disc_safe);

    const x_left = try sub_(ir, cx, h);
    const x_right = try add(ir, cx, h);

    const c_left = try combineArcCrossing(ir, x, y, x_left, has, cx, cy, cv_x, cv_y, half_angle);
    const c_right = try combineArcCrossing(ir, x, y, x_right, has, cx, cy, cv_x, cv_y, half_angle);

    return mul(
        ir,
        try crossingFactor(ir, c_left),
        try crossingFactor(ir, c_right),
    );
}

fn combineArcCrossing(
    ir: *m.MathIR,
    x: m.Expr,
    y: m.Expr,
    candidate_x: m.Expr,
    has: m.Expr,
    cx: m.Expr,
    cy: m.Expr,
    cv_x: f32,
    cv_y: f32,
    half_angle: f32,
) Error!m.Expr {
    const qx = try sub_(ir, candidate_x, cx);
    const qy = try sub_(ir, y, cy);
    const cross_2d = try sub_(
        ir,
        try mul(ir, try k(ir, cv_x), qy),
        try mul(ir, try k(ir, cv_y), qx),
    );
    const dot_v = try add(
        ir,
        try mul(ir, try k(ir, cv_x), qx),
        try mul(ir, try k(ir, cv_y), qy),
    );
    const ang_diff = try atan2Op(ir, cross_2d, dot_v);
    const in_arc = try step(
        ir,
        try sub_(ir, try k(ir, half_angle), try absOp(ir, ang_diff)),
    );
    const to_right = try step(ir, try sub_(ir, candidate_x, x));
    return mul(ir, try mul(ir, has, to_right), in_arc);
}

// ── Primitive SDFs ──────────────────────────────────────────────────────

fn sdfSphere(ir: *m.MathIR, x: m.Expr, y: m.Expr, z: m.Expr, radius: f32) Error!m.Expr {
    const sum_sq = try add(
        ir,
        try add(ir, try square(ir, x), try square(ir, y)),
        try square(ir, z),
    );
    return sub_(ir, try sqrtOp(ir, sum_sq), try k(ir, radius));
}

fn sdfCylinder(ir: *m.MathIR, x: m.Expr, y: m.Expr, z: m.Expr, radius: f32, height: f32) Error!m.Expr {
    const radial = try sub_(
        ir,
        try sqrtOp(ir, try add(ir, try square(ir, x), try square(ir, y))),
        try k(ir, radius),
    );
    const axial = try sub_(ir, try absOp(ir, z), try k(ir, height * 0.5));
    return maxOp(ir, radial, axial);
}

fn sdfBox(ir: *m.MathIR, x: m.Expr, y: m.Expr, z: m.Expr, width: f32, height: f32, depth: f32) Error!m.Expr {
    const qx = try sub_(ir, try absOp(ir, x), try k(ir, width * 0.5));
    const qy = try sub_(ir, try absOp(ir, y), try k(ir, height * 0.5));
    const qz = try sub_(ir, try absOp(ir, z), try k(ir, depth * 0.5));
    return maxOp(ir, qx, try maxOp(ir, qy, qz));
}

fn sdfHalfPlane(ir: *m.MathIR, coords: Coords, axis: Axis, offset: f32, flip: bool) Error!m.Expr {
    const coord = switch (axis) {
        .x => coords.x,
        .y => coords.y,
        .z => coords.z,
    };
    var d = try sub_(ir, coord, try k(ir, offset));
    if (flip) d = try neg(ir, d);
    return d;
}

// ── Rotate (inverse axis-angle, baked at lower time) ────────────────────

fn rotateAxisAngleInv(ir: *m.MathIR, coords: Coords, ax: f32, ay: f32, az: f32, angle: f32) Error!Coords {
    const axis_len = @sqrt(ax * ax + ay * ay + az * az);
    if (axis_len <= 1e-6) return coords;

    const ux = ax / axis_len;
    const uy = ay / axis_len;
    const uz = az / axis_len;
    const c = @cos(-angle);
    const s = @sin(-angle);
    const one_minus_c = 1.0 - c;

    const dot = try add(
        ir,
        try add(
            ir,
            try mul(ir, try k(ir, ux), coords.x),
            try mul(ir, try k(ir, uy), coords.y),
        ),
        try mul(ir, try k(ir, uz), coords.z),
    );

    const cross_x = try sub_(
        ir,
        try mul(ir, try k(ir, uy), coords.z),
        try mul(ir, try k(ir, uz), coords.y),
    );
    const cross_y = try sub_(
        ir,
        try mul(ir, try k(ir, uz), coords.x),
        try mul(ir, try k(ir, ux), coords.z),
    );
    const cross_z = try sub_(
        ir,
        try mul(ir, try k(ir, ux), coords.y),
        try mul(ir, try k(ir, uy), coords.x),
    );

    return .{
        .x = try add(
            ir,
            try add(
                ir,
                try mul(ir, coords.x, try k(ir, c)),
                try mul(ir, cross_x, try k(ir, s)),
            ),
            try mul(ir, try k(ir, ux * one_minus_c), dot),
        ),
        .y = try add(
            ir,
            try add(
                ir,
                try mul(ir, coords.y, try k(ir, c)),
                try mul(ir, cross_y, try k(ir, s)),
            ),
            try mul(ir, try k(ir, uy * one_minus_c), dot),
        ),
        .z = try add(
            ir,
            try add(
                ir,
                try mul(ir, coords.z, try k(ir, c)),
                try mul(ir, cross_z, try k(ir, s)),
            ),
            try mul(ir, try k(ir, uz * one_minus_c), dot),
        ),
    };
}

// ── iquilezles polynomial smooth-min, k = world-distance blend radius ───

fn smoothMin(ir: *m.MathIR, a: m.Expr, b: m.Expr, k_radius: f32) Error!m.Expr {
    if (k_radius <= 1e-6) return minOp(ir, a, b);

    const diff = try sub_(ir, a, b);
    const h = try divv(
        ir,
        try maxOp(ir, try sub_(ir, try k(ir, k_radius), try absOp(ir, diff)), try k(ir, 0)),
        try k(ir, k_radius),
    );
    const cubic = try mul(ir, try square(ir, h), h);
    return sub_(ir, try minOp(ir, a, b), try mul(ir, cubic, try k(ir, k_radius / 6.0)));
}

// ── 2D sketch helpers ───────────────────────────────────────────────────

fn sdfLineSegment2d(ir: *m.MathIR, x: m.Expr, y: m.Expr, start: [2]f32, end_: [2]f32) Error!m.Expr {
    const ex = end_[0] - start[0];
    const ey = end_[1] - start[1];
    const l = ex * ex + ey * ey + 1e-20;
    const wx = try sub_(ir, x, try k(ir, start[0]));
    const wy = try sub_(ir, y, try k(ir, start[1]));
    const t = try minOp(
        ir,
        try maxOp(
            ir,
            try divv(
                ir,
                try add(
                    ir,
                    try mul(ir, wx, try k(ir, ex)),
                    try mul(ir, wy, try k(ir, ey)),
                ),
                try k(ir, l),
            ),
            try k(ir, 0),
        ),
        try k(ir, 1),
    );
    const dx = try sub_(ir, wx, try mul(ir, try k(ir, ex), t));
    const dy = try sub_(ir, wy, try mul(ir, try k(ir, ey), t));
    return sqrtOp(ir, try add(ir, try square(ir, dx), try square(ir, dy)));
}

fn sdfCircleCurve2d(ir: *m.MathIR, x: m.Expr, y: m.Expr, center: [2]f32, radius: f32) Error!m.Expr {
    return absOp(ir, try sdfCircle2d(ir, x, y, center, radius));
}

fn sdfCircle2d(ir: *m.MathIR, x: m.Expr, y: m.Expr, center: [2]f32, radius: f32) Error!m.Expr {
    const dx = try sub_(ir, x, try k(ir, center[0]));
    const dy = try sub_(ir, y, try k(ir, center[1]));
    return sub_(
        ir,
        try sqrtOp(ir, try add(ir, try square(ir, dx), try square(ir, dy))),
        try k(ir, radius),
    );
}

fn sdfArcCurve2d(
    ir: *m.MathIR,
    x: m.Expr,
    y: m.Expr,
    start: [2]f32,
    end_: [2]f32,
    center: [2]f32,
    clockwise: bool,
) Error!m.Expr {
    const sx = start[0] - center[0];
    const sy = start[1] - center[1];
    const ex = end_[0] - center[0];
    const ey = end_[1] - center[1];
    const radius = @sqrt(sx * sx + sy * sy);
    if (radius < 1e-6) return sdfLineSegment2d(ir, x, y, start, end_);

    const qx = try sub_(ir, x, try k(ir, center[0]));
    const qy = try sub_(ir, y, try k(ir, center[1]));
    const start_angle = std.math.atan2(sy, sx);
    const end_angle = std.math.atan2(ey, ex);
    const sweep = if (clockwise)
        -positiveAngleDeltaConst(end_angle, start_angle)
    else
        positiveAngleDeltaConst(start_angle, end_angle);
    const center_angle = start_angle + sweep * 0.5;
    const half_angle = @abs(sweep) * 0.5;
    const cv_x = @cos(center_angle);
    const cv_y = @sin(center_angle);

    const cross_2d = try sub_(
        ir,
        try mul(ir, try k(ir, cv_x), qy),
        try mul(ir, try k(ir, cv_y), qx),
    );
    const dot_v = try add(
        ir,
        try mul(ir, try k(ir, cv_x), qx),
        try mul(ir, try k(ir, cv_y), qy),
    );
    const ang_diff = try atan2Op(ir, cross_2d, dot_v);
    const score = try sub_(ir, try k(ir, half_angle), try absOp(ir, ang_diff));

    const radial = try absOp(
        ir,
        try sub_(
            ir,
            try sqrtOp(ir, try add(ir, try square(ir, qx), try square(ir, qy))),
            try k(ir, radius),
        ),
    );
    const d_start = try pointDist2d(ir, x, y, start);
    const d_end = try pointDist2d(ir, x, y, end_);
    const endpoint = try minOp(ir, d_start, d_end);

    const penalty = try k(ir, 100.0);
    const not_in = try maxOp(ir, try neg(ir, score), try k(ir, 0));
    const in_ = try maxOp(ir, score, try k(ir, 0));
    return minOp(
        ir,
        try add(ir, radial, try mul(ir, not_in, penalty)),
        try add(ir, endpoint, try mul(ir, in_, penalty)),
    );
}

fn pointDist2d(ir: *m.MathIR, x: m.Expr, y: m.Expr, p: [2]f32) Error!m.Expr {
    const dx = try sub_(ir, x, try k(ir, p[0]));
    const dy = try sub_(ir, y, try k(ir, p[1]));
    return sqrtOp(ir, try add(ir, try square(ir, dx), try square(ir, dy)));
}

fn positiveAngleDeltaConst(start_angle: f32, end_angle: f32) f32 {
    const tau = 2.0 * std.math.pi;
    var d = end_angle - start_angle;
    while (d < 0.0) d += tau;
    while (d >= tau) d -= tau;
    return d;
}
