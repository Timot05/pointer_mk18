module NotebookTests

open Xunit
open Server
open Server.Lang
open Server.Lang.Notebook

// ─── Helpers ────────────────────────────────────────────────────────────────

let private nativeBlock id name specName args : Block =
    { Id = id
      Name = name
      Body = NativeBody(specName, Map.ofList args)
      Visibility = VIsosurface
      ColorIndex = 0
      SlicePlane = defaultSlicePlane }

// Convenience constructors used by the test fixtures: block args are
// now `Ast.Expr` directly. A scalar literal is `ENumber n`; a wire to
// upstream block "name" is `EVar "name"`.
let private argScalar (n: float) : Ast.Expr = AstBuilder.numE n
let private argRef (name: string) : Ast.Expr = AstBuilder.varE name

let private sketchBlockOf id name (sketch: ActionSketch) (plane: SketchPlane) : Block =
    // Run loop reconciliation so the sketch carries a populated Loops
    // registry — every real edit in the editor routes through normalize,
    // so test fixtures should too.
    let normalized = SketchLoops.normalize sketch
    { Id = id
      Name = name
      Body = SketchBody { Sketch = normalized; Plane = plane }
      Visibility = VIsosurface
      ColorIndex = 0
      SlicePlane = defaultSlicePlane }

let private notebookOf (blocks: Block list) : Notebook =
    { NextId = List.length blocks; Blocks = blocks }

/// Default user-script source (sphere/box/union/etc. as user specs)
/// auto-injected by `composeChecked` / `evaluateOk`. Sourced from the
/// boot document so tests see the same standard-library shapes the
/// editor presents to users — keeps the assertions on shape and types
/// honest end-to-end.
let private defaultUserScript : UserScript.Result =
    UserScript.analyze ((Server.Document.emptyDocument ()).ScriptSourceText)

/// Test-side analog of `NotebookCompose.compile` that injects the
/// default standard-library script — needed since sphere / union / etc.
/// migrated out of `BlockSpec.fs` into the default script.
let private compileWithDefault (nb: Notebook) =
    NotebookCompose.compileWith nb defaultUserScript

let private composeChecked (nb: Notebook) : NotebookCompose.Composed =
    let composed = NotebookCompose.composeWith nb defaultUserScript
    match Typecheck.elaborate composed.TypeEnv composed.Ast with
    | Ok _ -> composed
    | Error errs ->
        let msg = errs |> List.map Typecheck.formatError |> String.concat "; "
        failwithf "typecheck failed: %s" msg

let private evaluateOk (nb: Notebook) : NotebookCompose.Composed * NotebookCompose.EvalResult =
    let composed = composeChecked nb
    match NotebookCompose.evaluate nb composed with
    | Ok result -> composed, result
    | Error e -> failwithf "evaluate failed: %s" e.Message

/// Compose + evaluate a notebook against a user-script source string.
/// Routes through `UserScript.analyze` + `NotebookCompose.composeWith`
/// so user-defined block kinds (and the math-primitive callables bound
/// in `buildValueEnv`) are in scope.
let private evaluateWithScriptOk (script: string) (nb: Notebook)
        : NotebookCompose.Composed * NotebookCompose.EvalResult =
    let scriptResult = UserScript.analyze script
    match scriptResult.ParseError with
    | Some pe -> failwithf "user-script parse failed: %A" pe
    | None -> ()
    match scriptResult.AnalysisErrors with
    | [] -> ()
    | errs ->
        let msg = errs |> List.map (fun (n, m) -> sprintf "%s: %s" n m) |> String.concat "; "
        failwithf "user-script analysis failed: %s" msg
    let composed = NotebookCompose.composeWith nb scriptResult
    match Typecheck.elaborate composed.TypeEnv composed.Ast with
    | Ok _ -> ()
    | Error errs ->
        let msg = errs |> List.map Typecheck.formatError |> String.concat "; "
        failwithf "typecheck failed: %s" msg
    match NotebookCompose.evaluate nb composed with
    | Ok result -> composed, result
    | Error e -> failwithf "evaluate failed: %s" e.Message

