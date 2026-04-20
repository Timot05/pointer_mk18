import { mapIndexed, take, ofSeq, filter, forAll, reverse, last, length as length_1, toArray, max as max_1, min, empty, collect, iterate, singleton, append, tail as tail_1, head, isEmpty, item as item_1, ofArray, map as map_1, iterateIndexed, tryPick, choose, exists } from "../ui/fable_modules/fable-library-js.4.29.0/List.js";
import { item } from "../ui/fable_modules/fable-library-js.4.29.0/Array.js";
import { max } from "../ui/fable_modules/fable-library-js.4.29.0/Double.js";
import { tryFind, ofList } from "../ui/fable_modules/fable-library-js.4.29.0/Map.js";
import { SlotRef } from "../core/Editor/SlotTable.fs.js";
import { printf, toText } from "../ui/fable_modules/fable-library-js.4.29.0/String.js";
import { equals, disposeSafe, getEnumerator, curry2, comparePrimitives } from "../ui/fable_modules/fable-library-js.4.29.0/Util.js";
import { SketchConstraint, LabelPos } from "../core/Sketch/Sketch.fs.js";
import { bind, map } from "../ui/fable_modules/fable-library-js.4.29.0/Option.js";
import { map as map_2, delay, toList } from "../ui/fable_modules/fable-library-js.4.29.0/Seq.js";
import { rangeDouble } from "../ui/fable_modules/fable-library-js.4.29.0/Range.js";
import { addToSet } from "../ui/fable_modules/fable-library-js.4.29.0/MapUtil.js";
import { Quat__Rotate_Z2E054BF3 } from "../core/Math/Quat.fs.js";
import { Vec3_op_Multiply_ZB3DA56A, Vec3_op_Addition_Z3F547E60, Vec3_op_Subtraction_Z3F547E60, Vec3_Dot_Z3F547E60, Vec3 } from "../core/Math/Vec.fs.js";

const SKETCH_LINE = new Float32Array([0.23100000619888306, 0.23100000619888306, 0.23100000619888306, 1]);

const ACCENT = new Float32Array([0.5019999742507935, 0.7450000047683716, 0.5490000247955322, 1]);

function isEntityActive(sketchId, entityKind, entityId, hovered, selected) {
    const matches = (target) => {
        let matchResult;
        switch (entityKind) {
            case "point": {
                if (target.tag === 0) {
                    matchResult = 0;
                }
                else {
                    matchResult = 4;
                }
                break;
            }
            case "line": {
                if (target.tag === 1) {
                    matchResult = 1;
                }
                else {
                    matchResult = 4;
                }
                break;
            }
            case "circle": {
                if (target.tag === 2) {
                    matchResult = 2;
                }
                else {
                    matchResult = 4;
                }
                break;
            }
            case "arc": {
                if (target.tag === 3) {
                    matchResult = 3;
                }
                else {
                    matchResult = 4;
                }
                break;
            }
            default:
                matchResult = 4;
        }
        switch (matchResult) {
            case 0:
                if (target.fields[0] === sketchId) {
                    return target.fields[1] === entityId;
                }
                else {
                    return false;
                }
            case 1:
                if (target.fields[0] === sketchId) {
                    return target.fields[1] === entityId;
                }
                else {
                    return false;
                }
            case 2:
                if (target.fields[0] === sketchId) {
                    return target.fields[1] === entityId;
                }
                else {
                    return false;
                }
            case 3:
                if (target.fields[0] === sketchId) {
                    return target.fields[1] === entityId;
                }
                else {
                    return false;
                }
            default:
                return false;
        }
    };
    if ((hovered == null) ? false : matches(hovered)) {
        return true;
    }
    else {
        return exists(matches, selected);
    }
}

const CIRCLE_SEGMENTS = 64;

function pushVertex(out, x, y, color) {
    void (out.push(x));
    void (out.push(y));
    void (out.push(item(0, color)));
    void (out.push(item(1, color)));
    void (out.push(item(2, color)));
    void (out.push(item(3, color)));
}

function pushSegment(out, a_, a__1, b_, b__1, color) {
    const a = [a_, a__1];
    const b = [b_, b__1];
    pushVertex(out, a[0], a[1], color);
    pushVertex(out, b[0], b[1], color);
}

function pushCircle(out, center_, center__1, radius, color) {
    const center = [center_, center__1];
    const cy = center[1];
    const cx = center[0];
    const n = CIRCLE_SEGMENTS | 0;
    const twoPi = 2 * 3.141592653589793;
    let prev = [cx + radius, cy];
    for (let i = 1; i <= n; i++) {
        const t = (twoPi * i) / n;
        const next = [cx + (radius * Math.cos(t)), cy + (radius * Math.sin(t))];
        pushSegment(out, prev[0], prev[1], next[0], next[1], color);
        prev = next;
    }
}

function pushArc(out, startP_, startP__1, endP_, endP__1, center_, center__1, clockwise, color) {
    const startP = [startP_, startP__1];
    const endP = [endP_, endP__1];
    const center = [center_, center__1];
    const sy = startP[1];
    const sx = startP[0];
    const cy = center[1];
    const cx = center[0];
    const radius = Math.sqrt(((sx - cx) * (sx - cx)) + ((sy - cy) * (sy - cy)));
    if (radius < 1E-09) {
        pushSegment(out, startP[0], startP[1], endP[0], endP[1], color);
    }
    else {
        const startAngle = Math.atan2(sy - cy, sx - cx);
        const endAngle = Math.atan2(endP[1] - cy, endP[0] - cx);
        const tau = 2 * 3.141592653589793;
        let sweep;
        if (clockwise) {
            let d = startAngle - endAngle;
            while (d < 0) {
                d = (d + tau);
            }
            sweep = -d;
        }
        else {
            let d_1 = endAngle - startAngle;
            while (d_1 < 0) {
                d_1 = (d_1 + tau);
            }
            sweep = d_1;
        }
        const segments = max(4, ~~(Math.abs(sweep) / (tau / CIRCLE_SEGMENTS))) | 0;
        let prev = startP;
        for (let i = 1; i <= segments; i++) {
            const ang = startAngle + ((sweep * i) / segments);
            const next = [cx + (radius * Math.cos(ang)), cy + (radius * Math.sin(ang))];
            pushSegment(out, prev[0], prev[1], next[0], next[1], color);
            prev = next;
        }
    }
}

/**
 * Slot-backed 2D point lookup. Reads (x, y) for a sketch point from the
 * sketch's `sketch.entity.{id}.{x|y}` slot values, falling back to the
 * baseline coords carried in REPoint when the slot isn't resolved.
 */
export function resolvePointMap(slotLookup, paramValues, sketchId, entities) {
    return ofList(choose((entity) => {
        if (entity.tag === 0) {
            const id = entity.fields[0];
            const readSlot = (path, fallback) => {
                const matchValue = tryFind(new SlotRef(sketchId, path), slotLookup);
                let matchResult, s_1;
                if (matchValue != null) {
                    if (matchValue < paramValues.length) {
                        matchResult = 0;
                        s_1 = matchValue;
                    }
                    else {
                        matchResult = 1;
                    }
                }
                else {
                    matchResult = 1;
                }
                switch (matchResult) {
                    case 0:
                        return item(s_1, paramValues);
                    default:
                        return fallback;
                }
            };
            return [id, [readSlot(toText(printf("sketch.entity.%s.x"))(id), entity.fields[1]), readSlot(toText(printf("sketch.entity.%s.y"))(id), entity.fields[2])]];
        }
        else {
            return undefined;
        }
    }, entities), {
        Compare: comparePrimitives,
    });
}

function resolveScalar(slotLookup, paramValues, sketchId, path, fallback) {
    const matchValue = tryFind(new SlotRef(sketchId, path), slotLookup);
    let matchResult, s_1;
    if (matchValue != null) {
        if (matchValue < paramValues.length) {
            matchResult = 0;
            s_1 = matchValue;
        }
        else {
            matchResult = 1;
        }
    }
    else {
        matchResult = 1;
    }
    switch (matchResult) {
        case 0:
            return item(s_1, paramValues);
        default:
            return fallback;
    }
}

const SKETCH_POINT = new Float32Array([0.23100000619888306, 0.23100000619888306, 0.23100000619888306, 1]);

const GRID_MINOR = new Float32Array([0.8349999785423279, 0.8159999847412109, 0.7689999938011169, 0.3499999940395355]);

const GRID_MAJOR = new Float32Array([0.8349999785423279, 0.8159999847412109, 0.7689999938011169, 0.75]);

const AXIS_X_COLOUR = new Float32Array([0.75, 0.30000001192092896, 0.30000001192092896, 0.949999988079071]);

const AXIS_Y_COLOUR = new Float32Array([0.30000001192092896, 0.6499999761581421, 0.30000001192092896, 0.949999988079071]);

const DIM_COLOUR = new Float32Array([0.4269999861717224, 0.3409999907016754, 0.19200000166893005, 1]);

const DIM_HOVER_COLOUR = new Float32Array([0.7250000238418579, 0.5099999904632568, 0.17000000178813934, 1]);

const FIXED_COLOUR = new Float32Array([0.6899999976158142, 0.3499999940395355, 0.41600000858306885, 1]);

const DIM_OFFSET = 1.8;

/**
 * Default anchor for a linear Distance-like constraint between two points.
 * Matches the TS viewer's `fallbackAnchor = mid + perp(b-a) * 1.8`.
 */
export function distanceAnchorFallback(a_, a__1, b_, b__1) {
    const a = [a_, a__1];
    const b = [b_, b__1];
    const ay = a[1];
    const ax = a[0];
    const by = b[1];
    const bx = b[0];
    const dy = by - ay;
    const dx = bx - ax;
    const len = Math.sqrt((dx * dx) + (dy * dy));
    const patternInput_1 = (len < 1E-09) ? [0, 1] : [-dy / len, dx / len];
    return [((ax + bx) * 0.5) + (patternInput_1[0] * DIM_OFFSET), ((ay + by) * 0.5) + (patternInput_1[1] * DIM_OFFSET)];
}

function pushDistanceLines(out, a_, a__1, b_, b__1, anchor_, anchor__1, colour) {
    const a = [a_, a__1];
    const b = [b_, b__1];
    const anchor = [anchor_, anchor__1];
    const ay = a[1];
    const ax = a[0];
    const by = b[1];
    const bx = b[0];
    const anY = anchor[1];
    const anX = anchor[0];
    const dy = by - ay;
    const dx = bx - ax;
    const len = Math.sqrt((dx * dx) + (dy * dy));
    if (len < 1E-09) {
    }
    else {
        const axY = dy / len;
        const axX = dx / len;
        const nY = axX;
        const nX = -axY;
        const offsetAmount = ((anX - ((ax + bx) * 0.5)) * nX) + ((anY - ((ay + by) * 0.5)) * nY);
        const offY = nY * offsetAmount;
        const offX = nX * offsetAmount;
        const aaY = ay + offY;
        const aaX = ax + offX;
        const bbY = by + offY;
        const bbX = bx + offX;
        const projParam = ((anX - aaX) * axX) + ((anY - aaY) * axY);
        const projY = aaY + (axY * projParam);
        const projX = aaX + (axX * projParam);
        const extentA = ((projX - aaX) * axX) + ((projY - aaY) * axY);
        const extentB = ((projX - bbX) * axX) + ((projY - bbY) * axY);
        pushSegment(out, ax, ay, aaX, aaY, colour);
        pushSegment(out, bx, by, bbX, bbY, colour);
        pushSegment(out, aaX, aaY, bbX, bbY, colour);
        if (extentA < 0) {
            pushSegment(out, projX, projY, aaX, aaY, colour);
        }
        else if (extentB > 0) {
            pushSegment(out, bbX, bbY, projX, projY, colour);
        }
        pushSegment(out, projX, projY, anX, anY, colour);
    }
}

function pushFixedTick(out, p_, p__1) {
    const p = [p_, p__1];
    const py = p[1];
    const px = p[0];
    pushSegment(out, px - 0.75, py - 0.75, px + 0.75, py + 0.75, FIXED_COLOUR);
    pushSegment(out, px - 0.75, py + 0.75, px + 0.75, py - 0.75, FIXED_COLOUR);
}

function pushHVDash(out, a_, a__1, b_, b__1, isHorizontal) {
    const a = [a_, a__1];
    const b = [b_, b__1];
    const my = (a[1] + b[1]) * 0.5;
    const mx = (a[0] + b[0]) * 0.5;
    if (isHorizontal) {
        pushSegment(out, mx - 0.8, my, mx + 0.8, my, DIM_COLOUR);
    }
    else {
        pushSegment(out, mx, my - 0.8, mx, my + 0.8, DIM_COLOUR);
    }
}

