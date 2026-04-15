// ---------------------------------------------------------------------------
// Stateless UI renderer — takes a Document, produces DOM
// ---------------------------------------------------------------------------

import type { Document, Action, ActionKind, ActionError, SketchConstraint } from "./api";
import { renderIcon, renderIconForKind } from "./icons";
import { el, kbdHint, setupDraggable } from "./dom";

// ── Kind helpers ──────────────────────────────────────────────────────

function kindLabel(kind: ActionKind): string {
  return kind.case.toLowerCase();
}


function kindSubtitle(kind: ActionKind): string {
  switch (kind.case) {
    case "Cylinder":
      return `r${kind.radius} h${kind.height}`;
    case "Sphere":
      return `r${kind.radius}`;
    case "Box":
      return `${kind.width}\u00D7${kind.height}\u00D7${kind.depth}`;
    case "HalfPlane":
      return `${kind.axis} ${kind.offset}`;
    case "Translate":
      return `${kind.x}, ${kind.y}, ${kind.z}`;
    case "Rotate":
      return `${kind.angle}\u00B0`;
    case "Thicken":
      return `${kind.amount}`;
    case "Shell":
      return `${kind.thickness}`;
    case "Mesh":
      return `${kind.size} \u00D7${kind.resolution}`;
    default:
      return "";
  }
}

function fromSketchLoopOptions(doc: Document, childId: string | null): Array<{ value: string; label: string }> {
  if (!childId) return [];
  const loops = doc.sketchLoops[childId] ?? [];
  return loops.map((loop, index) => ({
    value: loop.id,
    label: `loop ${index + 1}`,
  }));
}

// ── Param fields for each kind ────────────────────────────────────────

interface ParamField {
  label: string;
  key: string;
  value: number;
}

function paramFields(kind: ActionKind): ParamField[] {
  switch (kind.case) {
    case "Cylinder":
      return [
        { label: "r", key: "radius", value: kind.radius },
        { label: "h", key: "height", value: kind.height },
      ];
    case "Sphere":
      return [{ label: "r", key: "radius", value: kind.radius }];
    case "Box":
      return [
        { label: "w", key: "width", value: kind.width },
        { label: "h", key: "height", value: kind.height },
        { label: "d", key: "depth", value: kind.depth },
      ];
    case "HalfPlane":
      return [{ label: "off", key: "offset", value: kind.offset }];
    case "Translate":
      return [
        { label: "x", key: "x", value: kind.x },
        { label: "y", key: "y", value: kind.y },
        { label: "z", key: "z", value: kind.z },
      ];
    case "Rotate":
      return [{ label: "\u03B8", key: "angle", value: kind.angle }];
    case "Union":
    case "Subtract":
    case "Intersect":
      return [{ label: "r", key: "radius", value: kind.radius }];
    case "Thicken":
      return [{ label: "amt", key: "amount", value: kind.amount }];
    case "Shell":
      return [{ label: "t", key: "thickness", value: kind.thickness }];
    case "Mesh":
      return [
        { label: "sz", key: "size", value: kind.size },
        { label: "res", key: "resolution", value: kind.resolution },
      ];
    default:
      return [];
  }
}

// ── Render ─────────────────────────────────────────────────────────────

export type OnSelect = (id: string) => void;
export type OnToggleVisible = (id: string) => void;
export type OnParamChange = (actionId: string, key: string, value: number | string | boolean | object) => void;
export type OnParamRapid = (actionId: string, key: string, value: number | string | boolean) => void;
export type OnAddAction = (kindCase: string) => void;
export type OnOpenPalette = () => void;
export type OnSaveDocument = () => void;
export type OnLoadDocument = (file: File) => void;
export type OnClearDocument = () => void;
export type OnReorder = (ids: string[]) => void;
export type OnToggleDisplay = (id: string) => void;
export type OnDisplayChange = (id: string, key: string, value: number | number[]) => void;
export type OnToggleFieldSlice = (id: string) => void;
export type OnFieldSliceChange = (id: string, key: string, value: number | string) => void;
export type OnToggleSketchEdit = () => void;
export type OnSetSketchTool = (tool: string) => void;
export type OnToggleConstraintPlacement = (kind: string) => void;
export type OnAddConstraintFromSelection = (kind: string) => void;
export type OnDeleteSketchConstraint = (index: number) => void;

export interface RenderCallbacks {
  onSelect: OnSelect;
  onToggleVisible: OnToggleVisible;
  onParamChange: OnParamChange;
  onParamRapid: OnParamRapid;
  onAddAction: OnAddAction;
  onOpenPalette: OnOpenPalette;
  onSaveDocument: OnSaveDocument;
  onLoadDocument: OnLoadDocument;
  onClearDocument: OnClearDocument;
  onReorder: OnReorder;
  onToggleDisplay: OnToggleDisplay;
  onDisplayChange: OnDisplayChange;
  onToggleFieldSlice: OnToggleFieldSlice;
  onFieldSliceChange: OnFieldSliceChange;
  onToggleSketchEdit: OnToggleSketchEdit;
  onSetSketchTool: OnSetSketchTool;
  onToggleConstraintPlacement: OnToggleConstraintPlacement;
  onAddConstraintFromSelection: OnAddConstraintFromSelection;
  onDeleteSketchConstraint: OnDeleteSketchConstraint;
  getSketchEditMode: () => boolean;
}

