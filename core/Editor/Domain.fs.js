import { Record, Union } from "../../ui/fable_modules/fable-library-js.4.29.0/Types.js";
import { class_type, list_type, record_type, array_type, union_type, int32_type, option_type, bool_type, string_type, float64_type } from "../../ui/fable_modules/fable-library-js.4.29.0/Reflection.js";
import { SketchPlane, FromSketchSelection, ActionSketch, ArcData, FreePoint, RenderEntity, SketchConstraint, LabelPos, FromSketchSelection_$reflection, ActionSketch_$reflection, SketchPlane_$reflection } from "../Sketch/Sketch.fs.js";
import { comparePrimitives, equals, round } from "../../ui/fable_modules/fable-library-js.4.29.0/Util.js";
import { isNullOrEmpty } from "../../ui/fable_modules/fable-library-js.4.29.0/String.js";
import { bind, defaultArg, map } from "../../ui/fable_modules/fable-library-js.4.29.0/Option.js";
import { mapIndexed, choose, filter, append, ofArray, singleton, empty, map as map_1, cons, foldBack, toArray } from "../../ui/fable_modules/fable-library-js.4.29.0/List.js";
import { ofList, tryFind } from "../../ui/fable_modules/fable-library-js.4.29.0/Map.js";

export class ActionKind extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["Origin", "Cylinder", "Sphere", "Box", "HalfPlane", "Translate", "Rotate", "Move", "Union", "Subtract", "Intersect", "Sketch", "FromSketch", "Thicken", "Shell", "Mesh"];
    }
}

export function ActionKind_$reflection() {
    return union_type("Server.ActionKind", [], ActionKind, () => [[], [["radius", float64_type], ["height", float64_type]], [["radius", float64_type]], [["width", float64_type], ["height", float64_type], ["depth", float64_type]], [["axis", string_type], ["offset", float64_type], ["flip", bool_type]], [["child", option_type(string_type)], ["x", float64_type], ["y", float64_type], ["z", float64_type]], [["child", option_type(string_type)], ["ax", float64_type], ["ay", float64_type], ["az", float64_type], ["angle", float64_type]], [["child", option_type(string_type)], ["frame", option_type(string_type)]], [["a", option_type(string_type)], ["b", option_type(string_type)], ["radius", float64_type]], [["a", option_type(string_type)], ["b", option_type(string_type)], ["radius", float64_type]], [["a", option_type(string_type)], ["b", option_type(string_type)], ["radius", float64_type]], [["origin", option_type(string_type)], ["plane", SketchPlane_$reflection()], ["sketch", ActionSketch_$reflection()]], [["child", option_type(string_type)], ["flip", bool_type], ["selection", FromSketchSelection_$reflection()]], [["child", option_type(string_type)], ["amount", float64_type]], [["child", option_type(string_type)], ["thickness", float64_type]], [["child", option_type(string_type)], ["size", float64_type], ["resolution", int32_type]]]);
}

export class DisplaySettings extends Record {
    constructor(Enabled, Color, Opacity, IsoValue) {
        super();
        this.Enabled = Enabled;
        this.Color = Color;
        this.Opacity = Opacity;
        this.IsoValue = IsoValue;
    }
}

export function DisplaySettings_$reflection() {
    return record_type("Server.DisplaySettings", [], DisplaySettings, () => [["Enabled", bool_type], ["Color", array_type(float64_type)], ["Opacity", float64_type], ["IsoValue", float64_type]]);
}

export const DisplaySettingsModule_defaults = new DisplaySettings(false, new Float64Array([0.522, 0.682, 0.784]), 0.9, 0);

export class FieldSliceSettings extends Record {
    constructor(Enabled, Plane, Offset, Extent) {
        super();
        this.Enabled = Enabled;
        this.Plane = Plane;
        this.Offset = Offset;
        this.Extent = Extent;
    }
}

export function FieldSliceSettings_$reflection() {
    return record_type("Server.FieldSliceSettings", [], FieldSliceSettings, () => [["Enabled", bool_type], ["Plane", string_type], ["Offset", float64_type], ["Extent", float64_type]]);
}

export const FieldSliceSettingsModule_defaults = new FieldSliceSettings(false, "Z", 0, 20);

export class DocAction extends Record {
    constructor(Id, Name, Kind, Visible, Display, FieldSlice) {
        super();
        this.Id = Id;
        this.Name = Name;
        this.Kind = Kind;
        this.Visible = Visible;
        this.Display = Display;
        this.FieldSlice = FieldSlice;
    }
}

export function DocAction_$reflection() {
    return record_type("Server.DocAction", [], DocAction, () => [["Id", string_type], ["Name", option_type(string_type)], ["Kind", ActionKind_$reflection()], ["Visible", bool_type], ["Display", option_type(DisplaySettings_$reflection())], ["FieldSlice", option_type(FieldSliceSettings_$reflection())]]);
}

