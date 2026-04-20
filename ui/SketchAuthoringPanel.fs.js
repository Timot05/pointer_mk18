import { Editor_constraintPlacementName, Editor_geometricConstraintName, ConstraintPlacementKind, GeometricConstraintKind, ConstraintPlacementKind_$reflection, GeometricConstraintKind_$reflection, Message, Editor_sketchToolName, SketchToolKind } from "../core/Editor/Editor.fs.js";
import { tryFind as tryFind_1, isEmpty, mapIndexed, filter, ofArray } from "./fable_modules/fable-library-js.4.29.0/List.js";
import { elText, el } from "./Dom.fs.js";
import { equals, disposeSafe, getEnumerator } from "./fable_modules/fable-library-js.4.29.0/Util.js";
import { Record } from "./fable_modules/fable-library-js.4.29.0/Types.js";
import { record_type, string_type } from "./fable_modules/fable-library-js.4.29.0/Reflection.js";
import { printf, toText } from "./fable_modules/fable-library-js.4.29.0/String.js";
import { bind, defaultArg } from "./fable_modules/fable-library-js.4.29.0/Option.js";
import { tryFind } from "./fable_modules/fable-library-js.4.29.0/Map.js";

const tools = ofArray([[new SketchToolKind(0, []), "select", undefined], [new SketchToolKind(1, []), "line", "L"], [new SketchToolKind(2, []), "rect", "G"], [new SketchToolKind(3, []), "rrect", "⇧G"], [new SketchToolKind(4, []), "circle", "C"], [new SketchToolKind(5, []), "arc", "U"]]);

function renderToolbar(dispatch, currentTool) {
    const toolbar = el("div", "sketch-toolbar");
    const enumerator = getEnumerator(tools);
    try {
        while (enumerator["System.Collections.IEnumerator.MoveNext"]()) {
            const forLoopVar = enumerator["System.Collections.Generic.IEnumerator`1.get_Current"]();
            const tool = forLoopVar[0];
            const hint = forLoopVar[2];
            const button = el("button", "sketch-tool-btn");
            button.type = "button";
            if (currentTool === Editor_sketchToolName(tool)) {
                button.classList.add("is-active");
            }
            button.appendChild(elText("span", "", forLoopVar[1]));
            if (hint == null) {
            }
            else {
                const h = hint;
                button.appendChild(elText("kbd", "tool-hint", h));
            }
            button.addEventListener("click", (_arg) => {
                dispatch(new Message(32, [tool]));
            });
            toolbar.appendChild(button);
        }
    }
    finally {
        disposeSafe(enumerator);
    }
    if (currentTool !== "none") {
        toolbar.appendChild(elText("span", "sketch-toolbar-hint", "click in the viewer to place geometry"));
    }
    return toolbar;
}

class GeomButton extends Record {
    constructor(Kind, Label, Symbol$, Shortcut) {
        super();
        this.Kind = Kind;
        this.Label = Label;
        this.Symbol = Symbol$;
        this.Shortcut = Shortcut;
    }
}

function GeomButton_$reflection() {
    return record_type("PointerMk18.Ui.SketchAuthoringPanel.GeomButton", [], GeomButton, () => [["Kind", GeometricConstraintKind_$reflection()], ["Label", string_type], ["Symbol", string_type], ["Shortcut", string_type]]);
}

class DimButton extends Record {
    constructor(Kind, Label, Symbol$, Shortcut) {
        super();
        this.Kind = Kind;
        this.Label = Label;
        this.Symbol = Symbol$;
        this.Shortcut = Shortcut;
    }
}

function DimButton_$reflection() {
    return record_type("PointerMk18.Ui.SketchAuthoringPanel.DimButton", [], DimButton, () => [["Kind", ConstraintPlacementKind_$reflection()], ["Label", string_type], ["Symbol", string_type], ["Shortcut", string_type]]);
}

