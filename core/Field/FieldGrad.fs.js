import { Record } from "../../ui/fable_modules/fable-library-js.4.29.0/Types.js";
import { record_type, float64_type } from "../../ui/fable_modules/fable-library-js.4.29.0/Reflection.js";
import { max } from "../../ui/fable_modules/fable-library-js.4.29.0/Double.js";
import { item } from "../../ui/fable_modules/fable-library-js.4.29.0/Array.js";
import { evalAt } from "./SketchSdf.fs.js";

export class Grad extends Record {
    constructor(V, Dx, Dy, Dz) {
        super();
        this.V = V;
        this.Dx = Dx;
        this.Dy = Dy;
        this.Dz = Dz;
    }
}

export function Grad_$reflection() {
    return record_type("Server.Grad", [], Grad, () => [["V", float64_type], ["Dx", float64_type], ["Dy", float64_type], ["Dz", float64_type]]);
}

export const GradModule_zero = new Grad(0, 0, 0, 0);

export function GradModule_constant(v) {
    return new Grad(v, 0, 0, 0);
}

export function GradModule_neg(g) {
    return new Grad(-g.V, -g.Dx, -g.Dy, -g.Dz);
}

export function GradModule_add(a, b) {
    return new Grad(a.V + b.V, a.Dx + b.Dx, a.Dy + b.Dy, a.Dz + b.Dz);
}

export function GradModule_sub(a, b) {
    return new Grad(a.V - b.V, a.Dx - b.Dx, a.Dy - b.Dy, a.Dz - b.Dz);
}

/**
 * Product rule: (ab)' = a'b + ab'.
 */
export function GradModule_mul(a, b) {
    return new Grad(a.V * b.V, (a.V * b.Dx) + (a.Dx * b.V), (a.V * b.Dy) + (a.Dy * b.V), (a.V * b.Dz) + (a.Dz * b.V));
}

export function GradModule_scale(s, g) {
    return new Grad(s * g.V, s * g.Dx, s * g.Dy, s * g.Dz);
}

/**
 * Square by self-multiply — re-uses product rule.
 */
export function GradModule_square(g) {
    return GradModule_mul(g, g);
}

/**
 * d/dx sqrt(f) = f' / (2 sqrt(f)). Clamps negative inputs to 0 at the
 * value layer; derivative near 0 is large but finite (we clamp √V to
 * a tiny epsilon to avoid divide-by-zero).
 */
export function GradModule_sqrt(g) {
    const k = 1 / (2 * Math.sqrt(max(g.V, 1E-12)));
    return new Grad((g.V > 0) ? Math.sqrt(g.V) : 0, k * g.Dx, k * g.Dy, k * g.Dz);
}

/**
 * d/dx |f| = sign(f) * f'. Subgradient at f=0: pick 0.
 */
export function GradModule_abs(g) {
    const s = (g.V > 0) ? 1 : ((g.V < 0) ? -1 : 0);
    return new Grad(Math.abs(g.V), s * g.Dx, s * g.Dy, s * g.Dz);
}

/**
 * min picks the whole Grad of whichever has the smaller value.
 * At ties, pick `a` (consistent subgradient choice).
 */
export function GradModule_imin(a, b) {
    if (a.V <= b.V) {
        return a;
    }
    else {
        return b;
    }
}

export function GradModule_imax(a, b) {
    if (a.V >= b.V) {
        return a;
    }
    else {
        return b;
    }
}

/**
 * max(g, 0) — clamps negative values to 0; derivative is 0 there.
 */
export function GradModule_clampNonNeg(g) {
    if (g.V > 0) {
        return g;
    }
    else {
        return GradModule_constant(0);
    }
}

function FieldGrad_slotV(slots, s) {
    return item(s, slots.Values);
}

function FieldGrad_smoothMin(a, b, k) {
    if (k <= 1E-12) {
        return GradModule_imin(a, b);
    }
    else {
        const m = GradModule_imin(a, b);
        const diffAbs = GradModule_abs(GradModule_sub(a, b));
        const h = GradModule_clampNonNeg(GradModule_sub(GradModule_constant(1), GradModule_scale(1 / k, diffAbs)));
        return GradModule_sub(m, GradModule_scale(k / 6, GradModule_mul(h, GradModule_square(h))));
    }
}

