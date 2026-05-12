namespace Server.Lang

// ---------------------------------------------------------------------------
// Tast.fs — typed AST. Mirrors `Ast.Expr` node-for-node but every node
// carries its resolved `Type.T`. The typechecker (`Typecheck.fs`) is the
// function that turns `Ast.Expr` into `Tast.TExpr` (or surfaces errors).
//
// Downstream consumers (the evaluator today, future optimisation passes)
// can pattern-match on TAST nodes and rely on every subtree's type being
// known. The shape parallels the surface AST so the elaboration is
// structural — most cases just walk children and tag each result with
// the inferred type.
// ---------------------------------------------------------------------------

module Tast =

    open Token
    open Ast

    type TExpr = { Node: TExprNode; Type: Type.T; Span: Span }

    and TExprNode =
        | TENumber of float
        | TEBool of bool
        | TEString of string
        | TEUnit
        | TEAxis of Axis
        | TEVar of Ident
        | TELambda of param: Ident * paramType: Type.T * body: TExpr
        | TEApply of fn: TExpr * arg: TExpr
        | TEBinary of op: BinaryOp * left: TExpr * right: TExpr
        | TEUnary of op: UnaryOp * inner: TExpr
        | TERemapAxes of target: TExpr * x: TExpr * y: TExpr * z: TExpr
        | TEFold of op: MathIr.FoldOp * children: TExpr list
        | TELineSegment of plane: MathIr.Plane * TExpr * TExpr * TExpr * TExpr
        | TECircle of plane: MathIr.Plane * TExpr * TExpr * TExpr
        | TEBezierQuadratic of plane: MathIr.Plane * TExpr * TExpr * TExpr * TExpr * TExpr * TExpr
        | TEBezierCubic of plane: MathIr.Plane * TExpr * TExpr * TExpr * TExpr * TExpr * TExpr * TExpr * TExpr
        | TEArcCenter of plane: MathIr.Plane * TExpr * TExpr * TExpr * TExpr * TExpr * TExpr * clockwise: bool
        | TEBlock of stmts: TStmt list
        | TEList of items: TExpr list
        | TETuple of items: TExpr list

    and TStmt =
        | TSLet of names: Ident list * value: TExpr
        | TSExpr of TExpr

    let mkTExpr node ty span : TExpr = { Node = node; Type = ty; Span = span }

    let stmtSpan (s: TStmt) : Span =
        match s with
        | TSLet(names, value) ->
            match names with
            | n :: _ -> mergeSpan n.Span value.Span
            | [] -> value.Span
        | TSExpr e -> e.Span

    let stmtType (s: TStmt) : Type.T =
        match s with
        | TSLet(_, v) -> v.Type
        | TSExpr e -> e.Type
