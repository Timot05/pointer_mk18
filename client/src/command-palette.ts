// ---------------------------------------------------------------------------
// Command palette — dumb renderer driven by backend state
// ---------------------------------------------------------------------------

import { renderIconForKind } from "./icons";
import {
  paletteOpen, paletteQuery, paletteQueryRapid, palettePick, paletteScalarRapid,
  paletteScalarsCommit, paletteFinish, paletteBack, paletteClose,
  type PaletteItem, type PaletteState, type PaletteAndDoc, type Document,
} from "./api";
import { el, setupDraggable } from "./dom";

let backdrop: HTMLElement | null = null;
let activeIndex = 0;
let currentItems: PaletteItem[] = [];
let onDocUpdate: ((doc: Document) => void) | null = null;
let cleanupKeydown: (() => void) | null = null;

// ── Helpers ───────────────────────────────────────────────────────────

function isPaletteAndDoc(r: PaletteState | PaletteAndDoc): r is PaletteAndDoc {
  return "document" in r;
}

// ── Public API ────────────────────────────────────────────────────────

export function isOpen(): boolean {
  return backdrop !== null;
}

export async function open(onDoc: (doc: Document) => void): Promise<void> {
  if (backdrop) return;
  onDocUpdate = onDoc;
  activeIndex = 0;
  const state = await paletteOpen();
  mount(state);
}

export async function close(): Promise<void> {
  unmount();
  await paletteClose();
  onDocUpdate = null;
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

function handleResponse(r: PaletteState | PaletteAndDoc) {
  if (isPaletteAndDoc(r)) {
    unmount();
    onDocUpdate?.(r.document);
    if (r.palette.isOpen) {
      activeIndex = 0;
      mount(r.palette);
    }
  } else if (r.isOpen) {
    activeIndex = 0;
    mount(r);
  } else {
    unmount();
  }
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
        (v) => paletteScalarRapid(field.key, v),
        (v) => paletteScalarRapid(field.key, v)
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
        const items = await paletteQueryRapid(input!.value);
        patchResults(items);
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
        paletteBack().then((r) => { activeIndex = 0; mount(r as PaletteState); });
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
          paletteFinish().then(handleResponse);
        } else {
          const item = currentItems[activeIndex];
          if (item) palettePick(item.id).then(handleResponse);
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
        paletteBack().then((r) => { activeIndex = 0; mount(r as PaletteState); });
      } else if (e.key === "Enter") {
        e.preventDefault();
        if (e.metaKey || e.ctrlKey) {
          paletteFinish().then(handleResponse);
        } else {
          paletteScalarsCommit().then(handleResponse);
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
      btn.addEventListener("click", () => palettePick(item.id).then(handleResponse));
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

