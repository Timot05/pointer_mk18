const std = @import("std");
const m = @import("math_domain.zig");
const complex_scene = @import("complex_scene.zig");
const dot = @import("math_ir_dot.zig");

pub fn main(args: std.process.Init) !void {
    const io = args.io;
    const gpa = args.gpa;

    var iter = try std.process.Args.Iterator.initAllocator(args.minimal.args, gpa);
    defer iter.deinit();
    _ = iter.next();
    const scene_arg = iter.next() orelse {
        std.debug.print("usage: ir-dump <complex_sketch|sphere|mega>\n", .{});
        return error.MissingSceneArg;
    };

    var scene: complex_scene.Scene = undefined;
    if (std.mem.eql(u8, scene_arg, "complex_sketch")) {
        try complex_scene.buildComplexSketchSceneInto(&scene);
    } else if (std.mem.eql(u8, scene_arg, "sphere")) {
        try complex_scene.buildSphereSceneInto(&scene);
    } else if (std.mem.eql(u8, scene_arg, "mega")) {
        try complex_scene.buildMegaSceneInto(&scene);
    } else {
        std.debug.print("unknown scene: {s}\n", .{scene_arg});
        return error.UnknownScene;
    }

    var tape_storage: m.RegTape = undefined;
    var tape_ptr: ?*const m.RegTape = null;
    var tape_err: ?anyerror = null;
    if (m.compileToRegTape(&scene.ir, scene.root)) |t| {
        tape_storage = t;
        tape_ptr = &tape_storage;
    } else |err| {
        tape_err = err;
    }

    var path_buf: [128]u8 = undefined;
    const path = try std.fmt.bufPrint(&path_buf, "{s}.dot", .{scene_arg});

    const cwd = std.Io.Dir.cwd();
    const file = try cwd.createFile(io, path, .{});
    defer file.close(io);

    const out = dot.Out{ .io = io, .file = file };
    if (tape_ptr) |t| {
        try dot.writeTapeComments(out, t, &scene.ir);
    } else if (tape_err) |err| {
        var buf: [128]u8 = undefined;
        const msg = try std.fmt.bufPrint(&buf, "// (tape compilation failed: {s})\n//\n", .{@errorName(err)});
        try out.writeAll(msg);
    }
    try dot.writeDot(out, &scene.ir, scene.root);

    if (tape_ptr) |t| {
        std.debug.print("wrote {s} (ir nodes={d}, tape ops={d})\n", .{ path, scene.ir.node_count, t.instruction_count });
    } else if (tape_err) |err| {
        std.debug.print("wrote {s} (ir nodes={d}, tape skipped: {s})\n", .{ path, scene.ir.node_count, @errorName(err) });
    }
}
