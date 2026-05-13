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

let private sketchBlockOf id name (sketch: ActionSketch) (plane: SketchPlane) : Block =
    { Id = id
      Name = name
      Body = SketchBody { Sketch = sketch; Plane = plane }
      Visibility = VIsosurface
      ColorIndex = 0
      SlicePlane = defaultSlicePlane }

let private notebookOf (blocks: Block list) : Notebook =
    { NextId = List.length blocks; Blocks = blocks }

let private composeChecked (nb: Notebook) : NotebookCompose.Composed =
    let composed = NotebookCompose.compose nb
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

let private bindingOf (name: string) (result: NotebookCompose.EvalResult) : Value.Value =
    match Map.tryFind name result.Bindings with
    | Some v -> v
    | None -> failwithf "missing binding '%s'" name

let private simpleLineSketch : ActionSketch =
    { Entities =
        [ REPoint("p0", 0.0, 0.0)
          REPoint("p1", 1.0, 0.0)
          RELine("l0", "p0", "p1") ]
      Constraints = [] }

let private verticalLineSketch x : ActionSketch =
    { Entities =
        [ REPoint("p0", x, 0.0)
          REPoint("p1", x, 2.0)
          RELine("l0", "p0", "p1") ]
      Constraints = [] }

// ─── Native blocks ──────────────────────────────────────────────────────────

[<Fact>]
let ``single sphere block produces a Field output`` () =
    let nb =
        notebookOf [
            nativeBlock 0 "shape" "sphere" [ "radius", ArgScalar 1.0 ]
        ]
    let _, result = evaluateOk nb
    match bindingOf "shape" result with
    | Value.VField _ -> ()
    | other -> failwithf "expected VField, got %A" other

[<Fact>]
let ``translate referencing an upstream sphere yields a RemapAxes-rooted Field`` () =
    let nb =
        notebookOf [
            nativeBlock 0 "shape" "sphere" [ "radius", ArgScalar 1.0 ]
            nativeBlock 1 "shifted" "translate"
                [ "x", ArgScalar 2.0
                  "y", ArgScalar 0.0
                  "z", ArgScalar 0.0
                  "child", ArgRef (Some 0) ]
        ]
    let _, result = evaluateOk nb
    match bindingOf "shifted" result with
    | Value.VField root ->
        let rootNode = result.Ir.Nodes.[root.Id]
        Assert.Equal(MathIr.NodeKind.RemapAxes, rootNode.Kind)
    | other -> failwithf "expected VField, got %A" other

[<Fact>]
let ``mirror symmetric wraps an upstream field in a RemapAxes node`` () =
    let nb =
        notebookOf [
            nativeBlock 0 "half" "sphere" [ "radius", ArgScalar 1.0 ]
            nativeBlock 1 "full" "mirror-symmetric"
                [ "axis", ArgScalar 1.0
                  "root", ArgScalar 0.0
                  "child", ArgRef (Some 0) ]
        ]
    let _, result = evaluateOk nb
    match bindingOf "full" result with
    | Value.VField root ->
        let rootNode = result.Ir.Nodes.[root.Id]
        Assert.Equal(MathIr.NodeKind.RemapAxes, rootNode.Kind)
    | other -> failwithf "expected VField, got %A" other

