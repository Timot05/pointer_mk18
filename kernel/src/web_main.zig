// Browser entry point.
//
// Flow:
//   1. Host writes serialized MathIR bytes into `ir_upload_buffer` (via
//      `ir_upload_buffer_ptr`), then calls `ir_upload(byte_len)`. We
//      decode straight into our `MathIR` instance, wrap with an identity
//      camera frame, compile to a reg-tape, and bind a `MutableCamera`.
//      No Field-IR lowering step — the F# host (`Server.Lang.MathIrCodec`)
//      ships MathIR-shaped bytes directly.
//   2. Host writes 12 f32s into `camera_buffer` (eye, basis_x, basis_y,
//      basis_z), then calls `set_camera`. We invert basis_z's sign — the
//      F# host's convention is "larger wcz = closer to camera"; the
//      renderer's is "smaller t = closer" — so we flip basis_z here.
//   3. Host calls `render_voxels(tile_w, tile_h, full_w, full_h, tile_x,
//      tile_y, ...)`: a sub-rect of a full image. We render only that
//      tile into the host-visible `gbuffer` (packed tile_w × tile_h,
//      depth lane negated so the WGSL viewer sees ascending-wcz = closer).
//
// Mesh exports are stubs (no meshing in this round).

const std = @import("std");
const m = @import("math_domain.zig");
const math_ir_decode = @import("math_ir_decode.zig");
const cpu_render = @import("cpu_render.zig");

/// Largest tile (width or height). Mirrors the host's MAX_TILE = 1024 in
/// `viewer/Kernel/Background.fs`. The host splits any canvas larger than
/// this into multiple tiles and dispatches each as a separate render.
pub const MAX_TILE_DIM: u32 = 1024;

// Tile-sized output buffers. `cpu_render.render` now does proper per-tile
// rendering (writes only the requested sub-rect at tile-relative
// indexing), so the kernel only needs storage for one tile's worth of
// pixels — not the entire canvas. Host reads `gbuffer` via `gbuffer_ptr`.
var pixels: [MAX_TILE_DIM * MAX_TILE_DIM]u32 = undefined;
var gbuffer: [MAX_TILE_DIM * MAX_TILE_DIM * 4]f32 = undefined;

var camera_buffer: [12]f32 align(4) = undefined;

var math_ir: m.MathIR = .{};
var scene_tape: m.RegTape = undefined;
var mutable_camera: m.MutableCamera = undefined;
var scene_loaded: bool = false;

/// Per-block view tapes — one per visible Field block. Used at hit
/// time by `cpu_render` to determine which block "owns" each pixel
/// for colour assignment. Empty when the F# host shipped no views,
/// in which case `cpu_render` falls back to a single shared colour.
var view_tapes: [math_ir_decode.max_views]m.RegTape = undefined;
var view_cameras: [math_ir_decode.max_views]m.MutableCamera = undefined;
var view_palettes: [math_ir_decode.max_views]u32 = undefined;
var view_count: usize = 0;

// ── IR upload ────────────────────────────────────────────────────────────

pub export fn ir_upload_buffer_ptr() [*]u8 {
    return math_ir_decode.uploadBufferPtr();
}

