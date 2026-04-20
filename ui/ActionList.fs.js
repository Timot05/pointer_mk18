import { printf, toText } from "./fable_modules/fable-library-js.4.29.0/String.js";
import { Message, ActionTemplate } from "../core/Editor/Editor.fs.js";
import { ofSeq, toArray, exists, ofArray } from "./fable_modules/fable-library-js.4.29.0/List.js";
import { defaultOf, int32ToString, disposeSafe, getEnumerator, equals } from "./fable_modules/fable-library-js.4.29.0/Util.js";
import { kbdHintTitled, elText, el } from "./Dom.fs.js";
import { forTemplate, forKind } from "./Icons.fs.js";
import { defaultArg } from "./fable_modules/fable-library-js.4.29.0/Option.js";
import { map, setItem, item as item_1, fill } from "./fable_modules/fable-library-js.4.29.0/Array.js";

export function kindLabel(kind) {
    switch (kind.tag) {
        case 1:
            return "cylinder";
        case 2:
            return "sphere";
        case 3:
            return "box";
        case 4:
            return "halfplane";
        case 5:
            return "translate";
        case 6:
            return "rotate";
        case 7:
            return "move";
        case 8:
            return "union";
        case 9:
            return "subtract";
        case 10:
            return "intersect";
        case 11:
            return "sketch";
        case 12:
            return "fromsketch";
        case 13:
            return "thicken";
        case 14:
            return "shell";
        case 15:
            return "mesh";
        default:
            return "origin";
    }
}

function kindSubtitle(kind) {
    switch (kind.tag) {
        case 1:
            return toText(printf("r%g h%g"))(kind.fields[0])(kind.fields[1]);
        case 2:
            return toText(printf("r%g"))(kind.fields[0]);
        case 3:
            return toText(printf("%g×%g×%g"))(kind.fields[0])(kind.fields[1])(kind.fields[2]);
        case 4:
            return toText(printf("%s %g"))(kind.fields[0])(kind.fields[1]);
        case 5:
            return toText(printf("%g, %g, %g"))(kind.fields[1])(kind.fields[2])(kind.fields[3]);
        case 6:
            return toText(printf("%g"))(kind.fields[4]);
        case 13:
            return toText(printf("%g"))(kind.fields[1]);
        case 14:
            return toText(printf("%g"))(kind.fields[1]);
        case 15:
            return toText(printf("%g ×%d"))(kind.fields[1])(kind.fields[2]);
        default:
            return "";
    }
}

const templates = ofArray([[new ActionTemplate(0, []), "Sphere"], [new ActionTemplate(1, []), "Cylinder"], [new ActionTemplate(2, []), "Box"], [new ActionTemplate(3, []), "HalfPlane"], [new ActionTemplate(4, []), "Translate"], [new ActionTemplate(5, []), "Rotate"], [new ActionTemplate(6, []), "Move"], [new ActionTemplate(7, []), "Union"], [new ActionTemplate(8, []), "Subtract"], [new ActionTemplate(9, []), "Intersect"], [new ActionTemplate(10, []), "Sketch"], [new ActionTemplate(11, []), "FromSketch"], [new ActionTemplate(12, []), "Thicken"], [new ActionTemplate(13, []), "Shell"], [new ActionTemplate(14, []), "Mesh"]]);

function newActionId(label) {
    return (label.toLocaleLowerCase() + "_") + (Math.random().toString(36).slice(2, 8));
}

function isOrigin(kind) {
    if (kind.tag === 0) {
        return true;
    }
    else {
        return false;
    }
}

