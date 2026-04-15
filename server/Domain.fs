namespace Server

open System.Text.Json
open System.Text.Json.Serialization

// ---------------------------------------------------------------------------
// Domain types — the document model that the frontend renders
// ---------------------------------------------------------------------------

type ActionId = string

type ActionKind =
    | Origin
    | Cylinder of radius: float * height: float
    | Sphere of radius: float
    | Box of width: float * height: float * depth: float
    | HalfPlane of axis: string * offset: float * flip: bool
    | Translate of child: ActionId option * x: float * y: float * z: float
    | Rotate of child: ActionId option * ax: float * ay: float * az: float * angle: float
    | Move of child: ActionId option * frame: ActionId option
    | Union of a: ActionId option * b: ActionId option * radius: float
    | Subtract of a: ActionId option * b: ActionId option * radius: float
    | Intersect of a: ActionId option * b: ActionId option * radius: float
    | Sketch of origin: ActionId option * sketch: ActionSketch
    | FromSketch of child: ActionId option * flip: bool * selection: FromSketchSelection
    | Thicken of child: ActionId option * amount: float
    | Shell of child: ActionId option * thickness: float
    | Mesh of child: ActionId option * size: float * resolution: int

type DisplaySettings =
    { Enabled: bool
      Color: float array  // [r, g, b] normalized 0-1
      Opacity: float
      IsoValue: float }

module DisplaySettings =
    let defaults =
        { Enabled = false
          Color = [| 0.522; 0.682; 0.784 |]  // #85AEC8
          Opacity = 0.9
          IsoValue = 0.0 }

type FieldSliceSettings =
    { Enabled: bool
      Plane: string  // "X" | "Y" | "Z"
      Offset: float
      Extent: float }

module FieldSliceSettings =
    let defaults =
        { Enabled = false
          Plane = "Z"
          Offset = 0.0
          Extent = 20.0 }

type DocAction =
    { Id: ActionId
      Name: string option
      Kind: ActionKind
      Visible: bool
      Display: DisplaySettings option
      FieldSlice: FieldSliceSettings option }

type Document ={ 
    Name: string
    Actions: DocAction list
    SelectedId: string option 
}

