const std = @import("std");
const tape_mod = @import("tape.zig");
const eval_mod = @import("eval.zig");
const interval_mod = @import("interval.zig");
const simplify_mod = @import("simplify.zig");
const grad_mod = @import("grad.zig");

// Voxel SDF renderer.
//
// Screen-space tile pyramid. At each level, a cubical tile of a fixed pixel
// size (TILE_SIZES[level]) is tested by interval arithmetic over its
// world-space AABB. Ambiguous tiles subdivide into 2×2×2 children at the
// next smaller tile size. At the smallest (leaf) size, per-pixel z-scan
// resolves the surface hit.
//
// Two key wins over a plain 3D octree:
//   1. z is traversed front-to-back (camera convention: larger wcz = closer).
//      Combined with the depth buffer we already write, this lets us skip
//      whole tiles whose nearest face is behind an already-recorded hit.
//   2. Simplified tapes are per-tile; the simplify cascade shrinks ops
//      dramatically as we descend.

pub const Stats = extern struct {
    eval_count: u32 = 0,
    leaf_inside: u32 = 0,
    leaf_outside: u32 = 0,
    leaf_ambiguous: u32 = 0,
    pixels_written: u32 = 0,
    original_tape_ops: u32 = 0,
    min_simplified_ops: u32 = 0,
    max_simplified_ops: u32 = 0,
    total_simplified_ops: u32 = 0,
    simplify_calls: u32 = 0,
    // Tile-level fast skip: tiles whose whole x,y span was already behind a
    // closer depth, so we didn't even run interval eval.
    tiles_depth_skipped: u32 = 0,
    // How many leaf tiles took the bulk SDF path vs. the scalar fallback.
    // Fallbacks happen only when the post-simplify leaf tape exceeds
    // LEAF_BULK_MAX_SLOTS — a non-zero count here suggests either the cap
    // is too tight or simplify isn't shrinking the tape enough.
    leaf_bulk_calls: u32 = 0,
    leaf_bulk_fallbacks: u32 = 0,
};

// ── Tape sizing ──────────────────────────────────────────────────────────
//
// `MAX_TAPE` / `MAX_CONST` cap the scene tape produced by lowering. Evaluation
// scratch arrays (interval_slots, simd_slots, grad_slots, choice_trace) and
// `simplify`'s output buffers are derived from these.
//
// `simplify.zig` promises "2× the original is safe" for its output buffers
// (transient constants from binary folds can briefly exceed the original op
// count before DCE collapses them). Simplified tapes feed into the next
// recursion level's `evalInterval`, so the eval-side slot arrays must also
// accept post-simplify sizes, not just MAX_TAPE.
//
// `MAX_CHOICES` bounds the min/max count of any tape passed to
// `evalInterval`. Choice count is always ≤ op count, so sizing it to the
// largest tape we'll ever evaluate (SIMPLIFY_OUT_TAPE) is a safe upper bound.
pub const MAX_TAPE: u32 = 4096;
pub const MAX_CONST: u32 = 1024;
pub const SIMPLIFY_OUT_TAPE: u32 = 2 * MAX_TAPE;
pub const SIMPLIFY_OUT_CONST: u32 = 2 * MAX_TAPE;
pub const MAX_CHOICES: u32 = SIMPLIFY_OUT_TAPE;

pub const MAX_W: u32 = 1024;
pub const MAX_H: u32 = 1024;

// Tile pyramid: each level halves the tile edge. Mirrors the rasterizer's
// default (`TileSizes::new(&[128, 64, 32, 16, 8])`).
//
// At levels 0..FINEST_TILE_LEVEL the renderer stamps a single center value
// over the whole tile (fast block preview). The extra level PER_PIXEL_LEVEL
// = FINEST_TILE_LEVEL + 1 drops back to the smallest tile size and runs the
// per-pixel sphere-trace leaf. So there are TILE_SIZES.len + 1 total render
// levels, indexed 0..PER_PIXEL_LEVEL.
const TILE_SIZES = [_]u32{ 128, 64, 32, 16, 8 };
pub const FINEST_TILE_LEVEL: u32 = TILE_SIZES.len - 1;
pub const PER_PIXEL_LEVEL: u32 = FINEST_TILE_LEVEL + 1;

