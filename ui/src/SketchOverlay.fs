module PointerMk18.Ui.SketchOverlay

open Fable.Core.JsInterop
open Server
open PointerMk18.Ui
open Browser.Dom
open Browser.Types

// ---------------------------------------------------------------------------
// Sketch authoring overlay — toolbar + constraint panels, shown only when
// the selected action is a Sketch and sketch edit mode is on.
// Ported from user-interface/src/render.ts:397–585.
// ---------------------------------------------------------------------------

// ── Toolbar ────────────────────────────────────────────────────────────

let private tools : (SketchToolKind * string * string option) list =
    [ NoSketchTool,         "select", None
      LineTool,             "line",   Some "L"
      RectangleTool,        "rect",   Some "G"
      RoundedRectangleTool, "rrect",  Some "\u21e7G"
      CircleTool,           "circle", Some "C"
      ArcTool,              "arc",    Some "U" ]

let private renderToolbar (dispatch: Message -> unit) (currentTool: string) : HTMLElement =
    let toolbar = Dom.el "div" "sketch-toolbar"
    for (tool, label, hint) in tools do
        let button = Dom.el "button" "sketch-tool-btn" :?> HTMLButtonElement
        button.``type`` <- "button"
        if currentTool = Editor.sketchToolName tool then
            button.classList.add "is-active"
        button.appendChild (Dom.elText "span" "" label :> Node) |> ignore
        match hint with
        | Some h -> button.appendChild (Dom.elText "kbd" "tool-hint" h :> Node) |> ignore
        | None -> ()
        button.addEventListener ("click", fun _ -> dispatch (SetSketchTool tool))
        toolbar.appendChild (button :> Node) |> ignore

    if currentTool <> "none" then
        toolbar.appendChild (
            Dom.elText "span" "sketch-toolbar-hint"
                "click in the viewer to place geometry" :> Node) |> ignore
    toolbar

// ── Constraint button tables ───────────────────────────────────────────

type private GeomButton =
    { Kind: GeometricConstraintKind
      Label: string
      Symbol: string
      Shortcut: string }

type private DimButton =
    { Kind: ConstraintPlacementKind
      Label: string
      Symbol: string
      Shortcut: string }

let private geometricButtons : GeomButton list =
    [ { Kind = CoincidentConstraint;    Label = "Coincident";    Symbol = "\u2261";       Shortcut = "I"     }
      { Kind = HorizontalConstraint;    Label = "Horizontal";    Symbol = "\u2194";       Shortcut = "H"     }
      { Kind = VerticalConstraint;      Label = "Vertical";      Symbol = "\u2195";       Shortcut = "V"     }
      { Kind = MidpointConstraint;      Label = "Midpoint";      Symbol = "\u00B7|\u00B7"; Shortcut = "\u21e7M" }
      { Kind = ParallelConstraint;      Label = "Parallel";      Symbol = "\u2225";       Shortcut = "B"     }
      { Kind = PerpendicularConstraint; Label = "Perpendicular"; Symbol = "\u22A5";       Shortcut = "\u21e7L" }
      { Kind = EqualConstraint;         Label = "Equal";         Symbol = "=";             Shortcut = "E"     }
      { Kind = TangentConstraint;       Label = "Tangent";       Symbol = "\u2312";       Shortcut = "T"     }
      { Kind = ConcentricConstraint;    Label = "Concentric";    Symbol = "\u25CE";       Shortcut = "\u21e7O" }
      { Kind = FixedConstraint;         Label = "Fixed";         Symbol = "\u2299";       Shortcut = "\u21e7J" } ]

let private dimensionButtons : DimButton list =
    [ { Kind = DistancePlacement; Label = "Distance"; Symbol = "\u21A6"; Shortcut = "D" }
      { Kind = AnglePlacement;    Label = "Angle";    Symbol = "\u2220"; Shortcut = "A" } ]

// ── Constraint row helpers ─────────────────────────────────────────────

/// Same symbol table the TS used. Works off SketchConstraint directly.
let private constraintSymbol (c: SketchConstraint) : string =
    match c with
    | Fixed _ -> "\u2299"
    | Coincident _ | FrameCoincident _ -> "\u2261"
    | Horizontal _ -> "\u2194"
    | Vertical _ -> "\u2195"
    | Parallel _ | FrameParallel _ -> "\u2225"
    | Perpendicular _ | FramePerpendicular _ -> "\u22A5"
    | Midpoint _ -> "\u00B7|\u00B7"
    | Tangent _ | CurveTangent _ -> "\u2312"
    | Concentric _ -> "\u25CE"
    | Equal _ | EqualRadius _ -> "="
    | Angle _ -> "\u2220"
    | CircleDiameter _ -> "\u2300"
    | Distance _ | FrameDistance _
    | LineDistance _ | FrameLineDistance _
    | PointLineDistance _ | PointCircleDistance _
    | LineCircleDistance _ | CircleCircleDistance _ -> "\u21A6"

