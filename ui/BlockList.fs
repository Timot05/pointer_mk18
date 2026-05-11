module PointerMk18.Ui.BlockList

open Fable.Core
open Fable.Core.JsInterop
open Server
open Server.Lang
open PointerMk18.Ui
open Browser.Dom
open Browser.Types

// ---------------------------------------------------------------------------
// BlockList — typed-block sidebar.
//
// Reuses the action-row / input-row / action-picker / wire-bubble /
// visibility-badge CSS that was already in `ui/styles.css` from the
// pre-notebook regime, so the panel inherits the established visual
// language without bespoke selectors.
//
// Per-block expansion lives on `doc.ExpandedBlockIds`; toggling a row's
// caret dispatches `ExpandBlock` / `CollapseBlock`. Refs render as
// `.wire-bubble`s; dropping one onto an upstream `.action-row` rewires
// it via `SetBlockArg`. Cmd+K opens the inline `.action-picker` mounted
// at the bottom of the list.
// ---------------------------------------------------------------------------

let private formatFloat (v: float) : string =
    if abs (v - round v) < 1e-9 then sprintf "%g" v
    else sprintf "%.2f" v

let [<Literal>] private WIRE_MIME = "application/x-block-wire"

/// Typed MIME marker — set by `renderRefBubble` on dragstart, scanned by
/// each row's dragover handler to gate drops by `Type.T` compatibility.
/// We can't read the payload data on dragover (browsers expose data only
/// on drop for security), so the *type* is encoded in the MIME *key* and
/// the dragover handler checks key presence via `dataTransfer.types`.
let private WIRE_TYPE_MIME_PREFIX = "application/x-block-wire-type-"

let private wireTypeMime (ty: Type.T) : string =
    WIRE_TYPE_MIME_PREFIX + Type.format ty

let private dataTransferHasWire (de: DragEvent) : bool =
    let types = de.dataTransfer.types
    let mutable found = false
    for i in 0 .. types.length - 1 do
        if types.[i] = WIRE_MIME then found <- true
    found

/// Does the in-flight drag carry a ref expecting the given type?
/// Used by row dragover handlers to decide whether to `preventDefault`.
let private dataTransferAcceptsType (de: DragEvent) (ty: Type.T) : bool =
    let target = wireTypeMime ty
    let types = de.dataTransfer.types
    let mutable found = false
    for i in 0 .. types.length - 1 do
        if types.[i] = target then found <- true
    found

// ── Body shape helpers ─────────────────────────────────────────────────────

let private specOf (block: Notebook.Block) : BlockSpec.BlockSpec option =
    match block.Body with
    | Notebook.NativeBody(name, _) -> BlockSpec.tryFind name
    | _ -> None

let private bodyKindLabel (body: Notebook.BlockBody) : string =
    match body with
    | Notebook.NativeBody(name, _) -> name
    | Notebook.SketchBody _ -> "sketch"

// ── Visibility badge (reuses `.visibility-badge`) ──────────────────────────

let private visibilityGlyph (v: Notebook.BlockVisibility) : string =
    match v with
    | Notebook.VHidden     -> ""
    | Notebook.VIsosurface -> "◎"   // ◎ bullseye — 3D surface
    | Notebook.VFieldLines -> "≡"   // ≡ — iso-contour overlay

let private renderVisibilityBadge
        (dispatch: Message -> unit)
        (block: Notebook.Block) : HTMLElement =
    let btn = Dom.elText "button" "visibility-badge" (visibilityGlyph block.Visibility)
    if block.Visibility = Notebook.VHidden then btn.classList.add "is-hidden"
    btn.title <- "Cycle visibility"
    btn.addEventListener (
        "click",
        fun e ->
            e.stopPropagation ()
            dispatch (CycleBlockVisibility block.Id))
    btn

// ── Scalar input editor (reuses `.input-row-edit`) ─────────────────────────

