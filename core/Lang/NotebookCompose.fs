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
    //
    // The general-purpose Expr builders (`mkAt`, `varEAt`, `numEAt`, etc.)
    // live in `AstBuilder` — opened below so existing references resolve
    // without the `AstBuilder.` qualifier.

    open AstBuilder

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

    /// Sample `numSegments + 1` points along an arc (ArcCenter mode),
    /// from start endpoint to end endpoint, sweeping in the given
    /// direction. Used by the closed-loop winding path: the chord-by-
    /// chord polygonal approximation of the arc lets the winding-angle
    /// integral converge to the correct ±2π per loop.
    let private arcSamplePoints
            (sx: float) (sy: float)
            (ex: float) (ey: float)
            (cx: float) (cy: float)
            (clockwise: bool)
            (numSegments: int) : (float * float) list =
        let dx0 = sx - cx
        let dy0 = sy - cy
        let radius = sqrt (dx0 * dx0 + dy0 * dy0)
        let startAngle = atan2 dy0 dx0
        let endAngle = atan2 (ey - cy) (ex - cx)
        let mutable delta = endAngle - startAngle
        let twoPi = 2.0 * System.Math.PI
        if clockwise && delta > 0.0 then delta <- delta - twoPi
        elif (not clockwise) && delta < 0.0 then delta <- delta + twoPi
        [ for i in 0 .. numSegments ->
            let t = float i / float numSegments
            let a = startAngle + delta * t
            (cx + radius * cos a, cy + radius * sin a) ]

    /// Number of chords used to approximate an arc when summing the
    /// winding-angle integral for a closed loop. 16 keeps each chord
    /// under ~6° for a quarter-circle arc — small enough that the
    /// chord-loop's winding matches the true arc-loop's winding for any
    /// sample point not sitting inside an individual chord's micro-lune.
    let private ARC_WINDING_SEGMENTS = 16

    /// Try to resolve an `REArc` into the geometry the kernel can render:
    /// (start, end, center, clockwise). Only `ArcCenter` mode is
    /// supported — `ArcThreePoint` is authoring-only per `SketchLoops`.
    let private tryArcGeometry
            (pts: Map<string, float * float>)
            (entity: Server.RenderEntity)
            : ((float * float) * (float * float) * (float * float) * bool) option =
        match entity with
        | Server.REArc(_, startId, endId, Server.ArcCenter(centerId, clockwise)) ->
            match Map.tryFind startId pts, Map.tryFind endId pts, Map.tryFind centerId pts with
            | Some (sx, sy), Some (ex, ey), Some (cx, cy) ->
                Some ((sx, sy), (ex, ey), (cx, cy), clockwise)
            | _ -> None
        | _ -> None

    /// Walk the sketch's entities and emit one AST primitive node per
    /// line/circle/arc. Bezier entities are still skipped; ArcThreePoint
    /// is authoring-only and falls through to nothing (matches
    /// `SketchLoops.detectLoops` behaviour).
    let private lowerSketchToPrimitives
            (sp: Span)
            (sketch: Server.ActionSketch)
            (plane: MathIr.Plane) : Expr list =
        let pts = pointCoordTable sketch
        let numAt (x: float) : Expr = numEAt sp x
        let lookup id = Map.tryFind id pts
        sketch.Entities
        |> List.choose (fun ent ->
            match ent with
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
            | Server.REArc _ ->
                tryArcGeometry pts ent
                |> Option.map (fun ((sx, sy), (ex, ey), (cx, cy), cw) ->
                    mkAt sp (EArcCenter(plane, numAt sx, numAt sy, numAt ex, numAt ey, numAt cx, numAt cy, cw)))
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

    /// Build a single-primitive distance Expr from an entity:
    /// - line   → unsigned distance to the segment (`ELineSegment`)
    /// - circle → signed disk distance (`circleSignedAst` for a true SDF)
    /// - arc    → unsigned distance to the arc (`EArcCenter`)
    /// Returns `None` for entities that don't have a primitive
    /// representation (points, ArcThreePoint without geometry).
    let private buildPrimitiveDistanceExpr
            (sp: Span)
            (plane: MathIr.Plane)
            (pointCoords: Map<string, float * float>)
            (entitiesById: Map<string, Server.RenderEntity>)
            (entityId: string) : Expr option =
        match Map.tryFind entityId entitiesById with
        | Some (Server.RELine(_, startId, endId)) ->
            match Map.tryFind startId pointCoords, Map.tryFind endId pointCoords with
            | Some (sx, sy), Some (ex, ey) ->
                Some (mkAt sp (ELineSegment(plane, numEAt sp sx, numEAt sp sy, numEAt sp ex, numEAt sp ey)))
            | _ -> None
        | Some (Server.RECircle(_, centerId, radius)) ->
            match Map.tryFind centerId pointCoords with
            | Some (cx, cy) -> Some (circleSignedAst sp plane cx cy radius)
            | None -> None
        | Some (Server.REArc _ as ent) ->
            tryArcGeometry pointCoords ent
            |> Option.map (fun ((sx, sy), (ex, ey), (cx, cy), cw) ->
                mkAt sp (EArcCenter(plane, numEAt sp sx, numEAt sp sy, numEAt sp ex, numEAt sp ey, numEAt sp cx, numEAt sp cy, cw)))
        | _ -> None

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
            // Build unsigned primitives + per-segment winding contributions
            // in one pass. Arcs contribute the exact analytic distance via
            // `EArcCenter` *and* a polygonal chord approximation for the
            // winding integral (the chord-loop's winding matches the
            // arc-loop's winding away from arc-bulge lunes). A circle
            // breaks the signed path because its winding integral is
            // implicit, not boundary-traversal-based; we fall back to
            // unsigned-only when a circle is present.
            let mutable signedSupported = true
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
                        signedSupported <- false
                    | None -> ()
                | Server.REArc _ ->
                    match tryArcGeometry pointCoords ent with
                    | Some ((sx, sy), (ex, ey), (cx, cy), cw) ->
                        unsigned.Add
                            (mkAt sp (EArcCenter(plane, numEAt sp sx, numEAt sp sy, numEAt sp ex, numEAt sp ey, numEAt sp cx, numEAt sp cy, cw)))
                        let samples = arcSamplePoints sx sy ex ey cx cy cw ARC_WINDING_SEGMENTS
                        samples
                        |> List.pairwise
                        |> List.iter (fun ((ax, ay), (bx, by)) ->
                            windings.Add (lineWindingAst sp plane ax ay bx by))
                    | None ->
                        // ArcThreePoint or missing point coord — can't
                        // ship a primitive for it. The loop's signed
                        // result will be wrong; fall back to unsigned.
                        signedSupported <- false
                | _ ->
                    signedSupported <- false
            if unsigned.Count = 0 then None
            else
                let unsignedExpr =
                    if unsigned.Count = 1 then unsigned.[0]
                    else mkAt sp (EFold(MathIr.FoldOp.Min, List.ofSeq unsigned))
                if signedSupported && windings.Count >= 2 then
                    Some (signedFromWindings sp unsignedExpr (List.ofSeq windings))
                else
                    // Loop contains a circle (or an entity we couldn't
                    // walk for winding): the boundary integral isn't
                    // well-defined for the polygonal sum, so emit
                    // unsigned only.
                    Some unsignedExpr

    /// Build the SLet RHS for a from-sketch block. Detects closed loops
    /// and emits signed-distance for each; non-loop entities contribute
    /// as unsigned primitives. Everything is combined via `fold(Min)`.
    /// Empty sketch → an unwired placeholder so the typecheck path
    /// surfaces the same kind of error a missing input would.
    ///
    /// Shared by the production compose/evaluate path and tests:
    /// sketch semantics live here as `sketch → AST → Eval → MathIR`.
    /// The legacy MathIR-direct mirror (`lowerSketchData`) was retired
    /// alongside the duplicate `lineWindingIr` / `circleSignedIr` helpers.
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

        // Non-loop line/circle/arc entities still contribute their
        // unsigned distance to the field so dangling sketches render
        // too. Beziers and ArcThreePoint stay out.
        let orphanExprs =
            entities
            |> List.filter (fun e -> not (Set.contains (entityId e) loopEntityIds))
            |> List.choose (fun ent ->
                match ent with
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
                | Server.REArc _ ->
                    tryArcGeometry pts ent
                    |> Option.map (fun ((sx, sy), (ex, ey), (cx, cy), cw) ->
                        mkAt sp (EArcCenter(plane, numEAt sp sx, numEAt sp sy, numEAt sp ex, numEAt sp ey, numEAt sp cx, numEAt sp cy, cw)))
                | _ -> None)

        match loopExprs @ orphanExprs with
        | [] -> varEAt sp UNWIRED_PLACEHOLDER
        | [ single ] -> single
        | many -> mkAt sp (EFold(MathIr.FoldOp.Min, many))

    // ── revolve lowering ──────────────────────────────────────────────────
    //
    // Sweep a 2D profile around a hardcoded axis in its plane. The 3D SDF
    // at world (x, y, z) equals `sketch_sdf_2d(radial, height)` where
    //   radial = sqrt of the two world coords perpendicular to the revolve axis
    //   height = world coord along the revolve axis
    // We reuse `buildFromSketchBody` to construct the 2D signed-distance
    // AST (closed loops produce signed; open profiles unsigned and look
    // weird revolved — out of scope for MVP) and wrap with `ERemapAxes` so
    // the EAxis references inside the 2D SDF resolve to the radial /
    // height substitutions.
    //
    // Revolve axis per plane:
    //   XY → Y axis  (radial = sqrt(x² + z²), height = y)
    //   XZ → Z axis  (radial = sqrt(x² + y²), height = z)
    //   YZ → Z axis  (radial = sqrt(x² + y²), height = z)

    let private radialDistance (sp: Span) (a: Expr) (b: Expr) : Expr =
        let sq e = mkAt sp (EUnary(UnaryOp.Square, e))
        let sum = mkAt sp (EBinary(BinaryOp.Add, sq a, sq b))
        mkAt sp (EUnary(UnaryOp.Sqrt, sum))

    /// Build the SLet RHS for a revolve block. The sketch's 2D SDF
    /// (whatever `buildFromSketchBody` would produce — typically a signed
    /// closed-loop distance) gets wrapped in `ERemapAxes` so EAxis
    /// references inside resolve to the radial / height substitutions.
    /// Empty sketch falls through `buildFromSketchBody`'s unwired
    /// placeholder, which then bypasses the remap (target is just a
    /// var-ref to UNWIRED_PLACEHOLDER, which yields its own typecheck
    /// error path).
    let buildRevolveBody
            (sp: Span)
            (sketchData: Notebook.SketchData) : Expr =
        let plane = planeMap sketchData.Plane
        let target = buildFromSketchBody sp sketchData
        let x = mkAt sp (EAxis AxisX)
        let y = mkAt sp (EAxis AxisY)
        let z = mkAt sp (EAxis AxisZ)
        // Unused-axis slots stay as identity remaps (axis → itself) so the
        // typechecker sees Field everywhere — `ENumber 0.0` would type as
        // Scalar and fail `check env Type.Field`. The unused axis never
        // appears inside `target` so the actual substitution is dead.
        match plane with
        | MathIr.Plane.XY ->
            // 2D SDF uses AxisX, AxisY. Revolve around Y.
            mkAt sp (ERemapAxes(target, radialDistance sp x z, y, z))
        | MathIr.Plane.XZ ->
            // 2D SDF uses AxisX, AxisZ. Revolve around Z.
            mkAt sp (ERemapAxes(target, radialDistance sp x y, y, z))
        | MathIr.Plane.YZ ->
            // 2D SDF uses AxisY, AxisZ. Revolve around Z.
            mkAt sp (ERemapAxes(target, x, radialDistance sp x y, z))
        | _ -> target

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
    /// Curried function type for a user-defined spec — same shape as
    /// `specFunType` produces from a built-in BlockSpec.
    let private userSpecFunType (us: UserScript.UserSpec) : Type.T =
        let inputs = us.Params |> List.map (fun p -> p.Type)
        Type.curried inputs us.Output

    let composeWith (notebook: Notebook) (userScript: UserScript.Result) : Composed =
        // Seed type env: built-in specs first, then user specs. User specs
        // override built-ins with the same name (last write wins on the
        // Map.add). That matches the env-binding order in `buildValueEnv`
        // and in the prepended user `Stmts`, so name resolution stays
        // consistent end-to-end.
        let mutable typeEnv : Typecheck.TypeEnv = Map.empty
        for spec in BlockSpec.all () do
            typeEnv <- Map.add spec.Name (specFunType spec) typeEnv
        for kv in userScript.Specs do
            typeEnv <- Map.add kv.Key (userSpecFunType kv.Value) typeEnv

        let blockNames =
            notebook.Blocks
            |> List.map (fun b -> b.Id, b.Name)
            |> Map.ofList

        // Reverse lookup — used by the from-sketch / revolve interceptors
        // to resolve a "wired upstream sketch" arg back to its BlockId
        // when the arg expression is a simple `EVar name`.
        let blockIdByName =
            notebook.Blocks
            |> List.map (fun b -> b.Name, b.Id)
            |> Map.ofList

        // Extract the upstream BlockId an arg refers to, if it's a
        // simple variable reference. Path refs (`EPath`) and richer
        // expressions return `None` — the interceptor falls back to
        // unwired, which surfaces a clean typecheck error against the
        // spec's declared input shape.
        let referencedBlockId (e: Expr) : BlockId option =
            match e.Node with
            | EVar id -> Map.tryFind id.Name blockIdByName
            | _ -> None

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
                // Build a structural refinement from the persisted Loops
                // registry. Each `LoopRecord.Id` becomes a `Loop` whose
                // refinement names `signed_distance: Field` plus a
                // `Type.Primitive` member per persisted PrimitiveRecord.
                // Loops and primitives both auto-project to Field via
                // `Type.isSubtypeOf` (matched by the runtime), so the
                // DSL surface `<sketch>.loop_0.line_2` and
                // `<sketch>.loop_0` both work where a Field is expected.
                let primitiveType : Type.T =
                    Type.Primitive (Map.ofList [ "signed_distance", Type.Field ])
                let refinement =
                    data.Sketch.Loops
                    |> List.map (fun r ->
                        let loopFieldsMap =
                            r.Primitives
                            |> List.map (fun (prim: Server.PrimitiveRecord) -> prim.Id, primitiveType)
                            |> Map.ofList
                            |> Map.add "signed_distance" Type.Field
                        r.Id, Type.Loop loopFieldsMap)
                    |> Map.ofList
                let sketchType = Type.Sketch refinement
                typeEnv <- Map.add block.Name sketchType typeEnv
                blockOutputs <- Map.add block.Id sketchType blockOutputs
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
                // Emit a single block as `let block_X = applyChain (varE specName) [args...]`
                // plus its bookkeeping (output type, visible-field union
                // membership). Shared between the generic built-in path and
                // the user-defined-spec path; both routes resolve the spec
                // by name at eval time via env lookup, so the only thing the
                // emitter needs is the typed parameter interface.
                let emitGeneric (typed: TypeExtract.ExtractedSpec) =
                    // Each arg slot stores an Ast.Expr directly. Absent
                    // keys / no-fallback cases lower to the unwired sentinel
                    // so the typechecker surfaces a clean `UndefinedVar`.
                    let argExprs =
                        typed.Params
                        |> List.map (fun p ->
                            Map.tryFind p.Name args
                            |> Option.defaultValue (varEAt bsp UNWIRED_PLACEHOLDER))
                    let call = applyChainAt bsp (varEAt bsp specName) argExprs
                    stmts.Add(SLet([ userAt block.Name bsp ], call))
                    blockOutputs <- Map.add block.Id typed.Output blockOutputs
                    let surfaceVisible =
                        match block.Visibility with
                        | VIsosurface           -> true
                        | VHidden | VFieldLines -> false
                    if typed.Output = Type.Field && surfaceVisible then
                        visibleFieldNames.Add block.Name
                        visibleFieldBlockIds.Add block.Id
                        visibleFieldKinds.Add block.Visibility
                        visibleFieldColorIndices.Add block.ColorIndex

                // User-defined specs win over built-ins with the same name.
                match Map.tryFind specName userScript.Specs with
                | Some us ->
                    emitGeneric { Params = us.Params; Output = us.Output }
                | None ->
                match BlockSpec.tryFind specName with
                | None ->
                    // Unknown spec — surface via an undefined ref so the
                    // typechecker reports it cleanly.
                    let call = applyChainAt bsp (varEAt bsp specName) []
                    stmts.Add(SLet([ userAt block.Name bsp ], call))
                | Some spec when specName = "from-sketch" ->
                    // Lower `from-sketch sketch` to an inlined `EFold(Min,
                    // [primitives...])` AST built from the referenced
                    // SketchBody. The spec itself never executes — it only
                    // contributes its `Sketch -> Field` type signature to
                    // the env so callers that mis-wire it still typecheck.
                    let body =
                        match Map.tryFind "sketch" args |> Option.bind referencedBlockId with
                        | Some refId ->
                            match Map.tryFind refId sketchByBlockId with
                            | Some data -> buildFromSketchBody bsp data
                            | None -> varEAt bsp UNWIRED_PLACEHOLDER
                        | None -> varEAt bsp UNWIRED_PLACEHOLDER
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
                | Some spec when specName = "revolve" ->
                    // Lower `revolve sketch` to `ERemapAxes(2D_sdf, ...)`
                    // — same pattern as from-sketch but the resulting
                    // expression is the 2D SDF sampled at (radial, height)
                    // coordinates rather than directly at world (x, y).
                    // See `buildRevolveBody` for the per-plane remap math.
                    let body =
                        match Map.tryFind "sketch" args |> Option.bind referencedBlockId with
                        | Some refId ->
                            match Map.tryFind refId sketchByBlockId with
                            | Some data -> buildRevolveBody bsp data
                            | None -> varEAt bsp UNWIRED_PLACEHOLDER
                        | None -> varEAt bsp UNWIRED_PLACEHOLDER
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
                    emitGeneric (BlockSpec.typedInterface spec)

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

        // Prepend user-script stmts so user lambdas / constants land in the
        // env before any per-block let-binding references them. Order: user
        // stmts → block lets → render-root SExpr.
        let allStmts = userScript.Stmts @ List.ofSeq stmts

        { Ast = mk (EBlock allStmts)
          TypeEnv = typeEnv
          BlockNames = blockNames
          BlockSpans = blockSpans
          BlockOutputs = blockOutputs
          VisibleFieldNames = visibleFieldNamesList
          VisibleFieldBlockIds = List.ofSeq visibleFieldBlockIds
          VisibleFieldKinds = List.ofSeq visibleFieldKinds
          VisibleFieldColorIndices = List.ofSeq visibleFieldColorIndices }

    /// Backward-compat wrapper: compose without a user script.
    let compose (notebook: Notebook) : Composed =
        composeWith notebook UserScript.empty

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

        // Sketch payloads. `Fields` is populated from the persisted Loops
        // registry: each `LoopRecord` becomes an entry keyed by its Id,
        // valued at the corresponding per-loop signed-distance `VField`.
        // Loops that can't be lowered (degenerate / contain ArcThreePoint
        // or Bezier) are silently skipped — DSL access to a missing loop
        // surfaces as a runtime "no such member" error, mirroring how
        // unwired entity references already surface.
        for block in notebook.Blocks do
            match block.Body with
            | SketchBody data ->
                let plane = planeMap data.Plane
                let entities = data.Sketch.Entities
                let entitiesById =
                    entities
                    |> List.map (fun e -> entityId e, e)
                    |> Map.ofList
                let pts = pointCoordTable data.Sketch
                let loopSpan = spanForBlock block.Id
                // Per-loop VLoop value: carries its own `signed_distance`
                // (whole-loop SDF) plus a VPrimitive per persisted
                // PrimitiveRecord, keyed by the record's stable id
                // (`line_0`, `arc_0`, `circle_0`). Failed lowerings are
                // silently dropped from the loop's Fields map.
                let buildPrimitiveFields (prims: Server.PrimitiveRecord list) : Map<string, Value> =
                    prims
                    |> List.choose (fun prim ->
                        buildPrimitiveDistanceExpr loopSpan plane pts entitiesById prim.EntityId
                        |> Option.bind (fun expr ->
                            match Eval.evalExpr ctx expr with
                            | Ok v ->
                                let primVal =
                                    VPrimitive { Fields = Map.ofList [ "signed_distance", v ] }
                                Some (prim.Id, primVal)
                            | Error _ -> None))
                    |> Map.ofList

                let fields =
                    data.Sketch.Loops
                    |> List.choose (fun r ->
                        let pseudoLoop : Server.SketchLoop =
                            { Id = r.Id
                              EntityIds = r.EntityIds
                              SignedArea = 0.0 }
                        buildLoopExpr loopSpan plane entitiesById pts pseudoLoop
                        |> Option.bind (fun expr ->
                            match Eval.evalExpr ctx expr with
                            | Ok v ->
                                let loopFields =
                                    buildPrimitiveFields r.Primitives
                                    |> Map.add "signed_distance" v
                                Some (r.Id, VLoop { Fields = loopFields })
                            | Error _ -> None))
                    |> Map.ofList
                envBind ctx.Env block.Name
                    (VSketch { Sketch = data.Sketch; Plane = data.Plane; Fields = fields })
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
                | Typecheck.InvalidOperand(_, sp)
                | Typecheck.UnknownSketchMember(_, _, _, sp)
                | Typecheck.NotASketch(_, sp)
                | Typecheck.EmptyPath sp
                | Typecheck.MissingSketchMembers(_, _, sp) -> sp
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
    /// `userScript` injects user-defined specs ahead of the built-in
    /// registry and prepends user-source stmts to the composed program.
    let compileWith (notebook: Notebook) (userScript: UserScript.Result) : CompileResult =
        let composed = composeWith notebook userScript
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

    /// Backward-compat wrapper: compile a notebook with no user script.
    let compile (notebook: Notebook) : CompileResult =
        compileWith notebook UserScript.empty

    /// Older shim for code paths that just want the (ir, root) pair.
    /// Returns `Error` when typecheck fails — call sites outside Editor
    /// (init seed, tests) still use this.
    let compileViewWith
            (notebook: Notebook)
            (_surfaceBlock: string option)
            (userScript: UserScript.Result)
            : Result<MathIr.MathIR * MathIr.Expr, Typecheck.TypeError list> =
        let composed = composeWith notebook userScript
        match Typecheck.elaborate composed.TypeEnv composed.Ast with
        | Error errs -> Error errs
        | Ok _ ->
            match evaluate notebook composed with
            | Ok { Ir = ir; Value = VField root } -> Ok (ir, root)
            | Ok _ ->
                Error [ Typecheck.InvalidOperand("notebook produced no Field render root", { Start = 0; Stop = 0 }) ]
            | Error e ->
                Error [ Typecheck.InvalidOperand(e.Message, e.Span) ]

    let compileView
            (notebook: Notebook)
            (surfaceBlock: string option)
            : Result<MathIr.MathIR * MathIr.Expr, Typecheck.TypeError list> =
        compileViewWith notebook surfaceBlock UserScript.empty
