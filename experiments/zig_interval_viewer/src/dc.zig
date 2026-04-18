const std = @import("std");
const tape_mod = @import("tape.zig");
const eval_mod = @import("eval.zig");
const grad_mod = @import("grad.zig");

// Dual contouring on a uniform grid at maxDepth.
//
// Unlike marching cubes, DC places a single vertex inside each surface-
// crossing cell (at a QEF-solved position that best fits the edge-
// crossing Hermite data), then emits a quad across every grid edge whose
// endpoints straddle the surface. The four cells adjacent to that edge
// contribute their DC vertices as the corners of the quad.
//
// A shared `sign_grid` keeps corner classifications consistent across
// adjacent cells — critical for DC, since two cells have to agree about
// whether any given edge is surface-crossing.

pub const MAX_DC_DEPTH: u32 = 8;
const GRID: u32 = 1 << MAX_DC_DEPTH; // 256
const CORN: u32 = GRID + 1; // 257
pub const MAX_LEAVES: u32 = 512 * 1024;

const SIGN_UNSET: u8 = 2;
const SIGN_OUTSIDE: u8 = 0;
const SIGN_INSIDE: u8 = 1;

const corner_offsets: [8][3]u8 = .{
    .{ 0, 0, 0 }, .{ 1, 0, 0 }, .{ 1, 1, 0 }, .{ 0, 1, 0 },
    .{ 0, 0, 1 }, .{ 1, 0, 1 }, .{ 1, 1, 1 }, .{ 0, 1, 1 },
};

const edge_endpoints: [12][2]u8 = .{
    .{ 0, 1 }, .{ 1, 2 }, .{ 2, 3 }, .{ 3, 0 },
    .{ 4, 5 }, .{ 5, 6 }, .{ 6, 7 }, .{ 7, 4 },
    .{ 0, 4 }, .{ 1, 5 }, .{ 2, 6 }, .{ 3, 7 },
};

pub const Leaf = struct {
    vertex: [3]f32,
    normal: [3]f32,
};

var sign_grid: [CORN * CORN * CORN]u8 = undefined;
var leaf_grid: [GRID * GRID * GRID]i32 = undefined;
var leaves: [MAX_LEAVES]Leaf = undefined;
var leaf_count: u32 = 0;
var current_half: f32 = 1.0;
var current_grid: u32 = 64;

inline fn sIdx(i: u32, j: u32, k: u32) usize {
    return @as(usize, i) * CORN * CORN + @as(usize, j) * CORN + @as(usize, k);
}

inline fn cIdx(i: u32, j: u32, k: u32) usize {
    return @as(usize, i) * GRID * GRID + @as(usize, j) * GRID + @as(usize, k);
}

inline fn gridCoord(idx: u32) f32 {
    return -current_half + 2.0 * current_half *
        @as(f32, @floatFromInt(idx)) / @as(f32, @floatFromInt(current_grid));
}

pub fn reset(half: f32, max_depth: u32) void {
    const d = @min(max_depth, MAX_DC_DEPTH);
    current_half = half;
    current_grid = @as(u32, 1) << @intCast(d);
    leaf_count = 0;
    // Only clear the active region. sign_grid touches corners [0, current_grid],
    // leaf_grid touches cells [0, current_grid).
    const s_extent = current_grid + 1;
    var i: u32 = 0;
    while (i < s_extent) : (i += 1) {
        var j: u32 = 0;
        while (j < s_extent) : (j += 1) {
            const row_start = sIdx(i, j, 0);
            @memset(sign_grid[row_start .. row_start + s_extent], SIGN_UNSET);
        }
    }
    i = 0;
    while (i < current_grid) : (i += 1) {
        var j: u32 = 0;
        while (j < current_grid) : (j += 1) {
            const row_start = cIdx(i, j, 0);
            @memset(std.mem.sliceAsBytes(leaf_grid[row_start .. row_start + current_grid]), 0xFF);
        }
    }
}

// Solve 3x3 symmetric positive-semidefinite system M·x = v via Cramer's
// rule. Returns a tiny det flag so the caller can fall back on rank
// deficiency. M is laid out as six unique entries.
const Mat3Sym = struct {
    m00: f32, m01: f32, m02: f32,
    m11: f32, m12: f32,
    m22: f32,
};

