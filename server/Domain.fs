namespace Server

#if !FABLE_COMPILER
open System.Text.Json
#endif
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
    | Sketch of origin: ActionId option * plane: SketchPlane * sketch: ActionSketch
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

type ParamValue =
    | VNull
    | VBool of bool
    | VInt of int
    | VFloat of float
    | VString of string
    | VArray of ParamValue list
    | VRecord of Map<string, ParamValue>

module ParamValue =

#if !FABLE_COMPILER
    let rec ofJsonElement (value: JsonElement) =
        match value.ValueKind with
        | JsonValueKind.Null
        | JsonValueKind.Undefined -> VNull
        | JsonValueKind.True -> VBool true
        | JsonValueKind.False -> VBool false
        | JsonValueKind.Number ->
            match value.TryGetInt32() with
            | true, i -> VInt i
            | _ -> VFloat(value.GetDouble())
        | JsonValueKind.String -> VString(value.GetString())
        | JsonValueKind.Array ->
            value.EnumerateArray()
            |> Seq.map ofJsonElement
            |> Seq.toList
            |> VArray
        | JsonValueKind.Object ->
            value.EnumerateObject()
            |> Seq.map (fun prop -> prop.Name, ofJsonElement prop.Value)
            |> Map.ofSeq
            |> VRecord
#endif

    let asFloat =
        function
        | VFloat x -> Some x
        | VInt x -> Some(float x)
        | _ -> None

    let asInt =
        function
        | VInt x -> Some x
        | VFloat x when abs (x - round x) < 1e-9 -> Some(int (round x))
        | _ -> None

    let asBool =
        function
        | VBool x -> Some x
        | _ -> None

    let asString =
        function
        | VString x -> Some x
        | _ -> None

    let asStringOption value =
        match value with
        | VNull -> None
        | VString s when System.String.IsNullOrEmpty(s) -> None
        | VString s -> Some s
        | _ -> None

    let asFloatArray =
        function
        | VArray values ->
            List.foldBack (fun item acc ->
                match item, acc with
                | Some x, Some xs -> Some(x :: xs)
                | _ -> None) (values |> List.map asFloat) (Some [])
            |> Option.map List.toArray
        | _ -> None

    let tryField key =
        function
        | VRecord fields -> Map.tryFind key fields
        | _ -> None

type Document ={ 
    Name: string
    Actions: DocAction list
    SelectedId: string option 
}

