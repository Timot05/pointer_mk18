import { FSharpRef, toString, Union, Record } from "../../ui/fable_modules/fable-library-js.4.29.0/Types.js";
import { class_type, int32_type, option_type, bool_type, union_type, list_type, record_type, float64_type, string_type } from "../../ui/fable_modules/fable-library-js.4.29.0/Reflection.js";
import { TypeCheck_acceptedInputs, FieldType_$reflection } from "./TypeCheck.fs.js";
import { DocAction, ActionKind } from "./Domain.fs.js";
import { FromSketchSelectionModule_defaults, ActionSketchModule_empty, SketchPlaneModule_defaults } from "../Sketch/Sketch.fs.js";
import { bind, map, defaultArg } from "../../ui/fable_modules/fable-library-js.4.29.0/Option.js";
import { add, ofList, tryFind, empty } from "../../ui/fable_modules/fable-library-js.4.29.0/Map.js";
import { safeHash, equals, comparePrimitives } from "../../ui/fable_modules/fable-library-js.4.29.0/Util.js";
import { item as item_1, getSlice, collect, length, contains, filter, map as map_1, ofArray, singleton, empty as empty_1 } from "../../ui/fable_modules/fable-library-js.4.29.0/List.js";
import { split, isNullOrEmpty } from "../../ui/fable_modules/fable-library-js.4.29.0/String.js";
import { item } from "../../ui/fable_modules/fable-library-js.4.29.0/Array.js";
import { tryParse } from "../../ui/fable_modules/fable-library-js.4.29.0/Double.js";
import { tryParse as tryParse_1 } from "../../ui/fable_modules/fable-library-js.4.29.0/Int32.js";

export class ScalarDef extends Record {
    constructor(Key, Label, Default) {
        super();
        this.Key = Key;
        this.Label = Label;
        this.Default = Default;
    }
}

export function ScalarDef_$reflection() {
    return record_type("Server.ScalarDef", [], ScalarDef, () => [["Key", string_type], ["Label", string_type], ["Default", float64_type]]);
}

export class PaletteStep extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["RefStep", "ScalarsStep"];
    }
}

export function PaletteStep_$reflection() {
    return union_type("Server.PaletteStep", [], PaletteStep, () => [[["key", string_type], ["label", string_type], ["accepts", list_type(FieldType_$reflection())]], [["label", string_type], ["fields", list_type(ScalarDef_$reflection())]]]);
}

export class PaletteItem extends Record {
    constructor(Id, Label, Kind) {
        super();
        this.Id = Id;
        this.Label = Label;
        this.Kind = Kind;
    }
}

export function PaletteItem_$reflection() {
    return record_type("Server.PaletteItem", [], PaletteItem, () => [["Id", string_type], ["Label", string_type], ["Kind", string_type]]);
}

export class PaletteChip extends Record {
    constructor(Label, Value) {
        super();
        this.Label = Label;
        this.Value = Value;
    }
}

export function PaletteChip_$reflection() {
    return record_type("Server.PaletteChip", [], PaletteChip, () => [["Label", string_type], ["Value", string_type]]);
}

export class PaletteScalarField extends Record {
    constructor(Key, Label, Value) {
        super();
        this.Key = Key;
        this.Label = Label;
        this.Value = Value;
    }
}

export function PaletteScalarField_$reflection() {
    return record_type("Server.PaletteScalarField", [], PaletteScalarField, () => [["Key", string_type], ["Label", string_type], ["Value", float64_type]]);
}

export class PaletteState extends Record {
    constructor(IsOpen, Mode, PickedKind, Chips, Prompt, Items, ScalarFields, HintBar) {
        super();
        this.IsOpen = IsOpen;
        this.Mode = Mode;
        this.PickedKind = PickedKind;
        this.Chips = Chips;
        this.Prompt = Prompt;
        this.Items = Items;
        this.ScalarFields = ScalarFields;
        this.HintBar = HintBar;
    }
}

export function PaletteState_$reflection() {
    return record_type("Server.PaletteState", [], PaletteState, () => [["IsOpen", bool_type], ["Mode", string_type], ["PickedKind", option_type(string_type)], ["Chips", list_type(PaletteChip_$reflection())], ["Prompt", string_type], ["Items", list_type(PaletteItem_$reflection())], ["ScalarFields", list_type(PaletteScalarField_$reflection())], ["HintBar", list_type(string_type)]]);
}

export class PaletteSession extends Record {
    constructor(PickedKind, Steps, StepIndex, Values, Query) {
        super();
        this.PickedKind = PickedKind;
        this.Steps = Steps;
        this.StepIndex = (StepIndex | 0);
        this.Values = Values;
        this.Query = Query;
    }
}

