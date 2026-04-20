import { Record, Union } from "../../ui/fable_modules/fable-library-js.4.29.0/Types.js";
import { record_type, list_type, class_type, bool_type, union_type, float64_type, string_type } from "../../ui/fable_modules/fable-library-js.4.29.0/Reflection.js";
import { SlotRef, SlotRef_$reflection } from "./SlotTable.fs.js";
import { FromSketchSelection_$reflection, ActionSketch_$reflection } from "../Sketch/Sketch.fs.js";
import { singleton, append, empty, map, tail, head, isEmpty } from "../../ui/fable_modules/fable-library-js.4.29.0/List.js";
import { empty as empty_1, add, tryFind, ofList } from "../../ui/fable_modules/fable-library-js.4.29.0/Map.js";
import { disposeSafe, getEnumerator, comparePrimitives } from "../../ui/fable_modules/fable-library-js.4.29.0/Util.js";

export class FrameStep extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["FrameTranslate", "FrameRotate"];
    }
}

export function FrameStep_$reflection() {
    return union_type("Server.FrameStep", [], FrameStep, () => [[["actionId", string_type], ["x", SlotRef_$reflection()], ["y", SlotRef_$reflection()], ["z", SlotRef_$reflection()], ["xDefault", float64_type], ["yDefault", float64_type], ["zDefault", float64_type]], [["actionId", string_type], ["ax", SlotRef_$reflection()], ["ay", SlotRef_$reflection()], ["az", SlotRef_$reflection()], ["angle", SlotRef_$reflection()], ["axDefault", float64_type], ["ayDefault", float64_type], ["azDefault", float64_type], ["angleDefault", float64_type]]]);
}

export class Element$ extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["EEmpty", "ESphere", "ECylinder", "EBox", "EHalfPlane", "ETranslate", "ERotate", "EUnion", "ESubtract", "EIntersect", "EThicken", "EShell", "EFromSketch"];
    }
}

export function Element$_$reflection() {
    return union_type("Server.Element", [], Element$, () => [[], [["actionId", string_type], ["radius", float64_type]], [["actionId", string_type], ["radius", float64_type], ["height", float64_type]], [["actionId", string_type], ["width", float64_type], ["height", float64_type], ["depth", float64_type]], [["actionId", string_type], ["axis", string_type], ["offset", float64_type], ["flip", bool_type]], [["actionId", string_type], ["x", float64_type], ["y", float64_type], ["z", float64_type], ["child", Element$_$reflection()]], [["actionId", string_type], ["ax", float64_type], ["ay", float64_type], ["az", float64_type], ["angle", float64_type], ["child", Element$_$reflection()]], [["actionId", string_type], ["a", Element$_$reflection()], ["b", Element$_$reflection()], ["radius", float64_type]], [["actionId", string_type], ["a", Element$_$reflection()], ["b", Element$_$reflection()], ["radius", float64_type]], [["actionId", string_type], ["a", Element$_$reflection()], ["b", Element$_$reflection()], ["radius", float64_type]], [["actionId", string_type], ["child", Element$_$reflection()], ["amount", float64_type]], [["actionId", string_type], ["child", Element$_$reflection()], ["thickness", float64_type]], [["actionId", string_type], ["sketchActionId", string_type], ["sketch", ActionSketch_$reflection()], ["selection", FromSketchSelection_$reflection()], ["flip", bool_type]]]);
}

export class BuildResult extends Record {
    constructor(Elements, Frames) {
        super();
        this.Elements = Elements;
        this.Frames = Frames;
    }
}

export function BuildResult_$reflection() {
    return record_type("Server.BuildResult", [], BuildResult, () => [["Elements", class_type("Microsoft.FSharp.Collections.FSharpMap`2", [string_type, Element$_$reflection()])], ["Frames", class_type("Microsoft.FSharp.Collections.FSharpMap`2", [string_type, list_type(FrameStep_$reflection())])]]);
}

/**
 * Wrap a field child with a frame's transform chain.
 * Steps are ordered from child/local to parent/world.
 */
export function ElementModule_applyFrame(chain, child) {
    if (!isEmpty(chain)) {
        if (head(chain).tag === 1) {
            return new Element$(6, [head(chain).fields[0], head(chain).fields[5], head(chain).fields[6], head(chain).fields[7], head(chain).fields[8], ElementModule_applyFrame(tail(chain), child)]);
        }
        else {
            return new Element$(5, [head(chain).fields[0], head(chain).fields[4], head(chain).fields[5], head(chain).fields[6], ElementModule_applyFrame(tail(chain), child)]);
        }
    }
    else {
        return child;
    }
}

/**
 * Build element trees and frame chains from a type-checked action graph.
 * Only Field-typed actions get Element entries; Frame-typed actions get
 * FrameChain entries instead.
 */
