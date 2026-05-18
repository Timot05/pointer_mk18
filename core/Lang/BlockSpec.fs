namespace Server.Lang

// ---------------------------------------------------------------------------
// BlockSpec.fs — registry of native block specs.
//
// A spec's `Body` is a fully-self-contained AST: it builds the SDF math
// directly via `EAxis`, `EBinary`, `EUnary`, and `ERemapAxes`. There is no
// reliance on `@`-prefixed builtins; the AST is the implementation.
//
// Each lambda parameter carries its `Type.T` annotation so the
// typechecker (and `TypeExtract.extract` while it lives) can yield the
// typed input list without an external signature table. The frontend
// uses that list to render scalar editors vs ref-bubbles, and the
// driver uses it to decide which inputs come from slots vs upstream
// outputs.
// ---------------------------------------------------------------------------

module BlockSpec =

    open Token
    open Ast

    // ── AST construction helpers ───────────────────────────────────────────

    let private noSpan : Span = { Start = 0; Stop = 0 }

    let private user (name: string) : Ident =
        { Name = name; IdentKind = User; Span = noSpan }

    let private internal' (name: string) : Ident =
        { Name = name; IdentKind = Internal; Span = noSpan }

    let private mk (node: ExprNode) : Expr = { Node = node; Span = noSpan }

    let private varE (name: string) : Expr = mk (EVar (user name))

    /// `@name` reference — resolves through `Builtins.dispatch` at eval
    /// time. Used for spec bodies whose math isn't expressible in pure
    /// AST.
    let private internalE (name: string) : Expr = mk (EVar (internal' name))

    let private nE (n: float) : Expr = mk (ENumber n)
    let private axE (a: Axis) : Expr = mk (EAxis a)

    let inline private ( +. ) (a: Expr) (b: Expr) = mk (EBinary(BinaryOp.Add, a, b))
    let inline private ( -. ) (a: Expr) (b: Expr) = mk (EBinary(BinaryOp.Sub, a, b))
    let inline private ( *. ) (a: Expr) (b: Expr) = mk (EBinary(BinaryOp.Mul, a, b))
    let inline private ( /. ) (a: Expr) (b: Expr) = mk (EBinary(BinaryOp.Div, a, b))

    // Only the helpers the surviving intrinsics still use are kept —
    // `absE` / `cmpE` for mirror-symmetric's axis selector and inward
    // reflection math. `sqrt` / `sq` / `min` / `max` / `neg` helpers
    // moved out alongside the migrated specs; user code calls them
    // as ordinary identifiers now (see `NotebookCompose.buildValueEnv`).
    let private absE (e: Expr) = mk (EUnary(UnaryOp.Abs, e))
    let private cmpE (a: Expr) (b: Expr) = mk (EBinary(BinaryOp.Compare, a, b))

    /// Exact selector for small enum-like scalar parameters:
    /// `1 - abs(compare(value, choice))`, yielding 1 when equal and 0
    /// otherwise for the axis values 0/1/2.
    let private eqChoiceE (value: Expr) (choice: float) : Expr =
        nE 1.0 -. absE (cmpE value (nE choice))

    /// Build a curried lambda with explicit parameter type hints,
    /// outermost first.
    let private lambda (params': (string * Type.T) list) (body: Expr) : Expr =
        List.foldBack
            (fun (name, ty) acc -> mk (ELambda(user name, Some ty, acc)))
            params'
            body

    // ── Spec record + registry ─────────────────────────────────────────────

    type BlockSpec = {
        /// Stable kind identifier — what a `Block` instance stores.
        Name: string
        /// Curried lambda. Saturating it with arguments yields the block's
        /// single output value via `Eval.evalExpr`.
        Body: Expr
        /// Default values for scalar parameters (keyed by param name).
        ScalarDefaults: Map<string, float>
    }

    let private mutableTable = System.Collections.Generic.Dictionary<string, BlockSpec>()
    let private orderedNames = ResizeArray<string>()

    let private register (s: BlockSpec) =
        if not (mutableTable.ContainsKey s.Name) then
            orderedNames.Add s.Name
        mutableTable.[s.Name] <- s

    // ── Native specs ───────────────────────────────────────────────────────
    //
    // Each spec's body builds the SDF math directly. These are the same
    // expressions the old `Builtins.<kind>Impl` functions produced via
    // MathIR, just expressed in our AST instead.

    // sphere / halfplane / box / cylinder migrated to the default
    // user script (see `Server.Document.emptyDocument`'s
    // `ScriptSourceText` in core/Editor/Domain.fs). They route through
    // `UserScript.Specs` at compose time. Math-primitive callables
    // (sqrt / abs / min / max / compare / remap_axes) used by their
    // bodies are bound in `NotebookCompose.buildValueEnv`.

    /// `translate x y z child` — coord remap (sample at p - (x,y,z)).
    let private translateSpec : BlockSpec =
        let tx = varE "x"
        let ty = varE "y"
        let tz = varE "z"
        let child = varE "child"
        let body = mk (ERemapAxes(child, axE AxisX -. tx, axE AxisY -. ty, axE AxisZ -. tz))
        { Name = "translate"
          Body = lambda
                    [ "x", Type.Scalar
                      "y", Type.Scalar
                      "z", Type.Scalar
                      "child", Type.Field ]
                    body
          ScalarDefaults = Map.ofList [ "x", 0.0; "y", 0.0; "z", 0.0 ] }

    /// `mirror-symmetric axis root child` — evaluate the positive/root
    /// side of `child` on both sides of the plane perpendicular to the
    /// chosen axis at `root`. `axis` is a discrete choice (0=X, 1=Y,
    /// 2=Z) stored as a numeric Expr literal and rendered as a dropdown
    /// by the BlockList.
    let private mirrorSymmetricSpec : BlockSpec =
        let axis = varE "axis"
        let root = varE "root"
        let child = varE "child"
        let sx = eqChoiceE axis 0.0
        let sy = eqChoiceE axis 1.0
        let sz = eqChoiceE axis 2.0
        let choose selector original mirrored =
            selector *. mirrored +. (nE 1.0 -. selector) *. original
        let mirrorCoord coord = root +. absE (coord -. root)
        let x = axE AxisX
        let y = axE AxisY
        let z = axE AxisZ
        let body =
            mk (ERemapAxes(
                child,
                choose sx x (mirrorCoord x),
                choose sy y (mirrorCoord y),
                choose sz z (mirrorCoord z)))
        { Name = "mirror-symmetric"
          Body = lambda
                    [ "axis", Type.Scalar
                      "root", Type.Scalar
                      "child", Type.Field ]
                    body
          ScalarDefaults = Map.ofList [ "axis", 1.0; "root", 0.0 ] }

    // union / intersect / subtract / thicken / shell migrated to the
    // default user script. Their bodies (and the shared `smooth_min`
    // helper) live in `ScriptSourceText` and route through
    // `UserScript.Specs`.

    /// `from-sketch loop` — project a sketch loop to its 3D signed-
    /// distance field. The parameter is a `Loop` whose refinement
    /// requires `signed_distance: Field`; the body is just
    /// `loop.signed_distance`, so this is effectively a typed identity
    /// that lifts a 2D boundary into a Field for downstream blocks.
    ///
    /// Users wire individual loops via paths: a sketch block named
    /// `profile` exposes `profile.loop_0`, `profile.loop_1`, ... — any
    /// of which can be the `loop` arg here. Compose handles the path
    /// resolution via the generic spec path; no interceptor is needed.
    let private fromSketchSpec : BlockSpec =
        let loopWithSd =
            Type.Loop (Map.ofList [ "signed_distance", Type.Field ])
        let body = mk (EPath [ user "loop"; user "signed_distance" ])
        { Name = "from-sketch"
          Body = lambda [ "loop", loopWithSd ] body
          ScalarDefaults = Map.empty }

    // revolve and extrude migrated to the default user script. They
    // both read the loop's `perpendicular_axis` member (a Scalar 0/1/2
    // seeded by the compose bridge from the parent sketch's plane) to
    // pick the right axis at typecheck time, removing the need for a
    // compose-time interceptor.

    /// `wing-remap-preview leading trailing` — experimental field that
    /// remaps a canonical unit chord/span strip through two one-line XY
    /// sketch guides. This validates the curve-distance/remap path before
    /// adding airfoil thickness or a closed wing solid.
    let private wingRemapPreviewSpec : BlockSpec =
        let body =
            mk (EApply(
                mk (EApply(internalE "wing_remap_preview", varE "leading")),
                varE "trailing"))
        { Name = "wing-remap-preview"
          Body = lambda [ "leading", Type.Sketch Map.empty; "trailing", Type.Sketch Map.empty ] body
          ScalarDefaults = Map.empty }

    // Intrinsic specs only — bodies that need MathIR-builder access at
    // compose time (wing-remap-preview), compose-time interceptors
    // (revolve), refined Loop param types the parser doesn't surface
    // (from-sketch), or identifier-name clashes / hyphenated names that
    // prevent migration to the user-script path (translate,
    // mirror-symmetric).
    do register translateSpec
    do register mirrorSymmetricSpec
    do register fromSketchSpec
    do register wingRemapPreviewSpec

    // ── Lookups ────────────────────────────────────────────────────────────

    let tryFind (name: string) : BlockSpec option =
        match mutableTable.TryGetValue name with
        | true, s -> Some s
        | _ -> None

    let find (name: string) : BlockSpec =
        match tryFind name with
        | Some s -> s
        | None -> failwithf "BlockSpec.find: no spec named '%s'" name

    let allNames () : string list = List.ofSeq orderedNames

    let all () : BlockSpec list =
        orderedNames |> Seq.map (fun n -> mutableTable.[n]) |> List.ofSeq

    let private intrinsicTypeEnv : Typecheck.TypeEnv =
        Map.ofList [
            "wing_remap_preview",
            Type.curried [ Type.Sketch Map.empty; Type.Sketch Map.empty ] Type.Field
        ]

    /// Convenience: typed input list + output type for a spec.
    let typedInterface (s: BlockSpec) : TypeExtract.ExtractedSpec =
        TypeExtract.extractWith intrinsicTypeEnv s.Body
