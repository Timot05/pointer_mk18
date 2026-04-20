import { item as item_1, iterateIndexed, length, isEmpty, empty } from "./fable_modules/fable-library-js.4.29.0/List.js";
import { printf, toText } from "./fable_modules/fable-library-js.4.29.0/String.js";
import { setupDraggable, elText, el } from "./Dom.fs.js";
import { max } from "./fable_modules/fable-library-js.4.29.0/Double.js";
import { forKindName } from "./Icons.fs.js";
import { Message } from "../core/Editor/Editor.fs.js";
import { equals, disposeSafe, getEnumerator } from "./fable_modules/fable-library-js.4.29.0/Util.js";

let backdrop = undefined;

let activeIndex = 0;

let currentItems = empty();

let cleanupKeydown = undefined;

let lastStructureSignature = undefined;

let lastItemsSignature = undefined;

let lastScalarSignature = undefined;

function unmount() {
    if (cleanupKeydown == null) {
    }
    else {
        cleanupKeydown();
        cleanupKeydown = undefined;
    }
    if (backdrop == null) {
    }
    else {
        const bd = backdrop;
        bd.remove();
        backdrop = undefined;
    }
    lastStructureSignature = undefined;
    lastItemsSignature = undefined;
    lastScalarSignature = undefined;
}

function structureSignature(state) {
    return toText(printf("%b|%s|%A|%A|%s|%A"))(state.IsOpen)(state.Mode)(state.PickedKind)(state.Chips)(state.Prompt)(state.HintBar);
}

function itemsSignature(items) {
    return toText(printf("%A"))(items);
}

function scalarSignature(fields) {
    return toText(printf("%A"))(fields);
}

function buildResultsList(dispatch, getDocActionCount, items, sync_1) {
    const results = el("div", "palette-results");
    if (isEmpty(items)) {
        results.appendChild(elText("div", "palette-empty", "No matches"));
    }
    else {
        if (activeIndex >= length(items)) {
            activeIndex = (max(0, length(items) - 1) | 0);
        }
        iterateIndexed((i, item) => {
            const btn = el("button", "palette-item");
            if (i === activeIndex) {
                btn.classList.add("is-active");
            }
            btn.appendChild(forKindName(item.Kind));
            btn.appendChild(elText("span", "palette-label", item.Label));
            btn.addEventListener("mouseenter", (_arg) => {
                activeIndex = (i | 0);
                const nodes = results.querySelectorAll(".palette-item");
                for (let j = 0; j <= (nodes.length - 1); j++) {
                    const n = nodes[j];
                    if (j === i) {
                        n.classList.add("is-active");
                    }
                    else {
                        n.classList.remove("is-active");
                    }
                }
            });
            btn.addEventListener("click", (_arg_1) => {
                const before = getDocActionCount() | 0;
                dispatch(new Message(39, [item.Id]));
                sync_1(getDocActionCount() !== before);
            });
            results.appendChild(btn);
        }, items);
    }
    return results;
}

function patchResults(dispatch, getDocActionCount, items, sync_1) {
    if (backdrop != null) {
        const bd = backdrop;
        const old = bd.querySelector(".palette-results");
        if (!(old == null)) {
            activeIndex = 0;
            currentItems = items;
            const fresh = buildResultsList(dispatch, getDocActionCount, items, sync_1);
            old.parentNode.replaceChild(fresh, old);
            lastItemsSignature = itemsSignature(items);
        }
    }
}

function patchScalarValues(fields) {
    if (backdrop != null) {
        const bd = backdrop;
        const enumerator = getEnumerator(fields);
        try {
            while (enumerator["System.Collections.IEnumerator.MoveNext"]()) {
                const field = enumerator["System.Collections.Generic.IEnumerator`1.get_Current"]();
                const matchValue = bd.querySelector(`.control-value[data-field-key="${field.Key}"]`);
                if (matchValue instanceof HTMLElement) {
                    const elem = matchValue;
                    elem.textContent = toText(printf("%.1f"))(field.Value);
                }
            }
        }
        finally {
            disposeSafe(enumerator);
        }
        lastScalarSignature = scalarSignature(fields);
    }
}

