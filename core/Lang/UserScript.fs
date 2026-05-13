namespace Server.Lang

// ---------------------------------------------------------------------------
// UserScript.fs — analyse a free-form DSL script (typed in the Monaco panel)
// and extract user-defined functions into spec-shaped records that the
// notebook driver and BlockList UI can consume alongside built-in BlockSpecs.
//
// Inputs:  raw source string from `Document.ScriptSourceText`.
// Outputs: a `Result` carrying every parsed top-level statement (`Stmts`),
//          a `Specs` map of fully-annotated lambda definitions, an optional
//          parser error, and per-spec analysis failures.
//
// What counts as a user spec: any top-level `let f (x: T) (y: U) ... = body`
// where every parameter has an F#-style type annotation. Unannotated
// parameters are tolerated in `Stmts` (they still bind in the eval env) but
// not surfaced as draggable blocks — there's no way to render their input
// editors without a known parameter type.
//
// The body is *not* typechecked here (UserScript sits before Typecheck.fs in
// the project order so the registry of user-spec types is available when
// `Typecheck.elaborate` later runs over the composed program). Body errors
// surface during compose / typecheck against the full program env.
// ---------------------------------------------------------------------------

module UserScript =

    open Token
    open Ast

    /// One user-defined function extracted from script source. Mirrors
    /// `BlockSpec.BlockSpec`'s `Name`/`Body` plus the typed-extraction shape
    /// `BlockSpec.typedInterface` exposes, so the BlockList renders user
    /// specs through the same code path as native ones.
    type UserSpec = {
        Name: string
        Body: Expr
        Params: TypeExtract.ExtractedParam list
        Output: Type.T
        /// Span of the `let f ... = body` for error routing.
        Span: Span
    }

    type Result = {
        /// Every parsed top-level statement, in source order. Threaded into
        /// the composed program ahead of block-let stmts so user lambdas
        /// land as closures in the eval env. Includes lambda defs *and*
        /// non-lambda lets (`let pi = 3.14159`).
        Stmts: Stmt list
        /// Map name → spec for every fully-annotated lambda. Lambdas with
        /// any un-annotated parameter are excluded and listed in
        /// `AnalysisErrors` instead.
        Specs: Map<string, UserSpec>
        /// Set when the source failed to lex or parse. `Stmts` and `Specs`
        /// are empty in that case.
        ParseError: Parser.ParseError option
        /// Per-spec analysis failures keyed by the offending let-binding's
        /// name. The statement itself is still present in `Stmts` (so a
        /// half-broken script keeps the rest working) but the spec isn't
        /// available for instantiation in the +Add palette.
        AnalysisErrors: (string * string) list
    }

    let empty : Result = {
        Stmts = []
        Specs = Map.empty
        ParseError = None
        AnalysisErrors = []
    }

    /// Walk a curried lambda. Each nested `ELambda` must carry `Some` on
    /// `paramAnno`; on `None`, return the offending param name so the caller
    /// can report it. Returns the typed parameter list (outermost first) and
    /// the innermost body expression.
    let private collectAnnotatedParams
            (lambdaExpr: Expr) : Result<TypeExtract.ExtractedParam list * Expr, string> =
        let rec walk (e: Expr) (acc: TypeExtract.ExtractedParam list) =
            match e.Node with
            | ELambda(param, Some ty, body) ->
                walk body ({ Name = param.Name; Type = ty } :: acc)
            | ELambda(param, None, _) ->
                Error param.Name
            | _ ->
                Ok (List.rev acc, e)
        walk lambdaExpr []

    /// Heuristic output type for the BlockList's output indicator. We don't
    /// run the typechecker here (it lives later in the compilation order),
    /// so this mirrors `TypeExtract.outputOf`: literal numbers → Scalar,
    /// anything else → Field. Correct for the dominant case (SDF-shaped
    /// user functions); off for pure-numeric helpers, which the user is
    /// unlikely to instantiate as a block anyway.
    let private guessOutput (body: Expr) : Type.T =
        match body.Node with
        | ENumber _ -> Type.Scalar
        | _ -> Type.Field

    /// Parse + extract. The contract is "best-effort": parse errors return
    /// `empty + ParseError`; analysis errors on individual specs don't fail
    /// the whole script — they're collected so the UI can mark each broken
    /// def while the rest of the editor remains usable.
    let analyze (source: string) : Result =
        match Parser.parseProgram source with
        | Error e ->
            { empty with ParseError = Some e }
        | Ok stmts ->
            let mutable specs : Map<string, UserSpec> = Map.empty
            let mutable errs : (string * string) list = []
            for stmt in stmts do
                match stmt with
                | SLet([ name ], value) ->
                    match value.Node with
                    | ELambda _ ->
                        match collectAnnotatedParams value with
                        | Ok (parameters, body) ->
                            specs <-
                                Map.add name.Name {
                                    Name = name.Name
                                    Body = value
                                    Params = parameters
                                    Output = guessOutput body
                                    Span = stmtSpan stmt
                                } specs
                        | Error missingName ->
                            errs <-
                                (name.Name,
                                 sprintf "parameter '%s' needs a type annotation" missingName)
                                :: errs
                    | _ -> ()
                | _ -> ()
            {
                Stmts = stmts
                Specs = specs
                ParseError = None
                AnalysisErrors = List.rev errs
            }
