// ---------------------------------------------------------------------------
// API client — single source of truth for backend communication
// ---------------------------------------------------------------------------

const BASE = "/api";

export interface DisplaySettings {
  enabled: boolean;
  color: number[];   // [r, g, b] normalized 0-1
  opacity: number;
  isoValue: number;
}

export interface FieldSliceSettings {
  enabled: boolean;
  plane: string;   // "X" | "Y" | "Z"
  offset: number;
  extent: number;
}

export interface Action {
  id: string;
  name: string | null;
  kind: ActionKind;
  visible: boolean;
  display: DisplaySettings | null;
  fieldSlice: FieldSliceSettings | null;
}

export type ActionKind =
  | { case: "Origin" }
  | { case: "Cylinder"; radius: number; height: number }
  | { case: "Sphere"; radius: number }
  | { case: "Box"; width: number; height: number; depth: number }
  | { case: "HalfPlane"; axis: string; offset: number; flip: boolean }
  | { case: "Translate"; child: string | null; x: number; y: number; z: number }
  | { case: "Rotate"; child: string | null; ax: number; ay: number; az: number; angle: number }
  | { case: "Move"; child: string | null; frame: string | null }
  | { case: "Union"; a: string | null; b: string | null; radius: number }
  | { case: "Subtract"; a: string | null; b: string | null; radius: number }
  | { case: "Intersect"; a: string | null; b: string | null; radius: number }
  | { case: "Sketch" }
  | { case: "FromSketch"; child: string | null; closed: boolean; flip: boolean }
  | { case: "Thicken"; child: string | null; amount: number }
  | { case: "Shell"; child: string | null; thickness: number }
  | { case: "Mesh"; child: string | null; size: number; resolution: number };

export interface ActionError {
  actionId: string;
  key: string;
  error: string;
}

export interface Document {
  name: string;
  actions: Action[];
  selectedId: string | null;
  refOptions: Record<string, string[]>;
  errors: ActionError[];
}

async function request(url: string, opts?: RequestInit): Promise<Document> {
  const res = await fetch(BASE + url, {
    headers: { "Content-Type": "application/json" },
    ...opts,
  });
  if (!res.ok) throw new Error(`${opts?.method ?? "GET"} ${url}: ${res.status}`);
  return res.json();
}

export async function getDocument(): Promise<Document> {
  return request("/document");
}

export async function selectAction(id: string): Promise<Document> {
  return request(`/document/select/${id}`, { method: "PUT" });
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

export async function toggleDisplay(id: string): Promise<Document> {
  return request(`/document/action/${id}/display/toggle`, { method: "PATCH" });
}

export async function patchDisplay(id: string, key: string, value: number | number[]): Promise<Document> {
  return request(`/document/action/${id}/display`, {
    method: "PATCH",
    body: JSON.stringify({ key, value }),
  });
}

export async function toggleFieldSlice(id: string): Promise<Document> {
  return request(`/document/action/${id}/field-slice/toggle`, { method: "PATCH" });
}

export async function patchFieldSlice(id: string, key: string, value: number | string): Promise<Document> {
  return request(`/document/action/${id}/field-slice`, {
    method: "PATCH",
    body: JSON.stringify({ key, value }),
  });
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

// ── Palette ───────────────────────────────────────────────────────────

export interface PaletteItem {
  id: string;
  label: string;
  kind: string;
}

export interface PaletteChip {
  label: string;
  value: string;
}

export interface PaletteScalarField {
  key: string;
  label: string;
  value: number;
}

export interface PaletteState {
  isOpen: boolean;
  mode: string; // "closed" | "command" | "ref" | "scalars" | "done"
  pickedKind: string | null;
  chips: PaletteChip[];
  prompt: string;
  items: PaletteItem[];
  scalarFields: PaletteScalarField[];
  hintBar: string[];
}

export interface PaletteAndDoc {
  palette: PaletteState;
  document: Document;
}

async function paletteRequest(url: string, body?: object): Promise<PaletteState | PaletteAndDoc> {
  const res = await fetch(BASE + url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!res.ok) throw new Error(`POST ${url}: ${res.status}`);
  return res.json();
}

export async function paletteOpen(): Promise<PaletteState> {
  return paletteRequest("/palette/open") as Promise<PaletteState>;
}

export async function paletteQuery(query: string): Promise<PaletteState> {
  return paletteRequest("/palette/query", { query }) as Promise<PaletteState>;
}

export async function paletteQueryRapid(query: string): Promise<PaletteItem[]> {
  const res = await fetch(BASE + "/palette/query/rapid", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ query }),
  });
  if (!res.ok) throw new Error(`POST /palette/query/rapid: ${res.status}`);
  return res.json();
}

export async function palettePick(id: string): Promise<PaletteState | PaletteAndDoc> {
  return paletteRequest("/palette/pick", { id });
}

export function paletteScalarRapid(key: string, value: number): void {
  fetch(BASE + "/palette/scalar/rapid", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ key, value }),
  });
}

export async function paletteScalarsCommit(): Promise<PaletteState | PaletteAndDoc> {
  return paletteRequest("/palette/scalars/commit");
}

export async function paletteFinish(): Promise<PaletteState | PaletteAndDoc> {
  return paletteRequest("/palette/finish");
}

export async function paletteBack(): Promise<PaletteState> {
  return paletteRequest("/palette/back") as Promise<PaletteState>;
}

export async function paletteClose(): Promise<void> {
  await fetch(BASE + "/palette/close", { method: "POST" });
}
