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
// render the right editor per input. It delegates output typing to the
// typechecker so block interfaces stay aligned with real elaboration.
// ---------------------------------------------------------------------------

module TypeExtract =

    open Ast

    type ExtractedParam = { Name: string; Type: Type.T }

    type ExtractedSpec = {
        Params: ExtractedParam list   // outermost lambda first
        Output: Type.T
    }

    let private paramNames (lambdaExpr: Expr) : string list =
        let rec walk (e: Expr) (acc: string list) =
            match e.Node with
            | ELambda(param, Some paramType, body) ->
                ignore paramType
                walk body (param.Name :: acc)
            | ELambda(param, None, _) ->
                // Native specs annotate every parameter. Hitting None
                // means a spec was defined wrong — surface immediately.
                failwithf "TypeExtract: lambda parameter '%s' has no type annotation" param.Name
            | _ ->
                List.rev acc
        walk lambdaExpr []

    let extractWith (env: Typecheck.TypeEnv) (lambdaExpr: Expr) : ExtractedSpec =
        let names = paramNames lambdaExpr
        match Typecheck.elaborate env lambdaExpr with
        | Error errs ->
            let msg = errs |> List.map Typecheck.formatError |> String.concat "; "
            failwithf "TypeExtract: spec body failed to typecheck: %s" msg
        | Ok typed ->
            let inputs, output = Type.unfold typed.Type
            if List.length inputs <> List.length names then
                failwithf
                    "TypeExtract: lambda parameter count mismatch (%d names, %d types)"
                    (List.length names)
                    (List.length inputs)
            let parameters =
                List.zip names inputs
                |> List.map (fun (name, ty) -> { Name = name; Type = ty })
            { Params = parameters; Output = output }

    let extract (lambdaExpr: Expr) : ExtractedSpec =
        extractWith Map.empty lambdaExpr
