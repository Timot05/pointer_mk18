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
    let blockIsSketch =
        match doc.SelectedBlockId with
        | Some bid ->
            doc.Blocks
            |> List.tryFind (fun b -> b.Id = bid)
            |> Option.exists (fun b ->
                match b.Body with
                | Server.Lang.Notebook.SketchBody _ -> true
                | _ -> false)
        | None -> false
    if blockIsSketch then true
    else
        match selectedAction doc with
        | Some { Kind = Sketch _ } -> true
        | _ -> false

/// Flat ordered list of keyboard-navigable rows. Each row is
/// `(actionId, focusIdx)` where `focusIdx = 0` is the action's own
/// row and `focusIdx = 1..N` are the N input rows of an expanded
/// action. Up/Down step through this list; a focus change that
/// crosses action boundaries also shifts `SelectedId`.
let private flatRows (doc: DocumentView) : (ActionId * int) list =
    doc.Actions
    |> List.collect (fun a ->
        let actionRow = [ a.Id, 0 ]
        if Set.contains a.Id doc.ExpandedActionIds then
            let n = List.length (ActionList.inputsOf doc a)
            actionRow @ [ for i in 1..n -> a.Id, i ]
        else
            actionRow)

let private currentRowIndex (rows: (ActionId * int) list) (doc: DocumentView) : int =
    match doc.SelectedId with
    | Some id ->
        rows
        |> List.tryFindIndex (fun (aid, idx) -> aid = id && idx = doc.EditFocusIdx)
        |> Option.defaultValue -1
    | None -> -1

/// Move keyboard focus by `dir` (±1) through `flatRows`. Dispatches
/// `SelectAction` when the step crosses into a different action, then
/// always finalises `SetEditFocus` with the row's focus index.
let private stepFocus (dispatch: Message -> unit) (doc: DocumentView) (dir: int) =
    let rows = flatRows doc
    if List.isEmpty rows then ()
    else
        let cur = currentRowIndex rows doc
        let target = max 0 (min (rows.Length - 1) (cur + dir))
        if target <> cur && cur >= 0 then
            let aid, idx = rows.[target]
            if Some aid <> doc.SelectedId then
                dispatch (SelectAction aid)
            dispatch (SetEditFocus idx)

/// Cycle a Ref input's value to the next/previous candidate and commit
/// the change. `dir` is +1 for Right, -1 for Left. When the ref is
/// currently unset, Right selects the first candidate and Left the last.
let private cycleRefCandidate
    (dispatch: Message -> unit)
    (doc: DocumentView)
    (sel: DocAction)
    (slot: ActionList.WireSlot)
    (dir: int)
    =
    let candidates =
        Map.tryFind slot.AcceptedKey doc.RefOptions
        |> Option.defaultValue []
    if not candidates.IsEmpty then
        let n = candidates.Length
        let curIdx =
            slot.Current
            |> Option.bind (fun cur -> candidates |> List.tryFindIndex ((=) cur))
        let nextIdx =
            match curIdx with
            | Some i -> (i + dir + n) % n
            | None -> if dir >= 0 then 0 else n - 1
        let nextValue = candidates.[nextIdx]
        if curIdx <> Some nextIdx then
            dispatch (Editor.setActionParamValue sel.Id slot.Field (VString nextValue))

/// Cycle a SelectDisplay's value through its list of choices. Used for
/// things like a sketch's plane. Same dir convention as cycleRefCandidate.
let private cycleSelectChoice
    (dispatch: Message -> unit)
    (sel: DocAction)
    (value: string)
    (choices: string list)
    (field: ActionParamField)
    (dir: int)
    =
    if not (List.isEmpty choices) then
        let n = choices.Length
        let curIdx =
            choices |> List.tryFindIndex ((=) value) |> Option.defaultValue 0
        let nextIdx = (curIdx + dir + n) % n
        let nextValue = choices.[nextIdx]
        if nextValue <> value then
            dispatch (Editor.setActionParamValue sel.Id field (VString nextValue))

/// Encode a FromSketchSelection as the VRecord payload setActionParamValue
/// expects on the FromSketchSelection field.
let private encodeFromSketchSelection (sel: FromSketchSelection) : ParamValue =
    let stringArray xs = VArray (xs |> List.map VString)
    match sel with
    | SelectionAllLoops ->
        VRecord (Map.ofList [ "case", VString "SelectionAllLoops" ])
    | SelectionLoops ids ->
        VRecord (Map.ofList [ "case", VString "SelectionLoops"
                              "loopIds", stringArray ids ])
    | SelectionElements ids ->
        VRecord (Map.ofList [ "case", VString "SelectionElements"
                              "lineIds", stringArray ids ])