fn solveSym3(m: Mat3Sym, v: [3]f32) struct { x: [3]f32, ok: bool } {
    const a = m.m00; const b = m.m01; const c = m.m02;
    const d = m.m11; const e = m.m12;
    const f = m.m22;
    const det = a * (d * f - e * e) - b * (b * f - e * c) + c * (b * e - d * c);
    if (@abs(det) < 1e-8) return .{ .x = .{ 0, 0, 0 }, .ok = false };
    const inv = 1.0 / det;
    const x0 = (v[0] * (d * f - e * e) - b * (v[1] * f - e * v[2]) + c * (v[1] * e - d * v[2])) * inv;
    const x1 = (a * (v[1] * f - v[2] * e) - v[0] * (b * f - c * e) + c * (b * v[2] - v[1] * c)) * inv;
    const x2 = (a * (d * v[2] - e * v[1]) - b * (b * v[2] - v[1] * c) + v[0] * (b * e - d * c)) * inv;
    return .{ .x = .{ x0, x1, x2 }, .ok = true };
}

// Solve a regularized QEF: minimize Σ (n_i · (x - p_i))² + λ|x - c|²,
// where c is the cell centroid and λ small. The regularizer pulls x to
// the centroid when the normal set is degenerate (e.g. <3 linearly
// independent crossings), which keeps smooth regions well-behaved.
fn solveQef(
    crossings_p: [12][3]f32,
    crossings_n: [12][3]f32,
    count: u32,
    cell_lo: [3]f32,
    cell_hi: [3]f32,
) [3]f32 {
    const centroid: [3]f32 = .{
        0.5 * (cell_lo[0] + cell_hi[0]),
        0.5 * (cell_lo[1] + cell_hi[1]),
        0.5 * (cell_lo[2] + cell_hi[2]),
    };
    if (count == 0) return centroid;

    const lambda: f32 = 0.01;
    var m: Mat3Sym = .{ .m00 = lambda, .m01 = 0, .m02 = 0, .m11 = lambda, .m12 = 0, .m22 = lambda };
    var v: [3]f32 = .{ lambda * centroid[0], lambda * centroid[1], lambda * centroid[2] };

    var i: u32 = 0;
    while (i < count) : (i += 1) {
        const n = crossings_n[i];
        const p = crossings_p[i];
        const d = n[0] * p[0] + n[1] * p[1] + n[2] * p[2];
        m.m00 += n[0] * n[0];
        m.m01 += n[0] * n[1];
        m.m02 += n[0] * n[2];
        m.m11 += n[1] * n[1];
        m.m12 += n[1] * n[2];
        m.m22 += n[2] * n[2];
        v[0] += n[0] * d;
        v[1] += n[1] * d;
        v[2] += n[2] * d;
    }

    const r = solveSym3(m, v);
    var x = if (r.ok) r.x else centroid;
    // Clamp to cell bounds to stop outliers when the linear system is
    // poorly conditioned (e.g. one crossing with a grazing normal).
    x[0] = @max(cell_lo[0], @min(cell_hi[0], x[0]));
    x[1] = @max(cell_lo[1], @min(cell_hi[1], x[1]));
    x[2] = @max(cell_lo[2], @min(cell_hi[2], x[2]));
    return x;
}