function isDimActive(sketchId, idx, hovered, selected) {
    const matches = (t) => {
        if (t.tag === 5) {
            if (t.fields[0] === sketchId) {
                return t.fields[1] === idx;
            }
            else {
                return false;
            }
        }
        else {
            return false;
        }
    };
    if ((hovered == null) ? false : matches(hovered)) {
        return true;
    }
    else {
        return exists(matches, selected);
    }
}

function perpFoot(p_, p__1, a_, a__1, b_, b__1) {
    const p = [p_, p__1];
    const a = [a_, a__1];
    const b = [b_, b__1];
    const aY = a[1];
    const aX = a[0];
    const dy = b[1] - aY;
    const dx = b[0] - aX;
    const len2 = (dx * dx) + (dy * dy);
    if (len2 < 1E-18) {
        return a;
    }
    else {
        const t = (((p[0] - aX) * dx) + ((p[1] - aY) * dy)) / len2;
        return [aX + (dx * t), aY + (dy * t)];
    }
}

function closestOnCircle(p_, p__1, center_, center__1, radius) {
    const p = [p_, p__1];
    const center = [center_, center__1];
    const cY = center[1];
    const cX = center[0];
    const dy = p[1] - cY;
    const dx = p[0] - cX;
    const len = Math.sqrt((dx * dx) + (dy * dy));
    if (len < 1E-09) {
        return [cX + radius, cY];
    }
    else {
        return [cX + ((dx / len) * radius), cY + ((dy / len) * radius)];
    }
}

function lineIntersection(a1_, a1__1, a2_, a2__1, b1_, b1__1, b2_, b2__1) {
    const a1 = [a1_, a1__1];
    const a2 = [a2_, a2__1];
    const b1 = [b1_, b1__1];
    const b2 = [b2_, b2__1];
    const a1Y = a1[1];
    const a1X = a1[0];
    const b1Y = b1[1];
    const b1X = b1[0];
    const dyA = a2[1] - a1Y;
    const dxA = a2[0] - a1X;
    const dyB = b2[1] - b1Y;
    const dxB = b2[0] - b1X;
    const denom = (dxA * dyB) - (dyA * dxB);
    if (Math.abs(denom) < 1E-09) {
        return undefined;
    }
    else {
        const t = (((b1X - a1X) * dyB) - ((b1Y - a1Y) * dxB)) / denom;
        return [a1X + (dxA * t), a1Y + (dyA * t)];
    }
}

function circleRadius(slotLookup, paramValues, sketchId, entities, circleId) {
    return tryPick((e) => {
        let matchResult, baseR_1, id_1;
        if (e.tag === 2) {
            if (e.fields[0] === circleId) {
                matchResult = 0;
                baseR_1 = e.fields[2];
                id_1 = e.fields[0];
            }
            else {
                matchResult = 1;
            }
        }
        else {
            matchResult = 1;
        }
        switch (matchResult) {
            case 0:
                return resolveScalar(slotLookup, paramValues, sketchId, toText(printf("sketch.entity.%s.radius"))(id_1), baseR_1);
            default:
                return undefined;
        }
    }, entities);
}

/**
 * Curried radius lookup bound to a sketch, for reuse across multiple
 * constraint-rendering calls.
 */
export function circleRadiusLookup(slotLookup, paramValues, sketchId, entities) {
    return (circleId) => circleRadius(slotLookup, paramValues, sketchId, entities, circleId);
}

function normalizedSweep(startAngle, endAngle, ccw) {
    const tau = 2 * 3.141592653589793;
    let sweep = endAngle - startAngle;
    if (ccw) {
        while (sweep < 0) {
            sweep = (sweep + tau);
        }
    }
    else {
        while (sweep > 0) {
            sweep = (sweep - tau);
        }
    }
    return sweep;
}

function pushAngleArc(out, apex_, apex__1, radius, startAngle, endAngle, ccw, colour) {
    const apex = [apex_, apex__1];
    const aY = apex[1];
    const aX = apex[0];
    const sweep = normalizedSweep(startAngle, endAngle, ccw);
    const segments = max(12, ~~Math.ceil((Math.abs(sweep) * 12) / 3.141592653589793)) | 0;
    let prev = [aX + (radius * Math.cos(startAngle)), aY + (radius * Math.sin(startAngle))];
    for (let i = 1; i <= segments; i++) {
        const ang = startAngle + (sweep * (i / segments));
        const next = [aX + (radius * Math.cos(ang)), aY + (radius * Math.sin(ang))];
        pushSegment(out, prev[0], prev[1], next[0], next[1], colour);
        prev = next;
    }
}

function lineIntersectionDir(originA_, originA__1, dirA_, dirA__1, originB_, originB__1, dirB_, dirB__1) {
    const originA = [originA_, originA__1];
    const dirA = [dirA_, dirA__1];
    const originB = [originB_, originB__1];
    const dirB = [dirB_, dirB__1];
    const oAy = originA[1];
    const oAx = originA[0];
    const dAy = dirA[1];
    const dAx = dirA[0];
    const dBy = dirB[1];
    const dBx = dirB[0];
    const denom = (dAx * dBy) - (dAy * dBx);
    if (Math.abs(denom) < 1E-09) {
        return undefined;
    }
    else {
        const t = (((originB[0] - oAx) * dBy) - ((originB[1] - oAy) * dBx)) / denom;
        return [oAx + (dAx * t), oAy + (dAy * t)];
    }
}

function resolveAngleGeometry(aStart_, aStart__1, aEnd_, aEnd__1, bStart_, bStart__1, bEnd_, bEnd__1, aReverse, bReverse, ccw) {
    const aStart = [aStart_, aStart__1];
    const aEnd = [aEnd_, aEnd__1];
    const bStart = [bStart_, bStart__1];
    const bEnd = [bEnd_, bEnd__1];
    const sub = (tupledArg, tupledArg_1) => [tupledArg[0] - tupledArg_1[0], tupledArg[1] - tupledArg_1[1]];
    const len = (tupledArg_2) => {
        const x = tupledArg_2[0];
        const y = tupledArg_2[1];
        return Math.sqrt((x * x) + (y * y));
    };
    const normalize = (v) => {
        const v_1 = v;
        const l = len(v_1);
        if (l < 1E-06) {
            return [0, 0];
        }
        else {
            return [v_1[0] / l, v_1[1] / l];
        }
    };
    const aVertex = aReverse ? aEnd : aStart;
    const bVertex = bReverse ? bEnd : bStart;
    const rayA = normalize(aReverse ? sub(aStart, aEnd) : sub(aEnd, aStart));
    const rayB = normalize(bReverse ? sub(bStart, bEnd) : sub(bEnd, bStart));
    if ((len(rayA) < 1E-06) ? true : (len(rayB) < 1E-06)) {
        return [aVertex, rayA, rayB, undefined];
    }
    else {
        let vertex;
        if (len(sub(aVertex, bVertex)) < 0.0001) {
            vertex = aVertex;
        }
        else {
            const matchValue = lineIntersectionDir(aVertex[0], aVertex[1], rayA[0], rayA[1], bVertex[0], bVertex[1], rayB[0], rayB[1]);
            vertex = ((matchValue == null) ? aVertex : matchValue);
        }
        const angA = Math.atan2(rayA[1], rayA[0]);
        const midAngle = angA + (normalizedSweep(angA, Math.atan2(rayB[1], rayB[0]), ccw) * 0.5);
        return [vertex, rayA, rayB, [Math.cos(midAngle), Math.sin(midAngle)]];
    }
}

/**
 * Default label anchor for a constraint when the user hasn't dragged it.
 * Kept here (rather than in LabelBuilder) so the constraint-line renderer
 * and the label renderer stay in sync — both use this for lp=None.
 */
export function dimensionFallbackAnchor(points, radiusLookup, c) {
    const pt = (id) => tryFind(id, points);
    const mid = (a, b) => [(a[0] + b[0]) * 0.5, (a[1] + b[1]) * 0.5];
    const linear = (a_1, b_1) => {
        const patternInput = distanceAnchorFallback(a_1[0], a_1[1], b_1[0], b_1[1]);
        return new LabelPos(patternInput[0], patternInput[1]);
    };
    switch (c.tag) {
        case 6: {
            const matchValue = pt(c.fields[0]);
            const matchValue_1 = pt(c.fields[1]);
            let matchResult, pa, pb;
            if (matchValue != null) {
                if (matchValue_1 != null) {
                    matchResult = 0;
                    pa = matchValue;
                    pb = matchValue_1;
                }
                else {
                    matchResult = 1;
                }
            }
            else {
                matchResult = 1;
            }
            switch (matchResult) {
                case 0:
                    return linear(pa, pb);
                default:
                    return undefined;
            }
        }
        case 18: {
            const matchValue_3 = pt(c.fields[0]);
            const matchValue_4 = pt(c.fields[1]);
            const matchValue_5 = pt(c.fields[2]);
            const matchValue_6 = pt(c.fields[3]);
            let matchResult_1, a1, a2, b1, b2;
            if (matchValue_3 != null) {
                if (matchValue_4 != null) {
                    if (matchValue_5 != null) {
                        if (matchValue_6 != null) {
                            matchResult_1 = 0;
                            a1 = matchValue_3;
                            a2 = matchValue_4;
                            b1 = matchValue_5;
                            b2 = matchValue_6;
                        }
                        else {
                            matchResult_1 = 1;
                        }
                    }
                    else {
                        matchResult_1 = 1;
                    }
                }
                else {
                    matchResult_1 = 1;
                }
            }
            else {
                matchResult_1 = 1;
            }
            switch (matchResult_1) {
                case 0:
                    return linear(mid(a1, a2), mid(b1, b2));
                default:
                    return undefined;
            }
        }
        case 20: {
            const matchValue_8 = pt(c.fields[0]);
            const matchValue_9 = pt(c.fields[2]);
            const matchValue_10 = pt(c.fields[3]);
            let matchResult_2, a_3, b_3, p;
            if (matchValue_8 != null) {
                if (matchValue_9 != null) {
                    if (matchValue_10 != null) {
                        matchResult_2 = 0;
                        a_3 = matchValue_9;
                        b_3 = matchValue_10;
                        p = matchValue_8;
                    }
                    else {
                        matchResult_2 = 1;
                    }
                }
                else {
                    matchResult_2 = 1;
                }
            }
            else {
                matchResult_2 = 1;
            }
            switch (matchResult_2) {
                case 0:
                    return linear(p, perpFoot(p[0], p[1], a_3[0], a_3[1], b_3[0], b_3[1]));
                default:
                    return undefined;
            }
        }
        case 21: {
            const matchValue_12 = pt(c.fields[0]);
            const matchValue_13 = pt(c.fields[2]);
            const matchValue_14 = radiusLookup(c.fields[1]);
            let matchResult_3, c_1, p_1, r;
            if (matchValue_12 != null) {
                if (matchValue_13 != null) {
                    if (matchValue_14 != null) {
                        matchResult_3 = 0;
                        c_1 = matchValue_13;
                        p_1 = matchValue_12;
                        r = matchValue_14;
                    }
                    else {
                        matchResult_3 = 1;
                    }
                }
                else {
                    matchResult_3 = 1;
                }
            }
            else {
                matchResult_3 = 1;
            }
            switch (matchResult_3) {
                case 0:
                    return linear(p_1, closestOnCircle(p_1[0], p_1[1], c_1[0], c_1[1], r));
                default:
                    return undefined;
            }
        }
        case 22: {
            const matchValue_16 = pt(c.fields[1]);
            const matchValue_17 = pt(c.fields[2]);
            const matchValue_18 = pt(c.fields[4]);
            const matchValue_19 = radiusLookup(c.fields[3]);
            let matchResult_4, a_4, b_4, c_2, r_1;
            if (matchValue_16 != null) {
                if (matchValue_17 != null) {
                    if (matchValue_18 != null) {
                        if (matchValue_19 != null) {
                            matchResult_4 = 0;
                            a_4 = matchValue_16;
                            b_4 = matchValue_17;
                            c_2 = matchValue_18;
                            r_1 = matchValue_19;
                        }
                        else {
                            matchResult_4 = 1;
                        }
                    }
                    else {
                        matchResult_4 = 1;
                    }
                }
                else {
                    matchResult_4 = 1;
                }
            }
            else {
                matchResult_4 = 1;
            }
            switch (matchResult_4) {
                case 0: {
                    const foot = perpFoot(c_2[0], c_2[1], a_4[0], a_4[1], b_4[0], b_4[1]);
                    return linear(foot, closestOnCircle(foot[0], foot[1], c_2[0], c_2[1], r_1));
                }
                default:
                    return undefined;
            }
        }
        case 23: {
            const matchValue_21 = pt(c.fields[1]);
            const matchValue_22 = pt(c.fields[3]);
            const matchValue_23 = radiusLookup(c.fields[0]);
            const matchValue_24 = radiusLookup(c.fields[2]);
            let matchResult_5, cA, cB, rA, rB;
            if (matchValue_21 != null) {
                if (matchValue_22 != null) {
                    if (matchValue_23 != null) {
                        if (matchValue_24 != null) {
                            matchResult_5 = 0;
                            cA = matchValue_21;
                            cB = matchValue_22;
                            rA = matchValue_23;
                            rB = matchValue_24;
                        }
                        else {
                            matchResult_5 = 1;
                        }
                    }
                    else {
                        matchResult_5 = 1;
                    }
                }
                else {
                    matchResult_5 = 1;
                }
            }
            else {
                matchResult_5 = 1;
            }
            switch (matchResult_5) {
                case 0:
                    return linear(closestOnCircle(cB[0], cB[1], cA[0], cA[1], rA), closestOnCircle(cA[0], cA[1], cB[0], cB[1], rB));
                default:
                    return undefined;
            }
        }
        case 17: {
            const matchValue_26 = pt(c.fields[1]);
            const matchValue_27 = radiusLookup(c.fields[0]);
            let matchResult_6, cx, cy, r_2;
            if (matchValue_26 != null) {
                if (matchValue_27 != null) {
                    matchResult_6 = 0;
                    cx = matchValue_26[0];
                    cy = matchValue_26[1];
                    r_2 = matchValue_27;
                }
                else {
                    matchResult_6 = 1;
                }
            }
            else {
                matchResult_6 = 1;
            }
            switch (matchResult_6) {
                case 0:
                    return new LabelPos((cx + r_2) + DIM_OFFSET, cy);
                default:
                    return undefined;
            }
        }
        case 24: {
            const matchValue_29 = pt(c.fields[0]);
            const matchValue_30 = pt(c.fields[1]);
            const matchValue_31 = pt(c.fields[2]);
            const matchValue_32 = pt(c.fields[3]);
            let matchResult_7, pa1, pa2, pb1, pb2;
            if (matchValue_29 != null) {
                if (matchValue_30 != null) {
                    if (matchValue_31 != null) {
                        if (matchValue_32 != null) {
                            matchResult_7 = 0;
                            pa1 = matchValue_29;
                            pa2 = matchValue_30;
                            pb1 = matchValue_31;
                            pb2 = matchValue_32;
                        }
                        else {
                            matchResult_7 = 1;
                        }
                    }
                    else {
                        matchResult_7 = 1;
                    }
                }
                else {
                    matchResult_7 = 1;
                }
            }
            else {
                matchResult_7 = 1;
            }
            switch (matchResult_7) {
                case 0: {
                    const patternInput_1 = resolveAngleGeometry(pa1[0], pa1[1], pa2[0], pa2[1], pb1[0], pb1[1], pb2[0], pb2[1], c.fields[7], c.fields[8], c.fields[9]);
                    const midOpt = patternInput_1[3];
                    if (midOpt == null) {
                        return undefined;
                    }
                    else {
                        return new LabelPos(patternInput_1[0][0] + (midOpt[0] * 4.4), patternInput_1[0][1] + (midOpt[1] * 4.4));
                    }
                }
                default:
                    return undefined;
            }
        }
        case 7:
            return map((tupledArg) => (new LabelPos(tupledArg[0] + DIM_OFFSET, tupledArg[1])), pt(c.fields[0]));
        case 19: {
            const matchValue_34 = pt(c.fields[1]);
            const matchValue_35 = pt(c.fields[2]);
            let matchResult_8, a_5, b_5;
            if (matchValue_34 != null) {
                if (matchValue_35 != null) {
                    matchResult_8 = 0;
                    a_5 = matchValue_34;
                    b_5 = matchValue_35;
                }
                else {
                    matchResult_8 = 1;
                }
            }
            else {
                matchResult_8 = 1;
            }
            switch (matchResult_8) {
                case 0:
                    return linear(a_5, b_5);
                default:
                    return undefined;
            }
        }
        default:
            return undefined;
    }
}

