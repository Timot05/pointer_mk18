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
    result

let buildElements (actions: DocAction list) =
    let tc = TypeCheck.typecheck actions
    let typeMap = tc.Typed |> List.map (fun t -> t.Id, t.Output) |> Map.ofList
    Element.build actions typeMap

let surfaceFor id (surfaces: FieldSurface list) =
    surfaces |> List.find (fun s -> s.ActionId = id)

let slotVal (table: SlotTable) actionId path =
    SlotTable.valueAt table { ActionId = actionId; Path = path }
    |> Option.defaultWith (fun () -> failwithf "No slot for %s.%s" actionId path)

// ── Element tree tests ───────────────────────────────────────────────────

[<Fact>]
let ``Sphere builds to ESphere element`` () =
    let r = pipeline [ action "s" (Sphere 5.0) ]
    let elements = (buildElements [ action "s" (Sphere 5.0) ]).Elements
    match Map.find "s" elements with
    | ESphere(id, rad) ->
        Assert.Equal("s", id)
        Assert.Equal(5.0, rad)
    | other -> failwithf "Expected ESphere, got %A" other
    Assert.Equal(5.0, slotVal r.Slots "s" "radius")

[<Fact>]
let ``Translate wraps child in ETranslate`` () =
    let actions = [ action "s" (Sphere 5.0); action "t" (Translate(Some "s", 1.0, 2.0, 3.0)) ]
    let elements = (buildElements actions).Elements
    match Map.find "t" elements with
    | ETranslate("t", 1.0, 2.0, 3.0, ESphere("s", 5.0)) -> ()
    | other -> failwithf "Expected ETranslate(ESphere), got %A" other

[<Fact>]
let ``Translate of Origin is a frame chain (no Field element)`` () =
    let actions = [ action "o" Origin; action "t" (Translate(Some "o", 5.0, 0.0, 0.0)) ]
    let bres = buildElements actions
    // Frame-typed actions don't appear as Field elements
    Assert.False(Map.containsKey "t" bres.Elements)
    // But they DO produce a frame chain
    Assert.Equal<FrameChain>([ FrameTranslate("t", 5.0, 0.0, 0.0) ], Map.find "t" bres.Frames)

[<Fact>]
let ``Rotate of Origin is a frame chain (no Field element)`` () =
    let actions = [ action "o" Origin; action "r" (Rotate(Some "o", 0.0, 0.0, 1.0, 90.0)) ]
    let bres = buildElements actions
    Assert.False(Map.containsKey "r" bres.Elements)
    Assert.Equal<FrameChain>([ FrameRotate("r", 0.0, 0.0, 1.0, 90.0) ], Map.find "r" bres.Frames)

[<Fact>]
let ``Union builds EUnion with both children`` () =
    let actions = [ action "a" (Sphere 5.0); action "b" (Sphere 3.0); action "u" (Union(Some "a", Some "b", 0.5)) ]
    let elements = (buildElements actions).Elements
    match Map.find "u" elements with
    | EUnion("u", ESphere("a", 5.0), ESphere("b", 3.0), 0.5) -> ()
    | other -> failwithf "Expected EUnion, got %A" other

[<Fact>]
let ``Shared references produce same subtree`` () =
    let actions = [ action "s" (Sphere 5.0)
                    action "t1" (Translate(Some "s", 5.0, 0.0, 0.0))
                    action "t2" (Translate(Some "s", -5.0, 0.0, 0.0))
                    action "u" (Union(Some "t1", Some "t2", 0.0)) ]
    let elements = (buildElements actions).Elements
    match Map.find "u" elements with
    | EUnion("u", ETranslate("t1", 5.0, _, _, ESphere("s", 5.0)),
                  ETranslate("t2", -5.0, _, _, ESphere("s", 5.0)), _) -> ()
    | other -> failwithf "Expected EUnion of two translated spheres, got %A" other

// ── FieldNode compilation tests ──────────────────────────────────────────

