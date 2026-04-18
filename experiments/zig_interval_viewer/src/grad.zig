const std = @import("std");
const tape_mod = @import("tape.zig");

// Forward-mode autodiff, packed as a v128: lane 0 is the value, lanes 1..3 are
// ∂/∂x, ∂/∂y, ∂/∂z. Packing as @Vector lets add/sub/neg/linear-scale collapse
// to single WASM v128 instructions. Product and quotient rules need a tiny
// fixup for lane 0 since the value is v_a*v_b, not 2*v_a*v_b.

pub const Grad = @Vector(4, f32);

pub inline fn gConst(c: f32) Grad {
    return .{ c, 0, 0, 0 };
}

inline fn gNeg(a: Grad) Grad {
    return -a;
}

inline fn gAbs(a: Grad) Grad {
    if (a[0] > 0) return a;
    if (a[0] < 0) return -a;
    return @splat(0);
}

inline fn gSqrt(a: Grad) Grad {
    if (a[0] <= 0) return @splat(0);
    const sv = @sqrt(a[0]);
    // result.v  = sqrt(v)
    // result.d* = d* * (0.5 / sqrt(v))
    const k: Grad = @splat(0.5 / sv);
    var r = a * k;
    r[0] = sv;
    return r;
}

inline fn gSquare(a: Grad) Grad {
    // result.v  = v*v
    // result.d* = 2v * d*
    const two_v: Grad = @splat(2.0 * a[0]);
    var r = two_v * a;
    r[0] = a[0] * a[0];
    return r;
}

inline fn gAdd(a: Grad, b: Grad) Grad {
    return a + b;
}

inline fn gSub(a: Grad, b: Grad) Grad {
    return a - b;
}

inline fn gMul(a: Grad, b: Grad) Grad {
    // Product rule: d(ab) = a·db + b·da.
    // broadcast(v_a)*b + broadcast(v_b)*a gives the correct derivatives but
    // double-counts the value; overwrite lane 0 with v_a*v_b.
    const va: Grad = @splat(a[0]);
    const vb: Grad = @splat(b[0]);
    var r = va * b + vb * a;
    r[0] = a[0] * b[0];
    return r;
}

inline fn gDiv(a: Grad, b: Grad) Grad {
    if (b[0] == 0) return @splat(0);
    const inv = 1.0 / b[0];
    const inv2: Grad = @splat(inv * inv);
    // Quotient rule: d(a/b) = (da·b - a·db) / b^2
    const va: Grad = @splat(a[0]);
    const vb: Grad = @splat(b[0]);
    var r = (vb * a - va * b) * inv2;
    r[0] = a[0] * inv;
    return r;
}

inline fn gMin(a: Grad, b: Grad) Grad {
    return if (a[0] <= b[0]) a else b;
}

inline fn gMax(a: Grad, b: Grad) Grad {
    return if (a[0] >= b[0]) a else b;
}

// atan2(y, x): value = atan2(y, x); derivatives = (x·dy − y·dx) / (x² + y²).
// Convention (matches tape op): a = y (first arg), b = x.
inline fn gAtan2(a: Grad, b: Grad) Grad {
    const y = a[0];
    const x = b[0];
    const r2 = x * x + y * y;
    if (r2 == 0) return @splat(0);
    const inv: Grad = @splat(1.0 / r2);
    const xs: Grad = @splat(x);
    const ys: Grad = @splat(y);
    var r = (xs * a - ys * b) * inv;
    r[0] = std.math.atan2(y, x);
    return r;
}

pub fn evalGrad(
    tape: *const tape_mod.Tape,
    x: f32,
    y: f32,
    z: f32,
    slots: []Grad,
) Grad {
    for (tape.ops, 0..) |ins, i| {
        slots[i] = switch (ins.op) {
            .input_x => Grad{ x, 1, 0, 0 },
            .input_y => Grad{ y, 0, 1, 0 },
            .input_z => Grad{ z, 0, 0, 1 },
            .constant => gConst(tape.constants[ins.a]),
            .neg => gNeg(slots[ins.a]),
            .abs => gAbs(slots[ins.a]),
            .sqrt => gSqrt(slots[ins.a]),
            .square => gSquare(slots[ins.a]),
            .add => gAdd(slots[ins.a], slots[ins.b]),
            .sub => gSub(slots[ins.a], slots[ins.b]),
            .mul => gMul(slots[ins.a], slots[ins.b]),
            .div => gDiv(slots[ins.a], slots[ins.b]),
            .min => gMin(slots[ins.a], slots[ins.b]),
            .max => gMax(slots[ins.a], slots[ins.b]),
            .atan2 => gAtan2(slots[ins.a], slots[ins.b]),
        };
    }
    return slots[tape.output_slot];
}
