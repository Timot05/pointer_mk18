import { Record, Union } from "../../ui/fable_modules/fable-library-js.4.29.0/Types.js";
import { record_type, float32_type, union_type, list_type, bool_type, string_type, int32_type } from "../../ui/fable_modules/fable-library-js.4.29.0/Reflection.js";
import { SlotPt2_$reflection } from "../Field/FieldIR.fs.js";
import { compareArrays, comparePrimitives, equals } from "../../ui/fable_modules/fable-library-js.4.29.0/Util.js";
import { tryFind, ofList } from "../../ui/fable_modules/fable-library-js.4.29.0/Map.js";
import { choose, sortBy, tryHead, map } from "../../ui/fable_modules/fable-library-js.4.29.0/List.js";
import { map as map_1 } from "../../ui/fable_modules/fable-library-js.4.29.0/Option.js";

export class Pickable extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["PickPoint", "PickLine", "PickCircle", "PickArc", "PickLoop", "PickDimension", "PickFrameOrigin", "PickFrameAxis"];
    }
}

export function Pickable_$reflection() {
    return union_type("Server.Pickable", [], Pickable, () => [[["pickId", int32_type], ["sketchId", string_type], ["entityId", string_type], ["xSlot", int32_type], ["ySlot", int32_type]], [["pickId", int32_type], ["sketchId", string_type], ["entityId", string_type], ["startP", SlotPt2_$reflection()], ["endP", SlotPt2_$reflection()]], [["pickId", int32_type], ["sketchId", string_type], ["entityId", string_type], ["center", SlotPt2_$reflection()], ["radiusSlot", int32_type]], [["pickId", int32_type], ["sketchId", string_type], ["entityId", string_type], ["startP", SlotPt2_$reflection()], ["endP", SlotPt2_$reflection()], ["center", SlotPt2_$reflection()], ["clockwise", bool_type]], [["pickId", int32_type], ["sketchId", string_type], ["loopId", string_type], ["entityIds", list_type(string_type)]], [["pickId", int32_type], ["sketchId", string_type], ["constraintIndex", int32_type], ["anchor", SlotPt2_$reflection()]], [["pickId", int32_type], ["frameId", string_type]], [["pickId", int32_type], ["frameId", string_type], ["part", string_type]]]);
}

export class SelectionTarget extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["TargetPoint", "TargetLine", "TargetCircle", "TargetArc", "TargetLoop", "TargetDimension", "TargetFrameOrigin", "TargetFrameAxis"];
    }
}

export function SelectionTarget_$reflection() {
    return union_type("Server.SelectionTarget", [], SelectionTarget, () => [[["sketchId", string_type], ["entityId", string_type]], [["sketchId", string_type], ["entityId", string_type]], [["sketchId", string_type], ["entityId", string_type]], [["sketchId", string_type], ["entityId", string_type]], [["sketchId", string_type], ["loopId", string_type]], [["sketchId", string_type], ["constraintIndex", int32_type]], [["frameId", string_type]], [["frameId", string_type], ["part", string_type]]]);
}

export class PickCandidate extends Record {
    constructor(PickId, Score) {
        super();
        this.PickId = (PickId | 0);
        this.Score = Score;
    }
}

export function PickCandidate_$reflection() {
    return record_type("Server.PickCandidate", [], PickCandidate, () => [["PickId", int32_type], ["Score", float32_type]]);
}

/**
 * Extract the PickId from any variant.
 */
export function PickableModule_pickId(_arg) {
    switch (_arg.tag) {
        case 1:
            return _arg.fields[0] | 0;
        case 2:
            return _arg.fields[0] | 0;
        case 3:
            return _arg.fields[0] | 0;
        case 4:
            return _arg.fields[0] | 0;
        case 5:
            return _arg.fields[0] | 0;
        case 6:
            return _arg.fields[0] | 0;
        case 7:
            return _arg.fields[0] | 0;
        default:
            return _arg.fields[0] | 0;
    }
}

/**
 * Resolve a pickable to the ActionId the server should select when
 * this pickable is clicked.
 */
export function PickableModule_targetAction(_arg) {
    switch (_arg.tag) {
        case 1:
            return _arg.fields[1];
        case 2:
            return _arg.fields[1];
        case 3:
            return _arg.fields[1];
        case 4:
            return _arg.fields[1];
        case 5:
            return _arg.fields[1];
        case 6:
            return _arg.fields[1];
        case 7:
            return _arg.fields[1];
        default:
            return _arg.fields[1];
    }
}

export function PickableModule_selectionTarget(_arg) {
    switch (_arg.tag) {
        case 1:
            return new SelectionTarget(1, [_arg.fields[1], _arg.fields[2]]);
        case 2:
            return new SelectionTarget(2, [_arg.fields[1], _arg.fields[2]]);
        case 3:
            return new SelectionTarget(3, [_arg.fields[1], _arg.fields[2]]);
        case 4:
            return new SelectionTarget(4, [_arg.fields[1], _arg.fields[2]]);
        case 5:
            return new SelectionTarget(5, [_arg.fields[1], _arg.fields[2]]);
        case 6:
            return new SelectionTarget(6, [_arg.fields[1]]);
        case 7:
            return new SelectionTarget(7, [_arg.fields[1], _arg.fields[2]]);
        default:
            return new SelectionTarget(0, [_arg.fields[1], _arg.fields[2]]);
    }
}

export function PickableModule_sameTarget(target, pickable) {
    return equals(PickableModule_selectionTarget(pickable), target);
}

function PickableModule_priority(_arg) {
    switch (_arg.tag) {
        case 1:
        case 2:
        case 3:
            return 1;
        case 5:
            return 2;
        case 4:
            return 3;
        case 6:
            return 4;
        case 7:
            return 4;
        default:
            return 0;
    }
}

export function PickableModule_reduceCandidates(pickables, candidates) {
    const byId = ofList(map((p) => [PickableModule_pickId(p), p], pickables), {
        Compare: comparePrimitives,
    });
    return map_1((tuple) => tuple[1], tryHead(sortBy((tupledArg) => {
        const p_2 = tupledArg[1];
        return [PickableModule_priority(p_2), tupledArg[0].Score, PickableModule_pickId(p_2)];
    }, choose((c) => map_1((p_1) => [c, p_1], tryFind(c.PickId, byId)), candidates), {
        Compare: compareArrays,
    })));
}

export function PickableModule_selectionPriority(_arg) {
    switch (_arg.tag) {
        case 1:
        case 2:
        case 3:
        case 7:
            return 1;
        case 5:
            return 2;
        case 4:
            return 3;
        default:
            return 0;
    }
}

