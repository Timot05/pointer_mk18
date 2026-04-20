import { Record, Union } from "../../ui/fable_modules/fable-library-js.4.29.0/Types.js";
import { list_type, record_type, union_type, bool_type, string_type, int32_type } from "../../ui/fable_modules/fable-library-js.4.29.0/Reflection.js";
import { SlotRef, SlotTableModule_alloc } from "../Editor/SlotTable.fs.js";
import { printf, toText } from "../../ui/fable_modules/fable-library-js.4.29.0/String.js";
import { ofList, tryFind } from "../../ui/fable_modules/fable-library-js.4.29.0/Map.js";
import { SketchLoops_detectLoops } from "../Sketch/SketchLoops.fs.js";
import { choose, map as map_1, tryFind as tryFind_1, head, empty, isEmpty } from "../../ui/fable_modules/fable-library-js.4.29.0/List.js";
import { bind, map, defaultArg } from "../../ui/fable_modules/fable-library-js.4.29.0/Option.js";
import { comparePrimitives } from "../../ui/fable_modules/fable-library-js.4.29.0/Util.js";

export class Primitive extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["PrimSphere", "PrimCylinder", "PrimBox", "PrimHalfPlane"];
    }
}

export function Primitive_$reflection() {
    return union_type("Server.Primitive", [], Primitive, () => [[["radius", int32_type]], [["radius", int32_type], ["height", int32_type]], [["width", int32_type], ["height", int32_type], ["depth", int32_type]], [["axis", string_type], ["offset", int32_type], ["flip", bool_type]]]);
}

export class BooleanOp extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["BoolUnion", "BoolSubtract", "BoolIntersect"];
    }
}

export function BooleanOp_$reflection() {
    return union_type("Server.BooleanOp", [], BooleanOp, () => [[], [], []]);
}

export class UnaryFieldOp extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["OpThicken", "OpShell"];
    }
}

export function UnaryFieldOp_$reflection() {
    return union_type("Server.UnaryFieldOp", [], UnaryFieldOp, () => [[], []]);
}

export class SlotPt2 extends Record {
    constructor(XSlot, YSlot) {
        super();
        this.XSlot = (XSlot | 0);
        this.YSlot = (YSlot | 0);
    }
}

export function SlotPt2_$reflection() {
    return record_type("Server.SlotPt2", [], SlotPt2, () => [["XSlot", int32_type], ["YSlot", int32_type]]);
}

export class SketchPrimitive2d extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["SpLineSegment", "SpCircle", "SpArcCenter"];
    }
}

export function SketchPrimitive2d_$reflection() {
    return union_type("Server.SketchPrimitive2d", [], SketchPrimitive2d, () => [[["startP", SlotPt2_$reflection()], ["endP", SlotPt2_$reflection()]], [["center", SlotPt2_$reflection()], ["radiusSlot", int32_type]], [["startP", SlotPt2_$reflection()], ["endP", SlotPt2_$reflection()], ["center", SlotPt2_$reflection()], ["clockwise", bool_type]]]);
}

export class Sketch2d extends Record {
    constructor(Primitives, Closed, Flip) {
        super();
        this.Primitives = Primitives;
        this.Closed = Closed;
        this.Flip = Flip;
    }
}

export function Sketch2d_$reflection() {
    return record_type("Server.Sketch2d", [], Sketch2d, () => [["Primitives", list_type(SketchPrimitive2d_$reflection())], ["Closed", bool_type], ["Flip", bool_type]]);
}

export class FieldNode extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["FPrimitive", "FTranslate", "FRotate", "FBoolean", "FFieldOp", "FSketch"];
    }
}

export function FieldNode_$reflection() {
    return union_type("Server.FieldNode", [], FieldNode, () => [[["prim", Primitive_$reflection()]], [["x", int32_type], ["y", int32_type], ["z", int32_type], ["child", FieldNode_$reflection()]], [["ax", int32_type], ["ay", int32_type], ["az", int32_type], ["angle", int32_type], ["child", FieldNode_$reflection()]], [["op", BooleanOp_$reflection()], ["radius", int32_type], ["a", FieldNode_$reflection()], ["b", FieldNode_$reflection()]], [["op", UnaryFieldOp_$reflection()], ["value", int32_type], ["child", FieldNode_$reflection()]], [["sketch", Sketch2d_$reflection()]]]);
}

export class FieldSurface extends Record {
    constructor(ActionId, Field) {
        super();
        this.ActionId = ActionId;
        this.Field = Field;
    }
}

