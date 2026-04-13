// ---------------------------------------------------------------------------
// API client — single source of truth for backend communication
// ---------------------------------------------------------------------------

const BASE = "/api";

export interface Action {
  id: string;
  name: string | null;
  kind: ActionKind;
  visible: boolean;
  children: string[];
}

export type ActionKind =
  | { case: "Origin" }
  | { case: "Cylinder"; radius: number; height: number }
  | { case: "Sphere"; radius: number }
  | { case: "Box"; width: number; height: number; depth: number }
  | { case: "HalfPlane"; axis: string; offset: number; flip: boolean }
  | { case: "Translate"; x: number; y: number; z: number }
  | { case: "Rotate"; axis: string; angle: number }
  | { case: "Move"; child: string | null; frame: string | null }
  | { case: "Union"; a: string | null; b: string | null; radius: number }
  | { case: "Subtract"; a: string | null; b: string | null; radius: number }
  | { case: "Intersect"; a: string | null; b: string | null; radius: number }
  | { case: "Sketch" }
  | { case: "FromSketch"; child: string | null; closed: boolean; flip: boolean }
  | { case: "Thicken"; child: string | null; amount: number }
  | { case: "Shell"; child: string | null; thickness: number }
  | { case: "Mesh"; child: string | null; size: number; resolution: number };

export interface Document {
  name: string;
  actions: Action[];
  selectedId: string | null;
}

async function request(url: string, opts?: RequestInit): Promise<Document> {
  const res = await fetch(BASE + url, {
    headers: { "Content-Type": "application/json" },
    ...opts,
  });
  return res.json();
}

export async function getDocument(): Promise<Document> {
  return request("/document");
}

export async function selectAction(id: string): Promise<Document> {
  return request(`/document/select/${id}`, { method: "PUT" });
}

export async function updateAction(action: Action): Promise<Document> {
  return request(`/document/action/${action.id}`, {
    method: "PUT",
    body: JSON.stringify(action),
  });
}

export async function patchActionParam(id: string, key: string, value: number | string | boolean): Promise<Document> {
  return request(`/document/action/${id}/param`, {
    method: "PATCH",
    body: JSON.stringify({ key, value }),
  });
}

export function patchActionParamRapid(id: string, key: string, value: number | string | boolean): void {
  fetch(BASE + `/document/action/${id}/param/rapid`, {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ key, value }),
  });
}

export async function toggleActionVisible(id: string): Promise<Document> {
  return request(`/document/action/${id}/visible`, { method: "PATCH" });
}

export async function addAction(action: Action): Promise<Document> {
  return request("/document/action", {
    method: "POST",
    body: JSON.stringify(action),
  });
}

export async function deleteAction(id: string): Promise<Document> {
  return request(`/document/action/${id}`, { method: "DELETE" });
}

export async function reorderActions(ids: string[]): Promise<Document> {
  return request("/document/reorder", {
    method: "PUT",
    body: JSON.stringify(ids),
  });
}
