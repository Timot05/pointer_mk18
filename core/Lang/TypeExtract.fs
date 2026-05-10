namespace Server.Lang

// ---------------------------------------------------------------------------
// TypeExtract.fs — derives a block's typed input list from its AST.
//
// Block bodies are curried lambdas. Each `ELambda` carries an optional
// `Type.T` annotation on its parameter. For hand-built native specs the
// annotation is always `Some`; user-source blocks (future) go through
// the full typechecker which fills in types before this module is
// consulted, so an unannotated lambda reaching here is treated as an
// internal bug.
//
// This module is a small convenience layer used by the BlockList UI to
// render the right editor per input. The richer `Typecheck.fs` will
// eventually subsume it once user-source blocks land.
// ---------------------------------------------------------------------------

module TypeExtract =

    open Ast

    type ExtractedParam = { Name: string; Type: Type.T }

    type ExtractedSpec = {
        Params: ExtractedParam list   // outermost lambda first
        Output: Type.T
    }

    /// Best-effort guess of an expression's resulting type. For our
    /// hand-built specs the body is always Field-shaped; this only gets
    /// finer once we consult the typechecker.
    let rec private outputOf (e: Expr) : Type.T =
        match e.Node with
        | EAxis _
        | ERemapAxes _ -> Type.Field
        | EUnary _
        | EBinary _ -> Type.Field
        | EApply _ -> Type.Field
        | ENumber _ -> Type.Scalar
        | _ -> Type.Field

    let extract (lambdaExpr: Expr) : ExtractedSpec =
        let rec walk (e: Expr) (acc: ExtractedParam list) =
            match e.Node with
            | ELambda(param, Some paramType, body) ->
                walk body ({ Name = param.Name; Type = paramType } :: acc)
            | ELambda(param, None, _) ->
                // Native specs annotate every parameter. Hitting None
                // means a spec was defined wrong — surface immediately.
                failwithf "TypeExtract: lambda parameter '%s' has no type annotation" param.Name
            | _ ->
                List.rev acc, e
        let parameters, body = walk lambdaExpr []
        { Params = parameters; Output = outputOf body }
