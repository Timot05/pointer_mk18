import { Record } from "../../ui/fable_modules/fable-library-js.4.29.0/Types.js";
import { record_type, float64_type } from "../../ui/fable_modules/fable-library-js.4.29.0/Reflection.js";
import { max, min } from "../../ui/fable_modules/fable-library-js.4.29.0/Double.js";
import { item } from "../../ui/fable_modules/fable-library-js.4.29.0/Array.js";
import { evalAt } from "./SketchSdf.fs.js";
import { FieldNode } from "./FieldIR.fs.js";

export class Interval extends Record {
    constructor(Lo, Hi) {
        super();
        this.Lo = Lo;
        this.Hi = Hi;
    }
}

export function Interval_$reflection() {
    return record_type("Server.Interval", [], Interval, () => [["Lo", float64_type], ["Hi", float64_type]]);
}

export function IntervalModule_single(v) {
    return new Interval(v, v);
}

export function IntervalModule_make(a, b) {
    if (a <= b) {
        return new Interval(a, b);
    }
    else {
        return new Interval(b, a);
    }
}

export const IntervalModule_unknown = new Interval(-Infinity, Infinity);

export function IntervalModule_contains(i, v) {
    if (v >= i.Lo) {
        return v <= i.Hi;
    }
    else {
        return false;
    }
}

export function IntervalModule_neg(i) {
    return new Interval(-i.Hi, -i.Lo);
}

export function IntervalModule_add(a, b) {
    return new Interval(a.Lo + b.Lo, a.Hi + b.Hi);
}

export function IntervalModule_sub(a, b) {
    return new Interval(a.Lo - b.Hi, a.Hi - b.Lo);
}

export function IntervalModule_mul(a, b) {
    const p1 = a.Lo * b.Lo;
    const p2 = a.Lo * b.Hi;
    const p3 = a.Hi * b.Lo;
    const p4 = a.Hi * b.Hi;
    return new Interval(min(min(p1, p2), min(p3, p4)), max(max(p1, p2), max(p3, p4)));
}

export function IntervalModule_imin(a, b) {
    return new Interval(min(a.Lo, b.Lo), min(a.Hi, b.Hi));
}

export function IntervalModule_imax(a, b) {
    return new Interval(max(a.Lo, b.Lo), max(a.Hi, b.Hi));
}

export function IntervalModule_abs(i) {
    if (i.Lo >= 0) {
        return i;
    }
    else if (i.Hi <= 0) {
        return IntervalModule_neg(i);
    }
    else {
        return new Interval(0, max(-i.Lo, i.Hi));
    }
}

/**
 * Interval sqrt. Clamps negative inputs to 0 — callers should only
 * invoke on intervals known non-negative (e.g. sums of squares).
 */
export function IntervalModule_sqrt(i) {
    return new Interval(Math.sqrt(max(i.Lo, 0)), Math.sqrt(max(i.Hi, 0)));
}

export function IntervalModule_square(i) {
    if (i.Lo >= 0) {
        return new Interval(i.Lo * i.Lo, i.Hi * i.Hi);
    }
    else if (i.Hi <= 0) {
        return new Interval(i.Hi * i.Hi, i.Lo * i.Lo);
    }
    else {
        return new Interval(0, max(i.Lo * i.Lo, i.Hi * i.Hi));
    }
}

/**
 * Width of the interval (Hi - Lo). Useful in tests.
 */
export function IntervalModule_width(i) {
    return i.Hi - i.Lo;
}

export class IntervalBox extends Record {
    constructor(XI, YI, ZI) {
        super();
        this.XI = XI;
        this.YI = YI;
        this.ZI = ZI;
    }
}

export function IntervalBox_$reflection() {
    return record_type("Server.IntervalBox", [], IntervalBox, () => [["XI", Interval_$reflection()], ["YI", Interval_$reflection()], ["ZI", Interval_$reflection()]]);
}

export function IntervalBoxModule_make(x, y, z) {
    return new IntervalBox(x, y, z);
}

export function IntervalBoxModule_cube(lo, hi) {
    const i = IntervalModule_make(lo, hi);
    return new IntervalBox(i, i, i);
}

function FieldInterval_slotV(slots, s) {
    return item(s, slots.Values);
}

/**
 * Returns an Interval that conservatively bounds the SDF value of `node`
 * over the input `box`.
 */