export interface RenderOptions {
  embedded?: boolean;
  centerContent?: HTMLElement | null;
}

// ── Action templates for the "+" dropdown ─────────────────────────────

const actionTemplates: { label: string; kindCase: string }[] = [
  { label: "Sphere", kindCase: "Sphere" },
  { label: "Cylinder", kindCase: "Cylinder" },
  { label: "Box", kindCase: "Box" },
  { label: "HalfPlane", kindCase: "HalfPlane" },
  { label: "Translate", kindCase: "Translate" },
  { label: "Rotate", kindCase: "Rotate" },
  { label: "Move", kindCase: "Move" },
  { label: "Union", kindCase: "Union" },
  { label: "Subtract", kindCase: "Subtract" },
  { label: "Intersect", kindCase: "Intersect" },
  { label: "Sketch", kindCase: "Sketch" },
  { label: "FromSketch", kindCase: "FromSketch" },
  { label: "Thicken", kindCase: "Thicken" },
  { label: "Shell", kindCase: "Shell" },
  { label: "Mesh", kindCase: "Mesh" },
];

export function render(root: HTMLElement, doc: Document, cb: RenderCallbacks, options: RenderOptions = {}): void {
  root.innerHTML = "";
  root.className = options.embedded ? "ui-root is-embedded" : "ui-root";

  // ── Top bar
  const topbar = el("div", "topbar");
  topbar.appendChild(el("span", "topbar-logo", "pointer mk18"));
  const fileMenu = el("div", "topbar-menu");
  const fileBtn = el("button", "topbar-button", "File");
  const fileDropdown = el("div", "topbar-dropdown");
  fileDropdown.style.display = "none";
  const fileInput = el("input", "") as HTMLInputElement;
  fileInput.id = "topbar-file-input";
  fileInput.type = "file";
  fileInput.accept = "application/json,.json";
  fileInput.style.display = "none";
  fileInput.addEventListener("change", () => {
    const file = fileInput.files?.[0];
    if (!file) return;
    fileDropdown.style.display = "none";
    cb.onLoadDocument(file);
    fileInput.value = "";
  });

  const modKey = navigator.platform.toLowerCase().includes("mac") ? "\u2318" : "Ctrl";

  const makeDropdownItem = (label: string, shortcut?: string): HTMLButtonElement => {
    const btn = el("button", "topbar-dropdown-item") as HTMLButtonElement;
    btn.appendChild(el("span", "", label));
    if (shortcut) btn.appendChild(kbdHint(shortcut));
    return btn;
  };

  const saveBtn = makeDropdownItem("Save", `${modKey}S`);
  saveBtn.addEventListener("click", () => {
    fileDropdown.style.display = "none";
    cb.onSaveDocument();
  });

  const loadBtn = makeDropdownItem("Load", `${modKey}O`);
  loadBtn.addEventListener("click", () => {
    fileDropdown.style.display = "none";
    fileInput.click();
  });

  const clearBtn = makeDropdownItem("Clear");
  clearBtn.addEventListener("click", () => {
    fileDropdown.style.display = "none";
    cb.onClearDocument();
  });

  fileBtn.addEventListener("click", (e) => {
    e.stopPropagation();
    fileDropdown.style.display = fileDropdown.style.display === "none" ? "flex" : "none";
  });

  document.addEventListener("click", () => {
    fileDropdown.style.display = "none";
  });

  fileDropdown.appendChild(saveBtn);
  fileDropdown.appendChild(loadBtn);
  fileDropdown.appendChild(clearBtn);
  fileMenu.appendChild(fileBtn);
  fileMenu.appendChild(fileDropdown);
  fileMenu.appendChild(fileInput);
  topbar.appendChild(fileMenu);
  topbar.appendChild(el("span", "topbar-spacer"));
  root.appendChild(topbar);

  // ── Layout
  const layout = el("div", "layout");

  // Left panel — actions list
  const left = el("div", "panel");
  const leftHeader = el("div", "panel-header");
  leftHeader.appendChild(el("h2", "", "Actions"));

  const paletteBtn = el("button", "palette-hint-btn");
  paletteBtn.innerHTML = '<kbd>\u2318</kbd><span class="palette-hint-plus">+</span><kbd>K</kbd> <span>palette</span>';
  paletteBtn.addEventListener("click", () => cb.onOpenPalette());
  leftHeader.appendChild(paletteBtn);

  const addWrapper = el("div", "add-wrapper");
  const addBtn = el("button", "btn-add", "+");
  const dropdown = el("div", "dropdown");
  dropdown.style.display = "none";

  for (const t of actionTemplates) {
    const item = el("button", "dropdown-item");
    item.appendChild(renderIconForKind(t.kindCase));
    item.appendChild(el("span", "", t.label));
    item.addEventListener("click", () => {
      dropdown.style.display = "none";
      cb.onAddAction(t.kindCase);
    });
    dropdown.appendChild(item);
  }

  addBtn.addEventListener("click", (e) => {
    e.stopPropagation();
    dropdown.style.display = dropdown.style.display === "none" ? "flex" : "none";
  });

  // close dropdown on outside click
  document.addEventListener("click", () => {
    dropdown.style.display = "none";
  });

  addWrapper.appendChild(addBtn);
  addWrapper.appendChild(dropdown);
  leftHeader.appendChild(addWrapper);
  left.appendChild(leftHeader);

  const list = el("div", "actions-list");
  const rows: HTMLElement[] = [];
  let dragIndex: number | null = null;
  let dropIndex: number | null = null;
  let dropPos: "before" | "after" = "after";

  function clearDropIndicators() {
    for (const r of rows) {
      r.classList.remove("drop-before", "drop-after");
    }
  }

  for (let i = 0; i < doc.actions.length; i++) {
    const action = doc.actions[i];
    const hasError = (doc.errors ?? []).some((e) => e.actionId === action.id);
    const row = renderActionRow(action, doc.selectedId, hasError, cb);

    if (action.kind.case !== "Origin") {
      row.draggable = true;
      row.addEventListener("dragstart", (e) => {
        dragIndex = i;
        e.dataTransfer!.effectAllowed = "move";
        e.dataTransfer!.setData("text/plain", String(i));
        requestAnimationFrame(() => row.classList.add("is-dragging"));
      });
    }

    row.addEventListener("dragover", (e) => {
      if (dragIndex == null || dragIndex === i) { dropIndex = null; clearDropIndicators(); return; }
      e.preventDefault();
      e.dataTransfer!.dropEffect = "move";
      const rect = (e.currentTarget as HTMLElement).getBoundingClientRect();
      const before = e.clientY < rect.top + rect.height / 2;
      if (action.kind.case === "Origin" && before) { dropIndex = null; clearDropIndicators(); return; }
      dropPos = before ? "before" : "after";
      dropIndex = i;
      clearDropIndicators();
      row.classList.add(before ? "drop-before" : "drop-after");
    });

    row.addEventListener("dragleave", (e) => {
      if ((e.currentTarget as HTMLElement).contains(e.relatedTarget as Node)) return;
    });

    rows.push(row);
    list.appendChild(row);
  }

  // Drop in empty space → append to end
  list.addEventListener("dragover", (e) => {
    if (dragIndex == null) return;
    if ((e.target as HTMLElement).closest(".action-row")) return;
    e.preventDefault();
    e.dataTransfer!.dropEffect = "move";
    const last = rows.length - 1;
    if (last < 0) return;
    dropIndex = last;
    dropPos = "after";
    clearDropIndicators();
    rows[last].classList.add("drop-after");
  });

  list.addEventListener("drop", (e) => {
    e.preventDefault();
    clearDropIndicators();
    for (const r of rows) r.classList.remove("is-dragging");
    if (dragIndex == null || dropIndex == null) { dragIndex = null; dropIndex = null; return; }

    const ids = doc.actions.map((a) => a.id);
    const [moved] = ids.splice(dragIndex, 1);
    let target = dropIndex + (dropPos === "after" ? 1 : 0);
    if (dragIndex < target) target -= 1;
    ids.splice(target, 0, moved);

    dragIndex = null;
    dropIndex = null;
    cb.onReorder(ids);
  });

  list.addEventListener("dragend", () => {
    dragIndex = null;
    dropIndex = null;
    clearDropIndicators();
    for (const r of rows) r.classList.remove("is-dragging");
  });

  left.appendChild(list);
  layout.appendChild(left);

  // Center — viewport placeholder
  const center = el("div", "panel panel-center");
  if (options.centerContent) {
    options.centerContent.classList.add("panel-center-host");
    center.appendChild(options.centerContent);
  } else {
    const vp = el("div", "viewport-placeholder", "WebGPU viewport");
    center.appendChild(vp);
  }
  renderSketchAuthoringOverlay(center, doc, cb);
  layout.appendChild(center);

  // Right panel — params
  const right = el("div", "panel");
  const rightHeader = el("div", "panel-header");
  rightHeader.appendChild(el("h2", "", "Properties"));
  right.appendChild(rightHeader);
  right.appendChild(renderParamsPanel(doc, cb));
  layout.appendChild(right);

  root.appendChild(layout);
}

