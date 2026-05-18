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
        | UnknownSketchMember of sketchName: string * memberName: string * available: string list * Span
        | NotASketch of actual: Type.T * Span
        | EmptyPath of Span
        | MissingSketchMembers of missing: string list * available: string list * Span

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
        | UnknownSketchMember(sketchName, memberName, available, _) ->
            let avail =
                if List.isEmpty available then "<none>"
                else String.concat ", " available
            sprintf "sketch '%s' has no member '%s' (available: %s)"
                sketchName memberName avail
        | NotASketch(t, _) ->
            sprintf "cannot read field from %s (only sketches and records support dotted access)"
                (Type.format t)
        | EmptyPath _ -> "empty path expression"
        | MissingSketchMembers(missing, available, _) ->
            let m = String.concat ", " missing
            let a =
                if List.isEmpty available then "<none>"
                else String.concat ", " available
            sprintf "sketch is missing required member(s) %s (available: %s)" m a

    type TypeEnv = Map<string, Type.T>

    // ── Refinement-cell stack ─────────────────────────────────────────────
    //
    // When a lambda's parameter has type `Type.Sketch _`, the body is
    // checked against a *mutable refinement* — a cell that accumulates
    // the required members as `EPath` accesses appear. After the body
    // typechecks, the cell is snapshotted into the parameter's final
    // type so the function's signature carries its structural
    // requirements (`Sketch { outer: Field, ... }`).
    //
    // The stack handles shadowing: head-of-list is the innermost frame.
    // Push = `prepend`, pop = `take tail`, lookup = `List.tryFind`. All
    // single operations that work uniformly on .NET and Fable (Fable's
    // BCL doesn't support `System.Threading.ThreadLocal` or the
    // `Stack<>` enumerator).
    //
    // The single-threaded assumption is safe at runtime — Fable in the
    // browser is single-threaded by construction. For .NET tests the
    // assembly-level `Xunit.CollectionBehavior(DisableTestParallelization)`
    // in `tests/AssemblyInfo.fs` serializes cross-class execution.
    // Cleared at the top of every `elaborate` call so state from any
    // earlier run can't leak in.

    /// Which structural-refinement kind the cell belongs to. Sketch /
    /// Loop / Primitive each use the same `Map<string, Type.T> ref`
    /// accumulator but project back to different `Type.T` constructors
    /// and grow under different default-member rules.
    type private RefinementKind =
        | RKSketch
        | RKLoop
        | RKPrimitive

    let mutable private cellStack
            : (string * Map<string, Type.T> ref * RefinementKind) list = []

    let private lookupCell
            (name: string)
            : (Map<string, Type.T> ref * RefinementKind) option =
        cellStack
        |> List.tryFind (fun (n, _, _) -> n = name)
        |> Option.map (fun (_, c, k) -> c, k)

    let private pushCell
            (name: string)
            (cell: Map<string, Type.T> ref)
            (kind: RefinementKind) =
        cellStack <- (name, cell, kind) :: cellStack

    let private popCell () =
        match cellStack with
        | _ :: tail -> cellStack <- tail
        | [] -> ()

    /// Wrap a refinement-cell snapshot in the right Type constructor.
    let private wrapRefinement (kind: RefinementKind) (fields: Map<string, Type.T>) : Type.T =
        match kind with
        | RKSketch    -> Type.Sketch fields
        | RKLoop      -> Type.Loop fields
        | RKPrimitive -> Type.Primitive fields

    /// Default inferred type for a member name accessed on a
    /// cell-bearing param. Sketch grows with `Loop { signed_distance:
    /// Field }` (matching the per-loop runtime payload). Loop grows
    /// with the canonical known names — `signed_distance: Field`,
    /// `perpendicular_axis: Scalar` — and treats everything else as a
    /// `Primitive { signed_distance: Field }` (per-line/arc/circle).
    /// Primitive grows only with `signed_distance: Field`.
    let private inferMemberType (kind: RefinementKind) (memberName: string) : Type.T =
        match kind with
        | RKSketch ->
            Type.Loop (Map.ofList [ "signed_distance", Type.Field ])
        | RKLoop ->
            match memberName with
            | "signed_distance"    -> Type.Field
            | "perpendicular_axis" -> Type.Scalar
            | _ ->
                Type.Primitive (Map.ofList [ "signed_distance", Type.Field ])
        | RKPrimitive ->
            match memberName with
            | "signed_distance" -> Type.Field
            // Geometric scalars surfaced by `NotebookCompose.composeWith`
            // for line / circle / arc entities. Match the variant-
            // specific refinement built there so widening these access
            // patterns through the cell-grow path doesn't conflict with
            // the actual Primitive type assigned to the value.
            | "x0" | "y0" | "x1" | "y1"   // line endpoints + spline endpoints
            | "cx" | "cy" | "r"           // circle params
            | "cx0" | "cy0" | "cx1" | "cy1"   // spline control points
                -> Type.Scalar
            | _ -> Type.Field

    // ── Helpers ────────────────────────────────────────────────────────────

    let private bind (env: TypeEnv) (name: string) (t: Type.T) : TypeEnv =
        Map.add name t env

    /// Project a Loop or Primitive with a `signed_distance: Field`
    /// member to plain Field for the purpose of arithmetic and other
    /// numeric contexts. Mirrors the runtime `coerceFieldLike` in
    /// `Eval.fs` and the subtyping rule in `Type.isSubtypeOf`.
    let private projectToFieldLike (t: Type.T) : Type.T =
        match t with
        | Type.Loop fields
        | Type.Primitive fields ->
            match Map.tryFind "signed_distance" fields with
            | Some Type.Field -> Type.Field
            | _ -> t
        | _ -> t

    /// Lift the result of binary / unary numeric ops. If either operand is
    /// `Field`, the lifted result is `Field` (matches `Eval`'s
    /// number↔field arithmetic semantics). Both numeric `Scalar` → `Scalar`.
    /// Anything else is invalid. Loops with a `signed_distance: Field`
    /// member are accepted as Fields (the runtime auto-projects).
    let private liftNumeric (a: Type.T) (b: Type.T) : Type.T option =
        match projectToFieldLike a, projectToFieldLike b with
        | Type.Scalar, Type.Scalar -> Some Type.Scalar
        | Type.Field, Type.Scalar
        | Type.Scalar, Type.Field
        | Type.Field, Type.Field   -> Some Type.Field
        | _ -> None

    let private liftUnary (t: Type.T) : Type.T option =
        match projectToFieldLike t with
        | Type.Scalar -> Some Type.Scalar
        | Type.Field  -> Some Type.Field
        | _ -> None

    // ── Bidirectional core ─────────────────────────────────────────────────

    let rec infer (env: TypeEnv) (expr: Expr) : Result<Tast.TExpr, TypeError list> =
        match expr.Node with
        | EUnit ->
            Ok (Tast.mkTExpr Tast.TEUnit Type.Unit expr.Span)

        | ENumber n ->
            Ok (Tast.mkTExpr (Tast.TENumber n) Type.Scalar expr.Span)

        | EBool b ->
            Ok (Tast.mkTExpr (Tast.TEBool b) Type.Bool expr.Span)

        | EString s ->
            Ok (Tast.mkTExpr (Tast.TEString s) Type.String expr.Span)

        | EAxis axis ->
            Ok (Tast.mkTExpr (Tast.TEAxis axis) Type.Field expr.Span)

        | EVar id ->
            match Map.tryFind id.Name env with
            | Some t -> Ok (Tast.mkTExpr (Tast.TEVar id) t expr.Span)
            | None   -> Error [ UndefinedVar(id.Name, id.Span) ]

        | ELambda(p, Some t, body) ->
            // For Sketch / Loop / Primitive parameters, open a refinement
            // cell so the body's EPath accesses can accumulate the
            // required members. The cell starts seeded with whatever the
            // annotation already declares (empty `Sketch` / `Loop` /
            // `Primitive` → empty cell; a hypothetical `Loop {
            // signed_distance }` annotation would seed it directly).
            let cellInfo =
                match t with
                | Type.Sketch m    -> Some (ref m, RKSketch)
                | Type.Loop m      -> Some (ref m, RKLoop)
                | Type.Primitive m -> Some (ref m, RKPrimitive)
                | _                -> None
            cellInfo |> Option.iter (fun (c, k) -> pushCell p.Name c k)
            let env' = bind env p.Name t
            let bodyResult = infer env' body
            cellInfo |> Option.iter (fun _ -> popCell ())
            bodyResult |> Result.map (fun tBody ->
                let resolvedParam =
                    match cellInfo with
                    | Some (c, k) -> wrapRefinement k !c
                    | None        -> t
                let lambdaTy = Type.Fun(resolvedParam, tBody.Type)
                Tast.mkTExpr (Tast.TELambda(p, resolvedParam, tBody)) lambdaTy expr.Span)

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
            let r2 = checkScalarOrField env x
            let r3 = checkScalarOrField env y
            let r4 = checkScalarOrField env z
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
            collectList (List.map (infer env) items)
            |> Result.map (fun ts ->
                let itemTy =
                    match ts with
                    | [] -> Type.Unit
                    | first :: rest ->
                        if rest |> List.forall (fun t -> t.Type = first.Type) then first.Type
                        else Type.Tuple (ts |> List.map (fun t -> t.Type))
                Tast.mkTExpr (Tast.TEList ts) (Type.List itemTy) expr.Span)

        | ETuple items ->
            collectList (List.map (infer env) items)
            |> Result.map (fun ts ->
                Tast.mkTExpr (Tast.TETuple ts) (Type.Tuple (ts |> List.map (fun t -> t.Type))) expr.Span)

        | EPath idents ->
            // Dotted access: `head.seg1.seg2...`. Walk segments through
            // the head's structural type. Sketch, Loop, and Primitive
            // are the cell-bearing kinds — when the head names a lambda
            // param with one of them, accessing a missing member grows
            // the param's cell with an inferred type (see
            // `inferMemberType`). Other types (Field, Scalar, ...)
            // don't carry refinements.
            //
            // Only the head is cell-bearing. Deeper segments walk
            // fully-resolved types from compose's seeded refinement, so
            // missing members at depth are real errors. This is how
            // `let extrude (l : Loop) = l.signed_distance` infers
            // `extrude`'s required refinement: walking the body adds
            // `signed_distance : Field` to the cell, which closes into
            // `Loop { signed_distance: Field }` when the lambda returns.
            match idents with
            | [] -> Error [ EmptyPath expr.Span ]
            | head :: rest ->
                let headCell = lookupCell head.Name
                let headTyOpt =
                    match headCell with
                    | Some (cell, kind) -> Some (wrapRefinement kind !cell)
                    | None -> Map.tryFind head.Name env
                match headTyOpt with
                | None -> Error [ UndefinedVar(head.Name, head.Span) ]
                | Some headTy ->
                    let rec walk (curTy: Type.T) (curName: string) (trail: Ident list) =
                        match trail with
                        | [] -> Ok curTy
                        | seg :: deeper ->
                            let refinementFields =
                                match curTy with
                                | Type.Sketch f | Type.Loop f | Type.Primitive f -> Some f
                                | _ -> None
                            match refinementFields with
                            | Some fields ->
                                match Map.tryFind seg.Name fields with
                                | Some t -> walk t seg.Name deeper
                                | None ->
                                    // Cell-grow only at the path head,
                                    // and only if its kind is cell-bearing.
                                    // Deeper segments walk fully-resolved
                                    // types from compose's seeded refinement.
                                    match headCell with
                                    | Some (cell, kind) when curName = head.Name ->
                                        let inferred = inferMemberType kind seg.Name
                                        cell := Map.add seg.Name inferred !cell
                                        walk inferred seg.Name deeper
                                    | _ ->
                                        let available = fields |> Map.toList |> List.map fst
                                        Error [ UnknownSketchMember(curName, seg.Name, available, seg.Span) ]
                            | None ->
                                Error [ NotASketch(curTy, seg.Span) ]
                    walk headTy head.Name rest
                    |> Result.map (fun finalTy ->
                        Tast.mkTExpr (Tast.TEPath idents) finalTy expr.Span)

        | EStackTop | ECall _ | EMatch _ ->
            // Surface-only / legacy nodes that don't exist in the
            // typed-block program shape. Surface a generic mismatch.
            Error [ InvalidOperand("unsupported AST node in typechecker", expr.Span) ]

    and check (env: TypeEnv) (expected: Type.T) (expr: Expr) : Result<Tast.TExpr, TypeError list> =
        match expr.Node, expected with
        | ELambda(p, anno, body), Type.Fun(a, b) ->
            // The annotation, if present, must match the expected param.
            // `<>` here uses structural equality on Type.T — adequate
            // for non-sketch types and for the common case where both
            // sides are the same refinement.
            match anno with
            | Some t when t <> a ->
                Error [ AnnotationConflict(t, a, p.Span) ]
            | _ ->
                // Open a refinement cell when the expected param type is
                // Sketch / Loop / Primitive, mirroring the infer-mode
                // lambda case. Lets refinement inference run inside
                // checked positions too.
                let cellInfo =
                    match a with
                    | Type.Sketch m    -> Some (ref m, RKSketch)
                    | Type.Loop m      -> Some (ref m, RKLoop)
                    | Type.Primitive m -> Some (ref m, RKPrimitive)
                    | _                -> None
                cellInfo |> Option.iter (fun (c, k) -> pushCell p.Name c k)
                let env' = bind env p.Name a
                let bodyResult = check env' b body
                cellInfo |> Option.iter (fun _ -> popCell ())
                bodyResult |> Result.map (fun tBody ->
                    let resolvedParam =
                        match cellInfo with
                        | Some (c, k) -> wrapRefinement k !c
                        | None        -> a
                    Tast.mkTExpr (Tast.TELambda(p, resolvedParam, tBody)) (Type.Fun(resolvedParam, b)) expr.Span)
        | _ ->
            // Default: infer then verify. Use width-subtype for sketches
            // so that a refined sketch (`Sketch { outer: Field, ... }`)
            // satisfies a parameter typed as a generic sketch
            // (`Sketch Map.empty`) — see `Type.isSubtypeOf`.
            infer env expr |> Result.bind (fun ti ->
                if Type.isSubtypeOf ti.Type expected then Ok ti
                else
                    // Sketch-vs-sketch mismatches get a richer error
                    // listing exactly which required members are missing
                    // and which the actual value provides. Falls through
                    // to plain TypeMismatch for any other shape.
                    match expected, ti.Type with
                    | Type.Sketch required, Type.Sketch actual ->
                        let missing =
                            required
                            |> Map.toList
                            |> List.filter (fun (k, _) -> not (Map.containsKey k actual))
                            |> List.map fst
                        let available = actual |> Map.toList |> List.map fst
                        Error [ MissingSketchMembers(missing, available, expr.Span) ]
                    | _ ->
                        Error [ TypeMismatch(expected, ti.Type, expr.Span) ])

    // ── Helpers used by infer ──────────────────────────────────────────────

    /// Accept numeric Scalar or Field for any child position that lifts to a
    /// MathIR expression (numbers → const nodes; fields pass through).
    /// Mirrors `Eval.liftToExpr`.
    and private checkScalarOrField (env: TypeEnv) (e: Expr) : Result<Tast.TExpr, TypeError list> =
        infer env e |> Result.bind (fun te ->
            if te.Type = Type.Scalar || Type.isSubtypeOf te.Type Type.Field then Ok te
            else Error [ TypeMismatch(Type.Field, te.Type, e.Span) ])

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
            let blockTy = lastTy |> Option.defaultValue Type.Unit
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
    /// before the AST is checked. Clears the refinement-cell stack so
    /// state from any earlier `elaborate` call (including ones that
    /// errored partway through) doesn't leak into this run.
    let elaborate (env: TypeEnv) (expr: Expr) : Result<Tast.TExpr, TypeError list> =
        cellStack <- []
        infer env expr
