module PointerMk18.Ui.TopBar

open Fable.Core.JsInterop
open Server
open PointerMk18.Ui
open Browser.Dom
open Browser.Types

// ---------------------------------------------------------------------------
// Top bar: logo + File menu (Save / Load / Clear).
//
// Save and Load currently stubbed — full JSON round-trip between the F#
// domain and the on-disk format is a separate problem (Fable's default
// union encoding differs from the .NET server's `case`+NamedFields format).
// Clear dispatches ClearModel directly.
// ---------------------------------------------------------------------------

let private modKey : string =
    let platform : string = emitJsExpr () "navigator.platform.toLowerCase()"
    if platform.Contains "mac" then "\u2318" else "Ctrl"

/// Right-side toggle that flips between the WASM kernel renderer and the
/// GPU raymarcher. Subscribes to the app store so the label tracks the
/// currently active mode without us having to re-render the whole topbar.
let private renderViewerModeToggle (dispatch: Message -> unit) : HTMLElement =
    let btn = Dom.elText "button" "topbar-button topbar-mode-toggle" ""

    let updateLabel () =
        btn.textContent <-
            match AppStore.store.State.ViewerMode with
            | IntervalKernel -> "Renderer: Kernel"
            | Raymarch -> "Renderer: Raymarch"

    updateLabel ()
    Store.subscribe AppStore.store updateLabel

    btn.addEventListener (
        "click",
        fun _ ->
            let next =
                match AppStore.store.State.ViewerMode with
                | IntervalKernel -> Raymarch
                | Raymarch -> IntervalKernel
            dispatch (SetViewerMode next)
    )
    btn

let private dropdownItem (label: string) (shortcut: string option) : HTMLButtonElement =
    let btn = Dom.el "button" "topbar-dropdown-item" :?> HTMLButtonElement
    btn.appendChild (Dom.elText "span" "" label :> Node) |> ignore
    match shortcut with
    | Some keys -> btn.appendChild (Dom.kbdHint keys :> Node) |> ignore
    | None -> ()
    btn

let render
        (dispatch: Message -> unit)
        (onSave: unit -> unit)
        (onLoad: unit -> unit) : HTMLElement =
    let topbar = Dom.el "div" "topbar"
    topbar.appendChild (Dom.elText "span" "topbar-logo" "Dekal" :> Node) |> ignore

    let fileMenu = Dom.el "div" "topbar-menu"
    let fileBtn = Dom.elText "button" "topbar-button" "File"
    let fileDropdown = Dom.el "div" "topbar-dropdown"
    fileDropdown?style?display <- "none"

    let saveBtn = dropdownItem "Save" (Some (modKey + "S"))
    saveBtn.addEventListener (
        "click",
        fun _ ->
            fileDropdown?style?display <- "none"
            onSave ()
    )

    let loadBtn = dropdownItem "Load" (Some (modKey + "O"))
    loadBtn.addEventListener (
        "click",
        fun _ ->
            fileDropdown?style?display <- "none"
            onLoad ()
    )

    let clearBtn = dropdownItem "Clear" None
    clearBtn.addEventListener (
        "click",
        fun _ ->
            fileDropdown?style?display <- "none"
            dispatch ClearModel
    )

    // Complex demo scene — ~100 prims spread across three surfaces
    // (sphere grid, cylinder ring, perforated slab). Exercises the
    // raymarcher's per-block pruning + the new global-prune pre-pass
    // with enough stuff that timing differences are visible.
    let stressBtn = dropdownItem "Load stress example" None
    stressBtn.addEventListener (
        "click",
        fun _ ->
            fileDropdown?style?display <- "none"
            dispatch (ReplaceDocument(Server.Document.stressDocument ()))
    )

    fileBtn.addEventListener (
        "click",
        fun e ->
            e.stopPropagation ()
            fileDropdown?style?display <-
                if unbox<string> (fileDropdown?style?display) = "none" then "flex" else "none"
    )

    document.addEventListener (
        "click",
        fun _ -> fileDropdown?style?display <- "none"
    )

    fileDropdown.appendChild (saveBtn :> Node) |> ignore
    fileDropdown.appendChild (loadBtn :> Node) |> ignore
    fileDropdown.appendChild (clearBtn :> Node) |> ignore
    fileDropdown.appendChild (stressBtn :> Node) |> ignore
    fileMenu.appendChild (fileBtn :> Node) |> ignore
    fileMenu.appendChild (fileDropdown :> Node) |> ignore
    topbar.appendChild (fileMenu :> Node) |> ignore
    topbar.appendChild (Dom.el "span" "topbar-spacer" :> Node) |> ignore
    // Renderer-mode toggle suppressed: GPU raymarcher is dormant in
    // notebook mode (it consumes FieldNode which is no longer produced).
    // The toggle (and SetViewerMode message) stay defined for the day we
    // wire a MathIR-aware GPU consumer.
    topbar