// Slices returned inside RenderResult alias module-level storage and are
// clobbered by the next render* call.
pub const RenderResult = struct {
    stats: Stats,
    depth: []const f32,
    normal: []const f32,
};

// Eval-side slot arrays accept post-simplify tapes (up to SIMPLIFY_OUT_TAPE).
var interval_slots: [SIMPLIFY_OUT_TAPE]interval_mod.Interval = undefined;
var choice_trace: [MAX_CHOICES]interval_mod.Choice = undefined;
var simd_slots: [SIMPLIFY_OUT_TAPE]eval_mod.F4 = undefined;
var grad_slots: [SIMPLIFY_OUT_TAPE]grad_mod.Grad = undefined;
var grad_simd_slots: [SIMPLIFY_OUT_TAPE]grad_mod.GradBatch = undefined;

// simplify scratch — `values` is indexed by the input tape (≤ SIMPLIFY_OUT_TAPE
// when fed a previously simplified tape); `live` and `new_idx` are indexed by
// the emitter's post-fold op count, which can reach 2× the input — i.e.
// 2 × SIMPLIFY_OUT_TAPE in the pathological recursion case. The output tape is
// clamped to SIMPLIFY_OUT_TAPE via the `DepthStorage` buffer below, so sizing
// these to SIMPLIFY_OUT_TAPE matches the buffer that bounds `op_count`.
var scratch_value: [SIMPLIFY_OUT_TAPE]simplify_mod.Value = undefined;
var scratch_new_idx: [SIMPLIFY_OUT_TAPE]u32 = undefined;
var scratch_live: [SIMPLIFY_OUT_TAPE]bool = undefined;

const DepthStorage = struct {
    ops: [SIMPLIFY_OUT_TAPE]tape_mod.Instruction,
    consts: [SIMPLIFY_OUT_CONST]f32,
};
var depth_storage: [TILE_SIZES.len]DepthStorage = undefined;

// ── Leaf-tile bulk evaluator scratch ─────────────────────────────────────
//
// At the finest tile level we pack every (wcx, wcy, wcz) triple for pixels
// that aren't already occluded into flat arrays, run one bulk SDF tape walk
// over the whole tile, then per-column scan for the frontmost hit. Amortizes
// per-op dispatch cost over hundreds of points instead of four.
//
// Scratch is sized for a typical post-simplify leaf tape; rare leaves whose
// tape exceeds LEAF_BULK_MAX_SLOTS fall back to `emitLeafSamplesScalar` so
// we never silently render garbage.
//
// Dimensions:
//   LEAF_TILE_SIZE   = 8 voxel edges ⇒ up to 64 pixels per leaf.
//   MAX_Z_SAMPLES    = nz + 1 where nz ≤ 8 in well-conditioned tiles, +1 slack
//                      for FP ceil overshoot, so cap at 10.
pub const LEAF_BULK_MAX_SLOTS: u32 = 512;
const LEAF_TILE_SIZE: u32 = 8;
const MAX_Z_SAMPLES: u32 = 10;
pub const LEAF_BULK_MAX_POINTS: u32 = LEAF_TILE_SIZE * LEAF_TILE_SIZE * MAX_Z_SAMPLES;
const LEAF_BULK_MAX_PIXELS: u32 = LEAF_TILE_SIZE * LEAF_TILE_SIZE;

var leaf_bulk_xs: [LEAF_BULK_MAX_POINTS]f32 = undefined;
var leaf_bulk_ys: [LEAF_BULK_MAX_POINTS]f32 = undefined;
var leaf_bulk_zs: [LEAF_BULK_MAX_POINTS]f32 = undefined;
var leaf_bulk_sdf: [LEAF_BULK_MAX_POINTS]f32 = undefined;
var leaf_bulk_slots: [LEAF_BULK_MAX_SLOTS * LEAF_BULK_MAX_POINTS]f32 = undefined;

// Per-pixel metadata, parallel arrays indexed 0..n_pixels.
var leaf_px_idx: [LEAF_BULK_MAX_PIXELS]usize = undefined;      // depth_buf index
var leaf_px_wcx: [LEAF_BULK_MAX_PIXELS]f32 = undefined;
var leaf_px_wcy: [LEAF_BULK_MAX_PIXELS]f32 = undefined;
var leaf_px_db: [LEAF_BULK_MAX_PIXELS]f32 = undefined;         // existing depth
var leaf_px_start: [LEAF_BULK_MAX_PIXELS]u32 = undefined;      // start offset in xs/ys/zs

