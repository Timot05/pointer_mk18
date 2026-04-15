import type { Action, Document } from "./api";
import { render, type RenderCallbacks } from "./render";
import * as palette from "./command-palette";
import {
  dispatchEditor,
  editorStore,
  normalizeSerializedModelForLoad,
  paramValueFromJs,
  selectDocumentView,
  selectSerializedModel,
} from "../../app/src/editor-store";
import {
  Editor_msgAddConstraintFromSelection,
  Editor_msgAddDefaultAction,
  Editor_msgClearModel,
  Editor_msgDeleteIntent,
  Editor_msgPatchActionParamValue,
  Editor_msgPatchDisplayValue,
  Editor_msgPatchFieldSliceValue,
  Editor_msgReorderActions,
  Editor_msgSelectAction,
  Editor_msgToggleActionVisible,
  Editor_msgToggleConstraintPlacement,
  Editor_msgToggleDisplay,
  Editor_msgToggleFieldSlice,
  Editor_msgToggleSketchEdit,
  Editor_msgSetSketchTool,
  Editor_msgDeleteSketchConstraint,
  Editor_msgLoadModel,
} from "../../app/src-gen/core/Editor";
import { ofArray as listOfArray } from "../../app/src-gen/core/fable_modules/fable-library-js.4.24.0/List";

export interface UserInterfaceMountOptions {
  embedded?: boolean;
  centerContent?: HTMLElement | null;
}

let doc: Document | null = null;
let sketchEditMode = false;
let mountRoot: HTMLElement | null = null;
let mountOptions: UserInterfaceMountOptions = {};

const SKETCH_TOOL_SHORTCUTS: Record<string, string> = {
  l: "line",
  g: "rectangle",
  c: "circle",
  u: "arc",
};

const SKETCH_TOOL_SHIFT_SHORTCUTS: Record<string, string> = {
  g: "roundedRectangle",
};

const SKETCH_CONSTRAINT_SHORTCUTS: Record<string, string> = {
  i: "Coincident",
  h: "Horizontal",
  v: "Vertical",
  b: "Parallel",
  t: "Tangent",
  e: "Equal",
};

const SKETCH_CONSTRAINT_SHIFT_SHORTCUTS: Record<string, string> = {
  o: "Concentric",
  l: "Perpendicular",
  m: "Midpoint",
  j: "Fixed",
};

const SKETCH_DIMENSION_SHORTCUTS: Record<string, string> = {
  d: "distance",
  a: "angle",
};

function refresh(newDoc: Document) {
  doc = newDoc;
  const sel = doc.actions.find((a) => a.id === doc!.selectedId);
  if (!sel || sel.kind.case !== "Sketch") sketchEditMode = false;
  if (mountRoot) render(mountRoot, doc, callbacks, mountOptions);
}

function rerender() {
  if (doc && mountRoot) render(mountRoot, doc, callbacks, mountOptions);
}

function openPalette() {
  if (!palette.isOpen()) {
    palette.open();
  }
}