export function PaletteSession_$reflection() {
    return record_type("Server.PaletteSession", [], PaletteSession, () => [["PickedKind", option_type(string_type)], ["Steps", list_type(PaletteStep_$reflection())], ["StepIndex", int32_type], ["Values", class_type("Microsoft.FSharp.Collections.FSharpMap`2", [string_type, string_type])], ["Query", string_type]]);
}

function Palette_dummyKind(kind) {
    switch (kind) {
        case "Translate":
            return new ActionKind(5, [undefined, 0, 0, 0]);
        case "Rotate":
            return new ActionKind(6, [undefined, 0, 0, 1, 0]);
        case "Move":
            return new ActionKind(7, [undefined, undefined]);
        case "Union":
            return new ActionKind(8, [undefined, undefined, 0]);
        case "Subtract":
            return new ActionKind(9, [undefined, undefined, 0]);
        case "Intersect":
            return new ActionKind(10, [undefined, undefined, 0]);
        case "Sketch":
            return new ActionKind(11, [undefined, SketchPlaneModule_defaults, ActionSketchModule_empty]);
        case "FromSketch":
            return new ActionKind(12, [undefined, false, FromSketchSelectionModule_defaults]);
        case "Thicken":
            return new ActionKind(13, [undefined, 0]);
        case "Shell":
            return new ActionKind(14, [undefined, 0]);
        case "Mesh":
            return new ActionKind(15, [undefined, 0, 0]);
        default:
            return undefined;
    }
}

function Palette_stepsFor(kind) {
    const accepted = defaultArg(map(TypeCheck_acceptedInputs, Palette_dummyKind(kind)), empty({
        Compare: comparePrimitives,
    }));
    const ref$0027 = (key, label) => (new PaletteStep(0, [key, label, defaultArg(tryFind(key, accepted), empty_1())]));
    const scalars = (label_1, fields) => (new PaletteStep(1, [label_1, fields]));
    const s = (key_1, label_2, def) => (new ScalarDef(key_1, label_2, def));
    switch (kind) {
        case "Sphere":
            return singleton(scalars("dimensions", singleton(s("radius", "radius", 8))));
        case "Cylinder":
            return singleton(scalars("dimensions", ofArray([s("radius", "radius", 5), s("height", "height", 20)])));
        case "Box":
            return singleton(scalars("dimensions", ofArray([s("width", "width", 10), s("height", "height", 10), s("depth", "depth", 10)])));
        case "HalfPlane":
            return singleton(scalars("offset", singleton(s("offset", "offset", 0))));
        case "Translate":
            return ofArray([ref$0027("child", "from"), scalars("offset", ofArray([s("x", "x", 0), s("y", "y", 0), s("z", "z", 0)]))]);
        case "Rotate":
            return ofArray([ref$0027("child", "from"), scalars("axis", ofArray([s("ax", "ax", 0), s("ay", "ay", 0), s("az", "az", 1)])), scalars("rotation", singleton(s("angle", "angle", 0)))]);
        case "Move":
            return ofArray([ref$0027("child", "from"), ref$0027("frame", "to frame")]);
        case "Sketch":
            return singleton(ref$0027("origin", "on frame"));
        case "FromSketch":
            return singleton(ref$0027("child", "sketch"));
        case "Union":
        case "Subtract":
        case "Intersect":
            return ofArray([ref$0027("a", "tool"), ref$0027("b", "target"), scalars("blend", singleton(s("radius", "blend", 0)))]);
        case "Thicken":
            return ofArray([ref$0027("child", "from"), scalars("amount", singleton(s("amount", "amount", 2)))]);
        case "Shell":
            return ofArray([ref$0027("child", "from"), scalars("thickness", singleton(s("thickness", "thickness", 1)))]);
        case "Mesh":
            return ofArray([ref$0027("child", "from"), scalars("mesh", ofArray([s("size", "size", 0.2), s("resolution", "res", 96)]))]);
        default:
            return empty_1();
    }
}

const Palette_templateLabels = ofArray(["Sphere", "Cylinder", "Box", "HalfPlane", "Translate", "Rotate", "Move", "Union", "Subtract", "Intersect", "Sketch", "FromSketch", "Thicken", "Shell", "Mesh"]);

function Palette_fuzzyMatch(query, text) {
    const q = query.toLowerCase();
    const t = text.toLowerCase();
    let qi = 0;
    for (let ti = 0; ti <= (t.length - 1); ti++) {
        if ((qi < q.length) && (t[ti] === q[qi])) {
            qi = ((qi + 1) | 0);
        }
    }
    return qi === q.length;
}

