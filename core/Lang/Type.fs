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
    type T =
        | Scalar
        | Field
        | Sketch
        | Frame
        // Function types arise from lambdas. A curried multi-arg lambda
        // becomes a chain of `Fun`s: `Scalar -> Scalar -> Field` is
        // `Fun(Scalar, Fun(Scalar, Field))`.
        | Fun of input: T * output: T

    /// Pretty-print a type for error messages.
    let rec format (t: T) : string =
        match t with
        | Scalar -> "Scalar"
        | Field -> "Field"
        | Sketch -> "Sketch"
        | Frame -> "Frame"
        | Fun(a, b) ->
            // Right-associative arrow notation.
            let lhs =
                match a with
                | Fun _ -> sprintf "(%s)" (format a)
                | _ -> format a
            sprintf "%s -> %s" lhs (format b)

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