// G-buffer. depth_buf == -inf marks "no hit".
var depth_buf: [MAX_W * MAX_H]f32 = undefined;
var normal_buf: [MAX_W * MAX_H * 3]f32 = undefined;

const BuildCtx = struct {
    stats: *Stats,
    pixel_world: f32,
    // Tile-local iteration bounds (pixels in this render call's output).
    width: u32,
    height: u32,
    // Full-image pixel dimensions + this tile's offset within the full image.
    // Used to compute world-space ray positions in the full-image NDC.
    full_width: u32,
    full_height: u32,
    tile_x: u32,
    tile_y: u32,
    image_depth_vox: u32,
    view_half_w: f32,
    view_half_h: f32,
    bz_lo: f32,
    max_level: u32,
};

pub fn render(
    tape: *const tape_mod.Tape,
    tile_width: u32, tile_height: u32,
    full_width: u32, full_height: u32,
    tile_x: u32, tile_y: u32,
    view_half_w: f32, view_half_h: f32,
    half: f32,
    max_level: u32,
) RenderResult {
    // pixel_world is the world-space extent of one full-image pixel.
    const pixel_world_x: f32 = 2.0 * view_half_w / @as(f32, @floatFromInt(full_width));
    const pixel_world_y: f32 = 2.0 * view_half_h / @as(f32, @floatFromInt(full_height));
    const pixel_world: f32 = @min(pixel_world_x, pixel_world_y);

    var stats: Stats = .{};
    stats.original_tape_ops = @intCast(tape.ops.len);

    const total_px: usize = @as(usize, tile_width) * @as(usize, tile_height);
    clearDepthBuffer(total_px);

    const image_depth_vox: u32 = @intFromFloat(@ceil((2.0 * half) / pixel_world));

    var bctx: BuildCtx = .{
        .stats = &stats,
        .pixel_world = pixel_world,
        .width = tile_width,
        .height = tile_height,
        .full_width = full_width,
        .full_height = full_height,
        .tile_x = tile_x,
        .tile_y = tile_y,
        .image_depth_vox = image_depth_vox,
        .view_half_w = view_half_w,
        .view_half_h = view_half_h,
        .bz_lo = -half,
        .max_level = @min(max_level, PER_PIXEL_LEVEL),
    };

    // Iterate top-level tiles of this sub-region. Z front-to-back (high
    // vz first, which is high wcz = closer to camera).
    const root_size = TILE_SIZES[0];
    const nx = (tile_width + root_size - 1) / root_size;
    const ny = (tile_height + root_size - 1) / root_size;
    const nz = (image_depth_vox + root_size - 1) / root_size;

    var zt: u32 = nz;
    while (zt > 0) {
        zt -= 1;
        var yt: u32 = 0;
        while (yt < ny) : (yt += 1) {
            var xt: u32 = 0;
            while (xt < nx) : (xt += 1) {
                renderTileRecurse(&bctx, tape, 0, xt, yt, zt);
            }
        }
    }

    return .{
        .stats = stats,
        .depth = depth_buf[0..total_px],
        .normal = normal_buf[0 .. total_px * 3],
    };
}

fn clearDepthBuffer(total_px: usize) void {
    const neg_inf = -std.math.inf(f32);
    var i: usize = 0;
    while (i < total_px) : (i += 1) {
        depth_buf[i] = neg_inf;
    }
}

