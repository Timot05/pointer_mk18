namespace Server

// ---------------------------------------------------------------------------
// Type-checking the action graph
//
// Walks the action list top-to-bottom, resolves references, checks that
// input/output types are compatible, and produces a typed graph or a list
// of located errors.
// ---------------------------------------------------------------------------

[<RequireQualifiedAccess>]
type FieldType =
    | Field
    | Sketch
    | Frame
    | Mesh

type TypeError =
    | MissingRef of actionId: ActionId * key: string
    | RefNotFound of actionId: ActionId * key: string * target: ActionId
    | ForwardRef of actionId: ActionId * key: string * target: ActionId
    | TypeMismatch of actionId: ActionId * key: string * expected: FieldType list * got: FieldType

type TypedAction =
    { Id: ActionId
      Output: FieldType
      Inputs: Map<string, ActionId * FieldType> }

module TypeCheck =

    /// Resolve a single optional reference.
    /// Returns Ok (id, type) or appends errors and returns None.
    let private resolveRef
        (actionId: ActionId)
        (key: string)
        (ref: ActionId option)
        (seen: Map<ActionId, int>)
        (types: Map<ActionId, FieldType>)
        (index: int)
        (errors: TypeError list)
        : (ActionId * FieldType) option * TypeError list =
        match ref with
        | None ->
            None, MissingRef(actionId, key) :: errors
        | Some targetId ->
            match Map.tryFind targetId types with
            | Some t -> Some(targetId, t), errors
            | None ->
                match Map.tryFind targetId seen with
                | Some ti when ti >= index ->
                    None, ForwardRef(actionId, key, targetId) :: errors
                | _ ->
                    None, RefNotFound(actionId, key, targetId) :: errors

    /// Resolve a ref and check it matches one of the expected types.
    let private resolveTyped
        (actionId: ActionId)
        (key: string)
        (ref: ActionId option)
        (expected: FieldType list)
        (seen: Map<ActionId, int>)
        (types: Map<ActionId, FieldType>)
        (index: int)
        (errors: TypeError list)
        : (ActionId * FieldType) option * TypeError list =
        let resolved, errors = resolveRef actionId key ref seen types index errors
        match resolved with
        | Some(tid, t) when List.contains t expected ->
            Some(tid, t), errors
        | Some(_tid, t) ->
            None, TypeMismatch(actionId, key, expected, t) :: errors
        | None ->
            None, errors  // error already added by resolveRef

    /// For a given ActionKind, returns the accepted input types per ref slot.
    let acceptedInputs (kind: ActionKind) : Map<string, FieldType list> =
        let fieldOrFrame = [ FieldType.Field; FieldType.Frame ]
        let fieldOnly = [ FieldType.Field ]
        let sketchOnly = [ FieldType.Sketch ]
        let frameOnly = [ FieldType.Frame ]
        match kind with
        | Translate _ -> Map.ofList [ "child", fieldOrFrame ]
        | Rotate _ -> Map.ofList [ "child", fieldOrFrame ]
        | Move _ -> Map.ofList [ "child", fieldOrFrame; "frame", frameOnly ]
        | Union _ | Subtract _ | Intersect _ -> Map.ofList [ "a", fieldOnly; "b", fieldOnly ]
        | Sketch _ -> Map.ofList [ "origin", frameOnly ]
        | FromSketch _ -> Map.ofList [ "child", sketchOnly ]
        | Thicken _ | Shell _ -> Map.ofList [ "child", fieldOnly ]
        | Mesh _ -> Map.ofList [ "child", fieldOnly ]
        | _ -> Map.empty

    let private emit id output inputs types typed errors =
        Map.add id output types,
        { Id = id; Output = output; Inputs = inputs } :: typed,
        errors

    type TypecheckResult =
        { Typed: TypedAction list
          Errors: TypeError list }

    /// Type-check the full action list.
    /// Always produces typed actions (best-effort) plus any errors found.
    let typecheck (actions: DocAction list) : TypecheckResult =
        // Build index map for forward-ref detection
        let seen =
            actions
            |> List.mapi (fun i a -> a.Id, i)
            |> Map.ofList

        let folder (types: Map<ActionId, FieldType>, typed: TypedAction list, errors: TypeError list) (index: int, action: DocAction) =
            let id = action.Id

            let addInput key resolved inputs =
                match resolved with
                | Some(tid, t) -> Map.add key (tid, t) inputs
                | None -> inputs

            match action.Kind with
            // ── Producers (no inputs) ────────────────────────────────
            | Origin ->
                emit id FieldType.Frame Map.empty types typed errors

            | Sphere _ | Cylinder _ | Box _ | HalfPlane _ ->
                emit id FieldType.Field Map.empty types typed errors

            | Sketch(origin, _) ->
                // origin is an optional Frame reference; type-check if present
                let resolved, errors =
                    match origin with
                    | None -> None, errors
                    | Some _ -> resolveTyped id "origin" origin [ FieldType.Frame ] seen types index errors
                emit id FieldType.Sketch (addInput "origin" resolved Map.empty) types typed errors

            // ── Polymorphic transforms (output = input type) ─────────
            | Translate(child, _, _, _) ->
                let resolved, errors = resolveTyped id "child" child [ FieldType.Field; FieldType.Frame ] seen types index errors
                let output = resolved |> Option.map snd |> Option.defaultValue FieldType.Field
                emit id output (addInput "child" resolved Map.empty) types typed errors

            | Rotate(child, _, _, _, _) ->
                let resolved, errors = resolveTyped id "child" child [ FieldType.Field; FieldType.Frame ] seen types index errors
                let output = resolved |> Option.map snd |> Option.defaultValue FieldType.Field
                emit id output (addInput "child" resolved Map.empty) types typed errors

            | Move(child, frame) ->
                let rChild, errors = resolveTyped id "child" child [ FieldType.Field; FieldType.Frame ] seen types index errors
                let rFrame, errors = resolveTyped id "frame" frame [ FieldType.Frame ] seen types index errors
                let output = rChild |> Option.map snd |> Option.defaultValue FieldType.Field
                let inputs = Map.empty |> addInput "child" rChild |> addInput "frame" rFrame
                emit id output inputs types typed errors

            // ── Field → Field (boolean ops, modifiers) ───────────────
            | Union(a, b, _) | Subtract(a, b, _) | Intersect(a, b, _) ->
                let rA, errors = resolveTyped id "a" a [ FieldType.Field ] seen types index errors
                let rB, errors = resolveTyped id "b" b [ FieldType.Field ] seen types index errors
                let inputs = Map.empty |> addInput "a" rA |> addInput "b" rB
                emit id FieldType.Field inputs types typed errors

            | Thicken(child, _) | Shell(child, _) ->
                let resolved, errors = resolveTyped id "child" child [ FieldType.Field ] seen types index errors
                emit id FieldType.Field (addInput "child" resolved Map.empty) types typed errors

            // ── Type converters ──────────────────────────────────────
            | FromSketch(child, _, _) ->
                let resolved, errors = resolveTyped id "child" child [ FieldType.Sketch ] seen types index errors
                emit id FieldType.Field (addInput "child" resolved Map.empty) types typed errors

            | Mesh(child, _, _) ->
                let resolved, errors = resolveTyped id "child" child [ FieldType.Field ] seen types index errors
                emit id FieldType.Mesh (addInput "child" resolved Map.empty) types typed errors

        let _, typed, errors =
            actions
            |> List.mapi (fun i a -> i, a)
            |> List.fold folder (Map.empty, [], [])

        { Typed = List.rev typed; Errors = List.rev errors }
