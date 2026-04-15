import { getDocument, exportModel, importModel, clearDocumentModel, selectAction, patchActionParam, patchActionParamRapid, toggleActionVisible, toggleDisplay, patchDisplay, toggleFieldSlice, patchFieldSlice, addAction, deleteCurrentSelection, reorderActions, toggleSketchEdit, setSketchTool, toggleConstraintPlacement, addConstraintFromSelection, deleteSketchConstraint, type Action, type Document, type ActionKind } from "./api";
import { render, type RenderCallbacks } from "./render";
import * as palette from "./command-palette";

export interface UserInterfaceMountOptions {
  embedded?: boolean;
  centerContent?: HTMLElement | null;
  onViewerStateDirty?: () => void;
  onViewerModelDirty?: () => void;
  subscribeDocumentDirty?: (listener: () => void) => () => void;
}

function defaultKind(kindCase: string): ActionKind | null {
  switch (kindCase) {
    case "Sphere": return { case: "Sphere", radius: 8 };
    case "Cylinder": return { case: "Cylinder", radius: 5, height: 20 };
    case "Box": return { case: "Box", width: 10, height: 10, depth: 10 };
    case "HalfPlane": return { case: "HalfPlane", axis: "Z", offset: 0, flip: false };
    case "Translate": return { case: "Translate", child: null, x: 0, y: 0, z: 0 };
    case "Rotate": return { case: "Rotate", child: null, ax: 0, ay: 0, az: 1, angle: 0 };
    case "Move": return { case: "Move", child: null, frame: null };
    case "Union": return { case: "Union", a: null, b: null, radius: 0 };
    case "Subtract": return { case: "Subtract", a: null, b: null, radius: 0 };
    case "Intersect": return { case: "Intersect", a: null, b: null, radius: 0 };
    case "Sketch": return { case: "Sketch", origin: null, plane: "XY", sketch: { entities: [], constraints: [] } };
    case "FromSketch": return { case: "FromSketch", child: null, flip: false, selection: { case: "SelectionLoop", loopId: null } };
    case "Thicken": return { case: "Thicken", child: null, amount: 2 };
    case "Shell": return { case: "Shell", child: null, thickness: 1 };
    case "Mesh": return { case: "Mesh", child: null, size: 0.2, resolution: 96 };
    default: return null;
  }
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

function emitViewerInvalidation(kind: "state" | "model"): void {
  if (kind === "state") mountOptions.onViewerStateDirty?.();
  else mountOptions.onViewerModelDirty?.();
}

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
    palette.open(refresh, {
      onViewerStateDirty: mountOptions.onViewerStateDirty,
      onViewerModelDirty: mountOptions.onViewerModelDirty,
    });
  }
}

async function saveDocumentModel(): Promise<void> {
  const model = await exportModel();
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
  const result = await importModel(model);
  refresh(result.document);
  emitViewerInvalidation(result.viewerInvalidation);
}

async function resetDocumentModel(): Promise<void> {
  const result = await clearDocumentModel();
  refresh(result.document);
  emitViewerInvalidation(result.viewerInvalidation);
}