let private bindingOf (name: string) (result: NotebookCompose.EvalResult) : Value.Value =
    match Map.tryFind name result.Bindings with
    | Some v -> v
    | None -> failwithf "missing binding '%s'" name

let private simpleLineSketch : ActionSketch =
    { Entities =
        [ REPoint("p0", 0.0, 0.0)
          REPoint("p1", 1.0, 0.0)
          RELine("l0", "p0", "p1") ]
      Constraints = []; Loops = [] }

let private verticalLineSketch x : ActionSketch =
    { Entities =
        [ REPoint("p0", x, 0.0)
          REPoint("p1", x, 2.0)
          RELine("l0", "p0", "p1") ]
      Constraints = []; Loops = [] }

// ─── Native blocks ──────────────────────────────────────────────────────────

[<Fact>]
let ``single sphere block produces a Field output`` () =
    let nb =
        notebookOf [
            nativeBlock 0 "shape" "sphere" [ "radius", argScalar 1.0 ]
        ]
    let _, result = evaluateOk nb
    match bindingOf "shape" result with
    | Value.VField _ -> ()
    | other -> failwithf "expected VField, got %A" other

[<Fact>]
let ``translate referencing an upstream sphere yields a RemapAxes-rooted Field`` () =
    let nb =
        notebookOf [
            nativeBlock 0 "shape" "sphere" [ "radius", argScalar 1.0 ]
            nativeBlock 1 "shifted" "translate"
                [ "x", argScalar 2.0
                  "y", argScalar 0.0
                  "z", argScalar 0.0
                  "child", argRef "shape" ]
        ]
    let _, result = evaluateOk nb
    match bindingOf "shifted" result with
    | Value.VField root ->
        let rootNode = result.Ir.Nodes.[root.Id]
        Assert.Equal(MathIr.NodeKind.RemapAxes, rootNode.Kind)
    | other -> failwithf "expected VField, got %A" other

[<Fact>]
let ``user script can call sqrt over field expressions to define a sphere`` () =
    // Verifies the math-primitive callable bindings in
    // NotebookCompose.buildValueEnv: `sqrt` resolves as a callable
    // closure that emits `EUnary(Sqrt, _)`, and the resulting MathIR
    // root matches what BlockSpec.fs's `sphereSpec` produced before
    // the script-migration refactor. Ground-truth shape for moving
    // sphere/box/etc. out of BlockSpec.fs.
    let script = "let my_sphere = fun (r: Scalar) -> sqrt (x*x + y*y + z*z) - r end"
    let nb =
        notebookOf [
            nativeBlock 0 "shape" "my_sphere" [ "r", argScalar 1.0 ]
        ]
    let _, result = evaluateWithScriptOk script nb
    match bindingOf "shape" result with
    | Value.VField root ->
        let rootNode = result.Ir.Nodes.[root.Id]
        Assert.Equal(MathIr.NodeKind.BinaryK, rootNode.Kind)
        Assert.Equal(int MathIr.Binary.Sub, rootNode.Op)
        let leftNode = result.Ir.Nodes.[rootNode.A]
        Assert.Equal(MathIr.NodeKind.UnaryK, leftNode.Kind)
        Assert.Equal(int MathIr.Unary.Sqrt, leftNode.Op)
    | other -> failwithf "expected VField, got %A" other

