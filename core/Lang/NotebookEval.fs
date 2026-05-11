namespace Server.Lang

// ---------------------------------------------------------------------------
// NotebookEval.fs — typed-block driver.
//
// For each block in declaration order:
//   1. Look up its spec (native blocks) or recognise it as a sketch.
//   2. Allocate a slot for every scalar parameter the spec declares; bind
//      the param name in the eval env to the slot-backed `VField`.
//   3. Resolve every ref parameter from the prior `Outputs` map; bind it
//      under the param name. Missing wires → record an error and bind
//      `VUnit` (best-effort, matches the previous fail-soft behaviour).
//   4. Evaluate `spec.Body` in that env via `Eval.evalExpr` against the
//      shared `MathIR`. The lambda saturates and returns the block's
//      single output `Value`.
//
// Sketch blocks bypass the spec/eval path: their output is a `VSketch`
// with the authored entities + plane.
// ---------------------------------------------------------------------------

module NotebookEval =

    open Token
    open Value
    open Notebook

    /// Build the eval env for a single native block — every input the spec
    /// declares gets bound under its name. Scalars are inlined as MathIR
    /// `Const` nodes so the actual numeric value flows into the kernel
    /// bytes; recompiling on every edit is cheap, so we don't need a
    /// separate slot table threading through the kernel just to pump the
    /// values. Refs resolve to upstream block outputs.
    let private buildBlockEnv
            (priorOutputs: Map<BlockId, Value>)
            (ctx: EvalContext)
            (specName: string)
            (args: Map<string, BlockArg>)
            (typed: TypeExtract.ExtractedSpec) : Env * EvalError option =
        let env = newEnv None
        let mutable err : EvalError option = None
        for p in typed.Params do
            match Map.tryFind p.Name args with
            | None ->
                if err.IsNone then
                    err <- Some
                        { Message = sprintf "block '%s' missing arg '%s'" specName p.Name
                          Span = { Start = 0; Stop = 0 } }
            | Some (ArgScalar n) ->
                envBind env p.Name (VField (ctx.Ir.Constant n))
            | Some (ArgRef None) ->
                envBind env p.Name VUnit
                if err.IsNone then
                    err <- Some
                        { Message = sprintf "block '%s' input '%s' is not wired" specName p.Name
                          Span = { Start = 0; Stop = 0 } }
            | Some (ArgRef (Some refId)) ->
                match Map.tryFind refId priorOutputs with
                | Some v -> envBind env p.Name v
                | None ->
                    envBind env p.Name VUnit
                    if err.IsNone then
                        err <- Some
                            { Message = sprintf "block '%s' input '%s' references unknown block %d" specName p.Name refId
                              Span = { Start = 0; Stop = 0 } }
        env, err

    /// Saturate the spec's curried lambda by applying every typed parameter
    /// in declaration order. Returns the block's output value.
    let private applyToParams
            (ctx: EvalContext)
            (typed: TypeExtract.ExtractedSpec)
            (env: Env)
            (body: Ast.Expr) : Result<Value, EvalError> =
        // Evaluate the lambda — this gives us a closure capturing `ctx.Env`.
        // We then apply each param in order with the value already bound
        // in `env`.
        let savedEnv = ctx.Env
        ctx.Env <- env
        let result = Eval.evalExpr ctx body
        ctx.Env <- savedEnv
        match result with
        | Error e -> Error e
        | Ok closure ->
            // Walk the param list, applying the bound value at each step.
            let mutable acc = Ok closure
            for p in typed.Params do
                match acc with
                | Error _ -> ()
                | Ok current ->
                    match envLookup env p.Name with
                    | None ->
                        acc <- Error
                            { Message = sprintf "internal: param '%s' missing from env" p.Name
                              Span = { Start = 0; Stop = 0 } }
                    | Some v ->
                        acc <- Eval.applyValue ctx { Start = 0; Stop = 0 } current v
            acc

    /// Drive the notebook in declaration order.
    let eval (notebook: Notebook) : Evaluation =
        let ir = MathIr.MathIR()
        let ctx = createContextWith ir (newEnv None)

        let mutable outputs : Map<BlockId, Value> = Map.empty
        let perBlock = ResizeArray<BlockEval>()

        for block in notebook.Blocks do
            match block.Body with
            | SketchBody data ->
                let v = VSketch { Sketch = data.Sketch; Plane = data.Plane }
                outputs <- Map.add block.Id v outputs
                perBlock.Add { Id = block.Id; Output = Some v; Error = None }
            | NativeBody(specName, args) when specName = "from-sketch" ->
                // From-sketch is special-cased: the spec body is a
                // placeholder (an empty fold). Resolve the upstream
                // SketchBody, lower it through the SAME AST path that
                // `NotebookCompose.compose` uses, then evaluate the
                // resulting AST against the shared MathIR. This keeps
                // sketch semantics in one place
                // (`NotebookCompose.buildFromSketchBody`) — both drivers
                // go: `sketch data → AST → Eval → MathIR`.
                let sketchData =
                    match Map.tryFind "sketch" args with
                    | Some (ArgRef (Some refId)) ->
                        match Map.tryFind refId outputs with
                        | Some (VSketch sv) -> Some { Sketch = sv.Sketch; Plane = sv.Plane }
                        | _ -> None
                    | _ -> None
                match sketchData with
                | None ->
                    perBlock.Add
                        { Id = block.Id
                          Output = None
                          Error =
                              Some
                                  { Message = "from-sketch: 'sketch' arg is missing or not a Sketch"
                                    Span = { Start = 0; Stop = 0 } } }
                | Some data ->
                    let body =
                        NotebookCompose.buildFromSketchBody
                            { Start = 0; Stop = 0 } data
                    match Eval.evalExpr ctx body with
                    | Ok (VField _ as v) ->
                        outputs <- Map.add block.Id v outputs
                        perBlock.Add { Id = block.Id; Output = Some v; Error = None }
                    | Ok _ ->
                        perBlock.Add
                            { Id = block.Id
                              Output = None
                              Error =
                                  Some
                                      { Message = "from-sketch: lowered AST did not evaluate to a Field"
                                        Span = { Start = 0; Stop = 0 } } }
                    | Error e ->
                        perBlock.Add
                            { Id = block.Id
                              Output = None
                              Error = Some e }
            | NativeBody(specName, args) ->
                match BlockSpec.tryFind specName with
                | None ->
                    perBlock.Add
                        { Id = block.Id
                          Output = None
                          Error =
                              Some
                                  { Message = sprintf "unknown block kind '%s'" specName
                                    Span = { Start = 0; Stop = 0 } } }
                | Some spec ->
                    let typed = BlockSpec.typedInterface spec
                    let env, envErr = buildBlockEnv outputs ctx specName args typed
                    match applyToParams ctx typed env spec.Body with
                    | Ok v ->
                        outputs <- Map.add block.Id v outputs
                        perBlock.Add { Id = block.Id; Output = Some v; Error = envErr }
                    | Error e ->
                        let firstErr =
                            match envErr with
                            | Some _ -> envErr
                            | None -> Some e
                        perBlock.Add { Id = block.Id; Output = None; Error = firstErr }

        { PerBlock = List.ofSeq perBlock
          Outputs = outputs
          Ir = ir }

    /// Pick a render target — last block whose output is a Field.
    let pickRenderRoot (eval: Evaluation) : (MathIr.MathIR * MathIr.Expr) option =
        let blocks = eval.PerBlock |> List.rev
        let rec loop (xs: BlockEval list) =
            match xs with
            | [] -> None
            | be :: rest ->
                match be.Output with
                | Some (VField root) -> Some (eval.Ir, root)
                | _ -> loop rest
        loop blocks

    /// Compile-and-pick: convenience for the viewer push path.
    let compileView
            (notebook: Notebook)
            (_surfaceBlock: string option) : Result<MathIr.MathIR * MathIr.Expr, string> =
        let result = eval notebook
        match pickRenderRoot result with
        | Some pair -> Ok pair
        | None -> Error "no renderable Field output in notebook"
