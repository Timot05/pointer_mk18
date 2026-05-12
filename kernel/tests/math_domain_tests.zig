const std = @import("std");
const m = @import("math_domain");

fn expectClose(expected: f64, actual: f64) !void {
    try std.testing.expectApproxEqAbs(expected, actual, 1.0e-9);
}

fn expectIntervalClose(expected: m.Interval, actual: m.Interval) !void {
    try expectClose(expected.lo, actual.lo);
    try expectClose(expected.hi, actual.hi);
}

fn sphere(ir: *m.MathIR, radius: f64) !m.Expr {
    const x = try ir.x();
    const y = try ir.y();
    const z = try ir.z();
    const xx = try ir.unary(.square, x);
    const yy = try ir.unary(.square, y);
    const zz = try ir.unary(.square, z);
    const sum = try ir.binary(.add, try ir.binary(.add, xx, yy), zz);
    return ir.binary(.sub, try ir.unary(.sqrt, sum), try ir.constant(radius));
}

fn translatedSphere(ir: *m.MathIR, radius: f64, tx: f64, ty: f64, tz: f64) !m.Expr {
    return ir.remapAxes(
        try sphere(ir, radius),
        try ir.binary(.sub, try ir.x(), try ir.constant(tx)),
        try ir.binary(.sub, try ir.y(), try ir.constant(ty)),
        try ir.binary(.sub, try ir.z(), try ir.constant(tz)),
    );
}

const Point2N = struct { x: m.Expr, y: m.Expr };

fn constPoint2(ir: *m.MathIR, x: f64, y: f64) !Point2N {
    return .{ .x = try ir.constant(x), .y = try ir.constant(y) };
}

fn lineSegN(ir: *m.MathIR, p0: Point2N, p1: Point2N) !m.Expr {
    return ir.lineSegmentN(.xy, p0.x, p0.y, p1.x, p1.y);
}

fn quadN(ir: *m.MathIR, p0: Point2N, p1: Point2N, p2: Point2N) !m.Expr {
    return ir.bezierQuadraticN(.xy, p0.x, p0.y, p1.x, p1.y, p2.x, p2.y);
}

fn circN(ir: *m.MathIR, center: Point2N, r: m.Expr) !m.Expr {
    return ir.circleN(.xy, center.x, center.y, r);
}

test "register tape matches IR-tree eval on sphere union" {
    var ir = m.MathIR{};
    const left = try translatedSphere(&ir, 0.72, -0.45, 0.0, 0.0);
    const right = try translatedSphere(&ir, 0.72, 0.45, 0.0, 0.0);
    const root = try ir.binary(.min, left, right);
    const tape = try m.compileToRegTape(&ir, root);
    const slots = [_]f64{};
    const points = [_]m.Vec3{
        .{ .x = 0.0, .y = 0.0, .z = 0.0 },
        .{ .x = 0.7, .y = -0.3, .z = 0.5 },
        .{ .x = -1.2, .y = 0.4, .z = -0.8 },
    };
    for (points) |p| {
        try expectClose(m.evalPoint(&ir, root, &slots, p), m.decodeRegEval(&tape, &ir, &slots, p));
    }
}

test "curve distance along evaluates line segment along x axis" {
    var ir = m.MathIR{};
    const line = try lineSegN(&ir, try constPoint2(&ir, 2.0, 0.0), try constPoint2(&ir, 2.0, 1.0));
    const root = try ir.curveDistanceAlong(.xy, &.{line}, try ir.constant(1.0), try ir.constant(0.0), try ir.constant(0.0), false);
    const tape = try m.compileToRegTape(&ir, root);
    const slots = [_]f64{};

    const p0 = m.Vec3{ .x = 0.0, .y = 0.5, .z = 0.0 };
    const p1 = m.Vec3{ .x = 3.0, .y = 0.5, .z = 0.0 };
    try expectClose(2.0, m.evalPoint(&ir, root, &slots, p0));
    try expectClose(-1.0, m.evalPoint(&ir, root, &slots, p1));
    try expectClose(m.evalPoint(&ir, root, &slots, p0), m.decodeRegEval(&tape, &ir, &slots, p0));
    try expectClose(m.evalPoint(&ir, root, &slots, p1), m.decodeRegEval(&tape, &ir, &slots, p1));
}

test "curve distance along evaluates circle along x axis" {
    var ir = m.MathIR{};
    const circle = try circN(&ir, try constPoint2(&ir, 0.0, 0.0), try ir.constant(2.0));
    const root = try ir.curveDistanceAlong(.xy, &.{circle}, try ir.constant(1.0), try ir.constant(0.0), try ir.constant(0.0), false);
    const tape = try m.compileToRegTape(&ir, root);
    const slots = [_]f64{};
    const p = m.Vec3{ .x = 3.0, .y = 0.0, .z = 0.0 };

    try expectClose(-1.0, m.evalPoint(&ir, root, &slots, p));
    try expectClose(m.evalPoint(&ir, root, &slots, p), m.decodeRegEval(&tape, &ir, &slots, p));
}

test "curve distance along evaluates quadratic bezier along x axis" {
    var ir = m.MathIR{};
    const quad = try quadN(
        &ir,
        try constPoint2(&ir, 0.0, 0.0),
        try constPoint2(&ir, 1.0, 1.0),
        try constPoint2(&ir, 2.0, 0.0),
    );
    const root = try ir.curveDistanceAlong(.xy, &.{quad}, try ir.constant(1.0), try ir.constant(0.0), try ir.constant(0.0), false);
    const tape = try m.compileToRegTape(&ir, root);
    const slots = [_]f64{};
    const p = m.Vec3{ .x = 0.0, .y = 0.5, .z = 0.0 };

    try expectClose(1.0, m.evalPoint(&ir, root, &slots, p));
    try expectClose(m.evalPoint(&ir, root, &slots, p), m.decodeRegEval(&tape, &ir, &slots, p));
}

