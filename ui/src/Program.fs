module PointerMk18.Ui.Program

open Browser.Dom
open Server
open PointerMk18.Ui

// --------------------------------------------------------------------------
// Entry point: owns the F# editor store, subscribes, rebuilds the Shell on
// every dispatch. Pure pull-based re-render, same pattern the TS code used
// but without the normalization layer in between.
// --------------------------------------------------------------------------

let private store =
    Store.create Editor.update (Editor.initState ())

let private dispatch msg = Store.dispatch store msg

let private renderInto (root: Browser.Types.HTMLElement) =
    let doc = DocumentPipeline.documentView store.State
    let shell = Shell.render dispatch doc
    root.innerHTML <- ""
    root.appendChild shell |> ignore

let private mount () =
    let root = document.getElementById "app"
    if isNull root then
        failwith "Missing #app element"
    Store.subscribe store (fun () -> renderInto root)
    renderInto root

mount ()
