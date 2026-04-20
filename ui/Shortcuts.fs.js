import { Editor_geometricConstraintName, Message, Editor_tryConstraintPlacementKind, ConstraintPlacementKind, GeometricConstraintKind, SketchToolKind } from "../core/Editor/Editor.fs.js";
import { item, length, tryFindIndex, tryFind, singleton, ofArray } from "./fable_modules/fable-library-js.4.29.0/List.js";
import { defaultArg, map, bind } from "./fable_modules/fable-library-js.4.29.0/Option.js";
import { tryFind as tryFind_1 } from "./fable_modules/fable-library-js.4.29.0/Map.js";
import { equals } from "./fable_modules/fable-library-js.4.29.0/Util.js";
import { max, min } from "./fable_modules/fable-library-js.4.29.0/Double.js";

const toolShortcuts = ofArray([["l", new SketchToolKind(1, [])], ["g", new SketchToolKind(2, [])], ["c", new SketchToolKind(4, [])], ["u", new SketchToolKind(5, [])]]);

const toolShiftShortcuts = singleton(["g", new SketchToolKind(3, [])]);

const geometricShortcuts = ofArray([["i", new GeometricConstraintKind(0, [])], ["h", new GeometricConstraintKind(1, [])], ["v", new GeometricConstraintKind(2, [])], ["b", new GeometricConstraintKind(4, [])], ["t", new GeometricConstraintKind(7, [])], ["e", new GeometricConstraintKind(6, [])]]);

const geometricShiftShortcuts = ofArray([["o", new GeometricConstraintKind(8, [])], ["l", new GeometricConstraintKind(5, [])], ["m", new GeometricConstraintKind(3, [])], ["j", new GeometricConstraintKind(9, [])]]);

const dimensionShortcuts = ofArray([["d", new ConstraintPlacementKind(0, [])], ["a", new ConstraintPlacementKind(1, [])]]);

function isEditable(target) {
    if (target == null) {
        return false;
    }
    else {
        const el = target;
        const tag = el.tagName;
        if (((tag === "INPUT") ? true : (tag === "TEXTAREA")) ? true : (tag === "SELECT")) {
            return true;
        }
        else {
            return el.isContentEditable;
        }
    }
}

function selectedAction(doc) {
    return bind((id) => tryFind((a) => (a.Id === id), doc.Actions), doc.SelectedId);
}