function renderSketchAuthoringOverlay(center: HTMLElement, doc: Document, cb: RenderCallbacks): void {
  const selected = doc.actions.find((action) => action.id === doc.selectedId);
  if (!selected || selected.kind.case !== "Sketch" || !doc.sketchUi.editMode) return;

  const overlay = el("div", "sketch-authoring-overlay");
  const tools = [
    { id: "none", label: "select", hint: null },
    { id: "line", label: "line", hint: "L" },
    { id: "rectangle", label: "rect", hint: "G" },
    { id: "roundedRectangle", label: "rrect", hint: "⇧G" },
    { id: "circle", label: "circle", hint: "C" },
    { id: "arc", label: "arc", hint: "U" },
  ];

  const toolbar = el("div", "sketch-toolbar");
  for (const tool of tools) {
    const button = el("button", "sketch-tool-btn");
    (button as HTMLButtonElement).type = "button";
    if (doc.sketchUi.tool === tool.id) button.classList.add("is-active");
    button.appendChild(el("span", "", tool.label));
    if (tool.hint) button.appendChild(el("kbd", "tool-hint", tool.hint));
    button.addEventListener("click", () => cb.onSetSketchTool(tool.id));
    toolbar.appendChild(button);
  }
  if (doc.sketchUi.tool !== "none") {
    toolbar.appendChild(el("span", "sketch-toolbar-hint", "click in the viewer to place geometry"));
  }
  overlay.appendChild(toolbar);

  const panel = el("div", "constraints-panel");
  panel.appendChild(renderConstraintSection("Constraints", geometricConstraintButtons(), doc, selected, cb));
  panel.appendChild(renderConstraintSection("Dimensions", dimensionConstraintButtons(), doc, selected, cb));
  overlay.appendChild(panel);

  center.appendChild(overlay);
}

