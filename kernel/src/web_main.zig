// Browser entry point — exports match mk18's host contract
// (`pointer_mk18/viewer/Kernel/Wasm.fs`) so mk21's renderer can drop in as
// the kernel WASM without F# host changes.
//
// Flow:
//   1. Host writes serialized Field IR bytes into `ir_upload_buffer` (via
//      `ir_upload_buffer_ptr`), then calls `ir_upload(byte_len)`. We
//      decode, lower (`field_lower`), wrap with an identity camera frame,
//      compile to a reg-tape, and bind a `MutableCamera`.
//   2. Host writes 12 f32s into `camera_buffer` (eye, basis_x, basis_y,
//      basis_z), then calls `set_camera`. We invert basis_z's sign — mk18
//      and mk21 disagree on its meaning (mk18: −forward, mk21: +forward).
//   3. Host calls `render_voxels(tile_w, tile_h, full_w, full_h, tile_x,
//      tile_y, ...)`: a sub-rect of a full image. We render the full
//      frame into `full_gbuffer`, then copy the tile sub-rect into the
//      host-visible `gbuffer` (packed tile_w × tile_h, depth lane
//      negated so mk18's WGSL sees ascending-wcz = closer).
//
// Mesh exports are stubs (mk21 has no meshing in this round).

const std = @import("std");
const m = @import("math_domain.zig");
const scene_decode = @import("scene_decode.zig");
const cpu_render = @import("cpu_render.zig");

/// Largest full-canvas dimensions we support. Beyond this `render_voxels`
/// returns 0. 1080p covers typical retina laptop logical canvases.
pub const MAX_W: u32 = 1920;
pub const MAX_H: u32 = 1080;

/// Largest tile (width or height). Mirrors the host's MAX_TILE = 1024 in
/// `viewer/Kernel/Background.fs`.
pub const MAX_TILE_DIM: u32 = 1024;

// Scratch — `cpu_render.render` writes the entire full_w × full_h frame
// into these. We then copy just the requested tile sub-rect out.
var full_pixels: [MAX_W * MAX_H]u32 = undefined;
var full_gbuffer: [MAX_W * MAX_H * 4]f32 = undefined;

// Host-visible gbuffer. Tile-packed: contains the most-recently-rendered
// tile's `tile_w * tile_h * 4` floats starting at offset 0, in row-major
// order. The host reads via `gbuffer_ptr` + `gbufferView(tileW, tileH)`.
var gbuffer: [MAX_TILE_DIM * MAX_TILE_DIM * 4]f32 = undefined;

var camera_buffer: [12]f32 align(4) = undefined;

var math_ir: m.MathIR = .{};
var scene_tape: m.RegTape = undefined;
var mutable_camera: m.MutableCamera = undefined;
var scene_loaded: bool = false;

// ── IR upload ────────────────────────────────────────────────────────────

pub export fn ir_upload_buffer_ptr() [*]u8 {
    return scene_decode.uploadBufferPtr();
}

/// 0 = ok, 1 = bad version, 2 = too many nodes, 3 = too many prims,
/// 4 = truncated, 5 = bad kind, 6 = lowering failed.
pub export fn ir_upload(byte_len: usize) u32 {
    const parsed = scene_decode.decode(byte_len) catch |e| return switch (e) {
        scene_decode.Error.BadVersion => 1,
        scene_decode.Error.TooManyNodes => 2,
        scene_decode.Error.TooManyPrims => 3,
        scene_decode.Error.Truncated => 4,
        scene_decode.Error.BadKind => 5,
    };

    math_ir = .{};
    const root = m.lowerField(&parsed, &math_ir) catch return 6;
    const wrapped = m.wrapWithCameraFrame(&math_ir, root, m.CameraFrame.identity) catch return 6;
    scene_tape = m.compileToRegTape(&math_ir, wrapped.wrapped_root) catch return 6;
    mutable_camera = m.MutableCamera.bind(wrapped.nodes, &scene_tape) catch return 6;
    scene_loaded = true;
    return 0;
}

// ── Camera ───────────────────────────────────────────────────────────────

pub export fn camera_buffer_ptr() [*]f32 {
    return @ptrCast(&camera_buffer);
}