test "register tape matches IR-tree eval on remap_affine" {
    var ir = m.MathIR{};
    const inner = try sphere(&ir, 0.5);
    const c0 = try ir.constant(0.0);
    const inv_s = try ir.constant(1.0 / 0.3);
    const off_x = try ir.constant(0.4 / 0.3);
    const off_y = try ir.constant(-0.2 / 0.3);
    const off_z = try ir.constant(0.1 / 0.3);
    const affine = try ir.affine3(.{
        .m00 = inv_s, .m01 = c0,    .m02 = c0,    .m03 = off_x,
        .m10 = c0,    .m11 = inv_s, .m12 = c0,    .m13 = off_y,
        .m20 = c0,    .m21 = c0,    .m22 = inv_s, .m23 = off_z,
    });
    const remapped = try ir.remapAffine(inner, affine);
    const root = try ir.binary(.mul, remapped, try ir.constant(0.3));
    const tape = try m.compileToRegTape(&ir, root);
    const slots = [_]f64{};
    const points = [_]m.Vec3{
        .{ .x = 0.0, .y = 0.0, .z = 0.0 },
        .{ .x = -0.4, .y = 0.2, .z = -0.1 },
        .{ .x = -0.55, .y = 0.2, .z = -0.1 },
        .{ .x = -0.4, .y = 0.4, .z = 0.05 },
        .{ .x = 0.7, .y = -0.5, .z = 0.3 },
    };
    for (points) |p| {
        try expectClose(m.evalPoint(&ir, root, &slots, p), m.decodeRegEval(&tape, &ir, &slots, p));
    }
}

test "register tape matches IR-tree eval on quadratic sketch primitive" {
    var ir = m.MathIR{};
    const slots = [_]f64{};
    const root = try quadN(&ir, try constPoint2(&ir, -1.0, 0.0), try constPoint2(&ir, 0.0, 1.0), try constPoint2(&ir, 1.0, 0.0));
    const tape = try m.compileToRegTape(&ir, root);
    const points = [_]m.Vec3{
        .{ .x = 0.0, .y = 0.5, .z = 0.0 },
        .{ .x = 0.5, .y = 0.1, .z = 0.0 },
        .{ .x = -0.8, .y = 0.2, .z = 0.0 },
    };
    for (points) |p| {
        try expectClose(m.evalPoint(&ir, root, &slots, p), m.decodeRegEval(&tape, &ir, &slots, p));
    }
}

test "closed-triangle signed distance: inside is negative, outside is positive" {
    // Mirrors NotebookCompose's closed-loop lowering byte-for-byte:
    //   signed = fold(min, [seg_unsigned]) * (-compare(|fold(sum, [seg_winding])|, π))
    // Per-segment winding angle is atan2(cross, dot) where
    //   cross = (a-p)×(b-p), dot = (a-p)·(b-p).
    // For a CCW-traversed closed polygon the sum lands at +2π inside, 0 outside.
    var ir = m.MathIR{};
    const slots = [_]f64{};

    // Triangle vertices (CCW): a=(0,0), b=(1,0), c=(0.5,1).
    // Per-segment winding builder. Reads the kernel's current sample
    // position via `ir.x()/y()` and emits the atan2(cross, dot) subtree.
    const ax: f64 = 0.0;  const ay: f64 = 0.0;
    const bx: f64 = 1.0;  const by: f64 = 0.0;
    const cx_: f64 = 0.5; const cy_: f64 = 1.0;

    const Wnd = struct {
        fn make(irp: *m.MathIR, sx: f64, sy: f64, ex: f64, ey: f64) !m.Expr {
            const px = try irp.x();
            const py = try irp.y();
            const dxA = try irp.binary(.sub, try irp.constant(sx), px);
            const dyA = try irp.binary(.sub, try irp.constant(sy), py);
            const dxB = try irp.binary(.sub, try irp.constant(ex), px);
            const dyB = try irp.binary(.sub, try irp.constant(ey), py);
            const cross = try irp.binary(.sub,
                try irp.binary(.mul, dxA, dyB),
                try irp.binary(.mul, dyA, dxB));
            const dot = try irp.binary(.add,
                try irp.binary(.mul, dxA, dxB),
                try irp.binary(.mul, dyA, dyB));
            return irp.binary(.atan2, cross, dot);
        }
    };

    // Unsigned: fold(min, [seg_unsigned])
    const seg_ab = try lineSegN(&ir, .{ .x = try ir.constant(ax), .y = try ir.constant(ay) }, .{ .x = try ir.constant(bx), .y = try ir.constant(by) });
    const seg_bc = try lineSegN(&ir, .{ .x = try ir.constant(bx), .y = try ir.constant(by) }, .{ .x = try ir.constant(cx_), .y = try ir.constant(cy_) });
    const seg_ca = try lineSegN(&ir, .{ .x = try ir.constant(cx_), .y = try ir.constant(cy_) }, .{ .x = try ir.constant(ax), .y = try ir.constant(ay) });
    const unsigned = try ir.fold(.min, &.{ seg_ab, seg_bc, seg_ca });

    // Total winding: fold(sum, [seg_winding])
    const w_ab = try Wnd.make(&ir, ax, ay, bx, by);
    const w_bc = try Wnd.make(&ir, bx, by, cx_, cy_);
    const w_ca = try Wnd.make(&ir, cx_, cy_, ax, ay);
    const sum_w = try ir.fold(.sum, &.{ w_ab, w_bc, w_ca });
    const abs_w = try ir.unary(.abs, sum_w);

    // Sign flip: -compare(|sum|, π)
    const pi = try ir.constant(std.math.pi);
    const cmp = try ir.binary(.compare, abs_w, pi);
    const sign_flip = try ir.unary(.neg, cmp);

    const root = try ir.binary(.mul, unsigned, sign_flip);
    const tape = try m.compileToRegTape(&ir, root);

    // Inside the triangle: centroid ≈ (0.5, 0.333).
    const p_inside = m.Vec3{ .x = 0.5, .y = 0.333, .z = 0.0 };
    try std.testing.expect(m.evalPoint(&ir, root, &slots, p_inside) < 0.0);
    try std.testing.expect(m.decodeRegEval(&tape, &ir, &slots, p_inside) < 0.0);

    // Outside the triangle, well away (winding ≈ 0).
    const p_outside = m.Vec3{ .x = 2.0, .y = 0.0, .z = 0.0 };
    try std.testing.expect(m.evalPoint(&ir, root, &slots, p_outside) > 0.0);
    try std.testing.expect(m.decodeRegEval(&tape, &ir, &slots, p_outside) > 0.0);

    // Far above the triangle, on the y-axis above c: also outside.
    const p_far = m.Vec3{ .x = 0.5, .y = 5.0, .z = 0.0 };
    try std.testing.expect(m.evalPoint(&ir, root, &slots, p_far) > 0.0);
}