[<Fact>]
let ``mirror symmetric wraps an upstream field in a RemapAxes node`` () =
    let nb =
        notebookOf [
            nativeBlock 0 "half" "sphere" [ "radius", argScalar 1.0 ]
            nativeBlock 1 "full" "mirror_symmetric"
                [ "axis", argScalar 1.0
                  "root", argScalar 0.0
                  "child", argRef "half" ]
        ]
    let _, result = evaluateOk nb
    match bindingOf "full" result with
    | Value.VField root ->
        let rootNode = result.Ir.Nodes.[root.Id]
        // mirror_symmetric is now `min(remap_pos, remap_neg)` so the
        // root is a Binary Min node whose children are both RemapAxes.
        Assert.Equal(MathIr.NodeKind.BinaryK, rootNode.Kind)
        Assert.Equal(int MathIr.Binary.Min, rootNode.Op)
        Assert.Equal(MathIr.NodeKind.RemapAxes, result.Ir.Nodes.[rootNode.A].Kind)
        Assert.Equal(MathIr.NodeKind.RemapAxes, result.Ir.Nodes.[rootNode.B].Kind)
    | other -> failwithf "expected VField, got %A" other

[<Fact>]
let ``union of two spheres accepts target tool and radius inputs`` () =
    let nb =
        notebookOf [
            nativeBlock 0 "a" "sphere" [ "radius", argScalar 1.0 ]
            nativeBlock 1 "b" "sphere" [ "radius", argScalar 2.0 ]
            nativeBlock 2 "u" "union" [ "target", argRef "a"; "tool", argRef "b"; "radius", argScalar 0.25 ]
        ]
    let _, result = evaluateOk nb
    match bindingOf "u" result with
    | Value.VField root ->
        let rootNode = result.Ir.Nodes.[root.Id]
        Assert.Equal(MathIr.NodeKind.BinaryK, rootNode.Kind)
        Assert.Equal(int MathIr.Binary.Sub, rootNode.Op)
    | other -> failwithf "expected VField, got %A" other

[<Fact>]
let ``unwired ref records a block error on the compose path`` () =
    let nb =
        notebookOf [
            nativeBlock 0 "shape" "sphere" [ "radius", argScalar 1.0 ]
            nativeBlock 1 "broken" "translate"
                [ "x", argScalar 1.0
                  "y", argScalar 0.0
                  "z", argScalar 0.0 ]   // "child" omitted = unwired
            nativeBlock 2 "tail" "sphere" [ "radius", argScalar 0.5 ]
        ]
    let result = compileWithDefault nb
    Assert.True(Map.containsKey 1 result.BlockErrors, "broken block should have an error")
    Assert.Equal(None, result.Bytes)
    Assert.Equal(Some Type.Field, Map.tryFind 2 result.BlockOutputs)

// ─── Sketch blocks ──────────────────────────────────────────────────────────

[<Fact>]
let ``sketch block surfaces as a VSketch output`` () =
    let nb = notebookOf [ sketchBlockOf 0 "outline" simpleLineSketch XY ]
    let _, result = evaluateOk nb
    match bindingOf "outline" result with
    | Value.VSketch sv ->
        Assert.Equal(3, List.length sv.Sketch.Entities)
        Assert.Equal(XY, sv.Plane)
    | other -> failwithf "expected VSketch, got %A" other