function renderRow(dispatch, doc, action) {
    const selected = equals(doc.SelectedId, action.Id);
    const hasError = exists((e) => (e.ActionId === action.Id), doc.Errors);
    const row = el("div", "action-row");
    row.dataset.actionId = action.Id;
    if (selected) {
        row.classList.add("is-selected");
    }
    if (isOrigin(action.Kind)) {
        row.classList.add("is-fixed");
    }
    if (hasError) {
        row.classList.add("has-error");
    }
    row.addEventListener("click", (_arg) => {
        dispatch(new Message(0, [action.Id]));
    });
    const main = el("div", "action-main");
    const icon = el("span", "action-icon");
    icon.appendChild(forKind(action.Kind));
    main.appendChild(icon);
    const info = el("div", "action-info");
    const title = defaultArg(action.Name, kindLabel(action.Kind));
    info.appendChild(elText("span", "action-title", title));
    const sub = kindSubtitle(action.Kind);
    if (sub !== "") {
        const subtitle = elText("span", "action-subtitle", sub);
        subtitle.dataset.actionId = action.Id;
        info.appendChild(subtitle);
    }
    main.appendChild(info);
    row.appendChild(main);
    if (!isOrigin(action.Kind)) {
        if (selected) {
            row.appendChild(kbdHintTitled("v", "Press v to toggle"));
        }
        const vis = el("button", "toggle-btn");
        vis.textContent = "●";
        if (action.Visible) {
            vis.classList.add("is-active");
        }
        vis.addEventListener("click", (e_1) => {
            e_1.stopPropagation();
            dispatch(new Message(8, [action.Id]));
        });
        row.appendChild(vis);
    }
    return row;
}

