const std = @import("std");
const field_ir = @import("field_ir.zig");
const lower_mod = @import("lower.zig");
const tape_mod = @import("tape.zig");
const voxel_mod = @import("voxel.zig");
const image_mod = @import("image.zig");

fn writeU32Be(out: *std.ArrayList(u8), value: u32) !void {
    var buf: [4]u8 = undefined;
    std.mem.writeInt(u32, &buf, value, .big);
    try out.appendSlice(std.heap.page_allocator, &buf);
}

fn crc32(bytes: []const u8) u32 {
    var crc: u32 = 0xffff_ffff;
    for (bytes) |byte| {
        crc ^= byte;
        var i: u32 = 0;
        while (i < 8) : (i += 1) {
            const mask: u32 = 0 -% (crc & 1);
            crc = (crc >> 1) ^ (0xedb8_8320 & mask);
        }
    }
    return ~crc;
}

fn adler32(bytes: []const u8) u32 {
    var s1: u32 = 1;
    var s2: u32 = 0;
    for (bytes) |byte| {
        s1 = (s1 + byte) % 65521;
        s2 = (s2 + s1) % 65521;
    }
    return (s2 << 16) | s1;
}

fn writeChunk(out: *std.ArrayList(u8), chunk_type: [4]u8, data: []const u8) !void {
    try writeU32Be(out, @intCast(data.len));
    try out.appendSlice(std.heap.page_allocator, &chunk_type);
    try out.appendSlice(std.heap.page_allocator, data);

    var crc_buf = try std.heap.page_allocator.alloc(u8, 4 + data.len);
    defer std.heap.page_allocator.free(crc_buf);
    @memcpy(crc_buf[0..4], &chunk_type);
    @memcpy(crc_buf[4..], data);
    try writeU32Be(out, crc32(crc_buf));
}

fn appendStoredDeflateBlocks(out: *std.ArrayList(u8), raw: []const u8) !void {
    var offset: usize = 0;
    while (offset < raw.len) {
        const remaining = raw.len - offset;
        const block_len: u16 = @intCast(@min(remaining, 65535));
        const final: u8 = if (offset + block_len == raw.len) 1 else 0;
        try out.append(std.heap.page_allocator, final);

        var len_buf: [2]u8 = undefined;
        var nlen_buf: [2]u8 = undefined;
        std.mem.writeInt(u16, &len_buf, block_len, .little);
        std.mem.writeInt(u16, &nlen_buf, ~block_len, .little);
        try out.appendSlice(std.heap.page_allocator, &len_buf);
        try out.appendSlice(std.heap.page_allocator, &nlen_buf);
        try out.appendSlice(std.heap.page_allocator, raw[offset .. offset + block_len]);
        offset += block_len;
    }
}

fn writeRgbaPng(path: []const u8, width: u32, height: u32, rgba: []const u8) !void {
    const row_bytes: usize = @as(usize, width) * 4;
    const raw_len: usize = @as(usize, height) * (1 + row_bytes);

    var raw = try std.heap.page_allocator.alloc(u8, raw_len);
    defer std.heap.page_allocator.free(raw);

    var src_off: usize = 0;
    var dst_off: usize = 0;
    var y: u32 = 0;
    while (y < height) : (y += 1) {
        raw[dst_off] = 0;
        dst_off += 1;
        @memcpy(raw[dst_off .. dst_off + row_bytes], rgba[src_off .. src_off + row_bytes]);
        dst_off += row_bytes;
        src_off += row_bytes;
    }

    var zlib: std.ArrayList(u8) = .empty;
    defer zlib.deinit(std.heap.page_allocator);
    try zlib.appendSlice(std.heap.page_allocator, &[_]u8{ 0x78, 0x01 });
    try appendStoredDeflateBlocks(&zlib, raw);

    var adler_buf: [4]u8 = undefined;
    std.mem.writeInt(u32, &adler_buf, adler32(raw), .big);
    try zlib.appendSlice(std.heap.page_allocator, &adler_buf);

    const io = std.Io.Threaded.global_single_threaded.io();
    if (std.fs.path.dirname(path)) |dir_path| {
        try std.Io.Dir.cwd().createDirPath(io, dir_path);
    }
    var png: std.ArrayList(u8) = .empty;
    defer png.deinit(std.heap.page_allocator);

    try png.appendSlice(std.heap.page_allocator, &[_]u8{ 0x89, 'P', 'N', 'G', 0x0d, 0x0a, 0x1a, 0x0a });

    var ihdr: [13]u8 = undefined;
    std.mem.writeInt(u32, ihdr[0..4], width, .big);
    std.mem.writeInt(u32, ihdr[4..8], height, .big);
    ihdr[8] = 8;
    ihdr[9] = 6;
    ihdr[10] = 0;
    ihdr[11] = 0;
    ihdr[12] = 0;
    try writeChunk(&png, .{ 'I', 'H', 'D', 'R' }, &ihdr);
    try writeChunk(&png, .{ 'I', 'D', 'A', 'T' }, zlib.items);
    try writeChunk(&png, .{ 'I', 'E', 'N', 'D' }, &.{});

    const file = try std.Io.Dir.createFileAbsolute(io, path, .{ .truncate = true });
    defer file.close(io);
    try file.writeStreamingAll(io, png.items);
}

