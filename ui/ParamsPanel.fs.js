import { DocumentModule_pathOfFieldSliceField, FieldSliceField, DocumentModule_pathOfDisplayField, DisplayField, ActionParamField, DocumentModule_pathOfParamField, ParamValue } from "../core/Editor/Domain.fs.js";
import { filter, tryFind as tryFind_1, head, choose, map as map_1, isEmpty, iterateIndexed, empty, ofArray } from "./fable_modules/fable-library-js.4.29.0/List.js";
import { item, map } from "./fable_modules/fable-library-js.4.29.0/Array.js";
import { kbdHint, kbdHintTitled, setupDraggable, elText, el } from "./Dom.fs.js";
import { printf, toText } from "./fable_modules/fable-library-js.4.29.0/String.js";
import { Editor_setFieldSliceValue, Editor_setDisplayValue, Message, Editor_setActionParamValue } from "../core/Editor/Editor.fs.js";
import { comparePrimitives, equals, disposeSafe, getEnumerator } from "./fable_modules/fable-library-js.4.29.0/Util.js";
import { bind, defaultArg } from "./fable_modules/fable-library-js.4.29.0/Option.js";
import { kindLabel } from "./ActionList.fs.js";
import { ofList, tryFind } from "./fable_modules/fable-library-js.4.29.0/Map.js";
import { forKind } from "./Icons.fs.js";
import { toString } from "./fable_modules/fable-library-js.4.29.0/Types.js";
import { SlotRef } from "../core/Editor/SlotTable.fs.js";

function vFloat(x) {
    return new ParamValue(3, [x]);
}

function vString(s) {
    return new ParamValue(4, [s]);
}

function vBool(b) {
    return new ParamValue(1, [b]);
}

function vColor(rgb) {
    return new ParamValue(5, [ofArray(map((Item) => (new ParamValue(3, [Item])), rgb))]);
}

function controlDrag(dispatch, label, value, actionId, field) {
    const row = el("div", "control-row");
    row.appendChild(elText("span", "control-name", label));
    const valSpan = elText("span", "control-value", toText(printf("%.1f"))(value));
    valSpan.dataset.slotActionId = actionId;
    valSpan.dataset.slotPath = DocumentModule_pathOfParamField(field);
    const dispatchValue = (nextValue) => {
        dispatch(Editor_setActionParamValue(actionId, field, vFloat(nextValue)));
    };
    setupDraggable(valSpan, value, dispatchValue, dispatchValue);
    row.appendChild(valSpan);
    return row;
}

function option(value, label, selected) {
    const o = document.createElement("option");
    o.value = value;
    o.textContent = label;
    if (selected) {
        o.selected = true;
    }
    return o;
}

function controlRef(dispatch, label, current, options, actionId, field) {
    const row = el("div", "control-row");
    row.appendChild(elText("span", "control-name", label));
    const select = document.createElement("select");
    select.className = "control-ref";
    select.appendChild(option("", "–", current == null));
    const enumerator = getEnumerator(options);
    try {
        while (enumerator["System.Collections.IEnumerator.MoveNext"]()) {
            const opt = enumerator["System.Collections.Generic.IEnumerator`1.get_Current"]();
            const txt = defaultArg(opt.Name, kindLabel(opt.Kind));
            select.appendChild(option(opt.Id, txt, equals(opt.Id, current)));
        }
    }
    finally {
        disposeSafe(enumerator);
    }
    select.addEventListener("change", (_arg) => {
        dispatch(Editor_setActionParamValue(actionId, field, vString(select.value)));
    });
    row.appendChild(select);
    return row;
}

