// Decode mk18's Field-IR wire format into a typed `ParsedScene`.
//
// The host (mk18's F# editor) writes a serialized tree into the static
// `ir_upload_buffer`, then calls `decode(byte_len)`. The wire format is
// identical to the one accepted by mk18's `kernel/src/main.zig` — we keep it
// byte-for-byte compatible so mk18's `viewer/Kernel/IrCodec.fs` and
// `FieldToIr.fs` need no changes.
//
// Wire format (header 16 B, all little-endian):
//   u32 version (=1), u32 node_count, u32 prim_count, u32 root
// Nodes (32 B each): u8 kind, u8 sub_kind, u16 _pad, u32 payload[7]
// Sketch prims (32 B each): u8 kind, u8 flags, u16 _pad, f32 payload[7]
//
// Slot baking: the host evaluates slots at serialize time and embeds the
// resulting f32s in node payloads — we don't carry a slot table.

const std = @import("std");

pub const NodeRef = u32;
pub const PrimRef = u32;

pub const Axis = enum(u8) { x, y, z };
pub const BooleanOp = enum(u8) { union_, subtract, intersect };
pub const UnaryOp = enum(u8) { thicken, shell };

pub const Primitive = union(enum) {
    sphere: struct { radius: f32 },
    cylinder: struct { radius: f32, height: f32 },
    box: struct { width: f32, height: f32, depth: f32 },
    half_plane: struct { axis: Axis, offset: f32, flip: bool },
};

pub const SketchPrimitive2d = union(enum) {
    line_segment: struct { start: [2]f32, end: [2]f32 },
    circle: struct { center: [2]f32, radius: f32 },
    arc_center: struct {
        start: [2]f32,
        end: [2]f32,
        center: [2]f32,
        clockwise: bool,
    },
};

pub const Node = union(enum) {
    primitive: Primitive,
    translate: struct { x: f32, y: f32, z: f32, child: NodeRef },
    rotate: struct { ax: f32, ay: f32, az: f32, angle: f32, child: NodeRef },
    boolean: struct { op: BooleanOp, radius: f32, a: NodeRef, b: NodeRef },
    unary: struct { op: UnaryOp, value: f32, child: NodeRef },
    sketch: struct {
        prims_first: PrimRef,
        prims_len: u32,
        closed: bool,
        flip: bool,
    },
};

pub const ParsedScene = struct {
    nodes: []const Node,
    prims: []const SketchPrimitive2d,
    root: NodeRef,
};

pub const Error = error{
    Truncated,
    BadVersion,
    TooManyNodes,
    TooManyPrims,
    BadKind,
};

pub const MAX_NODES: u32 = 256;
pub const MAX_PRIMS: u32 = 128;
pub const IR_UPLOAD_CAPACITY: usize = 64 * 1024;

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

var ir_upload_buffer: [IR_UPLOAD_CAPACITY]u8 align(4) = undefined;
var scene_nodes: [MAX_NODES]Node = undefined;
var scene_prims: [MAX_PRIMS]SketchPrimitive2d = undefined;

pub fn uploadBufferPtr() [*]u8 {
    return @ptrCast(&ir_upload_buffer);
}

pub fn decode(byte_len: usize) Error!ParsedScene {
    if (byte_len < IR_HEADER_SIZE) return Error.Truncated;
    const bytes: []const u8 = ir_upload_buffer[0..byte_len];

    const version = std.mem.readInt(u32, bytes[0..4], .little);
    if (version != 1) return Error.BadVersion;
    const node_count = std.mem.readInt(u32, bytes[4..8], .little);
    const prim_count = std.mem.readInt(u32, bytes[8..12], .little);
    const root = std.mem.readInt(u32, bytes[12..16], .little);

    if (node_count > MAX_NODES) return Error.TooManyNodes;
    if (prim_count > MAX_PRIMS) return Error.TooManyPrims;

    const nodes_end = IR_HEADER_SIZE + @as(usize, node_count) * IR_NODE_SIZE;
    const prims_end = nodes_end + @as(usize, prim_count) * IR_PRIM_SIZE;
    if (byte_len < prims_end) return Error.Truncated;

    var i: u32 = 0;
    while (i < node_count) : (i += 1) {
        const off = IR_HEADER_SIZE + @as(usize, i) * IR_NODE_SIZE;
        scene_nodes[i] = try decodeNode(bytes[off..][0..IR_NODE_SIZE]);
    }

    i = 0;
    while (i < prim_count) : (i += 1) {
        const off = nodes_end + @as(usize, i) * IR_PRIM_SIZE;
        scene_prims[i] = try decodePrim(bytes[off..][0..IR_PRIM_SIZE]);
    }

    return .{
        .nodes = scene_nodes[0..node_count],
        .prims = scene_prims[0..prim_count],
        .root = root,
    };
}

fn decodeNode(rec: *const [IR_NODE_SIZE]u8) Error!Node {
    const kind = rec[0];
    const sub = rec[1];
    const p0 = readU32(rec, 4);
    const p1 = readU32(rec, 8);
    const p2 = readU32(rec, 12);
    const p3 = readU32(rec, 16);
    const p4 = readU32(rec, 20);

    return switch (kind) {
        NK_PRIMITIVE => switch (sub) {
            PK_SPHERE => .{ .primitive = .{ .sphere = .{ .radius = @bitCast(p0) } } },
            PK_CYLINDER => .{ .primitive = .{ .cylinder = .{
                .radius = @bitCast(p0),
                .height = @bitCast(p1),
            } } },
            PK_BOX => .{ .primitive = .{ .box = .{
                .width = @bitCast(p0),
                .height = @bitCast(p1),
                .depth = @bitCast(p2),
            } } },
            PK_HALF_PLANE => blk: {
                if (p0 > 2) return Error.BadKind;
                break :blk .{ .primitive = .{ .half_plane = .{
                    .axis = @enumFromInt(@as(u8, @intCast(p0))),
                    .offset = @bitCast(p1),
                    .flip = p2 != 0,
                } } };
            },
            else => Error.BadKind,
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
            const op: BooleanOp = switch (sub) {
                BK_UNION => .union_,
                BK_SUBTRACT => .subtract,
                BK_INTERSECT => .intersect,
                else => return Error.BadKind,
            };
            break :blk .{ .boolean = .{
                .op = op,
                .radius = @bitCast(p0),
                .a = p1,
                .b = p2,
            } };
        },
        NK_UNARY => blk: {
            const op: UnaryOp = switch (sub) {
                UK_THICKEN => .thicken,
                UK_SHELL => .shell,
                else => return Error.BadKind,
            };
            break :blk .{ .unary = .{
                .op = op,
                .value = @bitCast(p0),
                .child = p1,
            } };
        },
        NK_SKETCH => .{ .sketch = .{
            .prims_first = p0,
            .prims_len = p1,
            .closed = p2 != 0,
            .flip = p3 != 0,
        } },
        else => Error.BadKind,
    };
}

fn decodePrim(rec: *const [IR_PRIM_SIZE]u8) Error!SketchPrimitive2d {
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
        else => Error.BadKind,
    };
}

inline fn readU32(rec: *const [IR_NODE_SIZE]u8, comptime offset: usize) u32 {
    return std.mem.readInt(u32, rec[offset..][0..4], .little);
}