fn isForeground(rgba: []const u8, width: u32, x: u32, y: u32) bool {
    const idx = (@as(usize, y) * width + @as(usize, x)) * 4;
    return rgba[idx + 0] != 10 or rgba[idx + 1] != 10 or rgba[idx + 2] != 13;
}

fn rowSpan(rgba: []const u8, width: u32, height: u32, y: u32) ?struct { first: u32, last: u32 } {
    _ = height;
    var first: ?u32 = null;
    var x: u32 = 0;
    while (x < width) : (x += 1) {
        if (isForeground(rgba, width, x, y)) {
            if (first == null) first = x;
        } else if (first != null) {
            var x2 = x;
            while (x2 < width) : (x2 += 1) {
                if (isForeground(rgba, width, x2, y)) {
                    return null;
                }
            }
            return .{ .first = first.?, .last = x - 1 };
        }
    }
    if (first) |f| return .{ .first = f, .last = width - 1 };
    return null;
}

fn buildSphereTape(
    radius: f32,
    ops: []tape_mod.Instruction,
    consts: []f32,
) !tape_mod.Tape {
    var nodes: [4]field_ir.FieldNode = undefined;
    var prims: [1]field_ir.SketchPrimitive2d = undefined;
    var ir_builder = field_ir.FieldBuilder.init(&nodes, &prims);
    const sphere = ir_builder.sphere(radius);
    const tree = ir_builder.finalize(sphere);

    var tape_builder = tape_mod.TapeBuilder.init(ops, consts);
    const out = try lower_mod.lower(tree, &tape_builder);
    return tape_builder.finalize(out);
}

test "rendered pure sphere rows are contiguous" {
    var ops: [128]tape_mod.Instruction = undefined;
    var consts: [64]f32 = undefined;
    const tape = try buildSphereTape(1.0, &ops, &consts);

    const width: u32 = 256;
    const height: u32 = 256;
    var pixels: [256 * 256 * 4]u8 = undefined;

    const r = voxel_mod.render(&tape, width, height, 1.25, 1.25, 1.5, voxel_mod.PER_PIXEL_LEVEL);
    try std.testing.expect(r.stats.pixels_written > 0);
    image_mod.resolveGbuffer(&pixels, width, height, r.depth, r.normal, 1.25, 1.25, .normal_rgb);

    var occupied_rows: u32 = 0;
    var y: u32 = 0;
    while (y < height) : (y += 1) {
        const span = rowSpan(&pixels, width, height, y);
        if (span != null) occupied_rows += 1;
        if (span) |_| {} else {
            var has_any = false;
            var x: u32 = 0;
            while (x < width) : (x += 1) {
                if (isForeground(&pixels, width, x, y)) {
                    has_any = true;
                    break;
                }
            }
            try std.testing.expect(!has_any);
        }
    }
    try std.testing.expect(occupied_rows > 0);
}

