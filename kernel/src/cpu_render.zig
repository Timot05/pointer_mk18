// CPU SDF renderer — mk18-style screen-space tile pyramid, camera-local.
//
// The tape's input space is camera-local — the host wraps the user's IR
// with `wrapWithCameraFrame` before compiling, so `var_x/y/z` inside the
// tape resolve to world coordinates via the camera basis. The renderer
// here doesn't see the camera basis at all; it marches purely in
// (wcx, wcy, wcz) and lets the tape do the world transform internally.
//
// Pyramid: 128/64/32/16/8 pixel tile sizes. At each level:
//   1. Tile's input-space AABB = (u, v, t) box, axis-aligned trivially.
//   2. Interval-eval the (possibly simplified) tape over that AABB.
//   3. Outside (lo > 0)         → cull.
//      Inside  (hi < 0)         → cull (a closer tile already has the
//                                  surface, since we go front-to-back).
//      Ambiguous                → at leaf, run per-pixel z-scan.
//                                  Otherwise simplify and recurse into 8.
//
// Leaf z-scan uses `decodeRegEvalF4` (4 columns of pixels in parallel);
// on hit, one `decodeRegEvalGrad` per hit pixel produces an analytical
// normal. Output: G-buffer (nx, ny, nz, depth_t) + RGBA8.
//
// Convention: (u, v) are camera-local screen plane (u=horizontal,
// v=vertical, +v up); t is depth from the eye (smaller t = closer).
// depth_t buffer is initialized to +inf; closer hits replace farther.

const std = @import("std");
const m = @import("math_domain.zig");
const decode = @import("math_ir_decode.zig");

const RegTape = m.RegTape;
const MathIR = m.MathIR;
const Vec3 = m.Vec3;
const Box3 = m.Box3;
const Interval = m.Interval;
const Choice = m.Choice;
const Grad = m.Grad;
const F4 = m.F4;
const max_nodes = m.max_nodes;
const max_tape_words = m.max_tape_words;

const TILE_SIZES = [_]u32{ 128, 64, 32, 16, 8 };
pub const FINEST_TILE_LEVEL: u32 = TILE_SIZES.len - 1;
/// Pseudo-level beyond the last tile size. At this level, leaf 8-px tiles
/// run a per-pixel z-scan instead of stamping a single value.
pub const PER_PIXEL_LEVEL: u32 = FINEST_TILE_LEVEL + 1;

const SIMPLIFY_OUT_TAPE: usize = 2 * max_tape_words;

// ── Module-level scratch (single-threaded WASM is fine) ───────────────────

var simd_slots: [max_nodes]F4 = undefined;
var grad4_slots: [max_nodes]m.Grad4 = undefined;
var choice_trace: [max_tape_words]Choice = undefined;

var depth_tapes: [TILE_SIZES.len]RegTape = undefined;

const NEG_LIGHT_DIR: [3]f32 = .{ -0.35, -0.55, 0.75 }; // pre-normalized

// ── Public entry point ────────────────────────────────────────────────────

