const std = @import("std");
const types = @import("mesh_types.zig");

pub const QuadraticErrorSolver = struct {
    ata: [6]f64 = .{ 0, 0, 0, 0, 0, 0 },
    atb: [3]f64 = .{ 0, 0, 0 },
    mass: [3]f64 = .{ 0, 0, 0 },
    count: u32 = 0,

    pub fn addIntersection(self: *QuadraticErrorSolver, p: types.Vec3, n0: types.Vec3) void {
        const n = types.Vec3.normalize(n0);
        if (types.Vec3.length(n) <= 1.0e-6) return;

        const nx: f64 = n.x;
        const ny: f64 = n.y;
        const nz: f64 = n.z;
        const px: f64 = p.x;
        const py: f64 = p.y;
        const pz: f64 = p.z;
        const b = nx * px + ny * py + nz * pz;

        self.ata[0] += nx * nx;
        self.ata[1] += nx * ny;
        self.ata[2] += nx * nz;
        self.ata[3] += ny * ny;
        self.ata[4] += ny * nz;
        self.ata[5] += nz * nz;
        self.atb[0] += nx * b;
        self.atb[1] += ny * b;
        self.atb[2] += nz * b;
        self.mass[0] += px;
        self.mass[1] += py;
        self.mass[2] += pz;
        self.count += 1;
    }

    pub fn massPoint(self: *const QuadraticErrorSolver) types.Vec3 {
        if (self.count == 0) return .{ .x = 0, .y = 0, .z = 0 };
        const inv = 1.0 / @as(f64, @floatFromInt(self.count));
        return .{
            .x = @floatCast(self.mass[0] * inv),
            .y = @floatCast(self.mass[1] * inv),
            .z = @floatCast(self.mass[2] * inv),
        };
    }

    pub fn solve(self: *const QuadraticErrorSolver) types.CellVertex {
        if (self.count == 0) {
            return .{ .pos = .{ .x = 0, .y = 0, .z = 0 }, .qef_error = std.math.inf(f32) };
        }

        if (solve3x3(self.ata, self.atb)) |x| {
            const p: types.Vec3 = .{ .x = @floatCast(x[0]), .y = @floatCast(x[1]), .z = @floatCast(x[2]) };
            return .{ .pos = p, .qef_error = @floatCast(self.errorAt(p)) };
        }

        const p = self.massPoint();
        return .{ .pos = p, .qef_error = @floatCast(self.errorAt(p)) };
    }

    pub fn errorAt(self: *const QuadraticErrorSolver, p: types.Vec3) f64 {
        const x: f64 = p.x;
        const y: f64 = p.y;
        const z: f64 = p.z;
        const ax =
            self.ata[0] * x * x +
            2.0 * self.ata[1] * x * y +
            2.0 * self.ata[2] * x * z +
            self.ata[3] * y * y +
            2.0 * self.ata[4] * y * z +
            self.ata[5] * z * z;
        const bx = 2.0 * (self.atb[0] * x + self.atb[1] * y + self.atb[2] * z);
        return @max(0.0, ax - bx + self.btb());
    }

    fn btb(self: *const QuadraticErrorSolver) f64 {
        if (self.count == 0) return 0.0;
        // For normalized planes b = n dot p, the accumulated constant term
        // is not recoverable from A^T A / A^T b alone. Error is only used as
        // a quality hint today, so leave the constant term at zero.
        return 0.0;
    }
};

fn solve3x3(ata: [6]f64, atb: [3]f64) ?[3]f64 {
    const a00 = ata[0];
    const a01 = ata[1];
    const a02 = ata[2];
    const a11 = ata[3];
    const a12 = ata[4];
    const a22 = ata[5];

    const det =
        a00 * (a11 * a22 - a12 * a12) -
        a01 * (a01 * a22 - a12 * a02) +
        a02 * (a01 * a12 - a11 * a02);
    if (@abs(det) < 1.0e-12) return null;
    const inv_det = 1.0 / det;

    const b0 = atb[0];
    const b1 = atb[1];
    const b2 = atb[2];

    return .{
        (b0 * (a11 * a22 - a12 * a12) - a01 * (b1 * a22 - a12 * b2) + a02 * (b1 * a12 - a11 * b2)) * inv_det,
        (a00 * (b1 * a22 - a12 * b2) - b0 * (a01 * a22 - a12 * a02) + a02 * (a01 * b2 - b1 * a02)) * inv_det,
        (a00 * (a11 * b2 - b1 * a12) - a01 * (a01 * b2 - b1 * a02) + b0 * (a01 * a12 - a11 * a02)) * inv_det,
    };
}