test "rendered pure sphere is vertically symmetric in width" {
    var ops: [128]tape_mod.Instruction = undefined;
    var consts: [64]f32 = undefined;
    const tape = try buildSphereTape(1.0, &ops, &consts);

    const width: u32 = 256;
    const height: u32 = 256;
    var pixels: [256 * 256 * 4]u8 = undefined;

    const r = voxel_mod.render(&tape, width, height, 1.25, 1.25, 1.5, voxel_mod.PER_PIXEL_LEVEL);
    image_mod.resolveGbuffer(&pixels, width, height, r.depth, r.normal, 1.25, 1.25, .normal_rgb);

    var top: ?u32 = null;
    var bottom: ?u32 = null;
    var y: u32 = 0;
    while (y < height) : (y += 1) {
        if (rowSpan(&pixels, width, height, y) != null) {
            if (top == null) top = y;
            bottom = y;
        }
    }
    try std.testing.expect(top != null and bottom != null);

    var max_abs_diff: i32 = 0;
    var offset: u32 = 0;
    while (top.? + offset <= bottom.? - offset) : (offset += 1) {
        const a = rowSpan(&pixels, width, height, top.? + offset);
        const b = rowSpan(&pixels, width, height, bottom.? - offset);
        if (a == null or b == null) continue;
        const wa = a.?.last - a.?.first;
        const wb = b.?.last - b.?.first;
        const diff = @as(i32, @intCast(wa)) - @as(i32, @intCast(wb));
        max_abs_diff = @max(max_abs_diff, @as(i32, @intCast(@abs(diff))));
    }
    std.debug.print("pure sphere vertical symmetry max row-width diff = {d}\n", .{max_abs_diff});
    try std.testing.expect(max_abs_diff <= 2);
}

test "render sphere normal debug image to png" {
    var nodes: [8]field_ir.FieldNode = undefined;
    var prims: [1]field_ir.SketchPrimitive2d = undefined;
    var ir_builder = field_ir.FieldBuilder.init(&nodes, &prims);
    const sphere = ir_builder.sphere(1.0);
    const cut_sphere = ir_builder.sphere(0.6);
    const moved_cut = ir_builder.translate(0.5, 0.0, 0.5, cut_sphere);
    const root = ir_builder.subtract(sphere, moved_cut, 0.0);
    const tree = ir_builder.finalize(root);

    var ops: [128]tape_mod.Instruction = undefined;
    var consts: [64]f32 = undefined;
    var tape_builder = tape_mod.TapeBuilder.init(&ops, &consts);
    const out = try lower_mod.lower(tree, &tape_builder);
    const tape = tape_builder.finalize(out);

    const width: u32 = 1000;
    const height: u32 = 1000;
    const half: f32 = 1.5;
    var pixels: [1000 * 1000 * 4]u8 = undefined;

    const io = std.Io.Threaded.global_single_threaded.io();
    const t0 = std.Io.Clock.Timestamp.now(io, .awake);
    const r = voxel_mod.render(&tape, width, height, 1.25, 1.25, half, voxel_mod.PER_PIXEL_LEVEL);
    const elapsed_ns: i64 = @intCast(t0.untilNow(io).raw.nanoseconds);
    const render_ms = @as(f64, @floatFromInt(elapsed_ns)) / 1_000_000.0;
    std.debug.print("voxel render {d}x{d} took {d:.2} ms\n", .{ width, height, render_ms });
    try std.testing.expect(r.stats.pixels_written > 0);

    image_mod.resolveGbuffer(&pixels, width, height, r.depth, r.normal, 1.25, 1.25, .normal_rgb);

    const center = (@as(usize, height / 2) * width + @as(usize, width / 2)) * 4;
    try std.testing.expect(pixels[center + 0] != 10 or pixels[center + 1] != 10 or pixels[center + 2] != 13);

    const normals_path = "artifacts/sphere_normals.png";
    try writeRgbaPng(normals_path, width, height, &pixels);
    std.debug.print("wrote sphere normal PNG to {s}\n", .{normals_path});

    image_mod.resolveDepthGray(&pixels, width, height, r.depth, half, -half);
    const depth_path = "artifacts/sphere_depth.png";
    try writeRgbaPng(depth_path, width, height, &pixels);
    std.debug.print("wrote sphere depth PNG to {s}\n", .{depth_path});
}