/// Render a sub-rect `[tile_x..tile_x+tile_w, tile_y..tile_y+tile_h]` of an
/// overall `full_w × full_h` image. Output is packed into `out_pixels` /
/// `out_gbuffer` in row-major `tile_w × tile_h` layout (not full-image
/// stride). Pass `tile_x = 0, tile_y = 0, tile_w = full_w, tile_h = full_h`
/// to render the entire image.
///
/// `view_tapes` / `view_palettes` / `view_kinds` (paired by index)
/// carry per-block surfaces. At each hit pixel the renderer evaluates
/// every view's SDF at the hit point and picks the smallest as the
/// winning block; its palette index + kind drive `shade()`. Empty
/// slices fall back to the default colour (palette index 0, kind 0).
///
/// Camera math (view_half_w/h, ray directions) uses `full_w × full_h` so
/// pixels render the same regardless of which tile they belong to —
/// adjacent tiles produce a seamless image when composited.
pub fn render(
    out_pixels: []u32,
    out_gbuffer: []f32,
    full_w: u32,
    full_h: u32,
    tile_x: u32,
    tile_y: u32,
    tile_w: u32,
    tile_h: u32,
    tape: *const RegTape,
    ir: *const MathIR,
    slots: []const f64,
    view_tapes: []const RegTape,
    view_palettes: []const u32,
    view_kinds: []const u32,
    view_half_w: f32,
    view_half_h: f32,
    near: f32,
    far: f32,
    max_level: u32,
) void {
    const tile_total: usize = @as(usize, tile_w) * @as(usize, tile_h);
    std.debug.assert(out_pixels.len >= tile_total);
    std.debug.assert(out_gbuffer.len >= tile_total * 4);
    std.debug.assert(tile_x + tile_w <= full_w);
    std.debug.assert(tile_y + tile_h <= full_h);
    std.debug.assert(view_tapes.len == view_palettes.len);
    std.debug.assert(view_tapes.len == view_kinds.len);

    initBuffers(out_pixels, out_gbuffer, tile_total);

    const pixel_world = @min(
        2.0 * view_half_w / @as(f32, @floatFromInt(full_w)),
        2.0 * view_half_h / @as(f32, @floatFromInt(full_h)),
    );
    const t_extent = far - near;
    const image_depth_vox: u32 = @intFromFloat(@ceil(t_extent / pixel_world));

    var ctx = RenderCtx{
        .out_pixels = out_pixels,
        .out_gbuffer = out_gbuffer,
        .full_w = full_w,
        .full_h = full_h,
        .tile_x = tile_x,
        .tile_y = tile_y,
        .tile_w = tile_w,
        .tile_h = tile_h,
        .ir = ir,
        .slots = slots,
        .view_tapes = view_tapes,
        .view_palettes = view_palettes,
        .view_kinds = view_kinds,
        .view_half_w = view_half_w,
        .view_half_h = view_half_h,
        .near = near,
        .pixel_world = pixel_world,
        .image_depth_vox = image_depth_vox,
        .max_level = @min(max_level, PER_PIXEL_LEVEL),
    };

    // Iterate only top-level (128-px) tiles that intersect the sub-rect.
    // Z still spans the full depth; t-range matches the full image.
    const root_size = TILE_SIZES[0];
    const xt_lo = tile_x / root_size;
    const xt_hi = (tile_x + tile_w + root_size - 1) / root_size;
    const yt_lo = tile_y / root_size;
    const yt_hi = (tile_y + tile_h + root_size - 1) / root_size;
    const nz = (image_depth_vox + root_size - 1) / root_size;

    var zt: u32 = 0;
    while (zt < nz) : (zt += 1) {
        var yt: u32 = yt_lo;
        while (yt < yt_hi) : (yt += 1) {
            var xt: u32 = xt_lo;
            while (xt < xt_hi) : (xt += 1) {
                renderTileRecurse(&ctx, tape, 0, xt, yt, zt);
            }
        }
    }
}

// ── Internals ─────────────────────────────────────────────────────────────

const RenderCtx = struct {
    out_pixels: []u32,
    out_gbuffer: []f32,
    full_w: u32,
    full_h: u32,
    tile_x: u32,
    tile_y: u32,
    tile_w: u32,
    tile_h: u32,
    ir: *const MathIR,
    slots: []const f64,
    view_tapes: []const RegTape,
    view_palettes: []const u32,
    view_kinds: []const u32,
    view_half_w: f32,
    view_half_h: f32,
    near: f32,
    pixel_world: f32,
    image_depth_vox: u32,
    max_level: u32,
};

/// Maps a (full-image) screen pixel into the tile-relative output buffer
/// index. Caller must guarantee `(px, py)` is inside the sub-rect.
inline fn outputIdx(ctx: *const RenderCtx, px: u32, py: u32) usize {
    return @as(usize, py - ctx.tile_y) * @as(usize, ctx.tile_w) + @as(usize, px - ctx.tile_x);
}

fn initBuffers(pixels: []u32, gbuffer: []f32, total: usize) void {
    const inf = std.math.inf(f32);
    var i: usize = 0;
    while (i < total) : (i += 1) {
        gbuffer[i * 4 + 0] = 0;
        gbuffer[i * 4 + 1] = 0;
        gbuffer[i * 4 + 2] = 0;
        gbuffer[i * 4 + 3] = inf;
        pixels[i] = packRGBA(20, 22, 28, 255);
    }
}