function geometricConstraintButtons() {
  return [
    { id: "Coincident", label: "Coincident", symbol: "≡", shortcut: "I", dimension: false },
    { id: "Horizontal", label: "Horizontal", symbol: "↔", shortcut: "H", dimension: false },
    { id: "Vertical", label: "Vertical", symbol: "↕", shortcut: "V", dimension: false },
    { id: "Midpoint", label: "Midpoint", symbol: "·|·", shortcut: "⇧M", dimension: false },
    { id: "Parallel", label: "Parallel", symbol: "∥", shortcut: "B", dimension: false },
    { id: "Perpendicular", label: "Perpendicular", symbol: "⊥", shortcut: "⇧L", dimension: false },
    { id: "Equal", label: "Equal", symbol: "=", shortcut: "E", dimension: false },
    { id: "Tangent", label: "Tangent", symbol: "⌒", shortcut: "T", dimension: false },
    { id: "Concentric", label: "Concentric", symbol: "◎", shortcut: "⇧O", dimension: false },
    { id: "Fixed", label: "Fixed", symbol: "⊙", shortcut: "⇧J", dimension: false },
  ];
}

function dimensionConstraintButtons() {
  return [
    { id: "distance", label: "Distance", symbol: "↦", shortcut: "D", dimension: true },
    { id: "angle", label: "Angle", symbol: "∠", shortcut: "A", dimension: true },
  ];
}

function renderConstraintSection(
  title: string,
  buttons: Array<{ id: string; label: string; symbol: string; shortcut: string; dimension: boolean }>,
  doc: Document,
  selected: Action,
  cb: RenderCallbacks,
): HTMLElement {
  const section = el("div", "constraint-section");
  const header = el("div", "constraint-section-header");
  header.appendChild(el("span", "constraint-section-title", title));
  section.appendChild(header);

  const row = el("div", "constraint-add-row");
  for (const item of buttons) {
    const button = el("button", "constraint-add-btn");
    (button as HTMLButtonElement).type = "button";
    const availability = item.dimension
      ? !!doc.sketchUi.dimensionPlacementAvailability[item.id]
      : !!doc.sketchUi.constraintAvailability[item.id];
    (button as HTMLButtonElement).disabled = !availability;
    if (item.dimension && doc.sketchUi.constraintPlacementMode === item.id) button.classList.add("is-active");
    button.appendChild(el("span", "sym", item.symbol));
    button.appendChild(el("span", "btn-label", item.label));
    button.appendChild(el("kbd", "shortcut", item.shortcut));
    button.addEventListener("click", () => {
      if (item.dimension) cb.onToggleConstraintPlacement(item.id);
      else cb.onAddConstraintFromSelection(item.id);
    });
    row.appendChild(button);
  }
  section.appendChild(row);

  if (title === "Dimensions") {
    return section;
  }

  const sketch = selected.kind.sketch;
  const constraints = itemizedConstraints(sketch.constraints, title === "Dimensions");
  if (constraints.length === 0) {
    section.appendChild(el("div", "constraint-empty", title === "Dimensions"
      ? "Use the viewer to place a dimension label."
      : "Select entities in the viewer to enable constraints."));
    return section;
  }

  const list = el("div", "constraint-list");
  for (const entry of constraints) {
    const rowEl = el("div", "constraint-row");
    rowEl.appendChild(el("span", "sym", constraintSymbol(entry.constraint.case)));
    rowEl.appendChild(el("span", "constraint-kind", constraintLabel(entry.constraint.case)));
    rowEl.appendChild(el("span", "constraint-summary", constraintSummary(entry.constraint)));
    const del = el("button", "constraint-delete", "×");
    (del as HTMLButtonElement).type = "button";
    del.addEventListener("click", () => cb.onDeleteSketchConstraint(entry.index));
    rowEl.appendChild(del);
    list.appendChild(rowEl);
  }
  section.appendChild(list);
  return section;
}

function itemizedConstraints(constraints: SketchConstraint[], dimensions: boolean) {
  const dimensionKinds = new Set([
    "Distance",
    "FrameDistance",
    "LineDistance",
    "FrameLineDistance",
    "PointLineDistance",
    "PointCircleDistance",
    "LineCircleDistance",
    "CircleCircleDistance",
    "Angle",
    "CircleDiameter",
  ]);
  return constraints
    .map((constraint, index) => ({ constraint, index }))
    .filter((entry) => dimensionKinds.has(entry.constraint.case) === dimensions);
}

function constraintLabel(kind: string): string {
  return kind === "CurveTangent" ? "Tangent" : kind;
}

