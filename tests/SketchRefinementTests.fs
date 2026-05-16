module SketchRefinementTests

// ---------------------------------------------------------------------------
// End-to-end tests for the DSL sketch refinement feature.
//
// Notebook sketch blocks now expose their persisted loop registry as
// structural members on the sketch type: `profile.loop_0` typechecks as
// Field against the sketch's actual Loops, and the value-side bridge
// populates each loop's signed-distance Expr behind that member.
// ---------------------------------------------------------------------------

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
    // Run loop reconciliation so the sketch carries a populated Loops
    // registry — matches the document state every real edit would leave.
    let normalized = SketchLoops.normalize sketch
    { Id = id
      Name = name
      Body = SketchBody { Sketch = normalized; Plane = plane }
      Visibility = VIsosurface
      ColorIndex = 0
      SlicePlane = defaultSlicePlane }

let private notebookOf (blocks: Block list) : Notebook =
    { NextId = List.length blocks; Blocks = blocks }

let private squareSketch : ActionSketch =
    { Entities =
        [ REPoint("p_bl", 0.0, 0.0)
          REPoint("p_br", 1.0, 0.0)
          REPoint("p_tr", 1.0, 1.0)
          REPoint("p_tl", 0.0, 1.0)
          RELine("l_b", "p_bl", "p_br")
          RELine("l_r", "p_br", "p_tr")
          RELine("l_t", "p_tr", "p_tl")
          RELine("l_l", "p_tl", "p_bl") ]
      Constraints = []; Loops = [] }

let private squarePlusCircleSketch : ActionSketch =
    { Entities =
        [ REPoint("p_bl", 0.0, 0.0)
          REPoint("p_br", 1.0, 0.0)
          REPoint("p_tr", 1.0, 1.0)
          REPoint("p_tl", 0.0, 1.0)
          RELine("l_b", "p_bl", "p_br")
          RELine("l_r", "p_br", "p_tr")
          RELine("l_t", "p_tr", "p_tl")
          RELine("l_l", "p_tl", "p_bl")
          REPoint("c0", 5.0, 5.0)
          RECircle("c1", "c0", 0.5) ]
      Constraints = []; Loops = [] }

let private emptySketch : ActionSketch =
    { Entities = []; Constraints = []; Loops = [] }

// Parse a DSL source string into a notebook code block body, attach it
// to the given notebook, and run the full compose+typecheck+eval path.
// Returns the composed result and the binding produced by the let-stmt
// for `target`.

// ─── Type env refinements ──────────────────────────────────────────────────

[<Fact>]
let ``compose: sketch with one loop carries `loop_0` Loop member`` () =
    let nb = notebookOf [ sketchBlockOf 0 "profile" squareSketch XY ]
    let composed = NotebookCompose.compose nb
    match Map.tryFind "profile" composed.TypeEnv with
    | Some (Type.Sketch fields) ->
        Assert.True(Map.containsKey "loop_0" fields,
                    sprintf "expected `loop_0` member, available: %A" (Map.toList fields |> List.map fst))
        // The loop is its own kind with a `signed_distance: Field`
        // refinement — not a bare Field. The runtime/typechecker
        // auto-project Loop→Field at consume sites.
        match Map.find "loop_0" fields with
        | Type.Loop loopFields ->
            Assert.Equal(Type.Field, Map.find "signed_distance" loopFields)
        | other -> failwithf "expected Type.Loop, got %s" (Type.format other)
    | other -> failwithf "expected Type.Sketch, got %A" other

[<Fact>]
let ``compose: sketch with two loops carries both members`` () =
    let nb = notebookOf [ sketchBlockOf 0 "profile" squarePlusCircleSketch XY ]
    let composed = NotebookCompose.compose nb
    match Map.tryFind "profile" composed.TypeEnv with
    | Some (Type.Sketch fields) ->
        let keys = fields |> Map.toList |> List.map fst |> Set.ofList
        Assert.True(Set.contains "loop_0" keys)
        Assert.True(Set.contains "loop_1" keys)
    | other -> failwithf "expected Type.Sketch, got %A" other

