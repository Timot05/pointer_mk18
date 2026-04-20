const std = @import("std");
const field_ir = @import("field_ir.zig");
const tape_mod = @import("tape.zig");
const eval_mod = @import("eval.zig");

const B = tape_mod.TapeBuilder;
const NR = tape_mod.NodeRef;
const FieldTree = field_ir.FieldTree;
const FieldNodeRef = field_ir.FieldNodeRef;
const Primitive = field_ir.Primitive;
const SketchPrimitive2d = field_ir.SketchPrimitive2d;
const Axis = field_ir.Axis;
const BooleanOp = field_ir.BooleanOp;
const UnaryOp = field_ir.UnaryOp;

pub const Error = error{
    InvalidRoot,
    InvalidChildRef,
    InvalidSketchRange,
    EmptySketch,
};

const Coords = struct {
    x: NR,
    y: NR,
    z: NR,
};

// Camera frame expressed as world-space vectors. The tape treats renderer
// input (wcx, wcy, wcz) as camera-local coordinates and evaluates the SDF at:
//
//     world = eye + basis_x * wcx + basis_y * wcy + basis_z * wcz
//
// Default (identity) frame: eye = 0, basis_x = +x, basis_y = +y, basis_z = +z.
// In that case world == input and no transform is applied semantically.
//
// For a lookAt-style camera, use CameraFrame.lookAt(eye, target, up_hint).
pub const CameraFrame = struct {
    eye: [3]f32,
    basis_x: [3]f32,
    basis_y: [3]f32,
    basis_z: [3]f32,

    pub const identity: CameraFrame = .{
        .eye = .{ 0, 0, 0 },
        .basis_x = .{ 1, 0, 0 },
        .basis_y = .{ 0, 1, 0 },
        .basis_z = .{ 0, 0, 1 },
    };

    // Standard right-handed lookAt. `basis_z` is the direction from target
    // toward eye (camera-local +z = away from scene), so our renderer's
    // convention of "larger wcz = closer to camera" carries over: stepping
    // wcz down moves into the scene along -forward.
    pub fn lookAt(eye: [3]f32, target: [3]f32, up_hint: [3]f32) CameraFrame {
        const cam_z = normalize(sub(eye, target));
        const basis_x = normalize(cross(up_hint, cam_z));
        const basis_y = cross(cam_z, basis_x);
        return .{ .eye = eye, .basis_x = basis_x, .basis_y = basis_y, .basis_z = cam_z };
    }
};

// Handle to the 12 camera-frame constants inside a lowered tape. The caller
// retains this and uses it to mutate the camera without re-lowering.
pub const MutableCamera = struct {
    eye: [3]u32,
    basis_x: [3]u32,
    basis_y: [3]u32,
    basis_z: [3]u32,

    pub fn setFrame(self: MutableCamera, tape: *tape_mod.Tape, frame: CameraFrame) void {
        inline for (0..3) |i| tape.constants[self.eye[i]] = frame.eye[i];
        inline for (0..3) |i| tape.constants[self.basis_x[i]] = frame.basis_x[i];
        inline for (0..3) |i| tape.constants[self.basis_y[i]] = frame.basis_y[i];
        inline for (0..3) |i| tape.constants[self.basis_z[i]] = frame.basis_z[i];
    }

    pub fn setLookAt(
        self: MutableCamera,
        tape: *tape_mod.Tape,
        eye: [3]f32,
        target: [3]f32,
        up_hint: [3]f32,
    ) void {
        self.setFrame(tape, CameraFrame.lookAt(eye, target, up_hint));
    }
};

pub const Options = struct {
    camera_local: ?CameraFrame = null,
    mutable_camera: bool = false,
};

pub const Lowered = struct {
    output: NR,
    mutable_camera: ?MutableCamera = null,
};

pub fn lower(tree: FieldTree, builder: *B) Error!NR {
    return (try lowerWithOptions(tree, builder, .{})).output;
}

pub fn lowerWithOptions(tree: FieldTree, builder: *B, options: Options) Error!Lowered {
    if (tree.root >= tree.nodes.len) return error.InvalidRoot;
    var mutable_camera: ?MutableCamera = null;
    var coords: Coords = .{
        .x = builder.inputX(),
        .y = builder.inputY(),
        .z = builder.inputZ(),
    };
    if (options.camera_local) |frame| {
        const transformed = applyCameraFrame(builder, coords, frame, options.mutable_camera);
        coords = transformed.coords;
        mutable_camera = transformed.mutable_camera;
    }
    return .{
        .output = try lowerNode(tree, builder, tree.root, coords),
        .mutable_camera = mutable_camera,
    };
}

const CameraFrameResult = struct {
    coords: Coords,
    mutable_camera: ?MutableCamera,
};

