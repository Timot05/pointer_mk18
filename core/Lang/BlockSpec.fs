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
    /// AST (e.g. `from-sketch` needs to walk a `VSketch` payload and
    /// emit MathIR primitives — that's a builtin).
    let private internalE (name: string) : Expr = mk (EVar (internal' name))

    let private nE (n: float) : Expr = mk (ENumber n)
    let private axE (a: Axis) : Expr = mk (EAxis a)

    let inline private ( +. ) (a: Expr) (b: Expr) = mk (EBinary(BinaryOp.Add, a, b))
    let inline private ( -. ) (a: Expr) (b: Expr) = mk (EBinary(BinaryOp.Sub, a, b))
    let inline private ( *. ) (a: Expr) (b: Expr) = mk (EBinary(BinaryOp.Mul, a, b))
    let inline private ( /. ) (a: Expr) (b: Expr) = mk (EBinary(BinaryOp.Div, a, b))

    let private sqrtE (e: Expr) = mk (EUnary(UnaryOp.Sqrt, e))
    let private absE  (e: Expr) = mk (EUnary(UnaryOp.Abs,  e))
    let private sqE   (e: Expr) = mk (EUnary(UnaryOp.Square, e))
    let private negE  (e: Expr) = mk (EUnary(UnaryOp.Neg,  e))
    let private minE  (a: Expr) (b: Expr) = mk (EBinary(BinaryOp.Min, a, b))
    let private maxE  (a: Expr) (b: Expr) = mk (EBinary(BinaryOp.Max, a, b))
    let private cmpE  (a: Expr) (b: Expr) = mk (EBinary(BinaryOp.Compare, a, b))

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

    /// `sphere radius` — `sqrt(x² + y² + z²) - radius`.
    let private sphereSpec : BlockSpec =
        let r = varE "radius"
        let xyz = sqE (axE AxisX) +. sqE (axE AxisY) +. sqE (axE AxisZ)
        let body = sqrtE xyz -. r
        { Name = "sphere"
          Body = lambda [ "radius", Type.Scalar ] body
          ScalarDefaults = Map.ofList [ "radius", 1.0 ] }

    /// `halfplane axis offset` — axis-aligned halfspace.
    /// `axis` is a discrete choice (0=X, 1=Y, 2=Z) stored as an `ArgScalar`
    /// and rendered in the UI as a dropdown (see `BlockList.renderInputRow`).
    /// SDF: `<axis> - offset`. The body below is a typed placeholder;
    /// `NotebookCompose` intercepts the `halfplane` spec name and emits
    /// the real AST with the concrete axis baked in — same pattern as
    /// `from-sketch`.
    let private halfplaneSpec : BlockSpec =
        let placeholder = sqrtE (sqE (axE AxisX)) -. nE 0.0
        { Name = "halfplane"
          Body = lambda [ "axis", Type.Scalar; "offset", Type.Scalar ] placeholder
          ScalarDefaults = Map.ofList [ "axis", 0.0; "offset", 0.0 ] }

    /// `box width height depth` — outside + inside form.
    /// outside = ||max(|p| - half, 0)||
    /// inside  = min(max(bx, max(by, bz)), 0)
    let private boxSpec : BlockSpec =
        let w = varE "width"
        let h = varE "height"
        let d = varE "depth"
        let two = nE 2.0
        let hx = w /. two
        let hy = h /. two
        let hz = d /. two
        let bx = absE (axE AxisX) -. hx
        let by = absE (axE AxisY) -. hy
        let bz = absE (axE AxisZ) -. hz
        let zero = nE 0.0
        let outside =
            sqrtE
                (sqE (maxE bx zero)
                 +. sqE (maxE by zero)
                 +. sqE (maxE bz zero))
        let inside = minE (maxE bx (maxE by bz)) zero
        let body = outside +. inside
        { Name = "box"
          Body = lambda
                    [ "width", Type.Scalar
                      "height", Type.Scalar
                      "depth", Type.Scalar ]
                    body
          ScalarDefaults = Map.ofList [ "width", 1.0; "height", 1.0; "depth", 1.0 ] }

    /// `cylinder radius height` — Y-axis cylinder.
    /// max(sqrt(x² + z²) - r, |y| - h/2)
    let private cylinderSpec : BlockSpec =
        let r = varE "radius"
        let h = varE "height"
        let two = nE 2.0
        let radial = sqrtE (sqE (axE AxisX) +. sqE (axE AxisZ)) -. r
        let axial = absE (axE AxisY) -. (h /. two)
        let body = maxE radial axial
        { Name = "cylinder"
          Body = lambda
                    [ "radius", Type.Scalar; "height", Type.Scalar ]
                    body
          ScalarDefaults = Map.ofList [ "radius", 1.0; "height", 2.0 ] }

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

    /// `mirror-symmetric-y rootY child` — evaluate the positive/root side
    /// of `child` on both sides of the XZ plane at `rootY`.
    let private mirrorSymmetricYSpec : BlockSpec =
        let rootY = varE "rootY"
        let child = varE "child"
        let mirroredY = rootY +. absE (axE AxisY -. rootY)
        let body = mk (ERemapAxes(child, axE AxisX, mirroredY, axE AxisZ))
        { Name = "mirror-symmetric-y"
          Body = lambda [ "rootY", Type.Scalar; "child", Type.Field ] body
          ScalarDefaults = Map.ofList [ "rootY", 0.0 ] }

    /// Iquilez polynomial smooth-min, with `radius <= eps` snapping to a
    /// hard min. The enable factor keeps radius=0 exact without adding a
    /// general AST `if` node:
    ///   h = max(k - abs(a-b), 0) / k
    ///   smin = min(a,b) - enabled * h^3 * k / 6
    let private smoothMinE (a: Expr) (b: Expr) (radius: Expr) : Expr =
        let eps = nE 1e-6
        let zero = nE 0.0
        let six = nE 6.0
        let k = maxE radius eps
        let h = maxE (k -. absE (a -. b)) zero /. k
        let enabled = maxE (cmpE radius eps) zero
        minE a b -. (enabled *. h *. h *. h *. k /. six)

    /// `union target tool radius` — smooth union via `smoothMin(target, tool, radius)`.
    let private unionSpec : BlockSpec =
        let body = smoothMinE (varE "target") (varE "tool") (varE "radius")
        { Name = "union"
          Body = lambda [ "target", Type.Field; "tool", Type.Field; "radius", Type.Scalar ] body
          ScalarDefaults = Map.ofList [ "radius", 0.0 ] }

    /// `intersect target tool radius` — `-smoothMin(-target, -tool, radius)`.
    let private intersectSpec : BlockSpec =
        let body = negE (smoothMinE (negE (varE "target")) (negE (varE "tool")) (varE "radius"))
        { Name = "intersect"
          Body = lambda [ "target", Type.Field; "tool", Type.Field; "radius", Type.Scalar ] body
          ScalarDefaults = Map.ofList [ "radius", 0.0 ] }

    /// `subtract target tool radius` — `-smoothMin(-target, tool, radius)`.
    let private subtractSpec : BlockSpec =
        let body = negE (smoothMinE (negE (varE "target")) (varE "tool") (varE "radius"))
        { Name = "subtract"
          Body = lambda [ "target", Type.Field; "tool", Type.Field; "radius", Type.Scalar ] body
          ScalarDefaults = Map.ofList [ "radius", 0.0 ] }

    /// `thicken amount child` — shifts iso-surface outward by `amount`.
    let private thickenSpec : BlockSpec =
        let body = varE "child" -. varE "amount"
        { Name = "thicken"
          Body = lambda
                    [ "amount", Type.Scalar; "child", Type.Field ]
                    body
          ScalarDefaults = Map.ofList [ "amount", 0.1 ] }

    /// `shell thickness child` — hollows the shape inward. The outer
    /// surface stays at the original iso (`f = 0`); the inner surface is
    /// the inward offset (`f = -thickness`). SDF: `max(f, -(f + t))` —
    /// the intersection of the original interior with the complement of
    /// the inward-offset interior. The shape doesn't grow.
    let private shellSpec : BlockSpec =
        let child = varE "child"
        let t = varE "thickness"
        let body = maxE child (negE (child +. t))
        { Name = "shell"
          Body = lambda
                    [ "thickness", Type.Scalar; "child", Type.Field ]
                    body
          ScalarDefaults = Map.ofList [ "thickness", 0.05 ] }

    /// `from-sketch sketch` — lower a 2D sketch to a 3D field. The body
    /// here is a placeholder; `NotebookCompose.compose` intercepts blocks
    /// with this spec name and emits an inlined `EFold(Min, [...primitive
    /// constructors])` AST built directly from the upstream SketchBody.
    /// The spec only needs to contribute its `Sketch -> Field` type
    /// signature to the env so mis-wired callers still get a clean error.
    /// An empty fold typechecks as `Field` and never runs.
    let private fromSketchSpec : BlockSpec =
        let body = mk (EFold(MathIr.FoldOp.Min, []))
        { Name = "from-sketch"
          Body = lambda [ "sketch", Type.Sketch ] body
          ScalarDefaults = Map.empty }

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
          Body = lambda [ "leading", Type.Sketch; "trailing", Type.Sketch ] body
          ScalarDefaults = Map.empty }

    do register sphereSpec
    do register boxSpec
    do register cylinderSpec
    do register halfplaneSpec
    do register translateSpec
    do register mirrorSymmetricYSpec
    do register unionSpec
    do register intersectSpec
    do register subtractSpec
    do register thickenSpec
    do register shellSpec
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

    /// Convenience: typed input list + output type for a spec.
    let typedInterface (s: BlockSpec) : TypeExtract.ExtractedSpec =
        TypeExtract.extract s.Body