export function render(dispatch, doc) {
    const left = el("div", "panel");
    const header = el("div", "panel-header");
    header.appendChild(elText("h2", "", "Actions"));
    const paletteBtn = el("button", "palette-hint-btn");
    paletteBtn.appendChild(elText("kbd", "", "⌘"));
    paletteBtn.appendChild(elText("span", "palette-hint-plus", "+"));
    paletteBtn.appendChild(elText("kbd", "", "K"));
    paletteBtn.appendChild(document.createTextNode(" "));
    paletteBtn.appendChild(elText("span", "", "palette"));
    paletteBtn.addEventListener("click", (_arg) => {
        dispatch(new Message(37, []));
    });
    header.appendChild(paletteBtn);
    const addWrapper = el("div", "add-wrapper");
    const addBtn = elText("button", "btn-add", "+");
    const dropdown = el("div", "dropdown");
    dropdown.style.display = "none";
    const enumerator = getEnumerator(templates);
    try {
        while (enumerator["System.Collections.IEnumerator.MoveNext"]()) {
            const forLoopVar = enumerator["System.Collections.Generic.IEnumerator`1.get_Current"]();
            const template = forLoopVar[0];
            const label = forLoopVar[1];
            const item = el("button", "dropdown-item");
            item.appendChild(forTemplate(template));
            item.appendChild(elText("span", "", label));
            item.addEventListener("click", (_arg_1) => {
                dropdown.style.display = "none";
                dispatch(new Message(3, [template, newActionId(label)]));
            });
            dropdown.appendChild(item);
        }
    }
    finally {
        disposeSafe(enumerator);
    }
    addBtn.addEventListener("click", (e) => {
        e.stopPropagation();
        dropdown.style.display = ((dropdown.style.display === "none") ? "flex" : "none");
    });
    document.addEventListener("click", (_arg_2) => {
        dropdown.style.display = "none";
    });
    addWrapper.appendChild(addBtn);
    addWrapper.appendChild(dropdown);
    header.appendChild(addWrapper);
    left.appendChild(header);
    const list = el("div", "actions-list");
    const actions = toArray(doc.Actions);
    const rows = fill(new Array(actions.length), 0, actions.length, null);
    let dragIndex = undefined;
    let dropIndex = undefined;
    let dropBefore = false;
    const clearDropIndicators = () => {
        for (let idx = 0; idx <= (rows.length - 1); idx++) {
            const r = item_1(idx, rows);
            if (!(r == null)) {
                r.classList.remove("drop-before");
                r.classList.remove("drop-after");
            }
        }
    };
    for (let i = 0; i <= (actions.length - 1); i++) {
        const action = item_1(i, actions);
        const row = renderRow(dispatch, doc, action);
        setItem(rows, i, row);
        if (!isOrigin(action.Kind)) {
            row.draggable = true;
            row.addEventListener("dragstart", (e_1) => {
                const de = e_1;
                dragIndex = i;
                de.dataTransfer.effectAllowed = "move";
                de.dataTransfer.setData("text/plain", int32ToString(i));
                window.requestAnimationFrame((_arg_3) => {
                    row.classList.add("is-dragging");
                });
            });
        }
        row.addEventListener("dragover", (e_2) => {
            if (dragIndex != null) {
                if (dragIndex === i) {
                    const di_1 = dragIndex | 0;
                    dropIndex = undefined;
                    clearDropIndicators();
                }
                else {
                    e_2.preventDefault();
                    const de_1 = e_2;
                    de_1.dataTransfer.dropEffect = "move";
                    const rect = row.getBoundingClientRect();
                    const before = de_1.clientY < (rect.top + (rect.height / 2));
                    if (isOrigin(action.Kind) && before) {
                        dropIndex = undefined;
                        clearDropIndicators();
                    }
                    else {
                        dropBefore = before;
                        dropIndex = i;
                        clearDropIndicators();
                        row.classList.add(before ? "drop-before" : "drop-after");
                    }
                }
            }
        });
        list.appendChild(row);
    }
    list.addEventListener("dragover", (e_3) => {
        if (dragIndex != null) {
            const de_2 = e_3;
            const target = de_2.target;
            if (target.closest(".action-row") == null) {
                e_3.preventDefault();
                de_2.dataTransfer.dropEffect = "move";
                const last = (rows.length - 1) | 0;
                if (last >= 0) {
                    dropIndex = last;
                    dropBefore = false;
                    clearDropIndicators();
                    item_1(last, rows).classList.add("drop-after");
                }
            }
        }
    });
    list.addEventListener("drop", (e_4) => {
        e_4.preventDefault();
        clearDropIndicators();
        for (let idx_1 = 0; idx_1 <= (rows.length - 1); idx_1++) {
            const r_1 = item_1(idx_1, rows);
            if (!(r_1 == null)) {
                r_1.classList.remove("is-dragging");
            }
        }
        const dragIndex_1 = dragIndex;
        const dropIndex_1 = dropIndex;
        let matchResult, di_2, dri;
        if (dragIndex_1 != null) {
            if (dropIndex_1 != null) {
                matchResult = 0;
                di_2 = dragIndex_1;
                dri = dropIndex_1;
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
                let ids;
                const collection = map((a) => a.Id, actions);
                ids = Array.from(collection);
                const moved = ids[di_2];
                ids.splice(di_2, 1);
                let target_1 = dri + (dropBefore ? 0 : 1);
                if (di_2 < target_1) {
                    target_1 = ((target_1 - 1) | 0);
                }
                ids.splice(target_1, 0, moved);
                dragIndex = undefined;
                dropIndex = undefined;
                dispatch(new Message(7, [ofSeq(ids)]));
                break;
            }
            case 1: {
                dragIndex = undefined;
                dropIndex = undefined;
                break;
            }
        }
    });
    list.addEventListener("dragend", (_arg_4) => {
        dragIndex = undefined;
        dropIndex = undefined;
        clearDropIndicators();
        for (let idx_2 = 0; idx_2 <= (rows.length - 1); idx_2++) {
            const r_2 = item_1(idx_2, rows);
            if (!(r_2 == null)) {
                r_2.classList.remove("is-dragging");
            }
        }
    });
    left.appendChild(list);
    return left;
}

export function syncSubtitles(root, doc) {
    const enumerator = getEnumerator(doc.Actions);
    try {
        while (enumerator["System.Collections.IEnumerator.MoveNext"]()) {
            const action = enumerator["System.Collections.Generic.IEnumerator`1.get_Current"]();
            const matchValue = root.querySelector(`.action-row[data-action-id="${action.Id}"]`);
            if (equals(matchValue, defaultOf())) {
            }
            else {
                const row = matchValue;
                const subtitleText = kindSubtitle(action.Kind);
                const existing = row.querySelector(".action-subtitle");
                if (subtitleText === "") {
                    if (!(existing == null)) {
                        existing.remove();
                    }
                }
                else if (equals(existing, defaultOf())) {
                    const info = row.querySelector(".action-info");
                    if (!(info == null)) {
                        const subtitle = elText("span", "action-subtitle", subtitleText);
                        subtitle.dataset.actionId = action.Id;
                        info.appendChild(subtitle);
                    }
                }
                else {
                    existing.textContent = subtitleText;
                }
            }
        }
    }
    finally {
        disposeSafe(enumerator);
    }
}