function mount(dispatch, getPaletteState, getDocActionCount, sync_1, state) {
    if (!state.IsOpen) {
    }
    else {
        const bd = el("div", "palette-backdrop");
        const palette = el("div", "palette");
        palette.setAttribute("role", "dialog");
        const row = el("div", "palette-row");
        const matchValue = state.PickedKind;
        if (matchValue == null) {
        }
        else {
            const kind = matchValue;
            const cmdChip = el("span", "chip chip-command");
            cmdChip.appendChild(forKindName(kind));
            cmdChip.appendChild(elText("span", "", kind));
            row.appendChild(cmdChip);
        }
        const enumerator = getEnumerator(state.Chips);
        try {
            while (enumerator["System.Collections.IEnumerator.MoveNext"]()) {
                const chip = enumerator["System.Collections.Generic.IEnumerator`1.get_Current"]();
                const c = el("span", "chip");
                c.appendChild(elText("span", "chip-label", chip.Label + ":"));
                c.appendChild(elText("span", "chip-value", chip.Value));
                row.appendChild(c);
            }
        }
        finally {
            disposeSafe(enumerator);
        }
        let inputOpt = undefined;
        if ((state.Mode === "command") ? true : (state.Mode === "ref")) {
            const input = document.createElement("input");
            input.type = "text";
            input.className = "palette-input";
            input.placeholder = state.Prompt;
            input.spellcheck = false;
            input.autocomplete = "off";
            row.appendChild(input);
            inputOpt = input;
        }
        else if (((state.Mode === "scalars") && isEmpty(state.Chips)) && (state.PickedKind != null)) {
            row.appendChild(elText("span", "prompt-label", "set values:"));
        }
        palette.appendChild(row);
        if ((state.Mode === "scalars") && !isEmpty(state.ScalarFields)) {
            const valRow = el("div", "value-row");
            const enumerator_1 = getEnumerator(state.ScalarFields);
            try {
                while (enumerator_1["System.Collections.IEnumerator.MoveNext"]()) {
                    const field = enumerator_1["System.Collections.Generic.IEnumerator`1.get_Current"]();
                    const cell = el("div", "value-cell");
                    cell.appendChild(elText("span", "value-axis", field.Label));
                    const valSpan = elText("span", "control-value", toText(printf("%.1f"))(field.Value));
                    valSpan.dataset.fieldKey = field.Key;
                    setupDraggable(valSpan, field.Value, (v) => {
                        dispatch(new Message(40, [field.Key, v]));
                    }, (v_1) => {
                        dispatch(new Message(40, [field.Key, v_1]));
                    });
                    cell.appendChild(valSpan);
                    valRow.appendChild(cell);
                }
            }
            finally {
                disposeSafe(enumerator_1);
            }
            palette.appendChild(valRow);
        }
        if ((state.Mode === "command") ? true : (state.Mode === "ref")) {
            currentItems = state.Items;
            palette.appendChild(buildResultsList(dispatch, getDocActionCount, state.Items, sync_1));
        }
        const hintBar = el("div", "palette-hint");
        const enumerator_2 = getEnumerator(state.HintBar);
        try {
            while (enumerator_2["System.Collections.IEnumerator.MoveNext"]()) {
                const h = enumerator_2["System.Collections.Generic.IEnumerator`1.get_Current"]();
                hintBar.appendChild(elText("span", "", h));
            }
        }
        finally {
            disposeSafe(enumerator_2);
        }
        palette.appendChild(hintBar);
        bd.appendChild(palette);
        document.body.appendChild(bd);
        backdrop = bd;
        lastStructureSignature = structureSignature(state);
        lastItemsSignature = itemsSignature(state.Items);
        lastScalarSignature = scalarSignature(state.ScalarFields);
        if (inputOpt != null) {
            const input_1 = inputOpt;
            let debounceId = undefined;
            input_1.addEventListener("input", (_arg) => {
                if (debounceId == null) {
                }
                else {
                    const id = debounceId;
                    window.clearTimeout(id);
                }
                debounceId = window.setTimeout((_arg_1) => {
                    dispatch(new Message(38, [input_1.value]));
                    patchResults(dispatch, getDocActionCount, getPaletteState().Items, sync_1);
                }, 80);
            });
            input_1.addEventListener("keydown", (e) => {
                const ke = e;
                const inPalette = palette.querySelectorAll(".palette-item");
                const matchValue_1 = ke.key;
                let matchResult;
                switch (matchValue_1) {
                    case "Escape": {
                        matchResult = 0;
                        break;
                    }
                    case "Backspace": {
                        if ((input_1.value === "") && (state.Mode !== "command")) {
                            matchResult = 1;
                        }
                        else {
                            matchResult = 5;
                        }
                        break;
                    }
                    case "ArrowDown": {
                        matchResult = 2;
                        break;
                    }
                    case "ArrowUp": {
                        matchResult = 3;
                        break;
                    }
                    case "Enter": {
                        matchResult = 4;
                        break;
                    }
                    default:
                        matchResult = 5;
                }
                switch (matchResult) {
                    case 0: {
                        e.preventDefault();
                        e.stopPropagation();
                        dispatch(new Message(44, []));
                        break;
                    }
                    case 1: {
                        e.preventDefault();
                        e.stopPropagation();
                        dispatch(new Message(43, []));
                        break;
                    }
                    case 2: {
                        e.preventDefault();
                        e.stopPropagation();
                        if (!isEmpty(state.Items)) {
                            activeIndex = (((activeIndex + 1) % length(state.Items)) | 0);
                        }
                        for (let j = 0; j <= (inPalette.length - 1); j++) {
                            const n = inPalette[j];
                            if (j === activeIndex) {
                                n.classList.add("is-active");
                            }
                            else {
                                n.classList.remove("is-active");
                            }
                        }
                        break;
                    }
                    case 3: {
                        e.preventDefault();
                        e.stopPropagation();
                        if (!isEmpty(state.Items)) {
                            activeIndex = ((((activeIndex - 1) + length(state.Items)) % length(state.Items)) | 0);
                        }
                        for (let j_1 = 0; j_1 <= (inPalette.length - 1); j_1++) {
                            const n_1 = inPalette[j_1];
                            if (j_1 === activeIndex) {
                                n_1.classList.add("is-active");
                            }
                            else {
                                n_1.classList.remove("is-active");
                            }
                        }
                        break;
                    }
                    case 4: {
                        e.preventDefault();
                        e.stopPropagation();
                        if (ke.metaKey ? true : ke.ctrlKey) {
                            dispatch(new Message(42, [Math.random().toString(36).slice(2, 8)]));
                        }
                        else if (activeIndex < length(currentItems)) {
                            const item = item_1(activeIndex, currentItems);
                            const before = getDocActionCount() | 0;
                            dispatch(new Message(39, [item.Id]));
                            sync_1(getDocActionCount() !== before);
                        }
                        break;
                    }
                    case 5: {
                        break;
                    }
                }
            });
            window.requestAnimationFrame((_arg_2) => {
                input_1.focus();
            });
        }
        if (state.Mode === "scalars") {
            const handler = (e_1) => {
                const ke_1 = e_1;
                const matchValue_2 = ke_1.key;
                switch (matchValue_2) {
                    case "Escape": {
                        e_1.preventDefault();
                        dispatch(new Message(44, []));
                        break;
                    }
                    case "Backspace": {
                        e_1.preventDefault();
                        dispatch(new Message(43, []));
                        break;
                    }
                    case "Enter": {
                        e_1.preventDefault();
                        if (ke_1.metaKey ? true : ke_1.ctrlKey) {
                            dispatch(new Message(42, [Math.random().toString(36).slice(2, 8)]));
                        }
                        else {
                            dispatch(new Message(41, []));
                        }
                        break;
                    }
                    default:
                        undefined;
                }
            };
            document.addEventListener("keydown", handler);
            cleanupKeydown = (() => {
                document.removeEventListener("keydown", handler);
            });
        }
        bd.addEventListener("click", (e_2) => {
            if (equals(e_2.target, bd)) {
                dispatch(new Message(44, []));
            }
        });
    }
}

