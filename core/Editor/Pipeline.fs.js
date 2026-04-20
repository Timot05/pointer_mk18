import { FSharpRef, Record } from "../../ui/fable_modules/fable-library-js.4.29.0/Types.js";
import { FieldCompile_compile, SlotPt2, FieldSurface_$reflection } from "../Field/FieldIR.fs.js";
import { record_type, class_type, string_type, list_type } from "../../ui/fable_modules/fable-library-js.4.29.0/Reflection.js";
import { TypeCheck_typecheck, TypeError$_$reflection, FieldType_$reflection } from "./TypeCheck.fs.js";
import { SlotTableModule_toTable, SlotTableModule_createBuilder, SlotRef, SlotTableModule_alloc, SlotTable_$reflection } from "./SlotTable.fs.js";
import { Pickable, Pickable_$reflection } from "./Pickable.fs.js";
import { ElementModule_build, FrameStep_$reflection } from "./Element.fs.js";
import { defaultArg } from "../../ui/fable_modules/fable-library-js.4.29.0/Option.js";
import { FieldSliceSettingsModule_defaults, DisplaySettingsModule_defaults } from "./Domain.fs.js";
import { item } from "../../ui/fable_modules/fable-library-js.4.29.0/Array.js";
import { comparePrimitives, disposeSafe, getEnumerator } from "../../ui/fable_modules/fable-library-js.4.29.0/Util.js";
import { printf, toText } from "../../ui/fable_modules/fable-library-js.4.29.0/String.js";
import { LabelPos } from "../Sketch/Sketch.fs.js";
import { fold, empty, ofArray, collect, mapIndexed, map, choose, append, iterateIndexed } from "../../ui/fable_modules/fable-library-js.4.29.0/List.js";
import { SketchLoops_detectLoops } from "../Sketch/SketchLoops.fs.js";
import { ofList, tryFind } from "../../ui/fable_modules/fable-library-js.4.29.0/Map.js";

export class PipelineResult extends Record {
    constructor(Surfaces, TypeMap, Errors, Slots, Pickables, Frames) {
        super();
        this.Surfaces = Surfaces;
        this.TypeMap = TypeMap;
        this.Errors = Errors;
        this.Slots = Slots;
        this.Pickables = Pickables;
        this.Frames = Frames;
    }
}

export function PipelineResult_$reflection() {
    return record_type("Server.PipelineResult", [], PipelineResult, () => [["Surfaces", list_type(FieldSurface_$reflection())], ["TypeMap", class_type("Microsoft.FSharp.Collections.FSharpMap`2", [string_type, FieldType_$reflection()])], ["Errors", list_type(TypeError$_$reflection())], ["Slots", SlotTable_$reflection()], ["Pickables", list_type(Pickable_$reflection())], ["Frames", class_type("Microsoft.FSharp.Collections.FSharpMap`2", [string_type, list_type(FrameStep_$reflection())])]]);
}

function Pipeline_allocDisplaySlots(b, action) {
    const d = defaultArg(action.Display, DisplaySettingsModule_defaults);
    const id = action.Id;
    SlotTableModule_alloc(b, new SlotRef(id, "display.color.0"), item(0, d.Color));
    SlotTableModule_alloc(b, new SlotRef(id, "display.color.1"), item(1, d.Color));
    SlotTableModule_alloc(b, new SlotRef(id, "display.color.2"), item(2, d.Color));
    SlotTableModule_alloc(b, new SlotRef(id, "display.opacity"), d.Opacity);
    SlotTableModule_alloc(b, new SlotRef(id, "display.isoValue"), d.IsoValue);
}

function Pipeline_allocFieldSliceSlots(b, action) {
    SlotTableModule_alloc(b, new SlotRef(action.Id, "fieldSlice.offset"), defaultArg(action.FieldSlice, FieldSliceSettingsModule_defaults).Offset);
}

function Pipeline_allocFrameSlots(b, action) {
    const matchValue = action.Kind;
    switch (matchValue.tag) {
        case 5: {
            SlotTableModule_alloc(b, new SlotRef(action.Id, "x"), matchValue.fields[1]);
            SlotTableModule_alloc(b, new SlotRef(action.Id, "y"), matchValue.fields[2]);
            SlotTableModule_alloc(b, new SlotRef(action.Id, "z"), matchValue.fields[3]);
            break;
        }
        case 6: {
            SlotTableModule_alloc(b, new SlotRef(action.Id, "ax"), matchValue.fields[1]);
            SlotTableModule_alloc(b, new SlotRef(action.Id, "ay"), matchValue.fields[2]);
            SlotTableModule_alloc(b, new SlotRef(action.Id, "az"), matchValue.fields[3]);
            SlotTableModule_alloc(b, new SlotRef(action.Id, "angle"), matchValue.fields[4]);
            break;
        }
        default:
            undefined;
    }
}

