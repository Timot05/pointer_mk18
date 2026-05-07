const m = @import("math_domain.zig");

pub const Scene = struct {
    ir: m.MathIR,
    root: m.Expr,
    slots: [31]f64,
};

fn pointSlot(index: usize) m.SlotPoint2 {
    return .{ .x = @intCast(index * 2), .y = @intCast(index * 2 + 1) };
}

fn circleExpr(ir: *m.MathIR, center: m.SlotPoint2, radius_slot: i32) !m.Expr {
    const primitive_start = try ir.circle(center, radius_slot);
    return ir.sketchPath(.xy, primitive_start, 1, true, false);
}

pub fn buildComplexSketchSceneInto(scene: *Scene) !void {
    scene.ir.node_count = 0;
    scene.ir.affine_count = 0;
    scene.ir.intrinsic_count = 0;
    scene.ir.primitive_count = 0;
    scene.root = .{ .id = -1 };
    scene.slots = .{
        -1.15, -0.10,
        -0.95, 0.55,
        -0.35, 0.82,
        0.35,  0.78,
        0.95,  0.42,
        1.12,  -0.18,
        0.70,  -0.78,
        -0.05, -0.92,
        -0.78, -0.68,
        -0.42, 0.18,
        0.31,  0.45,
        0.10,  0.28,
        0.05,  -0.46,
        0.24,  -0.88,
        0.08,  0.85,
        -0.02,
    };

    const outline_start = scene.ir.primitive_count;
    _ = try scene.ir.bezierQuadratic(pointSlot(0), pointSlot(1), pointSlot(2));
    _ = try scene.ir.bezierQuadratic(pointSlot(2), pointSlot(3), pointSlot(4));
    _ = try scene.ir.bezierQuadratic(pointSlot(4), pointSlot(5), pointSlot(6));
    _ = try scene.ir.bezierQuadratic(pointSlot(6), pointSlot(7), pointSlot(8));
    _ = try scene.ir.bezierQuadratic(pointSlot(8), pointSlot(0), pointSlot(0));
    var profile = try scene.ir.sketchPath(.xy, @intCast(outline_start), 5, true, false);

    const hole0 = try scene.ir.unary(.neg, try circleExpr(&scene.ir, scene.ir.point2(18, 19), 20));
    const hole1 = try scene.ir.unary(.neg, try circleExpr(&scene.ir, scene.ir.point2(21, 22), 23));
    const hole2 = try scene.ir.unary(.neg, try circleExpr(&scene.ir, scene.ir.point2(24, 25), 26));
    profile = try scene.ir.binary(.max, try scene.ir.binary(.max, try scene.ir.binary(.max, profile, hole0), hole1), hole2);

    const slab = try scene.ir.binary(.sub, try scene.ir.unary(.abs, try scene.ir.z()), try scene.ir.constant(0.18));
    const body = try scene.ir.binary(.max, profile, slab);

    const rib_primitive = try scene.ir.lineSegment(scene.ir.point2(27, 28), scene.ir.point2(29, 30));
    const rib_xy = try scene.ir.binary(.sub, try scene.ir.sketchDistance(.xy, rib_primitive), try scene.ir.constant(0.045));
    const rib_z = try scene.ir.binary(.sub, try scene.ir.unary(.abs, try scene.ir.binary(.sub, try scene.ir.z(), try scene.ir.constant(0.19))), try scene.ir.constant(0.035));
    const rib = try scene.ir.binary(.max, rib_xy, rib_z);

    scene.root = try scene.ir.binary(.min, body, rib);
}

pub fn buildComplexSketchScene() !Scene {
    var scene: Scene = undefined;
    try buildComplexSketchSceneInto(&scene);
    return scene;
}

pub const mega_grid_n: i32 = 6;
pub const mega_spacing: f64 = 0.85;
pub const mega_scale: f64 = 0.30;