export class DisplayField extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["DisplayColor", "DisplayOpacity", "DisplayIsoValue"];
    }
}

export function DisplayField_$reflection() {
    return union_type("Server.DisplayField", [], DisplayField, () => [[], [], []]);
}

export class FieldSliceField extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["SlicePlane", "SliceOffset"];
    }
}

export function FieldSliceField_$reflection() {
    return union_type("Server.FieldSliceField", [], FieldSliceField, () => [[], []]);
}

export class SketchEntityField extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["PointX", "PointY", "CircleRadius", "ArcThroughX", "ArcThroughY"];
    }
}

export function SketchEntityField_$reflection() {
    return union_type("Server.SketchEntityField", [], SketchEntityField, () => [[], [], [], [], []]);
}

export class SketchConstraintField extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["ConstraintLabelX", "ConstraintLabelY", "ConstraintDistance", "ConstraintDiameter", "ConstraintAngle"];
    }
}

export function SketchConstraintField_$reflection() {
    return union_type("Server.SketchConstraintField", [], SketchConstraintField, () => [[], [], [], [], []]);
}

export class FromSketchSelectionValue extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["SelectionLoopValue", "SelectionElementsValue"];
    }
}

export function FromSketchSelectionValue_$reflection() {
    return union_type("Server.FromSketchSelectionValue", [], FromSketchSelectionValue, () => [[["Item", option_type(string_type)]], [["Item", list_type(string_type)]]]);
}

export class ActionParamField extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["CylinderRadius", "CylinderHeight", "SphereRadius", "BoxWidth", "BoxHeight", "BoxDepth", "TranslateChild", "TranslateX", "TranslateY", "TranslateZ", "RotateChild", "RotateAxisX", "RotateAxisY", "RotateAxisZ", "RotateAngle", "HalfPlaneAxis", "HalfPlaneOffset", "HalfPlaneFlip", "MoveChild", "MoveFrame", "UnionA", "UnionB", "UnionRadius", "SubtractA", "SubtractB", "SubtractRadius", "IntersectA", "IntersectB", "IntersectRadius", "SketchOrigin", "SketchPlane", "SketchEntityField", "SketchConstraintField", "FromSketchChild", "FromSketchFlip", "FromSketchSelection", "ThickenChild", "ThickenAmount", "ShellChild", "ShellThickness", "MeshChild", "MeshSize", "MeshResolution"];
    }
}

export function ActionParamField_$reflection() {
    return union_type("Server.ActionParamField", [], ActionParamField, () => [[], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [["Item1", string_type], ["Item2", SketchEntityField_$reflection()]], [["Item1", int32_type], ["Item2", SketchConstraintField_$reflection()]], [], [], [], [], [], [], [], [], [], []]);
}

export class ParamValue extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["VNull", "VBool", "VInt", "VFloat", "VString", "VArray", "VRecord"];
    }
}

export function ParamValue_$reflection() {
    return union_type("Server.ParamValue", [], ParamValue, () => [[], [["Item", bool_type]], [["Item", int32_type]], [["Item", float64_type]], [["Item", string_type]], [["Item", list_type(ParamValue_$reflection())]], [["Item", class_type("Microsoft.FSharp.Collections.FSharpMap`2", [string_type, ParamValue_$reflection()])]]]);
}

export function ParamValueModule_asFloat(_arg) {
    switch (_arg.tag) {
        case 3:
            return _arg.fields[0];
        case 2:
            return _arg.fields[0];
        default:
            return undefined;
    }
}

export function ParamValueModule_asInt(_arg) {
    let x;
    let matchResult, x_1, x_2;
    switch (_arg.tag) {
        case 2: {
            matchResult = 0;
            x_1 = _arg.fields[0];
            break;
        }
        case 3: {
            if ((x = _arg.fields[0], Math.abs(x - round(x)) < 1E-09)) {
                matchResult = 1;
                x_2 = _arg.fields[0];
            }
            else {
                matchResult = 2;
            }
            break;
        }
        default:
            matchResult = 2;
    }
    switch (matchResult) {
        case 0:
            return x_1;
        case 1:
            return ~~round(x_2);
        default:
            return undefined;
    }
}

export function ParamValueModule_asBool(_arg) {
    if (_arg.tag === 1) {
        return _arg.fields[0];
    }
    else {
        return undefined;
    }
}

export function ParamValueModule_asString(_arg) {
    if (_arg.tag === 4) {
        return _arg.fields[0];
    }
    else {
        return undefined;
    }
}

export function ParamValueModule_asStringOption(value) {
    switch (value.tag) {
        case 0:
            return undefined;
        case 4:
            if (isNullOrEmpty(value.fields[0])) {
                return undefined;
            }
            else {
                return value.fields[0];
            }
        default:
            return undefined;
    }
}

