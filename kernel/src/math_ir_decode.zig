// Decode mk18's MathIR wire format directly into a `MathIR` struct.
//
// Wire format (all little-endian, header 32 bytes, "MIR2"):
//   u32 magic           = 0x4D495232 ("MIR2")
//   u32 version         = 3
//   u32 node_count
//   u32 affine_count
//   u32 intrinsic_count
//   u32 primitive_count
//   u32 root            (node id of the unioned render tree)
//   u32 view_count
//
// Sections in declaration order:
//   nodes:      node_count      × 32 B  (Node)
//   affines:    affine_count    × 48 B  (Affine3, 12 × i32 expr id)
//   intrinsics: intrinsic_count × 48 B  (Intrinsic + 4-byte pad)
//   primitives: primitive_count × 64 B  (SketchPrimitive + 7-byte pad)
//   views:      view_count      × 12 B  (i32 expr_id, u32 palette_idx, u32 kind)
//
// Views: one per visible Field block. The kernel renders each separately
// so the winning block at each hit pixel can be coloured by `palette_idx`
// and shaded by `kind` (0 = opaque surface, 1 = field-lines, 2 =
// isosurface). `view_count == 0` is valid — the kernel falls back to a
// default colour for the whole `root` surface.
//
// Per-element layouts mirror `math_ir.zig`'s in-memory shapes byte-for-byte
// where natural; padding is added only to keep section strides aligned.

const std = @import("std");
const m = @import("math_ir.zig");

pub const Error = error{
    Truncated,
    BadMagic,
    BadVersion,
    TooManyNodes,
    TooManyAffines,
    TooManyIntrinsics,
    TooManyPrimitives,
    TooManyViews,
    BadKind,
};

pub const IR_UPLOAD_CAPACITY: usize = 256 * 1024;

pub const MAGIC: u32 = 0x4D495232;
pub const VERSION: u32 = 3;

/// Maximum visible Field blocks the renderer can colour by tag.
/// Keep modest — each view holds its own ~50 KB RegTape on the kernel.
pub const max_views: usize = 16;

pub const View = struct {
    expr_id: i32,
    palette_idx: u32,
    kind: u32,
};

/// Shading mode encoded in `View.kind`. Mirrors `BlockVisibility` minus
/// `VHidden` (hidden blocks aren't sent as views). The F# host only
/// ships `VIsosurface` views today — `VFieldLines` is drawn by the
/// host's own GPU pass and never reaches the kernel — so kind=0 is the
/// only value seen in practice. The constants and bounds check stay
/// in place so future per-kind renderer dispatch can plug in here.
pub const VIEW_KIND_ISOSURFACE: u32 = 0;
pub const VIEW_KIND_FIELD_LINES: u32 = 1;
const VIEW_KIND_MAX: u32 = 1;

const HEADER_BYTES: usize = 32;
const NODE_BYTES: usize = 32;
const AFFINE_BYTES: usize = 48;
const INTRINSIC_BYTES: usize = 48;
const PRIMITIVE_BYTES: usize = 64;
const VIEW_BYTES: usize = 12;

var ir_upload_buffer: [IR_UPLOAD_CAPACITY]u8 align(8) = undefined;

pub fn uploadBufferPtr() [*]u8 {
    return @ptrCast(&ir_upload_buffer);
}

pub const Decoded = struct {
    root: m.Expr,
    views: [max_views]View = undefined,
    view_count: usize = 0,
};

pub fn decodeInto(byte_len: usize, ir: *m.MathIR) Error!Decoded {
    if (byte_len < HEADER_BYTES) return Error.Truncated;
    const bytes: []const u8 = ir_upload_buffer[0..byte_len];

    const magic = readU32(bytes, 0);
    if (magic != MAGIC) return Error.BadMagic;
    const version = readU32(bytes, 4);
    if (version != VERSION) return Error.BadVersion;

    const node_count = readU32(bytes, 8);
    const affine_count = readU32(bytes, 12);
    const intrinsic_count = readU32(bytes, 16);
    const primitive_count = readU32(bytes, 20);
    const root = readU32(bytes, 24);
    const view_count = readU32(bytes, 28);

    if (node_count > m.max_nodes) return Error.TooManyNodes;
    if (affine_count > m.max_affines) return Error.TooManyAffines;
    if (intrinsic_count > m.max_intrinsics) return Error.TooManyIntrinsics;
    if (primitive_count > m.max_primitives) return Error.TooManyPrimitives;
    if (view_count > max_views) return Error.TooManyViews;

    const nodes_off = HEADER_BYTES;
    const affines_off = nodes_off + @as(usize, node_count) * NODE_BYTES;
    const intrinsics_off = affines_off + @as(usize, affine_count) * AFFINE_BYTES;
    const primitives_off = intrinsics_off + @as(usize, intrinsic_count) * INTRINSIC_BYTES;
    const views_off = primitives_off + @as(usize, primitive_count) * PRIMITIVE_BYTES;
    const total = views_off + @as(usize, view_count) * VIEW_BYTES;
    if (byte_len < total) return Error.Truncated;

    var i: u32 = 0;
    while (i < node_count) : (i += 1) {
        const off = nodes_off + @as(usize, i) * NODE_BYTES;
        ir.nodes[i] = try decodeNode(bytes[off..][0..NODE_BYTES]);
    }
    ir.node_count = node_count;

    i = 0;
    while (i < affine_count) : (i += 1) {
        const off = affines_off + @as(usize, i) * AFFINE_BYTES;
        ir.affines[i] = decodeAffine(bytes[off..][0..AFFINE_BYTES]);
    }
    ir.affine_count = affine_count;

    i = 0;
    while (i < intrinsic_count) : (i += 1) {
        const off = intrinsics_off + @as(usize, i) * INTRINSIC_BYTES;
        ir.intrinsics[i] = try decodeIntrinsic(bytes[off..][0..INTRINSIC_BYTES]);
    }
    ir.intrinsic_count = intrinsic_count;

    i = 0;
    while (i < primitive_count) : (i += 1) {
        const off = primitives_off + @as(usize, i) * PRIMITIVE_BYTES;
        ir.primitives[i] = try decodePrimitive(bytes[off..][0..PRIMITIVE_BYTES]);
    }
    ir.primitive_count = primitive_count;

    if (root >= node_count) return Error.Truncated;

    var decoded: Decoded = .{ .root = .{ .id = @intCast(root) }, .view_count = view_count };
    i = 0;
    while (i < view_count) : (i += 1) {
        const off = views_off + @as(usize, i) * VIEW_BYTES;
        const expr_id = readI32(bytes, off);
        if (expr_id < 0 or @as(u32, @intCast(expr_id)) >= node_count) return Error.Truncated;
        const kind = readU32(bytes, off + 8);
        if (kind > VIEW_KIND_MAX) return Error.BadKind;
        decoded.views[i] = .{
            .expr_id = expr_id,
            .palette_idx = readU32(bytes, off + 4),
            .kind = kind,
        };
    }
    return decoded;
}

