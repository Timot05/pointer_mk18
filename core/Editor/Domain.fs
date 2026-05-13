namespace Server

// ---------------------------------------------------------------------------
// Domain types — sketch-slot field metadata + the document record.
// Pre-notebook this file held the action graph (ActionKind / DocAction /
// patchParamValue's giant kind matcher). All of that is gone; the slot
// table and sketch authoring still need a typed handle on "this scalar
// of this sketch entity / constraint", so what remains is the small
// ActionParamField vocabulary that names sketch-local scalars plus a
// patcher that walks Doc.Blocks' SketchBody.
// ---------------------------------------------------------------------------

/// Legacy alias retained because slot keys are typed `{ ActionId; Path }`
/// and rewriting the slot table would cascade. Sketch ids today are
/// `@block_<n>` strings (see `SketchAuthoring.blockSketchId`).
type ActionId = string

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

type ActionParamField =
    | SketchEntityField of string * SketchEntityField
    | SketchConstraintField of int * SketchConstraintField

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

type Document =
    { Name: string
      Blocks: Server.Lang.Notebook.Block list
      NextBlockId: Server.Lang.Notebook.BlockId
      SelectedBlockId: Server.Lang.Notebook.BlockId option
      /// Free-form DSL source authored in the Monaco script panel. Top-level
      /// `let f (x: Scalar) ... = body` defs surface as draggable blocks via
      /// `UserScript.analyze`. Empty for documents authored before the
      /// script editor shipped — JSON load tolerates the missing field.
      ScriptSourceText: string }

module Document =

    let pathOfParamField =
        function
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

    let private patchSketchEntity entityId field value (sketch: ActionSketch) =
        let entities =
            sketch.Entities
            |> List.map (fun entity ->
                match entity with
                | REPoint(id, x, y) when id = entityId ->
                    let number = ParamValue.asFloat value
                    REPoint(
                        id,
                        (match field with | PointX -> number |> Option.defaultValue x | _ -> x),
                        (match field with | PointY -> number |> Option.defaultValue y | _ -> y))
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

    let private patchSketchConstraint index field value (sketch: ActionSketch) =
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

    let private tryParseBlockId (sketchId: string) : Server.Lang.Notebook.BlockId option =
        if sketchId.StartsWith "@block_" then
            let rest = sketchId.Substring 7
            match System.Int32.TryParse rest with
            | true, bid -> Some bid
            | _ -> None
        else None

    /// Patch a single sketch-local scalar (entity coord or constraint
    /// label/value) inside the SketchBlock identified by `sketchId`.
    /// Sketch ids in notebook mode are `@block_<n>` strings; anything
    /// else is treated as a no-op.
    let patchParamValue (sketchId: string) (field: ActionParamField) (value: ParamValue) (doc: Document) : Document =
        match tryParseBlockId sketchId with
        | None -> doc
        | Some bid ->
            let blocks =
                doc.Blocks
                |> List.map (fun b ->
                    if b.Id <> bid then b
                    else
                        match b.Body with
                        | Server.Lang.Notebook.SketchBody data ->
                            let nextSketch =
                                match field with
                                | SketchEntityField(entityId, entityField) -> patchSketchEntity entityId entityField value data.Sketch
                                | SketchConstraintField(index, constraintField) -> patchSketchConstraint index constraintField value data.Sketch
                            { b with Body = Server.Lang.Notebook.SketchBody { data with Sketch = nextSketch } }
                        | _ -> b)
            { doc with Blocks = blocks }

    let private lineSketch x0 y0 x1 y1 : ActionSketch =
        { Entities =
            [ REPoint("p0", x0, y0)
              REPoint("p1", x1, y1)
              RELine("line0", "p0", "p1") ]
          Constraints = [] }

    /// Boot-time notebook: two sketch guide curves wired into the half-wing
    /// preview block, then mirrored symmetrically across the root XZ plane.
    let private defaultBlocks : Server.Lang.Notebook.Block list =
        [ { Id = 0
            Name = "leading"
            Body = Server.Lang.Notebook.SketchBody
                { Sketch = lineSketch 0.0 0.0 0.25 5.0
                  Plane = XY }
            Visibility = Server.Lang.Notebook.VHidden
            ColorIndex = 0
            SlicePlane = Server.Lang.Notebook.defaultSlicePlane }
          { Id = 1
            Name = "trailing"
            Body = Server.Lang.Notebook.SketchBody
                { Sketch = lineSketch 2.0 0.0 1.25 5.0
                  Plane = XY }
            Visibility = Server.Lang.Notebook.VHidden
            ColorIndex = 0
            SlicePlane = Server.Lang.Notebook.defaultSlicePlane }
          { Id = 2
            Name = "half_wing"
            Body =
                Server.Lang.Notebook.NativeBody(
                    "wing-remap-preview",
                    Map.ofList
                        [ "leading", Server.Lang.Notebook.ArgRef(Some 0)
                          "trailing", Server.Lang.Notebook.ArgRef(Some 1) ])
            Visibility = Server.Lang.Notebook.VHidden
            ColorIndex = 0
            SlicePlane =
                { Server.Lang.Notebook.defaultSlicePlane with
                    Origin = { X = 1.0; Y = 2.5; Z = 0.0 } } }
          { Id = 3
            Name = "full_wing"
            Body =
                Server.Lang.Notebook.NativeBody(
                    "mirror-symmetric",
                    Map.ofList
                        [ "axis", Server.Lang.Notebook.ArgScalar 1.0
                          "root", Server.Lang.Notebook.ArgScalar 0.0
                          "child", Server.Lang.Notebook.ArgRef(Some 2) ])
            Visibility = Server.Lang.Notebook.VIsosurface
            ColorIndex = 0
            SlicePlane =
                { Server.Lang.Notebook.defaultSlicePlane with
                    Origin = { X = 1.0; Y = 2.5; Z = 0.0 } } } ]

    /// Demo content the script editor shows on a fresh document. Defines
    /// one user spec (`capsule`) so the user can immediately see how a
    /// `let f (...) = body` def becomes a draggable block in the +Add
    /// palette (⌘K). Built-in primitives (sphere / box / cylinder /
    /// union / subtract / intersect / translate) are in scope by name.
    let private defaultScriptSource = """// User-defined block kinds. Each top-level
//     let name = fun (param: Type) ... -> body end
// appears in the +Add palette (⌘K). Built-in primitives —
// sphere, box, cylinder, union, subtract, intersect, translate —
// are in scope.

let capsule = fun (radius: Scalar) (length: Scalar) ->
    let half = length / 2
    let body = cylinder radius length
    let top = translate 0 0 half (sphere radius)
    let bottom = translate 0 0 (0 - half) (sphere radius)
    union (union body top 0) bottom 0
end
"""

    let emptyDocument () : Document =
        { Name = "untitled"
          Blocks = defaultBlocks
          NextBlockId = 4
          SelectedBlockId = Some 3
          ScriptSourceText = defaultScriptSource }
