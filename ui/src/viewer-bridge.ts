// ---------------------------------------------------------------------------
// viewer-bridge — the single TS↔F# boundary for the 3D viewer.
//
// The F# UI code in this folder is the primary consumer of the store; TS
// viewer code imports from here to read and dispatch. This file exists
// because the viewer is still TS and needs a way to consume F# union/
// record values as plain JS objects.
//
// NOTE: The app's store is owned by F# (see src/AppStore.fs). We import
// the singleton and wrap it. Do not create another store here.
// ---------------------------------------------------------------------------
import { store as editorStore } from "../src-gen/AppStore.js";
import { dispatch as storeDispatch, subscribe as storeSubscribe } from "../src-gen/Store.js";
import { ViewerPipeline_viewerModel, ViewerPipeline_viewerState } from "../src-gen/core/ViewerPipeline.js";
import { ofArray as listOfArray } from "../src-gen/fable_modules/fable-library-js.4.24.0/List.js";

// ── Normalization helpers (Fable-encoded → plain JS) ──────────────────

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
      return { case: tag, through: { x: (rest[0] as any)?.X ?? (rest[0] as any)?.x ?? 0, y: (rest[0] as any)?.Y ?? (rest[0] as any)?.y ?? 0 } };
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

function normalizeLabelPos(value: unknown): unknown {
  if (!value) return null;
  if (!isRecord(value)) return value;
  return { x: value.X ?? value.x ?? 0, y: value.Y ?? value.y ?? 0 };
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

function normalizeSketchLoop(value: unknown): unknown {
  if (!isRecord(value)) return value;
  return {
    id: value.Id,
    entityIds: Array.isArray(value.EntityIds) ? value.EntityIds : [],
  };
}

function normalizeViewerSketch(value: unknown): unknown {
  if (!isRecord(value)) return value;
  return {
    id: value.Id,
    origin: value.Origin ?? null,
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

function normalizeDisplayStateView(value: unknown): unknown {
  if (!isRecord(value)) return value;
  return {
    display: normalizeDisplay(value.Display),
    fieldSlice: normalizeFieldSliceSettings(value.FieldSlice),
  };
}

function normalizeActionError(value: unknown): unknown {
  if (!isRecord(value)) return value;
  return { actionId: value.ActionId, key: value.Key, error: value.Error };
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
    sketchTransforms: Array.isArray(value.SketchTransforms) ? value.SketchTransforms.map(normalizeFrameView) : [],
    fieldSlices: Array.isArray(value.FieldSlices) ? value.FieldSlices.map(normalizeFieldSliceView) : [],
    visible: toMapObject(value.Visible, (entryValue) => Boolean(entryValue)),
    constraintLabelPositions: Array.isArray(value.ConstraintLabelPositions) ? value.ConstraintLabelPositions.map(normalizeConstraintLabelPosition) : [],
    display: toMapObject(value.Display, normalizeDisplayStateView),
    errors: Array.isArray(value.Errors) ? value.Errors.map(normalizeActionError) : [],
  };
}

export function selectionCandidatesFromJs(candidates: Array<{ pickId: number; score: number }>) {
  return listOfArray(candidates.map((candidate) => ({
    PickId: candidate.pickId,
    Score: candidate.score,
  })));
}

// ── Public dispatch + selectors ───────────────────────────────────────

export function dispatchEditor(message: unknown) {
  storeDispatch(editorStore, message);
}

const state = (editorStore as unknown as { State: { Doc: unknown; Compiled: unknown } });

export function selectViewerModel() {
  return normalizeViewerModel(toPlain(ViewerPipeline_viewerModel(state.State)));
}
export function selectViewerState() {
  return normalizeViewerState(toPlain(ViewerPipeline_viewerState(state.State)));
}

// ── Viewer listeners (model vs state split for efficiency) ────────────
//
// Model listeners fire when Compiled topology changes — the viewer rebuilds
// its GPU buffers. State listeners fire on other changes (hover, drag,
// target, etc.) — the viewer only needs to update derived rendering.
// We diff the state ourselves inside the single F# store subscription so
// F# dispatches and TS dispatches both route through the same path.

const viewerModelListeners = new Set<() => void>();
const viewerStateListeners = new Set<() => void>();

let lastCompiled = state.State.Compiled;

storeSubscribe(editorStore, () => {
  const newCompiled = state.State.Compiled;
  const modelChanged = newCompiled !== lastCompiled;
  lastCompiled = newCompiled;
  if (modelChanged) {
    for (const l of viewerModelListeners) l();
  } else {
    for (const l of viewerStateListeners) l();
  }
});

export function subscribeViewerModel(listener: () => void): () => void {
  viewerModelListeners.add(listener);
  return () => { viewerModelListeners.delete(listener); };
}
export function subscribeViewerState(listener: () => void): () => void {
  viewerStateListeners.add(listener);
  return () => { viewerStateListeners.delete(listener); };
}

// ── Re-export the F# message factories for viewer.ts ──────────────────

export {
  viewerHover,
  viewerPick,
  beginPointDrag,
  beginConstraintLabelDrag,
  updateSketchDrag,
  finishSketchDrag,
  cancelSketchDrag,
  viewerToolClick,
  viewerPlaceConstraint,
  viewerDimensionClickTarget,
  startEditingDimension,
  commitEditingDimension,
  cancelEditingDimension,
  setConstraintPlacementCursor,
} from "../src-gen/ViewerMessages.js";
