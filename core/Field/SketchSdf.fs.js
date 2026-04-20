import { min, max } from "../../ui/fable_modules/fable-library-js.4.29.0/Double.js";
import { item } from "../../ui/fable_modules/fable-library-js.4.29.0/Array.js";
import { sumBy, map, min as min_1 } from "../../ui/fable_modules/fable-library-js.4.29.0/List.js";
import { comparePrimitives } from "../../ui/fable_modules/fable-library-js.4.29.0/Util.js";

function positiveAngleDelta(a, b) {
    const tau = 2 * 3.141592653589793;
    const d = b - a;
    return d - (tau * Math.floor(d / tau));
}

function arcContains(startA, endA, query, cw) {
    if (cw) {
        return positiveAngleDelta(endA, query) <= positiveAngleDelta(endA, startA);
    }
    else {
        return positiveAngleDelta(startA, query) <= positiveAngleDelta(startA, endA);
    }
}

export function segDist(p_, p__1, a_, a__1, b_, b__1) {
    const p = [p_, p__1];
    const a = [a_, a__1];
    const b = [b_, b__1];
    const ay = a[1];
    const ax = a[0];
    const ex = b[0] - ax;
    const ey = b[1] - ay;
    const wx = p[0] - ax;
    const wy = p[1] - ay;
    const t = max(0, min(1, ((wx * ex) + (wy * ey)) / (((ex * ex) + (ey * ey)) + 1E-20)));
    const dx = wx - (ex * t);
    const dy = wy - (ey * t);
    return Math.sqrt((dx * dx) + (dy * dy));
}

export function circleCurveDist(p_, p__1, center_, center__1, radius) {
    const p = [p_, p__1];
    const center = [center_, center__1];
    const dx = p[0] - center[0];
    const dy = p[1] - center[1];
    return Math.abs(Math.sqrt((dx * dx) + (dy * dy)) - radius);
}

export function arcCurveDist(p_, p__1, startP_, startP__1, endP_, endP__1, center_, center__1, cw) {
    const p = [p_, p__1];
    const startP = [startP_, startP__1];
    const endP = [endP_, endP__1];
    const center = [center_, center__1];
    const py = p[1];
    const px = p[0];
    const sy = startP[1];
    const sx = startP[0];
    const ey = endP[1];
    const ex = endP[0];
    const cy = center[1];
    const cx = center[0];
    const radius = Math.sqrt(((sx - cx) * (sx - cx)) + ((sy - cy) * (sy - cy)));
    if (radius < 1E-06) {
        return segDist(p[0], p[1], startP[0], startP[1], endP[0], endP[1]);
    }
    else {
        const qx = px - cx;
        const qy = py - cy;
        if (arcContains(Math.atan2(sy - cy, sx - cx), Math.atan2(ey - cy, ex - cx), Math.atan2(qy, qx), cw)) {
            return Math.abs(Math.sqrt((qx * qx) + (qy * qy)) - radius);
        }
        else {
            return min(Math.sqrt(((px - sx) * (px - sx)) + ((py - sy) * (py - sy))), Math.sqrt(((px - ex) * (px - ex)) + ((py - ey) * (py - ey))));
        }
    }
}

export function rayCrossLineSegment(p_, p__1, a_, a__1, b_, b__1) {
    const p = [p_, p__1];
    const a = [a_, a__1];
    const b = [b_, b__1];
    const py = p[1];
    const ay = a[1];
    const ax = a[0];
    const by = b[1];
    if ((ay > py) === (by > py)) {
        return 0;
    }
    else if ((ax + (((py - ay) / (by - ay)) * (b[0] - ax))) > p[0]) {
        return 1;
    }
    else {
        return 0;
    }
}

export function rayCrossCircle(p_, p__1, center_, center__1, radius) {
    const p = [p_, p__1];
    const center = [center_, center__1];
    const px = p[0];
    const cx = center[0];
    const dy = p[1] - center[1];
    const disc = (radius * radius) - (dy * dy);
    if (disc <= 1E-07) {
        return 0;
    }
    else {
        const h = Math.sqrt(disc);
        return ((((cx - h) > px) ? 1 : 0) + (((cx + h) > px) ? 1 : 0)) | 0;
    }
}