fn renderTileRecurse(
    ctx: *BuildCtx,
    tape: *const tape_mod.Tape,
    level: u32,
    tx: u32,
    ty: u32,
    tz: u32,
) void {
    const size = TILE_SIZES[level];
    const px_lo = tx * size;
    const py_lo = ty * size;
    const vz_lo = tz * size;
    const px_hi = @min(px_lo + size, ctx.width);
    const py_hi = @min(py_lo + size, ctx.height);
    const vz_hi = @min(vz_lo + size, ctx.image_depth_vox);
    if (px_lo >= px_hi or py_lo >= py_hi or vz_lo >= vz_hi) return;

    // World-space AABB for interval eval.
    const wx_lo = pixelToWorldX(ctx, px_lo);
    const wx_hi = pixelToWorldX(ctx, px_hi);
    // Image y grows downward; world y grows upward. Top-of-tile pixel (py_lo)
    // maps to the higher world y.
    const wy_hi = pixelToWorldY(ctx, py_lo);
    const wy_lo = pixelToWorldY(ctx, py_hi);
    const wz_lo = ctx.bz_lo + @as(f32, @floatFromInt(vz_lo)) * ctx.pixel_world;
    const wz_hi = ctx.bz_lo + @as(f32, @floatFromInt(vz_hi)) * ctx.pixel_world;

    // Depth skip: if every pixel in this tile's x,y span already has a closer
    // hit than wz_hi (the front face of the tile), there is nothing this tile
    // can contribute. Skips interval eval entirely.
    if (allPixelsCloserThan(ctx, px_lo, px_hi, py_lo, py_hi, wz_hi)) {
        ctx.stats.tiles_depth_skipped += 1;
        return;
    }

    const ix: interval_mod.Interval = .{ .lo = wx_lo, .hi = wx_hi };
    const iy: interval_mod.Interval = .{ .lo = wy_lo, .hi = wy_hi };
    const iz: interval_mod.Interval = .{ .lo = wz_lo, .hi = wz_hi };
    const ir = interval_mod.evalInterval(tape, ix, iy, iz, &interval_slots, &choice_trace);
    ctx.stats.eval_count += 1;

    if (ir.result.hi < 0) {
        ctx.stats.leaf_inside += 1;
        return;
    }
    if (ir.result.lo > 0) {
        ctx.stats.leaf_outside += 1;
        return;
    }

    if (level == FINEST_TILE_LEVEL) {
        ctx.stats.leaf_ambiguous += 1;
        if (ctx.max_level > FINEST_TILE_LEVEL) {
            // max_level == PER_PIXEL_LEVEL: finest tile, do per-pixel sphere trace.
            emitLeafSamples(ctx, tape, px_lo, px_hi, py_lo, py_hi, wz_lo, wz_hi);
        } else {
            // max_level == FINEST_TILE_LEVEL: stamp the finest tile as a block.
            stampTileBlock(ctx, tape, px_lo, px_hi, py_lo, py_hi, wx_lo, wx_hi, wy_lo, wy_hi, wz_lo, wz_hi);
        }
        return;
    }
    if (level == ctx.max_level) {
        // Coarse block stamp at a non-leaf tile size.
        ctx.stats.leaf_ambiguous += 1;
        stampTileBlock(ctx, tape, px_lo, px_hi, py_lo, py_hi, wx_lo, wx_hi, wy_lo, wy_hi, wz_lo, wz_hi);
        return;
    }

    var child_tape: *const tape_mod.Tape = tape;
    var simp: tape_mod.Tape = undefined;
    if (ir.has_any_pruneable) {
        const ds = &depth_storage[level];
        simp = simplify_mod.simplify(
            tape,
            choice_trace[0..tape.choice_count],
            &ds.ops,
            &ds.consts,
            .{
                .values = &scratch_value,
                .live = &scratch_live,
                .new_idx = &scratch_new_idx,
            },
        );
        child_tape = &simp;
        const sz: u32 = @intCast(simp.ops.len);
        ctx.stats.simplify_calls += 1;
        ctx.stats.total_simplified_ops += sz;
        if (sz > ctx.stats.max_simplified_ops) ctx.stats.max_simplified_ops = sz;
        if (ctx.stats.min_simplified_ops == 0 or sz < ctx.stats.min_simplified_ops) ctx.stats.min_simplified_ops = sz;
    }

    // Recurse into 8 children. z front-to-back.
    const cx = tx * 2;
    const cy = ty * 2;
    const cz = tz * 2;
    var zi: u32 = 2;
    while (zi > 0) {
        zi -= 1;
        var yi: u32 = 0;
        while (yi < 2) : (yi += 1) {
            var xi: u32 = 0;
            while (xi < 2) : (xi += 1) {
                renderTileRecurse(ctx, child_tape, level + 1, cx + xi, cy + yi, cz + zi);
            }
        }
    }
}

