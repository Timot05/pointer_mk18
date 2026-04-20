import { Record } from "../../ui/fable_modules/fable-library-js.4.29.0/Types.js";
import { bool_type, int32_type, record_type, float64_type, list_type, string_type } from "../../ui/fable_modules/fable-library-js.4.29.0/Reflection.js";
import { ofSeq as ofSeq_1, head as head_1, append, empty, isEmpty, singleton, sortBy, choose, sort, toArray, length } from "../../ui/fable_modules/fable-library-js.4.29.0/List.js";
import { fill, setItem, indexOf, item } from "../../ui/fable_modules/fable-library-js.4.29.0/Array.js";
import { printf, toText, join } from "../../ui/fable_modules/fable-library-js.4.29.0/String.js";
import { numberHash, disposeSafe, getEnumerator, comparePrimitives } from "../../ui/fable_modules/fable-library-js.4.29.0/Util.js";
import { tryFind, ofSeq, toList, ofList } from "../../ui/fable_modules/fable-library-js.4.29.0/Map.js";
import { FreePoint } from "./Sketch.fs.js";
import { delay, toList as toList_1, map } from "../../ui/fable_modules/fable-library-js.4.29.0/Seq.js";
import { rangeDouble } from "../../ui/fable_modules/fable-library-js.4.29.0/Range.js";
import { getItemFromDict } from "../../ui/fable_modules/fable-library-js.4.29.0/MapUtil.js";

export class SketchLoop extends Record {
    constructor(Id, EntityIds, SignedArea) {
        super();
        this.Id = Id;
        this.EntityIds = EntityIds;
        this.SignedArea = SignedArea;
    }
}

export function SketchLoop_$reflection() {
    return record_type("Server.SketchLoop", [], SketchLoop, () => [["Id", string_type], ["EntityIds", list_type(string_type)], ["SignedArea", float64_type]]);
}

const SketchLoops_CLUSTER_TOL = 0.001;

const SketchLoops_CLUSTER_TOL_SQ = SketchLoops_CLUSTER_TOL * SketchLoops_CLUSTER_TOL;

function SketchLoops_normalizeAngle(a) {
    const twoPi = 3.141592653589793 * 2;
    return ((a % twoPi) + twoPi) % twoPi;
}

/**
 * Shoelace formula. Positive = CCW winding, negative = CW.
 */
export function SketchLoops_polygonSignedArea(points) {
    if (length(points) < 3) {
        return 0;
    }
    else {
        const arr = toArray(points);
        let sum = 0;
        for (let i = 0; i <= (arr.length - 2); i++) {
            const patternInput = item(i, arr);
            const patternInput_1 = item(i + 1, arr);
            sum = ((sum + (patternInput[0] * patternInput_1[1])) - (patternInput_1[0] * patternInput[1]));
        }
        return sum * 0.5;
    }
}

function SketchLoops_loopIdFromEntities(ids) {
    return "loop:" + join(",", sort(ids, {
        Compare: comparePrimitives,
    }));
}

function SketchLoops_collectPoints(entities) {
    return ofList(choose((_arg) => {
        if (_arg.tag === 0) {
            return [_arg.fields[0], new FreePoint(_arg.fields[1], _arg.fields[2])];
        }
        else {
            return undefined;
        }
    }, entities), {
        Compare: comparePrimitives,
    });
}

function SketchLoops_clusterPoints(points) {
    const clusters = [];
    const pointToCluster = new Map([]);
    const enumerator = getEnumerator(sortBy((tuple) => tuple[0], toList(points), {
        Compare: comparePrimitives,
    }));
    try {
        while (enumerator["System.Collections.IEnumerator.MoveNext"]()) {
            const forLoopVar = enumerator["System.Collections.Generic.IEnumerator`1.get_Current"]();
            const p = forLoopVar[1];
            let found = -1;
            let i = 0;
            while ((found < 0) && (i < clusters.length)) {
                const cp = clusters[i];
                const dx = cp.X - p.X;
                const dy = cp.Y - p.Y;
                if (((dx * dx) + (dy * dy)) < SketchLoops_CLUSTER_TOL_SQ) {
                    found = (i | 0);
                }
                i = ((i + 1) | 0);
            }
            let idx;
            if (found >= 0) {
                idx = found;
            }
            else {
                void (clusters.push(p));
                idx = (clusters.length - 1);
            }
            pointToCluster.set(forLoopVar[0], idx);
        }
    }
    finally {
        disposeSafe(enumerator);
    }
    return [ofSeq(map((kv) => [kv[0], kv[1]], pointToCluster), {
        Compare: comparePrimitives,
    }), clusters.slice()];
}