function constraintSymbol(kind: string): string {
  switch (kind) {
    case "Fixed": return "⊙";
    case "Coincident":
    case "FrameCoincident": return "≡";
    case "Horizontal": return "↔";
    case "Vertical": return "↕";
    case "Parallel":
    case "FrameParallel": return "∥";
    case "Perpendicular":
    case "FramePerpendicular": return "⊥";
    case "Midpoint": return "·|·";
    case "Tangent":
    case "CurveTangent": return "⌒";
    case "Concentric": return "◎";
    case "Equal":
    case "EqualRadius": return "=";
    case "Angle": return "∠";
    case "CircleDiameter": return "⌀";
    default: return "↦";
  }
}

function constraintSummary(constraint: SketchConstraint): string {
  switch (constraint.case) {
    case "Fixed": return constraint.point;
    case "Coincident":
    case "Horizontal":
    case "Vertical":
    case "Distance": return `${constraint.a} · ${constraint.b}`;
    case "Midpoint":
    case "PointLineDistance": return `${constraint.point} · ${constraint.lineA}`;
    case "Parallel":
    case "Perpendicular":
    case "Equal":
    case "LineDistance":
    case "Angle": return `${constraint.lineA} · ${constraint.lineB}`;
    case "Tangent": return `${constraint.lineA} · ${constraint.circle}`;
    case "Concentric":
    case "EqualRadius": return `${constraint.entityA} · ${constraint.entityB}`;
    case "CircleDiameter": return constraint.circle;
    case "PointCircleDistance": return `${constraint.point} · ${constraint.circle}`;
    case "LineCircleDistance": return `${constraint.lineA} · ${constraint.circle}`;
    case "CircleCircleDistance": return `${constraint.circleA} · ${constraint.circleB}`;
    default: return "";
  }
}


function renderActionRow(
  action: Action,
  selectedId: string | null,
  hasError: boolean,
  cb: RenderCallbacks
): HTMLElement {
  const row = el("div", "action-row");
  if (action.id === selectedId) row.classList.add("is-selected");
  if (action.kind.case === "Origin") row.classList.add("is-fixed");
  if (hasError) row.classList.add("has-error");
  row.addEventListener("click", () => cb.onSelect(action.id));

  const main = el("div", "action-main");

  const icon = el("span", "action-icon");
  icon.appendChild(renderIcon(action.kind));
  main.appendChild(icon);

  const info = el("div", "action-info");
  info.appendChild(el("span", "action-title", action.name ?? kindLabel(action.kind)));
  const sub = kindSubtitle(action.kind);
  if (sub) info.appendChild(el("span", "action-subtitle", sub));
  main.appendChild(info);

  row.appendChild(main);

  // kbd hint + visibility toggle
  if (action.kind.case !== "Origin") {
    if (action.id === selectedId) {
      const kbd = el("kbd", "kbd-hint", "v");
      kbd.title = "Press v to toggle";
      row.appendChild(kbd);
    }

    const vis = el("button", "toggle-btn");
    vis.textContent = "\u25CF";
    if (action.visible) vis.classList.add("is-active");
    vis.addEventListener("click", (e) => {
      e.stopPropagation();
      cb.onToggleVisible(action.id);
    });
    row.appendChild(vis);
  }

  return row;
}

