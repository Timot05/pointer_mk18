module LabelBuilder

// MSDF label buffer construction. Given the font metrics + a list of
// (anchor, text) pairs, produce the interleaved vertex buffer consumed by
// the label pipeline.
//
// Each vertex = 10 floats: (anchor_2d.xy, offset_px.xy, uv.xy, color.rgba).
// 6 vertices per character (two triangles).

open Server
open MsdfAtlas

/// Default text colour for dimension labels.
let private LABEL_COLOUR : float32[] = [| 0.427f; 0.341f; 0.192f; 1.0f |]
let private LABEL_COLOUR_HOVER : float32[] = [| 0.725f; 0.510f; 0.170f; 1.0f |]

/// Target visual font size (height in pixels on screen).
let private DISPLAY_PX = 30.0

let private formatDistance (d: float) : string =
    if abs d < 0.001 then "0" else sprintf "%.2f" d

let private formatAngle (radians: float) : string =
    let deg = radians * 180.0 / System.Math.PI
    sprintf "%.1f°" deg

/// Text + world-local anchor for one label.
type Label =
    { Text: string
      Anchor: LabelPos
      Color: float32[] }

let private constraintText (c: SketchConstraint) : string option =
    match c with
    | Distance(_, _, d, _)
    | FrameDistance(_, _, _, d, _)
    | LineDistance(_, _, _, _, _, _, d, _)
    | FrameLineDistance(_, _, _, _, _, d, _)
    | PointLineDistance(_, _, _, _, d, _)
    | PointCircleDistance(_, _, _, d, _)
    | LineCircleDistance(_, _, _, _, _, d, _)
    | CircleCircleDistance(_, _, _, _, d, _, _)
    | CircleDiameter(_, _, d, _) -> Some (formatDistance d)
    | Angle(_, _, _, _, _, _, ang, _, _, _, _) -> Some (formatAngle ang)
    | _ -> None

let private constraintLabelPos (c: SketchConstraint) : LabelPos option =
    match c with
    | Distance(_, _, _, lp)
    | FrameDistance(_, _, _, _, lp)
    | LineDistance(_, _, _, _, _, _, _, lp)
    | FrameLineDistance(_, _, _, _, _, _, lp)
    | PointLineDistance(_, _, _, _, _, lp)
    | PointCircleDistance(_, _, _, _, lp)
    | LineCircleDistance(_, _, _, _, _, _, lp)
    | CircleCircleDistance(_, _, _, _, _, _, lp)
    | CircleDiameter(_, _, _, lp)
    | Angle(_, _, _, _, _, _, _, _, _, _, lp) -> lp
    | _ -> None

/// Pick the display text + anchor for any dimensional constraint. If the
/// constraint doesn't carry an explicit labelPosition, defer to
/// SketchOverlayRender.dimensionFallbackAnchor so labels stay aligned with the
/// dimension-line geometry.
let private constraintLabel
    (points: Map<string, float * float>)
    (radiusLookup: string -> float option)
    (c: SketchConstraint) : Label option =
    match constraintText c with
    | None -> None
    | Some text ->
        let anchor =
            match constraintLabelPos c with
            | Some lp -> Some lp
            | None -> SketchOverlayRender.dimensionFallbackAnchor points radiusLookup c
        anchor |> Option.map (fun a -> { Text = text; Anchor = a; Color = LABEL_COLOUR })

let private pushVertex
    (out: ResizeArray<float32>)
    (anchor: LabelPos)
    (ox: float) (oy: float)
    (u: float) (v: float)
    (color: float32[]) =
    out.Add(float32 anchor.X)
    out.Add(float32 anchor.Y)
    out.Add(float32 ox)
    out.Add(float32 oy)
    out.Add(float32 u)
    out.Add(float32 v)
    out.Add color.[0]
    out.Add color.[1]
    out.Add color.[2]
    out.Add color.[3]

/// Measure the total advance-width of a string in atlas pixels.
let private measureText (metrics: FontMetrics) (text: string) : float =
    text
    |> Seq.fold (fun acc ch ->
        match Map.tryFind (string ch) metrics.Chars with
        | Some c -> acc + c.XAdvance
        | None -> acc) 0.0

let private appendCharQuads
    (out: ResizeArray<float32>)
    (metrics: FontMetrics)
    (scale: float)
    (label: Label) =
    let totalWidth = measureText metrics label.Text
    // Centre the text horizontally about the anchor and vertically about
    // its x-height mid-line.
    let mutable cursorX = -totalWidth * 0.5
    let baseline = metrics.Base * 0.5
    let invAtlasW = 1.0 / metrics.ScaleW
    let invAtlasH = 1.0 / metrics.ScaleH

    for ch in label.Text do
        match Map.tryFind (string ch) metrics.Chars with
        | None -> ()
        | Some fc ->
            let px0 = (cursorX + fc.XOffset) * scale
            let py0 = -(baseline - fc.YOffset) * scale
            let px1 = (cursorX + fc.XOffset + fc.Width) * scale
            let py1 = -(baseline - fc.YOffset - fc.Height) * scale
            let u0 = fc.X * invAtlasW
            let v0 = fc.Y * invAtlasH
            let u1 = (fc.X + fc.Width) * invAtlasW
            let v1 = (fc.Y + fc.Height) * invAtlasH
            // Two triangles: (TL, TR, BL), (TR, BR, BL)
            pushVertex out label.Anchor px0 py0 u0 v0 label.Color
            pushVertex out label.Anchor px1 py0 u1 v0 label.Color
            pushVertex out label.Anchor px0 py1 u0 v1 label.Color
            pushVertex out label.Anchor px1 py0 u1 v0 label.Color
            pushVertex out label.Anchor px1 py1 u1 v1 label.Color
            pushVertex out label.Anchor px0 py1 u0 v1 label.Color
            cursorX <- cursorX + fc.XAdvance

let private isDimensionActive
    (sketchId: ActionId) (index: int)
    (hovered: SelectionTarget option) (selected: SelectionTarget list) : bool =
    let matches (t: SelectionTarget) =
        match t with
        | TargetDimension(sid, idx) -> sid = sketchId && idx = index
        | _ -> false
    (match hovered with Some h -> matches h | None -> false)
    || List.exists matches selected

/// Build the vertex buffer for all labels produced by the given sketch's
/// dimensional constraints. Active labels (hovered / selected) render in
/// a brighter accent colour.
let buildSketchLabelBuffer
    (metrics: FontMetrics)
    (points: Map<string, float * float>)
    (radiusLookup: string -> float option)
    (sketchId: ActionId)
    (constraints: SketchConstraint list)
    (hovered: SelectionTarget option)
    (selected: SelectionTarget list) : float32[] =
    let scale =
        if metrics.LineHeight > 0.0 then DISPLAY_PX / metrics.LineHeight
        else 1.0

    let out = ResizeArray<float32>()
    constraints
    |> List.iteri (fun i c ->
        match constraintLabel points radiusLookup c with
        | Some label ->
            let colour =
                if isDimensionActive sketchId i hovered selected then LABEL_COLOUR_HOVER
                else LABEL_COLOUR
            appendCharQuads out metrics scale { label with Color = colour }
        | None -> ())
    out.ToArray()
