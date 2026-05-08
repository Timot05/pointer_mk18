module PointerMk18.Ui.BlockList

open Fable.Core
open Fable.Core.JsInterop
open Server
open Server.Lang
open PointerMk18.Ui
open Browser.Dom
open Browser.Types

// ---------------------------------------------------------------------------
// BlockList — sidebar panel listing the active notebook's blocks.
//
// Replaces ActionList.fs in `panel-host-actions` when the notebook model is
// active. Keeps the panel pattern (header + scrollable list + sync at the
// bottom) familiar so the UX feels continuous with the old action list.
// ---------------------------------------------------------------------------

let private kindLabel (kind: Notebook.BlockKind) : string =
    match kind with
    | Notebook.ScriptBlock _ -> "Script"
    | Notebook.SketchBlock _ -> "Sketch"

let private renderRow
        (dispatch: Message -> unit)
        (doc: DocumentView)
        (block: Notebook.Block) : HTMLElement =
    let row = Dom.el "div" "block-row"
    if doc.SelectedBlockId = Some block.Id then
        row.classList.add "block-row-selected"

    let label = Dom.elText "span" "block-row-name" block.Name
    let kind = Dom.elText "span" "block-row-kind" (kindLabel block.Kind)

    row.appendChild (label :> Node) |> ignore
    row.appendChild (kind :> Node) |> ignore

    let deleteBtn = Dom.elText "button" "block-row-delete" "×"
    deleteBtn.title <- "Delete block"
    deleteBtn.addEventListener (
        "click",
        fun e ->
            e.stopPropagation ()
            dispatch (DeleteBlock block.Id))
    row.appendChild (deleteBtn :> Node) |> ignore

    row.addEventListener (
        "click",
        fun _ ->
            dispatch (SelectBlock block.Id)
            match block.Kind with
            | Notebook.ScriptBlock _ -> dispatch (OpenScriptEditor block.Id)
            | Notebook.SketchBlock _ -> ())
    row

let private renderActionsBar (dispatch: Message -> unit) : HTMLElement =
    let bar = Dom.el "div" "block-list-actions"

    let addScript = Dom.elText "button" "topbar-button" "+ Script"
    addScript.addEventListener ("click", fun _ -> dispatch AddScriptBlock)
    bar.appendChild (addScript :> Node) |> ignore

    let addSketch = Dom.elText "button" "topbar-button" "+ Sketch"
    addSketch.addEventListener ("click", fun _ -> dispatch AddSketchBlock)
    bar.appendChild (addSketch :> Node) |> ignore
    bar

let render (dispatch: Message -> unit) (doc: DocumentView) : HTMLElement =
    let panel = Dom.el "div" "panel"

    let header = Dom.elText "div" "panel-header" "Blocks"
    panel.appendChild (header :> Node) |> ignore

    panel.appendChild (renderActionsBar dispatch :> Node) |> ignore

    let list = Dom.el "div" "block-list"
    for block in doc.Blocks do
        list.appendChild (renderRow dispatch doc block :> Node) |> ignore
    panel.appendChild (list :> Node) |> ignore

    let runBar = Dom.el "div" "block-list-run"
    let runBtn = Dom.elText "button" "topbar-button block-run-btn" "Run"
    runBtn.addEventListener ("click", fun _ -> dispatch RunNotebook)
    runBar.appendChild (runBtn :> Node) |> ignore
    panel.appendChild (runBar :> Node) |> ignore

    match doc.LastNotebookError with
    | Some msg ->
        let err = Dom.elText "div" "block-list-error" msg
        panel.appendChild (err :> Node) |> ignore
    | None -> ()

    panel

let syncPanel (root: HTMLElement) (dispatch: Message -> unit) (doc: DocumentView) : unit =
    match root.querySelector ".panel-host-actions" with
    | :? HTMLElement as host ->
        let prevScroll =
            match host.querySelector ".block-list" with
            | :? HTMLElement as list -> list.scrollTop
            | _ -> 0.0
        host.innerHTML <- ""
        host.appendChild (render dispatch doc :> Node) |> ignore
        match host.querySelector ".block-list" with
        | :? HTMLElement as list -> list.scrollTop <- prevScroll
        | _ -> ()
    | _ -> ()