/// 0 = ok, 1 = bad magic, 2 = bad version, 3 = too many nodes, 4 = too many
/// affines, 5 = too many intrinsics, 6 = too many primitives, 7 = truncated,
/// 8 = bad kind, 9 = camera/tape build failed, 10 = too many views.
pub export fn ir_upload(byte_len: usize) u32 {
    math_ir = .{};
    const decoded = math_ir_decode.decodeInto(byte_len, &math_ir) catch |e| return switch (e) {
        math_ir_decode.Error.BadMagic => 1,
        math_ir_decode.Error.BadVersion => 2,
        math_ir_decode.Error.TooManyNodes => 3,
        math_ir_decode.Error.TooManyAffines => 4,
        math_ir_decode.Error.TooManyIntrinsics => 5,
        math_ir_decode.Error.TooManyPrimitives => 6,
        math_ir_decode.Error.Truncated => 7,
        math_ir_decode.Error.BadKind => 8,
        math_ir_decode.Error.TooManyViews => 10,
    };

    // One CameraAxes shared by the main render tape + every view tape.
    // Each tape gets its own immediates after compile, but they all
    // bind to the same `CameraFrameNodes` ids and so receive identical
    // updates from `set_camera`'s loop below.
    const axes = m.buildCameraAxes(&math_ir, m.CameraFrame.identity) catch return 9;

    const main_root = m.wrapWithAxes(&math_ir, decoded.root, axes) catch return 9;
    scene_tape = m.compileToRegTape(&math_ir, main_root) catch return 9;
    mutable_camera = m.MutableCamera.bind(axes.nodes, &scene_tape) catch return 9;

    view_count = decoded.view_count;
    var i: usize = 0;
    while (i < view_count) : (i += 1) {
        const view_root = m.wrapWithAxes(&math_ir, .{ .id = decoded.views[i].expr_id }, axes) catch return 9;
        view_tapes[i] = m.compileToRegTape(&math_ir, view_root) catch return 9;
        view_cameras[i] = m.MutableCamera.bind(axes.nodes, &view_tapes[i]) catch return 9;
        view_palettes[i] = decoded.views[i].palette_idx;
    }

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
    const frame: m.CameraFrame = .{
        .eye = .{ camera_buffer[0], camera_buffer[1], camera_buffer[2] },
        .basis_x = .{ camera_buffer[3], camera_buffer[4], camera_buffer[5] },
        .basis_y = .{ camera_buffer[6], camera_buffer[7], camera_buffer[8] },
        .basis_z = .{ -camera_buffer[9], -camera_buffer[10], -camera_buffer[11] },
    };
    mutable_camera.setFrame(&scene_tape, frame);
    var i: usize = 0;
    while (i < view_count) : (i += 1) {
        view_cameras[i].setFrame(&view_tapes[i], frame);
    }
    return 0;
}

// ── Render ───────────────────────────────────────────────────────────────

pub export fn gbuffer_ptr() [*]f32 {
    return @ptrCast(&gbuffer);
}

/// Render the requested tile sub-rect of a `full_w × full_h` image into
/// the host-visible `gbuffer`, packed as `tile_w × tile_h × (nx, ny, nz,
/// wcz)`. Returns `tile_w * tile_h` on success, 0 on bad input or
/// unloaded scene.
///
/// `cpu_render.render` writes only this tile's pixels (no full-frame
/// scratch) — the work scales with `tile_w × tile_h`, not the whole
/// canvas. After rendering, we negate the depth lane in place to convert
/// mk21's ascending-t convention to mk18's ascending-wcz convention.
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
    if (tile_w == 0 or tile_h == 0) return 0;
    if (tile_w > MAX_TILE_DIM or tile_h > MAX_TILE_DIM) return 0;
    if (tile_x + tile_w > full_w) return 0;
    if (tile_y + tile_h > full_h) return 0;

    const tile_total: usize = @as(usize, tile_w) * @as(usize, tile_h);
    cpu_render.render(
        pixels[0..tile_total],
        gbuffer[0 .. tile_total * 4],
        full_w, full_h,
        tile_x, tile_y, tile_w, tile_h,
        &scene_tape,
        &math_ir,
        &.{},
        view_tapes[0..view_count],
        view_palettes[0..view_count],
        view_half_w, view_half_h,
        -half, half,
        level,
    );

    // mk21's renderer stores depth as t (positive = away from eye); mk18's
    // WGSL expects wcz (positive = closer). Negate in place.
    var i: usize = 0;
    while (i < tile_total) : (i += 1) {
        gbuffer[i * 4 + 3] = -gbuffer[i * 4 + 3];
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
    return cpu_render.PER_PIXEL_LEVEL;
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
