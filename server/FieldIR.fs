namespace Server

// ---------------------------------------------------------------------------
// Field IR — GPU-ready signed distance field representation.
// Transforms are baked into leaf primitives as rigid transforms.
// ---------------------------------------------------------------------------

type Primitive =
    | PrimSphere of radius: float
    | PrimCylinder of radius: float * height: float
    | PrimBox of width: float * height: float * depth: float
    | PrimHalfPlane of axis: string * offset: float * flip: bool

type BooleanOp = BoolUnion | BoolSubtract | BoolIntersect

type UnaryFieldOp = OpThicken | OpShell

/// GPU-ready field node. Leaf primitives carry their full world→local transform.
type FieldNode =
    | FPrimitive of prim: Primitive * transform: RigidTransform
    | FBoolean of op: BooleanOp * radius: float * a: FieldNode * b: FieldNode
    | FFieldOp of op: UnaryFieldOp * value: float * child: FieldNode

type FieldSurface =
    { ActionId: ActionId
      Field: FieldNode }

module FieldCompile =

    /// Compile an Element tree into a FieldNode, accumulating rigid transforms.
    let rec private compileElement (parentTransform: RigidTransform) (elem: Element) : FieldNode option =
        match elem with
        | EEmpty | EFrame _ ->
            None

        | ESphere r ->
            Some (FPrimitive(PrimSphere r, parentTransform))

        | ECylinder(r, h) ->
            Some (FPrimitive(PrimCylinder(r, h), parentTransform))

        | EBox(w, h, d) ->
            Some (FPrimitive(PrimBox(w, h, d), parentTransform))

        | EHalfPlane(ax, off, fl) ->
            Some (FPrimitive(PrimHalfPlane(ax, off, fl), parentTransform))

        | ETranslate(offset, child) ->
            let t = parentTransform * RigidTransform.translate offset
            compileElement t child

        | ERotate(axis, angleDeg, child) ->
            let t = parentTransform * RigidTransform.fromAxisAngle axis angleDeg
            compileElement t child

        | EUnion(a, b, r) ->
            match compileElement parentTransform a, compileElement parentTransform b with
            | Some fa, Some fb -> Some (FBoolean(BoolUnion, r, fa, fb))
            | Some fa, None -> Some fa
            | None, Some fb -> Some fb
            | None, None -> None

        | ESubtract(a, b, r) ->
            // Subtract: a minus b. Convention in SDF: max(a, -b)
            match compileElement parentTransform a, compileElement parentTransform b with
            | Some fa, Some fb -> Some (FBoolean(BoolSubtract, r, fa, fb))
            | Some fa, None -> Some fa
            | _ -> None

        | EIntersect(a, b, r) ->
            match compileElement parentTransform a, compileElement parentTransform b with
            | Some fa, Some fb -> Some (FBoolean(BoolIntersect, r, fa, fb))
            | _ -> None

        | EThicken(child, amt) ->
            compileElement parentTransform child
            |> Option.map (fun fc -> FFieldOp(OpThicken, amt, fc))

        | EShell(child, t) ->
            compileElement parentTransform child
            |> Option.map (fun fc -> FFieldOp(OpShell, t, fc))

    /// Compile the full action graph into field surfaces.
    /// Returns a FieldSurface for each visible action that produces a field.
    let compile (actions: DocAction list) (elements: Map<ActionId, Element>) : FieldSurface list =
        actions
        |> List.choose (fun action ->
            if not action.Visible then None
            else
                Map.tryFind action.Id elements
                |> Option.bind (compileElement RigidTransform.Identity)
                |> Option.map (fun field -> { ActionId = action.Id; Field = field }))