[<Fact>]
let ``wing remap preview consumes two line primitives and emits remapped profile`` () =
    // One sketch carrying two parallel guide lines; the wing block
    // wires each line individually via `sketch.line_N` paths. The
    // top-level-primitive refinement on the sketch exposes line_0 /
    // line_1 directly.
    let sk : ActionSketch =
        { Entities =
            [ REPoint("a0", 0.0, 0.0)
              REPoint("a1", 0.0, 2.0)
              REPoint("b0", 1.0, 0.0)
              REPoint("b1", 1.0, 2.0)
              RELine("l_le", "a0", "a1")
              RELine("l_te", "b0", "b1") ]
          Constraints = []; Loops = [] }
    let nb =
        notebookOf [
            sketchBlockOf 0 "guides" sk XY
            nativeBlock 2 "naca_profile" "naca"
                [ "thickness", AstBuilder.numE 0.18
                  "camber", AstBuilder.numE 0.04
                  "chord", AstBuilder.numE 2.0
                  "span", AstBuilder.numE 1.0
                  "origin_x", AstBuilder.numE 0.0
                  "origin_y", AstBuilder.numE 0.0
                  "origin_z", AstBuilder.numE 0.0 ]
            nativeBlock 1 "wing" "wing-remap-preview"
                [ "profile", AstBuilder.varE "naca_profile"
                  "profile_chord", AstBuilder.numE 2.0
                  "profile_origin_x", AstBuilder.numE 0.0
                  "profile_origin_y", AstBuilder.numE 0.0
                  "profile_origin_z", AstBuilder.numE 0.0
                  "leading", AstBuilder.pathE [ "guides"; "line_0" ]
                  "trailing", AstBuilder.pathE [ "guides"; "line_1" ] ]
        ]
    let _, result = evaluateOk nb
    match bindingOf "wing" result with
    | Value.VField root ->
        let rootNode = result.Ir.Nodes.[root.Id]
        Assert.Equal(MathIr.NodeKind.BinaryK, rootNode.Kind)
        Assert.Equal(int MathIr.Binary.Max, rootNode.Op)
        let wgsl = MathIrWgsl.emit result.Ir root "wing_preview"
        Assert.Contains("fn wing_preview", wgsl)
    | other -> failwithf "expected VField, got %A" other

// ─── Sketch primitives (subtree nodes) ────────────────────────────────────
//
// `from-sketch` now takes a Loop (path-resolved), not a whole sketch.
// Loops are detected by `SketchLoops.normalize`, so the test sketches
// here use `sketchBlockOf` (which normalizes) and wire the resulting
// `loop_0` member into the block via `AstBuilder.pathE [sketchName;
// "loop_0"]`. Orphan-primitive sketches (parallel lines, single line)
// don't form a loop and so don't have a typed handle to wire — that
// behavior was retired alongside the compose interceptor.

// The two `from-sketch` MathIR-shape tests were retired alongside the
// from-sketch spec itself. The loop-lowering math (`buildLoopExpr` in
// `NotebookCompose.fs`) is now exercised indirectly through the
// surviving `revolve` and `extrude` tests — both wrap a loop's
// signed_distance through the same lowering path, so a regression in
// the loop SDF surfaces as an extrude/revolve failure.

[<Fact>]
let ``revolve over a circle loop wraps the 2D SDF in RemapAxes`` () =
    // Circle on XY plane revolved around Y → torus-like 3D field. The
    // script-defined `revolve` ends in `remap_axes loop.signed_distance
    // new_x new_y z`, so the root is a RemapAxes. Exact substitution
    // expressions differ from the prior intrinsic (selector arithmetic
    // over the loop's `perpendicular_axis`); we just check root shape
    // and a sqrt somewhere in the radial substitution.
    let sk =
        { Entities =
            [ REPoint("c", 2.0, 0.0)
              RECircle("circ", "c", 0.5) ]
          Constraints = []; Loops = [] }
    let nb =
        notebookOf [
            sketchBlockOf 0 "sk" sk XY
            nativeBlock 1 "torus" "revolve" [ "loop", AstBuilder.pathE [ "sk"; "loop_0" ] ]
        ]
    let _, result = evaluateOk nb
    match bindingOf "torus" result with
    | Value.VField root ->
        let rootNode = result.Ir.Nodes.[root.Id]
        Assert.Equal(MathIr.NodeKind.RemapAxes, rootNode.Kind)
        // Confirm at least one sqrt node lives in the IR — the radial
        // substitution. The exact path depth depends on selector
        // simplification, so we just check presence rather than shape.
        let hasSqrt =
            result.Ir.Nodes
            |> Seq.exists (fun n ->
                n.Kind = MathIr.NodeKind.UnaryK && n.Op = int MathIr.Unary.Sqrt)
        Assert.True(hasSqrt, "expected a Sqrt node for the radial substitution")
    | other -> failwithf "expected VField, got %A" other

