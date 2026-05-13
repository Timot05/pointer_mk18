module PointerMk18.Ui.TopBar

open Fable.Core.JsInterop
open Server
open PointerMk18.Ui
open Browser.Dom
open Browser.Types

// ---------------------------------------------------------------------------
// Top bar: logo + File menu (Save / Load / Clear).
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

let render
        (dispatch: Message -> unit)
        (onSave: unit -> unit)
        (onLoad: unit -> unit)
        (scriptOpen: bool) : HTMLElement =
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

    // Script editor toggle. `.topbar-button-active` paints the bold/primary
    // state when the panel is open (mirrors how block-list selection works).
    let scriptBtnClass =
        if scriptOpen then "topbar-button topbar-button-active"
        else "topbar-button"
    let scriptBtn = Dom.elText "button" scriptBtnClass "Script"
    scriptBtn.title <- "Toggle DSL script editor"
    scriptBtn.addEventListener (
        "click",
        fun e ->
            e.stopPropagation ()
            dispatch ToggleScriptEditor
    )
    topbar.appendChild (scriptBtn :> Node) |> ignore

    topbar.appendChild (Dom.el "span" "topbar-spacer" :> Node) |> ignore

    let githubLink = Dom.el "a" "topbar-github"
    githubLink.setAttribute ("href", "https://github.com/Timot05/pointer_mk18")
    githubLink.setAttribute ("target", "_blank")
    githubLink.setAttribute ("rel", "noopener noreferrer")
    githubLink.title <- "View source on GitHub"
    githubLink.appendChild (Icons.github () :> Node) |> ignore
    topbar.appendChild (githubLink :> Node) |> ignore

    topbar
