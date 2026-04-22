const std = @import("std");

pub const types = @import("mesh_types.zig");
pub const qef = @import("mesh_qef.zig");
pub const output = @import("mesh_output.zig");
pub const octree = @import("mesh_octree.zig");
pub const dc = @import("mesh_dc.zig");

pub const BuildSettings = octree.BuildSettings;
pub const Sampler = octree.Sampler;
pub const MeshBuffers = output.MeshBuffers;

pub fn buildMesh(
    allocator: std.mem.Allocator,
    sampler: Sampler,
    bounds: types.Aabb,
    settings: BuildSettings,
) !MeshBuffers {
    var tree = try octree.Octree.build(allocator, sampler, bounds, settings);
    defer tree.deinit();
    return try dc.extractMesh(allocator, &tree, sampler);
}

test "dual contour build returns triangles for a sphere" {
    const allocator = std.testing.allocator;

    const SphereCtx = struct {
        radius: f32,
    };

    const sampleFn = struct {
        fn call(raw: ?*const anyopaque, pos: types.Vec3) f32 {
            const ctx: *const SphereCtx = @ptrCast(@alignCast(raw.?));
            return @sqrt(pos.x * pos.x + pos.y * pos.y + pos.z * pos.z) - ctx.radius;
        }
    }.call;

    const gradFn = struct {
        fn call(raw: ?*const anyopaque, pos: types.Vec3) types.Vec3 {
            _ = raw;
            return types.Vec3.normalize(pos);
        }
    }.call;

    var sphere = SphereCtx{ .radius = 0.75 };
    var mesh = try buildMesh(allocator, .{
        .ctx = &sphere,
        .sampleFn = sampleFn,
        .gradientFn = gradFn,
    }, .{
        .min = .{ .x = -1.0, .y = -1.0, .z = -1.0 },
        .max = .{ .x = 1.0, .y = 1.0, .z = 1.0 },
    }, .{ .max_depth = 4 });
    defer mesh.deinit(allocator);

    try std.testing.expect(mesh.vertices.len > 0);
    try std.testing.expect(mesh.triangles.len > 0);
}