function pushConstraintGeometry(out, points, radiusOf, showDimensions, colour, c) {
    const pt = (id) => tryFind(id, points);
    const midpoint = (a, b) => [(a[0] + b[0]) * 0.5, (a[1] + b[1]) * 0.5];
    const linear = (a_1, b_1, lp_1) => {
        if (showDimensions) {
            let anchor;
            const lp = lp_1;
            const defaultAnchor = distanceAnchorFallback(a_1[0], a_1[1], b_1[0], b_1[1]);
            if (lp == null) {
                anchor = defaultAnchor;
            }
            else {
                const p = lp;
                anchor = [p.X, p.Y];
            }
            pushDistanceLines(out, a_1[0], a_1[1], b_1[0], b_1[1], anchor[0], anchor[1], colour);
        }
    };
    let matchResult, p_1, a_2, b_2, a_3, b_3, lp_2, aE, aS, bE, bS, lp_3, aE_1, aS_1, lp_4, point_1, centerId, circleId, lp_5, point_2, aE_2, aS_2, centerId_1, circleId_1, lp_6, centerA, centerB, circleA, circleB, lp_7, centerId_2, circleId_2, lp_8, aEnd, aReverse, aStart, bEnd, bReverse, bStart, ccw, lp_9;
    switch (c.tag) {
        case 0: {
            matchResult = 0;
            p_1 = c.fields[0];
            break;
        }
        case 4: {
            matchResult = 1;
            a_2 = c.fields[0];
            b_2 = c.fields[1];
            break;
        }
        case 5: {
            matchResult = 1;
            a_2 = c.fields[0];
            b_2 = c.fields[1];
            break;
        }
        case 6: {
            if (!showDimensions) {
                matchResult = 2;
            }
            else {
                matchResult = 3;
                a_3 = c.fields[0];
                b_3 = c.fields[1];
                lp_2 = c.fields[3];
            }
            break;
        }
        case 18: {
            if (!showDimensions) {
                matchResult = 2;
            }
            else {
                matchResult = 4;
                aE = c.fields[1];
                aS = c.fields[0];
                bE = c.fields[3];
                bS = c.fields[2];
                lp_3 = c.fields[7];
            }
            break;
        }
        case 20: {
            if (!showDimensions) {
                matchResult = 2;
            }
            else {
                matchResult = 5;
                aE_1 = c.fields[3];
                aS_1 = c.fields[2];
                lp_4 = c.fields[5];
                point_1 = c.fields[0];
            }
            break;
        }
        case 21: {
            if (!showDimensions) {
                matchResult = 2;
            }
            else {
                matchResult = 6;
                centerId = c.fields[2];
                circleId = c.fields[1];
                lp_5 = c.fields[4];
                point_2 = c.fields[0];
            }
            break;
        }
        case 22: {
            if (!showDimensions) {
                matchResult = 2;
            }
            else {
                matchResult = 7;
                aE_2 = c.fields[2];
                aS_2 = c.fields[1];
                centerId_1 = c.fields[4];
                circleId_1 = c.fields[3];
                lp_6 = c.fields[6];
            }
            break;
        }
        case 23: {
            if (!showDimensions) {
                matchResult = 2;
            }
            else {
                matchResult = 8;
                centerA = c.fields[1];
                centerB = c.fields[3];
                circleA = c.fields[0];
                circleB = c.fields[2];
                lp_7 = c.fields[6];
            }
            break;
        }
        case 17: {
            if (!showDimensions) {
                matchResult = 2;
            }
            else {
                matchResult = 9;
                centerId_2 = c.fields[1];
                circleId_2 = c.fields[0];
                lp_8 = c.fields[3];
            }
            break;
        }
        case 24: {
            if (!showDimensions) {
                matchResult = 2;
            }
            else {
                matchResult = 10;
                aEnd = c.fields[1];
                aReverse = c.fields[7];
                aStart = c.fields[0];
                bEnd = c.fields[3];
                bReverse = c.fields[8];
                bStart = c.fields[2];
                ccw = c.fields[9];
                lp_9 = c.fields[10];
            }
            break;
        }
        default:
            if (!showDimensions) {
                matchResult = 2;
            }
            else {
                matchResult = 11;
            }
    }
    switch (matchResult) {
        case 0: {
            const matchValue = pt(p_1);
            if (matchValue == null) {
            }
            else {
                const point = matchValue;
                pushFixedTick(out, point[0], point[1]);
            }
            break;
        }
        case 1: {
            const isH = c.tag === 4;
            const matchValue_1 = pt(a_2);
            const matchValue_2 = pt(b_2);
            let matchResult_1, pa, pb;
            if (matchValue_1 != null) {
                if (matchValue_2 != null) {
                    matchResult_1 = 0;
                    pa = matchValue_1;
                    pb = matchValue_2;
                }
                else {
                    matchResult_1 = 1;
                }
            }
            else {
                matchResult_1 = 1;
            }
            switch (matchResult_1) {
                case 0: {
                    pushHVDash(out, pa[0], pa[1], pb[0], pb[1], isH);
                    break;
                }
                case 1: {
                    break;
                }
            }
            break;
        }
        case 2: {
            break;
        }
        case 3: {
            const matchValue_4 = pt(a_3);
            const matchValue_5 = pt(b_3);
            let matchResult_2, pa_1, pb_1;
            if (matchValue_4 != null) {
                if (matchValue_5 != null) {
                    matchResult_2 = 0;
                    pa_1 = matchValue_4;
                    pb_1 = matchValue_5;
                }
                else {
                    matchResult_2 = 1;
                }
            }
            else {
                matchResult_2 = 1;
            }
            switch (matchResult_2) {
                case 0: {
                    linear(pa_1, pb_1, lp_2);
                    break;
                }
                case 1: {
                    break;
                }
            }
            break;
        }
        case 4: {
            const matchValue_7 = pt(aS);
            const matchValue_8 = pt(aE);
            const matchValue_9 = pt(bS);
            const matchValue_10 = pt(bE);
            let matchResult_3, pa1, pa2, pb1, pb2;
            if (matchValue_7 != null) {
                if (matchValue_8 != null) {
                    if (matchValue_9 != null) {
                        if (matchValue_10 != null) {
                            matchResult_3 = 0;
                            pa1 = matchValue_7;
                            pa2 = matchValue_8;
                            pb1 = matchValue_9;
                            pb2 = matchValue_10;
                        }
                        else {
                            matchResult_3 = 1;
                        }
                    }
                    else {
                        matchResult_3 = 1;
                    }
                }
                else {
                    matchResult_3 = 1;
                }
            }
            else {
                matchResult_3 = 1;
            }
            switch (matchResult_3) {
                case 0: {
                    linear(midpoint(pa1, pa2), midpoint(pb1, pb2), lp_3);
                    break;
                }
                case 1: {
                    break;
                }
            }
            break;
        }
        case 5: {
            const matchValue_12 = pt(point_1);
            const matchValue_13 = pt(aS_1);
            const matchValue_14 = pt(aE_1);
            let matchResult_4, a_4, b_4, p_2;
            if (matchValue_12 != null) {
                if (matchValue_13 != null) {
                    if (matchValue_14 != null) {
                        matchResult_4 = 0;
                        a_4 = matchValue_13;
                        b_4 = matchValue_14;
                        p_2 = matchValue_12;
                    }
                    else {
                        matchResult_4 = 1;
                    }
                }
                else {
                    matchResult_4 = 1;
                }
            }
            else {
                matchResult_4 = 1;
            }
            switch (matchResult_4) {
                case 0: {
                    linear(p_2, perpFoot(p_2[0], p_2[1], a_4[0], a_4[1], b_4[0], b_4[1]), lp_4);
                    break;
                }
                case 1: {
                    break;
                }
            }
            break;
        }
        case 6: {
            const matchValue_16 = pt(point_2);
            const matchValue_17 = pt(centerId);
            const matchValue_18 = radiusOf(circleId);
            let matchResult_5, c_1, p_3, r;
            if (matchValue_16 != null) {
                if (matchValue_17 != null) {
                    if (matchValue_18 != null) {
                        matchResult_5 = 0;
                        c_1 = matchValue_17;
                        p_3 = matchValue_16;
                        r = matchValue_18;
                    }
                    else {
                        matchResult_5 = 1;
                    }
                }
                else {
                    matchResult_5 = 1;
                }
            }
            else {
                matchResult_5 = 1;
            }
            switch (matchResult_5) {
                case 0: {
                    linear(p_3, closestOnCircle(p_3[0], p_3[1], c_1[0], c_1[1], r), lp_5);
                    break;
                }
                case 1: {
                    break;
                }
            }
            break;
        }
        case 7: {
            const matchValue_20 = pt(aS_2);
            const matchValue_21 = pt(aE_2);
            const matchValue_22 = pt(centerId_1);
            const matchValue_23 = radiusOf(circleId_1);
            let matchResult_6, a_5, b_5, c_2, r_1;
            if (matchValue_20 != null) {
                if (matchValue_21 != null) {
                    if (matchValue_22 != null) {
                        if (matchValue_23 != null) {
                            matchResult_6 = 0;
                            a_5 = matchValue_20;
                            b_5 = matchValue_21;
                            c_2 = matchValue_22;
                            r_1 = matchValue_23;
                        }
                        else {
                            matchResult_6 = 1;
                        }
                    }
                    else {
                        matchResult_6 = 1;
                    }
                }
                else {
                    matchResult_6 = 1;
                }
            }
            else {
                matchResult_6 = 1;
            }
            switch (matchResult_6) {
                case 0: {
                    const foot = perpFoot(c_2[0], c_2[1], a_5[0], a_5[1], b_5[0], b_5[1]);
                    linear(foot, closestOnCircle(foot[0], foot[1], c_2[0], c_2[1], r_1), lp_6);
                    break;
                }
                case 1: {
                    break;
                }
            }
            break;
        }
        case 8: {
            const matchValue_25 = pt(centerA);
            const matchValue_26 = pt(centerB);
            const matchValue_27 = radiusOf(circleA);
            const matchValue_28 = radiusOf(circleB);
            let matchResult_7, cA, cB, rA, rB;
            if (matchValue_25 != null) {
                if (matchValue_26 != null) {
                    if (matchValue_27 != null) {
                        if (matchValue_28 != null) {
                            matchResult_7 = 0;
                            cA = matchValue_25;
                            cB = matchValue_26;
                            rA = matchValue_27;
                            rB = matchValue_28;
                        }
                        else {
                            matchResult_7 = 1;
                        }
                    }
                    else {
                        matchResult_7 = 1;
                    }
                }
                else {
                    matchResult_7 = 1;
                }
            }
            else {
                matchResult_7 = 1;
            }
            switch (matchResult_7) {
                case 0: {
                    linear(closestOnCircle(cB[0], cB[1], cA[0], cA[1], rA), closestOnCircle(cA[0], cA[1], cB[0], cB[1], rB), lp_7);
                    break;
                }
                case 1: {
                    break;
                }
            }
            break;
        }
        case 9: {
            const matchValue_30 = pt(centerId_2);
            const matchValue_31 = radiusOf(circleId_2);
            let matchResult_8, cx, cy, r_2;
            if (matchValue_30 != null) {
                if (matchValue_31 != null) {
                    matchResult_8 = 0;
                    cx = matchValue_30[0];
                    cy = matchValue_30[1];
                    r_2 = matchValue_31;
                }
                else {
                    matchResult_8 = 1;
                }
            }
            else {
                matchResult_8 = 1;
            }
            switch (matchResult_8) {
                case 0: {
                    let patternInput_1;
                    if (lp_8 == null) {
                        patternInput_1 = [1, 0];
                    }
                    else {
                        const p_4 = lp_8;
                        const dy = p_4.Y - cy;
                        const dx = p_4.X - cx;
                        const len = Math.sqrt((dx * dx) + (dy * dy));
                        patternInput_1 = ((len < 1E-09) ? [1, 0] : [dx / len, dy / len]);
                    }
                    const dirY = patternInput_1[1];
                    const dirX = patternInput_1[0];
                    const a_6 = [cx - (dirX * r_2), cy - (dirY * r_2)];
                    const b_6 = [cx + (dirX * r_2), cy + (dirY * r_2)];
                    let anchor_1;
                    if (lp_8 == null) {
                        anchor_1 = [cx + (dirX * (r_2 + DIM_OFFSET)), cy + (dirY * (r_2 + DIM_OFFSET))];
                    }
                    else {
                        const p_5 = lp_8;
                        anchor_1 = [p_5.X, p_5.Y];
                    }
                    pushSegment(out, a_6[0], a_6[1], b_6[0], b_6[1], colour);
                    pushSegment(out, b_6[0], b_6[1], anchor_1[0], anchor_1[1], colour);
                    break;
                }
                case 1: {
                    break;
                }
            }
            break;
        }
        case 10: {
            const matchValue_35 = pt(aStart);
            const matchValue_36 = pt(aEnd);
            const matchValue_37 = pt(bStart);
            const matchValue_38 = pt(bEnd);
            let matchResult_9, pa1_1, pa2_1, pb1_1, pb2_1;
            if (matchValue_35 != null) {
                if (matchValue_36 != null) {
                    if (matchValue_37 != null) {
                        if (matchValue_38 != null) {
                            matchResult_9 = 0;
                            pa1_1 = matchValue_35;
                            pa2_1 = matchValue_36;
                            pb1_1 = matchValue_37;
                            pb2_1 = matchValue_38;
                        }
                        else {
                            matchResult_9 = 1;
                        }
                    }
                    else {
                        matchResult_9 = 1;
                    }
                }
                else {
                    matchResult_9 = 1;
                }
            }
            else {
                matchResult_9 = 1;
            }
            switch (matchResult_9) {
                case 0: {
                    const patternInput_2 = resolveAngleGeometry(pa1_1[0], pa1_1[1], pa2_1[0], pa2_1[1], pb1_1[0], pb1_1[1], pb2_1[0], pb2_1[1], aReverse, bReverse, ccw);
                    const vertex = patternInput_2[0];
                    const rayB = patternInput_2[2];
                    const rayA = patternInput_2[1];
                    const midOpt = patternInput_2[3];
                    if (midOpt == null) {
                    }
                    else {
                        const midDir = midOpt;
                        const vY = vertex[1];
                        const vX = vertex[0];
                        const fallbackAnchor = [vX + (midDir[0] * 4.4), vY + (midDir[1] * 4.4)];
                        let patternInput_3;
                        if (lp_9 == null) {
                            patternInput_3 = fallbackAnchor;
                        }
                        else {
                            const p_6 = lp_9;
                            patternInput_3 = [p_6.X, p_6.Y];
                        }
                        const anchorVy = patternInput_3[1] - vY;
                        const anchorVx = patternInput_3[0] - vX;
                        const anchorRadius = Math.sqrt((anchorVx * anchorVx) + (anchorVy * anchorVy));
                        if (anchorRadius > 1E-06) {
                            const anchorAngle = Math.atan2(anchorVy, anchorVx);
                            const startAngle = Math.atan2(rayA[1], rayA[0]);
                            const endAngle = Math.atan2(rayB[1], rayB[0]);
                            const arcSweep = Math.abs(normalizedSweep(startAngle, endAngle, ccw));
                            const anchorInsideSector = Math.abs(normalizedSweep(startAngle, anchorAngle, ccw)) <= (arcSweep + 1E-06);
                            const r_3 = anchorInsideSector ? (anchorRadius - 0.8) : anchorRadius;
                            if (r_3 > 1E-06) {
                                const extendAfterEnd = Math.abs(normalizedSweep(endAngle, anchorAngle, ccw));
                                const extendStart = !anchorInsideSector && (Math.abs(normalizedSweep(anchorAngle, startAngle, ccw)) < extendAfterEnd);
                                const arcStartAngle = extendStart ? anchorAngle : startAngle;
                                const arcEndAngle = (!anchorInsideSector && !extendStart) ? anchorAngle : endAngle;
                                pushSegment(out, vertex[0], vertex[1], vX + (rayA[0] * r_3), vY + (rayA[1] * r_3), colour);
                                pushSegment(out, vertex[0], vertex[1], vX + (rayB[0] * r_3), vY + (rayB[1] * r_3), colour);
                                pushAngleArc(out, vertex[0], vertex[1], r_3, arcStartAngle, arcEndAngle, ccw, colour);
                            }
                        }
                    }
                    break;
                }
                case 1: {
                    break;
                }
            }
            break;
        }
        case 11: {
            break;
        }
    }
}