[<Fact>]
let ``revolve block output is typed as Field`` () =
    let sk =
        { Entities =
            [ REPoint("c", 1.0, 0.0)
              RECircle("circ", "c", 0.25) ]
          Constraints = []; Loops = [] }
    let nb =
        notebookOf [
            sketchBlockOf 0 "sk" sk XY
            nativeBlock 1 "tor" "revolve" [ "loop", AstBuilder.pathE [ "sk"; "loop_0" ] ]
        ]
    let composed = composeChecked nb
    Assert.Equal(Some Type.Field, Map.tryFind 1 composed.BlockOutputs)

[<Fact>]
let ``revolve with no loop wired records a block error`` () =
    let nb =
        // "loop" omitted = unwired (compose falls back to UNWIRED_PLACEHOLDER)
        notebookOf [ nativeBlock 0 "tor" "revolve" [] ]
    let result = compileWithDefault nb
    Assert.True(
        Map.containsKey 0 result.BlockErrors,
        "unwired revolve should surface a block error")

// ─── extrude ──────────────────────────────────────────────────────────────

[<Fact>]
let ``extrude over a circle loop wraps the 2D SDF in a Max-rooted slab clamp`` () =
    // Circle on XY plane extruded along Z from 0 to 1 → finite cylinder.
    // The script-defined `extrude` produces
    //   `max(loop.signed_distance, max(perp - top, bottom - perp))`,
    // so the root is BinaryK Max and the right child is also a Max.
    // (The `perp` expression is a selector-driven sum of axes rather
    // than a bare `EAxis` — exact substructure depends on selector
    // arithmetic.)
    let sk =
        { Entities =
            [ REPoint("c", 0.0, 0.0)
              RECircle("circ", "c", 0.5) ]
          Constraints = []; Loops = [] }
    let nb =
        notebookOf [
            sketchBlockOf 0 "sk" sk XY
            nativeBlock 1 "post" "extrude"
                [ "loop", AstBuilder.pathE [ "sk"; "loop_0" ]
                  "bottom", argScalar 0.0
                  "top", argScalar 1.0 ]
        ]
    let _, result = evaluateOk nb
    match bindingOf "post" result with
    | Value.VField root ->
        let rootNode = result.Ir.Nodes.[root.Id]
        Assert.Equal(MathIr.NodeKind.BinaryK, rootNode.Kind)
        Assert.Equal(int MathIr.Binary.Max, rootNode.Op)
        let slabNode = result.Ir.Nodes.[rootNode.B]
        Assert.Equal(MathIr.NodeKind.BinaryK, slabNode.Kind)
        Assert.Equal(int MathIr.Binary.Max, slabNode.Op)
    | other -> failwithf "expected VField, got %A" other

[<Fact>]
let ``extrude block output is typed as Field`` () =
    let sk =
        { Entities =
            [ REPoint("c", 0.0, 0.0)
              RECircle("circ", "c", 0.25) ]
          Constraints = []; Loops = [] }
    let nb =
        notebookOf [
            sketchBlockOf 0 "sk" sk XY
            nativeBlock 1 "post" "extrude"
                [ "loop", AstBuilder.pathE [ "sk"; "loop_0" ]
                  "bottom", argScalar (-0.5)
                  "top", argScalar 0.5 ]
        ]
    let composed = composeChecked nb
    Assert.Equal(Some Type.Field, Map.tryFind 1 composed.BlockOutputs)

