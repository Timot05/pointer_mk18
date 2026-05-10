namespace Server.Lang

// ---------------------------------------------------------------------------
// Eval.fs — port of pointer_mk19/lib/block_eval.ml.
//
// AST-walking interpreter. Single-block (no notebook driver yet); the
// notebook layer that threads @output → @input across blocks is a follow-up.
//
// Core flow:
//   evalExpr → match on ExprNode → recurse / dispatch
//   evalStmt → SLet binds, SExpr discards result, stack ops error for now
//   runProgram → lex + parse + eval each statement, return last value + IR
// ---------------------------------------------------------------------------

module Eval =

    open Token
    open Ast
    open Value

    let private liftNumberToField (ir: MathIr.MathIR) (v: Value) : Value =
        match v with
        | VNumber n -> VField (ir.Constant n)
        | other -> other

    let private numericBinary (op: BinaryOp) (a: float) (b: float) : Result<float, string> =
        match op with
        | BinaryOp.Add -> Ok (a + b)
        | BinaryOp.Sub -> Ok (a - b)
        | BinaryOp.Mul -> Ok (a * b)
        | BinaryOp.Div -> Ok (a / b)
        | BinaryOp.Min -> Ok (min a b)
        | BinaryOp.Max -> Ok (max a b)
        | _ -> Error "operator not valid on numbers"

    let private fieldBinaryOp (op: BinaryOp) : MathIr.Binary option =
        match op with
        | BinaryOp.Add -> Some MathIr.Binary.Add
        | BinaryOp.Sub -> Some MathIr.Binary.Sub
        | BinaryOp.Mul -> Some MathIr.Binary.Mul
        | BinaryOp.Div -> Some MathIr.Binary.Div
        | BinaryOp.Min -> Some MathIr.Binary.Min
        | BinaryOp.Max -> Some MathIr.Binary.Max
        | _ -> None

    let private fieldBinary (ir: MathIr.MathIR) (op: BinaryOp) (a: MathIr.Expr) (b: MathIr.Expr) : Result<MathIr.Expr, string> =
        match fieldBinaryOp op with
        | Some k -> Ok (ir.Binary(k, a, b))
        | None -> Error "operator not valid on fields"

    let private numericUnary (op: UnaryOp) (a: float) : Result<float, string> =
        match op with
        | UnaryOp.Neg    -> Ok (-a)
        | UnaryOp.Sqrt   -> Ok (sqrt a)
        | UnaryOp.Abs    -> Ok (abs a)
        | UnaryOp.Square -> Ok (a * a)
        | UnaryOp.Sin    -> Ok (sin a)
        | UnaryOp.Cos    -> Ok (cos a)
        | UnaryOp.Tan    -> Ok (tan a)

    let private fieldUnaryOp (op: UnaryOp) : MathIr.Unary =
        match op with
        | UnaryOp.Neg    -> MathIr.Unary.Neg
        | UnaryOp.Sqrt   -> MathIr.Unary.Sqrt
        | UnaryOp.Abs    -> MathIr.Unary.Abs
        | UnaryOp.Square -> MathIr.Unary.Square
        | UnaryOp.Sin    -> MathIr.Unary.Sin
        | UnaryOp.Cos    -> MathIr.Unary.Cos
        | UnaryOp.Tan    -> MathIr.Unary.Tan

    let private axisOf (a: Axis) : MathIr.Axis =
        match a with
        | AxisX -> MathIr.Axis.X
        | AxisY -> MathIr.Axis.Y
        | AxisZ -> MathIr.Axis.Z

    let rec evalExpr (ctx: EvalContext) (e: Expr) : Result<Value, EvalError> =
        match e.Node with
        | EUnit -> Ok VUnit
        | ENumber n -> Ok (VNumber n)
        | EBool b -> Ok (VBool b)
        | EString s -> Ok (VString s)
        | EVar id ->
            match id.IdentKind with
            | Internal ->
                // `@name` is always a builtin handle — never an env binding.
                // Returning a fresh `VBuiltin` here makes `@sphere`, `@view`,
                // etc. first-class values that curry through `EApply` /
                // pipes (`x |> @sphere`).
                match Builtins.arityOf id.Name with
                | Some arity ->
                    Ok (VBuiltin { Name = id.Name; Arity = arity; AccArgs = [] })
                | None ->
                    evalError id.Span (sprintf "unknown builtin '@%s'" id.Name)
            | User ->
                match envLookup ctx.Env id.Name with
                | Some v -> Ok v
                | None ->
                    evalError id.Span (sprintf "unbound identifier '%s'" id.Name)
        | EPath idents ->
            match idents with
            | [] -> evalError e.Span "empty path"
            | head :: rest ->
                match envLookup ctx.Env head.Name with
                | None -> evalError head.Span (sprintf "unbound identifier '%s'" head.Name)
                | Some headValue ->
                    let mutable cur = Ok headValue
                    let mutable trail = rest
                    while (match cur with Ok _ -> true | _ -> false) && not trail.IsEmpty do
                        let seg = List.head trail
                        trail <- List.tail trail
                        match cur with
                        | Ok (VRecord fields) ->
                            match List.tryFind (fun (n, _) -> n = seg.Name) fields with
                            | Some (_, v) -> cur <- Ok v
                            | None -> cur <- evalError seg.Span (sprintf "record has no field '%s'" seg.Name)
                        | Ok _ ->
                            cur <- evalError seg.Span (sprintf "value is not a record (cannot read '.%s')" seg.Name)
                        | Error _ -> ()
                    cur
        | EStackTop ->
            evalError e.Span "stack-top (`pop`) requires a notebook context"
        | ELambda(param, _paramAnno, body) ->
            Ok (VClosure { Param = param.Name; Body = body; Captured = ctx.Env })
        | EApply(fnExpr, argExpr) ->
            evalExpr ctx fnExpr >>= fun fnVal ->
            evalExpr ctx argExpr >>= fun argVal ->
                applyValue ctx e.Span fnVal argVal
        | EBlock stmts -> evalBlock ctx stmts e.Span
        | EList items -> evalList ctx items
        | ETuple items -> evalList ctx items
        | ECall(callee, args) -> evalCall ctx e.Span callee args
        | EUnary(op, inner) ->
            evalExpr ctx inner >>= fun v ->
                match v with
                | VNumber n ->
                    match numericUnary op n with
                    | Ok r -> Ok (VNumber r)
                    | Error msg -> evalError e.Span msg
                | VField f ->
                    Ok (VField (ctx.Ir.Unary(fieldUnaryOp op, f)))
                | _ ->
                    evalError e.Span "unary op expects a number or field"
        | EBinary(op, l, r) -> evalBinary ctx e.Span op l r
        | EAxis a ->
            // Spatial axis variable — resolves to the kernel's per-sample
            // position component. Always wrapped as a Field so it composes
            // with numeric arithmetic (which lifts numbers to consts).
            Ok (VField (ctx.Ir.Var(axisOf a)))
        | ERemapAxes(target, nx, ny, nz) ->
            evalExpr ctx target >>= fun tv ->
            evalExpr ctx nx >>= fun xv ->
            evalExpr ctx ny >>= fun yv ->
            evalExpr ctx nz >>= fun zv ->
                let liftField span v =
                    match v with
                    | VField f -> Ok f
                    | VNumber n -> Ok (ctx.Ir.Constant n)
                    | _ -> evalError span "remap-axes expects field/number operands"
                liftField e.Span tv >>= fun t ->
                liftField e.Span xv >>= fun nx ->
                liftField e.Span yv >>= fun ny ->
                liftField e.Span zv >>= fun nz ->
                    Ok (VField (ctx.Ir.RemapAxes(t, nx, ny, nz)))
        | EMatch _ ->
            evalError e.Span "match expressions are not yet supported"

    and applyValue (ctx: EvalContext) (callSpan: Span) (fn: Value) (arg: Value) : Result<Value, EvalError> =
        match fn with
        | VClosure clo ->
            let child = newEnv (Some clo.Captured)
            envBind child clo.Param arg
            let savedEnv = ctx.Env
            ctx.Env <- child
            let result = evalExpr ctx clo.Body
            ctx.Env <- savedEnv
            result
        | VBuiltin b ->
            let acc = b.AccArgs @ [ arg ]
            if List.length acc < b.Arity then
                Ok (VBuiltin { b with AccArgs = acc })
            else
                dispatchBuiltin ctx callSpan b.Name acc
        | _ ->
            evalError callSpan "value is not callable"

    and dispatchBuiltin (ctx: EvalContext) (span: Span) (name: string) (args: Value list) : Result<Value, EvalError> =
        if Builtins.isSpecial name then
            match name, args with
            | "print", [ VString n; v ] -> ctx.Specials.Print span n v
            | "print", _ -> evalError span "print expects (string, value)"
            | "debug", [ v ] -> ctx.Specials.Debug span v
            | _ -> evalError span (sprintf "unexpected arity for special '%s'" name)
        else
            let argVals = args |> List.map Builtins.APos
            Builtins.dispatch ctx name argVals span

    and evalBlock (ctx: EvalContext) (stmts: Stmt list) (span: Span) : Result<Value, EvalError> =
        let scope = newEnv (Some ctx.Env)
        let savedEnv = ctx.Env
        ctx.Env <- scope
        let mutable last : Result<Value, EvalError> = Ok VUnit
        let mutable i = 0
        let stmtArr = List.toArray stmts
        let mutable halt = false
        while not halt && i < stmtArr.Length do
            match evalStmt ctx stmtArr.[i] with
            | Ok v -> last <- Ok v
            | Error e ->
                last <- Error e
                halt <- true
            i <- i + 1
        ctx.Env <- savedEnv
        match last with
        | Ok v -> Ok v
        | Error _ -> last

    and evalList (ctx: EvalContext) (items: Expr list) : Result<Value, EvalError> =
        let mutable acc : Value list = []
        let mutable err : EvalError option = None
        for item in items do
            if err.IsNone then
                match evalExpr ctx item with
                | Ok v -> acc <- v :: acc
                | Error e -> err <- Some e
        match err with
        | Some e -> Error e
        | None ->
            // For both List and Tuple we use VRecord with int keys? mk19 keeps them
            // as separate runtime kinds; for first cut we pack into a record indexed
            // by stringified position. Real list/tuple types arrive when they're
            // needed.
            let pairs =
                List.rev acc
                |> List.mapi (fun i v -> (string i, v))
            Ok (VRecord pairs)

    and evalCall (ctx: EvalContext) (span: Span) (callee: Ident) (args: Arg list) : Result<Value, EvalError> =
        match callee.IdentKind with
        | Internal -> evalInternalCall ctx span callee args
        | User -> evalUserCall ctx span callee args

    and evalArgs (ctx: EvalContext) (args: Arg list) : Result<Builtins.ArgValue list, EvalError> =
        let mutable acc : Builtins.ArgValue list = []
        let mutable err : EvalError option = None
        for arg in args do
            if err.IsNone then
                match arg with
                | Positional e ->
                    match evalExpr ctx e with
                    | Ok v -> acc <- (Builtins.APos v) :: acc
                    | Error r -> err <- Some r
                | Named(name, e) ->
                    match evalExpr ctx e with
                    | Ok v -> acc <- (Builtins.ANamed(name.Name, v)) :: acc
                    | Error r -> err <- Some r
        match err with
        | Some e -> Error e
        | None -> Ok (List.rev acc)

    and evalInternalCall (ctx: EvalContext) (span: Span) (callee: Ident) (args: Arg list) : Result<Value, EvalError> =
        // `@input` / `@output` / `@view` were retired in favour of
        // `let import` / `let pub` declarations. The ECall AST shape is
        // unreachable from parsed sources after the parens-call retirement,
        // but this dispatcher is kept on disk for `@print` / `@debug`.
        match callee.Name with
        | "print" ->
            evalArgs ctx args >>= fun argVals ->
                let pos =
                    argVals
                    |> List.choose (function Builtins.APos v -> Some v | _ -> None)
                match pos with
                | [ VString tag; value ] -> ctx.Specials.Print span tag value
                | _ -> evalError span "@print expects (string, value)"
        | "debug" ->
            evalArgs ctx args >>= fun argVals ->
                let pos =
                    argVals
                    |> List.choose (function Builtins.APos v -> Some v | _ -> None)
                match pos with
                | [ value ] -> ctx.Specials.Debug span value
                | _ -> evalError span "@debug expects exactly one argument"
        | _ ->
            evalArgs ctx args >>= fun argVals ->
                Builtins.dispatch ctx callee.Name argVals span

    and evalUserCall (ctx: EvalContext) (span: Span) (callee: Ident) (args: Arg list) : Result<Value, EvalError> =
        // User calls are sugar: f(a, b) = ((f a) b). Look up the callee, then
        // fold-apply the positional args left-to-right. Named args are not
        // supported on user-defined functions in this round.
        let posOnly =
            args
            |> List.forall (function Positional _ -> true | _ -> false)
        if not posOnly then
            evalError span "named arguments are only allowed on builtins"
        else
            match envLookup ctx.Env callee.Name with
            | None -> evalError callee.Span (sprintf "unbound identifier '%s'" callee.Name)
            | Some fnVal ->
                let mutable acc : Result<Value, EvalError> = Ok fnVal
                for arg in args do
                    match arg, acc with
                    | Positional e, Ok current ->
                        match evalExpr ctx e with
                        | Ok argVal -> acc <- applyValue ctx span current argVal
                        | Error r -> acc <- Error r
                    | _, Error _ -> ()
                    | _ -> ()
                acc

    and evalBinary (ctx: EvalContext) (span: Span) (op: BinaryOp) (left: Expr) (right: Expr) : Result<Value, EvalError> =
        match op with
        | BinaryOp.Pipe ->
            // x |> f  ===  f x
            evalExpr ctx left >>= fun lv ->
                evalExpr ctx right >>= fun rv ->
                    applyValue ctx span rv lv
        | BinaryOp.Compose ->
            evalError span "compose (>>) is not yet supported"
        | _ ->
            evalExpr ctx left >>= fun lv ->
                evalExpr ctx right >>= fun rv ->
                    match lv, rv with
                    | VNumber a, VNumber b ->
                        match numericBinary op a b with
                        | Ok n -> Ok (VNumber n)
                        | Error msg -> evalError span msg
                    | VField a, VField b ->
                        match fieldBinary ctx.Ir op a b with
                        | Ok e -> Ok (VField e)
                        | Error msg -> evalError span msg
                    | VField a, VNumber b ->
                        let bExpr = ctx.Ir.Constant b
                        match fieldBinary ctx.Ir op a bExpr with
                        | Ok e -> Ok (VField e)
                        | Error msg -> evalError span msg
                    | VNumber a, VField b ->
                        let aExpr = ctx.Ir.Constant a
                        match fieldBinary ctx.Ir op aExpr b with
                        | Ok e -> Ok (VField e)
                        | Error msg -> evalError span msg
                    | _ ->
                        evalError span "binary operator: incompatible operand types"

    and evalStmt (ctx: EvalContext) (stmt: Stmt) : Result<Value, EvalError> =
        match stmt with
        | SLet(names, value) ->
            evalExpr ctx value >>= fun v ->
                match names with
                | [ single ] ->
                    envBind ctx.Env single.Name v
                    Ok v
                | _ ->
                    Error { Message = "multi-name let bindings are not yet supported"; Span = value.Span }
        | SImport _ ->
            // Pre-populated by the notebook driver before this block runs;
            // the statement itself is a no-op at eval time.
            Ok VUnit
        | SExport(name, value) ->
            // Identical binding behaviour to a single-name `SLet`. The
            // export mark is read post-eval by the notebook driver from
            // the AST + final env (see `AstQueries.collectExports`).
            evalExpr ctx value >>= fun v ->
                envBind ctx.Env name.Name v
                Ok v
        | SExpr e -> evalExpr ctx e
        | SDup sp | SSwap sp | SRotate sp ->
            evalError sp "stack operators (dup/swap/rotate) require a notebook context"

    /// Evaluate a program top to bottom. Returns the active MathIR plus the
    /// last value produced. Runs with `unboundSpecials` unless the caller
    /// supplies their own.
    let runProgram (source: string) (specials: Specials) : Result<MathIr.MathIR * Value, EvalError> =
        match Parser.parseProgram source with
        | Error e -> Error { Message = e.Message; Span = e.Span }
        | Ok stmts ->
            let ctx = createContext ()
            ctx.Specials <- specials
            let mutable last : Value = VUnit
            let mutable err : EvalError option = None
            let stmtArr = List.toArray stmts
            let mutable i = 0
            while err.IsNone && i < stmtArr.Length do
                match evalStmt ctx stmtArr.[i] with
                | Ok v -> last <- v
                | Error e -> err <- Some e
                i <- i + 1
            match err with
            | Some e -> Error e
            | None -> Ok (ctx.Ir, last)

    /// Convenience overload using the no-op specials.
    let run (source: string) : Result<MathIr.MathIR * Value, EvalError> =
        runProgram source unboundSpecials

    /// Parse + evaluate against an existing context. Used by the notebook
    /// driver to evaluate input-wiring expressions and per-block source
    /// without spawning a fresh MathIR / fresh env.
    let evalSourceInContext (ctx: EvalContext) (source: string) : Result<Value, EvalError> =
        match Parser.parseProgram source with
        | Error e -> Error { Message = e.Message; Span = e.Span }
        | Ok stmts ->
            let mutable last : Value = VUnit
            let mutable err : EvalError option = None
            let arr = List.toArray stmts
            let mutable i = 0
            while err.IsNone && i < arr.Length do
                match evalStmt ctx arr.[i] with
                | Ok v -> last <- v
                | Error e -> err <- Some e
                i <- i + 1
            match err with
            | Some e -> Error e
            | None -> Ok last
