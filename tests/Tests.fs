module TypeCheckTests

open Xunit
open Server
open Server.Editor

// ── Helpers ──────────────────────────────────────────────────────────────

let action id kind : DocAction =
    { Id = id; Name = None; Kind = kind }

let quarterTurn = System.Math.PI * 0.5
let eighthTurn = System.Math.PI * 0.25

let ok (result: TypeCheck.TypecheckResult) =
    if result.Errors.Length > 0 then failwithf "Expected no errors but got: %A" result.Errors
    result.Typed

let errors (result: TypeCheck.TypecheckResult) =
    if result.Errors.Length = 0 then failwithf "Expected errors but got none"
    result.Errors

let outputOf id (typed: TypedAction list) =
    typed |> List.find (fun t -> t.Id = id) |> fun t -> t.Output

let inputOf id key (typed: TypedAction list) =
    let t = typed |> List.find (fun t -> t.Id = id)
    Map.find key t.Inputs

// ── Producers ────────────────────────────────────────────────────────────

[<Fact>]
let ``Origin produces Frame`` () =
    let typed =
        [ action "o" Origin ]
        |> TypeCheck.typecheck |> ok
    Assert.Equal(FieldType.Frame, outputOf "o" typed)

[<Fact>]
let ``Sphere produces Field`` () =
    let typed =
        [ action "s" (Sphere 5.0) ]
        |> TypeCheck.typecheck |> ok
    Assert.Equal(FieldType.Field, outputOf "s" typed)

[<Fact>]
let ``Cylinder produces Field`` () =
    let typed =
        [ action "c" (Cylinder(5.0, 10.0)) ]
        |> TypeCheck.typecheck |> ok
    Assert.Equal(FieldType.Field, outputOf "c" typed)

[<Fact>]
let ``Box produces Field`` () =
    let typed =
        [ action "b" (Box(1.0, 2.0, 3.0)) ]
        |> TypeCheck.typecheck |> ok
    Assert.Equal(FieldType.Field, outputOf "b" typed)

[<Fact>]
let ``HalfPlane produces Field`` () =
    let typed =
        [ action "h" (HalfPlane("Z", 0.0, false)) ]
        |> TypeCheck.typecheck |> ok
    Assert.Equal(FieldType.Field, outputOf "h" typed)

[<Fact>]
let ``Sketch produces Sketch`` () =
    let typed =
        [ action "s" (Sketch(None, XY, ActionSketch.empty)) ]
        |> TypeCheck.typecheck |> ok
    Assert.Equal(FieldType.Sketch, outputOf "s" typed)

// ── Boolean ops ──────────────────────────────────────────────────────────

[<Fact>]
let ``Union of two Fields produces Field`` () =
    let typed =
        [ action "a" (Sphere 5.0)
          action "b" (Sphere 3.0)
          action "u" (Union(Some "a", Some "b", 0.0)) ]
        |> TypeCheck.typecheck |> ok
    Assert.Equal(FieldType.Field, outputOf "u" typed)
    Assert.Equal(("a", FieldType.Field), inputOf "u" "a" typed)
    Assert.Equal(("b", FieldType.Field), inputOf "u" "b" typed)

[<Fact>]
let ``Subtract of two Fields produces Field`` () =
    let typed =
        [ action "a" (Cylinder(5.0, 10.0))
          action "b" (Sphere 3.0)
          action "s" (Subtract(Some "a", Some "b", 0.0)) ]
        |> TypeCheck.typecheck |> ok
    Assert.Equal(FieldType.Field, outputOf "s" typed)

[<Fact>]
let ``Intersect of two Fields produces Field`` () =
    let typed =
        [ action "a" (Sphere 5.0)
          action "b" (Box(1.0, 2.0, 3.0))
          action "i" (Intersect(Some "a", Some "b", 0.0)) ]
        |> TypeCheck.typecheck |> ok
    Assert.Equal(FieldType.Field, outputOf "i" typed)

// ── Polymorphic transforms ───────────────────────────────────────────────

[<Fact>]
let ``Translate Field produces Field`` () =
    let typed =
        [ action "s" (Sphere 5.0)
          action "t" (Translate(Some "s", 1.0, 0.0, 0.0)) ]
        |> TypeCheck.typecheck |> ok
    Assert.Equal(FieldType.Field, outputOf "t" typed)
    Assert.Equal(("s", FieldType.Field), inputOf "t" "child" typed)

[<Fact>]
let ``Translate Frame produces Frame`` () =
    let typed =
        [ action "o" Origin
          action "t" (Translate(Some "o", 1.0, 0.0, 0.0)) ]
        |> TypeCheck.typecheck |> ok
    Assert.Equal(FieldType.Frame, outputOf "t" typed)

[<Fact>]
let ``Rotate Field produces Field`` () =
    let typed =
        [ action "s" (Sphere 5.0)
          action "r" (Rotate(Some "s", 0.0, 0.0, 1.0, eighthTurn)) ]
        |> TypeCheck.typecheck |> ok
    Assert.Equal(FieldType.Field, outputOf "r" typed)

