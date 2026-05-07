// Native CLI: load a scene, render one frame, dump a PPM.
//
// Usage: zig build render-demo -- <scene_kind>
// Output: render_demo.ppm  (P6 binary, 640×360)
//
// Exists as a quick visual sanity check without spinning up a browser. The
// browser path is the production target; this is just a regression-tag for
// the renderer itself.

const std = @import("std");
const cpu_render = @import("cpu_render.zig");
const complex_scene = @import("complex_scene.zig");
const m = @import("math_domain.zig");

const W: u32 = 640;
const H: u32 = 360;

pub fn main(args: std.process.Init) !void {
    const io = args.io;
    const gpa = args.gpa;
    var iter = try std.process.Args.Iterator.initAllocator(args.minimal.args, gpa);
    defer iter.deinit();
    _ = iter.next();

    var scene_kind: i32 = 0;
    if (iter.next()) |arg| {
        if (std.mem.eql(u8, arg, "complex")) scene_kind = 0
        else if (std.mem.eql(u8, arg, "sphere")) scene_kind = 1
        else if (std.mem.eql(u8, arg, "mega")) scene_kind = 2
        else scene_kind = std.fmt.parseInt(i32, arg, 10) catch 0;
    }

    var scene: complex_scene.Scene = undefined;
    switch (scene_kind) {
        0 => try complex_scene.buildComplexSketchSceneInto(&scene),
        1 => try complex_scene.buildSphereSceneInto(&scene),
        2 => try complex_scene.buildMegaSceneInto(&scene),
        else => return error.UnknownScene,
    }

    // Wrap the scene with a camera frame (lookAt looking at origin from
    // -z, up = +y). This is the camera-baked-into-tape pattern.
    const frame = m.CameraFrame.lookAt(.{ 0, 0, -3 }, .{ 0, 0, 0 }, .{ 0, 1, 0 });
    const wrapped = try m.wrapWithCameraFrame(&scene.ir, scene.root, frame);
    var tape = try m.compileToRegTape(&scene.ir, wrapped.wrapped_root);
    _ = try m.MutableCamera.bind(wrapped.nodes, &tape);

    var pixels: [W * H]u32 = undefined;
    var gbuffer: [W * H * 4]f32 = undefined;

    cpu_render.render(
        pixels[0..],
        gbuffer[0..],
        W,
        H,
        &tape,
        &scene.ir,
        scene.slots[0..],
        1.5,
        0.85,
        0.5,
        6.0,
        cpu_render.PER_PIXEL_LEVEL,
    );
    // Count hit pixels as a smoke statistic.
    var hits: usize = 0;
    var i: usize = 0;
    while (i < W * H) : (i += 1) {
        if (gbuffer[i * 4 + 3] != std.math.inf(f32)) hits += 1;
    }
    std.debug.print("scene={d} hit_pixels={d}/{d} ({d:.1}%)\n", .{
        scene_kind,
        hits,
        @as(usize, W) * @as(usize, H),
        100.0 * @as(f64, @floatFromInt(hits)) / @as(f64, @floatFromInt(@as(usize, W) * @as(usize, H))),
    });

    // Dump P6 PPM. Convert RGBA8 (packed u32) to RGB triplets.
    const out_path = "render_demo.ppm";
    const cwd = std.Io.Dir.cwd();
    const file = try cwd.createFile(io, out_path, .{});
    defer file.close(io);

    var w_buf: [256]u8 = undefined;
    const header = try std.fmt.bufPrint(&w_buf, "P6\n{d} {d}\n255\n", .{ W, H });
    try file.writeStreamingAll(io,header);

    var rgb_row: [W * 3]u8 = undefined;
    var py: u32 = 0;
    while (py < H) : (py += 1) {
        var px: u32 = 0;
        while (px < W) : (px += 1) {
            const idx: usize = @as(usize, py) * @as(usize, W) + @as(usize, px);
            const c = pixels[idx];
            rgb_row[px * 3 + 0] = @intCast(c & 0xFF);
            rgb_row[px * 3 + 1] = @intCast((c >> 8) & 0xFF);
            rgb_row[px * 3 + 2] = @intCast((c >> 16) & 0xFF);
        }
        try file.writeStreamingAll(io,&rgb_row);
    }
    std.debug.print("wrote {s}\n", .{out_path});
}