function Palette_filterTemplates(query) {
    return map_1((l) => (new PaletteItem(l, l, l)), isNullOrEmpty(query) ? Palette_templateLabels : filter((text) => Palette_fuzzyMatch(query, text), Palette_templateLabels));
}

function Palette_filterActions(query, accepts, typeMap, doc) {
    return map_1((a_1) => (new PaletteItem(a_1.Id, defaultArg(a_1.Name, item(0, split(toString(a_1.Kind), ["("], undefined, 0))), item(0, split(toString(a_1.Kind), ["("], undefined, 0)))), filter((a) => {
        let matchValue;
        if ((matchValue = tryFind(a.Id, typeMap), (matchValue == null) ? false : contains(matchValue, accepts, {
            Equals: equals,
            GetHashCode: safeHash,
        }))) {
            if (isNullOrEmpty(query)) {
                return true;
            }
            else {
                return Palette_fuzzyMatch(query, defaultArg(a.Name, toString(a.Kind)));
            }
        }
        else {
            return false;
        }
    }, doc.Actions));
}

function Palette_chipForStep(step, values) {
    if (step.tag === 1) {
        return map_1((f) => (new PaletteChip(f.Label, defaultArg(tryFind(f.Key, values), f.Default.toString()))), step.fields[1]);
    }
    else {
        return singleton(new PaletteChip(step.fields[1], defaultArg(tryFind(step.fields[0], values), "–")));
    }
}

export const Palette_empty = new PaletteSession(undefined, empty_1(), -1, empty({
    Compare: comparePrimitives,
}), "");

export function Palette_toState(session, typeMap, doc) {
    const closed = new PaletteState(false, "closed", undefined, empty_1(), "", empty_1(), empty_1(), empty_1());
    if (session.StepIndex < 0) {
        return closed;
    }
    else {
        const matchValue = session.PickedKind;
        if (matchValue != null) {
            const kind = matchValue;
            const steps = session.Steps;
            if (session.StepIndex >= length(steps)) {
                return new PaletteState(closed.IsOpen, "done", kind, closed.Chips, closed.Prompt, closed.Items, closed.ScalarFields, closed.HintBar);
            }
            else {
                const chips = collect((st) => Palette_chipForStep(st, session.Values), getSlice(undefined, session.StepIndex - 1, steps));
                const step = item_1(session.StepIndex, steps);
                if (step.tag === 1) {
                    return new PaletteState(true, "scalars", kind, chips, "", empty_1(), map_1((f) => (new PaletteScalarField(f.Key, f.Label, defaultArg(bind((s) => {
                        let matchValue_1;
                        let outArg = 0;
                        matchValue_1 = [tryParse(s, new FSharpRef(() => outArg, (v) => {
                            outArg = v;
                        })), outArg];
                        if (matchValue_1[0]) {
                            return matchValue_1[1];
                        }
                        else {
                            return undefined;
                        }
                    }, tryFind(f.Key, session.Values)), f.Default))), step.fields[1]), ofArray(["drag to adjust", "↵ next", "⌘↵ create now", "⌫ back", "esc cancel"]));
                }
                else {
                    return new PaletteState(true, "ref", kind, chips, `Pick "${step.fields[1]}" for ${kind}…`, Palette_filterActions(session.Query, step.fields[2], typeMap, doc), empty_1(), ofArray(["↑↓ navigate", "↵ next", "⌘↵ create now", "⌫ back", "esc cancel"]));
                }
            }
        }
        else {
            return new PaletteState(true, "command", undefined, empty_1(), "Add action…", Palette_filterTemplates(session.Query), empty_1(), ofArray(["↑↓ navigate", "↵ select", "esc cancel"]));
        }
    }
}

export function Palette_openSession() {
    return new PaletteSession(Palette_empty.PickedKind, Palette_empty.Steps, 0, Palette_empty.Values, "");
}

export function Palette_setQuery(query, session) {
    return new PaletteSession(session.PickedKind, session.Steps, session.StepIndex, session.Values, query);
}

export function Palette_pickCommand(kindCase, session) {
    const steps = Palette_stepsFor(kindCase);
    return new PaletteSession(kindCase, steps, 0, ofList(collect((step) => {
        if (step.tag === 1) {
            return map_1((f) => [f.Key, f.Default.toString()], step.fields[1]);
        }
        else {
            return empty_1();
        }
    }, steps), {
        Compare: comparePrimitives,
    }), "");
}