export function ParamValueModule_asFloatArray(_arg) {
    if (_arg.tag === 5) {
        return map(toArray, foldBack((item, acc) => {
            let x, xs;
            return (item != null) ? ((acc != null) ? ((x = item, (xs = acc, cons(x, xs)))) : undefined) : undefined;
        }, map_1(ParamValueModule_asFloat, _arg.fields[0]), empty()));
    }
    else {
        return undefined;
    }
}

export function ParamValueModule_tryField(key, _arg) {
    if (_arg.tag === 6) {
        return tryFind(key, _arg.fields[0]);
    }
    else {
        return undefined;
    }
}

export class Document$ extends Record {
    constructor(Name, Actions, SelectedId) {
        super();
        this.Name = Name;
        this.Actions = Actions;
        this.SelectedId = SelectedId;
    }
}

export function Document$_$reflection() {
    return record_type("Server.Document", [], Document$, () => [["Name", string_type], ["Actions", list_type(DocAction_$reflection())], ["SelectedId", option_type(string_type)]]);
}

export function DocumentModule_pathOfDisplayField(_arg) {
    switch (_arg.tag) {
        case 1:
            return singleton("display.opacity");
        case 2:
            return singleton("display.isoValue");
        default:
            return ofArray(["display.color.0", "display.color.1", "display.color.2"]);
    }
}

export function DocumentModule_pathOfFieldSliceField(_arg) {
    if (_arg.tag === 1) {
        return singleton("fieldSlice.offset");
    }
    else {
        return empty();
    }
}

export function DocumentModule_pathOfParamField(_arg) {
    switch (_arg.tag) {
        case 1:
            return "height";
        case 2:
            return "radius";
        case 3:
            return "width";
        case 4:
            return "height";
        case 5:
            return "depth";
        case 6:
            return "child";
        case 7:
            return "x";
        case 8:
            return "y";
        case 9:
            return "z";
        case 10:
            return "child";
        case 11:
            return "ax";
        case 12:
            return "ay";
        case 13:
            return "az";
        case 14:
            return "angle";
        case 15:
            return "axis";
        case 16:
            return "offset";
        case 17:
            return "flip";
        case 18:
            return "child";
        case 19:
            return "frame";
        case 20:
            return "a";
        case 21:
            return "b";
        case 22:
            return "radius";
        case 23:
            return "a";
        case 24:
            return "b";
        case 25:
            return "radius";
        case 26:
            return "a";
        case 27:
            return "b";
        case 28:
            return "radius";
        case 29:
            return "origin";
        case 30:
            return "plane";
        case 31:
            switch (_arg.fields[1].tag) {
                case 1:
                    return `sketch.entity.${_arg.fields[0]}.y`;
                case 2:
                    return `sketch.entity.${_arg.fields[0]}.radius`;
                case 3:
                    return `sketch.entity.${_arg.fields[0]}.throughX`;
                case 4:
                    return `sketch.entity.${_arg.fields[0]}.throughY`;
                default:
                    return `sketch.entity.${_arg.fields[0]}.x`;
            }
        case 32:
            switch (_arg.fields[1].tag) {
                case 1:
                    return `sketch.constraint.${_arg.fields[0]}.labelPosition.y`;
                case 2:
                    return `sketch.constraint.${_arg.fields[0]}.distance`;
                case 3:
                    return `sketch.constraint.${_arg.fields[0]}.diameter`;
                case 4:
                    return `sketch.constraint.${_arg.fields[0]}.angle`;
                default:
                    return `sketch.constraint.${_arg.fields[0]}.labelPosition.x`;
            }
        case 33:
            return "child";
        case 34:
            return "flip";
        case 35:
            return "selection";
        case 36:
            return "child";
        case 37:
            return "amount";
        case 38:
            return "child";
        case 39:
            return "thickness";
        case 40:
            return "child";
        case 41:
            return "size";
        case 42:
            return "resolution";
        default:
            return "radius";
    }
}

function DocumentModule_mapActionById(id, update, doc) {
    return new Document$(doc.Name, map_1((action) => {
        if (action.Id === id) {
            return update(action);
        }
        else {
            return action;
        }
    }, doc.Actions), doc.SelectedId);
}

function DocumentModule_floatOr(current, key, expected, value) {
    if (equals(key, expected)) {
        return defaultArg(ParamValueModule_asFloat(value), current);
    }
    else {
        return current;
    }
}

function DocumentModule_intOr(current, key, expected, value) {
    if (equals(key, expected)) {
        return defaultArg(ParamValueModule_asInt(value), current) | 0;
    }
    else {
        return current | 0;
    }
}

function DocumentModule_boolOr(current, key, expected, value) {
    if (equals(key, expected)) {
        return defaultArg(ParamValueModule_asBool(value), current);
    }
    else {
        return current;
    }
}

function DocumentModule_stringOr(current, key, expected, value) {
    if (equals(key, expected)) {
        return defaultArg(ParamValueModule_asString(value), current);
    }
    else {
        return current;
    }
}

