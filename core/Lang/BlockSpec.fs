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

    // All math-op helpers (`absE`, `cmpE`, `eqChoiceE`, `sqrtE`, etc.)
    // moved out alongside the migrated specs; user code calls them as
    // ordinary identifiers now (see `NotebookCompose.buildValueEnv`).

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

// union / intersect / subtract / thicken / shell migrated to the
    // default user script. Their bodies (and the shared `smooth_min`
    // helper) live in `ScriptSourceText` and route through
    // `UserScript.Specs`.

// revolve and extrude migrated to the default user script. They
    // both read the loop's `perpendicular_axis` member (a Scalar 0/1/2
    // seeded by the compose bridge from the parent sketch's plane) to
    // pick the right axis at typecheck time, removing the need for a
    // compose-time interceptor.

    /// `naca thickness camber chord span origin_x origin_y origin_z` —
    /// canonical NACA-style airfoil profile positioned in world XZ.
    /// Useful as a standalone Field block for blueprint overlay / placement
    /// preview; can be wrapped or composed with other Fields like any other
    /// primitive. The wing-remap pipeline still computes its own canonical
    /// NACA internally — this spec doesn't feed wing-remap-preview today.
    let private nacaSpec : BlockSpec =
        let names =
            [ "thickness"; "camber"; "chord"; "span"
              "origin_x"; "origin_y"; "origin_z" ]
        let body =
            names
            |> List.fold
                (fun acc n -> mk (EApply(acc, varE n)))
                (internalE "naca")
        { Name = "naca"
          Body =
              lambda
                  (names |> List.map (fun n -> n, Type.Scalar))
                  body
          ScalarDefaults =
              Map.ofList
                  [ "thickness", 0.18
                    "camber", 0.04
                    "chord", 2.0
                    "span", 1.0
                    "origin_x", 0.0
                    "origin_y", 0.0
                    "origin_z", 0.0 ] }

    /// `wing-remap-preview profile profile_chord profile_origin_{x,y,z}
    /// leading trailing` — remaps a profile Field (typically a `naca`
    /// block output) through two one-line XY sketch guides to produce
    /// a swept wing surface.
    ///
    /// The `profile_*` scalars must match the source profile's own
    /// placement params (chord length + origin) so that the wing block
    /// can correctly inverse-transform them before substituting the
    /// canonical chord (twoBody) and chord-scaled Z into the profile's
    /// internal `(sx, sz)` coordinates.
    let private wingRemapPreviewSpec : BlockSpec =
        let linePrimitiveType =
            Type.Primitive (
                Map.ofList
                    [ "signed_distance", Type.Field
                      "x0", Type.Scalar; "y0", Type.Scalar
                      "x1", Type.Scalar; "y1", Type.Scalar ])
        let names =
            [ "profile"
              "profile_chord"
              "profile_origin_x"
              "profile_origin_y"
              "profile_origin_z"
              "leading"
              "trailing" ]
        let body =
            names
            |> List.fold
                (fun acc n -> mk (EApply(acc, varE n)))
                (internalE "wing_remap_preview")
        let params' =
            [ "profile", Type.Field
              "profile_chord", Type.Scalar
              "profile_origin_x", Type.Scalar
              "profile_origin_y", Type.Scalar
              "profile_origin_z", Type.Scalar
              "leading", linePrimitiveType
              "trailing", linePrimitiveType ]
        { Name = "wing-remap-preview"
          Body = lambda params' body
          ScalarDefaults =
              Map.ofList
                  [ "profile_chord", 2.0
                    "profile_origin_x", 0.0
                    "profile_origin_y", 0.0
                    "profile_origin_z", 0.0 ] }

    // Intrinsic specs only — bodies that need MathIR-builder access at
    // compose time (wing-remap-preview), compose-time interceptors
    // (revolve), refined Loop param types the parser doesn't surface
    // (from-sketch), or identifier-name clashes / hyphenated names that
    // prevent migration to the user-script path (translate,
    // mirror-symmetric).
    do register translateSpec
    do register wingRemapPreviewSpec
    do register nacaSpec

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
        let linePrimitiveType =
            Type.Primitive (
                Map.ofList
                    [ "signed_distance", Type.Field
                      "x0", Type.Scalar; "y0", Type.Scalar
                      "x1", Type.Scalar; "y1", Type.Scalar ])
        Map.ofList [
            "wing_remap_preview",
            Type.curried
                [ Type.Field
                  Type.Scalar; Type.Scalar; Type.Scalar; Type.Scalar
                  linePrimitiveType; linePrimitiveType ]
                Type.Field

            "naca",
            Type.curried
                [ Type.Scalar; Type.Scalar; Type.Scalar; Type.Scalar
                  Type.Scalar; Type.Scalar; Type.Scalar ]
                Type.Field
        ]

    /// Convenience: typed input list + output type for a spec.
    let typedInterface (s: BlockSpec) : TypeExtract.ExtractedSpec =
        TypeExtract.extractWith intrinsicTypeEnv s.Body
