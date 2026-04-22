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

type DisplayField =
    | DisplayColor
    | DisplayOpacity
    | DisplayIsoValue

type FieldSliceField =
    | SlicePlane
    | SliceOffset

type SketchEntityField =
    | PointX
    | PointY
    | CircleRadius
    | ArcThroughX
    | ArcThroughY

type SketchConstraintField =
    | ConstraintLabelX
    | ConstraintLabelY
    | ConstraintDistance
    | ConstraintDiameter
    | ConstraintAngle

type FromSketchSelectionValue =
    | SelectionLoopValue of string option
    | SelectionElementsValue of string list

type ActionParamField =
    | CylinderRadius
    | CylinderHeight
    | SphereRadius
    | BoxWidth
    | BoxHeight
    | BoxDepth
    | TranslateChild
    | TranslateX
    | TranslateY
    | TranslateZ
    | RotateChild
    | RotateAxisX
    | RotateAxisY
    | RotateAxisZ
    | RotateAngle
    | HalfPlaneAxis
    | HalfPlaneOffset
    | HalfPlaneFlip
    | MoveChild
    | MoveFrame
    | UnionA
    | UnionB
    | UnionRadius
    | SubtractA
    | SubtractB
    | SubtractRadius
    | IntersectA
    | IntersectB
    | IntersectRadius
    | SketchOrigin
    | SketchPlane
    | SketchEntityField of string * SketchEntityField
    | SketchConstraintField of int * SketchConstraintField
    | FromSketchChild
    | FromSketchFlip
    | FromSketchSelection
    | ThickenChild
    | ThickenAmount
    | ShellChild
    | ShellThickness
    | MeshChild
    | MeshSize
    | MeshResolution

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

/// Which renderer drives the 3D field background. `IntervalKernel` is the
/// Zig WASM voxel renderer; `Raymarch` is the older GPU sphere-tracer
/// kept as an alternative. Flipped from the UI via `SetViewerMode`.
type ViewerMode =
    | IntervalKernel
    | Raymarch

type Document =
    { Name: string
      Actions: DocAction list
      SelectedId: string option }

