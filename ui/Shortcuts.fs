module PointerMk18.Ui.Shortcuts

open Fable.Core.JsInterop
open Server
open PointerMk18.Ui
open Browser.Dom
open Browser.Types

// ---------------------------------------------------------------------------
// Global keyboard shortcuts. Ported from user-interface/src/main.ts:196–314.
//
// All shortcuts dispatch typed F# messages — the TS version passed strings
// like "Coincident" through; here we use the GeometricConstraintKind /
// SketchToolKind / ConstraintPlacementKind enums directly, so missing
// cases are compile errors.
// ---------------------------------------------------------------------------

// ── Shortcut tables (key → enum) ───────────────────────────────────────

let private toolShortcuts : (string * SketchToolKind) list =
    [ "l", LineTool
      "g", RectangleTool
      "c", CircleTool
      "u", ArcTool ]

let private toolShiftShortcuts : (string * SketchToolKind) list =
    [ "g", RoundedRectangleTool ]

let private geometricShortcuts : (string * GeometricConstraintKind) list =
    [ "i", CoincidentConstraint
      "h", HorizontalConstraint
      "v", VerticalConstraint
      "b", ParallelConstraint
      "t", TangentConstraint
      "e", EqualConstraint ]

let private geometricShiftShortcuts : (string * GeometricConstraintKind) list =
    [ "o", ConcentricConstraint
      "l", PerpendicularConstraint
      "m", MidpointConstraint
      "j", FixedConstraint ]

let private dimensionShortcuts : (string * ConstraintPlacementKind) list =
    [ "d", DistancePlacement
      "a", AnglePlacement ]

// ── Helpers ────────────────────────────────────────────────────────────

/// True when the event target is an editable element — we don't want to
/// hijack keystrokes the user is typing into an input.
let private isEditable (target: obj) : bool =
    if isNull target then false
    else
        let el : HTMLElement = unbox target
        let tag = el.tagName
        tag = "INPUT" || tag = "TEXTAREA" || tag = "SELECT" || el.isContentEditable

let private selectedAction (doc: DocumentView) : DocAction option =
    doc.SelectedId |> Option.bind (fun id -> doc.Actions |> List.tryFind (fun a -> a.Id = id))

let private selectedIsSketch (doc: DocumentView) : bool =
    match selectedAction doc with
    | Some { Kind = Sketch _ } -> true
    | _ -> false

// ── Sketch-mode shortcuts ──────────────────────────────────────────────

/// Returns true if the key was handled, so the global handler can stop.
let private handleSketchShortcut
        (dispatch: Message -> unit)
        (doc: DocumentView)
        (e: KeyboardEvent)
        : bool =
    if not doc.SketchUi.EditMode || not (selectedIsSketch doc) then false
    elif e.metaKey || e.ctrlKey || e.altKey then false
    else

    let key = e.key.ToLower()

    if e.key = "Escape" then
        e.preventDefault ()
        match doc.SketchUi.ConstraintPlacementMode |> Option.bind Editor.tryConstraintPlacementKind with
        | Some kind ->
            dispatch (ToggleConstraintPlacement kind)
            true
        | None when doc.SketchUi.Tool <> "none" ->
            dispatch (SetSketchTool NoSketchTool)
            true
        | None ->
            dispatch ToggleSketchEdit
            true
    else
        let tool =
            if e.shiftKey then List.tryFind (fun (k, _) -> k = key) toolShiftShortcuts |> Option.map snd
            else List.tryFind (fun (k, _) -> k = key) toolShortcuts |> Option.map snd
        match tool with
        | Some t ->
            e.preventDefault ()
            dispatch (SetSketchTool t)
            true
        | None ->
            let dimension =
                if e.shiftKey then None
                else List.tryFind (fun (k, _) -> k = key) dimensionShortcuts |> Option.map snd
            match dimension with
            | Some d ->
                e.preventDefault ()
                dispatch (ToggleConstraintPlacement d)
                true
            | None ->
                let constraintKind =
                    if e.shiftKey then List.tryFind (fun (k, _) -> k = key) geometricShiftShortcuts |> Option.map snd
                    else List.tryFind (fun (k, _) -> k = key) geometricShortcuts |> Option.map snd
                match constraintKind with
                | Some c ->
                    let available =
                        doc.SketchUi.ConstraintAvailability
                        |> Map.tryFind (Editor.geometricConstraintName c)
                        |> Option.defaultValue false
                    if available then
                        e.preventDefault ()
                        dispatch (AddConstraintFromSelection c)
                        true
                    else false
                | None -> false

// ── Main global keydown ────────────────────────────────────────────────