function DocumentModule_stringOptionOr(current, key, expected, value) {
    if (equals(key, expected)) {
        return ParamValueModule_asStringOption(value);
    }
    else {
        return current;
    }
}

function DocumentModule_applyWhenSome(decode, apply, current, value) {
    return defaultArg(map(apply, decode(value)), current);
}

function DocumentModule_patchLabelPosition(field, value, current) {
    const pos = defaultArg(current, new LabelPos(0, 0));
    const number = ParamValueModule_asFloat(value);
    return new LabelPos((field === "x") ? defaultArg(number, pos.X) : pos.X, (field === "y") ? defaultArg(number, pos.Y) : pos.Y);
}

function DocumentModule_patchConstraintLabel(field, value, _arg) {
    switch (_arg.tag) {
        case 6:
            return new SketchConstraint(6, [_arg.fields[0], _arg.fields[1], _arg.fields[2], DocumentModule_patchLabelPosition(field, value, _arg.fields[3])]);
        case 7:
            return new SketchConstraint(7, [_arg.fields[0], _arg.fields[1], _arg.fields[2], _arg.fields[3], DocumentModule_patchLabelPosition(field, value, _arg.fields[4])]);
        case 18:
            return new SketchConstraint(18, [_arg.fields[0], _arg.fields[1], _arg.fields[2], _arg.fields[3], _arg.fields[4], _arg.fields[5], _arg.fields[6], DocumentModule_patchLabelPosition(field, value, _arg.fields[7])]);
        case 19:
            return new SketchConstraint(19, [_arg.fields[0], _arg.fields[1], _arg.fields[2], _arg.fields[3], _arg.fields[4], _arg.fields[5], DocumentModule_patchLabelPosition(field, value, _arg.fields[6])]);
        case 20:
            return new SketchConstraint(20, [_arg.fields[0], _arg.fields[1], _arg.fields[2], _arg.fields[3], _arg.fields[4], DocumentModule_patchLabelPosition(field, value, _arg.fields[5])]);
        case 21:
            return new SketchConstraint(21, [_arg.fields[0], _arg.fields[1], _arg.fields[2], _arg.fields[3], DocumentModule_patchLabelPosition(field, value, _arg.fields[4])]);
        case 22:
            return new SketchConstraint(22, [_arg.fields[0], _arg.fields[1], _arg.fields[2], _arg.fields[3], _arg.fields[4], _arg.fields[5], DocumentModule_patchLabelPosition(field, value, _arg.fields[6])]);
        case 23:
            return new SketchConstraint(23, [_arg.fields[0], _arg.fields[1], _arg.fields[2], _arg.fields[3], _arg.fields[4], _arg.fields[5], DocumentModule_patchLabelPosition(field, value, _arg.fields[6])]);
        case 17:
            return new SketchConstraint(17, [_arg.fields[0], _arg.fields[1], _arg.fields[2], DocumentModule_patchLabelPosition(field, value, _arg.fields[3])]);
        case 24:
            return new SketchConstraint(24, [_arg.fields[0], _arg.fields[1], _arg.fields[2], _arg.fields[3], _arg.fields[4], _arg.fields[5], _arg.fields[6], _arg.fields[7], _arg.fields[8], _arg.fields[9], DocumentModule_patchLabelPosition(field, value, _arg.fields[10])]);
        default:
            return _arg;
    }
}

