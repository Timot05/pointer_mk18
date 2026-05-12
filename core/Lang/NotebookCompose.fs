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
//   * The trailing `SExpr` selects the render root: every Field-typed
//     block whose `Visibility ≠ VHidden` is unioned together, producing a
//     single combined SDF the kernel renders. With one visible block the
//     union collapses to that block alone (today's behaviour); with N
//     visible blocks the surfaces show simultaneously, blended at any
//     overlap.
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
        /// Names of every Field-typed block that is currently visible
        /// (Visibility ≠ VHidden), in declaration order. The render
        /// root is `union`-folded over these names; each name also
        /// becomes a per-block "view" the kernel renders separately
        /// for tag/colour assignment.
        VisibleFieldNames: string list
        /// Block id of each visible field name, paired by index with
        /// `VisibleFieldNames`. Used to colour views by block id.
        VisibleFieldBlockIds: BlockId list
        /// Visibility kind of each visible field name, paired by index with
        /// `VisibleFieldNames`. Drives the renderer's per-view shading mode.
        VisibleFieldKinds: BlockVisibility list
        /// Colour palette index of each visible field name, paired by index
        /// with `VisibleFieldNames`.
        VisibleFieldColorIndices: int list
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

    let private tryFindBlockArg (specName: string) (paramName: string) (args: Map<string, BlockArg>) : BlockArg option =
        match Map.tryFind paramName args with
        | Some arg -> Some arg
        | None ->
            // Legacy documents used `a` / `b` for boolean operands. New
            // specs use target/tool, but existing blocks should keep wiring.
            match specName, paramName with
            | ("union" | "intersect" | "subtract"), "target" -> Map.tryFind "a" args
            | ("union" | "intersect" | "subtract"), "tool" -> Map.tryFind "b" args
            | ("union" | "intersect" | "subtract"), "radius" -> Some (ArgScalar 0.0)
            | _ -> None

    // ── from-sketch lowering ───────────────────────────────────────────────

    let private planeMap (p: Server.SketchPlane) : MathIr.Plane =
        match p with
        | Server.XY -> MathIr.Plane.XY
        | Server.XZ -> MathIr.Plane.XZ
        | Server.YZ -> MathIr.Plane.YZ

    /// Build the `Map<string, (x, y)>` of REPoint coords keyed by entity id.
    let private pointCoordTable (sketch: Server.ActionSketch) : Map<string, float * float> =
        sketch.Entities
        |> List.choose (function
            | Server.REPoint(id, x, y) -> Some (id, (x, y))
            | _ -> None)
        |> Map.ofList

    /// Walk the sketch's entities and emit one AST primitive node per
    /// line/circle. Bezier and arc entities are skipped (current kernel
    /// support matches the legacy `fromSketchImpl`); they can be plugged
    /// in here as `EBezier*` / `EArcCenter` nodes when needed.
    let private lowerSketchToPrimitives
            (sp: Span)
            (sketch: Server.ActionSketch)
            (plane: MathIr.Plane) : Expr list =
        let pts = pointCoordTable sketch
        let numAt (x: float) : Expr = numEAt sp x
        let lookup id = Map.tryFind id pts
        sketch.Entities
        |> List.choose (function
            | Server.RELine(_, startId, endId) ->
                match lookup startId, lookup endId with
                | Some (sx, sy), Some (ex, ey) ->
                    Some (mkAt sp (ELineSegment(plane, numAt sx, numAt sy, numAt ex, numAt ey)))
                | _ -> None
            | Server.RECircle(_, centerId, radius) ->
                match lookup centerId with
                | Some (cx, cy) ->
                    Some (mkAt sp (ECircle(plane, numAt cx, numAt cy, numAt radius)))
                | None -> None
            | _ -> None)

    // ── Signed distance for closed loops ──────────────────────────────────
    //
    // A closed-loop sketch should render as a filled region — negative
    // inside, positive outside. After Phase 3 the kernel's
    // `SketchPath(closed=true)` shortcut went away; we rebuild the
    // equivalent in pure AST:
    //
    //   unsigned     = fold(Min, [primitive_distance(seg_i)])
    //   total_wind   = fold(Sum, [atan2(cross_i, dot_i)])
    //   sign_flip    = -compare(|total_wind|, π)   // +1 outside, -1 inside
    //   signed       = unsigned * sign_flip
    //
    // The winding-angle math per line segment is the classic 2D winding
    // contribution: `atan2((a-p)×(b-p), (a-p)·(b-p))`. Summed over a
    // closed polygon it lands at ±2π inside and 0 outside, so the
    // `compare(|sum|, π)` threshold flips the sign cleanly.
    //
    // Single-circle loops collapse to a direct `sqrt((x-cx)² + (y-cy)²) - r`
    // (the analytic signed disk). Cheaper, exact, no `atan2` needed.

    let private planeAxisExprs (sp: Span) (plane: MathIr.Plane) : Expr * Expr =
        let xa, ya =
            match plane with
            | MathIr.Plane.XY -> AxisX, AxisY
            | MathIr.Plane.XZ -> AxisX, AxisZ
            | MathIr.Plane.YZ -> AxisY, AxisZ
            | _ -> AxisX, AxisY
        mkAt sp (EAxis xa), mkAt sp (EAxis ya)

    let private entityId (e: Server.RenderEntity) : string =
        match e with
        | Server.REPoint(id, _, _) -> id
        | Server.RELine(id, _, _) -> id
        | Server.RECircle(id, _, _) -> id
        | Server.REArc(id, _, _, _) -> id

    /// Build the AST winding-angle contribution for a single line segment,
    /// seen from the kernel's `(x, y)` sample position in `plane`.
    /// `atan2((a-p)×(b-p), (a-p)·(b-p))`.
    let private lineWindingAst (sp: Span) (plane: MathIr.Plane) (sx: float) (sy: float) (ex: float) (ey: float) : Expr =
        let (px, py) = planeAxisExprs sp plane
        let numA v = numEAt sp v
        let bop op a b = mkAt sp (EBinary(op, a, b))
        let dxA = bop BinaryOp.Sub (numA sx) px
        let dyA = bop BinaryOp.Sub (numA sy) py
        let dxB = bop BinaryOp.Sub (numA ex) px
        let dyB = bop BinaryOp.Sub (numA ey) py
        let cross = bop BinaryOp.Sub (bop BinaryOp.Mul dxA dyB) (bop BinaryOp.Mul dyA dxB)
        let dot   = bop BinaryOp.Add (bop BinaryOp.Mul dxA dxB) (bop BinaryOp.Mul dyA dyB)
        bop BinaryOp.Atan2 cross dot

    /// Analytic signed disk distance: `sqrt((x-cx)² + (y-cy)²) - r`.
    let private circleSignedAst (sp: Span) (plane: MathIr.Plane) (cx: float) (cy: float) (r: float) : Expr =
        let (px, py) = planeAxisExprs sp plane
        let numA v = numEAt sp v
        let bop op a b = mkAt sp (EBinary(op, a, b))
        let uop op a = mkAt sp (EUnary(op, a))
        let dx = bop BinaryOp.Sub px (numA cx)
        let dy = bop BinaryOp.Sub py (numA cy)
        let rsq = bop BinaryOp.Add (uop UnaryOp.Square dx) (uop UnaryOp.Square dy)
        bop BinaryOp.Sub (uop UnaryOp.Sqrt rsq) (numA r)

    /// Wrap `unsigned` with the sign flip computed from `windings`:
    /// `signed = unsigned * (-compare(|sum windings|, π))`.
    /// `-compare(...)` is `+1` outside (compare = -1), `-1` inside
    /// (compare = +1), and `0` on the boundary (compare = 0).
    let private signedFromWindings (sp: Span) (unsigned: Expr) (windings: Expr list) : Expr =
        let bop op a b = mkAt sp (EBinary(op, a, b))
        let uop op a = mkAt sp (EUnary(op, a))
        let total = mkAt sp (EFold(MathIr.FoldOp.Sum, windings))
        let absT = uop UnaryOp.Abs total
        let pi = numEAt sp System.Math.PI
        let signFlip = uop UnaryOp.Neg (bop BinaryOp.Compare absT pi)
        bop BinaryOp.Mul unsigned signFlip

    /// Build a per-loop AST expression. Returns `None` if the loop is
    /// degenerate (no line/circle entities); the caller falls back to
    /// per-entity unsigned primitives in that case.
    let private buildLoopExpr
            (sp: Span)
            (plane: MathIr.Plane)
            (entitiesById: Map<string, Server.RenderEntity>)
            (pointCoords: Map<string, float * float>)
            (loop: Server.SketchLoop) : Expr option =
        let entitiesOfLoop =
            loop.EntityIds
            |> List.choose (fun id -> Map.tryFind id entitiesById)
        // Single-circle special case: emit analytic signed disk.
        match entitiesOfLoop with
        | [ Server.RECircle(_, centerId, radius) ] ->
            match Map.tryFind centerId pointCoords with
            | Some (cx, cy) -> Some (circleSignedAst sp plane cx cy radius)
            | None -> None
        | _ ->
            // Build unsigned primitives + per-line winding contributions in
            // the same pass. If any non-line entity is present, we can't
            // compute a winding sum, so fall back to unsigned-only for the
            // whole loop.
            let mutable allLines = true
            let unsigned = ResizeArray<Expr>()
            let windings = ResizeArray<Expr>()
            for ent in entitiesOfLoop do
                match ent with
                | Server.RELine(_, startId, endId) ->
                    match Map.tryFind startId pointCoords, Map.tryFind endId pointCoords with
                    | Some (sx, sy), Some (ex, ey) ->
                        unsigned.Add (mkAt sp (ELineSegment(plane, numEAt sp sx, numEAt sp sy, numEAt sp ex, numEAt sp ey)))
                        windings.Add (lineWindingAst sp plane sx sy ex ey)
                    | _ -> ()
                | Server.RECircle(_, centerId, radius) ->
                    match Map.tryFind centerId pointCoords with
                    | Some (cx, cy) ->
                        unsigned.Add (mkAt sp (ECircle(plane, numEAt sp cx, numEAt sp cy, numEAt sp radius)))
                        allLines <- false
                    | None -> ()
                | _ ->
                    allLines <- false
            if unsigned.Count = 0 then None
            else
                let unsignedExpr =
                    if unsigned.Count = 1 then unsigned.[0]
                    else mkAt sp (EFold(MathIr.FoldOp.Min, List.ofSeq unsigned))
                if allLines && windings.Count >= 2 then
                    Some (signedFromWindings sp unsignedExpr (List.ofSeq windings))
                else
                    // Mixed-shape loop (e.g. line + circle): the winding
                    // sum isn't well-defined here, so emit unsigned only.
                    Some unsignedExpr

    /// Build the SLet RHS for a from-sketch block. Detects closed loops
    /// and emits signed-distance for each; non-loop entities contribute
    /// as unsigned primitives. Everything is combined via `fold(Min)`.
    /// Empty sketch → an unwired placeholder so the typecheck path
    /// surfaces the same kind of error a missing input would.
    ///
    /// Public because `NotebookEval` reuses it: both drivers now share
    /// the same `sketch → AST → Eval → MathIR` pipeline, so sketch
    /// semantics live in one place. The legacy MathIR-direct mirror
    /// (`lowerSketchData`) was retired alongside the duplicate
    /// `lineWindingIr` / `circleSignedIr` helpers.
    let buildFromSketchBody
            (sp: Span)
            (sketchData: Notebook.SketchData) : Expr =
        let plane = planeMap sketchData.Plane
        let entities = sketchData.Sketch.Entities
        let pts = pointCoordTable sketchData.Sketch
        let entitiesById =
            entities |> List.map (fun e -> entityId e, e) |> Map.ofList
        let loops = Server.SketchLoops.detectLoops entities
        let loopEntityIds =
            loops |> List.collect (fun l -> l.EntityIds) |> Set.ofList

        let loopExprs =
            loops |> List.choose (buildLoopExpr sp plane entitiesById pts)

        // Non-loop line/circle entities still contribute their unsigned
        // distance to the field so dangling sketches render too.
        let orphanExprs =
            entities
            |> List.filter (fun e -> not (Set.contains (entityId e) loopEntityIds))
            |> List.choose (function
                | Server.RELine(_, startId, endId) ->
                    match Map.tryFind startId pts, Map.tryFind endId pts with
                    | Some (sx, sy), Some (ex, ey) ->
                        Some (mkAt sp (ELineSegment(plane, numEAt sp sx, numEAt sp sy, numEAt sp ex, numEAt sp ey)))
                    | _ -> None
                | Server.RECircle(_, centerId, radius) ->
                    match Map.tryFind centerId pts with
                    | Some (cx, cy) ->
                        Some (mkAt sp (ECircle(plane, numEAt sp cx, numEAt sp cy, numEAt sp radius)))
                    | None -> None
                | _ -> None)

        match loopExprs @ orphanExprs with
        | [] -> varEAt sp UNWIRED_PLACEHOLDER
        | [ single ] -> single
        | many -> mkAt sp (EFold(MathIr.FoldOp.Min, many))

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
        // entry directly. Stash the SketchData by block id so the
        // from-sketch lowering can reach it without re-walking blocks.
        let mutable sketchByBlockId : Map<BlockId, Notebook.SketchData> = Map.empty
        for block in notebook.Blocks do
            match block.Body with
            | SketchBody data ->
                typeEnv <- Map.add block.Name Type.Sketch typeEnv
                blockOutputs <- Map.add block.Id Type.Sketch blockOutputs
                sketchByBlockId <- Map.add block.Id data sketchByBlockId
            | _ -> ()

        // Build per-block let-bindings for the native blocks. Walk in
        // declaration order so refs only reach upstream names.
        let stmts = ResizeArray<Stmt>()
        // Names of every Field-typed block whose visibility is not
        // `VHidden`. The render root is `union`-folded over this list.
        let visibleFieldNames = ResizeArray<string>()
        let visibleFieldBlockIds = ResizeArray<BlockId>()
        let visibleFieldKinds = ResizeArray<BlockVisibility>()
        let visibleFieldColorIndices = ResizeArray<int>()

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
                | Some spec when specName = "halfplane" ->
                    // Lower `halfplane axis offset` to `<axis> - offset`.
                    // `axis` is encoded as an `ArgScalar` (0=X, 1=Y, 2=Z) so
                    // the BlockList UI can render it as a dropdown without
                    // a new `BlockArg` case.
                    let axisChoice =
                        match Map.tryFind "axis" args with
                        | Some (ArgScalar n) ->
                            match int (round n) with
                            | 1 -> AxisY
                            | 2 -> AxisZ
                            | _ -> AxisX
                        | _ -> AxisX
                    let offsetExpr =
                        match Map.tryFind "offset" args with
                        | Some (ArgScalar n) -> numEAt bsp n
                        | _ -> numEAt bsp 0.0
                    let body = mkAt bsp (EBinary(BinaryOp.Sub, mkAt bsp (EAxis axisChoice), offsetExpr))
                    stmts.Add(SLet([ userAt block.Name bsp ], body))

                    let outTy = specOutputType spec
                    blockOutputs <- Map.add block.Id outTy blockOutputs
                    let surfaceVisible =
                        match block.Visibility with
                        | VIsosurface           -> true
                        | VHidden | VFieldLines -> false
                    if outTy = Type.Field && surfaceVisible then
                        visibleFieldNames.Add block.Name
                        visibleFieldBlockIds.Add block.Id
                        visibleFieldKinds.Add block.Visibility
                        visibleFieldColorIndices.Add block.ColorIndex
                | Some spec when specName = "from-sketch" ->
                    // Lower `from-sketch sketch` to an inlined `EFold(Min,
                    // [primitives...])` AST built from the referenced
                    // SketchBody. The spec itself never executes — it only
                    // contributes its `Sketch -> Field` type signature to
                    // the env so callers that mis-wire it still typecheck.
                    let body =
                        match Map.tryFind "sketch" args with
                        | Some (ArgRef (Some refId)) ->
                            match Map.tryFind refId sketchByBlockId with
                            | Some data -> buildFromSketchBody bsp data
                            | None -> varEAt bsp UNWIRED_PLACEHOLDER
                        | _ -> varEAt bsp UNWIRED_PLACEHOLDER
                    stmts.Add(SLet([ userAt block.Name bsp ], body))

                    let outTy = specOutputType spec
                    blockOutputs <- Map.add block.Id outTy blockOutputs
                    let surfaceVisible =
                        match block.Visibility with
                        | VIsosurface           -> true
                        | VHidden | VFieldLines -> false
                    if outTy = Type.Field && surfaceVisible then
                        visibleFieldNames.Add block.Name
                        visibleFieldBlockIds.Add block.Id
                        visibleFieldKinds.Add block.Visibility
                        visibleFieldColorIndices.Add block.ColorIndex
                | Some spec ->
                    let typed = BlockSpec.typedInterface spec

                    // Build args in the order the spec declares them.
                    let argExprs =
                        typed.Params
                        |> List.map (fun p ->
                            match tryFindBlockArg specName p.Name args with
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
                    // Field blocks contribute to the surface union when
                    // their visibility is `VIsosurface`. `VFieldLines` is
                    // overlay-only and is drawn separately by the F#
                    // viewer's FieldSlice pass — including it in the
                    // union would double-draw the block as solid surface
                    // AND contour lines.
                    let surfaceVisible =
                        match block.Visibility with
                        | VIsosurface           -> true
                        | VHidden | VFieldLines -> false
                    if outTy = Type.Field && surfaceVisible then
                        visibleFieldNames.Add block.Name
                        visibleFieldBlockIds.Add block.Id
                        visibleFieldKinds.Add block.Visibility
                        visibleFieldColorIndices.Add block.ColorIndex

        // Render root = sharp `union` over every visible Field block.
        // Empty list → no trailing expression → "no renderable output".
        // Singleton → that block alone (collapses to today's behaviour).
        let visibleFieldNamesList = List.ofSeq visibleFieldNames
        match visibleFieldNamesList with
        | [] -> ()
        | [ single ] -> stmts.Add(SExpr (varE single))
        | head :: tail ->
            let unionCall a b = applyChain (varE "union") [ a; b; numE 0.0 ]
            let folded = tail |> List.fold (fun acc n -> unionCall acc (varE n)) (varE head)
            stmts.Add(SExpr folded)

        { Ast = mk (EBlock (List.ofSeq stmts))
          TypeEnv = typeEnv
          BlockNames = blockNames
          BlockSpans = blockSpans
          BlockOutputs = blockOutputs
          VisibleFieldNames = visibleFieldNamesList
          VisibleFieldBlockIds = List.ofSeq visibleFieldBlockIds
          VisibleFieldKinds = List.ofSeq visibleFieldKinds
          VisibleFieldColorIndices = List.ofSeq visibleFieldColorIndices }

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
        /// Top-level let-binding values, captured after the notebook's
        /// statements ran. Used to look up per-block render exprs (one
        /// MathIR view per visible Field block) without re-evaluating.
        Bindings: Map<string, Value>
    }

    /// Pre-typecheck step has already passed — build the MathIR + eval
    /// the composed AST. Stmts are iterated at the top level (rather
    /// than wrapped in an EBlock) so each block's let-binding stays
    /// in `ctx.Env` after evaluation, exposing per-block exprs through
    /// `EvalResult.Bindings`.
    let evaluate (notebook: Notebook) (composed: Composed) : Result<EvalResult, EvalError> =
        let ir = MathIr.MathIR()
        let ctx = createContextWith ir (newEnv None)
        buildValueEnv notebook ctx

        let stmts =
            match composed.Ast.Node with
            | EBlock ss -> ss
            | _ -> [ SExpr composed.Ast ]

        let mutable err : EvalError option = None
        let mutable last : Value = VUnit
        for stmt in stmts do
            if err.IsNone then
                match Eval.evalStmt ctx stmt with
                | Ok v -> last <- v
                | Error e -> err <- Some e

        match err with
        | Some e -> Error e
        | None ->
            // Snapshot just the keys we care about — sketch payloads + per-
            // block let bindings — out of the env's mutable Dictionary.
            let bindings =
                ctx.Env.Bindings
                |> Seq.map (fun kv -> kv.Key, kv.Value)
                |> Map.ofSeq
            Ok { Ir = ir; Value = last; Bindings = bindings }

    // ── Public entry points ────────────────────────────────────────────────

    /// Result the editor surfaces: bytes for the kernel (when the whole
    /// pipeline succeeds), plus the per-block error and output-type
    /// maps the UI consults for has-error styling and ref-drop
    /// validation. `Ir` and `FieldExprByBlock` let the F# viewer build
    /// per-block GPU shaders (field-slice overlay) without re-parsing
    /// the wire bytes.
    type CompileResult = {
        Bytes: obj option
        Ir: MathIr.MathIR option
        FieldExprByBlock: Map<BlockId, MathIr.Expr>
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

    /// Wire-format encoding of `BlockVisibility`. Hidden / field-line
    /// blocks never reach `viewsFromBindings` — only `VIsosurface`
    /// blocks ship as kernel views, so `kindCode` only ever returns 0
    /// in practice. The other arms are kept for completeness and to
    /// remind the renderer where to plug in future per-kind shading.
    let private kindCode (v: BlockVisibility) : uint32 =
        match v with
        | VIsosurface -> 0u
        | VFieldLines -> 1u  // unreachable — drawn by FieldSlice, not kernel
        | VHidden     -> 0u  // unreachable

    /// Pull `(expr, palette_idx, kind)` for each visible Field block out of
    /// the post-eval bindings. Drops names that didn't resolve to
    /// `VField` (shouldn't happen if typecheck passed; defensive).
    let private viewsFromBindings
            (composed: Composed)
            (bindings: Map<string, Value>) : (MathIr.Expr * uint32 * uint32) list =
        List.zip3 composed.VisibleFieldNames composed.VisibleFieldColorIndices composed.VisibleFieldKinds
        |> List.choose (fun (name, colorIndex, kind) ->
            match Map.tryFind name bindings with
            | Some (VField expr) ->
                let paletteIndex = uint32 (((colorIndex % 9) + 9) % 9)
                Some (expr, paletteIndex, kindCode kind)
            | _ -> None)

    /// Walk the per-block name → block-id table and the post-eval bindings
    /// to produce a `BlockId → Expr` map of just the Field outputs. Used by
    /// the F# viewer to emit per-block GPU shaders (field-slice overlay).
    let private fieldExprByBlock
            (composed: Composed)
            (bindings: Map<string, Value>) : Map<BlockId, MathIr.Expr> =
        composed.BlockNames
        |> Map.toSeq
        |> Seq.choose (fun (blockId, name) ->
            match Map.tryFind name bindings with
            | Some (VField expr) -> Some (blockId, expr)
            | _ -> None)
        |> Map.ofSeq

    /// Full notebook compile — typecheck + (on success) eval + serialise.
    let compile (notebook: Notebook) : CompileResult =
        let composed = compose notebook
        match Typecheck.elaborate composed.TypeEnv composed.Ast with
        | Error errs ->
            let blockErrors = routeErrorsToBlocks composed errs
            let summary =
                errs |> List.tryHead |> Option.map Typecheck.formatError
            { Bytes = None
              Ir = None
              FieldExprByBlock = Map.empty
              BlockErrors = blockErrors
              BlockOutputs = composed.BlockOutputs
              Summary = summary }
        | Ok _ ->
            match evaluate notebook composed with
            | Ok { Ir = ir; Value = VField root; Bindings = bindings } ->
                // Per-block field exprs flow to the F# viewer regardless of
                // whether anything goes to the surface union — `VFieldLines`
                // blocks need their MathIR root for the slice overlay even
                // when no surface ships to the kernel.
                let fieldExprs = fieldExprByBlock composed bindings
                if List.isEmpty composed.VisibleFieldNames then
                    // No `VIsosurface` blocks → nothing to raymarch. Skip
                    // the wire-bytes serialise; the kernel
                    // sees `LastNotebookBytes = None` and clears its
                    // background. Slice overlay still has the IR + exprs
                    // it needs.
                    { Bytes = None
                      Ir = Some ir
                      FieldExprByBlock = fieldExprs
                      BlockErrors = Map.empty
                      BlockOutputs = composed.BlockOutputs
                      Summary = None }
                else
                    // Serialise to wire bytes. On .NET (xUnit tests) the
                    // Fable Uint8Array binding throws — surface that
                    // explicitly so a failure here doesn't masquerade as
                    // "no render root" with a blank canvas.
                    try
                        let views = viewsFromBindings composed bindings
                        let bytes = MathIrCodec.serialize ir root views
                        { Bytes = Some bytes
                          Ir = Some ir
                          FieldExprByBlock = fieldExprs
                          BlockErrors = Map.empty
                          BlockOutputs = composed.BlockOutputs
                          Summary = None }
                    with ex ->
                        // Raw JS errors don't carry a .NET-shaped Type; calling
                        // GetType() on them throws inside the catch. Stick to
                        // the JS-safe `string ex` (Fable maps that to
                        // `String(ex)` which falls back to the JS error's own
                        // toString — that contains the constructor name +
                        // message).
                        let detail =
                            try string ex
                            with _ -> "<introspection failed>"
                        { Bytes = None
                          Ir = Some ir
                          FieldExprByBlock = fieldExprs
                          BlockErrors = Map.empty
                          BlockOutputs = composed.BlockOutputs
                          Summary = Some (sprintf "serialise failed: %s" detail) }
            | Ok _ ->
                { Bytes = None
                  Ir = None
                  FieldExprByBlock = Map.empty
                  BlockErrors = Map.empty
                  BlockOutputs = composed.BlockOutputs
                  Summary = Some "notebook produced no Field render root" }
            | Error e ->
                { Bytes = None
                  Ir = None
                  FieldExprByBlock = Map.empty
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
