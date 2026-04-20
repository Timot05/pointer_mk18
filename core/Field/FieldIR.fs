namespace Server

// ---------------------------------------------------------------------------
// Field IR — GPU-ready signed distance field representation.
//
// Slot-based: every numeric value is a slot index into a SlotTable. Transforms
// are NOT baked into primitives; they remain in the tree as FTranslate / FRotate
// nodes. The shader reads slots from a uniform buffer and composes transforms
// on the GPU.
// ---------------------------------------------------------------------------

type Primitive =
    | PrimSphere of radius: Slot
    | PrimCylinder of radius: Slot * height: Slot
    | PrimBox of width: Slot * height: Slot * depth: Slot
    | PrimHalfPlane of axis: string * offset: Slot * flip: bool

type BooleanOp = BoolUnion | BoolSubtract | BoolIntersect

type UnaryFieldOp = OpThicken | OpShell

/// 2D point expressed as two slot indices.
type SlotPt2 = { XSlot: Slot; YSlot: Slot }

/// Slot-backed 2D primitive used by FSketch. Only ArcCenter is supported;
/// ArcThreePoint is authoring-only and must be converted before persist.
type SketchPrimitive2d =
    | SpLineSegment of startP: SlotPt2 * endP: SlotPt2
    | SpCircle of center: SlotPt2 * radiusSlot: Slot
    | SpArcCenter of startP: SlotPt2 * endP: SlotPt2 * center: SlotPt2 * clockwise: bool

type Sketch2d =
    { Primitives: SketchPrimitive2d list
      Closed: bool
      Flip: bool }

/// GPU-ready field node. Slot indices point into the SlotTable values array.
type FieldNode =
    | FPrimitive of prim: Primitive
    | FTranslate of x: Slot * y: Slot * z: Slot * child: FieldNode
    | FRotate of ax: Slot * ay: Slot * az: Slot * angle: Slot * child: FieldNode
    | FBoolean of op: BooleanOp * radius: Slot * a: FieldNode * b: FieldNode
    | FFieldOp of op: UnaryFieldOp * value: Slot * child: FieldNode
    | FSketch of sketch: Sketch2d

type FieldSurface =
    { ActionId: ActionId
      Field: FieldNode }