function DocumentModule_patchConstraintScalar(value, _arg) {
    switch (_arg.tag) {
        case 6:
            return new SketchConstraint(6, [_arg.fields[0], _arg.fields[1], DocumentModule_applyWhenSome(ParamValueModule_asFloat, (next) => next, _arg.fields[2], value), _arg.fields[3]]);
        case 7:
            return new SketchConstraint(7, [_arg.fields[0], _arg.fields[1], _arg.fields[2], DocumentModule_applyWhenSome(ParamValueModule_asFloat, (next_1) => next_1, _arg.fields[3], value), _arg.fields[4]]);
        case 18:
            return new SketchConstraint(18, [_arg.fields[0], _arg.fields[1], _arg.fields[2], _arg.fields[3], _arg.fields[4], _arg.fields[5], DocumentModule_applyWhenSome(ParamValueModule_asFloat, (next_2) => next_2, _arg.fields[6], value), _arg.fields[7]]);
        case 19:
            return new SketchConstraint(19, [_arg.fields[0], _arg.fields[1], _arg.fields[2], _arg.fields[3], _arg.fields[4], DocumentModule_applyWhenSome(ParamValueModule_asFloat, (next_3) => next_3, _arg.fields[5], value), _arg.fields[6]]);
        case 20:
            return new SketchConstraint(20, [_arg.fields[0], _arg.fields[1], _arg.fields[2], _arg.fields[3], DocumentModule_applyWhenSome(ParamValueModule_asFloat, (next_4) => next_4, _arg.fields[4], value), _arg.fields[5]]);
        case 21:
            return new SketchConstraint(21, [_arg.fields[0], _arg.fields[1], _arg.fields[2], DocumentModule_applyWhenSome(ParamValueModule_asFloat, (next_5) => next_5, _arg.fields[3], value), _arg.fields[4]]);
        case 22:
            return new SketchConstraint(22, [_arg.fields[0], _arg.fields[1], _arg.fields[2], _arg.fields[3], _arg.fields[4], DocumentModule_applyWhenSome(ParamValueModule_asFloat, (next_6) => next_6, _arg.fields[5], value), _arg.fields[6]]);
        case 23:
            return new SketchConstraint(23, [_arg.fields[0], _arg.fields[1], _arg.fields[2], _arg.fields[3], DocumentModule_applyWhenSome(ParamValueModule_asFloat, (next_7) => next_7, _arg.fields[4], value), _arg.fields[5], _arg.fields[6]]);
        case 17:
            return new SketchConstraint(17, [_arg.fields[0], _arg.fields[1], DocumentModule_applyWhenSome(ParamValueModule_asFloat, (next_8) => next_8, _arg.fields[2], value), _arg.fields[3]]);
        case 24:
            return new SketchConstraint(24, [_arg.fields[0], _arg.fields[1], _arg.fields[2], _arg.fields[3], _arg.fields[4], _arg.fields[5], DocumentModule_applyWhenSome(ParamValueModule_asFloat, (next_9) => next_9, _arg.fields[6], value), _arg.fields[7], _arg.fields[8], _arg.fields[9], _arg.fields[10]]);
        default:
            return _arg;
    }
}

export function DocumentModule_select(id, doc) {
    return new Document$(doc.Name, doc.Actions, id);
}

export function DocumentModule_addAction(action, doc) {
    return new Document$(doc.Name, append(doc.Actions, singleton(action)), action.Id);
}

export function DocumentModule_updateAction(id, updated, doc) {
    return new Document$(doc.Name, map_1((a) => {
        if (a.Id === id) {
            return updated;
        }
        else {
            return a;
        }
    }, doc.Actions), doc.SelectedId);
}

export function DocumentModule_removeAction(id, doc) {
    return new Document$(doc.Name, filter((a) => (a.Id !== id), doc.Actions), equals(doc.SelectedId, id) ? undefined : doc.SelectedId);
}

export function DocumentModule_toggleVisible(id, doc) {
    return new Document$(doc.Name, map_1((a) => {
        if (a.Id === id) {
            return new DocAction(a.Id, a.Name, a.Kind, !a.Visible, a.Display, a.FieldSlice);
        }
        else {
            return a;
        }
    }, doc.Actions), doc.SelectedId);
}

export function DocumentModule_toggleDisplay(id, doc) {
    return new Document$(doc.Name, map_1((a) => {
        if (a.Id !== id) {
            return a;
        }
        else {
            const d = defaultArg(a.Display, DisplaySettingsModule_defaults);
            return new DocAction(a.Id, a.Name, a.Kind, a.Visible, new DisplaySettings(!d.Enabled, d.Color, d.Opacity, d.IsoValue), a.FieldSlice);
        }
    }, doc.Actions), doc.SelectedId);
}

export function DocumentModule_patchDisplayValue(id, field, value, doc) {
    return DocumentModule_mapActionById(id, (action) => {
        const display = defaultArg(action.Display, DisplaySettingsModule_defaults);
        return new DocAction(action.Id, action.Name, action.Kind, action.Visible, (field.tag === 1) ? DocumentModule_applyWhenSome(ParamValueModule_asFloat, (next_1) => (new DisplaySettings(display.Enabled, display.Color, next_1, display.IsoValue)), display, value) : ((field.tag === 2) ? DocumentModule_applyWhenSome(ParamValueModule_asFloat, (next_2) => (new DisplaySettings(display.Enabled, display.Color, display.Opacity, next_2)), display, value) : DocumentModule_applyWhenSome(ParamValueModule_asFloatArray, (next) => (new DisplaySettings(display.Enabled, next, display.Opacity, display.IsoValue)), display, value)), action.FieldSlice);
    }, doc);
}

export function DocumentModule_toggleFieldSlice(id, doc) {
    return DocumentModule_mapActionById(id, (action) => {
        const fieldSlice = defaultArg(action.FieldSlice, FieldSliceSettingsModule_defaults);
        return new DocAction(action.Id, action.Name, action.Kind, action.Visible, action.Display, new FieldSliceSettings(!fieldSlice.Enabled, fieldSlice.Plane, fieldSlice.Offset, fieldSlice.Extent));
    }, doc);
}