module Document =

    let select (id: string) (doc: Document) : Document =
        { doc with SelectedId = Some id }

    let private normalizeNewAction (doc: Document) (action: DocAction) : DocAction =
        let hasOrigin =
            doc.Actions |> List.exists (fun existing -> existing.Id = "origin")
        let kind =
            match action.Kind with
            | Sketch(None, plane, sketch) when hasOrigin ->
                Sketch(Some "origin", plane, sketch)
            | other ->
                other
        { action with Kind = kind }

    let addAction (action: DocAction) (doc: Document) : Document =
        let action = normalizeNewAction doc action
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

    let patchDisplayValue (id: string) (key: string) (value: ParamValue) (doc: Document) : Document =
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
                                match ParamValue.asFloatArray value with
                                | Some arr -> { d with Color = arr }
                                | None -> d
                            | "opacity" ->
                                match ParamValue.asFloat value with
                                | Some opacity -> { d with Opacity = opacity }
                                | None -> d
                            | "isoValue" ->
                                match ParamValue.asFloat value with
                                | Some iso -> { d with IsoValue = iso }
                                | None -> d
                            | _ -> d
                        { a with Display = Some d' }) }

#if !FABLE_COMPILER
    let patchDisplay (id: string) (key: string) (value: JsonElement) (doc: Document) : Document =
        patchDisplayValue id key (ParamValue.ofJsonElement value) doc
#endif

    let toggleFieldSlice (id: string) (doc: Document) : Document =
        { doc with
            Actions =
                doc.Actions
                |> List.map (fun a ->
                    if a.Id <> id then a
                    else
                        let fs = a.FieldSlice |> Option.defaultValue FieldSliceSettings.defaults
                        { a with FieldSlice = Some { fs with Enabled = not fs.Enabled } }) }

    let patchFieldSliceValue (id: string) (key: string) (value: ParamValue) (doc: Document) : Document =
        { doc with
            Actions =
                doc.Actions
                |> List.map (fun a ->
                    if a.Id <> id then a
                    else
                        let fs = a.FieldSlice |> Option.defaultValue FieldSliceSettings.defaults
                        let fs' =
                            match key with
                            | "plane" ->
                                match ParamValue.asString value with
                                | Some plane -> { fs with Plane = plane }
                                | None -> fs
                            | "offset" ->
                                match ParamValue.asFloat value with
                                | Some offset -> { fs with Offset = offset }
                                | None -> fs
                            | _ -> fs
                        { a with FieldSlice = Some fs' }) }

#if !FABLE_COMPILER
    let patchFieldSlice (id: string) (key: string) (value: JsonElement) (doc: Document) : Document =
        patchFieldSliceValue id key (ParamValue.ofJsonElement value) doc
#endif

    let reorder (ids: string list) (doc: Document) : Document =
        let lookup = doc.Actions |> List.map (fun a -> a.Id, a) |> Map.ofList
        { doc with Actions = ids |> List.choose (fun id -> Map.tryFind id lookup) }

    let private patchSketchEntityParam (key: string) (value: ParamValue) (sketch: ActionSketch) =
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
                        let value = ParamValue.asFloat value
                        REPoint(
                            id,
                            (if field = "x" then value |> Option.defaultValue x else x),
                            (if field = "y" then value |> Option.defaultValue y else y))
                    | RECircle(id, center, radius) when id = entityId && field = "radius" ->
                        RECircle(id, center, value |> ParamValue.asFloat |> Option.defaultValue radius)
                    | REArc(id, startId, endId, ArcThreePoint through) when id = entityId ->
                        let number = ParamValue.asFloat value
                        let through' =
                            ({ X = if field = "throughX" then number |> Option.defaultValue through.X else through.X
                               Y = if field = "throughY" then number |> Option.defaultValue through.Y else through.Y } : FreePoint)
                        REArc(id, startId, endId, ArcThreePoint through')
                    | _ -> entity)
            { sketch with Entities = entities }

    let private patchSketchConstraintParam (key: string) (value: ParamValue) (sketch: ActionSketch) =
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
                                    let number = ParamValue.asFloat value
                                    let pos' =
                                        { X = if field = "x" then number |> Option.defaultValue pos.X else pos.X
                                          Y = if field = "y" then number |> Option.defaultValue pos.Y else pos.Y }
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
                                | Angle(aStart, aEnd, bStart, bEnd, lineA, lineB, angle, aReverse, bReverse, ccw, lp) -> Angle(aStart, aEnd, bStart, bEnd, lineA, lineB, angle, aReverse, bReverse, ccw, patchLabel lp)
                                | other -> other
                            | "distance", Distance(a, b, current, lp) -> Distance(a, b, value |> ParamValue.asFloat |> Option.defaultValue current, lp)
                            | "distance", FrameDistance(point, frame, part, current, lp) -> FrameDistance(point, frame, part, value |> ParamValue.asFloat |> Option.defaultValue current, lp)
                            | "distance", LineDistance(aStart, aEnd, bStart, bEnd, lineA, lineB, current, lp) -> LineDistance(aStart, aEnd, bStart, bEnd, lineA, lineB, value |> ParamValue.asFloat |> Option.defaultValue current, lp)
                            | "distance", FrameLineDistance(lineA, aStart, aEnd, frame, part, current, lp) -> FrameLineDistance(lineA, aStart, aEnd, frame, part, value |> ParamValue.asFloat |> Option.defaultValue current, lp)
                            | "distance", PointLineDistance(point, lineA, aStart, aEnd, current, lp) -> PointLineDistance(point, lineA, aStart, aEnd, value |> ParamValue.asFloat |> Option.defaultValue current, lp)
                            | "distance", PointCircleDistance(point, circle, center, current, lp) -> PointCircleDistance(point, circle, center, value |> ParamValue.asFloat |> Option.defaultValue current, lp)
                            | "distance", LineCircleDistance(lineA, aStart, aEnd, circle, center, current, lp) -> LineCircleDistance(lineA, aStart, aEnd, circle, center, value |> ParamValue.asFloat |> Option.defaultValue current, lp)
                            | "distance", CircleCircleDistance(circleA, centerA, circleB, centerB, current, internalFlag, lp) -> CircleCircleDistance(circleA, centerA, circleB, centerB, value |> ParamValue.asFloat |> Option.defaultValue current, internalFlag, lp)
                            | "diameter", CircleDiameter(circle, center, current, lp) -> CircleDiameter(circle, center, value |> ParamValue.asFloat |> Option.defaultValue current, lp)
                            | "angle", Angle(aStart, aEnd, bStart, bEnd, lineA, lineB, _, aReverse, bReverse, ccw, lp) ->
                                Angle(aStart, aEnd, bStart, bEnd, lineA, lineB, value |> ParamValue.asFloat |> Option.defaultValue 0.0, aReverse, bReverse, ccw, lp)
                            | _ -> item)
                { sketch with Constraints = constraints }

    let patchParamValue (id: string) (key: string) (value: ParamValue) (doc: Document) : Document =
        { doc with
            Actions =
                doc.Actions
                |> List.map (fun a ->
                    if a.Id <> id then a
                    else
                        let kind =
                            match a.Kind with
                            | Cylinder(r, h) ->
                                let number = ParamValue.asFloat value
                                Cylinder(
                                    (if key = "radius" then number |> Option.defaultValue r else r),
                                    (if key = "height" then number |> Option.defaultValue h else h))
                            | Sphere r ->
                                Sphere(if key = "radius" then value |> ParamValue.asFloat |> Option.defaultValue r else r)
                            | Box(w, h, d) ->
                                let number = ParamValue.asFloat value
                                Box(
                                    (if key = "width" then number |> Option.defaultValue w else w),
                                    (if key = "height" then number |> Option.defaultValue h else h),
                                    (if key = "depth" then number |> Option.defaultValue d else d))
                            | Translate(c, x, y, z) ->
                                let number = ParamValue.asFloat value
                                Translate(
                                    (if key = "child" then ParamValue.asStringOption value else c),
                                    (if key = "x" then number |> Option.defaultValue x else x),
                                    (if key = "y" then number |> Option.defaultValue y else y),
                                    (if key = "z" then number |> Option.defaultValue z else z))
                            | Rotate(c, ax, ay, az, ang) ->
                                let number = ParamValue.asFloat value
                                Rotate(
                                    (if key = "child" then ParamValue.asStringOption value else c),
                                    (if key = "ax" then number |> Option.defaultValue ax else ax),
                                    (if key = "ay" then number |> Option.defaultValue ay else ay),
                                    (if key = "az" then number |> Option.defaultValue az else az),
                                    (if key = "angle" then number |> Option.defaultValue ang else ang))
                            | HalfPlane(ax, off, fl) ->
                                HalfPlane(
                                    (if key = "axis" then value |> ParamValue.asString |> Option.defaultValue ax else ax),
                                    (if key = "offset" then value |> ParamValue.asFloat |> Option.defaultValue off else off),
                                    (if key = "flip" then value |> ParamValue.asBool |> Option.defaultValue fl else fl))
                            | Move(c, f) ->
                                Move(
                                    (if key = "child" then ParamValue.asStringOption value else c),
                                    (if key = "frame" then ParamValue.asStringOption value else f))
                            | Union(a, b, r) ->
                                let number = ParamValue.asFloat value
                                Union(
                                    (if key = "a" then ParamValue.asStringOption value else a),
                                    (if key = "b" then ParamValue.asStringOption value else b),
                                    (if key = "radius" then number |> Option.defaultValue r else r))
                            | Subtract(a, b, r) ->
                                let number = ParamValue.asFloat value
                                Subtract(
                                    (if key = "a" then ParamValue.asStringOption value else a),
                                    (if key = "b" then ParamValue.asStringOption value else b),
                                    (if key = "radius" then number |> Option.defaultValue r else r))
                            | Intersect(a, b, r) ->
                                let number = ParamValue.asFloat value
                                Intersect(
                                    (if key = "a" then ParamValue.asStringOption value else a),
                                    (if key = "b" then ParamValue.asStringOption value else b),
                                    (if key = "radius" then number |> Option.defaultValue r else r))
                            | Sketch(origin, plane, s) ->
                                Sketch(
                                    (if key = "origin" then ParamValue.asStringOption value else origin),
                                    (if key = "plane" then
                                        match ParamValue.asString value with
                                        | Some "XZ" -> XZ
                                        | Some "YZ" -> YZ
                                        | _ -> XY
                                     else plane),
                                    (if key.StartsWith("sketch.entity.") then patchSketchEntityParam key value s
                                     elif key.StartsWith("sketch.constraint.") then patchSketchConstraintParam key value s
                                     else s))
                            | FromSketch(c, flip, sel) ->
                                let selection =
                                    if key = "selection" then
                                        match ParamValue.tryField "case" value |> Option.bind ParamValue.asString with
                                        | Some "SelectionElements" ->
                                            let lineIds =
                                                ParamValue.tryField "lineIds" value
                                                |> Option.bind (function
                                                    | VArray items ->
                                                        List.foldBack (fun item acc ->
                                                            match item, acc with
                                                            | Some x, Some xs -> Some(x :: xs)
                                                            | _ -> None) (items |> List.map ParamValue.asString) (Some [])
                                                    | _ -> None)
                                                |> Option.defaultValue []
                                            SelectionElements lineIds
                                        | _ ->
                                            let loopId =
                                                ParamValue.tryField "loopId" value
                                                |> Option.bind ParamValue.asStringOption
                                            SelectionLoop loopId
                                    else sel
                                FromSketch(
                                    (if key = "child" then ParamValue.asStringOption value else c),
                                    (if key = "flip" then value |> ParamValue.asBool |> Option.defaultValue flip else flip),
                                    selection)
                            | Thicken(c, amt) ->
                                Thicken(
                                    (if key = "child" then ParamValue.asStringOption value else c),
                                    (if key = "amount" then value |> ParamValue.asFloat |> Option.defaultValue amt else amt))
                            | Shell(c, t) ->
                                Shell(
                                    (if key = "child" then ParamValue.asStringOption value else c),
                                    (if key = "thickness" then value |> ParamValue.asFloat |> Option.defaultValue t else t))
                            | Mesh(c, s, res) ->
                                Mesh(
                                    (if key = "child" then ParamValue.asStringOption value else c),
                                    (if key = "size" then value |> ParamValue.asFloat |> Option.defaultValue s else s),
                                    (if key = "resolution" then value |> ParamValue.asInt |> Option.defaultValue res else res))
                            | other -> other
                        { a with Kind = kind }) }

#if !FABLE_COMPILER
    let patchParam (id: string) (key: string) (value: JsonElement) (doc: Document) : Document =
        patchParamValue id key (ParamValue.ofJsonElement value) doc
#endif

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
                    plane = XY,
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

    let emptyDocument () : Document =
        { Name = "untitled"
          SelectedId = Some "origin"
          Actions =
            [ { Id = "origin"
                Name = Some "origin"
                Kind = Origin
                Visible = true
                Display = None
                FieldSlice = None } ] }