pub fn processLeaf(
    tape: *const tape_mod.Tape,
    gi: u32,
    gj: u32,
    gk: u32,
    scalar_slots: []f32,
    grad_slots: []grad_mod.Grad,
) void {
    if (gi >= current_grid or gj >= current_grid or gk >= current_grid) return;
    if (leaf_count >= MAX_LEAVES) return;

    // Corner positions (world) and values.
    var cpos: [8][3]f32 = undefined;
    var cval: [8]f32 = undefined;
    var cix: [8]u32 = undefined;
    var cjy: [8]u32 = undefined;
    var ckz: [8]u32 = undefined;
    for (corner_offsets, 0..) |off, idx| {
        cix[idx] = gi + off[0];
        cjy[idx] = gj + off[1];
        ckz[idx] = gk + off[2];
        cpos[idx] = .{ gridCoord(cix[idx]), gridCoord(cjy[idx]), gridCoord(ckz[idx]) };
        cval[idx] = eval_mod.evalScalar(tape, cpos[idx][0], cpos[idx][1], cpos[idx][2], scalar_slots);
        // Publish this corner's sign so neighbouring cells see a consistent
        // value for quad enumeration. First writer wins; floating-point
        // disagreement across simplified tapes is tolerable because all
        // cells that share a corner will only disagree at values right on
        // the surface, and in that case either sign gives correct topology.
        const idx_s = sIdx(cix[idx], cjy[idx], ckz[idx]);
        if (sign_grid[idx_s] == SIGN_UNSET) {
            sign_grid[idx_s] = if (cval[idx] < 0) SIGN_INSIDE else SIGN_OUTSIDE;
        }
    }

    // Collect edge-crossing Hermite data (position + gradient).
    var xp: [12][3]f32 = undefined;
    var xn: [12][3]f32 = undefined;
    var xcount: u32 = 0;

    for (edge_endpoints) |ep| {
        const a = ep[0];
        const b = ep[1];
        const va = cval[a];
        const vb = cval[b];
        // Sign change = surface crossing. Use (va >= 0) != (vb >= 0) so that
        // zero-valued corners are treated as "outside" consistently with the
        // sign_grid publication above.
        if ((va >= 0) == (vb >= 0)) continue;
        const denom = va - vb;
        const t: f32 = if (@abs(denom) < 1e-12) 0.5 else va / denom;
        const pa = cpos[a];
        const pb = cpos[b];
        const p: [3]f32 = .{
            pa[0] + t * (pb[0] - pa[0]),
            pa[1] + t * (pb[1] - pa[1]),
            pa[2] + t * (pb[2] - pa[2]),
        };
        const g = grad_mod.evalGrad(tape, p[0], p[1], p[2], grad_slots);
        const mag = @sqrt(g[1] * g[1] + g[2] * g[2] + g[3] * g[3]);
        const n: [3]f32 = if (mag < 1e-9) .{ 0, 1, 0 } else .{ g[1] / mag, g[2] / mag, g[3] / mag };
        xp[xcount] = p;
        xn[xcount] = n;
        xcount += 1;
    }

    if (xcount == 0) return; // should be unreachable when the cell is truly ambiguous

    // QEF → vertex position within the cell.
    const cell_lo: [3]f32 = cpos[0];
    const cell_hi: [3]f32 = cpos[6];
    const vx = solveQef(xp, xn, xcount, cell_lo, cell_hi);

    // Normal at the QEF vertex (one extra AD eval — small compared to the
    // per-crossing ones we already did).
    const gv = grad_mod.evalGrad(tape, vx[0], vx[1], vx[2], grad_slots);
    const gv_mag = @sqrt(gv[1] * gv[1] + gv[2] * gv[2] + gv[3] * gv[3]);
    const vn: [3]f32 = if (gv_mag < 1e-9) .{ 0, 1, 0 } else .{ gv[1] / gv_mag, gv[2] / gv_mag, gv[3] / gv_mag };

    const idx = leaf_count;
    leaves[idx] = .{ .vertex = vx, .normal = vn };
    leaf_grid[cIdx(gi, gj, gk)] = @intCast(idx);
    leaf_count += 1;
}

inline fn getLeaf(i: i64, j: i64, k: i64) i32 {
    if (i < 0 or j < 0 or k < 0) return -1;
    if (i >= current_grid or j >= current_grid or k >= current_grid) return -1;
    return leaf_grid[cIdx(@intCast(i), @intCast(j), @intCast(k))];
}

fn emitQuad(a: u32, b: u32, c: u32, d: u32, flip: bool, out: []f32, vc: *u32) void {
    // Two triangles (a,b,c) + (a,c,d); winding reversed on `flip` so that
    // every quad faces "outward" (gradient at the surface points from
    // inside to outside; the grid edge goes from an inside corner to an
    // outside one).
    const order = if (flip)
        [_]u32{ a, c, b, a, d, c }
    else
        [_]u32{ a, b, c, a, c, d };

    for (order) |li| {
        const v = leaves[li];
        const base = @as(usize, vc.*) * 6;
        out[base + 0] = v.vertex[0];
        out[base + 1] = v.vertex[1];
        out[base + 2] = v.vertex[2];
        out[base + 3] = v.normal[0];
        out[base + 4] = v.normal[1];
        out[base + 5] = v.normal[2];
        vc.* += 1;
    }
}

