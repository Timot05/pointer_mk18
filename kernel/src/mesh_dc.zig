const std = @import("std");
const types = @import("mesh_types.zig");
const output = @import("mesh_output.zig");
const octree_mod = @import("mesh_octree.zig");

pub fn extractMesh(allocator: std.mem.Allocator, octree: *const octree_mod.Octree) !output.MeshBuffers {
    var builder = output.MeshBuilder.init(allocator);
    errdefer builder.deinit();

    var vertex_lookup = std.AutoHashMap(u64, u32).init(allocator);
    defer vertex_lookup.deinit();
    try vertex_lookup.ensureTotalCapacity(@intCast(octree.leaves.len));

    for (octree.leaves) |leaf| {
        const id = try builder.addVertex(leaf.vertex.pos);
        vertex_lookup.putAssumeCapacity(octree_mod.coordKey(leaf.coord), id);
    }

    var emitted = std.AutoHashMap(u64, void).init(allocator);
    defer emitted.deinit();

    for (octree.leaves) |leaf| {
        var edge_idx: usize = 0;
        while (edge_idx < 12) : (edge_idx += 1) {
            const corners = octree_mod.edge_corners[edge_idx];
            const va = leaf.corner_values[corners[0]];
            const vb = leaf.corner_values[corners[1]];
            if ((va < 0.0) == (vb < 0.0)) continue;

            const global = globalEdgeForCellEdge(leaf.coord, @intCast(edge_idx));
            const key = edgeKey(global.axis, global.start);
            if (emitted.contains(key)) continue;
            try emitted.put(key, {});

            const cells = cellsAroundEdge(global.axis, global.start) orelse continue;
            const a = vertex_lookup.get(octree_mod.coordKey(cells[0])) orelse continue;
            const b = vertex_lookup.get(octree_mod.coordKey(cells[1])) orelse continue;
            const c = vertex_lookup.get(octree_mod.coordKey(cells[2])) orelse continue;
            const d = vertex_lookup.get(octree_mod.coordKey(cells[3])) orelse continue;

            try builder.addQuad(a, b, c, d, va < 0.0);
        }
    }

    return builder.take();
}

const GlobalEdge = struct {
    axis: u2,
    start: [3]u32,
};

fn globalEdgeForCellEdge(coord: [3]u32, edge_idx: u8) GlobalEdge {
    const c = octree_mod.edge_corners[edge_idx];
    const a = octree_mod.corner_offsets[c[0]];
    const b = octree_mod.corner_offsets[c[1]];
    const axis: u2 = if (a[0] != b[0]) 0 else if (a[1] != b[1]) 1 else 2;
    return .{
        .axis = axis,
        .start = .{
            coord[0] + a[0],
            coord[1] + a[1],
            coord[2] + a[2],
        },
    };
}

fn edgeKey(axis: u2, start: [3]u32) u64 {
    return (@as(u64, axis) << 60) | (@as(u64, start[0]) << 40) | (@as(u64, start[1]) << 20) | @as(u64, start[2]);
}

fn cellsAroundEdge(axis: u2, start: [3]u32) ?[4][3]u32 {
    return switch (axis) {
        0 => blk: {
            if (start[1] == 0 or start[2] == 0) break :blk null;
            break :blk .{
                .{ start[0], start[1] - 1, start[2] - 1 },
                .{ start[0], start[1], start[2] - 1 },
                .{ start[0], start[1], start[2] },
                .{ start[0], start[1] - 1, start[2] },
            };
        },
        1 => blk: {
            if (start[0] == 0 or start[2] == 0) break :blk null;
            break :blk .{
                .{ start[0] - 1, start[1], start[2] - 1 },
                .{ start[0] - 1, start[1], start[2] },
                .{ start[0], start[1], start[2] },
                .{ start[0], start[1], start[2] - 1 },
            };
        },
        2 => blk: {
            if (start[0] == 0 or start[1] == 0) break :blk null;
            break :blk .{
                .{ start[0] - 1, start[1] - 1, start[2] },
                .{ start[0], start[1] - 1, start[2] },
                .{ start[0], start[1], start[2] },
                .{ start[0] - 1, start[1], start[2] },
            };
        },
        else => unreachable,
    };
}
