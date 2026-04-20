import { Record } from "../../ui/fable_modules/fable-library-js.4.29.0/Types.js";
import { ParamValue, DocumentModule_patchParamValue, DocumentModule_pathOfParamField, SketchConstraintField, ActionParamField, SketchEntityField, ActionParamField_$reflection } from "../Editor/Domain.fs.js";
import { record_type, class_type, int32_type, array_type } from "../../ui/fable_modules/fable-library-js.4.29.0/Reflection.js";
import { toList, empty, singleton, collect, append, delay, toArray } from "../../ui/fable_modules/fable-library-js.4.29.0/Seq.js";
import { fold, tryFind as tryFind_1, empty as empty_1, ofArray as ofArray_1, indexed } from "../../ui/fable_modules/fable-library-js.4.29.0/List.js";
import { setItem, item, copy, tryFindIndex, mapIndexed, map } from "../../ui/fable_modules/fable-library-js.4.29.0/Array.js";
import { tryFind, ofArray, find } from "../../ui/fable_modules/fable-library-js.4.29.0/Map.js";
import { SlotRef } from "../Editor/SlotTable.fs.js";
import { equals, comparePrimitives } from "../../ui/fable_modules/fable-library-js.4.29.0/Util.js";
import { SolverPin } from "../Solve/GpuLmSolver.fs.js";
import { min } from "../../ui/fable_modules/fable-library-js.4.29.0/Double.js";
import { rangeDouble } from "../../ui/fable_modules/fable-library-js.4.29.0/Range.js";

export class SketchSolveBinding extends Record {
    constructor(LocalFields, LocalToGlobal, VarIndexByLocal) {
        super();
        this.LocalFields = LocalFields;
        this.LocalToGlobal = LocalToGlobal;
        this.VarIndexByLocal = VarIndexByLocal;
    }
}

export function SketchSolveBinding_$reflection() {
    return record_type("Server.SketchSolveBinding", [], SketchSolveBinding, () => [["LocalFields", array_type(ActionParamField_$reflection())], ["LocalToGlobal", array_type(int32_type)], ["VarIndexByLocal", class_type("Microsoft.FSharp.Collections.FSharpMap`2", [int32_type, int32_type])]]);
}

export function SketchSolve_localFields(sketch) {
    return toArray(delay(() => append(collect((entity) => {
        const matchValue = entity;
        let matchResult, id, id_1, id_2;
        switch (matchValue.tag) {
            case 0: {
                matchResult = 0;
                id = matchValue.fields[0];
                break;
            }
            case 2: {
                matchResult = 1;
                id_1 = matchValue.fields[0];
                break;
            }
            case 3: {
                if (matchValue.fields[3].tag === 1) {
                    matchResult = 2;
                    id_2 = matchValue.fields[0];
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
                return append(singleton(new ActionParamField(31, [id, new SketchEntityField(0, [])])), delay(() => singleton(new ActionParamField(31, [id, new SketchEntityField(1, [])]))));
            case 1:
                return singleton(new ActionParamField(31, [id_1, new SketchEntityField(2, [])]));
            case 2:
                return append(singleton(new ActionParamField(31, [id_2, new SketchEntityField(3, [])])), delay(() => singleton(new ActionParamField(31, [id_2, new SketchEntityField(4, [])]))));
            default: {
                return empty();
            }
        }
    }, sketch.Entities), delay(() => collect((matchValue_1) => {
        const index = matchValue_1[0] | 0;
        const matchValue_2 = matchValue_1[1];
        switch (matchValue_2.tag) {
            case 6:
            case 7:
            case 18:
            case 19:
            case 20:
            case 21:
            case 22:
            case 23:
                return singleton(new ActionParamField(32, [index, new SketchConstraintField(2, [])]));
            case 17:
                return singleton(new ActionParamField(32, [index, new SketchConstraintField(3, [])]));
            case 24:
                return singleton(new ActionParamField(32, [index, new SketchConstraintField(4, [])]));
            default: {
                return empty();
            }
        }
    }, indexed(sketch.Constraints))))));
}

export function SketchSolve_binding(slots, sketchId, sketch, varSlots) {
    const fields = SketchSolve_localFields(sketch);
    return new SketchSolveBinding(fields, map((field) => find(new SlotRef(sketchId, DocumentModule_pathOfParamField(field)), slots.Index), fields, Int32Array), ofArray(mapIndexed((varIndex, localSlot) => [localSlot, varIndex], varSlots), {
        Compare: comparePrimitives,
    }));
}

export function SketchSolve_buildPins(weight, xField, yField, target, binding) {
    const findLocal = (field) => tryFindIndex((y) => equals(field, y), binding.LocalFields);
    const matchValue = findLocal(xField);
    const matchValue_1 = findLocal(yField);
    let matchResult, xLocal, yLocal;
    if (matchValue != null) {
        if (matchValue_1 != null) {
            matchResult = 0;
            xLocal = matchValue;
            yLocal = matchValue_1;
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
            const matchValue_3 = tryFind(xLocal, binding.VarIndexByLocal);
            const matchValue_4 = tryFind(yLocal, binding.VarIndexByLocal);
            let matchResult_1, xVar, yVar;
            if (matchValue_3 != null) {
                if (matchValue_4 != null) {
                    matchResult_1 = 0;
                    xVar = matchValue_3;
                    yVar = matchValue_4;
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
                    return ofArray_1([new SolverPin(xLocal, xVar, target.X, weight), new SolverPin(yLocal, yVar, target.Y, weight)]);
                default:
                    return empty_1();
            }
        }
        default:
            return empty_1();
    }
}

export function SketchSolve_overlaySolvedSketch(baseParams, slots, sketchId, sketch, solvedLocal) {
    const overlaid = copy(baseParams);
    const fields = SketchSolve_localFields(sketch);
    const count = min(fields.length, solvedLocal.length) | 0;
    for (let i = 0; i <= (count - 1); i++) {
        const globalSlot = find(new SlotRef(sketchId, DocumentModule_pathOfParamField(item(i, fields))), slots.Index) | 0;
        if (globalSlot < overlaid.length) {
            setItem(overlaid, globalSlot, item(i, solvedLocal));
        }
    }
    return overlaid;
}

export function SketchSolve_patchSolvedSketchSlots(baseParams, slots, sketchId, sketch, solvedLocal) {
    return SketchSolve_overlaySolvedSketch(baseParams, slots, sketchId, sketch, solvedLocal);
}

export function SketchSolve_commitSolvedSketch(sketchId, solvedLocal, doc) {
    const matchValue = tryFind_1((action) => (action.Id === sketchId), doc.Actions);
    let matchResult, sketch;
    if (matchValue != null) {
        if (matchValue.Kind.tag === 11) {
            matchResult = 0;
            sketch = matchValue.Kind.fields[2];
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
            const fields = SketchSolve_localFields(sketch);
            return fold((current, index) => DocumentModule_patchParamValue(sketchId, item(index, fields), new ParamValue(3, [item(index, solvedLocal)]), current), doc, toList(rangeDouble(0, 1, min(fields.length, solvedLocal.length) - 1)));
        }
        default:
            return doc;
    }
}

