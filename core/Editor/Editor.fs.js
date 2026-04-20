import { toString, Record, Union } from "../../ui/fable_modules/fable-library-js.4.29.0/Types.js";
import { tuple_type, bool_type, list_type, option_type, class_type, float32_type, array_type, float64_type, record_type, int32_type, string_type, union_type } from "../../ui/fable_modules/fable-library-js.4.29.0/Reflection.js";
import { DocumentModule_emptyDocument, Document$, ActionParamField, SketchConstraintField, DocumentModule_select, DocumentModule_patchFieldSliceValue, DocumentModule_toggleFieldSlice, DocumentModule_patchDisplayValue, DocumentModule_toggleDisplay, DocumentModule_toggleVisible, DocumentModule_reorder, DocumentModule_updateAction, DocAction, ParamValue, DocumentModule_patchParamValue, DocumentModule_addAction, DocumentModule_removeAction, DocumentModule_pathOfFieldSliceField, ParamValueModule_asFloatArray, DocumentModule_pathOfDisplayField, DocumentModule_pathOfParamField, ParamValueModule_asFloat, ParamValueModule_asInt, DocumentModule_defaultDocument, ActionKind, FieldSliceField_$reflection, ParamValue_$reflection, DisplayField_$reflection, DocAction_$reflection, Document$_$reflection, ActionParamField_$reflection } from "./Domain.fs.js";
import { FromSketchSelectionModule_defaults, ActionSketchModule_empty, SketchPlane, LabelPos, ActionSketch_$reflection, LabelPos_$reflection } from "../Sketch/Sketch.fs.js";
import { Pipeline_compile, PipelineResult_$reflection } from "./Pipeline.fs.js";
import { Palette_back, Palette_skipToEnd, Palette_commitScalars, Palette_setScalarField, Palette_pickCommand, Palette_pickItem, Palette_setQuery, Palette_openSession, Palette_buildAction, Palette_toState, Palette_empty, PaletteSession_$reflection } from "./Palette.fs.js";
import { PickableModule_targetAction, PickableModule_selectionTarget, PickableModule_selectionPriority, PickableModule_pickId, PickableModule_sameTarget, SelectionTarget_$reflection } from "./Pickable.fs.js";
import { SketchAuthoring_removeConstraintAt, SketchAuthoring_addConstraintFromSelection, SketchAuthoring_placePendingConstraint, SketchAuthoring_applyToolClick, SketchAuthoring_requiredToolPoints, SketchAuthoring_updatePlacementDraft, SketchAuthoring_deleteTargets, SketchAuthoring_withUpdatedSketch, SketchAuthoring_tryEditableDimension, SketchAuthoring_trySelectedSketch, SketchAuthoring_withResolvedPendingConstraintValue, SketchUiState, SketchAuthoring_availabilityForSelection, ConstraintPlacementDraft_$reflection, EditingDimension_$reflection } from "../Sketch/SketchAuthoring.fs.js";
import { length, sortBy, tryHead, cons, isEmpty, ofArray, zip, filter as filter_1, contains as contains_1, singleton, append, map as map_2, exists, findIndex, take, choose, tryFind as tryFind_1, empty as empty_1, tryPick } from "../../ui/fable_modules/fable-library-js.4.29.0/List.js";
import { setItem, copy } from "../../ui/fable_modules/fable-library-js.4.29.0/Array.js";
import { add, ofList as ofList_1, map as map_1, tryFind, empty } from "../../ui/fable_modules/fable-library-js.4.29.0/Map.js";
import { compareArrays, safeHash, equals, comparePrimitives } from "../../ui/fable_modules/fable-library-js.4.29.0/Util.js";
import { RigidTransform_get_Identity, RigidTransform, RigidTransform_op_Multiply_ZFA4D60 } from "../Math/Transform.fs.js";
import { Quat_get_Identity, QuatModule_fromBasis } from "../Math/Quat.fs.js";
import { Vec3_get_Zero, Vec3 } from "../Math/Vec.fs.js";
import { orElseWith, filter, bind, map, defaultArg } from "../../ui/fable_modules/fable-library-js.4.29.0/Option.js";
import { foldChain } from "./Frames.fs.js";
import { contains, empty as empty_2, ofList } from "../../ui/fable_modules/fable-library-js.4.29.0/Set.js";
import { printf, toFail, join } from "../../ui/fable_modules/fable-library-js.4.29.0/String.js";
import { SlotRef, SlotTableModule_tryFindSlot, SlotTableModule_patchedValues } from "./SlotTable.fs.js";
import { SketchSolve_patchSolvedSketchSlots, SketchSolve_commitSolvedSketch } from "../Sketch/SketchSolve.fs.js";

export class ActionTemplate extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["SphereTemplate", "CylinderTemplate", "BoxTemplate", "HalfPlaneTemplate", "TranslateTemplate", "RotateTemplate", "MoveTemplate", "UnionTemplate", "SubtractTemplate", "IntersectTemplate", "SketchTemplate", "FromSketchTemplate", "ThickenTemplate", "ShellTemplate", "MeshTemplate"];
    }
}

export function ActionTemplate_$reflection() {
    return union_type("Server.ActionTemplate", [], ActionTemplate, () => [[], [], [], [], [], [], [], [], [], [], [], [], [], [], []]);
}

export class SketchToolKind extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["NoSketchTool", "LineTool", "RectangleTool", "RoundedRectangleTool", "CircleTool", "ArcTool"];
    }
}

export function SketchToolKind_$reflection() {
    return union_type("Server.SketchToolKind", [], SketchToolKind, () => [[], [], [], [], [], []]);
}

export class ConstraintPlacementKind extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["DistancePlacement", "AnglePlacement"];
    }
}

export function ConstraintPlacementKind_$reflection() {
    return union_type("Server.ConstraintPlacementKind", [], ConstraintPlacementKind, () => [[], []]);
}

export class GeometricConstraintKind extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["CoincidentConstraint", "HorizontalConstraint", "VerticalConstraint", "MidpointConstraint", "ParallelConstraint", "PerpendicularConstraint", "EqualConstraint", "TangentConstraint", "ConcentricConstraint", "FixedConstraint"];
    }
}

export function GeometricConstraintKind_$reflection() {
    return union_type("Server.GeometricConstraintKind", [], GeometricConstraintKind, () => [[], [], [], [], [], [], [], [], [], []]);
}

export class SketchDragKind extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["DragPoint", "DragConstraintLabel"];
    }
}

export function SketchDragKind_$reflection() {
    return union_type("Server.SketchDragKind", [], SketchDragKind, () => [[["pointId", string_type]], [["constraintIndex", int32_type]]]);
}

export class SketchDrag extends Record {
    constructor(SketchId, Kind, XField, YField, Target) {
        super();
        this.SketchId = SketchId;
        this.Kind = Kind;
        this.XField = XField;
        this.YField = YField;
        this.Target = Target;
    }
}

export function SketchDrag_$reflection() {
    return record_type("Server.SketchDrag", [], SketchDrag, () => [["SketchId", string_type], ["Kind", SketchDragKind_$reflection()], ["XField", ActionParamField_$reflection()], ["YField", ActionParamField_$reflection()], ["Target", LabelPos_$reflection()]]);
}

export class EditorState extends Record {
    constructor(Doc, Compiled, SlotValues, SolvedSketchParams, PaletteSession, HoveredTarget, SelectedTargets, SketchEditMode, SketchTool, SketchToolPoints, SketchToolPointRefs, LineChainStartPointId, EditingDimension, ActiveSketchDrag, PendingSketchDragCommit, ConstraintPlacementMode, ConstraintPlacementDraft, ConstraintPlacementCursor) {
        super();
        this.Doc = Doc;
        this.Compiled = Compiled;
        this.SlotValues = SlotValues;
        this.SolvedSketchParams = SolvedSketchParams;
        this.PaletteSession = PaletteSession;
        this.HoveredTarget = HoveredTarget;
        this.SelectedTargets = SelectedTargets;
        this.SketchEditMode = SketchEditMode;
        this.SketchTool = SketchTool;
        this.SketchToolPoints = SketchToolPoints;
        this.SketchToolPointRefs = SketchToolPointRefs;
        this.LineChainStartPointId = LineChainStartPointId;
        this.EditingDimension = EditingDimension;
        this.ActiveSketchDrag = ActiveSketchDrag;
        this.PendingSketchDragCommit = PendingSketchDragCommit;
        this.ConstraintPlacementMode = ConstraintPlacementMode;
        this.ConstraintPlacementDraft = ConstraintPlacementDraft;
        this.ConstraintPlacementCursor = ConstraintPlacementCursor;
    }
}