/// Toggle one loop in a FromSketch action's selection and dispatch the
/// resulting selection back to the store. Switches between SelectionAllLoops
/// and SelectionLoops automatically — explicit subset when something is
/// off, "all" again when every loop is back on.
let private toggleFromSketchLoop
    (dispatch: Message -> unit)
    (sel: DocAction)
    (loopId: string)
    (allLoopIds: string list)
    (current: FromSketchSelection)
    (wasChecked: bool)
    =
    let currentExplicit =
        match current with
        | SelectionAllLoops -> allLoopIds
        | SelectionLoops ids -> ids
        | SelectionElements _ -> allLoopIds
    let next =
        if wasChecked then
            currentExplicit |> List.filter (fun id -> id <> loopId)
        else
            // Preserve detection order rather than appending arbitrarily.
            allLoopIds
            |> List.filter (fun id ->
                id = loopId || List.contains id currentExplicit)
    let nextSel =
        if List.length next = List.length allLoopIds
           && List.forall (fun id -> List.contains id next) allLoopIds
        then SelectionAllLoops
        else SelectionLoops next
    dispatch (Editor.setActionParamValue sel.Id FromSketchSelection (encodeFromSketchSelection nextSel))

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
                    // Toggle the typed-block palette (acts as the
                    // notebook-era replacement for the old action picker).
                    e.preventDefault ()
                    BlockList.togglePalette dispatch
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

                // Context snapshots used across several handlers.
                let selectedExpanded =
                    match doc.SelectedId with
                    | Some id -> Set.contains id doc.ExpandedActionIds
                    | None -> false
                let focusedInputSlot () =
                    match doc.SelectedId |> Option.bind (fun id -> doc.Actions |> List.tryFind (fun a -> a.Id = id)) with
                    | Some sel when selectedExpanded && doc.EditFocusIdx > 0 ->
                        let inputs = ActionList.inputsOf doc sel
                        let ii = doc.EditFocusIdx - 1
                        if ii >= 0 && ii < inputs.Length then Some(sel, inputs.[ii]) else None
                    | _ -> None
                let inRefPickSubEdit () =
                    match doc.EditingInputField, focusedInputSlot () with
                    | Some _, Some(_, ActionList.RefDisplay _) -> true
                    | _ -> false
                match ke.key with
                | "Escape" when doc.EditingBlockRef.IsSome ->
                    // Cancel block-ref pick mode (clicked a wire bubble,
                    // didn't pick a target).
                    e.preventDefault ()
                    dispatch CancelPickBlockRef
                | "Escape" when doc.EditingInputField.IsSome ->
                    // Cancel the current input sub-edit, keep the row focused.
                    e.preventDefault ()
                    dispatch StopEditingInputField
                | "Escape" ->
                    // Collapse the focused action, if any.
                    match doc.SelectedId with
                    | Some id when Set.contains id doc.ExpandedActionIds ->
                        e.preventDefault ()
                        dispatch (CollapseAction id)
                    | _ -> ()
                | "ArrowRight" ->
                    // Change the focused setting with Left/Right — no
                    // more Enter-based cycling. Each input type commits
                    // its own flavour of change on keypress.
                    match focusedInputSlot () with
                    | Some(sel, ActionList.RefDisplay slot) ->
                        e.preventDefault ()
                        cycleRefCandidate dispatch doc sel slot 1
                    | Some(sel, ActionList.SelectDisplay(_, value, choices, field)) ->
                        e.preventDefault ()
                        cycleSelectChoice dispatch sel value choices field 1
                    | Some(sel, ActionList.CheckDisplay(_, value, field)) ->
                        e.preventDefault ()
                        dispatch (Editor.setActionParamValue sel.Id field (VBool(not value)))
                    | Some(sel, ActionList.LoopToggleDisplay(_, isChecked, loopId, allIds, currentSel)) ->
                        e.preventDefault ()
                        toggleFromSketchLoop dispatch sel loopId allIds currentSel isChecked
                    | Some(_, ActionList.ScalarDisplay(_, _, field)) ->
                        // Scalars need a real value — Right opens the
                        // inline number editor (what Enter used to do)
                        // so arrow keys are the consistent entry point
                        // across all input types.
                        e.preventDefault ()
                        dispatch (StartEditingInputField(field, 0, None))
                    | _ ->
                        match doc.SelectedId with
                        | Some id ->
                            let action = doc.Actions |> List.tryFind (fun a -> a.Id = id)
                            match action with
                            | Some a when not (List.isEmpty (ActionList.inputsOf doc a)) ->
                                e.preventDefault ()
                                if not (Set.contains id doc.ExpandedActionIds) then
                                    dispatch (ExpandAction id)
                                elif doc.EditFocusIdx = 0 then
                                    dispatch (SetEditFocus 1)
                            | _ -> ()
                        | None -> ()
                | "ArrowLeft" ->
                    // On an input row: change the setting (cycle
                    // backward / toggle). On the action row: collapse.
                    // Scalars have no prev value, so Left is a no-op
                    // there — users still have Right/Enter/typing to
                    // open the inline editor.
                    match focusedInputSlot () with
                    | Some(sel, ActionList.RefDisplay slot) ->
                        e.preventDefault ()
                        cycleRefCandidate dispatch doc sel slot -1
                    | Some(sel, ActionList.SelectDisplay(_, value, choices, field)) ->
                        e.preventDefault ()
                        cycleSelectChoice dispatch sel value choices field -1
                    | Some(sel, ActionList.CheckDisplay(_, value, field)) ->
                        e.preventDefault ()
                        dispatch (Editor.setActionParamValue sel.Id field (VBool(not value)))
                    | Some(sel, ActionList.LoopToggleDisplay(_, isChecked, loopId, allIds, currentSel)) ->
                        e.preventDefault ()
                        toggleFromSketchLoop dispatch sel loopId allIds currentSel isChecked
                    | Some(_, ActionList.ScalarDisplay _) ->
                        // No-op — intentionally consume nothing; the
                        // action row below won't collapse because
                        // EditFocusIdx > 0.
                        ()
                    | _ ->
                        match doc.SelectedId with
                        | Some id when
                            Set.contains id doc.ExpandedActionIds
                            && doc.EditFocusIdx = 0 ->
                            e.preventDefault ()
                            dispatch (CollapseAction id)
                        | _ -> ()
                | "Enter" when doc.EditingInputField.IsSome ->
                    // Ref sub-edit: commit the highlighted upstream pick.
                    // Scalar sub-edit: the inline <input> normally
                    // handles Enter itself (its keydown handler stops
                    // propagation); this branch is the fallback for
                    // when focus never landed on the input.
                    match focusedInputSlot (), doc.EditingInputField with
                    | Some(sel, ActionList.RefDisplay s), Some field ->
                        e.preventDefault ()
                        let candidates =
                            Map.tryFind s.AcceptedKey doc.RefOptions
                            |> Option.defaultValue []
                        if doc.RefPickIdx >= 0 && doc.RefPickIdx < candidates.Length then
                            dispatch (Editor.setActionParamValue sel.Id field (VString candidates.[doc.RefPickIdx]))
                        dispatch StopEditingInputField
                    | Some(sel, ActionList.ScalarDisplay(_, _, scalarField)), _ ->
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
                                dispatch (Editor.setActionParamValue sel.Id scalarField payload)
                            | _ -> ()
                        dispatch StopEditingInputField
                    | _ -> ()
                | "Enter" when doc.SelectedId.IsSome ->
                    // Enter on an ACTION row only:
                    //  * collapsed + has inputs → expand.
                    //  * expanded + has inputs  → descend to first input.
                    // Enter on an input row is a no-op — Left/Right (and
                    // direct typing for scalars) are the authoritative
                    // way to change any setting.
                    match selectedAction doc with
                    | Some sel when doc.EditFocusIdx = 0 ->
                        let inputs = ActionList.inputsOf doc sel
                        if not (List.isEmpty inputs) then
                            e.preventDefault ()
                            if not (Set.contains sel.Id doc.ExpandedActionIds) then
                                dispatch (ExpandAction sel.Id)
                            else
                                dispatch (SetEditFocus 1)
                    | _ -> ()
                | k when
                    doc.EditingInputField.IsNone
                    && k.Length = 1
                    && (let c = k.[0] in c = '-' || c = '.' || (c >= '0' && c <= '9'))
                    && (match focusedInputSlot () with
                        | Some(_, ActionList.ScalarDisplay _) -> true
                        | _ -> false) ->
                    // Typing a digit / '-' / '.' over a focused scalar
                    // row starts the sub-edit pre-filled with that key.
                    match focusedInputSlot () with
                    | Some(sel, ActionList.ScalarDisplay(_, _, field)) ->
                        e.preventDefault ()
                        ignore sel
                        dispatch (StartEditingInputField(field, 0, Some k))
                    | _ -> ()
                | "Delete" | "Backspace" ->
                    e.preventDefault ()
                    dispatch DeleteIntent
                | "ArrowDown" | "ArrowUp" ->
                    e.preventDefault ()
                    let dir = if ke.key = "ArrowDown" then 1 else -1
                    if inRefPickSubEdit () then
                        // Ref sub-edit: cycle candidate picks.
                        match focusedInputSlot () with
                        | Some(_, ActionList.RefDisplay s) ->
                            let candidates =
                                Map.tryFind s.AcceptedKey doc.RefOptions
                                |> Option.defaultValue []
                            if not candidates.IsEmpty then
                                let cur = doc.RefPickIdx
                                let next = max 0 (min (candidates.Length - 1) (cur + dir))
                                if next <> cur then
                                    dispatch (SetRefPickIdx next)
                        | _ -> ()
                    else
                        // Navigate the flat list of visible rows —
                        // action rows + input rows of expanded actions.
                        stepFocus dispatch doc dir
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