[<Fact>]
let ``extrude on XZ sketch picks the Y axis for the slab`` () =
    // Same circle but on the XZ plane → the loop's `perpendicular_axis`
    // is Y (=1), so the slab clamp's `perp` selector picks the Y axis.
    // With the script-defined `extrude`, we can't easily assert on a
    // single MathIR node — `perp` is `0*x + 1*y + 0*z` after the
    // selectors evaluate. Best proxy: confirm the block compiles to
    // a Field and the IR contains at least one Y-axis Var (which the
    // selector arithmetic always references).
    let sk =
        { Entities =
            [ REPoint("c", 0.0, 0.0)
              RECircle("circ", "c", 0.5) ]
          Constraints = []; Loops = [] }
    let nb =
        notebookOf [
            sketchBlockOf 0 "sk" sk XZ
            nativeBlock 1 "post" "extrude"
                [ "loop", AstBuilder.pathE [ "sk"; "loop_0" ]
                  "bottom", argScalar 0.0
                  "top", argScalar 2.0 ]
        ]
    let _, result = evaluateOk nb
    match bindingOf "post" result with
    | Value.VField _ ->
        let hasYVar =
            result.Ir.Nodes
            |> Seq.exists (fun n -> n.Kind = MathIr.NodeKind.Var && n.Op = int MathIr.Axis.Y)
        Assert.True(hasYVar, "extrude on an XZ sketch should reference the Y axis")
    | other -> failwithf "expected VField, got %A" other

// ─── Fold node ─────────────────────────────────────────────────────────────

[<Fact>]
let ``Fold node packs children into NodeRefs and round-trips through evalPoint`` () =
    let ir = MathIr.MathIR()
    let a = ir.Constant 3.0
    let b = ir.Constant 1.0
    let c = ir.Constant 2.0
    let folded = ir.Fold(MathIr.FoldOp.Min, [ a; b; c ])

    let foldNode = ir.Nodes.[folded.Id]
    Assert.Equal(MathIr.NodeKind.Fold, foldNode.Kind)
    Assert.Equal(int MathIr.FoldOp.Min, foldNode.Op)
    Assert.Equal(3, foldNode.B)               // count
    Assert.Equal(3, ir.NodeRefs.Count)         // a, b, c packed in
    Assert.Equal(0, foldNode.A)                // start index
    Assert.Equal(a.Id, ir.NodeRefs.[0])
    Assert.Equal(b.Id, ir.NodeRefs.[1])
    Assert.Equal(c.Id, ir.NodeRefs.[2])

[<Fact>]
let ``Fold emits a fn through MathIrWgsl with one return per entry`` () =
    let ir = MathIr.MathIR()
    let a = ir.Constant 3.0
    let b = ir.Constant 1.0
    let folded = ir.Fold(MathIr.FoldOp.Min, [ a; b ])
    let wgsl = MathIrWgsl.emit ir folded "fold_test"
    Assert.Contains("fn fold_test", wgsl)
    Assert.Contains("return", wgsl)

// ─── compileView ───────────────────────────────────────────────────────────

[<Fact>]
let ``compileView returns the last Field output as the render root`` () =
    let nb =
        notebookOf [
            nativeBlock 0 "shape" "sphere" [ "radius", argScalar 1.5 ]
        ]
    match NotebookCompose.compileViewWith nb None defaultUserScript with
    | Ok (ir, root) ->
        Assert.True(ir.Nodes.Count > 0)
        let rootNode = ir.Nodes.[root.Id]
        Assert.Equal(MathIr.NodeKind.BinaryK, rootNode.Kind)
        Assert.Equal(int MathIr.Binary.Sub, rootNode.Op)
    | Error errs ->
        let msg = errs |> List.map Typecheck.formatError |> String.concat "; "
        failwithf "compileView failed: %s" msg

[<Fact>]
let ``compose preserves visible field color indices`` () =
    let block =
        { nativeBlock 0 "shape" "sphere" [ "radius", argScalar 1.5 ] with
            ColorIndex = 8 }
    let composed = NotebookCompose.composeWith (notebookOf [ block ]) defaultUserScript
    Assert.Equal<int list>([ 8 ], composed.VisibleFieldColorIndices)

// ─── User-defined specs (script editor) ─────────────────────────────────────