module Document =

    let select (id: string) (doc: Document) : Document =
        { doc with SelectedId = Some id }

    let addAction (action: DocAction) (doc: Document) : Document =
        { doc with Actions = doc.Actions @ [ action ]; SelectedId = Some action.Id }

    let updateAction (id: string) (updated: DocAction) (doc: Document) : Document =
        { doc with Actions = doc.Actions |> List.map (fun a -> if a.Id = id then updated else a) }

    let removeAction (id: string) (doc: Document) : Document =
        { doc with
            Actions = doc.Actions |> List.filter (fun a -> a.Id <> id)
            SelectedId = if doc.SelectedId = Some id then None else doc.SelectedId }

    let toggleVisible (id: string) (doc: Document) : Document =
        { doc with
            Actions =
                doc.Actions
                |> List.map (fun a -> if a.Id = id then { a with Visible = not a.Visible } else a) }

    let toggleDisplay (id: string) (doc: Document) : Document =
        { doc with
            Actions =
                doc.Actions
                |> List.map (fun a ->
                    if a.Id <> id then a
                    else
                        let d = a.Display |> Option.defaultValue DisplaySettings.defaults
                        { a with Display = Some { d with Enabled = not d.Enabled } }) }

    let patchDisplay (id: string) (key: string) (value: System.Text.Json.JsonElement) (doc: Document) : Document =
        { doc with
            Actions =
                doc.Actions
                |> List.map (fun a ->
                    if a.Id <> id then a
                    else
                        let d = a.Display |> Option.defaultValue DisplaySettings.defaults
                        let d' =
                            match key with
                            | "color" ->
                                let arr = value.EnumerateArray() |> Seq.map (fun e -> e.GetDouble()) |> Seq.toArray
                                { d with Color = arr }
                            | "opacity" -> { d with Opacity = value.GetDouble() }
                            | "isoValue" -> { d with IsoValue = value.GetDouble() }
                            | _ -> d
                        { a with Display = Some d' }) }

    let toggleFieldSlice (id: string) (doc: Document) : Document =
        { doc with
            Actions =
                doc.Actions
                |> List.map (fun a ->
                    if a.Id <> id then a
                    else
                        let fs = a.FieldSlice |> Option.defaultValue FieldSliceSettings.defaults
                        { a with FieldSlice = Some { fs with Enabled = not fs.Enabled } }) }

    let patchFieldSlice (id: string) (key: string) (value: System.Text.Json.JsonElement) (doc: Document) : Document =
        { doc with
            Actions =
                doc.Actions
                |> List.map (fun a ->
                    if a.Id <> id then a
                    else
                        let fs = a.FieldSlice |> Option.defaultValue FieldSliceSettings.defaults
                        let fs' =
                            match key with
                            | "plane" -> { fs with Plane = value.GetString() }
                            | "offset" -> { fs with Offset = value.GetDouble() }
                            | _ -> fs
                        { a with FieldSlice = Some fs' }) }

    let reorder (ids: string list) (doc: Document) : Document =
        let lookup = doc.Actions |> List.map (fun a -> a.Id, a) |> Map.ofList
        { doc with Actions = ids |> List.choose (fun id -> Map.tryFind id lookup) }

    let private optStr (value: System.Text.Json.JsonElement) =
        let s = value.GetString()
        if System.String.IsNullOrEmpty(s) then None else Some s

    let private patchSketchEntityParam (key: string) (value: System.Text.Json.JsonElement) (sketch: ActionSketch) =
        let parts = key.Split('.')
        if parts.Length <> 4 || parts.[0] <> "sketch" || parts.[1] <> "entity" then sketch
        else
            let entityId = parts.[2]
            let field = parts.[3]
            let entities =
                sketch.Entities
                |> List.map (fun entity ->
                    match entity with
                    | REPoint(id, x, y) when id = entityId ->
                        REPoint(
                            id,
                            (if field = "x" then value.GetDouble() else x),
                            (if field = "y" then value.GetDouble() else y))
                    | RECircle(id, center, radius) when id = entityId && field = "radius" ->
                        RECircle(id, center, value.GetDouble())
                    | REArc(id, startId, endId, ArcThreePoint through) when id = entityId ->
                        let through' =
                            ({ X = if field = "throughX" then value.GetDouble() else through.X
                               Y = if field = "throughY" then value.GetDouble() else through.Y } : FreePoint)
                        REArc(id, startId, endId, ArcThreePoint through')
                    | _ -> entity)
            { sketch with Entities = entities }

    let private patchSketchConstraintParam (key: string) (value: System.Text.Json.JsonElement) (sketch: ActionSketch) =
        let parts = key.Split('.')
        if parts.Length < 4 || parts.[0] <> "sketch" || parts.[1] <> "constraint" then sketch
        else
            match System.Int32.TryParse(parts.[2]) with
            | false, _ -> sketch
            | true, index ->
                let constraints =
                    sketch.Constraints
                    |> List.mapi (fun i item ->
                        if i <> index then item
                        else
                            match parts.[3], item with
                            | "labelPosition", _ when parts.Length = 5 ->
                                let field = parts.[4]
                                let patchLabel current =
                                    let pos = current |> Option.defaultValue { X = 0.0; Y = 0.0 }
                                    let pos' =
                                        { X = if field = "x" then value.GetDouble() else pos.X
                                          Y = if field = "y" then value.GetDouble() else pos.Y }
                                    Some pos'
                                match item with
                                | Distance(a, b, dist, lp) -> Distance(a, b, dist, patchLabel lp)
                                | FrameDistance(point, frame, part, dist, lp) -> FrameDistance(point, frame, part, dist, patchLabel lp)
                                | LineDistance(aStart, aEnd, bStart, bEnd, lineA, lineB, dist, lp) -> LineDistance(aStart, aEnd, bStart, bEnd, lineA, lineB, dist, patchLabel lp)
                                | FrameLineDistance(lineA, aStart, aEnd, frame, part, dist, lp) -> FrameLineDistance(lineA, aStart, aEnd, frame, part, dist, patchLabel lp)
                                | PointLineDistance(point, lineA, aStart, aEnd, dist, lp) -> PointLineDistance(point, lineA, aStart, aEnd, dist, patchLabel lp)
                                | PointCircleDistance(point, circle, center, dist, lp) -> PointCircleDistance(point, circle, center, dist, patchLabel lp)
                                | LineCircleDistance(lineA, aStart, aEnd, circle, center, dist, lp) -> LineCircleDistance(lineA, aStart, aEnd, circle, center, dist, patchLabel lp)
                                | CircleCircleDistance(circleA, centerA, circleB, centerB, dist, internalFlag, lp) -> CircleCircleDistance(circleA, centerA, circleB, centerB, dist, internalFlag, patchLabel lp)
                                | CircleDiameter(circle, center, diam, lp) -> CircleDiameter(circle, center, diam, patchLabel lp)
                                | Angle(aStart, aEnd, bStart, bEnd, lineA, lineB, degrees, aReverse, bReverse, ccw, lp) -> Angle(aStart, aEnd, bStart, bEnd, lineA, lineB, degrees, aReverse, bReverse, ccw, patchLabel lp)
                                | other -> other
                            | "distance", Distance(a, b, _, lp) -> Distance(a, b, value.GetDouble(), lp)
                            | "distance", FrameDistance(point, frame, part, _, lp) -> FrameDistance(point, frame, part, value.GetDouble(), lp)
                            | "distance", LineDistance(aStart, aEnd, bStart, bEnd, lineA, lineB, _, lp) -> LineDistance(aStart, aEnd, bStart, bEnd, lineA, lineB, value.GetDouble(), lp)
                            | "distance", FrameLineDistance(lineA, aStart, aEnd, frame, part, _, lp) -> FrameLineDistance(lineA, aStart, aEnd, frame, part, value.GetDouble(), lp)
                            | "distance", PointLineDistance(point, lineA, aStart, aEnd, _, lp) -> PointLineDistance(point, lineA, aStart, aEnd, value.GetDouble(), lp)
                            | "distance", PointCircleDistance(point, circle, center, _, lp) -> PointCircleDistance(point, circle, center, value.GetDouble(), lp)
                            | "distance", LineCircleDistance(lineA, aStart, aEnd, circle, center, _, lp) -> LineCircleDistance(lineA, aStart, aEnd, circle, center, value.GetDouble(), lp)
                            | "distance", CircleCircleDistance(circleA, centerA, circleB, centerB, _, internalFlag, lp) -> CircleCircleDistance(circleA, centerA, circleB, centerB, value.GetDouble(), internalFlag, lp)
                            | "diameter", CircleDiameter(circle, center, _, lp) -> CircleDiameter(circle, center, value.GetDouble(), lp)
                            | "angleDegrees", Angle(aStart, aEnd, bStart, bEnd, lineA, lineB, _, aReverse, bReverse, ccw, lp) ->
                                Angle(aStart, aEnd, bStart, bEnd, lineA, lineB, value.GetDouble(), aReverse, bReverse, ccw, lp)
                            | _ -> item)
                { sketch with Constraints = constraints }

    let patchParam (id: string) (key: string) (value: System.Text.Json.JsonElement) (doc: Document) : Document =
        { doc with
            Actions =
                doc.Actions
                |> List.map (fun a ->
                    if a.Id <> id then a
                    else
                        let kind =
                            match a.Kind with
                            | Cylinder(r, h) ->
                                Cylinder(
                                    (if key = "radius" then value.GetDouble() else r),
                                    (if key = "height" then value.GetDouble() else h))
                            | Sphere r ->
                                Sphere(if key = "radius" then value.GetDouble() else r)
                            | Box(w, h, d) ->
                                Box(
                                    (if key = "width" then value.GetDouble() else w),
                                    (if key = "height" then value.GetDouble() else h),
                                    (if key = "depth" then value.GetDouble() else d))
                            | Translate(c, x, y, z) ->
                                Translate(
                                    (if key = "child" then optStr value else c),
                                    (if key = "x" then value.GetDouble() else x),
                                    (if key = "y" then value.GetDouble() else y),
                                    (if key = "z" then value.GetDouble() else z))
                            | Rotate(c, ax, ay, az, ang) ->
                                Rotate(
                                    (if key = "child" then optStr value else c),
                                    (if key = "ax" then value.GetDouble() else ax),
                                    (if key = "ay" then value.GetDouble() else ay),
                                    (if key = "az" then value.GetDouble() else az),
                                    (if key = "angle" then value.GetDouble() else ang))
                            | HalfPlane(ax, off, fl) ->
                                HalfPlane(
                                    (if key = "axis" then value.GetString() else ax),
                                    (if key = "offset" then value.GetDouble() else off),
                                    (if key = "flip" then value.GetBoolean() else fl))
                            | Move(c, f) ->
                                Move(
                                    (if key = "child" then optStr value else c),
                                    (if key = "frame" then optStr value else f))
                            | Union(a, b, r) ->
                                Union(
                                    (if key = "a" then optStr value else a),
                                    (if key = "b" then optStr value else b),
                                    (if key = "radius" then value.GetDouble() else r))
                            | Subtract(a, b, r) ->
                                Subtract(
                                    (if key = "a" then optStr value else a),
                                    (if key = "b" then optStr value else b),
                                    (if key = "radius" then value.GetDouble() else r))
                            | Intersect(a, b, r) ->
                                Intersect(
                                    (if key = "a" then optStr value else a),
                                    (if key = "b" then optStr value else b),
                                    (if key = "radius" then value.GetDouble() else r))
                            | Sketch(origin, s) ->
                                Sketch(
                                    (if key = "origin" then optStr value else origin),
                                    (if key.StartsWith("sketch.entity.") then patchSketchEntityParam key value s
                                     elif key.StartsWith("sketch.constraint.") then patchSketchConstraintParam key value s
                                     else s))
                            | FromSketch(c, flip, sel) ->
                                let selection =
                                    if key = "selection" then
                                        let mutable caseEl = Unchecked.defaultof<JsonElement>
                                        if value.TryGetProperty("case", &caseEl) then
                                            match caseEl.GetString() with
                                            | "SelectionElements" ->
                                                let mutable lineIdsEl = Unchecked.defaultof<JsonElement>
                                                let lineIds =
                                                    if value.TryGetProperty("lineIds", &lineIdsEl) then
                                                        lineIdsEl.EnumerateArray() |> Seq.map (fun item -> item.GetString()) |> Seq.toList
                                                    else []
                                                SelectionElements lineIds
                                            | _ ->
                                                let mutable loopIdEl = Unchecked.defaultof<JsonElement>
                                                let loopId =
                                                    if value.TryGetProperty("loopId", &loopIdEl) then
                                                        if loopIdEl.ValueKind = JsonValueKind.Null then None else Some (loopIdEl.GetString())
                                                    else None
                                                SelectionLoop loopId
                                        else sel
                                    else sel
                                FromSketch(
                                    (if key = "child" then optStr value else c),
                                    (if key = "flip" then value.GetBoolean() else flip),
                                    selection)
                            | Thicken(c, amt) ->
                                Thicken(
                                    (if key = "child" then optStr value else c),
                                    (if key = "amount" then value.GetDouble() else amt))
                            | Shell(c, t) ->
                                Shell(
                                    (if key = "child" then optStr value else c),
                                    (if key = "thickness" then value.GetDouble() else t))
                            | Mesh(c, s, res) ->
                                Mesh(
                                    (if key = "child" then optStr value else c),
                                    (if key = "size" then value.GetDouble() else s),
                                    (if key = "resolution" then value.GetInt32() else res))
                            | other -> other
                        { a with Kind = kind }) }

    let defaultDocument () : Document =
        { Name = "untitled"
          SelectedId = Some "origin"
          Actions =
            [ { Id = "origin"
                Name = Some "origin"
                Kind = Origin
                Visible = true
                Display = None
                FieldSlice = None }
              { Id = "cyl1"
                Name = Some "cylinder"
                Kind = Cylinder(radius = 10.0, height = 40.0)
                Visible = true
                Display = None
                FieldSlice = None }
              { Id = "sph1"
                Name = Some "sphere"
                Kind = Sphere(radius = 8.0)
                Visible = true
                Display = None
                FieldSlice = None }
              { Id = "sub1"
                Name = Some "subtract"
                Kind = Subtract(a = Some "cyl1", b = Some "sph1", radius = 0.0)
                Visible = true
                Display = None
                FieldSlice = None }
              { Id = "sketch1"
                Name = Some "square"
                Kind = Sketch(
                    origin = Some "origin",
                    sketch =
                        { Entities =
                            [ REPoint("p_bl",  0.0,  0.0)
                              REPoint("p_br", 10.0,  0.0)
                              REPoint("p_tr", 10.0, 10.0)
                              REPoint("p_tl",  0.0, 10.0)
                              RELine("l_bottom", "p_bl", "p_br")
                              RELine("l_right",  "p_br", "p_tr")
                              RELine("l_top",    "p_tr", "p_tl")
                              RELine("l_left",   "p_tl", "p_bl") ]
                          Constraints =
                            [ Fixed("p_bl", 0.0, 0.0)
                              Horizontal("p_bl", "p_br")
                              Horizontal("p_tl", "p_tr")
                              Vertical("p_bl", "p_tl")
                              Vertical("p_br", "p_tr")
                              Distance("p_bl", "p_br", 10.0, None)
                              Distance("p_bl", "p_tl", 10.0, None) ] })
                Visible = true
                Display = None
                FieldSlice = None }
              { Id = "frame1"
                Name = Some "frame"
                Kind = Translate(child = Some "origin", x = 18.0, y = 6.0, z = 12.0)
                Visible = true
                Display = None
                FieldSlice = None }
              { Id = "from1"
                Name = Some "from-sketch"
                Kind = FromSketch(
                    child = Some "sketch1",
                    flip = false,
                    selection = SelectionLoop None)
                Visible = true
                Display = None
                FieldSlice = None } ] }