function renderParamsPanel(doc: Document, cb: RenderCallbacks): HTMLElement {
  const container = el("div", "selection-panel");
  const selected = doc.actions.find((a) => a.id === doc.selectedId);

  if (!selected) {
    container.appendChild(el("div", "selection-empty", "Select an action"));
    return container;
  }

  // header
  const header = el("div", "selection-header");
  const headerIcon = el("span", "action-icon");
  headerIcon.classList.add("large");
  headerIcon.appendChild(renderIcon(selected.kind));
  header.appendChild(headerIcon);
  const headerInfo = el("div", "header-info");
  headerInfo.appendChild(el("div", "header-kind", kindLabel(selected.kind)));
  headerInfo.appendChild(el("div", "header-name", selected.name ?? kindLabel(selected.kind)));
  header.appendChild(headerInfo);
  container.appendChild(header);

  // errors for this action
  const actionErrors = (doc.errors ?? []).filter((e) => e.actionId === selected.id);
  if (actionErrors.length > 0) {
    const errSection = el("div", "error-section");
    for (const err of actionErrors) {
      const row = el("div", "error-row");
      row.appendChild(el("span", "error-key", err.key));
      row.appendChild(el("span", "error-msg", err.error));
      errSection.appendChild(row);
    }
    container.appendChild(errSection);
  }

  // controls
  const section = el("div", "param-section");
  section.appendChild(el("div", "controls-hint", "drag values to adjust:"));
  const strip = el("div", "controls-strip");

  // ref options: backend provides valid IDs per slot, map them to actions
  const refOptions = doc.refOptions ?? {};
  const actionById = new Map(doc.actions.map((a) => [a.id, a]));
  const refOptsFor = (key: string): Action[] =>
    (refOptions[key] ?? []).map((id) => actionById.get(id)).filter((a): a is Action => a != null);

  const kind = selected.kind;

  switch (kind.case) {
    case "Origin":
      strip.appendChild(controlStatic("frame", "world"));
      break;

    case "Sphere":
      strip.appendChild(controlDrag("radius", kind.radius, selected.id, "radius", cb));
      break;

    case "Cylinder":
      strip.appendChild(controlDrag("radius", kind.radius, selected.id, "radius", cb));
      strip.appendChild(controlDrag("height", kind.height, selected.id, "height", cb));
      break;

    case "Box":
      strip.appendChild(controlDrag("width", kind.width, selected.id, "width", cb));
      strip.appendChild(controlDrag("height", kind.height, selected.id, "height", cb));
      strip.appendChild(controlDrag("depth", kind.depth, selected.id, "depth", cb));
      break;

    case "HalfPlane":
      strip.appendChild(controlSelect("axis", kind.axis, ["X", "Y", "Z"], selected.id, "axis", cb));
      strip.appendChild(controlDrag("offset", kind.offset, selected.id, "offset", cb));
      strip.appendChild(controlCheck("flip", kind.flip, selected.id, "flip", cb));
      break;

    case "Translate":
      strip.appendChild(controlRef("child", kind.child, refOptsFor("child"), selected.id, "child", cb));
      strip.appendChild(controlDrag("x", kind.x, selected.id, "x", cb));
      strip.appendChild(controlDrag("y", kind.y, selected.id, "y", cb));
      strip.appendChild(controlDrag("z", kind.z, selected.id, "z", cb));
      break;

    case "Rotate":
      strip.appendChild(controlRef("child", kind.child, refOptsFor("child"), selected.id, "child", cb));
      strip.appendChild(controlDrag("ax", kind.ax, selected.id, "ax", cb));
      strip.appendChild(controlDrag("ay", kind.ay, selected.id, "ay", cb));
      strip.appendChild(controlDrag("az", kind.az, selected.id, "az", cb));
      strip.appendChild(controlDrag("angle", kind.angle, selected.id, "angle", cb));
      break;

    case "Move":
      strip.appendChild(controlRef("child", kind.child, refOptsFor("child"), selected.id, "child", cb));
      strip.appendChild(controlRef("frame", kind.frame, refOptsFor("frame"), selected.id, "frame", cb));
      break;

    case "Union":
      strip.appendChild(controlRef("tool", kind.a, refOptsFor("a"), selected.id, "a", cb));
      strip.appendChild(controlRef("target", kind.b, refOptsFor("b"), selected.id, "b", cb));
      strip.appendChild(controlDrag("radius", kind.radius, selected.id, "radius", cb));
      break;

    case "Subtract":
      strip.appendChild(controlRef("tool", kind.a, refOptsFor("a"), selected.id, "a", cb));
      strip.appendChild(controlRef("target", kind.b, refOptsFor("b"), selected.id, "b", cb));
      strip.appendChild(controlDrag("radius", kind.radius, selected.id, "radius", cb));
      break;

    case "Intersect":
      strip.appendChild(controlRef("tool", kind.a, refOptsFor("a"), selected.id, "a", cb));
      strip.appendChild(controlRef("target", kind.b, refOptsFor("b"), selected.id, "b", cb));
      strip.appendChild(controlDrag("radius", kind.radius, selected.id, "radius", cb));
      break;

    case "Sketch":
      strip.appendChild(controlRef("origin", kind.origin, refOptsFor("origin"), selected.id, "origin", cb));
      strip.appendChild(controlSelect("plane", kind.plane, ["XY", "XZ", "YZ"], selected.id, "plane", cb));
      break;

    case "FromSketch":
      strip.appendChild(controlRef("sketch", kind.child, refOptsFor("child"), selected.id, "child", cb));
      strip.appendChild(controlCheck("flip", kind.flip, selected.id, "flip", cb));
      strip.appendChild(controlFromSketchLoop(doc, kind, selected.id, cb));
      break;

    case "Thicken":
      strip.appendChild(controlRef("child", kind.child, refOptsFor("child"), selected.id, "child", cb));
      strip.appendChild(controlDrag("amount", kind.amount, selected.id, "amount", cb));
      break;

    case "Shell":
      strip.appendChild(controlRef("child", kind.child, refOptsFor("child"), selected.id, "child", cb));
      strip.appendChild(controlDrag("thickness", kind.thickness, selected.id, "thickness", cb));
      break;

    case "Mesh":
      strip.appendChild(controlRef("child", kind.child, refOptsFor("child"), selected.id, "child", cb));
      strip.appendChild(controlDrag("size", kind.size, selected.id, "size", cb));
      strip.appendChild(controlDrag("res", kind.resolution, selected.id, "resolution", cb));
      break;
  }

  section.appendChild(strip);
  container.appendChild(section);

  // ── Sketch edit toggle (Sketch nodes only)
  if (kind.case === "Sketch") {
    const sketchEditMode = doc.sketchUi.editMode;
    const sketchSection = el("div", "sketch-edit-section");
    if (sketchEditMode) sketchSection.classList.add("is-active");
    const toggle = el("button", "sketch-edit-toggle");
    (toggle as HTMLButtonElement).type = "button";
    if (sketchEditMode) toggle.classList.add("is-active");
    toggle.appendChild(el("span", "sketch-edit-label", sketchEditMode ? "Exit sketch edit" : "Edit sketch"));
    toggle.appendChild(el("kbd", "kbd-hint", "E"));
    toggle.addEventListener("click", () => cb.onToggleSketchEdit());
    sketchSection.appendChild(toggle);
    container.appendChild(sketchSection);
  }

  // ── Field display settings (backend includes display only for Field-type nodes)
  if (selected.display) {
    const d = selected.display;
    const nodeVisible = selected.visible;
    const displaySection = el("div", "display-section");

    // Section title
    const title = el("div", "section-title");
    title.appendChild(el("span", "", "field display"));
    if (!nodeVisible) {
      const note = el("span", "field-disabled-note");
      const kbd = el("kbd", "kbd-hint", "v");
      note.appendChild(kbd);
      note.appendChild(el("span", "", "to enable"));
      title.appendChild(note);
    }
    displaySection.appendChild(title);

    // Controls wrapper (greyed out when node not visible)
    const controls = el("div", "display-controls");
    if (!nodeVisible) controls.classList.add("is-disabled");

    // Isosurface toggle checkbox
    const check = el("label", "display-check");
    const checkbox = document.createElement("input");
    checkbox.type = "checkbox";
    checkbox.checked = d.enabled;
    checkbox.disabled = !nodeVisible;
    checkbox.addEventListener("change", () => cb.onToggleDisplay(selected.id));
    check.appendChild(checkbox);
    const kbd = el("kbd", "kbd-hint", "s");
    kbd.title = "Press s to toggle";
    check.appendChild(kbd);
    check.appendChild(el("span", "", "Show field iso-surface"));
    controls.appendChild(check);

    if (d.enabled) {
      // Color swatches
      const colorRow = el("div", "control-row color-row");
      colorRow.appendChild(el("span", "control-name", "color"));
      const swatches = el("div", "color-swatches");
      // Roadrunner + Coyote-Roadrunner cartoon palette
      const colors: [string, number[]][] = [
        ["#85AEC8", [0x85 / 255, 0xAE / 255, 0xC8 / 255]], // Urban Sky Blue
        ["#341D7C", [0x34 / 255, 0x1D / 255, 0x7C / 255]], // Supreme Indigo
        ["#F1BA23", [0xF1 / 255, 0xBA / 255, 0x23 / 255]], // Egg Yolk
        ["#FFFFFF", [1.0, 1.0, 1.0]],                       // Full White
        ["#AC6614", [0xAC / 255, 0x66 / 255, 0x14 / 255]], // Reno Sand
        ["#E4D6AF", [0xE4 / 255, 0xD6 / 255, 0xAF / 255]], // Hampton
        ["#7D6400", [0x7D / 255, 0x64 / 255, 0x00 / 255]], // Yukon Gold
        ["#FFFFAA", [1.0, 1.0, 0xAA / 255]],                // Pale Yellow
        ["#D10005", [0xD1 / 255, 0x00 / 255, 0x05 / 255]], // Russian Red
      ];
      for (const [hex, rgb] of colors) {
        const swatch = el("button", "color-swatch");
        swatch.style.background = hex;
        const isActive = d.color.length === 3 &&
          Math.abs(d.color[0] - rgb[0]) < 0.01 &&
          Math.abs(d.color[1] - rgb[1]) < 0.01 &&
          Math.abs(d.color[2] - rgb[2]) < 0.01;
        if (isActive) swatch.classList.add("is-active");
        swatch.addEventListener("click", () => cb.onDisplayChange(selected.id, "color", rgb));
        swatches.appendChild(swatch);
      }
      colorRow.appendChild(swatches);
      controls.appendChild(colorRow);

      // Offset (iso_value)
      const offsetRow = el("div", "control-row");
      offsetRow.appendChild(el("span", "control-name", "offset"));
      const offsetVal = el("span", "control-value", d.isoValue.toFixed(1));
      setupDraggable(
        offsetVal, d.isoValue,
        () => {},
        (v) => cb.onDisplayChange(selected.id, "isoValue", v)
      );
      offsetRow.appendChild(offsetVal);
      controls.appendChild(offsetRow);
    }

    // Field slice toggle
    if (selected.fieldSlice) {
      const fs = selected.fieldSlice;
      const sliceCheck = el("label", "display-check");
      const sliceCheckbox = document.createElement("input");
      sliceCheckbox.type = "checkbox";
      sliceCheckbox.checked = fs.enabled;
      sliceCheckbox.disabled = !nodeVisible;
      sliceCheckbox.addEventListener("change", () => cb.onToggleFieldSlice(selected.id));
      sliceCheck.appendChild(sliceCheckbox);
      const fKbd = el("kbd", "kbd-hint", "f");
      fKbd.title = "Press f to toggle";
      sliceCheck.appendChild(fKbd);
      sliceCheck.appendChild(el("span", "", "Show field iso-lines"));
      controls.appendChild(sliceCheck);

      if (fs.enabled) {
        // Plane selector
        const planeRow = el("div", "control-row");
        planeRow.appendChild(el("span", "control-name", "plane"));
        const planeSelect = document.createElement("select");
        planeSelect.className = "control-select";
        for (const [value, label] of [["Z", "xy"], ["Y", "xz"], ["X", "yz"]] as const) {
          const opt = document.createElement("option");
          opt.value = value;
          opt.textContent = label;
          if (fs.plane === value) opt.selected = true;
          planeSelect.appendChild(opt);
        }
        planeSelect.addEventListener("change", () =>
          cb.onFieldSliceChange(selected.id, "plane", planeSelect.value));
        planeRow.appendChild(planeSelect);
        controls.appendChild(planeRow);

        // Offset
        const sliceOffsetRow = el("div", "control-row");
        sliceOffsetRow.appendChild(el("span", "control-name", "offset"));
        const sliceOffsetVal = el("span", "control-value", fs.offset.toFixed(1));
        setupDraggable(
          sliceOffsetVal, fs.offset,
          () => {},
          (v) => cb.onFieldSliceChange(selected.id, "offset", v)
        );
        sliceOffsetRow.appendChild(sliceOffsetVal);
        controls.appendChild(sliceOffsetRow);

        const sliceExtentRow = el("div", "control-row");
        sliceExtentRow.appendChild(el("span", "control-name", "extent"));
        const sliceExtentVal = el("span", "control-value", fs.extent.toFixed(1));
        setupDraggable(
          sliceExtentVal, fs.extent,
          () => {},
          (v) => cb.onFieldSliceChange(selected.id, "extent", Math.max(0.1, v))
        );
        sliceExtentRow.appendChild(sliceExtentVal);
        controls.appendChild(sliceExtentRow);
      }
    }

    displaySection.appendChild(controls);
    container.appendChild(displaySection);
  }

  return container;
}