function controlSelect(dispatch, label, current, choices, actionId, field) {
    const row = el("div", "control-row");
    row.appendChild(elText("span", "control-name", label));
    const select = document.createElement("select");
    select.className = "control-select";
    const enumerator = getEnumerator(choices);
    try {
        while (enumerator["System.Collections.IEnumerator.MoveNext"]()) {
            const c = enumerator["System.Collections.Generic.IEnumerator`1.get_Current"]();
            select.appendChild(option(c, c, c === current));
        }
    }
    finally {
        disposeSafe(enumerator);
    }
    select.addEventListener("change", (_arg) => {
        dispatch(Editor_setActionParamValue(actionId, field, vString(select.value)));
    });
    row.appendChild(select);
    return row;
}

function controlCheck(dispatch, label, checked_, actionId, field) {
    const row = el("div", "control-row control-check");
    const input = document.createElement("input");
    input.type = "checkbox";
    input.checked = checked_;
    input.addEventListener("change", (_arg) => {
        dispatch(Editor_setActionParamValue(actionId, field, vBool(input.checked)));
    });
    row.appendChild(input);
    row.appendChild(elText("label", "", label));
    return row;
}

function controlStatic(label, value) {
    const row = el("div", "control-row");
    row.appendChild(elText("span", "control-name", label));
    row.appendChild(elText("span", "", value));
    return row;
}

function planeOfSketchPlane(p) {
    switch (p.tag) {
        case 1:
            return "XZ";
        case 2:
            return "YZ";
        default:
            return "XY";
    }
}

function controlFromSketchLoop(dispatch, doc, childId, selection, actionId) {
    let id;
    const row = el("div", "control-row");
    row.appendChild(elText("span", "control-name", "loop"));
    const select = document.createElement("select");
    select.className = "control-select";
    const currentLoopId = (selection.tag === 0) ? ((selection.fields[0] != null) ? ((id = selection.fields[0], id)) : "") : "";
    const loops = (childId == null) ? empty() : defaultArg(tryFind(childId, doc.SketchLoops), empty());
    select.appendChild(option("", "first (auto)", currentLoopId === ""));
    iterateIndexed((i, loop) => {
        let arg;
        select.appendChild(option(loop.Id, (arg = ((i + 1) | 0), toText(printf("loop %d"))(arg)), loop.Id === currentLoopId));
    }, loops);
    select.disabled = ((childId == null) ? true : isEmpty(loops));
    select.addEventListener("change", (_arg) => {
        let id_2;
        const loopId = (select.value === "") ? undefined : select.value;
        dispatch(Editor_setActionParamValue(actionId, new ActionParamField(35, []), new ParamValue(6, [ofList(ofArray([["case", new ParamValue(4, ["SelectionLoop"])], ["loopId", (loopId != null) ? ((loopId !== "") ? ((id_2 = loopId, new ParamValue(4, [id_2]))) : (new ParamValue(0, []))) : (new ParamValue(0, []))]]), {
            Compare: comparePrimitives,
        })])));
    });
    row.appendChild(select);
    return row;
}

function refOptsFor(doc, field) {
    const byId = ofList(map_1((a) => [a.Id, a], doc.Actions), {
        Compare: comparePrimitives,
    });
    return choose((id) => tryFind(id, byId), defaultArg(tryFind((field.tag === 6) ? "child" : ((field.tag === 10) ? "child" : ((field.tag === 18) ? "child" : ((field.tag === 36) ? "child" : ((field.tag === 38) ? "child" : ((field.tag === 40) ? "child" : ((field.tag === 33) ? "child" : ((field.tag === 19) ? "frame" : ((field.tag === 20) ? "a" : ((field.tag === 23) ? "a" : ((field.tag === 26) ? "a" : ((field.tag === 21) ? "b" : ((field.tag === 24) ? "b" : ((field.tag === 27) ? "b" : ((field.tag === 29) ? "origin" : "")))))))))))))), doc.RefOptions), empty()));
}