[<Fact>]
let ``Sphere compiles to FPrimitive with a radius slot`` () =
    let r = pipeline [ action "s" (Sphere 5.0) ]
    let s = surfaceFor "s" r.Surfaces
    match s.Field with
    | FPrimitive(PrimSphere slot) ->
        Assert.Equal(5.0, r.Slots.Values.[slot])
    | other -> failwithf "Expected FPrimitive(PrimSphere), got %A" other

[<Fact>]
let ``Translated sphere produces FTranslate over FPrimitive`` () =
    let r =
        pipeline [ action "s" (Sphere 5.0)
                   action "t" (Translate(Some "s", 10.0, 0.0, 0.0)) ]
    let t = surfaceFor "t" r.Surfaces
    match t.Field with
    | FTranslate(xSlot, ySlot, zSlot, FPrimitive(PrimSphere rSlot)) ->
        Assert.Equal(10.0, r.Slots.Values.[xSlot])
        Assert.Equal(0.0, r.Slots.Values.[ySlot])
        Assert.Equal(0.0, r.Slots.Values.[zSlot])
        Assert.Equal(5.0, r.Slots.Values.[rSlot])
    | other -> failwithf "Expected FTranslate(FPrimitive), got %A" other

[<Fact>]
let ``Rotated sphere produces FRotate over FPrimitive`` () =
    let r =
        pipeline [ action "s" (Sphere 5.0)
                   action "r" (Rotate(Some "s", 0.0, 0.0, 1.0, 90.0)) ]
    let rot = surfaceFor "r" r.Surfaces
    match rot.Field with
    | FRotate(_, _, azSlot, angleSlot, FPrimitive(PrimSphere _)) ->
        Assert.Equal(1.0, r.Slots.Values.[azSlot])
        Assert.Equal(90.0, r.Slots.Values.[angleSlot])
    | other -> failwithf "Expected FRotate(FPrimitive), got %A" other

[<Fact>]
let ``Chained transforms produce nested FTranslate nodes`` () =
    let r =
        pipeline [ action "s" (Sphere 5.0)
                   action "t1" (Translate(Some "s", 10.0, 0.0, 0.0))
                   action "t2" (Translate(Some "t1", 0.0, 5.0, 0.0)) ]
    let t = surfaceFor "t2" r.Surfaces
    match t.Field with
    | FTranslate(_, t2y, _, FTranslate(t1x, _, _, FPrimitive(PrimSphere _))) ->
        Assert.Equal(5.0, r.Slots.Values.[t2y])
        Assert.Equal(10.0, r.Slots.Values.[t1x])
    | other -> failwithf "Expected nested FTranslate chain, got %A" other

[<Fact>]
let ``Union compiles to FBoolean with slot`` () =
    let r =
        pipeline [ action "a" (Sphere 5.0)
                   action "b" (Sphere 3.0)
                   action "u" (Union(Some "a", Some "b", 0.5)) ]
    let u = surfaceFor "u" r.Surfaces
    match u.Field with
    | FBoolean(BoolUnion, rSlot, FPrimitive(PrimSphere _), FPrimitive(PrimSphere _)) ->
        Assert.Equal(0.5, r.Slots.Values.[rSlot])
    | other -> failwithf "Expected FBoolean(Union), got %A" other

[<Fact>]
let ``Thicken compiles to FFieldOp with slot`` () =
    let r =
        pipeline [ action "s" (Sphere 5.0)
                   action "t" (Thicken(Some "s", 2.0)) ]
    let t = surfaceFor "t" r.Surfaces
    match t.Field with
    | FFieldOp(OpThicken, vSlot, FPrimitive(PrimSphere _)) ->
        Assert.Equal(2.0, r.Slots.Values.[vSlot])
    | other -> failwithf "Expected FFieldOp(Thicken), got %A" other

[<Fact>]
let ``Hidden actions produce no surfaces`` () =
    let r = pipeline [ hidden "s" (Sphere 5.0) ]
    Assert.Empty(r.Surfaces)

