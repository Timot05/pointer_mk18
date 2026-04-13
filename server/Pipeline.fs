namespace Server

// ---------------------------------------------------------------------------
// Compilation pipeline — chains typecheck → element tree → field IR.
// Returns either compiled surfaces or a list of type errors.
// ---------------------------------------------------------------------------

type PipelineResult =
    { Surfaces: FieldSurface list
      TypeMap: Map<ActionId, FieldType> }

module Pipeline =

    /// Run the full compilation pipeline.
    /// If typecheck fails, returns Error with all type errors.
    /// If typecheck passes, builds elements and compiles to field IR (cannot fail).
    let compile (actions: DocAction list) : Result<PipelineResult, TypeError list> =
        match TypeCheck.typecheck actions with
        | Error errors ->
            Error errors
        | Ok typed ->
            let elements = Element.build actions
            let surfaces = FieldCompile.compile actions elements
            let typeMap = typed |> List.map (fun t -> t.Id, t.Output) |> Map.ofList
            Ok { Surfaces = surfaces; TypeMap = typeMap }