[<Fact>]
let ``compose: empty sketch has Type.Sketch with empty refinement`` () =
    let nb = notebookOf [ sketchBlockOf 0 "profile" emptySketch XY ]
    let composed = NotebookCompose.compose nb
    match Map.tryFind "profile" composed.TypeEnv with
    | Some (Type.Sketch fields) ->
        Assert.True(Map.isEmpty fields)
    | other -> failwithf "expected Type.Sketch, got %A" other

// ─── Typecheck: EPath on Sketch ─────────────────────────────────────────────

let private squareEnv : Typecheck.TypeEnv =
    let nb = notebookOf [ sketchBlockOf 0 "profile" squareSketch XY ]
    NotebookCompose.compose nb |> fun c -> c.TypeEnv

let private path (segs: string list) : Ast.Expr =
    let span : Token.Span = { Start = 0; Stop = 0 }
    let mkIdent name : Ast.Ident = { Name = name; Span = span; IdentKind = Ast.User }
    { Node = Ast.EPath (List.map mkIdent segs); Span = span }

[<Fact>]
let ``typecheck: profile.loop_0 resolves to Loop`` () =
    match Typecheck.elaborate squareEnv (path [ "profile"; "loop_0" ]) with
    | Ok te ->
        match te.Type with
        | Type.Loop _ -> ()
        | other -> failwithf "expected Type.Loop, got %s" (Type.format other)
    | Error es ->
        let msg = es |> List.map Typecheck.formatError |> String.concat "; "
        failwithf "expected ok, got: %s" msg

[<Fact>]
let ``typecheck: profile.loop_0.signed_distance resolves to Field`` () =
    match Typecheck.elaborate squareEnv (path [ "profile"; "loop_0"; "signed_distance" ]) with
    | Ok te -> Assert.Equal(Type.Field, te.Type)
    | Error es ->
        let msg = es |> List.map Typecheck.formatError |> String.concat "; "
        failwithf "expected ok, got: %s" msg

[<Fact>]
let ``subtype: Loop {signed_distance: Field} is a subtype of Field`` () =
    let loopTy = Type.Loop (Map.ofList [ "signed_distance", Type.Field ])
    Assert.True(Type.isSubtypeOf loopTy Type.Field)

[<Fact>]
let ``subtype: bare Loop (no signed_distance) is NOT a subtype of Field`` () =
    let bareLoop = Type.Loop Map.empty
    Assert.False(Type.isSubtypeOf bareLoop Type.Field)

[<Fact>]
let ``typecheck: unknown sketch member emits UnknownSketchMember`` () =
    match Typecheck.elaborate squareEnv (path [ "profile"; "does_not_exist" ]) with
    | Ok te -> failwithf "expected error, got type %s" (Type.format te.Type)
    | Error es ->
        let isUnknown =
            es |> List.exists (function
                | Typecheck.UnknownSketchMember("profile", "does_not_exist", _, _) -> true
                | _ -> false)
        Assert.True(isUnknown, sprintf "expected UnknownSketchMember, got %A" es)

[<Fact>]
let ``typecheck: error message lists available members`` () =
    match Typecheck.elaborate squareEnv (path [ "profile"; "bogus" ]) with
    | Error [ Typecheck.UnknownSketchMember(_, _, available, _) ] ->
        Assert.Contains("loop_0", available)
    | other -> failwithf "unexpected: %A" other

[<Fact>]
let ``typecheck: dotted access on non-sketch errors`` () =
    // Build env where `x` is Scalar.
    let env = Map.ofList [ "x", Type.Scalar ]
    match Typecheck.elaborate env (path [ "x"; "anything" ]) with
    | Ok _ -> failwith "expected error"
    | Error es ->
        let isNotASketch =
            es |> List.exists (function
                | Typecheck.NotASketch(Type.Scalar, _) -> true
                | _ -> false)
        Assert.True(isNotASketch, sprintf "expected NotASketch, got %A" es)

[<Fact>]
let ``typecheck: head of path must be defined`` () =
    match Typecheck.elaborate Map.empty (path [ "missing"; "loop_0" ]) with
    | Ok _ -> failwith "expected error"
    | Error es ->
        let isUndefined =
            es |> List.exists (function
                | Typecheck.UndefinedVar("missing", _) -> true
                | _ -> false)
        Assert.True(isUndefined, sprintf "expected UndefinedVar, got %A" es)

