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
var choice_trace: [max_tape_words]Choice = undefined;

var depth_tapes: [TILE_SIZES.len]RegTape = undefined;

const NEG_LIGHT_DIR: [3]f32 = .{ -0.35, -0.55, 0.75 }; // pre-normalized

// ── Public entry point ────────────────────────────────────────────────────

pub fn render(
    out_pixels: []u32,
    out_gbuffer: []f32,
    width: u32,
    height: u32,
    tape: *const RegTape,
    ir: *const MathIR,
    slots: []const f64,
    view_half_w: f32,
    view_half_h: f32,
    near: f32,
    far: f32,
    max_level: u32,
) void {
    const total: usize = @as(usize, width) * @as(usize, height);
    std.debug.assert(out_pixels.len >= total);
    std.debug.assert(out_gbuffer.len >= total * 4);

    initBuffers(out_pixels, out_gbuffer, total);

    const pixel_world = @min(
        2.0 * view_half_w / @as(f32, @floatFromInt(width)),
        2.0 * view_half_h / @as(f32, @floatFromInt(height)),
    );
    const t_extent = far - near;
    const image_depth_vox: u32 = @intFromFloat(@ceil(t_extent / pixel_world));

    var ctx = RenderCtx{
        .out_pixels = out_pixels,
        .out_gbuffer = out_gbuffer,
        .width = width,
        .height = height,
        .ir = ir,
        .slots = slots,
        .view_half_w = view_half_w,
        .view_half_h = view_half_h,
        .near = near,
        .pixel_world = pixel_world,
        .image_depth_vox = image_depth_vox,
        .max_level = @min(max_level, PER_PIXEL_LEVEL),
    };

    // Iterate top-level (128-px) tiles, front-to-back (ascending t).
    const root_size = TILE_SIZES[0];
    const nx = (width + root_size - 1) / root_size;
    const ny = (height + root_size - 1) / root_size;
    const nz = (image_depth_vox + root_size - 1) / root_size;

    var zt: u32 = 0;
    while (zt < nz) : (zt += 1) {
        var yt: u32 = 0;
        while (yt < ny) : (yt += 1) {
            var xt: u32 = 0;
            while (xt < nx) : (xt += 1) {
                renderTileRecurse(&ctx, tape, 0, xt, yt, zt);
            }
        }
    }
}

// ── Internals ─────────────────────────────────────────────────────────────

const RenderCtx = struct {
    out_pixels: []u32,
    out_gbuffer: []f32,
    width: u32,
    height: u32,
    ir: *const MathIR,
    slots: []const f64,
    view_half_w: f32,
    view_half_h: f32,
    near: f32,
    pixel_world: f32,
    image_depth_vox: u32,
    max_level: u32,
};

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
    const w: f32 = @floatFromInt(ctx.width);
    return (px / w * 2.0 - 1.0) * ctx.view_half_w;
}

inline fn pixelToCameraV(ctx: *const RenderCtx, py: f32) f32 {
    const h: f32 = @floatFromInt(ctx.height);
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
    const px_lo = tx * size;
    const py_lo = ty * size;
    const vt_lo = tz * size;
    const px_hi = @min(px_lo + size, ctx.width);
    const py_hi = @min(py_lo + size, ctx.height);
    const vt_hi = @min(vt_lo + size, ctx.image_depth_vox);
    if (px_lo >= px_hi or py_lo >= py_hi or vt_lo >= vt_hi) return;

    // Camera-local extents — directly axis-aligned in tape input space.
    const u_lo = pixelToTu(ctx, px_lo);
    const u_hi = pixelToTu(ctx, px_hi);
    // py_lo (top of tile) maps to higher v; flip sign.
    const v_hi = pixelToTv(ctx, py_lo);
    const v_lo = pixelToTv(ctx, py_hi);
    const t_lo = ctx.near + @as(f32, @floatFromInt(vt_lo)) * ctx.pixel_world;
    const t_hi = ctx.near + @as(f32, @floatFromInt(vt_hi)) * ctx.pixel_world;

    // Depth-skip: if every pixel in the tile already has a closer hit
    // than t_lo (the tile's nearest face), nothing here can contribute.
    if (allPixelsCloserThan(ctx, px_lo, px_hi, py_lo, py_hi, t_lo)) return;

    const box: Box3 = .{
        .xi = .{ .lo = u_lo, .hi = u_hi },
        .yi = .{ .lo = v_lo, .hi = v_hi },
        .zi = .{ .lo = t_lo, .hi = t_hi },
    };
    const bounds = m.decodeRegEvalIntervalWithTrace(tape, ctx.ir, ctx.slots, box, choice_trace[0..]);
    if (bounds.lo > 0.0) return;
    if (bounds.hi < 0.0) return;

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
        const row: usize = @as(usize, py) * @as(usize, ctx.width);
        var px: u32 = px_lo;
        while (px < px_hi) : (px += 1) {
            const idx = row + px;
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
    const color = shade(nx, ny, nz);

    var py: u32 = py_lo;
    while (py < py_hi) : (py += 1) {
        const row: usize = @as(usize, py) * @as(usize, ctx.width);
        var px: u32 = px_lo;
        while (px < px_hi) : (px += 1) {
            const idx = row + px;
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
        const row: usize = @as(usize, py) * @as(usize, ctx.width);

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
                idx_arr[l] = row + lane_px;
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

            const hit_t_arr: [4]f32 = hit_t;
            var l: u32 = 0;
            while (l < lane_count) : (l += 1) {
                const t_hit = hit_t_arr[l];
                if (t_hit == std.math.inf(f32)) continue;
                const u = u_arr[l];
                // evalGrad takes the same camera-local point; the tape
                // chain-rules through the camera wrapper, so the returned
                // partials are w.r.t. (wcx, wcy, wcz). Those are the
                // partials we want for screen-space normals.
                const p = Vec3{ .x = u, .y = v, .z = t_hit };
                const grad = m.decodeRegEvalGrad(tape, ctx.ir, ctx.slots, p);
                const gx = grad[1];
                const gy = grad[2];
                const gz = grad[3];
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
                ctx.out_pixels[idx] = shade(nx, ny, nz_n);
            }
        }
    }
}

inline fn shade(nx: f32, ny: f32, nz: f32) u32 {
    const ldot = nx * NEG_LIGHT_DIR[0] + ny * NEG_LIGHT_DIR[1] + nz * NEG_LIGHT_DIR[2];
    const diffuse = std.math.clamp(ldot * 0.75 + 0.25, 0.0, 1.0);
    const r = 0.35 + 0.35 * diffuse;
    const g = 0.45 + 0.40 * diffuse;
    const b = 0.55 + 0.35 * diffuse;
    return packRGBA(colorByte(r), colorByte(g), colorByte(b), 255);
}
