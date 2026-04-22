const std = @import("std");
const types = @import("mesh_types.zig");

const Vec3 = types.Vec3;
const CellVertex = types.CellVertex;

pub const QuadraticErrorSolver = struct {
    ata: [3][3]f32 = [_][3]f32{
        [_]f32{ 0.0, 0.0, 0.0 },
        [_]f32{ 0.0, 0.0, 0.0 },
        [_]f32{ 0.0, 0.0, 0.0 },
    },
    atb: [3]f32 = [_]f32{ 0.0, 0.0, 0.0 },
    btb: f32 = 0.0,
    mass_point_sum: Vec3 = .{ .x = 0.0, .y = 0.0, .z = 0.0 },
    mass_point_count: f32 = 0.0,

    pub fn addIntersection(self: *QuadraticErrorSolver, pos: Vec3, normal_raw: Vec3) void {
        const normal = Vec3.normalize(normal_raw);
        if (Vec3.length(normal) <= 1e-6) return;

        self.mass_point_sum = Vec3.add(self.mass_point_sum, pos);
        self.mass_point_count += 1.0;

        const n = [_]f32{ normal.x, normal.y, normal.z };
        const d = Vec3.dot(normal, pos);
        var r: usize = 0;
        while (r < 3) : (r += 1) {
            var c: usize = 0;
            while (c < 3) : (c += 1) {
                self.ata[r][c] += n[r] * n[c];
            }
            self.atb[r] += n[r] * d;
        }
        self.btb += d * d;
    }

    pub fn solve(self: QuadraticErrorSolver) CellVertex {
        const center = self.massPoint();
        var shifted_atb = self.atb;
        const ac = mulMatVec(self.ata, center);
        var i: usize = 0;
        while (i < 3) : (i += 1) {
            shifted_atb[i] -= ac[i];
        }

        var regularized = self.ata;
        regularized[0][0] += 1e-6;
        regularized[1][1] += 1e-6;
        regularized[2][2] += 1e-6;

        const delta_opt = solve3x3(regularized, shifted_atb);
        const pos = if (delta_opt) |delta|
            Vec3.add(center, .{ .x = delta[0], .y = delta[1], .z = delta[2] })
        else
            center;

        return .{ .pos = pos, .qef_error = self.errorAt(pos) };
    }

    pub fn massPoint(self: QuadraticErrorSolver) Vec3 {
        if (self.mass_point_count <= 0.0) return .{ .x = 0.0, .y = 0.0, .z = 0.0 };
        return Vec3.scale(self.mass_point_sum, 1.0 / self.mass_point_count);
    }

    pub fn errorAt(self: QuadraticErrorSolver, pos: Vec3) f32 {
        const p = [_]f32{ pos.x, pos.y, pos.z };
        const ap = mulMatVec(self.ata, p);
        const quad = dot3(p, ap);
        const lin = 2.0 * dot3(p, self.atb);
        return @max(quad - lin + self.btb, 1e-6);
    }
};

fn dot3(a: [3]f32, b: [3]f32) f32 {
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
}

fn mulMatVec(m: [3][3]f32, v: anytype) [3]f32 {
    const info = @typeInfo(@TypeOf(v));
    const x, const y, const z = switch (info) {
        .@"struct" => .{ v.x, v.y, v.z },
        .array => .{ v[0], v[1], v[2] },
        .vector => .{ v[0], v[1], v[2] },
        else => @compileError("mulMatVec expects Vec3-like or [3]f32 input"),
    };
    return .{
        m[0][0] * x + m[0][1] * y + m[0][2] * z,
        m[1][0] * x + m[1][1] * y + m[1][2] * z,
        m[2][0] * x + m[2][1] * y + m[2][2] * z,
    };
}

fn solve3x3(a_in: [3][3]f32, b_in: [3]f32) ?[3]f32 {
    var a = a_in;
    var b = b_in;

    var col: usize = 0;
    while (col < 3) : (col += 1) {
        var pivot = col;
        var best = @abs(a[col][col]);
        var row = col + 1;
        while (row < 3) : (row += 1) {
            const score = @abs(a[row][col]);
            if (score > best) {
                best = score;
                pivot = row;
            }
        }
        if (best <= 1e-8) return null;
        if (pivot != col) {
            const tmp_row = a[col];
            a[col] = a[pivot];
            a[pivot] = tmp_row;
            const tmp_b = b[col];
            b[col] = b[pivot];
            b[pivot] = tmp_b;
        }

        const inv = 1.0 / a[col][col];
        row = col + 1;
        while (row < 3) : (row += 1) {
            const factor = a[row][col] * inv;
            var k: usize = col;
            while (k < 3) : (k += 1) {
                a[row][k] -= factor * a[col][k];
            }
            b[row] -= factor * b[col];
        }
    }

    var out = [_]f32{ 0.0, 0.0, 0.0 };
    var idx: isize = 2;
    while (idx >= 0) : (idx -= 1) {
        const i: usize = @intCast(idx);
        var sum = b[i];
        var k = i + 1;
        while (k < 3) : (k += 1) {
            sum -= a[i][k] * out[k];
        }
        if (@abs(a[i][i]) <= 1e-8) return null;
        out[i] = sum / a[i][i];
    }
    return out;
}

test "qef planar solve stays near plane" {
    var qef = QuadraticErrorSolver{};
    qef.addIntersection(.{ .x = 0.0, .y = 0.2, .z = 0.3 }, .{ .x = 1.0, .y = 0.0, .z = 0.0 });
    qef.addIntersection(.{ .x = 0.0, .y = 0.8, .z = 0.7 }, .{ .x = 1.0, .y = 0.0, .z = 0.0 });
    const solved = qef.solve();
    try std.testing.expectApproxEqAbs(0.0, solved.pos.x, 1e-4);
}
