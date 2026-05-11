const std = @import("std");

pub const Vec3 = struct {
    x: f32,
    y: f32,
    z: f32,

    pub fn init(x: f32, y: f32, z: f32) Vec3 {
        return .{ .x = x, .y = y, .z = z };
    }

    pub fn add(a: Vec3, b: Vec3) Vec3 {
        return .{ .x = a.x + b.x, .y = a.y + b.y, .z = a.z + b.z };
    }

    pub fn sub(a: Vec3, b: Vec3) Vec3 {
        return .{ .x = a.x - b.x, .y = a.y - b.y, .z = a.z - b.z };
    }

    pub fn scale(a: Vec3, s: f32) Vec3 {
        return .{ .x = a.x * s, .y = a.y * s, .z = a.z * s };
    }

    pub fn lerp(a: Vec3, b: Vec3, t: f32) Vec3 {
        return add(a, scale(sub(b, a), t));
    }

    pub fn dot(a: Vec3, b: Vec3) f32 {
        return a.x * b.x + a.y * b.y + a.z * b.z;
    }

    pub fn length(a: Vec3) f32 {
        return @sqrt(dot(a, a));
    }

    pub fn normalize(a: Vec3) Vec3 {
        const len = length(a);
        if (len <= 1.0e-8) return .{ .x = 0, .y = 0, .z = 0 };
        return scale(a, 1.0 / len);
    }

    pub fn clamp(v: Vec3, lo: Vec3, hi: Vec3) Vec3 {
        return .{
            .x = std.math.clamp(v.x, lo.x, hi.x),
            .y = std.math.clamp(v.y, lo.y, hi.y),
            .z = std.math.clamp(v.z, lo.z, hi.z),
        };
    }
};

pub const Interval = struct {
    lo: f32,
    hi: f32,
};

pub const Aabb = struct {
    min: Vec3,
    max: Vec3,

    pub fn size(self: Aabb) Vec3 {
        return Vec3.sub(self.max, self.min);
    }

    pub fn center(self: Aabb) Vec3 {
        return Vec3.scale(Vec3.add(self.min, self.max), 0.5);
    }

    pub fn child(self: Aabb, corner: u3) Aabb {
        const c = self.center();
        return .{
            .min = .{
                .x = if ((corner & 1) == 0) self.min.x else c.x,
                .y = if ((corner & 2) == 0) self.min.y else c.y,
                .z = if ((corner & 4) == 0) self.min.z else c.z,
            },
            .max = .{
                .x = if ((corner & 1) == 0) c.x else self.max.x,
                .y = if ((corner & 2) == 0) c.y else self.max.y,
                .z = if ((corner & 4) == 0) c.z else self.max.z,
            },
        };
    }
};

pub const CellVertex = struct {
    pos: Vec3,
    qef_error: f32,
};

pub const HermiteSample = struct {
    pos: Vec3,
    normal: Vec3,
    edge_index: u8,
};

pub const Leaf = struct {
    coord: [3]u32,
    bounds: Aabb,
    sign_mask: u8,
    corner_values: [8]f32,
    vertex: CellVertex,
    sample_count: u8,
    samples: [12]?HermiteSample,
};
