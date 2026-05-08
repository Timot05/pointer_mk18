namespace Server.Lang

// ---------------------------------------------------------------------------
// NotebookEval.fs — port of pointer_mk19/lib/notebook_eval.ml.
//
// Drives an ordered list of blocks against one shared MathIR. Each block
// runs with a `Specials` record that captures `@input`/`@output`/`@view`/...
// into a per-block scratchpad; afterwards the stitching heuristic reduces
// that scratchpad to a single Value bound under the block's name in the
// shared scope, ready for the next block to read.
//
// `eval` produces a full per-block trace + final scope. `compileView` picks
// one block's view and returns the kernel-ready (MathIR, Expr) pair.
// ---------------------------------------------------------------------------

module NotebookEval =

    open Token
    open Value
    open Notebook

    /// One output → bind that value as block.name.
    /// Zero outputs + one input  → bind that input's value.
    /// Otherwise → bind a Record of outputs ++ inputs-not-shadowed-by-outputs.
    let private stitch
            (outputs: (string * Value) list)
            (inputsUsed: (string * Value) list) : Value =
        match outputs, inputsUsed with
        | [ (_, v) ], _ -> v
        | [], [ (_, v) ] -> v
        | _, _ ->
            let outNames = outputs |> List.map fst |> Set.ofList
            let extras =
                inputsUsed |> List.filter (fun (n, _) -> not (outNames.Contains n))
            VRecord (outputs @ extras)

    /// Implicit view: if `@view` was never called and exactly one Field
    /// (or Sketch — placeholder) output exists, that's the view. Otherwise
    /// no view.
    let private implicitView
            (explicit: Value option)
            (outputs: (string * Value) list) : Value option =
        match explicit with
        | Some _ -> explicit
        | None ->
            let renderable =
                outputs
                |> List.choose (fun (_, v) ->
                    match v with
                    | VField _ | VSketch _ -> Some v
                    | _ -> None)
            match renderable with
            | [ v ] -> Some v
            | _ -> None

    /// Evaluate one block in isolation — fresh per-block env parented at
    /// `priorScope`, fresh per-block Specials closure that records into
    /// mutable scratchpad state.
    let private runBlock
            (ir: MathIr.MathIR)
            (priorScope: Env)
            (block: Block) : BlockEval =

        let outputs = ResizeArray<string * Value>()
        let trace = ResizeArray<IoBinding>()
        let inputsUsed = ResizeArray<string * Value>()
        let mutable view : Value option = None

        match block.Kind with
        | ScriptBlock script ->
            let inputsText = Map.ofList script.Inputs

            let resolveInput (span: Span) (name: string) : Result<Value, EvalError> =
                match Map.tryFind name inputsText with
                | None ->
                    evalError span (sprintf "@input(\"%s\") is not wired" name)
                | Some src ->
                    // Input expressions evaluate against priorScope ONLY,
                    // not the calling block's locals. unboundSpecials so
                    // input expressions can't recursively call @input.
                    let inputCtx = createContextWith ir (newEnv (Some priorScope))
                    match Eval.evalSourceInContext inputCtx src with
                    | Error e -> Error e
                    | Ok v ->
                        inputsUsed.Add(name, v)
                        trace.Add { Kind = InputIo; Name = name; Span = span; Value = v }
                        Ok v

            let recordOutput (sp: Span) (n: string) (v: Value) =
                outputs.Add(n, v)
                trace.Add { Kind = OutputIo; Name = n; Span = sp; Value = v }
                Ok VUnit

            let recordView (sp: Span) (v: Value) =
                match view with
                | Some _ -> evalError sp "@view called more than once in this block"
                | None ->
                    view <- Some v
                    trace.Add { Kind = ViewIo; Name = ""; Span = sp; Value = v }
                    Ok VUnit

            let recordPrint (sp: Span) (tag: string) (v: Value) =
                trace.Add { Kind = PrintIo; Name = tag; Span = sp; Value = v }
                Ok VUnit

            let recordDebug (sp: Span) (v: Value) =
                trace.Add { Kind = DebugIo; Name = ""; Span = sp; Value = v }
                Ok VUnit

            let blockSpecials : Specials = {
                Input  = resolveInput
                Output = recordOutput
                View   = recordView
                Print  = recordPrint
                Debug  = recordDebug
            }

            let blockCtx = createContextWith ir (newEnv (Some priorScope))
            blockCtx.Specials <- blockSpecials
            let result = Eval.evalSourceInContext blockCtx script.Source

            let outputsList = List.ofSeq outputs
            let err =
                match result with
                | Ok _ -> None
                | Error e -> Some e
            {
              Id = block.Id
              Outputs = outputsList
              IoTrace = List.ofSeq trace
              InputsUsed = inputsUsed |> Seq.map fst |> List.ofSeq
              View = implicitView view outputsList
              Error = err
            }

        | SketchBlock data ->
            // Sketch blocks have no DSL source. The authored ActionSketch
            // (with constraints) is wrapped as a VSketch and exposed as
            // the block's single output and its auto-view. No slots are
            // allocated and the shared MathIR is left untouched —
            // consumer-side builtins (future @sketch_distance) handle that.
            let sketchValue = VSketch { Sketch = data.Sketch; Plane = data.Plane }
            {
              Id = block.Id
              Outputs = [ block.Name, sketchValue ]
              IoTrace = []
              InputsUsed = []
              View = Some sketchValue
              Error = None
            }

    /// Walk the notebook in declaration order. After each block, stitch its
    /// outputs/inputs into a single value bound at `block.Name` in the
    /// shared scope. Blocks that error still contribute whatever bindings
    /// did succeed — matches mk19's continue-on-error.
    let eval (notebook: Notebook) : Evaluation =
        let ir = MathIr.MathIR()
        let scope = newEnv None
        let perBlock = ResizeArray<BlockEval>()

        for block in notebook.Blocks do
            let result = runBlock ir scope block
            // Recover per-block successful inputs from the trace for stitching.
            let inputsUsedPairs =
                result.IoTrace
                |> List.choose (function
                    | { Kind = InputIo; Name = n; Value = v } -> Some (n, v)
                    | _ -> None)
            let stitched = stitch result.Outputs inputsUsedPairs
            envBind scope block.Name stitched
            perBlock.Add(result)

        let scopeMap =
            scope.Bindings
            |> Seq.map (fun kv -> kv.Key, kv.Value)
            |> Map.ofSeq

        {
          PerBlock = List.ofSeq perBlock
          Scope = scopeMap
          Ir = ir
        }

    /// Pick a block's view and return (MathIR, root Expr) for the kernel.
    /// `surfaceBlock = Some name` selects by name; `None` picks the last
    /// block whose view is a Field. Only Fields are supported — Sketch
    /// lowering is out of scope this round.
    let compileView
            (notebook: Notebook)
            (surfaceBlock: string option) : Result<MathIr.MathIR * MathIr.Expr, string> =
        let result = eval notebook

        let pickedBlockEval =
            match surfaceBlock with
            | Some name ->
                notebook.Blocks
                |> List.tryFind (fun b -> b.Name = name)
                |> Option.bind (fun b ->
                    result.PerBlock |> List.tryFind (fun be -> be.Id = b.Id))
            | None ->
                result.PerBlock
                |> List.rev
                |> List.tryFind (fun be ->
                    match be.View with
                    | Some (VField _) -> true
                    | _ -> false)

        match pickedBlockEval with
        | None -> Error "no renderable view in notebook"
        | Some be ->
            match be.View with
            | Some (VField root) -> Ok (result.Ir, root)
            | Some _ -> Error "selected block's view is not a Field"
            | None -> Error "selected block has no view"
