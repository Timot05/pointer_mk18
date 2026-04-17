module PointerMk18.Ui.ParamsPanel

open Fable.Core
open Fable.Core.JsInterop
open Server
open PointerMk18.Ui
open Browser.Dom
open Browser.Types

// ---------------------------------------------------------------------------
// Right panel: properties / parameter editor for the selected action.
// ---------------------------------------------------------------------------

// ── ParamValue helpers ─────────────────────────────────────────────────

let private vFloat (x: float) = VFloat x
let private vString (s: string) = VString s
let private vBool (b: bool) = VBool b

let private vColor (rgb: float[]) : ParamValue =
    rgb |> Array.map VFloat |> Array.toList |> VArray

// ── Control builders — each takes a typed field, not a string key ─────

let private controlDrag
        (dispatch: Message -> unit)
        (label: string)
        (value: float)
        (actionId: string)
        (field: ActionParamField)
        : HTMLElement =
    let row = Dom.el "div" "control-row"
    row.appendChild (Dom.elText "span" "control-name" label :> Node) |> ignore
    let valSpan = Dom.elText "span" "control-value" (sprintf "%.1f" value)
    Dom.setupDraggable
        valSpan
        value
        (fun _ -> ())
        (fun v -> dispatch (PatchActionParamValue(actionId, field, vFloat v)))
    row.appendChild (valSpan :> Node) |> ignore
    row

let private option (value: string) (label: string) (selected: bool) : HTMLOptionElement =
    let o = document.createElement "option" :?> HTMLOptionElement
    o.value <- value
    o.textContent <- label
    if selected then o.selected <- true
    o

let private controlRef
        (dispatch: Message -> unit)
        (label: string)
        (current: string option)
        (options: DocAction list)
        (actionId: string)
        (field: ActionParamField)
        : HTMLElement =
    let row = Dom.el "div" "control-row"
    row.appendChild (Dom.elText "span" "control-name" label :> Node) |> ignore
    let select = document.createElement "select" :?> HTMLSelectElement
    select.className <- "control-ref"
    select.appendChild (option "" "\u2013" (Option.isNone current) :> Node) |> ignore
    for opt in options do
        let txt = opt.Name |> Option.defaultValue (ActionList.kindLabel opt.Kind)
        select.appendChild (option opt.Id txt (Some opt.Id = current) :> Node) |> ignore
    select.addEventListener (
        "change",
        fun _ ->
            dispatch (PatchActionParamValue(actionId, field, vString select.value))
    )
    row.appendChild (select :> Node) |> ignore
    row

let private controlSelect
        (dispatch: Message -> unit)
        (label: string)
        (current: string)
        (choices: string list)
        (actionId: string)
        (field: ActionParamField)
        : HTMLElement =
    let row = Dom.el "div" "control-row"
    row.appendChild (Dom.elText "span" "control-name" label :> Node) |> ignore
    let select = document.createElement "select" :?> HTMLSelectElement
    select.className <- "control-select"
    for c in choices do
        select.appendChild (option c c (c = current) :> Node) |> ignore
    select.addEventListener (
        "change",
        fun _ ->
            dispatch (PatchActionParamValue(actionId, field, vString select.value))
    )
    row.appendChild (select :> Node) |> ignore
    row

let private controlCheck
        (dispatch: Message -> unit)
        (label: string)
        (checked_: bool)
        (actionId: string)
        (field: ActionParamField)
        : HTMLElement =
    let row = Dom.el "div" "control-row control-check"
    let input = document.createElement "input" :?> HTMLInputElement
    input.``type`` <- "checkbox"
    input.``checked`` <- checked_
    input.addEventListener (
        "change",
        fun _ ->
            dispatch (PatchActionParamValue(actionId, field, vBool input.``checked``))
    )
    row.appendChild (input :> Node) |> ignore
    row.appendChild (Dom.elText "label" "" label :> Node) |> ignore
    row

let private controlStatic (label: string) (value: string) : HTMLElement =
    let row = Dom.el "div" "control-row"
    row.appendChild (Dom.elText "span" "control-name" label :> Node) |> ignore
    row.appendChild (Dom.elText "span" "" value :> Node) |> ignore
    row

