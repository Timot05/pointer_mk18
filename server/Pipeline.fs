namespace Server

// ---------------------------------------------------------------------------
// Compilation pipeline — chains typecheck → element tree → field IR.
// Always produces a type map and surfaces (best-effort), plus any errors.
// ---------------------------------------------------------------------------

type PipelineResult =
    { Surfaces: FieldSurface list
      TypeMap: Map<ActionId, FieldType>
      Errors: TypeError list }

module Pipeline =

    /// Run the full compilation pipeline.
    /// Always produces a type map (for ref filtering) and surfaces (for rendering).
    /// Errors are collected but don't prevent compilation of valid actions.
    let compile (actions: DocAction list) : PipelineResult =
        let tc = TypeCheck.typecheck actions
        let typeMap = tc.Typed |> List.map (fun t -> t.Id, t.Output) |> Map.ofList
        let elements = Element.build actions
        let surfaces = FieldCompile.compile actions elements
        { Surfaces = surfaces; TypeMap = typeMap; Errors = tc.Errors }
