import { Record } from "../../ui/fable_modules/fable-library-js.4.29.0/Types.js";
import { class_type, bool_type, array_type, anonRecord_type, list_type, option_type, float64_type, int32_type, record_type, string_type } from "../../ui/fable_modules/fable-library-js.4.29.0/Reflection.js";
import { RigidTransform_get_Identity, RigidTransformModule_fromAxisAngle, RigidTransformModule_translate, RigidTransform_op_Multiply_ZFA4D60, RigidTransform_$reflection } from "../Math/Transform.fs.js";
import { SketchConstraintModule_labelPos, ArcData, FreePoint, RenderEntity, ActionSketch_$reflection, LabelPos_$reflection } from "../Sketch/Sketch.fs.js";
import { DisplaySettingsModule_defaults, FieldSliceSettingsModule_defaults, FieldSliceSettings_$reflection, DisplaySettings_$reflection } from "./Domain.fs.js";
import { Vec3_op_Multiply_ZB3DA56A, Vec3_op_Addition_Z3F547E60, Vec3, Vec3_$reflection } from "../Math/Vec.fs.js";
import { Graph_$reflection } from "../Solve/GraphIR.fs.js";
import { Editor_formatErrors, Editor_sketchUiState, ConstraintPlacementKind, Editor_belongsToActiveSketch, SketchLoopView, Editor_resolvedFrames, Editor_resolveSketchTransform, Editor_sketchEditFrameIds, ActionErrorView_$reflection, SketchLoopView_$reflection } from "./Editor.fs.js";
import { FieldSurface_$reflection } from "../Field/FieldIR.fs.js";
import { SelectionTarget_$reflection, Pickable_$reflection } from "./Pickable.fs.js";
import { SketchUiState_$reflection } from "../Sketch/SketchAuthoring.fs.js";
import { toList as toList_1, ofList, tryFind } from "../../ui/fable_modules/fable-library-js.4.29.0/Map.js";
import { SlotRef } from "./SlotTable.fs.js";
import { item } from "../../ui/fable_modules/fable-library-js.4.29.0/Array.js";
import { filter as filter_1, collect, empty, singleton, tryFind as tryFind_1, fold, choose, mapIndexed, map } from "../../ui/fable_modules/fable-library-js.4.29.0/List.js";
import { printf, toText } from "../../ui/fable_modules/fable-library-js.4.29.0/String.js";
import { equals, comparePrimitives } from "../../ui/fable_modules/fable-library-js.4.29.0/Util.js";
import { filter, map as map_1, defaultArg } from "../../ui/fable_modules/fable-library-js.4.29.0/Option.js";
import { Quat__Rotate_Z2E054BF3 } from "../Math/Quat.fs.js";
import { foldChain } from "./Frames.fs.js";
import { toList } from "../../ui/fable_modules/fable-library-js.4.29.0/Set.js";
import { SketchCompileContext, SketchCompile_compile } from "../Sketch/SketchCompile.fs.js";
import { SketchSolve_overlaySolvedSketch } from "../Sketch/SketchSolve.fs.js";
import { SketchLoops_detectLoops } from "../Sketch/SketchLoops.fs.js";

export class FrameView extends Record {
    constructor(Id, Transform) {
        super();
        this.Id = Id;
        this.Transform = Transform;
    }
}

export function FrameView_$reflection() {
    return record_type("Server.FrameView", [], FrameView, () => [["Id", string_type], ["Transform", RigidTransform_$reflection()]]);
}

export class ConstraintLabelPositionView extends Record {
    constructor(SketchId, ConstraintIndex, Position) {
        super();
        this.SketchId = SketchId;
        this.ConstraintIndex = (ConstraintIndex | 0);
        this.Position = Position;
    }
}

export function ConstraintLabelPositionView_$reflection() {
    return record_type("Server.ConstraintLabelPositionView", [], ConstraintLabelPositionView, () => [["SketchId", string_type], ["ConstraintIndex", int32_type], ["Position", LabelPos_$reflection()]]);
}

export class DisplayStateView extends Record {
    constructor(Display, FieldSlice) {
        super();
        this.Display = Display;
        this.FieldSlice = FieldSlice;
    }
}

export function DisplayStateView_$reflection() {
    return record_type("Server.DisplayStateView", [], DisplayStateView, () => [["Display", DisplaySettings_$reflection()], ["FieldSlice", FieldSliceSettings_$reflection()]]);
}

