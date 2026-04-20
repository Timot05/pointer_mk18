import { SlotTableModule_tryFindSlot } from "./SlotTable.fs.js";
import { item } from "../../ui/fable_modules/fable-library-js.4.29.0/Array.js";
import { RigidTransform_get_Identity, RigidTransform_op_Multiply_ZFA4D60, RigidTransformModule_translate, RigidTransformModule_fromAxisAngle } from "../Math/Transform.fs.js";
import { Vec3 } from "../Math/Vec.fs.js";
import { fold } from "../../ui/fable_modules/fable-library-js.4.29.0/List.js";

function slotValue(slots, values, slotRef, defaultValue) {
    const matchValue = SlotTableModule_tryFindSlot(slots, slotRef);
    if (matchValue == null) {
        return defaultValue;
    }
    else {
        return item(matchValue, values);
    }
}

export function stepTransform(slots, values, step) {
    if (step.tag === 1) {
        return RigidTransformModule_fromAxisAngle(new Vec3(slotValue(slots, values, step.fields[1], step.fields[5]), slotValue(slots, values, step.fields[2], step.fields[6]), slotValue(slots, values, step.fields[3], step.fields[7])), slotValue(slots, values, step.fields[4], step.fields[8]));
    }
    else {
        return RigidTransformModule_translate(new Vec3(slotValue(slots, values, step.fields[1], step.fields[4]), slotValue(slots, values, step.fields[2], step.fields[5]), slotValue(slots, values, step.fields[3], step.fields[6])));
    }
}

/**
 * Fold a child/local-first frame chain into a concrete world transform.
 */
export function foldChain(slots, values, chain) {
    return fold((acc, step) => RigidTransform_op_Multiply_ZFA4D60(acc, stepTransform(slots, values, step)), RigidTransform_get_Identity(), chain);
}

