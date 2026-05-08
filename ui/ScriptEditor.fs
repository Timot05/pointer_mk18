module PointerMk18.Ui.ScriptEditor

open Fable.Core
open Fable.Core.JsInterop
open Server
open Server.Lang
open PointerMk18.Ui
open Browser.Dom
open Browser.Types

// ---------------------------------------------------------------------------
// ScriptEditor — modal overlay opened when a ScriptBlock is selected.
// Mount lifecycle mirrors CommandPalette: a `sync()` hook is called from
// Program.fs after every dispatch; it reads `doc.OpenedScriptBlockId` and
// mounts/unmounts accordingly.
//
// UI: a `<textarea>` for the source, an inputs list (name = expression),
// the last notebook error if any, plus Cancel / Run buttons.
// ---------------------------------------------------------------------------

let mutable private backdrop : HTMLElement option = None
let mutable private mountedFor : Notebook.BlockId option = None
let mutable private cleanupKeydown : (unit -> unit) option = None

let private unmount () =
    match cleanupKeydown with
    | Some fn -> fn (); cleanupKeydown <- None
    | None -> ()
    match backdrop with
    | Some bd -> bd.remove (); backdrop <- None
    | None -> ()
    mountedFor <- None

let private findBlock (doc: DocumentView) (id: Notebook.BlockId) : Notebook.Block option =
    doc.Blocks |> List.tryFind (fun b -> b.Id = id)

let private scriptOf (block: Notebook.Block) : Notebook.Script option =
    match block.Kind with
    | Notebook.ScriptBlock s -> Some s
    | _ -> None

let private renderInputsList
        (dispatch: Message -> unit)
        (block: Notebook.Block)
        (script: Notebook.Script) : HTMLElement =
    let container = Dom.el "div" "script-editor-inputs"

    let header = Dom.elText "div" "script-editor-inputs-header" "Inputs:"
    container.appendChild (header :> Node) |> ignore

    let updateAt (idx: int) (newPair: string * string) =
        let updated =
            script.Inputs
            |> List.mapi (fun i p -> if i = idx then newPair else p)
        dispatch (UpdateBlockInputs(block.Id, updated))

    let removeAt (idx: int) =
        let updated = script.Inputs |> List.mapi (fun i p -> i, p) |> List.filter (fun (i, _) -> i <> idx) |> List.map snd
        dispatch (UpdateBlockInputs(block.Id, updated))

    script.Inputs
    |> List.iteri (fun i (name, expr) ->
        let row = Dom.el "div" "script-editor-input-row"

        let nameInput = Dom.el "input" "script-editor-input-name"
        nameInput?``type`` <- "text"
        nameInput?value <- name
        nameInput?placeholder <- "name"
        nameInput.addEventListener (
            "input",
            fun _ ->
                let v : string = nameInput?value
                updateAt i (v, expr))
        row.appendChild (nameInput :> Node) |> ignore

        let eq = Dom.elText "span" "script-editor-input-eq" "="
        row.appendChild (eq :> Node) |> ignore

        let exprInput = Dom.el "input" "script-editor-input-expr"
        exprInput?``type`` <- "text"
        exprInput?value <- expr
        exprInput?placeholder <- "expression"
        exprInput.addEventListener (
            "input",
            fun _ ->
                let v : string = exprInput?value
                updateAt i (name, v))
        row.appendChild (exprInput :> Node) |> ignore

        let delBtn = Dom.elText "button" "script-editor-input-delete" "×"
        delBtn.addEventListener ("click", fun _ -> removeAt i)
        row.appendChild (delBtn :> Node) |> ignore

        container.appendChild (row :> Node) |> ignore)

    let addBtn = Dom.elText "button" "script-editor-input-add" "+ add input wire"
    addBtn.addEventListener (
        "click",
        fun _ ->
            let updated = script.Inputs @ [ ("", "") ]
            dispatch (UpdateBlockInputs(block.Id, updated)))
    container.appendChild (addBtn :> Node) |> ignore
    container