export class FieldSliceView extends Record {
    constructor(SurfaceIndex, PlaneOrigin, PlaneX, PlaneY, Extent) {
        super();
        this.SurfaceIndex = (SurfaceIndex | 0);
        this.PlaneOrigin = PlaneOrigin;
        this.PlaneX = PlaneX;
        this.PlaneY = PlaneY;
        this.Extent = Extent;
    }
}

export function FieldSliceView_$reflection() {
    return record_type("Server.FieldSliceView", [], FieldSliceView, () => [["SurfaceIndex", int32_type], ["PlaneOrigin", Vec3_$reflection()], ["PlaneX", Vec3_$reflection()], ["PlaneY", Vec3_$reflection()], ["Extent", float64_type]]);
}

export class ViewerSketchView extends Record {
    constructor(Id, Origin, Sketch, Graph) {
        super();
        this.Id = Id;
        this.Origin = Origin;
        this.Sketch = Sketch;
        this.Graph = Graph;
    }
}

export function ViewerSketchView_$reflection() {
    return record_type("Server.ViewerSketchView", [], ViewerSketchView, () => [["Id", string_type], ["Origin", option_type(string_type)], ["Sketch", ActionSketch_$reflection()], ["Graph", Graph_$reflection()]]);
}

export class SketchLoopsStateView extends Record {
    constructor(SketchId, Loops) {
        super();
        this.SketchId = SketchId;
        this.Loops = Loops;
    }
}

export function SketchLoopsStateView_$reflection() {
    return record_type("Server.SketchLoopsStateView", [], SketchLoopsStateView, () => [["SketchId", string_type], ["Loops", list_type(SketchLoopView_$reflection())]]);
}

export class ViewerModel extends Record {
    constructor(Surfaces, FieldWgsl, FieldSliceWgsl, FieldSurfaceActionIds, Sketches, NumSlots, SlotIndex, Pickables) {
        super();
        this.Surfaces = Surfaces;
        this.FieldWgsl = FieldWgsl;
        this.FieldSliceWgsl = FieldSliceWgsl;
        this.FieldSurfaceActionIds = FieldSurfaceActionIds;
        this.Sketches = Sketches;
        this.NumSlots = (NumSlots | 0);
        this.SlotIndex = SlotIndex;
        this.Pickables = Pickables;
    }
}

export function ViewerModel_$reflection() {
    return record_type("Server.ViewerModel", [], ViewerModel, () => [["Surfaces", list_type(FieldSurface_$reflection())], ["FieldWgsl", option_type(string_type)], ["FieldSliceWgsl", option_type(string_type)], ["FieldSurfaceActionIds", list_type(string_type)], ["Sketches", list_type(ViewerSketchView_$reflection())], ["NumSlots", int32_type], ["SlotIndex", list_type(anonRecord_type(["ActionId", string_type], ["Path", string_type], ["Slot", int32_type]))], ["Pickables", list_type(Pickable_$reflection())]]);
}

export class ViewerState extends Record {
    constructor(Params, SelectedId, HoveredTarget, HighlightedTarget, DragTarget, SelectedTargets, HighlightedTargets, VisibleDimensionSketchIds, SketchUi, Frames, SketchEditFrames, SketchTransforms, SketchLoops, FieldSlices, Visible, ConstraintLabelPositions, Display, Errors) {
        super();
        this.Params = Params;
        this.SelectedId = SelectedId;
        this.HoveredTarget = HoveredTarget;
        this.HighlightedTarget = HighlightedTarget;
        this.DragTarget = DragTarget;
        this.SelectedTargets = SelectedTargets;
        this.HighlightedTargets = HighlightedTargets;
        this.VisibleDimensionSketchIds = VisibleDimensionSketchIds;
        this.SketchUi = SketchUi;
        this.Frames = Frames;
        this.SketchEditFrames = SketchEditFrames;
        this.SketchTransforms = SketchTransforms;
        this.SketchLoops = SketchLoops;
        this.FieldSlices = FieldSlices;
        this.Visible = Visible;
        this.ConstraintLabelPositions = ConstraintLabelPositions;
        this.Display = Display;
        this.Errors = Errors;
    }
}