let private planeOfSketchPlane (p: SketchPlane) : string =
    match p with XY -> "XY" | XZ -> "XZ" | YZ -> "YZ"

let private controlFromSketchLoop
        (dispatch: Message -> unit)
        (doc: DocumentView)
        (childId: string option)
        (selection: FromSketchSelection)
        (actionId: string)
        : HTMLElement =
    let row = Dom.el "div" "control-row"
    row.appendChild (Dom.elText "span" "control-name" "loop" :> Node) |> ignore
    let select = document.createElement "select" :?> HTMLSelectElement
    select.className <- "control-select"

    let currentLoopId =
        match selection with
        | SelectionLoop (Some id) -> id
        | _ -> ""

    let loops =
        match childId with
        | Some cid ->
            doc.SketchLoops
            |> Map.tryFind cid
            |> Option.defaultValue []
        | None -> []

    select.appendChild (option "" "first (auto)" (currentLoopId = "") :> Node) |> ignore
    loops
    |> List.iteri (fun i loop ->
            select.appendChild (option loop.Id (sprintf "loop %d" (i + 1)) (loop.Id = currentLoopId) :> Node)
            |> ignore)

    select.disabled <- Option.isNone childId || loops.IsEmpty
    select.addEventListener (
        "change",
        fun _ ->
            let loopId = if select.value = "" then None else Some select.value
            // FromSketchSelection is its own VRecord-encoded payload.
            let record =
                let loopField =
                    match loopId with
                    | Some id when id <> "" -> VString id
                    | _ -> VNull
                VRecord (Map.ofList [ "case", VString "SelectionLoop"; "loopId", loopField ])
            dispatch (PatchActionParamValue(actionId, ActionParamField.FromSketchSelection, record))
    )
    row.appendChild (select :> Node) |> ignore
    row

// ── refOptions index is keyed by ActionParamField name ──────────────────

let private refOptsFor
        (doc: DocumentView)
        (field: ActionParamField)
        : DocAction list =
    let byId = doc.Actions |> List.map (fun a -> a.Id, a) |> Map.ofList
    let key =
        match field with
        | TranslateChild | RotateChild | MoveChild | ThickenChild | ShellChild
        | MeshChild | FromSketchChild -> "child"
        | MoveFrame -> "frame"
        | UnionA | SubtractA | IntersectA -> "a"
        | UnionB | SubtractB | IntersectB -> "b"
        | SketchOrigin -> "origin"
        | _ -> ""
    doc.RefOptions
    |> Map.tryFind key
    |> Option.defaultValue []
    |> List.choose (fun id -> Map.tryFind id byId)

// ── Kind → controls strip ──────────────────────────────────────────────

