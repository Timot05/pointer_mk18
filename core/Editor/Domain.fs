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
                            // Entity-coord patches don't change which entities exist
                            // — `normalize` is a near-no-op match here but kept for
                            // consistency so every write-back path runs reconciliation.
                            let normalized = SketchLoops.normalize nextSketch
                            { b with Body = Server.Lang.Notebook.SketchBody { data with Sketch = normalized } }
                        | _ -> b)
            { doc with Blocks = blocks }

    let private lineSketch x0 y0 x1 y1 : ActionSketch =
        { Entities =
            [ REPoint("p0", x0, y0)
              REPoint("p1", x1, y1)
              RELine("line0", "p0", "p1") ]
          Constraints = []
          Loops = [] }

    let private squareSketch cx cy half : ActionSketch =
        let raw : ActionSketch =
            { Entities =
                [ REPoint("p0", cx - half, cy - half)
                  REPoint("p1", cx + half, cy - half)
                  REPoint("p2", cx + half, cy + half)
                  REPoint("p3", cx - half, cy + half)
                  RELine("l0", "p0", "p1")
                  RELine("l1", "p1", "p2")
                  RELine("l2", "p2", "p3")
                  RELine("l3", "p3", "p0") ]
              Constraints = []
              Loops = [] }
        SketchLoops.normalize raw

    let private circleSketch cx cy r : ActionSketch =
        let raw : ActionSketch =
            { Entities =
                [ REPoint("c0", cx, cy)
                  RECircle("circ", "c0", r) ]
              Constraints = []
              Loops = [] }
        SketchLoops.normalize raw

    let private rectangleSketch cx cy halfX halfY : ActionSketch =
        let raw : ActionSketch =
            { Entities =
                [ REPoint("p0", cx - halfX, cy - halfY)
                  REPoint("p1", cx + halfX, cy - halfY)
                  REPoint("p2", cx + halfX, cy + halfY)
                  REPoint("p3", cx - halfX, cy + halfY)
                  RELine("l0", "p0", "p1")
                  RELine("l1", "p1", "p2")
                  RELine("l2", "p2", "p3")
                  RELine("l3", "p3", "p0") ]
              Constraints = []
              Loops = [] }
        SketchLoops.normalize raw

    /// Both wing guide curves bundled into one sketch. Two cubic Bezier
    /// splines: `spline_0` is the leading edge, `spline_1` is the
    /// trailing edge. Control points bow each edge outward at mid-span
    /// to produce a Spitfire-style elliptical planform — the chord
    /// peaks around y ≈ 2 and tapers smoothly to the tip.
    let private wingGuidesSketch : ActionSketch =
        { Entities =
            [ REPoint("le_root", 0.0, 0.0)
              REPoint("le_cp0", -0.5, 1.5)
              REPoint("le_cp1", -0.25, 3.5)
              REPoint("le_tip", 0.25, 5.0)
              REPoint("te_root", 2.0, 0.0)
              REPoint("te_cp0", 2.5, 1.5)
              REPoint("te_cp1", 2.25, 3.5)
              REPoint("te_tip", 1.25, 5.0)
              REBezierCubic("leading", "le_root", "le_cp0", "le_cp1", "le_tip")
              REBezierCubic("trailing", "te_root", "te_cp0", "te_cp1", "te_tip") ]
          Constraints = []
          Loops = [] }

    /// Boot-time notebook: one sketch with both wing guide curves
    /// wired into the half-wing preview block via line-primitive paths,
    /// then mirrored symmetrically across the root XZ plane.
    let private defaultBlocks : Server.Lang.Notebook.Block list =
        [ { Id = 0
            Name = "wing_guides"
            Body = Server.Lang.Notebook.SketchBody
                { Sketch = wingGuidesSketch
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
                        [ "leading", Server.Lang.AstBuilder.pathE [ "wing_guides"; "spline_0" ]
                          "trailing", Server.Lang.AstBuilder.pathE [ "wing_guides"; "spline_1" ] ])
            Visibility = Server.Lang.Notebook.VHidden
            ColorIndex = 0
            SlicePlane =
                { Server.Lang.Notebook.defaultSlicePlane with
                    Origin = { X = 1.0; Y = 2.5; Z = 0.0 } } }
          { Id = 3
            Name = "full_wing"
            Body =
                Server.Lang.Notebook.NativeBody(
                    "mirror_symmetric",
                    Map.ofList
                        [ "axis", Server.Lang.AstBuilder.numE 1.0
                          "root", Server.Lang.AstBuilder.numE 0.0
                          "child", Server.Lang.AstBuilder.varE "half_wing" ])
            Visibility = Server.Lang.Notebook.VIsosurface
            ColorIndex = 0
            SlicePlane =
                { Server.Lang.Notebook.defaultSlicePlane with
                    Origin = { X = 1.0; Y = 2.5; Z = 0.0 } } }
          // Aircraft fuselage: a body lofted along the world X axis
          // (nose at X=-3, tail at X=6) so it runs perpendicular to
          // the wing span (which lives along Y), sitting on the
          // wing's mirror plane (Y=0). Cross-sections are rectangles
          // in the YZ plane sized to match the spline separation at
          // X=start (±0.25 in world Y), so their natural width IS the
          // body's width at the nose. The splines then stretch the
          // body outward at mid-cabin and back inward at the tail.
          { Id = 4
            Name = "body_nose"
            Body = Server.Lang.Notebook.SketchBody
                { Sketch = rectangleSketch 0.0 0.0 0.25 0.30
                  Plane = YZ }
            Visibility = Server.Lang.Notebook.VHidden
            ColorIndex = 0
            SlicePlane = Server.Lang.Notebook.defaultSlicePlane }
          { Id = 5
            Name = "body_tail"
            Body = Server.Lang.Notebook.SketchBody
                { Sketch = rectangleSketch 0.0 0.0 0.25 0.20
                  Plane = YZ }
            Visibility = Server.Lang.Notebook.VHidden
            ColorIndex = 0
            SlicePlane = Server.Lang.Notebook.defaultSlicePlane }
          { Id = 7
            Name = "body_guides"
            Body = Server.Lang.Notebook.SketchBody
                { Sketch =
                    { Entities =
                        [ // Top (positive-Y) silhouette: nose 0.25 →
                          // bulge to 0.8 around mid-body → tail 0.15.
                          REPoint("t_nose", -3.0,  0.25)
                          REPoint("t_cp0",  -1.0,  0.85)
                          REPoint("t_cp1",   3.5,  0.85)
                          REPoint("t_tail",  6.0,  0.15)
                          // Bottom (negative-Y) silhouette: mirror of the top.
                          REPoint("b_nose", -3.0, -0.25)
                          REPoint("b_cp0",  -1.0, -0.85)
                          REPoint("b_cp1",   3.5, -0.85)
                          REPoint("b_tail",  6.0, -0.15)
                          REBezierCubic("g_top", "t_nose", "t_cp0", "t_cp1", "t_tail")
                          REBezierCubic("g_bot", "b_nose", "b_cp0", "b_cp1", "b_tail") ]
                      Constraints = []
                      Loops = [] }
                  Plane = XY }
            Visibility = Server.Lang.Notebook.VHidden
            ColorIndex = 0
            SlicePlane = Server.Lang.Notebook.defaultSlicePlane }
          { Id = 6
            Name = "fuselage"
            Body =
                Server.Lang.Notebook.NativeBody(
                    "body_loft",
                    Map.ofList
                        [ "a", Server.Lang.AstBuilder.pathE [ "body_nose"; "loop_0" ]
                          "b", Server.Lang.AstBuilder.pathE [ "body_tail"; "loop_0" ]
                          "g_top", Server.Lang.AstBuilder.pathE [ "body_guides"; "spline_0" ]
                          "g_bot", Server.Lang.AstBuilder.pathE [ "body_guides"; "spline_1" ]
                          "start", Server.Lang.AstBuilder.numE -3.0
                          "end_pos", Server.Lang.AstBuilder.numE 6.0 ])
            Visibility = Server.Lang.Notebook.VIsosurface
            ColorIndex = 1
            SlicePlane =
                { Server.Lang.Notebook.defaultSlicePlane with
                    Origin = { X = 1.5; Y = 0.0; Z = 0.0 } } } ]

    /// Demo content the script editor shows on a fresh document.
    /// Defines the standard library of SDF primitives (sphere, box,
    /// cylinder, halfplane, union, intersect, subtract, thicken, shell)
    /// plus a `capsule` example so the user can read / edit / extend
    /// them directly. The math-primitive callables (`sqrt` / `abs` /
    /// `min` / `max` / `compare` / `remap_axes`) and spatial axes
    /// (`x` / `y` / `z`) are bound by `NotebookCompose.buildValueEnv`.
    /// `translate` / `mirror-symmetric` / `from-sketch` / `revolve` /
    /// `wing-remap-preview` stay as F# intrinsics in `BlockSpec.fs`
    /// (param-name clashes with axis names, hyphenated names, refined
    /// Loop types, or compose-time interceptors).
    let private defaultScriptSource = """// Standard library of SDF primitives, defined as user-editable
// scripts. Each top-level `let name = fun (param: Type) ... -> body end`
// appears in the +Add palette (⌘K). The math-primitive callables
// (sqrt, abs, min, max, compare, remap_axes) and spatial axes
// (x, y, z) are pre-bound; intrinsics translate / mirror-symmetric /
// from-sketch / revolve / wing-remap-preview live in BlockSpec.fs.

let sphere = fun (radius: Scalar) ->
    sqrt (x*x + y*y + z*z) - radius
end

let halfplane = fun (axis: Scalar) (offset: Scalar) ->
    let sx = 1 - abs (compare axis 0)
    let sy = 1 - abs (compare axis 1)
    let sz = 1 - abs (compare axis 2)
    sx*x + sy*y + sz*z - offset
end

let box = fun (width: Scalar) (height: Scalar) (depth: Scalar) ->
    let hx = width / 2
    let hy = height / 2
    let hz = depth / 2
    let bx = abs x - hx
    let by = abs y - hy
    let bz = abs z - hz
    let mx = max bx 0
    let my = max by 0
    let mz = max bz 0
    let outside = sqrt (mx*mx + my*my + mz*mz)
    let inside = min (max bx (max by bz)) 0
    outside + inside
end

let cylinder = fun (radius: Scalar) (height: Scalar) ->
    let radial = sqrt (x*x + y*y) - radius
    let axial = abs z - height / 2
    max radial axial
end

// Iquilez polynomial smooth-min, with `radius <= eps` snapping to a
// hard min. The `enabled` factor keeps radius=0 exact without adding
// a general AST `if` — `compare radius eps` is -1 below the snap
// threshold, ≥ 0 above, and `max ... 0` clamps the gate to {0, 1}.
let smooth_min = fun (a: Field) (b: Field) (radius: Scalar) ->
    let eps = 0.000001
    let k = max radius eps
    let h = max (k - abs (a - b)) 0 / k
    let enabled = max (compare radius eps) 0
    min a b - (enabled * h * h * h * k / 6)
end

let union = fun (target: Field) (tool: Field) (radius: Scalar) ->
    smooth_min target tool radius
end

let intersect = fun (target: Field) (tool: Field) (radius: Scalar) ->
    0 - (smooth_min (0 - target) (0 - tool) radius)
end

let subtract = fun (target: Field) (tool: Field) (radius: Scalar) ->
    0 - (smooth_min (0 - target) tool radius)
end

let thicken = fun (amount: Scalar) (child: Field) ->
    child - amount
end

let shell = fun (thickness: Scalar) (child: Field) ->
    max child (0 - (child + thickness))
end

// Multi-guide loft (wing-style). Sweeps a cross-section between
// loop `a` (at span position `start`) and loop `b` (at `end_pos`),
// while constraining the chord direction with two cubic Bezier
// guide splines — `g_left` and `g_right`. Conventions match
// `wing-remap-preview`:
//   cross-sections in the XZ plane (their local axes are X = chord,
//     Z = thickness)
//   guides in the XY plane (their local Y is the span = world Y)
//   span direction = Y
//   chord direction = X
//
// Width convention: at `start`, the cross-section sits at its
// natural absolute X extent — its world-X edges coincide with the
// spline endpoints `g_left.x0` and `g_right.x0`. As the span axis
// progresses, the splines expand or contract the cross-section by
// the ratio `S(y) / S(start)`, where `S(y)` is the current
// spline separation. So a cross-section that's 2-wide at the root
// (matching guide X separation 2 at `start`) stretches to 1.7 wide
// where the guides separate by 1.7, and so on.
//
// The cubic-at-y arithmetic is inlined rather than factored into a
// helper because the typecheck's refinement-cell mechanism only
// grows through direct path access — calling a helper that
// requires the refined Primitive would fail to propagate the
// required `x0/y0/x1/y1/cx0/cy0/cx1/cy1` members from this site.
let wing_loft = fun (a: Loop) (b: Loop) (g_left: Primitive) (g_right: Primitive) (start: Scalar) (end_pos: Scalar) ->
    let t = (y - start) / (end_pos - start + 0.000001)
    let t_l = (y - g_left.y0) / (g_left.y1 - g_left.y0 + 0.000001)
    let u_l = 1 - t_l
    let left_x = u_l*u_l*u_l * g_left.x0 + 3*u_l*u_l*t_l * g_left.cx0 + 3*u_l*t_l*t_l * g_left.cx1 + t_l*t_l*t_l * g_left.x1
    let t_r = (y - g_right.y0) / (g_right.y1 - g_right.y0 + 0.000001)
    let u_r = 1 - t_r
    let right_x = u_r*u_r*u_r * g_right.x0 + 3*u_r*u_r*t_r * g_right.cx0 + 3*u_r*t_r*t_r * g_right.cx1 + t_r*t_r*t_r * g_right.x1
    let s_start = g_right.x0 - g_left.x0
    let center = (right_x + left_x) / 2
    let chord = (x - center) * s_start / (right_x - left_x + 0.000001)
    let a_remap = remap_axes a.signed_distance chord y z
    let b_remap = remap_axes b.signed_distance chord y z
    let blend = (1 - t) * a_remap + t * b_remap
    let slab = max (y - end_pos) (start - y)
    max blend slab
end

// Aircraft-fuselage loft — `wing_loft` rotated 90° so the body axis is
// world X (nose → tail) instead of world Y. Cross-sections `a` and `b`
// live in the YZ plane (their local X = world Y = body width, their
// local Y = world Z = body height). Guides `g_top` / `g_bot` live in
// the XY plane: their local X = world X = body length, their local Y =
// world Y = body half-width.
//
// Width convention: at `start`, the cross-section sits at its
// natural absolute Y extent — its world-Y edges coincide with the
// spline endpoints `g_top.y0` and `g_bot.y0`. As X moves nose-to-
// tail the splines stretch or shrink the cross-section by the
// ratio `S(x) / S(start)`. So if the splines are at ±0.25 at the
// nose, the cross-section is naturally 0.5 wide; the body expands
// to 1.7 wide where the guides bow out to ±0.85, and contracts to
// 0.3 wide where the guides taper to ±0.15.
let body_loft = fun (a: Loop) (b: Loop) (g_top: Primitive) (g_bot: Primitive) (start: Scalar) (end_pos: Scalar) ->
    let t = (x - start) / (end_pos - start + 0.000001)
    let t_t = (x - g_top.x0) / (g_top.x1 - g_top.x0 + 0.000001)
    let u_t = 1 - t_t
    let top_y = u_t*u_t*u_t * g_top.y0 + 3*u_t*u_t*t_t * g_top.cy0 + 3*u_t*t_t*t_t * g_top.cy1 + t_t*t_t*t_t * g_top.y1
    let t_b = (x - g_bot.x0) / (g_bot.x1 - g_bot.x0 + 0.000001)
    let u_b = 1 - t_b
    let bot_y = u_b*u_b*u_b * g_bot.y0 + 3*u_b*u_b*t_b * g_bot.cy0 + 3*u_b*t_b*t_b * g_bot.cy1 + t_b*t_b*t_b * g_bot.y1
    let s_start = g_top.y0 - g_bot.y0
    let center = (top_y + bot_y) / 2
    let chord = (y - center) * s_start / (top_y - bot_y + 0.000001)
    let a_remap = remap_axes a.signed_distance x chord z
    let b_remap = remap_axes b.signed_distance x chord z
    let blend = (1 - t) * a_remap + t * b_remap
    let slab = max (x - end_pos) (start - x)
    max blend slab
end

// Linearly loft between two parallel-plane sketch loops. `a` is the
// cross-section at perpendicular-axis position `start_pos`, `b` at
// `end_pos`. Both loops must lie in parallel planes (same
// `perpendicular_axis`) — `loft` reads the axis from `a` and assumes
// `b` matches. The 3D SDF linearly interpolates the two 2D fields by
// `t` along the perpendicular axis, then intersects with the slab
// `[start_pos, end_pos]` to close the body at both caps.
//
// Output is a Field lower-bound (not exact SDF) because of the
// linear blend. Adequate for raymarching; raymarcher may need
// smaller steps near the surface for steeply-morphing cross-sections.
let loft = fun (a: Loop) (b: Loop) (start_pos: Scalar) (end_pos: Scalar) ->
    let axis = a.perpendicular_axis
    let sx = 1 - abs (compare axis 0)
    let sy = 1 - abs (compare axis 1)
    let sz = 1 - abs (compare axis 2)
    let perp = sx*x + sy*y + sz*z
    let t = (perp - start_pos) / (end_pos - start_pos + 0.000001)
    let blend = (1 - t) * a.signed_distance + t * b.signed_distance
    let slab = max (perp - end_pos) (start_pos - perp)
    max blend slab
end

// Project a sketch loop's 2D signed-distance field into 3D. Identity
// on the loop's `signed_distance` member — the loop's enclosing
// sketch plane determines which two axes the field varies along; the
// third spatial axis is unused, so the field is infinite along it
// (you'll typically intersect with a slab via `extrude` or wrap into
// a revolution via `revolve`).
let from_sketch = fun (loop: Loop) ->
    loop.signed_distance
end

// Two-body coordinate field (signed). Given two distance fields a
// and b, returns t = (a - b) / (a + b), which is -1 on the boundary
// of a, +1 on the boundary of b, and 0 at points equidistant from
// both. Smoothly varies elsewhere. The eps guard keeps the divisor
// finite when both fields hit zero at the same point.
//
// Foundation for loft / blend operations and Blake-Courter-style
// band-pattern fields, with the sign convention matching the
// `wing-remap-preview` block (-1 = leading-edge side, +1 =
// trailing-edge side).
let two_body = fun (a: Field) (b: Field) ->
    (a - b) / (a + b + 0.000001)
end

// Reflect `child` across the plane perpendicular to `axis` at `root`,
// evaluating the positive (root-side) half on both sides. `axis` is a
// 0/1/2 discrete choice (rendered as a dropdown in the BlockList);
// `root` is the position of the mirror plane along that axis.
let mirror_symmetric = fun (axis: Scalar) (root: Scalar) (child: Field) ->
    let sx = 1 - abs (compare axis 0)
    let sy = 1 - abs (compare axis 1)
    let sz = 1 - abs (compare axis 2)
    let mx = root + abs (x - root)
    let my = root + abs (y - root)
    let mz = root + abs (z - root)
    let cx = sx*mx + (1 - sx)*x
    let cy = sy*my + (1 - sy)*y
    let cz = sz*mz + (1 - sz)*z
    remap_axes child cx cy cz
end

// Sweep a closed 2D sketch loop along the axis perpendicular to its
// plane, clamping to [bottom, top]. Picks the perpendicular axis from
// the loop's `perpendicular_axis` member (0=X / 1=Y / 2=Z), which the
// compose bridge seeds from the parent sketch's plane.
let extrude = fun (loop: Loop) (bottom: Scalar) (top: Scalar) ->
    let axis = loop.perpendicular_axis
    let sx = 1 - abs (compare axis 0)
    let sy = 1 - abs (compare axis 1)
    let sz = 1 - abs (compare axis 2)
    let perp = sx*x + sy*y + sz*z
    max loop.signed_distance (max (perp - top) (bottom - perp))
end

// Sweep a closed 2D sketch loop around the in-plane "height" axis,
// generating a body of revolution. The radial loop axis (the one
// orthogonal to the height in the plane) is replaced by the radial
// distance from the height axis in 3D. Picks both axes from the
// loop's plane via `perpendicular_axis`:
//   perp=Z (XY plane) → radial=X, height=Y; radial = sqrt(x² + z²)
//   perp=Y (XZ plane) → radial=X, height=Z; radial = sqrt(x² + y²)
//   perp=X (YZ plane) → radial=Y, height=Z; radial = sqrt(x² + y²)
let revolve = fun (loop: Loop) ->
    let perp = loop.perpendicular_axis
    // Radial loop axis = the in-plane axis with the lower index.
    // perp=0 (X)   → loop axes {Y, Z}, radial = Y = 1
    // perp=1 (Y)   → loop axes {X, Z}, radial = X = 0
    // perp=2 (Z)   → loop axes {X, Y}, radial = X = 0
    let radial_axis = 1 - abs (compare perp 0)
    let px = 1 - abs (compare perp 0)
    let py = 1 - abs (compare perp 1)
    let pz = 1 - abs (compare perp 2)
    let rx = 1 - abs (compare radial_axis 0)
    let ry = 1 - abs (compare radial_axis 1)
    let perp_coord = px*x + py*y + pz*z
    let radial_coord = rx*x + ry*y
    let r = sqrt (perp_coord*perp_coord + radial_coord*radial_coord)
    let new_x = rx*r + (1 - rx)*x
    let new_y = ry*r + (1 - ry)*y
    remap_axes loop.signed_distance new_x new_y z
end

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
          NextBlockId = 8
          SelectedBlockId = Some 3
          ScriptSourceText = defaultScriptSource }
