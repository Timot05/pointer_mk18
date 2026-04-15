import {
  Editor_documentView,
  Editor_initState,
  Editor_paletteView,
  Editor_serializedModel,
  Editor_update,
  Editor_viewerModel,
  Editor_viewerState,
  SerializedModel,
  type EditorState,
  type Message,
} from "../src-gen/core/Editor";
import { ParamValue } from "../src-gen/core/server/Domain";
import { ofArray as listOfArray } from "../src-gen/core/fable_modules/fable-library-js.4.24.0/List";
import { ofArray as mapOfArray } from "../src-gen/core/fable_modules/fable-library-js.4.24.0/Map";
import { createStore } from "./store";

function toPlain<T>(value: T): T {
  return JSON.parse(JSON.stringify(value)) as T;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function toNumberArray(value: unknown): number[] {
  if (Array.isArray(value)) return value as number[];
  if (isRecord(value)) {
    return Object.keys(value)
      .sort((a, b) => Number(a) - Number(b))
      .map((key) => Number(value[key]));
  }
  return [];
}

function toMapObject<T>(value: unknown, mapValue: (entryValue: unknown) => T): Record<string, T> {
  if (!Array.isArray(value)) return {};
  return Object.fromEntries(
    value
      .filter((entry): entry is [string, unknown] => Array.isArray(entry) && typeof entry[0] === "string")
      .map(([key, entryValue]) => [key, mapValue(entryValue)]),
  );
}

function normalizeSelectionTarget(value: unknown): unknown {
  if (!Array.isArray(value) || typeof value[0] !== "string") return value;
  const [tag, ...rest] = value;
  switch (tag) {
    case "TargetPoint": return { case: tag, sketchId: rest[0], entityId: rest[1] };
    case "TargetLine": return { case: tag, sketchId: rest[0], entityId: rest[1] };
    case "TargetCircle": return { case: tag, sketchId: rest[0], entityId: rest[1] };
    case "TargetArc": return { case: tag, sketchId: rest[0], entityId: rest[1] };
    case "TargetLoop": return { case: tag, sketchId: rest[0], loopId: rest[1] };
    case "TargetDimension": return { case: tag, sketchId: rest[0], constraintIndex: rest[1] };
    case "TargetFrameOrigin": return { case: tag, frameId: rest[0] };
    case "TargetFrameAxis": return { case: tag, frameId: rest[0], part: rest[1] };
    case "TargetSurface": return { case: tag, actionId: rest[0] };
    default: return { case: tag, values: rest };
  }
}

function normalizeArcData(value: unknown): unknown {
  if (!Array.isArray(value) || typeof value[0] !== "string") return value;
  const [tag, ...rest] = value;
  switch (tag) {
    case "ArcCenter":
      return { case: tag, center: rest[0], clockwise: rest[1] };
    case "ArcThreePoint":
      return { case: tag, through: { x: rest[0]?.X ?? rest[0]?.x ?? 0, y: rest[0]?.Y ?? rest[0]?.y ?? 0 } };
    default:
      return { case: tag, values: rest };
  }
}

function normalizeRenderEntity(value: unknown): unknown {
  if (!Array.isArray(value) || typeof value[0] !== "string") return value;
  const [tag, ...rest] = value;
  switch (tag) {
    case "REPoint": return { case: tag, id: rest[0], x: rest[1], y: rest[2] };
    case "RELine": return { case: tag, id: rest[0], startId: rest[1], endId: rest[2] };
    case "RECircle": return { case: tag, id: rest[0], center: rest[1], radius: rest[2] };
    case "REArc": return { case: tag, id: rest[0], startId: rest[1], endId: rest[2], data: normalizeArcData(rest[3]) };
    default: return { case: tag, values: rest };
  }
}

function normalizeSketchConstraint(value: unknown): unknown {
  if (!Array.isArray(value) || typeof value[0] !== "string") return value;
  const [tag, ...rest] = value;
  switch (tag) {
    case "Fixed": return { case: tag, point: rest[0], x: rest[1], y: rest[2] };
    case "Coincident": return { case: tag, a: rest[0], b: rest[1] };
    case "FrameCoincident": return { case: tag, point: rest[0], frame: rest[1], part: rest[2] };
    case "Concentric": return { case: tag, entityA: rest[0], entityB: rest[1], centerA: rest[2], centerB: rest[3] };
    case "Horizontal": return { case: tag, a: rest[0], b: rest[1] };
    case "Vertical": return { case: tag, a: rest[0], b: rest[1] };
    case "Distance": return { case: tag, a: rest[0], b: rest[1], distance: rest[2], labelPosition: normalizeLabelPos(rest[3]) };
    case "FrameDistance": return { case: tag, point: rest[0], frame: rest[1], part: rest[2], distance: rest[3], labelPosition: normalizeLabelPos(rest[4]) };
    case "Equal": return { case: tag, aStart: rest[0], aEnd: rest[1], bStart: rest[2], bEnd: rest[3], lineA: rest[4], lineB: rest[5] };
    case "EqualRadius": return { case: tag, entityA: rest[0], entityB: rest[1] };
    case "Midpoint": return { case: tag, point: rest[0], lineA: rest[1], aStart: rest[2], aEnd: rest[3] };
    case "Parallel": return { case: tag, aStart: rest[0], aEnd: rest[1], bStart: rest[2], bEnd: rest[3], lineA: rest[4], lineB: rest[5] };
    case "FrameParallel": return { case: tag, aStart: rest[0], aEnd: rest[1], lineA: rest[2], frame: rest[3], part: rest[4] };
    case "Perpendicular": return { case: tag, aStart: rest[0], aEnd: rest[1], bStart: rest[2], bEnd: rest[3], lineA: rest[4], lineB: rest[5] };
    case "FramePerpendicular": return { case: tag, aStart: rest[0], aEnd: rest[1], lineA: rest[2], frame: rest[3], part: rest[4] };
    case "Tangent": return { case: tag, aStart: rest[0], aEnd: rest[1], center: rest[2], circle: rest[3], lineA: rest[4], radius: rest[5] };
    case "CurveTangent": return { case: tag, entityA: rest[0], centerA: rest[1], entityB: rest[2], centerB: rest[3], internal: rest[4] };
    case "CircleDiameter": return { case: tag, circle: rest[0], center: rest[1], diameter: rest[2], labelPosition: normalizeLabelPos(rest[3]) };
    case "LineDistance": return { case: tag, aStart: rest[0], aEnd: rest[1], bStart: rest[2], bEnd: rest[3], lineA: rest[4], lineB: rest[5], distance: rest[6], labelPosition: normalizeLabelPos(rest[7]) };
    case "FrameLineDistance": return { case: tag, lineA: rest[0], aStart: rest[1], aEnd: rest[2], frame: rest[3], part: rest[4], distance: rest[5], labelPosition: normalizeLabelPos(rest[6]) };
    case "PointLineDistance": return { case: tag, point: rest[0], lineA: rest[1], aStart: rest[2], aEnd: rest[3], distance: rest[4], labelPosition: normalizeLabelPos(rest[5]) };
    case "PointCircleDistance": return { case: tag, point: rest[0], circle: rest[1], center: rest[2], distance: rest[3], labelPosition: normalizeLabelPos(rest[4]) };
    case "LineCircleDistance": return { case: tag, lineA: rest[0], aStart: rest[1], aEnd: rest[2], circle: rest[3], center: rest[4], distance: rest[5], labelPosition: normalizeLabelPos(rest[6]) };
    case "CircleCircleDistance": return { case: tag, circleA: rest[0], centerA: rest[1], circleB: rest[2], centerB: rest[3], distance: rest[4], internal: rest[5], labelPosition: normalizeLabelPos(rest[6]) };
    case "Angle":
      return { case: tag, aStart: rest[0], aEnd: rest[1], bStart: rest[2], bEnd: rest[3], lineA: rest[4], lineB: rest[5], angle: rest[6], aReverse: rest[7], bReverse: rest[8], ccwFromAToB: rest[9], labelPosition: normalizeLabelPos(rest[10]) };
    default:
      return { case: tag, values: rest };
  }
}

function normalizeActionSketch(value: unknown): unknown {
  if (!isRecord(value)) return value;
  return {
    entities: Array.isArray(value.Entities) ? value.Entities.map(normalizeRenderEntity) : [],
    constraints: Array.isArray(value.Constraints) ? value.Constraints.map(normalizeSketchConstraint) : [],
  };
}

function normalizeFromSketchSelection(value: unknown): unknown {
  if (!Array.isArray(value) || typeof value[0] !== "string") return value;
  const [tag, ...rest] = value;
  switch (tag) {
    case "SelectionLoop": return { case: tag, loopId: rest[0] ?? null };
    case "SelectionElements": return { case: tag, lineIds: Array.isArray(rest[0]) ? rest[0] : [] };
    default: return { case: tag, values: rest };
  }
}

function normalizeActionKind(value: unknown): unknown {
  if (typeof value === "string") return { case: value };
  if (!Array.isArray(value) || typeof value[0] !== "string") return value;
  const [tag, ...rest] = value;
  switch (tag) {
    case "Cylinder": return { case: tag, radius: rest[0], height: rest[1] };
    case "Sphere": return { case: tag, radius: rest[0] };
    case "Box": return { case: tag, width: rest[0], height: rest[1], depth: rest[2] };
    case "HalfPlane": return { case: tag, axis: rest[0], offset: rest[1], flip: rest[2] };
    case "Translate": return { case: tag, child: rest[0] ?? null, x: rest[1], y: rest[2], z: rest[3] };
    case "Rotate": return { case: tag, child: rest[0] ?? null, ax: rest[1], ay: rest[2], az: rest[3], angle: rest[4] };
    case "Move": return { case: tag, child: rest[0] ?? null, frame: rest[1] ?? null };
    case "Union":
    case "Subtract":
    case "Intersect":
      return { case: tag, a: rest[0] ?? null, b: rest[1] ?? null, radius: rest[2] };
    case "Sketch": return { case: tag, origin: rest[0] ?? null, plane: rest[1], sketch: normalizeActionSketch(rest[2]) };
    case "FromSketch": return { case: tag, child: rest[0] ?? null, flip: rest[1], selection: normalizeFromSketchSelection(rest[2]) };
    case "Thicken": return { case: tag, child: rest[0] ?? null, amount: rest[1] };
    case "Shell": return { case: tag, child: rest[0] ?? null, thickness: rest[1] };
    case "Mesh": return { case: tag, child: rest[0] ?? null, size: rest[1], resolution: rest[2] };
    default: return { case: tag, values: rest };
  }
}

function normalizeDisplay(value: unknown): unknown {
  if (!isRecord(value)) return value;
  return {
    enabled: Boolean(value.Enabled),
    color: toNumberArray(value.Color),
    opacity: Number(value.Opacity ?? 0),
    isoValue: Number(value.IsoValue ?? 0),
  };
}

function normalizeFieldSliceSettings(value: unknown): unknown {
  if (!isRecord(value)) return value;
  return {
    enabled: Boolean(value.Enabled),
    plane: value.Plane ?? "Z",
    offset: Number(value.Offset ?? 0),
    extent: Number(value.Extent ?? 0),
  };
}

function normalizeAction(value: unknown): unknown {
  if (!isRecord(value)) return value;
  return {
    id: value.Id,
    name: value.Name ?? null,
    kind: normalizeActionKind(value.Kind),
    visible: Boolean(value.Visible),
    display: value.Display ? normalizeDisplay(value.Display) : null,
    fieldSlice: value.FieldSlice ? normalizeFieldSliceSettings(value.FieldSlice) : null,
  };
}

function normalizeSketchLoop(value: unknown): unknown {
  if (!isRecord(value)) return value;
  return {
    id: value.Id,
    entityIds: Array.isArray(value.EntityIds) ? value.EntityIds : [],
  };
}

function normalizeLabelPos(value: unknown): unknown {
  if (!value) return null;
  if (!isRecord(value)) return value;
  return { x: value.X ?? value.x ?? 0, y: value.Y ?? value.y ?? 0 };
}

function normalizeJsonVec3(value: unknown): unknown {
  if (!isRecord(value)) return value;
  return { x: value.X ?? 0, y: value.Y ?? 0, z: value.Z ?? 0 };
}

function normalizeJsonQuat(value: unknown): unknown {
  if (!isRecord(value)) return value;
  return { w: value.W ?? 1, x: value.X ?? 0, y: value.Y ?? 0, z: value.Z ?? 0 };
}

function normalizeTransform(value: unknown): unknown {
  if (!isRecord(value)) return value;
  return {
    rot: normalizeJsonQuat(value.Rot),
    trans: normalizeJsonVec3(value.Trans),
  };
}

function normalizeViewerSketch(value: unknown): unknown {
  if (!isRecord(value)) return value;
  return {
    id: value.Id,
    origin: value.Origin ?? null,
    transform: normalizeTransform(value.Transform),
    sketch: normalizeActionSketch(value.Sketch),
    graph: isRecord(value.Graph) ? {
      nodes: Array.isArray(value.Graph.Nodes) ? value.Graph.Nodes.map((n) => isRecord(n) ? { op: n.Op, inputs: n.Inputs ?? {} } : n) : [],
      params: Array.isArray(value.Graph.Params) ? value.Graph.Params : [],
      outputs: Array.isArray(value.Graph.Outputs) ? value.Graph.Outputs : [],
      varSlots: Array.isArray(value.Graph.VarSlots) ? value.Graph.VarSlots : [],
    } : value.Graph,
    loops: Array.isArray(value.Loops) ? value.Loops.map(normalizeSketchLoop) : [],
  };
}

function normalizePickable(value: unknown): unknown {
  if (!Array.isArray(value) || typeof value[0] !== "string") return value;
  const [tag, ...rest] = value;
  switch (tag) {
    case "PickPoint": return { case: tag, pickId: rest[0], sketchId: rest[1], entityId: rest[2] };
    case "PickLine": return { case: tag, pickId: rest[0], sketchId: rest[1], entityId: rest[2] };
    case "PickCircle": return { case: tag, pickId: rest[0], sketchId: rest[1], entityId: rest[2] };
    case "PickArc": return { case: tag, pickId: rest[0], sketchId: rest[1], entityId: rest[2] };
    case "PickLoop": return { case: tag, pickId: rest[0], sketchId: rest[1], loopId: rest[2], entityIds: Array.isArray(rest[3]) ? rest[3] : [] };
    case "PickDimension": return { case: tag, pickId: rest[0], sketchId: rest[1], constraintIndex: rest[2] };
    case "PickFrameOrigin": return { case: tag, pickId: rest[0], frameId: rest[1] };
    case "PickSurface": return { case: tag, pickId: rest[0], actionId: rest[1] };
    default: return { case: tag, values: rest };
  }
}

function normalizeDocumentView(value: unknown): unknown {
  if (!isRecord(value)) return value;
  return {
    name: value.Name,
    actions: Array.isArray(value.Actions) ? value.Actions.map(normalizeAction) : [],
    selectedId: value.SelectedId ?? null,
    selectedTargets: Array.isArray(value.SelectedTargets) ? value.SelectedTargets.map(normalizeSelectionTarget) : [],
    sketchUi: normalizeSketchUi(value.SketchUi),
    refOptions: toMapObject(value.RefOptions, (entryValue) => Array.isArray(entryValue) ? entryValue : []),
    sketchLoops: toMapObject(value.SketchLoops, (entryValue) => Array.isArray(entryValue) ? entryValue.map(normalizeSketchLoop) : []),
    errors: Array.isArray(value.Errors) ? value.Errors.map(normalizeActionError) : [],
  };
}

function normalizeActionError(value: unknown): unknown {
  if (!isRecord(value)) return value;
  return { actionId: value.ActionId, key: value.Key, error: value.Error };
}

function normalizeSketchUi(value: unknown): unknown {
  if (!isRecord(value)) return value;
  return {
    editMode: Boolean(value.EditMode),
    tool: value.Tool ?? "none",
    toolPoints: Array.isArray(value.ToolPoints) ? value.ToolPoints.map(normalizeLabelPos) : [],
    editingDimension: value.EditingDimension
      ? {
          sketchId: (value.EditingDimension as Record<string, unknown>).SketchId,
          constraintIndex: (value.EditingDimension as Record<string, unknown>).ConstraintIndex,
          key: (value.EditingDimension as Record<string, unknown>).Key,
          value: (value.EditingDimension as Record<string, unknown>).Value,
        }
      : null,
    constraintPlacementMode: value.ConstraintPlacementMode ?? null,
    constraintPlacementDraft: value.ConstraintPlacementDraft
      ? {
          sketchId: (value.ConstraintPlacementDraft as Record<string, unknown>).SketchId,
          kind: (value.ConstraintPlacementDraft as Record<string, unknown>).Kind,
          clickedRefs: Array.isArray((value.ConstraintPlacementDraft as Record<string, unknown>).ClickedRefs)
            ? ((value.ConstraintPlacementDraft as Record<string, unknown>).ClickedRefs as unknown[]).map((entry) => normalizeUnionRef(entry))
            : [],
        }
      : null,
    pendingConstraintPlacement: value.PendingConstraintPlacement
      ? {
          sketchId: (value.PendingConstraintPlacement as Record<string, unknown>).SketchId,
          constraint: normalizeSketchConstraint((value.PendingConstraintPlacement as Record<string, unknown>).Constraint),
        }
      : null,
    constraintAvailability: toMapObject(value.ConstraintAvailability, (entryValue) => Boolean(entryValue)),
    dimensionPlacementAvailability: toMapObject(value.DimensionPlacementAvailability, (entryValue) => Boolean(entryValue)),
  };
}

function normalizeUnionRef(value: unknown): unknown {
  if (!Array.isArray(value) || typeof value[0] !== "string") return value;
  const [tag, ...rest] = value;
  return { case: tag, ...(rest[0] && isRecord(rest[0]) ? rest[0] : {}) };
}

function normalizeViewerModel(value: unknown): unknown {
  if (!isRecord(value)) return value;
  return {
    surfaces: Array.isArray(value.Surfaces) ? value.Surfaces : [],
    fieldWgsl: value.FieldWgsl ?? null,
    fieldSliceWgsl: value.FieldSliceWgsl ?? null,
    fieldSurfaceActionIds: Array.isArray(value.FieldSurfaceActionIds) ? value.FieldSurfaceActionIds : [],
    sketches: Array.isArray(value.Sketches) ? value.Sketches.map(normalizeViewerSketch) : [],
    numSlots: Number(value.NumSlots ?? 0),
    slotIndex: Array.isArray(value.SlotIndex)
      ? value.SlotIndex.map((entry) => isRecord(entry) ? { actionId: entry.ActionId, path: entry.Path, slot: entry.Slot } : entry)
      : [],
    pickables: Array.isArray(value.Pickables) ? value.Pickables.map(normalizePickable) : [],
  };
}

function normalizeViewerState(value: unknown): unknown {
  if (!isRecord(value)) return value;
  return {
    params: Array.isArray(value.Params) ? value.Params : [],
    selectedId: value.SelectedId ?? null,
    hoveredTarget: normalizeSelectionTarget(value.HoveredTarget),
    highlightedTarget: normalizeSelectionTarget(value.HighlightedTarget),
    dragTarget: normalizeSelectionTarget(value.DragTarget),
    selectedTargets: Array.isArray(value.SelectedTargets) ? value.SelectedTargets.map(normalizeSelectionTarget) : [],
    highlightedTargets: Array.isArray(value.HighlightedTargets) ? value.HighlightedTargets.map(normalizeSelectionTarget) : [],
    visibleDimensionSketchIds: Array.isArray(value.VisibleDimensionSketchIds) ? value.VisibleDimensionSketchIds : [],
    sketchUi: normalizeSketchUi(value.SketchUi),
    frames: Array.isArray(value.Frames) ? value.Frames.map(normalizeFrameView) : [],
    sketchEditFrames: Array.isArray(value.SketchEditFrames) ? value.SketchEditFrames.map(normalizeFrameView) : [],
    sketchFrames: Array.isArray(value.SketchFrames) ? value.SketchFrames.map(normalizeFrameView) : [],
    fieldSlices: Array.isArray(value.FieldSlices) ? value.FieldSlices.map(normalizeFieldSliceView) : [],
    visible: toMapObject(value.Visible, (entryValue) => Boolean(entryValue)),
    constraintLabelPositions: Array.isArray(value.ConstraintLabelPositions) ? value.ConstraintLabelPositions.map(normalizeConstraintLabelPosition) : [],
    display: toMapObject(value.Display, normalizeDisplayStateView),
    errors: Array.isArray(value.Errors) ? value.Errors.map(normalizeActionError) : [],
  };
}

function normalizeFrameView(value: unknown): unknown {
  if (!isRecord(value)) return value;
  return { id: value.Id, transform: normalizeTransform(value.Transform) };
}

function normalizeFieldSliceView(value: unknown): unknown {
  if (!isRecord(value)) return value;
  return {
    surfaceIndex: value.SurfaceIndex,
    planeOrigin: normalizeJsonVec3(value.PlaneOrigin),
    planeX: normalizeJsonVec3(value.PlaneX),
    planeY: normalizeJsonVec3(value.PlaneY),
    extent: value.Extent,
  };
}

function normalizeConstraintLabelPosition(value: unknown): unknown {
  if (!isRecord(value)) return value;
  return {
    sketchId: value.SketchId,
    constraintIndex: value.ConstraintIndex,
    position: normalizeLabelPos(value.Position),
  };
}

function normalizeDisplayStateView(value: unknown): unknown {
  if (!isRecord(value)) return value;
  return {
    display: normalizeDisplay(value.Display),
    fieldSlice: normalizeFieldSliceSettings(value.FieldSlice),
  };
}

function normalizePaletteView(value: unknown): unknown {
  if (!isRecord(value)) return value;
  return {
    isOpen: Boolean(value.IsOpen),
    mode: value.Mode ?? "command",
    pickedKind: value.PickedKind ?? null,
    chips: Array.isArray(value.Chips) ? value.Chips.map((chip) => isRecord(chip) ? { label: chip.Label, value: chip.Value } : chip) : [],
    prompt: value.Prompt ?? "",
    items: Array.isArray(value.Items) ? value.Items.map((item) => isRecord(item) ? { id: item.Id, label: item.Label, kind: item.Kind } : item) : [],
    scalarFields: Array.isArray(value.ScalarFields) ? value.ScalarFields.map((field) => isRecord(field) ? { key: field.Key, label: field.Label, value: field.Value } : field) : [],
    hintBar: Array.isArray(value.HintBar) ? value.HintBar : [],
  };
}

function normalizeSerializedModel(value: unknown): unknown {
  if (!isRecord(value)) return value;
  return {
    name: value.Name,
    actions: Array.isArray(value.Actions) ? value.Actions.map(normalizeAction) : [],
  };
}

export function paramValueFromJs(value: unknown): ParamValue {
  if (value === null || value === undefined) return new ParamValue(0, []);
  if (typeof value === "boolean") return new ParamValue(1, [value]);
  if (typeof value === "number") {
    return Number.isInteger(value) ? new ParamValue(2, [value]) : new ParamValue(3, [value]);
  }
  if (typeof value === "string") return new ParamValue(4, [value]);
  if (Array.isArray(value)) return new ParamValue(5, [listOfArray(value.map(paramValueFromJs))]);
  if (typeof value === "object") {
    return new ParamValue(6, [mapOfArray(Object.entries(value).map(([k, v]) => [k, paramValueFromJs(v)]))]);
  }
  throw new Error("Unsupported param value");
}

function normalizeSketchPlaneValue(value: unknown): unknown {
  if (typeof value === "string") return { case: value };
  return value;
}

function normalizeActionForLoad(action: unknown): unknown {
  if (!action || typeof action !== "object") return action;
  const kind = (action as { kind?: unknown }).kind;
  if (!kind || typeof kind !== "object") return action;
  const kindCase = (kind as { case?: unknown }).case;
  if (kindCase !== "Sketch") return action;
  return {
    ...(action as Record<string, unknown>),
    kind: {
      ...(kind as Record<string, unknown>),
      plane: normalizeSketchPlaneValue((kind as { plane?: unknown }).plane),
    },
  };
}

export function normalizeSerializedModelForLoad(model: unknown): SerializedModel {
  const record = (model ?? {}) as { name?: unknown; actions?: unknown[] };
  return new SerializedModel(
    typeof record.name === "string" ? record.name : "untitled",
    listOfArray((record.actions ?? []).map(normalizeActionForLoad) as never[]),
  );
}

export function selectionCandidatesFromJs(candidates: Array<{ pickId: number; score: number }>) {
  return listOfArray(candidates.map((candidate) => ({
    PickId: candidate.pickId,
    Score: candidate.score,
  })));
}

export const editorStore = createStore<EditorState, Message>(Editor_initState(), Editor_update);

const viewerStateListeners = new Set<() => void>();
const viewerModelListeners = new Set<() => void>();

export function dispatchEditor(message: Message, _kind?: "state" | "model") {
  const before = editorStore.getState();
  editorStore.dispatch(message);
  const after = editorStore.getState();
  if (before.Doc !== after.Doc || before.Compiled !== after.Compiled) {
    for (const listener of viewerModelListeners) listener();
    return;
  }
  if (before !== after) {
    for (const listener of viewerStateListeners) listener();
  }
}

export function subscribeViewerState(listener: () => void) {
  viewerStateListeners.add(listener);
  return () => {
    viewerStateListeners.delete(listener);
  };
}

export function subscribeViewerModel(listener: () => void) {
  viewerModelListeners.add(listener);
  return () => {
    viewerModelListeners.delete(listener);
  };
}

export function selectDocumentView() {
  return normalizeDocumentView(toPlain(Editor_documentView(editorStore.getState())));
}

export function selectViewerModel() {
  return normalizeViewerModel(toPlain(Editor_viewerModel(editorStore.getState())));
}

export function selectViewerState() {
  return normalizeViewerState(toPlain(Editor_viewerState(editorStore.getState())));
}

export function selectPaletteView() {
  return normalizePaletteView(toPlain(Editor_paletteView(editorStore.getState())));
}

export function selectSerializedModel() {
  return normalizeSerializedModel(toPlain(Editor_serializedModel(editorStore.getState())));
}
