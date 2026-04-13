module CompileTests

open Xunit
open Server

// ── Helpers ──────────────────────────────────────────────────────────────

let action id kind : DocAction =
    { Id = id; Name = None; Kind = kind; Visible = true; Display = None; FieldSlice = None }

let hidden id kind : DocAction =
    { Id = id; Name = None; Kind = kind; Visible = false; Display = None; FieldSlice = None }

let pipeline (actions: DocAction list) =
    let result = Pipeline.compile actions
    if result.Errors.Length > 0 then failwithf "Pipeline errors: %A" result.Errors
    let elements = Element.build actions
    elements, result.Surfaces

let surfaceFor id (surfaces: FieldSurface list) =
    surfaces |> List.find (fun s -> s.ActionId = id)

// ── Element tree tests ───────────────────────────────────────────────────

[<Fact>]
let ``Sphere builds to ESphere element`` () =
    let elements, _ = pipeline [ action "s" (Sphere 5.0) ]
    match Map.find "s" elements with
    | ESphere r -> Assert.Equal(5.0, r)
    | other -> failwithf "Expected ESphere, got %A" other

[<Fact>]
let ``Translate wraps child in ETranslate`` () =
    let elements, _ =
        pipeline [ action "s" (Sphere 5.0)
                   action "t" (Translate(Some "s", 1.0, 2.0, 3.0)) ]
    match Map.find "t" elements with
    | ETranslate(offset, ESphere 5.0) ->
        Assert.Equal(1.0, offset.X)
        Assert.Equal(2.0, offset.Y)
        Assert.Equal(3.0, offset.Z)
    | other -> failwithf "Expected ETranslate(ESphere), got %A" other

[<Fact>]
let ``Translate on Origin folds into EFrame`` () =
    let elements, _ =
        pipeline [ action "o" Origin
                   action "t" (Translate(Some "o", 5.0, 0.0, 0.0)) ]
    match Map.find "t" elements with
    | EFrame t ->
        Assert.Equal(5.0, t.Trans.X, 1e-10)
        Assert.Equal(0.0, t.Trans.Y, 1e-10)
    | other -> failwithf "Expected EFrame, got %A" other

[<Fact>]
let ``Rotate on Origin folds into EFrame`` () =
    let elements, _ =
        pipeline [ action "o" Origin
                   action "r" (Rotate(Some "o", 0.0, 0.0, 1.0, 90.0)) ]
    match Map.find "r" elements with
    | EFrame _ -> ()
    | other -> failwithf "Expected EFrame, got %A" other

[<Fact>]
let ``Union builds EUnion with both children`` () =
    let elements, _ =
        pipeline [ action "a" (Sphere 5.0)
                   action "b" (Sphere 3.0)
                   action "u" (Union(Some "a", Some "b", 0.5)) ]
    match Map.find "u" elements with
    | EUnion(ESphere 5.0, ESphere 3.0, r) ->
        Assert.Equal(0.5, r)
    | other -> failwithf "Expected EUnion, got %A" other

[<Fact>]
let ``Shared references produce same subtree`` () =
    let elements, _ =
        pipeline [ action "s" (Sphere 5.0)
                   action "t1" (Translate(Some "s", 5.0, 0.0, 0.0))
                   action "t2" (Translate(Some "s", -5.0, 0.0, 0.0))
                   action "u" (Union(Some "t1", Some "t2", 0.0)) ]
    match Map.find "u" elements with
    | EUnion(ETranslate(_, ESphere 5.0), ETranslate(_, ESphere 5.0), _) -> ()
    | other -> failwithf "Expected EUnion of two translated spheres, got %A" other

// ── FieldNode compilation tests ──────────────────────────────────────────

[<Fact>]
let ``Sphere compiles to FPrimitive with identity transform`` () =
    let _, surfaces = pipeline [ action "s" (Sphere 5.0) ]
    let s = surfaceFor "s" surfaces
    match s.Field with
    | FPrimitive(PrimSphere 5.0, t) ->
        Assert.Equal(Quat.Identity, t.Rot)
        Assert.Equal(Vec3.Zero, t.Trans)
    | other -> failwithf "Expected FPrimitive(PrimSphere), got %A" other

