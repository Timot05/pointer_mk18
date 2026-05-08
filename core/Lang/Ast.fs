namespace Server.Lang

// ---------------------------------------------------------------------------
// Ast.fs — port of pointer_mk19/compiler/lib/ast.ml.
//
// The AST mirrors mk19's exactly so the parser is a mechanical translation.
// Inline-record DU cases in OCaml become tuple-style cases here (F# DUs
// don't have inline records).
// ---------------------------------------------------------------------------

module Ast =

    open Token

    let mergeSpan (a: Span) (b: Span) : Span = { Start = a.Start; Stop = b.Stop }
    let noneSpan : Span = { Start = 0; Stop = 0 }

    type IdentKind = User | Internal

    type Ident = {
        Name: string
        IdentKind: IdentKind
        Span: Span
    }

    type UnaryOp = Neg

    type BinaryOp =
        | Add
        | Sub
        | Mul
        | Div
        | Pipe
        | Compose

    type Literal =
        | LNumber of float
        | LString of string
        | LUnit

    type Expr = { Node: ExprNode; Span: Span }

    and ExprNode =
        | EUnit
        | ENumber of float
        | EBool of bool
        | EString of string
        | EVar of Ident
        | EPath of Ident list
        | EStackTop
        | ELambda of Ident * Expr
        | EApply of Expr * Expr
        | EBlock of Stmt list
        | EList of Expr list
        | ETuple of Expr list
        | ECall of callee: Ident * args: Arg list
        | EUnary of UnaryOp * Expr
        | EBinary of op: BinaryOp * left: Expr * right: Expr
        | EMatch of subject: Expr * arms: MatchArm list

    and Arg =
        | Positional of Expr
        | Named of Ident * Expr

    and MatchArm = { Pattern: Pattern; Body: Expr }

    and Pattern =
        | PBind of Ident
        | PConstructor of Ident * Pattern list
        | PLiteral of Literal * Span
        | PWildcard of Span

    and Stmt =
        | SLet of Ident list * Expr
        | SExpr of Expr
        | SDup of Span
        | SSwap of Span
        | SRotate of Span

    let mkExpr node span : Expr = { Node = node; Span = span }

    let stmtSpan (s: Stmt) : Span =
        match s with
        | SLet(names, value) ->
            match names with
            | n :: _ -> mergeSpan n.Span value.Span
            | [] -> value.Span
        | SExpr e -> e.Span
        | SDup sp | SSwap sp | SRotate sp -> sp
