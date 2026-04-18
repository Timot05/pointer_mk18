const std = @import("std");
const tape_mod = @import("tape.zig");
const scene_mod = @import("scene.zig");
const mesh_mod = @import("mesh.zig");

// ── Scene tape storage ───────────────────────────────────────────────────
var scene_ops: [mesh_mod.MAX_TAPE]tape_mod.Instruction = undefined;
var scene_consts: [mesh_mod.MAX_CONST]f32 = undefined;
var scene_tape: tape_mod.Tape = undefined;
var scene_loaded: bool = false;

// ── Upload buffer (JS writes bytecode here, then calls tape_upload) ──────
// Size the scratch so a maxed-out tape + all its constants fits with room
// to spare. Per-op: 12 bytes; per-const: 4 bytes; header: 16 bytes.
const TAPE_UPLOAD_CAPACITY: usize = 64 * 1024;
var tape_upload_buffer: [TAPE_UPLOAD_CAPACITY]u8 align(4) = undefined;

// ── Stats (read back by JS after each build) ─────────────────────────────
var last_stats: mesh_mod.Stats = .{};

// ── Exports: buffer access ───────────────────────────────────────────────

export fn vertex_buffer_ptr() [*]f32 {
    return mesh_mod.vertexBufferPtr();
}
export fn vertex_buffer_capacity_floats() usize {
    return mesh_mod.vertexBufferCapacityFloats();
}
export fn stats_ptr() [*]const u8 {
    return @ptrCast(&last_stats);
}
export fn stats_size() usize {
    return @sizeOf(mesh_mod.Stats);
}
export fn tape_upload_buffer_ptr() [*]u8 {
    return @ptrCast(&tape_upload_buffer);
}
export fn tape_upload_buffer_capacity() usize {
    return TAPE_UPLOAD_CAPACITY;
}

// ── Exports: scene management ────────────────────────────────────────────

// Builds the hardcoded demo scene into the active tape slot. Used by the
// built-in viewer; main-project hosts should call `tape_upload` instead.
export fn use_default_scene() void {
    var builder = tape_mod.TapeBuilder.init(&scene_ops, &scene_consts);
    const out = scene_mod.build(&builder);
    scene_tape = builder.finalize(out);
    scene_loaded = true;
}

// Parses the upload buffer as a serialized tape and installs it as the
// active scene. Returns 0 on success; a small nonzero code otherwise.
//
// Binary format (little-endian):
//
//   u32 version          (= 1)
//   u32 op_count         (<= MAX_TAPE)
//   u32 const_count      (<= MAX_CONST)
//   u32 output_slot      (index into ops, must be < op_count)
//   f32 constants[const_count]
//   struct { u32 op; u32 a; u32 b; } ops[op_count]   // 12 bytes each
//
// The `op` field uses the discriminant order of `tape_mod.Op`:
//   0=input_x 1=input_y 2=input_z 3=constant 4=neg 5=abs 6=sqrt 7=square
//   8=add 9=sub 10=mul 11=div 12=min 13=max 14=atan2
//
// For constant ops, `a` is the index into the constants array; `b` is 0.
// For unary ops, `a` is the source slot; `b` is 0.
// For binary ops, `a` and `b` are source slots.
//
// Return codes:
//   0: success
//   1: wrong version
//   2: op_count > MAX_TAPE
//   3: const_count > MAX_CONST
//   4: truncated buffer
//   5: unknown op code
export fn tape_upload(byte_len: usize) u32 {
    if (byte_len < 16) return 4;

    const bytes: []const u8 = tape_upload_buffer[0..byte_len];
    const version = std.mem.readInt(u32, bytes[0..4], .little);
    if (version != 1) return 1;
    const op_count = std.mem.readInt(u32, bytes[4..8], .little);
    const const_count = std.mem.readInt(u32, bytes[8..12], .little);
    const output_slot = std.mem.readInt(u32, bytes[12..16], .little);

    if (op_count > mesh_mod.MAX_TAPE) return 2;
    if (const_count > mesh_mod.MAX_CONST) return 3;

    const header_size: usize = 16;
    const consts_size: usize = @as(usize, const_count) * 4;
    const ops_size: usize = @as(usize, op_count) * 12;
    if (byte_len < header_size + consts_size + ops_size) return 4;

    // Copy constants.
    var i: u32 = 0;
    while (i < const_count) : (i += 1) {
        const off: usize = header_size + @as(usize, i) * 4;
        scene_consts[i] = @bitCast(std.mem.readInt(u32, bytes[off..][0..4], .little));
    }

    // Decode ops.
    const max_op: u32 = @intFromEnum(tape_mod.Op.atan2);
    var choice_count: u32 = 0;
    i = 0;
    while (i < op_count) : (i += 1) {
        const off: usize = header_size + consts_size + @as(usize, i) * 12;
        const op_u32 = std.mem.readInt(u32, bytes[off..][0..4], .little);
        if (op_u32 > max_op) return 5;
        const op: tape_mod.Op = @enumFromInt(@as(u8, @intCast(op_u32)));
        const a = std.mem.readInt(u32, bytes[off + 4 ..][0..4], .little);
        const b = std.mem.readInt(u32, bytes[off + 8 ..][0..4], .little);
        scene_ops[i] = .{ .op = op, .a = a, .b = b };
        if (op == .min or op == .max) choice_count += 1;
    }

    scene_tape = .{
        .ops = scene_ops[0..op_count],
        .constants = scene_consts[0..const_count],
        .choice_count = choice_count,
        .output_slot = output_slot,
    };
    scene_loaded = true;
    return 0;
}

export fn scene_tape_op_count() u32 {
    if (!scene_loaded) use_default_scene();
    return @intCast(scene_tape.ops.len);
}

// Upper bound enforced internally on the octree / DC grid. JS should clamp
// its UI control so users don't ask for a depth we silently truncate.
export fn max_supported_depth() u32 {
    return @import("dc.zig").MAX_DC_DEPTH;
}

export fn build_mesh(half: f32, max_depth: u32) u32 {
    if (!scene_loaded) use_default_scene();
    const clamped_depth = @min(max_depth, mesh_mod.MAX_DEPTH - 1);
    const r = mesh_mod.build(&scene_tape, half, clamped_depth);
    last_stats = r.stats;
    return r.vertex_count;
}