// ── Control builders ──────────────────────────────────────────────────

function controlStatic(label: string, value: string): HTMLElement {
  const row = el("div", "control-row");
  row.appendChild(el("span", "control-name", label));
  row.appendChild(el("span", "", value));
  return row;
}

function controlDrag(
  label: string, value: number, actionId: string, key: string, cb: RenderCallbacks
): HTMLElement {
  const row = el("div", "control-row");
  row.appendChild(el("span", "control-name", label));
  const v0 = value ?? 0;
  const val = el("span", "control-value", v0.toFixed(1));
  setupDraggable(
    val, v0,
    (v) => cb.onParamRapid(actionId, key, v),
    (v) => cb.onParamChange(actionId, key, v)
  );
  row.appendChild(val);
  return row;
}

function controlRef(
  label: string, current: string | null, options: Action[], actionId: string, key: string, cb: RenderCallbacks
): HTMLElement {
  const row = el("div", "control-row");
  row.appendChild(el("span", "control-name", label));
  const select = document.createElement("select");
  select.className = "control-ref";
  const none = document.createElement("option");
  none.value = "";
  none.textContent = "\u2013";
  select.appendChild(none);
  for (const opt of options) {
    const o = document.createElement("option");
    o.value = opt.id;
    o.textContent = opt.name ?? kindLabel(opt.kind);
    if (opt.id === current) o.selected = true;
    select.appendChild(o);
  }
  select.addEventListener("change", () => {
    cb.onParamChange(actionId, key, select.value );
  });
  row.appendChild(select);
  return row;
}

