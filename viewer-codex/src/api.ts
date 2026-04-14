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
  | { case: "Distance"; a: string; b: string; distance: number; labelPosition?: { x: number; y: number } | null }
  | { case: "CircleDiameter"; circle: string; center: string; diameter: number; labelPosition?: { x: number; y: number } | null }
  | { case: "Angle"; aStart: string; aEnd: string; bStart: string; bEnd: string; lineA: string; lineB: string; angleDegrees: number; aReverse: boolean; bReverse: boolean; ccwFromAToB: boolean; labelPosition?: { x: number; y: number } | null }
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
  sketchFrame: JsonRigidTransform;
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
  display: Record<string, unknown>;
  errors: Array<{ actionId: string; key: string; error: string }>;
}

export interface DocumentPayload {
  selectedId: string | null;
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

export function postViewerPick(pickId: number): Promise<DocumentPayload> {
  return request("/viewer/pick", {
    method: "POST",
    body: JSON.stringify({ pickId }),
  });
}

export function patchActionParam(id: string, key: string, value: number | string | boolean): Promise<DocumentPayload> {
  return request(`/document/action/${id}/param`, {
    method: "PATCH",
    body: JSON.stringify({ key, value }),
  });
}