inline fn pixelToWorldX(ctx: *const BuildCtx, px: u32) f32 {
    // px is tile-local; convert to full-image pixel before NDC.
    const full: f32 = @floatFromInt(ctx.full_width);
    const absolute: f32 = @floatFromInt(ctx.tile_x + px);
    return (absolute / full * 2.0 - 1.0) * ctx.view_half_w;
}

inline fn pixelToWorldY(ctx: *const BuildCtx, py: u32) f32 {
    const full: f32 = @floatFromInt(ctx.full_height);
    const absolute: f32 = @floatFromInt(ctx.tile_y + py);
    return (1.0 - absolute / full * 2.0) * ctx.view_half_h;
}

fn allPixelsCloserThan(
    ctx: *const BuildCtx,
    px_lo: u32,
    px_hi: u32,
    py_lo: u32,
    py_hi: u32,
    wz: f32,
) bool {
    var py: u32 = py_lo;
    while (py < py_hi) : (py += 1) {
        const row = @as(usize, py) * ctx.width;
        var px: u32 = px_lo;
        while (px < px_hi) : (px += 1) {
            if (depth_buf[row + px] < wz) return false;
        }
    }
    return true;
}

fn emitLeafSamples(
    ctx: *BuildCtx,
    tape: *const tape_mod.Tape,
    px_lo: u32,
    px_hi: u32,
    py_lo: u32,
    py_hi: u32,
    wz_lo: f32,
    wz_hi: f32,
) void {
    if (tape.ops.len > LEAF_BULK_MAX_SLOTS) {
        ctx.stats.leaf_bulk_fallbacks += 1;
        emitLeafSamplesScalar(ctx, tape, px_lo, px_hi, py_lo, py_hi, wz_lo, wz_hi);
        return;
    }
    ctx.stats.leaf_bulk_calls += 1;
    emitLeafSamplesBulk(ctx, tape, px_lo, px_hi, py_lo, py_hi, wz_lo, wz_hi);
}