function controlSelect(
  label: string, current: string, choices: string[], actionId: string, key: string, cb: RenderCallbacks
): HTMLElement {
  const row = el("div", "control-row");
  row.appendChild(el("span", "control-name", label));
  const select = document.createElement("select");
  select.className = "control-select";
  for (const c of choices) {
    const o = document.createElement("option");
    o.value = c;
    o.textContent = c;
    if (c === current) o.selected = true;
    select.appendChild(o);
  }
  select.addEventListener("change", () => {
    cb.onParamChange(actionId, key, select.value );
  });
  row.appendChild(select);
  return row;
}

function controlCheck(
  label: string, checked: boolean, actionId: string, key: string, cb: RenderCallbacks
): HTMLElement {
  const row = el("div", "control-row control-check");
  const input = document.createElement("input");
  input.type = "checkbox";
  input.checked = checked;
  input.addEventListener("change", () => {
    cb.onParamChange(actionId, key, input.checked );
  });
  row.appendChild(input);
  row.appendChild(el("label", "", label));
  return row;
}

function controlFromSketchLoop(
  doc: Document,
  kind: Extract<ActionKind, { case: "FromSketch" }>,
  actionId: string,
  cb: RenderCallbacks,
): HTMLElement {
  const row = el("div", "control-row");
  row.appendChild(el("span", "control-name", "loop"));
  const select = document.createElement("select");
  select.className = "control-select";
  const auto = document.createElement("option");
  auto.value = "";
  auto.textContent = "first (auto)";
  select.appendChild(auto);
  const currentSelection = kind.selection.case === "SelectionLoop" ? (kind.selection.loopId ?? "") : "";
  const options = fromSketchLoopOptions(doc, kind.child);
  for (const option of options) {
    const o = document.createElement("option");
    o.value = option.value;
    o.textContent = option.label;
    if (option.value === currentSelection) o.selected = true;
    select.appendChild(o);
  }
  select.disabled = !kind.child || options.length === 0;
  select.addEventListener("change", () => {
    cb.onParamChange(actionId, "selection", {
      case: "SelectionLoop",
      loopId: select.value || null,
    });
  });
  row.appendChild(select);
  return row;
}
