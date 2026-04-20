import { FSharpRef, Record, Union } from "../../ui/fable_modules/fable-library-js.4.29.0/Types.js";
import { int32_type, record_type, class_type, string_type, float64_type, union_type } from "../../ui/fable_modules/fable-library-js.4.29.0/Reflection.js";
import { RigidTransform__Apply_Z2E054BF3, RigidTransform__get_Inverse, RigidTransform_$reflection } from "../Math/Transform.fs.js";
import { addToSet, tryGetValue, getItemFromDict } from "../../ui/fable_modules/fable-library-js.4.29.0/MapUtil.js";
import { equals, defaultOf, comparePrimitives, disposeSafe, getEnumerator } from "../../ui/fable_modules/fable-library-js.4.29.0/Util.js";
import { tryFind as tryFind_1, ofList } from "../../ui/fable_modules/fable-library-js.4.29.0/Map.js";
import { ofArray, tryFind, choose } from "../../ui/fable_modules/fable-library-js.4.29.0/List.js";
import { Quat__Rotate_Z2E054BF3 } from "../Math/Quat.fs.js";
import { Vec3_get_Zero, Vec3 } from "../Math/Vec.fs.js";
import { GraphBuilder__Build_7E3D5760, GraphBuilder__Div_Z37302880, GraphBuilder__Neg_Z524259A4, GraphBuilder__Atan2_Z37302880, GraphBuilder__Param_5E38073B, GraphBuilder__get_ParamCount, GraphBuilder_$ctor, GraphBuilder__Sqrt_Z524259A4, GraphBuilder__Mul_Z37302880, GraphBuilder__Add_Z37302880, GraphBuilder__Sub_Z37302880, GraphBuilder__Constant_5E38073B } from "../Solve/GraphIR.fs.js";
import { contains, isEmpty, ofList as ofList_1 } from "../../ui/fable_modules/fable-library-js.4.29.0/Set.js";
import { toArray as toArray_1, exists } from "../../ui/fable_modules/fable-library-js.4.29.0/Seq.js";
import { toArray } from "../../ui/fable_modules/fable-library-js.4.29.0/Option.js";
import { printf, toConsoleError } from "../../ui/fable_modules/fable-library-js.4.29.0/String.js";
import { rangeDouble } from "../../ui/fable_modules/fable-library-js.4.29.0/Range.js";

export class FramePart extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["FPOrigin", "FPXAxis", "FPYAxis", "FPZAxis"];
    }
}

export function FramePart_$reflection() {
    return union_type("Server.FramePart", [], FramePart, () => [[], [], [], []]);
}

export class FrameGeometry extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["FGPoint", "FGLine"];
    }
}

export function FrameGeometry_$reflection() {
    return union_type("Server.FrameGeometry", [], FrameGeometry, () => [[["x", float64_type], ["y", float64_type]], [["ox", float64_type], ["oy", float64_type], ["dx", float64_type], ["dy", float64_type]]]);
}

export class SketchCompileContext extends Record {
    constructor(SketchOrigin, Frames) {
        super();
        this.SketchOrigin = SketchOrigin;
        this.Frames = Frames;
    }
}

export function SketchCompileContext_$reflection() {
    return record_type("Server.SketchCompileContext", [], SketchCompileContext, () => [["SketchOrigin", RigidTransform_$reflection()], ["Frames", class_type("Microsoft.FSharp.Collections.FSharpMap`2", [string_type, RigidTransform_$reflection()])]]);
}

class SketchCompile_UnionFind {
    constructor() {
        this.parent = (new Map([]));
    }
}

function SketchCompile_UnionFind_$reflection() {
    return class_type("Server.SketchCompile.UnionFind", undefined, SketchCompile_UnionFind);
}

function SketchCompile_UnionFind_$ctor() {
    return new SketchCompile_UnionFind();
}

function SketchCompile_UnionFind__Add_Z721C83C5(_, id) {
    if (!_.parent.has(id)) {
        _.parent.set(id, id);
    }
}

function SketchCompile_UnionFind__Find_Z721C83C5(this$, id) {
    SketchCompile_UnionFind__Add_Z721C83C5(this$, id);
    let p = getItemFromDict(this$.parent, id);
    while (p !== getItemFromDict(this$.parent, p)) {
        p = getItemFromDict(this$.parent, p);
    }
    const root = p;
    let cur = id;
    while (getItemFromDict(this$.parent, cur) !== root) {
        const next = getItemFromDict(this$.parent, cur);
        this$.parent.set(cur, root);
        cur = next;
    }
    return root;
}

function SketchCompile_UnionFind__Union_Z384F8060(this$, a, b) {
    const ra = SketchCompile_UnionFind__Find_Z721C83C5(this$, a);
    const rb = SketchCompile_UnionFind__Find_Z721C83C5(this$, b);
    if (ra !== rb) {
        this$.parent.set(rb, ra);
    }
}

function SketchCompile_coincidentGroups(sketch) {
    const uf = SketchCompile_UnionFind_$ctor();
    const enumerator = getEnumerator(sketch.Entities);
    try {
        while (enumerator["System.Collections.IEnumerator.MoveNext"]()) {
            const entity = enumerator["System.Collections.Generic.IEnumerator`1.get_Current"]();
            if (entity.tag === 0) {
                SketchCompile_UnionFind__Add_Z721C83C5(uf, entity.fields[0]);
            }
        }
    }
    finally {
        disposeSafe(enumerator);
    }
    const enumerator_1 = getEnumerator(sketch.Constraints);
    try {
        while (enumerator_1["System.Collections.IEnumerator.MoveNext"]()) {
            const constraint_ = enumerator_1["System.Collections.Generic.IEnumerator`1.get_Current"]();
            if (constraint_.tag === 1) {
                SketchCompile_UnionFind__Union_Z384F8060(uf, constraint_.fields[0], constraint_.fields[1]);
            }
        }
    }
    finally {
        disposeSafe(enumerator_1);
    }
    return ofList(choose((_arg) => {
        if (_arg.tag === 0) {
            const id_1 = _arg.fields[0];
            return [id_1, SketchCompile_UnionFind__Find_Z721C83C5(uf, id_1)];
        }
        else {
            return undefined;
        }
    }, sketch.Entities), {
        Compare: comparePrimitives,
    });
}

export function SketchCompile_parseFramePart(s) {
    switch (s) {
        case "origin":
            return new FramePart(0, []);
        case "xAxis":
            return new FramePart(1, []);
        case "yAxis":
            return new FramePart(2, []);
        case "zAxis":
            return new FramePart(3, []);
        default:
            return undefined;
    }
}

/**
 * Project a 3D world point into the sketch's local 2D plane.
 * The sketch plane is the frame's XY-plane; Z is discarded.
 */
export function SketchCompile_projectPoint(sketchOrigin, wp) {
    let local;
    let copyOfStruct = RigidTransform__get_Inverse(sketchOrigin);
    local = RigidTransform__Apply_Z2E054BF3(copyOfStruct, wp);
    return [local.X, local.Y];
}

/**
 * Project a 3D world direction into the sketch's local 2D. Returns
 * None if the projected length is below an epsilon (degenerate).
 */
export function SketchCompile_projectDirection(sketchOrigin, wd) {
    let local;
    let copyOfStruct_1 = RigidTransform__get_Inverse(sketchOrigin).Rot;
    local = Quat__Rotate_Z2E054BF3(copyOfStruct_1, wd);
    const len = Math.sqrt((local.X * local.X) + (local.Y * local.Y));
    if (len < 1E-09) {
        return undefined;
    }
    else {
        return [local.X / len, local.Y / len];
    }
}

