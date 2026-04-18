const std = @import("std");
const tape_mod = @import("tape.zig");

const B = tape_mod.TapeBuilder;
const NR = tape_mod.NodeRef;

// ── 2D sketch primitives ─────────────────────────────────────────────────
// Mirrors the `SketchSdf` helpers in the F# codebase. All return a signed
// 2D distance; positive outside, negative inside (for closed primitives).

fn sd2dCircle(b: *B, x: NR, y: NR, cx: f32, cy: f32, r: f32) NR {
    const dx = b.sub(x, b.constant(cx));
    const dy = b.sub(y, b.constant(cy));
    return b.sub(b.sqrtOp(b.add(b.square(dx), b.square(dy))), b.constant(r));
}

fn sd2dSegment(b: *B, x: NR, y: NR, ax: f32, ay: f32, bx: f32, by: f32) NR {
    const ex = bx - ax;
    const ey = by - ay;
    const ee = ex * ex + ey * ey;
    const dx = b.sub(x, b.constant(ax));
    const dy = b.sub(y, b.constant(ay));
    const de = b.add(b.mul(dx, b.constant(ex)), b.mul(dy, b.constant(ey)));
    const t_raw = b.mul(de, b.constant(1.0 / ee));
    const t = b.minOp(b.maxOp(t_raw, b.constant(0)), b.constant(1));
    const px = b.sub(dx, b.mul(t, b.constant(ex)));
    const py = b.sub(dy, b.mul(t, b.constant(ey)));
    return b.sqrtOp(b.add(b.square(px), b.square(py)));
}

// Signed distance to a circular arc. Parameterised by center, radius,
// center_angle (angle of the arc's midpoint), and half_angle (half the
// arc's angular extent in radians, 0 < half_angle <= pi).
//
// The containment test uses the identity
//   ang_diff(q, c) = atan2(c.x*q.y - c.y*q.x,  c.x*q.x + c.y*q.y)
// so we only need one atan2 op rather than two + a modular subtract.
// Outside the angular span we fall back on the closest arc endpoint;
// the two branches are combined with a large-penalty gate.
fn sd2dArc(
    b: *B,
    x: NR,
    y: NR,
    cx: f32,
    cy: f32,
    r: f32,
    center_angle: f32,
    half_angle: f32,
) NR {
    const cv_x = @cos(center_angle);
    const cv_y = @sin(center_angle);
    const sx = cx + r * @cos(center_angle - half_angle);
    const sy = cy + r * @sin(center_angle - half_angle);
    const ex = cx + r * @cos(center_angle + half_angle);
    const ey = cy + r * @sin(center_angle + half_angle);

    const qx = b.sub(x, b.constant(cx));
    const qy = b.sub(y, b.constant(cy));

    // Signed angular diff between query and arc midpoint.
    const cross = b.sub(b.mul(b.constant(cv_x), qy), b.mul(b.constant(cv_y), qx));
    const dot_v = b.add(b.mul(b.constant(cv_x), qx), b.mul(b.constant(cv_y), qy));
    const ang_diff = b.atan2Op(cross, dot_v);
    const abs_diff = b.absOp(ang_diff);
    const score = b.sub(b.constant(half_angle), abs_diff);

    // Distance to the circle itself (valid when we are within the span).
    const radial = b.sqrtOp(b.add(b.square(qx), b.square(qy)));
    const arc_dist = b.absOp(b.sub(radial, b.constant(r)));

    // Distance to either endpoint (valid outside the span).
    const d_start = sd2dCircle(b, x, y, sx, sy, 0);
    const d_end = sd2dCircle(b, x, y, ex, ey, 0);
    const endpoint_dist = b.minOp(d_start, d_end);

    // Penalty-gate so that only the correct branch survives the min.
    const penalty = b.constant(100.0);
    const not_in = b.maxOp(b.neg(score), b.constant(0));
    const in_ = b.maxOp(score, b.constant(0));
    const gated_arc = b.add(arc_dist, b.mul(not_in, penalty));
    const gated_end = b.add(endpoint_dist, b.mul(in_, penalty));
    return b.minOp(gated_arc, gated_end);
}

// Take a signed 2D distance `d2` and extrude it along z with half-height h.
// Produces a 3D SDF whose 2D cross-section at any z ∈ [-h, h] matches d2.
fn sdExtrude(b: *B, d2: NR, z: NR, half_h: f32) NR {
    const wz = b.sub(b.absOp(z), b.constant(half_h));
    return b.maxOp(d2, wz);
}

// Convenience: "thicken" an open curve (e.g. a line segment or arc) by a
// constant width so it becomes a filled capsule-like region: d - t.
fn sd2dThicken(b: *B, d: NR, t: f32) NR {
    return b.sub(d, b.constant(t));
}

