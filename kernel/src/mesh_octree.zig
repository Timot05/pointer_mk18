const std = @import("std");
const types = @import("mesh_types.zig");
const qef_mod = @import("mesh_qef.zig");

pub const corner_offsets = [_][3]u32{
    .{ 0, 0, 0 },
    .{ 1, 0, 0 },
    .{ 0, 1, 0 },
    .{ 1, 1, 0 },
    .{ 0, 0, 1 },
    .{ 1, 0, 1 },
    .{ 0, 1, 1 },
    .{ 1, 1, 1 },
};

pub const edge_corners = [_][2]u8{
    .{ 0, 1 }, .{ 2, 3 }, .{ 4, 5 }, .{ 6, 7 },
    .{ 0, 2 }, .{ 1, 3 }, .{ 4, 6 }, .{ 5, 7 },
    .{ 0, 4 }, .{ 1, 5 }, .{ 2, 6 }, .{ 3, 7 },
};

pub const Sampler = struct {
    ctx: ?*const anyopaque,
    sampleFn: *const fn (?*const anyopaque, types.Vec3) f32,
    gradientFn: *const fn (?*const anyopaque, types.Vec3) types.Vec3,
    intervalFn: *const fn (?*const anyopaque, types.Aabb) types.Interval,

    pub fn sample(self: Sampler, p: types.Vec3) f32 {
        return self.sampleFn(self.ctx, p);
    }

    pub fn gradient(self: Sampler, p: types.Vec3) types.Vec3 {
        return self.gradientFn(self.ctx, p);
    }

    pub fn interval(self: Sampler, bounds: types.Aabb) types.Interval {
        return self.intervalFn(self.ctx, bounds);
    }
};

pub const BuildSettings = struct {
    max_depth: u8 = 7,
    binary_search_steps: u8 = 8,
};

pub const Octree = struct {
    allocator: std.mem.Allocator,
    bounds: types.Aabb,
    max_depth: u8,
    leaves: []types.Leaf,
    leaf_lookup: std.AutoHashMap(u64, u32),

    pub fn deinit(self: *Octree) void {
        self.allocator.free(self.leaves);
        self.leaf_lookup.deinit();
        self.* = .{
            .allocator = self.allocator,
            .bounds = self.bounds,
            .max_depth = self.max_depth,
            .leaves = &.{},
            .leaf_lookup = std.AutoHashMap(u64, u32).init(self.allocator),
        };
    }

    pub fn leafAt(self: *const Octree, coord: [3]u32) ?*const types.Leaf {
        const idx = self.leaf_lookup.get(coordKey(coord)) orelse return null;
        return &self.leaves[idx];
    }
};

pub fn build(allocator: std.mem.Allocator, sampler: Sampler, bounds: types.Aabb, settings: BuildSettings) !Octree {
    if (settings.max_depth == 0 or settings.max_depth > 10) return error.InvalidMaxDepth;

    var leaves = std.ArrayList(types.Leaf).empty;
    errdefer leaves.deinit(allocator);

    try recurse(allocator, &leaves, sampler, bounds, .{ 0, 0, 0 }, 0, settings);

    var lookup = std.AutoHashMap(u64, u32).init(allocator);
    errdefer lookup.deinit();
    try lookup.ensureTotalCapacity(@intCast(leaves.items.len));
    for (leaves.items, 0..) |leaf, i| {
        lookup.putAssumeCapacity(coordKey(leaf.coord), @intCast(i));
    }

    return .{
        .allocator = allocator,
        .bounds = bounds,
        .max_depth = settings.max_depth,
        .leaves = try leaves.toOwnedSlice(allocator),
        .leaf_lookup = lookup,
    };
}

pub fn coordKey(coord: [3]u32) u64 {
    return (@as(u64, coord[0]) << 42) | (@as(u64, coord[1]) << 21) | @as(u64, coord[2]);
}

