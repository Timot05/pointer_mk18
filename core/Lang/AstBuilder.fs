namespace Server.Lang

// ---------------------------------------------------------------------------
// AstBuilder.fs — typed helpers for constructing `Ast.Expr` values
// outside the parser. Used wherever the codebase synthesizes DSL
// expressions from non-source data: the notebook compose pass, the
// block-list UI, the editor reducers that set block inputs, and tests.
//
// The helpers all carry a span so downstream typecheck / eval errors
// route back to a meaningful location. `noSpan` is the zero sentinel
// used when no source location applies; `spanForBlock` synthesizes a
// per-block span keyed by `BlockId` so block-level errors can be mapped
// back to their originating block via `BlockSpans`.
// ---------------------------------------------------------------------------

module AstBuilder =

    open Token
    open Ast

    /// Zero span — for expressions with no source location.
    let noSpan : Span = { Start = 0; Stop = 0 }

    /// Per-block span. `Start = Stop = blockId`; meaningless as a source
    /// location but queryable through `BlockSpans` to map an error to a
    /// block.
    let spanForBlock (id: int) : Span =
        { Start = id; Stop = id }

    let userAt (name: string) (sp: Span) : Ident =
        { Name = name; IdentKind = User; Span = sp }

    let user (name: string) : Ident = userAt name noSpan

    let mkAt (sp: Span) node : Expr = { Node = node; Span = sp }
    let mk node : Expr = mkAt noSpan node

    let varE (name: string) : Expr = mk (EVar (user name))
    let varEAt (sp: Span) (name: string) : Expr = mkAt sp (EVar (userAt name sp))

    let numE (n: float) : Expr = mk (ENumber n)
    let numEAt (sp: Span) (n: float) : Expr = mkAt sp (ENumber n)

    /// Path access: `pathE ["profile"; "loop_0"]` → `EPath [profile; loop_0]`.
    let pathE (segments: string list) : Expr =
        mk (EPath (segments |> List.map user))

    let pathEAt (sp: Span) (segments: string list) : Expr =
        mkAt sp (EPath (segments |> List.map (fun n -> userAt n sp)))

    /// `EApply` chain — `applyChain f [a; b; c]` yields `((f a) b) c`.
    let applyChain (callee: Expr) (args: Expr list) : Expr =
        args |> List.fold (fun acc arg -> mk (EApply(acc, arg))) callee

    let applyChainAt (sp: Span) (callee: Expr) (args: Expr list) : Expr =
        args |> List.fold (fun acc arg -> mkAt sp (EApply(acc, arg))) callee

    /// Sentinel name planted into the AST when a block input is unwired.
    /// Resolves to `UndefinedVar` at typecheck — clean signal back to the
    /// editor that the user needs to wire something.
    [<Literal>]
    let UNWIRED_PLACEHOLDER = "<unwired>"

    let unwiredE : Expr = varE UNWIRED_PLACEHOLDER
    let unwiredEAt (sp: Span) : Expr = varEAt sp UNWIRED_PLACEHOLDER