export function DocumentModule_patchFieldSliceValue(id, field, value, doc) {
    return DocumentModule_mapActionById(id, (action) => {
        const fieldSlice = defaultArg(action.FieldSlice, FieldSliceSettingsModule_defaults);
        return new DocAction(action.Id, action.Name, action.Kind, action.Visible, action.Display, (field.tag === 1) ? DocumentModule_applyWhenSome(ParamValueModule_asFloat, (next_1) => (new FieldSliceSettings(fieldSlice.Enabled, fieldSlice.Plane, next_1, fieldSlice.Extent)), fieldSlice, value) : DocumentModule_applyWhenSome(ParamValueModule_asString, (next) => (new FieldSliceSettings(fieldSlice.Enabled, next, fieldSlice.Offset, fieldSlice.Extent)), fieldSlice, value));
    }, doc);
}

export function DocumentModule_reorder(ids, doc) {
    const lookup = ofList(map_1((a) => [a.Id, a], doc.Actions), {
        Compare: comparePrimitives,
    });
    return new Document$(doc.Name, choose((id) => tryFind(id, lookup), ids), doc.SelectedId);
}

function DocumentModule_patchSketchEntityParam(entityId, field, value, sketch) {
    return new ActionSketch(map_1((entity) => {
        let matchResult, id_3, x_1, y_1, center_1, id_4, radius_1, endId_1, id_5, startId_1, through_1;
        switch (entity.tag) {
            case 0: {
                if (entity.fields[0] === entityId) {
                    matchResult = 0;
                    id_3 = entity.fields[0];
                    x_1 = entity.fields[1];
                    y_1 = entity.fields[2];
                }
                else {
                    matchResult = 3;
                }
                break;
            }
            case 2: {
                if ((entity.fields[0] === entityId) && equals(field, new SketchEntityField(2, []))) {
                    matchResult = 1;
                    center_1 = entity.fields[1];
                    id_4 = entity.fields[0];
                    radius_1 = entity.fields[2];
                }
                else {
                    matchResult = 3;
                }
                break;
            }
            case 3: {
                if (entity.fields[3].tag === 1) {
                    if (entity.fields[0] === entityId) {
                        matchResult = 2;
                        endId_1 = entity.fields[2];
                        id_5 = entity.fields[0];
                        startId_1 = entity.fields[1];
                        through_1 = entity.fields[3].fields[0];
                    }
                    else {
                        matchResult = 3;
                    }
                }
                else {
                    matchResult = 3;
                }
                break;
            }
            default:
                matchResult = 3;
        }
        switch (matchResult) {
            case 0: {
                const number = ParamValueModule_asFloat(value);
                return new RenderEntity(0, [id_3, (field.tag === 0) ? defaultArg(number, x_1) : x_1, (field.tag === 1) ? defaultArg(number, y_1) : y_1]);
            }
            case 1:
                return new RenderEntity(2, [id_4, center_1, DocumentModule_applyWhenSome(ParamValueModule_asFloat, (next) => next, radius_1, value)]);
            case 2: {
                const number_1 = ParamValueModule_asFloat(value);
                return new RenderEntity(3, [id_5, startId_1, endId_1, new ArcData(1, [new FreePoint((field.tag === 3) ? defaultArg(number_1, through_1.X) : through_1.X, (field.tag === 4) ? defaultArg(number_1, through_1.Y) : through_1.Y)])]);
            }
            default:
                return entity;
        }
    }, sketch.Entities), sketch.Constraints);
}

function DocumentModule_patchFromSketchSelection(current, _arg) {
    if (_arg.tag === 0) {
        return new FromSketchSelection(0, [_arg.fields[0]]);
    }
    else {
        return new FromSketchSelection(1, [_arg.fields[0]]);
    }
}

function DocumentModule_patchSketchConstraintParam(index, field, value, sketch) {
    return new ActionSketch(sketch.Entities, mapIndexed((i, item) => {
        if (i !== index) {
            return item;
        }
        else {
            switch (field.tag) {
                case 1:
                    return DocumentModule_patchConstraintLabel("y", value, item);
                case 2:
                case 3:
                case 4:
                    return DocumentModule_patchConstraintScalar(value, item);
                default:
                    return DocumentModule_patchConstraintLabel("x", value, item);
            }
        }
    }, sketch.Constraints));
}