export function ViewerState_$reflection() {
    return record_type("Server.ViewerState", [], ViewerState, () => [["Params", array_type(float64_type)], ["SelectedId", option_type(string_type)], ["HoveredTarget", option_type(SelectionTarget_$reflection())], ["HighlightedTarget", option_type(SelectionTarget_$reflection())], ["DragTarget", option_type(SelectionTarget_$reflection())], ["SelectedTargets", list_type(SelectionTarget_$reflection())], ["HighlightedTargets", list_type(SelectionTarget_$reflection())], ["VisibleDimensionSketchIds", list_type(string_type)], ["SketchUi", SketchUiState_$reflection()], ["Frames", list_type(FrameView_$reflection())], ["SketchEditFrames", list_type(FrameView_$reflection())], ["SketchTransforms", list_type(FrameView_$reflection())], ["SketchLoops", list_type(SketchLoopsStateView_$reflection())], ["FieldSlices", list_type(FieldSliceView_$reflection())], ["Visible", class_type("Microsoft.FSharp.Collections.FSharpMap`2", [string_type, bool_type])], ["ConstraintLabelPositions", list_type(ConstraintLabelPositionView_$reflection())], ["Display", class_type("Microsoft.FSharp.Collections.FSharpMap`2", [string_type, DisplayStateView_$reflection()])], ["Errors", list_type(ActionErrorView_$reflection())]]);
}

