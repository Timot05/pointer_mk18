namespace Server

// ---------------------------------------------------------------------------
// Element tree — intermediate representation between actions and field IR.
// Built from a type-checked action graph. Since typecheck guarantees all
// references are valid and type-compatible, this build cannot fail.
// ---------------------------------------------------------------------------

/// Scene element — a recursive tree of geometry operations.
type Element =
    | EEmpty
    | EFrame of RigidTransform
    | ESphere of radius: float
    | ECylinder of radius: float * height: float
    | EBox of width: float * height: float * depth: float
    | EHalfPlane of axis: string * offset: float * flip: bool
    | ETranslate of offset: Vec3 * child: Element
    | ERotate of axis: Vec3 * angleDeg: float * child: Element
    | EUnion of a: Element * b: Element * radius: float
    | ESubtract of a: Element * b: Element * radius: float
    | EIntersect of a: Element * b: Element * radius: float
    | EThicken of child: Element * amount: float
    | EShell of child: Element * thickness: float

module Element =

    /// Build element trees from a type-checked action graph.
    /// Precondition: typecheck passed (all refs valid and type-compatible).
    let build (actions: DocAction list) : Map<ActionId, Element> =
        let actionMap = actions |> List.map (fun a -> a.Id, a) |> Map.ofList

        let rec compile (id: ActionId) (built: Map<ActionId, Element>) : Element * Map<ActionId, Element> =
            match Map.tryFind id built with
            | Some elem -> elem, built
            | None ->
                match Map.tryFind id actionMap with
                | None -> EEmpty, built
                | Some action ->
                    let elem, built = compileKind action.Kind built
                    let built = Map.add id elem built
                    elem, built

        and resolveChild (id: ActionId option) (built: Map<ActionId, Element>) : Element * Map<ActionId, Element> =
            match id with
            | None -> EEmpty, built
            | Some childId -> compile childId built

        and compileKind (kind: ActionKind) (built: Map<ActionId, Element>) : Element * Map<ActionId, Element> =
            match kind with
            | Origin ->
                EFrame RigidTransform.Identity, built

            | Sphere r -> ESphere r, built
            | Cylinder(r, h) -> ECylinder(r, h), built
            | Box(w, h, d) -> EBox(w, h, d), built
            | HalfPlane(ax, off, fl) -> EHalfPlane(ax, off, fl), built
            | Sketch _ -> EEmpty, built  // TODO (Phase 2): sketch → Element

            | Translate(child, x, y, z) ->
                let childElem, built = resolveChild child built
                let offset = { X = x; Y = y; Z = z }
                match childElem with
                | EFrame t ->
                    EFrame(RigidTransform.translate offset * t), built
                | _ ->
                    ETranslate(offset, childElem), built

            | Rotate(child, ax, ay, az, angle) ->
                let childElem, built = resolveChild child built
                let axis = { X = ax; Y = ay; Z = az }
                match childElem with
                | EFrame t ->
                    EFrame(RigidTransform.fromAxisAngle axis angle * t), built
                | _ ->
                    ERotate(axis, angle, childElem), built

            | Move(child, frame) ->
                let childElem, built = resolveChild child built
                let frameElem, built = resolveChild frame built
                match frameElem with
                | EFrame t -> applyTransform t childElem, built
                | _ -> childElem, built

            | Union(a, b, r) ->
                let ea, built = resolveChild a built
                let eb, built = resolveChild b built
                EUnion(ea, eb, r), built

            | Subtract(a, b, r) ->
                let ea, built = resolveChild a built
                let eb, built = resolveChild b built
                ESubtract(ea, eb, r), built

            | Intersect(a, b, r) ->
                let ea, built = resolveChild a built
                let eb, built = resolveChild b built
                EIntersect(ea, eb, r), built

            | Thicken(child, amt) ->
                let childElem, built = resolveChild child built
                EThicken(childElem, amt), built

            | Shell(child, t) ->
                let childElem, built = resolveChild child built
                EShell(childElem, t), built

            | FromSketch(child, _, _, _) ->
                let _childElem, built = resolveChild child built
                EEmpty, built // TODO (Phase 2): sketch compilation

            | Mesh(child, _, _) ->
                let _childElem, built = resolveChild child built
                EEmpty, built // Mesh doesn't produce a field element

        and applyTransform (t: RigidTransform) (elem: Element) : Element =
            if t = RigidTransform.Identity then elem
            else
                let rotated =
                    if t.Rot = Quat.Identity then elem
                    else
                        let half = acos (min 1.0 (max -1.0 t.Rot.W))
                        let s = sin half
                        if abs s < 1e-12 then elem
                        else
                            let axis = { X = t.Rot.X / s; Y = t.Rot.Y / s; Z = t.Rot.Z / s }
                            let angleDeg = half * 2.0 * 180.0 / System.Math.PI
                            ERotate(axis, angleDeg, elem)
                if t.Trans = Vec3.Zero then rotated
                else ETranslate(t.Trans, rotated)

        let mutable built = Map.empty
        for action in actions do
            if action.Visible then
                let _, b = compile action.Id built
                built <- b
        built