// ─── Eval: VSketch dispatch ─────────────────────────────────────────────────

let private composeAndEval (nb: Notebook) : NotebookCompose.EvalResult =
    let composed = NotebookCompose.compose nb
    match Typecheck.elaborate composed.TypeEnv composed.Ast with
    | Ok _ -> ()
    | Error errs ->
        let msg = errs |> List.map Typecheck.formatError |> String.concat "; "
        failwithf "typecheck failed: %s" msg
    match NotebookCompose.evaluate nb composed with
    | Ok r -> r
    | Error e -> failwithf "eval failed: %s" e.Message

[<Fact>]
let ``eval: VSketch.Fields contains a VLoop per persisted loop`` () =
    let nb = notebookOf [ sketchBlockOf 0 "profile" squareSketch XY ]
    let result = composeAndEval nb
    match Map.find "profile" result.Bindings with
    | Value.VSketch sv ->
        Assert.True(Map.containsKey "loop_0" sv.Fields)
        match Map.find "loop_0" sv.Fields with
        | Value.VLoop lv ->
            // The loop carries its derived signed_distance field.
            match Map.tryFind "signed_distance" lv.Fields with
            | Some (Value.VField _) -> ()
            | other -> failwithf "expected VField under .signed_distance, got %A" other
        | other -> failwithf "expected VLoop, got %A" other
    | other -> failwithf "expected VSketch, got %A" other

[<Fact>]
let ``eval: two-loop sketch produces two field entries`` () =
    let nb = notebookOf [ sketchBlockOf 0 "profile" squarePlusCircleSketch XY ]
    let result = composeAndEval nb
    match Map.find "profile" result.Bindings with
    | Value.VSketch sv ->
        Assert.Equal(2, Map.count sv.Fields)
    | other -> failwithf "expected VSketch, got %A" other

// ─── Subtype: generic Sketch accepts refined sketches ──────────────────────

[<Fact>]
let ``subtype: refined Sketch is a subtype of generic Sketch`` () =
    let refined = Type.Sketch (Map.ofList [ "loop_0", Type.Field ])
    let generic = Type.Sketch Map.empty
    Assert.True(Type.isSubtypeOf refined generic)

[<Fact>]
let ``subtype: generic Sketch is NOT a subtype of a refined Sketch`` () =
    let refined = Type.Sketch (Map.ofList [ "loop_0", Type.Field ])
    let generic = Type.Sketch Map.empty
    Assert.False(Type.isSubtypeOf generic refined)

[<Fact>]
let ``subtype: refined Sketch satisfies a refined Sketch requiring a subset`` () =
    let big = Type.Sketch (Map.ofList [ "loop_0", Type.Field; "loop_1", Type.Field ])
    let small = Type.Sketch (Map.ofList [ "loop_0", Type.Field ])
    Assert.True(Type.isSubtypeOf big small)
    Assert.False(Type.isSubtypeOf small big)

// ─── Phase 2: refinement inference on lambda parameters ────────────────────

let private parseExpr (src: string) : Ast.Expr =
    match Parser.parseProgram src with
    | Ok [ Ast.SExpr e ] -> e
    | Ok stmts -> failwithf "expected single expression, got %d statements" (List.length stmts)
    | Error e -> failwithf "parse error: %A" e

[<Fact>]
let ``infer: lambda with Sketch param and one member access infers a refinement`` () =
    // `fun (s: Sketch) -> s.loop_0 end` should infer the parameter type
    // as Sketch { loop_0: Loop {signed_distance: Field} }, so the
    // function has return type Loop {signed_distance: Field}.
    let src = "fun (s: Sketch) -> s.loop_0 end"
    let e = parseExpr src
    match Typecheck.elaborate Map.empty e with
    | Ok te ->
        match te.Type with
        | Type.Fun(Type.Sketch fields, Type.Loop _) ->
            match Map.tryFind "loop_0" fields with
            | Some (Type.Loop loopFields) ->
                Assert.Equal(Type.Field, Map.find "signed_distance" loopFields)
            | other -> failwithf "expected Loop member, got %A" other
        | other -> failwithf "expected Fun(Sketch{loop_0: Loop}, Loop), got %s" (Type.format other)
    | Error es ->
        let msg = es |> List.map Typecheck.formatError |> String.concat "; "
        failwithf "expected ok, got: %s" msg