[<Fact>]
let ``Frame actions produce no surfaces`` () =
    let r = pipeline [ action "o" Origin ]
    Assert.Empty(r.Surfaces)

[<Fact>]
let ``Default document compiles to four surfaces`` () =
    let actions = (Document.defaultDocument()).Actions
    let r = pipeline actions
    // cyl1, sph1, sub1 are visible Field-producing actions, plus from1 (FSketch)
    Assert.Equal(4, r.Surfaces.Length)

[<Fact>]
let ``Move applies frame transform as FTranslate wrapping the child`` () =
    let r =
        pipeline [ action "o" Origin
                   action "f" (Translate(Some "o", 10.0, 0.0, 0.0))
                   action "s" (Sphere 5.0)
                   action "m" (Move(Some "s", Some "f")) ]
    let m = surfaceFor "m" r.Surfaces
    // Frame 'f' is a Translate(10, 0, 0), so Move wraps the child with
    // FTranslate using 'f's slots.
    match m.Field with
    | FTranslate(xSlot, _, _, FPrimitive(PrimSphere _)) ->
        Assert.Equal(10.0, r.Slots.Values.[xSlot])
        // Verify the slot is the 'f' action's x slot, not duplicated
        let fXSlot = Map.find { ActionId = "f"; Path = "x" } r.Slots.Index
        Assert.Equal(fXSlot, xSlot)
    | other -> failwithf "Expected FTranslate(FPrimitive) from frame, got %A" other

// ── SlotTable tests ──────────────────────────────────────────────────────

[<Fact>]
let ``Slot allocation is idempotent`` () =
    let b = SlotTable.createBuilder ()
    let r = { ActionId = "s"; Path = "radius" }
    let s1 = SlotTable.alloc b r 5.0
    let s2 = SlotTable.alloc b r 999.0  // default ignored on reuse
    Assert.Equal(s1, s2)
    // First default wins
    let table = SlotTable.toTable b
    Assert.Equal(5.0, table.Values.[s1])

[<Fact>]
let ``SlotTable.update mutates in place`` () =
    let b = SlotTable.createBuilder ()
    let r = { ActionId = "s"; Path = "radius" }
    SlotTable.alloc b r 5.0 |> ignore
    let table = SlotTable.toTable b
    Assert.True(SlotTable.update table r 42.0)
    Assert.Equal(42.0, SlotTable.valueAt table r |> Option.get)

[<Fact>]
let ``SlotTable.update returns false on miss`` () =
    let b = SlotTable.createBuilder ()
    let table = SlotTable.toTable b
    Assert.False(SlotTable.update table { ActionId = "missing"; Path = "x" } 1.0)

[<Fact>]
let ``Slot indices are stable across compiles`` () =
    let actions = [ action "s" (Sphere 5.0); action "c" (Cylinder(3.0, 10.0)) ]
    let r1 = Pipeline.compile actions
    let r2 = Pipeline.compile actions
    Assert.Equal<Map<SlotRef, Slot>>(r1.Slots.Index, r2.Slots.Index)

[<Fact>]
let ``Display slots are allocated for Field-type actions`` () =
    let r = pipeline [ action "s" (Sphere 5.0) ]
    let colorSlot = Map.tryFind { ActionId = "s"; Path = "display.color.0" } r.Slots.Index
    let isoSlot = Map.tryFind { ActionId = "s"; Path = "display.isoValue" } r.Slots.Index
    Assert.True(colorSlot.IsSome)
    Assert.True(isoSlot.IsSome)

[<Fact>]
let ``Display slots are NOT allocated for non-Field actions`` () =
    let r = pipeline [ action "o" Origin ]
    let colorSlot = Map.tryFind { ActionId = "o"; Path = "display.color.0" } r.Slots.Index
    Assert.True(colorSlot.IsNone)