export function Palette_pickItem(itemId, session) {
    if (session.StepIndex >= length(session.Steps)) {
        return session;
    }
    else {
        const matchValue = item_1(session.StepIndex, session.Steps);
        if (matchValue.tag === 0) {
            return new PaletteSession(session.PickedKind, session.Steps, session.StepIndex + 1, add(matchValue.fields[0], itemId, session.Values), "");
        }
        else {
            return session;
        }
    }
}

/**
 * Commit the current scalars step and advance.
 */
export function Palette_commitScalars(session) {
    return new PaletteSession(session.PickedKind, session.Steps, session.StepIndex + 1, session.Values, "");
}

/**
 * Update a single scalar field value (fire-and-forget during drag).
 */
export function Palette_setScalarField(key, value, session) {
    return new PaletteSession(session.PickedKind, session.Steps, session.StepIndex, add(key, value.toString(), session.Values), session.Query);
}

export function Palette_back(session) {
    if (session.StepIndex > 0) {
        return new PaletteSession(session.PickedKind, session.Steps, session.StepIndex - 1, session.Values, "");
    }
    else {
        return new PaletteSession(undefined, empty_1(), 0, empty({
            Compare: comparePrimitives,
        }), "");
    }
}

export function Palette_skipToEnd(session) {
    return new PaletteSession(session.PickedKind, session.Steps, length(session.Steps), session.Values, session.Query);
}

/**
 * Build a DocAction from the completed palette session.
 */
export function Palette_buildAction(session, idSuffix) {
    const matchValue = session.PickedKind;
    if (matchValue != null) {
        const kind = matchValue;
        const v = session.Values;
        const str = (key) => tryFind(key, v);
        const flt = (key_1, def) => defaultArg(bind((s) => {
            let matchValue_1;
            let outArg = 0;
            matchValue_1 = [tryParse(s, new FSharpRef(() => outArg, (v_1) => {
                outArg = v_1;
            })), outArg];
            if (matchValue_1[0]) {
                return matchValue_1[1];
            }
            else {
                return undefined;
            }
        }, tryFind(key_1, v)), def);
        const actionKind = (kind === "Sphere") ? (new ActionKind(2, [flt("radius", 8)])) : ((kind === "Cylinder") ? (new ActionKind(1, [flt("radius", 5), flt("height", 20)])) : ((kind === "Box") ? (new ActionKind(3, [flt("width", 10), flt("height", 10), flt("depth", 10)])) : ((kind === "HalfPlane") ? (new ActionKind(4, ["Z", flt("offset", 0), false])) : ((kind === "Translate") ? (new ActionKind(5, [str("child"), flt("x", 0), flt("y", 0), flt("z", 0)])) : ((kind === "Rotate") ? (new ActionKind(6, [str("child"), flt("ax", 0), flt("ay", 0), flt("az", 1), flt("angle", 0)])) : ((kind === "Move") ? (new ActionKind(7, [str("child"), str("frame")])) : ((kind === "Union") ? (new ActionKind(8, [str("a"), str("b"), flt("radius", 0)])) : ((kind === "Subtract") ? (new ActionKind(9, [str("a"), str("b"), flt("radius", 0)])) : ((kind === "Intersect") ? (new ActionKind(10, [str("a"), str("b"), flt("radius", 0)])) : ((kind === "Sketch") ? (new ActionKind(11, [str("origin"), SketchPlaneModule_defaults, ActionSketchModule_empty])) : ((kind === "FromSketch") ? (new ActionKind(12, [str("child"), false, FromSketchSelectionModule_defaults])) : ((kind === "Thicken") ? (new ActionKind(13, [str("child"), flt("amount", 2)])) : ((kind === "Shell") ? (new ActionKind(14, [str("child"), flt("thickness", 1)])) : ((kind === "Mesh") ? (new ActionKind(15, [str("child"), flt("size", 0.2), defaultArg(bind((s_1) => {
            let matchValue_2;
            let outArg_1 = 0;
            matchValue_2 = [tryParse_1(s_1, 511, false, 32, new FSharpRef(() => outArg_1, (v_2) => {
                outArg_1 = (v_2 | 0);
            })), outArg_1];
            if (matchValue_2[0]) {
                return matchValue_2[1];
            }
            else {
                return undefined;
            }
        }, tryFind("resolution", v)), 96)])) : (new ActionKind(0, []))))))))))))))));
        return new DocAction((kind.toLowerCase() + "_") + idSuffix, undefined, actionKind, true, undefined, undefined);
    }
    else {
        return undefined;
    }
}

