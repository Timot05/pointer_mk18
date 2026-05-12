module PointerMk18.Ui.Shortcuts

open Fable.Core.JsInterop
open Server
open PointerMk18.Ui
open Browser.Dom
open Browser.Types

// ---------------------------------------------------------------------------
// Global keyboard shortcuts.
//
// Notebook-mode only. The action-anchored row navigation (Up/Down through a
// flat list of actions+inputs, Left/Right to cycle ref/select/check inputs,
// inline numeric editor on Enter etc.) was retired with the action graph.
// What remains: palette toggle, sketch-mode tool/constraint shortcuts,
// Escape, Backspace/Delete, visibility cycle, sketch-edit toggle.
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

let private selectedBlock (doc: DocumentView) : Server.Lang.Notebook.Block option =
    doc.SelectedBlockId
    |> Option.bind (fun bid -> doc.Blocks |> List.tryFind (fun b -> b.Id = bid))

let private selectedIsSketchBlock (doc: DocumentView) : bool =
    match selectedBlock doc with
    | Some b ->
        match b.Body with
        | Server.Lang.Notebook.SketchBody _ -> true
        | _ -> false
    | None -> false

// ── Sketch-mode shortcuts ──────────────────────────────────────────────

/// Returns true if the key was handled, so the global handler can stop.
let private handleSketchShortcut
        (dispatch: Message -> unit)
        (doc: DocumentView)
        (e: KeyboardEvent)
        : bool =
    if not doc.SketchUi.EditMode || not (selectedIsSketchBlock doc) then false
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
                // Palette open — Escape closes it globally; other keys are
                // handled by the palette input when focused.
                if getPaletteOpen () then
                    if ke.key = "Escape" then
                        e.preventDefault ()
                        BlockList.closePalette ()
                else

                let target : obj = e?target
                if isEditable target then () else

                let doc = getDoc ()

                // Sketch-mode shortcuts first.
                if handleSketchShortcut dispatch doc ke then () else

                match ke.key with
                | "Escape" when doc.EditingBlockRef.IsSome ->
                    // Cancel block-ref pick mode.
                    e.preventDefault ()
                    dispatch CancelPickBlockRef
                | "Delete" | "Backspace" ->
                    e.preventDefault ()
                    dispatch DeleteIntent
                | "v" ->
                    // Cycle visibility of the selected block.
                    match selectedBlock doc with
                    | Some b ->
                        e.preventDefault ()
                        dispatch (CycleBlockVisibility b.Id)
                    | None -> ()
                | "e" | "E" ->
                    // Toggle sketch-edit mode when a sketch block is selected.
                    if selectedIsSketchBlock doc then
                        e.preventDefault ()
                        dispatch ToggleSketchEdit
                | "ArrowDown" | "ArrowUp" ->
                    // Move selection across blocks in source order. Mirrors
                    // the main-branch action-row navigation (Up/Down through
                    // a flat list); now operates on `doc.Blocks` instead.
                    // No-selection → falls into the first/last block.
                    if not doc.Blocks.IsEmpty then
                        let step = if ke.key = "ArrowDown" then 1 else -1
                        let len = List.length doc.Blocks
                        let currentIdx =
                            doc.SelectedBlockId
                            |> Option.bind (fun id -> doc.Blocks |> List.tryFindIndex (fun b -> b.Id = id))
                        let nextIdx =
                            match currentIdx with
                            | Some i -> ((i + step) % len + len) % len
                            | None -> if step = 1 then 0 else len - 1
                        e.preventDefault ()
                        dispatch (SelectBlock doc.Blocks.[nextIdx].Id)
                | "ArrowRight" ->
                    match selectedBlock doc with
                    | Some b ->
                        e.preventDefault ()
                        dispatch (ExpandBlock b.Id)
                    | None -> ()
                | "ArrowLeft" ->
                    match selectedBlock doc with
                    | Some b ->
                        e.preventDefault ()
                        dispatch (CollapseBlock b.Id)
                    | None -> ()
                | _ -> ()
    )
