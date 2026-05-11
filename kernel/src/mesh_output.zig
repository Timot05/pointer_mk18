const std = @import("std");
const types = @import("mesh_types.zig");

pub const MeshBuffers = struct {
    vertices: []types.Vec3,
    triangles: [][3]u32,

    pub fn deinit(self: *MeshBuffers, allocator: std.mem.Allocator) void {
        allocator.free(self.vertices);
        allocator.free(self.triangles);
        self.* = .{ .vertices = &.{}, .triangles = &.{} };
    }
};

pub const MeshBuilder = struct {
    allocator: std.mem.Allocator,
    vertices: std.ArrayList(types.Vec3),
    triangles: std.ArrayList([3]u32),

    pub fn init(allocator: std.mem.Allocator) MeshBuilder {
        return .{
            .allocator = allocator,
            .vertices = .empty,
            .triangles = .empty,
        };
    }

    pub fn deinit(self: *MeshBuilder) void {
        self.vertices.deinit(self.allocator);
        self.triangles.deinit(self.allocator);
    }

    pub fn addVertex(self: *MeshBuilder, pos: types.Vec3) !u32 {
        const idx: u32 = @intCast(self.vertices.items.len);
        try self.vertices.append(self.allocator, pos);
        return idx;
    }

    pub fn addTriangle(self: *MeshBuilder, a: u32, b: u32, c: u32) !void {
        try self.triangles.append(self.allocator, .{ a, b, c });
    }

    pub fn addQuad(self: *MeshBuilder, a: u32, b: u32, c: u32, d: u32, flip: bool) !void {
        if (flip) {
            try self.addTriangle(a, c, b);
            try self.addTriangle(a, d, c);
        } else {
            try self.addTriangle(a, b, c);
            try self.addTriangle(a, c, d);
        }
    }

    pub fn take(self: *MeshBuilder) !MeshBuffers {
        return .{
            .vertices = try self.vertices.toOwnedSlice(self.allocator),
            .triangles = try self.triangles.toOwnedSlice(self.allocator),
        };
    }
};
