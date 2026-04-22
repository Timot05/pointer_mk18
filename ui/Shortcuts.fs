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
                    e.preventDefault ()
                    dispatch PaletteOpen
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
                | "Delete" | "Backspace" ->
                    e.preventDefault ()
                    dispatch DeleteIntent
                | "ArrowDown" | "ArrowUp" ->
                    e.preventDefault ()
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
                    // Toggle the eye on the selected action — creates
                    // one if missing, deletes it if already attached.
                    match selectedAction doc with
                    | Some sel ->
                        e.preventDefault ()
                        match doc.Eyes |> List.tryFind (fun eye -> eye.TargetActionId = sel.Id) with
                        | Some eye -> dispatch (DeleteEye eye.Id)
                        | None -> dispatch (CreateEyeFor sel.Id)
                    | None -> ()
                | "s" ->
                    // Ensure an eye is attached and toggle its
                    // iso-surface display. The reducer handles the
                    // create-if-missing case.
                    match selectedAction doc with
                    | Some sel ->
                        e.preventDefault ()
                        dispatch (ToggleActionSurface sel.Id)
                    | None -> ()
                | "e" | "E" ->
                    match selectedAction doc with
                    | Some { Kind = Sketch _; Id = id } ->
                        e.preventDefault ()
                        dispatch ToggleSketchEdit
                        // id used only to match; suppress unused warning
                        ignore id
                    | _ -> ()
                | "f" ->
                    // Ensure an eye is attached and toggle its
                    // field-slice overlay.
                    match selectedAction doc with
                    | Some sel ->
                        e.preventDefault ()
                        dispatch (ToggleActionFieldSlice sel.Id)
                    | None -> ()
                | _ -> ()
    )
