import { Union, Record } from "../../ui/fable_modules/fable-library-js.4.29.0/Types.js";
import { tuple_type, union_type, float64_type, int32_type, record_type, class_type, option_type, list_type, string_type, bool_type } from "../../ui/fable_modules/fable-library-js.4.29.0/Reflection.js";
import { LabelPos, ArcData, RenderEntity, SketchConstraint, ActionSketch, ActionSketch_$reflection, SketchConstraint_$reflection, LabelPos_$reflection } from "./Sketch.fs.js";
import { length as length_1, replicate, tryItem, head, tail, isEmpty as isEmpty_1, append, filter, collect, exists, ofArray, singleton, map, mapIndexed, choose, tryFind, empty } from "../../ui/fable_modules/fable-library-js.4.29.0/List.js";
import { tryFind as tryFind_1, ofList, empty as empty_1 } from "../../ui/fable_modules/fable-library-js.4.29.0/Map.js";
import { equals, stringHash, min, max, compareArrays, compare, comparePrimitives } from "../../ui/fable_modules/fable-library-js.4.29.0/Util.js";
import { DocAction, ActionKind, DocumentModule_updateAction, DocAction_$reflection } from "../Editor/Domain.fs.js";
import { filter as filter_2, map as map_1, defaultArg, map2, bind } from "../../ui/fable_modules/fable-library-js.4.29.0/Option.js";
import { empty as empty_2, isEmpty, filter as filter_1, exists as exists_1, FSharpSet__get_Count, union, toList, ofList as ofList_1, contains } from "../../ui/fable_modules/fable-library-js.4.29.0/Set.js";
import { min as min_1, max as max_1 } from "../../ui/fable_modules/fable-library-js.4.29.0/Double.js";
import { List_distinct } from "../../ui/fable_modules/fable-library-js.4.29.0/Seq2.js";
import { RigidTransform__Apply_Z2E054BF3, RigidTransform__get_Inverse } from "../Math/Transform.fs.js";
import { Vec2_pointLineDistance, Vec2_distance } from "../Math/Vec.fs.js";

export class SketchUiState extends Record {
    constructor(EditMode, Tool, ToolPoints, EditingDimension, ConstraintPlacementMode, ConstraintPlacementDraft, PendingConstraintPlacement, ConstraintAvailability, DimensionPlacementAvailability) {
        super();
        this.EditMode = EditMode;
        this.Tool = Tool;
        this.ToolPoints = ToolPoints;
        this.EditingDimension = EditingDimension;
        this.ConstraintPlacementMode = ConstraintPlacementMode;
        this.ConstraintPlacementDraft = ConstraintPlacementDraft;
        this.PendingConstraintPlacement = PendingConstraintPlacement;
        this.ConstraintAvailability = ConstraintAvailability;
        this.DimensionPlacementAvailability = DimensionPlacementAvailability;
    }
}

export function SketchUiState_$reflection() {
    return record_type("Server.SketchUiState", [], SketchUiState, () => [["EditMode", bool_type], ["Tool", string_type], ["ToolPoints", list_type(LabelPos_$reflection())], ["EditingDimension", option_type(EditingDimension_$reflection())], ["ConstraintPlacementMode", option_type(string_type)], ["ConstraintPlacementDraft", option_type(ConstraintPlacementDraft_$reflection())], ["PendingConstraintPlacement", option_type(PendingConstraintPlacement_$reflection())], ["ConstraintAvailability", class_type("Microsoft.FSharp.Collections.FSharpMap`2", [string_type, bool_type])], ["DimensionPlacementAvailability", class_type("Microsoft.FSharp.Collections.FSharpMap`2", [string_type, bool_type])]]);
}

export class EditingDimension extends Record {
    constructor(SketchId, ConstraintIndex, Key, Value) {
        super();
        this.SketchId = SketchId;
        this.ConstraintIndex = (ConstraintIndex | 0);
        this.Key = Key;
        this.Value = Value;
    }
}

export function EditingDimension_$reflection() {
    return record_type("Server.EditingDimension", [], EditingDimension, () => [["SketchId", string_type], ["ConstraintIndex", int32_type], ["Key", string_type], ["Value", float64_type]]);
}

export class PendingConstraintPlacement extends Record {
    constructor(SketchId, Constraint) {
        super();
        this.SketchId = SketchId;
        this.Constraint = Constraint;
    }
}

export function PendingConstraintPlacement_$reflection() {
    return record_type("Server.PendingConstraintPlacement", [], PendingConstraintPlacement, () => [["SketchId", string_type], ["Constraint", SketchConstraint_$reflection()]]);
}

export class ConstraintPlacementRef extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["RefPoint", "RefLine", "RefCircle", "RefArc", "RefFrameOrigin", "RefFrameAxis"];
    }
}

export function ConstraintPlacementRef_$reflection() {
    return union_type("Server.ConstraintPlacementRef", [], ConstraintPlacementRef, () => [[["Item", string_type]], [["Item", string_type]], [["Item", string_type]], [["Item", string_type]], [["Item", string_type]], [["Item1", string_type], ["Item2", string_type]]]);
}

export class ConstraintPlacementDraft extends Record {
    constructor(SketchId, Kind, ClickedRefs) {
        super();
        this.SketchId = SketchId;
        this.Kind = Kind;
        this.ClickedRefs = ClickedRefs;
    }
}

export function ConstraintPlacementDraft_$reflection() {
    return record_type("Server.ConstraintPlacementDraft", [], ConstraintPlacementDraft, () => [["SketchId", string_type], ["Kind", string_type], ["ClickedRefs", list_type(ConstraintPlacementRef_$reflection())]]);
}

export class SketchAuthoring_ToolApplyResult extends Record {
    constructor(Sketch, ContinueFrom) {
        super();
        this.Sketch = Sketch;
        this.ContinueFrom = ContinueFrom;
    }
}

export function SketchAuthoring_ToolApplyResult_$reflection() {
    return record_type("Server.SketchAuthoring.ToolApplyResult", [], SketchAuthoring_ToolApplyResult, () => [["Sketch", ActionSketch_$reflection()], ["ContinueFrom", option_type(tuple_type(string_type, LabelPos_$reflection()))]]);
}

export const SketchAuthoring_emptyUiState = new SketchUiState(false, "none", empty(), undefined, undefined, undefined, undefined, empty_1({
    Compare: comparePrimitives,
}), empty_1({
    Compare: comparePrimitives,
}));

export class SketchAuthoring_SelectedSketchContext extends Record {
    constructor(Action, Sketch) {
        super();
        this.Action = Action;
        this.Sketch = Sketch;
    }
}

export function SketchAuthoring_SelectedSketchContext_$reflection() {
    return record_type("Server.SketchAuthoring.SelectedSketchContext", [], SketchAuthoring_SelectedSketchContext, () => [["Action", DocAction_$reflection()], ["Sketch", ActionSketch_$reflection()]]);
}

export function SketchAuthoring_trySelectedSketch(doc) {
    const matchValue = doc.SelectedId;
    if (matchValue != null) {
        const id = matchValue;
        return bind((action_1) => {
            const matchValue_1 = action_1.Kind;
            if (matchValue_1.tag === 11) {
                return new SketchAuthoring_SelectedSketchContext(action_1, matchValue_1.fields[2]);
            }
            else {
                return undefined;
            }
        }, tryFind((action) => (action.Id === id), doc.Actions));
    }
    else {
        return undefined;
    }
}

export function SketchAuthoring_withUpdatedSketch(doc, actionId, nextSketch) {
    let matchValue_1;
    const matchValue = tryFind((action) => (action.Id === actionId), doc.Actions);
    if (matchValue == null) {
        return doc;
    }
    else {
        const action_1 = matchValue;
        return DocumentModule_updateAction(actionId, new DocAction(action_1.Id, action_1.Name, (matchValue_1 = action_1.Kind, (matchValue_1.tag === 11) ? (new ActionKind(11, [matchValue_1.fields[0], matchValue_1.fields[1], nextSketch])) : matchValue_1), action_1.Visible, action_1.Display, action_1.FieldSlice), doc);
    }
}

export function SketchAuthoring_removeConstraintAt(index, sketch) {
    return new ActionSketch(sketch.Entities, choose((tupledArg) => {
        if (tupledArg[0] === index) {
            return undefined;
        }
        else {
            return tupledArg[1];
        }
    }, mapIndexed((i, constraint_) => [i, constraint_], sketch.Constraints)));
}

function SketchAuthoring_entityIdOf(_arg) {
    let id;
    switch (_arg.tag) {
        case 1: {
            id = _arg.fields[0];
            break;
        }
        case 2: {
            id = _arg.fields[0];
            break;
        }
        case 3: {
            id = _arg.fields[0];
            break;
        }
        default:
            id = _arg.fields[0];
    }
    return id;
}

function SketchAuthoring_entityMap(sketch) {
    return ofList(map((entity) => [SketchAuthoring_entityIdOf(entity), entity], sketch.Entities), {
        Compare: comparePrimitives,
    });
}

function SketchAuthoring_entityRefsEntity(entityId, _arg) {
    switch (_arg.tag) {
        case 1:
            if (_arg.fields[1] === entityId) {
                return true;
            }
            else {
                return _arg.fields[2] === entityId;
            }
        case 2:
            return _arg.fields[1] === entityId;
        case 3:
            if (_arg.fields[3].tag === 1) {
                if (_arg.fields[1] === entityId) {
                    return true;
                }
                else {
                    return _arg.fields[2] === entityId;
                }
            }
            else if ((_arg.fields[1] === entityId) ? true : (_arg.fields[2] === entityId)) {
                return true;
            }
            else {
                return _arg.fields[3].fields[0] === entityId;
            }
        default:
            return false;
    }
}

function SketchAuthoring_entityReferencedPointIds(_arg) {
    switch (_arg.tag) {
        case 2:
            return singleton(_arg.fields[1]);
        case 3:
            if (_arg.fields[3].tag === 1) {
                return ofArray([_arg.fields[1], _arg.fields[2]]);
            }
            else {
                return ofArray([_arg.fields[1], _arg.fields[2], _arg.fields[3].fields[0]]);
            }
        case 0:
            return empty();
        default:
            return ofArray([_arg.fields[1], _arg.fields[2]]);
    }
}

function SketchAuthoring_normalizePair(a, b) {
    if (compare(a, b) < 0) {
        return [a, b];
    }
    else {
        return [b, a];
    }
}

function SketchAuthoring_constraintRefsAnyEntity(deletedEntityIds, deletedLinePairs) {
    const hasEntity = (id) => contains(id, deletedEntityIds);
    const hasLinePair = (a, b) => contains(SketchAuthoring_normalizePair(a, b), deletedLinePairs);
    return (_arg) => {
        let matchResult, a_1, b_1, point_1, aEnd, aStart, bEnd, bStart, lineA, lineB, aEnd_2, aStart_2, lineA_2;
        switch (_arg.tag) {
            case 9: {
                matchResult = 1;
                break;
            }
            case 1: {
                matchResult = 2;
                a_1 = _arg.fields[0];
                b_1 = _arg.fields[1];
                break;
            }
            case 4: {
                matchResult = 2;
                a_1 = _arg.fields[0];
                b_1 = _arg.fields[1];
                break;
            }
            case 5: {
                matchResult = 2;
                a_1 = _arg.fields[0];
                b_1 = _arg.fields[1];
                break;
            }
            case 2: {
                matchResult = 3;
                point_1 = _arg.fields[0];
                break;
            }
            case 7: {
                matchResult = 3;
                point_1 = _arg.fields[0];
                break;
            }
            case 3: {
                matchResult = 4;
                break;
            }
            case 6: {
                matchResult = 5;
                break;
            }
            case 8: {
                matchResult = 6;
                aEnd = _arg.fields[1];
                aStart = _arg.fields[0];
                bEnd = _arg.fields[3];
                bStart = _arg.fields[2];
                lineA = _arg.fields[4];
                lineB = _arg.fields[5];
                break;
            }
            case 11: {
                matchResult = 6;
                aEnd = _arg.fields[1];
                aStart = _arg.fields[0];
                bEnd = _arg.fields[3];
                bStart = _arg.fields[2];
                lineA = _arg.fields[4];
                lineB = _arg.fields[5];
                break;
            }
            case 13: {
                matchResult = 6;
                aEnd = _arg.fields[1];
                aStart = _arg.fields[0];
                bEnd = _arg.fields[3];
                bStart = _arg.fields[2];
                lineA = _arg.fields[4];
                lineB = _arg.fields[5];
                break;
            }
            case 18: {
                matchResult = 6;
                aEnd = _arg.fields[1];
                aStart = _arg.fields[0];
                bEnd = _arg.fields[3];
                bStart = _arg.fields[2];
                lineA = _arg.fields[4];
                lineB = _arg.fields[5];
                break;
            }
            case 10: {
                matchResult = 7;
                break;
            }
            case 12: {
                matchResult = 8;
                aEnd_2 = _arg.fields[1];
                aStart_2 = _arg.fields[0];
                lineA_2 = _arg.fields[2];
                break;
            }
            case 14: {
                matchResult = 8;
                aEnd_2 = _arg.fields[1];
                aStart_2 = _arg.fields[0];
                lineA_2 = _arg.fields[2];
                break;
            }
            case 19: {
                matchResult = 8;
                aEnd_2 = _arg.fields[2];
                aStart_2 = _arg.fields[1];
                lineA_2 = _arg.fields[0];
                break;
            }
            case 15: {
                matchResult = 9;
                break;
            }
            case 16: {
                matchResult = 10;
                break;
            }
            case 17: {
                matchResult = 11;
                break;
            }
            case 20: {
                matchResult = 12;
                break;
            }
            case 21: {
                matchResult = 13;
                break;
            }
            case 22: {
                matchResult = 14;
                break;
            }
            case 23: {
                matchResult = 15;
                break;
            }
            case 24: {
                matchResult = 16;
                break;
            }
            default:
                matchResult = 0;
        }
        switch (matchResult) {
            case 0:
                return hasEntity(_arg.fields[0]);
            case 1:
                return hasEntity(_arg.fields[0]) ? true : hasEntity(_arg.fields[1]);
            case 2:
                return (hasEntity(a_1) ? true : hasEntity(b_1)) ? true : hasLinePair(a_1, b_1);
            case 3:
                return hasEntity(point_1);
            case 4:
                return ((hasEntity(_arg.fields[0]) ? true : hasEntity(_arg.fields[1])) ? true : hasEntity(_arg.fields[2])) ? true : hasEntity(_arg.fields[3]);
            case 5: {
                const b_2 = _arg.fields[1];
                const a_2 = _arg.fields[0];
                return (hasEntity(a_2) ? true : hasEntity(b_2)) ? true : hasLinePair(a_2, b_2);
            }
            case 6:
                return exists(hasEntity, ofArray([aStart, aEnd, bStart, bEnd, lineA, lineB]));
            case 7:
                return exists(hasEntity, ofArray([_arg.fields[0], _arg.fields[1], _arg.fields[2], _arg.fields[3]]));
            case 8:
                return exists(hasEntity, ofArray([lineA_2, aStart_2, aEnd_2]));
            case 9:
                return exists(hasEntity, ofArray([_arg.fields[0], _arg.fields[1], _arg.fields[2], _arg.fields[3], _arg.fields[4]]));
            case 10:
                return exists(hasEntity, ofArray([_arg.fields[0], _arg.fields[1], _arg.fields[2], _arg.fields[3]]));
            case 11:
                return hasEntity(_arg.fields[0]) ? true : hasEntity(_arg.fields[1]);
            case 12:
                return exists(hasEntity, ofArray([_arg.fields[0], _arg.fields[1], _arg.fields[2], _arg.fields[3]]));
            case 13:
                return exists(hasEntity, ofArray([_arg.fields[0], _arg.fields[1], _arg.fields[2]]));
            case 14:
                return exists(hasEntity, ofArray([_arg.fields[0], _arg.fields[1], _arg.fields[2], _arg.fields[3], _arg.fields[4]]));
            case 15:
                return exists(hasEntity, ofArray([_arg.fields[0], _arg.fields[1], _arg.fields[2], _arg.fields[3]]));
            default:
                return exists(hasEntity, ofArray([_arg.fields[0], _arg.fields[1], _arg.fields[2], _arg.fields[3], _arg.fields[4], _arg.fields[5]]));
        }
    };
}