/**
 * Rewrite a dimensional constraint's labelPosition. Used when previewing
 * pending placements so the label follows the cursor.
 */
export function withLabelPosition(lp, c) {
    switch (c.tag) {
        case 6:
            return new SketchConstraint(6, [c.fields[0], c.fields[1], c.fields[2], lp]);
        case 7:
            return new SketchConstraint(7, [c.fields[0], c.fields[1], c.fields[2], c.fields[3], lp]);
        case 18:
            return new SketchConstraint(18, [c.fields[0], c.fields[1], c.fields[2], c.fields[3], c.fields[4], c.fields[5], c.fields[6], lp]);
        case 19:
            return new SketchConstraint(19, [c.fields[0], c.fields[1], c.fields[2], c.fields[3], c.fields[4], c.fields[5], lp]);
        case 20:
            return new SketchConstraint(20, [c.fields[0], c.fields[1], c.fields[2], c.fields[3], c.fields[4], lp]);
        case 21:
            return new SketchConstraint(21, [c.fields[0], c.fields[1], c.fields[2], c.fields[3], lp]);
        case 22:
            return new SketchConstraint(22, [c.fields[0], c.fields[1], c.fields[2], c.fields[3], c.fields[4], c.fields[5], lp]);
        case 23:
            return new SketchConstraint(23, [c.fields[0], c.fields[1], c.fields[2], c.fields[3], c.fields[4], c.fields[5], lp]);
        case 17:
            return new SketchConstraint(17, [c.fields[0], c.fields[1], c.fields[2], lp]);
        case 24:
            return new SketchConstraint(24, [c.fields[0], c.fields[1], c.fields[2], c.fields[3], c.fields[4], c.fields[5], c.fields[6], c.fields[7], c.fields[8], c.fields[9], lp]);
        default:
            return c;
    }
}

const PLACEMENT_PREVIEW_COLOUR = new Float32Array([0.5019999742507935, 0.7450000047683716, 0.5490000247955322, 0.8500000238418579]);

/**
 * Line-buffer for an in-progress constraint placement — same geometry as
 * the eventual constraint, drawn at the cursor position.
 */
export function buildPendingConstraintLineBuffer(sketchId, entities, slotLookup, paramValues, pending, cursor) {
    const out = [];
    pushConstraintGeometry(out, resolvePointMap(slotLookup, paramValues, sketchId, entities), (circleId) => circleRadius(slotLookup, paramValues, sketchId, entities, circleId), true, PLACEMENT_PREVIEW_COLOUR, withLabelPosition(cursor, pending));
    return out.slice();
}

/**
 * Build the vertex buffer of constraint-visualization line segments
 * (dimension lines, extension lines, H/V ticks, Fixed crosshairs).
 * Vertex format identical to the sketch-line buffer. Active dimensions
 * render in the hover/selected colour.
 */
export function buildSketchConstraintLinesBuffer(sketchId, sketch, slotLookup, paramValues, showDimensions, hovered, selected) {
    const points = resolvePointMap(slotLookup, paramValues, sketchId, sketch.Entities);
    const out = [];
    iterateIndexed((i, c) => {
        pushConstraintGeometry(out, points, (circleId) => circleRadius(slotLookup, paramValues, sketchId, sketch.Entities, circleId), showDimensions, isDimActive(sketchId, i, hovered, selected) ? DIM_HOVER_COLOUR : DIM_COLOUR, c);
    }, sketch.Constraints);
    return out.slice();
}

const PREVIEW_LINE = new Float32Array([0.5019999742507935, 0.7450000047683716, 0.5490000247955322, 0.7200000286102295]);

const PREVIEW_POINT = new Float32Array([0.5019999742507935, 0.7450000047683716, 0.5490000247955322, 0.9200000166893005]);

/**
 * Line-list preview buffer for the currently-active tool.
 */