let private renderKindControls
        (dispatch: Message -> unit)
        (doc: DocumentView)
        (selected: DocAction)
        : HTMLElement =
    let strip = Dom.el "div" "controls-strip"
    let drag label v field = controlDrag dispatch label v selected.Id field
    let ref label current field =
        controlRef dispatch label current (refOptsFor doc field) selected.Id field
    let select label current choices field =
        controlSelect dispatch label current choices selected.Id field
    let check label b field = controlCheck dispatch label b selected.Id field

    let append (e: HTMLElement) = strip.appendChild (e :> Node) |> ignore

    match selected.Kind with
    | Origin ->
        append (controlStatic "frame" "world")
    | Sphere r ->
        append (drag "radius" r SphereRadius)
    | Cylinder(r, h) ->
        append (drag "radius" r CylinderRadius)
        append (drag "height" h CylinderHeight)
    | Box(w, h, d) ->
        append (drag "width" w BoxWidth)
        append (drag "height" h BoxHeight)
        append (drag "depth" d BoxDepth)
    | HalfPlane(axis, offset, flip) ->
        append (select "axis" axis [ "X"; "Y"; "Z" ] HalfPlaneAxis)
        append (drag "offset" offset HalfPlaneOffset)
        append (check "flip" flip HalfPlaneFlip)
    | Translate(child, x, y, z) ->
        append (ref "child" child TranslateChild)
        append (drag "x" x TranslateX)
        append (drag "y" y TranslateY)
        append (drag "z" z TranslateZ)
    | Rotate(child, ax, ay, az, angle) ->
        append (ref "child" child RotateChild)
        append (drag "ax" ax RotateAxisX)
        append (drag "ay" ay RotateAxisY)
        append (drag "az" az RotateAxisZ)
        append (drag "angle" angle RotateAngle)
    | Move(child, frame) ->
        append (ref "child" child MoveChild)
        append (ref "frame" frame MoveFrame)
    | Union(a, b, r) ->
        append (ref "tool" a UnionA)
        append (ref "target" b UnionB)
        append (drag "radius" r UnionRadius)
    | Subtract(a, b, r) ->
        append (ref "tool" a SubtractA)
        append (ref "target" b SubtractB)
        append (drag "radius" r SubtractRadius)
    | Intersect(a, b, r) ->
        append (ref "tool" a IntersectA)
        append (ref "target" b IntersectB)
        append (drag "radius" r IntersectRadius)
    | Sketch(origin, plane, _) ->
        append (ref "origin" origin SketchOrigin)
        append (select "plane" (planeOfSketchPlane plane) [ "XY"; "XZ"; "YZ" ] ActionParamField.SketchPlane)
    | FromSketch(child, flip, selection) ->
        append (ref "sketch" child FromSketchChild)
        append (check "flip" flip FromSketchFlip)
        append (controlFromSketchLoop dispatch doc child selection selected.Id)
    | Thicken(child, amount) ->
        append (ref "child" child ThickenChild)
        append (drag "amount" amount ThickenAmount)
    | Shell(child, t) ->
        append (ref "child" child ShellChild)
        append (drag "thickness" t ShellThickness)
    | Mesh(child, size, res) ->
        append (ref "child" child MeshChild)
        append (drag "size" size MeshSize)
        append (drag "res" (float res) MeshResolution)

    strip

// ── Display / field-slice section (field-typed actions only) ───────────

let private roadrunnerPalette : (string * float[]) list =
    [ "#85AEC8", [| float 0x85 / 255.0; float 0xAE / 255.0; float 0xC8 / 255.0 |]
      "#341D7C", [| float 0x34 / 255.0; float 0x1D / 255.0; float 0x7C / 255.0 |]
      "#F1BA23", [| float 0xF1 / 255.0; float 0xBA / 255.0; float 0x23 / 255.0 |]
      "#FFFFFF", [| 1.0; 1.0; 1.0 |]
      "#AC6614", [| float 0xAC / 255.0; float 0x66 / 255.0; float 0x14 / 255.0 |]
      "#E4D6AF", [| float 0xE4 / 255.0; float 0xD6 / 255.0; float 0xAF / 255.0 |]
      "#7D6400", [| float 0x7D / 255.0; float 0x64 / 255.0; float 0x00 / 255.0 |]
      "#FFFFAA", [| 1.0; 1.0; float 0xAA / 255.0 |]
      "#D10005", [| float 0xD1 / 255.0; float 0x00 / 255.0; float 0x05 / 255.0 |] ]

let private colorsMatch (a: float[]) (b: float[]) =
    a.Length = 3 && b.Length = 3
    && abs (a.[0] - b.[0]) < 0.01
    && abs (a.[1] - b.[1]) < 0.01
    && abs (a.[2] - b.[2]) < 0.01

