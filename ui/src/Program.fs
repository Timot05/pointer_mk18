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

let private getPaletteState () = DocumentPipeline.paletteView store.State
let private getDocActionCount () =
    (DocumentPipeline.documentView store.State).Actions.Length

let private renderInto (root: Browser.Types.HTMLElement) =
    let doc = DocumentPipeline.documentView store.State
    let shell = Shell.render dispatch doc
    root.innerHTML <- ""
    root.appendChild shell |> ignore

let private onStateChange (root: Browser.Types.HTMLElement) () =
    renderInto root
    // The palette lives outside the shell DOM tree so it owns its own
    // mount/unmount. Re-sync after every dispatch.
    CommandPalette.sync dispatch getPaletteState getDocActionCount

let private getPaletteOpen () = (getPaletteState ()).IsOpen

let private onSave () =
    Browser.Dom.console.warn "save: JSON serialization not yet implemented (Phase 7)"

let private onLoad () =
    Browser.Dom.console.warn "load: JSON deserialization not yet implemented (Phase 7)"

let private mount () =
    let root = document.getElementById "app"
    if isNull root then
        failwith "Missing #app element"
    Store.subscribe store (onStateChange root)
    Shortcuts.register
        dispatch
        (fun () -> DocumentPipeline.documentView store.State)
        getPaletteOpen
        onSave
        onLoad
    renderInto root

mount ()
