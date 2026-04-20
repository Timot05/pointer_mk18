const std = @import("std");
const tape_mod = @import("tape.zig");
const field_ir = @import("field_ir.zig");
const lower_mod = @import("lower.zig");
const voxel_mod = @import("voxel.zig");

// ── Scene storage ────────────────────────────────────────────────────────
// The host uploads a serialized Field IR tree. Zig decodes it into these
// slabs, lowers to a tape, and renders.

const MAX_NODES: u32 = 256;
const MAX_PRIMS: u32 = 128;

var scene_nodes: [MAX_NODES]field_ir.FieldNode = undefined;
var scene_prims: [MAX_PRIMS]field_ir.SketchPrimitive2d = undefined;
var scene_ops: [voxel_mod.MAX_TAPE]tape_mod.Instruction = undefined;
var scene_consts: [voxel_mod.MAX_CONST]f32 = undefined;
var scene_tape: tape_mod.Tape = undefined;
var scene_camera: ?lower_mod.MutableCamera = null;
var scene_loaded: bool = false;

// ── Upload buffer (JS writes IR bytes, then calls ir_upload) ─────────────
const IR_UPLOAD_CAPACITY: usize = 64 * 1024;
var ir_upload_buffer: [IR_UPLOAD_CAPACITY]u8 align(4) = undefined;

// ── G-buffer output (shared by voxel render) ────────────────────────────
// One rgba32float per pixel: (normal.x, normal.y, normal.z, wcz). Miss
// pixels write wcz = -inf so the host fragment shader can discard them.
const GBUFFER_FLOATS: usize = @as(usize, voxel_mod.MAX_W) * voxel_mod.MAX_H * 4;
var gbuffer: [GBUFFER_FLOATS]f32 = undefined;

// ── Camera upload buffer (JS writes 12 f32s, then calls set_camera) ──────
// Layout: eye(3), basis_x(3), basis_y(3), basis_z(3).
var camera_buffer: [12]f32 align(4) = undefined;

// ── IR binary format ─────────────────────────────────────────────────────
//
// Header (16 B, little-endian):
//   u32 version       (= 1)
//   u32 node_count    (<= MAX_NODES)
//   u32 prim_count    (<= MAX_PRIMS)
//   u32 root          (node index)
//
// Nodes (32 B each, little-endian, packed as):
//   u8  kind
//   u8  sub_kind
//   u16 _pad
//   u32 payload[7]
//
// Kinds:
//   0 primitive   sub_kind: 0=sphere, 1=cylinder, 2=box, 3=half_plane
//   1 translate
//   2 rotate
//   3 boolean     sub_kind: 0=union, 1=subtract, 2=intersect
//   4 unary       sub_kind: 0=thicken, 1=shell
//   5 sketch
//
// Payload semantics (each slot is u32; floats are IEEE-754 bit-cast):
//   primitive
//     sphere       [0]=radius(f32)
//     cylinder     [0]=radius(f32) [1]=height(f32)
//     box          [0]=w(f32) [1]=h(f32) [2]=d(f32)
//     half_plane   [0]=axis(0=x,1=y,2=z) [1]=offset(f32) [2]=flip(0/1)
//   translate      [0]=x(f32) [1]=y(f32) [2]=z(f32) [3]=child_ref
//   rotate         [0..2]=axis(f32) [3]=angle(f32) [4]=child_ref
//   boolean        [0]=radius(f32) [1]=a_ref [2]=b_ref
//   unary          [0]=value(f32) [1]=child_ref
//   sketch         [0]=prims_first [1]=prims_len [2]=closed(0/1) [3]=flip(0/1)
//
// Sketch prims (32 B each) follow the nodes:
//   u8  kind         0=line, 1=circle, 2=arc
//   u8  flags        bit0: clockwise (arc only)
//   u16 _pad
//   f32 payload[7]
//     line    [0]=sx [1]=sy [2]=ex [3]=ey
//     circle  [0]=cx [1]=cy [2]=radius
//     arc     [0]=sx [1]=sy [2]=ex [3]=ey [4]=cx [5]=cy

const IR_HEADER_SIZE: usize = 16;
const IR_NODE_SIZE: usize = 32;
const IR_PRIM_SIZE: usize = 32;

const NK_PRIMITIVE: u8 = 0;
const NK_TRANSLATE: u8 = 1;
const NK_ROTATE: u8 = 2;
const NK_BOOLEAN: u8 = 3;
const NK_UNARY: u8 = 4;
const NK_SKETCH: u8 = 5;

const PK_SPHERE: u8 = 0;
const PK_CYLINDER: u8 = 1;
const PK_BOX: u8 = 2;
const PK_HALF_PLANE: u8 = 3;

const BK_UNION: u8 = 0;
const BK_SUBTRACT: u8 = 1;
const BK_INTERSECT: u8 = 2;

const UK_THICKEN: u8 = 0;
const UK_SHELL: u8 = 1;

const SP_LINE: u8 = 0;
const SP_CIRCLE: u8 = 1;
const SP_ARC: u8 = 2;

// ── Exports: IR upload ───────────────────────────────────────────────────

