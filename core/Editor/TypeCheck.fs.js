import { Record, Union } from "../../ui/fable_modules/fable-library-js.4.29.0/Types.js";
import { record_type, class_type, tuple_type, list_type, string_type, union_type } from "../../ui/fable_modules/fable-library-js.4.29.0/Reflection.js";
import { add, empty, ofList, tryFind } from "../../ui/fable_modules/fable-library-js.4.29.0/Map.js";
import { reverse, empty as empty_1, fold, mapIndexed, singleton, ofArray, contains, cons } from "../../ui/fable_modules/fable-library-js.4.29.0/List.js";
import { comparePrimitives, safeHash, equals } from "../../ui/fable_modules/fable-library-js.4.29.0/Util.js";
import { map, defaultArg } from "../../ui/fable_modules/fable-library-js.4.29.0/Option.js";

export class FieldType extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["Field", "Sketch", "Frame", "Mesh"];
    }
}

export function FieldType_$reflection() {
    return union_type("Server.FieldType", [], FieldType, () => [[], [], [], []]);
}

export class TypeError$ extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["MissingRef", "RefNotFound", "ForwardRef", "TypeMismatch"];
    }
}

export function TypeError$_$reflection() {
    return union_type("Server.TypeError", [], TypeError$, () => [[["actionId", string_type], ["key", string_type]], [["actionId", string_type], ["key", string_type], ["target", string_type]], [["actionId", string_type], ["key", string_type], ["target", string_type]], [["actionId", string_type], ["key", string_type], ["expected", list_type(FieldType_$reflection())], ["got", FieldType_$reflection()]]]);
}

export class TypedAction extends Record {
    constructor(Id, Output, Inputs) {
        super();
        this.Id = Id;
        this.Output = Output;
        this.Inputs = Inputs;
    }
}

export function TypedAction_$reflection() {
    return record_type("Server.TypedAction", [], TypedAction, () => [["Id", string_type], ["Output", FieldType_$reflection()], ["Inputs", class_type("Microsoft.FSharp.Collections.FSharpMap`2", [string_type, tuple_type(string_type, FieldType_$reflection())])]]);
}