export function rayCrossArc(p_, p__1, startP_, startP__1, endP_, endP__1, center_, center__1, cw) {
    const p = [p_, p__1];
    const startP = [startP_, startP__1];
    const endP = [endP_, endP__1];
    const center = [center_, center__1];
    const py = p[1];
    const sy = startP[1];
    const sx = startP[0];
    const ey = endP[1];
    const ex = endP[0];
    const cy = center[1];
    const cx = center[0];
    const radius = Math.sqrt(((sx - cx) * (sx - cx)) + ((sy - cy) * (sy - cy)));
    if (radius < 1E-06) {
        return 0;
    }
    else {
        const dy = py - cy;
        const disc = (radius * radius) - (dy * dy);
        if (disc <= 1E-07) {
            return 0;
        }
        else {
            const h = Math.sqrt(disc);
            const startAngle = Math.atan2(sy - cy, sx - cx);
            const endAngle = Math.atan2(ey - cy, ex - cx);
            const countAt = (xx) => {
                if (xx > p[0]) {
                    if (arcContains(startAngle, endAngle, Math.atan2(py - cy, xx - cx), cw) && ((Math.abs(xx - ex) > 1E-05) ? true : (Math.abs(py - ey) > 1E-05))) {
                        return 1;
                    }
                    else {
                        return 0;
                    }
                }
                else {
                    return 0;
                }
            };
            return (countAt(cx - h) + countAt(cx + h)) | 0;
        }
    }
}

/**
 * Scalar 2D signed distance for a sketch: min distance to boundary,
 * flipped sign via ray-crossings count if closed.
 */
export function evalAt(slots, sketch, p_, p__1) {
    const p = [p_, p__1];
    const slot = (s) => item(s, slots.Values);
    const pt = (sp) => [slot(sp.XSlot), slot(sp.YSlot)];
    const minD = min_1(map((prim) => {
        switch (prim.tag) {
            case 1: {
                const tupledArg_2 = pt(prim.fields[0]);
                return circleCurveDist(p[0], p[1], tupledArg_2[0], tupledArg_2[1], slot(prim.fields[1]));
            }
            case 2: {
                const tupledArg_3 = pt(prim.fields[0]);
                const tupledArg_4 = pt(prim.fields[1]);
                const tupledArg_5 = pt(prim.fields[2]);
                return arcCurveDist(p[0], p[1], tupledArg_3[0], tupledArg_3[1], tupledArg_4[0], tupledArg_4[1], tupledArg_5[0], tupledArg_5[1], prim.fields[3]);
            }
            default: {
                const tupledArg = pt(prim.fields[0]);
                const tupledArg_1 = pt(prim.fields[1]);
                return segDist(p[0], p[1], tupledArg[0], tupledArg[1], tupledArg_1[0], tupledArg_1[1]);
            }
        }
    }, sketch.Primitives), {
        Compare: comparePrimitives,
    });
    if (!sketch.Closed) {
        return minD;
    }
    else {
        const inside = (sumBy((prim_1) => {
            switch (prim_1.tag) {
                case 1: {
                    const tupledArg_8 = pt(prim_1.fields[0]);
                    return rayCrossCircle(p[0], p[1], tupledArg_8[0], tupledArg_8[1], slot(prim_1.fields[1])) | 0;
                }
                case 2: {
                    const tupledArg_9 = pt(prim_1.fields[0]);
                    const tupledArg_10 = pt(prim_1.fields[1]);
                    const tupledArg_11 = pt(prim_1.fields[2]);
                    return rayCrossArc(p[0], p[1], tupledArg_9[0], tupledArg_9[1], tupledArg_10[0], tupledArg_10[1], tupledArg_11[0], tupledArg_11[1], prim_1.fields[3]) | 0;
                }
                default: {
                    const tupledArg_6 = pt(prim_1.fields[0]);
                    const tupledArg_7 = pt(prim_1.fields[1]);
                    return rayCrossLineSegment(p[0], p[1], tupledArg_6[0], tupledArg_6[1], tupledArg_7[0], tupledArg_7[1]) | 0;
                }
            }
        }, sketch.Primitives, {
            GetZero: () => 0,
            Add: (x_1, y_1) => (x_1 + y_1),
        }) & 1) !== 0;
        if (sketch.Flip) {
            if (inside) {
                return minD;
            }
            else {
                return -minD;
            }
        }
        else if (inside) {
            return -minD;
        }
        else {
            return minD;
        }
    }
}