// ── Primitive SDFs ──────────────────────────────────────────────────────

fn sdSphere(b: *B, x: NR, y: NR, z: NR, cx: f32, cy: f32, cz: f32, r: f32) NR {
    const dx = b.sub(x, b.constant(cx));
    const dy = b.sub(y, b.constant(cy));
    const dz = b.sub(z, b.constant(cz));
    const s = b.add(b.add(b.square(dx), b.square(dy)), b.square(dz));
    const len = b.sqrtOp(s);
    return b.sub(len, b.constant(r));
}

// Inside-exact box SDF (conservative outside). Enough for interval bounds
// + smooth union/subtract, and the MC surface lives near zero where it's
// accurate anyway.
fn sdBox(b: *B, x: NR, y: NR, z: NR, cx: f32, cy: f32, cz: f32, w: f32, h: f32, d: f32) NR {
    const dx = b.sub(x, b.constant(cx));
    const dy = b.sub(y, b.constant(cy));
    const dz = b.sub(z, b.constant(cz));
    const qx = b.sub(b.absOp(dx), b.constant(w * 0.5));
    const qy = b.sub(b.absOp(dy), b.constant(h * 0.5));
    const qz = b.sub(b.absOp(dz), b.constant(d * 0.5));
    return b.maxOp(qx, b.maxOp(qy, qz));
}

// Y-axis aligned finite cylinder.
fn sdCylinder(b: *B, x: NR, y: NR, z: NR, cx: f32, cy: f32, cz: f32, r: f32, h: f32) NR {
    const dx = b.sub(x, b.constant(cx));
    const dy = b.sub(y, b.constant(cy));
    const dz = b.sub(z, b.constant(cz));
    const radial = b.sub(b.sqrtOp(b.add(b.square(dx), b.square(dz))), b.constant(r));
    const axial = b.sub(b.absOp(dy), b.constant(h * 0.5));
    return b.maxOp(radial, axial);
}

// ── Smooth boolean ops ──────────────────────────────────────────────────
// smin(a, c, k) = min(a, c) - max(k - |a-c|, 0)^2 / (4k)

fn smin(b: *B, a: NR, c: NR, k: f32) NR {
    const diff = b.sub(a, c);
    const adiff = b.absOp(diff);
    const slack = b.sub(b.constant(k), adiff);
    const clamped = b.maxOp(slack, b.constant(0));
    const squared = b.square(clamped);
    const scaled = b.mul(squared, b.constant(0.25 / k));
    const m = b.minOp(a, c);
    return b.sub(m, scaled);
}

fn smax(b: *B, a: NR, c: NR, k: f32) NR {
    return b.neg(smin(b, b.neg(a), b.neg(c), k));
}

fn ssub(b: *B, a: NR, c: NR, k: f32) NR {
    return smax(b, a, b.neg(c), k);
}

// ── Scene ───────────────────────────────────────────────────────────────
// Matches the hardcoded scene in pointer_mk18/experiments/interval_viewer/src/Scene.fs.

pub fn build(b: *B) NR {
    const x = b.inputX();
    const y = b.inputY();
    const z = b.inputZ();

    const s0 = sdSphere(b, x, y, z, 0.00, 0.00, 0.00, 0.75);
    const s1 = sdSphere(b, x, y, z, 0.85, 0.25, 0.10, 0.55);
    const s2 = sdSphere(b, x, y, z, -0.40, -0.55, 0.35, 0.55);
    const box = sdBox(b, x, y, z, 0.00, -0.80, 0.00, 1.80, 0.30, 1.60);

    // Hard booleans: union = min, subtract = max(a, -c). Sharp seams remain
    // as kinks in the SDF, and DC's QEF should pin vertices on those edges.
    const u_ab = b.minOp(s0, s1);
    const u_abc = b.minOp(u_ab, s2);
    const unions = b.minOp(u_abc, box);

    const cut_s = sdSphere(b, x, y, z, 0.55, 0.55, 0.55, 0.40);
    const cut_c = sdCylinder(b, x, y, z, 0.30, 0.10, 0.00, 0.22, 3.0);

    // Sketch-based cut: a thick arc on the XY plane, extruded along Z.
    // Exercises the atan2 op and the sketch helpers — equivalent to an
    // FSketch containing a single SpArcCenter, thickened and extruded.
    const pi: f32 = std.math.pi;
    const arc_2d = sd2dArc(b, x, y, 0.0, 0.0, 0.85, pi * 0.85, pi * 0.30);
    const arc_band = sd2dThicken(b, arc_2d, 0.06);
    const cut_arc = sdExtrude(b, arc_band, z, 0.40);

    const step1 = b.maxOp(unions, b.neg(cut_s));
    const step2 = b.maxOp(step1, b.neg(cut_c));
    return b.maxOp(step2, b.neg(cut_arc));
}
