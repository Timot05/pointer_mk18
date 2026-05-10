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

    type UnaryOp =
        | Neg
        | Sqrt
        | Abs
        | Square
        | Sin
        | Cos
        | Tan

    type BinaryOp =
        | Add
        | Sub
        | Mul
        | Div
        | Min
        | Max
        | Pipe
        | Compose

    /// Spatial axis — for the in-AST `EAxis` node that resolves to the
    /// MathIR `Var` (the kernel's per-sample x / y / z position).
    type Axis = AxisX | AxisY | AxisZ

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
        // `ELambda` carries an optional type *hint* on its parameter.
        // Hand-built specs always supply `Some`; user-source lambdas leave
        // it `None` and the typechecker infers (or errors with
        // `MissingTypeAnnotation`) in pure-infer position.
        | ELambda of param: Ident * paramAnno: Type.T option * body: Expr
        | EApply of Expr * Expr
        | EBlock of Stmt list
        | EList of Expr list
        | ETuple of Expr list
        | ECall of callee: Ident * args: Arg list
        | EUnary of UnaryOp * Expr
        | EBinary of op: BinaryOp * left: Expr * right: Expr
        // Spatial axis variable — `AxisX` / `AxisY` / `AxisZ`. At eval time
        // resolves to the kernel's per-sample position component.
        | EAxis of Axis
        // Coordinate remap: evaluate `target` in a frame where the kernel's
        // (x, y, z) substitution is replaced by (newX, newY, newZ).
        // Equivalent to MathIR's `RemapAxes` node — used by translate-style
        // blocks to shift the sample point before delegating.
        | ERemapAxes of target: Expr * newX: Expr * newY: Expr * newZ: Expr
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
        // `import x` — declares an external binding wired in by the
        // notebook driver before this block runs. No default value; if the
        // wire is missing, usages of `x` error as `unbound identifier`.
        | SImport of Ident
        // `export x = e` — like `SLet [x] e`, but the notebook driver
        // collects this binding as one of the block's outputs after eval.
        // The magic name `view` routes to the view slot instead of outputs.
        | SExport of Ident * Expr
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
        | SImport id -> id.Span
        | SExport(name, value) -> mergeSpan name.Span value.Span
        | SExpr e -> e.Span
        | SDup sp | SSwap sp | SRotate sp -> sp
