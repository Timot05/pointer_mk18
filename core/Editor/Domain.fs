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

// Cubic Bezier evaluation at parameter t (Bernstein form). The
// control points are scalar constants (primitive fields), but
// `t` is a Field because callers feed in a world-axis-derived
// value (e.g. `x` or `y`) which is itself Field-typed in this DSL.
let bezier_cubic = fun (p0: Scalar) (p1: Scalar) (p2: Scalar) (p3: Scalar) (t: Field) ->
    let u = 1 - t
    u*u*u * p0 + 3*u*u*t * p1 + 3*u*t*t * p2 + t*t*t * p3
end

// First derivative of the cubic Bezier wrt t. Itself a quadratic
// Bezier of the velocity vectors (p_{i+1} - p_i).
let bezier_cubic_deriv = fun (p0: Scalar) (p1: Scalar) (p2: Scalar) (p3: Scalar) (t: Field) ->
    let u = 1 - t
    3 * (u*u*(p1 - p0) + 2*u*t*(p2 - p1) + t*t*(p3 - p2))
end

// Find t such that bezier_cubic(p0..p3, t) = target. Three unrolled
// Newton steps starting from the linear (chord-line) initial guess.
// Used by wing_loft / body_loft to invert the guide X(t) (or Y(t))
// so the loft evaluates the spline at the correct parameter for each
// world coordinate, instead of the linear-t approximation which
// drifts when control points aren't placed at the canonical thirds.
//
// PRECONDITION: the X(t) (or Y(t)) component must be monotonic over
// [0,1]. If the curve folds back on itself, Newton can converge to
// any of the multiple roots depending on initial guess. Authoring
// guides nose → tail with monotonically increasing control points
// satisfies this.
let bezier_invert = fun (p0: Scalar) (p1: Scalar) (p2: Scalar) (p3: Scalar) (target: Field) ->
    let t0 = (target - p0) / (p3 - p0 + 0.000001)
    let f0 = bezier_cubic p0 p1 p2 p3 t0 - target
    let df0 = bezier_cubic_deriv p0 p1 p2 p3 t0
    let t1 = t0 - f0 / (df0 + 0.000001)
    let f1 = bezier_cubic p0 p1 p2 p3 t1 - target
    let df1 = bezier_cubic_deriv p0 p1 p2 p3 t1
    let t2 = t1 - f1 / (df1 + 0.000001)
    let f2 = bezier_cubic p0 p1 p2 p3 t2 - target
    let df2 = bezier_cubic_deriv p0 p1 p2 p3 t2
    t2 - f2 / (df2 + 0.000001)
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
// Each guide spline is inverted via Newton (see `bezier_invert`) to
// recover the parameter t where the curve hits world y, then the
// X-component is evaluated at that t. This corrects the previous
// linear-t-from-y approximation which only matches the visual
// spline when the Y control points sit at the canonical thirds.
// Path access of each guide field stays at this call site so the
// typecheck's refinement cell still grows the required members.
let wing_loft = fun (a: Loop) (b: Loop) (g_left: Primitive) (g_right: Primitive) (start: Scalar) (end_pos: Scalar) ->
    let t = (y - start) / (end_pos - start + 0.000001)
    let t_l = bezier_invert g_left.y0 g_left.cy0 g_left.cy1 g_left.y1 y
    let left_x = bezier_cubic g_left.x0 g_left.cx0 g_left.cx1 g_left.x1 t_l
    let t_r = bezier_invert g_right.y0 g_right.cy0 g_right.cy1 g_right.y1 y
    let right_x = bezier_cubic g_right.x0 g_right.cx0 g_right.cx1 g_right.x1 t_r
    let s_start = g_right.x0 - g_left.x0
    let center = (right_x + left_x) / 2
    let chord = (x - center) * s_start / (right_x - left_x + 0.000001)
    let a_remap = remap_axes a.signed_distance chord y z
    let b_remap = remap_axes b.signed_distance chord y z
    let blend = (1 - t) * a_remap + t * b_remap
    let slab = max (y - end_pos) (start - y)
    max blend slab
end