let private constraintLabel (c: SketchConstraint) : string =
    match c with
    | Fixed _ -> "Fixed"
    | Coincident _ -> "Coincident"
    | FrameCoincident _ -> "FrameCoincident"
    | Horizontal _ -> "Horizontal"
    | Vertical _ -> "Vertical"
    | Distance _ -> "Distance"
    | FrameDistance _ -> "FrameDistance"
    | Equal _ | EqualRadius _ -> "Equal"
    | Midpoint _ -> "Midpoint"
    | Parallel _ -> "Parallel"
    | FrameParallel _ -> "FrameParallel"
    | Perpendicular _ -> "Perpendicular"
    | FramePerpendicular _ -> "FramePerpendicular"
    | Tangent _ | CurveTangent _ -> "Tangent"
    | CircleDiameter _ -> "CircleDiameter"
    | LineDistance _ -> "LineDistance"
    | FrameLineDistance _ -> "FrameLineDistance"
    | PointLineDistance _ -> "PointLineDistance"
    | PointCircleDistance _ -> "PointCircleDistance"
    | LineCircleDistance _ -> "LineCircleDistance"
    | CircleCircleDistance _ -> "CircleCircleDistance"
    | Angle _ -> "Angle"
    | Concentric _ -> "Concentric"

let private constraintSummary (c: SketchConstraint) : string =
    match c with
    | Fixed(point, _, _) -> point
    | Coincident(a, b) | Horizontal(a, b) | Vertical(a, b) -> sprintf "%s \u00B7 %s" a b
    | Distance(a, b, _, _) -> sprintf "%s \u00B7 %s" a b
    | Midpoint(point, lineA, _, _) | PointLineDistance(point, lineA, _, _, _, _) -> sprintf "%s \u00B7 %s" point lineA
    | Parallel(_, _, _, _, lineA, lineB) | Perpendicular(_, _, _, _, lineA, lineB)
    | Equal(_, _, _, _, lineA, lineB)
    | LineDistance(_, _, _, _, lineA, lineB, _, _)
    | Angle(_, _, _, _, lineA, lineB, _, _, _, _, _) -> sprintf "%s \u00B7 %s" lineA lineB
    | Tangent(_, _, _, circle, lineA, _) -> sprintf "%s \u00B7 %s" lineA circle
    | Concentric(a, b, _, _) | EqualRadius(a, b) -> sprintf "%s \u00B7 %s" a b
    | CircleDiameter(circle, _, _, _) -> circle
    | PointCircleDistance(point, circle, _, _, _) -> sprintf "%s \u00B7 %s" point circle
    | LineCircleDistance(lineA, _, _, circle, _, _, _) -> sprintf "%s \u00B7 %s" lineA circle
    | CircleCircleDistance(circleA, _, circleB, _, _, _, _) -> sprintf "%s \u00B7 %s" circleA circleB
    | _ -> ""

let private isDimensionConstraint (c: SketchConstraint) : bool =
    match c with
    | Distance _ | FrameDistance _ | LineDistance _ | FrameLineDistance _
    | PointLineDistance _ | PointCircleDistance _ | LineCircleDistance _
    | CircleCircleDistance _ | Angle _ | CircleDiameter _ -> true
    | _ -> false

let private constraintValueText (c: SketchConstraint) : string option =
    match c with
    | Distance(_, _, distance, _)
    | FrameDistance(_, _, _, distance, _)
    | LineDistance(_, _, _, _, _, _, distance, _)
    | FrameLineDistance(_, _, _, _, _, distance, _)
    | PointLineDistance(_, _, _, _, distance, _)
    | PointCircleDistance(_, _, _, distance, _)
    | LineCircleDistance(_, _, _, _, _, distance, _)
    | CircleCircleDistance(_, _, _, _, distance, _, _) -> Some(sprintf "%.2f" distance)
    | CircleDiameter(_, _, diameter, _) -> Some(sprintf "%.2f" diameter)
    | Angle(_, _, _, _, _, _, angle, _, _, _, _) -> Some(sprintf "%.2f" angle)
    | _ -> None

// ── Sections ───────────────────────────────────────────────────────────

