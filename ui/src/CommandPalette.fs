module PointerMk18.Ui.CommandPalette

open Fable.Core
open Fable.Core.JsInterop
open Server
open PointerMk18.Ui
open Browser.Dom
open Browser.Types

// ---------------------------------------------------------------------------
// Command palette — mounted imperatively outside the main render tree so
// the F# shell doesn't have to re-render it on every dispatch. Ported from
// user-interface/src/command-palette.ts.
//
// Lifecycle:
//   Shell dispatches `PaletteOpen` → state.IsOpen flips true → sync()
//   mounts. Subsequent dispatches call sync() which re-reads state and
//   re-mounts or unmounts. On backspace/escape/click-backdrop we dispatch
//   `PaletteClose` which flips IsOpen and unmounts via sync().
// ---------------------------------------------------------------------------

// Module-level mutable state. The palette is a singleton — at most one is
// mounted at any time.
let mutable private backdrop : HTMLElement option = None
let mutable private activeIndex = 0
let mutable private currentItems : PaletteItem list = []
let mutable private cleanupKeydown : (unit -> unit) option = None

[<Emit("Math.random().toString(36).slice(2, 8)")>]
let private randomSuffix () : string = jsNative

// ── Unmount ────────────────────────────────────────────────────────────

let private unmount () =
    match cleanupKeydown with
    | Some fn ->
        fn ()
        cleanupKeydown <- None
    | None -> ()
    match backdrop with
    | Some bd ->
        bd.remove ()
        backdrop <- None
    | None -> ()

// ── Results list ───────────────────────────────────────────────────────

let private buildResultsList (dispatch: Message -> unit)
                             (getDocActionCount: unit -> int)
                             (items: PaletteItem list)
                             (sync: bool -> unit) : HTMLElement =
    let results = Dom.el "div" "palette-results"
    if items.IsEmpty then
        results.appendChild (Dom.elText "div" "palette-empty" "No matches" :> Node) |> ignore
    else
        if activeIndex >= items.Length then
            activeIndex <- max 0 (items.Length - 1)
        items
        |> List.iteri (fun i item ->
            let btn = Dom.el "button" "palette-item"
            if i = activeIndex then btn.classList.add "is-active"
            btn.appendChild (Icons.forKindName item.Kind :> Node) |> ignore
            btn.appendChild (Dom.elText "span" "palette-label" item.Label :> Node) |> ignore
            btn.addEventListener (
                "mouseenter",
                fun _ ->
                    activeIndex <- i
                    let nodes = results.querySelectorAll ".palette-item"
                    for j in 0 .. nodes.length - 1 do
                        let n = nodes.[j] :?> HTMLElement
                        if j = i then n.classList.add "is-active"
                        else n.classList.remove "is-active"
            )
            btn.addEventListener (
                "click",
                fun _ ->
                    let before = getDocActionCount ()
                    dispatch (PalettePick item.Id)
                    let after = getDocActionCount ()
                    sync (after <> before)
            )
            results.appendChild (btn :> Node) |> ignore)
    results

let private patchResults (dispatch: Message -> unit)
                         (getDocActionCount: unit -> int)
                         (items: PaletteItem list)
                         (sync: bool -> unit) =
    match backdrop with
    | None -> ()
    | Some bd ->
        let old = bd.querySelector ".palette-results"
        if not (isNull old) then
            activeIndex <- 0
            currentItems <- items
            let fresh = buildResultsList dispatch getDocActionCount items sync
            old.parentNode.replaceChild (fresh, old) |> ignore

// ── Mount ──────────────────────────────────────────────────────────────

