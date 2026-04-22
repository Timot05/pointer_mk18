const std = @import("std");
const types = @import("mesh_types.zig");
const octree_mod = @import("mesh_octree.zig");
const output_mod = @import("mesh_output.zig");

const Vec3 = types.Vec3;
const Aabb = types.Aabb;

pub fn extractMesh(
    allocator: std.mem.Allocator,
    octree: *const octree_mod.Octree,
    sampler: octree_mod.Sampler,
) !output_mod.MeshBuffers {
    var mesh = output_mod.MeshBuilder.init(allocator);
    defer mesh.deinit();

    var vertex_indices = std.AutoHashMap(u64, u32).init(allocator);
    defer vertex_indices.deinit();

    for (octree.leaves) |leaf| {
        const idx = try mesh.addVertex(leaf.vertex.pos);
        try vertex_indices.put(octree_mod.coordKey(leaf.coord), idx);
    }

    const cells_per_axis: u32 = @as(u32, 1) << @as(u5, @intCast(octree.max_depth));
    try emitAxisQuads(&mesh, &vertex_indices, octree, sampler, .x, cells_per_axis);
    try emitAxisQuads(&mesh, &vertex_indices, octree, sampler, .y, cells_per_axis);
    try emitAxisQuads(&mesh, &vertex_indices, octree, sampler, .z, cells_per_axis);

    return try mesh.take();
}

const Axis = enum { x, y, z };

fn emitAxisQuads(
    mesh: *output_mod.MeshBuilder,
    vertex_indices: *const std.AutoHashMap(u64, u32),
    octree: *const octree_mod.Octree,
    sampler: octree_mod.Sampler,
    axis: Axis,
    cells_per_axis: u32,
) !void {
    switch (axis) {
        .x => {
            var i: u32 = 0;
            while (i < cells_per_axis) : (i += 1) {
                var j: u32 = 1;
                while (j < cells_per_axis) : (j += 1) {
                    var k: u32 = 1;
                    while (k < cells_per_axis) : (k += 1) {
                        try emitQuadForEdge(mesh, vertex_indices, octree, sampler, axis, i, j, k);
                    }
                }
            }
        },
        .y => {
            var i: u32 = 1;
            while (i < cells_per_axis) : (i += 1) {
                var j: u32 = 0;
                while (j < cells_per_axis) : (j += 1) {
                    var k: u32 = 1;
                    while (k < cells_per_axis) : (k += 1) {
                        try emitQuadForEdge(mesh, vertex_indices, octree, sampler, axis, i, j, k);
                    }
                }
            }
        },
        .z => {
            var i: u32 = 1;
            while (i < cells_per_axis) : (i += 1) {
                var j: u32 = 1;
                while (j < cells_per_axis) : (j += 1) {
                    var k: u32 = 0;
                    while (k < cells_per_axis) : (k += 1) {
                        try emitQuadForEdge(mesh, vertex_indices, octree, sampler, axis, i, j, k);
                    }
                }
            }
        },
    }
}

fn emitQuadForEdge(
    mesh: *output_mod.MeshBuilder,
    vertex_indices: *const std.AutoHashMap(u64, u32),
    octree: *const octree_mod.Octree,
    sampler: octree_mod.Sampler,
    axis: Axis,
    i: u32,
    j: u32,
    k: u32,
) !void {
    const edge = worldEdge(octree.bounds, octree.max_depth, axis, i, j, k);
    const va = sampler.sample(edge.a);
    const vb = sampler.sample(edge.b);
    if ((va < 0.0) == (vb < 0.0)) return;

    const coords = cellsAroundEdge(axis, i, j, k);
    const a = vertex_indices.get(octree_mod.coordKey(coords[0])) orelse return;
    const b = vertex_indices.get(octree_mod.coordKey(coords[1])) orelse return;
    const c = vertex_indices.get(octree_mod.coordKey(coords[2])) orelse return;
    const d = vertex_indices.get(octree_mod.coordKey(coords[3])) orelse return;

    try mesh.addQuad(a, b, d, c, va < 0.0);
}

fn cellsAroundEdge(axis: Axis, i: u32, j: u32, k: u32) [4][3]u32 {
    return switch (axis) {
        .x => .{
            .{ i, j - 1, k - 1 },
            .{ i, j, k - 1 },
            .{ i, j - 1, k },
            .{ i, j, k },
        },
        .y => .{
            .{ i - 1, j, k - 1 },
            .{ i, j, k - 1 },
            .{ i - 1, j, k },
            .{ i, j, k },
        },
        .z => .{
            .{ i - 1, j - 1, k },
            .{ i, j - 1, k },
            .{ i - 1, j, k },
            .{ i, j, k },
        },
    };
}

fn worldEdge(bounds: Aabb, max_depth: u8, axis: Axis, i: u32, j: u32, k: u32) struct { a: Vec3, b: Vec3 } {
    const n = @as(f32, @floatFromInt(@as(u32, 1) << @as(u5, @intCast(max_depth))));
    const size = bounds.size();
    const step = .{ .x = size.x / n, .y = size.y / n, .z = size.z / n };

    const p = Vec3.init(
        bounds.min.x + @as(f32, @floatFromInt(i)) * step.x,
        bounds.min.y + @as(f32, @floatFromInt(j)) * step.y,
        bounds.min.z + @as(f32, @floatFromInt(k)) * step.z,
    );

    return switch (axis) {
        .x => .{ .a = p, .b = Vec3.add(p, .{ .x = step.x, .y = 0.0, .z = 0.0 }) },
        .y => .{ .a = p, .b = Vec3.add(p, .{ .x = 0.0, .y = step.y, .z = 0.0 }) },
        .z => .{ .a = p, .b = Vec3.add(p, .{ .x = 0.0, .y = 0.0, .z = step.z }) },
    };
}