class SketchLoops_HalfEdge extends Record {
    constructor(From, To, EntityId, OutAngle, Twin, IsArc) {
        super();
        this.From = (From | 0);
        this.To = (To | 0);
        this.EntityId = EntityId;
        this.OutAngle = OutAngle;
        this.Twin = (Twin | 0);
        this.IsArc = IsArc;
    }
}

function SketchLoops_HalfEdge_$reflection() {
    return record_type("Server.SketchLoops.HalfEdge", [], SketchLoops_HalfEdge, () => [["From", int32_type], ["To", int32_type], ["EntityId", string_type], ["OutAngle", float64_type], ["Twin", int32_type], ["IsArc", bool_type]]);
}

function SketchLoops_sampleCircleSignedArea(c, radius, segments) {
    return SketchLoops_polygonSignedArea(toList_1(delay(() => map((i) => {
        const angle = ((i / segments) * 3.141592653589793) * 2;
        return [c.X + (Math.cos(angle) * radius), c.Y + (Math.sin(angle) * radius)];
    }, rangeDouble(0, 1, segments)))));
}

export function SketchLoops_detectLoops(entities) {
    let c_1;
    const pointsById = SketchLoops_collectPoints(entities);
    const circleLoops = choose((_arg) => {
        if (_arg.tag === 2) {
            const id = _arg.fields[0];
            const matchValue = tryFind(_arg.fields[1], pointsById);
            if (matchValue == null) {
                return undefined;
            }
            else {
                const c = matchValue;
                return new SketchLoop(toText(printf("circle:%s"))(id), singleton(id), SketchLoops_sampleCircleSignedArea(c, _arg.fields[2], 24));
            }
        }
        else {
            return undefined;
        }
    }, entities);
    const patternInput = SketchLoops_clusterPoints(pointsById);
    const clusterPositions = patternInput[1];
    const edges = [];
    const tryClusterIdx = (id_1) => tryFind(id_1, patternInput[0]);
    const enumerator = getEnumerator(entities);
    try {
        while (enumerator["System.Collections.IEnumerator.MoveNext"]()) {
            const entity = enumerator["System.Collections.Generic.IEnumerator`1.get_Current"]();
            switch (entity.tag) {
                case 1: {
                    const matchValue_1 = tryClusterIdx(entity.fields[1]);
                    const matchValue_2 = tryClusterIdx(entity.fields[2]);
                    let matchResult, ei_1, si_1;
                    if (matchValue_1 != null) {
                        if (matchValue_2 != null) {
                            if (matchValue_1 !== matchValue_2) {
                                matchResult = 0;
                                ei_1 = matchValue_2;
                                si_1 = matchValue_1;
                            }
                            else {
                                matchResult = 1;
                            }
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
                            const sp = item(si_1, clusterPositions);
                            const ep = item(ei_1, clusterPositions);
                            const fwd = SketchLoops_normalizeAngle(Math.atan2(ep.Y - sp.Y, ep.X - sp.X));
                            const rev = SketchLoops_normalizeAngle(fwd + 3.141592653589793);
                            const idxA = edges.length | 0;
                            const idxB = (idxA + 1) | 0;
                            void (edges.push(new SketchLoops_HalfEdge(si_1, ei_1, entity.fields[0], fwd, idxB, false)));
                            void (edges.push(new SketchLoops_HalfEdge(ei_1, si_1, entity.fields[0], rev, idxA, false)));
                            break;
                        }
                    }
                    break;
                }
                case 3: {
                    if (entity.fields[3].tag === 1) {
                    }
                    else {
                        const matchValue_4 = tryClusterIdx(entity.fields[1]);
                        const matchValue_5 = tryClusterIdx(entity.fields[2]);
                        const matchValue_6 = tryFind(entity.fields[3].fields[0], pointsById);
                        let matchResult_1, c_2, ei_3, si_3;
                        if (matchValue_4 != null) {
                            if (matchValue_5 != null) {
                                if (matchValue_6 != null) {
                                    if ((c_1 = matchValue_6, matchValue_4 !== matchValue_5)) {
                                        matchResult_1 = 0;
                                        c_2 = matchValue_6;
                                        ei_3 = matchValue_5;
                                        si_3 = matchValue_4;
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
                            case 0: {
                                const sp_1 = item(si_3, clusterPositions);
                                const ep_1 = item(ei_3, clusterPositions);
                                const tangent = (p) => {
                                    const dx = p.X - c_2.X;
                                    const dy = p.Y - c_2.Y;
                                    if (entity.fields[3].fields[1]) {
                                        return [dy, -dx];
                                    }
                                    else {
                                        return [-dy, dx];
                                    }
                                };
                                const patternInput_1 = tangent(sp_1);
                                const fwd_1 = SketchLoops_normalizeAngle(Math.atan2(patternInput_1[1], patternInput_1[0]));
                                const patternInput_2 = tangent(ep_1);
                                const rev_1 = SketchLoops_normalizeAngle(Math.atan2(-patternInput_2[1], -patternInput_2[0]));
                                const idxA_1 = edges.length | 0;
                                const idxB_1 = (idxA_1 + 1) | 0;
                                void (edges.push(new SketchLoops_HalfEdge(si_3, ei_3, entity.fields[0], fwd_1, idxB_1, true)));
                                void (edges.push(new SketchLoops_HalfEdge(ei_3, si_3, entity.fields[0], rev_1, idxA_1, true)));
                                break;
                            }
                        }
                    }
                    break;
                }
                default:
                    undefined;
            }
        }
    }
    finally {
        disposeSafe(enumerator);
    }
    if (edges.length === 0) {
        return circleLoops;
    }
    else {
        const vertexOut = new Map([]);
        for (let i = 0; i <= (edges.length - 1); i++) {
            const fromV = edges[i].From | 0;
            if (!vertexOut.has(fromV)) {
                vertexOut.set(fromV, []);
            }
            void (getItemFromDict(vertexOut, fromV).push(i));
        }
        let enumerator_1 = getEnumerator(vertexOut);
        try {
            while (enumerator_1["System.Collections.IEnumerator.MoveNext"]()) {
                const kv = enumerator_1["System.Collections.Generic.IEnumerator`1.get_Current"]();
                kv[1].sort((a, b) => comparePrimitives(edges[a].OutAngle, edges[b].OutAngle));
            }
        }
        finally {
            disposeSafe(enumerator_1);
        }
        const nextEdge = new Int32Array(edges.length);
        for (let i_1 = 0; i_1 <= (edges.length - 1); i_1++) {
            const e = edges[i_1];
            const outList = getItemFromDict(vertexOut, e.To);
            const twinPos = indexOf(outList, e.Twin, undefined, undefined, {
                Equals: (x, y) => (x === y),
                GetHashCode: numberHash,
            }) | 0;
            const prevPos = ((twinPos === 0) ? (outList.length - 1) : (twinPos - 1)) | 0;
            setItem(nextEdge, i_1, outList[prevPos] | 0);
        }
        const visited = fill(new Array(edges.length), 0, edges.length, false);
        const faceLoops = [];
        for (let startEdge = 0; startEdge <= (edges.length - 1); startEdge++) {
            if (!item(startEdge, visited)) {
                const faceEdges = [];
                let cur = startEdge;
                let running = true;
                while (running) {
                    if (item(cur, visited)) {
                        running = false;
                    }
                    else {
                        setItem(visited, cur, true);
                        void (faceEdges.push(cur));
                        cur = (item(cur, nextEdge) | 0);
                        if (cur === startEdge) {
                            running = false;
                        }
                    }
                }
                if (!((faceEdges.length < 2) && !edges[faceEdges[0]].IsArc)) {
                    const boundary = toList_1(delay(() => map((ei_4) => {
                        const fromV_1 = item(edges[ei_4].From, clusterPositions);
                        return [fromV_1.X, fromV_1.Y];
                    }, faceEdges)));
                    const signedArea = SketchLoops_polygonSignedArea(isEmpty(boundary) ? empty() : append(boundary, singleton(head_1(boundary))));
                    if (signedArea > 0) {
                        const entityIds = toList_1(delay(() => map((ei_5) => edges[ei_5].EntityId, faceEdges)));
                        void (faceLoops.push(new SketchLoop(SketchLoops_loopIdFromEntities(entityIds), entityIds, signedArea)));
                    }
                }
            }
        }
        return append(circleLoops, ofSeq_1(faceLoops));
    }
}