export fn ir_upload_buffer_ptr() [*]u8 {
    return @ptrCast(&ir_upload_buffer);
}

// Parses the upload buffer as a serialized Field IR tree, decodes it into
// scene_nodes/scene_prims, lowers to a tape, and installs it as the active
// scene. Returns 0 on success, nonzero on error (see codes below).
//
// Error codes:
//   0: success
//   1: wrong version
//   2: node_count > MAX_NODES
//   3: prim_count > MAX_PRIMS
//   4: truncated buffer
//   5: unknown kind / sub_kind
//   6: lowering failed (invalid ref, bad sketch, etc.)
export fn ir_upload(byte_len: usize) u32 {
    if (byte_len < IR_HEADER_SIZE) return 4;

    const bytes: []const u8 = ir_upload_buffer[0..byte_len];
    const version = std.mem.readInt(u32, bytes[0..4], .little);
    if (version != 1) return 1;
    const node_count = std.mem.readInt(u32, bytes[4..8], .little);
    const prim_count = std.mem.readInt(u32, bytes[8..12], .little);
    const root = std.mem.readInt(u32, bytes[12..16], .little);

    if (node_count > MAX_NODES) return 2;
    if (prim_count > MAX_PRIMS) return 3;

    const nodes_end = IR_HEADER_SIZE + @as(usize, node_count) * IR_NODE_SIZE;
    const prims_end = nodes_end + @as(usize, prim_count) * IR_PRIM_SIZE;
    if (byte_len < prims_end) return 4;

    var i: u32 = 0;
    while (i < node_count) : (i += 1) {
        const off = IR_HEADER_SIZE + @as(usize, i) * IR_NODE_SIZE;
        scene_nodes[i] = decodeNode(bytes[off..][0..IR_NODE_SIZE]) catch return 5;
    }

    i = 0;
    while (i < prim_count) : (i += 1) {
        const off = nodes_end + @as(usize, i) * IR_PRIM_SIZE;
        scene_prims[i] = decodePrim(bytes[off..][0..IR_PRIM_SIZE]) catch return 5;
    }

    const tree: field_ir.FieldTree = .{
        .nodes = scene_nodes[0..node_count],
        .sketch_prims = scene_prims[0..prim_count],
        .root = root,
    };

    var builder = tape_mod.TapeBuilder.init(&scene_ops, &scene_consts);
    const lowered = lower_mod.lowerWithOptions(tree, &builder, .{
        .camera_local = lower_mod.CameraFrame.identity,
        .mutable_camera = true,
    }) catch return 6;
    scene_tape = builder.finalize(lowered.output);
    scene_camera = lowered.mutable_camera;
    scene_loaded = true;
    return 0;
}

// Caller writes 12 f32s into `camera_buffer` (via `camera_buffer_ptr`),
// then calls `set_camera()`. The tape evaluates SDF at
// `world = eye + bx*wcx + by*wcy + bz*wcz` for each ray sample.
// Returns 0 on success, 1 if no scene is loaded yet.
export fn camera_buffer_ptr() [*]f32 {
    return @ptrCast(&camera_buffer);
}

export fn set_camera() u32 {
    if (!scene_loaded) return 1;
    const cam = scene_camera orelse return 1;
    cam.setFrame(&scene_tape, .{
        .eye = .{ camera_buffer[0], camera_buffer[1], camera_buffer[2] },
        .basis_x = .{ camera_buffer[3], camera_buffer[4], camera_buffer[5] },
        .basis_y = .{ camera_buffer[6], camera_buffer[7], camera_buffer[8] },
        .basis_z = .{ camera_buffer[9], camera_buffer[10], camera_buffer[11] },
    });
    return 0;
}

fn decodeNode(rec: *const [IR_NODE_SIZE]u8) !field_ir.FieldNode {
    const kind = rec[0];
    const sub = rec[1];
    const p0 = readU32(rec, 4);
    const p1 = readU32(rec, 8);
    const p2 = readU32(rec, 12);
    const p3 = readU32(rec, 16);
    const p4 = readU32(rec, 20);
    // p5/p6 reserved for future use

    return switch (kind) {
        NK_PRIMITIVE => switch (sub) {
            PK_SPHERE => .{ .primitive = .{ .sphere = .{ .radius = @bitCast(p0) } } },
            PK_CYLINDER => .{ .primitive = .{ .cylinder = .{ .radius = @bitCast(p0), .height = @bitCast(p1) } } },
            PK_BOX => .{ .primitive = .{ .box = .{ .width = @bitCast(p0), .height = @bitCast(p1), .depth = @bitCast(p2) } } },
            PK_HALF_PLANE => blk: {
                if (p0 > 2) return error.InvalidIr;
                break :blk .{ .primitive = .{ .half_plane = .{
                    .axis = @enumFromInt(@as(u8, @intCast(p0))),
                    .offset = @bitCast(p1),
                    .flip = p2 != 0,
                } } };
            },
            else => error.InvalidIr,
        },
        NK_TRANSLATE => .{ .translate = .{
            .x = @bitCast(p0),
            .y = @bitCast(p1),
            .z = @bitCast(p2),
            .child = p3,
        } },
        NK_ROTATE => .{ .rotate = .{
            .ax = @bitCast(p0),
            .ay = @bitCast(p1),
            .az = @bitCast(p2),
            .angle = @bitCast(p3),
            .child = p4,
        } },
        NK_BOOLEAN => blk: {
            const op: field_ir.BooleanOp = switch (sub) {
                BK_UNION => .union_,
                BK_SUBTRACT => .subtract,
                BK_INTERSECT => .intersect,
                else => return error.InvalidIr,
            };
            break :blk .{ .boolean = .{ .op = op, .radius = @bitCast(p0), .a = p1, .b = p2 } };
        },
        NK_UNARY => blk: {
            const op: field_ir.UnaryOp = switch (sub) {
                UK_THICKEN => .thicken,
                UK_SHELL => .shell,
                else => return error.InvalidIr,
            };
            break :blk .{ .unary = .{ .op = op, .value = @bitCast(p0), .child = p1 } };
        },
        NK_SKETCH => .{ .sketch = .{
            .prims_first = p0,
            .prims_len = p1,
            .closed = p2 != 0,
            .flip = p3 != 0,
        } },
        else => error.InvalidIr,
    };
}