test "decodeRegEvalGrad through line_segment primitive matches finite differences" {
    // Phase 2 introduced primitive subtree nodes whose grad was approximated
    // as zero — which made `from-sketch` surfaces render flat-shaded (brown).
    // The grad path now central-differences the primitive; check that the
    // gradient lines up with a numerical FD on a single line-segment SDF.
    var ir = m.MathIR{};
    const slots = [_]f64{};
    const root = try lineSegN(&ir, try constPoint2(&ir, -1.0, 0.0), try constPoint2(&ir, 1.0, 0.0));
    const tape = try m.compileToRegTape(&ir, root);

    const px: f64 = 0.0;
    const py: f64 = 0.4;
    const pz: f64 = 0.0;
    const g = m.decodeRegEvalGrad(&tape, &ir, &slots, .{ .x = px, .y = py, .z = pz });

    const h: f32 = 1e-3;
    const vx_p = m.decodeRegEvalF32(&tape, &ir, &slots, .{ .x = px + h, .y = py, .z = pz });
    const vx_m = m.decodeRegEvalF32(&tape, &ir, &slots, .{ .x = px - h, .y = py, .z = pz });
    const vy_p = m.decodeRegEvalF32(&tape, &ir, &slots, .{ .x = px, .y = py + h, .z = pz });
    const vy_m = m.decodeRegEvalF32(&tape, &ir, &slots, .{ .x = px, .y = py - h, .z = pz });
    const fd_x = (vx_p - vx_m) / (2.0 * h);
    const fd_y = (vy_p - vy_m) / (2.0 * h);
    try std.testing.expectApproxEqAbs(fd_x, g[1], 5e-3);
    try std.testing.expectApproxEqAbs(fd_y, g[2], 5e-3);
    // Sanity: the gradient must be non-zero (point is 0.4 above the segment).
    const gmag = @sqrt(g[1] * g[1] + g[2] * g[2] + g[3] * g[3]);
    try std.testing.expect(gmag > 0.5);
}

test "register tape matches IR-tree eval on fold-min over line segments" {
    var ir = m.MathIR{};
    const slots = [_]f64{};
    const seg0 = try lineSegN(&ir, try constPoint2(&ir, -1.0, -0.7), try constPoint2(&ir, 1.0, -0.7));
    const seg1 = try lineSegN(&ir, try constPoint2(&ir, 1.0, -0.7), try constPoint2(&ir, 0.0, 0.9));
    const seg2 = try lineSegN(&ir, try constPoint2(&ir, 0.0, 0.9), try constPoint2(&ir, -1.0, -0.7));
    const root = try ir.fold(.min, &.{ seg0, seg1, seg2 });
    const tape = try m.compileToRegTape(&ir, root);
    const points = [_]m.Vec3{
        .{ .x = 0.0, .y = -0.2, .z = 0.0 },
        .{ .x = 1.5, .y = 0.0, .z = 0.0 },
        .{ .x = -0.5, .y = 0.4, .z = 0.0 },
    };
    for (points) |p| {
        try expectClose(m.evalPoint(&ir, root, &slots, p), m.decodeRegEval(&tape, &ir, &slots, p));
    }
}

test "register tape matches IR-tree eval on translated sphere via remap_axes" {
    var ir = m.MathIR{};
    const root = try translatedSphere(&ir, 0.5, 0.3, -0.2, 0.1);
    const tape = try m.compileToRegTape(&ir, root);
    const slots = [_]f64{};
    const points = [_]m.Vec3{
        .{ .x = 0.3, .y = -0.2, .z = 0.1 },
        .{ .x = 0.0, .y = 0.0, .z = 0.0 },
        .{ .x = 1.0, .y = 1.0, .z = 1.0 },
        .{ .x = -0.5, .y = 0.7, .z = -0.3 },
    };
    for (points) |p| {
        try expectClose(m.evalPoint(&ir, root, &slots, p), m.decodeRegEval(&tape, &ir, &slots, p));
    }
}

test "register tape preserves DAG sharing across many remap_affine copies" {
    var ir = m.MathIR{};
    const inner = try sphere(&ir, 0.4);
    const c0 = try ir.constant(0.0);
    const c1 = try ir.constant(1.0);
    var combined: ?m.Expr = null;
    var i: i32 = 0;
    while (i < 8) : (i += 1) {
        const tx = try ir.constant(-@as(f64, @floatFromInt(i)) * 0.3);
        const affine = try ir.affine3(.{
            .m00 = c1, .m01 = c0, .m02 = c0, .m03 = tx,
            .m10 = c0, .m11 = c1, .m12 = c0, .m13 = c0,
            .m20 = c0, .m21 = c0, .m22 = c1, .m23 = c0,
        });
        const remapped = try ir.remapAffine(inner, affine);
        combined = if (combined) |cc| try ir.binary(.min, cc, remapped) else remapped;
    }
    const root = combined.?;
    const tape = try m.compileToRegTape(&ir, root);
    const slots = [_]f64{};
    const points = [_]m.Vec3{
        .{ .x = 0.0, .y = 0.0, .z = 0.0 },
        .{ .x = 0.5, .y = 0.0, .z = 0.0 },
        .{ .x = 1.5, .y = 0.0, .z = 0.0 },
        .{ .x = 2.4, .y = 0.0, .z = 0.0 },
    };
    for (points) |p| {
        try expectClose(m.evalPoint(&ir, root, &slots, p), m.decodeRegEval(&tape, &ir, &slots, p));
    }
}

test "interval tape matches IR-tree evalInterval on sphere union" {
    var ir = m.MathIR{};
    const left = try translatedSphere(&ir, 0.72, -0.45, 0.0, 0.0);
    const right = try translatedSphere(&ir, 0.72, 0.45, 0.0, 0.0);
    const root = try ir.binary(.min, left, right);
    const tape = try m.compileToRegTape(&ir, root);
    const slots = [_]f64{};
    const boxes = [_]m.Box3{
        m.box3(m.interval(-1.5, 1.5), m.interval(-1.5, 1.5), m.interval(-1.5, 1.5)),
        m.box3(m.interval(-0.6, -0.3), m.interval(-0.1, 0.1), m.interval(-0.1, 0.1)),
        m.box3(m.interval(2.0, 3.0), m.interval(2.0, 3.0), m.interval(2.0, 3.0)),
        m.box3(m.interval(-0.05, 0.05), m.interval(-0.05, 0.05), m.interval(-0.05, 0.05)),
    };
    for (boxes) |box| {
        try expectIntervalClose(
            m.evalInterval(&ir, root, &slots, box),
            m.decodeRegEvalInterval(&tape, &ir, &slots, box),
        );
    }
}