fn recurse(
    allocator: std.mem.Allocator,
    leaves: *std.ArrayList(types.Leaf),
    sampler: Sampler,
    bounds: types.Aabb,
    coord: [3]u32,
    depth: u8,
    settings: BuildSettings,
) !void {
    const iv = sampler.interval(bounds);
    if (iv.lo > 0.0 or iv.hi < 0.0) return;

    if (depth == settings.max_depth) {
        if (buildLeaf(sampler, bounds, coord, settings)) |leaf| {
            try leaves.append(allocator, leaf);
        }
        return;
    }

    var child_idx: u8 = 0;
    while (child_idx < 8) : (child_idx += 1) {
        const child_corner: u3 = @intCast(child_idx);
        const child_coord = .{
            coord[0] * 2 + @as(u32, child_corner & 1),
            coord[1] * 2 + @as(u32, (child_corner >> 1) & 1),
            coord[2] * 2 + @as(u32, (child_corner >> 2) & 1),
        };
        try recurse(allocator, leaves, sampler, bounds.child(child_corner), child_coord, depth + 1, settings);
    }
}

fn buildLeaf(sampler: Sampler, bounds: types.Aabb, coord: [3]u32, settings: BuildSettings) ?types.Leaf {
    var corner_values: [8]f32 = undefined;
    var sign_mask: u8 = 0;
    inline for (0..8) |i| {
        const p = cornerPoint(bounds, @intCast(i));
        const v = sampler.sample(p);
        corner_values[i] = v;
        if (v < 0.0) sign_mask |= @as(u8, 1) << @intCast(i);
    }
    if (sign_mask == 0 or sign_mask == 0xff) return null;

    var qef = qef_mod.QuadraticErrorSolver{};
    var samples: [12]?types.HermiteSample = .{null} ** 12;
    var sample_count: u8 = 0;

    var edge_idx: usize = 0;
    while (edge_idx < 12) : (edge_idx += 1) {
        const corners = edge_corners[edge_idx];
        const va = corner_values[corners[0]];
        const vb = corner_values[corners[1]];
        if ((va < 0.0) == (vb < 0.0)) continue;

        const pa = cornerPoint(bounds, corners[0]);
        const pb = cornerPoint(bounds, corners[1]);
        const hit = findEdgeIntersection(sampler, pa, pb, va, vb, settings.binary_search_steps);
        const normal = sampler.gradient(hit);
        qef.addIntersection(hit, normal);
        samples[edge_idx] = .{
            .pos = hit,
            .normal = normal,
            .edge_index = @intCast(edge_idx),
        };
        sample_count += 1;
    }

    if (sample_count == 0 or qef.count == 0) return null;
    var vertex = qef.solve();
    vertex.pos = types.Vec3.clamp(vertex.pos, bounds.min, bounds.max);

    return .{
        .coord = coord,
        .bounds = bounds,
        .sign_mask = sign_mask,
        .corner_values = corner_values,
        .vertex = vertex,
        .sample_count = sample_count,
        .samples = samples,
    };
}

fn cornerPoint(bounds: types.Aabb, corner: u8) types.Vec3 {
    return .{
        .x = if ((corner & 1) == 0) bounds.min.x else bounds.max.x,
        .y = if ((corner & 2) == 0) bounds.min.y else bounds.max.y,
        .z = if ((corner & 4) == 0) bounds.min.z else bounds.max.z,
    };
}

fn findEdgeIntersection(sampler: Sampler, pa: types.Vec3, pb: types.Vec3, va0: f32, vb0: f32, steps: u8) types.Vec3 {
    var a = pa;
    var b = pb;
    var va = va0;
    _ = vb0;
    var i: u8 = 0;
    while (i < steps) : (i += 1) {
        const mid = types.Vec3.lerp(a, b, 0.5);
        const vm = sampler.sample(mid);
        if ((va < 0.0) == (vm < 0.0)) {
            a = mid;
            va = vm;
        } else {
            b = mid;
        }
    }
    return types.Vec3.lerp(a, b, 0.5);
}
