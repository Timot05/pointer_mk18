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

let private getPaletteState () = DocumentPipeline.paletteView store.State
let private getDocActionCount () =
    (DocumentPipeline.documentView store.State).Actions.Length
let private getPaletteOpen () = (getPaletteState ()).IsOpen

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
    let model = Editor.serializedModel store.State
    let json = Fable.Core.JS.JSON.stringify(model, space = 2)
    let baseName =
        let trimmed = model.Name.Trim().ToLower()
        if trimmed = "" || trimmed = "untitled" then "pointer-model" else trimmed
    let blob = newBlob [| json :> obj |] {| ``type`` = "application/json" |}
    let url = urlCreateObjectUrl blob
    let link = document.createElement "a" :?> HTMLAnchorElement
    link.href <- url
    link?download <- sprintf "%s.json" baseName
    link.click ()
    urlRevokeObjectUrl url

let private onLoad () =
    let input = document.createElement "input" :?> Browser.Types.HTMLInputElement
    input.``type`` <- "file"
    input.accept <- "application/json,.json"
    input.addEventListener (
        "change",
        fun _ ->
            let files = input.files
            if files.length > 0 then
                let file = files.[0]
                let reader = FileReader.Create()
                reader.onload <- (fun _ ->
                    let text : string = unbox reader.result
                    try
                        let parsed = Fable.Core.JS.JSON.parse(text)
                        let model : SerializedModel = unbox parsed
                        dispatch (LoadModel model)
                    with ex ->
                        console.error ("Failed to load: " + ex.Message))
                reader.readAsText(file)
    )
    input.click ()

// --------------------------------------------------------------------------
// Render loop.
// --------------------------------------------------------------------------

let private renderInto (root: Browser.Types.HTMLElement) =
    let doc = DocumentPipeline.documentView store.State
    let shell = Shell.render dispatch doc viewerHost onSave onLoad
    root.innerHTML <- ""
    root.appendChild shell |> ignore

let private actionListSignature (doc: DocumentView) =
    let actionErrors =
        doc.Errors
        |> List.map (fun e -> e.ActionId)
        |> Set.ofList
    doc.Actions
    |> List.map (fun a ->
        a.Id,
        a.Name,
        a.Visible,
        Set.contains a.Id actionErrors)
    |> sprintf "%A"

let private kindPanelSignature (kind: ActionKind) =
    match kind with
    | Origin -> "Origin"
    | Cylinder _ -> "Cylinder"
    | Sphere _ -> "Sphere"
    | Box _ -> "Box"
    | HalfPlane(axis, _, flip) -> sprintf "HalfPlane|%s|%b" axis flip
    | Translate(child, _, _, _) -> sprintf "Translate|%A" child
    | Rotate(child, ax, ay, az, _) -> sprintf "Rotate|%A|%.6f|%.6f|%.6f" child ax ay az
    | Move(child, frame) -> sprintf "Move|%A|%A" child frame
    | Union(a, b, _) -> sprintf "Union|%A|%A" a b
    | Subtract(a, b, _) -> sprintf "Subtract|%A|%A" a b
    | Intersect(a, b, _) -> sprintf "Intersect|%A|%A" a b
    | Sketch(origin, plane, sketch) ->
        sprintf "Sketch|%A|%A|%d|%d" origin plane sketch.Entities.Length sketch.Constraints.Length
    | FromSketch(child, flip, selection) -> sprintf "FromSketch|%A|%b|%A" child flip selection
    | Thicken(child, _) -> sprintf "Thicken|%A" child
    | Shell(child, _) -> sprintf "Shell|%A" child
    | Mesh(child, _, _) -> sprintf "Mesh|%A" child

let private paramsPanelSignature (doc: DocumentView) =
    let selectedErrors =
        doc.SelectedId
        |> Option.map (fun id ->
            doc.Errors
            |> List.filter (fun e -> e.ActionId = id)
            |> List.map (fun e -> e.Key, e.Error))
    match doc.SelectedId |> Option.bind (fun id -> doc.Actions |> List.tryFind (fun a -> a.Id = id)) with
    | None ->
        sprintf "none|%A" selectedErrors
    | Some selected ->
        let displaySig =
            selected.Display
            |> Option.map (fun d -> d.Enabled, selected.Visible)
        let fieldSliceSig =
            selected.FieldSlice
            |> Option.map (fun fs -> fs.Enabled, fs.Plane)
        let refOptionsSig = doc.RefOptions |> Map.toList
        let sketchLoopsSig = doc.SketchLoops |> Map.toList
        sprintf
            "%s|%A|%A|%A|%b|%A|%A|%A|%A"
            selected.Id
            selected.Name
            (kindPanelSignature selected.Kind)
            displaySig
            doc.SketchUi.EditMode
            fieldSliceSig
            selectedErrors
            refOptionsSig
            sketchLoopsSig

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
        state.ViewerMode

let mutable private lastCompiled = store.State.Compiled
let mutable private lastSlotValues = store.State.SlotValues
let mutable private lastUiSignature = uiSignature store.State
let initialDocView = DocumentPipeline.documentView store.State
let mutable private lastActionListSignature = actionListSignature initialDocView
let mutable private lastParamsPanelSignature = paramsPanelSignature initialDocView
let mutable private lastSelectedId = store.State.Doc.SelectedId

let private onStateChange (root: Browser.Types.HTMLElement) () =
    let state = store.State
    let compiledChanged = not (obj.ReferenceEquals(lastCompiled, state.Compiled))
    let slotValuesChanged = not (obj.ReferenceEquals(lastSlotValues, state.SlotValues))
    let selectionChanged = state.Doc.SelectedId <> lastSelectedId
    let nextUiSignature = uiSignature state
    let uiChanged = nextUiSignature <> lastUiSignature
    let doc = DocumentPipeline.documentView state
    let nextActionListSignature = actionListSignature doc
    let nextParamsPanelSignature = paramsPanelSignature doc
    let actionListChanged = nextActionListSignature <> lastActionListSignature
    let paramsPanelChanged = nextParamsPanelSignature <> lastParamsPanelSignature

    if compiledChanged || uiChanged then
        renderInto root
    else
        if actionListChanged || selectionChanged then
            ActionList.syncPanel root dispatch doc
        if paramsPanelChanged || selectionChanged then
            ParamsPanel.syncPanel root dispatch doc
        if selectionChanged then
            SketchAuthoringPanel.syncOverlay root dispatch doc
        if slotValuesChanged then
            ParamsPanel.syncSlotValues root state
            ActionList.syncSubtitles root doc

    // The palette is mounted outside the shell, so it must track its own
    // state transitions independently of whether the shell rerendered.
    CommandPalette.sync dispatch getPaletteState getDocActionCount

    lastCompiled <- state.Compiled
    lastSlotValues <- state.SlotValues
    lastUiSignature <- nextUiSignature
    lastActionListSignature <- nextActionListSignature
    lastParamsPanelSignature <- nextParamsPanelSignature
    lastSelectedId <- state.Doc.SelectedId

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