[<Fact>]
let ``union of two spheres accepts target tool and radius inputs`` () =
    let nb =
        notebookOf [
            nativeBlock 0 "a" "sphere" [ "radius", ArgScalar 1.0 ]
            nativeBlock 1 "b" "sphere" [ "radius", ArgScalar 2.0 ]
            nativeBlock 2 "u" "union" [ "target", ArgRef (Some 0); "tool", ArgRef (Some 1); "radius", ArgScalar 0.25 ]
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
            nativeBlock 0 "shape" "sphere" [ "radius", ArgScalar 1.0 ]
            nativeBlock 1 "broken" "translate"
                [ "x", ArgScalar 1.0
                  "y", ArgScalar 0.0
                  "z", ArgScalar 0.0
                  "child", ArgRef None ]
            nativeBlock 2 "tail" "sphere" [ "radius", ArgScalar 0.5 ]
        ]
    let result = NotebookCompose.compile nb
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
let ``wing remap preview consumes two sketch refs and emits remapped profile`` () =
    let nb =
        notebookOf [
            sketchBlockOf 0 "leading" (verticalLineSketch 0.0) XY
            sketchBlockOf 1 "trailing" (verticalLineSketch 1.0) XY
            nativeBlock 2 "wing" "wing-remap-preview"
                [ "leading", ArgRef (Some 0)
                  "trailing", ArgRef (Some 1) ]
        ]
    let _, result = evaluateOk nb
    match bindingOf "wing" result with
    | Value.VField root ->
        let rootNode = result.Ir.Nodes.[root.Id]
        Assert.Equal(MathIr.NodeKind.BinaryK, rootNode.Kind)
        Assert.Equal(int MathIr.Binary.Max, rootNode.Op)
        let curveIntrinsics =
            result.Ir.Intrinsics
            |> Seq.filter (fun i -> i.Kind = MathIr.IntrinsicKind.CurveDistanceAlong)
            |> Seq.length
        Assert.Equal(2, curveIntrinsics)
        let wgsl = MathIrWgsl.emit result.Ir root "wing_preview"
        Assert.Contains("let_axis_line_distance", wgsl)
        Assert.Contains("fn wing_preview", wgsl)
    | other -> failwithf "expected VField, got %A" other

// ─── Sketch primitives (subtree nodes) ────────────────────────────────────

[<Fact>]
let ``from-sketch over two lines roots a Fold(Min, [LineSegment; LineSegment])`` () =
    let sk =
        { Entities =
            [ REPoint("a0", 0.0, 0.0)
              REPoint("a1", 1.0, 0.0)
              REPoint("b0", 0.0, 1.0)
              REPoint("b1", 1.0, 1.0)
              RELine("la", "a0", "a1")
              RELine("lb", "b0", "b1") ]
          Constraints = [] }
    let nb =
        notebookOf [
            sketchBlockOf 0 "sk" sk XY
            nativeBlock 1 "field" "from-sketch" [ "sketch", ArgRef (Some 0) ]
        ]
    let _, result = evaluateOk nb
    match bindingOf "field" result with
    | Value.VField root ->
        let rootNode = result.Ir.Nodes.[root.Id]
        Assert.Equal(MathIr.NodeKind.Fold, rootNode.Kind)
        Assert.Equal(int MathIr.FoldOp.Min, rootNode.Op)
        Assert.Equal(2, rootNode.B)
        // Each child should be a LineSegment node.
        for k in 0 .. rootNode.B - 1 do
            let childId = result.Ir.NodeRefs.[rootNode.A + k]
            let childNode = result.Ir.Nodes.[childId]
            Assert.Equal(MathIr.NodeKind.LineSegment, childNode.Kind)
    | other -> failwithf "expected VField, got %A" other

[<Fact>]
let ``from-sketch over a single line returns the LineSegment node directly`` () =
    let sk =
        { Entities =
            [ REPoint("p0", 0.0, 0.0)
              REPoint("p1", 1.0, 0.0)
              RELine("l", "p0", "p1") ]
          Constraints = [] }
    let nb =
        notebookOf [
            sketchBlockOf 0 "sk" sk XY
            nativeBlock 1 "field" "from-sketch" [ "sketch", ArgRef (Some 0) ]
        ]
    let _, result = evaluateOk nb
    match bindingOf "field" result with
    | Value.VField root ->
        Assert.Equal(MathIr.NodeKind.LineSegment, result.Ir.Nodes.[root.Id].Kind)
    | other -> failwithf "expected VField, got %A" other

[<Fact>]
let ``from-sketch over a triangle wraps the Fold(Min, ...) in a sign flip (Mul)`` () =
    // Three line segments forming a closed loop share endpoint ids; the
    // loop detector picks this up and from-sketch lowers as
    //   signed = unsigned * (-compare(|fold(Sum, windings)|, π))
    // The root MathIR node should be a Binary.Mul.
    let sk =
        { Entities =
            [ REPoint("a", 0.0, 0.0)
              REPoint("b", 1.0, 0.0)
              REPoint("c", 0.5, 1.0)
              RELine("ab", "a", "b")
              RELine("bc", "b", "c")
              RELine("ca", "c", "a") ]
          Constraints = [] }
    let nb =
        notebookOf [
            sketchBlockOf 0 "sk" sk XY
            nativeBlock 1 "field" "from-sketch" [ "sketch", ArgRef (Some 0) ]
        ]
    let _, result = evaluateOk nb
    match bindingOf "field" result with
    | Value.VField root ->
        let rootNode = result.Ir.Nodes.[root.Id]
        Assert.Equal(MathIr.NodeKind.BinaryK, rootNode.Kind)
        Assert.Equal(int MathIr.Binary.Mul, rootNode.Op)
        // Left operand is the unsigned fold-min of 3 line segments.
        let unsignedNode = result.Ir.Nodes.[rootNode.A]
        Assert.Equal(MathIr.NodeKind.Fold, unsignedNode.Kind)
        Assert.Equal(int MathIr.FoldOp.Min, unsignedNode.Op)
        Assert.Equal(3, unsignedNode.B)
    | other -> failwithf "expected VField, got %A" other

