import { Record } from "../../ui/fable_modules/fable-library-js.4.29.0/Types.js";
import { record_type, class_type, option_type, list_type, string_type } from "../../ui/fable_modules/fable-library-js.4.29.0/Reflection.js";
import { DocAction, FieldSliceSettingsModule_defaults, DisplaySettingsModule_defaults, DocAction_$reflection } from "./Domain.fs.js";
import { SelectionTarget_$reflection } from "./Pickable.fs.js";
import { SketchUiState_$reflection } from "../Sketch/SketchAuthoring.fs.js";
import { Editor_sketchUiState, SketchLoopView, Editor_formatErrors, ActionErrorView_$reflection, SketchLoopView_$reflection } from "./Editor.fs.js";
import { map as map_1, contains, choose, findIndex, take, tryFind } from "../../ui/fable_modules/fable-library-js.4.29.0/List.js";
import { ofList, empty, tryFind as tryFind_1, map } from "../../ui/fable_modules/fable-library-js.4.29.0/Map.js";
import { comparePrimitives, safeHash, equals } from "../../ui/fable_modules/fable-library-js.4.29.0/Util.js";
import { TypeCheck_acceptedInputs } from "./TypeCheck.fs.js";
import { defaultArg } from "../../ui/fable_modules/fable-library-js.4.29.0/Option.js";
import { SketchLoops_detectLoops } from "../Sketch/SketchLoops.fs.js";
import { Palette_toState } from "./Palette.fs.js";

export class DocumentView extends Record {
    constructor(Name, Actions, SelectedId, SelectedTargets, SketchUi, RefOptions, SketchLoops, Errors) {
        super();
        this.Name = Name;
        this.Actions = Actions;
        this.SelectedId = SelectedId;
        this.SelectedTargets = SelectedTargets;
        this.SketchUi = SketchUi;
        this.RefOptions = RefOptions;
        this.SketchLoops = SketchLoops;
        this.Errors = Errors;
    }
}

export function DocumentView_$reflection() {
    return record_type("Server.DocumentView", [], DocumentView, () => [["Name", string_type], ["Actions", list_type(DocAction_$reflection())], ["SelectedId", option_type(string_type)], ["SelectedTargets", list_type(SelectionTarget_$reflection())], ["SketchUi", SketchUiState_$reflection()], ["RefOptions", class_type("Microsoft.FSharp.Collections.FSharpMap`2", [string_type, list_type(string_type)])], ["SketchLoops", class_type("Microsoft.FSharp.Collections.FSharpMap`2", [string_type, list_type(SketchLoopView_$reflection())])], ["Errors", list_type(ActionErrorView_$reflection())]]);
}

export function DocumentPipeline_documentView(state) {
    const tm = state.Compiled.TypeMap;
    const errors = Editor_formatErrors(state.Compiled.Errors);
    let refOptions;
    const matchValue = state.Doc.SelectedId;
    if (matchValue != null) {
        const selId = matchValue;
        const matchValue_1 = tryFind((a) => (a.Id === selId), state.Doc.Actions);
        if (matchValue_1 != null) {
            const sel = matchValue_1;
            const before = take(findIndex((a_1) => (a_1.Id === selId), state.Doc.Actions), state.Doc.Actions);
            refOptions = map((_key, types) => choose((a_2) => {
                const matchValue_2 = tryFind_1(a_2.Id, tm);
                let matchResult, t_1;
                if (matchValue_2 != null) {
                    if (contains(matchValue_2, types, {
                        Equals: equals,
                        GetHashCode: safeHash,
                    })) {
                        matchResult = 0;
                        t_1 = matchValue_2;
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
                        return a_2.Id;
                    default:
                        return undefined;
                }
            }, before), TypeCheck_acceptedInputs(sel.Kind));
        }
        else {
            refOptions = empty({
                Compare: comparePrimitives,
            });
        }
    }
    else {
        refOptions = empty({
            Compare: comparePrimitives,
        });
    }
    const actions = map_1((a_3) => {
        const matchValue_3 = tryFind_1(a_3.Id, tm);
        let matchResult_1;
        if (matchValue_3 != null) {
            if (matchValue_3.tag === 0) {
                matchResult_1 = 0;
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
                return new DocAction(a_3.Id, a_3.Name, a_3.Kind, a_3.Visible, defaultArg(a_3.Display, DisplaySettingsModule_defaults), defaultArg(a_3.FieldSlice, FieldSliceSettingsModule_defaults));
            default:
                return new DocAction(a_3.Id, a_3.Name, a_3.Kind, a_3.Visible, undefined, undefined);
        }
    }, state.Doc.Actions);
    const sketchLoops = ofList(choose((a_4) => {
        const matchValue_4 = a_4.Kind;
        if (matchValue_4.tag === 11) {
            return [a_4.Id, map_1((loop) => (new SketchLoopView(loop.Id, loop.EntityIds)), SketchLoops_detectLoops(matchValue_4.fields[2].Entities))];
        }
        else {
            return undefined;
        }
    }, actions), {
        Compare: comparePrimitives,
    });
    return new DocumentView(state.Doc.Name, actions, state.Doc.SelectedId, state.SelectedTargets, Editor_sketchUiState(state), refOptions, sketchLoops, errors);
}

export function DocumentPipeline_paletteView(state) {
    return Palette_toState(state.PaletteSession, state.Compiled.TypeMap, state.Doc);
}