// For every grid edge with a sign flip, look up the 4 adjacent cells and
// emit a quad connecting their DC vertices. Iterates over "forward" edges
// (from each corner in +X, +Y, +Z) so each edge is visited exactly once.
pub fn emitQuads(out: []f32, vc_in: u32) u32 {
    var vc = vc_in;
    const g = current_grid;
    const gc = g + 1;

    var ci: u32 = 0;
    while (ci < gc) : (ci += 1) {
        var cj: u32 = 0;
        while (cj < gc) : (cj += 1) {
            var ck: u32 = 0;
            while (ck < gc) : (ck += 1) {
                const s0 = sign_grid[sIdx(ci, cj, ck)];
                if (s0 == SIGN_UNSET) continue;

                // +X edge: endpoint at (ci+1, cj, ck). Adjacent cells have
                // corners at (ci, cj-1, ck-1), (ci, cj, ck-1), (ci, cj-1, ck), (ci, cj, ck).
                if (ci + 1 < gc) {
                    const s1 = sign_grid[sIdx(ci + 1, cj, ck)];
                    if (s1 != SIGN_UNSET and s0 != s1) {
                        const ii: i64 = ci;
                        const a = getLeaf(ii, @as(i64, cj) - 1, @as(i64, ck) - 1);
                        const b = getLeaf(ii, cj, @as(i64, ck) - 1);
                        const c_ = getLeaf(ii, cj, ck);
                        const dL = getLeaf(ii, @as(i64, cj) - 1, ck);
                        if (a >= 0 and b >= 0 and c_ >= 0 and dL >= 0 and vc + 6 <= out.len / 6) {
                            emitQuad(@intCast(a), @intCast(b), @intCast(c_), @intCast(dL), s0 == SIGN_INSIDE, out, &vc);
                        }
                    }
                }
                // +Y edge: endpoint at (ci, cj+1, ck). Adjacent cells:
                // (ci-1, cj, ck-1), (ci, cj, ck-1), (ci-1, cj, ck), (ci, cj, ck).
                if (cj + 1 < gc) {
                    const s1 = sign_grid[sIdx(ci, cj + 1, ck)];
                    if (s1 != SIGN_UNSET and s0 != s1) {
                        const jj: i64 = cj;
                        const a = getLeaf(@as(i64, ci) - 1, jj, @as(i64, ck) - 1);
                        const b = getLeaf(@as(i64, ci) - 1, jj, ck);
                        const c_ = getLeaf(ci, jj, ck);
                        const dL = getLeaf(ci, jj, @as(i64, ck) - 1);
                        if (a >= 0 and b >= 0 and c_ >= 0 and dL >= 0 and vc + 6 <= out.len / 6) {
                            emitQuad(@intCast(a), @intCast(b), @intCast(c_), @intCast(dL), s0 == SIGN_INSIDE, out, &vc);
                        }
                    }
                }
                // +Z edge: endpoint at (ci, cj, ck+1). Adjacent cells:
                // (ci-1, cj-1, ck), (ci, cj-1, ck), (ci-1, cj, ck), (ci, cj, ck).
                if (ck + 1 < gc) {
                    const s1 = sign_grid[sIdx(ci, cj, ck + 1)];
                    if (s1 != SIGN_UNSET and s0 != s1) {
                        const kk: i64 = ck;
                        const a = getLeaf(@as(i64, ci) - 1, @as(i64, cj) - 1, kk);
                        const b = getLeaf(ci, @as(i64, cj) - 1, kk);
                        const c_ = getLeaf(ci, cj, kk);
                        const dL = getLeaf(@as(i64, ci) - 1, cj, kk);
                        if (a >= 0 and b >= 0 and c_ >= 0 and dL >= 0 and vc + 6 <= out.len / 6) {
                            emitQuad(@intCast(a), @intCast(b), @intCast(c_), @intCast(dL), s0 == SIGN_INSIDE, out, &vc);
                        }
                    }
                }
            }
        }
    }
    return vc;
}