let private renderScalarEditor
        (dispatch: Message -> unit)
        (blockId: Notebook.BlockId)
        (paramName: string)
        (value: float) : HTMLElement =
    let input = document.createElement "input" :?> HTMLInputElement
    input.``type`` <- "number"
    input.className <- "input-row-edit"
    input.value <- formatFloat value
    let mutable cancelled = false
    let mutable finished = false
    let commit () =
        if not finished then
            finished <- true
            if not cancelled then
                match System.Double.TryParse(input.value) with
                | true, n ->
                    dispatch (SetBlockArg(blockId, paramName, Notebook.ArgScalar n))
                | _ -> ()
    input.addEventListener (
        "keydown",
        fun e ->
            let ke = e :?> KeyboardEvent
            match ke.key with
            | "Enter" -> e.preventDefault (); input.blur ()
            | "Escape" -> e.preventDefault (); cancelled <- true; input.blur ()
            | _ -> e.stopPropagation ())
    input.addEventListener ("blur", fun _ -> commit ())
    input :> HTMLElement

// ── Ref bubble (reuses `.wire-bubble`) ─────────────────────────────────────

let private upstreamBlockName (doc: DocumentView) (id: Notebook.BlockId) : string =
    doc.Blocks
    |> List.tryFind (fun b -> b.Id = id)
    |> Option.map (fun b -> b.Name)
    |> Option.defaultValue (sprintf "#%d" id)

let private renderRefBubble
        (dispatch: Message -> unit)
        (doc: DocumentView)
        (block: Notebook.Block)
        (paramName: string)
        (paramType: Type.T)
        (currentRef: Notebook.BlockId option) : HTMLElement =
    let bubble = Dom.el "div" "wire-bubble"
    bubble?draggable <- true

    let isPicking =
        doc.EditingBlockRef = Some(block.Id, paramName)
    if currentRef.IsSome then bubble.classList.add "is-assigned"
    if isPicking then bubble.classList.add "is-picking"

    // Body: name when wired, empty when not. The picking-state outline
    // comes from the parent bubble's CSS class.
    let label = Dom.el "span" "wire-bubble-label"
    label.textContent <-
        match currentRef with
        | Some id -> upstreamBlockName doc id
        | None -> ""
    bubble.appendChild (label :> Node) |> ignore

    // × close — only rendered when wired. Clicking unwires; the
    // bubble-level click below ignores the event so unwire is the only
    // result.
    if currentRef.IsSome then
        let close = Dom.elText "button" "wire-bubble-x" "×"
        close?``type`` <- "button"
        close.title <- "Disconnect"
        close.addEventListener (
            "click",
            fun ev ->
                ev.stopPropagation ()
                dispatch (SetBlockArg(block.Id, paramName, Notebook.ArgRef None)))
        bubble.appendChild (close :> Node) |> ignore

    bubble.addEventListener (
        "dragstart",
        fun ev ->
            let de = ev :?> DragEvent
            de.dataTransfer.effectAllowed <- "move"
            let payload = sprintf "%d|%s" block.Id paramName
            de.dataTransfer.setData (WIRE_MIME, payload) |> ignore
            // Typed marker — row dragover handlers gate by this so users
            // get a "no-drop" cursor when hovering type-incompatible rows.
            de.dataTransfer.setData (wireTypeMime paramType, "1") |> ignore
            ev.stopPropagation ())

    // Bubble click semantics:
    //  - Wired: clicks on the bubble body do nothing — unwire is via × only.
    //  - Unwired: enter (or toggle) pick mode.
    bubble.addEventListener (
        "click",
        fun ev ->
            ev.stopPropagation ()
            if currentRef.IsNone then
                dispatch (BeginPickBlockRef(block.Id, paramName)))
    bubble

// ── Inline input row (reuses `.input-row`) ─────────────────────────────────