fn decodePrim(rec: *const [IR_PRIM_SIZE]u8) !field_ir.SketchPrimitive2d {
    const kind = rec[0];
    const flags = rec[1];
    const f0: f32 = @bitCast(readU32(rec, 4));
    const f1: f32 = @bitCast(readU32(rec, 8));
    const f2: f32 = @bitCast(readU32(rec, 12));
    const f3: f32 = @bitCast(readU32(rec, 16));
    const f4: f32 = @bitCast(readU32(rec, 20));
    const f5: f32 = @bitCast(readU32(rec, 24));

    return switch (kind) {
        SP_LINE => .{ .line_segment = .{ .start = .{ f0, f1 }, .end = .{ f2, f3 } } },
        SP_CIRCLE => .{ .circle = .{ .center = .{ f0, f1 }, .radius = f2 } },
        SP_ARC => .{ .arc_center = .{
            .start = .{ f0, f1 },
            .end = .{ f2, f3 },
            .center = .{ f4, f5 },
            .clockwise = (flags & 1) != 0,
        } },
        else => error.InvalidIr,
    };
}

inline fn readU32(rec: *const [IR_NODE_SIZE]u8, comptime offset: usize) u32 {
    return std.mem.readInt(u32, rec[offset..][0..4], .little);
}

// ── Exports: voxel render ───────────────────────────────────────────────

export fn gbuffer_ptr() [*]f32 {
    return @ptrCast(&gbuffer);
}

// Renders one TILE of the current scene and packs its G-buffer into
// `gbuffer` as rgba32float: (normal.x, normal.y, normal.z, wcz) per pixel.
// The host uploads these floats to a WebGPU rgba32float texture and does
// shading + depth projection on the GPU.
//
// The tile covers the sub-rect `[tile_x, tile_x+tile_width) × [tile_y,
// tile_y+tile_height)` of a full image that's `full_width × full_height`
// pixels. `view_half_w/h` describe the view-space half-extents of the
// FULL image (not the tile); the kernel computes per-pixel rays as if
// rendering the full image and then only emits the requested sub-rect.
//
// Callers split larger canvases into tiles each ≤ MAX_W × MAX_H.
//
// Returns tile_width*tile_height on success, 0 on empty scene / oversized
// tile / malformed sub-rect.
export fn render_voxels(
    tile_width: u32, tile_height: u32,
    full_width: u32, full_height: u32,
    tile_x: u32, tile_y: u32,
    view_half_w: f32, view_half_h: f32,
    half: f32,
    level: u32,
) u32 {
    if (!scene_loaded) return 0;
    if (tile_width == 0 or tile_height == 0) return 0;
    if (tile_width > voxel_mod.MAX_W or tile_height > voxel_mod.MAX_H) return 0;
    if (tile_x + tile_width > full_width) return 0;
    if (tile_y + tile_height > full_height) return 0;
    const r = voxel_mod.render(
        &scene_tape,
        tile_width, tile_height,
        full_width, full_height,
        tile_x, tile_y,
        view_half_w, view_half_h,
        half,
        level,
    );
    // Interleave normal + depth into rgba32float (nx, ny, nz, wcz).
    const total: usize = @as(usize, tile_width) * @as(usize, tile_height);
    var i: usize = 0;
    while (i < total) : (i += 1) {
        gbuffer[i * 4 + 0] = r.normal[i * 3 + 0];
        gbuffer[i * 4 + 1] = r.normal[i * 3 + 1];
        gbuffer[i * 4 + 2] = r.normal[i * 3 + 2];
        gbuffer[i * 4 + 3] = r.depth[i];
    }
    return tile_width * tile_height;
}

export fn max_voxel_width() u32 {
    return voxel_mod.MAX_W;
}
export fn max_voxel_height() u32 {
    return voxel_mod.MAX_H;
}
export fn max_render_level() u32 {
    return voxel_mod.PER_PIXEL_LEVEL;
}