[<Fact>]
let ``infer: lambda accessing two members infers both`` () =
    // The body adds two loops: `s.loop_0 + s.loop_1`. Both must end
    // up in the refinement; binary `+` auto-projects each Loop to
    // its signed_distance Field at runtime.
    let src = "fun (s: Sketch) -> s.loop_0 + s.loop_1 end"
    let e = parseExpr src
    match Typecheck.elaborate Map.empty e with
    | Ok te ->
        match te.Type with
        | Type.Fun(Type.Sketch fields, _) ->
            Assert.True(Map.containsKey "loop_0" fields)
            Assert.True(Map.containsKey "loop_1" fields)
        | other -> failwithf "unexpected type: %s" (Type.format other)
    | Error es ->
        let msg = es |> List.map Typecheck.formatError |> String.concat "; "
        failwithf "expected ok, got: %s" msg

// ─── Primitive layer ────────────────────────────────────────────────────────

[<Fact>]
let ``primitives: square sketch has line_0..line_3 inside its loop`` () =
    let nb = notebookOf [ sketchBlockOf 0 "profile" squareSketch XY ]
    let normalized = SketchLoops.normalize squareSketch
    let loop = normalized.Loops.[0]
    Assert.Equal(4, List.length loop.Primitives)
    let ids = loop.Primitives |> List.map (fun p -> p.Id) |> Set.ofList
    Assert.Equal<Set<string>>(Set.ofList [ "line_0"; "line_1"; "line_2"; "line_3" ], ids)

[<Fact>]
let ``primitives: circle inside a sketch reconciles to circle_0`` () =
    let circleSketch : ActionSketch =
        { Entities =
            [ REPoint("c0", 0.0, 0.0)
              RECircle("c1", "c0", 1.0) ]
          Constraints = []; Loops = [] }
    let normalized = SketchLoops.normalize circleSketch
    let loop = normalized.Loops.[0]
    Assert.Equal(1, List.length loop.Primitives)
    Assert.Equal("circle_0", loop.Primitives.[0].Id)
    Assert.Equal("c1", loop.Primitives.[0].EntityId)

[<Fact>]
let ``primitives: IDs survive across edits when entity IDs unchanged`` () =
    // Reconcile twice; the second pass should preserve `line_0..line_3`.
    let pass1 = SketchLoops.normalize squareSketch
    let pass2 = SketchLoops.normalize pass1
    let ids1 = pass1.Loops.[0].Primitives |> List.map (fun p -> p.Id, p.EntityId)
    let ids2 = pass2.Loops.[0].Primitives |> List.map (fun p -> p.Id, p.EntityId)
    Assert.Equal<(string * string) list>(ids1, ids2)

[<Fact>]
let ``compose: loop carries Type.Primitive members per persisted primitive`` () =
    let nb = notebookOf [ sketchBlockOf 0 "profile" squareSketch XY ]
    let composed = NotebookCompose.compose nb
    match Map.tryFind "profile" composed.TypeEnv with
    | Some (Type.Sketch sketchFields) ->
        match Map.find "loop_0" sketchFields with
        | Type.Loop loopFields ->
            // signed_distance + four lines
            Assert.True(Map.containsKey "signed_distance" loopFields)
            for i in 0 .. 3 do
                let key = sprintf "line_%d" i
                Assert.True(Map.containsKey key loopFields, sprintf "missing %s" key)
                match Map.find key loopFields with
                | Type.Primitive primFields ->
                    Assert.Equal(Type.Field, Map.find "signed_distance" primFields)
                | other -> failwithf "expected Type.Primitive for %s, got %s" key (Type.format other)
        | other -> failwithf "expected Type.Loop, got %s" (Type.format other)
    | other -> failwithf "expected Type.Sketch, got %A" other