let private renderExistingConstraints
        (dispatch: Message -> unit)
        (sketch: ActionSketch)
        (isDimensionSection: bool)
        (section: HTMLElement)
        : unit =
    let items =
        sketch.Constraints
        |> List.mapi (fun i c -> c, i)
        |> List.filter (fun (c, _) -> isDimensionConstraint c = isDimensionSection)
    if items.IsEmpty then
        let msg =
            if isDimensionSection then "Use the viewer to place a dimension label."
            else "Select entities in the viewer to enable constraints."
        section.appendChild (Dom.elText "div" "constraint-empty" msg :> Node) |> ignore
    else
        let list = Dom.el "div" "constraint-list"
        for (c, index) in items do
            let row = Dom.el "div" "constraint-row"
            row.appendChild (Dom.elText "span" "sym" (constraintSymbol c) :> Node) |> ignore
            row.appendChild (Dom.elText "span" "constraint-kind" (constraintLabel c) :> Node) |> ignore
            row.appendChild (Dom.elText "span" "constraint-summary" (constraintSummary c) :> Node) |> ignore
            match constraintValueText c with
            | Some value ->
                row.appendChild (Dom.elText "span" "constraint-value" value :> Node) |> ignore
            | None -> ()
            let del = Dom.elText "button" "constraint-delete" "\u00D7" :?> HTMLButtonElement
            del.``type`` <- "button"
            del.addEventListener ("click", fun _ -> dispatch (DeleteSketchConstraint index))
            row.appendChild (del :> Node) |> ignore
            list.appendChild (row :> Node) |> ignore
        section.appendChild (list :> Node) |> ignore

let private renderGeometricSection
        (dispatch: Message -> unit)
        (doc: DocumentView)
        (sketch: ActionSketch)
        : HTMLElement =
    let section = Dom.el "div" "constraint-section"
    let header = Dom.el "div" "constraint-section-header"
    header.appendChild (Dom.elText "span" "constraint-section-title" "Constraints" :> Node) |> ignore
    section.appendChild (header :> Node) |> ignore

    let row = Dom.el "div" "constraint-add-row"
    for b in geometricButtons do
        let button = Dom.el "button" "constraint-add-btn" :?> HTMLButtonElement
        button.``type`` <- "button"
        let key = Editor.geometricConstraintName b.Kind
        let available =
            doc.SketchUi.ConstraintAvailability
            |> Map.tryFind key
            |> Option.defaultValue false
        button.disabled <- not available
        button.appendChild (Dom.elText "span" "sym" b.Symbol :> Node) |> ignore
        button.appendChild (Dom.elText "span" "btn-label" b.Label :> Node) |> ignore
        button.appendChild (Dom.elText "kbd" "shortcut" b.Shortcut :> Node) |> ignore
        button.addEventListener ("click", fun _ -> dispatch (AddConstraintFromSelection b.Kind))
        row.appendChild (button :> Node) |> ignore
    section.appendChild (row :> Node) |> ignore

    renderExistingConstraints dispatch sketch false section
    section

let private renderDimensionSection
        (dispatch: Message -> unit)
        (doc: DocumentView)
        (sketch: ActionSketch)
        : HTMLElement =
    let section = Dom.el "div" "constraint-section"
    let header = Dom.el "div" "constraint-section-header"
    header.appendChild (Dom.elText "span" "constraint-section-title" "Dimensions" :> Node) |> ignore
    section.appendChild (header :> Node) |> ignore

    let row = Dom.el "div" "constraint-add-row"
    for b in dimensionButtons do
        let button = Dom.el "button" "constraint-add-btn" :?> HTMLButtonElement
        button.``type`` <- "button"
        let key = Editor.constraintPlacementName b.Kind
        let available =
            doc.SketchUi.DimensionPlacementAvailability
            |> Map.tryFind key
            |> Option.defaultValue false
        button.disabled <- not available
        if doc.SketchUi.ConstraintPlacementMode = Some key then
            button.classList.add "is-active"
        button.appendChild (Dom.elText "span" "sym" b.Symbol :> Node) |> ignore
        button.appendChild (Dom.elText "span" "btn-label" b.Label :> Node) |> ignore
        button.appendChild (Dom.elText "kbd" "shortcut" b.Shortcut :> Node) |> ignore
        button.addEventListener ("click", fun _ -> dispatch (ToggleConstraintPlacement b.Kind))
        row.appendChild (button :> Node) |> ignore
    section.appendChild (row :> Node) |> ignore
    renderExistingConstraints dispatch sketch true section
    section

// ── Top-level overlay ──────────────────────────────────────────────────

let render (dispatch: Message -> unit) (doc: DocumentView) : HTMLElement option =
    if not doc.SketchUi.EditMode then None
    else
        match doc.SelectedId |> Option.bind (fun id -> doc.Actions |> List.tryFind (fun a -> a.Id = id)) with
        | Some { Kind = Sketch(_, _, sketch) } ->
            let overlay = Dom.el "div" "sketch-authoring-overlay"
            overlay.appendChild (renderToolbar dispatch doc.SketchUi.Tool :> Node) |> ignore

            let panel = Dom.el "div" "constraints-panel"
            panel.appendChild (renderGeometricSection dispatch doc sketch :> Node) |> ignore
            panel.appendChild (renderDimensionSection dispatch doc sketch :> Node) |> ignore
            overlay.appendChild (panel :> Node) |> ignore
            Some overlay
        | _ -> None
