const std = @import("std");
const types = @import("mesh_types.zig");
const qef_mod = @import("mesh_qef.zig");

const Vec3 = types.Vec3;
const Aabb = types.Aabb;
const Leaf = types.Leaf;
const HermiteSample = types.HermiteSample;

pub const Sampler = struct {
    ctx: ?*const anyopaque,
    sampleFn: *const fn (?*const anyopaque, Vec3) f32,
    gradientFn: *const fn (?*const anyopaque, Vec3) Vec3,

    pub fn sample(self: Sampler, pos: Vec3) f32 {
        return self.sampleFn(self.ctx, pos);
    }

    pub fn gradient(self: Sampler, pos: Vec3) Vec3 {
        return self.gradientFn(self.ctx, pos);
    }
};

pub const BuildSettings = struct {
    max_depth: u8 = 6,
    binary_search_steps: u8 = 8,
};

pub const Octree = struct {
    allocator: std.mem.Allocator,
    bounds: Aabb,
    max_depth: u8,
    leaves: []Leaf,
    leaf_lookup: std.AutoHashMap(u64, u32),

    pub fn build(
        allocator: std.mem.Allocator,
        sampler: Sampler,
        bounds: Aabb,
        settings: BuildSettings,
    ) !Octree {
        var leaves: std.ArrayList(Leaf) = .empty;
        errdefer leaves.deinit(allocator);

        try recurse(
            allocator,
            &leaves,
            sampler,
            bounds,
            settings,
            .{ 0, 0, 0 },
            0,
        );

        var leaf_lookup = std.AutoHashMap(u64, u32).init(allocator);
        errdefer leaf_lookup.deinit();
        for (leaves.items, 0..) |leaf, i| {
            try leaf_lookup.put(coordKey(leaf.coord), @intCast(i));
        }

        return .{
            .allocator = allocator,
            .bounds = bounds,
            .max_depth = settings.max_depth,
            .leaves = try leaves.toOwnedSlice(allocator),
            .leaf_lookup = leaf_lookup,
        };
    }

    pub fn deinit(self: *Octree) void {
        self.allocator.free(self.leaves);
        self.leaf_lookup.deinit();
        self.* = undefined;
    }

    pub fn leafAt(self: *const Octree, coord: [3]u32) ?*const Leaf {
        const index = self.leaf_lookup.get(coordKey(coord)) orelse return null;
        return &self.leaves[index];
    }
};

pub fn coordKey(coord: [3]u32) u64 {
    return (@as(u64, coord[0]) << 42) | (@as(u64, coord[1]) << 21) | @as(u64, coord[2]);
}

const corner_offsets = [_]Vec3{
    .{ .x = 0.0, .y = 0.0, .z = 0.0 },
    .{ .x = 1.0, .y = 0.0, .z = 0.0 },
    .{ .x = 0.0, .y = 1.0, .z = 0.0 },
    .{ .x = 1.0, .y = 1.0, .z = 0.0 },
    .{ .x = 0.0, .y = 0.0, .z = 1.0 },
    .{ .x = 1.0, .y = 0.0, .z = 1.0 },
    .{ .x = 0.0, .y = 1.0, .z = 1.0 },
    .{ .x = 1.0, .y = 1.0, .z = 1.0 },
};

const edge_corners = [_][2]u8{
    .{ 0, 1 }, .{ 2, 3 }, .{ 4, 5 }, .{ 6, 7 },
    .{ 0, 2 }, .{ 1, 3 }, .{ 4, 6 }, .{ 5, 7 },
    .{ 0, 4 }, .{ 1, 5 }, .{ 2, 6 }, .{ 3, 7 },
};

fn recurse(
    allocator: std.mem.Allocator,
    leaves: *std.ArrayList(Leaf),
    sampler: Sampler,
    bounds: Aabb,
    settings: BuildSettings,
    coord: [3]u32,
    depth: u8,
) !void {
    const samples = sampleCorners(sampler, bounds);
    const sign_mask = cornerSignMask(samples.values);
    if (sign_mask == 0 or sign_mask == 0xff) {
        const center_value = sampler.sample(bounds.center());
        const corners_negative = sign_mask == 0xff;
        const center_negative = center_value < 0.0;
        if (corners_negative == center_negative) return;
    }

        if (depth == settings.max_depth) {
        if (try buildLeaf(sampler, bounds, coord, depth, sign_mask, samples.values, settings.binary_search_steps)) |leaf| {
            try leaves.append(allocator, leaf);
        }
        return;
    }

    var child_corner: u8 = 0;
    while (child_corner < 8) : (child_corner += 1) {
        const child_bounds = bounds.child(@intCast(child_corner));
        const child_coord = .{
            coord[0] * 2 + @as(u32, child_corner & 1),
            coord[1] * 2 + @as(u32, (child_corner >> 1) & 1),
            coord[2] * 2 + @as(u32, (child_corner >> 2) & 1),
        };
        try recurse(allocator, leaves, sampler, child_bounds, settings, child_coord, depth + 1);
    }
}