function Pipeline_allocSketchSlots(b, actionId, s) {
    const a = (path, v) => {
        SlotTableModule_alloc(b, new SlotRef(actionId, path), v);
    };
    const enumerator = getEnumerator(s.Entities);
    try {
        while (enumerator["System.Collections.IEnumerator.MoveNext"]()) {
            const entity = enumerator["System.Collections.Generic.IEnumerator`1.get_Current"]();
            switch (entity.tag) {
                case 1: {
                    break;
                }
                case 2: {
                    a(toText(printf("sketch.entity.%s.radius"))(entity.fields[0]), entity.fields[2]);
                    break;
                }
                case 3: {
                    if (entity.fields[3].tag === 1) {
                        a(toText(printf("sketch.entity.%s.throughX"))(entity.fields[0]), entity.fields[3].fields[0].X);
                        a(toText(printf("sketch.entity.%s.throughY"))(entity.fields[0]), entity.fields[3].fields[0].Y);
                    }
                    break;
                }
                default: {
                    a(toText(printf("sketch.entity.%s.x"))(entity.fields[0]), entity.fields[1]);
                    a(toText(printf("sketch.entity.%s.y"))(entity.fields[0]), entity.fields[2]);
                }
            }
        }
    }
    finally {
        disposeSafe(enumerator);
    }
    const labelXY = (i, lp) => {
        const pos = defaultArg(lp, new LabelPos(0, 0));
        a(toText(printf("sketch.constraint.%d.labelPosition.x"))(i), pos.X);
        a(toText(printf("sketch.constraint.%d.labelPosition.y"))(i), pos.Y);
    };
    iterateIndexed((i_1, c) => {
        let matchResult, dist, lp_1;
        switch (c.tag) {
            case 0: {
                matchResult = 0;
                break;
            }
            case 6: {
                matchResult = 1;
                dist = c.fields[2];
                lp_1 = c.fields[3];
                break;
            }
            case 7: {
                matchResult = 1;
                dist = c.fields[3];
                lp_1 = c.fields[4];
                break;
            }
            case 18: {
                matchResult = 1;
                dist = c.fields[6];
                lp_1 = c.fields[7];
                break;
            }
            case 19: {
                matchResult = 1;
                dist = c.fields[5];
                lp_1 = c.fields[6];
                break;
            }
            case 20: {
                matchResult = 1;
                dist = c.fields[4];
                lp_1 = c.fields[5];
                break;
            }
            case 21: {
                matchResult = 1;
                dist = c.fields[3];
                lp_1 = c.fields[4];
                break;
            }
            case 22: {
                matchResult = 1;
                dist = c.fields[5];
                lp_1 = c.fields[6];
                break;
            }
            case 23: {
                matchResult = 1;
                dist = c.fields[4];
                lp_1 = c.fields[6];
                break;
            }
            case 17: {
                matchResult = 2;
                break;
            }
            case 24: {
                matchResult = 3;
                break;
            }
            case 15: {
                matchResult = 4;
                break;
            }
            default:
                matchResult = 5;
        }
        switch (matchResult) {
            case 0: {
                a(toText(printf("sketch.constraint.%d.x"))(i_1), c.fields[1]);
                a(toText(printf("sketch.constraint.%d.y"))(i_1), c.fields[2]);
                break;
            }
            case 1: {
                a(toText(printf("sketch.constraint.%d.distance"))(i_1), dist);
                labelXY(i_1, lp_1);
                break;
            }
            case 2: {
                a(toText(printf("sketch.constraint.%d.diameter"))(i_1), c.fields[2]);
                labelXY(i_1, c.fields[3]);
                break;
            }
            case 3: {
                a(toText(printf("sketch.constraint.%d.angle"))(i_1), c.fields[6]);
                labelXY(i_1, c.fields[10]);
                break;
            }
            case 4: {
                a(toText(printf("sketch.constraint.%d.radius"))(i_1), c.fields[5]);
                break;
            }
            case 5: {
                break;
            }
        }
    }, s.Constraints);
}

function Pipeline_slotOf(b, actionId, path) {
    return SlotTableModule_alloc(b, new SlotRef(actionId, path), 0);
}

function Pipeline_ptSlot(b, sketchId, pointId) {
    return new SlotPt2(Pipeline_slotOf(b, sketchId, toText(printf("sketch.entity.%s.x"))(pointId)), Pipeline_slotOf(b, sketchId, toText(printf("sketch.entity.%s.y"))(pointId)));
}

function Pipeline_labelSlot(b, sketchId, i) {
    return new SlotPt2(Pipeline_slotOf(b, sketchId, toText(printf("sketch.constraint.%d.labelPosition.x"))(i)), Pipeline_slotOf(b, sketchId, toText(printf("sketch.constraint.%d.labelPosition.y"))(i)));
}

