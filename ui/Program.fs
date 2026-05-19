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

[<Emit("new FileReader()")>]
let private newFileReader () : obj = jsNative

// --------------------------------------------------------------------------
// Entry point. Owns:
//   - the singleton F# editor store (via AppStore)
//   - the viewer host element (long-lived so WebGPU context survives renders)
//   - the subscription that re-renders the shell on every dispatch
// --------------------------------------------------------------------------

let private store = AppStore.store

let private dispatch msg = Store.dispatch store msg

// Block palette state is owned by `BlockList` directly.
let private getPaletteOpen () = BlockList.isPaletteOpen ()

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
// Save / Load. Uses Thoth.Json's auto-derived encoders/decoders, which
// reconstruct F# discriminated unions / records / maps / lists faithfully
// across the JSON round-trip — unlike `Fable.Core.JS.JSON.stringify` which
// leaves DUs as plain arrays that lose their `.tag` / `.fields` runtime
// shape on parse.
// --------------------------------------------------------------------------

let private onSave () =
    let doc = store.State.Doc
    let json = Thoth.Json.Encode.Auto.toString(2, doc)
    let baseName =
        let trimmed = doc.Name.Trim().ToLower()
        if trimmed = "" || trimmed = "untitled" then "pointer-model" else trimmed
    let blob = newBlob [| json :> obj |] {| ``type`` = "application/json" |}
    let url = urlCreateObjectUrl blob
    let link = document.createElement "a" :?> HTMLAnchorElement
    link.href <- url
    link?download <- sprintf "%s.json" baseName
    link.click ()
    urlRevokeObjectUrl url

let private onLoad () =
    let input = document.createElement "input" :?> HTMLInputElement
    input.``type`` <- "file"
    input.accept <- "application/json,.json"
    input.addEventListener (
        "change",
        fun _ ->
            let files = input.files
            if files.length > 0 then
                let file = files.[0]
                let reader : obj = newFileReader ()
                reader?onload <- (fun _ ->
                    let text : string = unbox reader?result
                    match Thoth.Json.Decode.Auto.fromString<Server.Document>(text) with
                    | Ok doc ->
                        dispatch (ReplaceDocument doc)
                    | Error msg ->
                        console.error ("Failed to load: " + msg))
                reader?readAsText(file) |> ignore
    )
    input.click ()

// Load an example whose JSON content is already in-memory (bundled
// at build time via `Examples.fs`). Mirrors `onLoad`'s parse-and-
// dispatch flow without the file-picker step.
let private onLoadExample (jsonContent: string) =
    match Thoth.Json.Decode.Auto.fromString<Server.Document>(jsonContent) with
    | Ok doc -> dispatch (ReplaceDocument doc)
    | Error msg -> console.error ("Failed to load example: " + msg)

// `BlockList` is compiled before `MeshExport` so it can't reference it
// statically; bind the per-block mesh-export hook here, after both are in
// scope. Runs once at startup.
BlockList.downloadMeshFor <- MeshExport.downloadBlockStl

// --------------------------------------------------------------------------
// Render loop.
// --------------------------------------------------------------------------

let private renderInto (root: Browser.Types.HTMLElement) =
    let doc = DocumentPipeline.documentView store.State
    let shell = Shell.render dispatch doc viewerHost onSave onLoad onLoadExample
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
                    let summary =
                        match v.Node with
                        | Server.Lang.Ast.ENumber n -> sprintf "%g" n
                        | Server.Lang.Ast.EVar id -> sprintf "#%s" id.Name
                        | Server.Lang.Ast.EPath segments ->
                            segments
                            |> List.map (fun s -> s.Name)
                            |> String.concat "."
                            |> sprintf "#%s"
                        | _ -> "expr"
                    sprintf "%s=%s" k summary)
                |> String.concat ","
            sprintf "%s(%s)" name argDigest
        | Server.Lang.Notebook.SketchBody _ -> "sketch"
        | Server.Lang.Notebook.ImageBody data ->
            sprintf "image(%s|%A|%g,%g,%g|%g,%g|%g|%g)"
                data.Url data.Plane data.Origin.X data.Origin.Y data.Origin.Z
                data.Width data.Height data.Opacity data.Rotation
    let rows = doc.Blocks |> List.map (fun b -> b.Id, b.Name, bodyTag b.Body, b.Visibility, b.ColorIndex)
    // User-spec names + analysis errors flow into the +Add palette and
    // BlockList row rendering, so include them in the signature so a
    // script-source edit triggers a BlockList sync without needing a
    // full Shell re-render.
    let userSpecNames =
        doc.UserScript.Specs |> Map.toList |> List.map fst
    let userScriptErrSummary =
        match doc.UserScript.ParseError with
        | Some e -> e.Message
        | None ->
            doc.UserScript.AnalysisErrors
            |> List.map (fun (n, m) -> sprintf "%s:%s" n m)
            |> String.concat ";"
    sprintf "%A|%A|%A|%A|%A|%s"
        doc.SelectedBlockId
        doc.ExpandedBlockIds
        doc.EditingBlockRef
        rows
        userSpecNames
        userScriptErrSummary

let private uiSignature (state: EditorState) =
    sprintf
        "%A|%A|%A|%A|%A|%A|%A|%A|%A"
        state.SketchEditMode
        state.SketchTool
        state.SelectedTargets
        state.HoveredTarget
        state.EditingDimension
        state.ConstraintPlacementMode
        state.ConstraintPlacementDraft
        state.ConstraintPlacementCursor
        state.ScriptEditorOpen

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
