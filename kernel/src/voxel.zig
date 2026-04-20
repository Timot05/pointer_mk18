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
};

pub const MAX_TAPE: u32 = 1024;
pub const MAX_CONST: u32 = 256;
pub const MAX_CHOICES: u32 = 256;

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

var interval_slots: [MAX_TAPE]interval_mod.Interval = undefined;
var choice_trace: [MAX_CHOICES]interval_mod.Choice = undefined;
var simd_slots: [MAX_TAPE]eval_mod.F4 = undefined;
var grad_slots: [MAX_TAPE]grad_mod.Grad = undefined;
var grad_simd_slots: [MAX_TAPE]grad_mod.GradBatch = undefined;
var scratch_value: [MAX_TAPE]simplify_mod.Value = undefined;
var scratch_new_idx: [MAX_TAPE]u32 = undefined;
var scratch_live: [MAX_TAPE]bool = undefined;

const DepthStorage = struct {
    ops: [MAX_TAPE]tape_mod.Instruction,
    consts: [MAX_CONST]f32,
};
var depth_storage: [TILE_SIZES.len]DepthStorage = undefined;

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