function Pipeline_buildSketchPickables(b, counter, sketchId, sketch) {
    const nextId = () => {
        const id = counter.contents | 0;
        counter.contents = ((id + 1) | 0);
        return id | 0;
    };
    return append(choose((e) => {
        switch (e.tag) {
            case 1:
                return new Pickable(1, [nextId(), sketchId, e.fields[0], Pipeline_ptSlot(b, sketchId, e.fields[1]), Pipeline_ptSlot(b, sketchId, e.fields[2])]);
            case 2: {
                const r = Pipeline_slotOf(b, sketchId, toText(printf("sketch.entity.%s.radius"))(e.fields[0])) | 0;
                return new Pickable(2, [nextId(), sketchId, e.fields[0], Pipeline_ptSlot(b, sketchId, e.fields[1]), r]);
            }
            case 3:
                if (e.fields[3].tag === 1) {
                    return undefined;
                }
                else {
                    return new Pickable(3, [nextId(), sketchId, e.fields[0], Pipeline_ptSlot(b, sketchId, e.fields[1]), Pipeline_ptSlot(b, sketchId, e.fields[2]), Pipeline_ptSlot(b, sketchId, e.fields[3].fields[0]), e.fields[3].fields[1]]);
                }
            default: {
                const x = Pipeline_slotOf(b, sketchId, toText(printf("sketch.entity.%s.x"))(e.fields[0])) | 0;
                const y = Pipeline_slotOf(b, sketchId, toText(printf("sketch.entity.%s.y"))(e.fields[0])) | 0;
                return new Pickable(0, [nextId(), sketchId, e.fields[0], x, y]);
            }
        }
    }, sketch.Entities), append(map((loop) => (new Pickable(4, [nextId(), sketchId, loop.Id, loop.EntityIds])), SketchLoops_detectLoops(sketch.Entities)), choose((tupledArg) => {
        const i_1 = tupledArg[0] | 0;
        const c_1 = tupledArg[1];
        if ((c_1.tag === 6) ? true : ((c_1.tag === 7) ? true : ((c_1.tag === 18) ? true : ((c_1.tag === 19) ? true : ((c_1.tag === 20) ? true : ((c_1.tag === 21) ? true : ((c_1.tag === 22) ? true : ((c_1.tag === 23) ? true : ((c_1.tag === 17) ? true : (c_1.tag === 24)))))))))) {
            return new Pickable(5, [nextId(), sketchId, i_1, Pipeline_labelSlot(b, sketchId, i_1)]);
        }
        else {
            return undefined;
        }
    }, mapIndexed((i, c) => [i, c], sketch.Constraints))));
}

function Pipeline_buildPickables(b, actions, typeMap) {
    const counter = new FSharpRef(0);
    const nextId = () => {
        const id = counter.contents | 0;
        counter.contents = ((id + 1) | 0);
        return id | 0;
    };
    return collect((action) => {
        const matchValue = tryFind(action.Id, typeMap);
        let matchResult;
        if (matchValue != null) {
            switch (matchValue.tag) {
                case 2: {
                    matchResult = 0;
                    break;
                }
                case 1: {
                    matchResult = 1;
                    break;
                }
                default:
                    matchResult = 2;
            }
        }
        else {
            matchResult = 2;
        }
        switch (matchResult) {
            case 0:
                return ofArray([new Pickable(6, [nextId(), action.Id]), new Pickable(7, [nextId(), action.Id, "xAxis"]), new Pickable(7, [nextId(), action.Id, "yAxis"]), new Pickable(7, [nextId(), action.Id, "zAxis"])]);
            case 1: {
                const matchValue_1 = action.Kind;
                if (matchValue_1.tag === 11) {
                    return Pipeline_buildSketchPickables(b, counter, action.Id, matchValue_1.fields[2]);
                }
                else {
                    return empty();
                }
            }
            default:
                return empty();
        }
    }, actions);
}

function Pipeline_allocActionSlots(typeMap, b, action) {
    const matchValue = tryFind(action.Id, typeMap);
    const matchValue_1 = action.Kind;
    let matchResult, sketch;
    if (matchValue != null) {
        switch (matchValue.tag) {
            case 0: {
                matchResult = 0;
                break;
            }
            case 2: {
                matchResult = 1;
                break;
            }
            case 1: {
                if (matchValue_1.tag === 11) {
                    matchResult = 2;
                    sketch = matchValue_1.fields[2];
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
        matchResult = 3;
    }
    switch (matchResult) {
        case 0: {
            Pipeline_allocDisplaySlots(b, action);
            Pipeline_allocFieldSliceSlots(b, action);
            return b;
        }
        case 1: {
            Pipeline_allocFrameSlots(b, action);
            return b;
        }
        case 2: {
            Pipeline_allocSketchSlots(b, action.Id, sketch);
            return b;
        }
        default:
            return b;
    }
}

export function Pipeline_compile(actions) {
    const tc = TypeCheck_typecheck(actions);
    const typeMap = ofList(map((t) => [t.Id, t.Output], tc.Typed), {
        Compare: comparePrimitives,
    });
    const buildResult = ElementModule_build(actions, typeMap);
    const b = SlotTableModule_createBuilder();
    const surfaces = FieldCompile_compile(actions, buildResult.Elements, b);
    fold((b_1, action) => Pipeline_allocActionSlots(typeMap, b_1, action), b, actions);
    const pickables = Pipeline_buildPickables(b, actions, typeMap);
    return new PipelineResult(surfaces, typeMap, tc.Errors, SlotTableModule_toTable(b), pickables, buildResult.Frames);
}