/**
 * Entry point called from Program.fs after every dispatch. Reads the
 * current palette state and mounts/unmounts accordingly.
 */
export function sync(dispatch, getPaletteState, getDocActionCount) {
    const syncImpl = (_afterModelChange) => {
        let input, input_1, bd;
        const state = getPaletteState();
        if (!state.IsOpen) {
            unmount();
        }
        else {
            const nextStructure = structureSignature(state);
            const backdrop_1 = backdrop;
            const lastStructureSignature_1 = lastStructureSignature;
            let matchResult, bd_1, previousStructure_1, bd_2;
            if (backdrop_1 != null) {
                if (lastStructureSignature_1 != null) {
                    if ((bd = backdrop_1, lastStructureSignature_1 !== nextStructure)) {
                        matchResult = 1;
                        bd_1 = backdrop_1;
                        previousStructure_1 = lastStructureSignature_1;
                    }
                    else {
                        matchResult = 2;
                        bd_2 = backdrop_1;
                    }
                }
                else {
                    matchResult = 0;
                }
            }
            else {
                matchResult = 0;
            }
            switch (matchResult) {
                case 0: {
                    activeIndex = 0;
                    mount(dispatch, getPaletteState, getDocActionCount, syncImpl, state);
                    break;
                }
                case 1: {
                    let preservedValue;
                    const matchValue_1 = bd_1.querySelector(".palette-input");
                    preservedValue = ((matchValue_1 instanceof HTMLInputElement) ? ((input = matchValue_1, input.value)) : undefined);
                    let preservedSelection;
                    const matchValue_2 = bd_1.querySelector(".palette-input");
                    preservedSelection = ((matchValue_2 instanceof HTMLInputElement) ? ((input_1 = matchValue_2, [input_1.selectionStart, input_1.selectionEnd])) : undefined);
                    activeIndex = 0;
                    unmount();
                    mount(dispatch, getPaletteState, getDocActionCount, syncImpl, state);
                    const backdrop_2 = backdrop;
                    let matchResult_1, nextBd, value;
                    if (preservedValue != null) {
                        if (backdrop_2 != null) {
                            matchResult_1 = 0;
                            nextBd = backdrop_2;
                            value = preservedValue;
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
                            const matchValue_4 = nextBd.querySelector(".palette-input");
                            if (matchValue_4 instanceof HTMLInputElement) {
                                const nextInput = matchValue_4;
                                nextInput.value = value;
                                if (preservedSelection != null) {
                                    const startPos = preservedSelection[0] | 0;
                                    const endPos = preservedSelection[1] | 0;
                                    nextInput.setSelectionRange(startPos, endPos);
                                }
                            }
                            break;
                        }
                        case 1: {
                            break;
                        }
                    }
                    break;
                }
                case 2: {
                    if ((state.Mode === "command") ? true : (state.Mode === "ref")) {
                        if (!equals(lastItemsSignature, itemsSignature(state.Items))) {
                            patchResults(dispatch, getDocActionCount, state.Items, syncImpl);
                        }
                    }
                    else if (state.Mode === "scalars") {
                        if (!equals(lastScalarSignature, scalarSignature(state.ScalarFields))) {
                            patchScalarValues(state.ScalarFields);
                        }
                    }
                    break;
                }
            }
        }
    };
    syncImpl(false);
}