[<Fact>]
let ``from-sketch over a single circle lowers to analytic signed disk distance`` () =
    // A lone circle is its own closed loop; the lowering emits
    //   sqrt((x-cx)² + (y-cy)²) - r
    // The root is a Binary.Sub of (Unary.Sqrt of ...) - (Const r).
    let sk =
        { Entities =
            [ REPoint("c", 0.0, 0.0)
              RECircle("circ", "c", 1.5) ]
          Constraints = [] }
    let nb =
        notebookOf [
            sketchBlockOf 0 "sk" sk XY
            nativeBlock 1 "field" "from-sketch" [ "sketch", ArgRef (Some 0) ]
        ]
    let _, result = evaluateOk nb
    match bindingOf "field" result with
    | Value.VField root ->
        let rootNode = result.Ir.Nodes.[root.Id]
        Assert.Equal(MathIr.NodeKind.BinaryK, rootNode.Kind)
        Assert.Equal(int MathIr.Binary.Sub, rootNode.Op)
        // Left operand is sqrt(...)
        let sqrtNode = result.Ir.Nodes.[rootNode.A]
        Assert.Equal(MathIr.NodeKind.UnaryK, sqrtNode.Kind)
        Assert.Equal(int MathIr.Unary.Sqrt, sqrtNode.Op)
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
            nativeBlock 0 "shape" "sphere" [ "radius", ArgScalar 1.5 ]
        ]
    match NotebookCompose.compileView nb None with
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
        { nativeBlock 0 "shape" "sphere" [ "radius", ArgScalar 1.5 ] with
            ColorIndex = 8 }
    let composed = NotebookCompose.compose (notebookOf [ block ])
    Assert.Equal<int list>([ 8 ], composed.VisibleFieldColorIndices)

// ─── User-defined specs (script editor) ─────────────────────────────────────

[<Fact>]
let ``composeWith: user spec seeds typeEnv with curried function type`` () =
    let userScript =
        UserScript.analyze "let donut = fun (r: Scalar) -> r + 1 end"
    let nb = notebookOf []
    let composed = NotebookCompose.composeWith nb userScript
    // donut : Scalar -> Field
    match Map.tryFind "donut" composed.TypeEnv with
    | Some (Type.Fun(Type.Scalar, Type.Field)) -> ()
    | other -> failwithf "expected Scalar -> Field, got %A" other

[<Fact>]
let ``composeWith: user-defined block resolves and typechecks against user spec`` () =
    let userScript =
        UserScript.analyze "let donut = fun (r: Scalar) -> r + 1 end"
    let nb =
        notebookOf [ nativeBlock 0 "ring" "donut" [ "r", ArgScalar 0.5 ] ]
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
        notebookOf [ nativeBlock 0 "weird" "donut" [ "r", ArgScalar 0.5 ] ]
    // No user script — donut is unknown.
    let composed = NotebookCompose.composeWith nb UserScript.empty
    match Typecheck.elaborate composed.TypeEnv composed.Ast with
    | Ok _ -> failwith "expected typecheck failure for unknown spec"
    | Error _ -> ()

[<Fact>]
let ``compileWith: user-defined block compiles to MathIR end to end`` () =
    // A genuinely Field-valued user spec — `union` on a sphere doubles
    // up as a field, returning a Field.
    let userScript =
        UserScript.analyze "let bubble = fun (r: Scalar) -> sphere r end"
    let nb =
        notebookOf [ nativeBlock 0 "ring" "bubble" [ "r", ArgScalar 1.0 ] ]
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
                [ "radius", ArgScalar 0.5
                  "length", ArgScalar 2.0 ] ]
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