inline fn packRGBA(r: u8, g: u8, b: u8, a: u8) u32 {
    return @as(u32, r) | (@as(u32, g) << 8) | (@as(u32, b) << 16) | (@as(u32, a) << 24);
}

inline fn colorByte(v: f32) u8 {
    const cl = std.math.clamp(v * 255.0, 0.0, 255.0);
    return @intFromFloat(cl);
}

// Pixel → camera-local (u, v) using full-image NDC. Top of image (py=0)
// maps to +v; bottom to -v. Pixel center is offset by 0.5.
inline fn pixelToCameraU(ctx: *const RenderCtx, px: f32) f32 {
    const w: f32 = @floatFromInt(ctx.full_w);
    return (px / w * 2.0 - 1.0) * ctx.view_half_w;
}

inline fn pixelToCameraV(ctx: *const RenderCtx, py: f32) f32 {
    const h: f32 = @floatFromInt(ctx.full_h);
    return (1.0 - py / h * 2.0) * ctx.view_half_h;
}

inline fn pixelToTu(ctx: *const RenderCtx, px: u32) f32 {
    return pixelToCameraU(ctx, @floatFromInt(px));
}
inline fn pixelToTv(ctx: *const RenderCtx, py: u32) f32 {
    return pixelToCameraV(ctx, @floatFromInt(py));
}