export function EditorState_$reflection() {
    return record_type("Server.EditorState", [], EditorState, () => [["Doc", Document$_$reflection()], ["Compiled", PipelineResult_$reflection()], ["SlotValues", array_type(float64_type)], ["SolvedSketchParams", class_type("Microsoft.FSharp.Collections.FSharpMap`2", [string_type, array_type(float32_type)])], ["PaletteSession", PaletteSession_$reflection()], ["HoveredTarget", option_type(SelectionTarget_$reflection())], ["SelectedTargets", list_type(SelectionTarget_$reflection())], ["SketchEditMode", bool_type], ["SketchTool", string_type], ["SketchToolPoints", list_type(LabelPos_$reflection())], ["SketchToolPointRefs", list_type(option_type(string_type))], ["LineChainStartPointId", option_type(string_type)], ["EditingDimension", option_type(EditingDimension_$reflection())], ["ActiveSketchDrag", option_type(SketchDrag_$reflection())], ["PendingSketchDragCommit", bool_type], ["ConstraintPlacementMode", option_type(ConstraintPlacementKind_$reflection())], ["ConstraintPlacementDraft", option_type(ConstraintPlacementDraft_$reflection())], ["ConstraintPlacementCursor", option_type(tuple_type(string_type, LabelPos_$reflection()))]]);
}

export class SerializedModel extends Record {
    constructor(Name, Actions) {
        super();
        this.Name = Name;
        this.Actions = Actions;
    }
}

export function SerializedModel_$reflection() {
    return record_type("Server.SerializedModel", [], SerializedModel, () => [["Name", string_type], ["Actions", list_type(DocAction_$reflection())]]);
}

export class ActionErrorView extends Record {
    constructor(ActionId, Key, Error$) {
        super();
        this.ActionId = ActionId;
        this.Key = Key;
        this.Error = Error$;
    }
}

export function ActionErrorView_$reflection() {
    return record_type("Server.ActionErrorView", [], ActionErrorView, () => [["ActionId", string_type], ["Key", string_type], ["Error", string_type]]);
}

export class SketchLoopView extends Record {
    constructor(Id, EntityIds) {
        super();
        this.Id = Id;
        this.EntityIds = EntityIds;
    }
}

export function SketchLoopView_$reflection() {
    return record_type("Server.SketchLoopView", [], SketchLoopView, () => [["Id", string_type], ["EntityIds", list_type(string_type)]]);
}

export class PickCandidateInput extends Record {
    constructor(PickId, Score) {
        super();
        this.PickId = (PickId | 0);
        this.Score = Score;
    }
}

export function PickCandidateInput_$reflection() {
    return record_type("Server.PickCandidateInput", [], PickCandidateInput, () => [["PickId", int32_type], ["Score", float32_type]]);
}

export class Effect extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["RunSketchSolve", "FinalizeSketchDrag", "ResolveAllSketches"];
    }
}

export function Effect_$reflection() {
    return union_type("Server.Effect", [], Effect, () => [[["Item", SketchDrag_$reflection()]], [["Item", SketchDrag_$reflection()]], []]);
}

export class Message extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["SelectAction", "SetHoveredTarget", "SetSelectedTargets", "AddDefaultAction", "AddAction", "UpdateAction", "RemoveAction", "ReorderActions", "ToggleActionVisible", "ToggleDisplay", "SetDisplayValue", "ToggleFieldSlice", "SetFieldSliceValue", "SetActionSlotValue", "SetActionStructureValue", "DeleteIntent", "ViewerHover", "ViewerPick", "StartEditingDimension", "CancelEditingDimension", "CommitEditingDimension", "ViewerDimensionClickTarget", "ReplaceSketch", "BeginSketchDrag", "UpdateSketchDragTarget", "ApplySketchSolveResult", "ApplyResolvedSketchResult", "FinishSketchDrag", "CancelSketchDrag", "ViewerToolClick", "ViewerPlaceConstraint", "ToggleSketchEdit", "SetSketchTool", "ToggleConstraintPlacement", "AddConstraintFromSelection", "DeleteSketchConstraint", "SetConstraintPlacementCursor", "PaletteOpen", "PaletteSetQuery", "PalettePick", "PaletteSetScalarField", "PaletteCommitScalars", "PaletteFinish", "PaletteBack", "PaletteClose", "ReplaceDocument", "LoadModel", "ClearModel"];
    }
}

export function Message_$reflection() {
    return union_type("Server.Message", [], Message, () => [[["Item", string_type]], [["Item", option_type(SelectionTarget_$reflection())]], [["Item", list_type(SelectionTarget_$reflection())]], [["Item1", ActionTemplate_$reflection()], ["Item2", string_type]], [["Item", DocAction_$reflection()]], [["Item1", string_type], ["Item2", DocAction_$reflection()]], [["Item", string_type]], [["Item", list_type(string_type)]], [["Item", string_type]], [["Item", string_type]], [["Item1", string_type], ["Item2", DisplayField_$reflection()], ["Item3", ParamValue_$reflection()]], [["Item", string_type]], [["Item1", string_type], ["Item2", FieldSliceField_$reflection()], ["Item3", ParamValue_$reflection()]], [["Item1", string_type], ["Item2", ActionParamField_$reflection()], ["Item3", ParamValue_$reflection()]], [["Item1", string_type], ["Item2", ActionParamField_$reflection()], ["Item3", ParamValue_$reflection()]], [], [["Item", list_type(PickCandidateInput_$reflection())]], [["Item1", string_type], ["Item2", list_type(PickCandidateInput_$reflection())]], [["Item", int32_type]], [], [["Item", float64_type]], [], [["Item1", string_type], ["Item2", ActionSketch_$reflection()]], [["Item", SketchDrag_$reflection()]], [["Item", LabelPos_$reflection()]], [["Item1", SketchDrag_$reflection()], ["Item2", array_type(float32_type)]], [["Item1", string_type], ["Item2", array_type(float32_type)]], [], [], [["Item1", float64_type], ["Item2", float64_type]], [["Item1", float64_type], ["Item2", float64_type]], [], [["Item", SketchToolKind_$reflection()]], [["Item", ConstraintPlacementKind_$reflection()]], [["Item", GeometricConstraintKind_$reflection()]], [["Item", int32_type]], [["Item", option_type(tuple_type(string_type, LabelPos_$reflection()))]], [], [["Item", string_type]], [["Item", string_type]], [["Item1", string_type], ["Item2", float64_type]], [], [["Item", string_type]], [], [], [["Item", Document$_$reflection()]], [["Item", SerializedModel_$reflection()]], []]);
}