const geometricButtons = ofArray([new GeomButton(new GeometricConstraintKind(0, []), "Coincident", "≡", "I"), new GeomButton(new GeometricConstraintKind(1, []), "Horizontal", "↔", "H"), new GeomButton(new GeometricConstraintKind(2, []), "Vertical", "↕", "V"), new GeomButton(new GeometricConstraintKind(3, []), "Midpoint", "·|·", "⇧M"), new GeomButton(new GeometricConstraintKind(4, []), "Parallel", "∥", "B"), new GeomButton(new GeometricConstraintKind(5, []), "Perpendicular", "⊥", "⇧L"), new GeomButton(new GeometricConstraintKind(6, []), "Equal", "=", "E"), new GeomButton(new GeometricConstraintKind(7, []), "Tangent", "⌒", "T"), new GeomButton(new GeometricConstraintKind(8, []), "Concentric", "◎", "⇧O"), new GeomButton(new GeometricConstraintKind(9, []), "Fixed", "⊙", "⇧J")]);

const dimensionButtons = ofArray([new DimButton(new ConstraintPlacementKind(0, []), "Distance", "↦", "D"), new DimButton(new ConstraintPlacementKind(1, []), "Angle", "∠", "A")]);

function constraintSymbol(c) {
    switch (c.tag) {
        case 1:
        case 2:
            return "≡";
        case 4:
            return "↔";
        case 5:
            return "↕";
        case 11:
        case 12:
            return "∥";
        case 13:
        case 14:
            return "⊥";
        case 10:
            return "·|·";
        case 15:
        case 16:
            return "⌒";
        case 3:
            return "◎";
        case 8:
        case 9:
            return "=";
        case 24:
            return "∠";
        case 17:
            return "⌀";
        case 6:
        case 7:
        case 18:
        case 19:
        case 20:
        case 21:
        case 22:
        case 23:
            return "↦";
        default:
            return "⊙";
    }
}

function constraintLabel(c) {
    switch (c.tag) {
        case 1:
            return "Coincident";
        case 2:
            return "FrameCoincident";
        case 4:
            return "Horizontal";
        case 5:
            return "Vertical";
        case 6:
            return "Distance";
        case 7:
            return "FrameDistance";
        case 8:
        case 9:
            return "Equal";
        case 10:
            return "Midpoint";
        case 11:
            return "Parallel";
        case 12:
            return "FrameParallel";
        case 13:
            return "Perpendicular";
        case 14:
            return "FramePerpendicular";
        case 15:
        case 16:
            return "Tangent";
        case 17:
            return "CircleDiameter";
        case 18:
            return "LineDistance";
        case 19:
            return "FrameLineDistance";
        case 20:
            return "PointLineDistance";
        case 21:
            return "PointCircleDistance";
        case 22:
            return "LineCircleDistance";
        case 23:
            return "CircleCircleDistance";
        case 24:
            return "Angle";
        case 3:
            return "Concentric";
        default:
            return "Fixed";
    }
}