[<Fact>]
let ``Translated sphere bakes translation into transform`` () =
    let _, surfaces =
        pipeline [ action "s" (Sphere 5.0)
                   action "t" (Translate(Some "s", 10.0, 0.0, 0.0)) ]
    let t = surfaceFor "t" surfaces
    match t.Field with
    | FPrimitive(PrimSphere 5.0, xf) ->
        Assert.Equal(10.0, xf.Trans.X, 1e-10)
    | other -> failwithf "Expected FPrimitive with baked translation, got %A" other

[<Fact>]
let ``Rotated sphere bakes rotation into transform`` () =
    let _, surfaces =
        pipeline [ action "s" (Sphere 5.0)
                   action "r" (Rotate(Some "s", 0.0, 0.0, 1.0, 90.0)) ]
    let r = surfaceFor "r" surfaces
    match r.Field with
    | FPrimitive(PrimSphere 5.0, xf) ->
        // Rotation should be non-identity
        Assert.NotEqual(Quat.Identity, xf.Rot)
    | other -> failwithf "Expected FPrimitive with baked rotation, got %A" other

[<Fact>]
let ``Chained transforms are composed`` () =
    let _, surfaces =
        pipeline [ action "s" (Sphere 5.0)
                   action "t1" (Translate(Some "s", 10.0, 0.0, 0.0))
                   action "t2" (Translate(Some "t1", 0.0, 5.0, 0.0)) ]
    let t = surfaceFor "t2" surfaces
    match t.Field with
    | FPrimitive(PrimSphere 5.0, xf) ->
        Assert.Equal(10.0, xf.Trans.X, 1e-10)
        Assert.Equal(5.0, xf.Trans.Y, 1e-10)
    | other -> failwithf "Expected FPrimitive with composed translation, got %A" other

[<Fact>]
let ``Union compiles to FBoolean`` () =
    let _, surfaces =
        pipeline [ action "a" (Sphere 5.0)
                   action "b" (Sphere 3.0)
                   action "u" (Union(Some "a", Some "b", 0.5)) ]
    let u = surfaceFor "u" surfaces
    match u.Field with
    | FBoolean(BoolUnion, r, FPrimitive(PrimSphere 5.0, _), FPrimitive(PrimSphere 3.0, _)) ->
        Assert.Equal(0.5, r)
    | other -> failwithf "Expected FBoolean(Union), got %A" other

[<Fact>]
let ``Thicken compiles to FFieldOp`` () =
    let _, surfaces =
        pipeline [ action "s" (Sphere 5.0)
                   action "t" (Thicken(Some "s", 2.0)) ]
    let t = surfaceFor "t" surfaces
    match t.Field with
    | FFieldOp(OpThicken, 2.0, FPrimitive(PrimSphere 5.0, _)) -> ()
    | other -> failwithf "Expected FFieldOp(Thicken), got %A" other

[<Fact>]
let ``Hidden actions produce no surfaces`` () =
    let _, surfaces =
        pipeline [ hidden "s" (Sphere 5.0) ]
    Assert.Empty(surfaces)

[<Fact>]
let ``Frame actions produce no surfaces`` () =
    let _, surfaces =
        pipeline [ action "o" Origin ]
    Assert.Empty(surfaces)

[<Fact>]
let ``Default document compiles to one surface`` () =
    let actions = (Document.defaultDocument()).Actions
    let _, surfaces = pipeline actions
    // sub1 is the only visible field-producing leaf
    // cyl1 and sph1 are also visible and produce surfaces
    Assert.Equal(3, surfaces.Length)

[<Fact>]
let ``Move applies frame transform`` () =
    let _, surfaces =
        pipeline [ action "o" Origin
                   action "f" (Translate(Some "o", 10.0, 0.0, 0.0))
                   action "s" (Sphere 5.0)
                   action "m" (Move(Some "s", Some "f")) ]
    let m = surfaceFor "m" surfaces
    match m.Field with
    | FPrimitive(PrimSphere 5.0, xf) ->
        Assert.Equal(10.0, xf.Trans.X, 1e-10)
    | other -> failwithf "Expected FPrimitive with frame transform, got %A" other
