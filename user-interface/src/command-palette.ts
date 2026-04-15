// ---------------------------------------------------------------------------
// Command palette — dumb renderer driven by backend state
// ---------------------------------------------------------------------------

import { renderIconForKind } from "./icons";
import { type PaletteItem, type PaletteState } from "./api";
import { el, setupDraggable } from "./dom";
import {
  dispatchEditor,
  selectDocumentView,
  selectPaletteView,
} from "../../app/src/editor-store";
import {
  Editor_msgPaletteBack,
  Editor_msgPaletteClose,
  Editor_msgPaletteCommitScalars,
  Editor_msgPaletteFinish,
  Editor_msgPaletteOpen,
  Editor_msgPalettePick,
  Editor_msgPaletteSetQuery,
  Editor_msgPaletteSetScalarField,
} from "../../app/src-gen/core/Editor";

let backdrop: HTMLElement | null = null;
let activeIndex = 0;
let currentItems: PaletteItem[] = [];
let cleanupKeydown: (() => void) | null = null;

// ── Public API ────────────────────────────────────────────────────────

export function isOpen(): boolean {
  return backdrop !== null;
}

export async function open(): Promise<void> {
  if (backdrop) return;
  activeIndex = 0;
  dispatchEditor(Editor_msgPaletteOpen);
  mount(selectPaletteView() as PaletteState);
}

export async function close(): Promise<void> {
  unmount();
  dispatchEditor(Editor_msgPaletteClose);
}

// ── Mount / unmount ───────────────────────────────────────────────────

function unmount() {
  if (cleanupKeydown) {
    cleanupKeydown();
    cleanupKeydown = null;
  }
  if (backdrop) {
    backdrop.remove();
    backdrop = null;
  }
}

function syncPalette(afterModelChange = false) {
  const state = selectPaletteView() as PaletteState;
  if (!state.isOpen) {
    unmount();
    return;
  }
  activeIndex = 0;
  mount(state);
}