let private renderInputRow
        (dispatch: Message -> unit)
        (doc: DocumentView)
        (block: Notebook.Block)
        (param: TypeExtract.ExtractedParam)
        (args: Map<string, Notebook.BlockArg>) : HTMLElement =
    let row = Dom.el "div" "input-row"
    row.appendChild (Dom.elText "span" "input-row-label" param.Name :> Node) |> ignore

    let editor =
        match param.Type with
        | Type.Scalar ->
            let v =
                match Map.tryFind param.Name args with
                | Some (Notebook.ArgScalar n) -> n
                | _ -> 0.0
            renderScalarEditor dispatch block.Id param.Name v
        | Type.Field
        | Type.Sketch
        | Type.Frame ->
            let r =
                match Map.tryFind param.Name args with
                | Some (Notebook.ArgRef ref) -> ref
                | _ -> None
            renderRefBubble dispatch doc block param.Name param.Type r :> HTMLElement
        | Type.Fun _ ->
            // First-class functions can't be wired or typed in the row
            // editor today — show a placeholder until we add a function-
            // valued ref bubble.
            Dom.elText "span" "input-row-value is-empty" "(fn)" :> HTMLElement

    row.appendChild (editor :> Node) |> ignore
    row

// ── Block row (reuses `.action-row` + `.action-main`) ──────────────────────

/// Expected `Type.T` for a (sourceBlockId, paramName) — the param the
/// user is currently picking a ref for. Lifts from the source block's
/// spec via `BlockSpec.typedInterface`. Returns `None` if the source
/// no longer exists or has no such param (defensive — the pick-mode
/// state could survive a block delete).
let private expectedRefType
        (doc: DocumentView)
        (sourceBlockId: Notebook.BlockId)
        (paramName: string) : Type.T option =
    doc.Blocks
    |> List.tryFind (fun b -> b.Id = sourceBlockId)
    |> Option.bind (fun b ->
        match b.Body with
        | Notebook.NativeBody(specName, _) -> BlockSpec.tryFind specName
        | _ -> None)
    |> Option.map BlockSpec.typedInterface
    |> Option.bind (fun ti ->
        ti.Params
        |> List.tryFind (fun p -> p.Name = paramName)
        |> Option.map (fun p -> p.Type))

/// Is `candidate` a valid commit target for the current pick? Type
/// compatible and not the source itself. DAG ordering isn't enforced
/// here — `SetBlockArg` auto-reorders so the source ends up upstream
/// of its targets, so any type-compatible non-self block works.
let private isValidPickTarget
        (doc: DocumentView)
        (sourceBlockId: Notebook.BlockId)
        (paramName: string)
        (candidate: Notebook.BlockId) : bool =
    if candidate = sourceBlockId then false
    else
        match expectedRefType doc sourceBlockId paramName,
              Map.tryFind candidate doc.BlockOutputs with
        | Some expected, Some actual -> expected = actual
        | _ -> false

