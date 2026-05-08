module NotebookTests

open Xunit
open Server
open Server.Lang
open Server.Lang.Notebook

// ─── Helpers ────────────────────────────────────────────────────────────────

let private scriptBlock id name source inputs : Block =
    { Id = id
      Name = name
      Kind = ScriptBlock { Source = source; Inputs = inputs } }

let private sketchBlockOf id name (sketch: ActionSketch) (plane: SketchPlane) : Block =
    { Id = id
      Name = name
      Kind = SketchBlock { Sketch = sketch; Plane = plane } }

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

// ─── Facts ──────────────────────────────────────────────────────────────────

[<Fact>]
let ``single block with one output stitches as the block's name in scope`` () =
    let nb =
        notebookOf [
            scriptBlock 0 "p" "@output(\"r\", 1.5)" []
        ]
    let result = NotebookEval.eval nb
    Assert.Equal(1, List.length result.PerBlock)
    let p = blockEvalOf 0 result
    Assert.Equal(None, p.Error)
    // single output → block.name binds the bare value
    match Map.tryFind "p" result.Scope with
    | Some (Value.VNumber 1.5) -> ()
    | other -> failwithf "expected p = VNumber 1.5, got %A" other

[<Fact>]
let ``two-block wiring: q reads p's output via input wire`` () =
    let nb =
        notebookOf [
            scriptBlock 0 "p" "@output(\"x\", 7)" []
            scriptBlock 1 "q" "@input(\"v\")" [ "v", "p" ]
        ]
    let result = NotebookEval.eval nb
    let q = blockEvalOf 1 result
    Assert.Equal(None, q.Error)
    Assert.Contains("v", q.InputsUsed)
    // q's scope binding is the stitched value: zero outputs + one input → input value
    match Map.tryFind "q" result.Scope with
    | Some (Value.VNumber 7.0) -> ()
    | other -> failwithf "expected q = VNumber 7.0, got %A" other

[<Fact>]
let ``path resolution: downstream block reads params.chord via input expression`` () =
    let nb =
        notebookOf [
            scriptBlock 0 "params"
                "@output(\"chord\", 1.0)\n@output(\"thickness\", 0.12)"
                []
            scriptBlock 1 "shape"
                "@output(\"r\", @input(\"radius\"))"
                [ "radius", "params.chord" ]
        ]
    let result = NotebookEval.eval nb
    let shape = blockEvalOf 1 result
    Assert.Equal(None, shape.Error)
    match Map.tryFind "shape" result.Scope with
    | Some (Value.VNumber 1.0) -> ()
    | other -> failwithf "expected shape = VNumber 1.0 (chord), got %A" other

[<Fact>]
let ``implicit view: a single Field output becomes the block's view`` () =
    let nb =
        notebookOf [
            scriptBlock 0 "shape" "@output(\"f\", @sphere(1.0))" []
        ]
    let result = NotebookEval.eval nb
    let shape = blockEvalOf 0 result
    match shape.View with
    | Some (Value.VField _) -> ()
    | other -> failwithf "expected implicit Field view, got %A" other

[<Fact>]
let ``explicit view wins over outputs`` () =
    let nb =
        notebookOf [
            scriptBlock 0 "shape"
                "@output(\"unused\", 1)\n@view(@sphere(2))"
                []
        ]
    let result = NotebookEval.eval nb
    let shape = blockEvalOf 0 result
    match shape.View with
    | Some (Value.VField _) -> ()
    | other -> failwithf "expected explicit Field view, got %A" other

[<Fact>]
let ``compileView returns the shared MathIR with a Sub-rooted sphere`` () =
    // params has two outputs so it stitches to a Record (single-output blocks
    // bind the bare value, which would block `params.r` path access).
    let nb =
        notebookOf [
            scriptBlock 0 "params"
                "@output(\"r\", 1.5)\n@output(\"unused\", 0)"
                []
            scriptBlock 1 "shape"
                "@view(@sphere(@input(\"radius\")))"
                [ "radius", "params.r" ]
        ]
    match NotebookEval.compileView nb None with
    | Ok (ir, root) ->
        Assert.True(ir.Nodes.Count > 0, "expected non-empty math IR")
        let rootNode = ir.Nodes.[root.Id]
        // sphere SDF: sqrt(x²+y²+z²) - r → root must be a Sub binary
        Assert.Equal(MathIr.NodeKind.BinaryK, rootNode.Kind)
        Assert.Equal(int MathIr.Binary.Sub, rootNode.Op)
    | Error msg -> failwithf "compileView failed: %s" msg

[<Fact>]
let ``continue on error: a failing block doesn't halt the next block`` () =
    let nb =
        notebookOf [
            scriptBlock 0 "good" "@output(\"x\", 1)" []
            scriptBlock 1 "bad"  "@input(\"missing\")" []     // unwired @input
            scriptBlock 2 "tail" "@output(\"y\", 2)" []
        ]
    let result = NotebookEval.eval nb
    Assert.Equal(3, List.length result.PerBlock)
    let badEval = blockEvalOf 1 result
    Assert.True(badEval.Error.IsSome, "bad block should have an error")
    let tailEval = blockEvalOf 2 result
    Assert.Equal(None, tailEval.Error)
    Assert.True(Map.containsKey "good" result.Scope)
    Assert.True(Map.containsKey "tail" result.Scope)

// ─── Sketch blocks ──────────────────────────────────────────────────────────

[<Fact>]
let ``sketch block stitches as a VSketch under the block's name`` () =
    let nb = notebookOf [ sketchBlockOf 0 "outline" simpleLineSketch XY ]
    let result = NotebookEval.eval nb
    match Map.tryFind "outline" result.Scope with
    | Some (Value.VSketch sv) ->
        Assert.Equal(3, List.length sv.Sketch.Entities)
        Assert.Equal(XY, sv.Plane)
    | other -> failwithf "expected VSketch in scope, got %A" other

[<Fact>]
let ``sketch block becomes its own auto-view`` () =
    let nb = notebookOf [ sketchBlockOf 0 "outline" simpleLineSketch XZ ]
    let result = NotebookEval.eval nb
    let be = blockEvalOf 0 result
    match be.View with
    | Some (Value.VSketch sv) ->
        Assert.Equal(XZ, sv.Plane)
    | other -> failwithf "expected sketch as view, got %A" other

[<Fact>]
let ``downstream script block can read a sketch by name via input wire`` () =
    let nb =
        notebookOf [
            sketchBlockOf 0 "outline" simpleLineSketch XY
            scriptBlock 1 "consumer" "@input(\"sk\")" [ "sk", "outline" ]
        ]
    let result = NotebookEval.eval nb
    let consumer = blockEvalOf 1 result
    Assert.Equal(None, consumer.Error)
    Assert.Contains("sk", consumer.InputsUsed)
    // The sketch passes through identity-style; consumer's stitched value
    // is the input value (zero outputs + one input → bind input).
    match Map.tryFind "consumer" result.Scope with
    | Some (Value.VSketch sv) -> Assert.Equal(3, List.length sv.Sketch.Entities)
    | other -> failwithf "expected VSketch in consumer scope, got %A" other