/// 0 = ok, 1 = no scene loaded.
///
/// mk18's host writes `basis_z = -forward` (its convention is "larger wcz
/// = closer to camera"); mk21's renderer expects `basis_z = +forward`
/// ("smaller t = closer"). We flip the sign here so the rest of the
/// pipeline reads basis_z in its native sense.
pub export fn set_camera() u32 {
    if (!scene_loaded) return 1;
    mutable_camera.setFrame(&scene_tape, .{
        .eye = .{ camera_buffer[0], camera_buffer[1], camera_buffer[2] },
        .basis_x = .{ camera_buffer[3], camera_buffer[4], camera_buffer[5] },
        .basis_y = .{ camera_buffer[6], camera_buffer[7], camera_buffer[8] },
        .basis_z = .{ -camera_buffer[9], -camera_buffer[10], -camera_buffer[11] },
    });
    return 0;
}

// ── Render ───────────────────────────────────────────────────────────────

pub export fn gbuffer_ptr() [*]f32 {
    return @ptrCast(&gbuffer);
}

/// Render a tile sub-rect of a `full_w × full_h` image and pack the result
/// into the host-visible `gbuffer` as `tile_w × tile_h × (nx, ny, nz, wcz)`.
/// Returns `tile_w * tile_h` on success, 0 on bad input or unloaded scene.
///
/// Wasteful first cut: renders the entire full frame on every call, then
/// copies just the tile sub-rect. The host tiles a typical canvas into
/// 1–4 pieces, so this is 1–4× the optimal work — a real per-tile path is
/// a future optimization.
pub export fn render_voxels(
    tile_w: u32, tile_h: u32,
    full_w: u32, full_h: u32,
    tile_x: u32, tile_y: u32,
    view_half_w: f32, view_half_h: f32,
    half: f32,
    level: u32,
) u32 {
    if (!scene_loaded) return 0;
    if (full_w == 0 or full_h == 0) return 0;
    if (full_w > MAX_W or full_h > MAX_H) return 0;
    if (tile_w == 0 or tile_h == 0) return 0;
    if (tile_w > MAX_TILE_DIM or tile_h > MAX_TILE_DIM) return 0;
    if (tile_x + tile_w > full_w) return 0;
    if (tile_y + tile_h > full_h) return 0;

    const full_total: usize = @as(usize, full_w) * @as(usize, full_h);
    cpu_render.render(
        full_pixels[0..full_total],
        full_gbuffer[0 .. full_total * 4],
        full_w,
        full_h,
        &scene_tape,
        &math_ir,
        &.{},
        view_half_w,
        view_half_h,
        -half,
        half,
        level,
    );

    // Copy the tile sub-rect, negating the depth lane so mk18's WGSL
    // shader sees ascending-wcz = closer-to-camera (mk21 stores ascending-t
    // = away-from-eye internally).
    var ty: u32 = 0;
    while (ty < tile_h) : (ty += 1) {
        const src_pixel_row: usize = @as(usize, tile_y + ty) * @as(usize, full_w) + @as(usize, tile_x);
        const dst_pixel_row: usize = @as(usize, ty) * @as(usize, tile_w);
        var tx: u32 = 0;
        while (tx < tile_w) : (tx += 1) {
            const src = (src_pixel_row + tx) * 4;
            const dst = (dst_pixel_row + tx) * 4;
            gbuffer[dst + 0] = full_gbuffer[src + 0];
            gbuffer[dst + 1] = full_gbuffer[src + 1];
            gbuffer[dst + 2] = full_gbuffer[src + 2];
            gbuffer[dst + 3] = -full_gbuffer[src + 3];
        }
    }

    return tile_w * tile_h;
}

pub export fn max_voxel_width() u32 {
    return MAX_TILE_DIM;
}

pub export fn max_voxel_height() u32 {
    return MAX_TILE_DIM;
}

pub export fn max_render_level() u32 {
    // DEBUG: cap at FINEST_TILE_LEVEL to force the host to stop refining at
    // the leaf-tile-stamp step, skipping per-pixel scan entirely.
    return cpu_render.FINEST_TILE_LEVEL;
}

// ── Mesh exports (stubbed; mk21 has no DC meshing this round) ────────────

pub export fn mesh_build(half_extent: f32, max_depth: u32) u32 {
    _ = half_extent;
    _ = max_depth;
    return 1;
}

pub export fn mesh_vertices_ptr() usize {
    return 0;
}

pub export fn mesh_vertices_len() u32 {
    return 0;
}

pub export fn mesh_triangles_ptr() usize {
    return 0;
}

pub export fn mesh_triangles_len() u32 {
    return 0;
}