let private mount (dispatch: Message -> unit)
                  (getPaletteState: unit -> PaletteState)
                  (getDocActionCount: unit -> int)
                  (sync: bool -> unit)
                  (state: PaletteState) =
    unmount ()
    if not state.IsOpen then () else

    let bd = Dom.el "div" "palette-backdrop"
    let palette = Dom.el "div" "palette"
    palette.setAttribute ("role", "dialog")

    // Top row: chips + input/prompt
    let row = Dom.el "div" "palette-row"

    match state.PickedKind with
    | Some kind ->
        let cmdChip = Dom.el "span" "chip chip-command"
        cmdChip.appendChild (Icons.forKindName kind :> Node) |> ignore
        cmdChip.appendChild (Dom.elText "span" "" kind :> Node) |> ignore
        row.appendChild (cmdChip :> Node) |> ignore
    | None -> ()

    for chip in state.Chips do
        let c = Dom.el "span" "chip"
        c.appendChild (Dom.elText "span" "chip-label" (chip.Label + ":") :> Node) |> ignore
        c.appendChild (Dom.elText "span" "chip-value" chip.Value :> Node) |> ignore
        row.appendChild (c :> Node) |> ignore

    let mutable inputOpt : HTMLInputElement option = None

    if state.Mode = "command" || state.Mode = "ref" then
        let input = document.createElement "input" :?> HTMLInputElement
        input.``type`` <- "text"
        input.className <- "palette-input"
        input.placeholder <- state.Prompt
        input.spellcheck <- false
        input.autocomplete <- "off"
        row.appendChild (input :> Node) |> ignore
        inputOpt <- Some input
    elif state.Mode = "scalars" && state.Chips.IsEmpty && state.PickedKind.IsSome then
        row.appendChild (Dom.elText "span" "prompt-label" "set values:" :> Node) |> ignore

    palette.appendChild (row :> Node) |> ignore

    // Scalar fields row
    if state.Mode = "scalars" && not state.ScalarFields.IsEmpty then
        let valRow = Dom.el "div" "value-row"
        for field in state.ScalarFields do
            let cell = Dom.el "div" "value-cell"
            cell.appendChild (Dom.elText "span" "value-axis" field.Label :> Node) |> ignore
            let valSpan = Dom.elText "span" "control-value" (sprintf "%.1f" field.Value)
            Dom.setupDraggable
                valSpan
                field.Value
                (fun v -> dispatch (PaletteSetScalarField(field.Key, v)))
                (fun v -> dispatch (PaletteSetScalarField(field.Key, v)))
            cell.appendChild (valSpan :> Node) |> ignore
            valRow.appendChild (cell :> Node) |> ignore
        palette.appendChild (valRow :> Node) |> ignore

    // Results list
    if state.Mode = "command" || state.Mode = "ref" then
        currentItems <- state.Items
        palette.appendChild (buildResultsList dispatch getDocActionCount state.Items sync :> Node) |> ignore

    // Hint bar
    let hintBar = Dom.el "div" "palette-hint"
    for h in state.HintBar do
        hintBar.appendChild (Dom.elText "span" "" h :> Node) |> ignore
    palette.appendChild (hintBar :> Node) |> ignore

    bd.appendChild (palette :> Node) |> ignore
    document.body.appendChild (bd :> Node) |> ignore
    backdrop <- Some bd

    // Input event handlers
    match inputOpt with
    | None -> ()
    | Some input ->
        let mutable debounceId : float option = None

        input.addEventListener (
            "input",
            fun _ ->
                match debounceId with
                | Some id -> window.clearTimeout id
                | None -> ()
                debounceId <-
                    window.setTimeout (
                        (fun _ ->
                            dispatch (PaletteSetQuery input.value)
                            let next = getPaletteState ()
                            patchResults dispatch getDocActionCount next.Items sync),
                        80
                    )
                    |> Some
        )

        input.addEventListener (
            "keydown",
            fun e ->
                let ke = e :?> KeyboardEvent
                let inPalette = palette.querySelectorAll ".palette-item"
                match ke.key with
                | "Escape" ->
                    e.preventDefault ()
                    e.stopPropagation ()
                    dispatch PaletteClose
                | "Backspace" when input.value = "" && state.Mode <> "command" ->
                    e.preventDefault ()
                    dispatch PaletteBack
                | "ArrowDown" ->
                    e.preventDefault ()
                    if not state.Items.IsEmpty then
                        activeIndex <- (activeIndex + 1) % state.Items.Length
                    for j in 0 .. inPalette.length - 1 do
                        let n = inPalette.[j] :?> HTMLElement
                        if j = activeIndex then n.classList.add "is-active"
                        else n.classList.remove "is-active"
                | "ArrowUp" ->
                    e.preventDefault ()
                    if not state.Items.IsEmpty then
                        activeIndex <- (activeIndex - 1 + state.Items.Length) % state.Items.Length
                    for j in 0 .. inPalette.length - 1 do
                        let n = inPalette.[j] :?> HTMLElement
                        if j = activeIndex then n.classList.add "is-active"
                        else n.classList.remove "is-active"
                | "Enter" ->
                    e.preventDefault ()
                    if ke.metaKey || ke.ctrlKey then
                        dispatch (PaletteFinish(randomSuffix ()))
                    else
                        if activeIndex < currentItems.Length then
                            let item = currentItems.[activeIndex]
                            let before = getDocActionCount ()
                            dispatch (PalettePick item.Id)
                            let after = getDocActionCount ()
                            sync (after <> before)
                | _ -> ()
        )

        window.requestAnimationFrame (fun _ -> input.focus ()) |> ignore

    // Global keydown for scalars mode (no input element to capture keys)
    if state.Mode = "scalars" then
        let handler =
            fun (e: Event) ->
                let ke = e :?> KeyboardEvent
                match ke.key with
                | "Escape" ->
                    e.preventDefault ()
                    dispatch PaletteClose
                | "Backspace" ->
                    e.preventDefault ()
                    dispatch PaletteBack
                | "Enter" ->
                    e.preventDefault ()
                    if ke.metaKey || ke.ctrlKey then
                        dispatch (PaletteFinish(randomSuffix ()))
                    else
                        dispatch PaletteCommitScalars
                | _ -> ()
        document.addEventListener ("keydown", handler)
        cleanupKeydown <- Some (fun () -> document.removeEventListener ("keydown", handler))

    bd.addEventListener (
        "click",
        fun e ->
            let target : obj = e?target
            if target = (bd :> obj) then
                dispatch PaletteClose
    )

// ── Public sync ────────────────────────────────────────────────────────

/// Entry point called from Program.fs after every dispatch. Reads the
/// current palette state and mounts/unmounts accordingly.
let sync (dispatch: Message -> unit)
         (getPaletteState: unit -> PaletteState)
         (getDocActionCount: unit -> int) =
    let rec syncImpl (_afterModelChange: bool) =
        let state = getPaletteState ()
        if not state.IsOpen then
            unmount ()
        else
            activeIndex <- 0
            mount dispatch getPaletteState getDocActionCount syncImpl state
    syncImpl false