fn applyCameraFrame(
    builder: *B,
    input: Coords,
    frame: CameraFrame,
    mutable: bool,
) CameraFrameResult {
    // Emit 12 mutable constants (eye + 3 basis vectors). When not mutable,
    // the same math happens but nothing tracks the indices.
    var eye_idx: [3]u32 = undefined;
    var bx_idx: [3]u32 = undefined;
    var by_idx: [3]u32 = undefined;
    var bz_idx: [3]u32 = undefined;

    const eye = emitConstVec3(builder, frame.eye, if (mutable) &eye_idx else null);
    const bx = emitConstVec3(builder, frame.basis_x, if (mutable) &bx_idx else null);
    const by = emitConstVec3(builder, frame.basis_y, if (mutable) &by_idx else null);
    const bz = emitConstVec3(builder, frame.basis_z, if (mutable) &bz_idx else null);

    // world_i = eye_i + bx_i * wcx + by_i * wcy + bz_i * wcz
    const world_x = axisFold(builder, eye[0], bx[0], by[0], bz[0], input.x, input.y, input.z);
    const world_y = axisFold(builder, eye[1], bx[1], by[1], bz[1], input.x, input.y, input.z);
    const world_z = axisFold(builder, eye[2], bx[2], by[2], bz[2], input.x, input.y, input.z);

    return .{
        .coords = .{ .x = world_x, .y = world_y, .z = world_z },
        .mutable_camera = if (mutable) .{
            .eye = eye_idx,
            .basis_x = bx_idx,
            .basis_y = by_idx,
            .basis_z = bz_idx,
        } else null,
    };
}

fn emitConstVec3(builder: *B, v: [3]f32, out_idx: ?*[3]u32) [3]NR {
    var nodes: [3]NR = undefined;
    inline for (0..3) |i| {
        if (out_idx) |o| o[i] = builder.const_count;
        nodes[i] = builder.constant(v[i]);
    }
    return nodes;
}

fn axisFold(builder: *B, eye: NR, bx: NR, by: NR, bz: NR, x: NR, y: NR, z: NR) NR {
    return builder.add(
        builder.add(eye, builder.mul(bx, x)),
        builder.add(builder.mul(by, y), builder.mul(bz, z)),
    );
}

// ── Small vec3 helpers for CameraFrame.lookAt ─────────────────────────────

fn sub(a: [3]f32, b: [3]f32) [3]f32 {
    return .{ a[0] - b[0], a[1] - b[1], a[2] - b[2] };
}

fn cross(a: [3]f32, b: [3]f32) [3]f32 {
    return .{
        a[1] * b[2] - a[2] * b[1],
        a[2] * b[0] - a[0] * b[2],
        a[0] * b[1] - a[1] * b[0],
    };
}

fn normalize(v: [3]f32) [3]f32 {
    const len = @sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
    if (len < 1e-8) return .{ 0, 0, 1 };
    const inv = 1.0 / len;
    return .{ v[0] * inv, v[1] * inv, v[2] * inv };
}

fn lowerNode(tree: FieldTree, builder: *B, node_ref: FieldNodeRef, coords: Coords) Error!NR {
    if (node_ref >= tree.nodes.len) return error.InvalidChildRef;

    return switch (tree.nodes[node_ref]) {
        .primitive => |prim| lowerPrimitive(builder, coords, prim),
        .translate => |tr| blk: {
            const child_coords: Coords = .{
                .x = builder.sub(coords.x, builder.constant(tr.x)),
                .y = builder.sub(coords.y, builder.constant(tr.y)),
                .z = builder.sub(coords.z, builder.constant(tr.z)),
            };
            break :blk try lowerNode(tree, builder, tr.child, child_coords);
        },
        .rotate => |rot| blk: {
            const child_coords = rotateAxisAngleInv(builder, coords, rot.ax, rot.ay, rot.az, rot.angle);
            break :blk try lowerNode(tree, builder, rot.child, child_coords);
        },
        .boolean => |bool_node| blk: {
            const a = try lowerNode(tree, builder, bool_node.a, coords);
            const b = try lowerNode(tree, builder, bool_node.b, coords);
            break :blk lowerBoolean(builder, bool_node.op, bool_node.radius, a, b);
        },
        .unary => |unary| blk: {
            const child = try lowerNode(tree, builder, unary.child, coords);
            break :blk lowerUnary(builder, unary.op, unary.value, child);
        },
        .sketch => |sketch| blk: {
            break :blk try lowerSketch(tree, builder, coords, sketch.prims_first, sketch.prims_len, sketch.closed, sketch.flip);
        },
    };
}

fn lowerPrimitive(builder: *B, coords: Coords, prim: Primitive) NR {
    return switch (prim) {
        .sphere => |sphere| sdfSphere(builder, coords.x, coords.y, coords.z, sphere.radius),
        .cylinder => |cyl| sdfCylinder(builder, coords.x, coords.y, coords.z, cyl.radius, cyl.height),
        .box => |box| sdfBox(builder, coords.x, coords.y, coords.z, box.width, box.height, box.depth),
        .half_plane => |hp| sdfHalfPlane(builder, coords, hp.axis, hp.offset, hp.flip),
    };
}

fn lowerBoolean(builder: *B, op: BooleanOp, radius: f32, a: NR, b: NR) NR {
    return switch (op) {
        .union_ => smoothMin(builder, a, b, radius),
        .intersect => builder.neg(smoothMin(builder, builder.neg(a), builder.neg(b), radius)),
        .subtract => builder.neg(smoothMin(builder, builder.neg(a), b, radius)),
    };
}

fn lowerUnary(builder: *B, op: UnaryOp, value: f32, child: NR) NR {
    return switch (op) {
        .thicken => builder.sub(child, builder.constant(value)),
        .shell => builder.maxOp(child, builder.neg(builder.add(child, builder.constant(value)))),
    };
}