pub fn buildMegaSceneInto(scene: *Scene) !void {
    scene.ir.node_count = 0;
    scene.ir.affine_count = 0;
    scene.ir.intrinsic_count = 0;
    scene.ir.primitive_count = 0;
    scene.root = .{ .id = -1 };
    scene.slots = .{
        -1.15, -0.10,
        -0.95, 0.55,
        -0.35, 0.82,
        0.35,  0.78,
        0.95,  0.42,
        1.12,  -0.18,
        0.70,  -0.78,
        -0.05, -0.92,
        -0.78, -0.68,
        -0.42, 0.18,
        0.31,  0.45,
        0.10,  0.28,
        0.05,  -0.46,
        0.24,  -0.88,
        0.08,  0.85,
        -0.02,
    };

    const outline_start = scene.ir.primitive_count;
    _ = try scene.ir.bezierQuadratic(pointSlot(0), pointSlot(1), pointSlot(2));
    _ = try scene.ir.bezierQuadratic(pointSlot(2), pointSlot(3), pointSlot(4));
    _ = try scene.ir.bezierQuadratic(pointSlot(4), pointSlot(5), pointSlot(6));
    _ = try scene.ir.bezierQuadratic(pointSlot(6), pointSlot(7), pointSlot(8));
    _ = try scene.ir.bezierQuadratic(pointSlot(8), pointSlot(0), pointSlot(0));
    var profile = try scene.ir.sketchPath(.xy, @intCast(outline_start), 5, true, false);

    const hole0 = try scene.ir.unary(.neg, try circleExpr(&scene.ir, scene.ir.point2(18, 19), 20));
    const hole1 = try scene.ir.unary(.neg, try circleExpr(&scene.ir, scene.ir.point2(21, 22), 23));
    const hole2 = try scene.ir.unary(.neg, try circleExpr(&scene.ir, scene.ir.point2(24, 25), 26));
    profile = try scene.ir.binary(.max, try scene.ir.binary(.max, try scene.ir.binary(.max, profile, hole0), hole1), hole2);

    const slab = try scene.ir.binary(.sub, try scene.ir.unary(.abs, try scene.ir.z()), try scene.ir.constant(0.18));
    const body = try scene.ir.binary(.max, profile, slab);

    const rib_primitive = try scene.ir.lineSegment(scene.ir.point2(27, 28), scene.ir.point2(29, 30));
    const rib_xy = try scene.ir.binary(.sub, try scene.ir.sketchDistance(.xy, rib_primitive), try scene.ir.constant(0.045));
    const rib_z = try scene.ir.binary(.sub, try scene.ir.unary(.abs, try scene.ir.binary(.sub, try scene.ir.z(), try scene.ir.constant(0.19))), try scene.ir.constant(0.035));
    const rib = try scene.ir.binary(.max, rib_xy, rib_z);

    const module_local = try scene.ir.binary(.min, body, rib);

    const half_span = @as(f64, @floatFromInt(mega_grid_n - 1)) * mega_spacing * 0.5;
    const inv_s = 1.0 / mega_scale;
    const c0 = try scene.ir.constant(0.0);
    const inv_s_expr = try scene.ir.constant(inv_s);
    const scale_expr = try scene.ir.constant(mega_scale);

    var combined: ?m.Expr = null;
    var ix: i32 = 0;
    while (ix < mega_grid_n) : (ix += 1) {
        var iy: i32 = 0;
        while (iy < mega_grid_n) : (iy += 1) {
            const tx = @as(f64, @floatFromInt(ix)) * mega_spacing - half_span;
            const ty = @as(f64, @floatFromInt(iy)) * mega_spacing - half_span;
            const off_x = try scene.ir.constant(-tx * inv_s);
            const off_y = try scene.ir.constant(-ty * inv_s);
            const affine_id = try scene.ir.affine3(.{
                .m00 = inv_s_expr,
                .m01 = c0,
                .m02 = c0,
                .m03 = off_x,
                .m10 = c0,
                .m11 = inv_s_expr,
                .m12 = c0,
                .m13 = off_y,
                .m20 = c0,
                .m21 = c0,
                .m22 = inv_s_expr,
                .m23 = c0,
            });
            const transformed = try scene.ir.remapAffine(module_local, affine_id);
            const corrected = try scene.ir.binary(.mul, transformed, scale_expr);
            combined = if (combined) |c| try scene.ir.binary(.min, c, corrected) else corrected;
        }
    }

    scene.root = combined.?;
}

pub fn buildMegaScene() !Scene {
    var scene: Scene = undefined;
    try buildMegaSceneInto(&scene);
    return scene;
}

pub fn buildSphereSceneInto(scene: *Scene) !void {
    scene.ir.node_count = 0;
    scene.ir.affine_count = 0;
    scene.ir.intrinsic_count = 0;
    scene.ir.primitive_count = 0;
    scene.root = .{ .id = -1 };
    @memset(scene.slots[0..], 0.0);
    const x = try scene.ir.x();
    const y = try scene.ir.y();
    const z = try scene.ir.z();
    const xx = try scene.ir.unary(.square, x);
    const yy = try scene.ir.unary(.square, y);
    const zz = try scene.ir.unary(.square, z);
    const sum = try scene.ir.binary(.add, try scene.ir.binary(.add, xx, yy), zz);
    scene.root = try scene.ir.binary(.sub, try scene.ir.unary(.sqrt, sum), try scene.ir.constant(0.82));
}