export function DocumentModule_patchParamValue(id, field, value, doc) {
    return DocumentModule_mapActionById(id, (action) => {
        let matchValue, r, h, r_1, w, h_1, d, z, y, x, az, ay, ax, ang, off, fl, ax_1, r_2, r_3, r_4, sketch, nextPlane, matchValue_1, nextSketch, sel, flip, matchValue_2, amt, t, s, res;
        return new DocAction(action.Id, action.Name, (matchValue = action.Kind, (matchValue.tag === 1) ? ((r = matchValue.fields[0], (h = matchValue.fields[1], new ActionKind(1, [(field.tag === 0) ? defaultArg(ParamValueModule_asFloat(value), r) : r, (field.tag === 1) ? defaultArg(ParamValueModule_asFloat(value), h) : h])))) : ((matchValue.tag === 2) ? ((r_1 = matchValue.fields[0], new ActionKind(2, [(field.tag === 2) ? defaultArg(ParamValueModule_asFloat(value), r_1) : r_1]))) : ((matchValue.tag === 3) ? ((w = matchValue.fields[0], (h_1 = matchValue.fields[1], (d = matchValue.fields[2], new ActionKind(3, [(field.tag === 3) ? defaultArg(ParamValueModule_asFloat(value), w) : w, (field.tag === 4) ? defaultArg(ParamValueModule_asFloat(value), h_1) : h_1, (field.tag === 5) ? defaultArg(ParamValueModule_asFloat(value), d) : d]))))) : ((matchValue.tag === 5) ? ((z = matchValue.fields[3], (y = matchValue.fields[2], (x = matchValue.fields[1], new ActionKind(5, [(field.tag === 6) ? ParamValueModule_asStringOption(value) : matchValue.fields[0], (field.tag === 7) ? defaultArg(ParamValueModule_asFloat(value), x) : x, (field.tag === 8) ? defaultArg(ParamValueModule_asFloat(value), y) : y, (field.tag === 9) ? defaultArg(ParamValueModule_asFloat(value), z) : z]))))) : ((matchValue.tag === 6) ? ((az = matchValue.fields[3], (ay = matchValue.fields[2], (ax = matchValue.fields[1], (ang = matchValue.fields[4], new ActionKind(6, [(field.tag === 10) ? ParamValueModule_asStringOption(value) : matchValue.fields[0], (field.tag === 11) ? defaultArg(ParamValueModule_asFloat(value), ax) : ax, (field.tag === 12) ? defaultArg(ParamValueModule_asFloat(value), ay) : ay, (field.tag === 13) ? defaultArg(ParamValueModule_asFloat(value), az) : az, (field.tag === 14) ? defaultArg(ParamValueModule_asFloat(value), ang) : ang])))))) : ((matchValue.tag === 4) ? ((off = matchValue.fields[1], (fl = matchValue.fields[2], (ax_1 = matchValue.fields[0], new ActionKind(4, [(field.tag === 15) ? defaultArg(ParamValueModule_asString(value), ax_1) : ax_1, (field.tag === 16) ? defaultArg(ParamValueModule_asFloat(value), off) : off, (field.tag === 17) ? defaultArg(ParamValueModule_asBool(value), fl) : fl]))))) : ((matchValue.tag === 7) ? (new ActionKind(7, [(field.tag === 18) ? ParamValueModule_asStringOption(value) : matchValue.fields[0], (field.tag === 19) ? ParamValueModule_asStringOption(value) : matchValue.fields[1]])) : ((matchValue.tag === 8) ? ((r_2 = matchValue.fields[2], new ActionKind(8, [(field.tag === 20) ? ParamValueModule_asStringOption(value) : matchValue.fields[0], (field.tag === 21) ? ParamValueModule_asStringOption(value) : matchValue.fields[1], (field.tag === 22) ? defaultArg(ParamValueModule_asFloat(value), r_2) : r_2]))) : ((matchValue.tag === 9) ? ((r_3 = matchValue.fields[2], new ActionKind(9, [(field.tag === 23) ? ParamValueModule_asStringOption(value) : matchValue.fields[0], (field.tag === 24) ? ParamValueModule_asStringOption(value) : matchValue.fields[1], (field.tag === 25) ? defaultArg(ParamValueModule_asFloat(value), r_3) : r_3]))) : ((matchValue.tag === 10) ? ((r_4 = matchValue.fields[2], new ActionKind(10, [(field.tag === 26) ? ParamValueModule_asStringOption(value) : matchValue.fields[0], (field.tag === 27) ? ParamValueModule_asStringOption(value) : matchValue.fields[1], (field.tag === 28) ? defaultArg(ParamValueModule_asFloat(value), r_4) : r_4]))) : ((matchValue.tag === 11) ? ((sketch = matchValue.fields[2], (nextPlane = ((field.tag === 30) ? ((matchValue_1 = ParamValueModule_asString(value), (matchValue_1 != null) ? ((matchValue_1 === "XZ") ? (new SketchPlane(1, [])) : ((matchValue_1 === "YZ") ? (new SketchPlane(2, [])) : (new SketchPlane(0, [])))) : (new SketchPlane(0, [])))) : matchValue.fields[1]), (nextSketch = ((field.tag === 31) ? DocumentModule_patchSketchEntityParam(field.fields[0], field.fields[1], value, sketch) : ((field.tag === 32) ? DocumentModule_patchSketchConstraintParam(field.fields[0], field.fields[1], value, sketch) : sketch)), new ActionKind(11, [(field.tag === 29) ? ParamValueModule_asStringOption(value) : matchValue.fields[0], nextPlane, nextSketch]))))) : ((matchValue.tag === 12) ? ((sel = matchValue.fields[2], (flip = matchValue.fields[1], new ActionKind(12, [(field.tag === 33) ? ParamValueModule_asStringOption(value) : matchValue.fields[0], (field.tag === 34) ? defaultArg(ParamValueModule_asBool(value), flip) : flip, (field.tag === 35) ? ((value.tag === 6) ? ((matchValue_2 = bind(ParamValueModule_asString, ParamValueModule_tryField("case", value)), (matchValue_2 != null) ? ((matchValue_2 === "SelectionElements") ? DocumentModule_patchFromSketchSelection(sel, new FromSketchSelectionValue(1, [defaultArg(bind((_arg_21) => {
            if (_arg_21.tag === 5) {
                return foldBack((item, acc) => {
                    let x_1, xs;
                    return (item != null) ? ((acc != null) ? ((x_1 = item, (xs = acc, cons(x_1, xs)))) : undefined) : undefined;
                }, map_1(ParamValueModule_asString, _arg_21.fields[0]), empty());
            }
            else {
                return undefined;
            }
        }, ParamValueModule_tryField("lineIds", value)), empty())])) : DocumentModule_patchFromSketchSelection(sel, new FromSketchSelectionValue(0, [bind(ParamValueModule_asStringOption, ParamValueModule_tryField("loopId", value))]))) : DocumentModule_patchFromSketchSelection(sel, new FromSketchSelectionValue(0, [bind(ParamValueModule_asStringOption, ParamValueModule_tryField("loopId", value))])))) : sel) : sel])))) : ((matchValue.tag === 13) ? ((amt = matchValue.fields[1], new ActionKind(13, [(field.tag === 36) ? ParamValueModule_asStringOption(value) : matchValue.fields[0], (field.tag === 37) ? defaultArg(ParamValueModule_asFloat(value), amt) : amt]))) : ((matchValue.tag === 14) ? ((t = matchValue.fields[1], new ActionKind(14, [(field.tag === 38) ? ParamValueModule_asStringOption(value) : matchValue.fields[0], (field.tag === 39) ? defaultArg(ParamValueModule_asFloat(value), t) : t]))) : ((matchValue.tag === 15) ? ((s = matchValue.fields[1], (res = (matchValue.fields[2] | 0), new ActionKind(15, [(field.tag === 40) ? ParamValueModule_asStringOption(value) : matchValue.fields[0], (field.tag === 41) ? defaultArg(ParamValueModule_asFloat(value), s) : s, (field.tag === 42) ? defaultArg(ParamValueModule_asInt(value), res) : res])))) : matchValue))))))))))))))), action.Visible, action.Display, action.FieldSlice);
    }, doc);
}