export function buildToolPreviewLineBuffer(tool, toolPoints, cursor) {
    const out = [];
    const points = map_1((p) => [p.X, p.Y], toolPoints);
    let matchResult, c, p0, c_1, p0_1, c_2, p0_2, c_3, center, startPt, c_4, p0_3;
    switch (tool) {
        case "line": {
            if (!isEmpty(points)) {
                if (cursor != null) {
                    matchResult = 0;
                    c = cursor;
                    p0 = head(points);
                }
                else {
                    matchResult = 5;
                }
            }
            else {
                matchResult = 5;
            }
            break;
        }
        case "rectangle": {
            if (!isEmpty(points)) {
                if (cursor != null) {
                    matchResult = 1;
                    c_1 = cursor;
                    p0_1 = head(points);
                }
                else {
                    matchResult = 5;
                }
            }
            else {
                matchResult = 5;
            }
            break;
        }
        case "circle": {
            if (!isEmpty(points)) {
                if (cursor != null) {
                    matchResult = 2;
                    c_2 = cursor;
                    p0_2 = head(points);
                }
                else {
                    matchResult = 5;
                }
            }
            else {
                matchResult = 5;
            }
            break;
        }
        case "arc": {
            if (!isEmpty(points)) {
                if (isEmpty(tail_1(points))) {
                    if (cursor != null) {
                        matchResult = 4;
                        c_4 = cursor;
                        p0_3 = head(points);
                    }
                    else {
                        matchResult = 5;
                    }
                }
                else if (isEmpty(tail_1(tail_1(points)))) {
                    if (cursor != null) {
                        matchResult = 3;
                        c_3 = cursor;
                        center = head(points);
                        startPt = head(tail_1(points));
                    }
                    else {
                        matchResult = 5;
                    }
                }
                else {
                    matchResult = 5;
                }
            }
            else {
                matchResult = 5;
            }
            break;
        }
        default:
            matchResult = 5;
    }
    switch (matchResult) {
        case 0: {
            pushSegment(out, p0[0], p0[1], c[0], c[1], PREVIEW_LINE);
            break;
        }
        case 1: {
            const y1 = c_1[1];
            const y0 = p0_1[1];
            const x1 = c_1[0];
            const x0 = p0_1[0];
            const corners = ofArray([[x0, y0], [x1, y0], [x1, y1], [x0, y1]]);
            iterateIndexed((i, a) => {
                const b = item_1((i + 1) % 4, corners);
                pushSegment(out, a[0], a[1], b[0], b[1], PREVIEW_LINE);
            }, corners);
            break;
        }
        case 2: {
            const dy = c_2[1] - p0_2[1];
            const dx = c_2[0] - p0_2[0];
            pushCircle(out, p0_2[0], p0_2[1], max(1E-06, Math.sqrt((dx * dx) + (dy * dy))), PREVIEW_LINE);
            break;
        }
        case 3: {
            const sy = startPt[1];
            const sx = startPt[0];
            const my_1 = c_3[1];
            const mx_1 = c_3[0];
            const cy_1 = center[1];
            const cx_1 = center[0];
            const dy_1 = my_1 - cy_1;
            const dx_1 = mx_1 - cx_1;
            const len = Math.sqrt((dx_1 * dx_1) + (dy_1 * dy_1));
            if (len > 1E-09) {
                const r0 = Math.sqrt(((sx - cx_1) * (sx - cx_1)) + ((sy - cy_1) * (sy - cy_1)));
                pushArc(out, startPt[0], startPt[1], cx_1 + ((dx_1 / len) * r0), cy_1 + ((dy_1 / len) * r0), center[0], center[1], (((sx - cx_1) * (my_1 - cy_1)) - ((sy - cy_1) * (mx_1 - cx_1))) < 0, PREVIEW_LINE);
            }
            break;
        }
        case 4: {
            pushSegment(out, p0_3[0], p0_3[1], c_4[0], c_4[1], PREVIEW_LINE);
            break;
        }
    }
    return out.slice();
}

/**
 * Point instance buffer (7 floats per instance) for the currently-active tool.
 */
export function buildToolPreviewPointBuffer(tool, toolPoints, cursor) {
    const pushInstance = (out, _arg) => {
        void (out.push(_arg[0]));
        void (out.push(_arg[1]));
        void (out.push(5.5));
        void (out.push(item(0, PREVIEW_POINT)));
        void (out.push(item(1, PREVIEW_POINT)));
        void (out.push(item(2, PREVIEW_POINT)));
        void (out.push(item(3, PREVIEW_POINT)));
    };
    const pts = map_1((p) => [p.X, p.Y], toolPoints);
    const out_1 = [];
    switch (tool) {
        case "line":
        case "rectangle":
        case "roundedRectangle":
        case "circle": {
            const all = (cursor == null) ? pts : append(pts, singleton(cursor));
            iterate(curry2(pushInstance)(out_1), all);
            break;
        }
        case "arc": {
            const cOpt = cursor;
            iterate(curry2(pushInstance)(out_1), pts);
            if (cOpt == null) {
            }
            else {
                pushInstance(out_1, cOpt);
            }
            break;
        }
        default:
            undefined;
    }
    return out_1.slice();
}

/**
 * Gizmo: short X and Y axis line segments at the sketch origin.
 * Axis length is a fixed value in sketch-local coords (~10 units). Drawn
 * via the normal line pipeline (same vertex format).
 */
export function buildSketchGizmoBuffer() {
    const out = [];
    pushSegment(out, 0, 0, 10, 0, AXIS_X_COLOUR);
    pushSegment(out, 0, 0, 0, 10, AXIS_Y_COLOUR);
    return out.slice();
}

function computeSketchBounds(points, entities) {
    const contributions = collect((e) => {
        switch (e.tag) {
            case 0: {
                const matchValue = tryFind(e.fields[0], points);
                if (matchValue == null) {
                    return empty();
                }
                else {
                    return singleton(matchValue);
                }
            }
            case 2: {
                const fallbackR = e.fields[2];
                const matchValue_1 = tryFind(e.fields[1], points);
                if (matchValue_1 == null) {
                    return empty();
                }
                else {
                    const cy = matchValue_1[1];
                    const cx = matchValue_1[0];
                    return ofArray([[cx - fallbackR, cy - fallbackR], [cx + fallbackR, cy + fallbackR]]);
                }
            }
            default:
                return empty();
        }
    }, entities);
    if (isEmpty(contributions)) {
        return [[-10, -10], [10, 10]];
    }
    else {
        const xs = map_1((tuple) => tuple[0], contributions);
        const ys = map_1((tuple_1) => tuple_1[1], contributions);
        return [[min(xs, {
            Compare: comparePrimitives,
        }), min(ys, {
            Compare: comparePrimitives,
        })], [max_1(xs, {
            Compare: comparePrimitives,
        }), max_1(ys, {
            Compare: comparePrimitives,
        })]];
    }
}

/**
 * Build a line-list grid covering the sketch's extent plus a margin.
 * Minor lines at every `step` unit, major every `majorEvery * step`.
 */
export function buildSketchGridBuffer(sketchId, entities, slotLookup, paramValues, step, majorEvery) {
    const patternInput = computeSketchBounds(resolvePointMap(slotLookup, paramValues, sketchId, entities), entities);
    const minP = patternInput[0];
    const maxP = patternInput[1];
    const margin = step * majorEvery;
    const loX = minP[0] - margin;
    const hiX = maxP[0] + margin;
    const loY = minP[1] - margin;
    const hiY = maxP[1] + margin;
    const out = [];
    iterate((i) => {
        const x = i * step;
        pushSegment(out, x, loY, x, hiY, ((i % majorEvery) === 0) ? GRID_MAJOR : GRID_MINOR);
    }, toList(rangeDouble(~~Math.floor(loX / step), 1, ~~Math.ceil(hiX / step))));
    iterate((i_1) => {
        const y = i_1 * step;
        pushSegment(out, loX, y, hiX, y, ((i_1 % majorEvery) === 0) ? GRID_MAJOR : GRID_MINOR);
    }, toList(rangeDouble(~~Math.floor(loY / step), 1, ~~Math.ceil(hiY / step))));
    return out.slice();
}

const POINT_RADIUS_PX = 5;

/**
 * Build an instance buffer for one sketch's points.
 * Each instance = 7 floats: (cx, cy, radiusPx, r, g, b, a).
 * Active (hovered / selected) points render bigger in accent colour.
 */
export function buildSketchPointBuffer(sketchId, entities, slotLookup, paramValues, hovered, selected) {
    return toArray(collect((entity) => {
        if (entity.tag === 0) {
            const id = entity.fields[0];
            const readSlot = (path, fallback) => {
                const matchValue = tryFind(new SlotRef(sketchId, path), slotLookup);
                let matchResult, s_1;
                if (matchValue != null) {
                    if (matchValue < paramValues.length) {
                        matchResult = 0;
                        s_1 = matchValue;
                    }
                    else {
                        matchResult = 1;
                    }
                }
                else {
                    matchResult = 1;
                }
                switch (matchResult) {
                    case 0:
                        return item(s_1, paramValues);
                    default:
                        return fallback;
                }
            };
            const rx = readSlot(toText(printf("sketch.entity.%s.x"))(id), entity.fields[1]);
            const ry = readSlot(toText(printf("sketch.entity.%s.y"))(id), entity.fields[2]);
            const patternInput = isEntityActive(sketchId, "point", id, hovered, selected) ? [ACCENT, POINT_RADIUS_PX * 1.5] : [SKETCH_POINT, POINT_RADIUS_PX];
            const colour = patternInput[0];
            return ofArray([rx, ry, patternInput[1], item(0, colour), item(1, colour), item(2, colour), item(3, colour)]);
        }
        else {
            return empty();
        }
    }, entities));
}

const LINE_PICK_THICKNESS = 0.15000000596046448;

function pushPickSegment(out, a_, a__1, b_, b__1, pickId) {
    const a = [a_, a__1];
    const b = [b_, b__1];
    void (out.push(a[0]));
    void (out.push(a[1]));
    void (out.push(b[0]));
    void (out.push(b[1]));
    void (out.push(pickId));
}

function pushPickCircle(out, center_, center__1, radius, pickId) {
    const center = [center_, center__1];
    const cy = center[1];
    const cx = center[0];
    const n = CIRCLE_SEGMENTS | 0;
    const twoPi = 2 * 3.141592653589793;
    let prev = [cx + radius, cy];
    for (let i = 1; i <= n; i++) {
        const t = (twoPi * i) / n;
        const next = [cx + (radius * Math.cos(t)), cy + (radius * Math.sin(t))];
        pushPickSegment(out, prev[0], prev[1], next[0], next[1], pickId);
        prev = next;
    }
}

function pushPickArc(out, startP_, startP__1, endP_, endP__1, center_, center__1, clockwise, pickId) {
    const startP = [startP_, startP__1];
    const endP = [endP_, endP__1];
    const center = [center_, center__1];
    const sy = startP[1];
    const sx = startP[0];
    const cy = center[1];
    const cx = center[0];
    const radius = Math.sqrt(((sx - cx) * (sx - cx)) + ((sy - cy) * (sy - cy)));
    if (radius < 1E-09) {
        pushPickSegment(out, startP[0], startP[1], endP[0], endP[1], pickId);
    }
    else {
        const startAngle = Math.atan2(sy - cy, sx - cx);
        const endAngle = Math.atan2(endP[1] - cy, endP[0] - cx);
        const tau = 2 * 3.141592653589793;
        let sweep;
        if (clockwise) {
            let d = startAngle - endAngle;
            while (d < 0) {
                d = (d + tau);
            }
            sweep = -d;
        }
        else {
            let d_1 = endAngle - startAngle;
            while (d_1 < 0) {
                d_1 = (d_1 + tau);
            }
            sweep = d_1;
        }
        const segments = max(4, ~~(Math.abs(sweep) / (tau / CIRCLE_SEGMENTS))) | 0;
        let prev = startP;
        for (let i = 1; i <= segments; i++) {
            const ang = startAngle + ((sweep * i) / segments);
            const next = [cx + (radius * Math.cos(ang)), cy + (radius * Math.sin(ang))];
            pushPickSegment(out, prev[0], prev[1], next[0], next[1], pickId);
            prev = next;
        }
    }
}

/**
 * Build an instance buffer for picking sketch lines, circles and arcs.
 * Each instance = 5 floats: (ax, ay, bx, by, pickIdAsFloat).
 */
