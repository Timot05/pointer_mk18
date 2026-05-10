namespace Server.Lang

// ---------------------------------------------------------------------------
// NotebookCompose.fs — lifts a `Notebook` into a single `Ast.Expr` and the
// envs the typechecker and evaluator need.
//
// Architecture:
//   * Each native block becomes a `SLet` whose RHS is a saturated call
//     to the primitive named after its spec, e.g.
//        let block_2 = translate 2 0 0 block_0
//   * Each sketch block is pre-bound in the value/type envs as a typed
//     `Sketch` value; it doesn't appear in the AST. Downstream blocks
//     reference it with a plain `EVar`.
//   * The trailing `SExpr` selects the render root — last block whose
//     output type is `Field`.
//
// `compose` is pure: it builds the AST + type env without instantiating
// a MathIR. `evaluate` is the second phase, building a fresh MathIR
// and seeding the value env with primitive closures + sketch payloads
// before running `Eval.evalExpr`. This keeps typecheck cheap (no MathIR
// allocation) and makes errors surface before any evaluation work.
// ---------------------------------------------------------------------------

module NotebookCompose =

    open Token
    open Ast
    open Value
    open Notebook

    /// Output of the pure compose phase. Carries everything the
    /// typechecker needs without touching the kernel-side MathIR.
    /// `BlockSpans` maps each block's id to a unique synthetic span we
    /// stamp onto its sub-AST, so typecheck errors can be routed back to
    /// the block they came from.
    type Composed = {
        Ast: Expr
        TypeEnv: Typecheck.TypeEnv
        BlockNames: Map<BlockId, string>
        BlockSpans: Map<BlockId, Span>
        BlockOutputs: Map<BlockId, Type.T>
    }

    // ── AST construction helpers ───────────────────────────────────────────

    let private noSpan : Span = { Start = 0; Stop = 0 }

    /// Synthesise a unique span per `BlockId` so typecheck errors can be
    /// routed back. We allocate `Start = blockId, Stop = blockId` — the
    /// span is meaningless as a source location but its `Start` field
    /// makes it queryable through the `BlockSpans` map.
    let private spanForBlock (id: BlockId) : Span =
        { Start = id; Stop = id }

    let private userAt (name: string) (sp: Span) : Ident =
        { Name = name; IdentKind = User; Span = sp }

    let private user (name: string) : Ident = userAt name noSpan

    let private mkAt (sp: Span) node : Expr = { Node = node; Span = sp }
    let private mk node : Expr = mkAt noSpan node
    let private varE name : Expr = mk (EVar (user name))
    let private varEAt sp name : Expr = mkAt sp (EVar (userAt name sp))
    let private numEAt sp n : Expr = mkAt sp (ENumber n)
    let private numE n : Expr = mk (ENumber n)

    /// `EApply` chain — `applyChain f [a; b; c]` yields `((f a) b) c`.
    let private applyChain (callee: Expr) (args: Expr list) : Expr =
        args |> List.fold (fun acc arg -> mk (EApply(acc, arg))) callee

    let private applyChainAt (sp: Span) (callee: Expr) (args: Expr list) : Expr =
        args |> List.fold (fun acc arg -> mkAt sp (EApply(acc, arg))) callee

    /// Sentinel name we plant into the AST when a ref input is unwired.
    /// Resolves to `UndefinedVar` at typecheck — clean signal back to the
    /// UI that this block has a missing input.
    let [<Literal>] private UNWIRED_PLACEHOLDER = "<unwired>"

    // ── Type signatures for native specs ───────────────────────────────────

    /// Curried function type derived from a spec's typed interface.
    /// `sphere : Scalar -> Field`, `translate : Scalar -> Scalar -> Scalar -> Field -> Field`.
    let private specFunType (spec: BlockSpec.BlockSpec) : Type.T =
        let typed = BlockSpec.typedInterface spec
        let inputs = typed.Params |> List.map (fun p -> p.Type)
        Type.curried inputs typed.Output

    let private specOutputType (spec: BlockSpec.BlockSpec) : Type.T =
        (BlockSpec.typedInterface spec).Output

    // ── Compose ────────────────────────────────────────────────────────────

    /// Build the notebook AST + the type environment. Pure; no MathIR
    /// involvement. The block name → identifier mapping is preserved so
    /// downstream tools (typecheck error reporting, ref-drop UI) can
    /// recover which block an AST node came from.
    let compose (notebook: Notebook) : Composed =
        // Seed type env with every primitive's typed signature.
        let mutable typeEnv : Typecheck.TypeEnv = Map.empty
        for spec in BlockSpec.all () do
            typeEnv <- Map.add spec.Name (specFunType spec) typeEnv

        let blockNames =
            notebook.Blocks
            |> List.map (fun b -> b.Id, b.Name)
            |> Map.ofList

        let blockSpans =
            notebook.Blocks
            |> List.map (fun b -> b.Id, spanForBlock b.Id)
            |> Map.ofList

        let mutable blockOutputs : Map<BlockId, Type.T> = Map.empty

        // Pre-seed sketch block names in the type env. They never become
        // let-bindings; downstream `EVar` lookups resolve against this
        // entry directly.
        for block in notebook.Blocks do
            match block.Body with
            | SketchBody _ ->
                typeEnv <- Map.add block.Name Type.Sketch typeEnv
                blockOutputs <- Map.add block.Id Type.Sketch blockOutputs
            | _ -> ()

        // Build per-block let-bindings for the native blocks. Walk in
        // declaration order so refs only reach upstream names.
        let stmts = ResizeArray<Stmt>()
        let mutable renderTarget : string option = None

        for block in notebook.Blocks do
            match block.Body with
            | SketchBody _ ->
                // Sketches are pre-bound — nothing to add to the AST.
                ()
            | NativeBody(specName, args) ->
                let bsp = spanForBlock block.Id
                match BlockSpec.tryFind specName with
                | None ->
                    // Unknown spec — surface via an undefined ref so the
                    // typechecker reports it cleanly.
                    let call = applyChainAt bsp (varEAt bsp specName) []
                    stmts.Add(SLet([ userAt block.Name bsp ], call))
                | Some spec ->
                    let typed = BlockSpec.typedInterface spec

                    // Build args in the order the spec declares them.
                    let argExprs =
                        typed.Params
                        |> List.map (fun p ->
                            match Map.tryFind p.Name args with
                            | Some (ArgScalar n) ->
                                numEAt bsp n
                            | Some (ArgRef (Some refId)) ->
                                match Map.tryFind refId blockNames with
                                | Some n -> varEAt bsp n
                                | None -> varEAt bsp UNWIRED_PLACEHOLDER
                            | Some (ArgRef None)
                            | None ->
                                varEAt bsp UNWIRED_PLACEHOLDER)

                    let call = applyChainAt bsp (varEAt bsp specName) argExprs
                    stmts.Add(SLet([ userAt block.Name bsp ], call))

                    let outTy = specOutputType spec
                    blockOutputs <- Map.add block.Id outTy blockOutputs
                    if outTy = Type.Field then
                        renderTarget <- Some block.Name

        // If the last Field-typed block exists, select it as the render
        // root. Otherwise the AST has no trailing expression and the
        // block returns `()` which surfaces as "no renderable output".
        match renderTarget with
        | Some n -> stmts.Add(SExpr (varE n))
        | None -> ()

        { Ast = mk (EBlock (List.ofSeq stmts))
          TypeEnv = typeEnv
          BlockNames = blockNames
          BlockSpans = blockSpans
          BlockOutputs = blockOutputs }

    // ── Evaluation ─────────────────────────────────────────────────────────

    /// Build the value env (closures for every primitive + sketch
    /// payloads) on a fresh MathIR and run the composed AST.
    let private buildValueEnv (notebook: Notebook) (ctx: EvalContext) : unit =
        // Each primitive's body evaluates to a `VClosure`. We never apply
        // it here — we just bind the closure under the spec's name so
        // `EVar specName` in the composed AST resolves to it later.
        for spec in BlockSpec.all () do
            match Eval.evalExpr ctx spec.Body with
            | Ok v -> envBind ctx.Env spec.Name v
            | Error _ -> ()   // shouldn't happen for hand-built specs

        // Sketch payloads.
        for block in notebook.Blocks do
            match block.Body with
            | SketchBody data ->
                envBind ctx.Env block.Name
                    (VSketch { Sketch = data.Sketch; Plane = data.Plane })
            | _ -> ()

    type EvalResult = {
        Ir: MathIr.MathIR
        Value: Value
    }

    /// Pre-typecheck step has already passed — build the MathIR + eval
    /// the composed AST.
    let evaluate (notebook: Notebook) (composed: Composed) : Result<EvalResult, EvalError> =
        let ir = MathIr.MathIR()
        let ctx = createContextWith ir (newEnv None)
        buildValueEnv notebook ctx
        match Eval.evalExpr ctx composed.Ast with
        | Ok v -> Ok { Ir = ir; Value = v }
        | Error e -> Error e

    // ── Public entry points ────────────────────────────────────────────────

    /// Result the editor surfaces: bytes for the kernel (when the whole
    /// pipeline succeeds), plus the per-block error and output-type
    /// maps the UI consults for has-error styling and ref-drop
    /// validation.
    type CompileResult = {
        Bytes: obj option
        BlockErrors: Map<BlockId, string list>
        BlockOutputs: Map<BlockId, Type.T>
        Summary: string option   // first error formatted, for the panel-level banner
    }

    /// Errors don't all have a span tied to a known block. Anything
    /// without a recognisable mapping lands under the `synthetic` block
    /// id `-1` so the panel-level summary can pick it up.
    let private routeErrorsToBlocks
            (composed: Composed)
            (errs: Typecheck.TypeError list) : Map<BlockId, string list> =
        let spanToBlock =
            composed.BlockSpans
            |> Map.toList
            |> List.map (fun (k, v) -> v, k)
            |> Map.ofList
        let mutable acc : Map<BlockId, string list> = Map.empty
        let push (id: BlockId) (msg: string) =
            let prev = Map.tryFind id acc |> Option.defaultValue []
            acc <- Map.add id (prev @ [ msg ]) acc
        for e in errs do
            let span =
                match e with
                | Typecheck.UndefinedVar(_, sp)
                | Typecheck.TypeMismatch(_, _, sp)
                | Typecheck.NotAFunction(_, sp)
                | Typecheck.MissingTypeAnnotation(_, sp)
                | Typecheck.AnnotationConflict(_, _, sp)
                | Typecheck.InvalidOperand(_, sp) -> sp
            let id =
                match Map.tryFind span spanToBlock with
                | Some id -> id
                | None -> -1
            push id (Typecheck.formatError e)
        acc

    /// Full notebook compile — typecheck + (on success) eval + serialise.
    let compile (notebook: Notebook) : CompileResult =
        let composed = compose notebook
        match Typecheck.elaborate composed.TypeEnv composed.Ast with
        | Error errs ->
            let blockErrors = routeErrorsToBlocks composed errs
            let summary =
                errs |> List.tryHead |> Option.map Typecheck.formatError
            { Bytes = None
              BlockErrors = blockErrors
              BlockOutputs = composed.BlockOutputs
              Summary = summary }
        | Ok _ ->
            match evaluate notebook composed with
            | Ok { Ir = ir; Value = VField root } ->
                let bytes =
                    try Some (MathIrCodec.serialize ir root)
                    with _ -> None
                { Bytes = bytes
                  BlockErrors = Map.empty
                  BlockOutputs = composed.BlockOutputs
                  Summary = None }
            | Ok _ ->
                { Bytes = None
                  BlockErrors = Map.empty
                  BlockOutputs = composed.BlockOutputs
                  Summary = Some "notebook produced no Field render root" }
            | Error e ->
                { Bytes = None
                  BlockErrors = Map.empty
                  BlockOutputs = composed.BlockOutputs
                  Summary = Some e.Message }

    /// Older shim for code paths that just want the (ir, root) pair.
    /// Returns `Error` when typecheck fails — call sites outside Editor
    /// (init seed, tests) still use this.
    let compileView
            (notebook: Notebook)
            (_surfaceBlock: string option) : Result<MathIr.MathIR * MathIr.Expr, Typecheck.TypeError list> =
        let composed = compose notebook
        match Typecheck.elaborate composed.TypeEnv composed.Ast with
        | Error errs -> Error errs
        | Ok _ ->
            match evaluate notebook composed with
            | Ok { Ir = ir; Value = VField root } -> Ok (ir, root)
            | Ok _ ->
                Error [ Typecheck.InvalidOperand("notebook produced no Field render root", { Start = 0; Stop = 0 }) ]
            | Error e ->
                Error [ Typecheck.InvalidOperand(e.Message, e.Span) ]