let private mount (dispatch: Message -> unit) (doc: DocumentView) (block: Notebook.Block) (script: Notebook.Script) =
    let bd = Dom.el "div" "script-editor-backdrop"
    bd.addEventListener (
        "click",
        fun e ->
            // Only close if clicking the backdrop directly, not a child.
            if (e.target :> obj) = (bd :> obj) then
                dispatch CloseScriptEditor)

    let modal = Dom.el "div" "script-editor-modal"
    modal.setAttribute ("role", "dialog")

    // Header
    let header = Dom.el "div" "script-editor-header"
    let title = Dom.elText "span" "script-editor-title" (sprintf "Edit script: %s" block.Name)
    header.appendChild (title :> Node) |> ignore

    let closeBtn = Dom.elText "button" "script-editor-close" "×"
    closeBtn.addEventListener ("click", fun _ -> dispatch CloseScriptEditor)
    header.appendChild (closeBtn :> Node) |> ignore
    modal.appendChild (header :> Node) |> ignore

    // Textarea for source
    let textarea = Dom.el "textarea" "script-editor-textarea"
    textarea?value <- script.Source
    textarea?spellcheck <- false
    textarea?wrap <- "off"
    textarea.addEventListener (
        "input",
        fun _ ->
            let v : string = textarea?value
            dispatch (UpdateBlockSource(block.Id, v)))
    modal.appendChild (textarea :> Node) |> ignore

    // Inputs section
    modal.appendChild (renderInputsList dispatch block script :> Node) |> ignore

    // Errors
    match doc.LastNotebookError with
    | Some msg ->
        let errBox = Dom.el "div" "script-editor-error"
        let errLabel = Dom.elText "span" "script-editor-error-label" "Error: "
        errBox.appendChild (errLabel :> Node) |> ignore
        errBox.appendChild (Dom.elText "span" "script-editor-error-msg" msg :> Node) |> ignore
        modal.appendChild (errBox :> Node) |> ignore
    | None -> ()

    // Footer buttons
    let footer = Dom.el "div" "script-editor-footer"

    let cancelBtn = Dom.elText "button" "topbar-button" "Cancel"
    cancelBtn.addEventListener ("click", fun _ -> dispatch CloseScriptEditor)
    footer.appendChild (cancelBtn :> Node) |> ignore

    let runBtn = Dom.elText "button" "topbar-button script-editor-run" "Run"
    runBtn.addEventListener ("click", fun _ -> dispatch RunNotebook)
    footer.appendChild (runBtn :> Node) |> ignore

    modal.appendChild (footer :> Node) |> ignore

    bd.appendChild (modal :> Node) |> ignore
    document.body.appendChild (bd :> Node) |> ignore

    // Focus textarea so the user can start typing immediately.
    textarea.focus ()

    // Escape key closes the editor.
    let handler =
        fun (e: Event) ->
            let ke = e :?> KeyboardEvent
            if ke.key = "Escape" then
                e.preventDefault ()
                dispatch CloseScriptEditor
    document.addEventListener ("keydown", handler)
    cleanupKeydown <- Some(fun () -> document.removeEventListener ("keydown", handler))

    backdrop <- Some bd
    mountedFor <- Some block.Id

/// Entry point called from Program.fs after every dispatch. Reads
/// `doc.OpenedScriptBlockId` and mounts/unmounts accordingly. If the same
/// block is already mounted, leaves the DOM alone (preserves caret position).
let sync (dispatch: Message -> unit) (doc: DocumentView) =
    match doc.OpenedScriptBlockId with
    | None -> unmount ()
    | Some id ->
        match findBlock doc id with
        | None -> unmount ()
        | Some block ->
            match scriptOf block with
            | None -> unmount ()
            | Some script ->
                if mountedFor = Some id then
                    // Already mounted; refresh error display only (don't
                    // rebuild the whole modal — it would lose textarea
                    // caret position and focus).
                    match backdrop with
                    | Some bd ->
                        match bd.querySelector ".script-editor-error" with
                        | :? HTMLElement as old -> old.remove ()
                        | _ -> ()
                        match doc.LastNotebookError with
                        | Some msg ->
                            let modal = bd.querySelector ".script-editor-modal" :?> HTMLElement
                            let footer = bd.querySelector ".script-editor-footer" :?> HTMLElement
                            let errBox = Dom.el "div" "script-editor-error"
                            let errLabel = Dom.elText "span" "script-editor-error-label" "Error: "
                            errBox.appendChild (errLabel :> Node) |> ignore
                            errBox.appendChild (Dom.elText "span" "script-editor-error-msg" msg :> Node) |> ignore
                            modal.insertBefore (errBox :> Node, footer :> Node) |> ignore
                        | None -> ()
                    | None -> ()
                else
                    unmount ()
                    mount dispatch doc block script