// ── FSketch / FromSketch tests ───────────────────────────────────────────

let squareSketch : ActionSketch =
    { Entities =
        [ REPoint("p_bl", 0.0, 0.0)
          REPoint("p_br", 10.0, 0.0)
          REPoint("p_tr", 10.0, 10.0)
          REPoint("p_tl", 0.0, 10.0)
          RELine("l_bottom", "p_bl", "p_br")
          RELine("l_right", "p_br", "p_tr")
          RELine("l_top", "p_tr", "p_tl")
          RELine("l_left", "p_tl", "p_bl") ]
      Constraints = [] }

[<Fact>]
let ``FromSketch with SelectionLoop None compiles to FSketch with 4 line segments`` () =
    let r =
        pipeline [ action "sk" (Sketch(None, squareSketch))
                   action "f" (FromSketch(Some "sk", true, false, SelectionLoop None)) ]
    let f = surfaceFor "f" r.Surfaces
    match f.Field with
    | FSketch { Primitives = prims; Closed = true; Flip = false } ->
        Assert.Equal(4, prims.Length)
        // Every primitive should be a line segment
        for p in prims do
            match p with
            | SpLineSegment _ -> ()
            | other -> failwithf "Expected SpLineSegment, got %A" other
    | other -> failwithf "Expected FSketch, got %A" other

[<Fact>]
let ``FromSketch with SelectionElements compiles lines in the given order`` () =
    let r =
        pipeline [ action "sk" (Sketch(None, squareSketch))
                   action "f" (FromSketch(
                       Some "sk",
                       false,
                       false,
                       SelectionElements [ "l_bottom"; "l_right" ])) ]
    let f = surfaceFor "f" r.Surfaces
    match f.Field with
    | FSketch { Primitives = [ SpLineSegment _; SpLineSegment _ ]; Closed = false } -> ()
    | other -> failwithf "Expected two-line FSketch, got %A" other

[<Fact>]
let ``FSketch primitive slots match the sketch's pre-allocated point slots`` () =
    let r =
        pipeline [ action "sk" (Sketch(None, squareSketch))
                   action "f" (FromSketch(Some "sk", true, false, SelectionLoop None)) ]
    let f = surfaceFor "f" r.Surfaces
    // Grab the first segment and verify its start-point slots coincide
    // with sketch "sk"'s authored point slots for one of the corners.
    let firstSeg =
        match f.Field with
        | FSketch { Primitives = (SpLineSegment(s, _)) :: _ } -> s
        | _ -> failwith "no line segment"
    // The allocated slot for p_bl.x lives in r.Slots.Index
    let expectedXSlots =
        [ "p_bl"; "p_br"; "p_tr"; "p_tl" ]
        |> List.map (fun id -> Map.find { ActionId = "sk"; Path = sprintf "sketch.entity.%s.x" id } r.Slots.Index)
        |> Set.ofList
    Assert.Contains(firstSeg.XSlot, expectedXSlots)

[<Fact>]
let ``FromSketch with a Sketch origin wraps FSketch in FTranslate`` () =
    let r =
        pipeline [ action "o" Origin
                   action "tf" (Translate(Some "o", 5.0, 0.0, 0.0))
                   action "sk" (Sketch(Some "tf", squareSketch))
                   action "f" (FromSketch(Some "sk", true, false, SelectionLoop None)) ]
    let f = surfaceFor "f" r.Surfaces
    match f.Field with
    | FTranslate(xSlot, _, _, FSketch _) ->
        Assert.Equal(5.0, r.Slots.Values.[xSlot])
    | other -> failwithf "Expected FTranslate wrapping FSketch, got %A" other

[<Fact>]
let ``Default document now includes the from1 FSketch surface`` () =
    let actions = (Document.defaultDocument()).Actions
    let r = pipeline actions
    // cyl1, sph1, sub1 (Fields) + from1 (FSketch) = 4 surfaces
    Assert.Equal(4, r.Surfaces.Length)
    let from1 = r.Surfaces |> List.find (fun s -> s.ActionId = "from1")
    match from1.Field with
    | FSketch { Primitives = prims } -> Assert.Equal(4, prims.Length)
    | other -> failwithf "Expected FSketch for from1, got %A" other