[<Fact>]
let ``typecheck: profile.loop_0.line_0 resolves to Primitive`` () =
    match Typecheck.elaborate squareEnv (path [ "profile"; "loop_0"; "line_0" ]) with
    | Ok te ->
        match te.Type with
        | Type.Primitive _ -> ()
        | other -> failwithf "expected Type.Primitive, got %s" (Type.format other)
    | Error es ->
        let msg = es |> List.map Typecheck.formatError |> String.concat "; "
        failwithf "expected ok, got: %s" msg

[<Fact>]
let ``typecheck: profile.loop_0.line_0.signed_distance resolves to Field`` () =
    let p = path [ "profile"; "loop_0"; "line_0"; "signed_distance" ]
    match Typecheck.elaborate squareEnv p with
    | Ok te -> Assert.Equal(Type.Field, te.Type)
    | Error es ->
        let msg = es |> List.map Typecheck.formatError |> String.concat "; "
        failwithf "expected ok, got: %s" msg

[<Fact>]
let ``subtype: Primitive {signed_distance: Field} <: Field`` () =
    let primTy = Type.Primitive (Map.ofList [ "signed_distance", Type.Field ])
    Assert.True(Type.isSubtypeOf primTy Type.Field)

[<Fact>]
let ``eval: loop_0.line_0 evaluates to a VPrimitive with signed_distance VField`` () =
    let nb = notebookOf [ sketchBlockOf 0 "profile" squareSketch XY ]
    let result = composeAndEval nb
    match Map.find "profile" result.Bindings with
    | Value.VSketch sv ->
        match Map.find "loop_0" sv.Fields with
        | Value.VLoop lv ->
            match Map.tryFind "line_0" lv.Fields with
            | Some (Value.VPrimitive pv) ->
                match Map.tryFind "signed_distance" pv.Fields with
                | Some (Value.VField _) -> ()
                | other -> failwithf "expected VField under .signed_distance, got %A" other
            | other -> failwithf "expected VPrimitive at line_0, got %A" other
        | other -> failwithf "expected VLoop, got %A" other
    | other -> failwithf "expected VSketch, got %A" other

[<Fact>]
let ``typecheck: primitive auto-projects to Field at consume site`` () =
    let nb = notebookOf [ sketchBlockOf 0 "profile" squareSketch XY ]
    let composed = NotebookCompose.compose nb
    let src = "(fun (f: Field) -> f + 0.0 end) profile.loop_0.line_0"
    let e = parseExpr src
    match Typecheck.elaborate composed.TypeEnv e with
    | Ok te -> Assert.Equal(Type.Field, te.Type)
    | Error es ->
        let msg = es |> List.map Typecheck.formatError |> String.concat "; "
        failwithf "expected ok, got: %s" msg

[<Fact>]
let ``apply: loop satisfies a Field-typed function parameter via projection`` () =
    // Test the implicit Loop→Field coercion at application:
    // `(fun (f: Field) -> f + 0.0 end) profile.loop_0` should typecheck,
    // and runtime should auto-project the loop to its signed_distance.
    let nb = notebookOf [ sketchBlockOf 0 "profile" squareSketch XY ]
    let composed = NotebookCompose.compose nb
    let src = "(fun (f: Field) -> f + 0.0 end) profile.loop_0"
    let e = parseExpr src
    match Typecheck.elaborate composed.TypeEnv e with
    | Ok te -> Assert.Equal(Type.Field, te.Type)
    | Error es ->
        let msg = es |> List.map Typecheck.formatError |> String.concat "; "
        failwithf "expected ok, got: %s" msg

[<Fact>]
let ``infer: lambda with no member access keeps empty refinement`` () =
    // A function that doesn't dot-access its sketch parameter should
    // have Sketch Map.empty (generic) as its inferred parameter type.
    let src = "fun (s: Sketch) -> 1.0 end"
    let e = parseExpr src
    match Typecheck.elaborate Map.empty e with
    | Ok te ->
        match te.Type with
        | Type.Fun(Type.Sketch fields, _) ->
            Assert.True(Map.isEmpty fields, sprintf "expected empty refinement, got %A" fields)
        | other -> failwithf "unexpected type: %s" (Type.format other)
    | Error es ->
        let msg = es |> List.map Typecheck.formatError |> String.concat "; "
        failwithf "expected ok, got: %s" msg

