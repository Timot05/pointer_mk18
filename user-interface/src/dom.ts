// ---------------------------------------------------------------------------
// Shared DOM helpers
// ---------------------------------------------------------------------------

export function el(tag: string, className: string, text?: string): HTMLElement {
  const e = document.createElement(tag);
  if (className) e.className = className;
  if (text !== undefined) e.textContent = text;
  return e;
}

export function kbdHint(keys: string, tooltip?: string): HTMLElement {
  const hint = el("kbd", "kbd-hint", keys);
  if (tooltip) hint.title = tooltip;
  return hint;
}

export function setupDraggable(
  elem: HTMLElement,
  initial: number,
  onRapid: (v: number) => void,
  onCommit: (v: number) => void
) {
  let startX = 0;
  let startVal = initial;
  let dragging = false;
  let lastVal = initial;

  elem.addEventListener("pointerdown", (e) => {
    startX = e.clientX;
    startVal = initial;
    dragging = true;
    lastVal = initial;
    elem.classList.add("is-dragging");
    elem.setPointerCapture(e.pointerId);
  });

  elem.addEventListener("pointermove", (e) => {
    if (!dragging) return;
    const dx = e.clientX - startX;
    const step = e.shiftKey ? 0.1 : 1.0;
    const newVal = Math.round((startVal + dx * step) * 10) / 10;
    elem.textContent = newVal.toFixed(1);
    lastVal = newVal;
    onRapid(newVal);
  });

  elem.addEventListener("pointerup", () => {
    if (!dragging) return;
    dragging = false;
    elem.classList.remove("is-dragging");
    onCommit(lastVal);
  });

  // double-click to type
  elem.addEventListener("dblclick", () => {
    const input = document.createElement("input");
    input.type = "number";
    input.className = "control-value-input";
    input.value = elem.textContent ?? "";
    elem.replaceWith(input);
    input.focus();
    input.select();

    const commit = () => {
      const v = parseFloat(input.value) || initial;
      onCommit(v);
    };
    input.addEventListener("blur", commit);
    input.addEventListener("keydown", (e) => {
      if (e.key === "Enter") input.blur();
      if (e.key === "Escape") {
        input.value = initial.toFixed(1);
        input.blur();
      }
    });
  });
}