fn sampleCorners(sampler: Sampler, bounds: Aabb) struct { values: [8]f32 } {
    const size = bounds.size();
    var values: [8]f32 = undefined;
    for (corner_offsets, 0..) |offset, i| {
        const pos = .{
            .x = bounds.min.x + offset.x * size.x,
            .y = bounds.min.y + offset.y * size.y,
            .z = bounds.min.z + offset.z * size.z,
        };
        values[i] = sampler.sample(Vec3.init(pos.x, pos.y, pos.z));
    }
    return .{ .values = values };
}

fn cornerSignMask(values: [8]f32) u8 {
    var mask: u8 = 0;
    for (values, 0..) |value, i| {
        if (value < 0.0) mask |= @as(u8, 1) << @intCast(i);
    }
    return mask;
}

fn cornerPos(bounds: Aabb, corner: u8) Vec3 {
    const size = bounds.size();
    return .{
        .x = bounds.min.x + size.x * @as(f32, @floatFromInt(corner & 1)),
        .y = bounds.min.y + size.y * @as(f32, @floatFromInt((corner >> 1) & 1)),
        .z = bounds.min.z + size.z * @as(f32, @floatFromInt((corner >> 2) & 1)),
    };
}

fn buildLeaf(
    sampler: Sampler,
    bounds: Aabb,
    coord: [3]u32,
    depth: u8,
    sign_mask: u8,
    corner_values: [8]f32,
    binary_search_steps: u8,
) !?Leaf {
    var qef = qef_mod.QuadraticErrorSolver{};
    var samples: [12]?HermiteSample = [_]?HermiteSample{null} ** 12;
    var sample_count: u8 = 0;

    for (edge_corners, 0..) |edge, edge_index| {
        const a_index = edge[0];
        const b_index = edge[1];
        const va = corner_values[a_index];
        const vb = corner_values[b_index];
        if ((va < 0.0) == (vb < 0.0)) continue;

        const pa = cornerPos(bounds, a_index);
        const pb = cornerPos(bounds, b_index);
        const hit = findEdgeIntersection(sampler, pa, pb, va, vb, binary_search_steps);
        const normal = sampler.gradient(hit);
        qef.addIntersection(hit, normal);
        samples[edge_index] = .{
            .pos = hit,
            .normal = Vec3.normalize(normal),
            .edge_index = @intCast(edge_index),
        };
        sample_count += 1;
    }

    if (sample_count == 0) return null;

    var vertex = qef.solve();
    vertex.pos = Vec3.clamp(vertex.pos, bounds.min, bounds.max);

    return .{
        .coord = coord,
        .depth = depth,
        .bounds = bounds,
        .sign_mask = sign_mask,
        .vertex = vertex,
        .sample_count = sample_count,
        .samples = samples,
    };
}

fn findEdgeIntersection(
    sampler: Sampler,
    pa: Vec3,
    pb: Vec3,
    va: f32,
    vb: f32,
    binary_search_steps: u8,
) Vec3 {
    var lo = pa;
    var hi = pb;
    var lo_v = va;
    var hi_v = vb;

    var step: u8 = 0;
    while (step < binary_search_steps) : (step += 1) {
        const mid = Vec3.lerp(lo, hi, 0.5);
        const mid_v = sampler.sample(mid);
        if ((lo_v < 0.0) == (mid_v < 0.0)) {
            lo = mid;
            lo_v = mid_v;
        } else {
            hi = mid;
            hi_v = mid_v;
        }
    }

    const denom = lo_v - hi_v;
    if (@abs(denom) <= 1e-8) return Vec3.lerp(lo, hi, 0.5);
    const t = std.math.clamp(lo_v / denom, 0.0, 1.0);
    return Vec3.lerp(lo, hi, t);
}

test "octree build finds ambiguous sphere leaves" {
    const allocator = std.testing.allocator;

    const SphereCtx = struct {
        radius: f32,
    };

    const sampleFn = struct {
        fn call(raw: ?*const anyopaque, pos: Vec3) f32 {
            const ctx: *const SphereCtx = @ptrCast(@alignCast(raw.?));
            return @sqrt(pos.x * pos.x + pos.y * pos.y + pos.z * pos.z) - ctx.radius;
        }
    }.call;

    const gradFn = struct {
        fn call(raw: ?*const anyopaque, pos: Vec3) Vec3 {
            _ = raw;
            return Vec3.normalize(pos);
        }
    }.call;

    var sphere = SphereCtx{ .radius = 0.75 };
    var octree = try Octree.build(allocator, .{
        .ctx = &sphere,
        .sampleFn = sampleFn,
        .gradientFn = gradFn,
    }, .{
        .min = .{ .x = -1.0, .y = -1.0, .z = -1.0 },
        .max = .{ .x = 1.0, .y = 1.0, .z = 1.0 },
    }, .{ .max_depth = 4 });
    defer octree.deinit();

    try std.testing.expect(octree.leaves.len > 0);
}