fn renderTileRecurse(
    ctx: *RenderCtx,
    tape: *const RegTape,
    level: u32,
    tx: u32,
    ty: u32,
    tz: u32,
) void {
    const size = TILE_SIZES[level];
    // FULL screen-space extent of this tile (clipped to the image).
    // Used for interval testing — over-conservative if the tile bleeds
    // outside the sub-rect, but always safe.
    const full_px_lo = tx * size;
    const full_py_lo = ty * size;
    const vt_lo = tz * size;
    const full_px_hi = @min(full_px_lo + size, ctx.full_w);
    const full_py_hi = @min(full_py_lo + size, ctx.full_h);
    const vt_hi = @min(vt_lo + size, ctx.image_depth_vox);
    if (full_px_lo >= full_px_hi or full_py_lo >= full_py_hi or vt_lo >= vt_hi) return;

    // Pixel-write extent: clip to the requested sub-rect. Tiles entirely
    // outside the sub-rect contribute nothing and exit immediately.
    const sub_x_hi = ctx.tile_x + ctx.tile_w;
    const sub_y_hi = ctx.tile_y + ctx.tile_h;
    const px_lo = @max(full_px_lo, ctx.tile_x);
    const py_lo = @max(full_py_lo, ctx.tile_y);
    const px_hi = @min(full_px_hi, sub_x_hi);
    const py_hi = @min(full_py_hi, sub_y_hi);
    if (px_lo >= px_hi or py_lo >= py_hi) return;

    // Camera-local extents over the FULL tile (not the clipped one) —
    // the interval test is correct that way; recursing in over-large
    // ambiguous regions is wasteful but correct. The clipped px/py
    // bounds are only used when stamping/scanning pixel writes.
    const u_lo = pixelToTu(ctx, full_px_lo);
    const u_hi = pixelToTu(ctx, full_px_hi);
    const v_hi = pixelToTv(ctx, full_py_lo);
    const v_lo = pixelToTv(ctx, full_py_hi);
    const t_lo = ctx.near + @as(f32, @floatFromInt(vt_lo)) * ctx.pixel_world;
    const t_hi = ctx.near + @as(f32, @floatFromInt(vt_hi)) * ctx.pixel_world;

    // Depth-skip: if every pixel in the (clipped) tile already has a
    // closer hit than t_lo, nothing here can contribute.
    if (allPixelsCloserThan(ctx, px_lo, px_hi, py_lo, py_hi, t_lo)) return;

    const box: Box3 = .{
        .xi = .{ .lo = u_lo, .hi = u_hi },
        .yi = .{ .lo = v_lo, .hi = v_hi },
        .zi = .{ .lo = t_lo, .hi = t_hi },
    };
    const bounds = m.decodeRegEvalIntervalWithTrace(tape, ctx.ir, ctx.slots, box, choice_trace[0..]);
    if (bounds.lo > 0.0) return;
    if (bounds.hi < 0.0) return;

    // Sphere-tracing fallback. The interval check above is the cheap
    // bound, but for SDFs like the closed-sketch signed distance
    // (`unsigned * (-compare(|winding|, π))`) it's always loose:
    // `[+pos_lo, +pos_hi] * [-1, +1]` collapses to `[-pos_hi, +pos_hi]`
    // for every tile, no matter how far from the surface. Without this
    // additional check the recursion descends to per-pixel level over
    // the entire viewport — correct but monstrously slow.
    //
    // For a Lipschitz-1 SDF, `|sdf(center)| > tile_radius` is a sound
    // "surface not in tile" predicate. But some pipelines aren't
    // Lipschitz-1 — `wing-remap-preview`'s `twoBody` remap divides by
    // `trailingX - leadingX`, amplifying gradients near the wing tip
    // by ~1/clearance. Pruning by `tile_radius` alone there would
    // falsely skip tiles that actually contain surface.
    //
    // We compute the gradient analytically at the tile center via
    // `decodeRegEvalGrad` (one tape walk that yields value + ∂x∂y∂z
    // in one go) and use `|∇sdf|` as the local Lipschitz estimate.
    // For Lipschitz-1 SDFs this lands at ≈ 1 and reproduces the simple
    // `|sdf| > radius` check; for warped SDFs it scales up
    // automatically. Still a local estimate — the gradient elsewhere
    // in the tile may be larger — but it's strictly more accurate than
    // forward-difference probes at the same cost, and is the analytical
    // counterpart of the same idea.
    const u_c = 0.5 * (u_lo + u_hi);
    const v_c = 0.5 * (v_lo + v_hi);
    const t_c = 0.5 * (t_lo + t_hi);
    const half_u = 0.5 * (u_hi - u_lo);
    const half_v = 0.5 * (v_hi - v_lo);
    const half_t = 0.5 * (t_hi - t_lo);
    const tile_radius = @sqrt(half_u * half_u + half_v * half_v + half_t * half_t);
    const center_grad = m.decodeRegEvalGrad(tape, ctx.ir, ctx.slots, .{ .x = u_c, .y = v_c, .z = t_c });
    const center_val = center_grad[0];
    const gx = center_grad[1];
    const gy = center_grad[2];
    const gz = center_grad[3];
    const grad_mag = @sqrt(gx * gx + gy * gy + gz * gz);
    const lipschitz = @max(@as(f32, 1.0), grad_mag);
    if (@abs(center_val) > tile_radius * lipschitz) return;

    if (level == FINEST_TILE_LEVEL) {
        if (ctx.max_level > FINEST_TILE_LEVEL) {
            // PER_PIXEL_LEVEL: scan per-pixel only. Each ray gets its own
            // hit at native screen-pixel density — no tile-stamp baseline,
            // since stamping fills silhouette-edge pixels with the wrong
            // normal and visibly blockifies the result.
            emitLeafSamples(ctx, tape, px_lo, px_hi, py_lo, py_hi, t_lo, t_hi);
        } else {
            // max_level == FINEST_TILE_LEVEL: stamp the leaf tile as a block.
            stampTileBlock(ctx, tape, px_lo, px_hi, py_lo, py_hi, u_lo, u_hi, v_lo, v_hi, t_lo, t_hi);
        }
        return;
    }
    if (level == ctx.max_level) {
        // Coarse block stamp at a non-leaf tile size — the progressive
        // preview path. All pixels in the tile share the tile-center
        // normal + depth.
        stampTileBlock(ctx, tape, px_lo, px_hi, py_lo, py_hi, u_lo, u_hi, v_lo, v_hi, t_lo, t_hi);
        return;
    }

    const next_level = level + 1;
    var child_tape: *const RegTape = tape;
    if (m.simplifyTape(tape, ctx.ir, choice_trace[0..], &depth_tapes[next_level])) |_| {
        child_tape = &depth_tapes[next_level];
    } else |_| {}

    // Recurse into 8 children, t-axis front-to-back (ascending t).
    const cx = tx * 2;
    const cy = ty * 2;
    const cz = tz * 2;
    var zi: u32 = 0;
    while (zi < 2) : (zi += 1) {
        var yi: u32 = 0;
        while (yi < 2) : (yi += 1) {
            var xi: u32 = 0;
            while (xi < 2) : (xi += 1) {
                renderTileRecurse(ctx, child_tape, next_level, cx + xi, cy + yi, cz + zi);
            }
        }
    }
}

