const std = @import("std");
const tape_mod = @import("tape.zig");
const interval_mod = @import("interval.zig");
const simplify_mod = @import("simplify.zig");
const eval_mod = @import("eval.zig");
const grad_mod = @import("grad.zig");
const dc = @import("dc.zig");

pub const Stats = extern struct {
    eval_count: u32 = 0,
    leaf_count: u32 = 0,
    leaf_outside: u32 = 0,
    leaf_inside: u32 = 0,
    leaf_ambiguous: u32 = 0,
    triangles_emitted: u32 = 0,
    original_tape_ops: u32 = 0,
    min_simplified_ops: u32 = 0,
    max_simplified_ops: u32 = 0,
    total_simplified_ops: u32 = 0, // sum; divide by simplify_calls for avg
    simplify_calls: u32 = 0,
};

pub const MAX_TAPE: u32 = 1024;
pub const MAX_CONST: u32 = 256;
pub const MAX_CHOICES: u32 = 256;
pub const MAX_DEPTH: u32 = 10;
pub const MAX_VERTICES: u32 = 2 * 1024 * 1024; // 2M verts × 24 B = 48 MB

// ── Shared per-frame scratch (safe to share across depths during DFS) ───
var interval_slots: [MAX_TAPE]interval_mod.Interval = undefined;
var choice_trace: [MAX_CHOICES]interval_mod.Choice = undefined;
var scalar_slots: [MAX_TAPE]f32 = undefined;
var grad_slots: [MAX_TAPE]grad_mod.Grad = undefined;
var scratch_value: [MAX_TAPE]simplify_mod.Value = undefined;
var scratch_new_idx: [MAX_TAPE]u32 = undefined;
var scratch_live: [MAX_TAPE]bool = undefined;

// ── Per-depth simplified tape storage (must outlive child recursion) ────
const DepthStorage = struct {
    ops: [MAX_TAPE]tape_mod.Instruction,
    consts: [MAX_CONST]f32,
};
var depth_storage: [MAX_DEPTH]DepthStorage = undefined;

// ── Vertex buffer: interleaved position(3) + normal(3) per vertex ───────
var vertex_buffer: [MAX_VERTICES * 6]f32 = undefined;
var vertex_count: u32 = 0;

pub fn vertexBufferPtr() [*]f32 {
    return &vertex_buffer;
}

pub fn vertexBufferCapacityFloats() usize {
    return vertex_buffer.len;
}

const Ctx = struct {
    stats: *Stats,
    max_depth: u32,
};

fn recurse(
    ctx: *Ctx,
    tape: *const tape_mod.Tape,
    bx_lo: f32,
    bx_hi: f32,
    by_lo: f32,
    by_hi: f32,
    bz_lo: f32,
    bz_hi: f32,
    depth: u32,
    gi: u32,
    gj: u32,
    gk: u32,
) void {
    const ix: interval_mod.Interval = .{ .lo = bx_lo, .hi = bx_hi };
    const iy: interval_mod.Interval = .{ .lo = by_lo, .hi = by_hi };
    const iz: interval_mod.Interval = .{ .lo = bz_lo, .hi = bz_hi };

    const ir = interval_mod.evalInterval(tape, ix, iy, iz, &interval_slots, &choice_trace);
    ctx.stats.eval_count += 1;

    // Inside / outside culling: no surface crossing in here.
    if (ir.result.hi < 0) {
        ctx.stats.leaf_inside += 1;
        return;
    }
    if (ir.result.lo > 0) {
        ctx.stats.leaf_outside += 1;
        return;
    }

    if (depth >= ctx.max_depth) {
        ctx.stats.leaf_ambiguous += 1;
        ctx.stats.leaf_count += 1;
        dc.processLeaf(tape, gi, gj, gk, &scalar_slots, &grad_slots);
        return;
    }

    // Simplify for the 8 children if the interval eval pruned any choice.
    var child_tape_ptr: *const tape_mod.Tape = tape;
    var simp_tape_storage: tape_mod.Tape = undefined;
    if (ir.has_any_pruneable) {
        const ds = &depth_storage[depth];
        simp_tape_storage = simplify_mod.simplify(
            tape,
            choice_trace[0..tape.choice_count],
            &ds.ops,
            &ds.consts,
            &scratch_value,
            &scratch_live,
            &scratch_new_idx,
        );
        child_tape_ptr = &simp_tape_storage;
        const sz: u32 = @intCast(simp_tape_storage.ops.len);
        ctx.stats.total_simplified_ops += sz;
        ctx.stats.simplify_calls += 1;
        if (sz > ctx.stats.max_simplified_ops) ctx.stats.max_simplified_ops = sz;
        if (ctx.stats.min_simplified_ops == 0 or sz < ctx.stats.min_simplified_ops) {
            ctx.stats.min_simplified_ops = sz;
        }
    }

    // Split 8 ways at the midpoint. Grid coords double at each descent.
    const mx = 0.5 * (bx_lo + bx_hi);
    const my = 0.5 * (by_lo + by_hi);
    const mz = 0.5 * (bz_lo + bz_hi);
    const gi2 = gi * 2;
    const gj2 = gj * 2;
    const gk2 = gk * 2;
    const xs = [_][2]f32{ .{ bx_lo, mx }, .{ mx, bx_hi } };
    const ys = [_][2]f32{ .{ by_lo, my }, .{ my, by_hi } };
    const zs = [_][2]f32{ .{ bz_lo, mz }, .{ mz, bz_hi } };
    var zi: u32 = 0;
    while (zi < 2) : (zi += 1) {
        var yi: u32 = 0;
        while (yi < 2) : (yi += 1) {
            var xi: u32 = 0;
            while (xi < 2) : (xi += 1) {
                recurse(
                    ctx,
                    child_tape_ptr,
                    xs[xi][0], xs[xi][1],
                    ys[yi][0], ys[yi][1],
                    zs[zi][0], zs[zi][1],
                    depth + 1,
                    gi2 + xi,
                    gj2 + yi,
                    gk2 + zi,
                );
            }
        }
    }
}

pub fn build(
    tape: *const tape_mod.Tape,
    half: f32,
    max_depth: u32,
) struct { stats: Stats, vertex_count: u32 } {
    var stats: Stats = .{};
    stats.original_tape_ops = @intCast(tape.ops.len);

    // DC caps out at depth 7 (128³ grid). Clamp silently for now.
    const d = @min(max_depth, dc.MAX_DC_DEPTH);
    dc.reset(half, d);

    var ctx: Ctx = .{ .stats = &stats, .max_depth = d };
    recurse(&ctx, tape, -half, half, -half, half, -half, half, 0, 0, 0, 0);

    // All ambiguous leaves now have DC vertices; stitch them into quads.
    vertex_count = dc.emitQuads(&vertex_buffer, 0);
    stats.triangles_emitted = vertex_count / 3;

    return .{ .stats = stats, .vertex_count = vertex_count };
}