export function FieldGrad_eval(slots_mut, point__mut, point__1_mut, point__2_mut, node_mut) {
    FieldGrad_eval:
    while (true) {
        const slots = slots_mut, point_ = point__mut, point__1 = point__1_mut, point__2 = point__2_mut, node = node_mut;
        const point = [point_, point__1, point__2];
        const z = point[2];
        const y = point[1];
        const x = point[0];
        switch (node.tag) {
            case 1: {
                slots_mut = slots;
                point__mut = (new Grad(x.V - FieldGrad_slotV(slots, node.fields[0]), x.Dx, x.Dy, x.Dz));
                point__1_mut = (new Grad(y.V - FieldGrad_slotV(slots, node.fields[1]), y.Dx, y.Dy, y.Dz));
                point__2_mut = (new Grad(z.V - FieldGrad_slotV(slots, node.fields[2]), z.Dx, z.Dy, z.Dz));
                node_mut = node.fields[3];
                continue FieldGrad_eval;
            }
            case 3: {
                const op = node.fields[0];
                const ga = FieldGrad_eval(slots, point[0], point[1], point[2], node.fields[2]);
                const gb = FieldGrad_eval(slots, point[0], point[1], point[2], node.fields[3]);
                const k = FieldGrad_slotV(slots, node.fields[1]);
                switch (op.tag) {
                    case 2:
                        return GradModule_neg(FieldGrad_smoothMin(GradModule_neg(ga), GradModule_neg(gb), k));
                    case 1:
                        return GradModule_neg(FieldGrad_smoothMin(GradModule_neg(ga), gb, k));
                    default:
                        return FieldGrad_smoothMin(ga, gb, k);
                }
            }
            case 4: {
                const v = FieldGrad_slotV(slots, node.fields[1]);
                const gc = FieldGrad_eval(slots, point[0], point[1], point[2], node.fields[2]);
                if (node.fields[0].tag === 1) {
                    return GradModule_imax(gc, GradModule_neg(GradModule_add(gc, GradModule_constant(v))));
                }
                else {
                    return GradModule_sub(gc, GradModule_constant(v));
                }
            }
            case 2:
                throw new Error("FieldGrad.eval: FRotate not implemented yet");
            case 5: {
                const sketch = node.fields[0];
                const y_1 = point[1];
                const x_1 = point[0];
                const px = x_1.V;
                const py = y_1.V;
                const v_1 = evalAt(slots, sketch, px, py);
                const dvdx = (evalAt(slots, sketch, px + 0.0001, py) - evalAt(slots, sketch, px - 0.0001, py)) / (2 * 0.0001);
                const dvdy = (evalAt(slots, sketch, px, py + 0.0001) - evalAt(slots, sketch, px, py - 0.0001)) / (2 * 0.0001);
                return new Grad(v_1, (dvdx * x_1.Dx) + (dvdy * y_1.Dx), (dvdx * x_1.Dy) + (dvdy * y_1.Dy), (dvdx * x_1.Dz) + (dvdy * y_1.Dz));
            }
            default:
                return FieldGrad_evalPrimitive(slots, point[0], point[1], point[2], node.fields[0]);
        }
        break;
    }
}

function FieldGrad_evalPrimitive(slots, point_, point__1, point__2, prim) {
    const point = [point_, point__1, point__2];
    const z = point[2];
    const y = point[1];
    const x = point[0];
    switch (prim.tag) {
        case 3: {
            const axis = prim.fields[0];
            const off = FieldGrad_slotV(slots, prim.fields[1]);
            const raw = GradModule_sub((axis === "X") ? x : ((axis === "Y") ? y : z), GradModule_constant(off));
            if (prim.fields[2]) {
                return GradModule_neg(raw);
            }
            else {
                return raw;
            }
        }
        case 2: {
            const hx = FieldGrad_slotV(slots, prim.fields[0]) * 0.5;
            const hy = FieldGrad_slotV(slots, prim.fields[1]) * 0.5;
            const hz = FieldGrad_slotV(slots, prim.fields[2]) * 0.5;
            const qx = GradModule_sub(GradModule_abs(x), GradModule_constant(hx));
            const qy = GradModule_sub(GradModule_abs(y), GradModule_constant(hy));
            const qz = GradModule_sub(GradModule_abs(z), GradModule_constant(hz));
            const zero = GradModule_constant(0);
            return GradModule_add(GradModule_sqrt(GradModule_add(GradModule_add(GradModule_square(GradModule_imax(qx, zero)), GradModule_square(GradModule_imax(qy, zero))), GradModule_square(GradModule_imax(qz, zero)))), GradModule_imin(GradModule_imax(qx, GradModule_imax(qy, qz)), zero));
        }
        case 1: {
            const r_1 = FieldGrad_slotV(slots, prim.fields[0]);
            const halfH = FieldGrad_slotV(slots, prim.fields[1]) * 0.5;
            const dRadial = GradModule_sub(GradModule_sqrt(GradModule_add(GradModule_square(x), GradModule_square(y))), GradModule_constant(r_1));
            const dAxial = GradModule_sub(GradModule_abs(z), GradModule_constant(halfH));
            if ((dRadial.V > 0) && (dAxial.V > 0)) {
                return GradModule_sqrt(GradModule_add(GradModule_square(dRadial), GradModule_square(dAxial)));
            }
            else {
                return GradModule_imax(dRadial, dAxial);
            }
        }
        default: {
            const r = FieldGrad_slotV(slots, prim.fields[0]);
            return GradModule_sub(GradModule_sqrt(GradModule_add(GradModule_add(GradModule_square(x), GradModule_square(y)), GradModule_square(z))), GradModule_constant(r));
        }
    }
}

/**
 * Convenience: seed x, y, z as independent variables and evaluate.
 * Returns (V, ∂V/∂x, ∂V/∂y, ∂V/∂z) at the query point.
 */
export function FieldGrad_evalAt(slots, x, y, z, node) {
    return FieldGrad_eval(slots, new Grad(x, 1, 0, 0), new Grad(y, 0, 1, 0), new Grad(z, 0, 0, 1), node);
}