// Aircraft-fuselage loft — body axis = world X (nose → tail).
// Cross-sections `a` and `b` live in the YZ plane (their local X =
// world Y = body width, their local Y = world Z = body height).
// Guides `g_top` / `g_bot` live in the XZ plane (SIDE VIEW): their
// local X = world X = body length, their local Y = world Z = body
// half-height. The guides therefore control the body's HEIGHT
// profile along its length; the cross-sections supply the width.
//
// Height convention: at `start`, the cross-section sits at its
// natural absolute Z extent — its world-Z edges coincide with the
// spline endpoints `g_top.y0` and `g_bot.y0` (sketch-local Y =
// world Z). As X moves nose-to-tail the splines stretch or shrink
// the cross-section vertically by the ratio `S(x) / S(start)`. So
// if the splines are at ±0.25 at the nose, the cross-section is
// naturally 0.5 tall; the body expands where the guides bow out
// and contracts where they taper back in.
let body_loft = fun (a: Loop) (b: Loop) (g_top: Primitive) (g_bot: Primitive) (start: Scalar) (end_pos: Scalar) ->
    let t = (x - start) / (end_pos - start + 0.000001)
    // Invert each guide's X(t) to find where the spline reaches the
    // current world x, then evaluate the spline's Y-component
    // (= world Z, since guides are in XZ) at that parameter. See
    // `bezier_invert` for the monotonicity precondition.
    let t_top = bezier_invert g_top.x0 g_top.cx0 g_top.cx1 g_top.x1 x
    let top_z = bezier_cubic g_top.y0 g_top.cy0 g_top.cy1 g_top.y1 t_top
    let t_bot = bezier_invert g_bot.x0 g_bot.cx0 g_bot.cx1 g_bot.x1 x
    let bot_z = bezier_cubic g_bot.y0 g_bot.cy0 g_bot.cy1 g_bot.y1 t_bot
    let s_start = g_top.y0 - g_bot.y0
    let center = (top_z + bot_z) / 2
    // Isotropic scaling: the single XZ guide pair drives the body's
    // overall scale at each station. Sampling Y and (Z - center) at
    // the same inverse-scale keeps the cross-section's aspect ratio
    // (a circle stays a circle, just bigger/smaller).
    let inv_scale = s_start / (top_z - bot_z + 0.000001)
    let y_scaled = y * inv_scale
    let z_scaled = (z - center) * inv_scale
    let a_remap = remap_axes a.signed_distance x y_scaled z_scaled
    let b_remap = remap_axes b.signed_distance x y_scaled z_scaled
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

// Reflect `child` across the plane perpendicular to `axis` at `root`.
// Evaluates `child` at both the + and - reflection of the sample
// point and takes the SDF union (min) so the result is symmetric
// regardless of which side of `root` the child originally lives on.
// `axis` is a 0/1/2 discrete choice (rendered as a dropdown in the
// BlockList); `root` is the position of the mirror plane along that
// axis.
let mirror_symmetric = fun (axis: Scalar) (root: Scalar) (child: Field) ->
    let sx = 1 - abs (compare axis 0)
    let sy = 1 - abs (compare axis 1)
    let sz = 1 - abs (compare axis 2)
    let mx_pos = root + abs (x - root)
    let my_pos = root + abs (y - root)
    let mz_pos = root + abs (z - root)
    let mx_neg = root - abs (x - root)
    let my_neg = root - abs (y - root)
    let mz_neg = root - abs (z - root)
    let cx_pos = sx*mx_pos + (1 - sx)*x
    let cy_pos = sy*my_pos + (1 - sy)*y
    let cz_pos = sz*mz_pos + (1 - sz)*z
    let cx_neg = sx*mx_neg + (1 - sx)*x
    let cy_neg = sy*my_neg + (1 - sy)*y
    let cz_neg = sz*mz_neg + (1 - sz)*z
    let pos_eval = remap_axes child cx_pos cy_pos cz_pos
    let neg_eval = remap_axes child cx_neg cy_neg cz_neg
    min pos_eval neg_eval
end

// Swap world Y and Z when sampling `child` — turns a Y-spanning
// field (e.g. a `wing-remap-preview` wing) into a Z-spanning field
// (e.g. a vertical stabilizer / fin). The child's chord (X) stays
// horizontal; what was its span direction now extends upward, and
// what was its thickness direction now extends sideways.
let swap_yz = fun (child: Field) ->
    remap_axes child x z y
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

    /// Minimal empty document — no blocks, default user-script library.
    /// Returned when the bundled boot example is missing or fails to
    /// decode; the user can still author from scratch since the
    /// script library is intact and the Examples dropdown remains
    /// available.
    let private emptyFallbackDocument () : Document =
        { Name = "untitled"
          Blocks = []
          NextBlockId = 0
          SelectedBlockId = None
          ScriptSourceText = defaultScriptSource }

#if FABLE_COMPILER
    /// Raw JSON text bundled by Vite — the boot document. Points at
    /// `ui/defaults/examples/basic.json` which doubles as a selectable
    /// entry under the Examples top-bar dropdown, so editing or
    /// replacing it changes both the boot default and that menu
    /// entry in lockstep.
    let private bootDocumentJson : string =
        Fable.Core.JsInterop.importDefault "@defaults/examples/basic.json?raw"
#else
    let private bootDocumentJson : string = ""
#endif

    let emptyDocument () : Document =
#if FABLE_COMPILER
        let trimmed = bootDocumentJson.Trim()
        if trimmed = "" || trimmed = "null" then
            emptyFallbackDocument ()
        else
            match Thoth.Json.Decode.Auto.fromString<Document>(bootDocumentJson) with
            | Ok doc -> doc
            | Error msg ->
                Browser.Dom.console.warn ("basic.json failed to decode; using empty document. " + msg)
                emptyFallbackDocument ()
#else
        emptyFallbackDocument ()
#endif
