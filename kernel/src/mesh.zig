const std = @import("std");

pub const types = @import("mesh_types.zig");
pub const output = @import("mesh_output.zig");
pub const octree = @import("mesh_octree.zig");
pub const dc = @import("mesh_dc.zig");

pub const Vec3 = types.Vec3;
pub const Aabb = types.Aabb;
pub const Interval = types.Interval;
pub const Sampler = octree.Sampler;
pub const BuildSettings = octree.BuildSettings;
pub const MeshBuffers = output.MeshBuffers;

pub fn buildMesh(
    allocator: std.mem.Allocator,
    sampler: Sampler,
    bounds: Aabb,
    settings: BuildSettings,
) !MeshBuffers {
    var tree = try octree.build(allocator, sampler, bounds, settings);
    defer tree.deinit();
    return dc.extractMesh(allocator, &tree);
}

