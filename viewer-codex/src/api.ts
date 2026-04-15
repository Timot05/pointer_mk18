import { graphFromJson, type Graph, type GraphJson } from "./graph";

export interface JsonVec3 { x: number; y: number; z: number }
export interface JsonQuat { w: number; x: number; y: number; z: number }
export interface JsonRigidTransform { rot: JsonQuat; trans: JsonVec3 }

export interface SlotIndexEntry {
  actionId: string;
  path: string;
  slot: number;
}

export type RenderEntity =
  | { case: "REPoint"; id: string; x: number; y: number }
  | { case: "RELine"; id: string; startId: string; endId: string }
  | { case: "RECircle"; id: string; center: string; radius: number }
  | { case: "REArc"; id: string; startId: string; endId: string; data: ArcData };

export type ArcData =
  | { case: "ArcCenter"; center: string; clockwise: boolean }
  | { case: "ArcThreePoint"; through: { x: number; y: number } };

export type SketchConstraint =
  | { case: "Fixed"; point: string; x: number; y: number }
  | { case: "Horizontal"; a: string; b: string }
  | { case: "Vertical"; a: string; b: string }
  | { case: "Distance"; a: string; b: string; distance: number }
  | { case: "CircleDiameter"; circle: string; center: string; diameter: number }
  | { case: "Angle"; aStart: string; aEnd: string; bStart: string; bEnd: string; lineA: string; lineB: string; angleDegrees: number; aReverse: boolean; bReverse: boolean; ccwFromAToB: boolean }
  | { case: string; [key: string]: unknown };

export interface ActionSketch {
  entities: RenderEntity[];
  constraints: SketchConstraint[];
}

export interface SketchLoop {
  id: string;
  entityIds: string[];
}

export interface ViewerSketch {
  id: string;
  origin: string | null;
  sketch: ActionSketch;
  graph: Graph;
  loops: SketchLoop[];
}

export interface Pickable {
  case: string;
  pickId: number;
  sketchId?: string;
  entityId?: string;
  loopId?: string;
  entityIds?: string[];
  actionId?: string;
  constraintIndex?: number;
}

export interface ViewerModel {
  surfaces: unknown[];
  sketches: ViewerSketch[];
  numSlots: number;
  slotIndex: SlotIndexEntry[];
  pickables: Pickable[];
}

interface ViewerSketchJson extends Omit<ViewerSketch, "graph"> {
  graph: GraphJson;
}

interface ViewerModelJson extends Omit<ViewerModel, "sketches"> {
  sketches: ViewerSketchJson[];
}

export interface ViewerState {
  params: number[];
  selectedId: string | null;
  hoveredTarget: SelectionTarget | null;
  highlightedTarget: SelectionTarget | null;
  dragTarget: SelectionTarget | null;
  selectedTargets: SelectionTarget[];
  highlightedTargets: SelectionTarget[];
  visibleDimensionSketchIds: string[];
  sketchUi: SketchUiState;
  frames: Array<{ id: string; transform: JsonRigidTransform }>;
  sketchFrames: Array<{ id: string; transform: JsonRigidTransform }>;
  visible: Record<string, boolean>;
  constraintLabelPositions: Array<{ sketchId: string; constraintIndex: number; position: { x: number; y: number } }>;
  display: Record<string, unknown>;
  errors: Array<{ actionId: string; key: string; error: string }>;
}

export type SelectionTarget =
  | { case: "TargetPoint"; sketchId: string; entityId: string }
  | { case: "TargetLine"; sketchId: string; entityId: string }
  | { case: "TargetCircle"; sketchId: string; entityId: string }
  | { case: "TargetArc"; sketchId: string; entityId: string }
  | { case: "TargetLoop"; sketchId: string; loopId: string }
  | { case: "TargetDimension"; sketchId: string; constraintIndex: number }
  | { case: "TargetSurface"; actionId: string };

export interface SketchUiState {
  editMode: boolean;
  tool: string;
  toolPoints: Array<{ x: number; y: number }>;
  editingDimension: { sketchId: string; constraintIndex: number; key: string; value: number } | null;
  constraintPlacementMode: string | null;
  constraintAvailability: Record<string, boolean>;
  dimensionPlacementAvailability: Record<string, boolean>;
}

const BASE = "/api";

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(BASE + path, {
    headers: { "Content-Type": "application/json" },
    ...init,
  });
  if (!res.ok) throw new Error(`${init?.method ?? "GET"} ${path}: ${res.status}`);
  return res.json();
}

export async function getViewerModel(): Promise<ViewerModel> {
  const model = await request<ViewerModelJson>("/viewer/model");
  return {
    ...model,
    sketches: model.sketches.map((sketch) => ({
      ...sketch,
      graph: graphFromJson(sketch.graph),
    })),
  };
}

export function getViewerState(): Promise<ViewerState> {
  return request("/viewer/state");
}

export function postViewerHover(candidates: Array<{ pickId: number; score: number }>): Promise<ViewerState> {
  return request("/viewer/hover", {
    method: "POST",
    body: JSON.stringify({ candidates }),
  });
}

export function postViewerPick(
  candidates: Array<{ pickId: number; score: number }>,
  intent: "replace" | "toggle",
): Promise<ViewerState> {
  return request("/viewer/pick", {
    method: "POST",
    body: JSON.stringify({ candidates, intent }),
  });
}

export function patchActionParam(id: string, key: string, value: number | string | boolean): Promise<Record<string, unknown>> {
  return request(`/document/action/${id}/param`, {
    method: "PATCH",
    body: JSON.stringify({ key, value }),
  });
}

export function patchViewerSketchParams(
  actionId: string,
  params: Array<{ key: string; value: number }>,
): Promise<ViewerState> {
  return request("/viewer/sketch-params", {
    method: "PATCH",
    body: JSON.stringify({ actionId, params }),
  });
}

export function replaceViewerSketch(actionId: string, sketch: ActionSketch): Promise<ViewerState> {
  return request("/viewer/sketch", {
    method: "PUT",
    body: JSON.stringify({ actionId, sketch }),
  });
}

export function placeViewerConstraint(x: number, y: number): Promise<ViewerState> {
  return request("/viewer/place-constraint", {
    method: "POST",
    body: JSON.stringify({ x, y }),
  });
}

export function postViewerToolClick(x: number, y: number): Promise<ViewerState> {
  return request("/viewer/tool-click", {
    method: "POST",
    body: JSON.stringify({ x, y }),
  });
}

export function postStartEditingDimension(constraintIndex: number): Promise<ViewerState> {
  return request("/viewer/dimension-edit/start", {
    method: "POST",
    body: JSON.stringify({ constraintIndex }),
  });
}