test "interval tape matches IR-tree evalInterval through remap_axes" {
    var ir = m.MathIR{};
    const root = try translatedSphere(&ir, 0.5, 0.3, -0.2, 0.1);
    const tape = try m.compileToRegTape(&ir, root);
    const slots = [_]f64{};
    const boxes = [_]m.Box3{
        m.box3(m.interval(-1.0, 1.0), m.interval(-1.0, 1.0), m.interval(-1.0, 1.0)),
        m.box3(m.interval(0.2, 0.4), m.interval(-0.3, -0.1), m.interval(0.0, 0.2)),
        m.box3(m.interval(-2.0, -1.5), m.interval(-2.0, -1.5), m.interval(-2.0, -1.5)),
    };
    for (boxes) |box| {
        try expectIntervalClose(
            m.evalInterval(&ir, root, &slots, box),
            m.decodeRegEvalInterval(&tape, &ir, &slots, box),
        );
    }
}

test "interval tape matches IR-tree evalInterval through remap_affine" {
    var ir = m.MathIR{};
    const inner = try sphere(&ir, 0.5);
    const c0 = try ir.constant(0.0);
    const inv_s = try ir.constant(1.0 / 0.3);
    const off_x = try ir.constant(0.4 / 0.3);
    const off_y = try ir.constant(-0.2 / 0.3);
    const off_z = try ir.constant(0.1 / 0.3);
    const affine = try ir.affine3(.{
        .m00 = inv_s, .m01 = c0,    .m02 = c0,    .m03 = off_x,
        .m10 = c0,    .m11 = inv_s, .m12 = c0,    .m13 = off_y,
        .m20 = c0,    .m21 = c0,    .m22 = inv_s, .m23 = off_z,
    });
    const remapped = try ir.remapAffine(inner, affine);
    const root = try ir.binary(.mul, remapped, try ir.constant(0.3));
    const tape = try m.compileToRegTape(&ir, root);
    const slots = [_]f64{};
    const boxes = [_]m.Box3{
        m.box3(m.interval(-1.0, 1.0), m.interval(-1.0, 1.0), m.interval(-1.0, 1.0)),
        m.box3(m.interval(-0.5, -0.3), m.interval(0.1, 0.3), m.interval(-0.2, 0.0)),
        m.box3(m.interval(0.5, 1.0), m.interval(-0.5, -0.2), m.interval(0.2, 0.5)),
    };
    for (boxes) |box| {
        try expectIntervalClose(
            m.evalInterval(&ir, root, &slots, box),
            m.decodeRegEvalInterval(&tape, &ir, &slots, box),
        );
    }
}

test "interval tape matches IR-tree evalInterval through fold-min over line segments" {
    var ir = m.MathIR{};
    const slots = [_]f64{};
    const seg0 = try lineSegN(&ir, try constPoint2(&ir, -1.0, -0.7), try constPoint2(&ir, 1.0, -0.7));
    const seg1 = try lineSegN(&ir, try constPoint2(&ir, 1.0, -0.7), try constPoint2(&ir, 0.0, 0.9));
    const seg2 = try lineSegN(&ir, try constPoint2(&ir, 0.0, 0.9), try constPoint2(&ir, -1.0, -0.7));
    const root = try ir.fold(.min, &.{ seg0, seg1, seg2 });
    const tape = try m.compileToRegTape(&ir, root);
    const boxes = [_]m.Box3{
        m.box3(m.interval(-0.1, 0.1), m.interval(-0.3, -0.1), m.interval(0.0, 0.0)),
        m.box3(m.interval(1.4, 1.6), m.interval(-0.1, 0.1), m.interval(0.0, 0.0)),
        m.box3(m.interval(-1.5, 1.5), m.interval(-1.0, 1.0), m.interval(0.0, 0.0)),
    };
    for (boxes) |box| {
        try expectIntervalClose(
            m.evalInterval(&ir, root, &slots, box),
            m.decodeRegEvalInterval(&tape, &ir, &slots, box),
        );
    }
}

test "interval tape preserves DAG sharing across many remap_affine copies" {
    var ir = m.MathIR{};
    const inner = try sphere(&ir, 0.4);
    const c0 = try ir.constant(0.0);
    const c1 = try ir.constant(1.0);
    var combined: ?m.Expr = null;
    var i: i32 = 0;
    while (i < 8) : (i += 1) {
        const tx = try ir.constant(-@as(f64, @floatFromInt(i)) * 0.3);
        const affine = try ir.affine3(.{
            .m00 = c1, .m01 = c0, .m02 = c0, .m03 = tx,
            .m10 = c0, .m11 = c1, .m12 = c0, .m13 = c0,
            .m20 = c0, .m21 = c0, .m22 = c1, .m23 = c0,
        });
        const remapped = try ir.remapAffine(inner, affine);
        combined = if (combined) |cc| try ir.binary(.min, cc, remapped) else remapped;
    }
    const root = combined.?;
    const tape = try m.compileToRegTape(&ir, root);
    const slots = [_]f64{};
    const boxes = [_]m.Box3{
        m.box3(m.interval(-0.5, 0.5), m.interval(-0.5, 0.5), m.interval(-0.5, 0.5)),
        m.box3(m.interval(2.0, 2.5), m.interval(-0.5, 0.5), m.interval(-0.5, 0.5)),
        m.box3(m.interval(-3.0, 3.0), m.interval(-1.0, 1.0), m.interval(-1.0, 1.0)),
    };
    for (boxes) |box| {
        try expectIntervalClose(
            m.evalInterval(&ir, root, &slots, box),
            m.decodeRegEvalInterval(&tape, &ir, &slots, box),
        );
    }
}

test "simplifyTape preserves eval on disjoint sphere union" {
    var ir = m.MathIR{};
    const left = try translatedSphere(&ir, 0.4, -1.5, 0.0, 0.0);
    const right = try translatedSphere(&ir, 0.4, 1.5, 0.0, 0.0);
    const root = try ir.binary(.min, left, right);
    const tape = try m.compileToRegTape(&ir, root);
    const slots = [_]f64{};

    const box = m.box3(m.interval(-2.0, -1.0), m.interval(-0.5, 0.5), m.interval(-0.5, 0.5));
    var trace: [4096]m.Choice = undefined;
    const orig_iv = m.decodeRegEvalIntervalWithTrace(&tape, &ir, &slots, box, &trace);

    var simp: m.RegTape = undefined;
    try m.simplifyTape(&tape, &ir, &trace, &simp);

    try std.testing.expect(simp.instruction_count < tape.instruction_count);

    const simp_iv = m.decodeRegEvalInterval(&simp, &ir, &slots, box);
    try expectIntervalClose(orig_iv, simp_iv);

    const points = [_]m.Vec3{
        .{ .x = -1.5, .y = 0.0, .z = 0.0 },
        .{ .x = -1.2, .y = 0.2, .z = -0.1 },
        .{ .x = -1.8, .y = -0.3, .z = 0.4 },
    };
    for (points) |p| {
        try expectClose(m.decodeRegEval(&tape, &ir, &slots, p), m.decodeRegEval(&simp, &ir, &slots, p));
    }
}