fn allPixelsCloserThan(
    ctx: *const RenderCtx,
    px_lo: u32,
    px_hi: u32,
    py_lo: u32,
    py_hi: u32,
    t_lo: f32,
) bool {
    var py: u32 = py_lo;
    while (py < py_hi) : (py += 1) {
        var px: u32 = px_lo;
        while (px < px_hi) : (px += 1) {
            const idx = outputIdx(ctx, px, py);
            if (ctx.out_gbuffer[idx * 4 + 3] > t_lo) return false;
        }
    }
    return true;
}

// ── Block stamp (coarse-level progressive preview) ───────────────────────

fn stampTileBlock(
    ctx: *RenderCtx,
    tape: *const RegTape,
    px_lo: u32,
    px_hi: u32,
    py_lo: u32,
    py_hi: u32,
    u_lo: f32,
    u_hi: f32,
    v_lo: f32,
    v_hi: f32,
    t_lo: f32,
    t_hi: f32,
) void {
    const u_c = 0.5 * (u_lo + u_hi);
    const v_c = 0.5 * (v_lo + v_hi);
    const t_c = 0.5 * (t_lo + t_hi);

    // One Grad eval at the tile center → value + analytical normal.
    //
    // No sphere-tracing-style guard here. `renderTileRecurse` already
    // ran a Lipschitz-scaled `|sdf| > radius * lipschitz` check before
    // descending to this tile; duplicating a stricter Lipschitz-1 check
    // here would over-prune warped SDFs (visible as missing patches on
    // the wing surface).
    const g = m.decodeRegEvalGrad(tape, ctx.ir, ctx.slots, .{ .x = u_c, .y = v_c, .z = t_c });

    const gx = g[1];
    const gy = g[2];
    const gz = g[3];
    const gmag = @sqrt(gx * gx + gy * gy + gz * gz);
    var nx: f32 = 0;
    var ny: f32 = 1;
    var nz: f32 = 0;
    if (gmag > 1e-9) {
        nx = gx / gmag;
        ny = gy / gmag;
        nz = gz / gmag;
    }
    const palette = pickViewPalette(ctx, u_c, v_c, t_c);
    const color = shade(palette, nx, ny, nz);

    var py: u32 = py_lo;
    while (py < py_hi) : (py += 1) {
        var px: u32 = px_lo;
        while (px < px_hi) : (px += 1) {
            const idx = outputIdx(ctx, px, py);
            // Only stamp if the tile-center is closer than any existing
            // hit. Front-to-back tile order guarantees we get the closer
            // hit when multiple tiles cover the same pixel.
            if (t_c < ctx.out_gbuffer[idx * 4 + 3]) {
                ctx.out_gbuffer[idx * 4 + 0] = nx;
                ctx.out_gbuffer[idx * 4 + 1] = ny;
                ctx.out_gbuffer[idx * 4 + 2] = nz;
                ctx.out_gbuffer[idx * 4 + 3] = t_c;
                ctx.out_pixels[idx] = color;
            }
        }
    }
}

// ── Leaf scan (scalar fallback only — SIMD-bulk path is a follow-up) ─────