function ViewerPipeline_slotValue(slots, values, actionId, path, defaultValue) {
    const matchValue = tryFind(new SlotRef(actionId, path), slots.Index);
    let matchResult, slot_1;
    if (matchValue != null) {
        if (matchValue < values.length) {
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
        case 0:
            return item(slot_1, values);
        default:
            return defaultValue;
    }
}

function ViewerPipeline_resolveSketchEntities(slots, values, sketchId, sketch) {
    return map((_arg) => {
        let matchResult, id, x, y, centerId, id_1, radius, endId, id_2, startId, through, other;
        switch (_arg.tag) {
            case 0: {
                matchResult = 0;
                id = _arg.fields[0];
                x = _arg.fields[1];
                y = _arg.fields[2];
                break;
            }
            case 2: {
                matchResult = 1;
                centerId = _arg.fields[1];
                id_1 = _arg.fields[0];
                radius = _arg.fields[2];
                break;
            }
            case 3: {
                if (_arg.fields[3].tag === 1) {
                    matchResult = 2;
                    endId = _arg.fields[2];
                    id_2 = _arg.fields[0];
                    startId = _arg.fields[1];
                    through = _arg.fields[3].fields[0];
                }
                else {
                    matchResult = 3;
                    other = _arg;
                }
                break;
            }
            default: {
                matchResult = 3;
                other = _arg;
            }
        }
        switch (matchResult) {
            case 0:
                return new RenderEntity(0, [id, ViewerPipeline_slotValue(slots, values, sketchId, toText(printf("sketch.entity.%s.x"))(id), x), ViewerPipeline_slotValue(slots, values, sketchId, toText(printf("sketch.entity.%s.y"))(id), y)]);
            case 1:
                return new RenderEntity(2, [id_1, centerId, ViewerPipeline_slotValue(slots, values, sketchId, toText(printf("sketch.entity.%s.radius"))(id_1), radius)]);
            case 2:
                return new RenderEntity(3, [id_2, startId, endId, new ArcData(1, [new FreePoint(ViewerPipeline_slotValue(slots, values, sketchId, toText(printf("sketch.entity.%s.through.x"))(id_2), through.X), ViewerPipeline_slotValue(slots, values, sketchId, toText(printf("sketch.entity.%s.through.y"))(id_2), through.Y))])]);
            default:
                return other;
        }
    }, sketch.Entities);
}

function ViewerPipeline_localSliceBasis(plane) {
    switch (plane) {
        case "X":
            return [new Vec3(0, 1, 0), new Vec3(0, 0, 1), new Vec3(1, 0, 0)];
        case "Y":
            return [new Vec3(1, 0, 0), new Vec3(0, 0, 1), new Vec3(0, 1, 0)];
        default:
            return [new Vec3(1, 0, 0), new Vec3(0, 1, 0), new Vec3(0, 0, 1)];
    }
}

function ViewerPipeline_leadingFieldTransform(state_mut, field_mut, acc_mut) {
    ViewerPipeline_leadingFieldTransform:
    while (true) {
        const state = state_mut, field = field_mut, acc = acc_mut;
        const slot = (s) => item(s, state.SlotValues);
        switch (field.tag) {
            case 1: {
                state_mut = state;
                field_mut = field.fields[3];
                acc_mut = RigidTransform_op_Multiply_ZFA4D60(acc, RigidTransformModule_translate(new Vec3(slot(field.fields[0]), slot(field.fields[1]), slot(field.fields[2]))));
                continue ViewerPipeline_leadingFieldTransform;
            }
            case 2: {
                state_mut = state;
                field_mut = field.fields[4];
                acc_mut = RigidTransform_op_Multiply_ZFA4D60(acc, RigidTransformModule_fromAxisAngle(new Vec3(slot(field.fields[0]), slot(field.fields[1]), slot(field.fields[2])), slot(field.fields[3])));
                continue ViewerPipeline_leadingFieldTransform;
            }
            case 4: {
                state_mut = state;
                field_mut = field.fields[2];
                acc_mut = acc;
                continue ViewerPipeline_leadingFieldTransform;
            }
            default:
                return acc;
        }
        break;
    }
}

function ViewerPipeline_activeFieldSlices(state) {
    const surfaceIndexByAction = ofList(mapIndexed((index, surface) => [surface.ActionId, [index, surface.Field]], state.Compiled.Surfaces), {
        Compare: comparePrimitives,
    });
    return choose((action) => {
        const fs = defaultArg(action.FieldSlice, FieldSliceSettingsModule_defaults);
        if (!action.Visible ? true : !fs.Enabled) {
            return undefined;
        }
        else {
            const matchValue = tryFind(action.Id, surfaceIndexByAction);
            if (matchValue != null) {
                const surfaceIndex = matchValue[0] | 0;
                const frame = ViewerPipeline_leadingFieldTransform(state, matchValue[1], RigidTransform_get_Identity());
                const patternInput = ViewerPipeline_localSliceBasis(fs.Plane);
                const planeX = Quat__Rotate_Z2E054BF3(frame.Rot, patternInput[0]);
                const planeY = Quat__Rotate_Z2E054BF3(frame.Rot, patternInput[1]);
                return new FieldSliceView(surfaceIndex, Vec3_op_Addition_Z3F547E60(frame.Trans, Vec3_op_Multiply_ZB3DA56A(fs.Offset, Quat__Rotate_Z2E054BF3(frame.Rot, patternInput[2]))), planeX, planeY, fs.Extent);
            }
            else {
                return undefined;
            }
        }
    }, state.Doc.Actions);
}

function ViewerPipeline_sketchEditFrames(state) {
    return choose((id) => map_1((t) => (new FrameView(id, t)), map_1((chain) => foldChain(state.Compiled.Slots, state.SlotValues, chain), tryFind(id, state.Compiled.Frames))), toList(Editor_sketchEditFrameIds(state)));
}

export function ViewerPipeline_viewerModel(state) {
    const indexList = map((tupledArg) => {
        const r = tupledArg[0];
        return {
            ActionId: r.ActionId,
            Path: r.Path,
            Slot: tupledArg[1],
        };
    }, toList_1(state.Compiled.Slots.Index));
    const sketches = choose((a) => {
        const matchValue = a.Kind;
        if (matchValue.tag === 11) {
            const sk = matchValue.fields[2];
            const origin = matchValue.fields[0];
            return new ViewerSketchView(a.Id, origin, sk, SketchCompile_compile(sk, new SketchCompileContext(Editor_resolveSketchTransform(state, origin, matchValue.fields[1]), Editor_resolvedFrames(state))));
        }
        else {
            return undefined;
        }
    }, state.Doc.Actions);
    return new ViewerModel(state.Compiled.Surfaces, undefined, undefined, map((s_1) => s_1.ActionId, state.Compiled.Surfaces), sketches, state.Compiled.Slots.Values.length, indexList, state.Compiled.Pickables);
}

export function ViewerPipeline_viewerState(state) {
    const effectiveParams = fold((current, action) => {
        const matchValue = action.Kind;
        const matchValue_1 = tryFind(action.Id, state.SolvedSketchParams);
        let matchResult, sketch, solvedLocal;
        if (matchValue.tag === 11) {
            if (matchValue_1 != null) {
                matchResult = 0;
                sketch = matchValue.fields[2];
                solvedLocal = matchValue_1;
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
                return SketchSolve_overlaySolvedSketch(current, state.Compiled.Slots, action.Id, sketch, solvedLocal);
            default:
                return current;
        }
    }, state.SlotValues, state.Doc.Actions);
    const sketchLoops = choose((action_1) => {
        const matchValue_3 = action_1.Kind;
        if (matchValue_3.tag === 11) {
            return new SketchLoopsStateView(action_1.Id, map((l) => (new SketchLoopView(l.Id, l.EntityIds)), SketchLoops_detectLoops(ViewerPipeline_resolveSketchEntities(state.Compiled.Slots, effectiveParams, action_1.Id, matchValue_3.fields[2]))));
        }
        else {
            return undefined;
        }
    }, state.Doc.Actions);
    const dragTarget = filter((_arg) => {
        let matchResult_1, t;
        switch (_arg.tag) {
            case 0: {
                matchResult_1 = 0;
                t = _arg;
                break;
            }
            case 5: {
                matchResult_1 = 0;
                t = _arg;
                break;
            }
            default:
                matchResult_1 = 1;
        }
        switch (matchResult_1) {
            case 0:
                return Editor_belongsToActiveSketch(state, t);
            default:
                return false;
        }
    }, state.HoveredTarget);
    const frameHighlightAllowed = !equals(state.ConstraintPlacementMode, new ConstraintPlacementKind(1, []));
    const highlightedTargetAllowed = (target) => {
        switch (target.tag) {
            case 7:
                return false;
            case 6:
                if (frameHighlightAllowed) {
                    return Editor_belongsToActiveSketch(state, target);
                }
                else {
                    return false;
                }
            default:
                return Editor_belongsToActiveSketch(state, target);
        }
    };
    let visibleDimensionSketchIds;
    const matchValue_5 = state.Doc.SelectedId;
    let matchResult_2;
    if (state.SketchEditMode) {
        if (matchValue_5 != null) {
            matchResult_2 = 0;
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
            const selectedId = matchValue_5;
            const matchValue_7 = tryFind_1((a) => (a.Id === selectedId), state.Doc.Actions);
            let matchResult_3;
            if (matchValue_7 != null) {
                if (matchValue_7.Kind.tag === 11) {
                    matchResult_3 = 0;
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
                    visibleDimensionSketchIds = singleton(selectedId);
                    break;
                }
                default:
                    visibleDimensionSketchIds = empty();
            }
            break;
        }
        default:
            visibleDimensionSketchIds = empty();
    }
    const displayByAction = ofList(choose((a_1) => {
        const matchValue_8 = tryFind(a_1.Id, state.Compiled.TypeMap);
        let matchResult_4;
        if (matchValue_8 != null) {
            if (matchValue_8.tag === 0) {
                matchResult_4 = 0;
            }
            else {
                matchResult_4 = 1;
            }
        }
        else {
            matchResult_4 = 1;
        }
        switch (matchResult_4) {
            case 0:
                return [a_1.Id, new DisplayStateView(defaultArg(a_1.Display, DisplaySettingsModule_defaults), defaultArg(a_1.FieldSlice, FieldSliceSettingsModule_defaults))];
            default:
                return undefined;
        }
    }, state.Doc.Actions), {
        Compare: comparePrimitives,
    });
    const frames = choose((a_2) => {
        const matchValue_9 = tryFind(a_2.Id, state.Compiled.TypeMap);
        let matchResult_5;
        if (matchValue_9 != null) {
            if (matchValue_9.tag === 2) {
                matchResult_5 = 0;
            }
            else {
                matchResult_5 = 1;
            }
        }
        else {
            matchResult_5 = 1;
        }
        switch (matchResult_5) {
            case 0:
                return map_1((t_1) => (new FrameView(a_2.Id, t_1)), map_1((chain) => foldChain(state.Compiled.Slots, state.SlotValues, chain), tryFind(a_2.Id, state.Compiled.Frames)));
            default:
                return undefined;
        }
    }, state.Doc.Actions);
    const sketchTransforms = choose((a_3) => {
        const matchValue_10 = a_3.Kind;
        if (matchValue_10.tag === 11) {
            return new FrameView(a_3.Id, Editor_resolveSketchTransform(state, matchValue_10.fields[0], matchValue_10.fields[1]));
        }
        else {
            return undefined;
        }
    }, state.Doc.Actions);
    const visibleByAction = ofList(map((a_4) => [a_4.Id, a_4.Visible], state.Doc.Actions), {
        Compare: comparePrimitives,
    });
    const constraintLabelPositions = collect((a_5) => {
        const matchValue_11 = a_5.Kind;
        if (matchValue_11.tag === 11) {
            return choose((x_2) => x_2, mapIndexed((i, c) => map_1((pos) => (new ConstraintLabelPositionView(a_5.Id, i, pos)), SketchConstraintModule_labelPos(c)), matchValue_11.fields[2].Constraints));
        }
        else {
            return empty();
        }
    }, state.Doc.Actions);
    return new ViewerState(effectiveParams, state.Doc.SelectedId, state.HoveredTarget, filter(highlightedTargetAllowed, state.HoveredTarget), dragTarget, state.SelectedTargets, filter_1(highlightedTargetAllowed, state.SelectedTargets), visibleDimensionSketchIds, Editor_sketchUiState(state), frames, ViewerPipeline_sketchEditFrames(state), sketchTransforms, sketchLoops, ViewerPipeline_activeFieldSlices(state), visibleByAction, constraintLabelPositions, displayByAction, Editor_formatErrors(state.Compiled.Errors));
}