export function buildSketchPickLineBuffer(sketchId, entities, slotLookup, paramValues, pickables) {
    const idByKey = ofList(choose((p) => {
        let matchResult, eid_3, pid_3, sid_3, eid_4, pid_4, sid_4, eid_5, pid_5, sid_5;
        switch (p.tag) {
            case 1: {
                if (p.fields[1] === sketchId) {
                    matchResult = 0;
                    eid_3 = p.fields[2];
                    pid_3 = p.fields[0];
                    sid_3 = p.fields[1];
                }
                else {
                    matchResult = 3;
                }
                break;
            }
            case 2: {
                if (p.fields[1] === sketchId) {
                    matchResult = 1;
                    eid_4 = p.fields[2];
                    pid_4 = p.fields[0];
                    sid_4 = p.fields[1];
                }
                else {
                    matchResult = 3;
                }
                break;
            }
            case 3: {
                if (p.fields[1] === sketchId) {
                    matchResult = 2;
                    eid_5 = p.fields[2];
                    pid_5 = p.fields[0];
                    sid_5 = p.fields[1];
                }
                else {
                    matchResult = 3;
                }
                break;
            }
            default:
                matchResult = 3;
        }
        switch (matchResult) {
            case 0:
                return ["line:" + eid_3, pid_3];
            case 1:
                return ["circle:" + eid_4, pid_4];
            case 2:
                return ["arc:" + eid_5, pid_5];
            default:
                return undefined;
        }
    }, pickables), {
        Compare: comparePrimitives,
    });
    const lookup = (key) => tryFind(key, idByKey);
    const out = [];
    const points = resolvePointMap(slotLookup, paramValues, sketchId, entities);
    iterate((entity) => {
        switch (entity.tag) {
            case 1: {
                const matchValue = lookup("line:" + entity.fields[0]);
                const matchValue_1 = tryFind(entity.fields[1], points);
                const matchValue_2 = tryFind(entity.fields[2], points);
                let matchResult_1, a, b, pid_6;
                if (matchValue != null) {
                    if (matchValue_1 != null) {
                        if (matchValue_2 != null) {
                            matchResult_1 = 0;
                            a = matchValue_1;
                            b = matchValue_2;
                            pid_6 = matchValue;
                        }
                        else {
                            matchResult_1 = 1;
                        }
                    }
                    else {
                        matchResult_1 = 1;
                    }
                }
                else {
                    matchResult_1 = 1;
                }
                switch (matchResult_1) {
                    case 0: {
                        pushPickSegment(out, a[0], a[1], b[0], b[1], pid_6);
                        break;
                    }
                    case 1: {
                        break;
                    }
                }
                break;
            }
            case 2: {
                const matchValue_4 = lookup("circle:" + entity.fields[0]);
                const matchValue_5 = tryFind(entity.fields[1], points);
                let matchResult_2, c, pid_7;
                if (matchValue_4 != null) {
                    if (matchValue_5 != null) {
                        matchResult_2 = 0;
                        c = matchValue_5;
                        pid_7 = matchValue_4;
                    }
                    else {
                        matchResult_2 = 1;
                    }
                }
                else {
                    matchResult_2 = 1;
                }
                switch (matchResult_2) {
                    case 0: {
                        pushPickCircle(out, c[0], c[1], resolveScalar(slotLookup, paramValues, sketchId, toText(printf("sketch.entity.%s.radius"))(entity.fields[0]), entity.fields[2]), pid_7);
                        break;
                    }
                    case 1: {
                        break;
                    }
                }
                break;
            }
            case 3: {
                if (entity.fields[3].tag === 1) {
                }
                else {
                    const matchValue_7 = lookup("arc:" + entity.fields[0]);
                    const matchValue_8 = tryFind(entity.fields[1], points);
                    const matchValue_9 = tryFind(entity.fields[2], points);
                    const matchValue_10 = tryFind(entity.fields[3].fields[0], points);
                    let matchResult_3, c_1, e, pid_8, s;
                    if (matchValue_7 != null) {
                        if (matchValue_8 != null) {
                            if (matchValue_9 != null) {
                                if (matchValue_10 != null) {
                                    matchResult_3 = 0;
                                    c_1 = matchValue_10;
                                    e = matchValue_9;
                                    pid_8 = matchValue_7;
                                    s = matchValue_8;
                                }
                                else {
                                    matchResult_3 = 1;
                                }
                            }
                            else {
                                matchResult_3 = 1;
                            }
                        }
                        else {
                            matchResult_3 = 1;
                        }
                    }
                    else {
                        matchResult_3 = 1;
                    }
                    switch (matchResult_3) {
                        case 0: {
                            pushPickArc(out, s[0], s[1], e[0], e[1], c_1[0], c_1[1], entity.fields[3].fields[1], pid_8);
                            break;
                        }
                        case 1: {
                            break;
                        }
                    }
                }
                break;
            }
            default:
                undefined;
        }
    }, entities);
    return out.slice();
}

/**
 * The shader needs this as a uniform or a constant. Exposed here so the
 * viewer can pass it through.
 */
export function pickLineThickness() {
    return LINE_PICK_THICKNESS;
}

const LOOP_FILL = new Float32Array([0.7409999966621399, 0.6940000057220459, 0.574999988079071, 0.18000000715255737]);

const LOOP_HIGHLIGHT = new Float32Array([0.5019999742507935, 0.7450000047683716, 0.5490000247955322, 0.20000000298023224]);

function sampleCircleBoundary(center_, center__1, radius, segments) {
    const center = [center_, center__1];
    return toList(delay(() => map_2((i) => {
        const ang = ((i / segments) * 2) * 3.141592653589793;
        return [center[0] + (radius * Math.cos(ang)), center[1] + (radius * Math.sin(ang))];
    }, rangeDouble(0, 1, segments))));
}

function sampleArcBoundary(startP_, startP__1, endP_, endP__1, center_, center__1, cw, segments) {
    const startP = [startP_, startP__1];
    const endP = [endP_, endP__1];
    const center = [center_, center__1];
    const sy = startP[1];
    const sx = startP[0];
    const cy = center[1];
    const cx = center[0];
    const startAngle = Math.atan2(sy - cy, sx - cx);
    const endAngle = Math.atan2(endP[1] - cy, endP[0] - cx);
    const radius = Math.sqrt(((sx - cx) * (sx - cx)) + ((sy - cy) * (sy - cy)));
    let sweep = endAngle - startAngle;
    if (cw && (sweep > 0)) {
        sweep = (sweep - (2 * 3.141592653589793));
    }
    else if (!cw && (sweep < 0)) {
        sweep = (sweep + (2 * 3.141592653589793));
    }
    return toList(delay(() => map_2((i) => {
        const t = i / segments;
        const ang = startAngle + (sweep * t);
        return [cx + (radius * Math.cos(ang)), cy + (radius * Math.sin(ang))];
    }, rangeDouble(0, 1, segments))));
}

function near2(a_, a__1, b_, b__1) {
    const a = [a_, a__1];
    const b = [b_, b__1];
    const dy = a[1] - b[1];
    const dx = a[0] - b[0];
    return ((dx * dx) + (dy * dy)) < 1E-06;
}

function edgeForward(entity, points, slotLookup, paramValues, sketchId) {
    let matchResult, endId, startId, centerId, cw, endId_1, startId_1;
    switch (entity.tag) {
        case 1: {
            matchResult = 0;
            endId = entity.fields[2];
            startId = entity.fields[1];
            break;
        }
        case 3: {
            if (entity.fields[3].tag === 0) {
                matchResult = 1;
                centerId = entity.fields[3].fields[0];
                cw = entity.fields[3].fields[1];
                endId_1 = entity.fields[2];
                startId_1 = entity.fields[1];
            }
            else {
                matchResult = 2;
            }
            break;
        }
        default:
            matchResult = 2;
    }
    switch (matchResult) {
        case 0: {
            const matchValue = tryFind(startId, points);
            const matchValue_1 = tryFind(endId, points);
            let matchResult_1, e, s;
            if (matchValue != null) {
                if (matchValue_1 != null) {
                    matchResult_1 = 0;
                    e = matchValue_1;
                    s = matchValue;
                }
                else {
                    matchResult_1 = 1;
                }
            }
            else {
                matchResult_1 = 1;
            }
            switch (matchResult_1) {
                case 0:
                    return ofArray([s, e]);
                default:
                    return undefined;
            }
        }
        case 1: {
            const matchValue_3 = tryFind(startId_1, points);
            const matchValue_4 = tryFind(endId_1, points);
            const matchValue_5 = tryFind(centerId, points);
            let matchResult_2, c, e_1, s_1;
            if (matchValue_3 != null) {
                if (matchValue_4 != null) {
                    if (matchValue_5 != null) {
                        matchResult_2 = 0;
                        c = matchValue_5;
                        e_1 = matchValue_4;
                        s_1 = matchValue_3;
                    }
                    else {
                        matchResult_2 = 1;
                    }
                }
                else {
                    matchResult_2 = 1;
                }
            }
            else {
                matchResult_2 = 1;
            }
            switch (matchResult_2) {
                case 0:
                    return sampleArcBoundary(s_1[0], s_1[1], e_1[0], e_1[1], c[0], c[1], cw, 48);
                default:
                    return undefined;
            }
        }
        default:
            return undefined;
    }
}

function resolveLoopBoundary(entityMap, points, slotLookup, paramValues, sketchId, entityIds) {
    let tupledArg_1, tupledArg_2;
    let matchResult, singleId;
    if (!isEmpty(entityIds)) {
        if (isEmpty(tail_1(entityIds))) {
            matchResult = 0;
            singleId = head(entityIds);
        }
        else {
            matchResult = 1;
        }
    }
    else {
        matchResult = 1;
    }
    switch (matchResult) {
        case 0: {
            const matchValue = tryFind(singleId, entityMap);
            let matchResult_1, centerId, fallbackR, id;
            if (matchValue != null) {
                if (matchValue.tag === 2) {
                    matchResult_1 = 0;
                    centerId = matchValue.fields[1];
                    fallbackR = matchValue.fields[2];
                    id = matchValue.fields[0];
                }
                else {
                    matchResult_1 = 1;
                }
            }
            else {
                matchResult_1 = 1;
            }
            switch (matchResult_1) {
                case 0: {
                    const matchValue_1 = tryFind(centerId, points);
                    if (matchValue_1 == null) {
                        return undefined;
                    }
                    else {
                        const c = matchValue_1;
                        return sampleCircleBoundary(c[0], c[1], resolveScalar(slotLookup, paramValues, sketchId, toText(printf("sketch.entity.%s.radius"))(id), fallbackR), 48);
                    }
                }
                default:
                    return undefined;
            }
        }
        default: {
            const edges = choose((id_1) => bind((e) => edgeForward(e, points, slotLookup, paramValues, sketchId), tryFind(id_1, entityMap)), entityIds);
            if (length_1(edges) !== length_1(entityIds)) {
                return undefined;
            }
            else {
                const arr = toArray(edges);
                const used = new Set([]);
                addToSet(0, used);
                let ordered = item(0, arr);
                let tail = last(ordered);
                let progress = true;
                while ((used.size < arr.length) && progress) {
                    progress = false;
                    let i = 0;
                    while ((i < arr.length) && !progress) {
                        if (!used.has(i)) {
                            const edge = item(i, arr);
                            let startsAtTail;
                            const tupledArg = head(edge);
                            startsAtTail = near2(tupledArg[0], tupledArg[1], tail[0], tail[1]);
                            if (startsAtTail ? true : ((tupledArg_1 = last(edge), near2(tupledArg_1[0], tupledArg_1[1], tail[0], tail[1])))) {
                                const segment = startsAtTail ? edge : reverse(edge);
                                ordered = append(ordered, tail_1(segment));
                                tail = last(segment);
                                addToSet(i, used);
                                progress = true;
                            }
                        }
                        i = ((i + 1) | 0);
                    }
                }
                if (used.size === arr.length) {
                    const closed = !((tupledArg_2 = head(ordered), near2(tupledArg_2[0], tupledArg_2[1], tail[0], tail[1]))) ? append(ordered, singleton(head(ordered))) : ordered;
                    if (length_1(closed) >= 4) {
                        return closed;
                    }
                    else {
                        return undefined;
                    }
                }
                else {
                    return undefined;
                }
            }
        }
    }
}

function cross2(_arg2_, _arg2__1, _arg1_, _arg1__1) {
    const _arg = [_arg2_, _arg2__1];
    const _arg_1 = [_arg1_, _arg1__1];
    return (_arg[0] * _arg_1[1]) - (_arg[1] * _arg_1[0]);
}

function sub2(_arg2_, _arg2__1, _arg1_, _arg1__1) {
    const _arg = [_arg2_, _arg2__1];
    const _arg_1 = [_arg1_, _arg1__1];
    return [_arg[0] - _arg_1[0], _arg[1] - _arg_1[1]];
}

function pointInTriangle(p_, p__1, a_, a__1, b_, b__1, c_, c__1) {
    const p = [p_, p__1];
    const a = [a_, a__1];
    const b = [b_, b__1];
    const c = [c_, c__1];
    let s1;
    const tupledArg = sub2(b[0], b[1], a[0], a[1]);
    const tupledArg_1 = sub2(p[0], p[1], a[0], a[1]);
    s1 = cross2(tupledArg[0], tupledArg[1], tupledArg_1[0], tupledArg_1[1]);
    let s2;
    const tupledArg_2 = sub2(c[0], c[1], b[0], b[1]);
    const tupledArg_3 = sub2(p[0], p[1], b[0], b[1]);
    s2 = cross2(tupledArg_2[0], tupledArg_2[1], tupledArg_3[0], tupledArg_3[1]);
    let s3;
    const tupledArg_4 = sub2(a[0], a[1], c[0], c[1]);
    const tupledArg_5 = sub2(p[0], p[1], c[0], c[1]);
    s3 = cross2(tupledArg_4[0], tupledArg_4[1], tupledArg_5[0], tupledArg_5[1]);
    return !((((s1 < -1E-06) ? true : (s2 < -1E-06)) ? true : (s3 < -1E-06)) && (((s1 > 1E-06) ? true : (s2 > 1E-06)) ? true : (s3 > 1E-06)));
}

