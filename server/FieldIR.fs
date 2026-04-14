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

/// GPU-ready field node. Slot indices point into the SlotTable values array.
type FieldNode =
    | FPrimitive of prim: Primitive
    | FTranslate of x: Slot * y: Slot * z: Slot * child: FieldNode
    | FRotate of ax: Slot * ay: Slot * az: Slot * angle: Slot * child: FieldNode
    | FBoolean of op: BooleanOp * radius: Slot * a: FieldNode * b: FieldNode
    | FFieldOp of op: UnaryFieldOp * value: Slot * child: FieldNode

type FieldSurface =
    { ActionId: ActionId
      Field: FieldNode }

module FieldCompile =

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

    /// Compile each visible action's element tree into a FieldSurface.
    /// Slots are allocated into the provided builder. Skipped (None) if the
    /// element produces no field (e.g. Empty, Mesh, Frame-only chains).
    let compile (actions: DocAction list) (elements: Map<ActionId, Element>) (b: SlotTable.Builder) : FieldSurface list =
        actions
        |> List.choose (fun action ->
            if not action.Visible then None
            else
                Map.tryFind action.Id elements
                |> Option.bind (compileElement b)
                |> Option.map (fun field -> { ActionId = action.Id; Field = field }))
