module PointerMk18.Ui.Dom

open Browser.Dom
open Browser.Types

// ---------------------------------------------------------------------------
// Thin DOM helpers. Ported from user-interface/src/dom.ts. Intended to stay
// small — if a helper needs more than ~5 lines or leaks framework ideas,
// put it somewhere else.
// ---------------------------------------------------------------------------

/// Create an element with a class. For elements without a class, pass "".
let el (tag: string) (className: string) : HTMLElement =
    let e = document.createElement tag
    if className <> "" then e.className <- className
    e

/// Create an element with a class and initial text content.
let elText (tag: string) (className: string) (text: string) : HTMLElement =
    let e = el tag className
    e.textContent <- text
    e

/// <kbd class="kbd-hint">keys</kbd> — the small key-hint badges in the UI.
let kbdHint (keys: string) : HTMLElement =
    elText "kbd" "kbd-hint" keys

/// Same as kbdHint but with a hover tooltip.
let kbdHintTitled (keys: string) (tooltip: string) : HTMLElement =
    let e = kbdHint keys
    e.title <- tooltip
    e

// ---------------------------------------------------------------------------
// Drag-to-edit a numeric value. Used by the parameter editor and by sketch
// dimension handles.
//
// Behaviour (matching dom.ts:18):
//   - pointerdown  captures the pointer and records starting x + value
//   - pointermove  updates the element's text content and fires onRapid
//                  (shift held → fine-grained 0.1 step, else 1.0 step)
//   - pointerup    fires onCommit with the last dragged value
//   - dblclick     replaces the element with an <input type="number">;
//                  onCommit fires on blur or Enter, Escape restores
// ---------------------------------------------------------------------------
let setupDraggable
        (elem: HTMLElement)
        (initial: float)
        (onRapid: float -> unit)
        (onCommit: float -> unit)
        : unit =

    let mutable startX = 0.0
    let mutable startVal = initial
    let mutable dragging = false
    let mutable lastVal = initial

    elem.addEventListener (
        "pointerdown",
        fun e ->
            let pe = e :?> PointerEvent
            startX <- pe.clientX
            startVal <- initial
            dragging <- true
            lastVal <- initial
            elem.classList.add "is-dragging"
            elem.setPointerCapture pe.pointerId
    )

    elem.addEventListener (
        "pointermove",
        fun e ->
            if dragging then
                let pe = e :?> PointerEvent
                let dx = pe.clientX - startX
                let step = if pe.shiftKey then 0.1 else 1.0
                let newVal = round ((startVal + dx * step) * 10.0) / 10.0
                elem.textContent <- sprintf "%.1f" newVal
                lastVal <- newVal
                onRapid newVal
    )

    elem.addEventListener (
        "pointerup",
        fun _ ->
            if dragging then
                dragging <- false
                elem.classList.remove "is-dragging"
                onCommit lastVal
    )

    elem.addEventListener (
        "dblclick",
        fun _ ->
            let input = document.createElement "input" :?> HTMLInputElement
            input.``type`` <- "number"
            input.className <- "control-value-input"
            input.value <- elem.textContent
            elem.parentNode.replaceChild (input, elem) |> ignore
            input.focus ()
            input.select ()

            let commit () =
                let v =
                    match System.Double.TryParse input.value with
                    | true, x -> x
                    | _ -> initial
                onCommit v

            input.addEventListener ("blur", fun _ -> commit ())
            input.addEventListener (
                "keydown",
                fun e ->
                    let ke = e :?> KeyboardEvent
                    if ke.key = "Enter" then input.blur ()
                    if ke.key = "Escape" then
                        input.value <- sprintf "%.1f" initial
                        input.blur ()
            )
    )