function polygonSignedArea(points) {
    const n = points.length | 0;
    let area = 0;
    for (let i = 0; i <= (n - 1); i++) {
        const patternInput = item(i, points);
        const patternInput_1 = item((i + 1) % n, points);
        area = ((area + (patternInput[0] * patternInput_1[1])) - (patternInput_1[0] * patternInput[1]));
    }
    return area * 0.5;
}

function sameP(a_, a__1, b_, b__1) {
    const a = [a_, a__1];
    const b = [b_, b__1];
    return near2(a[0], a[1], b[0], b[1]);
}

function triangulatePolygon(polygon) {
    const points = toArray(polygon);
    if (points.length < 3) {
        return empty();
    }
    else {
        const winding = ((polygonSignedArea(points) >= 0) ? 1 : -1) | 0;
        let indices = toList(rangeDouble(0, 1, points.length - 1));
        const triangles = [];
        let guard = 0;
        const maxGuard = ((points.length * points.length) + 2) | 0;
        while ((length_1(indices) > 2) && (guard < maxGuard)) {
            const idxArr = toArray(indices);
            let clipped = false;
            let i = 0;
            while ((i < idxArr.length) && !clipped) {
                const i0 = item(((i + idxArr.length) - 1) % idxArr.length, idxArr) | 0;
                const i1 = item(i, idxArr) | 0;
                const i2 = item((i + 1) % idxArr.length, idxArr) | 0;
                const a = item(i0, points);
                const b = item(i1, points);
                const c = item(i2, points);
                let turn;
                const tupledArg = sub2(b[0], b[1], a[0], a[1]);
                const tupledArg_1 = sub2(c[0], c[1], b[0], b[1]);
                turn = cross2(tupledArg[0], tupledArg[1], tupledArg_1[0], tupledArg_1[1]);
                if (((winding > 0) ? (turn > 1E-06) : (turn < -1E-06)) && forAll((k) => {
                    const p = item(k, points);
                    if ((sameP(p[0], p[1], a[0], a[1]) ? true : sameP(p[0], p[1], b[0], b[1])) ? true : sameP(p[0], p[1], c[0], c[1])) {
                        return true;
                    }
                    else {
                        return !pointInTriangle(p[0], p[1], a[0], a[1], b[0], b[1], c[0], c[1]);
                    }
                }, indices)) {
                    void (triangles.push((winding > 0) ? [a, b, c] : [a, c, b]));
                    indices = filter((x) => (x !== i1), indices);
                    clipped = true;
                }
                i = ((i + 1) | 0);
            }
            if (!clipped) {
                guard = (maxGuard | 0);
            }
            guard = ((guard + 1) | 0);
        }
        return ofSeq(triangles);
    }
}

function pushTriangleVertex(out, _arg1_, _arg1__1, colour) {
    const _arg = [_arg1_, _arg1__1];
    void (out.push(_arg[0]));
    void (out.push(_arg[1]));
    void (out.push(item(0, colour)));
    void (out.push(item(1, colour)));
    void (out.push(item(2, colour)));
    void (out.push(item(3, colour)));
}

function isLoopActive(sketchId, loopId, hovered, selected) {
    const matches = (t) => {
        if (t.tag === 4) {
            if (t.fields[0] === sketchId) {
                return t.fields[1] === loopId;
            }
            else {
                return false;
            }
        }
        else {
            return false;
        }
    };
    if ((hovered == null) ? false : matches(hovered)) {
        return true;
    }
    else {
        return exists(matches, selected);
    }
}

/**
 * Triangle-list pick buffer for loops. 3 floats per vertex: (x, y, pickId).
 */
export function buildSketchLoopPickBuffer(sketchId, sketch, loops, slotLookup, paramValues, pickables) {
    const pickByLoopId = ofList(choose((p) => {
        let matchResult, lid_1, pid_1, sid_1;
        if (p.tag === 4) {
            if (p.fields[1] === sketchId) {
                matchResult = 0;
                lid_1 = p.fields[2];
                pid_1 = p.fields[0];
                sid_1 = p.fields[1];
            }
            else {
                matchResult = 1;
            }
        }
        else {
            matchResult = 1;
        }
        switch (matchResult) {
            case 0:
                return [lid_1, pid_1];
            default:
                return undefined;
        }
    }, pickables), {
        Compare: comparePrimitives,
    });
    const entityMap = ofList(map_1((e) => {
        switch (e.tag) {
            case 1:
                return [e.fields[0], e];
            case 2:
                return [e.fields[0], e];
            case 3:
                return [e.fields[0], e];
            default:
                return [e.fields[0], e];
        }
    }, sketch.Entities), {
        Compare: comparePrimitives,
    });
    const points = resolvePointMap(slotLookup, paramValues, sketchId, sketch.Entities);
    const push = (out, tupledArg, pid_2) => {
        void (out.push(tupledArg[0]));
        void (out.push(tupledArg[1]));
        void (out.push(pid_2));
    };
    const out_1 = [];
    iterate((loop) => {
        let tupledArg_1, tupledArg_2;
        const matchValue = tryFind(loop.Id, pickByLoopId);
        if (matchValue != null) {
            const pickId = matchValue | 0;
            const matchValue_1 = resolveLoopBoundary(entityMap, points, slotLookup, paramValues, sketchId, loop.EntityIds);
            if (matchValue_1 != null) {
                const boundary = matchValue_1;
                const enumerator = getEnumerator(triangulatePolygon(((length_1(boundary) >= 2) && ((tupledArg_1 = head(boundary), (tupledArg_2 = last(boundary), near2(tupledArg_1[0], tupledArg_1[1], tupledArg_2[0], tupledArg_2[1]))))) ? take(length_1(boundary) - 1, boundary) : boundary));
                try {
                    while (enumerator["System.Collections.IEnumerator.MoveNext"]()) {
                        const forLoopVar = enumerator["System.Collections.Generic.IEnumerator`1.get_Current"]();
                        push(out_1, forLoopVar[0], pickId);
                        push(out_1, forLoopVar[1], pickId);
                        push(out_1, forLoopVar[2], pickId);
                    }
                }
                finally {
                    disposeSafe(enumerator);
                }
            }
        }
    }, loops);
    return out_1.slice();
}

/**
 * Build a triangle-list vertex buffer (6 floats per vertex: pos.xy + rgba)
 * for all loop fills in one sketch, optionally via ear-clip triangulation.
 */
export function buildSketchLoopFillBuffer(sketchId, sketch, loops, slotLookup, paramValues, hovered, selected) {
    const entityMap = ofList(map_1((e) => {
        switch (e.tag) {
            case 1:
                return [e.fields[0], e];
            case 2:
                return [e.fields[0], e];
            case 3:
                return [e.fields[0], e];
            default:
                return [e.fields[0], e];
        }
    }, sketch.Entities), {
        Compare: comparePrimitives,
    });
    const points = resolvePointMap(slotLookup, paramValues, sketchId, sketch.Entities);
    const out = [];
    iterate((loop) => {
        let tupledArg, tupledArg_1;
        const matchValue = resolveLoopBoundary(entityMap, points, slotLookup, paramValues, sketchId, loop.EntityIds);
        if (matchValue != null) {
            const boundary = matchValue;
            const polygon = ((length_1(boundary) >= 2) && ((tupledArg = head(boundary), (tupledArg_1 = last(boundary), near2(tupledArg[0], tupledArg[1], tupledArg_1[0], tupledArg_1[1]))))) ? take(length_1(boundary) - 1, boundary) : boundary;
            const colour = isLoopActive(sketchId, loop.Id, hovered, selected) ? LOOP_HIGHLIGHT : LOOP_FILL;
            const enumerator = getEnumerator(triangulatePolygon(polygon));
            try {
                while (enumerator["System.Collections.IEnumerator.MoveNext"]()) {
                    const forLoopVar = enumerator["System.Collections.Generic.IEnumerator`1.get_Current"]();
                    const c = forLoopVar[2];
                    const b = forLoopVar[1];
                    const a = forLoopVar[0];
                    pushTriangleVertex(out, a[0], a[1], colour);
                    pushTriangleVertex(out, b[0], b[1], colour);
                    pushTriangleVertex(out, c[0], c[1], colour);
                }
            }
            finally {
                disposeSafe(enumerator);
            }
        }
    }, loops);
    return out.slice();
}

function dimensionAnchor(points, c) {
    const perpOffset = (a, b) => {
        const matchValue = tryFind(a, points);
        const matchValue_1 = tryFind(b, points);
        let matchResult, pa, pb;
        if (matchValue != null) {
            if (matchValue_1 != null) {
                matchResult = 0;
                pa = matchValue;
                pb = matchValue_1;
            }
            else {
                matchResult = 1;
            }
        }
        else {
            matchResult = 1;
        }
        switch (matchResult) {
            case 0:
                return distanceAnchorFallback(pa[0], pa[1], pb[0], pb[1]);
            default:
                return undefined;
        }
    };
    const resolve = (lp, fallback) => {
        if (lp == null) {
            return fallback;
        }
        else {
            const p = lp;
            return [p.X, p.Y];
        }
    };
    switch (c.tag) {
        case 6:
            return resolve(c.fields[3], perpOffset(c.fields[0], c.fields[1]));
        case 19:
            return resolve(c.fields[6], perpOffset(c.fields[1], c.fields[2]));
        case 18:
            return resolve(c.fields[7], perpOffset(c.fields[1], c.fields[2]));
        case 23:
            return resolve(c.fields[6], perpOffset(c.fields[1], c.fields[3]));
        case 24:
            return resolve(c.fields[10], perpOffset(c.fields[0], c.fields[3]));
        case 7:
            return resolve(c.fields[4], tryFind(c.fields[0], points));
        case 20:
            return resolve(c.fields[5], tryFind(c.fields[0], points));
        case 21:
            return resolve(c.fields[4], tryFind(c.fields[0], points));
        case 22:
            return resolve(c.fields[6], perpOffset(c.fields[1], c.fields[2]));
        case 17:
            return resolve(c.fields[3], tryFind(c.fields[1], points));
        default:
            return undefined;
    }
}

/**
 * Build a pick-point instance buffer for dimension labels (one fat
 * billboard per label at its anchor). Each instance = 4 floats:
 * (cx, cy, radiusPx, pickIdAsFloat). Uses the existing pointPick pipeline.
 */
export function buildSketchDimensionPickBuffer(sketchId, sketch, slotLookup, paramValues, pickables) {
    const points = resolvePointMap(slotLookup, paramValues, sketchId, sketch.Entities);
    const pickByIndex = ofList(choose((p) => {
        let matchResult, idx_1, pid_1, sid_1;
        if (p.tag === 5) {
            if (p.fields[1] === sketchId) {
                matchResult = 0;
                idx_1 = p.fields[2];
                pid_1 = p.fields[0];
                sid_1 = p.fields[1];
            }
            else {
                matchResult = 1;
            }
        }
        else {
            matchResult = 1;
        }
        switch (matchResult) {
            case 0:
                return [idx_1, pid_1];
            default:
                return undefined;
        }
    }, pickables), {
        Compare: comparePrimitives,
    });
    return toArray(collect((tupledArg) => {
        const matchValue = tryFind(tupledArg[0], pickByIndex);
        const matchValue_1 = dimensionAnchor(points, tupledArg[1]);
        let matchResult_1, ax, ay, pid_2;
        if (matchValue != null) {
            if (matchValue_1 != null) {
                matchResult_1 = 0;
                ax = matchValue_1[0];
                ay = matchValue_1[1];
                pid_2 = matchValue;
            }
            else {
                matchResult_1 = 1;
            }
        }
        else {
            matchResult_1 = 1;
        }
        switch (matchResult_1) {
            case 0:
                return ofArray([ax, ay, 20, pid_2]);
            default:
                return empty();
        }
    }, mapIndexed((i, c) => [i, c], sketch.Constraints)));
}

/**
 * Build an instance buffer for picking sketch points. Each instance =
 * 4 floats: (cx, cy, radiusPx, pickIdAsFloat). Uses a fatter radius than
 * the visual pipeline to make points easier to hit.
 */
