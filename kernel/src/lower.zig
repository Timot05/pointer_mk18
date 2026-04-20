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
    UnsupportedClosedSketch,
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
    if (closed) {
        if (prims.len == 1) {
            return lowerClosedSketchSingleton(builder, coords.x, coords.y, prims[0], flip);
        }
        return error.UnsupportedClosedSketch;
    }

    var acc = lowerOpenSketchPrim(builder, coords.x, coords.y, prims[0]);
    for (prims[1..]) |prim| {
        const d = lowerOpenSketchPrim(builder, coords.x, coords.y, prim);
        acc = builder.minOp(acc, d);
    }
    return acc;
}

fn lowerClosedSketchSingleton(builder: *B, x: NR, y: NR, prim: SketchPrimitive2d, flip: bool) Error!NR {
    return switch (prim) {
        .circle => |circle| blk: {
            var d = sdfCircle2d(builder, x, y, circle.center, circle.radius);
            if (flip) d = builder.neg(d);
            break :blk d;
        },
        else => error.UnsupportedClosedSketch,
    };
}

fn lowerOpenSketchPrim(builder: *B, x: NR, y: NR, prim: SketchPrimitive2d) NR {
    return switch (prim) {
        .line_segment => |seg| sdfLineSegment2d(builder, x, y, seg.start, seg.end),
        .circle => |circle| sdfCircleCurve2d(builder, x, y, circle.center, circle.radius),
        .arc_center => |arc| sdfArcCurve2d(builder, x, y, arc.start, arc.end, arc.center, arc.clockwise),
    };
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

test "lower rejects unsupported closed multi-primitive sketch" {
    var nodes: [4]field_ir.FieldNode = undefined;
    var prims: [4]field_ir.SketchPrimitive2d = undefined;
    var builder_ir = field_ir.FieldBuilder.init(&nodes, &prims);
    const first = builder_ir.sketchLine(.{ 0.0, 0.0 }, .{ 1.0, 0.0 });
    _ = builder_ir.sketchLine(.{ 1.0, 0.0 }, .{ 1.0, 1.0 });
    const sketch = builder_ir.sketch(first, 2, true, false);
    const tree = builder_ir.finalize(sketch);

    var ops: [128]tape_mod.Instruction = undefined;
    var consts: [128]f32 = undefined;
    var builder = tape_mod.TapeBuilder.init(&ops, &consts);

    try std.testing.expectError(error.UnsupportedClosedSketch, lower(tree, &builder));
}
