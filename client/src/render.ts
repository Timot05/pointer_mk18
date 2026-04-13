// ---------------------------------------------------------------------------
// Stateless UI renderer — takes a Document, produces DOM
// ---------------------------------------------------------------------------

import type { Document, Action, ActionKind, ActionError } from "./api";
import { renderIcon, renderIconForKind } from "./icons";
import { el, setupDraggable } from "./dom";

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
export type OnParamChange = (actionId: string, key: string, value: number | string | boolean) => void;
export type OnParamRapid = (actionId: string, key: string, value: number | string | boolean) => void;
export type OnAddAction = (kindCase: string) => void;
export type OnOpenPalette = () => void;
export type OnReorder = (ids: string[]) => void;

export interface RenderCallbacks {
  onSelect: OnSelect;
  onToggleVisible: OnToggleVisible;
  onParamChange: OnParamChange;
  onParamRapid: OnParamRapid;
  onAddAction: OnAddAction;
  onOpenPalette: OnOpenPalette;
  onReorder: OnReorder;
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

export function render(doc: Document, cb: RenderCallbacks): void {
  const app = document.getElementById("app")!;
  app.innerHTML = "";

  // ── Top bar
  const topbar = el("div", "topbar");
  topbar.appendChild(el("span", "topbar-logo", "pointer mk18"));
  topbar.appendChild(el("span", "topbar-spacer"));
  app.appendChild(topbar);

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
  const vp = el("div", "viewport-placeholder", "WebGPU viewport");
  center.appendChild(vp);
  layout.appendChild(center);

  // Right panel — params
  const right = el("div", "panel");
  const rightHeader = el("div", "panel-header");
  rightHeader.appendChild(el("h2", "", "Properties"));
  right.appendChild(rightHeader);
  right.appendChild(renderParamsPanel(doc, cb));
  layout.appendChild(right);

  app.appendChild(layout);
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
      break;

    case "FromSketch":
      strip.appendChild(controlRef("sketch", kind.child, refOptsFor("child"), selected.id, "child", cb));
      strip.appendChild(controlCheck("closed", kind.closed, selected.id, "closed", cb));
      if (kind.closed) {
        strip.appendChild(controlCheck("flip", kind.flip, selected.id, "flip", cb));
      }
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