function SketchCompile_frameGeometry(sketchOrigin, frameT, part) {
    const patternInput = SketchCompile_projectPoint(sketchOrigin, frameT.Trans);
    const oy = patternInput[1];
    const ox = patternInput[0];
    switch (part.tag) {
        case 1:
        case 2:
        case 3: {
            const matchValue = SketchCompile_projectDirection(sketchOrigin, Quat__Rotate_Z2E054BF3(frameT.Rot, (part.tag === 1) ? (new Vec3(1, 0, 0)) : ((part.tag === 2) ? (new Vec3(0, 1, 0)) : ((part.tag === 3) ? (new Vec3(0, 0, 1)) : Vec3_get_Zero()))));
            if (matchValue == null) {
                return undefined;
            }
            else {
                return new FrameGeometry(1, [ox, oy, matchValue[0], matchValue[1]]);
            }
        }
        default:
            return new FrameGeometry(0, [ox, oy]);
    }
}

class SketchCompile_PointRef extends Record {
    constructor(XNode, YNode, XSlot, YSlot) {
        super();
        this.XNode = (XNode | 0);
        this.YNode = (YNode | 0);
        this.XSlot = (XSlot | 0);
        this.YSlot = (YSlot | 0);
    }
}

function SketchCompile_PointRef_$reflection() {
    return record_type("Server.SketchCompile.PointRef", [], SketchCompile_PointRef, () => [["XNode", int32_type], ["YNode", int32_type], ["XSlot", int32_type], ["YSlot", int32_type]]);
}

class SketchCompile_CircleRef extends Record {
    constructor(RadiusNode, RadiusSlot, CenterId) {
        super();
        this.RadiusNode = (RadiusNode | 0);
        this.RadiusSlot = (RadiusSlot | 0);
        this.CenterId = CenterId;
    }
}

function SketchCompile_CircleRef_$reflection() {
    return record_type("Server.SketchCompile.CircleRef", [], SketchCompile_CircleRef, () => [["RadiusNode", int32_type], ["RadiusSlot", int32_type], ["CenterId", string_type]]);
}

class SketchCompile_ArcThroughRef extends Record {
    constructor(TxNode, TyNode, TxSlot, TySlot) {
        super();
        this.TxNode = (TxNode | 0);
        this.TyNode = (TyNode | 0);
        this.TxSlot = (TxSlot | 0);
        this.TySlot = (TySlot | 0);
    }
}

function SketchCompile_ArcThroughRef_$reflection() {
    return record_type("Server.SketchCompile.ArcThroughRef", [], SketchCompile_ArcThroughRef, () => [["TxNode", int32_type], ["TyNode", int32_type], ["TxSlot", int32_type], ["TySlot", int32_type]]);
}

class SketchCompile_EntityTables extends Record {
    constructor(Points, Circles, ArcThrough) {
        super();
        this.Points = Points;
        this.Circles = Circles;
        this.ArcThrough = ArcThrough;
    }
}

function SketchCompile_EntityTables_$reflection() {
    return record_type("Server.SketchCompile.EntityTables", [], SketchCompile_EntityTables, () => [["Points", class_type("System.Collections.Generic.Dictionary`2", [string_type, SketchCompile_PointRef_$reflection()])], ["Circles", class_type("System.Collections.Generic.Dictionary`2", [string_type, SketchCompile_CircleRef_$reflection()])], ["ArcThrough", class_type("System.Collections.Generic.Dictionary`2", [string_type, SketchCompile_ArcThroughRef_$reflection()])]]);
}

function SketchCompile_constant(b, v) {
    return GraphBuilder__Constant_5E38073B(b, v);
}

function SketchCompile_vecSub(b, a, c) {
    return [GraphBuilder__Sub_Z37302880(b, c.XNode, a.XNode), GraphBuilder__Sub_Z37302880(b, c.YNode, a.YNode)];
}

function SketchCompile_lenSq(b, dx, dy) {
    return GraphBuilder__Add_Z37302880(b, GraphBuilder__Mul_Z37302880(b, dx, dx), GraphBuilder__Mul_Z37302880(b, dy, dy));
}

function SketchCompile_length(b, dx, dy) {
    return GraphBuilder__Sqrt_Z524259A4(b, SketchCompile_lenSq(b, dx, dy));
}

function SketchCompile_dot(b, ax, ay, bx, by) {
    return GraphBuilder__Add_Z37302880(b, GraphBuilder__Mul_Z37302880(b, ax, bx), GraphBuilder__Mul_Z37302880(b, ay, by));
}

function SketchCompile_crossZ(b, ax, ay, bx, by) {
    return GraphBuilder__Sub_Z37302880(b, GraphBuilder__Mul_Z37302880(b, ax, by), GraphBuilder__Mul_Z37302880(b, ay, bx));
}