fn decodeNode(rec: *const [NODE_BYTES]u8) Error!m.Node {
    const kind_raw = readI32(rec, 0);
    if (kind_raw < 0 or kind_raw > 7) return Error.BadKind;
    const kind: m.NodeKind = @enumFromInt(kind_raw);
    return .{
        .kind = kind,
        .op = readI32(rec, 4),
        .a = readI32(rec, 8),
        .b = readI32(rec, 12),
        .c = readI32(rec, 16),
        .d = readI32(rec, 20),
        .value = readF64(rec, 24),
    };
}

fn decodeAffine(rec: *const [AFFINE_BYTES]u8) m.Affine3 {
    return .{
        .m00 = .{ .id = readI32(rec, 0) },
        .m01 = .{ .id = readI32(rec, 4) },
        .m02 = .{ .id = readI32(rec, 8) },
        .m03 = .{ .id = readI32(rec, 12) },
        .m10 = .{ .id = readI32(rec, 16) },
        .m11 = .{ .id = readI32(rec, 20) },
        .m12 = .{ .id = readI32(rec, 24) },
        .m13 = .{ .id = readI32(rec, 28) },
        .m20 = .{ .id = readI32(rec, 32) },
        .m21 = .{ .id = readI32(rec, 36) },
        .m22 = .{ .id = readI32(rec, 40) },
        .m23 = .{ .id = readI32(rec, 44) },
    };
}

fn decodeIntrinsic(rec: *const [INTRINSIC_BYTES]u8) Error!m.Intrinsic {
    const kind_raw = readI32(rec, 0);
    if (kind_raw < 0 or kind_raw > 2) return Error.BadKind;
    const plane_raw = readI32(rec, 4);
    if (plane_raw < 0 or plane_raw > 2) return Error.BadKind;
    return .{
        .kind = @enumFromInt(kind_raw),
        .plane = @enumFromInt(plane_raw),
        .primitive_start = readI32(rec, 8),
        .primitive_count = readI32(rec, 12),
        .closed = rec[16] != 0,
        .flip = rec[17] != 0,
        // bytes 18..19: pad
        .ox = readI32(rec, 20),
        .oy = readI32(rec, 24),
        .oz = readI32(rec, 28),
        .ax = readI32(rec, 32),
        .ay = readI32(rec, 36),
        .az = readI32(rec, 40),
        // bytes 44..47: pad
    };
}

fn decodePrimitive(rec: *const [PRIMITIVE_BYTES]u8) Error!m.SketchPrimitive {
    const kind_raw = readI32(rec, 0);
    if (kind_raw < 0 or kind_raw > 5) return Error.BadKind;
    return .{
        .kind = @enumFromInt(kind_raw),
        .p0 = .{ .x = readI32(rec, 4), .y = readI32(rec, 8) },
        .p1 = .{ .x = readI32(rec, 12), .y = readI32(rec, 16) },
        .p2 = .{ .x = readI32(rec, 20), .y = readI32(rec, 24) },
        .p3 = .{ .x = readI32(rec, 28), .y = readI32(rec, 32) },
        .radius = readI32(rec, 36),
        .chord = readI32(rec, 40),
        .max_camber = readI32(rec, 44),
        .camber_pos = readI32(rec, 48),
        .thickness = readI32(rec, 52),
        .clockwise = rec[56] != 0,
        // bytes 57..63: pad
    };
}

inline fn readU32(bytes: []const u8, offset: usize) u32 {
    return std.mem.readInt(u32, bytes[offset..][0..4], .little);
}

inline fn readI32(bytes: []const u8, offset: usize) i32 {
    return std.mem.readInt(i32, bytes[offset..][0..4], .little);
}

inline fn readF64(bytes: []const u8, offset: usize) f64 {
    const raw = std.mem.readInt(u64, bytes[offset..][0..8], .little);
    return @bitCast(raw);
}