[<Fact>]
let ``composeWith: user spec seeds typeEnv with curried function type`` () =
    // `x` is the pre-bound Field-typed axis identifier (see
    // `NotebookCompose.composeWith` / `UserScript.nativeTypeEnv`), so the
    // body lifts to `Field` and the spec gets `Scalar -> Field`.
    let userScript =
        UserScript.analyze "let donut = fun (r: Scalar) -> r + x end"
    let nb = notebookOf []
    let composed = NotebookCompose.composeWith nb userScript
    match Map.tryFind "donut" composed.TypeEnv with
    | Some (Type.Fun(Type.Scalar, Type.Field)) -> ()
    | other -> failwithf "expected Scalar -> Field, got %A" other

[<Fact>]
let ``composeWith: user-defined block resolves and typechecks against user spec`` () =
    let userScript =
        UserScript.analyze "let donut = fun (r: Scalar) -> r + x end"
    let nb =
        notebookOf [ nativeBlock 0 "ring" "donut" [ "r", argScalar 0.5 ] ]
    let composed = NotebookCompose.composeWith nb userScript
    // Block output should be the user spec's declared output (Field).
    Assert.Equal(Some Type.Field, Map.tryFind 0 composed.BlockOutputs)
    // Typecheck should pass.
    match Typecheck.elaborate composed.TypeEnv composed.Ast with
    | Ok _ -> ()
    | Error errs ->
        let msg = errs |> List.map Typecheck.formatError |> String.concat "; "
        failwithf "expected typecheck to pass with user spec, got: %s" msg

[<Fact>]
let ``composeWith: unknown spec still errors when no user spec covers it`` () =
    let nb =
        notebookOf [ nativeBlock 0 "weird" "donut" [ "r", argScalar 0.5 ] ]
    // No user script — donut is unknown.
    let composed = NotebookCompose.composeWith nb UserScript.empty
    match Typecheck.elaborate composed.TypeEnv composed.Ast with
    | Ok _ -> failwith "expected typecheck failure for unknown spec"
    | Error _ -> ()

[<Fact>]
let ``compileWith: user-defined block compiles to MathIR end to end`` () =
    // A genuinely Field-valued user spec — wraps `sphere` from the
    // default script. The standard-library source has to be in the
    // analyze input so `sphere` resolves; after the BlockSpec→script
    // migration, `nativeTypeEnv` no longer carries sphere on its own.
    let source =
        (Server.Document.emptyDocument ()).ScriptSourceText
        + "\nlet bubble = fun (r: Scalar) -> sphere r end\n"
    let userScript = UserScript.analyze source
    let nb =
        notebookOf [ nativeBlock 0 "ring" "bubble" [ "r", argScalar 1.0 ] ]
    let result = NotebookCompose.compileWith nb userScript
    // The single visible Field block should compile to a non-empty IR.
    Assert.True(result.Ir.IsSome, "expected an IR")
    Assert.True(result.BlockErrors.IsEmpty, "expected no block errors")

[<Fact>]
let ``compileWith: default capsule script compiles end to end`` () =
    // The capsule definition in Document.emptyDocument uses let-blocks,
    // translate, sphere, cylinder, union — all the patterns a user is
    // likely to start from. Instantiating one as a block must produce
    // a clean compile with a non-empty IR.
    let doc = Server.Document.emptyDocument ()
    let userScript = UserScript.analyze doc.ScriptSourceText
    let nb =
        notebookOf
            [ nativeBlock 0 "pill" "capsule"
                [ "radius", argScalar 0.5
                  "length", argScalar 2.0 ] ]
    let result = NotebookCompose.compileWith nb userScript
    Assert.True(result.Ir.IsSome, "expected an IR from capsule compile")
    Assert.True(
        result.BlockErrors.IsEmpty,
        sprintf "expected no block errors, got %A" result.BlockErrors)
    // IR must contain real geometry — at least the sphere + cylinder +
    // union nodes (≫ 10). On .NET, MathIrCodec.serialize no-ops (Fable
    // Uint8Array bindings), so `Bytes` / `Summary` aren't checked here.
    let ir = result.Ir.Value
    Assert.True(
        ir.Nodes.Count > 10,
        sprintf "expected non-trivial IR, got %d nodes" ir.Nodes.Count)
