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

let private dropdownItem (label: string) (shortcut: string option) : HTMLButtonElement =
    let btn = Dom.el "button" "topbar-dropdown-item" :?> HTMLButtonElement
    btn.appendChild (Dom.elText "span" "" label :> Node) |> ignore
    match shortcut with
    | Some keys -> btn.appendChild (Dom.kbdHint keys :> Node) |> ignore
    | None -> ()
    btn

let private viewerModeLabel (mode: ViewerMode) : string =
    match mode with
    | IntervalKernel -> "Interval"
    | Raymarch -> "Raymarch"

let private viewerModeToggle
        (dispatch: Message -> unit) (mode: ViewerMode) : HTMLElement =
    // Two-button segmented control; clicking the inactive mode flips it.
    let group = Dom.el "div" "topbar-segmented"
    let makeBtn (target: ViewerMode) =
        let cls =
            if mode = target then "topbar-button topbar-button-active"
            else "topbar-button"
        let btn = Dom.elText "button" cls (viewerModeLabel target) :?> HTMLButtonElement
        btn.addEventListener ("click", fun _ -> dispatch (SetViewerMode target))
        btn
    group.appendChild (makeBtn IntervalKernel :> Node) |> ignore
    group.appendChild (makeBtn Raymarch :> Node) |> ignore
    group

let render
        (dispatch: Message -> unit)
        (viewerMode: ViewerMode)
        (onSave: unit -> unit)
        (onLoad: unit -> unit) : HTMLElement =
    let topbar = Dom.el "div" "topbar"
    topbar.appendChild (Dom.elText "span" "topbar-logo" "pointer mk18" :> Node) |> ignore

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
    fileMenu.appendChild (fileBtn :> Node) |> ignore
    fileMenu.appendChild (fileDropdown :> Node) |> ignore
    topbar.appendChild (fileMenu :> Node) |> ignore
    topbar.appendChild (Dom.el "span" "topbar-spacer" :> Node) |> ignore
    topbar.appendChild (viewerModeToggle dispatch viewerMode :> Node) |> ignore
    topbar