async function saveDocumentModel(): Promise<void> {
  const model = selectSerializedModel() as { name?: string; actions: unknown[] };
  const baseName =
    (model.name ?? "").trim().toLowerCase() === "untitled" || !(model.name ?? "").trim()
      ? "pointer-model"
      : model.name.trim().replace(/[^a-z0-9_-]+/gi, "-").replace(/^-+|-+$/g, "");
  const blob = new Blob([JSON.stringify(model, null, 2)], { type: "application/json" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = `${baseName || "pointer-model"}.json`;
  link.click();
  URL.revokeObjectURL(url);
}

async function loadDocumentModel(file: File): Promise<void> {
  const text = await file.text();
  const model = JSON.parse(text);
  dispatchEditor(Editor_msgLoadModel(normalizeSerializedModelForLoad(model)));
}

async function resetDocumentModel(): Promise<void> {
  dispatchEditor(Editor_msgClearModel);
}

const callbacks: RenderCallbacks = {
  onSelect: async (id) => {
    dispatchEditor(Editor_msgSelectAction(id));
  },

  onToggleVisible: async (id) => {
    dispatchEditor(Editor_msgToggleActionVisible(id));
  },

  onAddAction: async (kindCase) => {
    const id = kindCase.toLowerCase() + "_" + Math.random().toString(36).slice(2, 8);
    dispatchEditor(Editor_msgAddDefaultAction(kindCase, id));
  },

  onOpenPalette: openPalette,

  onSaveDocument: () => {
    void saveDocumentModel();
  },

  onLoadDocument: (file) => {
    void loadDocumentModel(file);
  },

  onClearDocument: () => {
    void resetDocumentModel();
  },

  onReorder: async (ids) => {
    dispatchEditor(Editor_msgReorderActions(listOfArray(ids)));
  },

  onParamRapid: (actionId, key, value) => {
    dispatchEditor(Editor_msgPatchActionParamValue(actionId, key, paramValueFromJs(value)));
  },

  onParamChange: async (actionId, key, value) => {
    dispatchEditor(Editor_msgPatchActionParamValue(actionId, key, paramValueFromJs(value)));
  },

  onToggleDisplay: async (id) => {
    dispatchEditor(Editor_msgToggleDisplay(id));
  },

  onDisplayChange: async (id, key, value) => {
    dispatchEditor(Editor_msgPatchDisplayValue(id, key, paramValueFromJs(value)));
  },

  onToggleFieldSlice: async (id) => {
    dispatchEditor(Editor_msgToggleFieldSlice(id));
  },

  onFieldSliceChange: async (id, key, value) => {
    dispatchEditor(Editor_msgPatchFieldSliceValue(id, key, paramValueFromJs(value)));
  },

  onToggleSketchEdit: () => {
    dispatchEditor(Editor_msgToggleSketchEdit);
  },

  getSketchEditMode: () => doc?.sketchUi.editMode ?? sketchEditMode,

  onSetSketchTool: async (tool) => {
    dispatchEditor(Editor_msgSetSketchTool(tool));
  },

  onToggleConstraintPlacement: async (kind) => {
    dispatchEditor(Editor_msgToggleConstraintPlacement(kind));
  },

  onAddConstraintFromSelection: async (kind) => {
    dispatchEditor(Editor_msgAddConstraintFromSelection(kind));
  },

  onDeleteSketchConstraint: async (index) => {
    dispatchEditor(Editor_msgDeleteSketchConstraint(index));
  },
};

function isEditable(el: EventTarget | null): boolean {
  if (!el || !(el instanceof HTMLElement)) return false;
  const tag = el.tagName;
  return tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT" || el.isContentEditable;
}

function selectedSketch(): Action | null {
  if (!doc) return null;
  const sel = doc.actions.find((action) => action.id === doc.selectedId) ?? null;
  return sel?.kind.case === "Sketch" ? sel : null;
}

async function handleSketchShortcut(e: KeyboardEvent): Promise<boolean> {
  const sketch = selectedSketch();
  if (!sketch || !doc?.sketchUi.editMode) return false;
  if (e.metaKey || e.ctrlKey || e.altKey) return false;

  const key = e.key.toLowerCase();

  if (e.key === "Escape") {
    e.preventDefault();
    if (doc.sketchUi.constraintPlacementMode) {
      dispatchEditor(Editor_msgToggleConstraintPlacement(doc.sketchUi.constraintPlacementMode));
      return true;
    }
    if (doc.sketchUi.tool !== "none") {
      dispatchEditor(Editor_msgSetSketchTool("none"));
      return true;
    }
    dispatchEditor(Editor_msgToggleSketchEdit);
    return true;
  }

  const tool = e.shiftKey ? SKETCH_TOOL_SHIFT_SHORTCUTS[key] : SKETCH_TOOL_SHORTCUTS[key];
  if (tool) {
    e.preventDefault();
    dispatchEditor(Editor_msgSetSketchTool(tool));
    return true;
  }

  const dimension = !e.shiftKey ? SKETCH_DIMENSION_SHORTCUTS[key] : undefined;
  if (dimension) {
    e.preventDefault();
    dispatchEditor(Editor_msgToggleConstraintPlacement(dimension));
    return true;
  }

  const constraint = e.shiftKey ? SKETCH_CONSTRAINT_SHIFT_SHORTCUTS[key] : SKETCH_CONSTRAINT_SHORTCUTS[key];
  if (constraint && doc.sketchUi.constraintAvailability[constraint]) {
    e.preventDefault();
    dispatchEditor(Editor_msgAddConstraintFromSelection(constraint));
    return true;
  }

  return false;
}

document.addEventListener("keydown", async (e) => {
  if ((e.metaKey || e.ctrlKey) && !e.altKey && !e.shiftKey) {
    const key = e.key.toLowerCase();
    if (key === "k") {
      e.preventDefault();
      openPalette();
      return;
    }
    if (key === "s") {
      e.preventDefault();
      void saveDocumentModel();
      return;
    }
    if (key === "o") {
      e.preventDefault();
      document.getElementById("topbar-file-input")?.click?.();
      return;
    }
  }

  if (palette.isOpen()) return;
  if (!doc || isEditable(e.target)) return;
  if (await handleSketchShortcut(e)) return;

  if (e.key === "Delete" || e.key === "Backspace") {
    e.preventDefault();
    dispatchEditor(Editor_msgDeleteIntent);
  } else if (e.key === "ArrowDown" || e.key === "ArrowUp") {
    e.preventDefault();
    const idx = doc.actions.findIndex((a) => a.id === doc!.selectedId);
    const next = e.key === "ArrowDown"
      ? Math.min(idx + 1, doc.actions.length - 1)
      : Math.max(idx - 1, 0);
    if (next !== idx) {
      dispatchEditor(Editor_msgSelectAction(doc.actions[next].id));
    }
  } else if (e.key === "v") {
    const sel = doc.actions.find((a: Action) => a.id === doc!.selectedId);
    if (sel && sel.kind.case !== "Origin") {
      e.preventDefault();
      dispatchEditor(Editor_msgToggleActionVisible(sel.id));
    }
  } else if (e.key === "s") {
    const sel = doc.actions.find((a: Action) => a.id === doc!.selectedId);
    if (sel && sel.display) {
      e.preventDefault();
      dispatchEditor(Editor_msgToggleDisplay(sel.id));
    }
  } else if (e.key === "e" || e.key === "E") {
    const sel = doc.actions.find((a: Action) => a.id === doc!.selectedId);
    if (sel && sel.kind.case === "Sketch") {
      e.preventDefault();
      dispatchEditor(Editor_msgToggleSketchEdit);
    }
  } else if (e.key === "f") {
    const sel = doc.actions.find((a: Action) => a.id === doc!.selectedId);
    if (sel && sel.fieldSlice) {
      e.preventDefault();
      dispatchEditor(Editor_msgToggleFieldSlice(sel.id));
    }
  }
});

export async function mountUserInterface(root: HTMLElement, options: UserInterfaceMountOptions = {}): Promise<void> {
  mountRoot = root;
  mountOptions = options;
  editorStore.subscribe(() => {
    refresh(selectDocumentView() as Document);
  });
  refresh(selectDocumentView() as Document);
}