fn emitLeafSamples(
    ctx: *RenderCtx,
    tape: *const RegTape,
    px_lo: u32,
    px_hi: u32,
    py_lo: u32,
    py_hi: u32,
    t_lo: f32,
    t_hi: f32,
) void {
    const extent = t_hi - t_lo;
    const nz_f = @ceil(extent / ctx.pixel_world);
    const nz: u32 = @intFromFloat(@max(2.0, nz_f));
    const dt = extent / @as(f32, @floatFromInt(nz));

    var py: u32 = py_lo;
    while (py < py_hi) : (py += 1) {
        const v = pixelToCameraV(ctx, @as(f32, @floatFromInt(py)) + 0.5);

        var px_base: u32 = px_lo;
        while (px_base < px_hi) : (px_base += 4) {
            const lane_count: u32 = @min(4, px_hi - px_base);

            var u_arr: [4]f32 = undefined;
            var idx_arr: [4]usize = undefined;
            var t_db_arr: [4]f32 = undefined;
            inline for (0..4) |l| {
                const lane_offset: u32 = @min(@as(u32, l), lane_count - 1);
                const lane_px = px_base + lane_offset;
                u_arr[l] = pixelToCameraU(ctx, @as(f32, @floatFromInt(lane_px)) + 0.5);
                idx_arr[l] = outputIdx(ctx, lane_px, py);
                t_db_arr[l] = ctx.out_gbuffer[idx_arr[l] * 4 + 3];
            }
            const u_vec: F4 = u_arr;
            const v_vec: F4 = @splat(v);
            const t_db_vec: F4 = t_db_arr;

            // Find each lane's hit (smallest t with sdf<0 AND t<t_db).
            // Pure camera-local — wcx=u, wcy=v, wcz=t. The tape's camera
            // wrapper handles the world transform internally.
            const inf_v: F4 = @splat(std.math.inf(f32));
            var hit_t: F4 = inf_v;
            var zi: u32 = 0;
            while (zi <= nz) : (zi += 1) {
                const t_step = t_lo + @as(f32, @floatFromInt(zi)) * dt;
                const t_vec: F4 = @splat(t_step);

                const sdf = m.decodeRegEvalF4(tape, ctx.ir, ctx.slots, u_vec, v_vec, t_vec, simd_slots[0..]);
                const not_hit_yet: @Vector(4, bool) = hit_t == inf_v;
                const negative: @Vector(4, bool) = sdf < @as(F4, @splat(0));
                const closer_than_db: @Vector(4, bool) = t_vec < t_db_vec;
                const new_hit: @Vector(4, bool) = @select(bool, not_hit_yet, @select(bool, negative, closer_than_db, @as(@Vector(4, bool), @splat(false))), @as(@Vector(4, bool), @splat(false)));
                hit_t = @select(f32, new_hit, t_vec, hit_t);

                if (@reduce(.And, hit_t != inf_v)) break;
            }

            // Batched-Grad path: one tape walk computes (∂x, ∂y, ∂z) for
            // all 4 lanes at once. Lanes that didn't hit (t_hit = inf)
            // are evaluated too — wasted work for those lanes, but we
            // skip writing them. Net savings: ~3-4× fewer tape walks
            // versus per-hit `decodeRegEvalGrad`.
            const grad4 = m.decodeRegEvalGrad4(
                tape, ctx.ir, ctx.slots,
                u_vec, v_vec, hit_t,
                grad4_slots[0..],
            );
            // F4 tag eval — one batched eval per view tape, then take
            // the per-lane min. Lanes that didn't hit produce garbage
            // tags but the per-lane write below skips them.
            const palette4 = pickViewPaletteF4(ctx, u_vec, v_vec, hit_t);
            const dx_arr: [4]f32 = grad4.dx;
            const dy_arr: [4]f32 = grad4.dy;
            const dz_arr: [4]f32 = grad4.dz;
            const hit_t_arr: [4]f32 = hit_t;
            var l: u32 = 0;
            while (l < lane_count) : (l += 1) {
                const t_hit = hit_t_arr[l];
                if (t_hit == std.math.inf(f32)) continue;
                const gx = dx_arr[l];
                const gy = dy_arr[l];
                const gz = dz_arr[l];
                const gmag = @sqrt(gx * gx + gy * gy + gz * gz);
                var nx: f32 = 0;
                var ny: f32 = 1;
                var nz_n: f32 = 0;
                if (gmag > 1e-9) {
                    nx = gx / gmag;
                    ny = gy / gmag;
                    nz_n = gz / gmag;
                }
                const idx = idx_arr[l];
                ctx.out_gbuffer[idx * 4 + 0] = nx;
                ctx.out_gbuffer[idx * 4 + 1] = ny;
                ctx.out_gbuffer[idx * 4 + 2] = nz_n;
                ctx.out_gbuffer[idx * 4 + 3] = t_hit;
                ctx.out_pixels[idx] = shade(palette4[l], nx, ny, nz_n);
            }
        }
    }
}