module FieldCompile =

    /// Look up a sketch entity by id and produce a SlotPt2 for its coords.
    /// Uses the sketch slot keys already allocated by Pipeline.allocSketchSlots,
    /// so these calls are idempotent and return the same slot indices.
    let private slotPtForPoint
        (b: SlotTable.Builder)
        (sketchActionId: ActionId)
        (pointId: string)
        (x: float)
        (y: float)
        : SlotPt2 =
        let xSlot = SlotTable.alloc b { ActionId = sketchActionId; Path = sprintf "sketch.entity.%s.x" pointId } x
        let ySlot = SlotTable.alloc b { ActionId = sketchActionId; Path = sprintf "sketch.entity.%s.y" pointId } y
        { XSlot = xSlot; YSlot = ySlot }

    let private entityId =
        function
        | REPoint(id, _, _) -> id
        | RELine(id, _, _) -> id
        | RECircle(id, _, _) -> id
        | REArc(id, _, _, _) -> id

    /// Convert a RenderEntity (Line/Circle/Arc) to a SketchPrimitive2d.
    /// Point lookups reference the already-allocated sketch point slots.
    let private entityToPrimitive
        (b: SlotTable.Builder)
        (sketchActionId: ActionId)
        (entityMap: Map<string, RenderEntity>)
        (entity: RenderEntity)
        : SketchPrimitive2d option =
        let pt (pointId: string) : SlotPt2 option =
            match Map.tryFind pointId entityMap with
            | Some (REPoint(_, x, y)) -> Some (slotPtForPoint b sketchActionId pointId x y)
            | _ -> None

        match entity with
        | RELine(_, startId, endId) ->
            match pt startId, pt endId with
            | Some s, Some e -> Some (SpLineSegment(s, e))
            | _ -> None

        | RECircle(id, centerId, radius) ->
            match pt centerId with
            | Some c ->
                let rSlot = SlotTable.alloc b { ActionId = sketchActionId; Path = sprintf "sketch.entity.%s.radius" id } radius
                Some (SpCircle(c, rSlot))
            | None -> None

        | REArc(_, startId, endId, ArcCenter(centerId, cw)) ->
            match pt startId, pt endId, pt centerId with
            | Some s, Some e, Some c -> Some (SpArcCenter(s, e, c, cw))
            | _ -> None

        | REArc(_, _, _, ArcThreePoint _) ->
            // Authoring-only. Skip at render time.
            None

        | REPoint _ -> None

    /// Pick the ordered list of entity ids to trace for a FromSketch selection.
    let private selectEntityIds (sketch: ActionSketch) (selection: FromSketchSelection) : string list =
        match selection with
        | SelectionElements lineIds -> lineIds
        | SelectionLoop loopId ->
            let loops = SketchLoops.detectLoops sketch.Entities
            match loopId with
            | Some id ->
                loops
                |> List.tryFind (fun l -> l.Id = id)
                |> Option.map (fun l -> l.EntityIds)
                |> Option.defaultValue []
            | None ->
                match loops with
                | first :: _ -> first.EntityIds
                | [] -> []

    /// Walks an Element tree, allocating slots as it goes, producing a
    /// FieldNode. Every numeric value on an Element node becomes a slot
    /// keyed by (ActionId, path).
    let rec private compileElement (b: SlotTable.Builder) (elem: Element) : FieldNode option =
        let slot actionId path value =
            SlotTable.alloc b { ActionId = actionId; Path = path } value

        match elem with
        | EEmpty -> None

        | ESphere(id, r) ->
            Some (FPrimitive(PrimSphere(slot id "radius" r)))

        | ECylinder(id, r, h) ->
            Some (FPrimitive(PrimCylinder(slot id "radius" r, slot id "height" h)))

        | EBox(id, w, h, d) ->
            Some (FPrimitive(PrimBox(slot id "width" w, slot id "height" h, slot id "depth" d)))

        | EHalfPlane(id, axis, off, flip) ->
            Some (FPrimitive(PrimHalfPlane(axis, slot id "offset" off, flip)))

        | ETranslate(id, x, y, z, child) ->
            match compileElement b child with
            | None -> None
            | Some fc ->
                Some (FTranslate(
                    slot id "x" x,
                    slot id "y" y,
                    slot id "z" z,
                    fc))

        | ERotate(id, ax, ay, az, angle, child) ->
            match compileElement b child with
            | None -> None
            | Some fc ->
                Some (FRotate(
                    slot id "ax" ax,
                    slot id "ay" ay,
                    slot id "az" az,
                    slot id "angle" angle,
                    fc))

        | EUnion(id, a, b', r) ->
            match compileElement b a, compileElement b b' with
            | Some fa, Some fb -> Some (FBoolean(BoolUnion, slot id "radius" r, fa, fb))
            | Some fa, None -> Some fa
            | None, Some fb -> Some fb
            | None, None -> None

        | ESubtract(id, a, b', r) ->
            match compileElement b a, compileElement b b' with
            | Some fa, Some fb -> Some (FBoolean(BoolSubtract, slot id "radius" r, fa, fb))
            | Some fa, None -> Some fa
            | _ -> None

        | EIntersect(id, a, b', r) ->
            match compileElement b a, compileElement b b' with
            | Some fa, Some fb -> Some (FBoolean(BoolIntersect, slot id "radius" r, fa, fb))
            | _ -> None

        | EThicken(id, child, amt) ->
            compileElement b child
            |> Option.map (fun fc -> FFieldOp(OpThicken, slot id "amount" amt, fc))

        | EShell(id, child, t) ->
            compileElement b child
            |> Option.map (fun fc -> FFieldOp(OpShell, slot id "thickness" t, fc))

        | EFromSketch(_, sketchActionId, sketch, selection, flip) ->
            let entityMap =
                sketch.Entities
                |> List.map (fun e -> entityId e, e)
                |> Map.ofList
            let ids = selectEntityIds sketch selection
            let prims =
                ids
                |> List.choose (fun eid ->
                    Map.tryFind eid entityMap
                    |> Option.bind (entityToPrimitive b sketchActionId entityMap))
            if prims.IsEmpty then None
            else Some (FSketch { Primitives = prims; Closed = true; Flip = flip })

    /// Compile each visible action's element tree into a FieldSurface.
    /// Slots are allocated into the provided builder. Skipped (None) if the
    /// element produces no field (e.g. Empty, Mesh, Frame-only chains).
    let compile (actions: DocAction list) (elements: Map<ActionId, Element>) (b: SlotTable.Builder) : FieldSurface list =
        actions
        |> List.choose (fun action ->
            Map.tryFind action.Id elements
            |> Option.bind (compileElement b)
            |> Option.map (fun field -> { ActionId = action.Id; Field = field }))
