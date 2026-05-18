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

    /// Both wing guide curves bundled into one sketch. Two lines:
    /// `line_0` is the leading edge, `line_1` is the trailing edge.
    /// Top-level primitives are exposed on the sketch refinement, so
    /// downstream blocks can wire `wing_guides.line_0` / `.line_1`
    /// directly without splitting them into separate sketch blocks.
    let private wingGuidesSketch : ActionSketch =
        { Entities =
            [ REPoint("le_root", 0.0, 0.0)
              REPoint("le_tip", 0.25, 5.0)
              REPoint("te_root", 2.0, 0.0)
              REPoint("te_tip", 1.25, 5.0)
              RELine("leading", "le_root", "le_tip")
              RELine("trailing", "te_root", "te_tip") ]
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
                        [ "leading", Server.Lang.AstBuilder.pathE [ "wing_guides"; "line_0" ]
                          "trailing", Server.Lang.AstBuilder.pathE [ "wing_guides"; "line_1" ] ])
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
          // Loft demo: square cross-section at z=0 morphing into a
          // circle at z=3. Positioned off to the right (x ~ 5) so it
          // doesn't overlap with the wing.
          { Id = 4
            Name = "loft_square"
            Body = Server.Lang.Notebook.SketchBody
                { Sketch = squareSketch 5.0 0.0 1.0
                  Plane = XY }
            Visibility = Server.Lang.Notebook.VHidden
            ColorIndex = 0
            SlicePlane = Server.Lang.Notebook.defaultSlicePlane }
          { Id = 5
            Name = "loft_circle"
            Body = Server.Lang.Notebook.SketchBody
                { Sketch = circleSketch 5.0 0.0 1.0
                  Plane = XY }
            Visibility = Server.Lang.Notebook.VHidden
            ColorIndex = 0
            SlicePlane = Server.Lang.Notebook.defaultSlicePlane }
          { Id = 6
            Name = "loft_demo"
            Body =
                Server.Lang.Notebook.NativeBody(
                    "loft",
                    Map.ofList
                        [ "a", Server.Lang.AstBuilder.pathE [ "loft_square"; "loop_0" ]
                          "b", Server.Lang.AstBuilder.pathE [ "loft_circle"; "loop_0" ]
                          "start_pos", Server.Lang.AstBuilder.numE 0.0
                          "end_pos", Server.Lang.AstBuilder.numE 3.0 ])
            Visibility = Server.Lang.Notebook.VIsosurface
            ColorIndex = 1
            SlicePlane =
                { Server.Lang.Notebook.defaultSlicePlane with
                    Origin = { X = 5.0; Y = 0.0; Z = 1.5 } } } ]

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
          NextBlockId = 7
          SelectedBlockId = Some 3
          ScriptSourceText = defaultScriptSource }