export function SketchAuthoring_deleteTargets(targets, sketch) {
    const constraintIndicesToDelete = ofList_1(choose((_arg) => {
        if (_arg.tag === 5) {
            return _arg.fields[1];
        }
        else {
            return undefined;
        }
    }, targets), {
        Compare: comparePrimitives,
    });
    const directlyDeletedEntityIds = ofList_1(choose((_arg_1) => {
        let matchResult, entityId;
        switch (_arg_1.tag) {
            case 0: {
                matchResult = 0;
                entityId = _arg_1.fields[1];
                break;
            }
            case 1: {
                matchResult = 0;
                entityId = _arg_1.fields[1];
                break;
            }
            case 2: {
                matchResult = 0;
                entityId = _arg_1.fields[1];
                break;
            }
            case 3: {
                matchResult = 0;
                entityId = _arg_1.fields[1];
                break;
            }
            default:
                matchResult = 1;
        }
        switch (matchResult) {
            case 0:
                return entityId;
            default:
                return undefined;
        }
    }, targets), {
        Compare: comparePrimitives,
    });
    const candidatePointIds = ofList_1(collect(SketchAuthoring_entityReferencedPointIds, choose((entityId_1) => tryFind((entity) => (SketchAuthoring_entityIdOf(entity) === entityId_1), sketch.Entities), toList(directlyDeletedEntityIds))), {
        Compare: comparePrimitives,
    });
    const deletedLinePairs = ofList_1(choose((_arg_3) => {
        if (_arg_3.tag === 1) {
            const matchValue = tryFind((entity_1) => (SketchAuthoring_entityIdOf(entity_1) === _arg_3.fields[1]), sketch.Entities);
            let matchResult_1, a, b;
            if (matchValue != null) {
                if (matchValue.tag === 1) {
                    matchResult_1 = 0;
                    a = matchValue.fields[1];
                    b = matchValue.fields[2];
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
                    return SketchAuthoring_normalizePair(a, b);
                default:
                    return undefined;
            }
        }
        else {
            return undefined;
        }
    }, targets), {
        Compare: compareArrays,
    });
    const expand = (deleted_mut) => {
        expand:
        while (true) {
            const deleted = deleted_mut;
            const combined = union(deleted, ofList_1(map(SketchAuthoring_entityIdOf, filter((entity_2) => {
                if (!contains(SketchAuthoring_entityIdOf(entity_2), deleted)) {
                    return entityRefsEntityAny(deleted)(entity_2);
                }
                else {
                    return false;
                }
            }, sketch.Entities)), {
                Compare: comparePrimitives,
            }));
            if (FSharpSet__get_Count(combined) === FSharpSet__get_Count(deleted)) {
                return deleted;
            }
            else {
                deleted_mut = combined;
                continue expand;
            }
            break;
        }
    };
    const entityRefsEntityAny = (deleted_1) => ((entity_3) => exists_1((id_1) => SketchAuthoring_entityRefsEntity(id_1, entity_3), deleted_1));
    const deletedEntityIds = expand(directlyDeletedEntityIds);
    const afterDirectDelete = new ActionSketch(filter((entity_4) => !contains(SketchAuthoring_entityIdOf(entity_4), deletedEntityIds), sketch.Entities), choose((tupledArg) => {
        const constraint__1 = tupledArg[1];
        if (contains(tupledArg[0], constraintIndicesToDelete) ? true : SketchAuthoring_constraintRefsAnyEntity(deletedEntityIds, deletedLinePairs)(constraint__1)) {
            return undefined;
        }
        else {
            return constraint__1;
        }
    }, mapIndexed((i, constraint_) => [i, constraint_], sketch.Constraints)));
    const remainingReferencedPointIds = ofList_1(collect(SketchAuthoring_entityReferencedPointIds, afterDirectDelete.Entities), {
        Compare: comparePrimitives,
    });
    const orphanCandidatePointIds = filter_1((pointId) => !contains(pointId, remainingReferencedPointIds), candidatePointIds);
    if (isEmpty(orphanCandidatePointIds)) {
        return afterDirectDelete;
    }
    else {
        return new ActionSketch(filter((entity_5) => {
            if (entity_5.tag === 0) {
                return !contains(entity_5.fields[0], orphanCandidatePointIds);
            }
            else {
                return true;
            }
        }, afterDirectDelete.Entities), filter((constraint__2) => !SketchAuthoring_constraintRefsAnyEntity(orphanCandidatePointIds, empty_2({
            Compare: compareArrays,
        }))(constraint__2), afterDirectDelete.Constraints));
    }
}

function SketchAuthoring_tryPoint(sketch, id) {
    const matchValue = tryFind_1(id, SketchAuthoring_entityMap(sketch));
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
            return [x, y];
        default:
            return undefined;
    }
}

