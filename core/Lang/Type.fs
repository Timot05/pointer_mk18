namespace Server.Lang

// ---------------------------------------------------------------------------
// Type.fs — types for our small DSL.
//
// This is the resolved type representation produced by the typechecker.
// Source-level annotations on lambda parameters use the same vocabulary
// (modulo `Var`, which is internal to the checker if/when we add
// inference). Annotations are wrapped in `Option` so users can leave
// hints off; the typechecker fills them in via bidirectional checking.
// ---------------------------------------------------------------------------

module Type =

    /// Resolved type — what the typechecker produces for every node.
    ///
    /// `Sketch` carries a structural refinement: a map from member name
    /// (typically a `LoopRecord.Id` like `loop_0`, or a user-renamed name)
    /// to the member's type. An empty map is the "generic Sketch" (no
    /// known members), which behaves as the supertype of every refined
    /// sketch — see `isSubtypeOf`. Specs that take a `Sketch` argument
    /// without caring about its content (`from-sketch`, `revolve`) use
    /// `Sketch Map.empty`; the bridge in `NotebookCompose` populates a
    /// concrete refinement when a notebook sketch block is seeded.
    type T =
        // Numeric scalar. The surface syntax keeps the existing `Scalar`
        // name because block parameters and UI defaults are built around it.
        | Scalar
        | Bool
        | String
        | Unit
        | Field
        | List of element: T
        | Tuple of elements: T list
        | Sketch of fields: Map<string, T>
        // A closed sketch boundary. The refinement names its derivable
        // values — at minimum `signed_distance: Field`, plus per-
        // primitive members (`line_0: Primitive`, ...). A Loop whose
        // refinement includes `signed_distance: Field` is treated as a
        // subtype of `Field` (see `isSubtypeOf`), so passing a loop
        // anywhere a field is expected projects automatically.
        | Loop of fields: Map<string, T>
        // A single sketch primitive (line/arc/circle inside a loop).
        // The refinement encodes variant-specific members; the variant
        // itself is implicit in which keys exist. Primitives auto-
        // project to Field through `signed_distance`, same as Loop.
        | Primitive of fields: Map<string, T>
        | Frame
        // Function types arise from lambdas. A curried multi-arg lambda
        // becomes a chain of `Fun`s: `Scalar -> Scalar -> Field` is
        // `Fun(Scalar, Fun(Scalar, Field))`.
        | Fun of input: T * output: T

    /// Pretty-print a type for error messages.
    let rec format (t: T) : string =
        match t with
        | Scalar -> "Scalar"
        | Bool -> "Bool"
        | String -> "String"
        | Unit -> "Unit"
        | Field -> "Field"
        | List element -> sprintf "List<%s>" (format element)
        | Tuple elements ->
            let body = elements |> List.map format |> String.concat " * "
            sprintf "(%s)" body
        | Sketch fields when Map.isEmpty fields -> "Sketch"
        | Sketch fields ->
            let body =
                fields
                |> Map.toList
                |> List.map (fun (n, ft) -> sprintf "%s: %s" n (format ft))
                |> String.concat ", "
            sprintf "Sketch { %s }" body
        | Loop fields when Map.isEmpty fields -> "Loop"
        | Loop fields ->
            let body =
                fields
                |> Map.toList
                |> List.map (fun (n, ft) -> sprintf "%s: %s" n (format ft))
                |> String.concat ", "
            sprintf "Loop { %s }" body
        | Primitive fields when Map.isEmpty fields -> "Primitive"
        | Primitive fields ->
            let body =
                fields
                |> Map.toList
                |> List.map (fun (n, ft) -> sprintf "%s: %s" n (format ft))
                |> String.concat ", "
            sprintf "Primitive { %s }" body
        | Frame -> "Frame"
        | Fun(a, b) ->
            // Right-associative arrow notation.
            let lhs =
                match a with
                | Fun _ -> sprintf "(%s)" (format a)
                | _ -> format a
            sprintf "%s -> %s" lhs (format b)

    /// The structural fields of a refinement-bearing type, or `None`
    /// for types that don't carry one. Used by `isSubtypeOf` for
    /// width-subtype checks on `Sketch` / `Loop` / `Primitive`.
    let private refinementFields (t: T) : Map<string, T> option =
        match t with
        | Sketch f | Loop f | Primitive f -> Some f
        | _ -> None

    /// Width subtyping + the auto-projection-to-Field rule.
    ///
    /// `sub <: sup` rules:
    /// - Refined kinds of the same constructor (`Sketch`/`Loop`/
    ///   `Primitive`) use width subtyping: every member `sup` requires
    ///   must exist in `sub` with a compatible type.
    /// - `Loop { signed_distance: Field, ... } <: Field` and
    ///   `Primitive { signed_distance: Field, ... } <: Field`. The
    ///   runtime auto-projects via `.signed_distance` at consume sites
    ///   so loops and primitives stand in for fields wherever fields
    ///   are expected.
    /// - `Fun` is contravariant in input, covariant in output.
    /// - Everything else is structural equality.
    let rec isSubtypeOf (sub: T) (sup: T) : bool =
        let sameKind =
            match sub, sup with
            | Sketch _, Sketch _ | Loop _, Loop _ | Primitive _, Primitive _ -> true
            | _ -> false
        if sameKind then
            match refinementFields sub, refinementFields sup with
            | Some sub_fields, Some sup_fields ->
                sup_fields
                |> Map.forall (fun k v ->
                    match Map.tryFind k sub_fields with
                    | Some v' -> isSubtypeOf v' v
                    | None -> false)
            | _ -> false
        else
            match sub, sup with
            | (Loop fields | Primitive fields), Field ->
                match Map.tryFind "signed_distance" fields with
                | Some Field -> true
                | _ -> false
            | Fun(a1, b1), Fun(a2, b2) ->
                isSubtypeOf a2 a1 && isSubtypeOf b1 b2
            | _ -> sub = sup

    /// Unfold a curried function type into its argument list and final
    /// result. `Fun(A, Fun(B, C)) → ([A; B], C)`.
    let unfold (t: T) : T list * T =
        let rec loop acc =
            function
            | Fun(a, b) -> loop (a :: acc) b
            | other -> List.rev acc, other
        loop [] t

    /// Build a curried function type from an argument list and result.
    let rec curried (inputs: T list) (output: T) : T =
        match inputs with
        | [] -> output
        | hd :: rest -> Fun(hd, curried rest output)