function renderKindControls(dispatch, doc, selected) {
    const strip = el("div", "controls-strip");
    const drag = (label, v, field) => controlDrag(dispatch, label, v, selected.Id, field);
    const ref = (label_1, current, field_1) => controlRef(dispatch, label_1, current, refOptsFor(doc, field_1), selected.Id, field_1);
    const select = (label_2, current_1, choices, field_2) => controlSelect(dispatch, label_2, current_1, choices, selected.Id, field_2);
    const check = (label_3, b, field_3) => controlCheck(dispatch, label_3, b, selected.Id, field_3);
    const append = (e) => {
        strip.appendChild(e);
    };
    const matchValue = selected.Kind;
    switch (matchValue.tag) {
        case 2: {
            append(drag("radius", matchValue.fields[0], new ActionParamField(2, [])));
            break;
        }
        case 1: {
            append(drag("radius", matchValue.fields[0], new ActionParamField(0, [])));
            append(drag("height", matchValue.fields[1], new ActionParamField(1, [])));
            break;
        }
        case 3: {
            append(drag("width", matchValue.fields[0], new ActionParamField(3, [])));
            append(drag("height", matchValue.fields[1], new ActionParamField(4, [])));
            append(drag("depth", matchValue.fields[2], new ActionParamField(5, [])));
            break;
        }
        case 4: {
            append(select("axis", matchValue.fields[0], ofArray(["X", "Y", "Z"]), new ActionParamField(15, [])));
            append(drag("offset", matchValue.fields[1], new ActionParamField(16, [])));
            append(check("flip", matchValue.fields[2], new ActionParamField(17, [])));
            break;
        }
        case 5: {
            append(ref("child", matchValue.fields[0], new ActionParamField(6, [])));
            append(drag("x", matchValue.fields[1], new ActionParamField(7, [])));
            append(drag("y", matchValue.fields[2], new ActionParamField(8, [])));
            append(drag("z", matchValue.fields[3], new ActionParamField(9, [])));
            break;
        }
        case 6: {
            append(ref("child", matchValue.fields[0], new ActionParamField(10, [])));
            append(drag("ax", matchValue.fields[1], new ActionParamField(11, [])));
            append(drag("ay", matchValue.fields[2], new ActionParamField(12, [])));
            append(drag("az", matchValue.fields[3], new ActionParamField(13, [])));
            append(drag("angle", matchValue.fields[4], new ActionParamField(14, [])));
            break;
        }
        case 7: {
            append(ref("child", matchValue.fields[0], new ActionParamField(18, [])));
            append(ref("frame", matchValue.fields[1], new ActionParamField(19, [])));
            break;
        }
        case 8: {
            append(ref("tool", matchValue.fields[0], new ActionParamField(20, [])));
            append(ref("target", matchValue.fields[1], new ActionParamField(21, [])));
            append(drag("radius", matchValue.fields[2], new ActionParamField(22, [])));
            break;
        }
        case 9: {
            append(ref("tool", matchValue.fields[0], new ActionParamField(23, [])));
            append(ref("target", matchValue.fields[1], new ActionParamField(24, [])));
            append(drag("radius", matchValue.fields[2], new ActionParamField(25, [])));
            break;
        }
        case 10: {
            append(ref("tool", matchValue.fields[0], new ActionParamField(26, [])));
            append(ref("target", matchValue.fields[1], new ActionParamField(27, [])));
            append(drag("radius", matchValue.fields[2], new ActionParamField(28, [])));
            break;
        }
        case 11: {
            append(ref("origin", matchValue.fields[0], new ActionParamField(29, [])));
            append(select("plane", planeOfSketchPlane(matchValue.fields[1]), ofArray(["XY", "XZ", "YZ"]), new ActionParamField(30, [])));
            break;
        }
        case 12: {
            const child_3 = matchValue.fields[0];
            append(ref("sketch", child_3, new ActionParamField(33, [])));
            append(check("flip", matchValue.fields[1], new ActionParamField(34, [])));
            append(controlFromSketchLoop(dispatch, doc, child_3, matchValue.fields[2], selected.Id));
            break;
        }
        case 13: {
            append(ref("child", matchValue.fields[0], new ActionParamField(36, [])));
            append(drag("amount", matchValue.fields[1], new ActionParamField(37, [])));
            break;
        }
        case 14: {
            append(ref("child", matchValue.fields[0], new ActionParamField(38, [])));
            append(drag("thickness", matchValue.fields[1], new ActionParamField(39, [])));
            break;
        }
        case 15: {
            append(ref("child", matchValue.fields[0], new ActionParamField(40, [])));
            append(drag("size", matchValue.fields[1], new ActionParamField(41, [])));
            append(drag("res", matchValue.fields[2], new ActionParamField(42, [])));
            break;
        }
        default:
            append(controlStatic("frame", "world"));
    }
    return strip;
}

