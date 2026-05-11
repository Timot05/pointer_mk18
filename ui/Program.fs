module PointerMk18.Ui.Program

open Fable.Core
open Fable.Core.JsInterop
open Browser.Dom
open Browser.Types
open Server
open PointerMk18.Ui

// --- Minimal globals Fable's Browser.Dom doesn't expose directly ---

[<Emit("new Blob($0, $1)")>]
let private newBlob (parts: obj[]) (opts: obj) : obj = jsNative

[<Emit("URL.createObjectURL($0)")>]
let private urlCreateObjectUrl (blob: obj) : string = jsNative

[<Emit("URL.revokeObjectURL($0)")>]
let private urlRevokeObjectUrl (url: string) : unit = jsNative

// --------------------------------------------------------------------------
// Entry point. Owns:
//   - the singleton F# editor store (via AppStore)
//   - the viewer host element (long-lived so WebGPU context survives renders)
//   - the subscription that re-renders the shell on every dispatch
// --------------------------------------------------------------------------

let private store = AppStore.store

let private dispatch msg = Store.dispatch store msg

// Action palette dormant. Block palette state is owned by `BlockList`
// directly (see `BlockList.togglePalette`).
let private getPaletteOpen () = false

// --------------------------------------------------------------------------
// Viewer mount. The F# viewer is now the only viewer; the legacy TS viewer
// was removed alongside the raymarcher. See project memory
// `project_viewer_rewrite.md`.
// --------------------------------------------------------------------------

let private mountViewer (root: Browser.Types.HTMLElement) : JS.Promise<obj> =
    Viewer.mount root

let private viewerHost =
    let host = document.createElement "div"
    host.className <- "panel-center-host"
    host

// --------------------------------------------------------------------------
// Save / Load. Uses Fable's native JSON encoding (round-trips Fable→Fable).
// Not wire-compatible with the old .NET server save format — new regime.
// --------------------------------------------------------------------------

let private onSave () =
    // Save / load was action-graph flavoured. Notebook-mode persistence
    // is a future feature — needs a block-aware on-disk format.
    console.warn "Save is not implemented in notebook mode"

let private onLoad () =
    console.warn "Load is not implemented in notebook mode"

let private onExportStl () =
    MeshExport.downloadCurrentStl ()

// --------------------------------------------------------------------------
// Render loop.
// --------------------------------------------------------------------------

let private renderInto (root: Browser.Types.HTMLElement) =
    let doc = DocumentPipeline.documentView store.State
    let shell = Shell.render dispatch doc viewerHost onSave onLoad onExportStl
    root.innerHTML <- ""
    root.appendChild shell |> ignore

let private actionListSignature (doc: DocumentView) =
    // Block-list signature: id, name, body shape, plus selection.
    // Bodies are summarised so changing a scalar arg or rewiring a ref
    // triggers the BlockList to re-render the inline input rows.
    let bodyTag (b: Server.Lang.Notebook.BlockBody) =
        match b with
        | Server.Lang.Notebook.NativeBody(name, args) ->
            let argDigest =
                args
                |> Map.toList
                |> List.map (fun (k, v) ->
                    match v with
                    | Server.Lang.Notebook.ArgScalar n -> sprintf "%s=%g" k n
                    | Server.Lang.Notebook.ArgRef None -> sprintf "%s=-" k
                    | Server.Lang.Notebook.ArgRef (Some r) -> sprintf "%s=#%d" k r)
                |> String.concat ","
            sprintf "%s(%s)" name argDigest
        | Server.Lang.Notebook.SketchBody _ -> "sketch"
    let rows = doc.Blocks |> List.map (fun b -> b.Id, b.Name, bodyTag b.Body, b.Visibility)
    sprintf "%A|%A|%A|%A"
        doc.SelectedBlockId
        doc.ExpandedBlockIds
        doc.EditingBlockRef
        rows

let private uiSignature (state: EditorState) =
    sprintf
        "%A|%A|%A|%A|%A|%A|%A|%A"
        state.SketchEditMode
        state.SketchTool
        state.SelectedTargets
        state.HoveredTarget
        state.EditingDimension
        state.ConstraintPlacementMode
        state.ConstraintPlacementDraft
        state.ConstraintPlacementCursor

let mutable private lastCompiled = store.State.Compiled
let mutable private lastSlotValues = store.State.SlotValues
let mutable private lastUiSignature = uiSignature store.State
let initialDocView = DocumentPipeline.documentView store.State
let mutable private lastActionListSignature = actionListSignature initialDocView
let mutable private lastSelectedBlockId = store.State.Doc.SelectedBlockId

let private onStateChange (root: Browser.Types.HTMLElement) () =
    let state = store.State
    let compiledChanged = not (obj.ReferenceEquals(lastCompiled, state.Compiled))
    let slotValuesChanged = not (obj.ReferenceEquals(lastSlotValues, state.SlotValues))
    let selectionChanged = state.Doc.SelectedBlockId <> lastSelectedBlockId
    let nextUiSignature = uiSignature state
    let uiChanged = nextUiSignature <> lastUiSignature
    let doc = DocumentPipeline.documentView state
    let nextActionListSignature = actionListSignature doc
    let actionListChanged = nextActionListSignature <> lastActionListSignature

    if compiledChanged || uiChanged then
        renderInto root
    else
        if actionListChanged || selectionChanged then
            BlockList.syncPanel root dispatch doc
        if selectionChanged then
            SketchAuthoringPanel.syncOverlay root dispatch doc

    lastCompiled <- state.Compiled
    lastSlotValues <- state.SlotValues
    lastUiSignature <- nextUiSignature
    lastActionListSignature <- nextActionListSignature
    lastSelectedBlockId <- state.Doc.SelectedBlockId

// --------------------------------------------------------------------------
// Bootstrap.
// --------------------------------------------------------------------------

let private mount () =
    let root = document.getElementById "app"
    if isNull root then
        failwith "Missing #app element"

    Benchmarks.installGlobals ()

    Store.subscribe store (onStateChange root)

    Shortcuts.register
        dispatch
        (fun () -> DocumentPipeline.documentView store.State)
        getPaletteOpen
        onSave
        onLoad

    renderInto root

    // Mount the viewer AFTER the shell so viewerHost is already in the DOM.
    // The viewer uses shadow DOM so our styles don't leak into its canvas.
    mountViewer viewerHost |> ignore

mount ()