export function ElementModule_build(actions, typeMap) {
    let matchValue;
    const actionMap = ofList(map((a) => [a.Id, a], actions), {
        Compare: comparePrimitives,
    });
    const frameChain = (id_1, cache) => {
        const matchValue_1 = tryFind(id_1, cache);
        if (matchValue_1 == null) {
            const matchValue_2 = tryFind(id_1, actionMap);
            if (matchValue_2 != null) {
                const action = matchValue_2;
                let patternInput_2;
                const matchValue_3 = action.Kind;
                switch (matchValue_3.tag) {
                    case 0: {
                        patternInput_2 = [empty(), cache];
                        break;
                    }
                    case 5: {
                        const child = matchValue_3.fields[0];
                        const xRef = new SlotRef(action.Id, "x");
                        const yRef = new SlotRef(action.Id, "y");
                        const zRef = new SlotRef(action.Id, "z");
                        const patternInput = (child == null) ? [empty(), cache] : frameChain(child, cache);
                        patternInput_2 = [append(patternInput[0], singleton(new FrameStep(0, [action.Id, xRef, yRef, zRef, matchValue_3.fields[1], matchValue_3.fields[2], matchValue_3.fields[3]]))), patternInput[1]];
                        break;
                    }
                    case 6: {
                        const child_1 = matchValue_3.fields[0];
                        const axRef = new SlotRef(action.Id, "ax");
                        const ayRef = new SlotRef(action.Id, "ay");
                        const azRef = new SlotRef(action.Id, "az");
                        const angleRef = new SlotRef(action.Id, "angle");
                        const patternInput_1 = (child_1 == null) ? [empty(), cache] : frameChain(child_1, cache);
                        patternInput_2 = [append(patternInput_1[0], singleton(new FrameStep(1, [action.Id, axRef, ayRef, azRef, angleRef, matchValue_3.fields[1], matchValue_3.fields[2], matchValue_3.fields[3], matchValue_3.fields[4]]))), patternInput_1[1]];
                        break;
                    }
                    default:
                        patternInput_2 = [empty(), cache];
                }
                const chain = patternInput_2[0];
                return [chain, add(id_1, chain, patternInput_2[1])];
            }
            else {
                return [empty(), cache];
            }
        }
        else {
            return [matchValue_1, cache];
        }
    };
    const compile = (id_2) => ((state) => {
        const matchValue_4 = tryFind(id_2, state[0]);
        if (matchValue_4 == null) {
            const matchValue_5 = tryFind(id_2, actionMap);
            if (matchValue_5 != null) {
                const patternInput_3 = compileKind(matchValue_5)(state);
                const state_1 = patternInput_3[1];
                const elem_1 = patternInput_3[0];
                return [elem_1, [add(id_2, elem_1, state_1[0]), state_1[1]]];
            }
            else {
                return [new Element$(0, []), state];
            }
        }
        else {
            return [matchValue_4, state];
        }
    });
    const resolveChild = (id_3) => ((state_2) => ((id_3 != null) ? compile(id_3)(state_2) : [new Element$(0, []), state_2]));
    const resolveFrame = (id_4) => ((state_3) => {
        if (id_4 != null) {
            const patternInput_4 = frameChain(id_4, state_3[1]);
            return [patternInput_4[0], [state_3[0], patternInput_4[1]]];
        }
        else {
            return [empty(), state_3];
        }
    });
    const compileKind = (action_2) => ((state_4) => {
        const id_5 = action_2.Id;
        const matchValue_6 = action_2.Kind;
        switch (matchValue_6.tag) {
            case 2:
                return [new Element$(1, [id_5, matchValue_6.fields[0]]), state_4];
            case 1:
                return [new Element$(2, [id_5, matchValue_6.fields[0], matchValue_6.fields[1]]), state_4];
            case 3:
                return [new Element$(3, [id_5, matchValue_6.fields[0], matchValue_6.fields[1], matchValue_6.fields[2]]), state_4];
            case 4:
                return [new Element$(4, [id_5, matchValue_6.fields[0], matchValue_6.fields[1], matchValue_6.fields[2]]), state_4];
            case 11:
                return [new Element$(0, []), state_4];
            case 5: {
                const patternInput_5 = resolveChild(matchValue_6.fields[0])(state_4);
                return [new Element$(5, [id_5, matchValue_6.fields[1], matchValue_6.fields[2], matchValue_6.fields[3], patternInput_5[0]]), patternInput_5[1]];
            }
            case 6: {
                const patternInput_6 = resolveChild(matchValue_6.fields[0])(state_4);
                return [new Element$(6, [id_5, matchValue_6.fields[1], matchValue_6.fields[2], matchValue_6.fields[3], matchValue_6.fields[4], patternInput_6[0]]), patternInput_6[1]];
            }
            case 7: {
                const patternInput_7 = resolveChild(matchValue_6.fields[0])(state_4);
                const patternInput_8 = resolveFrame(matchValue_6.fields[1])(patternInput_7[1]);
                return [ElementModule_applyFrame(patternInput_8[0], patternInput_7[0]), patternInput_8[1]];
            }
            case 8: {
                const patternInput_9 = resolveChild(matchValue_6.fields[0])(state_4);
                const patternInput_10 = resolveChild(matchValue_6.fields[1])(patternInput_9[1]);
                return [new Element$(7, [id_5, patternInput_9[0], patternInput_10[0], matchValue_6.fields[2]]), patternInput_10[1]];
            }
            case 9: {
                const patternInput_11 = resolveChild(matchValue_6.fields[0])(state_4);
                const patternInput_12 = resolveChild(matchValue_6.fields[1])(patternInput_11[1]);
                return [new Element$(8, [id_5, patternInput_11[0], patternInput_12[0], matchValue_6.fields[2]]), patternInput_12[1]];
            }
            case 10: {
                const patternInput_13 = resolveChild(matchValue_6.fields[0])(state_4);
                const patternInput_14 = resolveChild(matchValue_6.fields[1])(patternInput_13[1]);
                return [new Element$(9, [id_5, patternInput_13[0], patternInput_14[0], matchValue_6.fields[2]]), patternInput_14[1]];
            }
            case 13: {
                const patternInput_15 = resolveChild(matchValue_6.fields[0])(state_4);
                return [new Element$(10, [id_5, patternInput_15[0], matchValue_6.fields[1]]), patternInput_15[1]];
            }
            case 14: {
                const patternInput_16 = resolveChild(matchValue_6.fields[0])(state_4);
                return [new Element$(11, [id_5, patternInput_16[0], matchValue_6.fields[1]]), patternInput_16[1]];
            }
            case 12: {
                const child_7 = matchValue_6.fields[0];
                if (child_7 != null) {
                    const sketchId = child_7;
                    const matchValue_7 = tryFind(sketchId, actionMap);
                    if (matchValue_7 == null) {
                        return [new Element$(0, []), state_4];
                    }
                    else {
                        const matchValue_8 = matchValue_7.Kind;
                        if (matchValue_8.tag === 11) {
                            const plane = matchValue_8.fields[1];
                            const originId = matchValue_8.fields[0];
                            let patternInput_18;
                            if (originId == null) {
                                patternInput_18 = [empty(), state_4];
                            }
                            else {
                                const patternInput_17 = frameChain(originId, state_4[1]);
                                patternInput_18 = [patternInput_17[0], [state_4[0], patternInput_17[1]]];
                            }
                            const core = new Element$(12, [id_5, sketchId, matchValue_8.fields[2], matchValue_6.fields[2], matchValue_6.fields[1]]);
                            return [ElementModule_applyFrame(patternInput_18[0], (plane.tag === 1) ? (new Element$(6, [`${id_5}_plane`, 1, 0, 0, 3.141592653589793 * 0.5, core])) : ((plane.tag === 2) ? (new Element$(6, [`${id_5}_plane_z`, 0, 0, 1, 3.141592653589793 * 0.5, new Element$(6, [`${id_5}_plane_x`, 1, 0, 0, 3.141592653589793 * 0.5, core])])) : core)), patternInput_18[1]];
                        }
                        else {
                            return [new Element$(0, []), state_4];
                        }
                    }
                }
                else {
                    return [new Element$(0, []), state_4];
                }
            }
            case 15:
                return [new Element$(0, []), resolveChild(matchValue_6.fields[0])(state_4)[1]];
            default:
                return [new Element$(0, []), state_4];
        }
    });
    let state_19 = [empty_1({
        Compare: comparePrimitives,
    }), empty_1({
        Compare: comparePrimitives,
    })];
    const enumerator = getEnumerator(actions);
    try {
        while (enumerator["System.Collections.IEnumerator.MoveNext"]()) {
            const action_3 = enumerator["System.Collections.Generic.IEnumerator`1.get_Current"]();
            if (action_3.Visible && ((matchValue = tryFind(action_3.Id, typeMap), (matchValue != null) && (matchValue.tag === 0)))) {
                const patternInput_20 = compile(action_3.Id)(state_19);
                state_19 = patternInput_20[1];
            }
            const matchValue_9 = tryFind(action_3.Id, typeMap);
            let matchResult;
            if (matchValue_9 != null) {
                if (matchValue_9.tag === 2) {
                    matchResult = 0;
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
                    const frames_3 = state_19[1];
                    const built_3 = state_19[0];
                    const patternInput_21 = frameChain(action_3.Id, frames_3);
                    state_19 = [built_3, patternInput_21[1]];
                    break;
                }
            }
        }
    }
    finally {
        disposeSafe(enumerator);
    }
    const frames_4 = state_19[1];
    return new BuildResult(state_19[0], frames_4);
}