// ── Pickable tests ───────────────────────────────────────────────────────

let pickIds (picks: Pickable list) = picks |> List.map Pickable.pickId

[<Fact>]
let ``Default document produces the expected pickable counts`` () =
    let actions = (Document.defaultDocument()).Actions
    let r = pipeline actions
    let countBy pred = r.Pickables |> List.filter pred |> List.length
    // 4 visible Field actions → 4 PickSurface
    Assert.Equal(4, countBy (function PickSurface _ -> true | _ -> false))
    // sketch1: 4 points + 4 lines + 1 loop + 2 dimensions
    Assert.Equal(4, countBy (function PickPoint _ -> true | _ -> false))
    Assert.Equal(4, countBy (function PickLine _ -> true | _ -> false))
    Assert.Equal(1, countBy (function PickLoop _ -> true | _ -> false))
    Assert.Equal(2, countBy (function PickDimension _ -> true | _ -> false))

[<Fact>]
let ``PickIds form a contiguous zero-based range`` () =
    let actions = (Document.defaultDocument()).Actions
    let r = pipeline actions
    let ids = pickIds r.Pickables |> List.sort
    Assert.Equal<int list>([ 0 .. r.Pickables.Length - 1 ], ids)

[<Fact>]
let ``PickPoint xSlot matches the sketch entity's authored slot`` () =
    let r = pipeline (Document.defaultDocument()).Actions
    let p_bl_xSlot = Map.find { ActionId = "sketch1"; Path = "sketch.entity.p_bl.x" } r.Slots.Index
    let pickP_bl =
        r.Pickables
        |> List.pick (function
            | PickPoint(_, "sketch1", "p_bl", xs, _) -> Some xs
            | _ -> None)
    Assert.Equal(p_bl_xSlot, pickP_bl)

[<Fact>]
let ``PickLoop.entityIds matches detectLoops output`` () =
    let actions = (Document.defaultDocument()).Actions
    let r = pipeline actions
    let sketch =
        actions
        |> List.pick (fun a ->
            match a.Kind with Sketch(_, s) -> Some s | _ -> None)
    let detectedIds =
        (SketchLoops.detectLoops sketch.Entities |> List.head).EntityIds
    let pickLoopIds =
        r.Pickables
        |> List.pick (function
            | PickLoop(_, "sketch1", _, ids) -> Some ids
            | _ -> None)
    Assert.Equal<string list>(detectedIds, pickLoopIds)

[<Fact>]
let ``Pickable list is stable across compiles of the same input`` () =
    let actions = (Document.defaultDocument()).Actions
    let r1 = Pipeline.compile actions
    let r2 = Pipeline.compile actions
    // Same count + same ordered list of variants + ids
    Assert.Equal(r1.Pickables.Length, r2.Pickables.Length)
    let key (p: Pickable) =
        match p with
        | PickPoint(id, s, e, _, _) -> sprintf "point|%d|%s|%s" id s e
        | PickLine(id, s, e, _, _) -> sprintf "line|%d|%s|%s" id s e
        | PickCircle(id, s, e, _, _) -> sprintf "circle|%d|%s|%s" id s e
        | PickArc(id, s, e, _, _, _, _) -> sprintf "arc|%d|%s|%s" id s e
        | PickLoop(id, s, l, _) -> sprintf "loop|%d|%s|%s" id s l
        | PickDimension(id, s, i, _) -> sprintf "dim|%d|%s|%d" id s i
        | PickSurface(id, a) -> sprintf "surf|%d|%s" id a
    let k1 = r1.Pickables |> List.map key
    let k2 = r2.Pickables |> List.map key
    Assert.Equal<string list>(k1, k2)
