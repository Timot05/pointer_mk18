import { getDocument, selectAction, patchActionParam, patchActionParamRapid, toggleActionVisible, addAction, deleteAction, type Document, type ActionKind } from "./api";
import { render, type RenderCallbacks } from "./render";
import * as palette from "./command-palette";

function defaultKind(kindCase: string): ActionKind | null {
  switch (kindCase) {
    case "Sphere": return { case: "Sphere", radius: 8 };
    case "Cylinder": return { case: "Cylinder", radius: 5, height: 20 };
    case "Box": return { case: "Box", width: 10, height: 10, depth: 10 };
    case "HalfPlane": return { case: "HalfPlane", axis: "Z", offset: 0, flip: false };
    case "Translate": return { case: "Translate", x: 0, y: 0, z: 0 };
    case "Rotate": return { case: "Rotate", axis: "Z", angle: 0 };
    case "Move": return { case: "Move", child: null, frame: null };
    case "Union": return { case: "Union", a: null, b: null, radius: 0 };
    case "Subtract": return { case: "Subtract", a: null, b: null, radius: 0 };
    case "Intersect": return { case: "Intersect", a: null, b: null, radius: 0 };
    case "Sketch": return { case: "Sketch" };
    case "FromSketch": return { case: "FromSketch", child: null, closed: true, flip: false };
    case "Thicken": return { case: "Thicken", child: null, amount: 2 };
    case "Shell": return { case: "Shell", child: null, thickness: 1 };
    case "Mesh": return { case: "Mesh", child: null, size: 0.2, resolution: 96 };
    default: return null;
  }
}

let doc: Document | null = null;

function refresh(newDoc: Document) {
  doc = newDoc;
  render(doc, callbacks);
}

function openPalette() {
  if (!palette.isOpen()) {
    palette.open(refresh);
  }
}

const callbacks: RenderCallbacks = {
  onSelect: async (id) => {
    refresh(await selectAction(id));
  },

  onToggleVisible: async (id) => {
    refresh(await toggleActionVisible(id));
  },

  onAddAction: async (kindCase) => {
    const kind = defaultKind(kindCase);
    if (!kind) return;
    const id = kindCase.toLowerCase() + "_" + Math.random().toString(36).slice(2, 8);
    refresh(await addAction({ id, name: null, kind, visible: true, children: [] }));
  },

  onOpenPalette: openPalette,

  onParamRapid: (actionId, key, value) => {
    patchActionParamRapid(actionId, key, value);
  },

  onParamChange: async (actionId, key, value) => {
    refresh(await patchActionParam(actionId, key, value));
  },
};

function isEditable(el: EventTarget | null): boolean {
  if (!el || !(el instanceof HTMLElement)) return false;
  const tag = el.tagName;
  return tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT" || el.isContentEditable;
}

document.addEventListener("keydown", async (e) => {
  if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === "k") {
    e.preventDefault();
    openPalette();
    return;
  }

  if (palette.isOpen()) return;
  if (!doc || isEditable(e.target)) return;

  if (e.key === "Delete" || e.key === "Backspace") {
    const sel = doc.actions.find((a) => a.id === doc!.selectedId);
    if (sel && sel.kind.case !== "Origin") {
      e.preventDefault();
      refresh(await deleteAction(sel.id));
    }
  } else if (e.key === "ArrowDown" || e.key === "ArrowUp") {
    e.preventDefault();
    const idx = doc.actions.findIndex((a) => a.id === doc!.selectedId);
    const next = e.key === "ArrowDown"
      ? Math.min(idx + 1, doc.actions.length - 1)
      : Math.max(idx - 1, 0);
    if (next !== idx) {
      refresh(await selectAction(doc.actions[next].id));
    }
  }
});

// Boot
(async () => {
  refresh(await getDocument());
})();