function selectedIsSketch(doc) {
    const matchValue = selectedAction(doc);
    let matchResult;
    if (matchValue != null) {
        if (matchValue.Kind.tag === 11) {
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
        case 0:
            return true;
        default:
            return false;
    }
}

function handleSketchShortcut(dispatch, doc, e) {
    if (!doc.SketchUi.EditMode ? true : !selectedIsSketch(doc)) {
        return false;
    }
    else if ((e.metaKey ? true : e.ctrlKey) ? true : e.altKey) {
        return false;
    }
    else {
        const key = e.key.toLocaleLowerCase();
        if (e.key === "Escape") {
            e.preventDefault();
            const matchValue = bind(Editor_tryConstraintPlacementKind, doc.SketchUi.ConstraintPlacementMode);
            if (matchValue == null) {
                if (doc.SketchUi.Tool !== "none") {
                    dispatch(new Message(32, [new SketchToolKind(0, [])]));
                    return true;
                }
                else {
                    dispatch(new Message(31, []));
                    return true;
                }
            }
            else {
                dispatch(new Message(33, [matchValue]));
                return true;
            }
        }
        else {
            const tool = e.shiftKey ? map((tuple) => tuple[1], tryFind((tupledArg) => (tupledArg[0] === key), toolShiftShortcuts)) : map((tuple_1) => tuple_1[1], tryFind((tupledArg_1) => (tupledArg_1[0] === key), toolShortcuts));
            if (tool == null) {
                const dimension = e.shiftKey ? undefined : map((tuple_2) => tuple_2[1], tryFind((tupledArg_2) => (tupledArg_2[0] === key), dimensionShortcuts));
                if (dimension == null) {
                    const constraintKind = e.shiftKey ? map((tuple_3) => tuple_3[1], tryFind((tupledArg_3) => (tupledArg_3[0] === key), geometricShiftShortcuts)) : map((tuple_4) => tuple_4[1], tryFind((tupledArg_4) => (tupledArg_4[0] === key), geometricShortcuts));
                    if (constraintKind == null) {
                        return false;
                    }
                    else {
                        const c = constraintKind;
                        if (defaultArg(tryFind_1(Editor_geometricConstraintName(c), doc.SketchUi.ConstraintAvailability), false)) {
                            e.preventDefault();
                            dispatch(new Message(34, [c]));
                            return true;
                        }
                        else {
                            return false;
                        }
                    }
                }
                else {
                    const d = dimension;
                    e.preventDefault();
                    dispatch(new Message(33, [d]));
                    return true;
                }
            }
            else {
                const t = tool;
                e.preventDefault();
                dispatch(new Message(32, [t]));
                return true;
            }
        }
    }
}

/**
 * Wire up the global keyboard handler once at mount time. Reads current
 * state via `getDoc` and `getPaletteOpen` so each keystroke sees the
 * up-to-date view.
 */
export function register(dispatch, getDoc, getPaletteOpen, onSave, onLoad) {
    document.addEventListener("keydown", (e) => {
        const ke = e;
        if (((ke.metaKey ? true : ke.ctrlKey) && !ke.altKey) && !ke.shiftKey) {
            const matchValue = ke.key.toLocaleLowerCase();
            switch (matchValue) {
                case "k": {
                    e.preventDefault();
                    dispatch(new Message(37, []));
                    break;
                }
                case "s": {
                    e.preventDefault();
                    onSave();
                    break;
                }
                case "o": {
                    e.preventDefault();
                    onLoad();
                    break;
                }
                default:
                    undefined;
            }
        }
        else if (getPaletteOpen()) {
        }
        else if (isEditable(e.target)) {
        }
        else {
            const doc = getDoc();
            if (handleSketchShortcut(dispatch, doc, ke)) {
            }
            else {
                const matchValue_1 = ke.key;
                switch (matchValue_1) {
                    case "Delete":
                    case "Backspace": {
                        e.preventDefault();
                        dispatch(new Message(15, []));
                        break;
                    }
                    case "ArrowDown":
                    case "ArrowUp": {
                        e.preventDefault();
                        const actions = doc.Actions;
                        const idx = defaultArg(tryFindIndex((a) => equals(a.Id, doc.SelectedId), actions), -1) | 0;
                        if (idx >= 0) {
                            const next = ((ke.key === "ArrowDown") ? min(idx + 1, length(actions) - 1) : max(idx - 1, 0)) | 0;
                            if (next !== idx) {
                                dispatch(new Message(0, [item(next, actions).Id]));
                            }
                        }
                        break;
                    }
                    case "v": {
                        const matchValue_2 = selectedAction(doc);
                        if (matchValue_2 == null) {
                        }
                        else {
                            const sel = matchValue_2;
                            if (sel.Kind.tag === 0) {
                            }
                            else {
                                e.preventDefault();
                                dispatch(new Message(8, [sel.Id]));
                            }
                        }
                        break;
                    }
                    case "s": {
                        const matchValue_4 = selectedAction(doc);
                        let matchResult, sel_2;
                        if (matchValue_4 != null) {
                            if (matchValue_4.Display != null) {
                                matchResult = 0;
                                sel_2 = matchValue_4;
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
                                e.preventDefault();
                                dispatch(new Message(9, [sel_2.Id]));
                                break;
                            }
                            case 1: {
                                break;
                            }
                        }
                        break;
                    }
                    case "e":
                    case "E": {
                        const matchValue_5 = selectedAction(doc);
                        let matchResult_1, id;
                        if (matchValue_5 != null) {
                            if (matchValue_5.Kind.tag === 11) {
                                matchResult_1 = 0;
                                id = matchValue_5.Id;
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
                                e.preventDefault();
                                dispatch(new Message(31, []));
                                break;
                            }
                            case 1: {
                                break;
                            }
                        }
                        break;
                    }
                    case "f": {
                        const matchValue_6 = selectedAction(doc);
                        let matchResult_2, sel_4;
                        if (matchValue_6 != null) {
                            if (matchValue_6.FieldSlice != null) {
                                matchResult_2 = 0;
                                sel_4 = matchValue_6;
                            }
                            else {
                                matchResult_2 = 1;
                            }
                        }
                        else {
                            matchResult_2 = 1;
                        }
                        switch (matchResult_2) {
                            case 0: {
                                e.preventDefault();
                                dispatch(new Message(11, [sel_4.Id]));
                                break;
                            }
                            case 1: {
                                break;
                            }
                        }
                        break;
                    }
                    default:
                        undefined;
                }
            }
        }
    });
}