let private renderDisplaySection
        (dispatch: Message -> unit)
        (selected: DocAction)
        : HTMLElement option =

    match selected.Display with
    | None -> None
    | Some d ->
        let nodeVisible = selected.Visible
        let section = Dom.el "div" "display-section"

        let title = Dom.el "div" "section-title"
        title.appendChild (Dom.elText "span" "" "field display" :> Node) |> ignore
        if not nodeVisible then
            let note = Dom.el "span" "field-disabled-note"
            note.appendChild (Dom.kbdHintTitled "v" "Press v to toggle" :> Node) |> ignore
            note.appendChild (Dom.elText "span" "" "to enable" :> Node) |> ignore
            title.appendChild (note :> Node) |> ignore
        section.appendChild (title :> Node) |> ignore

        let controls = Dom.el "div" "display-controls"
        if not nodeVisible then controls.classList.add "is-disabled"

        // Isosurface toggle
        let check = Dom.el "label" "display-check"
        let checkbox = document.createElement "input" :?> HTMLInputElement
        checkbox.``type`` <- "checkbox"
        checkbox.``checked`` <- d.Enabled
        checkbox.disabled <- not nodeVisible
        checkbox.addEventListener ("change", fun _ -> dispatch (ToggleDisplay selected.Id))
        check.appendChild (checkbox :> Node) |> ignore
        check.appendChild (Dom.kbdHintTitled "s" "Press s to toggle" :> Node) |> ignore
        check.appendChild (Dom.elText "span" "" "Show field iso-surface" :> Node) |> ignore
        controls.appendChild (check :> Node) |> ignore

        if d.Enabled then
            // Color swatches
            let colorRow = Dom.el "div" "control-row color-row"
            colorRow.appendChild (Dom.elText "span" "control-name" "color" :> Node) |> ignore
            let swatches = Dom.el "div" "color-swatches"
            for (hex, rgb) in roadrunnerPalette do
                let swatch = Dom.el "button" "color-swatch"
                swatch?style?background <- hex
                if colorsMatch d.Color rgb then swatch.classList.add "is-active"
                swatch.addEventListener (
                    "click",
                    fun _ ->
                        dispatch (PatchDisplayValue(selected.Id, DisplayColor, vColor rgb))
                )
                swatches.appendChild (swatch :> Node) |> ignore
            colorRow.appendChild (swatches :> Node) |> ignore
            controls.appendChild (colorRow :> Node) |> ignore

            // Iso offset
            let offsetRow = Dom.el "div" "control-row"
            offsetRow.appendChild (Dom.elText "span" "control-name" "offset" :> Node) |> ignore
            let offsetVal = Dom.elText "span" "control-value" (sprintf "%.1f" d.IsoValue)
            Dom.setupDraggable
                offsetVal
                d.IsoValue
                (fun _ -> ())
                (fun v -> dispatch (PatchDisplayValue(selected.Id, DisplayIsoValue, vFloat v)))
            offsetRow.appendChild (offsetVal :> Node) |> ignore
            controls.appendChild (offsetRow :> Node) |> ignore

        // Field slice
        match selected.FieldSlice with
        | None -> ()
        | Some fs ->
            let sliceCheck = Dom.el "label" "display-check"
            let sliceCheckbox = document.createElement "input" :?> HTMLInputElement
            sliceCheckbox.``type`` <- "checkbox"
            sliceCheckbox.``checked`` <- fs.Enabled
            sliceCheckbox.disabled <- not nodeVisible
            sliceCheckbox.addEventListener ("change", fun _ -> dispatch (ToggleFieldSlice selected.Id))
            sliceCheck.appendChild (sliceCheckbox :> Node) |> ignore
            sliceCheck.appendChild (Dom.kbdHintTitled "f" "Press f to toggle" :> Node) |> ignore
            sliceCheck.appendChild (Dom.elText "span" "" "Show field iso-lines" :> Node) |> ignore
            controls.appendChild (sliceCheck :> Node) |> ignore

            if fs.Enabled then
                // Plane
                let planeRow = Dom.el "div" "control-row"
                planeRow.appendChild (Dom.elText "span" "control-name" "plane" :> Node) |> ignore
                let planeSelect = document.createElement "select" :?> HTMLSelectElement
                planeSelect.className <- "control-select"
                for (value, label) in [ "Z", "xy"; "Y", "xz"; "X", "yz" ] do
                    planeSelect.appendChild (option value label (fs.Plane = value) :> Node) |> ignore
                planeSelect.addEventListener (
                    "change",
                    fun _ -> dispatch (PatchFieldSliceValue(selected.Id, SlicePlane, vString planeSelect.value))
                )
                planeRow.appendChild (planeSelect :> Node) |> ignore
                controls.appendChild (planeRow :> Node) |> ignore

                // Offset
                let sOffsetRow = Dom.el "div" "control-row"
                sOffsetRow.appendChild (Dom.elText "span" "control-name" "offset" :> Node) |> ignore
                let sOffsetVal = Dom.elText "span" "control-value" (sprintf "%.1f" fs.Offset)
                Dom.setupDraggable
                    sOffsetVal
                    fs.Offset
                    (fun _ -> ())
                    (fun v -> dispatch (PatchFieldSliceValue(selected.Id, SliceOffset, vFloat v)))
                sOffsetRow.appendChild (sOffsetVal :> Node) |> ignore
                controls.appendChild (sOffsetRow :> Node) |> ignore

        section.appendChild (controls :> Node) |> ignore
        Some section