function constraintSummary(c) {
    let matchResult, a, b, lineA, point_1, lineA_1, lineB, a_2, b_2;
    switch (c.tag) {
        case 0: {
            matchResult = 0;
            break;
        }
        case 1: {
            matchResult = 1;
            a = c.fields[0];
            b = c.fields[1];
            break;
        }
        case 4: {
            matchResult = 1;
            a = c.fields[0];
            b = c.fields[1];
            break;
        }
        case 5: {
            matchResult = 1;
            a = c.fields[0];
            b = c.fields[1];
            break;
        }
        case 6: {
            matchResult = 2;
            break;
        }
        case 10: {
            matchResult = 3;
            lineA = c.fields[1];
            point_1 = c.fields[0];
            break;
        }
        case 20: {
            matchResult = 3;
            lineA = c.fields[1];
            point_1 = c.fields[0];
            break;
        }
        case 11: {
            matchResult = 4;
            lineA_1 = c.fields[4];
            lineB = c.fields[5];
            break;
        }
        case 13: {
            matchResult = 4;
            lineA_1 = c.fields[4];
            lineB = c.fields[5];
            break;
        }
        case 8: {
            matchResult = 4;
            lineA_1 = c.fields[4];
            lineB = c.fields[5];
            break;
        }
        case 18: {
            matchResult = 4;
            lineA_1 = c.fields[4];
            lineB = c.fields[5];
            break;
        }
        case 24: {
            matchResult = 4;
            lineA_1 = c.fields[4];
            lineB = c.fields[5];
            break;
        }
        case 15: {
            matchResult = 5;
            break;
        }
        case 3: {
            matchResult = 6;
            a_2 = c.fields[0];
            b_2 = c.fields[1];
            break;
        }
        case 9: {
            matchResult = 6;
            a_2 = c.fields[0];
            b_2 = c.fields[1];
            break;
        }
        case 17: {
            matchResult = 7;
            break;
        }
        case 21: {
            matchResult = 8;
            break;
        }
        case 22: {
            matchResult = 9;
            break;
        }
        case 23: {
            matchResult = 10;
            break;
        }
        default:
            matchResult = 11;
    }
    switch (matchResult) {
        case 0:
            return c.fields[0];
        case 1:
            return toText(printf("%s · %s"))(a)(b);
        case 2:
            return toText(printf("%s · %s"))(c.fields[0])(c.fields[1]);
        case 3:
            return toText(printf("%s · %s"))(point_1)(lineA);
        case 4:
            return toText(printf("%s · %s"))(lineA_1)(lineB);
        case 5:
            return toText(printf("%s · %s"))(c.fields[4])(c.fields[3]);
        case 6:
            return toText(printf("%s · %s"))(a_2)(b_2);
        case 7:
            return c.fields[0];
        case 8:
            return toText(printf("%s · %s"))(c.fields[0])(c.fields[1]);
        case 9:
            return toText(printf("%s · %s"))(c.fields[0])(c.fields[3]);
        case 10:
            return toText(printf("%s · %s"))(c.fields[0])(c.fields[2]);
        default:
            return "";
    }
}

function isDimensionConstraint(c) {
    switch (c.tag) {
        case 6:
        case 7:
        case 18:
        case 19:
        case 20:
        case 21:
        case 22:
        case 23:
        case 24:
        case 17:
            return true;
        default:
            return false;
    }
}

function constraintValueText(c) {
    let matchResult, distance;
    switch (c.tag) {
        case 6: {
            matchResult = 0;
            distance = c.fields[2];
            break;
        }
        case 7: {
            matchResult = 0;
            distance = c.fields[3];
            break;
        }
        case 18: {
            matchResult = 0;
            distance = c.fields[6];
            break;
        }
        case 19: {
            matchResult = 0;
            distance = c.fields[5];
            break;
        }
        case 20: {
            matchResult = 0;
            distance = c.fields[4];
            break;
        }
        case 21: {
            matchResult = 0;
            distance = c.fields[3];
            break;
        }
        case 22: {
            matchResult = 0;
            distance = c.fields[5];
            break;
        }
        case 23: {
            matchResult = 0;
            distance = c.fields[4];
            break;
        }
        case 17: {
            matchResult = 1;
            break;
        }
        case 24: {
            matchResult = 2;
            break;
        }
        default:
            matchResult = 3;
    }
    switch (matchResult) {
        case 0:
            return toText(printf("%.2f"))(distance);
        case 1:
            return toText(printf("%.2f"))(c.fields[2]);
        case 2:
            return toText(printf("%.2f"))(c.fields[6]);
        default:
            return undefined;
    }
}

function renderExistingConstraints(dispatch, sketch, isDimensionSection, section) {
    const items = filter((tupledArg) => (isDimensionConstraint(tupledArg[0]) === isDimensionSection), mapIndexed((i, c) => [c, i], sketch.Constraints));
    if (isEmpty(items)) {
        const msg = isDimensionSection ? "Use the viewer to place a dimension label." : "Select entities in the viewer to enable constraints.";
        section.appendChild(elText("div", "constraint-empty", msg));
    }
    else {
        const list_2 = el("div", "constraint-list");
        const enumerator = getEnumerator(items);
        try {
            while (enumerator["System.Collections.IEnumerator.MoveNext"]()) {
                const forLoopVar = enumerator["System.Collections.Generic.IEnumerator`1.get_Current"]();
                const c_2 = forLoopVar[0];
                const row = el("div", "constraint-row");
                row.appendChild(elText("span", "sym", constraintSymbol(c_2)));
                row.appendChild(elText("span", "constraint-kind", constraintLabel(c_2)));
                row.appendChild(elText("span", "constraint-summary", constraintSummary(c_2)));
                const matchValue = constraintValueText(c_2);
                if (matchValue == null) {
                }
                else {
                    const value_4 = matchValue;
                    row.appendChild(elText("span", "constraint-value", value_4));
                }
                const del = elText("button", "constraint-delete", "×");
                del.type = "button";
                del.addEventListener("click", (_arg_1) => {
                    dispatch(new Message(35, [forLoopVar[1]]));
                });
                row.appendChild(del);
                list_2.appendChild(row);
            }
        }
        finally {
            disposeSafe(enumerator);
        }
        section.appendChild(list_2);
    }
}

