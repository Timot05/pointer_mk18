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

/// Per-action visibility in the 3D viewer. `v` cycles through the
/// modes the action's kind actually supports:
///   * Field-producing (Sphere / Box / Union / …): Hidden → Isosurface
///     → FieldLines → Hidden.
///   * Frames + sketches + origin: Hidden → Visible → Hidden. These
///     kinds use `Visible` only; `FieldLines` / `Isosurface` never
///     apply and are treated as Hidden at render time.
type ActionVisibility =
    | VHidden
    | VVisible
    | VFieldLines
    | VIsosurface

type DocAction =
    { Id: ActionId
      Name: string option
      Kind: ActionKind
      Visibility: ActionVisibility }

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
    | SelectionAllLoopsValue
    | SelectionLoopsValue of string list
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
      Actions: DocAction list                         // legacy, unused — kept for compile compat
      SelectedId: string option                       // legacy, unused
      Blocks: Server.Lang.Notebook.Block list
      NextBlockId: Server.Lang.Notebook.BlockId
      SelectedBlockId: Server.Lang.Notebook.BlockId option }

module Document =

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

    let freshActionId (kind: string) (doc: Document) : ActionId =
        let prefix = kind.ToLowerInvariant() + "_"

        let rec loop counter =
            let candidate = prefix + string counter
            if doc.Actions |> List.exists (fun action -> action.Id = candidate) then
                loop (counter + 1)
            else
                candidate

        loop 1

    let addAction (action: DocAction) (doc: Document) : Document =
        { doc with
            Actions = doc.Actions @ [ action ]
            SelectedId = Some action.Id }

    let updateAction (id: string) (updated: DocAction) (doc: Document) : Document =
        { doc with
            Actions = doc.Actions |> List.map (fun a -> if a.Id = id then updated else a) }

    let removeAction (id: string) (doc: Document) : Document =
        // When the deleted action is the selected one, move selection to
        // the action above it (or to the first remaining action if we
        // removed the topmost non-Origin row). Leaves selection alone
        // when something else was selected.
        let idx = doc.Actions |> List.tryFindIndex (fun a -> a.Id = id)
        let nextActions = doc.Actions |> List.filter (fun a -> a.Id <> id)
        let nextSelected =
            if doc.SelectedId = Some id then
                match idx with
                | Some i when i > 0 ->
                    doc.Actions |> List.tryItem (i - 1) |> Option.map (fun a -> a.Id)
                | _ ->
                    nextActions |> List.tryHead |> Option.map (fun a -> a.Id)
            else
                doc.SelectedId
        { doc with
            Actions = nextActions
            SelectedId = nextSelected }

    // ── Visibility helpers ──────────────────────────────────────────
    //
    // Visibility behaviour is driven by the action's *output* type, not
    // its kind. A `Translate` chained onto a Frame only has a gizmo to
    // show, but one chained onto a Field can render as an isosurface
    // or field-line slice — the cycle differs accordingly. Callers
    // pass the post-typecheck output type from `Compiled.TypeMap`;
    // when that's missing (pre-compile, missing refs, etc.) we fall
    // back to the binary "gizmo/hidden" toggle.

    /// Default visibility for a newly-added action — picked before the
    /// post-insert typecheck runs, so it's kind-only. `recompileState`
    /// later normalises to the correct mode once the output type is
    /// known (see `normalizeVisibility`).
    let defaultVisibility (kind: ActionKind) : ActionVisibility =
        match kind with
        | Origin | Translate _ | Rotate _ | Move _ | Sketch _ -> VVisible
        // Half-planes are infinite — an isosurface is a single boundary
        // plane and not very informative. Default to the iso-line slice
        // which shows the distance-field structure.
        | HalfPlane _ -> VFieldLines
        | _ -> VIsosurface

    /// Cycle order on `v`. Field outputs normally rotate through three
    /// modes (Hidden → Isosurface → FieldLines → Hidden); HalfPlane
    /// skips isosurface (not useful for an infinite primitive) and
    /// toggles Hidden ↔ FieldLines. Non-field kinds (Frame / Sketch /
    /// Mesh) are a binary Hidden ↔ Visible toggle.
    let cycleVisibility (kind: ActionKind) (isFieldOutput: bool) (current: ActionVisibility) : ActionVisibility =
        match kind with
        | HalfPlane _ ->
            match current with
            | VHidden -> VFieldLines
            | _ -> VHidden
        | _ when isFieldOutput ->
            match current with
            | VHidden -> VIsosurface
            | VIsosurface -> VFieldLines
            | VFieldLines | VVisible -> VHidden
        | _ ->
            match current with
            | VHidden -> VVisible
            | _ -> VHidden

    /// Snap visibility to a mode valid for the given output type. Used
    /// after recompile so a Translate that used to wrap a field and
    /// had `VIsosurface` drops to `VVisible` when its child is rewired
    /// to a frame (and vice versa). HalfPlanes additionally coerce
    /// Isosurface/Visible to FieldLines — the isosurface mode isn't
    /// exposed through the cycle so any stale value is an invalid state.
    let normalizeVisibility (kind: ActionKind) (isFieldOutput: bool) (current: ActionVisibility) : ActionVisibility =
        match kind with
        | HalfPlane _ ->
            match current with
            | VHidden -> VHidden
            | _ -> VFieldLines
        | _ when isFieldOutput ->
            match current with
            | VVisible -> VIsosurface
            | _ -> current
        | _ ->
            match current with
            | VIsosurface | VFieldLines -> VVisible
            | _ -> current

    let setVisibility (id: ActionId) (visibility: ActionVisibility) (doc: Document) : Document =
        { doc with
            Actions =
                doc.Actions
                |> List.map (fun a ->
                    if a.Id = id then { a with Visibility = visibility } else a) }

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

    let private patchFromSketchSelection _current =
        function
        | SelectionAllLoopsValue -> SelectionAllLoops
        | SelectionLoopsValue loopIds -> SelectionLoops loopIds
        | SelectionElementsValue lineIds -> SelectionElements lineIds

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
                                    let stringList field =
                                        ParamValue.tryField field value
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
                                    match ParamValue.tryField "case" value |> Option.bind ParamValue.asString with
                                    | Some "SelectionElements" ->
                                        patchFromSketchSelection sel (SelectionElementsValue(stringList "lineIds"))
                                    | Some "SelectionLoops" ->
                                        patchFromSketchSelection sel (SelectionLoopsValue(stringList "loopIds"))
                                    | Some "SelectionAllLoops" ->
                                        patchFromSketchSelection sel SelectionAllLoopsValue
                                    | _ ->
                                        // Unknown / legacy payload — fall back to "all".
                                        patchFromSketchSelection sel SelectionAllLoopsValue
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

    /// Build an action with default visibility derived from its kind.
    let private act id name kind =
        { Id = id
          Name = Some name
          Kind = kind
          Visibility = defaultVisibility kind }

    /// Same but with the Name = None (anonymous action).
    let private anon id kind =
        { Id = id
          Name = None
          Kind = kind
          Visibility = defaultVisibility kind }

    /// Same, but forces a specific visibility (used for the stress doc
    /// where intermediate surfaces stay hidden and only the final three
    /// show as isosurfaces).
    let private actWith id name kind visibility =
        { Id = id
          Name = Some name
          Kind = kind
          Visibility = visibility }

    let defaultDocument () : Document =
        { Name = "untitled"
          SelectedId = Some "origin"
          Blocks = []
          NextBlockId = 0
          SelectedBlockId = None
          Actions =
            [ act "origin" "origin" Origin
              actWith "cyl1" "cylinder" (Cylinder(radius = 10.0, height = 40.0)) VHidden
              actWith "sph1" "sphere" (Sphere(radius = 8.0)) VHidden
              actWith "sub1" "subtract" (Subtract(a = Some "cyl1", b = Some "sph1", radius = 0.0)) VHidden
              act "sketch1" "square"
                  (Sketch(
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
                                Distance("p_bl", "p_tl", 10.0, None) ] }))
              act "frame1" "frame" (Translate(child = Some "origin", x = 18.0, y = 6.0, z = 12.0))
              act "from1" "from-sketch" (FromSketch(child = Some "sketch1", flip = false, selection = SelectionAllLoops)) ] }

    /// Boot-time notebook: a two-block demo that exercises both the
    /// `@output` → `@input` cross-block wire and the `@view` builtin. The
    /// canvas stays empty until the user clicks Run; the seed just gives
    /// them something to read and tweak.
    let private defaultBlocks : Server.Lang.Notebook.Block list =
        [ { Id = 0
            Name = "params"
            Kind = Server.Lang.Notebook.ScriptBlock {
                Source = "@output(\"r\", 1.0)"
                Inputs = []
            } }
          { Id = 1
            Name = "shape"
            Kind = Server.Lang.Notebook.ScriptBlock {
                Source = "@view(@sphere(@input(\"radius\")))"
                Inputs = [ "radius", "params" ]
            } } ]

    let emptyDocument () : Document =
        { Name = "untitled"
          SelectedId = Some "origin"
          Blocks = defaultBlocks
          NextBlockId = 2
          SelectedBlockId = None
          Actions = [ act "origin" "origin" Origin ] }

    // Stress document — extends the default doc with three distinct
    // display-enabled surfaces so the viewer exercises both:
    //   * per-block alive-mask pruning (each block typically covers only
    //     one of the three surfaces),
    //   * tight per-block tStart / tEnd from the raymarch probes (the
    //     three surfaces are spatially separated so most blocks start
    //     deep into the scene).
    //
    // NOTE: the Zig voxel kernel has `MAX_TAPE = 1024` and `simplify`'s
    // out-buffer is sized to MAX_TAPE, so the lowered tape must stay well
    // under that limit (transient constants can briefly double the op
    // count). Each surface here ends with its own final action — the
    // kernel lowers each independently so they don't share a tape budget.
    let stressDocument () : Document =
        let baseDoc = defaultDocument ()

        // All intermediate stress geometry is hidden; only the final
        // actions (gridFinal / ringFinal / slabFinal) are flipped to
        // VIsosurface below. Frames in this doc are throwaway structure
        // — hide their gizmos too to keep the viewport clean.
        let mk id name kind =
            { Id = id
              Name = Some name
              Kind = kind
              Visibility = VHidden }

        // Fold a list of IDs into a binary chain of smooth unions. Returns
        // the created union actions plus the last union's ID (= the root
        // of the chain). Shared by all three surfaces below.
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

        // ── Surface 1: 3×3×3 grid of spheres (warm), centered left ─────
        let gridN = 3
        let gridSpacing = 4.5
        let gridSphereR = 1.8
        let gridSmoothR = 0.8
        let gridCenterOff = float (gridN - 1) * gridSpacing * 0.5

        let gridCells =
            [ for i in 0 .. gridN - 1 do
                for j in 0 .. gridN - 1 do
                    for k in 0 .. gridN - 1 do
                        yield i, j, k ]

        let gridSphereId (i, j, k) = sprintf "gsph_%d_%d_%d" i j k

        let gridSphereActions =
            gridCells
            |> List.collect (fun (i, j, k) ->
                let sid = sprintf "gsrc_%d_%d_%d" i j k
                let x = float i * gridSpacing - gridCenterOff
                let y = float j * gridSpacing - gridCenterOff
                let z = float k * gridSpacing - gridCenterOff
                [ mk sid "sph" (Sphere(radius = gridSphereR))
                  mk (gridSphereId (i, j, k)) "tsph" (Translate(child = Some sid, x = x, y = y, z = z)) ])

        let gridUnionActions, gridRootId =
            gridCells |> List.map gridSphereId |> chainUnions "gu" gridSmoothR

        let gridFinal =
            gridRootId
            |> Option.map (fun rid ->
                { mk "s1_final" "sphere grid" (Translate(child = Some rid, x = -14.0, y = 0.0, z = 0.0))
                    with Visibility = VIsosurface })

        // ── Surface 2: ring of cylinders (cool), center ────────────────
        let ringN = 8
        let ringRadius = 6.0
        let ringCylR = 0.9
        let ringCylH = 5.0
        let ringCylId i = sprintf "rcyl_%d" i

        let ringActions =
            [ 0 .. ringN - 1 ]
            |> List.collect (fun i ->
                let a = 2.0 * System.Math.PI * float i / float ringN
                let x = cos a * ringRadius
                let z = sin a * ringRadius
                let srcId = sprintf "rcyl_src_%d" i
                [ mk srcId "cyl" (Cylinder(radius = ringCylR, height = ringCylH))
                  mk (ringCylId i) "tcyl" (Translate(child = Some srcId, x = x, y = 0.0, z = z)) ])

        let ringUnionActions, ringRootId =
            [ 0 .. ringN - 1 ] |> List.map ringCylId |> chainUnions "ru" 0.6

        let ringFinal =
            ringRootId
            |> Option.map (fun rid ->
                { mk "s2_final" "ring" (Translate(child = Some rid, x = 0.0, y = -8.0, z = 0.0))
                    with Visibility = VIsosurface })

        // ── Surface 3: perforated slab (green), centered right ─────────
        // Big box with 4 cylindrical holes drilled through — exercises
        // interval evaluation for Subtract + Box + Cylinder and gives the
        // alive-mask a spatial reason to turn this bit off outside the
        // slab's screen region.
        let slabAction = mk "slab_box" "slab" (Box(width = 7.0, height = 4.0, depth = 7.0))
        let holeActions =
            [ -2.0, -2.0; 2.0, -2.0; -2.0, 2.0; 2.0, 2.0 ]
            |> List.mapi (fun i (x, z) ->
                let srcId = sprintf "slab_hole_src_%d" i
                let tId = sprintf "slab_hole_%d" i
                [ mk srcId "hole" (Cylinder(radius = 1.0, height = 6.0))
                  mk tId "thole" (Translate(child = Some srcId, x = x, y = 0.0, z = z)) ])
            |> List.concat

        let holeIds = [ for i in 0 .. 3 -> sprintf "slab_hole_%d" i ]
        let holeUnionActions, holeUnionRootId = chainUnions "slab_hu" 0.2 holeIds

        let slabSubtractActions =
            match holeUnionRootId with
            | Some hRoot ->
                [ mk "slab_sub" "slab - holes"
                    (Subtract(a = Some "slab_box", b = Some hRoot, radius = 0.3)) ]
            | None -> []

        let slabRootId =
            match holeUnionRootId with
            | Some _ -> Some "slab_sub"
            | None -> Some "slab_box"

        let slabFinal =
            slabRootId
            |> Option.map (fun rid ->
                { mk "s3_final" "slab" (Translate(child = Some rid, x = 14.0, y = 0.0, z = 0.0))
                    with Visibility = VIsosurface })

        // ── Assemble ───────────────────────────────────────────────────
        let finals = List.choose id [ gridFinal; ringFinal; slabFinal ]
        let selectedId =
            finals |> List.tryHead |> Option.map (fun a -> a.Id) |> Option.defaultValue "origin"

        let extras =
            gridSphereActions @ gridUnionActions @ (Option.toList gridFinal)
            @ ringActions @ ringUnionActions @ (Option.toList ringFinal)
            @ [ slabAction ] @ holeActions @ holeUnionActions @ slabSubtractActions @ (Option.toList slabFinal)

        { baseDoc with
            Actions = baseDoc.Actions @ extras
            SelectedId = Some selectedId }
