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

// Shell.render rebuilds the topbar on every state change. The old
// dropdowns used `document.addEventListener` to close themselves on
// outside clicks, but that attached a fresh global listener per
// render \u2014 over a session the doc accumulates hundreds of stale
// closures, each firing on every click. We instead install ONE
// global listener (module-level guard) that walks the DOM and closes
// any element matching `.topbar-dropdown-open`. Per-dropdown logic
// just toggles that class.
let mutable private documentListenerInstalled = false

let private installGlobalDropdownCloser () =
    if not documentListenerInstalled then
        documentListenerInstalled <- true
        document.addEventListener (
            "click",
            fun _ ->
                let openDropdowns =
                    document.querySelectorAll ".topbar-dropdown-open"
                for i in 0 .. openDropdowns.length - 1 do
                    let el = openDropdowns.[i] :?> HTMLElement
                    el.classList.remove "topbar-dropdown-open"
                    el?style?display <- "none"
        )

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
        (onLoadExample: string -> unit)
        (scriptOpen: bool) : HTMLElement =
    installGlobalDropdownCloser ()
    let topbar = Dom.el "div" "topbar"
    topbar.appendChild (Dom.elText "span" "topbar-logo" "Dekal" :> Node) |> ignore

    // Close every currently-open top-bar dropdown. Used by each
    // dropdown button before opening itself so two dropdowns can't
    // be open at the same time.
    let closeAllDropdowns () =
        let openDropdowns = document.querySelectorAll ".topbar-dropdown-open"
        for i in 0 .. openDropdowns.length - 1 do
            let el = openDropdowns.[i] :?> HTMLElement
            el.classList.remove "topbar-dropdown-open"
            el?style?display <- "none"

    let fileMenu = Dom.el "div" "topbar-menu"
    let fileBtn = Dom.elText "button" "topbar-button" "File"
    let fileDropdown = Dom.el "div" "topbar-dropdown"
    fileDropdown?style?display <- "none"

    let closeFile () =
        fileDropdown.classList.remove "topbar-dropdown-open"
        fileDropdown?style?display <- "none"

    let saveBtn = dropdownItem "Save" (Some (modKey + "S"))
    saveBtn.addEventListener (
        "click",
        fun _ ->
            closeFile ()
            onSave ()
    )

    let loadBtn = dropdownItem "Load" (Some (modKey + "O"))
    loadBtn.addEventListener (
        "click",
        fun _ ->
            closeFile ()
            onLoad ()
    )

    let clearBtn = dropdownItem "Clear" None
    clearBtn.addEventListener (
        "click",
        fun _ ->
            closeFile ()
            dispatch ClearModel
    )

    fileBtn.addEventListener (
        "click",
        fun e ->
            e.stopPropagation ()
            let wasOpen = fileDropdown.classList.contains "topbar-dropdown-open"
            closeAllDropdowns ()
            if not wasOpen then
                fileDropdown.classList.add "topbar-dropdown-open"
                fileDropdown?style?display <- "flex"
    )

    fileDropdown.appendChild (saveBtn :> Node) |> ignore
    fileDropdown.appendChild (loadBtn :> Node) |> ignore
    fileDropdown.appendChild (clearBtn :> Node) |> ignore
    fileMenu.appendChild (fileBtn :> Node) |> ignore
    fileMenu.appendChild (fileDropdown :> Node) |> ignore
    topbar.appendChild (fileMenu :> Node) |> ignore

    // Examples dropdown. One entry per JSON file in
    // `ui/defaults/examples/`; clicking an entry replaces the
    // current document with the example's contents (same wire path
    // as Load, just sourced from a bundled string instead of a
    // file picker). If the directory is empty, the dropdown shows
    // a disabled-looking hint instead of being blank.
    let examplesMenu = Dom.el "div" "topbar-menu"
    let examplesBtn = Dom.elText "button" "topbar-button" "Examples"
    let examplesDropdown = Dom.el "div" "topbar-dropdown"
    examplesDropdown?style?display <- "none"

    let examples = Examples.all ()
    match examples with
    | [] ->
        let empty = Dom.el "div" "topbar-dropdown-item topbar-dropdown-empty"
        empty.appendChild (Dom.elText "span" "" "(none yet)" :> Node) |> ignore
        empty.title <- "Drop saved-document JSON files into ui/defaults/examples/"
        examplesDropdown.appendChild (empty :> Node) |> ignore
    | _ ->
        for label, jsonContent in examples do
            let item = dropdownItem label None
            item.addEventListener (
                "click",
                fun _ ->
                    examplesDropdown.classList.remove "topbar-dropdown-open"
                    examplesDropdown?style?display <- "none"
                    onLoadExample jsonContent
            )
            examplesDropdown.appendChild (item :> Node) |> ignore

    examplesBtn.addEventListener (
        "click",
        fun e ->
            e.stopPropagation ()
            let wasOpen = examplesDropdown.classList.contains "topbar-dropdown-open"
            closeAllDropdowns ()
            if not wasOpen then
                examplesDropdown.classList.add "topbar-dropdown-open"
                examplesDropdown?style?display <- "flex"
    )

    examplesMenu.appendChild (examplesBtn :> Node) |> ignore
    examplesMenu.appendChild (examplesDropdown :> Node) |> ignore
    topbar.appendChild (examplesMenu :> Node) |> ignore

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