function TypeCheck_resolveRef(actionId, key, ref, seen, types, index, errors) {
    if (ref != null) {
        const targetId = ref;
        const matchValue = tryFind(targetId, types);
        if (matchValue == null) {
            const matchValue_1 = tryFind(targetId, seen);
            let matchResult, ti_1;
            if (matchValue_1 != null) {
                if (matchValue_1 >= index) {
                    matchResult = 0;
                    ti_1 = matchValue_1;
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
                    return [undefined, cons(new TypeError$(2, [actionId, key, targetId]), errors)];
                default:
                    return [undefined, cons(new TypeError$(1, [actionId, key, targetId]), errors)];
            }
        }
        else {
            return [[targetId, matchValue], errors];
        }
    }
    else {
        return [undefined, cons(new TypeError$(0, [actionId, key]), errors)];
    }
}

function TypeCheck_resolveTyped(actionId, key, ref, expected, seen, types, index, errors) {
    let tid;
    const patternInput = TypeCheck_resolveRef(actionId, key, ref, seen, types, index, errors);
    const resolved = patternInput[0];
    const errors_1 = patternInput[1];
    if (resolved == null) {
        return [undefined, errors_1];
    }
    else if ((tid = resolved[0], contains(resolved[1], expected, {
        Equals: equals,
        GetHashCode: safeHash,
    }))) {
        const t_1 = resolved[1];
        const tid_1 = resolved[0];
        return [[tid_1, t_1], errors_1];
    }
    else {
        const _tid = resolved[0];
        const t_2 = resolved[1];
        return [undefined, cons(new TypeError$(3, [actionId, key, expected, t_2]), errors_1)];
    }
}

/**
 * For a given ActionKind, returns the accepted input types per ref slot.
 */
export function TypeCheck_acceptedInputs(kind) {
    const fieldOrFrame = ofArray([new FieldType(0, []), new FieldType(2, [])]);
    const fieldOnly = singleton(new FieldType(0, []));
    const frameOnly = singleton(new FieldType(2, []));
    switch (kind.tag) {
        case 5:
            return ofList(singleton(["child", fieldOrFrame]), {
                Compare: comparePrimitives,
            });
        case 6:
            return ofList(singleton(["child", fieldOrFrame]), {
                Compare: comparePrimitives,
            });
        case 7:
            return ofList(ofArray([["child", fieldOrFrame], ["frame", frameOnly]]), {
                Compare: comparePrimitives,
            });
        case 8:
        case 9:
        case 10:
            return ofList(ofArray([["a", fieldOnly], ["b", fieldOnly]]), {
                Compare: comparePrimitives,
            });
        case 11:
            return ofList(singleton(["origin", frameOnly]), {
                Compare: comparePrimitives,
            });
        case 12:
            return ofList(singleton(["child", singleton(new FieldType(1, []))]), {
                Compare: comparePrimitives,
            });
        case 13:
        case 14:
            return ofList(singleton(["child", fieldOnly]), {
                Compare: comparePrimitives,
            });
        case 15:
            return ofList(singleton(["child", fieldOnly]), {
                Compare: comparePrimitives,
            });
        default:
            return empty({
                Compare: comparePrimitives,
            });
    }
}

function TypeCheck_emit(id, output, inputs, types, typed, errors) {
    return [add(id, output, types), cons(new TypedAction(id, output, inputs), typed), errors];
}

export class TypeCheck_TypecheckResult extends Record {
    constructor(Typed, Errors) {
        super();
        this.Typed = Typed;
        this.Errors = Errors;
    }
}

export function TypeCheck_TypecheckResult_$reflection() {
    return record_type("Server.TypeCheck.TypecheckResult", [], TypeCheck_TypecheckResult, () => [["Typed", list_type(TypedAction_$reflection())], ["Errors", list_type(TypeError$_$reflection())]]);
}

/**
 * Type-check the full action list.
 * Always produces typed actions (best-effort) plus any errors found.
 */
export function TypeCheck_typecheck(actions) {
    const seen = ofList(mapIndexed((i, a) => [a.Id, i], actions), {
        Compare: comparePrimitives,
    });
    let patternInput_10;
    const list_2 = mapIndexed((i_1, a_2) => [i_1, a_2], actions);
    patternInput_10 = fold((tupledArg, tupledArg_1) => {
        const types = tupledArg[0];
        const typed = tupledArg[1];
        const errors = tupledArg[2];
        const index = tupledArg_1[0] | 0;
        const action = tupledArg_1[1];
        const id = action.Id;
        const addInput = (key, resolved, inputs) => {
            if (resolved == null) {
                return inputs;
            }
            else {
                return add(key, [resolved[0], resolved[1]], inputs);
            }
        };
        const matchValue = action.Kind;
        let matchResult, a_1, b, child_3;
        switch (matchValue.tag) {
            case 2:
            case 1:
            case 3:
            case 4: {
                matchResult = 1;
                break;
            }
            case 11: {
                matchResult = 2;
                break;
            }
            case 5: {
                matchResult = 3;
                break;
            }
            case 6: {
                matchResult = 4;
                break;
            }
            case 7: {
                matchResult = 5;
                break;
            }
            case 12: {
                matchResult = 8;
                break;
            }
            case 15: {
                matchResult = 9;
                break;
            }
            case 8: {
                matchResult = 6;
                a_1 = matchValue.fields[0];
                b = matchValue.fields[1];
                break;
            }
            case 9: {
                matchResult = 6;
                a_1 = matchValue.fields[0];
                b = matchValue.fields[1];
                break;
            }
            case 10: {
                matchResult = 6;
                a_1 = matchValue.fields[0];
                b = matchValue.fields[1];
                break;
            }
            case 13: {
                matchResult = 7;
                child_3 = matchValue.fields[0];
                break;
            }
            case 14: {
                matchResult = 7;
                child_3 = matchValue.fields[0];
                break;
            }
            default:
                matchResult = 0;
        }
        switch (matchResult) {
            case 0:
                return TypeCheck_emit(id, new FieldType(2, []), empty({
                    Compare: comparePrimitives,
                }), types, typed, errors);
            case 1:
                return TypeCheck_emit(id, new FieldType(0, []), empty({
                    Compare: comparePrimitives,
                }), types, typed, errors);
            case 2: {
                const origin = matchValue.fields[0];
                const patternInput = (origin != null) ? TypeCheck_resolveTyped(id, "origin", origin, singleton(new FieldType(2, [])), seen, types, index, errors) : [undefined, errors];
                return TypeCheck_emit(id, new FieldType(1, []), addInput("origin", patternInput[0], empty({
                    Compare: comparePrimitives,
                })), types, typed, patternInput[1]);
            }
            case 3: {
                const patternInput_1 = TypeCheck_resolveTyped(id, "child", matchValue.fields[0], ofArray([new FieldType(0, []), new FieldType(2, [])]), seen, types, index, errors);
                const resolved_2 = patternInput_1[0];
                return TypeCheck_emit(id, defaultArg(map((tuple) => tuple[1], resolved_2), new FieldType(0, [])), addInput("child", resolved_2, empty({
                    Compare: comparePrimitives,
                })), types, typed, patternInput_1[1]);
            }
            case 4: {
                const patternInput_2 = TypeCheck_resolveTyped(id, "child", matchValue.fields[0], ofArray([new FieldType(0, []), new FieldType(2, [])]), seen, types, index, errors);
                const resolved_3 = patternInput_2[0];
                return TypeCheck_emit(id, defaultArg(map((tuple_1) => tuple_1[1], resolved_3), new FieldType(0, [])), addInput("child", resolved_3, empty({
                    Compare: comparePrimitives,
                })), types, typed, patternInput_2[1]);
            }
            case 5: {
                const patternInput_3 = TypeCheck_resolveTyped(id, "child", matchValue.fields[0], ofArray([new FieldType(0, []), new FieldType(2, [])]), seen, types, index, errors);
                const rChild = patternInput_3[0];
                const patternInput_4 = TypeCheck_resolveTyped(id, "frame", matchValue.fields[1], singleton(new FieldType(2, [])), seen, types, index, patternInput_3[1]);
                return TypeCheck_emit(id, defaultArg(map((tuple_2) => tuple_2[1], rChild), new FieldType(0, [])), addInput("frame", patternInput_4[0], addInput("child", rChild, empty({
                    Compare: comparePrimitives,
                }))), types, typed, patternInput_4[1]);
            }
            case 6: {
                const patternInput_5 = TypeCheck_resolveTyped(id, "a", a_1, singleton(new FieldType(0, [])), seen, types, index, errors);
                const patternInput_6 = TypeCheck_resolveTyped(id, "b", b, singleton(new FieldType(0, [])), seen, types, index, patternInput_5[1]);
                return TypeCheck_emit(id, new FieldType(0, []), addInput("b", patternInput_6[0], addInput("a", patternInput_5[0], empty({
                    Compare: comparePrimitives,
                }))), types, typed, patternInput_6[1]);
            }
            case 7: {
                const patternInput_7 = TypeCheck_resolveTyped(id, "child", child_3, singleton(new FieldType(0, [])), seen, types, index, errors);
                return TypeCheck_emit(id, new FieldType(0, []), addInput("child", patternInput_7[0], empty({
                    Compare: comparePrimitives,
                })), types, typed, patternInput_7[1]);
            }
            case 8: {
                const patternInput_8 = TypeCheck_resolveTyped(id, "child", matchValue.fields[0], singleton(new FieldType(1, [])), seen, types, index, errors);
                return TypeCheck_emit(id, new FieldType(0, []), addInput("child", patternInput_8[0], empty({
                    Compare: comparePrimitives,
                })), types, typed, patternInput_8[1]);
            }
            default: {
                const patternInput_9 = TypeCheck_resolveTyped(id, "child", matchValue.fields[0], singleton(new FieldType(0, [])), seen, types, index, errors);
                return TypeCheck_emit(id, new FieldType(3, []), addInput("child", patternInput_9[0], empty({
                    Compare: comparePrimitives,
                })), types, typed, patternInput_9[1]);
            }
        }
    }, [empty({
        Compare: comparePrimitives,
    }), empty_1(), empty_1()], list_2);
    return new TypeCheck_TypecheckResult(reverse(patternInput_10[1]), reverse(patternInput_10[2]));
}