function Editor_trySketchPointPosition(sketch, pointId) {
    return tryPick((_arg) => {
        let matchResult, id_1, x_1, y_1;
        if (_arg.tag === 0) {
            if (_arg.fields[0] === pointId) {
                matchResult = 0;
                id_1 = _arg.fields[0];
                x_1 = _arg.fields[1];
                y_1 = _arg.fields[2];
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
                return new LabelPos(x_1, y_1);
            default:
                return undefined;
        }
    }, sketch.Entities);
}

export function Editor_actionTemplateKind(_arg) {
    switch (_arg.tag) {
        case 1:
            return new ActionKind(1, [5, 20]);
        case 2:
            return new ActionKind(3, [10, 10, 10]);
        case 3:
            return new ActionKind(4, ["Z", 0, false]);
        case 4:
            return new ActionKind(5, [undefined, 0, 0, 0]);
        case 5:
            return new ActionKind(6, [undefined, 0, 0, 1, 0]);
        case 6:
            return new ActionKind(7, [undefined, undefined]);
        case 7:
            return new ActionKind(8, [undefined, undefined, 0]);
        case 8:
            return new ActionKind(9, [undefined, undefined, 0]);
        case 9:
            return new ActionKind(10, [undefined, undefined, 0]);
        case 10:
            return new ActionKind(11, ["origin", new SketchPlane(0, []), ActionSketchModule_empty]);
        case 11:
            return new ActionKind(12, [undefined, false, FromSketchSelectionModule_defaults]);
        case 12:
            return new ActionKind(13, [undefined, 2]);
        case 13:
            return new ActionKind(14, [undefined, 1]);
        case 14:
            return new ActionKind(15, [undefined, 0.2, 96]);
        default:
            return new ActionKind(2, [8]);
    }
}

export function Editor_sketchToolName(_arg) {
    switch (_arg.tag) {
        case 1:
            return "line";
        case 2:
            return "rectangle";
        case 3:
            return "roundedRectangle";
        case 4:
            return "circle";
        case 5:
            return "arc";
        default:
            return "none";
    }
}

export function Editor_constraintPlacementName(_arg) {
    if (_arg.tag === 1) {
        return "angle";
    }
    else {
        return "distance";
    }
}

export function Editor_tryConstraintPlacementKind(_arg) {
    switch (_arg) {
        case "distance":
            return new ConstraintPlacementKind(0, []);
        case "angle":
            return new ConstraintPlacementKind(1, []);
        default:
            return undefined;
    }
}

/**
 * String key the sketch authoring module expects for each geometric
 * constraint kind (must match SketchAuthoring.buildConstraint's match).
 */
export function Editor_geometricConstraintName(_arg) {
    switch (_arg.tag) {
        case 1:
            return "Horizontal";
        case 2:
            return "Vertical";
        case 3:
            return "Midpoint";
        case 4:
            return "Parallel";
        case 5:
            return "Perpendicular";
        case 6:
            return "Equal";
        case 7:
            return "Tangent";
        case 8:
            return "Concentric";
        case 9:
            return "Fixed";
        default:
            return "Coincident";
    }
}

export function Editor_initState() {
    const doc = DocumentModule_defaultDocument();
    const compiled = Pipeline_compile(doc.Actions);
    return new EditorState(doc, compiled, copy(compiled.Slots.Values), empty({
        Compare: comparePrimitives,
    }), Palette_empty, undefined, empty_1(), false, "none", empty_1(), empty_1(), undefined, undefined, undefined, false, undefined, undefined, undefined);
}

export function Editor_isSlotBackedActionParamField(_arg) {
    switch (_arg.tag) {
        case 6:
        case 10:
        case 15:
        case 17:
        case 18:
        case 19:
        case 20:
        case 21:
        case 23:
        case 24:
        case 26:
        case 27:
        case 29:
        case 30:
        case 33:
        case 34:
        case 35:
        case 36:
        case 38:
        case 40:
            return false;
        default:
            return true;
    }
}

export function Editor_setActionParamValue(id, field, value) {
    if (Editor_isSlotBackedActionParamField(field)) {
        return new Message(13, [id, field, value]);
    }
    else {
        return new Message(14, [id, field, value]);
    }
}

export function Editor_setDisplayValue(id, field, value) {
    return new Message(10, [id, field, value]);
}

export function Editor_setFieldSliceValue(id, field, value) {
    return new Message(12, [id, field, value]);
}

export function Editor_sketchPlaneTransform(originFrame, plane) {
    return RigidTransform_op_Multiply_ZFA4D60(originFrame, new RigidTransform((plane.tag === 1) ? QuatModule_fromBasis(new Vec3(1, 0, 0), new Vec3(0, 0, 1), new Vec3(0, -1, 0)) : ((plane.tag === 2) ? QuatModule_fromBasis(new Vec3(0, 1, 0), new Vec3(0, 0, 1), new Vec3(1, 0, 0)) : Quat_get_Identity()), Vec3_get_Zero()));
}

export function Editor_resolveSketchTransform(state, origin, plane) {
    return Editor_sketchPlaneTransform(defaultArg(map((chain) => foldChain(state.Compiled.Slots, state.SlotValues, chain), bind((id) => tryFind(id, state.Compiled.Frames), origin)), RigidTransform_get_Identity()), plane);
}

export function Editor_resolvedFrames(state) {
    return map_1((_arg, chain) => foldChain(state.Compiled.Slots, state.SlotValues, chain), state.Compiled.Frames);
}

/**
 * ID of the sketch currently being edited, if any.
 */
export function Editor_activeSketchEditId(state) {
    const matchValue_1 = state.Doc.SelectedId;
    let matchResult;
    if (state.SketchEditMode) {
        if (matchValue_1 != null) {
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
            const id = matchValue_1;
            return bind((a_1) => {
                if (a_1.Kind.tag === 11) {
                    return id;
                }
                else {
                    return undefined;
                }
            }, tryFind_1((a) => (a.Id === id), state.Doc.Actions));
        }
        default:
            return undefined;
    }
}

/**
 * Ids of frame actions that appear before the active sketch — the
 * frame origins the sketch is allowed to reference.
 */
export function Editor_sketchEditFrameIds(state) {
    const matchValue = Editor_activeSketchEditId(state);
    if (matchValue != null) {
        const sketchId = matchValue;
        return ofList(choose((a_1) => {
            const matchValue_1 = tryFind(a_1.Id, state.Compiled.TypeMap);
            let matchResult;
            if (matchValue_1 != null) {
                if (matchValue_1.tag === 2) {
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
                    return a_1.Id;
                default:
                    return undefined;
            }
        }, take(findIndex((a) => (a.Id === sketchId), state.Doc.Actions), state.Doc.Actions)), {
            Compare: comparePrimitives,
        });
    }
    else {
        return empty_2({
            Compare: comparePrimitives,
        });
    }
}

/**
 * True when `target` belongs to the actively-edited sketch: either its
 * own geometry (point/line/circle/arc/loop/dimension) or a frame origin
 * the sketch is allowed to reference.
 */
export function Editor_belongsToActiveSketch(state, target) {
    const matchValue = Editor_activeSketchEditId(state);
    if (matchValue != null) {
        const sid = matchValue;
        let matchResult, s;
        switch (target.tag) {
            case 0: {
                matchResult = 0;
                s = target.fields[0];
                break;
            }
            case 1: {
                matchResult = 0;
                s = target.fields[0];
                break;
            }
            case 2: {
                matchResult = 0;
                s = target.fields[0];
                break;
            }
            case 3: {
                matchResult = 0;
                s = target.fields[0];
                break;
            }
            case 4: {
                matchResult = 0;
                s = target.fields[0];
                break;
            }
            case 5: {
                matchResult = 0;
                s = target.fields[0];
                break;
            }
            case 6: {
                matchResult = 1;
                break;
            }
            default:
                matchResult = 2;
        }
        switch (matchResult) {
            case 0:
                return s === sid;
            case 1:
                return contains(target.fields[0], Editor_sketchEditFrameIds(state));
            default:
                return false;
        }
    }
    else {
        return false;
    }
}

export function Editor_isValidSelectionTarget(state, target) {
    if (target.tag === 6) {
        return Editor_belongsToActiveSketch(state, target);
    }
    else {
        return exists((pickable) => PickableModule_sameTarget(target, pickable), state.Compiled.Pickables);
    }
}

/**
 * When clicking a target in sketch-edit mode, keep the active sketch
 * selected if the target belongs to it (geometry or allowed frame);
 * otherwise fall back to whichever action the target normally belongs to.
 */
export function Editor_actionSelectionForTarget(state, target, actionId) {
    let sketchId;
    const matchValue = Editor_activeSketchEditId(state);
    let matchResult, sketchId_1;
    if (matchValue != null) {
        if ((sketchId = matchValue, Editor_belongsToActiveSketch(state, target))) {
            matchResult = 0;
            sketchId_1 = matchValue;
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
            return sketchId_1;
        default:
            return actionId;
    }
}

/**
 * Resolve a sketch id to its content + origin transform in the current
 * compiled state. Used as a lookup callback by SketchAuthoring.
 */
export function Editor_trySketchContext(state, sketchId) {
    return bind((action_1) => {
        const matchValue = action_1.Kind;
        if (matchValue.tag === 11) {
            return [matchValue.fields[2], Editor_resolveSketchTransform(state, matchValue.fields[0], matchValue.fields[1])];
        }
        else {
            return undefined;
        }
    }, tryFind_1((action) => (action.Id === sketchId), state.Doc.Actions));
}

export function Editor_formatErrors(errs) {
    return map_2((e) => {
        switch (e.tag) {
            case 1:
                return new ActionErrorView(e.fields[0], e.fields[1], `not found: ${e.fields[2]}`);
            case 2:
                return new ActionErrorView(e.fields[0], e.fields[1], `forward ref: ${e.fields[2]}`);
            case 3:
                return new ActionErrorView(e.fields[0], e.fields[1], `expected ${join("|", map_2(toString, e.fields[2]))}, got ${e.fields[3]}`);
            default:
                return new ActionErrorView(e.fields[0], e.fields[1], "missing");
        }
    }, errs);
}

export function Editor_sketchUiState(state) {
    let position;
    let placementCursor;
    const matchValue = state.ConstraintPlacementCursor;
    const matchValue_1 = state.Doc.SelectedId;
    let matchResult, position_1, selectedId_1, sketchId_1;
    if (matchValue != null) {
        if (matchValue_1 != null) {
            if ((position = matchValue[1], state.SketchEditMode && (matchValue_1 === matchValue[0]))) {
                matchResult = 0;
                position_1 = matchValue[1];
                selectedId_1 = matchValue_1;
                sketchId_1 = matchValue[0];
            }
            else {
                matchResult = 1;
            }
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
            placementCursor = [position_1.X, position_1.Y];
            break;
        }
        default:
            placementCursor = undefined;
    }
    let placementTargets;
    const matchValue_4 = state.HoveredTarget;
    let matchResult_1, hover_1;
    if (state.ConstraintPlacementMode == null) {
        matchResult_1 = 2;
    }
    else if (matchValue_4 != null) {
        if (!contains_1(matchValue_4, state.SelectedTargets, {
            Equals: equals,
            GetHashCode: safeHash,
        })) {
            matchResult_1 = 0;
            hover_1 = matchValue_4;
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
            placementTargets = append(state.SelectedTargets, singleton(hover_1));
            break;
        }
        case 1: {
            placementTargets = state.SelectedTargets;
            break;
        }
        default:
            placementTargets = state.SelectedTargets;
    }
    const baseState = SketchAuthoring_availabilityForSelection(state.Doc, state.SketchEditMode, state.SketchTool, map(Editor_constraintPlacementName, state.ConstraintPlacementMode), placementTargets, placementCursor, state.ConstraintPlacementDraft, state.HoveredTarget);
    const sketchUi = new SketchUiState(baseState.EditMode, baseState.Tool, (baseState.Tool === "none") ? empty_1() : state.SketchToolPoints, state.EditingDimension, baseState.ConstraintPlacementMode, baseState.ConstraintPlacementDraft, baseState.PendingConstraintPlacement, baseState.ConstraintAvailability, baseState.DimensionPlacementAvailability);
    return SketchAuthoring_withResolvedPendingConstraintValue((sketchId_2) => Editor_trySketchContext(state, sketchId_2), Editor_resolvedFrames(state), sketchUi);
}

export function Editor_normalizeState(state) {
    let pos, draft;
    const next = Editor_sketchUiState(state);
    const editingDimension = bind((current) => {
        const matchValue = SketchAuthoring_trySelectedSketch(state.Doc);
        let matchResult, selected_1;
        if (matchValue != null) {
            if (next.EditMode && (matchValue.Action.Id === current.SketchId)) {
                matchResult = 0;
                selected_1 = matchValue;
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
                return SketchAuthoring_tryEditableDimension(current.SketchId, selected_1.Sketch, current.ConstraintIndex);
            default:
                return undefined;
        }
    }, state.EditingDimension);
    let constraintPlacementCursor;
    const matchValue_1 = bind(Editor_tryConstraintPlacementKind, next.ConstraintPlacementMode);
    const matchValue_2 = state.ConstraintPlacementCursor;
    const matchValue_3 = state.Doc.SelectedId;
    let matchResult_1, pos_1, selectedId_1, sketchId_1;
    if (matchValue_1 != null) {
        if (matchValue_2 != null) {
            if (matchValue_3 != null) {
                if ((pos = matchValue_2[1], (next.EditMode && (next.Tool === "none")) && (matchValue_3 === matchValue_2[0]))) {
                    matchResult_1 = 0;
                    pos_1 = matchValue_2[1];
                    selectedId_1 = matchValue_3;
                    sketchId_1 = matchValue_2[0];
                }
                else {
                    matchResult_1 = 1;
                }
            }
            else {
                matchResult_1 = 1;
            }
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
            constraintPlacementCursor = [sketchId_1, pos_1];
            break;
        }
        default:
            constraintPlacementCursor = undefined;
    }
    let constraintPlacementDraft;
    const matchValue_5 = bind(Editor_tryConstraintPlacementKind, next.ConstraintPlacementMode);
    const matchValue_6 = state.ConstraintPlacementDraft;
    const matchValue_7 = state.Doc.SelectedId;
    let matchResult_2, draft_1, kind_1, selectedId_3;
    if (matchValue_5 != null) {
        if (matchValue_6 != null) {
            if (matchValue_7 != null) {
                if ((draft = matchValue_6, ((next.EditMode && (next.Tool === "none")) && (draft.SketchId === matchValue_7)) && (draft.Kind === Editor_constraintPlacementName(matchValue_5)))) {
                    matchResult_2 = 0;
                    draft_1 = matchValue_6;
                    kind_1 = matchValue_5;
                    selectedId_3 = matchValue_7;
                }
                else {
                    matchResult_2 = 1;
                }
            }
            else {
                matchResult_2 = 1;
            }
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
            constraintPlacementDraft = draft_1;
            break;
        }
        default:
            constraintPlacementDraft = undefined;
    }
    let activeSketchDrag;
    const matchValue_9 = state.ActiveSketchDrag;
    const matchValue_10 = state.Doc.SelectedId;
    let matchResult_3, drag_1, selectedId_5;
    if (matchValue_9 != null) {
        if (matchValue_10 != null) {
            if (next.EditMode && (matchValue_10 === matchValue_9.SketchId)) {
                matchResult_3 = 0;
                drag_1 = matchValue_9;
                selectedId_5 = matchValue_10;
            }
            else {
                matchResult_3 = 1;
            }
        }
        else {
            matchResult_3 = 1;
        }
    }
    else {
        matchResult_3 = 1;
    }
    switch (matchResult_3) {
        case 0: {
            activeSketchDrag = drag_1;
            break;
        }
        default:
            activeSketchDrag = undefined;
    }
    return new EditorState(state.Doc, state.Compiled, state.SlotValues, state.SolvedSketchParams, state.PaletteSession, state.HoveredTarget, state.SelectedTargets, next.EditMode, next.Tool, (next.Tool === "none") ? empty_1() : state.SketchToolPoints, (next.Tool === "none") ? empty_1() : state.SketchToolPointRefs, (next.Tool === "line") ? state.LineChainStartPointId : undefined, editingDimension, activeSketchDrag, (activeSketchDrag != null) && state.PendingSketchDragCommit, bind(Editor_tryConstraintPlacementKind, next.ConstraintPlacementMode), constraintPlacementDraft, constraintPlacementCursor);
}

export function Editor_recompileState(state) {
    let state_1, state_2;
    const compiled = Pipeline_compile(state.Doc.Actions);
    return Editor_normalizeState(new EditorState(state.Doc, compiled, copy(compiled.Slots.Values), empty({
        Compare: comparePrimitives,
    }), state.PaletteSession, filter((state_1 = (new EditorState(state.Doc, compiled, state.SlotValues, state.SolvedSketchParams, state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor)), (target) => Editor_isValidSelectionTarget(state_1, target)), state.HoveredTarget), filter_1((state_2 = (new EditorState(state.Doc, compiled, state.SlotValues, state.SolvedSketchParams, state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor)), (target_1) => Editor_isValidSelectionTarget(state_2, target_1)), state.SelectedTargets), state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor));
}

function Editor_patchSlotValues(slotValues, compiled, updates) {
    return SlotTableModule_patchedValues(slotValues, map_2((tupledArg) => {
        const slotRef = tupledArg[0];
        const matchValue = SlotTableModule_tryFindSlot(compiled.Slots, slotRef);
        if (matchValue == null) {
            return toFail(printf("Missing slot for %s/%s"))(slotRef.ActionId)(slotRef.Path);
        }
        else {
            return [matchValue, tupledArg[1]];
        }
    }, updates));
}

function Editor_floatValueForSlotField(field, value) {
    if (field.tag === 42) {
        return map((value_1) => value_1, ParamValueModule_asInt(value));
    }
    else {
        return ParamValueModule_asFloat(value);
    }
}

function Editor_patchActionSlotValues(state, actionId, field, value) {
    if (!Editor_isSlotBackedActionParamField(field)) {
        toFail(printf("Expected slot-backed action field, got %A"))(field);
    }
    const matchValue = Editor_floatValueForSlotField(field, value);
    if (matchValue == null) {
        return toFail(printf("Expected numeric slot value for %A, got %A"))(field)(value);
    }
    else {
        const number = matchValue;
        return Editor_patchSlotValues(state.SlotValues, state.Compiled, singleton([new SlotRef(actionId, DocumentModule_pathOfParamField(field)), number]));
    }
}

function Editor_patchDisplaySlotValues(state, actionId, field, value) {
    let matchValue_1, number, matchValue, color, color_1, color_2, arg;
    return Editor_patchSlotValues(state.SlotValues, state.Compiled, (field.tag === 1) ? ((matchValue_1 = ParamValueModule_asFloat(value), (matchValue_1 == null) ? toFail(printf("Expected numeric display value for %A, got %A"))(field)(value) : ((number = matchValue_1, map_2((path_1) => [new SlotRef(actionId, path_1), number], DocumentModule_pathOfDisplayField(field)))))) : ((field.tag === 2) ? ((matchValue_1 = ParamValueModule_asFloat(value), (matchValue_1 == null) ? toFail(printf("Expected numeric display value for %A, got %A"))(field)(value) : ((number = matchValue_1, map_2((path_1) => [new SlotRef(actionId, path_1), number], DocumentModule_pathOfDisplayField(field)))))) : ((matchValue = ParamValueModule_asFloatArray(value), (matchValue == null) ? toFail(printf("Expected display color array, got %A"))(value) : (((color = matchValue, color.length === 3)) ? ((color_1 = matchValue, map_2((tupledArg) => [new SlotRef(actionId, tupledArg[0]), tupledArg[1]], zip(DocumentModule_pathOfDisplayField(field), ofArray(color_1))))) : ((color_2 = matchValue, (arg = (color_2.length | 0), toFail(printf("Expected RGB display color, got %d components"))(arg)))))))));
}

function Editor_patchFieldSliceSlotValues(state, actionId, field, value) {
    let updates;
    if (field.tag === 1) {
        const matchValue = ParamValueModule_asFloat(value);
        if (matchValue == null) {
            updates = toFail(printf("Expected numeric field-slice value for %A, got %A"))(field)(value);
        }
        else {
            const number = matchValue;
            updates = map_2((path) => [new SlotRef(actionId, path), number], DocumentModule_pathOfFieldSliceField(field));
        }
    }
    else {
        updates = empty_1();
    }
    if (isEmpty(updates)) {
        return state.SlotValues;
    }
    else {
        return Editor_patchSlotValues(state.SlotValues, state.Compiled, updates);
    }
}

/**
 * Clear only the "in-progress placement" scratch state — dimension
 * being edited and the constraint-placement draft/cursor. Used when
 * cancelling or committing a single widget while leaving tool and mode
 * selection alone.
 */
export function Editor_clearDrafts(state) {
    return new EditorState(state.Doc, state.Compiled, state.SlotValues, empty({
        Compare: comparePrimitives,
    }), state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, undefined, undefined, false, state.ConstraintPlacementMode, undefined, undefined);
}

/**
 * Clear tool-related transient state (tool points, pending edits,
 * placement mode/draft/cursor) while preserving selection and hover.
 * Used when switching tool or placement mode.
 */
export function Editor_clearToolState(state) {
    return new EditorState(state.Doc, state.Compiled, state.SlotValues, empty({
        Compare: comparePrimitives,
    }), state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, empty_1(), empty_1(), undefined, undefined, undefined, false, undefined, undefined, undefined);
}

/**
 * Full reset of transient UI state after a committing action (add
 * constraint, delete, replace sketch, etc.). Leaves SketchEditMode
 * and SketchTool intact; everything else goes back to idle.
 */
export function Editor_clearTransient(state) {
    return new EditorState(state.Doc, state.Compiled, state.SlotValues, empty({
        Compare: comparePrimitives,
    }), state.PaletteSession, undefined, empty_1(), state.SketchEditMode, state.SketchTool, empty_1(), empty_1(), undefined, undefined, undefined, false, undefined, undefined, undefined);
}

/**
 * Wholesale document replacement with a full UI reset. Used by
 * LoadModel and ClearModel.
 */
export function Editor_loadDoc(doc, state) {
    return Editor_recompileState(new EditorState(doc, state.Compiled, state.SlotValues, empty({
        Compare: comparePrimitives,
    }), Palette_empty, undefined, empty_1(), false, "none", empty_1(), empty_1(), undefined, undefined, undefined, false, undefined, undefined, undefined));
}

export function Editor_applySelectionIntent(intent, target, current) {
    if (intent === "toggle") {
        if (exists((y) => equals(target, y), current)) {
            return filter_1((y_1) => !equals(target, y_1), current);
        }
        else {
            return cons(target, current);
        }
    }
    else {
        return singleton(target);
    }
}

export function Editor_reduceSelectionCandidates(state, pickCandidates) {
    const pickableById = ofList_1(map_2((p) => [PickableModule_pickId(p), p], state.Compiled.Pickables), {
        Compare: comparePrimitives,
    });
    return tryHead(sortBy((tupledArg_1) => [PickableModule_selectionPriority(tupledArg_1[0]), tupledArg_1[1]], choose((candidate) => filter((tupledArg) => Editor_isValidSelectionTarget(state, tupledArg[0]), map((pickable) => [PickableModule_selectionTarget(pickable), candidate.Score, PickableModule_targetAction(pickable)], tryFind(candidate.PickId, pickableById))), pickCandidates), {
        Compare: compareArrays,
    }));
}

export function Editor_applyDeleteIntent(state) {
    let ctx;
    if (state.SketchEditMode) {
        const matchValue = SketchAuthoring_trySelectedSketch(state.Doc);
        let matchResult, ctx_1;
        if (matchValue != null) {
            if ((ctx = matchValue, !isEmpty(state.SelectedTargets))) {
                matchResult = 0;
                ctx_1 = matchValue;
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
                return Editor_recompileState(Editor_clearTransient(new EditorState(SketchAuthoring_withUpdatedSketch(state.Doc, ctx_1.Action.Id, SketchAuthoring_deleteTargets(state.SelectedTargets, ctx_1.Sketch)), state.Compiled, state.SlotValues, state.SolvedSketchParams, state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor)));
            default:
                return state;
        }
    }
    else {
        const matchValue_1 = state.Doc.SelectedId;
        let matchResult_1, id_1;
        if (matchValue_1 != null) {
            if (matchValue_1 !== "origin") {
                matchResult_1 = 0;
                id_1 = matchValue_1;
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
                return Editor_recompileState(Editor_clearTransient(new EditorState(DocumentModule_removeAction(id_1, state.Doc), state.Compiled, state.SlotValues, state.SolvedSketchParams, state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor)));
            default:
                return state;
        }
    }
}

export function Editor_paletteMaybeBuild(idSuffix, state) {
    if (Palette_toState(state.PaletteSession, state.Compiled.TypeMap, state.Doc).Mode === "done") {
        const matchValue = Palette_buildAction(state.PaletteSession, idSuffix);
        if (matchValue == null) {
            return new EditorState(state.Doc, state.Compiled, state.SlotValues, state.SolvedSketchParams, Palette_empty, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor);
        }
        else {
            return Editor_recompileState(new EditorState(DocumentModule_addAction(matchValue, state.Doc), state.Compiled, state.SlotValues, state.SolvedSketchParams, Palette_empty, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor));
        }
    }
    else {
        return state;
    }
}

export function Editor_serializedModel(state) {
    return new SerializedModel(state.Doc.Name, state.Doc.Actions);
}

export const Editor_noEffects = empty_1();

function Editor_isLabelDrag(_arg) {
    if (_arg.Kind.tag === 1) {
        return true;
    }
    else {
        return false;
    }
}

function Editor_patchDragTargetSlotValues(state, drag, target) {
    const patched = copy(state.SlotValues);
    const tryPatch = (field, number) => {
        const matchValue = SlotTableModule_tryFindSlot(state.Compiled.Slots, new SlotRef(drag.SketchId, DocumentModule_pathOfParamField(field)));
        let matchResult, slot_1;
        if (matchValue != null) {
            if (matchValue < patched.length) {
                matchResult = 0;
                slot_1 = matchValue;
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
                setItem(patched, slot_1, number);
                break;
            }
            case 1: {
                break;
            }
        }
    };
    tryPatch(drag.XField, target.X);
    tryPatch(drag.YField, target.Y);
    return patched;
}

export function Editor_update(message, state) {
    let bind$0040, bind$0040_1, value_2, key_1, id_8, value_3, key_2, id_10, value_4, key_3, id_11, intent, matchValue_5, target_2, actionId, _score_1, SelectedTargets, matchValue_6, editing, matchValue_7, selected_1, bind$0040_2, value_6, matchValue_8, current, field_2, matchValue_9, bind$0040_3, matchValue_10, matchValue_11, selected_2, kind, kind_1, selected_3, actionId_1, matchValue_13, y_3, x_3, matchValue_14, selected_4, selected_5, clickedPointRef, matchValue_15, pointId, pointId_1, sketchId_2, nextPoints, value_7, nextPointRefs, matchValue_16, result, nextState, bind$0040_4, matchValue_18, nextPointId, matchValue_20, matchValue_21, nextDoc, bind$0040_5, bind$0040_6, kind_2, nextMode, matchValue_22, active_5, bind$0040_7, matchValue_23, matchValue_24, ctx, id_14, model;
    switch (message.tag) {
        case 23: {
            const drag = message.fields[0];
            if (Editor_isLabelDrag(drag)) {
                return [(bind$0040 = Editor_clearDrafts(state), new EditorState(DocumentModule_patchParamValue(drag.SketchId, drag.YField, new ParamValue(3, [drag.Target.Y]), DocumentModule_patchParamValue(drag.SketchId, drag.XField, new ParamValue(3, [drag.Target.X]), state.Doc)), bind$0040.Compiled, Editor_patchDragTargetSlotValues(state, drag, drag.Target), bind$0040.SolvedSketchParams, bind$0040.PaletteSession, bind$0040.HoveredTarget, bind$0040.SelectedTargets, bind$0040.SketchEditMode, bind$0040.SketchTool, bind$0040.SketchToolPoints, bind$0040.SketchToolPointRefs, bind$0040.LineChainStartPointId, bind$0040.EditingDimension, drag, false, bind$0040.ConstraintPlacementMode, bind$0040.ConstraintPlacementDraft, bind$0040.ConstraintPlacementCursor)), Editor_noEffects];
            }
            else {
                return [(bind$0040_1 = Editor_clearDrafts(state), new EditorState(bind$0040_1.Doc, bind$0040_1.Compiled, bind$0040_1.SlotValues, bind$0040_1.SolvedSketchParams, bind$0040_1.PaletteSession, bind$0040_1.HoveredTarget, bind$0040_1.SelectedTargets, bind$0040_1.SketchEditMode, bind$0040_1.SketchTool, bind$0040_1.SketchToolPoints, bind$0040_1.SketchToolPointRefs, bind$0040_1.LineChainStartPointId, bind$0040_1.EditingDimension, drag, false, bind$0040_1.ConstraintPlacementMode, bind$0040_1.ConstraintPlacementDraft, bind$0040_1.ConstraintPlacementCursor)), singleton(new Effect(0, [drag]))];
            }
        }
        case 24: {
            const target = message.fields[0];
            const matchValue = state.ActiveSketchDrag;
            if (matchValue == null) {
                return [state, Editor_noEffects];
            }
            else {
                const drag_1 = matchValue;
                const nextDrag = new SketchDrag(drag_1.SketchId, drag_1.Kind, drag_1.XField, drag_1.YField, target);
                if (Editor_isLabelDrag(drag_1)) {
                    return [new EditorState(DocumentModule_patchParamValue(drag_1.SketchId, drag_1.YField, new ParamValue(3, [target.Y]), DocumentModule_patchParamValue(drag_1.SketchId, drag_1.XField, new ParamValue(3, [target.X]), state.Doc)), state.Compiled, Editor_patchDragTargetSlotValues(state, drag_1, target), state.SolvedSketchParams, state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, nextDrag, false, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor), Editor_noEffects];
                }
                else {
                    return [new EditorState(state.Doc, state.Compiled, state.SlotValues, state.SolvedSketchParams, state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, nextDrag, false, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor), singleton(new Effect(0, [nextDrag]))];
                }
            }
        }
        case 25: {
            const solvedLocal = message.fields[1];
            const drag_2 = message.fields[0];
            const matchValue_1 = state.ActiveSketchDrag;
            let matchResult, active_1;
            if (matchValue_1 != null) {
                if (equals(matchValue_1, drag_2)) {
                    matchResult = 0;
                    active_1 = matchValue_1;
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
                    if (state.PendingSketchDragCommit) {
                        return [Editor_recompileState(new EditorState(SketchSolve_commitSolvedSketch(drag_2.SketchId, solvedLocal, state.Doc), state.Compiled, state.SlotValues, empty({
                            Compare: comparePrimitives,
                        }), state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, undefined, false, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor)), Editor_noEffects];
                    }
                    else {
                        return [new EditorState(state.Doc, state.Compiled, state.SlotValues, add(drag_2.SketchId, solvedLocal, state.SolvedSketchParams), state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor), Editor_noEffects];
                    }
                default:
                    return [state, Editor_noEffects];
            }
        }
        case 26: {
            const solvedLocal_1 = message.fields[1];
            const sketchId = message.fields[0];
            const matchValue_2 = state.ActiveSketchDrag;
            let matchResult_1, active_3;
            if (matchValue_2 != null) {
                if (matchValue_2.SketchId === sketchId) {
                    matchResult_1 = 0;
                    active_3 = matchValue_2;
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
                    return [state, Editor_noEffects];
                default: {
                    const matchValue_3 = tryFind_1((action) => (action.Id === sketchId), state.Doc.Actions);
                    let matchResult_2, sketch;
                    if (matchValue_3 != null) {
                        if (matchValue_3.Kind.tag === 11) {
                            matchResult_2 = 0;
                            sketch = matchValue_3.Kind.fields[2];
                        }
                        else {
                            matchResult_2 = 1;
                        }
                    }
                    else {
                        matchResult_2 = 1;
                    }
                    switch (matchResult_2) {
                        case 0:
                            return [new EditorState(state.Doc, state.Compiled, SketchSolve_patchSolvedSketchSlots(state.SlotValues, state.Compiled.Slots, sketchId, sketch, solvedLocal_1), add(sketchId, solvedLocal_1, state.SolvedSketchParams), state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor), Editor_noEffects];
                        default:
                            return [state, Editor_noEffects];
                    }
                }
            }
        }
        case 27: {
            const matchValue_4 = state.ActiveSketchDrag;
            if (matchValue_4 == null) {
                return [new EditorState(state.Doc, state.Compiled, state.SlotValues, empty({
                    Compare: comparePrimitives,
                }), state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, false, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor), Editor_noEffects];
            }
            else {
                const drag_3 = matchValue_4;
                if (Editor_isLabelDrag(drag_3)) {
                    return [new EditorState(state.Doc, state.Compiled, state.SlotValues, state.SolvedSketchParams, state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, undefined, false, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor), Editor_noEffects];
                }
                else {
                    return [new EditorState(state.Doc, state.Compiled, state.SlotValues, state.SolvedSketchParams, state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, true, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor), singleton(new Effect(1, [drag_3]))];
                }
            }
        }
        case 28:
            return [new EditorState(state.Doc, state.Compiled, state.SlotValues, empty({
                Compare: comparePrimitives,
            }), state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, undefined, false, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor), Editor_noEffects];
        default:
            return [(message.tag === 1) ? (new EditorState(state.Doc, state.Compiled, state.SlotValues, state.SolvedSketchParams, state.PaletteSession, message.fields[0], state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor)) : ((message.tag === 2) ? (new EditorState(state.Doc, state.Compiled, state.SlotValues, state.SolvedSketchParams, state.PaletteSession, state.HoveredTarget, message.fields[0], state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor)) : ((message.tag === 3) ? Editor_recompileState(new EditorState(DocumentModule_addAction(new DocAction(message.fields[1], undefined, Editor_actionTemplateKind(message.fields[0]), true, undefined, undefined), state.Doc), state.Compiled, state.SlotValues, state.SolvedSketchParams, state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor)) : ((message.tag === 4) ? Editor_recompileState(new EditorState(DocumentModule_addAction(message.fields[0], state.Doc), state.Compiled, state.SlotValues, state.SolvedSketchParams, state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor)) : ((message.tag === 5) ? Editor_recompileState(new EditorState(DocumentModule_updateAction(message.fields[0], message.fields[1], state.Doc), state.Compiled, state.SlotValues, state.SolvedSketchParams, state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor)) : ((message.tag === 6) ? Editor_recompileState(new EditorState(DocumentModule_removeAction(message.fields[0], state.Doc), state.Compiled, state.SlotValues, state.SolvedSketchParams, state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor)) : ((message.tag === 7) ? Editor_recompileState(new EditorState(DocumentModule_reorder(message.fields[0], state.Doc), state.Compiled, state.SlotValues, state.SolvedSketchParams, state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor)) : ((message.tag === 8) ? Editor_normalizeState(new EditorState(DocumentModule_toggleVisible(message.fields[0], state.Doc), state.Compiled, state.SlotValues, state.SolvedSketchParams, state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor)) : ((message.tag === 9) ? Editor_normalizeState(new EditorState(DocumentModule_toggleDisplay(message.fields[0], state.Doc), state.Compiled, state.SlotValues, state.SolvedSketchParams, state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor)) : ((message.tag === 10) ? ((value_2 = message.fields[2], (key_1 = message.fields[1], (id_8 = message.fields[0], Editor_normalizeState(new EditorState(DocumentModule_patchDisplayValue(id_8, key_1, value_2, state.Doc), state.Compiled, Editor_patchDisplaySlotValues(state, id_8, key_1, value_2), state.SolvedSketchParams, state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor)))))) : ((message.tag === 11) ? Editor_normalizeState(new EditorState(DocumentModule_toggleFieldSlice(message.fields[0], state.Doc), state.Compiled, state.SlotValues, state.SolvedSketchParams, state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor)) : ((message.tag === 12) ? ((value_3 = message.fields[2], (key_2 = message.fields[1], (id_10 = message.fields[0], Editor_normalizeState(new EditorState(DocumentModule_patchFieldSliceValue(id_10, key_2, value_3, state.Doc), state.Compiled, Editor_patchFieldSliceSlotValues(state, id_10, key_2, value_3), state.SolvedSketchParams, state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor)))))) : ((message.tag === 13) ? ((value_4 = message.fields[2], (key_3 = message.fields[1], (id_11 = message.fields[0], Editor_normalizeState(new EditorState(DocumentModule_patchParamValue(id_11, key_3, value_4, state.Doc), state.Compiled, Editor_patchActionSlotValues(state, id_11, key_3, value_4), state.SolvedSketchParams, state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor)))))) : ((message.tag === 14) ? Editor_recompileState(new EditorState(DocumentModule_patchParamValue(message.fields[0], message.fields[1], message.fields[2], state.Doc), state.Compiled, state.SlotValues, state.SolvedSketchParams, state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor)) : ((message.tag === 15) ? Editor_applyDeleteIntent(state) : ((message.tag === 16) ? Editor_normalizeState(new EditorState(state.Doc, state.Compiled, state.SlotValues, state.SolvedSketchParams, state.PaletteSession, map((tupledArg) => tupledArg[0], Editor_reduceSelectionCandidates(state, message.fields[0])), state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor)) : ((message.tag === 17) ? ((intent = message.fields[0], (matchValue_5 = Editor_reduceSelectionCandidates(state, message.fields[1]), (matchValue_5 == null) ? Editor_normalizeState(new EditorState(state.Doc, state.Compiled, state.SlotValues, state.SolvedSketchParams, state.PaletteSession, undefined, (intent === "replace") ? empty_1() : state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor)) : ((target_2 = matchValue_5[0], (actionId = matchValue_5[2], (_score_1 = matchValue_5[1], Editor_normalizeState((SelectedTargets = Editor_applySelectionIntent(intent, target_2, state.SelectedTargets), new EditorState((matchValue_6 = Editor_actionSelectionForTarget(state, target_2, actionId), (matchValue_6 == null) ? state.Doc : DocumentModule_select(matchValue_6, state.Doc)), state.Compiled, state.SlotValues, state.SolvedSketchParams, state.PaletteSession, target_2, SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor)))))))))) : ((message.tag === 18) ? ((editing = ((matchValue_7 = SketchAuthoring_trySelectedSketch(state.Doc), (matchValue_7 != null) ? ((state.SketchEditMode && equals(state.Doc.SelectedId, matchValue_7.Action.Id)) ? ((selected_1 = matchValue_7, SketchAuthoring_tryEditableDimension(selected_1.Action.Id, selected_1.Sketch, message.fields[0]))) : undefined) : undefined)), Editor_normalizeState((bind$0040_2 = Editor_clearToolState(state), new EditorState(bind$0040_2.Doc, bind$0040_2.Compiled, bind$0040_2.SlotValues, bind$0040_2.SolvedSketchParams, bind$0040_2.PaletteSession, bind$0040_2.HoveredTarget, bind$0040_2.SelectedTargets, bind$0040_2.SketchEditMode, "none", bind$0040_2.SketchToolPoints, bind$0040_2.SketchToolPointRefs, bind$0040_2.LineChainStartPointId, editing, bind$0040_2.ActiveSketchDrag, bind$0040_2.PendingSketchDragCommit, bind$0040_2.ConstraintPlacementMode, bind$0040_2.ConstraintPlacementDraft, bind$0040_2.ConstraintPlacementCursor))))) : ((message.tag === 19) ? Editor_normalizeState(Editor_clearDrafts(state)) : ((message.tag === 20) ? ((value_6 = message.fields[0], (matchValue_8 = state.EditingDimension, (matchValue_8 == null) ? state : ((current = matchValue_8, (field_2 = ((matchValue_9 = current.Key, (matchValue_9 === "distance") ? (new SketchConstraintField(2, [])) : ((matchValue_9 === "diameter") ? (new SketchConstraintField(3, [])) : ((matchValue_9 === "angle") ? (new SketchConstraintField(4, [])) : toFail(printf("Unsupported editable dimension key: %s"))(matchValue_9))))), Editor_normalizeState((bind$0040_3 = Editor_clearDrafts(state), new EditorState(DocumentModule_patchParamValue(current.SketchId, new ActionParamField(32, [current.ConstraintIndex, field_2]), new ParamValue(3, [value_6]), state.Doc), bind$0040_3.Compiled, Editor_patchActionSlotValues(Editor_clearDrafts(state), current.SketchId, new ActionParamField(32, [current.ConstraintIndex, field_2]), new ParamValue(3, [value_6])), bind$0040_3.SolvedSketchParams, bind$0040_3.PaletteSession, bind$0040_3.HoveredTarget, bind$0040_3.SelectedTargets, bind$0040_3.SketchEditMode, bind$0040_3.SketchTool, bind$0040_3.SketchToolPoints, bind$0040_3.SketchToolPointRefs, bind$0040_3.LineChainStartPointId, bind$0040_3.EditingDimension, bind$0040_3.ActiveSketchDrag, bind$0040_3.PendingSketchDragCommit, bind$0040_3.ConstraintPlacementMode, bind$0040_3.ConstraintPlacementDraft, bind$0040_3.ConstraintPlacementCursor))))))))) : ((message.tag === 21) ? ((matchValue_10 = state.ConstraintPlacementMode, (matchValue_11 = SketchAuthoring_trySelectedSketch(state.Doc), (matchValue_10 != null) ? ((matchValue_11 != null) ? (((selected_2 = matchValue_11, (kind = matchValue_10, state.SketchEditMode))) ? ((kind_1 = matchValue_10, (selected_3 = matchValue_11, Editor_normalizeState(new EditorState(state.Doc, state.Compiled, state.SlotValues, state.SolvedSketchParams, state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, SketchAuthoring_updatePlacementDraft(selected_3.Action.Id, Editor_constraintPlacementName(kind_1), state.HoveredTarget, state.ConstraintPlacementDraft), state.ConstraintPlacementCursor))))) : state) : state) : state))) : ((message.tag === 22) ? ((actionId_1 = message.fields[0], (matchValue_13 = tryFind_1((action_4) => (action_4.Id === actionId_1), state.Doc.Actions), (matchValue_13 != null) ? ((matchValue_13.Kind.tag === 11) ? Editor_recompileState(Editor_clearTransient(new EditorState(SketchAuthoring_withUpdatedSketch(state.Doc, actionId_1, message.fields[1]), state.Compiled, state.SlotValues, state.SolvedSketchParams, state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor))) : state) : state))) : ((message.tag === 23) ? state : ((message.tag === 24) ? state : ((message.tag === 25) ? state : ((message.tag === 26) ? state : ((message.tag === 27) ? state : ((message.tag === 28) ? state : ((message.tag === 29) ? ((y_3 = message.fields[1], (x_3 = message.fields[0], (matchValue_14 = SketchAuthoring_trySelectedSketch(state.Doc), (matchValue_14 != null) ? (((selected_4 = matchValue_14, state.SketchEditMode && (state.SketchTool !== "none"))) ? ((selected_5 = matchValue_14, (clickedPointRef = ((matchValue_15 = state.HoveredTarget, (matchValue_15 != null) ? ((matchValue_15.tag === 0) ? (((pointId = matchValue_15.fields[1], matchValue_15.fields[0] === selected_5.Action.Id)) ? ((pointId_1 = matchValue_15.fields[1], (sketchId_2 = matchValue_15.fields[0], pointId_1))) : undefined) : undefined) : undefined)), (nextPoints = append(state.SketchToolPoints, singleton((clickedPointRef == null) ? (new LabelPos(x_3, y_3)) : ((value_7 = (new LabelPos(x_3, y_3)), defaultArg(Editor_trySketchPointPosition(selected_5.Sketch, clickedPointRef), value_7))))), (nextPointRefs = append(state.SketchToolPointRefs, singleton(clickedPointRef)), (length(nextPoints) >= SketchAuthoring_requiredToolPoints(state.SketchTool)) ? ((matchValue_16 = SketchAuthoring_applyToolClick(state.SketchTool, nextPoints, nextPointRefs, selected_5.Sketch, state.LineChainStartPointId), (matchValue_16 == null) ? state : ((result = matchValue_16, (nextState = Editor_recompileState((bind$0040_4 = Editor_clearTransient(state), new EditorState(SketchAuthoring_withUpdatedSketch(state.Doc, selected_5.Action.Id, result.Sketch), bind$0040_4.Compiled, bind$0040_4.SlotValues, bind$0040_4.SolvedSketchParams, bind$0040_4.PaletteSession, bind$0040_4.HoveredTarget, bind$0040_4.SelectedTargets, bind$0040_4.SketchEditMode, bind$0040_4.SketchTool, bind$0040_4.SketchToolPoints, bind$0040_4.SketchToolPointRefs, bind$0040_4.LineChainStartPointId, bind$0040_4.EditingDimension, bind$0040_4.ActiveSketchDrag, bind$0040_4.PendingSketchDragCommit, state.ConstraintPlacementMode, bind$0040_4.ConstraintPlacementDraft, bind$0040_4.ConstraintPlacementCursor))), (matchValue_18 = result.ContinueFrom, (state.SketchTool === "line") ? ((matchValue_18 != null) ? ((nextPointId = matchValue_18[0], new EditorState(nextState.Doc, nextState.Compiled, nextState.SlotValues, nextState.SolvedSketchParams, nextState.PaletteSession, nextState.HoveredTarget, nextState.SelectedTargets, nextState.SketchEditMode, "line", singleton(matchValue_18[1]), singleton(nextPointId), nextPointId, nextState.EditingDimension, nextState.ActiveSketchDrag, nextState.PendingSketchDragCommit, nextState.ConstraintPlacementMode, nextState.ConstraintPlacementDraft, nextState.ConstraintPlacementCursor))) : nextState) : nextState)))))) : (new EditorState(state.Doc, state.Compiled, state.SlotValues, state.SolvedSketchParams, state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, nextPoints, nextPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor))))))) : state) : state)))) : ((message.tag === 30) ? ((matchValue_20 = Editor_sketchUiState(state).PendingConstraintPlacement, (matchValue_20 == null) ? state : ((matchValue_21 = SketchAuthoring_placePendingConstraint(state.Doc, matchValue_20, new LabelPos(message.fields[0], message.fields[1])), (matchValue_21 == null) ? state : ((nextDoc = matchValue_21, Editor_recompileState((bind$0040_5 = Editor_clearTransient(state), new EditorState(nextDoc, bind$0040_5.Compiled, bind$0040_5.SlotValues, bind$0040_5.SolvedSketchParams, bind$0040_5.PaletteSession, bind$0040_5.HoveredTarget, bind$0040_5.SelectedTargets, bind$0040_5.SketchEditMode, bind$0040_5.SketchTool, bind$0040_5.SketchToolPoints, bind$0040_5.SketchToolPointRefs, bind$0040_5.LineChainStartPointId, bind$0040_5.EditingDimension, bind$0040_5.ActiveSketchDrag, bind$0040_5.PendingSketchDragCommit, bind$0040_5.ConstraintPlacementMode, bind$0040_5.ConstraintPlacementDraft, bind$0040_5.ConstraintPlacementCursor))))))))) : ((message.tag === 31) ? Editor_normalizeState(new EditorState(state.Doc, state.Compiled, state.SlotValues, state.SolvedSketchParams, state.PaletteSession, state.HoveredTarget, state.SelectedTargets, !state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor)) : ((message.tag === 32) ? Editor_normalizeState((bind$0040_6 = Editor_clearToolState(state), new EditorState(bind$0040_6.Doc, bind$0040_6.Compiled, bind$0040_6.SlotValues, bind$0040_6.SolvedSketchParams, bind$0040_6.PaletteSession, bind$0040_6.HoveredTarget, bind$0040_6.SelectedTargets, true, Editor_sketchToolName(message.fields[0]), bind$0040_6.SketchToolPoints, bind$0040_6.SketchToolPointRefs, bind$0040_6.LineChainStartPointId, bind$0040_6.EditingDimension, bind$0040_6.ActiveSketchDrag, bind$0040_6.PendingSketchDragCommit, bind$0040_6.ConstraintPlacementMode, bind$0040_6.ConstraintPlacementDraft, bind$0040_6.ConstraintPlacementCursor))) : ((message.tag === 33) ? ((kind_2 = message.fields[0], (nextMode = ((matchValue_22 = state.ConstraintPlacementMode, (matchValue_22 != null) ? (equals(matchValue_22, kind_2) ? ((active_5 = matchValue_22, undefined)) : kind_2) : kind_2)), Editor_normalizeState((bind$0040_7 = Editor_clearToolState(state), new EditorState(bind$0040_7.Doc, bind$0040_7.Compiled, bind$0040_7.SlotValues, bind$0040_7.SolvedSketchParams, bind$0040_7.PaletteSession, bind$0040_7.HoveredTarget, bind$0040_7.SelectedTargets, true, "none", bind$0040_7.SketchToolPoints, bind$0040_7.SketchToolPointRefs, bind$0040_7.LineChainStartPointId, bind$0040_7.EditingDimension, bind$0040_7.ActiveSketchDrag, bind$0040_7.PendingSketchDragCommit, nextMode, bind$0040_7.ConstraintPlacementDraft, bind$0040_7.ConstraintPlacementCursor)))))) : ((message.tag === 34) ? ((matchValue_23 = SketchAuthoring_addConstraintFromSelection(state.Doc, state.SelectedTargets, Editor_geometricConstraintName(message.fields[0])), (matchValue_23 == null) ? state : Editor_recompileState(Editor_clearTransient(new EditorState(matchValue_23, state.Compiled, state.SlotValues, state.SolvedSketchParams, state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor))))) : ((message.tag === 35) ? ((matchValue_24 = SketchAuthoring_trySelectedSketch(state.Doc), (matchValue_24 == null) ? state : ((ctx = matchValue_24, Editor_recompileState(Editor_clearTransient(new EditorState(SketchAuthoring_withUpdatedSketch(state.Doc, ctx.Action.Id, SketchAuthoring_removeConstraintAt(message.fields[0], ctx.Sketch)), state.Compiled, state.SlotValues, state.SolvedSketchParams, state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor))))))) : ((message.tag === 36) ? Editor_normalizeState(new EditorState(state.Doc, state.Compiled, state.SlotValues, state.SolvedSketchParams, state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, message.fields[0])) : ((message.tag === 37) ? (new EditorState(state.Doc, state.Compiled, state.SlotValues, state.SolvedSketchParams, Palette_openSession(), state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor)) : ((message.tag === 38) ? (new EditorState(state.Doc, state.Compiled, state.SlotValues, state.SolvedSketchParams, Palette_setQuery(message.fields[0], state.PaletteSession), state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor)) : ((message.tag === 39) ? ((id_14 = message.fields[0], Editor_paletteMaybeBuild(id_14, new EditorState(state.Doc, state.Compiled, state.SlotValues, state.SolvedSketchParams, (state.PaletteSession.PickedKind != null) ? Palette_pickItem(id_14, state.PaletteSession) : Palette_pickCommand(id_14, state.PaletteSession), state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor)))) : ((message.tag === 40) ? (new EditorState(state.Doc, state.Compiled, state.SlotValues, state.SolvedSketchParams, Palette_setScalarField(message.fields[0], message.fields[1], state.PaletteSession), state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor)) : ((message.tag === 41) ? Editor_paletteMaybeBuild("", new EditorState(state.Doc, state.Compiled, state.SlotValues, state.SolvedSketchParams, Palette_commitScalars(state.PaletteSession), state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor)) : ((message.tag === 42) ? Editor_paletteMaybeBuild(message.fields[0], new EditorState(state.Doc, state.Compiled, state.SlotValues, state.SolvedSketchParams, Palette_skipToEnd(state.PaletteSession), state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor)) : ((message.tag === 43) ? (new EditorState(state.Doc, state.Compiled, state.SlotValues, state.SolvedSketchParams, Palette_back(state.PaletteSession), state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor)) : ((message.tag === 44) ? (new EditorState(state.Doc, state.Compiled, state.SlotValues, state.SolvedSketchParams, Palette_empty, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor)) : ((message.tag === 45) ? Editor_recompileState(new EditorState(message.fields[0], state.Compiled, state.SlotValues, state.SolvedSketchParams, state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor)) : ((message.tag === 46) ? ((model = message.fields[0], Editor_loadDoc(new Document$(model.Name, model.Actions, map((a_1) => a_1.Id, orElseWith(tryFind_1((a) => (a.Id === "origin"), model.Actions), () => tryHead(model.Actions)))), state))) : ((message.tag === 47) ? Editor_loadDoc(DocumentModule_emptyDocument(), state) : (new EditorState(DocumentModule_select(message.fields[0], state.Doc), state.Compiled, state.SlotValues, state.SolvedSketchParams, state.PaletteSession, state.HoveredTarget, state.SelectedTargets, state.SketchEditMode, state.SketchTool, state.SketchToolPoints, state.SketchToolPointRefs, state.LineChainStartPointId, state.EditingDimension, state.ActiveSketchDrag, state.PendingSketchDragCommit, state.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.ConstraintPlacementCursor)))))))))))))))))))))))))))))))))))))))))))))))), (message.tag === 10) ? singleton(new Effect(2, [])) : ((message.tag === 12) ? singleton(new Effect(2, [])) : ((message.tag === 13) ? singleton(new Effect(2, [])) : ((message.tag === 20) ? singleton(new Effect(2, [])) : ((message.tag === 30) ? singleton(new Effect(2, [])) : ((message.tag === 34) ? singleton(new Effect(2, [])) : Editor_noEffects)))))];
    }
}