// Bulk SDF + batched-gradient leaf sampler.
//
// Strategy (mirrors fidget's `render_tile_pixels` in fidget-raster):
//   1. For each pixel whose existing depth is in front of wz_hi we can stop
//      early — nothing in this tile can improve on it (per-pixel early out).
//   2. Pack n_z_samples (x, y, z) triples per surviving pixel into flat
//      arrays, ordered front-to-back so the column scan below finds the
//      frontmost hit first.
//   3. One `evalFloatSlice` call walks the tape once over all N points.
//   4. Per-pixel column scan: first sample with sdf < 0 AND wcz > existing
//      depth becomes the hit. Capture its (wcx, wcy, hit_wcz) for gradient.
//   5. Gradient pass in groups of 4 via the existing `evalGrad4` — keeping
//      the SIMD-4 grad path is enough because hit count per leaf is low
//      (≤ 64) and grad cost is already one walk per quad in the original.
fn emitLeafSamplesBulk(
    ctx: *BuildCtx,
    tape: *const tape_mod.Tape,
    px_lo: u32,
    px_hi: u32,
    py_lo: u32,
    py_hi: u32,
    wz_lo: f32,
    wz_hi: f32,
) void {
    const extent = wz_hi - wz_lo;
    const nz_f = @ceil(extent / ctx.pixel_world);
    const nz_u: u32 = @intFromFloat(@max(2.0, nz_f));
    const n_z_samples: u32 = nz_u + 1;
    // Clamp to the scratch cap. Overshooting MAX_Z_SAMPLES in practice would
    // mean the pixel_world/extent ratio is unexpectedly small; safer to lose
    // a sliver of z resolution than to walk off the scratch.
    const nz_clamped: u32 = @min(n_z_samples, MAX_Z_SAMPLES);
    const dz = extent / @as(f32, @floatFromInt(nz_u));
    const neg_inf: f32 = -std.math.inf(f32);

    const full_wf: f32 = @floatFromInt(ctx.full_width);
    const full_hf: f32 = @floatFromInt(ctx.full_height);

    // ── Pass 1: build candidate list ─────────────────────────────────
    var n_pixels: u32 = 0;
    var n_points: u32 = 0;

    var py: u32 = py_lo;
    while (py < py_hi) : (py += 1) {
        const abs_py: f32 = @as(f32, @floatFromInt(ctx.tile_y + py)) + 0.5;
        const wcy = (1.0 - (abs_py / full_hf) * 2.0) * ctx.view_half_h;
        const row_base: usize = @as(usize, py) * ctx.width;

        var px: u32 = px_lo;
        while (px < px_hi) : (px += 1) {
            const idx = row_base + px;
            const db = depth_buf[idx];
            // Per-pixel early exit: if the whole tile is behind existing
            // depth, this pixel can't contribute.
            if (db >= wz_hi) continue;

            const abs_px: f32 = @as(f32, @floatFromInt(ctx.tile_x + px)) + 0.5;
            const wcx = ((abs_px / full_wf) * 2.0 - 1.0) * ctx.view_half_w;

            leaf_px_idx[n_pixels] = idx;
            leaf_px_wcx[n_pixels] = wcx;
            leaf_px_wcy[n_pixels] = wcy;
            leaf_px_db[n_pixels] = db;
            leaf_px_start[n_pixels] = n_points;
            n_pixels += 1;

            // Push z samples front-to-back (high wcz first), matching the
            // original scalar loop's `zi = nz+1; zi -= 1` iteration. The
            // column scan stops at the first negative which is therefore
            // the frontmost hit.
            var zi: u32 = nz_clamped;
            while (zi > 0) {
                zi -= 1;
                const wcz = wz_lo + @as(f32, @floatFromInt(zi)) * dz;
                leaf_bulk_xs[n_points] = wcx;
                leaf_bulk_ys[n_points] = wcy;
                leaf_bulk_zs[n_points] = wcz;
                n_points += 1;
            }
        }
    }

    if (n_pixels == 0) return;

    // ── Pass 2: one bulk SDF tape walk over all points ──────────────
    eval_mod.evalFloatSlice(
        tape,
        leaf_bulk_xs[0..n_points],
        leaf_bulk_ys[0..n_points],
        leaf_bulk_zs[0..n_points],
        leaf_bulk_sdf[0..n_points],
        &leaf_bulk_slots,
        LEAF_BULK_MAX_POINTS,
    );

    // ── Pass 3: per-column scan + grad staging ──────────────────────
    // Reuse leaf_px_* slots after this point: index 0..n_grad indexes into
    // staged grad inputs. We overwrite leaf_px_idx[], leaf_px_wcx/wcy[] with
    // the subset of pixels that actually hit.
    var n_grad: u32 = 0;
    var p: u32 = 0;
    while (p < n_pixels) : (p += 1) {
        const start = leaf_px_start[p];
        const end = start + nz_clamped;
        const db = leaf_px_db[p];

        var hit_wcz: f32 = neg_inf;
        var s: u32 = start;
        while (s < end) : (s += 1) {
            if (leaf_bulk_sdf[s] < 0.0 and leaf_bulk_zs[s] > db) {
                hit_wcz = leaf_bulk_zs[s];
                break;
            }
        }
        if (hit_wcz == neg_inf) continue;

        // Compact into the front of the per-pixel arrays as grad-staging.
        leaf_px_idx[n_grad] = leaf_px_idx[p];
        leaf_px_wcx[n_grad] = leaf_px_wcx[p];
        leaf_px_wcy[n_grad] = leaf_px_wcy[p];
        leaf_px_db[n_grad] = hit_wcz; // reuse slot to carry hit z into grad
        n_grad += 1;
    }

    if (n_grad == 0) return;

    // ── Pass 4: batched gradient eval (SIMD-4 groups) ────────────────
    // Keep the existing `evalGrad4` — per-leaf grad count is capped at 64 so
    // the dispatch overhead is small relative to the SDF hot loop we already
    // amortized. A bulk-grad evaluator would be a further step.
    var g: u32 = 0;
    while (g < n_grad) : (g += 4) {
        const n_lanes: u32 = @min(4, n_grad - g);

        var xs: [4]f32 = undefined;
        var ys: [4]f32 = undefined;
        var zs: [4]f32 = undefined;
        inline for (0..4) |l| {
            const src: u32 = g + @min(@as(u32, l), n_lanes - 1);
            xs[l] = leaf_px_wcx[src];
            ys[l] = leaf_px_wcy[src];
            zs[l] = leaf_px_db[src];
        }
        const wcx_vec: eval_mod.F4 = xs;
        const wcy_vec: eval_mod.F4 = ys;
        const wcz_vec: eval_mod.F4 = zs;

        const gb = grad_mod.evalGrad4(tape, wcx_vec, wcy_vec, wcz_vec, &grad_simd_slots);
        const dx_arr: [4]f32 = gb.dx;
        const dy_arr: [4]f32 = gb.dy;
        const dz_arr: [4]f32 = gb.dz;

        var l: u32 = 0;
        while (l < n_lanes) : (l += 1) {
            const gx = dx_arr[l];
            const gy = dy_arr[l];
            const gz = dz_arr[l];
            const mag = @sqrt(gx * gx + gy * gy + gz * gz);
            const nvx: f32 = if (mag < 1e-9) 0 else gx / mag;
            const nvy: f32 = if (mag < 1e-9) 1 else gy / mag;
            const nvz: f32 = if (mag < 1e-9) 0 else gz / mag;
            const src: u32 = g + l;
            const idx = leaf_px_idx[src];
            depth_buf[idx] = leaf_px_db[src];
            normal_buf[idx * 3 + 0] = nvx;
            normal_buf[idx * 3 + 1] = nvy;
            normal_buf[idx * 3 + 2] = nvz;
            ctx.stats.pixels_written += 1;
        }
    }
}