test "simplifyTape preserves eval on overlapping sphere union" {
    var ir = m.MathIR{};
    const left = try translatedSphere(&ir, 0.6, -0.3, 0.0, 0.0);
    const right = try translatedSphere(&ir, 0.6, 0.3, 0.0, 0.0);
    const root = try ir.binary(.min, left, right);
    const tape = try m.compileToRegTape(&ir, root);
    const slots = [_]f64{};

    const box = m.box3(m.interval(-0.4, 0.4), m.interval(-0.4, 0.4), m.interval(-0.4, 0.4));
    var trace: [4096]m.Choice = undefined;
    _ = m.decodeRegEvalIntervalWithTrace(&tape, &ir, &slots, box, &trace);

    var simp: m.RegTape = undefined;
    try m.simplifyTape(&tape, &ir, &trace, &simp);

    const points = [_]m.Vec3{
        .{ .x = 0.0, .y = 0.0, .z = 0.0 },
        .{ .x = -0.2, .y = 0.1, .z = -0.1 },
        .{ .x = 0.3, .y = -0.2, .z = 0.05 },
    };
    for (points) |p| {
        try expectClose(m.decodeRegEval(&tape, &ir, &slots, p), m.decodeRegEval(&simp, &ir, &slots, p));
    }
}

test "simplifyTape preserves eval through remap_affine" {
    var ir = m.MathIR{};
    const inner = try sphere(&ir, 0.4);
    const c0 = try ir.constant(0.0);
    const c1 = try ir.constant(1.0);
    var combined: ?m.Expr = null;
    var i: i32 = 0;
    while (i < 4) : (i += 1) {
        const tx = try ir.constant(-@as(f64, @floatFromInt(i)) * 1.5);
        const affine = try ir.affine3(.{
            .m00 = c1, .m01 = c0, .m02 = c0, .m03 = tx,
            .m10 = c0, .m11 = c1, .m12 = c0, .m13 = c0,
            .m20 = c0, .m21 = c0, .m22 = c1, .m23 = c0,
        });
        const remapped = try ir.remapAffine(inner, affine);
        combined = if (combined) |cc| try ir.binary(.min, cc, remapped) else remapped;
    }
    const root = combined.?;
    const tape = try m.compileToRegTape(&ir, root);
    const slots = [_]f64{};

    const box = m.box3(m.interval(-0.5, 0.5), m.interval(-0.4, 0.4), m.interval(-0.4, 0.4));
    var trace: [4096]m.Choice = undefined;
    _ = m.decodeRegEvalIntervalWithTrace(&tape, &ir, &slots, box, &trace);

    var simp: m.RegTape = undefined;
    try m.simplifyTape(&tape, &ir, &trace, &simp);

    try std.testing.expect(simp.instruction_count < tape.instruction_count);

    const points = [_]m.Vec3{
        .{ .x = 0.0, .y = 0.0, .z = 0.0 },
        .{ .x = 0.3, .y = 0.2, .z = -0.1 },
        .{ .x = -0.4, .y = 0.0, .z = 0.3 },
    };
    for (points) |p| {
        try expectClose(m.decodeRegEval(&tape, &ir, &slots, p), m.decodeRegEval(&simp, &ir, &slots, p));
    }
}

test "fold-min over line segments returns unsigned distance" {
    // After Phase 3 the closed-path-signed-distance shortcut moved out of
    // MathIR — `sketchPath(closed=true)` is gone. Lowering to
    // `fold(min, [...primitives])` gives unsigned distance only. Verify that
    // both inside and outside points yield non-negative distances.
    var ir = m.MathIR{};
    const slots = [_]f64{};
    const seg0 = try lineSegN(&ir, try constPoint2(&ir, -1.0, -0.7), try constPoint2(&ir, 1.0, -0.7));
    const seg1 = try lineSegN(&ir, try constPoint2(&ir, 1.0, -0.7), try constPoint2(&ir, 0.0, 0.9));
    const seg2 = try lineSegN(&ir, try constPoint2(&ir, 0.0, 0.9), try constPoint2(&ir, -1.0, -0.7));
    const root = try ir.fold(.min, &.{ seg0, seg1, seg2 });
    try std.testing.expect(m.evalPoint(&ir, root, &slots, .{ .x = 0.0, .y = -0.2, .z = 0.0 }) >= 0.0);
    try std.testing.expect(m.evalPoint(&ir, root, &slots, .{ .x = 1.5, .y = 0.0, .z = 0.0 }) >= 0.0);
}

test "dual contour mesh samples current reg tape evaluator" {
    var ir = m.MathIR{};
    const root = try sphere(&ir, 0.75);
    const tape = try m.compileToRegTape(&ir, root);
    const slots = [_]f64{};

    const Ctx = struct {
        tape: *const m.RegTape,
        ir: *const m.MathIR,
        slots: []const f64,

        fn cast(ctx: ?*const anyopaque) *const @This() {
            return @ptrCast(@alignCast(ctx.?));
        }

        fn sample(ctx: ?*const anyopaque, p: m.mesh.Vec3) f32 {
            const c = cast(ctx);
            return m.decodeRegEvalF32(c.tape, c.ir, c.slots, .{ .x = p.x, .y = p.y, .z = p.z });
        }

        fn gradient(ctx: ?*const anyopaque, p: m.mesh.Vec3) m.mesh.Vec3 {
            const c = cast(ctx);
            const g = m.decodeRegEvalGrad(c.tape, c.ir, c.slots, .{ .x = p.x, .y = p.y, .z = p.z });
            return m.mesh.Vec3.normalize(.{ .x = g[1], .y = g[2], .z = g[3] });
        }

        fn interval(ctx: ?*const anyopaque, bounds: m.mesh.Aabb) m.mesh.Interval {
            const c = cast(ctx);
            const iv = m.decodeRegEvalInterval(
                c.tape,
                c.ir,
                c.slots,
                m.box3(
                    m.interval(bounds.min.x, bounds.max.x),
                    m.interval(bounds.min.y, bounds.max.y),
                    m.interval(bounds.min.z, bounds.max.z),
                ),
            );
            return .{ .lo = @floatCast(iv.lo), .hi = @floatCast(iv.hi) };
        }
    };

    const ctx: Ctx = .{ .tape = &tape, .ir = &ir, .slots = slots[0..] };
    const sampler: m.mesh.Sampler = .{
        .ctx = &ctx,
        .sampleFn = Ctx.sample,
        .gradientFn = Ctx.gradient,
        .intervalFn = Ctx.interval,
    };
    var mesh = try m.mesh.buildMesh(
        std.testing.allocator,
        sampler,
        .{
            .min = .{ .x = -1.25, .y = -1.25, .z = -1.25 },
            .max = .{ .x = 1.25, .y = 1.25, .z = 1.25 },
        },
        .{ .max_depth = 4, .binary_search_steps = 6 },
    );
    defer mesh.deinit(std.testing.allocator);

    try std.testing.expect(mesh.vertices.len > 0);
    try std.testing.expect(mesh.triangles.len > 0);
}