export function SketchCompile_compile(sketch, ctx) {
    let ft_2, aS_9, aE_9, ft_4, aS_11, aE_11, ft_6, aS_13, aE_13;
    const b = GraphBuilder_$ctor();
    const tables = new SketchCompile_EntityTables(new Map([]), new Map([]), new Map([]));
    const fixedSlots = new Set([]);
    const fixedInputSlots = new Set([]);
    const enumerator = getEnumerator(sketch.Entities);
    try {
        while (enumerator["System.Collections.IEnumerator.MoveNext"]()) {
            const e = enumerator["System.Collections.Generic.IEnumerator`1.get_Current"]();
            switch (e.tag) {
                case 2: {
                    const rSlot = GraphBuilder__get_ParamCount(b) | 0;
                    const rNode = GraphBuilder__Param_5E38073B(b, e.fields[2]) | 0;
                    tables.Circles.set(e.fields[0], new SketchCompile_CircleRef(rNode, rSlot, e.fields[1]));
                    break;
                }
                case 3: {
                    if (e.fields[3].tag === 1) {
                        const xSlot_1 = GraphBuilder__get_ParamCount(b) | 0;
                        const xNode_1 = GraphBuilder__Param_5E38073B(b, e.fields[3].fields[0].X) | 0;
                        const ySlot_1 = GraphBuilder__get_ParamCount(b) | 0;
                        const yNode_1 = GraphBuilder__Param_5E38073B(b, e.fields[3].fields[0].Y) | 0;
                        tables.ArcThrough.set(e.fields[0], new SketchCompile_ArcThroughRef(xNode_1, yNode_1, xSlot_1, ySlot_1));
                    }
                    break;
                }
                case 1: {
                    break;
                }
                default: {
                    const xSlot = GraphBuilder__get_ParamCount(b) | 0;
                    const xNode = GraphBuilder__Param_5E38073B(b, e.fields[1]) | 0;
                    const ySlot = GraphBuilder__get_ParamCount(b) | 0;
                    const yNode = GraphBuilder__Param_5E38073B(b, e.fields[2]) | 0;
                    tables.Points.set(e.fields[0], new SketchCompile_PointRef(xNode, yNode, xSlot, ySlot));
                }
            }
        }
    }
    finally {
        disposeSafe(enumerator);
    }
    const outputs = [];
    let skipped = 0;
    const tryPoint = (id_3) => {
        let matchValue;
        let outArg = defaultOf();
        matchValue = [tryGetValue(tables.Points, id_3, new FSharpRef(() => outArg, (v) => {
            outArg = v;
        })), outArg];
        if (matchValue[0]) {
            return matchValue[1];
        }
        else {
            return undefined;
        }
    };
    const tryCircle = (id_4) => {
        let matchValue_1;
        let outArg_1 = defaultOf();
        matchValue_1 = [tryGetValue(tables.Circles, id_4, new FSharpRef(() => outArg_1, (v_1) => {
            outArg_1 = v_1;
        })), outArg_1];
        if (matchValue_1[0]) {
            return matchValue_1[1];
        }
        else {
            return undefined;
        }
    };
    const coincidentGroups = SketchCompile_coincidentGroups(sketch);
    const tryDiameterEntity = (id_5) => {
        const matchValue_2 = tryCircle(id_5);
        if (matchValue_2 == null) {
            const matchValue_3 = tryFind((_arg) => {
                let matchResult, entityId_1;
                if (_arg.tag === 3) {
                    if (_arg.fields[3].tag === 0) {
                        if (_arg.fields[0] === id_5) {
                            matchResult = 0;
                            entityId_1 = _arg.fields[0];
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
                    case 0:
                        return true;
                    default:
                        return false;
                }
            }, sketch.Entities);
            let matchResult_1, centerId_1, startId;
            if (matchValue_3 != null) {
                if (matchValue_3.tag === 3) {
                    if (matchValue_3.fields[3].tag === 0) {
                        matchResult_1 = 0;
                        centerId_1 = matchValue_3.fields[3].fields[0];
                        startId = matchValue_3.fields[1];
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
                    const matchValue_4 = tryPoint(startId);
                    const matchValue_5 = tryPoint(centerId_1);
                    let matchResult_2, centerP, startP;
                    if (matchValue_4 != null) {
                        if (matchValue_5 != null) {
                            matchResult_2 = 0;
                            centerP = matchValue_5;
                            startP = matchValue_4;
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
                            const patternInput = SketchCompile_vecSub(b, centerP, startP);
                            return SketchCompile_length(b, patternInput[0], patternInput[1]);
                        }
                        default:
                            return undefined;
                    }
                }
                default:
                    return undefined;
            }
        }
        else {
            return matchValue_2.RadiusNode;
        }
    };
    const emitDiff = (a, cNode) => {
        void (outputs.push(GraphBuilder__Sub_Z37302880(b, a, cNode)));
    };
    const emitDiffConst = (a_1, k) => {
        void (outputs.push(GraphBuilder__Sub_Z37302880(b, a_1, SketchCompile_constant(b, k))));
    };
    const fixedParam = (value) => {
        const slot = GraphBuilder__get_ParamCount(b) | 0;
        const node = GraphBuilder__Param_5E38073B(b, value) | 0;
        addToSet(slot, fixedInputSlots);
        return node | 0;
    };
    const enumerator_1 = getEnumerator(sketch.Entities);
    try {
        while (enumerator_1["System.Collections.IEnumerator.MoveNext"]()) {
            const e_1 = enumerator_1["System.Collections.Generic.IEnumerator`1.get_Current"]();
            let matchResult_3, centerId_2, endId_1, startId_2;
            if (e_1.tag === 3) {
                if (e_1.fields[3].tag === 0) {
                    matchResult_3 = 0;
                    centerId_2 = e_1.fields[3].fields[0];
                    endId_1 = e_1.fields[2];
                    startId_2 = e_1.fields[1];
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
                    const matchValue_8 = tryPoint(startId_2);
                    const matchValue_9 = tryPoint(endId_1);
                    const matchValue_10 = tryPoint(centerId_2);
                    let matchResult_4, centerP_1, endP, startP_1;
                    if (matchValue_8 != null) {
                        if (matchValue_9 != null) {
                            if (matchValue_10 != null) {
                                matchResult_4 = 0;
                                centerP_1 = matchValue_10;
                                endP = matchValue_9;
                                startP_1 = matchValue_8;
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
                            const patternInput_1 = SketchCompile_vecSub(b, centerP_1, startP_1);
                            const patternInput_2 = SketchCompile_vecSub(b, centerP_1, endP);
                            void (outputs.push(GraphBuilder__Sub_Z37302880(b, SketchCompile_length(b, patternInput_1[0], patternInput_1[1]), SketchCompile_length(b, patternInput_2[0], patternInput_2[1]))));
                            break;
                        }
                        case 1: {
                            skipped = ((skipped + 1) | 0);
                            break;
                        }
                    }
                    break;
                }
            }
        }
    }
    finally {
        disposeSafe(enumerator_1);
    }
    const enumerator_2 = getEnumerator(sketch.Constraints);
    try {
        while (enumerator_2["System.Collections.IEnumerator.MoveNext"]()) {
            const c_1 = enumerator_2["System.Collections.Generic.IEnumerator`1.get_Current"]();
            switch (c_1.tag) {
                case 1: {
                    const matchValue_13 = tryPoint(c_1.fields[0]);
                    const matchValue_14 = tryPoint(c_1.fields[1]);
                    let matchResult_5, pa, pb;
                    if (matchValue_13 != null) {
                        if (matchValue_14 != null) {
                            matchResult_5 = 0;
                            pa = matchValue_13;
                            pb = matchValue_14;
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
                            void (outputs.push(GraphBuilder__Sub_Z37302880(b, pa.XNode, pb.XNode)));
                            void (outputs.push(GraphBuilder__Sub_Z37302880(b, pa.YNode, pb.YNode)));
                            break;
                        }
                        case 1: {
                            skipped = ((skipped + 1) | 0);
                            break;
                        }
                    }
                    break;
                }
                case 3: {
                    const matchValue_16 = tryPoint(c_1.fields[2]);
                    const matchValue_17 = tryPoint(c_1.fields[3]);
                    let matchResult_6, pa_1, pb_1;
                    if (matchValue_16 != null) {
                        if (matchValue_17 != null) {
                            matchResult_6 = 0;
                            pa_1 = matchValue_16;
                            pb_1 = matchValue_17;
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
                            void (outputs.push(GraphBuilder__Sub_Z37302880(b, pa_1.XNode, pb_1.XNode)));
                            void (outputs.push(GraphBuilder__Sub_Z37302880(b, pa_1.YNode, pb_1.YNode)));
                            break;
                        }
                        case 1: {
                            skipped = ((skipped + 1) | 0);
                            break;
                        }
                    }
                    break;
                }
                case 4: {
                    const matchValue_19 = tryPoint(c_1.fields[0]);
                    const matchValue_20 = tryPoint(c_1.fields[1]);
                    let matchResult_7, pa_2, pb_2;
                    if (matchValue_19 != null) {
                        if (matchValue_20 != null) {
                            matchResult_7 = 0;
                            pa_2 = matchValue_19;
                            pb_2 = matchValue_20;
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
                            void (outputs.push(GraphBuilder__Sub_Z37302880(b, pa_2.YNode, pb_2.YNode)));
                            break;
                        }
                        case 1: {
                            skipped = ((skipped + 1) | 0);
                            break;
                        }
                    }
                    break;
                }
                case 5: {
                    const matchValue_22 = tryPoint(c_1.fields[0]);
                    const matchValue_23 = tryPoint(c_1.fields[1]);
                    let matchResult_8, pa_3, pb_3;
                    if (matchValue_22 != null) {
                        if (matchValue_23 != null) {
                            matchResult_8 = 0;
                            pa_3 = matchValue_22;
                            pb_3 = matchValue_23;
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
                            void (outputs.push(GraphBuilder__Sub_Z37302880(b, pa_3.XNode, pb_3.XNode)));
                            break;
                        }
                        case 1: {
                            skipped = ((skipped + 1) | 0);
                            break;
                        }
                    }
                    break;
                }
                case 6: {
                    const matchValue_25 = tryPoint(c_1.fields[0]);
                    const matchValue_26 = tryPoint(c_1.fields[1]);
                    let matchResult_9, pa_4, pb_4;
                    if (matchValue_25 != null) {
                        if (matchValue_26 != null) {
                            matchResult_9 = 0;
                            pa_4 = matchValue_25;
                            pb_4 = matchValue_26;
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
                            const patternInput_3 = SketchCompile_vecSub(b, pa_4, pb_4);
                            emitDiff(SketchCompile_length(b, patternInput_3[0], patternInput_3[1]), fixedParam(c_1.fields[2]));
                            break;
                        }
                        case 1: {
                            skipped = ((skipped + 1) | 0);
                            break;
                        }
                    }
                    break;
                }
                case 8: {
                    const matchValue_28 = tryPoint(c_1.fields[0]);
                    const matchValue_29 = tryPoint(c_1.fields[1]);
                    const matchValue_30 = tryPoint(c_1.fields[2]);
                    const matchValue_31 = tryPoint(c_1.fields[3]);
                    let matchResult_10, aE, aS, bE, bS;
                    if (matchValue_28 != null) {
                        if (matchValue_29 != null) {
                            if (matchValue_30 != null) {
                                if (matchValue_31 != null) {
                                    matchResult_10 = 0;
                                    aE = matchValue_29;
                                    aS = matchValue_28;
                                    bE = matchValue_31;
                                    bS = matchValue_30;
                                }
                                else {
                                    matchResult_10 = 1;
                                }
                            }
                            else {
                                matchResult_10 = 1;
                            }
                        }
                        else {
                            matchResult_10 = 1;
                        }
                    }
                    else {
                        matchResult_10 = 1;
                    }
                    switch (matchResult_10) {
                        case 0: {
                            const patternInput_4 = SketchCompile_vecSub(b, aS, aE);
                            const patternInput_5 = SketchCompile_vecSub(b, bS, bE);
                            void (outputs.push(GraphBuilder__Sub_Z37302880(b, SketchCompile_lenSq(b, patternInput_4[0], patternInput_4[1]), SketchCompile_lenSq(b, patternInput_5[0], patternInput_5[1]))));
                            break;
                        }
                        case 1: {
                            skipped = ((skipped + 1) | 0);
                            break;
                        }
                    }
                    break;
                }
                case 9: {
                    const matchValue_33 = tryDiameterEntity(c_1.fields[0]);
                    const matchValue_34 = tryDiameterEntity(c_1.fields[1]);
                    let matchResult_11, rA, rB;
                    if (matchValue_33 != null) {
                        if (matchValue_34 != null) {
                            matchResult_11 = 0;
                            rA = matchValue_33;
                            rB = matchValue_34;
                        }
                        else {
                            matchResult_11 = 1;
                        }
                    }
                    else {
                        matchResult_11 = 1;
                    }
                    switch (matchResult_11) {
                        case 0: {
                            void (outputs.push(GraphBuilder__Sub_Z37302880(b, rA, rB)));
                            break;
                        }
                        case 1: {
                            skipped = ((skipped + 1) | 0);
                            break;
                        }
                    }
                    break;
                }
                case 10: {
                    const matchValue_36 = tryPoint(c_1.fields[0]);
                    const matchValue_37 = tryPoint(c_1.fields[2]);
                    const matchValue_38 = tryPoint(c_1.fields[3]);
                    let matchResult_12, aE_1, aS_1, p_2;
                    if (matchValue_36 != null) {
                        if (matchValue_37 != null) {
                            if (matchValue_38 != null) {
                                matchResult_12 = 0;
                                aE_1 = matchValue_38;
                                aS_1 = matchValue_37;
                                p_2 = matchValue_36;
                            }
                            else {
                                matchResult_12 = 1;
                            }
                        }
                        else {
                            matchResult_12 = 1;
                        }
                    }
                    else {
                        matchResult_12 = 1;
                    }
                    switch (matchResult_12) {
                        case 0: {
                            const two = SketchCompile_constant(b, 2) | 0;
                            const sumX = GraphBuilder__Add_Z37302880(b, aS_1.XNode, aE_1.XNode) | 0;
                            const sumY = GraphBuilder__Add_Z37302880(b, aS_1.YNode, aE_1.YNode) | 0;
                            void (outputs.push(GraphBuilder__Sub_Z37302880(b, GraphBuilder__Mul_Z37302880(b, p_2.XNode, two), sumX)));
                            void (outputs.push(GraphBuilder__Sub_Z37302880(b, GraphBuilder__Mul_Z37302880(b, p_2.YNode, two), sumY)));
                            break;
                        }
                        case 1: {
                            skipped = ((skipped + 1) | 0);
                            break;
                        }
                    }
                    break;
                }
                case 11: {
                    const matchValue_40 = tryPoint(c_1.fields[0]);
                    const matchValue_41 = tryPoint(c_1.fields[1]);
                    const matchValue_42 = tryPoint(c_1.fields[2]);
                    const matchValue_43 = tryPoint(c_1.fields[3]);
                    let matchResult_13, aE_2, aS_2, bE_1, bS_1;
                    if (matchValue_40 != null) {
                        if (matchValue_41 != null) {
                            if (matchValue_42 != null) {
                                if (matchValue_43 != null) {
                                    matchResult_13 = 0;
                                    aE_2 = matchValue_41;
                                    aS_2 = matchValue_40;
                                    bE_1 = matchValue_43;
                                    bS_1 = matchValue_42;
                                }
                                else {
                                    matchResult_13 = 1;
                                }
                            }
                            else {
                                matchResult_13 = 1;
                            }
                        }
                        else {
                            matchResult_13 = 1;
                        }
                    }
                    else {
                        matchResult_13 = 1;
                    }
                    switch (matchResult_13) {
                        case 0: {
                            const patternInput_6 = SketchCompile_vecSub(b, aS_2, aE_2);
                            const patternInput_7 = SketchCompile_vecSub(b, bS_1, bE_1);
                            void (outputs.push(SketchCompile_crossZ(b, patternInput_6[0], patternInput_6[1], patternInput_7[0], patternInput_7[1])));
                            break;
                        }
                        case 1: {
                            skipped = ((skipped + 1) | 0);
                            break;
                        }
                    }
                    break;
                }
                case 13: {
                    const matchValue_45 = tryPoint(c_1.fields[0]);
                    const matchValue_46 = tryPoint(c_1.fields[1]);
                    const matchValue_47 = tryPoint(c_1.fields[2]);
                    const matchValue_48 = tryPoint(c_1.fields[3]);
                    let matchResult_14, aE_3, aS_3, bE_2, bS_2;
                    if (matchValue_45 != null) {
                        if (matchValue_46 != null) {
                            if (matchValue_47 != null) {
                                if (matchValue_48 != null) {
                                    matchResult_14 = 0;
                                    aE_3 = matchValue_46;
                                    aS_3 = matchValue_45;
                                    bE_2 = matchValue_48;
                                    bS_2 = matchValue_47;
                                }
                                else {
                                    matchResult_14 = 1;
                                }
                            }
                            else {
                                matchResult_14 = 1;
                            }
                        }
                        else {
                            matchResult_14 = 1;
                        }
                    }
                    else {
                        matchResult_14 = 1;
                    }
                    switch (matchResult_14) {
                        case 0: {
                            const patternInput_8 = SketchCompile_vecSub(b, aS_3, aE_3);
                            const patternInput_9 = SketchCompile_vecSub(b, bS_2, bE_2);
                            void (outputs.push(SketchCompile_dot(b, patternInput_8[0], patternInput_8[1], patternInput_9[0], patternInput_9[1])));
                            break;
                        }
                        case 1: {
                            skipped = ((skipped + 1) | 0);
                            break;
                        }
                    }
                    break;
                }
                case 15: {
                    const curveId_1 = c_1.fields[3];
                    const asId_3 = c_1.fields[0];
                    const aeId_3 = c_1.fields[1];
                    const matchValue_50 = tryPoint(asId_3);
                    const matchValue_51 = tryPoint(aeId_3);
                    const matchValue_52 = tryPoint(c_1.fields[2]);
                    let matchResult_15, aE_4, aS_4, c_2;
                    if (matchValue_50 != null) {
                        if (matchValue_51 != null) {
                            if (matchValue_52 != null) {
                                matchResult_15 = 0;
                                aE_4 = matchValue_51;
                                aS_4 = matchValue_50;
                                c_2 = matchValue_52;
                            }
                            else {
                                matchResult_15 = 1;
                            }
                        }
                        else {
                            matchResult_15 = 1;
                        }
                    }
                    else {
                        matchResult_15 = 1;
                    }
                    switch (matchResult_15) {
                        case 0: {
                            const patternInput_10 = SketchCompile_vecSub(b, aS_4, aE_4);
                            const dyL = patternInput_10[1] | 0;
                            const dxL = patternInput_10[0] | 0;
                            let matchValue_54;
                            const groupOf = (id_6) => tryFind_1(id_6, coincidentGroups);
                            const lineGroups = ofList_1(choose(groupOf, ofArray([asId_3, aeId_3])), {
                                Compare: comparePrimitives,
                            });
                            if (isEmpty(lineGroups)) {
                                matchValue_54 = undefined;
                            }
                            else {
                                const matchValue_7 = tryFind((_arg_1) => {
                                    let matchResult_16, entityId_3;
                                    if (_arg_1.tag === 3) {
                                        if (_arg_1.fields[3].tag === 0) {
                                            if (_arg_1.fields[0] === curveId_1) {
                                                matchResult_16 = 0;
                                                entityId_3 = _arg_1.fields[0];
                                            }
                                            else {
                                                matchResult_16 = 1;
                                            }
                                        }
                                        else {
                                            matchResult_16 = 1;
                                        }
                                    }
                                    else {
                                        matchResult_16 = 1;
                                    }
                                    switch (matchResult_16) {
                                        case 0:
                                            return true;
                                        default:
                                            return false;
                                    }
                                }, sketch.Entities);
                                let matchResult_17, endId, startId_1;
                                if (matchValue_7 != null) {
                                    if (matchValue_7.tag === 3) {
                                        if (matchValue_7.fields[3].tag === 0) {
                                            matchResult_17 = 0;
                                            endId = matchValue_7.fields[2];
                                            startId_1 = matchValue_7.fields[1];
                                        }
                                        else {
                                            matchResult_17 = 1;
                                        }
                                    }
                                    else {
                                        matchResult_17 = 1;
                                    }
                                }
                                else {
                                    matchResult_17 = 1;
                                }
                                switch (matchResult_17) {
                                    case 0: {
                                        matchValue_54 = (exists((group) => contains(group, lineGroups), toArray(groupOf(startId_1))) ? startId_1 : (exists((group_1) => contains(group_1, lineGroups), toArray(groupOf(endId))) ? endId : undefined));
                                        break;
                                    }
                                    default:
                                        matchValue_54 = undefined;
                                }
                            }
                            if (matchValue_54 == null) {
                                const cross = SketchCompile_crossZ(b, dxL, dyL, GraphBuilder__Sub_Z37302880(b, c_2.XNode, aS_4.XNode), GraphBuilder__Sub_Z37302880(b, c_2.YNode, aS_4.YNode)) | 0;
                                const lineLen = SketchCompile_length(b, dxL, dyL) | 0;
                                let radiusNode;
                                const matchValue_56 = tryDiameterEntity(curveId_1);
                                radiusNode = ((matchValue_56 == null) ? SketchCompile_constant(b, c_1.fields[5]) : matchValue_56);
                                void (outputs.push(GraphBuilder__Sub_Z37302880(b, cross, GraphBuilder__Mul_Z37302880(b, radiusNode, lineLen))));
                            }
                            else {
                                const matchValue_55 = tryPoint(matchValue_54);
                                if (matchValue_55 != null) {
                                    const contact = matchValue_55;
                                    const rcx = GraphBuilder__Sub_Z37302880(b, contact.XNode, c_2.XNode) | 0;
                                    const rcy = GraphBuilder__Sub_Z37302880(b, contact.YNode, c_2.YNode) | 0;
                                    void (outputs.push(SketchCompile_dot(b, rcx, rcy, dxL, dyL)));
                                }
                                else {
                                    skipped = ((skipped + 1) | 0);
                                }
                            }
                            break;
                        }
                        case 1: {
                            skipped = ((skipped + 1) | 0);
                            break;
                        }
                    }
                    break;
                }
                case 17: {
                    const matchValue_57 = tryDiameterEntity(c_1.fields[0]);
                    if (matchValue_57 != null) {
                        emitDiff(GraphBuilder__Mul_Z37302880(b, matchValue_57, SketchCompile_constant(b, 2)), fixedParam(c_1.fields[2]));
                    }
                    else {
                        skipped = ((skipped + 1) | 0);
                    }
                    break;
                }
                case 18: {
                    const matchValue_58 = tryPoint(c_1.fields[0]);
                    const matchValue_59 = tryPoint(c_1.fields[1]);
                    const matchValue_60 = tryPoint(c_1.fields[2]);
                    let matchResult_18, aE_5, aS_5, bS_3;
                    if (matchValue_58 != null) {
                        if (matchValue_59 != null) {
                            if (matchValue_60 != null) {
                                matchResult_18 = 0;
                                aE_5 = matchValue_59;
                                aS_5 = matchValue_58;
                                bS_3 = matchValue_60;
                            }
                            else {
                                matchResult_18 = 1;
                            }
                        }
                        else {
                            matchResult_18 = 1;
                        }
                    }
                    else {
                        matchResult_18 = 1;
                    }
                    switch (matchResult_18) {
                        case 0: {
                            const patternInput_11 = SketchCompile_vecSub(b, aS_5, aE_5);
                            const dyL_1 = patternInput_11[1] | 0;
                            const dxL_1 = patternInput_11[0] | 0;
                            const cross_1 = SketchCompile_crossZ(b, dxL_1, dyL_1, GraphBuilder__Sub_Z37302880(b, bS_3.XNode, aS_5.XNode), GraphBuilder__Sub_Z37302880(b, bS_3.YNode, aS_5.YNode)) | 0;
                            const lineLen_1 = SketchCompile_length(b, dxL_1, dyL_1) | 0;
                            void (outputs.push(GraphBuilder__Sub_Z37302880(b, cross_1, GraphBuilder__Mul_Z37302880(b, fixedParam(c_1.fields[6]), lineLen_1))));
                            break;
                        }
                        case 1: {
                            skipped = ((skipped + 1) | 0);
                            break;
                        }
                    }
                    break;
                }
                case 20: {
                    const matchValue_62 = tryPoint(c_1.fields[0]);
                    const matchValue_63 = tryPoint(c_1.fields[2]);
                    const matchValue_64 = tryPoint(c_1.fields[3]);
                    let matchResult_19, aE_6, aS_6, p_3;
                    if (matchValue_62 != null) {
                        if (matchValue_63 != null) {
                            if (matchValue_64 != null) {
                                matchResult_19 = 0;
                                aE_6 = matchValue_64;
                                aS_6 = matchValue_63;
                                p_3 = matchValue_62;
                            }
                            else {
                                matchResult_19 = 1;
                            }
                        }
                        else {
                            matchResult_19 = 1;
                        }
                    }
                    else {
                        matchResult_19 = 1;
                    }
                    switch (matchResult_19) {
                        case 0: {
                            const patternInput_12 = SketchCompile_vecSub(b, aS_6, aE_6);
                            const dyL_2 = patternInput_12[1] | 0;
                            const dxL_2 = patternInput_12[0] | 0;
                            const cross_2 = SketchCompile_crossZ(b, dxL_2, dyL_2, GraphBuilder__Sub_Z37302880(b, p_3.XNode, aS_6.XNode), GraphBuilder__Sub_Z37302880(b, p_3.YNode, aS_6.YNode)) | 0;
                            const lineLen_2 = SketchCompile_length(b, dxL_2, dyL_2) | 0;
                            void (outputs.push(GraphBuilder__Sub_Z37302880(b, cross_2, GraphBuilder__Mul_Z37302880(b, fixedParam(c_1.fields[4]), lineLen_2))));
                            break;
                        }
                        case 1: {
                            skipped = ((skipped + 1) | 0);
                            break;
                        }
                    }
                    break;
                }
                case 21: {
                    const matchValue_66 = tryPoint(c_1.fields[0]);
                    const matchValue_67 = tryPoint(c_1.fields[2]);
                    const matchValue_68 = tryCircle(c_1.fields[1]);
                    let matchResult_20, c_3, cr, p_4;
                    if (matchValue_66 != null) {
                        if (matchValue_67 != null) {
                            if (matchValue_68 != null) {
                                matchResult_20 = 0;
                                c_3 = matchValue_67;
                                cr = matchValue_68;
                                p_4 = matchValue_66;
                            }
                            else {
                                matchResult_20 = 1;
                            }
                        }
                        else {
                            matchResult_20 = 1;
                        }
                    }
                    else {
                        matchResult_20 = 1;
                    }
                    switch (matchResult_20) {
                        case 0: {
                            const dist = SketchCompile_length(b, GraphBuilder__Sub_Z37302880(b, p_4.XNode, c_3.XNode), GraphBuilder__Sub_Z37302880(b, p_4.YNode, c_3.YNode)) | 0;
                            const target = GraphBuilder__Add_Z37302880(b, cr.RadiusNode, fixedParam(c_1.fields[3])) | 0;
                            void (outputs.push(GraphBuilder__Sub_Z37302880(b, dist, target)));
                            break;
                        }
                        case 1: {
                            skipped = ((skipped + 1) | 0);
                            break;
                        }
                    }
                    break;
                }
                case 22: {
                    const matchValue_70 = tryPoint(c_1.fields[1]);
                    const matchValue_71 = tryPoint(c_1.fields[2]);
                    const matchValue_72 = tryPoint(c_1.fields[4]);
                    const matchValue_73 = tryCircle(c_1.fields[3]);
                    let matchResult_21, aE_7, aS_7, c_4, cr_1;
                    if (matchValue_70 != null) {
                        if (matchValue_71 != null) {
                            if (matchValue_72 != null) {
                                if (matchValue_73 != null) {
                                    matchResult_21 = 0;
                                    aE_7 = matchValue_71;
                                    aS_7 = matchValue_70;
                                    c_4 = matchValue_72;
                                    cr_1 = matchValue_73;
                                }
                                else {
                                    matchResult_21 = 1;
                                }
                            }
                            else {
                                matchResult_21 = 1;
                            }
                        }
                        else {
                            matchResult_21 = 1;
                        }
                    }
                    else {
                        matchResult_21 = 1;
                    }
                    switch (matchResult_21) {
                        case 0: {
                            const patternInput_13 = SketchCompile_vecSub(b, aS_7, aE_7);
                            const dyL_3 = patternInput_13[1] | 0;
                            const dxL_3 = patternInput_13[0] | 0;
                            const cross_3 = SketchCompile_crossZ(b, dxL_3, dyL_3, GraphBuilder__Sub_Z37302880(b, c_4.XNode, aS_7.XNode), GraphBuilder__Sub_Z37302880(b, c_4.YNode, aS_7.YNode)) | 0;
                            const lineLen_3 = SketchCompile_length(b, dxL_3, dyL_3) | 0;
                            const target_1 = GraphBuilder__Add_Z37302880(b, cr_1.RadiusNode, fixedParam(c_1.fields[5])) | 0;
                            void (outputs.push(GraphBuilder__Sub_Z37302880(b, cross_3, GraphBuilder__Mul_Z37302880(b, target_1, lineLen_3))));
                            break;
                        }
                        case 1: {
                            skipped = ((skipped + 1) | 0);
                            break;
                        }
                    }
                    break;
                }
                case 23: {
                    const distance_4 = c_1.fields[4];
                    const matchValue_75 = tryPoint(c_1.fields[1]);
                    const matchValue_76 = tryPoint(c_1.fields[3]);
                    const matchValue_77 = tryCircle(c_1.fields[0]);
                    const matchValue_78 = tryCircle(c_1.fields[2]);
                    let matchResult_22, crA, crB, pa_5, pb_5;
                    if (matchValue_75 != null) {
                        if (matchValue_76 != null) {
                            if (matchValue_77 != null) {
                                if (matchValue_78 != null) {
                                    matchResult_22 = 0;
                                    crA = matchValue_77;
                                    crB = matchValue_78;
                                    pa_5 = matchValue_75;
                                    pb_5 = matchValue_76;
                                }
                                else {
                                    matchResult_22 = 1;
                                }
                            }
                            else {
                                matchResult_22 = 1;
                            }
                        }
                        else {
                            matchResult_22 = 1;
                        }
                    }
                    else {
                        matchResult_22 = 1;
                    }
                    switch (matchResult_22) {
                        case 0: {
                            const patternInput_14 = SketchCompile_vecSub(b, pa_5, pb_5);
                            const centerDist = SketchCompile_length(b, patternInput_14[0], patternInput_14[1]) | 0;
                            const target_2 = (c_1.fields[5] ? GraphBuilder__Sub_Z37302880(b, GraphBuilder__Sub_Z37302880(b, crA.RadiusNode, crB.RadiusNode), fixedParam(distance_4)) : GraphBuilder__Add_Z37302880(b, GraphBuilder__Add_Z37302880(b, crA.RadiusNode, crB.RadiusNode), fixedParam(distance_4))) | 0;
                            void (outputs.push(GraphBuilder__Sub_Z37302880(b, centerDist, target_2)));
                            break;
                        }
                        case 1: {
                            skipped = ((skipped + 1) | 0);
                            break;
                        }
                    }
                    break;
                }
                case 24: {
                    const matchValue_80 = tryPoint(c_1.fields[0]);
                    const matchValue_81 = tryPoint(c_1.fields[1]);
                    const matchValue_82 = tryPoint(c_1.fields[2]);
                    const matchValue_83 = tryPoint(c_1.fields[3]);
                    let matchResult_23, aE_8, aS_8, bE_3, bS_4;
                    if (matchValue_80 != null) {
                        if (matchValue_81 != null) {
                            if (matchValue_82 != null) {
                                if (matchValue_83 != null) {
                                    matchResult_23 = 0;
                                    aE_8 = matchValue_81;
                                    aS_8 = matchValue_80;
                                    bE_3 = matchValue_83;
                                    bS_4 = matchValue_82;
                                }
                                else {
                                    matchResult_23 = 1;
                                }
                            }
                            else {
                                matchResult_23 = 1;
                            }
                        }
                        else {
                            matchResult_23 = 1;
                        }
                    }
                    else {
                        matchResult_23 = 1;
                    }
                    switch (matchResult_23) {
                        case 0: {
                            const sign = (r_1) => {
                                if (r_1) {
                                    return -1;
                                }
                                else {
                                    return 1;
                                }
                            };
                            const sA = SketchCompile_constant(b, sign(c_1.fields[7])) | 0;
                            const sB = SketchCompile_constant(b, sign(c_1.fields[8])) | 0;
                            const patternInput_15 = SketchCompile_vecSub(b, aS_8, aE_8);
                            const patternInput_16 = SketchCompile_vecSub(b, bS_4, bE_3);
                            const dxA_3 = GraphBuilder__Mul_Z37302880(b, sA, patternInput_15[0]) | 0;
                            const dyA_3 = GraphBuilder__Mul_Z37302880(b, sA, patternInput_15[1]) | 0;
                            const dxB_3 = GraphBuilder__Mul_Z37302880(b, sB, patternInput_16[0]) | 0;
                            const dyB_3 = GraphBuilder__Mul_Z37302880(b, sB, patternInput_16[1]) | 0;
                            const angle = GraphBuilder__Atan2_Z37302880(b, SketchCompile_crossZ(b, dxA_3, dyA_3, dxB_3, dyB_3), SketchCompile_dot(b, dxA_3, dyA_3, dxB_3, dyB_3)) | 0;
                            emitDiff(c_1.fields[9] ? angle : GraphBuilder__Neg_Z524259A4(b, angle), fixedParam(c_1.fields[6]));
                            break;
                        }
                        case 1: {
                            skipped = ((skipped + 1) | 0);
                            break;
                        }
                    }
                    break;
                }
                case 2: {
                    const matchValue_85 = tryPoint(c_1.fields[0]);
                    const matchValue_86 = SketchCompile_parseFramePart(c_1.fields[2]);
                    const matchValue_87 = tryFind_1(c_1.fields[1], ctx.Frames);
                    let matchResult_24, fp, ft, p_5;
                    if (matchValue_85 != null) {
                        if (matchValue_86 != null) {
                            if (matchValue_87 != null) {
                                matchResult_24 = 0;
                                fp = matchValue_86;
                                ft = matchValue_87;
                                p_5 = matchValue_85;
                            }
                            else {
                                matchResult_24 = 1;
                            }
                        }
                        else {
                            matchResult_24 = 1;
                        }
                    }
                    else {
                        matchResult_24 = 1;
                    }
                    switch (matchResult_24) {
                        case 0: {
                            const matchValue_89 = SketchCompile_frameGeometry(ctx.SketchOrigin, ft, fp);
                            if (matchValue_89 == null) {
                                skipped = ((skipped + 1) | 0);
                            }
                            else if (matchValue_89.tag === 1) {
                                const dx_4 = matchValue_89.fields[2];
                                const dy_4 = matchValue_89.fields[3];
                                const ox = matchValue_89.fields[0];
                                const oy = matchValue_89.fields[1];
                                const dpx = GraphBuilder__Sub_Z37302880(b, p_5.XNode, SketchCompile_constant(b, ox)) | 0;
                                const dpy = GraphBuilder__Sub_Z37302880(b, p_5.YNode, SketchCompile_constant(b, oy)) | 0;
                                void (outputs.push(SketchCompile_crossZ(b, SketchCompile_constant(b, dx_4), SketchCompile_constant(b, dy_4), dpx, dpy)));
                            }
                            else {
                                const fx = matchValue_89.fields[0];
                                const fy = matchValue_89.fields[1];
                                emitDiffConst(p_5.XNode, fx);
                                emitDiffConst(p_5.YNode, fy);
                            }
                            break;
                        }
                        case 1: {
                            skipped = ((skipped + 1) | 0);
                            break;
                        }
                    }
                    break;
                }
                case 7: {
                    const distance_5 = c_1.fields[3];
                    const matchValue_90 = tryPoint(c_1.fields[0]);
                    const matchValue_91 = SketchCompile_parseFramePart(c_1.fields[2]);
                    const matchValue_92 = tryFind_1(c_1.fields[1], ctx.Frames);
                    let matchResult_25, fp_1, ft_1, p_6;
                    if (matchValue_90 != null) {
                        if (matchValue_91 != null) {
                            if (matchValue_92 != null) {
                                matchResult_25 = 0;
                                fp_1 = matchValue_91;
                                ft_1 = matchValue_92;
                                p_6 = matchValue_90;
                            }
                            else {
                                matchResult_25 = 1;
                            }
                        }
                        else {
                            matchResult_25 = 1;
                        }
                    }
                    else {
                        matchResult_25 = 1;
                    }
                    switch (matchResult_25) {
                        case 0: {
                            const matchValue_94 = SketchCompile_frameGeometry(ctx.SketchOrigin, ft_1, fp_1);
                            if (matchValue_94 == null) {
                                skipped = ((skipped + 1) | 0);
                            }
                            else if (matchValue_94.tag === 1) {
                                const dx_6 = matchValue_94.fields[2];
                                const dy_6 = matchValue_94.fields[3];
                                const ox_1 = matchValue_94.fields[0];
                                const oy_1 = matchValue_94.fields[1];
                                const dpx_1 = GraphBuilder__Sub_Z37302880(b, p_6.XNode, SketchCompile_constant(b, ox_1)) | 0;
                                const dpy_1 = GraphBuilder__Sub_Z37302880(b, p_6.YNode, SketchCompile_constant(b, oy_1)) | 0;
                                emitDiff(SketchCompile_crossZ(b, SketchCompile_constant(b, dx_6), SketchCompile_constant(b, dy_6), dpx_1, dpy_1), fixedParam(distance_5));
                            }
                            else {
                                const fx_1 = matchValue_94.fields[0];
                                const fy_1 = matchValue_94.fields[1];
                                emitDiff(SketchCompile_length(b, GraphBuilder__Sub_Z37302880(b, p_6.XNode, SketchCompile_constant(b, fx_1)), GraphBuilder__Sub_Z37302880(b, p_6.YNode, SketchCompile_constant(b, fy_1))), fixedParam(distance_5));
                            }
                            break;
                        }
                        case 1: {
                            skipped = ((skipped + 1) | 0);
                            break;
                        }
                    }
                    break;
                }
                case 12: {
                    const matchValue_95 = tryPoint(c_1.fields[0]);
                    const matchValue_96 = tryPoint(c_1.fields[1]);
                    const matchValue_97 = SketchCompile_parseFramePart(c_1.fields[4]);
                    const matchValue_98 = tryFind_1(c_1.fields[3], ctx.Frames);
                    let matchResult_26, aE_10, aS_10, fp_3, ft_3;
                    if (matchValue_95 != null) {
                        if (matchValue_96 != null) {
                            if (matchValue_97 != null) {
                                if (matchValue_98 != null) {
                                    if ((ft_2 = matchValue_98, (aS_9 = matchValue_95, (aE_9 = matchValue_96, !equals(matchValue_97, new FramePart(0, [])))))) {
                                        matchResult_26 = 0;
                                        aE_10 = matchValue_96;
                                        aS_10 = matchValue_95;
                                        fp_3 = matchValue_97;
                                        ft_3 = matchValue_98;
                                    }
                                    else {
                                        matchResult_26 = 1;
                                    }
                                }
                                else {
                                    matchResult_26 = 1;
                                }
                            }
                            else {
                                matchResult_26 = 1;
                            }
                        }
                        else {
                            matchResult_26 = 1;
                        }
                    }
                    else {
                        matchResult_26 = 1;
                    }
                    switch (matchResult_26) {
                        case 0: {
                            const matchValue_100 = SketchCompile_frameGeometry(ctx.SketchOrigin, ft_3, fp_3);
                            let matchResult_27, dx_7, dy_7;
                            if (matchValue_100 != null) {
                                if (matchValue_100.tag === 1) {
                                    matchResult_27 = 0;
                                    dx_7 = matchValue_100.fields[2];
                                    dy_7 = matchValue_100.fields[3];
                                }
                                else {
                                    matchResult_27 = 1;
                                }
                            }
                            else {
                                matchResult_27 = 1;
                            }
                            switch (matchResult_27) {
                                case 0: {
                                    const patternInput_17 = SketchCompile_vecSub(b, aS_10, aE_10);
                                    void (outputs.push(SketchCompile_crossZ(b, patternInput_17[0], patternInput_17[1], SketchCompile_constant(b, dx_7), SketchCompile_constant(b, dy_7))));
                                    break;
                                }
                                case 1: {
                                    skipped = ((skipped + 1) | 0);
                                    break;
                                }
                            }
                            break;
                        }
                        case 1: {
                            skipped = ((skipped + 1) | 0);
                            break;
                        }
                    }
                    break;
                }
                case 14: {
                    const matchValue_101 = tryPoint(c_1.fields[0]);
                    const matchValue_102 = tryPoint(c_1.fields[1]);
                    const matchValue_103 = SketchCompile_parseFramePart(c_1.fields[4]);
                    const matchValue_104 = tryFind_1(c_1.fields[3], ctx.Frames);
                    let matchResult_28, aE_12, aS_12, fp_5, ft_5;
                    if (matchValue_101 != null) {
                        if (matchValue_102 != null) {
                            if (matchValue_103 != null) {
                                if (matchValue_104 != null) {
                                    if ((ft_4 = matchValue_104, (aS_11 = matchValue_101, (aE_11 = matchValue_102, !equals(matchValue_103, new FramePart(0, [])))))) {
                                        matchResult_28 = 0;
                                        aE_12 = matchValue_102;
                                        aS_12 = matchValue_101;
                                        fp_5 = matchValue_103;
                                        ft_5 = matchValue_104;
                                    }
                                    else {
                                        matchResult_28 = 1;
                                    }
                                }
                                else {
                                    matchResult_28 = 1;
                                }
                            }
                            else {
                                matchResult_28 = 1;
                            }
                        }
                        else {
                            matchResult_28 = 1;
                        }
                    }
                    else {
                        matchResult_28 = 1;
                    }
                    switch (matchResult_28) {
                        case 0: {
                            const matchValue_106 = SketchCompile_frameGeometry(ctx.SketchOrigin, ft_5, fp_5);
                            let matchResult_29, dx_8, dy_8;
                            if (matchValue_106 != null) {
                                if (matchValue_106.tag === 1) {
                                    matchResult_29 = 0;
                                    dx_8 = matchValue_106.fields[2];
                                    dy_8 = matchValue_106.fields[3];
                                }
                                else {
                                    matchResult_29 = 1;
                                }
                            }
                            else {
                                matchResult_29 = 1;
                            }
                            switch (matchResult_29) {
                                case 0: {
                                    const patternInput_18 = SketchCompile_vecSub(b, aS_12, aE_12);
                                    void (outputs.push(SketchCompile_dot(b, patternInput_18[0], patternInput_18[1], SketchCompile_constant(b, dx_8), SketchCompile_constant(b, dy_8))));
                                    break;
                                }
                                case 1: {
                                    skipped = ((skipped + 1) | 0);
                                    break;
                                }
                            }
                            break;
                        }
                        case 1: {
                            skipped = ((skipped + 1) | 0);
                            break;
                        }
                    }
                    break;
                }
                case 19: {
                    const matchValue_107 = tryPoint(c_1.fields[1]);
                    const matchValue_108 = tryPoint(c_1.fields[2]);
                    const matchValue_109 = SketchCompile_parseFramePart(c_1.fields[4]);
                    const matchValue_110 = tryFind_1(c_1.fields[3], ctx.Frames);
                    let matchResult_30, aE_14, aS_14, fp_7, ft_7;
                    if (matchValue_107 != null) {
                        if (matchValue_108 != null) {
                            if (matchValue_109 != null) {
                                if (matchValue_110 != null) {
                                    if ((ft_6 = matchValue_110, (aS_13 = matchValue_107, (aE_13 = matchValue_108, equals(matchValue_109, new FramePart(0, [])))))) {
                                        matchResult_30 = 0;
                                        aE_14 = matchValue_108;
                                        aS_14 = matchValue_107;
                                        fp_7 = matchValue_109;
                                        ft_7 = matchValue_110;
                                    }
                                    else {
                                        matchResult_30 = 1;
                                    }
                                }
                                else {
                                    matchResult_30 = 1;
                                }
                            }
                            else {
                                matchResult_30 = 1;
                            }
                        }
                        else {
                            matchResult_30 = 1;
                        }
                    }
                    else {
                        matchResult_30 = 1;
                    }
                    switch (matchResult_30) {
                        case 0: {
                            const matchValue_112 = SketchCompile_frameGeometry(ctx.SketchOrigin, ft_7, fp_7);
                            let matchResult_31, fx_2, fy_2;
                            if (matchValue_112 != null) {
                                if (matchValue_112.tag === 0) {
                                    matchResult_31 = 0;
                                    fx_2 = matchValue_112.fields[0];
                                    fy_2 = matchValue_112.fields[1];
                                }
                                else {
                                    matchResult_31 = 1;
                                }
                            }
                            else {
                                matchResult_31 = 1;
                            }
                            switch (matchResult_31) {
                                case 0: {
                                    const patternInput_19 = SketchCompile_vecSub(b, aS_14, aE_14);
                                    const dyL_6 = patternInput_19[1] | 0;
                                    const dxL_6 = patternInput_19[0] | 0;
                                    emitDiff(GraphBuilder__Div_Z37302880(b, SketchCompile_crossZ(b, dxL_6, dyL_6, GraphBuilder__Sub_Z37302880(b, SketchCompile_constant(b, fx_2), aS_14.XNode), GraphBuilder__Sub_Z37302880(b, SketchCompile_constant(b, fy_2), aS_14.YNode)), SketchCompile_length(b, dxL_6, dyL_6)), fixedParam(c_1.fields[5]));
                                    break;
                                }
                                case 1: {
                                    skipped = ((skipped + 1) | 0);
                                    break;
                                }
                            }
                            break;
                        }
                        case 1: {
                            skipped = ((skipped + 1) | 0);
                            break;
                        }
                    }
                    break;
                }
                case 16: {
                    const matchValue_113 = tryPoint(c_1.fields[1]);
                    const matchValue_114 = tryPoint(c_1.fields[3]);
                    const matchValue_115 = tryDiameterEntity(c_1.fields[0]);
                    const matchValue_116 = tryDiameterEntity(c_1.fields[2]);
                    let matchResult_32, pa_6, pb_6, radiusA, radiusB;
                    if (matchValue_113 != null) {
                        if (matchValue_114 != null) {
                            if (matchValue_115 != null) {
                                if (matchValue_116 != null) {
                                    matchResult_32 = 0;
                                    pa_6 = matchValue_113;
                                    pb_6 = matchValue_114;
                                    radiusA = matchValue_115;
                                    radiusB = matchValue_116;
                                }
                                else {
                                    matchResult_32 = 1;
                                }
                            }
                            else {
                                matchResult_32 = 1;
                            }
                        }
                        else {
                            matchResult_32 = 1;
                        }
                    }
                    else {
                        matchResult_32 = 1;
                    }
                    switch (matchResult_32) {
                        case 0: {
                            const patternInput_20 = SketchCompile_vecSub(b, pa_6, pb_6);
                            const centerDist_1 = SketchCompile_length(b, patternInput_20[0], patternInput_20[1]) | 0;
                            const radiusCombo = (c_1.fields[4] ? GraphBuilder__Sub_Z37302880(b, radiusA, radiusB) : GraphBuilder__Add_Z37302880(b, radiusA, radiusB)) | 0;
                            void (outputs.push(GraphBuilder__Sub_Z37302880(b, centerDist_1, radiusCombo)));
                            break;
                        }
                        case 1: {
                            skipped = ((skipped + 1) | 0);
                            break;
                        }
                    }
                    break;
                }
                default: {
                    const matchValue_12 = tryPoint(c_1.fields[0]);
                    if (matchValue_12 == null) {
                        skipped = ((skipped + 1) | 0);
                    }
                    else {
                        const p_1 = matchValue_12;
                        emitDiffConst(p_1.XNode, c_1.fields[1]);
                        emitDiffConst(p_1.YNode, c_1.fields[2]);
                        addToSet(p_1.XSlot, fixedSlots);
                        addToSet(p_1.YSlot, fixedSlots);
                    }
                }
            }
        }
    }
    finally {
        disposeSafe(enumerator_2);
    }
    if (skipped > 0) {
        const arg = skipped | 0;
        toConsoleError(printf("[SketchCompile] skipped %d constraints (unsupported variant, unresolved ref, or degenerate frame axis)"))(arg);
    }
    let varSlots;
    const array = toArray_1(rangeDouble(0, 1, GraphBuilder__get_ParamCount(b) - 1));
    varSlots = array.filter((s) => !(fixedSlots.has(s) ? true : fixedInputSlots.has(s)));
    return GraphBuilder__Build_7E3D5760(b, outputs.slice(), varSlots);
}