fn emitLeafSamplesScalar(
    ctx: *BuildCtx,
    tape: *const tape_mod.Tape,
    px_lo: u32,
    px_hi: u32,
    py_lo: u32,
    py_hi: u32,
    wz_lo: f32,
    wz_hi: f32,
) void {
    // nz segments ⇒ nz+1 samples, endpoints inclusive. Hitting both cell edges
    // avoids pinholes when the surface grazes a corner.
    const extent = wz_hi - wz_lo;
    const nz_f = @ceil(extent / ctx.pixel_world);
    const nz: u32 = @intFromFloat(@max(2.0, nz_f));
    const dz = extent / @as(f32, @floatFromInt(nz));

    // Full-image NDC uses absolute pixel coordinates = tile origin + local.
    const full_wf: f32 = @floatFromInt(ctx.full_width);
    const full_hf: f32 = @floatFromInt(ctx.full_height);
    const neg_inf: f32 = -std.math.inf(f32);

    var py: u32 = py_lo;
    while (py < py_hi) : (py += 1) {
        const abs_py: f32 = @as(f32, @floatFromInt(ctx.tile_y + py)) + 0.5;
        const wcy_s = (1.0 - (abs_py / full_hf) * 2.0) * ctx.view_half_h;
        const wcy_vec: eval_mod.F4 = @splat(wcy_s);
        const row_base: usize = @as(usize, py) * ctx.width;

        var px_base: u32 = px_lo;
        while (px_base < px_hi) : (px_base += 4) {
            const lane_count: u32 = @min(4, px_hi - px_base);

            var wcx_arr: [4]f32 = undefined;
            var idx_arr: [4]usize = undefined;
            var db_arr: [4]f32 = undefined;
            inline for (0..4) |l| {
                const lane_offset: u32 = @min(l, lane_count - 1);
                const lane_px = px_base + lane_offset;
                const abs_px: f32 = @as(f32, @floatFromInt(ctx.tile_x + lane_px)) + 0.5;
                wcx_arr[l] = ((abs_px / full_wf) * 2.0 - 1.0) * ctx.view_half_w;
                idx_arr[l] = row_base + lane_px;
                db_arr[l] = depth_buf[idx_arr[l]];
            }
            const wcx_vec: eval_mod.F4 = wcx_arr;
            const db_vec: eval_mod.F4 = db_arr;

            var hit_z: eval_mod.F4 = @splat(neg_inf);
            var zi: u32 = nz + 1;
            while (zi > 0) {
                zi -= 1;
                const wcz_s = wz_lo + @as(f32, @floatFromInt(zi)) * dz;
                const wcz_vec: eval_mod.F4 = @splat(wcz_s);

                const not_hit: @Vector(4, bool) = hit_z == @as(eval_mod.F4, @splat(neg_inf));
                const above_db: @Vector(4, bool) = wcz_vec > db_vec;
                const active: @Vector(4, bool) = @select(bool, not_hit, above_db, @as(@Vector(4, bool), @splat(false)));
                if (!@reduce(.Or, active)) break;

                const sdf = eval_mod.evalScalar4(tape, wcx_vec, wcy_vec, wcz_vec, &simd_slots);
                const neg: @Vector(4, bool) = sdf < @as(eval_mod.F4, @splat(0.0));
                const hit_now: @Vector(4, bool) = @select(bool, active, neg, @as(@Vector(4, bool), @splat(false)));
                hit_z = @select(f32, hit_now, wcz_vec, hit_z);
            }

            const hit_mask: @Vector(4, bool) = hit_z != @as(eval_mod.F4, @splat(neg_inf));
            if (!@reduce(.Or, hit_mask)) continue;

            // One batched gradient walk covers all 4 lanes. Lanes that didn't
            // hit still participate (their wcz is -inf → bogus grad), but
            // we skip writing them below, so the bogus work is discarded.
            const gb = grad_mod.evalGrad4(tape, wcx_vec, wcy_vec, hit_z, &grad_simd_slots);
            const hit_z_arr: [4]f32 = hit_z;
            const dx_arr: [4]f32 = gb.dx;
            const dy_arr: [4]f32 = gb.dy;
            const dz_arr: [4]f32 = gb.dz;

            var l: u32 = 0;
            while (l < lane_count) : (l += 1) {
                if (hit_z_arr[l] == neg_inf) continue;
                const gx = dx_arr[l];
                const gy = dy_arr[l];
                const gz = dz_arr[l];
                const mag = @sqrt(gx * gx + gy * gy + gz * gz);
                const nvx: f32 = if (mag < 1e-9) 0 else gx / mag;
                const nvy: f32 = if (mag < 1e-9) 1 else gy / mag;
                const nvz: f32 = if (mag < 1e-9) 0 else gz / mag;
                const idx = idx_arr[l];
                depth_buf[idx] = hit_z_arr[l];
                normal_buf[idx * 3 + 0] = nvx;
                normal_buf[idx * 3 + 1] = nvy;
                normal_buf[idx * 3 + 2] = nvz;
                ctx.stats.pixels_written += 1;
            }
        }
    }
}

