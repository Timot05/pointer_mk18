namespace Server.Lang

// ---------------------------------------------------------------------------
// Typecheck.fs — bidirectional elaborator from `Ast.Expr` to `Tast.TExpr`.
//
// Two modes:
//   * `infer env e`        — synthesise the type of `e` from its shape and
//                            the env. Used at top-level and in any
//                            position with no surrounding type signal.
//   * `check env t e`      — given an expected type `t`, walk `e` and
//                            confirm it matches.
//
// Lambdas in `infer` mode require an explicit annotation on their
// parameter; lambdas in `check (Fun(a, b))` mode borrow `a` from the
// surrounding context, so user code that omits annotations works as
// long as it appears in an applied position.
//
// No unification, no polymorphism, no type variables yet — every
// resolved type is monomorphic. We can add inference later when the
// surface language grows organic enough to need it.
// ---------------------------------------------------------------------------

module Typecheck =

    open Token
    open Ast

    type TypeError =
        | UndefinedVar of name: string * Span
        | TypeMismatch of expected: Type.T * actual: Type.T * Span
        | NotAFunction of actual: Type.T * Span
        | MissingTypeAnnotation of paramName: string * Span
        | AnnotationConflict of annotated: Type.T * expected: Type.T * Span
        | InvalidOperand of context: string * Span

    let formatError (err: TypeError) : string =
        match err with
        | UndefinedVar(name, _) -> sprintf "undefined identifier '%s'" name
        | TypeMismatch(exp, act, _) ->
            sprintf "type mismatch: expected %s, got %s"
                (Type.format exp) (Type.format act)
        | NotAFunction(t, _) ->
            sprintf "expected a function, got %s" (Type.format t)
        | MissingTypeAnnotation(name, _) ->
            sprintf "missing type annotation on parameter '%s'" name
        | AnnotationConflict(annotated, expected, _) ->
            sprintf "annotation %s conflicts with expected %s"
                (Type.format annotated) (Type.format expected)
        | InvalidOperand(ctx, _) ->
            sprintf "invalid operand for %s" ctx

    type TypeEnv = Map<string, Type.T>

    // ── Helpers ────────────────────────────────────────────────────────────

    let private bind (env: TypeEnv) (name: string) (t: Type.T) : TypeEnv =
        Map.add name t env

    /// Lift the result of binary / unary numeric ops. If either operand is
    /// `Field`, the lifted result is `Field` (matches `Eval`'s
    /// number↔field arithmetic semantics). Both `Scalar` → `Scalar`.
    /// Anything else is invalid.
    let private liftNumeric (a: Type.T) (b: Type.T) : Type.T option =
        match a, b with
        | Type.Scalar, Type.Scalar -> Some Type.Scalar
        | Type.Field, Type.Scalar
        | Type.Scalar, Type.Field
        | Type.Field, Type.Field   -> Some Type.Field
        | _ -> None

    let private liftUnary (t: Type.T) : Type.T option =
        match t with
        | Type.Scalar -> Some Type.Scalar
        | Type.Field  -> Some Type.Field
        | _ -> None

    // ── Bidirectional core ─────────────────────────────────────────────────

    let rec infer (env: TypeEnv) (expr: Expr) : Result<Tast.TExpr, TypeError list> =
        match expr.Node with
        | EUnit ->
            // Unit is rare in our DSL; treat as `Scalar` for now (it never
            // shows up in spec bodies). We can introduce `TUnit` later.
            Ok (Tast.mkTExpr Tast.TEUnit Type.Scalar expr.Span)

        | ENumber n ->
            Ok (Tast.mkTExpr (Tast.TENumber n) Type.Scalar expr.Span)

        | EBool b ->
            Ok (Tast.mkTExpr (Tast.TEBool b) Type.Scalar expr.Span)

        | EString s ->
            Ok (Tast.mkTExpr (Tast.TEString s) Type.Scalar expr.Span)

        | EAxis axis ->
            Ok (Tast.mkTExpr (Tast.TEAxis axis) Type.Field expr.Span)

        | EVar id ->
            match Map.tryFind id.Name env with
            | Some t -> Ok (Tast.mkTExpr (Tast.TEVar id) t expr.Span)
            | None   -> Error [ UndefinedVar(id.Name, id.Span) ]

        | ELambda(p, Some t, body) ->
            let env' = bind env p.Name t
            infer env' body |> Result.map (fun tBody ->
                let lambdaTy = Type.Fun(t, tBody.Type)
                Tast.mkTExpr (Tast.TELambda(p, t, tBody)) lambdaTy expr.Span)

        | ELambda(p, None, _) ->
            // In infer-mode we can't synthesise the param type from
            // nothing. Surface this so the spec author / user sees the
            // missing hint clearly.
            Error [ MissingTypeAnnotation(p.Name, p.Span) ]

        | EApply(fn, arg) ->
            inferApply env fn arg expr.Span

        | EBinary(op, lhs, rhs) ->
            inferBinary env op lhs rhs expr.Span

        | EUnary(op, inner) ->
            infer env inner |> Result.bind (fun ti ->
                match liftUnary ti.Type with
                | Some t ->
                    Ok (Tast.mkTExpr (Tast.TEUnary(op, ti)) t expr.Span)
                | None ->
                    Error [ InvalidOperand("unary op", expr.Span) ])

        | ERemapAxes(target, x, y, z) ->
            let r1 = check env Type.Field target
            let r2 = check env Type.Field x
            let r3 = check env Type.Field y
            let r4 = check env Type.Field z
            collect4 r1 r2 r3 r4 |> Result.map (fun (t, ax, ay, az) ->
                Tast.mkTExpr (Tast.TERemapAxes(t, ax, ay, az)) Type.Field expr.Span)

        | EFold(op, children) ->
            // Each child must be a Field (or coerce — numbers lift to fields
            // via the same path Eval uses). Output is always Field.
            let results = children |> List.map (checkScalarOrField env)
            collectList results |> Result.map (fun ts ->
                Tast.mkTExpr (Tast.TEFold(op, ts)) Type.Field expr.Span)

        | ELineSegment(plane, p0x, p0y, p1x, p1y) ->
            checkPrimitive env [ p0x; p0y; p1x; p1y ] expr.Span |> Result.map (fun ts ->
                Tast.mkTExpr (Tast.TELineSegment(plane, ts.[0], ts.[1], ts.[2], ts.[3])) Type.Field expr.Span)

        | ECircle(plane, cx, cy, r) ->
            checkPrimitive env [ cx; cy; r ] expr.Span |> Result.map (fun ts ->
                Tast.mkTExpr (Tast.TECircle(plane, ts.[0], ts.[1], ts.[2])) Type.Field expr.Span)

        | EBezierQuadratic(plane, p0x, p0y, p1x, p1y, p2x, p2y) ->
            checkPrimitive env [ p0x; p0y; p1x; p1y; p2x; p2y ] expr.Span |> Result.map (fun ts ->
                Tast.mkTExpr (Tast.TEBezierQuadratic(plane, ts.[0], ts.[1], ts.[2], ts.[3], ts.[4], ts.[5])) Type.Field expr.Span)

        | EBezierCubic(plane, p0x, p0y, p1x, p1y, p2x, p2y, p3x, p3y) ->
            checkPrimitive env [ p0x; p0y; p1x; p1y; p2x; p2y; p3x; p3y ] expr.Span |> Result.map (fun ts ->
                Tast.mkTExpr (Tast.TEBezierCubic(plane, ts.[0], ts.[1], ts.[2], ts.[3], ts.[4], ts.[5], ts.[6], ts.[7])) Type.Field expr.Span)

        | EArcCenter(plane, sx, sy, ex, ey, cx, cy, cw) ->
            checkPrimitive env [ sx; sy; ex; ey; cx; cy ] expr.Span |> Result.map (fun ts ->
                Tast.mkTExpr (Tast.TEArcCenter(plane, ts.[0], ts.[1], ts.[2], ts.[3], ts.[4], ts.[5], cw)) Type.Field expr.Span)

        | EBlock stmts ->
            inferBlock env stmts expr.Span

        | EList items ->
            // Lists aren't typed precisely yet — treat as Scalar for now.
            // Proper list types can land alongside polymorphism.
            collectList (List.map (infer env) items)
            |> Result.map (fun ts ->
                Tast.mkTExpr (Tast.TEList ts) Type.Scalar expr.Span)

        | ETuple items ->
            collectList (List.map (infer env) items)
            |> Result.map (fun ts ->
                Tast.mkTExpr (Tast.TETuple ts) Type.Scalar expr.Span)

        | EPath _ | EStackTop | ECall _ | EMatch _ ->
            // Surface-only / legacy nodes that don't exist in the
            // typed-block program shape. Surface a generic mismatch.
            Error [ InvalidOperand("unsupported AST node in typechecker", expr.Span) ]

    and check (env: TypeEnv) (expected: Type.T) (expr: Expr) : Result<Tast.TExpr, TypeError list> =
        match expr.Node, expected with
        | ELambda(p, anno, body), Type.Fun(a, b) ->
            // The annotation, if present, must match the expected param.
            match anno with
            | Some t when t <> a ->
                Error [ AnnotationConflict(t, a, p.Span) ]
            | _ ->
                let env' = bind env p.Name a
                check env' b body |> Result.map (fun tBody ->
                    Tast.mkTExpr (Tast.TELambda(p, a, tBody)) (Type.Fun(a, b)) expr.Span)
        | _ ->
            // Default: infer then verify.
            infer env expr |> Result.bind (fun ti ->
                if ti.Type = expected then Ok ti
                else Error [ TypeMismatch(expected, ti.Type, expr.Span) ])

    // ── Helpers used by infer ──────────────────────────────────────────────

    /// Accept Scalar or Field for any child position that lifts to a
    /// MathIR expression (numbers → const nodes; fields pass through).
    /// Mirrors `Eval.liftToExpr`.
    and private checkScalarOrField (env: TypeEnv) (e: Expr) : Result<Tast.TExpr, TypeError list> =
        infer env e |> Result.bind (fun te ->
            match te.Type with
            | Type.Scalar | Type.Field -> Ok te
            | other -> Error [ TypeMismatch(Type.Field, other, e.Span) ])

    /// Check each child as Scalar-or-Field and return them in source order.
    and private checkPrimitive
            (env: TypeEnv)
            (children: Expr list)
            (_span: Span) : Result<Tast.TExpr array, TypeError list> =
        let results = children |> List.map (checkScalarOrField env)
        collectList results |> Result.map List.toArray

    and private inferApply (env: TypeEnv) (fn: Expr) (arg: Expr) (span: Span) =
        infer env fn |> Result.bind (fun tFn ->
            match tFn.Type with
            | Type.Fun(a, b) ->
                check env a arg |> Result.map (fun tArg ->
                    Tast.mkTExpr (Tast.TEApply(tFn, tArg)) b span)
            | other ->
                Error [ NotAFunction(other, fn.Span) ])

    and private inferBinary (env: TypeEnv) (op: BinaryOp) (lhs: Expr) (rhs: Expr) (span: Span) =
        // Pipe / Compose are surface-only sugar that don't survive
        // elaboration in our flow; reject them at typecheck time. The
        // arithmetic ops dispatch through `liftNumeric`.
        match op with
        | BinaryOp.Pipe | BinaryOp.Compose ->
            Error [ InvalidOperand("pipe/compose at type-check", span) ]
        | _ ->
            let r1 = infer env lhs
            let r2 = infer env rhs
            collect2 r1 r2 |> Result.bind (fun (tl, tr) ->
                match liftNumeric tl.Type tr.Type with
                | Some t ->
                    Ok (Tast.mkTExpr (Tast.TEBinary(op, tl, tr)) t span)
                | None ->
                    Error [ InvalidOperand("binary op", span) ])

    and private inferBlock (env: TypeEnv) (stmts: Stmt list) (span: Span) =
        // Walk statements, threading the env. Non-let statements just
        // evaluate but don't bind; their type is irrelevant except for
        // the trailing one, which is the block's overall type.
        let mutable accEnv = env
        let mutable accStmts : Tast.TStmt list = []
        let mutable err : TypeError list = []
        let mutable lastTy : Type.T option = None
        for s in stmts do
            if err.IsEmpty then
                match s with
                | SLet(names, value) ->
                    match infer accEnv value with
                    | Ok tv ->
                        // Multi-name lets aren't supported in our AST yet;
                        // bind the first name (matches `Eval.evalStmt`).
                        match names with
                        | [ single ] ->
                            accEnv <- bind accEnv single.Name tv.Type
                        | _ ->
                            err <- err @ [ InvalidOperand("multi-name let unsupported", value.Span) ]
                        accStmts <- accStmts @ [ Tast.TSLet(names, tv) ]
                        lastTy <- Some tv.Type
                    | Error es -> err <- err @ es
                | SExpr e ->
                    match infer accEnv e with
                    | Ok te ->
                        accStmts <- accStmts @ [ Tast.TSExpr te ]
                        lastTy <- Some te.Type
                    | Error es -> err <- err @ es
                | SImport _ | SExport _ | SDup _ | SSwap _ | SRotate _ ->
                    err <- err @ [ InvalidOperand("legacy statement in typechecker", span) ]
        if not err.IsEmpty then Error err
        else
            let blockTy = lastTy |> Option.defaultValue Type.Scalar
            Ok (Tast.mkTExpr (Tast.TEBlock accStmts) blockTy span)

    and private collect2
            (a: Result<Tast.TExpr, TypeError list>)
            (b: Result<Tast.TExpr, TypeError list>)
            : Result<Tast.TExpr * Tast.TExpr, TypeError list> =
        match a, b with
        | Ok ta, Ok tb -> Ok (ta, tb)
        | Error es, Ok _ -> Error es
        | Ok _, Error es -> Error es
        | Error es1, Error es2 -> Error (es1 @ es2)

    and private collect4
            (a: Result<Tast.TExpr, TypeError list>)
            (b: Result<Tast.TExpr, TypeError list>)
            (c: Result<Tast.TExpr, TypeError list>)
            (d: Result<Tast.TExpr, TypeError list>)
            : Result<Tast.TExpr * Tast.TExpr * Tast.TExpr * Tast.TExpr, TypeError list> =
        let mutable errs = []
        let unwrap = function
            | Ok v -> Some v
            | Error es -> errs <- errs @ es; None
        let va = unwrap a
        let vb = unwrap b
        let vc = unwrap c
        let vd = unwrap d
        match errs, va, vb, vc, vd with
        | [], Some ta, Some tb, Some tc, Some td -> Ok (ta, tb, tc, td)
        | _ -> Error errs

    and private collectList
            (results: Result<Tast.TExpr, TypeError list> list)
            : Result<Tast.TExpr list, TypeError list> =
        let mutable errs = []
        let mutable acc = []
        for r in results do
            match r with
            | Ok t -> acc <- acc @ [ t ]
            | Error es -> errs <- errs @ es
        if errs.IsEmpty then Ok acc else Error errs

    // ── Public entry point ─────────────────────────────────────────────────

    /// Elaborate a top-level expression against a starting environment.
    /// The environment seeds primitive function types (e.g. block specs)
    /// before the AST is checked.
    let elaborate (env: TypeEnv) (expr: Expr) : Result<Tast.TExpr, TypeError list> =
        infer env expr