// ── Forward-mode autodiff (`Grad = @Vector(4, f32)`) ────────────────────

fn expectGradClose(actual: m.Grad, expected: [4]f32, tol: f32) !void {
    inline for (0..4) |i| {
        try std.testing.expectApproxEqAbs(expected[i], actual[i], tol);
    }
}

fn evalGradAt(ir: *m.MathIR, root: m.Expr, slots: []const f64, x: f64, y: f64, z: f64) !m.Grad {
    const tape = try m.compileToRegTape(ir, root);
    return m.decodeRegEvalGrad(&tape, ir, slots, .{ .x = x, .y = y, .z = z });
}

fn evalF32At(ir: *m.MathIR, root: m.Expr, slots: []const f64, x: f64, y: f64, z: f64) !f32 {
    const tape = try m.compileToRegTape(ir, root);
    return m.decodeRegEvalF32(&tape, ir, slots, .{ .x = x, .y = y, .z = z });
}

test "decodeRegEvalGrad on sphere returns analytic gradient" {
    var ir = m.MathIR{};
    const root = try sphere(&ir, 1.0);
    const slots = [_]f64{};
    const g = try evalGradAt(&ir, root, &slots, 3.0, 4.0, 0.0);
    // sqrt(9+16+0) - 1 = 4; gradient of √(x²+y²+z²) at (3,4,0) is (3/5, 4/5, 0).
    try expectGradClose(g, .{ 4.0, 0.6, 0.8, 0.0 }, 1e-6);
}

test "decodeRegEvalGrad of translated quadratic follows chain rule" {
    var ir = m.MathIR{};
    const x = try ir.x();
    const y = try ir.y();
    const dx = try ir.binary(.sub, x, try ir.constant(2.0));
    const dy = try ir.binary(.sub, y, try ir.constant(-1.0));
    const root = try ir.binary(.add, try ir.unary(.square, dx), try ir.unary(.square, dy));
    const slots = [_]f64{};
    // f(x,y) = (x-2)² + (y+1)²; at (5, 3): value = 9 + 16 = 25; ∂x = 2(x-2) = 6, ∂y = 2(y+1) = 8.
    const g = try evalGradAt(&ir, root, &slots, 5.0, 3.0, 0.0);
    try expectGradClose(g, .{ 25.0, 6.0, 8.0, 0.0 }, 1e-5);
}

test "decodeRegEvalGrad of atan2 matches closed-form partials" {
    var ir = m.MathIR{};
    const root = try ir.binary(.atan2, try ir.y(), try ir.x());
    const slots = [_]f64{};
    // atan2(y, x) at (x=2, y=3): value = atan2(3,2); ∂x = -y/(x²+y²) = -3/13; ∂y = x/(x²+y²) = 2/13.
    const g = try evalGradAt(&ir, root, &slots, 2.0, 3.0, 0.0);
    const r2: f32 = 13.0;
    try expectGradClose(g, .{ @floatCast(std.math.atan2(@as(f32, 3.0), @as(f32, 2.0))), -3.0 / r2, 2.0 / r2, 0.0 }, 1e-6);
}

test "decodeRegEvalGrad picks active branch for min and max" {
    var ir = m.MathIR{};
    const x = try ir.x();
    const y = try ir.y();
    const root = try ir.binary(.add, try ir.binary(.min, x, y), try ir.binary(.max, x, y));
    // min(x,y) + max(x,y) = x + y always; gradient at (1,3) is (1,1,0), value 4.
    const slots = [_]f64{};
    const g = try evalGradAt(&ir, root, &slots, 1.0, 3.0, 0.0);
    try expectGradClose(g, .{ 4.0, 1.0, 1.0, 0.0 }, 1e-6);
}

test "decodeRegEvalF32 value matches decodeRegEvalGrad value lane" {
    var ir = m.MathIR{};
    const left = try translatedSphere(&ir, 0.72, -0.45, 0.0, 0.0);
    const right = try translatedSphere(&ir, 0.72, 0.45, 0.0, 0.0);
    const root = try ir.binary(.min, left, right);
    const slots = [_]f64{};
    const tape = try m.compileToRegTape(&ir, root);
    // Sample several points (avoid the exact min cusp at x=0).
    const points = [_][3]f64{
        .{ 0.3, 0.0, 0.0 },
        .{ -0.5, 0.2, 0.1 },
        .{ 0.8, -0.1, 0.4 },
        .{ -0.7, 0.6, -0.3 },
    };
    for (points) |p| {
        const v = m.decodeRegEvalF32(&tape, &ir, &slots, .{ .x = p[0], .y = p[1], .z = p[2] });
        const g = m.decodeRegEvalGrad(&tape, &ir, &slots, .{ .x = p[0], .y = p[1], .z = p[2] });
        try std.testing.expectApproxEqAbs(v, g[0], 1e-6);
    }
}