/// Wire up the global keyboard handler once at mount time. Reads current
/// state via `getDoc` and `getPaletteOpen` so each keystroke sees the
/// up-to-date view.
let register
        (dispatch: Message -> unit)
        (getDoc: unit -> DocumentView)
        (getPaletteOpen: unit -> bool)
        (onSave: unit -> unit)
        (onLoad: unit -> unit)
        : unit =

    document.addEventListener (
        "keydown",
        fun e ->
            let ke = e :?> KeyboardEvent

            // ── Modifier shortcuts (⌘/Ctrl, no alt/shift) ──
            if (ke.metaKey || ke.ctrlKey) && not ke.altKey && not ke.shiftKey then
                match ke.key.ToLower() with
                | "k" ->
                    // Toggle the inline action picker at the bottom of
                    // the action list.
                    e.preventDefault ()
                    let doc = getDoc ()
                    if doc.ActionPickerOpen
                    then dispatch CloseActionPicker
                    else dispatch OpenActionPicker
                | "s" ->
                    e.preventDefault ()
                    onSave ()
                | "o" ->
                    e.preventDefault ()
                    onLoad ()
                | _ -> ()
            else
                // Palette open — let the palette handle everything
                if getPaletteOpen () then () else

                let target : obj = e?target
                if isEditable target then () else

                let doc = getDoc ()

                // Sketch-mode shortcuts first
                if handleSketchShortcut dispatch doc ke then () else

                match ke.key with
                | "Escape" when doc.EditingInputField.IsSome ->
                    // Cancel the current input sub-edit, but keep the
                    // row focused so the user can keep navigating.
                    e.preventDefault ()
                    dispatch StopEditingInputField
                | "Escape" when doc.WiringActionId.IsSome ->
                    e.preventDefault ()
                    dispatch StopWiring
                | "Enter" when doc.WiringActionId.IsNone && doc.SelectedId.IsSome ->
                    // Enter on a selected action opens edit mode —
                    // input rows expand directly beneath it. Skipped
                    // for primitives without any editable inputs so
                    // users don't see an empty expansion.
                    match selectedAction doc with
                    | Some sel ->
                        if not (List.isEmpty (ActionList.inputsOf sel.Kind)) then
                            e.preventDefault ()
                            dispatch (StartWiring sel.Id)
                    | None -> ()
                | "Enter" when doc.WiringActionId.IsSome && doc.EditingInputField.IsSome ->
                    // Ref sub-edit: commit the highlighted upstream pick.
                    // Scalar sub-edit: the inline <input> normally
                    // handles Enter itself (and its keydown handler
                    // stops propagation). This branch is reached only
                    // when focus never actually landed on the input — a
                    // fallback that reads value from the DOM so Enter
                    // still saves reliably.
                    match doc.WiringActionId |> Option.bind (fun id -> doc.Actions |> List.tryFind (fun a -> a.Id = id)) with
                    | Some wired ->
                        match doc.EditingInputField with
                        | Some field ->
                            let input =
                                ActionList.inputsOf wired.Kind
                                |> List.tryFind (fun i ->
                                    match i with
                                    | ActionList.RefDisplay s -> s.Field = field
                                    | ActionList.ScalarDisplay(_, _, f)
                                    | ActionList.SelectDisplay(_, _, _, f)
                                    | ActionList.CheckDisplay(_, _, f) -> f = field)
                            match input with
                            | Some (ActionList.RefDisplay s) ->
                                e.preventDefault ()
                                let candidates =
                                    Map.tryFind s.AcceptedKey doc.RefOptions
                                    |> Option.defaultValue []
                                if doc.RefPickIdx >= 0 && doc.RefPickIdx < candidates.Length then
                                    dispatch (Editor.setActionParamValue wired.Id field (VString candidates.[doc.RefPickIdx]))
                                dispatch StopEditingInputField
                            | Some (ActionList.ScalarDisplay(_, _, scalarField)) ->
                                e.preventDefault ()
                                let el = document.querySelector ".input-row-edit"
                                if not (isNull el) then
                                    let inputEl = el :?> HTMLInputElement
                                    match System.Double.TryParse(inputEl.value) with
                                    | true, v ->
                                        let payload =
                                            match scalarField with
                                            | MeshResolution -> VInt(int v)
                                            | _ -> VFloat v
                                        dispatch (Editor.setActionParamValue wired.Id scalarField payload)
                                    | _ -> ()
                                dispatch StopEditingInputField
                            | _ -> ()
                        | None -> ()
                    | None -> ()
                | "Enter" when doc.WiringActionId.IsSome ->
                    // Enter over a row in edit mode:
                    //   idx = 0 (action row)    → leave edit mode.
                    //   idx > 0 (input row)     → start / apply sub-edit
                    //                             based on the input's kind.
                    e.preventDefault ()
                    if doc.EditFocusIdx = 0 then
                        dispatch StopWiring
                    else
                        match doc.WiringActionId |> Option.bind (fun id -> doc.Actions |> List.tryFind (fun a -> a.Id = id)) with
                        | Some wired ->
                            let inputs = ActionList.inputsOf wired.Kind
                            let ii = doc.EditFocusIdx - 1
                            if ii >= 0 && ii < inputs.Length then
                                match inputs.[ii] with
                                | ActionList.CheckDisplay(_, value, field) ->
                                    dispatch (Editor.setActionParamValue wired.Id field (VBool(not value)))
                                | ActionList.SelectDisplay(_, value, choices, field) ->
                                    let nextChoice =
                                        match List.tryFindIndex ((=) value) choices with
                                        | Some i -> choices.[(i + 1) % choices.Length]
                                        | None when not choices.IsEmpty -> choices.[0]
                                        | None -> value
                                    if nextChoice <> value then
                                        dispatch (Editor.setActionParamValue wired.Id field (VString nextChoice))
                                | ActionList.ScalarDisplay(_, _, field) ->
                                    // ActionList will render an inline
                                    // number input for this field and
                                    // focus it on the next render tick.
                                    dispatch (StartEditingInputField(field, 0, None))
                                | ActionList.RefDisplay slot ->
                                    let candidates =
                                        Map.tryFind slot.AcceptedKey doc.RefOptions
                                        |> Option.defaultValue []
                                    if not candidates.IsEmpty then
                                        let initial =
                                            slot.Current
                                            |> Option.bind (fun cur ->
                                                candidates |> List.tryFindIndex ((=) cur))
                                            |> Option.defaultValue 0
                                        dispatch (StartEditingInputField(slot.Field, initial, None))
                        | None -> ()
                | k when doc.WiringActionId.IsSome
                         && doc.EditingInputField.IsNone
                         && doc.EditFocusIdx > 0
                         && k.Length = 1
                         && (let c = k.[0] in c = '-' || c = '.' || (c >= '0' && c <= '9')) ->
                    // Typing a digit / '-' / '.' over a focused scalar
                    // row starts the sub-edit pre-filled with that key,
                    // so the user doesn't need to press Enter first.
                    match doc.WiringActionId |> Option.bind (fun id -> doc.Actions |> List.tryFind (fun a -> a.Id = id)) with
                    | Some wired ->
                        let inputs = ActionList.inputsOf wired.Kind
                        let ii = doc.EditFocusIdx - 1
                        if ii >= 0 && ii < inputs.Length then
                            match inputs.[ii] with
                            | ActionList.ScalarDisplay(_, _, field) ->
                                e.preventDefault ()
                                dispatch (StartEditingInputField(field, 0, Some k))
                            | _ -> ()
                    | None -> ()
                | "Delete" | "Backspace" ->
                    e.preventDefault ()
                    dispatch DeleteIntent
                | "ArrowDown" | "ArrowUp" ->
                    e.preventDefault ()
                    // Three arrow-key regimes:
                    //   1. Ref sub-edit  → cycle candidate picks.
                    //   2. Edit mode     → move focus across action row
                    //                      and input rows of the wired action.
                    //   3. Idle          → change the selected action.
                    match doc.WiringActionId |> Option.bind (fun id -> doc.Actions |> List.tryFind (fun a -> a.Id = id)) with
                    | Some wired when doc.EditingInputField.IsSome ->
                        match doc.EditingInputField with
                        | Some field ->
                            let input =
                                ActionList.inputsOf wired.Kind
                                |> List.tryFind (fun i ->
                                    match i with
                                    | ActionList.RefDisplay s -> s.Field = field
                                    | _ -> false)
                            match input with
                            | Some (ActionList.RefDisplay s) ->
                                let candidates =
                                    Map.tryFind s.AcceptedKey doc.RefOptions
                                    |> Option.defaultValue []
                                if not candidates.IsEmpty then
                                    let cur = doc.RefPickIdx
                                    let next =
                                        if ke.key = "ArrowDown" then min (cur + 1) (candidates.Length - 1)
                                        else max (cur - 1) 0
                                    if next <> cur then
                                        dispatch (SetRefPickIdx next)
                            | _ -> ()
                        | None -> ()
                    | Some wired ->
                        let inputCount = List.length (ActionList.inputsOf wired.Kind)
                        let cur = doc.EditFocusIdx
                        let next =
                            if ke.key = "ArrowDown" then min (cur + 1) inputCount
                            else max (cur - 1) 0
                        if next <> cur then
                            dispatch (SetEditFocus next)
                    | None ->
                        let actions = doc.Actions
                        let idx =
                            actions
                            |> List.tryFindIndex (fun a -> Some a.Id = doc.SelectedId)
                            |> Option.defaultValue -1
                        if idx >= 0 then
                            let next =
                                if ke.key = "ArrowDown" then min (idx + 1) (actions.Length - 1)
                                else max (idx - 1) 0
                            if next <> idx then
                                dispatch (SelectAction actions.[next].Id)
                | "v" ->
                    // Cycle visibility of the selected action through
                    // the modes its kind supports (Hidden / Visible for
                    // frames + sketches; Hidden / Isosurface / FieldLines
                    // for field-producing kinds).
                    match selectedAction doc with
                    | Some sel ->
                        e.preventDefault ()
                        dispatch (CycleActionVisibility sel.Id)
                    | None -> ()
                | "e" | "E" ->
                    match selectedAction doc with
                    | Some { Kind = Sketch _; Id = id } ->
                        e.preventDefault ()
                        dispatch ToggleSketchEdit
                        // id used only to match; suppress unused warning
                        ignore id
                    | _ -> ()
                | _ -> ()
    )
