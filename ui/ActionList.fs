module PointerMk18.Ui.ActionList

open Fable.Core
open Fable.Core.JsInterop
open Server
open PointerMk18.Ui
open Browser.Dom
open Browser.Types

// ---------------------------------------------------------------------------
// Left panel: the list of actions in the current document. Ported from
// render.ts:244–371 with the row renderer from render.ts:588–633.
// ---------------------------------------------------------------------------

// ── Action kind → display helpers ──────────────────────────────────────

let kindLabel (kind: ActionKind) : string =
    match kind with
    | Origin -> "origin"
    | Cylinder _ -> "cylinder"
    | Sphere _ -> "sphere"
    | Box _ -> "box"
    | HalfPlane _ -> "halfplane"
    | Translate _ -> "translate"
    | Rotate _ -> "rotate"
    | Move _ -> "move"
    | Union _ -> "union"
    | Subtract _ -> "subtract"
    | Intersect _ -> "intersect"
    | Sketch _ -> "sketch"
    | FromSketch _ -> "fromsketch"
    | Thicken _ -> "thicken"
    | Shell _ -> "shell"
    | Mesh _ -> "mesh"

// ── Wire-mode helpers ──────────────────────────────────────────────────
//
// When an action is in "wire its inputs" mode (Enter on the selected
// action sets `doc.WiringActionId`), each of its ref slots surfaces as
// a draggable "bubble":
//   * If the slot is unassigned, the bubble sits in a tray row below
//     the wired action.
//   * If the slot is assigned, the bubble sits on the target action's
//     drop-zone column.
// Dragging a bubble onto an earlier action's drop zone sets that slot
// to the earlier action. Clicking an already-placed bubble detaches it.

type WireSlot =
    { FullLabel: string          // "tool", "target", "child", ...
      AcceptedKey: string        // "a" | "b" | "child" | "frame" | "origin" — matches TypeCheck.acceptedInputs
      Field: ActionParamField    // dispatch target
      Current: ActionId option } // current ref value (None = unassigned)

let wireSlotsOf (kind: ActionKind) : WireSlot list =
    let s label accepted field current =
        { FullLabel = label
          AcceptedKey = accepted
          Field = field
          Current = current }
    match kind with
    | Translate(c, _, _, _) -> [ s "child" "child" TranslateChild c ]
    | Rotate(c, _, _, _, _) -> [ s "child" "child" RotateChild c ]
    | Move(c, f) ->
        [ s "child" "child" MoveChild c
          s "frame" "frame" MoveFrame f ]
    | Union(a, b, _) ->
        [ s "tool"   "a" UnionA a
          s "target" "b" UnionB b ]
    | Subtract(a, b, _) ->
        [ s "target" "a" SubtractA a
          s "tool"   "b" SubtractB b ]
    | Intersect(a, b, _) ->
        [ s "tool"   "a" IntersectA a
          s "target" "b" IntersectB b ]
    | Sketch(o, _, _) -> [ s "origin" "origin" SketchOrigin o ]
    | FromSketch(c, _, _) -> [ s "sketch" "child" FromSketchChild c ]
    | Thicken(c, _) -> [ s "child" "child" ThickenChild c ]
    | Shell(c, _) -> [ s "child" "child" ShellChild c ]
    | Mesh(c, _, _) -> [ s "child" "child" MeshChild c ]
    | _ -> []

let WIRE_MIME = "application/x-wire-bubble"

let dataTransferHasWire (de: DragEvent) : bool =
    let types = de.dataTransfer.types
    let mutable found = false
    for i in 0 .. types.length - 1 do
        if types.[i] = WIRE_MIME then found <- true
    found