function SketchAuthoring_tryLine(sketch, id) {
    const matchValue = tryFind_1(id, SketchAuthoring_entityMap(sketch));
    let matchResult, endId, startId;
    if (matchValue != null) {
        if (matchValue.tag === 1) {
            matchResult = 0;
            endId = matchValue.fields[2];
            startId = matchValue.fields[1];
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
            return [startId, endId];
        default:
            return undefined;
    }
}

function SketchAuthoring_tryCircle(sketch, id) {
    const matchValue = tryFind_1(id, SketchAuthoring_entityMap(sketch));
    let matchResult, centerId, radius;
    if (matchValue != null) {
        if (matchValue.tag === 2) {
            matchResult = 0;
            centerId = matchValue.fields[1];
            radius = matchValue.fields[2];
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
            return [centerId, radius];
        default:
            return undefined;
    }
}

function SketchAuthoring_tryDiameterEntity(sketch, id) {
    const matchValue = SketchAuthoring_tryCircle(sketch, id);
    if (matchValue == null) {
        const matchValue_1 = tryFind_1(id, SketchAuthoring_entityMap(sketch));
        let matchResult, centerId_1, startId;
        if (matchValue_1 != null) {
            if (matchValue_1.tag === 3) {
                if (matchValue_1.fields[3].tag === 0) {
                    matchResult = 0;
                    centerId_1 = matchValue_1.fields[3].fields[0];
                    startId = matchValue_1.fields[1];
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
                const matchValue_2 = SketchAuthoring_tryPoint(sketch, centerId_1);
                const matchValue_3 = SketchAuthoring_tryPoint(sketch, startId);
                let matchResult_1, cx, cy, sx, sy;
                if (matchValue_2 != null) {
                    if (matchValue_3 != null) {
                        matchResult_1 = 0;
                        cx = matchValue_2[0];
                        cy = matchValue_2[1];
                        sx = matchValue_3[0];
                        sy = matchValue_3[1];
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
                        const dx = sx - cx;
                        const dy = sy - cy;
                        return [centerId_1, Math.sqrt((dx * dx) + (dy * dy))];
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
        return [matchValue[0], matchValue[1]];
    }
}

function SketchAuthoring_tryCurve(sketch, id) {
    return SketchAuthoring_tryDiameterEntity(sketch, id);
}

function SketchAuthoring_dist(ax, ay, bx, by) {
    const dx = bx - ax;
    const dy = by - ay;
    return Math.sqrt((dx * dx) + (dy * dy));
}

function SketchAuthoring_dot(ax, ay, bx, by) {
    return (ax * bx) + (ay * by);
}

function SketchAuthoring_cross(ax, ay, bx, by) {
    return (ax * by) - (ay * bx);
}

function SketchAuthoring_sub(ax, ay, bx, by) {
    return [ax - bx, ay - by];
}

function SketchAuthoring_clamp(minv, maxv, value) {
    return max(compare, minv, min(compare, maxv, value));
}

function SketchAuthoring_angleOf(x, y) {
    return Math.atan2(y, x);
}

const SketchAuthoring_tau = 3.141592653589793 * 2;

function SketchAuthoring_normalizePositive(angle) {
    let a = angle;
    while (a < 0) {
        a = (a + SketchAuthoring_tau);
    }
    while (a >= SketchAuthoring_tau) {
        a = (a - SketchAuthoring_tau);
    }
    return a;
}

function SketchAuthoring_clockwiseSweep(fromAngle, toAngle) {
    const ccw = SketchAuthoring_normalizePositive(toAngle - fromAngle);
    if (ccw <= 0) {
        return 0;
    }
    else {
        return SketchAuthoring_tau - ccw;
    }
}

function SketchAuthoring_pointInSector(cursorAngle, startAngle, sweep, ccw) {
    return (ccw ? SketchAuthoring_normalizePositive(cursorAngle - startAngle) : SketchAuthoring_clockwiseSweep(startAngle, cursorAngle)) <= (sweep + 1E-06);
}

function SketchAuthoring_lineDirection(sketch, lineId) {
    return bind((tupledArg) => {
        const startId = tupledArg[0];
        const endId = tupledArg[1];
        return map2((a, b) => [SketchAuthoring_sub(b[0], b[1], a[0], a[1]), [startId, endId]], SketchAuthoring_tryPoint(sketch, startId), SketchAuthoring_tryPoint(sketch, endId));
    }, SketchAuthoring_tryLine(sketch, lineId));
}

function SketchAuthoring_lineDistanceValue(sketch, lineA, lineB) {
    let tupledArg;
    const matchValue = SketchAuthoring_lineDirection(sketch, lineA);
    const matchValue_1 = SketchAuthoring_lineDirection(sketch, lineB);
    let matchResult, _bdx, _bdy, aStart, adx, ady, bStart;
    if (matchValue != null) {
        if (matchValue_1 != null) {
            matchResult = 0;
            _bdx = matchValue_1[0][0];
            _bdy = matchValue_1[0][1];
            aStart = matchValue[1][0];
            adx = matchValue[0][0];
            ady = matchValue[0][1];
            bStart = matchValue_1[1][0];
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
            const matchValue_3 = SketchAuthoring_tryPoint(sketch, aStart);
            const matchValue_4 = SketchAuthoring_tryPoint(sketch, bStart);
            let matchResult_1, pa, pb;
            if (matchValue_3 != null) {
                if (matchValue_4 != null) {
                    matchResult_1 = 0;
                    pa = matchValue_3;
                    pb = matchValue_4;
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
                    const denom = max_1(1E-06, Math.sqrt((adx * adx) + (ady * ady)));
                    return Math.abs((tupledArg = SketchAuthoring_sub(pb[0], pb[1], pa[0], pa[1]), SketchAuthoring_cross(tupledArg[0], tupledArg[1], adx, ady))) / denom;
                }
                default:
                    return 10;
            }
        }
        default:
            return 10;
    }
}

function SketchAuthoring_angleValue(sketch, lineA, lineB) {
    const matchValue = SketchAuthoring_lineDirection(sketch, lineA);
    const matchValue_1 = SketchAuthoring_lineDirection(sketch, lineB);
    let matchResult, adx, ady, bdx, bdy;
    if (matchValue != null) {
        if (matchValue_1 != null) {
            matchResult = 0;
            adx = matchValue[0][0];
            ady = matchValue[0][1];
            bdx = matchValue_1[0][0];
            bdy = matchValue_1[0][1];
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
            const la = max_1(1E-06, Math.sqrt((adx * adx) + (ady * ady)));
            const lb = max_1(1E-06, Math.sqrt((bdx * bdx) + (bdy * bdy)));
            const c = SketchAuthoring_clamp(-1, 1, SketchAuthoring_dot(adx, ady, bdx, bdy) / (la * lb));
            return Math.acos(c);
        }
        default:
            return 3.141592653589793 * 0.5;
    }
}

function SketchAuthoring_selectionForSketch(sketchId, targets) {
    const Points = choose((_arg) => {
        let matchResult, entityId_1, id_1;
        if (_arg.tag === 0) {
            if (_arg.fields[0] === sketchId) {
                matchResult = 0;
                entityId_1 = _arg.fields[1];
                id_1 = _arg.fields[0];
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
                return entityId_1;
            default:
                return undefined;
        }
    }, targets);
    const Lines = choose((_arg_1) => {
        let matchResult_1, entityId_3, id_3;
        if (_arg_1.tag === 1) {
            if (_arg_1.fields[0] === sketchId) {
                matchResult_1 = 0;
                entityId_3 = _arg_1.fields[1];
                id_3 = _arg_1.fields[0];
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
                return entityId_3;
            default:
                return undefined;
        }
    }, targets);
    const Circles = choose((_arg_2) => {
        let matchResult_2, entityId_5, id_5;
        if (_arg_2.tag === 2) {
            if (_arg_2.fields[0] === sketchId) {
                matchResult_2 = 0;
                entityId_5 = _arg_2.fields[1];
                id_5 = _arg_2.fields[0];
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
                return entityId_5;
            default:
                return undefined;
        }
    }, targets);
    return {
        Arcs: choose((_arg_3) => {
            let matchResult_3, entityId_7, id_7;
            if (_arg_3.tag === 3) {
                if (_arg_3.fields[0] === sketchId) {
                    matchResult_3 = 0;
                    entityId_7 = _arg_3.fields[1];
                    id_7 = _arg_3.fields[0];
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
                    return entityId_7;
                default:
                    return undefined;
            }
        }, targets),
        Circles: Circles,
        Lines: Lines,
        Points: Points,
    };
}

function SketchAuthoring_selectionForFrames(targets) {
    const Origins = choose((_arg) => {
        if (_arg.tag === 6) {
            return _arg.fields[0];
        }
        else {
            return undefined;
        }
    }, targets);
    return {
        Axes: choose((_arg_1) => {
            if (_arg_1.tag === 7) {
                return [_arg_1.fields[0], _arg_1.fields[1]];
            }
            else {
                return undefined;
            }
        }, targets),
        Origins: Origins,
    };
}

function SketchAuthoring_frameOriginFromSelection(origins, axes) {
    const matchValue = List_distinct(append(origins, map((tuple) => tuple[0], axes)), {
        Equals: (x, y) => (x === y),
        GetHashCode: stringHash,
    });
    let matchResult, frameId;
    if (!isEmpty_1(matchValue)) {
        if (isEmpty_1(tail(matchValue))) {
            matchResult = 0;
            frameId = head(matchValue);
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
            return frameId;
        default:
            return undefined;
    }
}

export function SketchAuthoring_tryEditableDimension(sketchId, sketch, index) {
    return bind((constraint_) => {
        let matchResult, distance;
        switch (constraint_.tag) {
            case 6: {
                matchResult = 0;
                distance = constraint_.fields[2];
                break;
            }
            case 7: {
                matchResult = 0;
                distance = constraint_.fields[3];
                break;
            }
            case 18: {
                matchResult = 0;
                distance = constraint_.fields[6];
                break;
            }
            case 19: {
                matchResult = 0;
                distance = constraint_.fields[5];
                break;
            }
            case 20: {
                matchResult = 0;
                distance = constraint_.fields[4];
                break;
            }
            case 21: {
                matchResult = 0;
                distance = constraint_.fields[3];
                break;
            }
            case 22: {
                matchResult = 0;
                distance = constraint_.fields[5];
                break;
            }
            case 23: {
                matchResult = 0;
                distance = constraint_.fields[4];
                break;
            }
            case 17: {
                matchResult = 1;
                break;
            }
            case 24: {
                matchResult = 2;
                break;
            }
            default:
                matchResult = 3;
        }
        switch (matchResult) {
            case 0:
                return new EditingDimension(sketchId, index, "distance", distance);
            case 1:
                return new EditingDimension(sketchId, index, "diameter", constraint_.fields[2]);
            case 2:
                return new EditingDimension(sketchId, index, "angle", constraint_.fields[6]);
            default:
                return undefined;
        }
    }, tryItem(index, sketch.Constraints));
}

function SketchAuthoring_chooseAngleConstraint(sketch, lineA, lineB, cursor) {
    let tupledArg;
    const matchValue = SketchAuthoring_tryLine(sketch, lineA);
    const matchValue_1 = SketchAuthoring_tryLine(sketch, lineB);
    let matchResult, aEnd, aStart, bEnd, bStart;
    if (matchValue != null) {
        if (matchValue_1 != null) {
            matchResult = 0;
            aEnd = matchValue[1];
            aStart = matchValue[0];
            bEnd = matchValue_1[1];
            bStart = matchValue_1[0];
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
            const matchValue_3 = SketchAuthoring_tryPoint(sketch, aStart);
            const matchValue_4 = SketchAuthoring_tryPoint(sketch, aEnd);
            const matchValue_5 = SketchAuthoring_tryPoint(sketch, bStart);
            const matchValue_6 = SketchAuthoring_tryPoint(sketch, bEnd);
            let matchResult_1, pa0, pa1, pb0, pb1;
            if (matchValue_3 != null) {
                if (matchValue_4 != null) {
                    if (matchValue_5 != null) {
                        if (matchValue_6 != null) {
                            matchResult_1 = 0;
                            pa0 = matchValue_3;
                            pa1 = matchValue_4;
                            pb0 = matchValue_5;
                            pb1 = matchValue_6;
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
                    let lineIntersection;
                    const ad = SketchAuthoring_sub(pa1[0], pa1[1], pa0[0], pa0[1]);
                    const bd = SketchAuthoring_sub(pb1[0], pb1[1], pb0[0], pb0[1]);
                    const det = SketchAuthoring_cross(ad[0], ad[1], bd[0], bd[1]);
                    if (Math.abs(det) < 1E-06) {
                        lineIntersection = undefined;
                    }
                    else {
                        const t = ((tupledArg = SketchAuthoring_sub(pb0[0], pb0[1], pa0[0], pa0[1]), SketchAuthoring_cross(tupledArg[0], tupledArg[1], bd[0], bd[1]))) / det;
                        lineIntersection = [pa0[0] + (ad[0] * t), pa0[1] + (ad[1] * t)];
                    }
                    const sharedVertex = tryFind((tupledArg_1) => (tupledArg_1[0] === tupledArg_1[1]), ofArray([[aStart, bStart, pa0, false, false], [aStart, bEnd, pa0, false, true], [aEnd, bStart, pa1, true, false], [aEnd, bEnd, pa1, true, true]]));
                    const vertex_1 = (sharedVertex == null) ? defaultArg(lineIntersection, pa0) : sharedVertex[2];
                    const candidates = choose((tupledArg_2) => {
                        const aReverse = tupledArg_2[0];
                        const bReverse = tupledArg_2[1];
                        const rayA = aReverse ? SketchAuthoring_sub(pa0[0], pa0[1], pa1[0], pa1[1]) : SketchAuthoring_sub(pa1[0], pa1[1], pa0[0], pa0[1]);
                        const rayB = bReverse ? SketchAuthoring_sub(pb0[0], pb0[1], pb1[0], pb1[1]) : SketchAuthoring_sub(pb1[0], pb1[1], pb0[0], pb0[1]);
                        if ((Math.sqrt(SketchAuthoring_dot(rayA[0], rayA[1], rayA[0], rayA[1])) < 1E-06) ? true : (Math.sqrt(SketchAuthoring_dot(rayB[0], rayB[1], rayB[0], rayB[1])) < 1E-06)) {
                            return undefined;
                        }
                        else {
                            const angleA = SketchAuthoring_angleOf(rayA[0], rayA[1]);
                            const ccwSweep = SketchAuthoring_normalizePositive(SketchAuthoring_angleOf(rayB[0], rayB[1]) - angleA);
                            const ccw = ccwSweep <= 3.141592653589793;
                            const sweep = ccw ? ccwSweep : (SketchAuthoring_tau - ccwSweep);
                            return [aReverse, bReverse, ccw, sweep, SketchAuthoring_normalizePositive(ccw ? (angleA + (sweep * 0.5)) : (angleA - (sweep * 0.5))), angleA, sweep];
                        }
                    }, ofArray([[false, false], [false, true], [true, false], [true, true]]));
                    let chosen;
                    if (cursor == null) {
                        chosen = undefined;
                    }
                    else {
                        const cursorPoint = cursor;
                        let cursorAngle;
                        const tupledArg_3 = SketchAuthoring_sub(cursorPoint[0], cursorPoint[1], vertex_1[0], vertex_1[1]);
                        cursorAngle = SketchAuthoring_angleOf(tupledArg_3[0], tupledArg_3[1]);
                        chosen = tryFind((tupledArg_4) => SketchAuthoring_pointInSector(cursorAngle, tupledArg_4[5], tupledArg_4[6], tupledArg_4[2]), candidates);
                    }
                    let patternInput;
                    if (chosen == null) {
                        if (isEmpty_1(candidates)) {
                            patternInput = [false, false, true, SketchAuthoring_angleValue(sketch, lineA, lineB)];
                        }
                        else {
                            const first = head(candidates);
                            patternInput = [first[0], first[1], first[2], first[3]];
                        }
                    }
                    else {
                        patternInput = [chosen[0], chosen[1], chosen[2], chosen[3]];
                    }
                    return new SketchConstraint(24, [aStart, aEnd, bStart, bEnd, lineA, lineB, patternInput[3], patternInput[0], patternInput[1], patternInput[2], undefined]);
                }
                default:
                    return undefined;
            }
        }
        default:
            return undefined;
    }
}

function SketchAuthoring_buildConstraint(sketch, sketchId, kind, targets, cursor) {
    const selection = SketchAuthoring_selectionForSketch(sketchId, targets);
    const frameSelection = SketchAuthoring_selectionForFrames(targets);
    const frameOrigin = SketchAuthoring_frameOriginFromSelection(frameSelection.Origins, frameSelection.Axes);
    switch (kind) {
        case "Coincident": {
            const matchValue = selection.Points;
            let matchResult, a, b, pointId;
            if (!isEmpty_1(matchValue)) {
                if (isEmpty_1(tail(matchValue))) {
                    matchResult = 1;
                    pointId = head(matchValue);
                }
                else if (isEmpty_1(tail(tail(matchValue)))) {
                    matchResult = 0;
                    a = head(matchValue);
                    b = head(tail(matchValue));
                }
                else {
                    matchResult = 2;
                }
            }
            else {
                matchResult = 2;
            }
            switch (matchResult) {
                case 0:
                    return new SketchConstraint(1, [a, b]);
                case 1: {
                    const matchValue_1 = frameSelection.Origins;
                    let matchResult_1, frameId;
                    if (!isEmpty_1(matchValue_1)) {
                        if (isEmpty_1(tail(matchValue_1))) {
                            matchResult_1 = 0;
                            frameId = head(matchValue_1);
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
                            return new SketchConstraint(2, [pointId, frameId, "origin"]);
                        default:
                            return undefined;
                    }
                }
                default:
                    return undefined;
            }
        }
        case "Horizontal": {
            const matchValue_2 = selection.Points;
            const matchValue_3 = selection.Lines;
            let matchResult_2, a_1, b_1, lineId;
            if (!isEmpty_1(matchValue_2)) {
                if (!isEmpty_1(tail(matchValue_2))) {
                    if (isEmpty_1(tail(tail(matchValue_2)))) {
                        matchResult_2 = 0;
                        a_1 = head(matchValue_2);
                        b_1 = head(tail(matchValue_2));
                    }
                    else if (!isEmpty_1(matchValue_3)) {
                        if (isEmpty_1(tail(matchValue_3))) {
                            matchResult_2 = 1;
                            lineId = head(matchValue_3);
                        }
                        else {
                            matchResult_2 = 2;
                        }
                    }
                    else {
                        matchResult_2 = 2;
                    }
                }
                else if (!isEmpty_1(matchValue_3)) {
                    if (isEmpty_1(tail(matchValue_3))) {
                        matchResult_2 = 1;
                        lineId = head(matchValue_3);
                    }
                    else {
                        matchResult_2 = 2;
                    }
                }
                else {
                    matchResult_2 = 2;
                }
            }
            else if (!isEmpty_1(matchValue_3)) {
                if (isEmpty_1(tail(matchValue_3))) {
                    matchResult_2 = 1;
                    lineId = head(matchValue_3);
                }
                else {
                    matchResult_2 = 2;
                }
            }
            else {
                matchResult_2 = 2;
            }
            switch (matchResult_2) {
                case 0:
                    return new SketchConstraint(4, [a_1, b_1]);
                case 1:
                    return map_1((tupledArg) => (new SketchConstraint(4, [tupledArg[0], tupledArg[1]])), SketchAuthoring_tryLine(sketch, lineId));
                default:
                    return undefined;
            }
        }
        case "Vertical": {
            const matchValue_5 = selection.Points;
            const matchValue_6 = selection.Lines;
            let matchResult_3, a_3, b_3, lineId_1;
            if (!isEmpty_1(matchValue_5)) {
                if (!isEmpty_1(tail(matchValue_5))) {
                    if (isEmpty_1(tail(tail(matchValue_5)))) {
                        matchResult_3 = 0;
                        a_3 = head(matchValue_5);
                        b_3 = head(tail(matchValue_5));
                    }
                    else if (!isEmpty_1(matchValue_6)) {
                        if (isEmpty_1(tail(matchValue_6))) {
                            matchResult_3 = 1;
                            lineId_1 = head(matchValue_6);
                        }
                        else {
                            matchResult_3 = 2;
                        }
                    }
                    else {
                        matchResult_3 = 2;
                    }
                }
                else if (!isEmpty_1(matchValue_6)) {
                    if (isEmpty_1(tail(matchValue_6))) {
                        matchResult_3 = 1;
                        lineId_1 = head(matchValue_6);
                    }
                    else {
                        matchResult_3 = 2;
                    }
                }
                else {
                    matchResult_3 = 2;
                }
            }
            else if (!isEmpty_1(matchValue_6)) {
                if (isEmpty_1(tail(matchValue_6))) {
                    matchResult_3 = 1;
                    lineId_1 = head(matchValue_6);
                }
                else {
                    matchResult_3 = 2;
                }
            }
            else {
                matchResult_3 = 2;
            }
            switch (matchResult_3) {
                case 0:
                    return new SketchConstraint(5, [a_3, b_3]);
                case 1:
                    return map_1((tupledArg_1) => (new SketchConstraint(5, [tupledArg_1[0], tupledArg_1[1]])), SketchAuthoring_tryLine(sketch, lineId_1));
                default:
                    return undefined;
            }
        }
        case "Midpoint": {
            const matchValue_8 = selection.Points;
            const matchValue_9 = selection.Lines;
            let matchResult_4, lineId_2, pointId_1;
            if (!isEmpty_1(matchValue_8)) {
                if (isEmpty_1(tail(matchValue_8))) {
                    if (!isEmpty_1(matchValue_9)) {
                        if (isEmpty_1(tail(matchValue_9))) {
                            matchResult_4 = 0;
                            lineId_2 = head(matchValue_9);
                            pointId_1 = head(matchValue_8);
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
                case 0:
                    return map_1((tupledArg_2) => (new SketchConstraint(10, [pointId_1, lineId_2, tupledArg_2[0], tupledArg_2[1]])), SketchAuthoring_tryLine(sketch, lineId_2));
                default:
                    return undefined;
            }
        }
        case "Parallel": {
            const matchValue_11 = selection.Lines;
            const matchValue_12 = frameSelection.Axes;
            let matchResult_5, lineA_1, lineB, frameId_2, lineA_2, part_1;
            if (!isEmpty_1(matchValue_11)) {
                if (isEmpty_1(tail(matchValue_11))) {
                    if (!isEmpty_1(matchValue_12)) {
                        if (isEmpty_1(tail(matchValue_12))) {
                            if (head(matchValue_12)[1] !== "origin") {
                                matchResult_5 = 1;
                                frameId_2 = head(matchValue_12)[0];
                                lineA_2 = head(matchValue_11);
                                part_1 = head(matchValue_12)[1];
                            }
                            else {
                                matchResult_5 = 2;
                            }
                        }
                        else {
                            matchResult_5 = 2;
                        }
                    }
                    else {
                        matchResult_5 = 2;
                    }
                }
                else if (isEmpty_1(tail(tail(matchValue_11)))) {
                    matchResult_5 = 0;
                    lineA_1 = head(matchValue_11);
                    lineB = head(tail(matchValue_11));
                }
                else {
                    matchResult_5 = 2;
                }
            }
            else {
                matchResult_5 = 2;
            }
            switch (matchResult_5) {
                case 0:
                    return map2((tupledArg_3, tupledArg_4) => (new SketchConstraint(11, [tupledArg_3[0], tupledArg_3[1], tupledArg_4[0], tupledArg_4[1], lineA_1, lineB])), SketchAuthoring_tryLine(sketch, lineA_1), SketchAuthoring_tryLine(sketch, lineB));
                case 1:
                    return map_1((tupledArg_5) => (new SketchConstraint(12, [tupledArg_5[0], tupledArg_5[1], lineA_2, frameId_2, part_1])), SketchAuthoring_tryLine(sketch, lineA_2));
                default:
                    return undefined;
            }
        }
        case "Perpendicular": {
            const matchValue_14 = selection.Lines;
            const matchValue_15 = frameSelection.Axes;
            let matchResult_6, lineA_4, lineB_1, frameId_4, lineA_5, part_3;
            if (!isEmpty_1(matchValue_14)) {
                if (isEmpty_1(tail(matchValue_14))) {
                    if (!isEmpty_1(matchValue_15)) {
                        if (isEmpty_1(tail(matchValue_15))) {
                            if (head(matchValue_15)[1] !== "origin") {
                                matchResult_6 = 1;
                                frameId_4 = head(matchValue_15)[0];
                                lineA_5 = head(matchValue_14);
                                part_3 = head(matchValue_15)[1];
                            }
                            else {
                                matchResult_6 = 2;
                            }
                        }
                        else {
                            matchResult_6 = 2;
                        }
                    }
                    else {
                        matchResult_6 = 2;
                    }
                }
                else if (isEmpty_1(tail(tail(matchValue_14)))) {
                    matchResult_6 = 0;
                    lineA_4 = head(matchValue_14);
                    lineB_1 = head(tail(matchValue_14));
                }
                else {
                    matchResult_6 = 2;
                }
            }
            else {
                matchResult_6 = 2;
            }
            switch (matchResult_6) {
                case 0:
                    return map2((tupledArg_6, tupledArg_7) => (new SketchConstraint(13, [tupledArg_6[0], tupledArg_6[1], tupledArg_7[0], tupledArg_7[1], lineA_4, lineB_1])), SketchAuthoring_tryLine(sketch, lineA_4), SketchAuthoring_tryLine(sketch, lineB_1));
                case 1:
                    return map_1((tupledArg_8) => (new SketchConstraint(14, [tupledArg_8[0], tupledArg_8[1], lineA_5, frameId_4, part_3])), SketchAuthoring_tryLine(sketch, lineA_5));
                default:
                    return undefined;
            }
        }
        case "Equal": {
            const matchValue_17 = selection.Lines;
            const matchValue_18 = selection.Circles;
            const matchValue_19 = selection.Arcs;
            let matchResult_7, lineA_6, lineB_2, circleA, circleB, arcA, arcB;
            if (!isEmpty_1(matchValue_17)) {
                if (!isEmpty_1(tail(matchValue_17))) {
                    if (isEmpty_1(tail(tail(matchValue_17)))) {
                        matchResult_7 = 0;
                        lineA_6 = head(matchValue_17);
                        lineB_2 = head(tail(matchValue_17));
                    }
                    else if (!isEmpty_1(matchValue_18)) {
                        if (!isEmpty_1(tail(matchValue_18))) {
                            if (isEmpty_1(tail(tail(matchValue_18)))) {
                                matchResult_7 = 1;
                                circleA = head(matchValue_18);
                                circleB = head(tail(matchValue_18));
                            }
                            else if (!isEmpty_1(matchValue_19)) {
                                if (!isEmpty_1(tail(matchValue_19))) {
                                    if (isEmpty_1(tail(tail(matchValue_19)))) {
                                        matchResult_7 = 2;
                                        arcA = head(matchValue_19);
                                        arcB = head(tail(matchValue_19));
                                    }
                                    else {
                                        matchResult_7 = 3;
                                    }
                                }
                                else {
                                    matchResult_7 = 3;
                                }
                            }
                            else {
                                matchResult_7 = 3;
                            }
                        }
                        else if (!isEmpty_1(matchValue_19)) {
                            if (!isEmpty_1(tail(matchValue_19))) {
                                if (isEmpty_1(tail(tail(matchValue_19)))) {
                                    matchResult_7 = 2;
                                    arcA = head(matchValue_19);
                                    arcB = head(tail(matchValue_19));
                                }
                                else {
                                    matchResult_7 = 3;
                                }
                            }
                            else {
                                matchResult_7 = 3;
                            }
                        }
                        else {
                            matchResult_7 = 3;
                        }
                    }
                    else if (!isEmpty_1(matchValue_19)) {
                        if (!isEmpty_1(tail(matchValue_19))) {
                            if (isEmpty_1(tail(tail(matchValue_19)))) {
                                matchResult_7 = 2;
                                arcA = head(matchValue_19);
                                arcB = head(tail(matchValue_19));
                            }
                            else {
                                matchResult_7 = 3;
                            }
                        }
                        else {
                            matchResult_7 = 3;
                        }
                    }
                    else {
                        matchResult_7 = 3;
                    }
                }
                else if (!isEmpty_1(matchValue_18)) {
                    if (!isEmpty_1(tail(matchValue_18))) {
                        if (isEmpty_1(tail(tail(matchValue_18)))) {
                            matchResult_7 = 1;
                            circleA = head(matchValue_18);
                            circleB = head(tail(matchValue_18));
                        }
                        else if (!isEmpty_1(matchValue_19)) {
                            if (!isEmpty_1(tail(matchValue_19))) {
                                if (isEmpty_1(tail(tail(matchValue_19)))) {
                                    matchResult_7 = 2;
                                    arcA = head(matchValue_19);
                                    arcB = head(tail(matchValue_19));
                                }
                                else {
                                    matchResult_7 = 3;
                                }
                            }
                            else {
                                matchResult_7 = 3;
                            }
                        }
                        else {
                            matchResult_7 = 3;
                        }
                    }
                    else if (!isEmpty_1(matchValue_19)) {
                        if (!isEmpty_1(tail(matchValue_19))) {
                            if (isEmpty_1(tail(tail(matchValue_19)))) {
                                matchResult_7 = 2;
                                arcA = head(matchValue_19);
                                arcB = head(tail(matchValue_19));
                            }
                            else {
                                matchResult_7 = 3;
                            }
                        }
                        else {
                            matchResult_7 = 3;
                        }
                    }
                    else {
                        matchResult_7 = 3;
                    }
                }
                else if (!isEmpty_1(matchValue_19)) {
                    if (!isEmpty_1(tail(matchValue_19))) {
                        if (isEmpty_1(tail(tail(matchValue_19)))) {
                            matchResult_7 = 2;
                            arcA = head(matchValue_19);
                            arcB = head(tail(matchValue_19));
                        }
                        else {
                            matchResult_7 = 3;
                        }
                    }
                    else {
                        matchResult_7 = 3;
                    }
                }
                else {
                    matchResult_7 = 3;
                }
            }
            else if (!isEmpty_1(matchValue_18)) {
                if (!isEmpty_1(tail(matchValue_18))) {
                    if (isEmpty_1(tail(tail(matchValue_18)))) {
                        matchResult_7 = 1;
                        circleA = head(matchValue_18);
                        circleB = head(tail(matchValue_18));
                    }
                    else if (!isEmpty_1(matchValue_19)) {
                        if (!isEmpty_1(tail(matchValue_19))) {
                            if (isEmpty_1(tail(tail(matchValue_19)))) {
                                matchResult_7 = 2;
                                arcA = head(matchValue_19);
                                arcB = head(tail(matchValue_19));
                            }
                            else {
                                matchResult_7 = 3;
                            }
                        }
                        else {
                            matchResult_7 = 3;
                        }
                    }
                    else {
                        matchResult_7 = 3;
                    }
                }
                else if (!isEmpty_1(matchValue_19)) {
                    if (!isEmpty_1(tail(matchValue_19))) {
                        if (isEmpty_1(tail(tail(matchValue_19)))) {
                            matchResult_7 = 2;
                            arcA = head(matchValue_19);
                            arcB = head(tail(matchValue_19));
                        }
                        else {
                            matchResult_7 = 3;
                        }
                    }
                    else {
                        matchResult_7 = 3;
                    }
                }
                else {
                    matchResult_7 = 3;
                }
            }
            else if (!isEmpty_1(matchValue_19)) {
                if (!isEmpty_1(tail(matchValue_19))) {
                    if (isEmpty_1(tail(tail(matchValue_19)))) {
                        matchResult_7 = 2;
                        arcA = head(matchValue_19);
                        arcB = head(tail(matchValue_19));
                    }
                    else {
                        matchResult_7 = 3;
                    }
                }
                else {
                    matchResult_7 = 3;
                }
            }
            else {
                matchResult_7 = 3;
            }
            switch (matchResult_7) {
                case 0:
                    return map2((tupledArg_9, tupledArg_10) => (new SketchConstraint(8, [tupledArg_9[0], tupledArg_9[1], tupledArg_10[0], tupledArg_10[1], lineA_6, lineB_2])), SketchAuthoring_tryLine(sketch, lineA_6), SketchAuthoring_tryLine(sketch, lineB_2));
                case 1:
                    return new SketchConstraint(9, [circleA, circleB]);
                case 2:
                    return new SketchConstraint(9, [arcA, arcB]);
                default:
                    return undefined;
            }
        }
        case "Tangent": {
            const curveIds = append(selection.Circles, selection.Arcs);
            const matchValue_21 = selection.Lines;
            let matchResult_8, curveId, lineId_3, curveA, curveB;
            if (isEmpty_1(matchValue_21)) {
                if (!isEmpty_1(curveIds)) {
                    if (!isEmpty_1(tail(curveIds))) {
                        if (isEmpty_1(tail(tail(curveIds)))) {
                            matchResult_8 = 1;
                            curveA = head(curveIds);
                            curveB = head(tail(curveIds));
                        }
                        else {
                            matchResult_8 = 2;
                        }
                    }
                    else {
                        matchResult_8 = 2;
                    }
                }
                else {
                    matchResult_8 = 2;
                }
            }
            else if (isEmpty_1(tail(matchValue_21))) {
                if (!isEmpty_1(curveIds)) {
                    if (isEmpty_1(tail(curveIds))) {
                        matchResult_8 = 0;
                        curveId = head(curveIds);
                        lineId_3 = head(matchValue_21);
                    }
                    else {
                        matchResult_8 = 2;
                    }
                }
                else {
                    matchResult_8 = 2;
                }
            }
            else {
                matchResult_8 = 2;
            }
            switch (matchResult_8) {
                case 0: {
                    const matchValue_23 = SketchAuthoring_tryLine(sketch, lineId_3);
                    const matchValue_24 = SketchAuthoring_tryCurve(sketch, curveId);
                    let matchResult_9, aEnd_6, aStart_6, centerId, radius;
                    if (matchValue_23 != null) {
                        if (matchValue_24 != null) {
                            matchResult_9 = 0;
                            aEnd_6 = matchValue_23[1];
                            aStart_6 = matchValue_23[0];
                            centerId = matchValue_24[0];
                            radius = matchValue_24[1];
                        }
                        else {
                            matchResult_9 = 1;
                        }
                    }
                    else {
                        matchResult_9 = 1;
                    }
                    switch (matchResult_9) {
                        case 0:
                            return new SketchConstraint(15, [aStart_6, aEnd_6, centerId, curveId, lineId_3, radius]);
                        default:
                            return undefined;
                    }
                }
                case 1: {
                    const matchValue_26 = SketchAuthoring_tryCurve(sketch, curveA);
                    const matchValue_27 = SketchAuthoring_tryCurve(sketch, curveB);
                    let matchResult_10, centerA, centerB, radiusA, radiusB;
                    if (matchValue_26 != null) {
                        if (matchValue_27 != null) {
                            matchResult_10 = 0;
                            centerA = matchValue_26[0];
                            centerB = matchValue_27[0];
                            radiusA = matchValue_26[1];
                            radiusB = matchValue_27[1];
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
                            const matchValue_29 = SketchAuthoring_tryPoint(sketch, centerA);
                            const matchValue_30 = SketchAuthoring_tryPoint(sketch, centerB);
                            let matchResult_11, pa, pb;
                            if (matchValue_29 != null) {
                                if (matchValue_30 != null) {
                                    matchResult_11 = 0;
                                    pa = matchValue_29;
                                    pb = matchValue_30;
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
                                    const centerDistance = SketchAuthoring_dist(pa[0], pa[1], pb[0], pb[1]);
                                    const externalDistance = radiusA + radiusB;
                                    const internalDistance = Math.abs(radiusA - radiusB);
                                    return new SketchConstraint(16, [curveA, centerA, curveB, centerB, Math.abs(centerDistance - internalDistance) < Math.abs(centerDistance - externalDistance)]);
                                }
                                default:
                                    return undefined;
                            }
                        }
                        default:
                            return undefined;
                    }
                }
                default:
                    return undefined;
            }
        }
        case "Concentric": {
            const matchValue_32 = selection.Circles;
            let matchResult_12, circleA_1, circleB_1;
            if (!isEmpty_1(matchValue_32)) {
                if (!isEmpty_1(tail(matchValue_32))) {
                    if (isEmpty_1(tail(tail(matchValue_32)))) {
                        matchResult_12 = 0;
                        circleA_1 = head(matchValue_32);
                        circleB_1 = head(tail(matchValue_32));
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
                    const matchValue_33 = SketchAuthoring_tryCircle(sketch, circleA_1);
                    const matchValue_34 = SketchAuthoring_tryCircle(sketch, circleB_1);
                    let matchResult_13, centerA_1, centerB_1;
                    if (matchValue_33 != null) {
                        if (matchValue_34 != null) {
                            matchResult_13 = 0;
                            centerA_1 = matchValue_33[0];
                            centerB_1 = matchValue_34[0];
                        }
                        else {
                            matchResult_13 = 1;
                        }
                    }
                    else {
                        matchResult_13 = 1;
                    }
                    switch (matchResult_13) {
                        case 0:
                            return new SketchConstraint(3, [circleA_1, circleB_1, centerA_1, centerB_1]);
                        default:
                            return undefined;
                    }
                }
                default:
                    return undefined;
            }
        }
        case "Fixed": {
            const matchValue_36 = selection.Points;
            let matchResult_14, pointId_2;
            if (!isEmpty_1(matchValue_36)) {
                if (isEmpty_1(tail(matchValue_36))) {
                    matchResult_14 = 0;
                    pointId_2 = head(matchValue_36);
                }
                else {
                    matchResult_14 = 1;
                }
            }
            else {
                matchResult_14 = 1;
            }
            switch (matchResult_14) {
                case 0:
                    return map_1((tupledArg_11) => (new SketchConstraint(0, [pointId_2, tupledArg_11[0], tupledArg_11[1]])), SketchAuthoring_tryPoint(sketch, pointId_2));
                default:
                    return undefined;
            }
        }
        case "distance": {
            const matchValue_37 = selection.Points;
            const matchValue_38 = selection.Lines;
            const matchValue_39 = selection.Circles;
            const matchValue_40 = selection.Arcs;
            let matchResult_15, a_5, b_5, frameId_5, pointId_3, lineA_7, lineB_3, frameId_6, lineA_8, circleId, arcId;
            if (!isEmpty_1(matchValue_37)) {
                if (isEmpty_1(tail(matchValue_37))) {
                    if (frameOrigin != null) {
                        matchResult_15 = 1;
                        frameId_5 = frameOrigin;
                        pointId_3 = head(matchValue_37);
                    }
                    else if (!isEmpty_1(matchValue_38)) {
                        if (!isEmpty_1(tail(matchValue_38))) {
                            if (isEmpty_1(tail(tail(matchValue_38)))) {
                                matchResult_15 = 2;
                                lineA_7 = head(matchValue_38);
                                lineB_3 = head(tail(matchValue_38));
                            }
                            else if (isEmpty_1(matchValue_39)) {
                                if (!isEmpty_1(matchValue_40)) {
                                    if (isEmpty_1(tail(matchValue_40))) {
                                        matchResult_15 = 5;
                                        arcId = head(matchValue_40);
                                    }
                                    else {
                                        matchResult_15 = 6;
                                    }
                                }
                                else {
                                    matchResult_15 = 6;
                                }
                            }
                            else if (isEmpty_1(tail(matchValue_39))) {
                                if (isEmpty_1(matchValue_40)) {
                                    matchResult_15 = 4;
                                    circleId = head(matchValue_39);
                                }
                                else {
                                    matchResult_15 = 6;
                                }
                            }
                            else {
                                matchResult_15 = 6;
                            }
                        }
                        else if (isEmpty_1(matchValue_39)) {
                            if (!isEmpty_1(matchValue_40)) {
                                if (isEmpty_1(tail(matchValue_40))) {
                                    matchResult_15 = 5;
                                    arcId = head(matchValue_40);
                                }
                                else {
                                    matchResult_15 = 6;
                                }
                            }
                            else {
                                matchResult_15 = 6;
                            }
                        }
                        else if (isEmpty_1(tail(matchValue_39))) {
                            if (isEmpty_1(matchValue_40)) {
                                matchResult_15 = 4;
                                circleId = head(matchValue_39);
                            }
                            else {
                                matchResult_15 = 6;
                            }
                        }
                        else {
                            matchResult_15 = 6;
                        }
                    }
                    else if (isEmpty_1(matchValue_39)) {
                        if (!isEmpty_1(matchValue_40)) {
                            if (isEmpty_1(tail(matchValue_40))) {
                                matchResult_15 = 5;
                                arcId = head(matchValue_40);
                            }
                            else {
                                matchResult_15 = 6;
                            }
                        }
                        else {
                            matchResult_15 = 6;
                        }
                    }
                    else if (isEmpty_1(tail(matchValue_39))) {
                        if (isEmpty_1(matchValue_40)) {
                            matchResult_15 = 4;
                            circleId = head(matchValue_39);
                        }
                        else {
                            matchResult_15 = 6;
                        }
                    }
                    else {
                        matchResult_15 = 6;
                    }
                }
                else if (isEmpty_1(tail(tail(matchValue_37)))) {
                    matchResult_15 = 0;
                    a_5 = head(matchValue_37);
                    b_5 = head(tail(matchValue_37));
                }
                else if (!isEmpty_1(matchValue_38)) {
                    if (isEmpty_1(tail(matchValue_38))) {
                        if (frameOrigin != null) {
                            matchResult_15 = 3;
                            frameId_6 = frameOrigin;
                            lineA_8 = head(matchValue_38);
                        }
                        else if (isEmpty_1(matchValue_39)) {
                            if (!isEmpty_1(matchValue_40)) {
                                if (isEmpty_1(tail(matchValue_40))) {
                                    matchResult_15 = 5;
                                    arcId = head(matchValue_40);
                                }
                                else {
                                    matchResult_15 = 6;
                                }
                            }
                            else {
                                matchResult_15 = 6;
                            }
                        }
                        else if (isEmpty_1(tail(matchValue_39))) {
                            if (isEmpty_1(matchValue_40)) {
                                matchResult_15 = 4;
                                circleId = head(matchValue_39);
                            }
                            else {
                                matchResult_15 = 6;
                            }
                        }
                        else {
                            matchResult_15 = 6;
                        }
                    }
                    else if (isEmpty_1(tail(tail(matchValue_38)))) {
                        matchResult_15 = 2;
                        lineA_7 = head(matchValue_38);
                        lineB_3 = head(tail(matchValue_38));
                    }
                    else if (isEmpty_1(matchValue_39)) {
                        if (!isEmpty_1(matchValue_40)) {
                            if (isEmpty_1(tail(matchValue_40))) {
                                matchResult_15 = 5;
                                arcId = head(matchValue_40);
                            }
                            else {
                                matchResult_15 = 6;
                            }
                        }
                        else {
                            matchResult_15 = 6;
                        }
                    }
                    else if (isEmpty_1(tail(matchValue_39))) {
                        if (isEmpty_1(matchValue_40)) {
                            matchResult_15 = 4;
                            circleId = head(matchValue_39);
                        }
                        else {
                            matchResult_15 = 6;
                        }
                    }
                    else {
                        matchResult_15 = 6;
                    }
                }
                else if (isEmpty_1(matchValue_39)) {
                    if (!isEmpty_1(matchValue_40)) {
                        if (isEmpty_1(tail(matchValue_40))) {
                            matchResult_15 = 5;
                            arcId = head(matchValue_40);
                        }
                        else {
                            matchResult_15 = 6;
                        }
                    }
                    else {
                        matchResult_15 = 6;
                    }
                }
                else if (isEmpty_1(tail(matchValue_39))) {
                    if (isEmpty_1(matchValue_40)) {
                        matchResult_15 = 4;
                        circleId = head(matchValue_39);
                    }
                    else {
                        matchResult_15 = 6;
                    }
                }
                else {
                    matchResult_15 = 6;
                }
            }
            else if (!isEmpty_1(matchValue_38)) {
                if (isEmpty_1(tail(matchValue_38))) {
                    if (frameOrigin != null) {
                        matchResult_15 = 3;
                        frameId_6 = frameOrigin;
                        lineA_8 = head(matchValue_38);
                    }
                    else if (isEmpty_1(matchValue_39)) {
                        if (!isEmpty_1(matchValue_40)) {
                            if (isEmpty_1(tail(matchValue_40))) {
                                matchResult_15 = 5;
                                arcId = head(matchValue_40);
                            }
                            else {
                                matchResult_15 = 6;
                            }
                        }
                        else {
                            matchResult_15 = 6;
                        }
                    }
                    else if (isEmpty_1(tail(matchValue_39))) {
                        if (isEmpty_1(matchValue_40)) {
                            matchResult_15 = 4;
                            circleId = head(matchValue_39);
                        }
                        else {
                            matchResult_15 = 6;
                        }
                    }
                    else {
                        matchResult_15 = 6;
                    }
                }
                else if (isEmpty_1(tail(tail(matchValue_38)))) {
                    matchResult_15 = 2;
                    lineA_7 = head(matchValue_38);
                    lineB_3 = head(tail(matchValue_38));
                }
                else if (isEmpty_1(matchValue_39)) {
                    if (!isEmpty_1(matchValue_40)) {
                        if (isEmpty_1(tail(matchValue_40))) {
                            matchResult_15 = 5;
                            arcId = head(matchValue_40);
                        }
                        else {
                            matchResult_15 = 6;
                        }
                    }
                    else {
                        matchResult_15 = 6;
                    }
                }
                else if (isEmpty_1(tail(matchValue_39))) {
                    if (isEmpty_1(matchValue_40)) {
                        matchResult_15 = 4;
                        circleId = head(matchValue_39);
                    }
                    else {
                        matchResult_15 = 6;
                    }
                }
                else {
                    matchResult_15 = 6;
                }
            }
            else if (isEmpty_1(matchValue_39)) {
                if (!isEmpty_1(matchValue_40)) {
                    if (isEmpty_1(tail(matchValue_40))) {
                        matchResult_15 = 5;
                        arcId = head(matchValue_40);
                    }
                    else {
                        matchResult_15 = 6;
                    }
                }
                else {
                    matchResult_15 = 6;
                }
            }
            else if (isEmpty_1(tail(matchValue_39))) {
                if (isEmpty_1(matchValue_40)) {
                    matchResult_15 = 4;
                    circleId = head(matchValue_39);
                }
                else {
                    matchResult_15 = 6;
                }
            }
            else {
                matchResult_15 = 6;
            }
            switch (matchResult_15) {
                case 0:
                    return map2((pa_1, pb_1) => (new SketchConstraint(6, [a_5, b_5, SketchAuthoring_dist(pa_1[0], pa_1[1], pb_1[0], pb_1[1]), undefined])), SketchAuthoring_tryPoint(sketch, a_5), SketchAuthoring_tryPoint(sketch, b_5));
                case 1:
                    return new SketchConstraint(7, [pointId_3, frameId_5, "origin", 0, undefined]);
                case 2: {
                    const matchValue_42 = SketchAuthoring_tryLine(sketch, lineA_7);
                    const matchValue_43 = SketchAuthoring_tryLine(sketch, lineB_3);
                    let matchResult_16, aEnd_7, aStart_7, bEnd_3, bStart_3;
                    if (matchValue_42 != null) {
                        if (matchValue_43 != null) {
                            matchResult_16 = 0;
                            aEnd_7 = matchValue_42[1];
                            aStart_7 = matchValue_42[0];
                            bEnd_3 = matchValue_43[1];
                            bStart_3 = matchValue_43[0];
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
                            return new SketchConstraint(18, [aStart_7, aEnd_7, bStart_3, bEnd_3, lineA_7, lineB_3, SketchAuthoring_lineDistanceValue(sketch, lineA_7, lineB_3), undefined]);
                        default:
                            return undefined;
                    }
                }
                case 3:
                    return map_1((tupledArg_12) => (new SketchConstraint(19, [lineA_8, tupledArg_12[0], tupledArg_12[1], frameId_6, "origin", 0, undefined])), SketchAuthoring_tryLine(sketch, lineA_8));
                case 4: {
                    const matchValue_45 = SketchAuthoring_tryDiameterEntity(sketch, circleId);
                    if (matchValue_45 != null) {
                        return new SketchConstraint(17, [circleId, matchValue_45[0], matchValue_45[1] * 2, undefined]);
                    }
                    else {
                        return undefined;
                    }
                }
                case 5: {
                    const matchValue_46 = SketchAuthoring_tryDiameterEntity(sketch, arcId);
                    if (matchValue_46 != null) {
                        return new SketchConstraint(17, [arcId, matchValue_46[0], matchValue_46[1] * 2, undefined]);
                    }
                    else {
                        return undefined;
                    }
                }
                default:
                    return undefined;
            }
        }
        case "angle": {
            const matchValue_47 = selection.Lines;
            let matchResult_17, lineA_9, lineB_4;
            if (!isEmpty_1(matchValue_47)) {
                if (!isEmpty_1(tail(matchValue_47))) {
                    if (isEmpty_1(tail(tail(matchValue_47)))) {
                        matchResult_17 = 0;
                        lineA_9 = head(matchValue_47);
                        lineB_4 = head(tail(matchValue_47));
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
                case 0:
                    return SketchAuthoring_chooseAngleConstraint(sketch, lineA_9, lineB_4, cursor);
                default:
                    return undefined;
            }
        }
        default:
            return undefined;
    }
}

function SketchAuthoring_buildDistanceConstraintFromDraft(sketch, draft, hoveredRef) {
    const normalizeFrameRef = (_arg) => {
        switch (_arg.tag) {
            case 4:
                return new ConstraintPlacementRef(4, [_arg.fields[0]]);
            case 5:
                return new ConstraintPlacementRef(4, [_arg.fields[0]]);
            default:
                return _arg;
        }
    };
    const hoveredRef_1 = map_1(normalizeFrameRef, hoveredRef);
    const clickedRefs = map(normalizeFrameRef, draft.ClickedRefs);
    let matchResult, frameId_2, lineA_2, frameId_3, pointId, lineA_3, lineB_2, lineId, circleId, arcId, a_3, b_3, frameId_4, lineA_4, frameId_5, pointId_1, lineA_5, lineB_3, a_4, b_4;
    if (!isEmpty_1(clickedRefs)) {
        switch (head(clickedRefs).tag) {
            case 1: {
                if (!isEmpty_1(tail(clickedRefs))) {
                    switch (head(tail(clickedRefs)).tag) {
                        case 4: {
                            if (isEmpty_1(tail(tail(clickedRefs)))) {
                                matchResult = 7;
                                frameId_4 = head(tail(clickedRefs)).fields[0];
                                lineA_4 = head(clickedRefs).fields[0];
                            }
                            else {
                                matchResult = 11;
                            }
                            break;
                        }
                        case 1: {
                            if (isEmpty_1(tail(tail(clickedRefs)))) {
                                if (head(clickedRefs).fields[0] !== head(tail(clickedRefs)).fields[0]) {
                                    matchResult = 9;
                                    lineA_5 = head(clickedRefs).fields[0];
                                    lineB_3 = head(tail(clickedRefs)).fields[0];
                                }
                                else {
                                    matchResult = 11;
                                }
                            }
                            else {
                                matchResult = 11;
                            }
                            break;
                        }
                        default:
                            matchResult = 11;
                    }
                }
                else if (hoveredRef_1 != null) {
                    switch (hoveredRef_1.tag) {
                        case 4: {
                            matchResult = 0;
                            frameId_2 = hoveredRef_1.fields[0];
                            lineA_2 = head(clickedRefs).fields[0];
                            break;
                        }
                        case 1: {
                            if (head(clickedRefs).fields[0] !== hoveredRef_1.fields[0]) {
                                matchResult = 2;
                                lineA_3 = head(clickedRefs).fields[0];
                                lineB_2 = hoveredRef_1.fields[0];
                            }
                            else {
                                matchResult = 3;
                                lineId = head(clickedRefs).fields[0];
                            }
                            break;
                        }
                        default: {
                            matchResult = 3;
                            lineId = head(clickedRefs).fields[0];
                        }
                    }
                }
                else {
                    matchResult = 3;
                    lineId = head(clickedRefs).fields[0];
                }
                break;
            }
            case 4: {
                if (isEmpty_1(tail(clickedRefs))) {
                    if (hoveredRef_1 != null) {
                        switch (hoveredRef_1.tag) {
                            case 1: {
                                matchResult = 0;
                                frameId_2 = head(clickedRefs).fields[0];
                                lineA_2 = hoveredRef_1.fields[0];
                                break;
                            }
                            case 0: {
                                matchResult = 1;
                                frameId_3 = head(clickedRefs).fields[0];
                                pointId = hoveredRef_1.fields[0];
                                break;
                            }
                            default:
                                matchResult = 11;
                        }
                    }
                    else {
                        matchResult = 11;
                    }
                }
                else {
                    matchResult = 11;
                }
                break;
            }
            case 0: {
                if (!isEmpty_1(tail(clickedRefs))) {
                    switch (head(tail(clickedRefs)).tag) {
                        case 4: {
                            if (isEmpty_1(tail(tail(clickedRefs)))) {
                                matchResult = 8;
                                frameId_5 = head(tail(clickedRefs)).fields[0];
                                pointId_1 = head(clickedRefs).fields[0];
                            }
                            else {
                                matchResult = 11;
                            }
                            break;
                        }
                        case 0: {
                            if (isEmpty_1(tail(tail(clickedRefs)))) {
                                if (head(clickedRefs).fields[0] !== head(tail(clickedRefs)).fields[0]) {
                                    matchResult = 10;
                                    a_4 = head(clickedRefs).fields[0];
                                    b_4 = head(tail(clickedRefs)).fields[0];
                                }
                                else {
                                    matchResult = 11;
                                }
                            }
                            else {
                                matchResult = 11;
                            }
                            break;
                        }
                        default:
                            matchResult = 11;
                    }
                }
                else if (hoveredRef_1 != null) {
                    switch (hoveredRef_1.tag) {
                        case 4: {
                            matchResult = 1;
                            frameId_3 = hoveredRef_1.fields[0];
                            pointId = head(clickedRefs).fields[0];
                            break;
                        }
                        case 0: {
                            if (head(clickedRefs).fields[0] !== hoveredRef_1.fields[0]) {
                                matchResult = 6;
                                a_3 = head(clickedRefs).fields[0];
                                b_3 = hoveredRef_1.fields[0];
                            }
                            else {
                                matchResult = 11;
                            }
                            break;
                        }
                        default:
                            matchResult = 11;
                    }
                }
                else {
                    matchResult = 11;
                }
                break;
            }
            case 2: {
                if (isEmpty_1(tail(clickedRefs))) {
                    matchResult = 4;
                    circleId = head(clickedRefs).fields[0];
                }
                else {
                    matchResult = 11;
                }
                break;
            }
            case 3: {
                if (isEmpty_1(tail(clickedRefs))) {
                    matchResult = 5;
                    arcId = head(clickedRefs).fields[0];
                }
                else {
                    matchResult = 11;
                }
                break;
            }
            default:
                matchResult = 11;
        }
    }
    else {
        matchResult = 11;
    }
    switch (matchResult) {
        case 0: {
            const matchValue_1 = SketchAuthoring_tryLine(sketch, lineA_2);
            if (matchValue_1 != null) {
                return new SketchConstraint(19, [lineA_2, matchValue_1[0], matchValue_1[1], frameId_2, "origin", 0, undefined]);
            }
            else {
                return undefined;
            }
        }
        case 1:
            return new SketchConstraint(7, [pointId, frameId_3, "origin", 0, undefined]);
        case 2: {
            const matchValue_2 = SketchAuthoring_tryLine(sketch, lineA_3);
            const matchValue_3 = SketchAuthoring_tryLine(sketch, lineB_2);
            let matchResult_1, aEnd_1, aStart_1, bEnd, bStart;
            if (matchValue_2 != null) {
                if (matchValue_3 != null) {
                    matchResult_1 = 0;
                    aEnd_1 = matchValue_2[1];
                    aStart_1 = matchValue_2[0];
                    bEnd = matchValue_3[1];
                    bStart = matchValue_3[0];
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
                    return new SketchConstraint(18, [aStart_1, aEnd_1, bStart, bEnd, lineA_3, lineB_2, SketchAuthoring_lineDistanceValue(sketch, lineA_3, lineB_2), undefined]);
                default:
                    return undefined;
            }
        }
        case 3:
            return bind((tupledArg) => {
                const a_2 = tupledArg[0];
                const b_2 = tupledArg[1];
                return map2((pa, pb) => (new SketchConstraint(6, [a_2, b_2, SketchAuthoring_dist(pa[0], pa[1], pb[0], pb[1]), undefined])), SketchAuthoring_tryPoint(sketch, a_2), SketchAuthoring_tryPoint(sketch, b_2));
            }, SketchAuthoring_tryLine(sketch, lineId));
        case 4: {
            const matchValue_5 = SketchAuthoring_tryDiameterEntity(sketch, circleId);
            if (matchValue_5 != null) {
                return new SketchConstraint(17, [circleId, matchValue_5[0], matchValue_5[1] * 2, undefined]);
            }
            else {
                return undefined;
            }
        }
        case 5: {
            const matchValue_6 = SketchAuthoring_tryDiameterEntity(sketch, arcId);
            if (matchValue_6 != null) {
                return new SketchConstraint(17, [arcId, matchValue_6[0], matchValue_6[1] * 2, undefined]);
            }
            else {
                return undefined;
            }
        }
        case 6:
            return map2((pa_1, pb_1) => (new SketchConstraint(6, [a_3, b_3, SketchAuthoring_dist(pa_1[0], pa_1[1], pb_1[0], pb_1[1]), undefined])), SketchAuthoring_tryPoint(sketch, a_3), SketchAuthoring_tryPoint(sketch, b_3));
        case 7: {
            const matchValue_7 = SketchAuthoring_tryLine(sketch, lineA_4);
            if (matchValue_7 != null) {
                return new SketchConstraint(19, [lineA_4, matchValue_7[0], matchValue_7[1], frameId_4, "origin", 0, undefined]);
            }
            else {
                return undefined;
            }
        }
        case 8:
            return new SketchConstraint(7, [pointId_1, frameId_5, "origin", 0, undefined]);
        case 9: {
            const matchValue_8 = SketchAuthoring_tryLine(sketch, lineA_5);
            const matchValue_9 = SketchAuthoring_tryLine(sketch, lineB_3);
            let matchResult_2, aEnd_3, aStart_3, bEnd_1, bStart_1;
            if (matchValue_8 != null) {
                if (matchValue_9 != null) {
                    matchResult_2 = 0;
                    aEnd_3 = matchValue_8[1];
                    aStart_3 = matchValue_8[0];
                    bEnd_1 = matchValue_9[1];
                    bStart_1 = matchValue_9[0];
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
                    return new SketchConstraint(18, [aStart_3, aEnd_3, bStart_1, bEnd_1, lineA_5, lineB_3, SketchAuthoring_lineDistanceValue(sketch, lineA_5, lineB_3), undefined]);
                default:
                    return undefined;
            }
        }
        case 10:
            return map2((pa_2, pb_2) => (new SketchConstraint(6, [a_4, b_4, SketchAuthoring_dist(pa_2[0], pa_2[1], pb_2[0], pb_2[1]), undefined])), SketchAuthoring_tryPoint(sketch, a_4), SketchAuthoring_tryPoint(sketch, b_4));
        default:
            return undefined;
    }
}

function SketchAuthoring_buildAngleConstraintFromDraft(sketch, draft, hoveredRef, cursor) {
    const matchValue = draft.ClickedRefs;
    let matchResult, lineA_2, lineB_2, lineA_3, lineB_3;
    if (!isEmpty_1(matchValue)) {
        if (head(matchValue).tag === 1) {
            if (!isEmpty_1(tail(matchValue))) {
                if (head(tail(matchValue)).tag === 1) {
                    if (isEmpty_1(tail(tail(matchValue)))) {
                        if (head(matchValue).fields[0] !== head(tail(matchValue)).fields[0]) {
                            matchResult = 1;
                            lineA_3 = head(matchValue).fields[0];
                            lineB_3 = head(tail(matchValue)).fields[0];
                        }
                        else {
                            matchResult = 2;
                        }
                    }
                    else {
                        matchResult = 2;
                    }
                }
                else {
                    matchResult = 2;
                }
            }
            else if (hoveredRef != null) {
                if (hoveredRef.tag === 1) {
                    if (head(matchValue).fields[0] !== hoveredRef.fields[0]) {
                        matchResult = 0;
                        lineA_2 = head(matchValue).fields[0];
                        lineB_2 = hoveredRef.fields[0];
                    }
                    else {
                        matchResult = 2;
                    }
                }
                else {
                    matchResult = 2;
                }
            }
            else {
                matchResult = 2;
            }
        }
        else {
            matchResult = 2;
        }
    }
    else {
        matchResult = 2;
    }
    switch (matchResult) {
        case 0:
            return SketchAuthoring_chooseAngleConstraint(sketch, lineA_2, lineB_2, cursor);
        case 1:
            return SketchAuthoring_chooseAngleConstraint(sketch, lineA_3, lineB_3, cursor);
        default:
            return undefined;
    }
}

export function SketchAuthoring_pendingConstraintForDraft(sketch, draft, hoveredRef, cursor) {
    const matchValue = draft.Kind;
    switch (matchValue) {
        case "distance":
            return SketchAuthoring_buildDistanceConstraintFromDraft(sketch, draft, hoveredRef);
        case "angle":
            return SketchAuthoring_buildAngleConstraintFromDraft(sketch, draft, hoveredRef, cursor);
        default:
            return undefined;
    }
}

export function SketchAuthoring_placementRefFromTarget(sketchId, _arg) {
    let matchResult, entityId_4, id_4, entityId_5, id_5, entityId_6, id_6, entityId_7, id_7, frameId, frameId_1, part;
    switch (_arg.tag) {
        case 0: {
            if (_arg.fields[0] === sketchId) {
                matchResult = 0;
                entityId_4 = _arg.fields[1];
                id_4 = _arg.fields[0];
            }
            else {
                matchResult = 6;
            }
            break;
        }
        case 1: {
            if (_arg.fields[0] === sketchId) {
                matchResult = 1;
                entityId_5 = _arg.fields[1];
                id_5 = _arg.fields[0];
            }
            else {
                matchResult = 6;
            }
            break;
        }
        case 2: {
            if (_arg.fields[0] === sketchId) {
                matchResult = 2;
                entityId_6 = _arg.fields[1];
                id_6 = _arg.fields[0];
            }
            else {
                matchResult = 6;
            }
            break;
        }
        case 3: {
            if (_arg.fields[0] === sketchId) {
                matchResult = 3;
                entityId_7 = _arg.fields[1];
                id_7 = _arg.fields[0];
            }
            else {
                matchResult = 6;
            }
            break;
        }
        case 6: {
            matchResult = 4;
            frameId = _arg.fields[0];
            break;
        }
        case 7: {
            matchResult = 5;
            frameId_1 = _arg.fields[0];
            part = _arg.fields[1];
            break;
        }
        default:
            matchResult = 6;
    }
    switch (matchResult) {
        case 0:
            return new ConstraintPlacementRef(0, [entityId_4]);
        case 1:
            return new ConstraintPlacementRef(1, [entityId_5]);
        case 2:
            return new ConstraintPlacementRef(2, [entityId_6]);
        case 3:
            return new ConstraintPlacementRef(3, [entityId_7]);
        case 4:
            return new ConstraintPlacementRef(4, [frameId]);
        case 5:
            return new ConstraintPlacementRef(5, [frameId_1, part]);
        default:
            return undefined;
    }
}

export function SketchAuthoring_updatePlacementDraft(sketchId, kind, hoveredTarget, draft) {
    let matchValue_1, lineA_1, frameId_2, a_1, frameId_4, lineA_2, pointId, matchValue_3, lineA_5;
    const clickedRef = bind((_arg) => SketchAuthoring_placementRefFromTarget(sketchId, _arg), hoveredTarget);
    let matchResult, ref_, line;
    if (clickedRef != null) {
        switch (kind) {
            case "distance": {
                matchResult = 1;
                ref_ = clickedRef;
                break;
            }
            case "angle": {
                if (clickedRef.tag === 1) {
                    matchResult = 2;
                    line = clickedRef.fields[0];
                }
                else {
                    matchResult = 3;
                }
                break;
            }
            default:
                matchResult = 3;
        }
    }
    else {
        matchResult = 0;
    }
    switch (matchResult) {
        case 0:
            return draft;
        case 1: {
            const ref__1 = (ref_.tag === 5) ? (new ConstraintPlacementRef(4, [ref_.fields[0]])) : ref_;
            return new ConstraintPlacementDraft(sketchId, kind, (matchValue_1 = bind((d) => {
                if (d.Kind === kind) {
                    return d.ClickedRefs;
                }
                else {
                    return undefined;
                }
            }, draft), (ref__1.tag === 1) ? ((matchValue_1 != null) ? (!isEmpty_1(matchValue_1) ? ((head(matchValue_1).tag === 1) ? (isEmpty_1(tail(matchValue_1)) ? ((head(matchValue_1).fields[0] !== ref__1.fields[0]) ? ((lineA_1 = head(matchValue_1).fields[0], ofArray([new ConstraintPlacementRef(1, [lineA_1]), new ConstraintPlacementRef(1, [ref__1.fields[0]])]))) : singleton(ref__1)) : singleton(ref__1)) : ((head(matchValue_1).tag === 4) ? (isEmpty_1(tail(matchValue_1)) ? ((frameId_2 = head(matchValue_1).fields[0], ofArray([new ConstraintPlacementRef(1, [ref__1.fields[0]]), new ConstraintPlacementRef(4, [frameId_2])]))) : singleton(ref__1)) : singleton(ref__1))) : singleton(ref__1)) : singleton(ref__1)) : ((ref__1.tag === 0) ? ((matchValue_1 != null) ? (!isEmpty_1(matchValue_1) ? ((head(matchValue_1).tag === 0) ? (isEmpty_1(tail(matchValue_1)) ? ((head(matchValue_1).fields[0] !== ref__1.fields[0]) ? ((a_1 = head(matchValue_1).fields[0], ofArray([new ConstraintPlacementRef(0, [a_1]), new ConstraintPlacementRef(0, [ref__1.fields[0]])]))) : singleton(ref__1)) : singleton(ref__1)) : ((head(matchValue_1).tag === 4) ? (isEmpty_1(tail(matchValue_1)) ? ((frameId_4 = head(matchValue_1).fields[0], ofArray([new ConstraintPlacementRef(0, [ref__1.fields[0]]), new ConstraintPlacementRef(4, [frameId_4])]))) : singleton(ref__1)) : singleton(ref__1))) : singleton(ref__1)) : singleton(ref__1)) : ((ref__1.tag === 4) ? ((matchValue_1 != null) ? (!isEmpty_1(matchValue_1) ? ((head(matchValue_1).tag === 1) ? (isEmpty_1(tail(matchValue_1)) ? ((lineA_2 = head(matchValue_1).fields[0], ofArray([new ConstraintPlacementRef(1, [lineA_2]), new ConstraintPlacementRef(4, [ref__1.fields[0]])]))) : singleton(ref__1)) : ((head(matchValue_1).tag === 0) ? (isEmpty_1(tail(matchValue_1)) ? ((pointId = head(matchValue_1).fields[0], ofArray([new ConstraintPlacementRef(0, [pointId]), new ConstraintPlacementRef(4, [ref__1.fields[0]])]))) : singleton(ref__1)) : singleton(ref__1))) : singleton(ref__1)) : singleton(ref__1)) : singleton(ref__1)))));
        }
        case 2:
            return new ConstraintPlacementDraft(sketchId, kind, (matchValue_3 = bind((d_1) => {
                if (d_1.Kind === kind) {
                    return d_1.ClickedRefs;
                }
                else {
                    return undefined;
                }
            }, draft), (matchValue_3 != null) ? (!isEmpty_1(matchValue_3) ? ((head(matchValue_3).tag === 1) ? (isEmpty_1(tail(matchValue_3)) ? ((head(matchValue_3).fields[0] !== line) ? ((lineA_5 = head(matchValue_3).fields[0], ofArray([new ConstraintPlacementRef(1, [lineA_5]), new ConstraintPlacementRef(1, [line])]))) : singleton(new ConstraintPlacementRef(1, [line]))) : singleton(new ConstraintPlacementRef(1, [line]))) : singleton(new ConstraintPlacementRef(1, [line]))) : singleton(new ConstraintPlacementRef(1, [line]))) : singleton(new ConstraintPlacementRef(1, [line]))));
        default:
            return draft;
    }
}

export function SketchAuthoring_addConstraintFromSelection(doc, targets, kind) {
    return bind((ctx) => map_1((constraint_) => SketchAuthoring_withUpdatedSketch(doc, ctx.Action.Id, new ActionSketch(ctx.Sketch.Entities, append(ctx.Sketch.Constraints, singleton(constraint_)))), SketchAuthoring_buildConstraint(ctx.Sketch, ctx.Action.Id, kind, targets, undefined)), SketchAuthoring_trySelectedSketch(doc));
}

export function SketchAuthoring_placeConstraintFromSelection(doc, targets, placementKind, labelPosition) {
    return bind((ctx) => map_1((constraint_) => SketchAuthoring_withUpdatedSketch(doc, ctx.Action.Id, new ActionSketch(ctx.Sketch.Entities, append(ctx.Sketch.Constraints, singleton((constraint_.tag === 6) ? (new SketchConstraint(6, [constraint_.fields[0], constraint_.fields[1], constraint_.fields[2], labelPosition])) : ((constraint_.tag === 18) ? (new SketchConstraint(18, [constraint_.fields[0], constraint_.fields[1], constraint_.fields[2], constraint_.fields[3], constraint_.fields[4], constraint_.fields[5], constraint_.fields[6], labelPosition])) : ((constraint_.tag === 17) ? (new SketchConstraint(17, [constraint_.fields[0], constraint_.fields[1], constraint_.fields[2], labelPosition])) : ((constraint_.tag === 24) ? (new SketchConstraint(24, [constraint_.fields[0], constraint_.fields[1], constraint_.fields[2], constraint_.fields[3], constraint_.fields[4], constraint_.fields[5], constraint_.fields[6], constraint_.fields[7], constraint_.fields[8], constraint_.fields[9], labelPosition])) : constraint_))))))), SketchAuthoring_buildConstraint(ctx.Sketch, ctx.Action.Id, placementKind, targets, [labelPosition.X, labelPosition.Y])), SketchAuthoring_trySelectedSketch(doc));
}

/**
 * A pending placement's constraint may carry a placeholder distance
 * (0.0) when the user is about to click; fix it up with the real
 * distance from the sketch geometry so the preview reads correctly.
 * Currently only frame-distance and frame-line-distance constraints
 * need this — others carry their final distance at draft time.
 */
export function SketchAuthoring_withResolvedPendingConstraintValue(resolveSketchContext, frames, sketchUi) {
    const tryFrameOrigin = (sketchOrigin, frameId) => map_1((frameT) => {
        let local;
        let copyOfStruct = RigidTransform__get_Inverse(sketchOrigin);
        local = RigidTransform__Apply_Z2E054BF3(copyOfStruct, frameT.Trans);
        return [local.X, local.Y];
    }, tryFind_1(frameId, frames));
    return new SketchUiState(sketchUi.EditMode, sketchUi.Tool, sketchUi.ToolPoints, sketchUi.EditingDimension, sketchUi.ConstraintPlacementMode, sketchUi.ConstraintPlacementDraft, bind((pending) => map_1((tupledArg) => {
        let matchValue_3, matchValue_4, matchValue_5, fp, p, matchValue_7, sketch, matchValue, matchValue_1, a, b, matchValue_8, a_1, b_1, fp_1;
        const sketch_1 = tupledArg[0];
        const sketchOrigin_1 = tupledArg[1];
        return new PendingConstraintPlacement(pending.SketchId, (matchValue_3 = pending.Constraint, (matchValue_3.tag === 7) ? ((matchValue_3.fields[2] === "origin") ? ((matchValue_4 = SketchAuthoring_tryPoint(sketch_1, matchValue_3.fields[0]), (matchValue_5 = tryFrameOrigin(sketchOrigin_1, matchValue_3.fields[1]), (matchValue_4 != null) ? ((matchValue_5 != null) ? ((fp = matchValue_5, (p = matchValue_4, new SketchConstraint(7, [matchValue_3.fields[0], matchValue_3.fields[1], "origin", Vec2_distance(p[0], p[1], fp[0], fp[1]), matchValue_3.fields[4]])))) : pending.Constraint) : pending.Constraint))) : pending.Constraint) : ((matchValue_3.tag === 19) ? ((matchValue_3.fields[4] === "origin") ? ((matchValue_7 = ((sketch = sketch_1, (matchValue = SketchAuthoring_tryPoint(sketch, matchValue_3.fields[1]), (matchValue_1 = SketchAuthoring_tryPoint(sketch, matchValue_3.fields[2]), (matchValue != null) ? ((matchValue_1 != null) ? ((a = matchValue, (b = matchValue_1, [a, b]))) : undefined) : undefined)))), (matchValue_8 = tryFrameOrigin(sketchOrigin_1, matchValue_3.fields[3]), (matchValue_7 != null) ? ((matchValue_8 != null) ? ((a_1 = matchValue_7[0], (b_1 = matchValue_7[1], (fp_1 = matchValue_8, new SketchConstraint(19, [matchValue_3.fields[0], matchValue_3.fields[1], matchValue_3.fields[2], matchValue_3.fields[3], "origin", Vec2_pointLineDistance(fp_1[0], fp_1[1], a_1[0], a_1[1], b_1[0], b_1[1]), matchValue_3.fields[6]]))))) : pending.Constraint) : pending.Constraint))) : pending.Constraint) : pending.Constraint)));
    }, resolveSketchContext(pending.SketchId)), sketchUi.PendingConstraintPlacement), sketchUi.ConstraintAvailability, sketchUi.DimensionPlacementAvailability);
}

export function SketchAuthoring_placePendingConstraint(doc, pending, labelPosition) {
    let withLabel;
    const matchValue = pending.Constraint;
    withLabel = ((matchValue.tag === 6) ? (new SketchConstraint(6, [matchValue.fields[0], matchValue.fields[1], matchValue.fields[2], labelPosition])) : ((matchValue.tag === 18) ? (new SketchConstraint(18, [matchValue.fields[0], matchValue.fields[1], matchValue.fields[2], matchValue.fields[3], matchValue.fields[4], matchValue.fields[5], matchValue.fields[6], labelPosition])) : ((matchValue.tag === 17) ? (new SketchConstraint(17, [matchValue.fields[0], matchValue.fields[1], matchValue.fields[2], labelPosition])) : ((matchValue.tag === 24) ? (new SketchConstraint(24, [matchValue.fields[0], matchValue.fields[1], matchValue.fields[2], matchValue.fields[3], matchValue.fields[4], matchValue.fields[5], matchValue.fields[6], matchValue.fields[7], matchValue.fields[8], matchValue.fields[9], labelPosition])) : matchValue))));
    return map_1((ctx_1) => SketchAuthoring_withUpdatedSketch(doc, ctx_1.Action.Id, new ActionSketch(ctx_1.Sketch.Entities, append(ctx_1.Sketch.Constraints, singleton(withLabel)))), filter_2((ctx) => (ctx.Action.Id === pending.SketchId), SketchAuthoring_trySelectedSketch(doc)));
}

export function SketchAuthoring_availabilityForSelection(doc, editMode, tool, placementMode, targets, placementCursor, placementDraft, hoveredTarget) {
    const matchValue = SketchAuthoring_trySelectedSketch(doc);
    if (matchValue != null) {
        const ctx = matchValue;
        const activeDraft = editMode ? filter_2((d) => {
            if (d.SketchId === ctx.Action.Id) {
                return equals(placementMode, d.Kind);
            }
            else {
                return false;
            }
        }, placementDraft) : undefined;
        const hoveredRef = bind((_arg) => SketchAuthoring_placementRefFromTarget(ctx.Action.Id, _arg), hoveredTarget);
        return new SketchUiState(editMode, editMode ? tool : "none", empty(), undefined, editMode ? placementMode : undefined, activeDraft, editMode ? ((activeDraft == null) ? bind((kind_1) => map_1((constraint__1) => (new PendingConstraintPlacement(ctx.Action.Id, constraint__1)), SketchAuthoring_buildConstraint(ctx.Sketch, ctx.Action.Id, kind_1, targets, placementCursor)), placementMode) : map_1((constraint_) => (new PendingConstraintPlacement(ctx.Action.Id, constraint_)), SketchAuthoring_pendingConstraintForDraft(ctx.Sketch, activeDraft, hoveredRef, placementCursor))) : undefined, ofList(map((kind_2) => [kind_2, SketchAuthoring_buildConstraint(ctx.Sketch, ctx.Action.Id, kind_2, targets, undefined) != null], ofArray(["Coincident", "Horizontal", "Vertical", "Midpoint", "Parallel", "Perpendicular", "Equal", "Tangent", "Concentric", "Fixed"])), {
            Compare: comparePrimitives,
        }), ofList(map((kind_3) => [kind_3, editMode], ofArray(["distance", "angle"])), {
            Compare: comparePrimitives,
        }));
    }
    else {
        return new SketchUiState(false, "none", empty(), SketchAuthoring_emptyUiState.EditingDimension, undefined, undefined, undefined, SketchAuthoring_emptyUiState.ConstraintAvailability, SketchAuthoring_emptyUiState.DimensionPlacementAvailability);
    }
}

export function SketchAuthoring_requiredToolPoints(tool) {
    switch (tool) {
        case "line":
        case "rectangle":
        case "roundedRectangle":
        case "circle":
            return 2;
        case "arc":
            return 3;
        default:
            return 0;
    }
}

function SketchAuthoring_nextEntityId(sketch, prefix) {
    const taken = ofList_1(map((_arg) => {
        let id;
        switch (_arg.tag) {
            case 1: {
                id = _arg.fields[0];
                break;
            }
            case 2: {
                id = _arg.fields[0];
                break;
            }
            case 3: {
                id = _arg.fields[0];
                break;
            }
            default:
                id = _arg.fields[0];
        }
        return id;
    }, sketch.Entities), {
        Compare: comparePrimitives,
    });
    const loop = (i_mut) => {
        loop:
        while (true) {
            const i = i_mut;
            const id_1 = `${prefix}${i}`;
            if (contains(id_1, taken)) {
                i_mut = (i + 1);
                continue loop;
            }
            else {
                return id_1;
            }
            break;
        }
    };
    return loop(1);
}

function SketchAuthoring_addPoint(sketch, x, y) {
    const pointId = SketchAuthoring_nextEntityId(sketch, "p");
    return [new ActionSketch(append(sketch.Entities, singleton(new RenderEntity(0, [pointId, x, y]))), sketch.Constraints), pointId];
}

function SketchAuthoring_reuseOrAddPoint(sketch, existingPointId, x, y) {
    if (existingPointId == null) {
        const patternInput_1 = SketchAuthoring_addPoint(sketch, x, y);
        return [patternInput_1[0], patternInput_1[1], [x, y]];
    }
    else {
        const pointId = existingPointId;
        const matchValue = SketchAuthoring_tryPoint(sketch, pointId);
        if (matchValue == null) {
            const patternInput = SketchAuthoring_addPoint(sketch, x, y);
            return [patternInput[0], patternInput[1], [x, y]];
        }
        else {
            return [sketch, pointId, [matchValue[0], matchValue[1]]];
        }
    }
}

function SketchAuthoring_addLineEntity(sketch, startId, endId) {
    const lineId = SketchAuthoring_nextEntityId(sketch, "l");
    return [new ActionSketch(append(sketch.Entities, singleton(new RenderEntity(1, [lineId, startId, endId]))), sketch.Constraints), lineId];
}

function SketchAuthoring_addRectangleToSketch(sketch, cornerA_, cornerA__1, cornerB_, cornerB__1) {
    let Constraints;
    const cornerA = [cornerA_, cornerA__1];
    const cornerB = [cornerB_, cornerB__1];
    const minX = min_1(cornerA[0], cornerB[0]);
    const maxX = max_1(cornerA[0], cornerB[0]);
    const minY = min_1(cornerA[1], cornerB[1]);
    const maxY = max_1(cornerA[1], cornerB[1]);
    if ((Math.abs(maxX - minX) < 1E-09) ? true : (Math.abs(maxY - minY) < 1E-09)) {
        return undefined;
    }
    else {
        let next = sketch;
        const patternInput = SketchAuthoring_addPoint(next, minX, minY);
        const p1 = patternInput[1];
        next = patternInput[0];
        const patternInput_1 = SketchAuthoring_addPoint(next, maxX, minY);
        const p2 = patternInput_1[1];
        next = patternInput_1[0];
        const patternInput_2 = SketchAuthoring_addPoint(next, maxX, maxY);
        const p3 = patternInput_2[1];
        next = patternInput_2[0];
        const patternInput_3 = SketchAuthoring_addPoint(next, minX, maxY);
        const p4 = patternInput_3[1];
        next = patternInput_3[0];
        const patternInput_4 = SketchAuthoring_addLineEntity(next, p1, p2);
        const l1 = patternInput_4[1];
        next = patternInput_4[0];
        const patternInput_5 = SketchAuthoring_addLineEntity(next, p2, p3);
        const l2 = patternInput_5[1];
        next = patternInput_5[0];
        const patternInput_6 = SketchAuthoring_addLineEntity(next, p3, p4);
        const l3 = patternInput_6[1];
        next = patternInput_6[0];
        const patternInput_7 = SketchAuthoring_addLineEntity(next, p4, p1);
        const l4 = patternInput_7[1];
        next = patternInput_7[0];
        return (Constraints = append(next.Constraints, ofArray([new SketchConstraint(4, [p1, p2]), new SketchConstraint(5, [p2, p3]), new SketchConstraint(4, [p4, p3]), new SketchConstraint(5, [p1, p4]), new SketchConstraint(13, [p1, p2, p2, p3, l1, l2]), new SketchConstraint(13, [p2, p3, p3, p4, l2, l3]), new SketchConstraint(13, [p3, p4, p4, p1, l3, l4]), new SketchConstraint(13, [p4, p1, p1, p2, l4, l1])])), new ActionSketch(next.Entities, Constraints));
    }
}

function SketchAuthoring_roundedRectRadius(minX, maxX, minY, maxY) {
    const width = maxX - minX;
    const height = maxY - minY;
    return min_1((height * 0.5) - 1E-06, min_1((width * 0.5) - 1E-06, max_1(0.002, min_1(width, height) * 0.2)));
}

function SketchAuthoring_addRoundedRectangleToSketch(sketch, cornerA_, cornerA__1, cornerB_, cornerB__1) {
    let Constraints;
    const cornerA = [cornerA_, cornerA__1];
    const cornerB = [cornerB_, cornerB__1];
    const minX = min_1(cornerA[0], cornerB[0]);
    const maxX = max_1(cornerA[0], cornerB[0]);
    const minY = min_1(cornerA[1], cornerB[1]);
    const maxY = max_1(cornerA[1], cornerB[1]);
    const width = maxX - minX;
    const height = maxY - minY;
    if ((Math.abs(width) < 1E-09) ? true : (Math.abs(height) < 1E-09)) {
        return undefined;
    }
    else {
        const radius = SketchAuthoring_roundedRectRadius(minX, maxX, minY, maxY);
        if (radius <= 1E-06) {
            return SketchAuthoring_addRectangleToSketch(sketch, cornerA[0], cornerA[1], cornerB[0], cornerB[1]);
        }
        else {
            let next = sketch;
            const addP = (x, y) => {
                const patternInput = SketchAuthoring_addPoint(next, x, y);
                next = patternInput[0];
                return patternInput[1];
            };
            const topLeftStart = addP(minX + radius, maxY);
            const topRightStart = addP(maxX - radius, maxY);
            const rightTopStart = addP(maxX, maxY - radius);
            const rightBottomStart = addP(maxX, minY + radius);
            const bottomRightStart = addP(maxX - radius, minY);
            const bottomLeftStart = addP(minX + radius, minY);
            const leftBottomStart = addP(minX, minY + radius);
            const leftTopStart = addP(minX, maxY - radius);
            const patternInput_1 = SketchAuthoring_addLineEntity(next, topLeftStart, topRightStart);
            const topLine = patternInput_1[1];
            next = patternInput_1[0];
            const patternInput_2 = SketchAuthoring_addLineEntity(next, rightTopStart, rightBottomStart);
            const rightLine = patternInput_2[1];
            next = patternInput_2[0];
            const patternInput_3 = SketchAuthoring_addLineEntity(next, bottomRightStart, bottomLeftStart);
            const bottomLine = patternInput_3[1];
            next = patternInput_3[0];
            const patternInput_4 = SketchAuthoring_addLineEntity(next, leftBottomStart, leftTopStart);
            const leftLine = patternInput_4[1];
            next = patternInput_4[0];
            const tlCenter = addP(minX + radius, maxY - radius);
            const trCenter = addP(maxX - radius, maxY - radius);
            const brCenter = addP(maxX - radius, minY + radius);
            const blCenter = addP(minX + radius, minY + radius);
            const trArcId = SketchAuthoring_nextEntityId(next, "a");
            next = (new ActionSketch(append(next.Entities, singleton(new RenderEntity(3, [trArcId, topRightStart, rightTopStart, new ArcData(0, [trCenter, true])]))), next.Constraints));
            const brArcId = SketchAuthoring_nextEntityId(next, "a");
            next = (new ActionSketch(append(next.Entities, singleton(new RenderEntity(3, [brArcId, rightBottomStart, bottomRightStart, new ArcData(0, [brCenter, true])]))), next.Constraints));
            const blArcId = SketchAuthoring_nextEntityId(next, "a");
            next = (new ActionSketch(append(next.Entities, singleton(new RenderEntity(3, [blArcId, bottomLeftStart, leftBottomStart, new ArcData(0, [blCenter, true])]))), next.Constraints));
            const tlArcId = SketchAuthoring_nextEntityId(next, "a");
            next = (new ActionSketch(append(next.Entities, singleton(new RenderEntity(3, [tlArcId, leftTopStart, topLeftStart, new ArcData(0, [tlCenter, true])]))), next.Constraints));
            return (Constraints = append(next.Constraints, ofArray([new SketchConstraint(4, [topLeftStart, topRightStart]), new SketchConstraint(5, [rightTopStart, rightBottomStart]), new SketchConstraint(4, [bottomLeftStart, bottomRightStart]), new SketchConstraint(5, [leftBottomStart, leftTopStart]), new SketchConstraint(13, [topLeftStart, topRightStart, rightTopStart, rightBottomStart, topLine, rightLine]), new SketchConstraint(13, [rightTopStart, rightBottomStart, bottomRightStart, bottomLeftStart, rightLine, bottomLine]), new SketchConstraint(13, [bottomRightStart, bottomLeftStart, leftBottomStart, leftTopStart, bottomLine, leftLine]), new SketchConstraint(13, [leftBottomStart, leftTopStart, topLeftStart, topRightStart, leftLine, topLine]), new SketchConstraint(5, [trCenter, topRightStart]), new SketchConstraint(4, [trCenter, rightTopStart]), new SketchConstraint(4, [brCenter, rightBottomStart]), new SketchConstraint(5, [brCenter, bottomRightStart]), new SketchConstraint(5, [blCenter, bottomLeftStart]), new SketchConstraint(4, [blCenter, leftBottomStart]), new SketchConstraint(4, [tlCenter, leftTopStart]), new SketchConstraint(5, [tlCenter, topLeftStart]), new SketchConstraint(9, [trArcId, brArcId]), new SketchConstraint(9, [brArcId, blArcId]), new SketchConstraint(9, [blArcId, tlArcId]), new SketchConstraint(15, [topLeftStart, topRightStart, trCenter, trArcId, topLine, radius]), new SketchConstraint(15, [topLeftStart, topRightStart, tlCenter, tlArcId, topLine, radius]), new SketchConstraint(15, [rightTopStart, rightBottomStart, trCenter, trArcId, rightLine, radius]), new SketchConstraint(15, [rightTopStart, rightBottomStart, brCenter, brArcId, rightLine, radius]), new SketchConstraint(15, [bottomRightStart, bottomLeftStart, brCenter, brArcId, bottomLine, radius]), new SketchConstraint(15, [bottomRightStart, bottomLeftStart, blCenter, blArcId, bottomLine, radius]), new SketchConstraint(15, [leftBottomStart, leftTopStart, blCenter, blArcId, leftLine, radius]), new SketchConstraint(15, [leftBottomStart, leftTopStart, tlCenter, tlArcId, leftLine, radius])])), new ActionSketch(next.Entities, Constraints));
        }
    }
}

function SketchAuthoring_projectPointToCircle(cx, cy, sx, sy, px, py) {
    const radius = max_1(1E-06, SketchAuthoring_dist(cx, cy, sx, sy));
    const dx = px - cx;
    const dy = py - cy;
    const length = Math.sqrt((dx * dx) + (dy * dy));
    if (length < 1E-06) {
        return [cx + radius, cy];
    }
    else {
        return [cx + ((dx / length) * radius), cy + ((dy / length) * radius)];
    }
}

export function SketchAuthoring_applyToolClick(tool, points, pointRefs, sketch, carriedLineStartId) {
    let tupledArg, tupledArg_1;
    const coords = map((p) => [p.X, p.Y], points);
    const refs = append(pointRefs, replicate(max_1(0, length_1(coords) - length_1(pointRefs)), undefined));
    let matchResult, endPoint, endRef, startPoint, startRef, x0, x1, y0, y1, x0_1, x1_1, y0_1, y1_1, centerPoint, centerRef, radiusPoint, centerPoint_1, centerRef_1, endPoint_1, endRef_1, startPoint_1, startRef_1;
    switch (tool) {
        case "line": {
            if (!isEmpty_1(coords)) {
                if (!isEmpty_1(tail(coords))) {
                    if (isEmpty_1(tail(tail(coords)))) {
                        if (!isEmpty_1(refs)) {
                            if (!isEmpty_1(tail(refs))) {
                                matchResult = 0;
                                endPoint = head(tail(coords));
                                endRef = head(tail(refs));
                                startPoint = head(coords);
                                startRef = head(refs);
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
            if (!isEmpty_1(coords)) {
                if (!isEmpty_1(tail(coords))) {
                    if (isEmpty_1(tail(tail(coords)))) {
                        matchResult = 1;
                        x0 = head(coords)[0];
                        x1 = head(tail(coords))[0];
                        y0 = head(coords)[1];
                        y1 = head(tail(coords))[1];
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
        case "roundedRectangle": {
            if (!isEmpty_1(coords)) {
                if (!isEmpty_1(tail(coords))) {
                    if (isEmpty_1(tail(tail(coords)))) {
                        matchResult = 2;
                        x0_1 = head(coords)[0];
                        x1_1 = head(tail(coords))[0];
                        y0_1 = head(coords)[1];
                        y1_1 = head(tail(coords))[1];
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
        case "circle": {
            if (!isEmpty_1(coords)) {
                if (!isEmpty_1(tail(coords))) {
                    if (isEmpty_1(tail(tail(coords)))) {
                        if (!isEmpty_1(refs)) {
                            matchResult = 3;
                            centerPoint = head(coords);
                            centerRef = head(refs);
                            radiusPoint = head(tail(coords));
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
            }
            else {
                matchResult = 5;
            }
            break;
        }
        case "arc": {
            if (!isEmpty_1(coords)) {
                if (!isEmpty_1(tail(coords))) {
                    if (!isEmpty_1(tail(tail(coords)))) {
                        if (isEmpty_1(tail(tail(tail(coords))))) {
                            if (!isEmpty_1(refs)) {
                                if (!isEmpty_1(tail(refs))) {
                                    if (!isEmpty_1(tail(tail(refs)))) {
                                        matchResult = 4;
                                        centerPoint_1 = head(coords);
                                        centerRef_1 = head(refs);
                                        endPoint_1 = head(tail(tail(coords)));
                                        endRef_1 = head(tail(tail(refs)));
                                        startPoint_1 = head(tail(coords));
                                        startRef_1 = head(tail(refs));
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
            let patternInput_1;
            if (carriedLineStartId == null) {
                const patternInput = SketchAuthoring_reuseOrAddPoint(sketch, startRef, startPoint[0], startPoint[1]);
                patternInput_1 = [patternInput[0], patternInput[1]];
            }
            else {
                patternInput_1 = [sketch, carriedLineStartId];
            }
            const startId_1 = patternInput_1[1];
            const patternInput_2 = SketchAuthoring_reuseOrAddPoint(patternInput_1[0], endRef, endPoint[0], endPoint[1]);
            const resolvedEndPoint = patternInput_2[2];
            const endId = patternInput_2[1];
            if (startId_1 === endId) {
                return undefined;
            }
            else {
                return new SketchAuthoring_ToolApplyResult(SketchAuthoring_addLineEntity(patternInput_2[0], startId_1, endId)[0], [endId, new LabelPos(resolvedEndPoint[0], resolvedEndPoint[1])]);
            }
        }
        case 1:
            return map_1((next_4) => (new SketchAuthoring_ToolApplyResult(next_4, undefined)), SketchAuthoring_addRectangleToSketch(sketch, x0, y0, x1, y1));
        case 2:
            return map_1((next_5) => (new SketchAuthoring_ToolApplyResult(next_5, undefined)), SketchAuthoring_addRoundedRectangleToSketch(sketch, x0_1, y0_1, x1_1, y1_1));
        case 3: {
            const patternInput_4 = SketchAuthoring_reuseOrAddPoint(sketch, centerRef, centerPoint[0], centerPoint[1]);
            const resolvedCenterPoint = patternInput_4[2];
            const next_6 = patternInput_4[0];
            return new SketchAuthoring_ToolApplyResult(new ActionSketch(append(next_6.Entities, singleton(new RenderEntity(2, [SketchAuthoring_nextEntityId(next_6, "c"), patternInput_4[1], max_1(1E-06, SketchAuthoring_dist(resolvedCenterPoint[0], resolvedCenterPoint[1], radiusPoint[0], radiusPoint[1]))]))), next_6.Constraints), undefined);
        }
        case 4: {
            const patternInput_5 = SketchAuthoring_reuseOrAddPoint(sketch, centerRef_1, centerPoint_1[0], centerPoint_1[1]);
            const resolvedCenterPoint_1 = patternInput_5[2];
            const centerId_1 = patternInput_5[1];
            const patternInput_6 = SketchAuthoring_reuseOrAddPoint(patternInput_5[0], startRef_1, startPoint_1[0], startPoint_1[1]);
            const startId_2 = patternInput_6[1];
            const resolvedStartPoint = patternInput_6[2];
            const projectedEnd = SketchAuthoring_projectPointToCircle(resolvedCenterPoint_1[0], resolvedCenterPoint_1[1], resolvedStartPoint[0], resolvedStartPoint[1], endPoint_1[0], endPoint_1[1]);
            const patternInput_7 = SketchAuthoring_reuseOrAddPoint(patternInput_6[0], endRef_1, projectedEnd[0], projectedEnd[1]);
            const next_9 = patternInput_7[0];
            const endId_1 = patternInput_7[1];
            if (((centerId_1 === startId_2) ? true : (centerId_1 === endId_1)) ? true : (startId_2 === endId_1)) {
                return undefined;
            }
            else {
                return new SketchAuthoring_ToolApplyResult(new ActionSketch(append(next_9.Entities, singleton(new RenderEntity(3, [SketchAuthoring_nextEntityId(next_9, "a"), startId_2, endId_1, new ArcData(0, [centerId_1, ((tupledArg = SketchAuthoring_sub(resolvedStartPoint[0], resolvedStartPoint[1], resolvedCenterPoint_1[0], resolvedCenterPoint_1[1]), (tupledArg_1 = SketchAuthoring_sub(endPoint_1[0], endPoint_1[1], resolvedCenterPoint_1[0], resolvedCenterPoint_1[1]), SketchAuthoring_cross(tupledArg[0], tupledArg[1], tupledArg_1[0], tupledArg_1[1])))) < 0])]))), next_9.Constraints), undefined);
            }
        }
        default:
            return undefined;
    }
}