fn lowerSketch(
    tree: FieldTree,
    builder: *B,
    coords: Coords,
    first: u32,
    len: u32,
    closed: bool,
    flip: bool,
) Error!NR {
    const end = @as(u64, first) + @as(u64, len);
    if (end > tree.sketch_prims.len) return error.InvalidSketchRange;
    if (len == 0) return error.EmptySketch;

    const prims = tree.sketch_prims[first..@intCast(end)];

    // Unsigned distance to the curve set. `lowerOpenSketchPrim` returns
    // the SDF of each individual curve segment (line segment, circle
    // outline, arc); `min` over them gives unsigned distance to the
    // whole boundary.
    var min_d = lowerOpenSketchPrim(builder, coords.x, coords.y, prims[0]);
    for (prims[1..]) |prim| {
        const d = lowerOpenSketchPrim(builder, coords.x, coords.y, prim);
        min_d = builder.minOp(min_d, d);
    }

    if (!closed) return min_d;

    // Closed sketch. Prefer a natively-smooth signed SDF: for a convex
    // polygon of line segments, the signed distance equals the max over
    // edges of the signed distance to each edge's line. That form has
    // no branches, so interval arithmetic evaluates it tightly and the
    // coarse refinement levels show the correct silhouette instead of
    // filling the whole tile on every uncertainty.
    //
    // If the sketch isn't a simple convex polygon (arcs, circles, or
    // non-convex), fall back to the ray-crossing parity trick which
    // renders correctly at the per-pixel level but lights up coarse
    // tiles because of the `step()` looseness under IA.
    if (tryLowerConvexPolygon(builder, coords.x, coords.y, prims, flip)) |d| {
        return d;
    }

    var sign = builder.constant(1.0);
    for (prims) |prim| {
        const f = crossingSignFactor(builder, coords.x, coords.y, prim);
        sign = builder.mul(sign, f);
    }

    var signed = builder.mul(sign, min_d);
    if (flip) signed = builder.neg(signed);
    return signed;
}

// ── Convex-polygon signed SDF (IA-tight) ─────────────────────────────────
//
// Returns `null` when the input doesn't fit the shape: any non-line prim,
// fewer than three sides, a non-closed chain, or a non-convex/self-
// intersecting polygon. Callers fall through to a less tight encoding.

const MAX_CONVEX_VERTS: usize = 64;

fn tryLowerConvexPolygon(
    builder: *B,
    x: NR,
    y: NR,
    prims: []const SketchPrimitive2d,
    flip: bool,
) ?NR {
    if (prims.len < 3 or prims.len > MAX_CONVEX_VERTS) return null;

    // All prims must be line segments forming a closed loop
    // (prim[i].end ≈ prim[(i+1) mod n].start). Vertices are the segment
    // start points in order.
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

    // Signed area × 2 (shoelace) → orientation. Positive = CCW, negative
    // = CW. A polygon we can handle has a well-defined non-zero area.
    var area2: f32 = 0;
    var i: usize = 0;
    while (i < n) : (i += 1) {
        const a = verts[i];
        const b = verts[(i + 1) % n];
        area2 += a[0] * b[1] - b[0] * a[1];
    }
    if (@abs(area2) < 1e-10) return null;

    // Convexity: all consecutive edge cross products share the sign of
    // the polygon's winding. Any mismatch → concave or self-intersecting.
    const orient: f32 = if (area2 > 0) 1.0 else -1.0;
    i = 0;
    while (i < n) : (i += 1) {
        const a = verts[i];
        const b = verts[(i + 1) % n];
        const c = verts[(i + 2) % n];
        const cz = (b[0] - a[0]) * (c[1] - b[1]) - (b[1] - a[1]) * (c[0] - b[0]);
        if (cz * orient < -1e-6) return null;
    }

    // Emit max over edges of signed distance to each edge's line. For
    // CCW (orient = +1) the outward normal of edge (a, b) is
    // (b.y−a.y, a.x−b.x) / |b−a|; for CW we flip that.
    var result: ?NR = null;
    i = 0;
    while (i < n) : (i += 1) {
        const a = verts[i];
        const b = verts[(i + 1) % n];
        const dx = b[0] - a[0];
        const dy = b[1] - a[1];
        const len = @sqrt(dx * dx + dy * dy);
        if (len < 1e-9) continue; // zero-length edge; skip
        const nx = (orient * dy) / len;
        const ny = (-orient * dx) / len;
        const rel_x = builder.sub(x, builder.constant(a[0]));
        const rel_y = builder.sub(y, builder.constant(a[1]));
        const d = builder.add(
            builder.mul(rel_x, builder.constant(nx)),
            builder.mul(rel_y, builder.constant(ny)),
        );
        result = if (result) |r| builder.maxOp(r, d) else d;
    }

    if (result) |r| {
        return if (flip) builder.neg(r) else r;
    }
    return null;
}

fn lowerOpenSketchPrim(builder: *B, x: NR, y: NR, prim: SketchPrimitive2d) NR {
    return switch (prim) {
        .line_segment => |seg| sdfLineSegment2d(builder, x, y, seg.start, seg.end),
        .circle => |circle| sdfCircleCurve2d(builder, x, y, circle.center, circle.radius),
        .arc_center => |arc| sdfArcCurve2d(builder, x, y, arc.start, arc.end, arc.center, arc.clockwise),
    };
}

// ── Closed-polygon sign via horizontal-ray crossings ─────────────────────
//
// `step(v)` is a branch-free approximation of the Heaviside step. With
// `K = 1e6`, it resolves to {0, 1} outside a transition band ~1 µu wide —
// far tighter than any distance we need to resolve. Interval arithmetic
// collapses it tightly because `min`/`max` with constants are cheap.
const STEP_SHARPNESS: f32 = 1.0e6;