[<Fact>]
let ``Rotate Frame produces Frame`` () =
    let typed =
        [ action "o" Origin
          action "r" (Rotate(Some "o", 0.0, 0.0, 1.0, quarterTurn)) ]
        |> TypeCheck.typecheck |> ok
    Assert.Equal(FieldType.Frame, outputOf "r" typed)

[<Fact>]
let ``Move with Field child produces Field`` () =
    let typed =
        [ action "o" Origin
          action "s" (Sphere 5.0)
          action "m" (Move(Some "s", Some "o")) ]
        |> TypeCheck.typecheck |> ok
    Assert.Equal(FieldType.Field, outputOf "m" typed)
    Assert.Equal(("s", FieldType.Field), inputOf "m" "child" typed)
    Assert.Equal(("o", FieldType.Frame), inputOf "m" "frame" typed)

[<Fact>]
let ``Clearing a document leaves only the origin action`` () =
    let cleared = Document.emptyDocument ()

    Assert.Equal("untitled", cleared.Name)
    Assert.Equal(Some "origin", cleared.SelectedId)
    Assert.Single(cleared.Actions) |> ignore
    match cleared.Actions with
    | [ origin ] ->
        Assert.Equal("origin", origin.Id)
        match origin.Kind with
        | Origin -> ()
        | other -> failwithf "Expected Origin action, got %A" other
    | other ->
        failwithf "Expected exactly one action after clear, got %A" other

[<Fact>]
let ``AddDefaultAction "Sketch" attaches the new sketch to the origin frame`` () =
    let state =
        Editor.initState ()
        |> Editor.update (AddDefaultAction(SketchTemplate, "sketch_new"))
        |> fst

    match state.Doc.Actions |> List.find (fun action -> action.Id = "sketch_new") with
    | { Kind = Sketch(Some "origin", XY, _) } -> ()
    | other -> failwithf "Expected default sketch to be tied to origin, got %A" other

// ── Type converters ──────────────────────────────────────────────────────

[<Fact>]
let ``FromSketch converts Sketch to Field`` () =
    let typed =
        [ action "sk" (Sketch(None, XY, ActionSketch.empty))
          action "fs" (FromSketch(Some "sk", false, FromSketchSelection.defaults)) ]
        |> TypeCheck.typecheck |> ok
    Assert.Equal(FieldType.Field, outputOf "fs" typed)
    Assert.Equal(("sk", FieldType.Sketch), inputOf "fs" "child" typed)

[<Fact>]
let ``Mesh converts Field to Mesh`` () =
    let typed =
        [ action "s" (Sphere 5.0)
          action "m" (Mesh(Some "s", 0.2, 96)) ]
        |> TypeCheck.typecheck |> ok
    Assert.Equal(FieldType.Mesh, outputOf "m" typed)
    Assert.Equal(("s", FieldType.Field), inputOf "m" "child" typed)

// ── Modifiers ────────────────────────────────────────────────────────────

[<Fact>]
let ``Thicken Field produces Field`` () =
    let typed =
        [ action "s" (Sphere 5.0)
          action "t" (Thicken(Some "s", 2.0)) ]
        |> TypeCheck.typecheck |> ok
    Assert.Equal(FieldType.Field, outputOf "t" typed)

[<Fact>]
let ``Shell Field produces Field`` () =
    let typed =
        [ action "s" (Sphere 5.0)
          action "sh" (Shell(Some "s", 1.0)) ]
        |> TypeCheck.typecheck |> ok
    Assert.Equal(FieldType.Field, outputOf "sh" typed)

// ── Error: missing refs ──────────────────────────────────────────────────

[<Fact>]
let ``Missing ref on Union produces MissingRef errors`` () =
    let errs =
        [ action "u" (Union(None, None, 0.0)) ]
        |> TypeCheck.typecheck |> errors
    Assert.Equal(2, errs.Length)
    Assert.True(errs |> List.forall (fun e -> match e with MissingRef _ -> true | _ -> false))

[<Fact>]
let ``Missing child on Translate produces MissingRef`` () =
    let errs =
        [ action "t" (Translate(None, 1.0, 0.0, 0.0)) ]
        |> TypeCheck.typecheck |> errors
    match errs with
    | [ MissingRef(id, key) ] ->
        Assert.Equal("t", id)
        Assert.Equal("child", key)
    | _ -> failwithf "Unexpected errors: %A" errs

// ── Error: ref not found ─────────────────────────────────────────────────

[<Fact>]
let ``Reference to nonexistent action produces RefNotFound`` () =
    let errs =
        [ action "t" (Thicken(Some "ghost", 2.0)) ]
        |> TypeCheck.typecheck |> errors
    match errs with
    | [ RefNotFound(id, key, target) ] ->
        Assert.Equal("t", id)
        Assert.Equal("child", key)
        Assert.Equal("ghost", target)
    | _ -> failwithf "Unexpected errors: %A" errs