export function buildSketchPointPickBuffer(sketchId, entities, slotLookup, paramValues, pickables) {
    const pickByPoint = ofList(choose((p) => {
        let matchResult, eid_1, pid_1, sid_1;
        if (p.tag === 0) {
            if (p.fields[1] === sketchId) {
                matchResult = 0;
                eid_1 = p.fields[2];
                pid_1 = p.fields[0];
                sid_1 = p.fields[1];
            }
            else {
                matchResult = 1;
            }
        }
        else {
            matchResult = 1;
        }
        switch (matchResult) {
            case 0:
                return [eid_1, pid_1];
            default:
                return undefined;
        }
    }, pickables), {
        Compare: comparePrimitives,
    });
    return toArray(collect((entity) => {
        if (entity.tag === 0) {
            const id = entity.fields[0];
            const matchValue = tryFind(id, pickByPoint);
            if (matchValue != null) {
                const pickId = matchValue | 0;
                const readSlot = (path, fallback) => {
                    const matchValue_1 = tryFind(new SlotRef(sketchId, path), slotLookup);
                    let matchResult_1, s_1;
                    if (matchValue_1 != null) {
                        if (matchValue_1 < paramValues.length) {
                            matchResult_1 = 0;
                            s_1 = matchValue_1;
                        }
                        else {
                            matchResult_1 = 1;
                        }
                    }
                    else {
                        matchResult_1 = 1;
                    }
                    switch (matchResult_1) {
                        case 0:
                            return item(s_1, paramValues);
                        default:
                            return fallback;
                    }
                };
                return ofArray([readSlot(toText(printf("sketch.entity.%s.x"))(id), entity.fields[1]), readSlot(toText(printf("sketch.entity.%s.y"))(id), entity.fields[2]), 10, pickId]);
            }
            else {
                return empty();
            }
        }
        else {
            return empty();
        }
    }, entities));
}

/**
 * Build an interleaved line-list vertex buffer for one sketch.
 * Each vertex = 6 floats: (x, y, r, g, b, a). Draw mode: line-list.
 */
export function buildSketchLineBuffer(sketchId, entities, slotLookup, paramValues, hovered, selected) {
    const out = [];
    const points = resolvePointMap(slotLookup, paramValues, sketchId, entities);
    const colourFor = (kind, id) => {
        if (isEntityActive(sketchId, kind, id, hovered, selected)) {
            return ACCENT;
        }
        else {
            return SKETCH_LINE;
        }
    };
    iterate((entity) => {
        switch (entity.tag) {
            case 1: {
                const matchValue = tryFind(entity.fields[1], points);
                const matchValue_1 = tryFind(entity.fields[2], points);
                let matchResult, a, b;
                if (matchValue != null) {
                    if (matchValue_1 != null) {
                        matchResult = 0;
                        a = matchValue;
                        b = matchValue_1;
                    }
                    else {
                        matchResult = 1;
                    }
                }
                else {
                    matchResult = 1;
                }
                switch (matchResult) {
                    case 0: {
                        pushSegment(out, a[0], a[1], b[0], b[1], colourFor("line", entity.fields[0]));
                        break;
                    }
                    case 1: {
                        break;
                    }
                }
                break;
            }
            case 2: {
                const matchValue_3 = tryFind(entity.fields[1], points);
                if (matchValue_3 == null) {
                }
                else {
                    const c = matchValue_3;
                    pushCircle(out, c[0], c[1], resolveScalar(slotLookup, paramValues, sketchId, toText(printf("sketch.entity.%s.radius"))(entity.fields[0]), entity.fields[2]), colourFor("circle", entity.fields[0]));
                }
                break;
            }
            case 3: {
                if (entity.fields[3].tag === 1) {
                }
                else {
                    const matchValue_4 = tryFind(entity.fields[1], points);
                    const matchValue_5 = tryFind(entity.fields[2], points);
                    const matchValue_6 = tryFind(entity.fields[3].fields[0], points);
                    let matchResult_1, c_1, e, s;
                    if (matchValue_4 != null) {
                        if (matchValue_5 != null) {
                            if (matchValue_6 != null) {
                                matchResult_1 = 0;
                                c_1 = matchValue_6;
                                e = matchValue_5;
                                s = matchValue_4;
                            }
                            else {
                                matchResult_1 = 1;
                            }
                        }
                        else {
                            matchResult_1 = 1;
                        }
                    }
                    else {
                        matchResult_1 = 1;
                    }
                    switch (matchResult_1) {
                        case 0: {
                            pushArc(out, s[0], s[1], e[0], e[1], c_1[0], c_1[1], entity.fields[3].fields[1], colourFor("arc", entity.fields[0]));
                            break;
                        }
                        case 1: {
                            break;
                        }
                    }
                }
                break;
            }
            default:
                undefined;
        }
    }, entities);
    return out.slice();
}

const FRAME_ORIGIN_COLOUR = new Float32Array([0.30000001192092896, 0.30000001192092896, 0.30000001192092896, 1]);

/**
 * Instance buffer for all frame origin handles, stored as world-space
 * positions. One draw covers every frame in the scene, so no per-frame
 * uniform writes are needed. Layout per instance: 8 floats —
 * (wx, wy, wz, radiusPx, r, g, b, a).
 */
export function buildFrameOriginsPointBuffer(frames, hovered, selected) {
    const out = [];
    const enumerator = getEnumerator(frames);
    try {
        while (enumerator["System.Collections.IEnumerator.MoveNext"]()) {
            const frame = enumerator["System.Collections.Generic.IEnumerator`1.get_Current"]();
            const matches = (t) => {
                if (t.tag === 6) {
                    return t.fields[0] === frame.Id;
                }
                else {
                    return false;
                }
            };
            const patternInput = (((hovered == null) ? false : matches(hovered)) ? true : exists(matches, selected)) ? [ACCENT, POINT_RADIUS_PX * 1.5] : [FRAME_ORIGIN_COLOUR, POINT_RADIUS_PX];
            const colour = patternInput[0];
            const pos = frame.Transform.Trans;
            void (out.push(pos.X));
            void (out.push(pos.Y));
            void (out.push(pos.Z));
            void (out.push(patternInput[1]));
            void (out.push(item(0, colour)));
            void (out.push(item(1, colour)));
            void (out.push(item(2, colour)));
            void (out.push(item(3, colour)));
        }
    }
    finally {
        disposeSafe(enumerator);
    }
    return out.slice();
}

/**
 * Per-frame axis gizmo vertices. Topology = line-list, two vertices per
 * axis × three axes per frame = 6 vertices per frame. Each vertex has 12
 * floats: (origin.xyz, axis.xyz, axis_px, endpoint, color.rgba).
 */
export function buildFramesGizmoBuffer(frames, hovered, selected, selectedActionId) {
    const accent = ACCENT;
    const axisColourX = new Float32Array([0.8799999952316284, 0.41999998688697815, 0.41999998688697815, 1]);
    const axisColourY = new Float32Array([0.47999998927116394, 0.7799999713897705, 0.5400000214576721, 1]);
    const axisColourZ = new Float32Array([0.44999998807907104, 0.5600000023841858, 0.9200000166893005, 1]);
    const out = [];
    const enumerator = getEnumerator(frames);
    try {
        while (enumerator["System.Collections.IEnumerator.MoveNext"]()) {
            const frame = enumerator["System.Collections.Generic.IEnumerator`1.get_Current"]();
            let active;
            const matches = (t) => {
                switch (t.tag) {
                    case 6:
                        return t.fields[0] === frame.Id;
                    case 7:
                        return t.fields[0] === frame.Id;
                    default:
                        return false;
                }
            };
            active = ((((hovered == null) ? false : matches(hovered)) ? true : exists(matches, selected)) ? true : equals(selectedActionId, frame.Id));
            const colourFor = (base_) => {
                if (active) {
                    return new Float32Array([item(0, accent), item(1, accent), item(2, accent), 1]);
                }
                else {
                    return base_;
                }
            };
            const axisPx = (frame.Id === "origin") ? 64 : 52;
            const origin = frame.Transform.Trans;
            const pushAxis = (axis, colour) => {
                const emit = (endpoint) => {
                    void (out.push(origin.X));
                    void (out.push(origin.Y));
                    void (out.push(origin.Z));
                    void (out.push(axis.X));
                    void (out.push(axis.Y));
                    void (out.push(axis.Z));
                    void (out.push(axisPx));
                    void (out.push(endpoint));
                    void (out.push(item(0, colour)));
                    void (out.push(item(1, colour)));
                    void (out.push(item(2, colour)));
                    void (out.push(item(3, colour)));
                };
                emit(0);
                emit(1);
            };
            const rot = frame.Transform.Rot;
            pushAxis(Quat__Rotate_Z2E054BF3(rot, new Vec3(1, 0, 0)), colourFor(axisColourX));
            pushAxis(Quat__Rotate_Z2E054BF3(rot, new Vec3(0, 1, 0)), colourFor(axisColourY));
            pushAxis(Quat__Rotate_Z2E054BF3(rot, new Vec3(0, 0, 1)), colourFor(axisColourZ));
        }
    }
    finally {
        disposeSafe(enumerator);
    }
    return out.slice();
}

/**
 * Pick instances sampled along every frame axis. The visual gizmo scales
 * axis length by screen-space pixels at the frame's depth, so we compute
 * the same `worldPerPx` on the CPU and sprinkle point-pick instances along
 * each axis. Output format matches `worldPointPickPipeline` — 5 floats
 * per instance: (wx, wy, wz, radiusPx, pickId).
 */
export function buildFrameAxesPickBuffer(frames, pickables, eye, forward, tanHalfFov, viewportHeight) {
    const out = [];
    const enumerator = getEnumerator(frames);
    try {
        while (enumerator["System.Collections.IEnumerator.MoveNext"]()) {
            const frame = enumerator["System.Collections.Generic.IEnumerator`1.get_Current"]();
            const origin = frame.Transform.Trans;
            const axisLen = ((frame.Id === "origin") ? 64 : 52) * (((2 * max(Math.abs(Vec3_Dot_Z3F547E60(Vec3_op_Subtraction_Z3F547E60(origin, eye), forward)), 0.001)) * tanHalfFov) / max(viewportHeight, 1));
            const emit = (part_1, localAxis) => {
                const matchValue = tryPick((_arg) => {
                    let matchResult, fid_1, p_1, pid_1;
                    if (_arg.tag === 7) {
                        if ((_arg.fields[1] === frame.Id) && (_arg.fields[2] === part_1)) {
                            matchResult = 0;
                            fid_1 = _arg.fields[1];
                            p_1 = _arg.fields[2];
                            pid_1 = _arg.fields[0];
                        }
                        else {
                            matchResult = 1;
                        }
                    }
                    else {
                        matchResult = 1;
                    }
                    switch (matchResult) {
                        case 0:
                            return pid_1;
                        default:
                            return undefined;
                    }
                }, pickables);
                if (matchValue != null) {
                    const pid_2 = matchValue | 0;
                    const axWorld = Quat__Rotate_Z2E054BF3(frame.Transform.Rot, localAxis);
                    for (let i = 1; i <= 16; i++) {
                        const pos = Vec3_op_Addition_Z3F547E60(origin, Vec3_op_Multiply_ZB3DA56A(axisLen * (i / 16), axWorld));
                        void (out.push(pos.X));
                        void (out.push(pos.Y));
                        void (out.push(pos.Z));
                        void (out.push(6));
                        void (out.push(pid_2));
                    }
                }
            };
            emit("xAxis", new Vec3(1, 0, 0));
            emit("yAxis", new Vec3(0, 1, 0));
            emit("zAxis", new Vec3(0, 0, 1));
        }
    }
    finally {
        disposeSafe(enumerator);
    }
    return out.slice();
}

/**
 * Pick instances for all frame origins. Layout per instance: 5 floats —
 * (wx, wy, wz, radiusPx, pickId).
 */
export function buildFrameOriginsPickBuffer(frames, pickables) {
    const out = [];
    const enumerator = getEnumerator(frames);
    try {
        while (enumerator["System.Collections.IEnumerator.MoveNext"]()) {
            const frame = enumerator["System.Collections.Generic.IEnumerator`1.get_Current"]();
            const pickIdOpt = tryPick((_arg) => {
                let matchResult, fid_1, pid_1;
                if (_arg.tag === 6) {
                    if (_arg.fields[1] === frame.Id) {
                        matchResult = 0;
                        fid_1 = _arg.fields[1];
                        pid_1 = _arg.fields[0];
                    }
                    else {
                        matchResult = 1;
                    }
                }
                else {
                    matchResult = 1;
                }
                switch (matchResult) {
                    case 0:
                        return pid_1;
                    default:
                        return undefined;
                }
            }, pickables);
            if (pickIdOpt == null) {
            }
            else {
                const pid_2 = pickIdOpt | 0;
                const pos = frame.Transform.Trans;
                void (out.push(pos.X));
                void (out.push(pos.Y));
                void (out.push(pos.Z));
                void (out.push(POINT_RADIUS_PX * 2.5));
                void (out.push(pid_2));
            }
        }
    }
    finally {
        disposeSafe(enumerator);
    }
    return out.slice();
}