export function DocumentModule_defaultDocument() {
    return new Document$("untitled", ofArray([new DocAction("origin", "origin", new ActionKind(0, []), true, undefined, undefined), new DocAction("cyl1", "cylinder", new ActionKind(1, [10, 40]), true, undefined, undefined), new DocAction("sph1", "sphere", new ActionKind(2, [8]), true, undefined, undefined), new DocAction("sub1", "subtract", new ActionKind(9, ["cyl1", "sph1", 0]), true, undefined, undefined), new DocAction("sketch1", "square", new ActionKind(11, ["origin", new SketchPlane(0, []), new ActionSketch(ofArray([new RenderEntity(0, ["p_bl", 0, 0]), new RenderEntity(0, ["p_br", 10, 0]), new RenderEntity(0, ["p_tr", 10, 10]), new RenderEntity(0, ["p_tl", 0, 10]), new RenderEntity(1, ["l_bottom", "p_bl", "p_br"]), new RenderEntity(1, ["l_right", "p_br", "p_tr"]), new RenderEntity(1, ["l_top", "p_tr", "p_tl"]), new RenderEntity(1, ["l_left", "p_tl", "p_bl"])]), ofArray([new SketchConstraint(0, ["p_bl", 0, 0]), new SketchConstraint(4, ["p_bl", "p_br"]), new SketchConstraint(4, ["p_tl", "p_tr"]), new SketchConstraint(5, ["p_bl", "p_tl"]), new SketchConstraint(5, ["p_br", "p_tr"]), new SketchConstraint(6, ["p_bl", "p_br", 10, undefined]), new SketchConstraint(6, ["p_bl", "p_tl", 10, undefined])]))]), true, undefined, undefined), new DocAction("frame1", "frame", new ActionKind(5, ["origin", 18, 6, 12]), true, undefined, undefined), new DocAction("from1", "from-sketch", new ActionKind(12, ["sketch1", false, new FromSketchSelection(0, [undefined])]), true, undefined, undefined)]), "origin");
}

export function DocumentModule_emptyDocument() {
    return new Document$("untitled", singleton(new DocAction("origin", "origin", new ActionKind(0, []), true, undefined, undefined)), "origin");
}