fn step(builder: *B, v: NR) NR {
    // clamp01(v * K + 0.5)
    const ramp = builder.add(
        builder.mul(builder.constant(STEP_SHARPNESS), v),
        builder.constant(0.5),
    );
    return builder.minOp(
        builder.constant(1.0),
        builder.maxOp(builder.constant(0.0), ramp),
    );
}

/// `(1 - 2·c)` for a scalar `c ∈ {0, 1}`. Returns +1 when `c = 0` and −1
/// when `c = 1`. Multiplying all such factors together gives `(-1)^N` for
/// the total crossing count — exactly the sign-flip parity we need.
fn crossingFactor(builder: *B, c: NR) NR {
    return builder.sub(
        builder.constant(1.0),
        builder.mul(builder.constant(2.0), c),
    );
}

fn crossingSignFactor(builder: *B, x: NR, y: NR, prim: SketchPrimitive2d) NR {
    return switch (prim) {
        .line_segment => |seg| lineCrossingFactor(builder, x, y, seg.start, seg.end),
        .circle => |circle| circleCrossingFactor(builder, x, y, circle.center, circle.radius),
        .arc_center => |arc| arcCrossingFactor(builder, x, y, arc.start, arc.end, arc.center, arc.clockwise),
    };
}

/// Does the horizontal ray from `p` (in +x direction) cross segment a→b?
/// Condition: the segment straddles `y = p.y` AND the intersection x is
/// to the right of `p.x`. Derived without division: the intersection
/// offset `(x_i − p.x)` equals `cross((a−p), (b−p)) / (b.y − a.y)`, so
/// `x_i > p.x  ⇔  cross · (b.y − a.y) > 0`.
fn lineCrossingFactor(builder: *B, x: NR, y: NR, a: [2]f32, b: [2]f32) NR {
    const ax = builder.constant(a[0]);
    const ay = builder.constant(a[1]);
    const bx = builder.constant(b[0]);
    const by = builder.constant(b[1]);

    const s1 = builder.sub(ay, y); // a.y − p.y
    const s2 = builder.sub(by, y); // b.y − p.y

    // straddles: signs of s1, s2 differ  ⇔  s1·s2 < 0  ⇔  −s1·s2 > 0.
    const straddles = step(builder, builder.neg(builder.mul(s1, s2)));

    // cross((a−p), (b−p)) = (a.x − p.x)·s2 − (b.x − p.x)·s1
    const ax_minus_px = builder.sub(ax, x);
    const bx_minus_px = builder.sub(bx, x);
    const cross_pab = builder.sub(
        builder.mul(ax_minus_px, s2),
        builder.mul(bx_minus_px, s1),
    );
    const dy = builder.sub(s2, s1); // b.y − a.y
    const to_right = step(builder, builder.mul(cross_pab, dy));

    const crossing = builder.mul(straddles, to_right);
    return crossingFactor(builder, crossing);
}

/// A horizontal line y = p.y meets a circle in 0 or 2 points (tangent
/// case is a measure-zero coincidence). Each intersection that lies to
/// the right of `p.x` contributes one crossing; we emit the two as
/// independent 0/1 crossings and fold them both into the running product.
///
///   disc    = r² − (p.y − cy)²          (negative → no intersection)
///   h       = √max(disc, 0)
///   x_left  = cx − h
///   x_right = cx + h
///   c_left  = 1 if disc > 0 AND x_left  > p.x
///   c_right = 1 if disc > 0 AND x_right > p.x
///
/// For a point clearly inside the disk, `c_left = 0, c_right = 1`
/// (product = −1); for a point clearly outside on either side, both are
/// 0 or both are 1 (product = +1). Matches the main-branch reference.
fn circleCrossingFactor(builder: *B, x: NR, y: NR, center: [2]f32, radius: f32) NR {
    const cx = builder.constant(center[0]);
    const cy = builder.constant(center[1]);
    const r2 = builder.constant(radius * radius);

    const dy = builder.sub(y, cy);
    const disc = builder.sub(r2, builder.square(dy));
    const has = step(builder, disc);

    // √max(disc, 0) — guard sqrt from negative inputs.
    const disc_safe = builder.maxOp(disc, builder.constant(0.0));
    const h = builder.sqrtOp(disc_safe);

    const x_left = builder.sub(cx, h);
    const x_right = builder.add(cx, h);
    const c_left = builder.mul(has, step(builder, builder.sub(x_left, x)));
    const c_right = builder.mul(has, step(builder, builder.sub(x_right, x)));

    return builder.mul(
        crossingFactor(builder, c_left),
        crossingFactor(builder, c_right),
    );
}