[<Fact>]
let ``apply: refined sketch satisfies a function requiring fewer members`` () =
    // `(fun (s: Sketch) -> s.loop_0 end) profile` where profile has loop_0.
    // The function's body returns a Loop (with `signed_distance: Field`).
    let nb = notebookOf [ sketchBlockOf 0 "profile" squareSketch XY ]
    let composed = NotebookCompose.compose nb
    let src = "(fun (s: Sketch) -> s.loop_0 end) profile"
    let e = parseExpr src
    match Typecheck.elaborate composed.TypeEnv e with
    | Ok te ->
        match te.Type with
        | Type.Loop _ -> ()
        | other -> failwithf "expected Type.Loop, got %s" (Type.format other)
    | Error es ->
        let msg = es |> List.map Typecheck.formatError |> String.concat "; "
        failwithf "expected ok, got: %s" msg

[<Fact>]
let ``apply: sketch missing required member yields MissingSketchMembers`` () =
    // Build a function requiring `loop_42` (a name no real sketch has)
    // and apply it to a sketch with only `loop_0`.
    let nb = notebookOf [ sketchBlockOf 0 "profile" squareSketch XY ]
    let composed = NotebookCompose.compose nb
    let src = "(fun (s: Sketch) -> s.loop_42 end) profile"
    let e = parseExpr src
    match Typecheck.elaborate composed.TypeEnv e with
    | Ok _ -> failwith "expected error"
    | Error es ->
        let isMissing =
            es |> List.exists (function
                | Typecheck.MissingSketchMembers(missing, _, _) ->
                    List.contains "loop_42" missing
                | _ -> false)
        Assert.True(isMissing, sprintf "expected MissingSketchMembers, got %A" es)

[<Fact>]
let ``apply: function with empty refinement accepts any sketch`` () =
    // Functions that don't access members work on every sketch.
    let nb = notebookOf [ sketchBlockOf 0 "profile" squareSketch XY ]
    let composed = NotebookCompose.compose nb
    let src = "(fun (s: Sketch) -> 1.0 end) profile"
    let e = parseExpr src
    match Typecheck.elaborate composed.TypeEnv e with
    | Ok _ -> ()
    | Error es ->
        let msg = es |> List.map Typecheck.formatError |> String.concat "; "
        failwithf "expected ok, got: %s" msg

[<Fact>]
let ``apply: from-sketch (generic Sketch) accepts a refined sketch`` () =
    // Regression: existing specs typed as `Sketch Map.empty -> Field`
    // must still accept a refined sketch — covered by width subtyping.
    let nb =
        notebookOf [
            sketchBlockOf 0 "profile" squareSketch XY
            nativeBlock 1 "field" "from-sketch" [ "sketch", AstBuilder.varE "profile" ]
        ]
    let composed = NotebookCompose.compose nb
    match Typecheck.elaborate composed.TypeEnv composed.Ast with
    | Ok _ -> ()
    | Error es ->
        let msg = es |> List.map Typecheck.formatError |> String.concat "; "
        failwithf "expected ok, got: %s" msg

[<Fact>]
let ``block input: wire profile.loop_0 to a Field-typed param via EPath`` () =
    // The payoff of the Expr-as-BlockArg refactor: a downstream block
    // can wire its Field input straight to a sketch loop using
    // `EPath ["profile"; "loop_0"]`. Auto-projection handles the
    // Loop→Field projection at compose time.
    //
    // Here we wire `profile.loop_0` (a Loop, auto-projects to Field)
    // into mirror-symmetric's `child` slot. The result is a Field
    // whose root is a `RemapAxes` over the loop's signed_distance.
    let nb =
        notebookOf [
            sketchBlockOf 0 "profile" squareSketch XY
            nativeBlock 1 "mirrored" "mirror-symmetric"
                [ "axis", AstBuilder.numE 0.0
                  "root", AstBuilder.numE 0.0
                  "child", AstBuilder.pathE [ "profile"; "loop_0" ] ]
        ]
    let result = composeAndEval nb
    match Map.tryFind "mirrored" result.Bindings with
    | Some (Value.VField root) ->
        let rootNode = result.Ir.Nodes.[root.Id]
        Assert.Equal(MathIr.NodeKind.RemapAxes, rootNode.Kind)
    | other -> failwithf "expected VField, got %A" other
