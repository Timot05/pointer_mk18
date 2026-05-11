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
            nativeBlock 2 "u" "union" [ "a", ArgRef (Some 0); "b", ArgRef (Some 1) ]
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