/// Arc crossings: like circle, but each candidate intersection is also
/// gated by "does its angular position fall on the arc?". The angular
/// membership test reuses the same "signed half-sweep" trick as
/// `sdfArcCurve2d`: compute each candidate's angular offset from the
/// arc's centre angle via `atan2`, then compare to the half-sweep.
fn arcCrossingFactor(
    builder: *B,
    x: NR,
    y: NR,
    start: [2]f32,
    end_: [2]f32,
    center: [2]f32,
    clockwise: bool,
) NR {
    const sx = start[0] - center[0];
    const sy = start[1] - center[1];
    const ex = end_[0] - center[0];
    const ey = end_[1] - center[1];
    const radius = @sqrt(sx * sx + sy * sy);
    if (radius < 1e-6) {
        // Degenerate arc → treat as a line segment.
        return lineCrossingFactor(builder, x, y, start, end_);
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

    const cx = builder.constant(center[0]);
    const cy = builder.constant(center[1]);
    const r2 = builder.constant(radius * radius);

    const dy = builder.sub(y, cy);
    const disc = builder.sub(r2, builder.square(dy));
    const has = step(builder, disc);

    const disc_safe = builder.maxOp(disc, builder.constant(0.0));
    const h = builder.sqrtOp(disc_safe);

    const x_left = builder.sub(cx, h);
    const x_right = builder.add(cx, h);

    // For each candidate, check both:
    //   (1) disc > 0 AND x_candidate > p.x   (same as circle)
    //   (2) the candidate's angle lies within [center_angle ± half_angle]
    const c_left = combineArcCrossing(
        builder, x, y, x_left, has, cx, cy, cv_x, cv_y, half_angle,
    );
    const c_right = combineArcCrossing(
        builder, x, y, x_right, has, cx, cy, cv_x, cv_y, half_angle,
    );

    return builder.mul(
        crossingFactor(builder, c_left),
        crossingFactor(builder, c_right),
    );
}

/// Shared tail for arc left/right crossings. `candidate_x` is one of the
/// two horizontal-line intersections with the circle; we combine the
/// "to-right" step, the "has-intersection" flag, and the angular
/// membership step into one 0/1 crossing.
fn combineArcCrossing(
    builder: *B,
    x: NR,
    y: NR,
    candidate_x: NR,
    has: NR,
    cx: NR,
    cy: NR,
    cv_x: f32,
    cv_y: f32,
    half_angle: f32,
) NR {
    const qx = builder.sub(candidate_x, cx);
    const qy = builder.sub(y, cy);
    // Signed angle from centre_angle to the candidate's angle — same
    // cross/dot+atan2 trick as sdfArcCurve2d.
    const cross_2d = builder.sub(
        builder.mul(builder.constant(cv_x), qy),
        builder.mul(builder.constant(cv_y), qx),
    );
    const dot_v = builder.add(
        builder.mul(builder.constant(cv_x), qx),
        builder.mul(builder.constant(cv_y), qy),
    );
    const ang_diff = builder.atan2Op(cross_2d, dot_v);
    // In-arc: |ang_diff| ≤ half_angle  ⇔  half_angle − |ang_diff| ≥ 0.
    const in_arc = step(
        builder,
        builder.sub(builder.constant(half_angle), builder.absOp(ang_diff)),
    );

    const to_right = step(builder, builder.sub(candidate_x, x));

    return builder.mul(builder.mul(has, to_right), in_arc);
}

fn sdfSphere(builder: *B, x: NR, y: NR, z: NR, radius: f32) NR {
    const sum_sq = builder.add(
        builder.add(builder.square(x), builder.square(y)),
        builder.square(z),
    );
    return builder.sub(builder.sqrtOp(sum_sq), builder.constant(radius));
}

fn sdfCylinder(builder: *B, x: NR, y: NR, z: NR, radius: f32, height: f32) NR {
    const radial = builder.sub(
        builder.sqrtOp(builder.add(builder.square(x), builder.square(y))),
        builder.constant(radius),
    );
    const axial = builder.sub(builder.absOp(z), builder.constant(height * 0.5));
    return builder.maxOp(radial, axial);
}

fn sdfBox(builder: *B, x: NR, y: NR, z: NR, width: f32, height: f32, depth: f32) NR {
    const qx = builder.sub(builder.absOp(x), builder.constant(width * 0.5));
    const qy = builder.sub(builder.absOp(y), builder.constant(height * 0.5));
    const qz = builder.sub(builder.absOp(z), builder.constant(depth * 0.5));
    return builder.maxOp(qx, builder.maxOp(qy, qz));
}

fn sdfHalfPlane(builder: *B, coords: Coords, axis: Axis, offset: f32, flip: bool) NR {
    const coord = switch (axis) {
        .x => coords.x,
        .y => coords.y,
        .z => coords.z,
    };
    var d = builder.sub(coord, builder.constant(offset));
    if (flip) d = builder.neg(d);
    return d;
}

fn rotateAxisAngleInv(builder: *B, coords: Coords, ax: f32, ay: f32, az: f32, angle: f32) Coords {
    const axis_len = @sqrt(ax * ax + ay * ay + az * az);
    if (axis_len <= 1e-6) return coords;

    const ux = ax / axis_len;
    const uy = ay / axis_len;
    const uz = az / axis_len;
    const c = @cos(-angle);
    const s = @sin(-angle);
    const one_minus_c = 1.0 - c;

    const dot = builder.add(
        builder.add(
            builder.mul(builder.constant(ux), coords.x),
            builder.mul(builder.constant(uy), coords.y),
        ),
        builder.mul(builder.constant(uz), coords.z),
    );

    const cross_x = builder.sub(
        builder.mul(builder.constant(uy), coords.z),
        builder.mul(builder.constant(uz), coords.y),
    );
    const cross_y = builder.sub(
        builder.mul(builder.constant(uz), coords.x),
        builder.mul(builder.constant(ux), coords.z),
    );
    const cross_z = builder.sub(
        builder.mul(builder.constant(ux), coords.y),
        builder.mul(builder.constant(uy), coords.x),
    );

    return .{
        .x = builder.add(
            builder.add(
                builder.mul(coords.x, builder.constant(c)),
                builder.mul(cross_x, builder.constant(s)),
            ),
            builder.mul(builder.constant(ux * one_minus_c), dot),
        ),
        .y = builder.add(
            builder.add(
                builder.mul(coords.y, builder.constant(c)),
                builder.mul(cross_y, builder.constant(s)),
            ),
            builder.mul(builder.constant(uy * one_minus_c), dot),
        ),
        .z = builder.add(
            builder.add(
                builder.mul(coords.z, builder.constant(c)),
                builder.mul(cross_z, builder.constant(s)),
            ),
            builder.mul(builder.constant(uz * one_minus_c), dot),
        ),
    };
}

fn smoothMin(builder: *B, a: NR, b: NR, k: f32) NR {
    if (k <= 1e-6) return builder.minOp(a, b);

    const diff = builder.sub(a, b);
    const h = builder.div(
        builder.maxOp(builder.sub(builder.constant(k), builder.absOp(diff)), builder.constant(0)),
        builder.constant(k),
    );
    const cubic = builder.mul(builder.square(h), h);
    return builder.sub(builder.minOp(a, b), builder.mul(cubic, builder.constant(k / 6.0)));
}

fn sdfLineSegment2d(builder: *B, x: NR, y: NR, start: [2]f32, end_: [2]f32) NR {
    const ex = end_[0] - start[0];
    const ey = end_[1] - start[1];
    const l = ex * ex + ey * ey + 1e-20;
    const wx = builder.sub(x, builder.constant(start[0]));
    const wy = builder.sub(y, builder.constant(start[1]));
    const t = builder.minOp(
        builder.maxOp(
            builder.div(
                builder.add(
                    builder.mul(wx, builder.constant(ex)),
                    builder.mul(wy, builder.constant(ey)),
                ),
                builder.constant(l),
            ),
            builder.constant(0),
        ),
        builder.constant(1),
    );
    const dx = builder.sub(wx, builder.mul(builder.constant(ex), t));
    const dy = builder.sub(wy, builder.mul(builder.constant(ey), t));
    return builder.sqrtOp(builder.add(builder.square(dx), builder.square(dy)));
}

fn sdfCircleCurve2d(builder: *B, x: NR, y: NR, center: [2]f32, radius: f32) NR {
    return builder.absOp(sdfCircle2d(builder, x, y, center, radius));
}

fn sdfCircle2d(builder: *B, x: NR, y: NR, center: [2]f32, radius: f32) NR {
    const dx = builder.sub(x, builder.constant(center[0]));
    const dy = builder.sub(y, builder.constant(center[1]));
    return builder.sub(
        builder.sqrtOp(builder.add(builder.square(dx), builder.square(dy))),
        builder.constant(radius),
    );
}

fn sdfArcCurve2d(
    builder: *B,
    x: NR,
    y: NR,
    start: [2]f32,
    end_: [2]f32,
    center: [2]f32,
    clockwise: bool,
) NR {
    const sx = start[0] - center[0];
    const sy = start[1] - center[1];
    const ex = end_[0] - center[0];
    const ey = end_[1] - center[1];
    const radius = @sqrt(sx * sx + sy * sy);
    if (radius < 1e-6) return sdfLineSegment2d(builder, x, y, start, end_);

    const qx = builder.sub(x, builder.constant(center[0]));
    const qy = builder.sub(y, builder.constant(center[1]));
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

    const cross_2d = builder.sub(
        builder.mul(builder.constant(cv_x), qy),
        builder.mul(builder.constant(cv_y), qx),
    );
    const dot_v = builder.add(
        builder.mul(builder.constant(cv_x), qx),
        builder.mul(builder.constant(cv_y), qy),
    );
    const ang_diff = builder.atan2Op(cross_2d, dot_v);
    const score = builder.sub(builder.constant(half_angle), builder.absOp(ang_diff));

    const radial = builder.absOp(builder.sub(
        builder.sqrtOp(builder.add(builder.square(qx), builder.square(qy))),
        builder.constant(radius),
    ));
    const d_start = pointDist2d(builder, x, y, start);
    const d_end = pointDist2d(builder, x, y, end_);
    const endpoint = builder.minOp(d_start, d_end);

    const penalty = builder.constant(100.0);
    const not_in = builder.maxOp(builder.neg(score), builder.constant(0));
    const in_ = builder.maxOp(score, builder.constant(0));
    return builder.minOp(
        builder.add(radial, builder.mul(not_in, penalty)),
        builder.add(endpoint, builder.mul(in_, penalty)),
    );
}

fn pointDist2d(builder: *B, x: NR, y: NR, p: [2]f32) NR {
    const dx = builder.sub(x, builder.constant(p[0]));
    const dy = builder.sub(y, builder.constant(p[1]));
    return builder.sqrtOp(builder.add(builder.square(dx), builder.square(dy)));
}

fn positiveAngleDeltaConst(start_angle: f32, end_angle: f32) f32 {
    const tau = 2.0 * std.math.pi;
    var d = end_angle - start_angle;
    while (d < 0.0) d += tau;
    while (d >= tau) d -= tau;
    return d;
}

fn dumpTape(tape: tape_mod.Tape) void {
    std.debug.print("output_slot={d}\n", .{tape.output_slot});
    std.debug.print("choice_count={d}\n", .{tape.choice_count});
    std.debug.print("constants[{d}]\n", .{tape.constants.len});
    for (tape.constants, 0..) |c, i| {
        std.debug.print("  [{d}] {d}\n", .{ i, c });
    }
    std.debug.print("ops[{d}]\n", .{tape.ops.len});
    for (tape.ops, 0..) |op, i| {
        std.debug.print("  [{d}] {s} a={d} b={d}\n", .{ i, @tagName(op.op), op.a, op.b });
    }
}

test "lower sphere translate thicken" {
    var nodes: [8]field_ir.FieldNode = undefined;
    var prims: [1]field_ir.SketchPrimitive2d = undefined;
    var builder_ir = field_ir.FieldBuilder.init(&nodes, &prims);
    const sphere = builder_ir.sphere(2.0);
    const moved = builder_ir.translate(1.0, 2.0, 3.0, sphere);
    const thick = builder_ir.thicken(0.25, moved);
    const tree = builder_ir.finalize(thick);

    var ops: [128]tape_mod.Instruction = undefined;
    var consts: [128]f32 = undefined;
    var builder = tape_mod.TapeBuilder.init(&ops, &consts);
    const out = try lower(tree, &builder);
    const tape = builder.finalize(out);

    try std.testing.expect(tape.ops.len > 3);
    try std.testing.expectEqual(@as(u32, out), tape.output_slot);
}

test "lower open sketch circle" {
    var nodes: [4]field_ir.FieldNode = undefined;
    var prims: [2]field_ir.SketchPrimitive2d = undefined;
    var builder_ir = field_ir.FieldBuilder.init(&nodes, &prims);
    const first = builder_ir.sketchCircle(.{ 1.0, -2.0 }, 3.0);
    const sketch = builder_ir.sketch(first, 1, false, false);
    const tree = builder_ir.finalize(sketch);

    var ops: [128]tape_mod.Instruction = undefined;
    var consts: [128]f32 = undefined;
    var builder = tape_mod.TapeBuilder.init(&ops, &consts);
    _ = try lower(tree, &builder);

    try std.testing.expect(builder.op_count > 0);
}

test "print lowered sphere tape" {
    var nodes: [2]field_ir.FieldNode = undefined;
    var prims: [1]field_ir.SketchPrimitive2d = undefined;
    var builder_ir = field_ir.FieldBuilder.init(&nodes, &prims);
    const sphere = builder_ir.sphere(1.5);
    const tree = builder_ir.finalize(sphere);

    var ops: [64]tape_mod.Instruction = undefined;
    var consts: [64]f32 = undefined;
    var builder = tape_mod.TapeBuilder.init(&ops, &consts);
    const out = try lower(tree, &builder);
    const tape = builder.finalize(out);

    dumpTape(tape);

    try std.testing.expectEqual(@as(usize, 11), tape.ops.len);
    try std.testing.expectEqual(@as(usize, 1), tape.constants.len);
    try std.testing.expectEqual(@as(u32, 10), tape.output_slot);
}

test "print lowered subtract tape" {
    var nodes: [8]field_ir.FieldNode = undefined;
    var prims: [1]field_ir.SketchPrimitive2d = undefined;
    var builder_ir = field_ir.FieldBuilder.init(&nodes, &prims);
    const base = builder_ir.sphere(1.5);
    const cut_sphere = builder_ir.sphere(0.75);
    const moved_cut = builder_ir.translate(0.5, 0.0, 0.0, cut_sphere);
    const diff = builder_ir.subtract(base, moved_cut, 0.0);
    const tree = builder_ir.finalize(diff);

    var ops: [128]tape_mod.Instruction = undefined;
    var consts: [128]f32 = undefined;
    var builder = tape_mod.TapeBuilder.init(&ops, &consts);
    const out = try lower(tree, &builder);
    const tape = builder.finalize(out);

    dumpTape(tape);

    try std.testing.expect(tape.ops.len > 11);
    try std.testing.expectEqual(@as(usize, 5), tape.constants.len);
    try std.testing.expectEqual(@as(u32, out), tape.output_slot);
}

test "mutable camera updates lowered tape without rebuild" {
    var nodes: [2]field_ir.FieldNode = undefined;
    var prims: [1]field_ir.SketchPrimitive2d = undefined;
    var builder_ir = field_ir.FieldBuilder.init(&nodes, &prims);
    const sphere = builder_ir.sphere(1.0);
    const tree = builder_ir.finalize(sphere);

    var ops: [128]tape_mod.Instruction = undefined;
    var consts: [128]f32 = undefined;
    var builder = tape_mod.TapeBuilder.init(&ops, &consts);
    const lowered = try lowerWithOptions(tree, &builder, .{
        .camera_local = CameraFrame.identity,
        .mutable_camera = true,
    });
    var tape = builder.finalize(lowered.output);
    const cam = lowered.mutable_camera.?;

    var slots: [128]f32 = undefined;
    // Identity camera: world = input. Sphere at world origin, radius 1.
    // evalScalar(0,0,0) → world (0,0,0) → SDF = -1 (sphere center).
    const before = eval_mod.evalScalar(&tape, 0.0, 0.0, 0.0, &slots);
    try std.testing.expectEqual(@as(f32, -1.0), before);

    // Shift eye to (2,0,0). With identity basis: world = eye + input.
    // evalScalar(0,0,0) → world (2,0,0) → SDF = sqrt(4)-1 = 1.
    cam.setFrame(&tape, .{
        .eye = .{ 2.0, 0.0, 0.0 },
        .basis_x = .{ 1, 0, 0 },
        .basis_y = .{ 0, 1, 0 },
        .basis_z = .{ 0, 0, 1 },
    });
    const shifted = eval_mod.evalScalar(&tape, 0.0, 0.0, 0.0, &slots);
    try std.testing.expectEqual(@as(f32, 1.0), shifted);

    // At camera-local input (-2,0,0), world = (0,0,0) → sphere center.
    const at_sphere_center = eval_mod.evalScalar(&tape, -2.0, 0.0, 0.0, &slots);
    try std.testing.expectEqual(@as(f32, -1.0), at_sphere_center);
}

test "CameraFrame lookAt produces orthonormal right-handed basis" {
    // Camera at (0,0,3) looking at origin with world +y up.
    // Expected: basis_z = +z (from target to eye), basis_x = +x, basis_y = +y.
    const frame = CameraFrame.lookAt(.{ 0, 0, 3 }, .{ 0, 0, 0 }, .{ 0, 1, 0 });
    try std.testing.expectApproxEqAbs(@as(f32, 1.0), frame.basis_x[0], 1e-6);
    try std.testing.expectApproxEqAbs(@as(f32, 0.0), frame.basis_x[1], 1e-6);
    try std.testing.expectApproxEqAbs(@as(f32, 0.0), frame.basis_x[2], 1e-6);
    try std.testing.expectApproxEqAbs(@as(f32, 0.0), frame.basis_y[0], 1e-6);
    try std.testing.expectApproxEqAbs(@as(f32, 1.0), frame.basis_y[1], 1e-6);
    try std.testing.expectApproxEqAbs(@as(f32, 0.0), frame.basis_y[2], 1e-6);
    try std.testing.expectApproxEqAbs(@as(f32, 0.0), frame.basis_z[0], 1e-6);
    try std.testing.expectApproxEqAbs(@as(f32, 0.0), frame.basis_z[1], 1e-6);
    try std.testing.expectApproxEqAbs(@as(f32, 1.0), frame.basis_z[2], 1e-6);
}

test "lower closed L-shape falls back to ray-crossing and still signs correctly" {
    // L-shape (non-convex): (0,0) (2,0) (2,1) (1,1) (1,2) (0,2) closed.
    var nodes: [16]field_ir.FieldNode = undefined;
    var prims: [16]field_ir.SketchPrimitive2d = undefined;
    var builder_ir = field_ir.FieldBuilder.init(&nodes, &prims);
    const first = builder_ir.sketchLine(.{ 0.0, 0.0 }, .{ 2.0, 0.0 });
    _ = builder_ir.sketchLine(.{ 2.0, 0.0 }, .{ 2.0, 1.0 });
    _ = builder_ir.sketchLine(.{ 2.0, 1.0 }, .{ 1.0, 1.0 });
    _ = builder_ir.sketchLine(.{ 1.0, 1.0 }, .{ 1.0, 2.0 });
    _ = builder_ir.sketchLine(.{ 1.0, 2.0 }, .{ 0.0, 2.0 });
    _ = builder_ir.sketchLine(.{ 0.0, 2.0 }, .{ 0.0, 0.0 });
    const sketch = builder_ir.sketch(first, 6, true, false);
    const tree = builder_ir.finalize(sketch);

    var ops: [1024]tape_mod.Instruction = undefined;
    var consts: [1024]f32 = undefined;
    var builder = tape_mod.TapeBuilder.init(&ops, &consts);

    const out = try lower(tree, &builder);
    const tape = builder.finalize(out);

    var slots: [1024]f32 = undefined;
    // (0.5, 0.5) is inside the L.
    const inside = eval_mod.evalScalar(&tape, 0.5, 0.5, 0.0, slots[0..]);
    try std.testing.expect(inside < 0.0);
    // (1.5, 1.5) is in the L's inner cutout → outside.
    const cut = eval_mod.evalScalar(&tape, 1.5, 1.5, 0.0, slots[0..]);
    try std.testing.expect(cut > 0.0);
}

test "lower closed unit square sketch reports inside/outside" {
    // Unit square: (0,0) → (1,0) → (1,1) → (0,1) → (0,0).
    var nodes: [8]field_ir.FieldNode = undefined;
    var prims: [8]field_ir.SketchPrimitive2d = undefined;
    var builder_ir = field_ir.FieldBuilder.init(&nodes, &prims);
    const first = builder_ir.sketchLine(.{ 0.0, 0.0 }, .{ 1.0, 0.0 });
    _ = builder_ir.sketchLine(.{ 1.0, 0.0 }, .{ 1.0, 1.0 });
    _ = builder_ir.sketchLine(.{ 1.0, 1.0 }, .{ 0.0, 1.0 });
    _ = builder_ir.sketchLine(.{ 0.0, 1.0 }, .{ 0.0, 0.0 });
    const sketch = builder_ir.sketch(first, 4, true, false);
    const tree = builder_ir.finalize(sketch);

    var ops: [1024]tape_mod.Instruction = undefined;
    var consts: [1024]f32 = undefined;
    var builder = tape_mod.TapeBuilder.init(&ops, &consts);

    const out = try lower(tree, &builder);
    const tape = builder.finalize(out);

    var slots: [1024]f32 = undefined;
    // Inside the square → SDF should be negative.
    const inside = eval_mod.evalScalar(&tape, 0.5, 0.5, 0.0, slots[0..]);
    try std.testing.expect(inside < 0.0);
    // Outside to the right → SDF should be positive.
    const outside = eval_mod.evalScalar(&tape, 2.0, 0.5, 0.0, slots[0..]);
    try std.testing.expect(outside > 0.0);
}
