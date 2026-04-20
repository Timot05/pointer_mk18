import { round } from "./fable_modules/fable-library-js.4.29.0/Util.js";
import { printf, toText } from "./fable_modules/fable-library-js.4.29.0/String.js";
import { tryParse } from "./fable_modules/fable-library-js.4.29.0/Double.js";
import { FSharpRef } from "./fable_modules/fable-library-js.4.29.0/Types.js";

/**
 * Create an element with a class. For elements without a class, pass "".
 */
export function el(tag, className) {
    const e = document.createElement(tag);
    if (className !== "") {
        e.className = className;
    }
    return e;
}

/**
 * Create an element with a class and initial text content.
 */
export function elText(tag, className, text) {
    const e = el(tag, className);
    e.textContent = text;
    return e;
}

export function kbdHint(keys) {
    return elText("kbd", "kbd-hint", keys);
}

/**
 * Same as kbdHint but with a hover tooltip.
 */
export function kbdHintTitled(keys, tooltip) {
    const e = kbdHint(keys);
    e.title = tooltip;
    return e;
}

export function setupDraggable(elem, initial, onRapid, onCommit) {
    let startX = 0;
    let startVal = initial;
    let dragging = false;
    let lastVal = initial;
    elem.addEventListener("pointerdown", (e) => {
        const pe = e;
        startX = pe.clientX;
        startVal = initial;
        dragging = true;
        lastVal = initial;
        elem.classList.add("is-dragging");
        elem.setPointerCapture(pe.pointerId);
    });
    elem.addEventListener("pointermove", (e_1) => {
        if (dragging) {
            const pe_1 = e_1;
            const dx = pe_1.clientX - startX;
            const step = pe_1.shiftKey ? 0.1 : 1;
            const newVal = round((startVal + (dx * step)) * 10) / 10;
            elem.textContent = toText(printf("%.1f"))(newVal);
            lastVal = newVal;
            onRapid(newVal);
        }
    });
    elem.addEventListener("pointerup", (_arg) => {
        if (dragging) {
            dragging = false;
            elem.classList.remove("is-dragging");
            onCommit(lastVal);
        }
    });
    elem.addEventListener("dblclick", (_arg_1) => {
        const input = document.createElement("input");
        input.type = "number";
        input.className = "control-value-input";
        input.value = elem.textContent;
        elem.parentNode.replaceChild(input, elem);
        input.focus();
        input.select();
        input.addEventListener("blur", (_arg_2) => {
            let matchValue, outArg;
            onCommit((matchValue = ((outArg = 0, [tryParse(input.value, new FSharpRef(() => outArg, (v) => {
                outArg = v;
            })), outArg])), matchValue[0] ? matchValue[1] : initial));
        });
        input.addEventListener("keydown", (e_2) => {
            const ke = e_2;
            if (ke.key === "Enter") {
                input.blur();
            }
            if (ke.key === "Escape") {
                input.value = toText(printf("%.1f"))(initial);
                input.blur();
            }
        });
    });
}