// Coarse block stamp used when max_level <= FINEST_TILE_LEVEL. One grad eval at
// the tile's world center, then fill the pixel block with (tile_center_z,
// normalized_gradient). Deliberately approximate — the block is ambiguous
// by interval test but we don't know where within it the surface sits; using
// the center z is an acceptable first-pass estimate that gets replaced as
// the caller refines to finer levels.
fn stampTileBlock(
    ctx: *BuildCtx,
    tape: *const tape_mod.Tape,
    px_lo: u32,
    px_hi: u32,
    py_lo: u32,
    py_hi: u32,
    wx_lo: f32,
    wx_hi: f32,
    wy_lo: f32,
    wy_hi: f32,
    wz_lo: f32,
    wz_hi: f32,
) void {
    const wcx = 0.5 * (wx_lo + wx_hi);
    const wcy = 0.5 * (wy_lo + wy_hi);
    const wcz = 0.5 * (wz_lo + wz_hi);

    const g = grad_mod.evalGrad(tape, wcx, wcy, wcz, &grad_slots);
    const mag = @sqrt(g[1] * g[1] + g[2] * g[2] + g[3] * g[3]);
    const nvx: f32 = if (mag < 1e-9) 0 else g[1] / mag;
    const nvy: f32 = if (mag < 1e-9) 1 else g[2] / mag;
    const nvz: f32 = if (mag < 1e-9) 0 else g[3] / mag;

    var py: u32 = py_lo;
    while (py < py_hi) : (py += 1) {
        const row = @as(usize, py) * ctx.width;
        var px: u32 = px_lo;
        while (px < px_hi) : (px += 1) {
            const idx = row + px;
            if (wcz > depth_buf[idx]) {
                depth_buf[idx] = wcz;
                normal_buf[idx * 3 + 0] = nvx;
                normal_buf[idx * 3 + 1] = nvy;
                normal_buf[idx * 3 + 2] = nvz;
                ctx.stats.pixels_written += 1;
            }
        }
    }
}