function mount(state: PaletteState) {
  unmount();
  if (!state.isOpen) return;

  backdrop = el("div", "palette-backdrop");
  const palette = el("div", "palette");
  palette.setAttribute("role", "dialog");

  // ── Top row: chips + input/prompt
  const row = el("div", "palette-row");

  if (state.pickedKind) {
    const cmdChip = el("span", "chip chip-command");
    cmdChip.appendChild(renderIconForKind(state.pickedKind));
    cmdChip.appendChild(el("span", "", state.pickedKind));
    row.appendChild(cmdChip);
  }

  for (const chip of state.chips) {
    const c = el("span", "chip");
    c.appendChild(el("span", "chip-label", chip.label + ":"));
    c.appendChild(el("span", "chip-value", chip.value));
    row.appendChild(c);
  }

  // Text input for command and ref modes
  let input: HTMLInputElement | null = null;
  if (state.mode === "command" || state.mode === "ref") {
    input = document.createElement("input");
    input.type = "text";
    input.className = "palette-input";
    input.placeholder = state.prompt;
    input.spellcheck = false;
    input.autocomplete = "off";
    row.appendChild(input);
  } else if (state.mode === "scalars") {
    // Just a prompt label for context
    if (state.chips.length === 0 && state.pickedKind) {
      row.appendChild(el("span", "prompt-label", "set values:"));
    }
  }

  palette.appendChild(row);

  // ── Scalar fields row (draggable values)
  if (state.mode === "scalars" && state.scalarFields.length > 0) {
    const valRow = el("div", "value-row");
    for (const field of state.scalarFields) {
      const cell = el("div", "value-cell");
      cell.appendChild(el("span", "value-axis", field.label));
      const valSpan = el("span", "control-value", field.value.toFixed(1));
      setupDraggable(
        valSpan, field.value,
        (v) => {
          dispatchEditor(Editor_msgPaletteSetScalarField(field.key, v));
        },
        (v) => {
          dispatchEditor(Editor_msgPaletteSetScalarField(field.key, v));
        }
      );
      cell.appendChild(valSpan);
      valRow.appendChild(cell);
    }
    palette.appendChild(valRow);
  }

  // ── Results list (command and ref modes)
  if (state.mode === "command" || state.mode === "ref") {
    currentItems = state.items;
    palette.appendChild(buildResultsList(state.items));
  }

  // ── Hint bar
  const hintBar = el("div", "palette-hint");
  for (const h of state.hintBar) {
    hintBar.appendChild(el("span", "", h));
  }
  palette.appendChild(hintBar);

  backdrop.appendChild(palette);
  document.body.appendChild(backdrop);

  // ── Input events
  if (input) {
    let debounce: ReturnType<typeof setTimeout> | null = null;

    input.addEventListener("input", () => {
      if (debounce) clearTimeout(debounce);
      debounce = setTimeout(async () => {
        dispatchEditor(Editor_msgPaletteSetQuery(input!.value));
        const next = selectPaletteView() as PaletteState;
        patchResults(next.items);
      }, 80);
    });

    input.addEventListener("keydown", (e) => {
      if (e.key === "Escape") {
        e.preventDefault();
        e.stopPropagation();
        close();
        return;
      }
      if (e.key === "Backspace" && input!.value === "" && state.mode !== "command") {
        e.preventDefault();
        dispatchEditor(Editor_msgPaletteBack);
        syncPalette();
        return;
      }
      if (e.key === "ArrowDown") {
        e.preventDefault();
        if (state.items.length > 0) activeIndex = (activeIndex + 1) % state.items.length;
        palette.querySelectorAll(".palette-item").forEach((el, j) => {
          el.classList.toggle("is-active", j === activeIndex);
        });
      } else if (e.key === "ArrowUp") {
        e.preventDefault();
        if (state.items.length > 0) activeIndex = (activeIndex - 1 + state.items.length) % state.items.length;
        palette.querySelectorAll(".palette-item").forEach((el, j) => {
          el.classList.toggle("is-active", j === activeIndex);
        });
      } else if (e.key === "Enter") {
        e.preventDefault();
        if (e.metaKey || e.ctrlKey) {
          dispatchEditor(Editor_msgPaletteFinish(Math.random().toString(36).slice(2, 8)));
          syncPalette(true);
        } else {
          const item = currentItems[activeIndex];
          if (item) {
            const actionCountBefore = (selectDocumentView() as { actions: unknown[] }).actions.length;
            dispatchEditor(Editor_msgPalettePick(item.id));
            const actionCountAfter = (selectDocumentView() as { actions: unknown[] }).actions.length;
            syncPalette(actionCountAfter !== actionCountBefore);
          }
        }
      }
    });

    requestAnimationFrame(() => input!.focus());
  }

  // ── Global keydown for scalar mode (no text input to capture keys)
  if (state.mode === "scalars") {
    function onScalarKeydown(e: KeyboardEvent) {
      if (e.key === "Escape") {
        e.preventDefault();
        close();
      } else if (e.key === "Backspace") {
        e.preventDefault();
        dispatchEditor(Editor_msgPaletteBack);
        syncPalette();
      } else if (e.key === "Enter") {
        e.preventDefault();
        if (e.metaKey || e.ctrlKey) {
          dispatchEditor(Editor_msgPaletteFinish(Math.random().toString(36).slice(2, 8)));
          syncPalette(true);
        } else {
          const actionCountBefore = (selectDocumentView() as { actions: unknown[] }).actions.length;
          dispatchEditor(Editor_msgPaletteCommitScalars);
          const actionCountAfter = (selectDocumentView() as { actions: unknown[] }).actions.length;
          syncPalette(actionCountAfter !== actionCountBefore);
        }
      }
    }
    document.addEventListener("keydown", onScalarKeydown);
    cleanupKeydown = () => document.removeEventListener("keydown", onScalarKeydown);
  }

  backdrop.addEventListener("click", (e) => {
    if (e.target === backdrop) close();
  });
}

// ── Results list builder ─────────────────────────────────────────────

function buildResultsList(items: PaletteItem[]): HTMLElement {
  const results = el("div", "palette-results");
  if (items.length === 0) {
    results.appendChild(el("div", "palette-empty", "No matches"));
  } else {
    if (activeIndex >= items.length) activeIndex = Math.max(0, items.length - 1);
    items.forEach((item, i) => {
      const btn = el("button", "palette-item");
      if (i === activeIndex) btn.classList.add("is-active");
      btn.appendChild(renderIconForKind(item.kind));
      btn.appendChild(el("span", "palette-label", item.label));
      btn.addEventListener("mouseenter", () => {
        activeIndex = i;
        results.querySelectorAll(".palette-item").forEach((el, j) => {
          el.classList.toggle("is-active", j === i);
        });
      });
      btn.addEventListener("click", () => {
        const actionCountBefore = (selectDocumentView() as { actions: unknown[] }).actions.length;
        dispatchEditor(Editor_msgPalettePick(item.id));
        const actionCountAfter = (selectDocumentView() as { actions: unknown[] }).actions.length;
        syncPalette(actionCountAfter !== actionCountBefore);
      });
      results.appendChild(btn);
    });
  }
  return results;
}

function patchResults(items: PaletteItem[]) {
  if (!backdrop) return;
  const old = backdrop.querySelector(".palette-results");
  if (!old) return;
  activeIndex = 0;
  currentItems = items;
  old.replaceWith(buildResultsList(items));
}