test "decodeRegEvalGrad through enter_remap_affine matches finite differences" {
    // Sphere at the origin, then translated via an affine remap (identity + translation).
    var ir = m.MathIR{};
    const inner = try sphere(&ir, 0.7);
    const tx_slot = try ir.constant(0.4);
    const ty_slot = try ir.constant(-0.3);
    const tz_slot = try ir.constant(0.2);
    const one = try ir.constant(1.0);
    const zero = try ir.constant(0.0);
    // Affine maps outer (x,y,z) → inner (x-tx, y-ty, z-tz). m_ij entries are constants.
    const neg_tx = try ir.unary(.neg, tx_slot);
    const neg_ty = try ir.unary(.neg, ty_slot);
    const neg_tz = try ir.unary(.neg, tz_slot);
    const af_id = try ir.affine3(.{
        .m00 = one,  .m01 = zero, .m02 = zero, .m03 = neg_tx,
        .m10 = zero, .m11 = one,  .m12 = zero, .m13 = neg_ty,
        .m20 = zero, .m21 = zero, .m22 = one,  .m23 = neg_tz,
    });
    const root = try ir.remapAffine(inner, af_id);
    const slots = [_]f64{};
    const tape = try m.compileToRegTape(&ir, root);

    // At outer (1, 0, 0), inner = (0.6, 0.3, -0.2); value = sqrt(0.49) - 0.7 = 0.
    // Wait: sqrt(0.36 + 0.09 + 0.04) - 0.7 = sqrt(0.49) - 0.7 = 0.
    // Outer-frame partials match the inner-frame partials (identity rotation).
    const px = 1.0;
    const py = 0.0;
    const pz = 0.0;
    const g = m.decodeRegEvalGrad(&tape, &ir, &slots, .{ .x = px, .y = py, .z = pz });

    const h: f32 = 1e-3;
    const v0 = m.decodeRegEvalF32(&tape, &ir, &slots, .{ .x = px, .y = py, .z = pz });
    const vx_p = m.decodeRegEvalF32(&tape, &ir, &slots, .{ .x = px + h, .y = py, .z = pz });
    const vx_m = m.decodeRegEvalF32(&tape, &ir, &slots, .{ .x = px - h, .y = py, .z = pz });
    const vy_p = m.decodeRegEvalF32(&tape, &ir, &slots, .{ .x = px, .y = py + h, .z = pz });
    const vy_m = m.decodeRegEvalF32(&tape, &ir, &slots, .{ .x = px, .y = py - h, .z = pz });
    const vz_p = m.decodeRegEvalF32(&tape, &ir, &slots, .{ .x = px, .y = py, .z = pz + h });
    const vz_m = m.decodeRegEvalF32(&tape, &ir, &slots, .{ .x = px, .y = py, .z = pz - h });
    const fd_x = (vx_p - vx_m) / (2.0 * h);
    const fd_y = (vy_p - vy_m) / (2.0 * h);
    const fd_z = (vz_p - vz_m) / (2.0 * h);

    try std.testing.expectApproxEqAbs(v0, g[0], 1e-5);
    try std.testing.expectApproxEqAbs(fd_x, g[1], 5e-3);
    try std.testing.expectApproxEqAbs(fd_y, g[2], 5e-3);
    try std.testing.expectApproxEqAbs(fd_z, g[3], 5e-3);
}

test "decodeRegEvalGrad on union via min flows through winning branch" {
    // Two spheres at x = ±0.45, radius 0.4. At a point clearly inside the
    // left sphere (x < 0), the min-union's gradient should match the LEFT
    // sphere's gradient — i.e. point away from (-0.45, 0, 0).
    var ir = m.MathIR{};
    const left = try translatedSphere(&ir, 0.4, -0.45, 0.0, 0.0);
    const right = try translatedSphere(&ir, 0.4, 0.45, 0.0, 0.0);
    const root = try ir.binary(.min, left, right);
    const slots = [_]f64{};
    const px = -0.6; // well into the left sphere; right-sphere value is large
    const py = 0.05;
    const pz = 0.0;
    const g = try evalGradAt(&ir, root, &slots, px, py, pz);

    // For the LEFT sphere, gradient of |p - (-0.45, 0, 0)| points along (p - center)/|p-center|.
    const dxc: f32 = @floatCast(px - (-0.45));
    const dyc: f32 = @floatCast(py - 0.0);
    const dzc: f32 = @floatCast(pz - 0.0);
    const r: f32 = @sqrt(dxc * dxc + dyc * dyc + dzc * dzc);
    try std.testing.expectApproxEqAbs(dxc / r, g[1], 1e-5);
    try std.testing.expectApproxEqAbs(dyc / r, g[2], 1e-5);
    try std.testing.expectApproxEqAbs(dzc / r, g[3], 1e-5);
}

// ── Camera frame wrapping ────────────────────────────────────────────────

test "wrapWithCameraFrame identity preserves point eval" {
    var ir = m.MathIR{};
    const root = try sphere(&ir, 0.7);
    const slots = [_]f64{};

    const tape_plain = try m.compileToRegTape(&ir, root);

    const wrapped = try m.wrapWithCameraFrame(&ir, root, m.CameraFrame.identity);
    const tape_wrapped = try m.compileToRegTape(&ir, wrapped.wrapped_root);

    // For identity frame, world == input → wrapped eval matches plain eval.
    const points = [_][3]f64{
        .{ 0.3, 0.4, 0.0 },
        .{ -0.2, 0.5, 0.6 },
        .{ 0.0, 0.0, 0.0 },
    };
    for (points) |p| {
        const plain = m.decodeRegEval(&tape_plain, &ir, &slots, .{ .x = p[0], .y = p[1], .z = p[2] });
        const wrap = m.decodeRegEval(&tape_wrapped, &ir, &slots, .{ .x = p[0], .y = p[1], .z = p[2] });
        try expectClose(plain, wrap);
    }
}

test "wrapWithCameraFrame applies frame: input is camera-local, sphere evaluates at world" {
    var ir = m.MathIR{};
    const root = try sphere(&ir, 0.7);
    const slots = [_]f64{};

    // Translate the eye by (0.3, -0.2, 0.1); identity rotation. So
    // world = (wcx + 0.3, wcy - 0.2, wcz + 0.1). Camera-local input
    // (-0.3, 0.2, -0.1) maps to world origin → sphere SDF at origin =
    // -0.7 (inside).
    const frame = m.CameraFrame{
        .eye = .{ 0.3, -0.2, 0.1 },
        .basis_x = .{ 1, 0, 0 },
        .basis_y = .{ 0, 1, 0 },
        .basis_z = .{ 0, 0, 1 },
    };
    const wrapped = try m.wrapWithCameraFrame(&ir, root, frame);
    const tape = try m.compileToRegTape(&ir, wrapped.wrapped_root);

    const v = m.decodeRegEval(&tape, &ir, &slots, .{ .x = -0.3, .y = 0.2, .z = -0.1 });
    // Camera frame stores f32 → f64 round-trip, ~1e-7 relative drift.
    try std.testing.expectApproxEqAbs(-0.7, v, 1e-6);
}