let private renderRow
        (dispatch: Message -> unit)
        (doc: DocumentView)
        (block: Notebook.Block) : HTMLElement =

    let row = Dom.el "div" "action-row"
    if doc.SelectedBlockId = Some block.Id then row.classList.add "is-selected"

    // Pick-mode: highlight rows that are valid commit targets.
    let pickTarget =
        match doc.EditingBlockRef with
        | Some(srcId, paramName) when isValidPickTarget doc srcId paramName block.Id ->
            Some(srcId, paramName)
        | _ -> None
    if pickTarget.IsSome then row.classList.add "is-pick-target"

    // Per-block typecheck errors → `.has-error` styling + tooltip. The
    // map is populated by `recompileNotebook` on every block edit.
    match Map.tryFind block.Id doc.BlockErrors with
    | Some msgs when not msgs.IsEmpty ->
        row.classList.add "has-error"
        row.title <- String.concat "\n" msgs
    | _ -> ()

    let typedSpec = specOf block |> Option.map BlockSpec.typedInterface
    let hasInputs =
        typedSpec |> Option.exists (fun ts -> not ts.Params.IsEmpty)
    let isExpanded =
        hasInputs && Set.contains block.Id doc.ExpandedBlockIds
    if isExpanded then row.classList.add "is-expanded"

    let main = Dom.el "div" "action-main"

    // Caret — `.action-expand` rotates 90° via CSS when the parent has
    // `.is-expanded`.
    let caret = Dom.el "button" "action-expand"
    if not hasInputs then caret.classList.add "is-hidden"
    caret.textContent <- "▸"   // ▸
    caret.addEventListener (
        "click",
        fun e ->
            e.stopPropagation ()
            if hasInputs then
                if isExpanded then dispatch (CollapseBlock block.Id)
                else dispatch (ExpandBlock block.Id))
    main.appendChild (caret :> Node) |> ignore

    // Icon column — kept as a placeholder so column alignment matches
    // the action-list spacing rules even though we don't have per-block
    // icons yet.
    let iconSlot = Dom.el "span" "action-icon"
    main.appendChild (iconSlot :> Node) |> ignore

    let info = Dom.el "div" "action-info"
    let title = Dom.elText "span" "action-title" block.Name
    info.appendChild (title :> Node) |> ignore
    let subtitleText = bodyKindLabel block.Body
    if subtitleText <> "" then
        info.appendChild (Dom.elText "span" "action-subtitle" subtitleText :> Node) |> ignore
    main.appendChild (info :> Node) |> ignore

    // Title rename via double-click — replaces the span with `.action-title-edit`.
    let beginRename () =
        let input = document.createElement "input" :?> HTMLInputElement
        input.``type`` <- "text"
        input.className <- "action-title-edit"
        input.value <- block.Name
        title.parentNode.replaceChild(input, title) |> ignore
        input.focus ()
        input.select ()
        let mutable finished = false
        let commit () =
            if not finished then
                finished <- true
                let trimmed = input.value.Trim()
                if trimmed <> "" && trimmed <> block.Name then
                    dispatch (RenameBlock(block.Id, trimmed))
        let cancel () =
            if not finished then
                finished <- true
                if not (isNull input.parentNode) then
                    input.parentNode.replaceChild(title, input) |> ignore
        input.addEventListener ("blur", fun _ -> commit ())
        input.addEventListener (
            "keydown",
            fun e ->
                let ke = e :?> KeyboardEvent
                match ke.key with
                | "Enter" -> e.preventDefault (); commit (); input.blur ()
                | "Escape" -> e.preventDefault (); cancel ()
                | _ -> e.stopPropagation ())
        input.addEventListener ("click", fun e -> e.stopPropagation ())
        input.addEventListener ("mousedown", fun e -> e.stopPropagation ())
    title.addEventListener (
        "dblclick",
        fun ev ->
            ev.preventDefault ()
            ev.stopPropagation ()
            beginRename ())

    row.appendChild (main :> Node) |> ignore
    row.appendChild (renderVisibilityBadge dispatch block :> Node) |> ignore

    // Drop zone for ref-bubble drags — allows dropping onto the row even
    // outside `.action-main` so the whole bar is a target. Drop is
    // accepted only if the block's output type matches the dragged ref's
    // expected type (encoded in `application/x-block-wire-type-<Ty>` on
    // dragstart) AND the candidate is upstream of the source. When no
    // output type is known (e.g. unknown spec, or the typecheck-stage
    // `BlockOutputs` map missed it), the type gate is skipped — we still
    // require a wire MIME to be present.
    let blockOutputTy = Map.tryFind block.Id doc.BlockOutputs
    let acceptsDrag (de: DragEvent) =
        match blockOutputTy with
        | Some ty -> dataTransferAcceptsType de ty
        | None -> dataTransferHasWire de
    row.addEventListener (
        "dragover",
        fun ev ->
            let de = ev :?> DragEvent
            if acceptsDrag de then
                ev.preventDefault ()
                de.dataTransfer.dropEffect <- "move")
    row.addEventListener (
        "drop",
        fun ev ->
            let de = ev :?> DragEvent
            if acceptsDrag de then
                ev.preventDefault ()
                let raw : string = de.dataTransfer.getData WIRE_MIME
                let parts = raw.Split('|')
                if parts.Length = 2 then
                    match System.Int32.TryParse parts.[0] with
                    | true, sourceId when sourceId <> block.Id ->
                        // No upstream check — SetBlockArg auto-reorders
                        // the dragged source into upstream position.
                        dispatch (SetBlockArg(sourceId, parts.[1], Notebook.ArgRef (Some block.Id)))
                    | _ -> ())

    row.addEventListener (
        "click",
        fun _ ->
            // In pick mode: commit if this row is a valid target;
            // otherwise leave pick state alone (user can still select).
            match pickTarget with
            | Some(srcId, paramName) ->
                dispatch (SetBlockArg(srcId, paramName, Notebook.ArgRef (Some block.Id)))
            | None ->
                dispatch (SelectBlock block.Id))

    row