export function FieldInterval_eval(slots_mut, box_mut, node_mut) {
    FieldInterval_eval:
    while (true) {
        const slots = slots_mut, box = box_mut, node = node_mut;
        switch (node.tag) {
            case 1: {
                const dx = FieldInterval_slotV(slots, node.fields[0]);
                const dy = FieldInterval_slotV(slots, node.fields[1]);
                const dz = FieldInterval_slotV(slots, node.fields[2]);
                slots_mut = slots;
                box_mut = (new IntervalBox(IntervalModule_sub(box.XI, IntervalModule_single(dx)), IntervalModule_sub(box.YI, IntervalModule_single(dy)), IntervalModule_sub(box.ZI, IntervalModule_single(dz))));
                node_mut = node.fields[3];
                continue FieldInterval_eval;
            }
            case 3: {
                const op = node.fields[0];
                const ia = FieldInterval_eval(slots, box, node.fields[2]);
                const ib = FieldInterval_eval(slots, box, node.fields[3]);
                const k = FieldInterval_slotV(slots, node.fields[1]);
                switch (op.tag) {
                    case 2:
                        return IntervalModule_neg(FieldInterval_smoothMin(IntervalModule_neg(ia), IntervalModule_neg(ib), k));
                    case 1:
                        return IntervalModule_neg(FieldInterval_smoothMin(IntervalModule_neg(ia), ib, k));
                    default:
                        return FieldInterval_smoothMin(ia, ib, k);
                }
            }
            case 4: {
                const v = FieldInterval_slotV(slots, node.fields[1]);
                const ic = FieldInterval_eval(slots, box, node.fields[2]);
                if (node.fields[0].tag === 1) {
                    return IntervalModule_imax(ic, IntervalModule_neg(IntervalModule_add(ic, IntervalModule_single(v))));
                }
                else {
                    return IntervalModule_sub(ic, IntervalModule_single(v));
                }
            }
            case 2:
                return IntervalModule_unknown;
            case 5: {
                const v_1 = evalAt(slots, node.fields[0], (box.XI.Lo + box.XI.Hi) * 0.5, (box.YI.Lo + box.YI.Hi) * 0.5);
                const dx_1 = (box.XI.Hi - box.XI.Lo) * 0.5;
                const dy_1 = (box.YI.Hi - box.YI.Lo) * 0.5;
                const halfDiag = Math.sqrt((dx_1 * dx_1) + (dy_1 * dy_1));
                return new Interval(v_1 - halfDiag, v_1 + halfDiag);
            }
            default:
                return FieldInterval_evalPrimitive(slots, box, node.fields[0]);
        }
        break;
    }
}

/**
 * Same as `eval`, but also returns a potentially smaller `FieldNode`
 * with dominated boolean branches replaced by the surviving child.
 * Deeper recursions evaluate a smaller tree — the tape-simplification
 * win from Fidget. Rules use `k` as the smooth-min slop:
 * Union:     drop B if A.Hi + k ≤ B.Lo  (A dominates)
 * drop A if B.Hi + k ≤ A.Lo
 * Intersect: drop B if A.Lo ≥ B.Hi + k
 * drop A if B.Lo ≥ A.Hi + k
 * Subtract:  drop B if A.Lo + B.Lo ≥ k
 */
export function FieldInterval_simplify(slots, box, node) {
    switch (node.tag) {
        case 1: {
            const sz = node.fields[2] | 0;
            const sy = node.fields[1] | 0;
            const sx = node.fields[0] | 0;
            const dx = FieldInterval_slotV(slots, sx);
            const dy = FieldInterval_slotV(slots, sy);
            const dz = FieldInterval_slotV(slots, sz);
            const patternInput = FieldInterval_simplify(slots, new IntervalBox(IntervalModule_sub(box.XI, IntervalModule_single(dx)), IntervalModule_sub(box.YI, IntervalModule_single(dy)), IntervalModule_sub(box.ZI, IntervalModule_single(dz))), node.fields[3]);
            return [patternInput[0], new FieldNode(1, [sx, sy, sz, patternInput[1]])];
        }
        case 3: {
            const op = node.fields[0];
            const kSlot = node.fields[1] | 0;
            const patternInput_1 = FieldInterval_simplify(slots, box, node.fields[2]);
            const sa = patternInput_1[1];
            const ia = patternInput_1[0];
            const patternInput_2 = FieldInterval_simplify(slots, box, node.fields[3]);
            const sb = patternInput_2[1];
            const ib = patternInput_2[0];
            const k = FieldInterval_slotV(slots, kSlot);
            switch (op.tag) {
                case 2:
                    if (ia.Lo >= (ib.Hi + k)) {
                        return [ia, sa];
                    }
                    else if (ib.Lo >= (ia.Hi + k)) {
                        return [ib, sb];
                    }
                    else {
                        return [IntervalModule_neg(FieldInterval_smoothMin(IntervalModule_neg(ia), IntervalModule_neg(ib), k)), new FieldNode(3, [op, kSlot, sa, sb])];
                    }
                case 1:
                    if ((ia.Lo + ib.Lo) >= k) {
                        return [ia, sa];
                    }
                    else {
                        return [IntervalModule_neg(FieldInterval_smoothMin(IntervalModule_neg(ia), ib, k)), new FieldNode(3, [op, kSlot, sa, sb])];
                    }
                default:
                    if ((ia.Hi + k) <= ib.Lo) {
                        return [ia, sa];
                    }
                    else if ((ib.Hi + k) <= ia.Lo) {
                        return [ib, sb];
                    }
                    else {
                        return [FieldInterval_smoothMin(ia, ib, k), new FieldNode(3, [op, kSlot, sa, sb])];
                    }
            }
        }
        case 4: {
            const vSlot = node.fields[1] | 0;
            const op_1 = node.fields[0];
            const v = FieldInterval_slotV(slots, vSlot);
            const patternInput_3 = FieldInterval_simplify(slots, box, node.fields[2]);
            const ic = patternInput_3[0];
            return [(op_1.tag === 1) ? IntervalModule_imax(ic, IntervalModule_neg(IntervalModule_add(ic, IntervalModule_single(v)))) : IntervalModule_sub(ic, IntervalModule_single(v)), new FieldNode(4, [op_1, vSlot, patternInput_3[1]])];
        }
        case 2:
        case 5:
            return [IntervalModule_unknown, node];
        default:
            return [FieldInterval_evalPrimitive(slots, box, node.fields[0]), node];
    }
}