/// Build one bubble DOM element. `draggable=true`; sets the drag
/// payload to the slot's accepted key so drop targets can look it up
/// in the wired action's slot list.
let makeWireBubble
        (dispatch: Message -> unit)
        (wiredId: ActionId)
        (slot: WireSlot)
        (assignedBackground: bool)
        : HTMLElement =
    let bubble = Dom.el "button" "wire-bubble"
    if assignedBackground then bubble.classList.add "is-assigned"
    bubble.textContent <- slot.FullLabel
    bubble?draggable <- true
    bubble.addEventListener (
        "dragstart",
        fun ev ->
            let de = ev :?> DragEvent
            de.dataTransfer.effectAllowed <- "move"
            de.dataTransfer.setData (WIRE_MIME, slot.AcceptedKey) |> ignore
            ev.stopPropagation())
    // Click an already-placed bubble to detach it back into the tray.
    if assignedBackground then
        bubble.addEventListener (
            "click",
            fun ev ->
                ev.stopPropagation()
                dispatch (Editor.setActionParamValue wiredId slot.Field VNull))
    bubble

// ── Inline input rows (edit mode) ─────────────────────────────────────
//
// When `doc.WiringActionId = Some aid`, one row per editable input of
// that action is rendered directly beneath it. Phase 1: display only
// (label + current value). Keyboard editing comes in a follow-up.

type InputDisplay =
    | RefDisplay of slot: WireSlot
    | ScalarDisplay of label: string * value: float * field: ActionParamField
    | SelectDisplay of label: string * value: string * choices: string list * field: ActionParamField
    | CheckDisplay of label: string * value: bool * field: ActionParamField