// ── Cmd+K palette state ────────────────────────────────────────────────────

let private fuzzyMatch (query: string) (text: string) : bool =
    let q = query.ToLowerInvariant()
    let t = text.ToLowerInvariant()
    let mutable qi = 0
    for ti in 0 .. t.Length - 1 do
        if qi < q.Length && t.[ti] = q.[qi] then qi <- qi + 1
    qi = q.Length

let private allPaletteEntries () : (string * Message) list =
    let nativeEntries =
        BlockSpec.allNames ()
        |> List.map (fun n -> n, AddNativeBlock n)
    nativeEntries @ [ "sketch", AddSketchBlock ]

let mutable private paletteOpen = false

let private renderPicker (dispatch: Message -> unit) : HTMLElement =
    let picker = Dom.el "div" "action-picker"

    let input = document.createElement "input" :?> HTMLInputElement
    input.``type`` <- "text"
    input.className <- "action-picker-input"
    input.placeholder <- "Add block…"
    input.autocomplete <- "off"
    input.spellcheck <- false
    picker.appendChild (input :> Node) |> ignore

    let resultsEl = Dom.el "div" "action-picker-results"
    picker.appendChild (resultsEl :> Node) |> ignore

    let allEntries = allPaletteEntries ()
    let mutable current = allEntries
    let mutable highlighted = 0

    let refreshHighlight () =
        let items = resultsEl.querySelectorAll ".action-picker-item"
        for i in 0 .. items.length - 1 do
            let el = items.[i] :?> HTMLElement
            if i = highlighted then el.classList.add "is-active"
            else el.classList.remove "is-active"

    let closePicker () =
        paletteOpen <- false
        picker.remove ()

    let commit () =
        if highlighted >= 0 && highlighted < current.Length then
            let _, msg = current.[highlighted]
            dispatch msg
        closePicker ()

    let rebuild () =
        resultsEl.innerHTML <- ""
        current
        |> List.iteri (fun i (name, _) ->
            let item = Dom.el "button" "action-picker-item"
            item.appendChild (Dom.elText "span" "action-picker-label" name :> Node) |> ignore
            item.addEventListener ("mouseenter", fun _ ->
                highlighted <- i
                refreshHighlight ())
            item.addEventListener ("click", fun e ->
                e.stopPropagation ()
                highlighted <- i
                commit ())
            resultsEl.appendChild (item :> Node) |> ignore)
        refreshHighlight ()

    let applyQuery (q: string) =
        let filtered =
            if System.String.IsNullOrEmpty q then allEntries
            else allEntries |> List.filter (fun (n, _) -> fuzzyMatch q n)
        current <- filtered
        if highlighted >= current.Length then
            highlighted <- max 0 (current.Length - 1)
        rebuild ()

    applyQuery ""

    input.addEventListener (
        "input",
        fun _ ->
            highlighted <- 0
            applyQuery input.value)

    input.addEventListener (
        "keydown",
        fun e ->
            let ke = e :?> KeyboardEvent
            match ke.key with
            | "ArrowDown" ->
                e.preventDefault (); e.stopPropagation ()
                if not current.IsEmpty then
                    highlighted <- (highlighted + 1) % current.Length
                    refreshHighlight ()
            | "ArrowUp" ->
                e.preventDefault (); e.stopPropagation ()
                if not current.IsEmpty then
                    highlighted <- (highlighted - 1 + current.Length) % current.Length
                    refreshHighlight ()
            | "Enter" ->
                e.preventDefault (); e.stopPropagation ()
                commit ()
            | "Escape" ->
                e.preventDefault (); e.stopPropagation ()
                closePicker ()
            | _ -> ())

    window.requestAnimationFrame (fun _ -> input.focus ()) |> ignore
    picker