function FieldInterval_smoothMin(a, b, k) {
    const sharp = IntervalModule_imin(a, b);
    if (k <= 0) {
        return sharp;
    }
    else {
        return new Interval(sharp.Lo - (k / 6), sharp.Hi);
    }
}

function FieldInterval_evalPrimitive(slots, box, prim) {
    switch (prim.tag) {
        case 3: {
            const axis = prim.fields[0];
            const off = FieldInterval_slotV(slots, prim.fields[1]);
            const raw = IntervalModule_sub((axis === "X") ? box.XI : ((axis === "Y") ? box.YI : box.ZI), IntervalModule_single(off));
            if (prim.fields[2]) {
                return IntervalModule_neg(raw);
            }
            else {
                return raw;
            }
        }
        case 2: {
            const hx = FieldInterval_slotV(slots, prim.fields[0]) * 0.5;
            const hy = FieldInterval_slotV(slots, prim.fields[1]) * 0.5;
            const hz = FieldInterval_slotV(slots, prim.fields[2]) * 0.5;
            const qx = IntervalModule_sub(IntervalModule_abs(box.XI), IntervalModule_single(hx));
            const qy = IntervalModule_sub(IntervalModule_abs(box.YI), IntervalModule_single(hy));
            const qz = IntervalModule_sub(IntervalModule_abs(box.ZI), IntervalModule_single(hz));
            const zero = IntervalModule_single(0);
            const mx = IntervalModule_imax(qx, zero);
            const my = IntervalModule_imax(qy, zero);
            const mz = IntervalModule_imax(qz, zero);
            return IntervalModule_add(IntervalModule_sqrt(IntervalModule_add(IntervalModule_add(IntervalModule_square(mx), IntervalModule_square(my)), IntervalModule_square(mz))), IntervalModule_imin(IntervalModule_imax(qx, IntervalModule_imax(qy, qz)), zero));
        }
        case 1: {
            const r_1 = FieldInterval_slotV(slots, prim.fields[0]);
            const halfH = FieldInterval_slotV(slots, prim.fields[1]) * 0.5;
            const dRadial = IntervalModule_sub(IntervalModule_sqrt(IntervalModule_add(IntervalModule_square(box.XI), IntervalModule_square(box.YI))), IntervalModule_single(r_1));
            const dAxial = IntervalModule_sub(IntervalModule_abs(box.ZI), IntervalModule_single(halfH));
            const branch1 = IntervalModule_sqrt(IntervalModule_add(IntervalModule_square(dRadial), IntervalModule_square(dAxial)));
            const branch2 = IntervalModule_imax(dRadial, dAxial);
            const matchValue = (dRadial.Lo > 0) && (dAxial.Lo > 0);
            const matchValue_1 = (dRadial.Hi <= 0) ? true : (dAxial.Hi <= 0);
            if (matchValue) {
                return branch1;
            }
            else if (matchValue_1) {
                return branch2;
            }
            else {
                return new Interval(min(branch1.Lo, branch2.Lo), max(branch1.Hi, branch2.Hi));
            }
        }
        default: {
            const r = FieldInterval_slotV(slots, prim.fields[0]);
            return IntervalModule_sub(IntervalModule_sqrt(IntervalModule_add(IntervalModule_add(IntervalModule_square(box.XI), IntervalModule_square(box.YI)), IntervalModule_square(box.ZI))), IntervalModule_single(r));
        }
    }
}