module Document =

    let pathOfDisplayField =
        function
        | DisplayColor -> [ "display.color.0"; "display.color.1"; "display.color.2" ]
        | DisplayOpacity -> [ "display.opacity" ]
        | DisplayIsoValue -> [ "display.isoValue" ]

    let pathOfFieldSliceField =
        function
        | SlicePlane -> []
        | SliceOffset -> [ "fieldSlice.offset" ]

    let pathOfParamField =
        function
        | CylinderRadius -> "radius"
        | CylinderHeight -> "height"
        | SphereRadius -> "radius"
        | BoxWidth -> "width"
        | BoxHeight -> "height"
        | BoxDepth -> "depth"
        | TranslateChild -> "child"
        | TranslateX -> "x"
        | TranslateY -> "y"
        | TranslateZ -> "z"
        | RotateChild -> "child"
        | RotateAxisX -> "ax"
        | RotateAxisY -> "ay"
        | RotateAxisZ -> "az"
        | RotateAngle -> "angle"
        | HalfPlaneAxis -> "axis"
        | HalfPlaneOffset -> "offset"
        | HalfPlaneFlip -> "flip"
        | MoveChild -> "child"
        | MoveFrame -> "frame"
        | UnionA -> "a"
        | UnionB -> "b"
        | UnionRadius -> "radius"
        | SubtractA -> "a"
        | SubtractB -> "b"
        | SubtractRadius -> "radius"
        | IntersectA -> "a"
        | IntersectB -> "b"
        | IntersectRadius -> "radius"
        | SketchOrigin -> "origin"
        | SketchPlane -> "plane"
        | SketchEntityField(entityId, PointX) -> $"sketch.entity.{entityId}.x"
        | SketchEntityField(entityId, PointY) -> $"sketch.entity.{entityId}.y"
        | SketchEntityField(entityId, CircleRadius) -> $"sketch.entity.{entityId}.radius"
        | SketchEntityField(entityId, ArcThroughX) -> $"sketch.entity.{entityId}.throughX"
        | SketchEntityField(entityId, ArcThroughY) -> $"sketch.entity.{entityId}.throughY"
        | SketchConstraintField(index, ConstraintLabelX) -> $"sketch.constraint.{index}.labelPosition.x"
        | SketchConstraintField(index, ConstraintLabelY) -> $"sketch.constraint.{index}.labelPosition.y"
        | SketchConstraintField(index, ConstraintDistance) -> $"sketch.constraint.{index}.distance"
        | SketchConstraintField(index, ConstraintDiameter) -> $"sketch.constraint.{index}.diameter"
        | SketchConstraintField(index, ConstraintAngle) -> $"sketch.constraint.{index}.angle"
        | FromSketchChild -> "child"
        | FromSketchFlip -> "flip"
        | FromSketchSelection -> "selection"
        | ThickenChild -> "child"
        | ThickenAmount -> "amount"
        | ShellChild -> "child"
        | ShellThickness -> "thickness"
        | MeshChild -> "child"
        | MeshSize -> "size"
        | MeshResolution -> "resolution"

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

    let patchDisplayValue (id: string) (field: DisplayField) (value: ParamValue) (doc: Document) : Document =
        mapActionById id
            (fun action ->
                let display = action.Display |> Option.defaultValue DisplaySettings.defaults
                let nextDisplay =
                    match field with
                    | DisplayColor -> applyWhenSome ParamValue.asFloatArray (fun next -> { display with Color = next }) display value
                    | DisplayOpacity -> applyWhenSome ParamValue.asFloat (fun next -> { display with Opacity = next }) display value
                    | DisplayIsoValue -> applyWhenSome ParamValue.asFloat (fun next -> { display with IsoValue = next }) display value
                { action with Display = Some nextDisplay })
            doc

    let toggleFieldSlice (id: string) (doc: Document) : Document =
        mapActionById id
            (fun action ->
                let fieldSlice = action.FieldSlice |> Option.defaultValue FieldSliceSettings.defaults
                { action with FieldSlice = Some { fieldSlice with Enabled = not fieldSlice.Enabled } })
            doc

    let patchFieldSliceValue (id: string) (field: FieldSliceField) (value: ParamValue) (doc: Document) : Document =
        mapActionById id
            (fun action ->
                let fieldSlice = action.FieldSlice |> Option.defaultValue FieldSliceSettings.defaults
                let nextFieldSlice =
                    match field with
                    | SlicePlane -> applyWhenSome ParamValue.asString (fun next -> { fieldSlice with Plane = next }) fieldSlice value
                    | SliceOffset -> applyWhenSome ParamValue.asFloat (fun next -> { fieldSlice with Offset = next }) fieldSlice value
                { action with FieldSlice = Some nextFieldSlice })
            doc

    let reorder (ids: string list) (doc: Document) : Document =
        let lookup = doc.Actions |> List.map (fun a -> a.Id, a) |> Map.ofList

        { doc with
            Actions = ids |> List.choose (fun id -> Map.tryFind id lookup) }

    let private patchSketchEntityParam entityId field value (sketch: ActionSketch) =
        let entities =
            sketch.Entities
            |> List.map (fun entity ->
                match entity with
                | REPoint(id, x, y) when id = entityId ->
                    let number = ParamValue.asFloat value
                    REPoint(
                        id,
                        (match field with | PointX -> number |> Option.defaultValue x | _ -> x),
                        (match field with | PointY -> number |> Option.defaultValue y | _ -> y)
                    )
                | RECircle(id, center, radius) when id = entityId && field = CircleRadius ->
                    RECircle(id, center, applyWhenSome ParamValue.asFloat (fun next -> next) radius value)
                | REArc(id, startId, endId, ArcThreePoint through) when id = entityId ->
                    let number = ParamValue.asFloat value
                    let through' : FreePoint =
                        { X = (match field with | ArcThroughX -> number |> Option.defaultValue through.X | _ -> through.X)
                          Y = (match field with | ArcThroughY -> number |> Option.defaultValue through.Y | _ -> through.Y) }
                    REArc(id, startId, endId, ArcThreePoint through')
                | _ -> entity)
        { sketch with Entities = entities }

    let private patchFromSketchSelection current =
        function
        | SelectionElementsValue lineIds -> SelectionElements lineIds
        | SelectionLoopValue loopId -> SelectionLoop loopId

    let private patchSketchConstraintParam index field value (sketch: ActionSketch) =
        let constraints =
            sketch.Constraints
            |> List.mapi (fun i item ->
                if i <> index then
                    item
                else
                    match field with
                    | ConstraintLabelX -> patchConstraintLabel "x" value item
                    | ConstraintLabelY -> patchConstraintLabel "y" value item
                    | ConstraintDistance
                    | ConstraintDiameter
                    | ConstraintAngle -> patchConstraintScalar value item)
        { sketch with Constraints = constraints }

    let patchParamValue (id: string) (field: ActionParamField) (value: ParamValue) (doc: Document) : Document =

        mapActionById id
            (fun action ->
                let nextKind =
                    match action.Kind with
                    | Cylinder(r, h) ->
                        Cylinder(
                            (match field with | CylinderRadius -> value |> ParamValue.asFloat |> Option.defaultValue r | _ -> r),
                            (match field with | CylinderHeight -> value |> ParamValue.asFloat |> Option.defaultValue h | _ -> h))
                    | Sphere r ->
                        Sphere(match field with | SphereRadius -> value |> ParamValue.asFloat |> Option.defaultValue r | _ -> r)
                    | Box(w, h, d) ->
                        Box(
                            (match field with | BoxWidth -> value |> ParamValue.asFloat |> Option.defaultValue w | _ -> w),
                            (match field with | BoxHeight -> value |> ParamValue.asFloat |> Option.defaultValue h | _ -> h),
                            (match field with | BoxDepth -> value |> ParamValue.asFloat |> Option.defaultValue d | _ -> d))
                    | Translate(c, x, y, z) ->
                        Translate(
                            (match field with | TranslateChild -> ParamValue.asStringOption value | _ -> c),
                            (match field with | TranslateX -> value |> ParamValue.asFloat |> Option.defaultValue x | _ -> x),
                            (match field with | TranslateY -> value |> ParamValue.asFloat |> Option.defaultValue y | _ -> y),
                            (match field with | TranslateZ -> value |> ParamValue.asFloat |> Option.defaultValue z | _ -> z))
                    | Rotate(c, ax, ay, az, ang) ->
                        Rotate(
                            (match field with | RotateChild -> ParamValue.asStringOption value | _ -> c),
                            (match field with | RotateAxisX -> value |> ParamValue.asFloat |> Option.defaultValue ax | _ -> ax),
                            (match field with | RotateAxisY -> value |> ParamValue.asFloat |> Option.defaultValue ay | _ -> ay),
                            (match field with | RotateAxisZ -> value |> ParamValue.asFloat |> Option.defaultValue az | _ -> az),
                            (match field with | RotateAngle -> value |> ParamValue.asFloat |> Option.defaultValue ang | _ -> ang))
                    | HalfPlane(ax, off, fl) ->
                        HalfPlane(
                            (match field with | HalfPlaneAxis -> value |> ParamValue.asString |> Option.defaultValue ax | _ -> ax),
                            (match field with | HalfPlaneOffset -> value |> ParamValue.asFloat |> Option.defaultValue off | _ -> off),
                            (match field with | HalfPlaneFlip -> value |> ParamValue.asBool |> Option.defaultValue fl | _ -> fl))
                    | Move(c, f) ->
                        Move(
                            (match field with | MoveChild -> ParamValue.asStringOption value | _ -> c),
                            (match field with | MoveFrame -> ParamValue.asStringOption value | _ -> f))
                    | Union(a, b, r) ->
                        Union(
                            (match field with | UnionA -> ParamValue.asStringOption value | _ -> a),
                            (match field with | UnionB -> ParamValue.asStringOption value | _ -> b),
                            (match field with | UnionRadius -> value |> ParamValue.asFloat |> Option.defaultValue r | _ -> r))
                    | Subtract(a, b, r) ->
                        Subtract(
                            (match field with | SubtractA -> ParamValue.asStringOption value | _ -> a),
                            (match field with | SubtractB -> ParamValue.asStringOption value | _ -> b),
                            (match field with | SubtractRadius -> value |> ParamValue.asFloat |> Option.defaultValue r | _ -> r))
                    | Intersect(a, b, r) ->
                        Intersect(
                            (match field with | IntersectA -> ParamValue.asStringOption value | _ -> a),
                            (match field with | IntersectB -> ParamValue.asStringOption value | _ -> b),
                            (match field with | IntersectRadius -> value |> ParamValue.asFloat |> Option.defaultValue r | _ -> r))
                    | Sketch(origin, plane, sketch) ->
                        let nextPlane =
                            match field with
                            | SketchPlane ->
                                match ParamValue.asString value with
                                | Some "XZ" -> XZ
                                | Some "YZ" -> YZ
                                | _ -> XY
                            | _ -> plane
                        let nextSketch =
                            match field with
                            | SketchEntityField(entityId, entityField) -> patchSketchEntityParam entityId entityField value sketch
                            | SketchConstraintField(index, constraintField) -> patchSketchConstraintParam index constraintField value sketch
                            | _ -> sketch
                        Sketch(
                            (match field with | SketchOrigin -> ParamValue.asStringOption value | _ -> origin),
                            nextPlane,
                            nextSketch)
                    | FromSketch(c, flip, sel) ->
                        FromSketch(
                            (match field with | FromSketchChild -> ParamValue.asStringOption value | _ -> c),
                            (match field with | FromSketchFlip -> value |> ParamValue.asBool |> Option.defaultValue flip | _ -> flip),
                            (match field with
                             | FromSketchSelection ->
                                match value with
                                | VRecord _ ->
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
                                        patchFromSketchSelection sel (SelectionElementsValue lineIds)
                                    | _ ->
                                        patchFromSketchSelection sel (SelectionLoopValue(ParamValue.tryField "loopId" value |> Option.bind ParamValue.asStringOption))
                                | _ -> sel
                             | _ -> sel))
                    | Thicken(c, amt) ->
                        Thicken(
                            (match field with | ThickenChild -> ParamValue.asStringOption value | _ -> c),
                            (match field with | ThickenAmount -> value |> ParamValue.asFloat |> Option.defaultValue amt | _ -> amt))
                    | Shell(c, t) ->
                        Shell(
                            (match field with | ShellChild -> ParamValue.asStringOption value | _ -> c),
                            (match field with | ShellThickness -> value |> ParamValue.asFloat |> Option.defaultValue t | _ -> t))
                    | Mesh(c, s, res) ->
                        Mesh(
                            (match field with | MeshChild -> ParamValue.asStringOption value | _ -> c),
                            (match field with | MeshSize -> value |> ParamValue.asFloat |> Option.defaultValue s | _ -> s),
                            (match field with | MeshResolution -> value |> ParamValue.asInt |> Option.defaultValue res | _ -> res))
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

    // Stress document — extends the default doc with a small CSG blob so the
    // viewer renders something non-trivial on fresh load. Tunable via the
    // `gridN` constant. Keep `defaultDocument` untouched so the existing
    // pipeline/typecheck tests continue to compare against the small reference.
    //
    // NOTE: the Zig voxel kernel has `MAX_TAPE = 1024` and `simplify`'s
    // out-buffer is sized to MAX_TAPE, so the lowered tape must stay well
    // under that limit (transient constants can briefly double the op count).
    let stressDocument () : Document =
        let baseDoc = defaultDocument ()

        let mk id name kind =
            { Id = id
              Name = Some name
              Kind = kind
              Visible = true
              Display = None
              FieldSlice = None }

        let gridN = 2
        let spacing = 6.0
        let sphereR = 2.6
        let smoothR = 0.8
        let centerOffset = float (gridN - 1) * spacing * 0.5

        let gridCells =
            [ for i in 0 .. gridN - 1 do
                for j in 0 .. gridN - 1 do
                    for k in 0 .. gridN - 1 do
                        yield i, j, k ]

        let translatedSphereId (i, j, k) = sprintf "tsph_%d_%d_%d" i j k

        let sphereActions =
            gridCells
            |> List.collect (fun (i, j, k) ->
                let sid = sprintf "ssrc_%d_%d_%d" i j k
                let x = float i * spacing - centerOffset
                let y = float j * spacing - centerOffset
                let z = float k * spacing - centerOffset
                [ mk sid "sph" (Sphere(radius = sphereR))
                  mk (translatedSphereId (i, j, k)) "tsph" (Translate(child = Some sid, x = x, y = y, z = z)) ])

        let chainUnions (prefix: string) (radius: float) (ids: string list) : DocAction list * string option =
            match ids with
            | [] -> [], None
            | first :: rest ->
                let folder (acc, lastId, counter) nextId =
                    let uid = sprintf "%s_%d" prefix counter
                    let union = mk uid "u" (Union(a = Some lastId, b = Some nextId, radius = radius))
                    acc @ [ union ], uid, counter + 1
                let actions, lastId, _ = List.fold folder ([], first, 0) rest
                actions, Some lastId

        let sphereUnionActions, gridRootId =
            gridCells |> List.map translatedSphereId |> chainUnions "usph" smoothR

        let displayOn = { DisplaySettings.defaults with Enabled = true }

        let finalId, finalAction =
            match gridRootId with
            | Some rootId ->
                // Display lives on a dedicated final action so the root id is
                // stable regardless of how many spheres/unions were emitted.
                let finalId = "stressFinal"
                let kind = Translate(child = Some rootId, x = 0.0, y = 0.0, z = 0.0)
                finalId,
                { Id = finalId
                  Name = Some "stress root"
                  Kind = kind
                  Visible = true
                  Display = Some displayOn
                  FieldSlice = None }
            | None ->
                "origin", baseDoc.Actions |> List.head

        let extras = sphereActions @ sphereUnionActions @ [ finalAction ]

        { baseDoc with
            Actions = baseDoc.Actions @ extras
            SelectedId = Some finalId }