function renderGeometricSection(dispatch, doc, sketch) {
    const section = el("div", "constraint-section");
    const header = el("div", "constraint-section-header");
    header.appendChild(elText("span", "constraint-section-title", "Constraints"));
    section.appendChild(header);
    const row = el("div", "constraint-add-row");
    const enumerator = getEnumerator(geometricButtons);
    try {
        while (enumerator["System.Collections.IEnumerator.MoveNext"]()) {
            const b = enumerator["System.Collections.Generic.IEnumerator`1.get_Current"]();
            const button = el("button", "constraint-add-btn");
            button.type = "button";
            const available = defaultArg(tryFind(Editor_geometricConstraintName(b.Kind), doc.SketchUi.ConstraintAvailability), false);
            button.disabled = !available;
            button.appendChild(elText("span", "sym", b.Symbol));
            button.appendChild(elText("span", "btn-label", b.Label));
            button.appendChild(elText("kbd", "shortcut", b.Shortcut));
            button.addEventListener("click", (_arg) => {
                dispatch(new Message(34, [b.Kind]));
            });
            row.appendChild(button);
        }
    }
    finally {
        disposeSafe(enumerator);
    }
    section.appendChild(row);
    renderExistingConstraints(dispatch, sketch, false, section);
    return section;
}

function renderDimensionSection(dispatch, doc, sketch) {
    const section = el("div", "constraint-section");
    const header = el("div", "constraint-section-header");
    header.appendChild(elText("span", "constraint-section-title", "Dimensions"));
    section.appendChild(header);
    const row = el("div", "constraint-add-row");
    const enumerator = getEnumerator(dimensionButtons);
    try {
        while (enumerator["System.Collections.IEnumerator.MoveNext"]()) {
            const b = enumerator["System.Collections.Generic.IEnumerator`1.get_Current"]();
            const button = el("button", "constraint-add-btn");
            button.type = "button";
            const key = Editor_constraintPlacementName(b.Kind);
            const available = defaultArg(tryFind(key, doc.SketchUi.DimensionPlacementAvailability), false);
            button.disabled = !available;
            if (equals(doc.SketchUi.ConstraintPlacementMode, key)) {
                button.classList.add("is-active");
            }
            button.appendChild(elText("span", "sym", b.Symbol));
            button.appendChild(elText("span", "btn-label", b.Label));
            button.appendChild(elText("kbd", "shortcut", b.Shortcut));
            button.addEventListener("click", (_arg) => {
                dispatch(new Message(33, [b.Kind]));
            });
            row.appendChild(button);
        }
    }
    finally {
        disposeSafe(enumerator);
    }
    section.appendChild(row);
    renderExistingConstraints(dispatch, sketch, true, section);
    return section;
}

export function render(dispatch, doc) {
    if (!doc.SketchUi.EditMode) {
        return undefined;
    }
    else {
        const matchValue = bind((id) => tryFind_1((a) => (a.Id === id), doc.Actions), doc.SelectedId);
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
                const overlay = el("div", "sketch-authoring-overlay");
                overlay.appendChild(renderToolbar(dispatch, doc.SketchUi.Tool));
                const panel = el("div", "constraints-panel");
                panel.appendChild(renderGeometricSection(dispatch, doc, sketch));
                panel.appendChild(renderDimensionSection(dispatch, doc, sketch));
                overlay.appendChild(panel);
                return overlay;
            }
            default:
                return undefined;
        }
    }
}