test "MutableCamera.setFrame mutates eval without recompile" {
    var ir = m.MathIR{};
    const root = try sphere(&ir, 0.5);
    const slots = [_]f64{};

    const wrapped = try m.wrapWithCameraFrame(&ir, root, m.CameraFrame.identity);
    var tape = try m.compileToRegTape(&ir, wrapped.wrapped_root);
    const cam = try m.MutableCamera.bind(wrapped.nodes, &tape);

    // Initial: identity frame; eval at camera-local (0,0,0) → world (0,0,0)
    // → sphere SDF = -0.5.
    const v0 = m.decodeRegEval(&tape, &ir, &slots, .{ .x = 0, .y = 0, .z = 0 });
    try std.testing.expectApproxEqAbs(-0.5, v0, 1e-6);

    // Move eye to (1, 0, 0); same input → world (1, 0, 0) → SDF = 0.5.
    cam.setFrame(&tape, .{
        .eye = .{ 1, 0, 0 },
        .basis_x = .{ 1, 0, 0 },
        .basis_y = .{ 0, 1, 0 },
        .basis_z = .{ 0, 0, 1 },
    });
    const v1 = m.decodeRegEval(&tape, &ir, &slots, .{ .x = 0, .y = 0, .z = 0 });
    try std.testing.expectApproxEqAbs(0.5, v1, 1e-6);

    // Walk back to identity: eval should match v0.
    cam.setFrame(&tape, m.CameraFrame.identity);
    const v2 = m.decodeRegEval(&tape, &ir, &slots, .{ .x = 0, .y = 0, .z = 0 });
    try std.testing.expectApproxEqAbs(-0.5, v2, 1e-9);
}

test "CameraFrame.lookAt produces orthonormal right-handed basis" {
    // Eye at -3z, target at origin. Forward = (target - eye) normalized
    // = (0, 0, +1). basis_z is forward.
    const f = m.CameraFrame.lookAt(.{ 0, 0, -3 }, .{ 0, 0, 0 }, .{ 0, 1, 0 });
    try std.testing.expectApproxEqAbs(@as(f32, 1), f.basis_z[2], 1e-6);
    // basis_x = up_hint × basis_z = (0,1,0) × (0,0,1) = (1, 0, 0).
    try std.testing.expectApproxEqAbs(@as(f32, 1), f.basis_x[0], 1e-6);
    // basis_y = basis_z × basis_x = (0,0,1) × (1,0,0) = (0, 1, 0).
    try std.testing.expectApproxEqAbs(@as(f32, 1), f.basis_y[1], 1e-6);
}

// ── SIMD-4 bulk evaluator ────────────────────────────────────────────────

test "decodeRegEvalF4 lanes match decodeRegEvalF32 on sphere" {
    var ir = m.MathIR{};
    const root = try sphere(&ir, 0.7);
    const slots = [_]f64{};
    const tape = try m.compileToRegTape(&ir, root);

    const xs: m.F4 = .{ 0.3, -0.5, 0.8, 0.0 };
    const ys: m.F4 = .{ 0.0, 0.2, -0.1, 0.0 };
    const zs: m.F4 = .{ 0.0, 0.1, 0.4, 0.0 };
    var values: [m.max_nodes]m.F4 = undefined;
    const out = m.decodeRegEvalF4(&tape, &ir, &slots, xs, ys, zs, values[0..]);
    const lanes: [4]f32 = out;

    inline for (0..4) |i| {
        const xv: [4]f32 = xs;
        const yv: [4]f32 = ys;
        const zv: [4]f32 = zs;
        const ref = m.decodeRegEvalF32(&tape, &ir, &slots, .{ .x = xv[i], .y = yv[i], .z = zv[i] });
        try std.testing.expectApproxEqAbs(ref, lanes[i], 1e-5);
    }
}

test "decodeRegEvalF4 lanes match decodeRegEvalF32 through remap_axes" {
    var ir = m.MathIR{};
    const root = try translatedSphere(&ir, 0.5, 0.3, -0.2, 0.1);
    const slots = [_]f64{};
    const tape = try m.compileToRegTape(&ir, root);

    const xs: m.F4 = .{ 0.0, 0.4, -0.3, 0.6 };
    const ys: m.F4 = .{ 0.0, -0.1, 0.5, 0.2 };
    const zs: m.F4 = .{ 0.0, 0.3, -0.4, -0.1 };
    var values: [m.max_nodes]m.F4 = undefined;
    const out = m.decodeRegEvalF4(&tape, &ir, &slots, xs, ys, zs, values[0..]);
    const lanes: [4]f32 = out;

    inline for (0..4) |i| {
        const xv: [4]f32 = xs;
        const yv: [4]f32 = ys;
        const zv: [4]f32 = zs;
        const ref = m.decodeRegEvalF32(&tape, &ir, &slots, .{ .x = xv[i], .y = yv[i], .z = zv[i] });
        try std.testing.expectApproxEqAbs(ref, lanes[i], 1e-5);
    }
}

test "decodeRegEvalF4 lanes match decodeRegEvalF32 on sphere union via min" {
    var ir = m.MathIR{};
    const left = try translatedSphere(&ir, 0.4, -0.45, 0.0, 0.0);
    const right = try translatedSphere(&ir, 0.4, 0.45, 0.0, 0.0);
    const root = try ir.binary(.min, left, right);
    const slots = [_]f64{};
    const tape = try m.compileToRegTape(&ir, root);

    // Mix of left-winning, right-winning, and tie-ish lanes.
    const xs: m.F4 = .{ -0.5, 0.5, 0.0, -0.3 };
    const ys: m.F4 = .{ 0.0, 0.0, 0.5, 0.2 };
    const zs: m.F4 = .{ 0.0, 0.0, 0.0, 0.1 };
    var values: [m.max_nodes]m.F4 = undefined;
    const out = m.decodeRegEvalF4(&tape, &ir, &slots, xs, ys, zs, values[0..]);
    const lanes: [4]f32 = out;

    inline for (0..4) |i| {
        const xv: [4]f32 = xs;
        const yv: [4]f32 = ys;
        const zv: [4]f32 = zs;
        const ref = m.decodeRegEvalF32(&tape, &ir, &slots, .{ .x = xv[i], .y = yv[i], .z = zv[i] });
        try std.testing.expectApproxEqAbs(ref, lanes[i], 1e-5);
    }
}

// The legacy "decodeRegEvalGrad through sketch_path matches FD" test was
// dropped in Phase 3: the `sketch_path` intrinsic is gone (lowered to a
// circle primitive subtree node), and primitive-node gradients are still
// approximated as zero (`Op.primitive` returns `gConst(value)` — see
// math_grad.zig). When analytic gradients for `.line_segment`, `.circle`,
// and `.bezier_quadratic` land, a finite-difference test against the
// circle primitive can replace this comment.

// field_lower tests removed: the FieldIR wire format (scene_decode + field_lower)
// no longer exists — the F# host now ships MathIR-shaped bytes directly via
// MathIrCodec, decoded by math_ir_decode. See kernel/src/math_ir_decode.zig.