/// Bound to ⌘K from `Shortcuts.fs`. Mounts an inline `.action-picker` at
/// the bottom of the panel-host-actions column; toggle dismisses.
let togglePalette (dispatch: Message -> unit) =
    let host = document.querySelector ".panel-host-actions"
    match host with
    | :? HTMLElement as host ->
        let existing = host.querySelector ".action-picker"
        if not (isNull existing) then
            paletteOpen <- false
            existing.remove ()
        else
            paletteOpen <- true
            host.appendChild (renderPicker dispatch :> Node) |> ignore
    | _ -> ()

// ── Top-level render ──────────────────────────────────────────────────────

let render (dispatch: Message -> unit) (doc: DocumentView) : HTMLElement =
    let panel = Dom.el "div" "panel"

    let header = Dom.elText "div" "panel-header" "Blocks"
    panel.appendChild (header :> Node) |> ignore

    let list = Dom.el "div" "block-list"
    for block in doc.Blocks do
        list.appendChild (renderRow dispatch doc block :> Node) |> ignore
        // Append param-input rows for expanded blocks. Siblings of the
        // action-row, same pattern ActionList uses — the CSS rotates
        // the caret and indents the input rows under their parent.
        if Set.contains block.Id doc.ExpandedBlockIds then
            match specOf block, block.Body with
            | Some spec, Notebook.NativeBody(_, args) ->
                let typed = BlockSpec.typedInterface spec
                for param in typed.Params do
                    list.appendChild (renderInputRow dispatch doc block param args :> Node) |> ignore
            | _ -> ()
    panel.appendChild (list :> Node) |> ignore

    // Panel-level summary. Surfaces compile failures (typecheck errors
    // not anchored to a block, eval errors, serialise failures) so a
    // blank canvas always has a corresponding message in the sidebar
    // rather than failing silently.
    match doc.LastNotebookError with
    | Some msg ->
        let banner = Dom.elText "div" "panel-error" msg
        panel.appendChild (banner :> Node) |> ignore
    | None -> ()

    panel

let syncPanel (root: HTMLElement) (dispatch: Message -> unit) (doc: DocumentView) : unit =
    match root.querySelector ".panel-host-actions" with
    | :? HTMLElement as host ->
        let prevScroll =
            match host.querySelector ".block-list" with
            | :? HTMLElement as list -> list.scrollTop
            | _ -> 0.0
        // Preserve the inline picker across re-renders.
        let pickerNode = host.querySelector ".action-picker"
        host.innerHTML <- ""
        host.appendChild (render dispatch doc :> Node) |> ignore
        match host.querySelector ".block-list" with
        | :? HTMLElement as list -> list.scrollTop <- prevScroll
        | _ -> ()
        if not (isNull pickerNode) then
            host.appendChild (pickerNode :> Node) |> ignore
    | _ -> ()