/// Every editable input of the action, in the same order as the right-
/// panel's `renderKindControls` (so the two views line up semantically).
let inputsOf (kind: ActionKind) : InputDisplay list =
    let ref' label accepted field current : InputDisplay =
        RefDisplay { FullLabel = label; AcceptedKey = accepted; Field = field; Current = current }
    let scalar label value field = ScalarDisplay(label, value, field)
    match kind with
    | Origin -> []
    | Sphere r -> [ scalar "radius" r SphereRadius ]
    | Cylinder(r, h) ->
        [ scalar "radius" r CylinderRadius
          scalar "height" h CylinderHeight ]
    | Box(w, h, d) ->
        [ scalar "width" w BoxWidth
          scalar "height" h BoxHeight
          scalar "depth" d BoxDepth ]
    | HalfPlane(axis, offset, flip) ->
        [ SelectDisplay("axis", axis, [ "X"; "Y"; "Z" ], HalfPlaneAxis)
          scalar "offset" offset HalfPlaneOffset
          CheckDisplay("flip", flip, HalfPlaneFlip) ]
    | Translate(c, x, y, z) ->
        [ ref' "child" "child" TranslateChild c
          scalar "x" x TranslateX
          scalar "y" y TranslateY
          scalar "z" z TranslateZ ]
    | Rotate(c, ax, ay, az, angle) ->
        [ ref' "child" "child" RotateChild c
          scalar "ax" ax RotateAxisX
          scalar "ay" ay RotateAxisY
          scalar "az" az RotateAxisZ
          scalar "angle" angle RotateAngle ]
    | Move(c, f) ->
        [ ref' "child" "child" MoveChild c
          ref' "frame" "frame" MoveFrame f ]
    | Union(a, b, r) ->
        [ ref' "tool" "a" UnionA a
          ref' "target" "b" UnionB b
          scalar "radius" r UnionRadius ]
    | Subtract(a, b, r) ->
        [ ref' "target" "a" SubtractA a
          ref' "tool" "b" SubtractB b
          scalar "radius" r SubtractRadius ]
    | Intersect(a, b, r) ->
        [ ref' "tool" "a" IntersectA a
          ref' "target" "b" IntersectB b
          scalar "radius" r IntersectRadius ]
    | Sketch(o, plane, _) ->
        let planeLabel =
            match plane with
            | XY -> "XY"
            | XZ -> "XZ"
            | YZ -> "YZ"
        [ ref' "origin" "origin" SketchOrigin o
          SelectDisplay("plane", planeLabel, [ "XY"; "XZ"; "YZ" ], SketchPlane) ]
    | FromSketch(c, flip, _) ->
        [ ref' "sketch" "child" FromSketchChild c
          CheckDisplay("flip", flip, FromSketchFlip) ]
    | Thicken(c, amount) ->
        [ ref' "child" "child" ThickenChild c
          scalar "amount" amount ThickenAmount ]
    | Shell(c, t) ->
        [ ref' "child" "child" ShellChild c
          scalar "thickness" t ShellThickness ]
    | Mesh(c, size, res) ->
        [ ref' "child" "child" MeshChild c
          scalar "size" size MeshSize
          scalar "res" (float res) MeshResolution ]

let private formatFloat (v: float) : string =
    // Compact but still readable: integers show as "5", others as ".1f".
    if abs (v - round v) < 1e-9 then sprintf "%g" v
    else sprintf "%.2f" v

let private refDisplayValue (doc: DocumentView) (slot: WireSlot) : string =
    match slot.Current with
    | None -> "\u2013"  // en dash — "unassigned"
    | Some id ->
        doc.Actions
        |> List.tryFind (fun a -> a.Id = id)
        |> Option.map (fun a -> a.Name |> Option.defaultValue (kindLabel a.Kind))
        |> Option.defaultValue id

let private fieldOfInput (input: InputDisplay) : ActionParamField =
    match input with
    | RefDisplay s -> s.Field
    | ScalarDisplay(_, _, f)
    | SelectDisplay(_, _, _, f)
    | CheckDisplay(_, _, f) -> f

let private renderInputRow
        (dispatch: Message -> unit)
        (doc: DocumentView)
        (wiredActionId: ActionId)
        (input: InputDisplay)
        : HTMLElement =
    let row = Dom.el "div" "input-row"
    let label =
        match input with
        | RefDisplay s -> s.FullLabel
        | ScalarDisplay(l, _, _) -> l
        | SelectDisplay(l, _, _, _) -> l
        | CheckDisplay(l, _, _) -> l
    row.appendChild (Dom.elText "span" "input-row-label" label :> Node) |> ignore

    let field = fieldOfInput input
    let isEditingThis = doc.EditingInputField = Some field

    match input, isEditingThis with
    | ScalarDisplay(_, value, scalarField), true ->
        let inputEl = document.createElement "input" :?> HTMLInputElement
        inputEl.``type`` <- "number"
        inputEl.className <- "input-row-edit"
        // When the user triggered sub-edit by typing a digit/./-,
        // EditingInputInitial carries that key so we pre-fill the
        // input with it (instead of the current value) and place the
        // cursor at the end — the typed character is not lost.
        let prefilled = doc.EditingInputInitial.IsSome
        inputEl.value <-
            match doc.EditingInputInitial with
            | Some s -> s
            | None -> formatFloat value
        // Enter blurs → blur commits (same pattern as Dom.setupDraggable).
        // Escape flips `cancelled` so the blur handler only dispatches
        // StopEditingInputField without writing the value back.
        let mutable cancelled = false
        let mutable finished = false
        let finish () =
            if not finished then
                finished <- true
                if not cancelled then
                    match System.Double.TryParse(inputEl.value) with
                    | true, v ->
                        let payload =
                            match scalarField with
                            | MeshResolution -> VInt(int v)
                            | _ -> VFloat v
                        dispatch (Editor.setActionParamValue wiredActionId scalarField payload)
                    | _ -> ()
                dispatch StopEditingInputField
        inputEl.addEventListener ("keydown", fun ev ->
            let ke = ev :?> KeyboardEvent
            match ke.key with
            | "Enter" ->
                ev.preventDefault(); ev.stopPropagation()
                inputEl.blur ()
            | "Escape" ->
                ev.preventDefault(); ev.stopPropagation()
                cancelled <- true
                inputEl.blur ()
            | _ -> ev.stopPropagation())
        inputEl.addEventListener ("blur", fun _ -> finish ())
        window.requestAnimationFrame (fun _ ->
            inputEl.focus()
            // When pre-filled from a direct keystroke, leave the
            // cursor at its default position (end) so more digits
            // append; otherwise select all so the first keystroke
            // replaces the existing value.
            if not prefilled then inputEl.select()) |> ignore
        row.appendChild (inputEl :> Node) |> ignore
    | _ ->
        let valueText =
            match input with
            | RefDisplay s -> refDisplayValue doc s
            | ScalarDisplay(_, v, _) -> formatFloat v
            | SelectDisplay(_, v, _, _) -> v
            | CheckDisplay(_, v, _) -> if v then "yes" else "no"
        let valueEl = Dom.elText "span" "input-row-value" valueText
        match input with
        | RefDisplay s when s.Current.IsNone ->
            valueEl.classList.add "is-empty"
        | _ -> ()
        if isEditingThis then
            match input with
            | RefDisplay _ -> valueEl.classList.add "is-picking"
            | _ -> ()
        row.appendChild (valueEl :> Node) |> ignore
    row

let private kindSubtitle (kind: ActionKind) : string =
    match kind with
    | Cylinder(r, h) -> sprintf "r%g h%g" r h
    | Sphere r -> sprintf "r%g" r
    | Box(w, h, d) -> sprintf "%g\u00D7%g\u00D7%g" w h d
    | HalfPlane(axis, offset, _) -> sprintf "%s %g" axis offset
    | Translate(_, x, y, z) -> sprintf "%g, %g, %g" x y z
    | Rotate(_, _, _, _, a) -> sprintf "%g" a
    | Thicken(_, amount) -> sprintf "%g" amount
    | Shell(_, t) -> sprintf "%g" t
    | Mesh(_, size, res) -> sprintf "%g \u00D7%d" size res
    | _ -> ""

// ── Action row ─────────────────────────────────────────────────────────

let private isOrigin (kind: ActionKind) =
    match kind with Origin -> true | _ -> false

let private renderRow
        (dispatch: Message -> unit)
        (doc: DocumentView)
        (action: DocAction)
        : HTMLElement =

    let selected = doc.SelectedId = Some action.Id
    let hasError = doc.Errors |> List.exists (fun e -> e.ActionId = action.Id)

    let row = Dom.el "div" "action-row"
    row.dataset?actionId <- action.Id
    if selected then row.classList.add "is-selected"
    if isOrigin action.Kind then row.classList.add "is-fixed"
    if hasError then row.classList.add "has-error"
    row.addEventListener ("click", fun _ -> dispatch (SelectAction action.Id))

    let main = Dom.el "div" "action-main"

    let icon = Dom.el "span" "action-icon"
    icon.appendChild (Icons.forKind action.Kind :> Node) |> ignore
    main.appendChild (icon :> Node) |> ignore

    let info = Dom.el "div" "action-info"
    let titleText = action.Name |> Option.defaultValue (kindLabel action.Kind)
    let titleSpan = Dom.elText "span" "action-title" titleText
    info.appendChild (titleSpan :> Node) |> ignore
    let sub = kindSubtitle action.Kind
    if sub <> "" then
        let subtitle = Dom.elText "span" "action-subtitle" sub
        subtitle.dataset?actionId <- action.Id
        info.appendChild (subtitle :> Node) |> ignore
    main.appendChild (info :> Node) |> ignore

    // Double-click the title to rename. Swaps the <span> for an
    // <input>; Enter commits via UpdateAction, Escape cancels, blur
    // commits (matches the behaviour of similar inline-edit fields
    // across the app).
    let beginRename () =
        let current = action.Name |> Option.defaultValue ""
        let input = document.createElement "input" :?> HTMLInputElement
        input.``type`` <- "text"
        input.className <- "action-title-edit"
        input.value <- current
        titleSpan.parentNode.replaceChild(input, titleSpan) |> ignore
        input.focus ()
        input.select ()
        let mutable finished = false
        let commit () =
            if not finished then
                finished <- true
                let trimmed = input.value.Trim()
                let nextName =
                    if trimmed = "" || trimmed = kindLabel action.Kind then None
                    else Some trimmed
                if nextName <> action.Name then
                    dispatch (UpdateAction(action.Id, { action with Name = nextName }))
        let cancel () =
            if not finished then
                finished <- true
                // Let the store-driven re-render put the original span
                // back. Swap locally as a fallback if there's no redraw.
                if not (isNull input.parentNode) then
                    input.parentNode.replaceChild(titleSpan, input) |> ignore
        input.addEventListener ("blur", fun _ -> commit ())
        input.addEventListener (
            "keydown",
            fun ev ->
                let ke = ev :?> KeyboardEvent
                match ke.key with
                | "Enter" ->
                    ev.preventDefault ()
                    ev.stopPropagation ()
                    commit ()
                    input.blur ()
                | "Escape" ->
                    ev.preventDefault ()
                    ev.stopPropagation ()
                    cancel ()
                | _ -> ev.stopPropagation ())
        input.addEventListener ("click", fun ev -> ev.stopPropagation ())
        input.addEventListener ("mousedown", fun ev -> ev.stopPropagation ())

    titleSpan.addEventListener (
        "dblclick",
        fun ev ->
            ev.preventDefault ()
            ev.stopPropagation ()
            beginRename ())

    row.appendChild (main :> Node) |> ignore

    // Visibility badge on the right of the row — shows the current
    // visibility mode and serves as a click target for cycling.
    let visibilityBadge = Dom.el "button" "visibility-badge"
    let badgeText =
        match action.Visibility with
        | VHidden -> ""
        | VVisible -> "\u25CE"       // bullseye dot — "shown"
        | VFieldLines -> "\u2261"    // ≡ — stacked slice lines
        | VIsosurface -> "\u25CB"    // ○ — surface outline
    if action.Visibility = VHidden then
        visibilityBadge.classList.add "is-hidden"
    visibilityBadge.textContent <- badgeText
    visibilityBadge.addEventListener (
        "click",
        fun ev ->
            ev.stopPropagation ()
            dispatch (CycleActionVisibility action.Id))
    row.appendChild (visibilityBadge :> Node) |> ignore

    row

// ── Inline add-action picker ──────────────────────────────────────────
//
// Cmd+K toggles `doc.ActionPickerOpen`. When open we mount a small
// input + filtered-template list at the bottom of the action list
// panel. Query state lives in the DOM input so each keystroke only
// rewrites the filter list (no reducer roundtrip). Enter dispatches
// `QuickAddAction`, Escape closes.

let private ALL_TEMPLATES : ActionTemplate list =
    [ SphereTemplate; CylinderTemplate; BoxTemplate; HalfPlaneTemplate
      TranslateTemplate; RotateTemplate; MoveTemplate
      UnionTemplate; SubtractTemplate; IntersectTemplate
      SketchTemplate; FromSketchTemplate
      ThickenTemplate; ShellTemplate; MeshTemplate ]

let private templateLabel (t: ActionTemplate) : string =
    Editor.templateKindName t

/// Subsequence fuzzy match — same flavour as the old palette.
let private fuzzyMatch (query: string) (text: string) : bool =
    let q = query.ToLowerInvariant()
    let t = text.ToLowerInvariant()
    let mutable qi = 0
    for ti in 0 .. t.Length - 1 do
        if qi < q.Length && t.[ti] = q.[qi] then
            qi <- qi + 1
    qi = q.Length

let private filterTemplates (query: string) : ActionTemplate list =
    if System.String.IsNullOrEmpty query then ALL_TEMPLATES
    else ALL_TEMPLATES |> List.filter (fun t -> fuzzyMatch query (templateLabel t))

let private renderActionPicker (dispatch: Message -> unit) : HTMLElement =
    let picker = Dom.el "div" "action-picker"

    let input = document.createElement "input" :?> HTMLInputElement
    input.``type`` <- "text"
    input.className <- "action-picker-input"
    input.placeholder <- "Add action\u2026"
    input.autocomplete <- "off"
    input.spellcheck <- false
    picker.appendChild (input :> Node) |> ignore

    let resultsEl = Dom.el "div" "action-picker-results"
    picker.appendChild (resultsEl :> Node) |> ignore

    // Highlight index — scoped to this picker instance via closure.
    let mutable current : ActionTemplate list = ALL_TEMPLATES
    let mutable highlighted = 0

    let refreshHighlight () =
        let items = resultsEl.querySelectorAll ".action-picker-item"
        for i in 0 .. items.length - 1 do
            let el = items.[i] :?> HTMLElement
            if i = highlighted then el.classList.add "is-active"
            else el.classList.remove "is-active"

    let commit () =
        if highlighted >= 0 && highlighted < current.Length then
            dispatch (QuickAddAction current.[highlighted])
        else
            dispatch CloseActionPicker

    let rebuildResults () =
        resultsEl.innerHTML <- ""
        current
        |> List.iteri (fun i template ->
            let item = Dom.el "button" "action-picker-item"
            item.appendChild (Icons.forTemplate template :> Node) |> ignore
            item.appendChild
                (Dom.elText "span" "action-picker-label" (templateLabel template) :> Node)
            |> ignore
            item.addEventListener ("mouseenter", fun _ ->
                highlighted <- i
                refreshHighlight())
            item.addEventListener ("click", fun e ->
                e.stopPropagation()
                highlighted <- i
                commit())
            resultsEl.appendChild (item :> Node) |> ignore)
        refreshHighlight()

    let applyQuery (q: string) =
        current <- filterTemplates q
        if highlighted >= current.Length then
            highlighted <- max 0 (current.Length - 1)
        rebuildResults()

    applyQuery ""

    input.addEventListener ("input", fun _ ->
        highlighted <- 0
        applyQuery input.value)

    input.addEventListener ("keydown", fun e ->
        let ke = e :?> KeyboardEvent
        match ke.key with
        | "ArrowDown" ->
            e.preventDefault(); e.stopPropagation()
            if not current.IsEmpty then
                highlighted <- (highlighted + 1) % current.Length
                refreshHighlight()
        | "ArrowUp" ->
            e.preventDefault(); e.stopPropagation()
            if not current.IsEmpty then
                highlighted <- (highlighted - 1 + current.Length) % current.Length
                refreshHighlight()
        | "Enter" ->
            e.preventDefault(); e.stopPropagation()
            commit()
        | "Escape" ->
            e.preventDefault(); e.stopPropagation()
            dispatch CloseActionPicker
        | _ -> ())

    // Auto-focus once the picker lands in the DOM.
    window.requestAnimationFrame (fun _ -> input.focus()) |> ignore
    picker

// ── Panel ──────────────────────────────────────────────────────────────

let render (dispatch: Message -> unit) (doc: DocumentView) : HTMLElement =
    let left = Dom.el "div" "panel"
    let header = Dom.el "div" "panel-header"
    header.appendChild (Dom.elText "h2" "" "Actions" :> Node) |> ignore

    let rightGroup = Dom.el "div" "header-right"

    // Palette hint button.
    let paletteBtn = Dom.el "button" "palette-hint-btn"
    paletteBtn.appendChild (Dom.elText "kbd" "" "\u2318" :> Node) |> ignore
    paletteBtn.appendChild (Dom.elText "span" "palette-hint-plus" "+" :> Node) |> ignore
    paletteBtn.appendChild (Dom.elText "kbd" "" "K" :> Node) |> ignore
    paletteBtn.appendChild (document.createTextNode " " :> Node) |> ignore
    paletteBtn.appendChild (Dom.elText "span" "" "palette" :> Node) |> ignore
    paletteBtn.addEventListener ("click", fun _ -> dispatch OpenActionPicker)
    rightGroup.appendChild (paletteBtn :> Node) |> ignore

    header.appendChild (rightGroup :> Node) |> ignore

    left.appendChild (header :> Node) |> ignore

    // ── List with drag-reorder ─────────────────────────────────────────
    let list = Dom.el "div" "actions-list"

    let actions = doc.Actions |> List.toArray
    let rows : HTMLElement[] = Array.zeroCreate actions.Length
    let mutable dragIndex : int option = None
    let mutable dropIndex : int option = None
    let mutable dropBefore = false

    // Edit-mode snapshot — the action whose inputs are being expanded
    // below it in the list.
    let wiredIdx =
        doc.WiringActionId
        |> Option.bind (fun id ->
            actions |> Array.tryFindIndex (fun a -> a.Id = id))

    // When a ref input is being picked, the candidate at RefPickIdx
    // gets highlighted on its own action row above, so the user sees
    // which upstream action Enter will assign.
    let refPickCandidateId : ActionId option =
        match wiredIdx, doc.EditingInputField with
        | Some wi, Some field ->
            let wired = actions.[wi]
            let input =
                inputsOf wired.Kind
                |> List.tryFind (fun i ->
                    match i with
                    | RefDisplay s -> s.Field = field
                    | _ -> false)
            match input with
            | Some (RefDisplay s) ->
                let candidates =
                    Map.tryFind s.AcceptedKey doc.RefOptions
                    |> Option.defaultValue []
                if doc.RefPickIdx >= 0 && doc.RefPickIdx < candidates.Length then
                    Some candidates.[doc.RefPickIdx]
                else None
            | _ -> None
        | _ -> None

    let clearDropIndicators () =
        for r in rows do
            if not (isNull r) then
                r.classList.remove "drop-before"
                r.classList.remove "drop-after"

    for i in 0 .. actions.Length - 1 do
        let action = actions.[i]
        let row = renderRow dispatch doc action
        rows.[i] <- row

        // Wiring mode's drop zones + bubble tray live in the sibling
        // `WireColumn` panel; here we only need to render empty spacer
        // rows below the wired action so the action list stays
        // vertically aligned with the bubble rows in the wire column.
        ()

        // In edit mode, highlight whichever row (action or input) has
        // keyboard focus. EditFocusIdx = 0 means the action row itself;
        // 1..N means the Nth input row.
        match wiredIdx with
        | Some wi when i = wi && doc.EditFocusIdx = 0 ->
            row.classList.add "is-edit-focused"
        | _ -> ()

        if refPickCandidateId = Some action.Id then
            row.classList.add "is-pick-candidate"

        if not (isOrigin action.Kind) then
            row?draggable <- true
            row.addEventListener (
                "dragstart",
                fun e ->
                    let de = e :?> DragEvent
                    dragIndex <- Some i
                    de.dataTransfer.effectAllowed <- "move"
                    de.dataTransfer.setData ("text/plain", string i) |> ignore
                    window.requestAnimationFrame (fun _ -> row.classList.add "is-dragging")
                    |> ignore
            )

        row.addEventListener (
            "dragover",
            fun e ->
                match dragIndex with
                | None -> ()
                | Some di when di = i ->
                    dropIndex <- None
                    clearDropIndicators ()
                | Some _ ->
                    e.preventDefault ()
                    let de = e :?> DragEvent
                    de.dataTransfer.dropEffect <- "move"
                    let rect = row.getBoundingClientRect ()
                    let before = de.clientY < rect.top + rect.height / 2.0
                    if isOrigin action.Kind && before then
                        dropIndex <- None
                        clearDropIndicators ()
                    else
                        dropBefore <- before
                        dropIndex <- Some i
                        clearDropIndicators ()
                        row.classList.add (if before then "drop-before" else "drop-after")
        )

        list.appendChild (row :> Node) |> ignore

        // Input rows — expanded directly below the action being
        // edited. Shows label + current value for every ref / scalar
        // / select / check input the action has. Editing comes from
        // the keyboard handlers (Enter / Arrow keys) in a later pass.
        match wiredIdx with
        | Some wi when i = wi ->
            let inputs = inputsOf action.Kind
            inputs
            |> List.iteri (fun ii input ->
                let inputRow = renderInputRow dispatch doc action.Id input
                if doc.EditFocusIdx = ii + 1 then
                    inputRow.classList.add "is-focused"
                list.appendChild (inputRow :> Node) |> ignore)
        | _ -> ()

    // Drop in empty space → append at end
    list.addEventListener (
        "dragover",
        fun e ->
            match dragIndex with
            | None -> ()
            | Some _ ->
                let de = e :?> DragEvent
                let target = de.target :?> HTMLElement
                if Option.isNone (target.closest ".action-row") then
                    e.preventDefault ()
                    de.dataTransfer.dropEffect <- "move"
                    let last = rows.Length - 1
                    if last >= 0 then
                        dropIndex <- Some last
                        dropBefore <- false
                        clearDropIndicators ()
                        rows.[last].classList.add "drop-after"
    )

    list.addEventListener (
        "drop",
        fun e ->
            e.preventDefault ()
            clearDropIndicators ()
            for r in rows do
                if not (isNull r) then r.classList.remove "is-dragging"
            match dragIndex, dropIndex with
            | Some di, Some dri ->
                let ids = actions |> Array.map (fun a -> a.Id) |> ResizeArray
                let moved = ids.[di]
                ids.RemoveAt di
                let mutable target = dri + (if dropBefore then 0 else 1)
                if di < target then target <- target - 1
                ids.Insert (target, moved)
                dragIndex <- None
                dropIndex <- None
                dispatch (ReorderActions(List.ofSeq ids))
            | _ ->
                dragIndex <- None
                dropIndex <- None
    )

    list.addEventListener (
        "dragend",
        fun _ ->
            dragIndex <- None
            dropIndex <- None
            clearDropIndicators ()
            for r in rows do
                if not (isNull r) then r.classList.remove "is-dragging"
    )

    left.appendChild (list :> Node) |> ignore

    // Inline add-action picker, pinned to the bottom of the panel
    // when the user hit Cmd+K. Not inside `.actions-list` so it
    // doesn't scroll with the action list.
    if doc.ActionPickerOpen then
        left.appendChild (renderActionPicker dispatch :> Node) |> ignore

    left

let syncSubtitles (root: HTMLElement) (doc: DocumentView) : unit =
    for action in doc.Actions do
        match root.querySelector($".action-row[data-action-id=\"{action.Id}\"]") with
        | null -> ()
        | row ->
            let subtitleText = kindSubtitle action.Kind
            let existing = row.querySelector ".action-subtitle"

            if subtitleText = "" then
                if not (isNull existing) then
                    existing.remove ()
            else
                match existing with
                | null ->
                    let info = row.querySelector ".action-info"
                    if not (isNull info) then
                        let subtitle = Dom.elText "span" "action-subtitle" subtitleText
                        subtitle.dataset?actionId <- action.Id
                        info.appendChild (subtitle :> Node) |> ignore
                | existingSubtitle ->
                    existingSubtitle.textContent <- subtitleText

let syncPanel (root: HTMLElement) (dispatch: Message -> unit) (doc: DocumentView) : unit =
    match root.querySelector ".panel-host-actions" with
    | :? HTMLElement as host ->
        let prevScroll =
            match host.querySelector ".actions-list" with
            | :? HTMLElement as list -> list.scrollTop
            | _ -> 0.0
        host.innerHTML <- ""
        host.appendChild (render dispatch doc :> Node) |> ignore
        match host.querySelector ".actions-list" with
        | :? HTMLElement as list -> list.scrollTop <- prevScroll
        | _ -> ()
    | _ ->
        ()