export function FieldSurface_$reflection() {
    return record_type("Server.FieldSurface", [], FieldSurface, () => [["ActionId", string_type], ["Field", FieldNode_$reflection()]]);
}

function FieldCompile_slotPtForPoint(b, sketchActionId, pointId, x, y) {
    return new SlotPt2(SlotTableModule_alloc(b, new SlotRef(sketchActionId, toText(printf("sketch.entity.%s.x"))(pointId)), x), SlotTableModule_alloc(b, new SlotRef(sketchActionId, toText(printf("sketch.entity.%s.y"))(pointId)), y));
}

function FieldCompile_entityId(_arg) {
    switch (_arg.tag) {
        case 1:
            return _arg.fields[0];
        case 2:
            return _arg.fields[0];
        case 3:
            return _arg.fields[0];
        default:
            return _arg.fields[0];
    }
}

function FieldCompile_entityToPrimitive(b, sketchActionId, entityMap, entity) {
    const pt = (pointId) => {
        const matchValue = tryFind(pointId, entityMap);
        let matchResult, x, y;
        if (matchValue != null) {
            if (matchValue.tag === 0) {
                matchResult = 0;
                x = matchValue.fields[1];
                y = matchValue.fields[2];
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
                return FieldCompile_slotPtForPoint(b, sketchActionId, pointId, x, y);
            default:
                return undefined;
        }
    };
    switch (entity.tag) {
        case 2: {
            const matchValue_4 = pt(entity.fields[1]);
            if (matchValue_4 == null) {
                return undefined;
            }
            else {
                return new SketchPrimitive2d(1, [matchValue_4, SlotTableModule_alloc(b, new SlotRef(sketchActionId, toText(printf("sketch.entity.%s.radius"))(entity.fields[0])), entity.fields[2])]);
            }
        }
        case 3:
            if (entity.fields[3].tag === 1) {
                return undefined;
            }
            else {
                const matchValue_5 = pt(entity.fields[1]);
                const matchValue_6 = pt(entity.fields[2]);
                const matchValue_7 = pt(entity.fields[3].fields[0]);
                let matchResult_1, c_1, e_1, s_1;
                if (matchValue_5 != null) {
                    if (matchValue_6 != null) {
                        if (matchValue_7 != null) {
                            matchResult_1 = 0;
                            c_1 = matchValue_7;
                            e_1 = matchValue_6;
                            s_1 = matchValue_5;
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
                        return new SketchPrimitive2d(2, [s_1, e_1, c_1, entity.fields[3].fields[1]]);
                    default:
                        return undefined;
                }
            }
        case 0:
            return undefined;
        default: {
            const matchValue_1 = pt(entity.fields[1]);
            const matchValue_2 = pt(entity.fields[2]);
            let matchResult_2, e, s;
            if (matchValue_1 != null) {
                if (matchValue_2 != null) {
                    matchResult_2 = 0;
                    e = matchValue_2;
                    s = matchValue_1;
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
                    return new SketchPrimitive2d(0, [s, e]);
                default:
                    return undefined;
            }
        }
    }
}

function FieldCompile_selectEntityIds(sketch, selection) {
    if (selection.tag === 0) {
        const loopId = selection.fields[0];
        const loops = SketchLoops_detectLoops(sketch.Entities);
        if (loopId == null) {
            if (isEmpty(loops)) {
                return empty();
            }
            else {
                return head(loops).EntityIds;
            }
        }
        else {
            const id = loopId;
            return defaultArg(map((l_1) => l_1.EntityIds, tryFind_1((l) => (l.Id === id), loops)), empty());
        }
    }
    else {
        return selection.fields[0];
    }
}

function FieldCompile_compileElement(b, elem) {
    const slot = (actionId, path, value) => SlotTableModule_alloc(b, new SlotRef(actionId, path), value);
    switch (elem.tag) {
        case 1:
            return new FieldNode(0, [new Primitive(0, [slot(elem.fields[0], "radius", elem.fields[1])])]);
        case 2: {
            const id_1 = elem.fields[0];
            return new FieldNode(0, [new Primitive(1, [slot(id_1, "radius", elem.fields[1]), slot(id_1, "height", elem.fields[2])])]);
        }
        case 3: {
            const id_2 = elem.fields[0];
            return new FieldNode(0, [new Primitive(2, [slot(id_2, "width", elem.fields[1]), slot(id_2, "height", elem.fields[2]), slot(id_2, "depth", elem.fields[3])])]);
        }
        case 4:
            return new FieldNode(0, [new Primitive(3, [elem.fields[1], slot(elem.fields[0], "offset", elem.fields[2]), elem.fields[3]])]);
        case 5: {
            const id_4 = elem.fields[0];
            const matchValue = FieldCompile_compileElement(b, elem.fields[4]);
            if (matchValue != null) {
                const fc = matchValue;
                return new FieldNode(1, [slot(id_4, "x", elem.fields[1]), slot(id_4, "y", elem.fields[2]), slot(id_4, "z", elem.fields[3]), fc]);
            }
            else {
                return undefined;
            }
        }
        case 6: {
            const id_5 = elem.fields[0];
            const matchValue_1 = FieldCompile_compileElement(b, elem.fields[5]);
            if (matchValue_1 != null) {
                const fc_1 = matchValue_1;
                return new FieldNode(2, [slot(id_5, "ax", elem.fields[1]), slot(id_5, "ay", elem.fields[2]), slot(id_5, "az", elem.fields[3]), slot(id_5, "angle", elem.fields[4]), fc_1]);
            }
            else {
                return undefined;
            }
        }
        case 7: {
            const matchValue_2 = FieldCompile_compileElement(b, elem.fields[1]);
            const matchValue_3 = FieldCompile_compileElement(b, elem.fields[2]);
            if (matchValue_2 == null) {
                if (matchValue_3 == null) {
                    return undefined;
                }
                else {
                    const fb_1 = matchValue_3;
                    return fb_1;
                }
            }
            else if (matchValue_3 == null) {
                const fa_1 = matchValue_2;
                return fa_1;
            }
            else {
                const fa = matchValue_2;
                const fb = matchValue_3;
                return new FieldNode(3, [new BooleanOp(0, []), slot(elem.fields[0], "radius", elem.fields[3]), fa, fb]);
            }
        }
        case 8: {
            const matchValue_5 = FieldCompile_compileElement(b, elem.fields[1]);
            const matchValue_6 = FieldCompile_compileElement(b, elem.fields[2]);
            if (matchValue_5 != null) {
                if (matchValue_6 == null) {
                    const fa_3 = matchValue_5;
                    return fa_3;
                }
                else {
                    const fa_2 = matchValue_5;
                    const fb_2 = matchValue_6;
                    return new FieldNode(3, [new BooleanOp(1, []), slot(elem.fields[0], "radius", elem.fields[3]), fa_2, fb_2]);
                }
            }
            else {
                return undefined;
            }
        }
        case 9: {
            const matchValue_8 = FieldCompile_compileElement(b, elem.fields[1]);
            const matchValue_9 = FieldCompile_compileElement(b, elem.fields[2]);
            let matchResult, fa_4, fb_3;
            if (matchValue_8 != null) {
                if (matchValue_9 != null) {
                    matchResult = 0;
                    fa_4 = matchValue_8;
                    fb_3 = matchValue_9;
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
                    return new FieldNode(3, [new BooleanOp(2, []), slot(elem.fields[0], "radius", elem.fields[3]), fa_4, fb_3]);
                default:
                    return undefined;
            }
        }
        case 10:
            return map((fc_2) => (new FieldNode(4, [new UnaryFieldOp(0, []), slot(elem.fields[0], "amount", elem.fields[2]), fc_2])), FieldCompile_compileElement(b, elem.fields[1]));
        case 11:
            return map((fc_3) => (new FieldNode(4, [new UnaryFieldOp(1, []), slot(elem.fields[0], "thickness", elem.fields[2]), fc_3])), FieldCompile_compileElement(b, elem.fields[1]));
        case 12: {
            const sketch = elem.fields[2];
            const entityMap = ofList(map_1((e) => [FieldCompile_entityId(e), e], sketch.Entities), {
                Compare: comparePrimitives,
            });
            const prims = choose((eid) => bind((entity) => FieldCompile_entityToPrimitive(b, elem.fields[1], entityMap, entity), tryFind(eid, entityMap)), FieldCompile_selectEntityIds(sketch, elem.fields[3]));
            if (isEmpty(prims)) {
                return undefined;
            }
            else {
                return new FieldNode(5, [new Sketch2d(prims, true, elem.fields[4])]);
            }
        }
        default:
            return undefined;
    }
}

/**
 * Compile each visible action's element tree into a FieldSurface.
 * Slots are allocated into the provided builder. Skipped (None) if the
 * element produces no field (e.g. Empty, Mesh, Frame-only chains).
 */
export function FieldCompile_compile(actions, elements, b) {
    return choose((action) => map((field) => (new FieldSurface(action.Id, field)), bind((elem) => FieldCompile_compileElement(b, elem), tryFind(action.Id, elements))), actions);
}