/// 8-colour palette indexed by `View.palette_idx`. Indexed mod-8 — the
/// host derives indices from block ids so adding/removing intermediate
/// blocks doesn't reshuffle the palette.
///
/// Tuples are (base_r, base_g, base_b, lit_r, lit_g, lit_b) — base is
/// the ambient floor; lit is the fully-illuminated tint. Diffuse lerps
/// between the two.
const PALETTE: [8][6]f32 = .{
    .{ 0.35, 0.45, 0.55, 0.70, 0.85, 0.90 }, // 0: cool grey (legacy default)
    .{ 0.55, 0.30, 0.30, 0.95, 0.60, 0.55 }, // 1: red
    .{ 0.30, 0.50, 0.30, 0.55, 0.90, 0.55 }, // 2: green
    .{ 0.30, 0.40, 0.60, 0.55, 0.70, 0.95 }, // 3: blue
    .{ 0.55, 0.50, 0.25, 0.95, 0.85, 0.45 }, // 4: amber
    .{ 0.45, 0.30, 0.55, 0.80, 0.55, 0.95 }, // 5: violet
    .{ 0.30, 0.55, 0.55, 0.55, 0.95, 0.95 }, // 6: teal
    .{ 0.55, 0.40, 0.30, 0.95, 0.70, 0.50 }, // 7: terracotta
};

inline fn shade(palette_idx: u32, nx: f32, ny: f32, nz: f32) u32 {
    const ldot = nx * NEG_LIGHT_DIR[0] + ny * NEG_LIGHT_DIR[1] + nz * NEG_LIGHT_DIR[2];
    const diffuse = std.math.clamp(ldot * 0.75 + 0.25, 0.0, 1.0);
    const c = PALETTE[palette_idx & 7];
    const r = c[0] + (c[3] - c[0]) * diffuse;
    const g = c[1] + (c[4] - c[1]) * diffuse;
    const b = c[2] + (c[5] - c[2]) * diffuse;
    return packRGBA(colorByte(r), colorByte(g), colorByte(b), 255);
}

/// Pick the winning view's palette index for a hit at `(u, v, t)` in
/// camera-local space. Evaluates each view tape's SDF and returns the
/// palette index with the smallest value. Returns 0 (default colour)
/// when no views were supplied. `view_kinds` is unused here — the
/// kernel renders every non-hidden block as a surface; per-kind
/// overlays (field lines, etc.) are drawn additively by the F# viewer.
inline fn pickViewPalette(ctx: *const RenderCtx, u: f32, v: f32, t: f32) u32 {
    if (ctx.view_tapes.len == 0) return 0;
    var best_val: f32 = std.math.inf(f32);
    var best_palette: u32 = 0;
    var i: usize = 0;
    while (i < ctx.view_tapes.len) : (i += 1) {
        const val = m.decodeRegEvalF32(&ctx.view_tapes[i], ctx.ir, ctx.slots, .{ .x = u, .y = v, .z = t });
        if (val < best_val) {
            best_val = val;
            best_palette = ctx.view_palettes[i];
        }
    }
    return best_palette;
}

/// 4-lane variant — runs each view tape with `decodeRegEvalF4` and
/// returns the per-lane winning palette indices.
inline fn pickViewPaletteF4(ctx: *const RenderCtx, u: F4, v: F4, t: F4) [4]u32 {
    var out: [4]u32 = .{ 0, 0, 0, 0 };
    if (ctx.view_tapes.len == 0) return out;
    var best_val: F4 = @splat(std.math.inf(f32));
    var i: usize = 0;
    while (i < ctx.view_tapes.len) : (i += 1) {
        const val = m.decodeRegEvalF4(&ctx.view_tapes[i], ctx.ir, ctx.slots, u, v, t, simd_slots[0..]);
        const closer: @Vector(4, bool) = val < best_val;
        best_val = @select(f32, closer, val, best_val);
        const palette = ctx.view_palettes[i];
        const closer_arr: [4]bool = closer;
        inline for (0..4) |l| {
            if (closer_arr[l]) out[l] = palette;
        }
    }
    return out;
}
