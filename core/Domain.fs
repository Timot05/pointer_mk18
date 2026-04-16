namespace Server

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
      Color: float array // [r, g, b] normalized 0-1
      Opacity: float
      IsoValue: float }

module DisplaySettings =
    let defaults =
        { Enabled = false
          Color = [| 0.522; 0.682; 0.784 |] // #85AEC8
          Opacity = 0.9
          IsoValue = 0.0 }

type FieldSliceSettings =
    { Enabled: bool
      Plane: string // "X" | "Y" | "Z"
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
            List.foldBack
                (fun item acc ->
                    match item, acc with
                    | Some x, Some xs -> Some(x :: xs)
                    | _ -> None)
                (values |> List.map asFloat)
                (Some [])
            |> Option.map List.toArray
        | _ -> None

    let tryField key =
        function
        | VRecord fields -> Map.tryFind key fields
        | _ -> None

type Document =
    { Name: string
      Actions: DocAction list
      SelectedId: string option }

module Document =

    let private mapActionById (id: string) (update: DocAction -> DocAction) (doc: Document) : Document =
        { doc with
            Actions = doc.Actions |> List.map (fun action -> if action.Id = id then update action else action) }

    let private floatOr current key expected value =
        if key = expected then value |> ParamValue.asFloat |> Option.defaultValue current else current

    let private intOr current key expected value =
        if key = expected then value |> ParamValue.asInt |> Option.defaultValue current else current

    let private boolOr current key expected value =
        if key = expected then value |> ParamValue.asBool |> Option.defaultValue current else current

    let private stringOr current key expected value =
        if key = expected then value |> ParamValue.asString |> Option.defaultValue current else current

    let private stringOptionOr current key expected value =
        if key = expected then ParamValue.asStringOption value else current

    let private applyWhenSome decode apply current value =
        value |> decode |> Option.map apply |> Option.defaultValue current

    let private patchLabelPosition field value current =
        let pos = current |> Option.defaultValue { X = 0.0; Y = 0.0 }
        let number = ParamValue.asFloat value
        Some
            { X = if field = "x" then number |> Option.defaultValue pos.X else pos.X
              Y = if field = "y" then number |> Option.defaultValue pos.Y else pos.Y }

    let private patchConstraintLabel field value =
        function
        | Distance(a, b, dist, lp) -> Distance(a, b, dist, patchLabelPosition field value lp)
        | FrameDistance(point, frame, part, dist, lp) -> FrameDistance(point, frame, part, dist, patchLabelPosition field value lp)
        | LineDistance(aStart, aEnd, bStart, bEnd, lineA, lineB, dist, lp) ->
            LineDistance(aStart, aEnd, bStart, bEnd, lineA, lineB, dist, patchLabelPosition field value lp)
        | FrameLineDistance(lineA, aStart, aEnd, frame, part, dist, lp) ->
            FrameLineDistance(lineA, aStart, aEnd, frame, part, dist, patchLabelPosition field value lp)
        | PointLineDistance(point, lineA, aStart, aEnd, dist, lp) ->
            PointLineDistance(point, lineA, aStart, aEnd, dist, patchLabelPosition field value lp)
        | PointCircleDistance(point, circle, center, dist, lp) ->
            PointCircleDistance(point, circle, center, dist, patchLabelPosition field value lp)
        | LineCircleDistance(lineA, aStart, aEnd, circle, center, dist, lp) ->
            LineCircleDistance(lineA, aStart, aEnd, circle, center, dist, patchLabelPosition field value lp)
        | CircleCircleDistance(circleA, centerA, circleB, centerB, dist, internalFlag, lp) ->
            CircleCircleDistance(circleA, centerA, circleB, centerB, dist, internalFlag, patchLabelPosition field value lp)
        | CircleDiameter(circle, center, diam, lp) ->
            CircleDiameter(circle, center, diam, patchLabelPosition field value lp)
        | Angle(aStart, aEnd, bStart, bEnd, lineA, lineB, angle, aReverse, bReverse, ccw, lp) ->
            Angle(aStart, aEnd, bStart, bEnd, lineA, lineB, angle, aReverse, bReverse, ccw, patchLabelPosition field value lp)
        | other -> other

    let private patchConstraintScalar value =
        function
        | Distance(a, b, current, lp) -> Distance(a, b, applyWhenSome ParamValue.asFloat (fun next -> next) current value, lp)
        | FrameDistance(point, frame, part, current, lp) ->
            FrameDistance(point, frame, part, applyWhenSome ParamValue.asFloat (fun next -> next) current value, lp)
        | LineDistance(aStart, aEnd, bStart, bEnd, lineA, lineB, current, lp) ->
            LineDistance(aStart, aEnd, bStart, bEnd, lineA, lineB, applyWhenSome ParamValue.asFloat (fun next -> next) current value, lp)
        | FrameLineDistance(lineA, aStart, aEnd, frame, part, current, lp) ->
            FrameLineDistance(lineA, aStart, aEnd, frame, part, applyWhenSome ParamValue.asFloat (fun next -> next) current value, lp)
        | PointLineDistance(point, lineA, aStart, aEnd, current, lp) ->
            PointLineDistance(point, lineA, aStart, aEnd, applyWhenSome ParamValue.asFloat (fun next -> next) current value, lp)
        | PointCircleDistance(point, circle, center, current, lp) ->
            PointCircleDistance(point, circle, center, applyWhenSome ParamValue.asFloat (fun next -> next) current value, lp)
        | LineCircleDistance(lineA, aStart, aEnd, circle, center, current, lp) ->
            LineCircleDistance(lineA, aStart, aEnd, circle, center, applyWhenSome ParamValue.asFloat (fun next -> next) current value, lp)
        | CircleCircleDistance(circleA, centerA, circleB, centerB, current, internalFlag, lp) ->
            CircleCircleDistance(circleA, centerA, circleB, centerB, applyWhenSome ParamValue.asFloat (fun next -> next) current value, internalFlag, lp)
        | CircleDiameter(circle, center, current, lp) ->
            CircleDiameter(circle, center, applyWhenSome ParamValue.asFloat (fun next -> next) current value, lp)
        | Angle(aStart, aEnd, bStart, bEnd, lineA, lineB, current, aReverse, bReverse, ccw, lp) ->
            Angle(aStart, aEnd, bStart, bEnd, lineA, lineB, applyWhenSome ParamValue.asFloat (fun next -> next) current value, aReverse, bReverse, ccw, lp)
        | other -> other

    let select (id: string) (doc: Document) : Document = { doc with SelectedId = Some id }

    let addAction (action: DocAction) (doc: Document) : Document =
        { doc with
            Actions = doc.Actions @ [ action ]
            SelectedId = Some action.Id }

    let updateAction (id: string) (updated: DocAction) (doc: Document) : Document =
        { doc with
            Actions = doc.Actions |> List.map (fun a -> if a.Id = id then updated else a) }

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
                    if a.Id <> id then
                        a
                    else
                        let d = a.Display |> Option.defaultValue DisplaySettings.defaults

                        { a with
                            Display = Some { d with Enabled = not d.Enabled } }) }

    let patchDisplayValue (id: string) (key: string) (value: ParamValue) (doc: Document) : Document =
        mapActionById id
            (fun action ->
                let display = action.Display |> Option.defaultValue DisplaySettings.defaults
                let nextDisplay =
                    match key with
                    | "color" -> applyWhenSome ParamValue.asFloatArray (fun next -> { display with Color = next }) display value
                    | "opacity" -> applyWhenSome ParamValue.asFloat (fun next -> { display with Opacity = next }) display value
                    | "isoValue" -> applyWhenSome ParamValue.asFloat (fun next -> { display with IsoValue = next }) display value
                    | _ -> display
                { action with Display = Some nextDisplay })
            doc

    let toggleFieldSlice (id: string) (doc: Document) : Document =
        mapActionById id
            (fun action ->
                let fieldSlice = action.FieldSlice |> Option.defaultValue FieldSliceSettings.defaults
                { action with FieldSlice = Some { fieldSlice with Enabled = not fieldSlice.Enabled } })
            doc

    let patchFieldSliceValue (id: string) (key: string) (value: ParamValue) (doc: Document) : Document =
        mapActionById id
            (fun action ->
                let fieldSlice = action.FieldSlice |> Option.defaultValue FieldSliceSettings.defaults
                let nextFieldSlice =
                    match key with
                    | "plane" -> applyWhenSome ParamValue.asString (fun next -> { fieldSlice with Plane = next }) fieldSlice value
                    | "offset" -> applyWhenSome ParamValue.asFloat (fun next -> { fieldSlice with Offset = next }) fieldSlice value
                    | _ -> fieldSlice
                { action with FieldSlice = Some nextFieldSlice })
            doc

    let reorder (ids: string list) (doc: Document) : Document =
        let lookup = doc.Actions |> List.map (fun a -> a.Id, a) |> Map.ofList

        { doc with
            Actions = ids |> List.choose (fun id -> Map.tryFind id lookup) }

    let private patchSketchEntityParam (key: string) (value: ParamValue) (sketch: ActionSketch) =
        let parts = key.Split('.')

        if parts.Length <> 4 || parts.[0] <> "sketch" || parts.[1] <> "entity" then
            sketch
        else
            let entityId = parts.[2]
            let field = parts.[3]

            let entities =
                sketch.Entities
                |> List.map (fun entity ->
                    match entity with
                    | REPoint(id, x, y) when id = entityId ->
                        let number = ParamValue.asFloat value
                        REPoint(id, (if field = "x" then number |> Option.defaultValue x else x), (if field = "y" then number |> Option.defaultValue y else y))
                    | RECircle(id, center, radius) when id = entityId && field = "radius" ->
                        RECircle(id, center, applyWhenSome ParamValue.asFloat (fun next -> next) radius value)
                    | REArc(id, startId, endId, ArcThreePoint through) when id = entityId ->
                        let number = ParamValue.asFloat value
                        let through' : FreePoint =
                            { X = if field = "throughX" then number |> Option.defaultValue through.X else through.X
                              Y = if field = "throughY" then number |> Option.defaultValue through.Y else through.Y }
                        REArc(id, startId, endId, ArcThreePoint through')
                    | _ -> entity)

            { sketch with Entities = entities }

    let private patchSketchConstraintParam (key: string) (value: ParamValue) (sketch: ActionSketch) =
        let parts = key.Split('.')

        if parts.Length < 4 || parts.[0] <> "sketch" || parts.[1] <> "constraint" then
            sketch
        else
            match System.Int32.TryParse(parts.[2]) with
            | false, _ -> sketch
            | true, index ->
                let constraints =
                    sketch.Constraints
                    |> List.mapi (fun i item ->
                        if i <> index then
                            item
                        else
                            match parts.[3], item with
                            | "labelPosition", _ when parts.Length = 5 ->
                                patchConstraintLabel parts.[4] value item
                            | "distance", _
                            | "diameter", _
                            | "angle", _ ->
                                patchConstraintScalar value item
                            | _ -> item)

                { sketch with
                    Constraints = constraints }

    let patchParamValue (id: string) (key: string) (value: ParamValue) (doc: Document) : Document =
        let patchFromSketchSelection current =
            if key <> "selection" then
                current
            else
                match ParamValue.tryField "case" value |> Option.bind ParamValue.asString with
                | Some "SelectionElements" ->
                    let lineIds =
                        ParamValue.tryField "lineIds" value
                        |> Option.bind (function
                            | VArray items ->
                                List.foldBack
                                    (fun item acc ->
                                        match item, acc with
                                        | Some x, Some xs -> Some(x :: xs)
                                        | _ -> None)
                                    (items |> List.map ParamValue.asString)
                                    (Some [])
                            | _ -> None)
                        |> Option.defaultValue []
                    SelectionElements lineIds
                | _ ->
                    SelectionLoop(ParamValue.tryField "loopId" value |> Option.bind ParamValue.asStringOption)

        mapActionById id
            (fun action ->
                let nextKind =
                    match action.Kind with
                    | Cylinder(r, h) -> Cylinder(floatOr r key "radius" value, floatOr h key "height" value)
                    | Sphere r -> Sphere(floatOr r key "radius" value)
                    | Box(w, h, d) -> Box(floatOr w key "width" value, floatOr h key "height" value, floatOr d key "depth" value)
                    | Translate(c, x, y, z) ->
                        Translate(stringOptionOr c key "child" value, floatOr x key "x" value, floatOr y key "y" value, floatOr z key "z" value)
                    | Rotate(c, ax, ay, az, ang) ->
                        Rotate(stringOptionOr c key "child" value, floatOr ax key "ax" value, floatOr ay key "ay" value, floatOr az key "az" value, floatOr ang key "angle" value)
                    | HalfPlane(ax, off, fl) ->
                        HalfPlane(stringOr ax key "axis" value, floatOr off key "offset" value, boolOr fl key "flip" value)
                    | Move(c, f) ->
                        Move(stringOptionOr c key "child" value, stringOptionOr f key "frame" value)
                    | Union(a, b, r) ->
                        Union(stringOptionOr a key "a" value, stringOptionOr b key "b" value, floatOr r key "radius" value)
                    | Subtract(a, b, r) ->
                        Subtract(stringOptionOr a key "a" value, stringOptionOr b key "b" value, floatOr r key "radius" value)
                    | Intersect(a, b, r) ->
                        Intersect(stringOptionOr a key "a" value, stringOptionOr b key "b" value, floatOr r key "radius" value)
                    | Sketch(origin, plane, sketch) ->
                        let nextPlane =
                            if key = "plane" then
                                match ParamValue.asString value with
                                | Some "XZ" -> XZ
                                | Some "YZ" -> YZ
                                | _ -> XY
                            else
                                plane
                        let nextSketch =
                            if key.StartsWith("sketch.entity.") then patchSketchEntityParam key value sketch
                            elif key.StartsWith("sketch.constraint.") then patchSketchConstraintParam key value sketch
                            else sketch
                        Sketch(stringOptionOr origin key "origin" value, nextPlane, nextSketch)
                    | FromSketch(c, flip, sel) ->
                        FromSketch(stringOptionOr c key "child" value, boolOr flip key "flip" value, patchFromSketchSelection sel)
                    | Thicken(c, amt) ->
                        Thicken(stringOptionOr c key "child" value, floatOr amt key "amount" value)
                    | Shell(c, t) ->
                        Shell(stringOptionOr c key "child" value, floatOr t key "thickness" value)
                    | Mesh(c, s, res) ->
                        Mesh(stringOptionOr c key "child" value, floatOr s key "size" value, intOr res key "resolution" value)
                    | other -> other
                { action with Kind = nextKind })
            doc

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
                Kind =
                  Sketch(
                      origin = Some "origin",
                      plane = XY,
                      sketch =
                          { Entities =
                              [ REPoint("p_bl", 0.0, 0.0)
                                REPoint("p_br", 10.0, 0.0)
                                REPoint("p_tr", 10.0, 10.0)
                                REPoint("p_tl", 0.0, 10.0)
                                RELine("l_bottom", "p_bl", "p_br")
                                RELine("l_right", "p_br", "p_tr")
                                RELine("l_top", "p_tr", "p_tl")
                                RELine("l_left", "p_tl", "p_bl") ]
                            Constraints =
                              [ Fixed("p_bl", 0.0, 0.0)
                                Horizontal("p_bl", "p_br")
                                Horizontal("p_tl", "p_tr")
                                Vertical("p_bl", "p_tl")
                                Vertical("p_br", "p_tr")
                                Distance("p_bl", "p_br", 10.0, None)
                                Distance("p_bl", "p_tl", 10.0, None) ] }
                  )
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
                Kind = FromSketch(child = Some "sketch1", flip = false, selection = SelectionLoop None)
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