// ── Sketch edit toggle (Sketch kind only) ──────────────────────────────

let private renderSketchEditToggle
        (dispatch: Message -> unit)
        (doc: DocumentView)
        (kind: ActionKind)
        : HTMLElement option =
    match kind with
    | Sketch _ ->
        let editMode = doc.SketchUi.EditMode
        let section = Dom.el "div" "sketch-edit-section"
        if editMode then section.classList.add "is-active"
        let toggle = Dom.el "button" "sketch-edit-toggle" :?> HTMLButtonElement
        toggle.``type`` <- "button"
        if editMode then toggle.classList.add "is-active"
        let label = if editMode then "Exit sketch edit" else "Edit sketch"
        toggle.appendChild (Dom.elText "span" "sketch-edit-label" label :> Node) |> ignore
        toggle.appendChild (Dom.kbdHint "E" :> Node) |> ignore
        toggle.addEventListener ("click", fun _ -> dispatch ToggleSketchEdit)
        section.appendChild (toggle :> Node) |> ignore
        Some section
    | _ -> None

// ── Top-level render ───────────────────────────────────────────────────

let render (dispatch: Message -> unit) (doc: DocumentView) : HTMLElement =
    let container = Dom.el "div" "selection-panel"

    match doc.SelectedId |> Option.bind (fun id -> doc.Actions |> List.tryFind (fun a -> a.Id = id)) with
    | None ->
        container.appendChild (Dom.elText "div" "selection-empty" "Select an action" :> Node) |> ignore
    | Some selected ->
        // Header
        let header = Dom.el "div" "selection-header"
        let headerIcon = Dom.el "span" "action-icon"
        headerIcon.classList.add "large"
        headerIcon.appendChild (Icons.forKind selected.Kind :> Node) |> ignore
        header.appendChild (headerIcon :> Node) |> ignore
        let headerInfo = Dom.el "div" "header-info"
        headerInfo.appendChild (Dom.elText "div" "header-kind" (ActionList.kindLabel selected.Kind) :> Node) |> ignore
        let name = selected.Name |> Option.defaultValue (ActionList.kindLabel selected.Kind)
        headerInfo.appendChild (Dom.elText "div" "header-name" name :> Node) |> ignore
        header.appendChild (headerInfo :> Node) |> ignore
        container.appendChild (header :> Node) |> ignore

        // Errors
        let actionErrors = doc.Errors |> List.filter (fun e -> e.ActionId = selected.Id)
        if not actionErrors.IsEmpty then
            let errSection = Dom.el "div" "error-section"
            for err in actionErrors do
                let row = Dom.el "div" "error-row"
                row.appendChild (Dom.elText "span" "error-key" err.Key :> Node) |> ignore
                row.appendChild (Dom.elText "span" "error-msg" err.Error :> Node) |> ignore
                errSection.appendChild (row :> Node) |> ignore
            container.appendChild (errSection :> Node) |> ignore

        // Controls
        let section = Dom.el "div" "param-section"
        section.appendChild (Dom.elText "div" "controls-hint" "drag values to adjust:" :> Node) |> ignore
        section.appendChild (renderKindControls dispatch doc selected :> Node) |> ignore
        container.appendChild (section :> Node) |> ignore

        // Sketch edit toggle
        match renderSketchEditToggle dispatch doc selected.Kind with
        | Some s -> container.appendChild (s :> Node) |> ignore
        | None -> ()

        // Field display
        match renderDisplaySection dispatch selected with
        | Some s -> container.appendChild (s :> Node) |> ignore
        | None -> ()

    container
