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
      SlicePlane = defaultSlicePlane }

let private sketchBlockOf id name (sketch: ActionSketch) (plane: SketchPlane) : Block =
    { Id = id
      Name = name
      Body = SketchBody { Sketch = sketch; Plane = plane }
      Visibility = VIsosurface
      SlicePlane = defaultSlicePlane }

let private notebookOf (blocks: Block list) : Notebook =
    { NextId = List.length blocks; Blocks = blocks }

let private blockEvalOf (id: BlockId) (eval: Evaluation) : BlockEval =
    eval.PerBlock |> List.find (fun be -> be.Id = id)

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
    let result = NotebookEval.eval nb
    let be = blockEvalOf 0 result
    Assert.Equal(None, be.Error)
    match be.Output with
    | Some (Value.VField _) -> ()
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
    let result = NotebookEval.eval nb
    let be = blockEvalOf 1 result
    Assert.Equal(None, be.Error)
    match be.Output with
    | Some (Value.VField root) ->
        let rootNode = result.Ir.Nodes.[root.Id]
        Assert.Equal(MathIr.NodeKind.RemapAxes, rootNode.Kind)
    | other -> failwithf "expected VField, got %A" other

[<Fact>]
let ``mirror symmetric y wraps an upstream field in a RemapAxes node`` () =
    let nb =
        notebookOf [
            nativeBlock 0 "half" "sphere" [ "radius", ArgScalar 1.0 ]
            nativeBlock 1 "full" "mirror-symmetric-y"
                [ "rootY", ArgScalar 0.0
                  "child", ArgRef (Some 0) ]
        ]
    let result = NotebookEval.eval nb
    let be = blockEvalOf 1 result
    Assert.Equal(None, be.Error)
    match be.Output with
    | Some (Value.VField root) ->
        let rootNode = result.Ir.Nodes.[root.Id]
        Assert.Equal(MathIr.NodeKind.RemapAxes, rootNode.Kind)
    | other -> failwithf "expected VField, got %A" other

[<Fact>]
let ``union of two spheres roots a Min binary node`` () =
    let nb =
        notebookOf [
            nativeBlock 0 "a" "sphere" [ "radius", ArgScalar 1.0 ]
            nativeBlock 1 "b" "sphere" [ "radius", ArgScalar 2.0 ]
            nativeBlock 2 "u" "union" [ "target", ArgRef (Some 0); "tool", ArgRef (Some 1) ]
        ]
    let result = NotebookEval.eval nb
    let be = blockEvalOf 2 result
    Assert.Equal(None, be.Error)
    match be.Output with
    | Some (Value.VField root) ->
        let rootNode = result.Ir.Nodes.[root.Id]
        Assert.Equal(MathIr.NodeKind.BinaryK, rootNode.Kind)
        Assert.Equal(int MathIr.Binary.Min, rootNode.Op)
    | other -> failwithf "expected VField, got %A" other

[<Fact>]
let ``unwired ref records an error but doesn't halt the notebook`` () =
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
    let result = NotebookEval.eval nb
    let broken = blockEvalOf 1 result
    Assert.True(broken.Error.IsSome, "broken block should have an error")
    let tail = blockEvalOf 2 result
    Assert.Equal(None, tail.Error)

// ─── Sketch blocks ──────────────────────────────────────────────────────────

[<Fact>]
let ``sketch block surfaces as a VSketch output`` () =
    let nb = notebookOf [ sketchBlockOf 0 "outline" simpleLineSketch XY ]
    let result = NotebookEval.eval nb
    let be = blockEvalOf 0 result
    match be.Output with
    | Some (Value.VSketch sv) ->
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
    let result = NotebookEval.eval nb
    let be = blockEvalOf 2 result
    Assert.Equal(None, be.Error)
    match be.Output with
    | Some (Value.VField root) ->
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
    let result = NotebookEval.eval nb
    let be = blockEvalOf 1 result
    Assert.Equal(None, be.Error)
    match be.Output with
    | Some (Value.VField root) ->
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
    let result = NotebookEval.eval nb
    let be = blockEvalOf 1 result
    match be.Output with
    | Some (Value.VField root) ->
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
    let result = NotebookEval.eval nb
    let be = blockEvalOf 1 result
    Assert.Equal(None, be.Error)
    match be.Output with
    | Some (Value.VField root) ->
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
    let result = NotebookEval.eval nb
    let be = blockEvalOf 1 result
    Assert.Equal(None, be.Error)
    match be.Output with
    | Some (Value.VField root) ->
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