const roadrunnerPalette = ofArray([["#85AEC8", new Float64Array([133 / 255, 174 / 255, 200 / 255])], ["#341D7C", new Float64Array([52 / 255, 29 / 255, 124 / 255])], ["#F1BA23", new Float64Array([241 / 255, 186 / 255, 35 / 255])], ["#FFFFFF", new Float64Array([1, 1, 1])], ["#AC6614", new Float64Array([172 / 255, 102 / 255, 20 / 255])], ["#E4D6AF", new Float64Array([228 / 255, 214 / 255, 175 / 255])], ["#7D6400", new Float64Array([125 / 255, 100 / 255, 0 / 255])], ["#FFFFAA", new Float64Array([1, 1, 170 / 255])], ["#D10005", new Float64Array([209 / 255, 0 / 255, 5 / 255])]]);

function colorsMatch(a, b) {
    if ((((a.length === 3) && (b.length === 3)) && (Math.abs(item(0, a) - item(0, b)) < 0.01)) && (Math.abs(item(1, a) - item(1, b)) < 0.01)) {
        return Math.abs(item(2, a) - item(2, b)) < 0.01;
    }
    else {
        return false;
    }
}

function renderDisplaySection(dispatch, selected) {
    const matchValue = selected.Display;
    if (matchValue != null) {
        const d = matchValue;
        const nodeVisible = selected.Visible;
        const section = el("div", "display-section");
        const title = el("div", "section-title");
        title.appendChild(elText("span", "", "field display"));
        if (!nodeVisible) {
            const note = el("span", "field-disabled-note");
            note.appendChild(kbdHintTitled("v", "Press v to toggle"));
            note.appendChild(elText("span", "", "to enable"));
            title.appendChild(note);
        }
        section.appendChild(title);
        const controls = el("div", "display-controls");
        if (!nodeVisible) {
            controls.classList.add("is-disabled");
        }
        const check = el("label", "display-check");
        const checkbox = document.createElement("input");
        checkbox.type = "checkbox";
        checkbox.checked = d.Enabled;
        checkbox.disabled = !nodeVisible;
        checkbox.addEventListener("change", (_arg) => {
            dispatch(new Message(9, [selected.Id]));
        });
        check.appendChild(checkbox);
        check.appendChild(kbdHintTitled("s", "Press s to toggle"));
        check.appendChild(elText("span", "", "Show field iso-surface"));
        controls.appendChild(check);
        if (d.Enabled) {
            const colorRow = el("div", "control-row color-row");
            colorRow.appendChild(elText("span", "control-name", "color"));
            const swatches = el("div", "color-swatches");
            const enumerator = getEnumerator(roadrunnerPalette);
            try {
                while (enumerator["System.Collections.IEnumerator.MoveNext"]()) {
                    const forLoopVar = enumerator["System.Collections.Generic.IEnumerator`1.get_Current"]();
                    const rgb = forLoopVar[1];
                    const swatch = el("button", "color-swatch");
                    swatch.style.background = forLoopVar[0];
                    if (colorsMatch(d.Color, rgb)) {
                        swatch.classList.add("is-active");
                    }
                    swatch.addEventListener("click", (_arg_1) => {
                        dispatch(Editor_setDisplayValue(selected.Id, new DisplayField(0, []), vColor(rgb)));
                    });
                    swatches.appendChild(swatch);
                }
            }
            finally {
                disposeSafe(enumerator);
            }
            colorRow.appendChild(swatches);
            controls.appendChild(colorRow);
            const offsetRow = el("div", "control-row");
            offsetRow.appendChild(elText("span", "control-name", "offset"));
            const offsetVal = elText("span", "control-value", toText(printf("%.1f"))(d.IsoValue));
            const isoPath = head(DocumentModule_pathOfDisplayField(new DisplayField(2, [])));
            offsetVal.dataset.slotActionId = selected.Id;
            offsetVal.dataset.slotPath = isoPath;
            const dispatchIsoValue = (nextValue) => {
                dispatch(Editor_setDisplayValue(selected.Id, new DisplayField(2, []), vFloat(nextValue)));
            };
            setupDraggable(offsetVal, d.IsoValue, dispatchIsoValue, dispatchIsoValue);
            offsetRow.appendChild(offsetVal);
            controls.appendChild(offsetRow);
        }
        const matchValue_1 = selected.FieldSlice;
        if (matchValue_1 != null) {
            const fs = matchValue_1;
            const sliceCheck = el("label", "display-check");
            const sliceCheckbox = document.createElement("input");
            sliceCheckbox.type = "checkbox";
            sliceCheckbox.checked = fs.Enabled;
            sliceCheckbox.disabled = !nodeVisible;
            sliceCheckbox.addEventListener("change", (_arg_2) => {
                dispatch(new Message(11, [selected.Id]));
            });
            sliceCheck.appendChild(sliceCheckbox);
            sliceCheck.appendChild(kbdHintTitled("f", "Press f to toggle"));
            sliceCheck.appendChild(elText("span", "", "Show field iso-lines"));
            controls.appendChild(sliceCheck);
            if (fs.Enabled) {
                const planeRow = el("div", "control-row");
                planeRow.appendChild(elText("span", "control-name", "plane"));
                const planeSelect = document.createElement("select");
                planeSelect.className = "control-select";
                const enumerator_1 = getEnumerator([["Z", "xy"], ["Y", "xz"], ["X", "yz"]]);
                try {
                    while (enumerator_1["System.Collections.IEnumerator.MoveNext"]()) {
                        const forLoopVar_1 = enumerator_1["System.Collections.Generic.IEnumerator`1.get_Current"]();
                        const value_21 = forLoopVar_1[0];
                        planeSelect.appendChild(option(value_21, forLoopVar_1[1], fs.Plane === value_21));
                    }
                }
                finally {
                    disposeSafe(enumerator_1);
                }
                planeSelect.addEventListener("change", (_arg_3) => {
                    dispatch(Editor_setFieldSliceValue(selected.Id, new FieldSliceField(0, []), vString(planeSelect.value)));
                });
                planeRow.appendChild(planeSelect);
                controls.appendChild(planeRow);
                const sOffsetRow = el("div", "control-row");
                sOffsetRow.appendChild(elText("span", "control-name", "offset"));
                const sOffsetVal = elText("span", "control-value", toText(printf("%.1f"))(fs.Offset));
                const offsetPath = head(DocumentModule_pathOfFieldSliceField(new FieldSliceField(1, [])));
                sOffsetVal.dataset.slotActionId = selected.Id;
                sOffsetVal.dataset.slotPath = offsetPath;
                const dispatchSliceOffset = (nextValue_1) => {
                    dispatch(Editor_setFieldSliceValue(selected.Id, new FieldSliceField(1, []), vFloat(nextValue_1)));
                };
                setupDraggable(sOffsetVal, fs.Offset, dispatchSliceOffset, dispatchSliceOffset);
                sOffsetRow.appendChild(sOffsetVal);
                controls.appendChild(sOffsetRow);
            }
        }
        section.appendChild(controls);
        return section;
    }
    else {
        return undefined;
    }
}