// ── Error: forward ref ───────────────────────────────────────────────────

[<Fact>]
let ``Forward reference produces ForwardRef`` () =
    let errs =
        [ action "t" (Thicken(Some "s", 2.0))
          action "s" (Sphere 5.0) ]
        |> TypeCheck.typecheck |> errors
    match errs with
    | [ ForwardRef(id, key, target) ] ->
        Assert.Equal("t", id)
        Assert.Equal("child", key)
        Assert.Equal("s", target)
    | _ -> failwithf "Unexpected errors: %A" errs

// ── Error: type mismatch ─────────────────────────────────────────────────

[<Fact>]
let ``Union with Sketch input produces TypeMismatch`` () =
    let errs =
        [ action "sk" (Sketch(None, XY, ActionSketch.empty))
          action "s" (Sphere 5.0)
          action "u" (Union(Some "sk", Some "s", 0.0)) ]
        |> TypeCheck.typecheck |> errors
    match errs with
    | [ TypeMismatch(id, key, expected, got) ] ->
        Assert.Equal("u", id)
        Assert.Equal("a", key)
        Assert.Equal<FieldType list>([ FieldType.Field ], expected)
        Assert.Equal(FieldType.Sketch, got)
    | _ -> failwithf "Unexpected errors: %A" errs

[<Fact>]
let ``FromSketch with Field input produces TypeMismatch`` () =
    let errs =
        [ action "s" (Sphere 5.0)
          action "fs" (FromSketch(Some "s", false, FromSketchSelection.defaults)) ]
        |> TypeCheck.typecheck |> errors
    match errs with
    | [ TypeMismatch(id, key, expected, got) ] ->
        Assert.Equal("fs", id)
        Assert.Equal("child", key)
        Assert.Equal<FieldType list>([ FieldType.Sketch ], expected)
        Assert.Equal(FieldType.Field, got)
    | _ -> failwithf "Unexpected errors: %A" errs

[<Fact>]
let ``Mesh with Sketch input produces TypeMismatch`` () =
    let errs =
        [ action "sk" (Sketch(None, XY, ActionSketch.empty))
          action "m" (Mesh(Some "sk", 0.2, 96)) ]
        |> TypeCheck.typecheck |> errors
    match errs with
    | [ TypeMismatch(id, key, expected, got) ] ->
        Assert.Equal("m", id)
        Assert.Equal("child", key)
        Assert.Equal<FieldType list>([ FieldType.Field ], expected)
        Assert.Equal(FieldType.Sketch, got)
    | _ -> failwithf "Unexpected errors: %A" errs

// ── Multiple errors collected ────────────────────────────────────────────

[<Fact>]
let ``Multiple errors are collected across actions`` () =
    let errs =
        [ action "u" (Union(None, None, 0.0))
          action "t" (Thicken(Some "ghost", 2.0)) ]
        |> TypeCheck.typecheck |> errors
    // Union: 2 MissingRef, Thicken: 1 RefNotFound
    Assert.Equal(3, errs.Length)

// ── Complex graph ────────────────────────────────────────────────────────

[<Fact>]
let ``Default document typechecks successfully`` () =
    let typed =
        (Document.defaultDocument()).Actions
        |> TypeCheck.typecheck |> ok
    Assert.Equal(7, typed.Length)
    Assert.Equal(FieldType.Frame, outputOf "origin" typed)
    Assert.Equal(FieldType.Field, outputOf "cyl1" typed)
    Assert.Equal(FieldType.Field, outputOf "sph1" typed)
    Assert.Equal(FieldType.Field, outputOf "sub1" typed)
    Assert.Equal(FieldType.Sketch, outputOf "sketch1" typed)
    Assert.Equal(FieldType.Frame, outputOf "frame1" typed)
    Assert.Equal(FieldType.Field, outputOf "from1" typed)

[<Fact>]
let ``Chained transforms preserve type`` () =
    let typed =
        [ action "o" Origin
          action "t1" (Translate(Some "o", 1.0, 0.0, 0.0))
          action "r1" (Rotate(Some "t1", 0.0, 0.0, 1.0, eighthTurn))
          action "t2" (Translate(Some "r1", 0.0, 5.0, 0.0)) ]
        |> TypeCheck.typecheck |> ok
    // Frame flows through the chain
    Assert.Equal(FieldType.Frame, outputOf "t1" typed)
    Assert.Equal(FieldType.Frame, outputOf "r1" typed)
    Assert.Equal(FieldType.Frame, outputOf "t2" typed)

[<Fact>]
let ``DAG with shared references typechecks`` () =
    let typed =
        [ action "s" (Sphere 5.0)
          action "t1" (Translate(Some "s", 5.0, 0.0, 0.0))
          action "t2" (Translate(Some "s", -5.0, 0.0, 0.0))
          action "u" (Union(Some "t1", Some "t2", 0.0)) ]
        |> TypeCheck.typecheck |> ok
    Assert.Equal(FieldType.Field, outputOf "u" typed)