const callbacks: RenderCallbacks = {
  onSelect: async (id) => {
    refresh(await selectAction(id));
    mountOptions.onViewerStateDirty?.();
  },

  onToggleVisible: async (id) => {
    refresh(await toggleActionVisible(id));
    mountOptions.onViewerStateDirty?.();
  },

  onAddAction: async (kindCase) => {
    const kind = defaultKind(kindCase);
    if (!kind) return;
    const id = kindCase.toLowerCase() + "_" + Math.random().toString(36).slice(2, 8);
    refresh(await addAction({ id, name: null, kind, visible: true, display: null, fieldSlice: null }));
    mountOptions.onViewerModelDirty?.();
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
    refresh(await reorderActions(ids));
    mountOptions.onViewerModelDirty?.();
  },

  onParamRapid: (actionId, key, value) => {
    void patchActionParamRapid(actionId, key, value).then((kind) => {
      emitViewerInvalidation(kind);
    });
  },

  onParamChange: async (actionId, key, value) => {
    const result = await patchActionParam(actionId, key, value);
    refresh(result.document);
    emitViewerInvalidation(result.viewerInvalidation);
  },

  onToggleDisplay: async (id) => {
    refresh(await toggleDisplay(id));
    mountOptions.onViewerStateDirty?.();
  },

  onDisplayChange: async (id, key, value) => {
    refresh(await patchDisplay(id, key, value));
    mountOptions.onViewerStateDirty?.();
  },

  onToggleFieldSlice: async (id) => {
    refresh(await toggleFieldSlice(id));
    mountOptions.onViewerStateDirty?.();
  },

  onFieldSliceChange: async (id, key, value) => {
    refresh(await patchFieldSlice(id, key, value));
    mountOptions.onViewerStateDirty?.();
  },

  onToggleSketchEdit: () => {
    void toggleSketchEdit().then((result) => {
      refresh(result.document);
      emitViewerInvalidation(result.viewerInvalidation);
    });
  },

  getSketchEditMode: () => doc?.sketchUi.editMode ?? sketchEditMode,

  onSetSketchTool: async (tool) => {
    const result = await setSketchTool(tool);
    refresh(result.document);
    emitViewerInvalidation(result.viewerInvalidation);
  },

  onToggleConstraintPlacement: async (kind) => {
    const result = await toggleConstraintPlacement(kind);
    refresh(result.document);
    emitViewerInvalidation(result.viewerInvalidation);
  },

  onAddConstraintFromSelection: async (kind) => {
    const result = await addConstraintFromSelection(kind);
    refresh(result.document);
    emitViewerInvalidation(result.viewerInvalidation);
  },

  onDeleteSketchConstraint: async (index) => {
    const result = await deleteSketchConstraint(index);
    refresh(result.document);
    emitViewerInvalidation(result.viewerInvalidation);
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
      const result = await toggleConstraintPlacement(doc.sketchUi.constraintPlacementMode);
      refresh(result.document);
      emitViewerInvalidation(result.viewerInvalidation);
      return true;
    }
    if (doc.sketchUi.tool !== "none") {
      const result = await setSketchTool("none");
      refresh(result.document);
      emitViewerInvalidation(result.viewerInvalidation);
      return true;
    }
    const result = await toggleSketchEdit();
    refresh(result.document);
    emitViewerInvalidation(result.viewerInvalidation);
    return true;
  }

  const tool = e.shiftKey ? SKETCH_TOOL_SHIFT_SHORTCUTS[key] : SKETCH_TOOL_SHORTCUTS[key];
  if (tool) {
    e.preventDefault();
    const result = await setSketchTool(tool);
    refresh(result.document);
    emitViewerInvalidation(result.viewerInvalidation);
    return true;
  }

  const dimension = !e.shiftKey ? SKETCH_DIMENSION_SHORTCUTS[key] : undefined;
  if (dimension) {
    e.preventDefault();
    const result = await toggleConstraintPlacement(dimension);
    refresh(result.document);
    emitViewerInvalidation(result.viewerInvalidation);
    return true;
  }

  const constraint = e.shiftKey ? SKETCH_CONSTRAINT_SHIFT_SHORTCUTS[key] : SKETCH_CONSTRAINT_SHORTCUTS[key];
  if (constraint && doc.sketchUi.constraintAvailability[constraint]) {
    e.preventDefault();
    const result = await addConstraintFromSelection(constraint);
    refresh(result.document);
    emitViewerInvalidation(result.viewerInvalidation);
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
    const result = await deleteCurrentSelection();
    refresh(result.document);
    emitViewerInvalidation(result.viewerInvalidation);
  } else if (e.key === "ArrowDown" || e.key === "ArrowUp") {
    e.preventDefault();
    const idx = doc.actions.findIndex((a) => a.id === doc!.selectedId);
    const next = e.key === "ArrowDown"
      ? Math.min(idx + 1, doc.actions.length - 1)
      : Math.max(idx - 1, 0);
    if (next !== idx) {
      refresh(await selectAction(doc.actions[next].id));
      mountOptions.onViewerStateDirty?.();
    }
  } else if (e.key === "v") {
    const sel = doc.actions.find((a: Action) => a.id === doc!.selectedId);
    if (sel && sel.kind.case !== "Origin") {
      e.preventDefault();
      refresh(await toggleActionVisible(sel.id));
      mountOptions.onViewerStateDirty?.();
    }
  } else if (e.key === "s") {
    const sel = doc.actions.find((a: Action) => a.id === doc!.selectedId);
    if (sel && sel.display) {
      e.preventDefault();
      refresh(await toggleDisplay(sel.id));
      mountOptions.onViewerStateDirty?.();
    }
  } else if (e.key === "e" || e.key === "E") {
    const sel = doc.actions.find((a: Action) => a.id === doc!.selectedId);
    if (sel && sel.kind.case === "Sketch") {
      e.preventDefault();
      const result = await toggleSketchEdit();
      refresh(result.document);
      emitViewerInvalidation(result.viewerInvalidation);
    }
  } else if (e.key === "f") {
    const sel = doc.actions.find((a: Action) => a.id === doc!.selectedId);
    if (sel && sel.fieldSlice) {
      e.preventDefault();
      refresh(await toggleFieldSlice(sel.id));
      mountOptions.onViewerStateDirty?.();
    }
  }
});

export async function mountUserInterface(root: HTMLElement, options: UserInterfaceMountOptions = {}): Promise<void> {
  mountRoot = root;
  mountOptions = options;
  mountOptions.subscribeDocumentDirty?.(() => {
    void getDocument().then(refresh);
  });
  refresh(await getDocument());
}