function renderSketchEditToggle(dispatch, doc, kind) {
    if (kind.tag === 11) {
        const editMode = doc.SketchUi.EditMode;
        const section = el("div", "sketch-edit-section");
        if (editMode) {
            section.classList.add("is-active");
        }
        const toggle = el("button", "sketch-edit-toggle");
        toggle.type = "button";
        if (editMode) {
            toggle.classList.add("is-active");
        }
        const label = editMode ? "Exit sketch edit" : "Edit sketch";
        toggle.appendChild(elText("span", "sketch-edit-label", label));
        toggle.appendChild(kbdHint("E"));
        toggle.addEventListener("click", (_arg) => {
            dispatch(new Message(31, []));
        });
        section.appendChild(toggle);
        return section;
    }
    else {
        return undefined;
    }
}

export function render(dispatch, doc) {
    const container = el("div", "selection-panel");
    const matchValue = bind((id) => tryFind_1((a) => (a.Id === id), doc.Actions), doc.SelectedId);
    if (matchValue != null) {
        const selected = matchValue;
        const header = el("div", "selection-header");
        const headerIcon = el("span", "action-icon");
        headerIcon.classList.add("large");
        headerIcon.appendChild(forKind(selected.Kind));
        header.appendChild(headerIcon);
        const headerInfo = el("div", "header-info");
        headerInfo.appendChild(elText("div", "header-kind", kindLabel(selected.Kind)));
        const name = defaultArg(selected.Name, kindLabel(selected.Kind));
        headerInfo.appendChild(elText("div", "header-name", name));
        header.appendChild(headerInfo);
        container.appendChild(header);
        const actionErrors = filter((e) => (e.ActionId === selected.Id), doc.Errors);
        if (!isEmpty(actionErrors)) {
            const errSection = el("div", "error-section");
            const enumerator = getEnumerator(actionErrors);
            try {
                while (enumerator["System.Collections.IEnumerator.MoveNext"]()) {
                    const err = enumerator["System.Collections.Generic.IEnumerator`1.get_Current"]();
                    const row = el("div", "error-row");
                    row.appendChild(elText("span", "error-key", err.Key));
                    row.appendChild(elText("span", "error-msg", err.Error));
                    errSection.appendChild(row);
                }
            }
            finally {
                disposeSafe(enumerator);
            }
            container.appendChild(errSection);
        }
        const section = el("div", "param-section");
        section.appendChild(elText("div", "controls-hint", "drag values to adjust:"));
        section.appendChild(renderKindControls(dispatch, doc, selected));
        container.appendChild(section);
        const matchValue_1 = renderSketchEditToggle(dispatch, doc, selected.Kind);
        if (matchValue_1 == null) {
        }
        else {
            const s = matchValue_1;
            container.appendChild(s);
        }
        const matchValue_2 = renderDisplaySection(dispatch, selected);
        if (matchValue_2 == null) {
        }
        else {
            const s_1 = matchValue_2;
            container.appendChild(s_1);
        }
    }
    else {
        container.appendChild(elText("div", "selection-empty", "Select an action"));
    }
    return container;
}

export function syncSlotValues(root, state) {
    let arg;
    const spans = root.querySelectorAll("[data-slot-action-id][data-slot-path]");
    for (let i = 0; i <= (spans.length - 1); i++) {
        const matchValue = spans.item(i);
        if (matchValue instanceof HTMLElement) {
            const elem = matchValue;
            const matchValue_1 = tryFind(new SlotRef(toString(elem.dataset.slotActionId), toString(elem.dataset.slotPath)), state.Compiled.Slots.Index);
            if (matchValue_1 == null) {
            }
            else {
                const slot = matchValue_1 | 0;
                elem.textContent = ((arg = item(slot, state.SlotValues), toText(printf("%.1f"))(arg)));
            }
        }
    }
}

