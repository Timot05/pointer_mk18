module SketchAuthoringTests

open Xunit
open Server

let angleDoc () =
    let sketch =
        { Entities =
            [ REPoint("o", 0.0, 0.0)
              REPoint("ax", 10.0, 0.0)
              REPoint("bx", 6.0, 6.0)
              RELine("line_a", "o", "ax")
              RELine("line_b", "o", "bx") ]
          Constraints = [] }
    { Name = "angle"
      SelectedId = Some "sketchA"
      Actions =
        [ { Id = "origin"; Name = None; Kind = Origin; Visible = true; Display = None; FieldSlice = None }
          { Id = "sketchA"; Name = None; Kind = Sketch(Some "origin", XY, sketch); Visible = true; Display = None; FieldSlice = None } ] }

let arcDoc () =
    let sketch =
        { Entities =
            [ REPoint("c", 0.0, 0.0)
              REPoint("s", 10.0, 0.0)
              REPoint("e", 0.0, 10.0)
              REArc("arc1", "s", "e", ArcCenter("c", false)) ]
          Constraints = [] }
    { Name = "arc"
      SelectedId = Some "sketchArc"
      Actions =
        [ { Id = "origin"; Name = None; Kind = Origin; Visible = true; Display = None; FieldSlice = None }
          { Id = "sketchArc"; Name = None; Kind = Sketch(Some "origin", XY, sketch); Visible = true; Display = None; FieldSlice = None } ] }

let tangentArcDoc () =
    let sketch =
        { Entities =
            [ REPoint("c", 0.0, 0.0)
              REPoint("s", 10.0, 0.0)
              REPoint("e", 0.0, 10.0)
              REPoint("l1", 10.0, 0.0)
              REPoint("l2", 10.0, 8.0)
              RELine("line1", "l1", "l2")
              REArc("arc1", "s", "e", ArcCenter("c", false)) ]
          Constraints = [] }
    { Name = "tangent-arc"
      SelectedId = Some "sketchT"
      Actions =
        [ { Id = "origin"; Name = None; Kind = Origin; Visible = true; Display = None; FieldSlice = None }
          { Id = "sketchT"; Name = None; Kind = Sketch(Some "origin", XY, sketch); Visible = true; Display = None; FieldSlice = None } ] }

let curveTangentDoc () =
    let sketch =
        { Entities =
            [ REPoint("c0", 0.0, 0.0)
              REPoint("c1", 20.0, 0.0)
              REPoint("a0s", 10.0, 0.0)
              REPoint("a1s", 26.0, 0.0)
              RECircle("circle1", "c0", 10.0)
              REArc("arc1", "a1s", "c1", ArcCenter("c1", false)) ]
          Constraints = [] }
    { Name = "curve-tangent"
      SelectedId = Some "sketchCT"
      Actions =
        [ { Id = "origin"; Name = None; Kind = Origin; Visible = true; Display = None; FieldSlice = None }
          { Id = "sketchCT"; Name = None; Kind = Sketch(Some "origin", XY, sketch); Visible = true; Display = None; FieldSlice = None } ] }

let frameConstraintDoc () =
    let sketch =
        { Entities =
            [ REPoint("p0", 0.0, 0.0)
              REPoint("p1", 10.0, 0.0)
              RELine("l1", "p0", "p1") ]
          Constraints = [] }
    { Name = "frame-constraints"
      SelectedId = Some "sketchF"
      Actions =
        [ { Id = "origin"; Name = None; Kind = Origin; Visible = true; Display = None; FieldSlice = None }
          { Id = "f1"; Name = None; Kind = Translate(Some "origin", 5.0, 0.0, 0.0); Visible = true; Display = None; FieldSlice = None }
          { Id = "sketchF"; Name = None; Kind = Sketch(Some "origin", XY, sketch); Visible = true; Display = None; FieldSlice = None } ] }

[<Fact>]
let ``Dimension placement buttons stay enabled in sketch edit mode`` () =
    let doc = Document.defaultDocument () |> Document.select "sketch1"
    let state =
        SketchAuthoring.availabilityForSelection doc true "none" None [] None None None

    Assert.True(state.DimensionPlacementAvailability["distance"])
    Assert.True(state.DimensionPlacementAvailability["angle"])

[<Fact>]
let ``Distance placement with one selected line has no pending constraint`` () =
    let doc = Document.defaultDocument () |> Document.select "sketch1"
    let targets = [ TargetLine("sketch1", "l_bottom") ]
    let state =
        SketchAuthoring.availabilityForSelection doc true "none" (Some "distance") targets (Some(10.0, -4.0)) None None

    Assert.Equal(Some "distance", state.ConstraintPlacementMode)
    Assert.Null(box state.PendingConstraintPlacement)

[<Fact>]
let ``Distance placement with two selected lines yields pending line distance`` () =
    let doc = Document.defaultDocument () |> Document.select "sketch1"
    let targets = [ TargetLine("sketch1", "l_bottom"); TargetLine("sketch1", "l_top") ]
    let state =
        SketchAuthoring.availabilityForSelection doc true "none" (Some "distance") targets (Some(8.0, 5.0)) None None

    Assert.Equal(Some "distance", state.ConstraintPlacementMode)
    match state.PendingConstraintPlacement with
    | Some pending ->
        Assert.Equal("sketch1", pending.SketchId)
        match pending.Constraint with
        | LineDistance(aStart, aEnd, bStart, bEnd, lineA, lineB, distance, None) ->
            Assert.Equal("l_bottom", lineA)
            Assert.Equal("l_top", lineB)
            Assert.Equal("p_bl", aStart)
            Assert.Equal("p_br", aEnd)
            Assert.Equal("p_tr", bStart)
            Assert.Equal("p_tl", bEnd)
            Assert.True(distance > 0.0)
        | other ->
            failwithf "Expected pending LineDistance, got %A" other
    | None ->
        failwith "Expected pending distance placement"

[<Fact>]
let ``Distance placement can resolve from one selected line plus hovered line`` () =
    let doc = Document.defaultDocument () |> Document.select "sketch1"
    let selected = [ TargetLine("sketch1", "l_bottom") ]
    let effective = selected @ [ TargetLine("sketch1", "l_top") ]
    let state =
        SketchAuthoring.availabilityForSelection doc true "none" (Some "distance") effective (Some(8.0, 5.0)) None None

    match state.PendingConstraintPlacement with
    | Some pending ->
        match pending.Constraint with
        | LineDistance(_, _, _, _, lineA, lineB, _, None) ->
            Assert.Equal("l_bottom", lineA)
            Assert.Equal("l_top", lineB)
        | other ->
            failwithf "Expected pending LineDistance, got %A" other
    | None ->
        failwith "Expected pending distance placement"

[<Fact>]
let ``Angle placement with two selected lines yields pending angle`` () =
    let doc = Document.defaultDocument () |> Document.select "sketch1"
    let targets = [ TargetLine("sketch1", "l_bottom"); TargetLine("sketch1", "l_right") ]
    let state =
        SketchAuthoring.availabilityForSelection doc true "none" (Some "angle") targets (Some(18.0, 2.0)) None None

    Assert.Equal(Some "angle", state.ConstraintPlacementMode)
    match state.PendingConstraintPlacement with
    | Some pending ->
        Assert.Equal("sketch1", pending.SketchId)
        match pending.Constraint with
        | Angle(_, _, _, _, lineA, lineB, degrees, _, _, _, None) ->
            Assert.Equal("l_bottom", lineA)
            Assert.Equal("l_right", lineB)
            Assert.True(degrees > 0.0)
        | other ->
            failwithf "Expected pending Angle, got %A" other
    | None ->
        failwith "Expected pending angle placement"

[<Fact>]
let ``Angle draft chooses acute or obtuse value based on cursor quadrant`` () =
    let doc = angleDoc ()
    let draft = Some { SketchId = "sketchA"; Kind = "angle"; ClickedRefs = [ RefLine "line_a"; RefLine "line_b" ] }

    let acuteState =
        SketchAuthoring.availabilityForSelection doc true "none" (Some "angle") [] (Some(3.0, 1.0)) draft None
    let obtuseState =
        SketchAuthoring.availabilityForSelection doc true "none" (Some "angle") [] (Some(-3.0, 1.0)) draft None

    let angleValue =
        function
        | Some { Constraint = Angle(_, _, _, _, _, _, degrees, _, _, _, None) } -> degrees
        | other -> failwithf "Expected pending Angle, got %A" other

    let acute = angleValue acuteState.PendingConstraintPlacement
    let obtuse = angleValue obtuseState.PendingConstraintPlacement

    Assert.True(acute < 90.0)
    Assert.True(obtuse > 90.0)

[<Fact>]
let ``Deleting a selected sketch line removes dependent constraints`` () =
    let doc = Document.defaultDocument () |> Document.select "sketch1"
    let sketch =
        match doc.Actions |> List.find (fun a -> a.Id = "sketch1") with
        | { Kind = Sketch(_, _, sketch) } -> sketch
        | _ -> failwith "Expected sketch1 to be a sketch"

    let next = SketchAuthoring.deleteTargets [ TargetLine("sketch1", "l_bottom") ] sketch

    Assert.False(
        next.Entities
        |> List.exists (function
            | RELine("l_bottom", _, _) -> true
            | _ -> false))
    Assert.False(
        next.Constraints
        |> List.exists (function
            | Distance("p_bl", "p_br", _, _) -> true
            | Horizontal("p_bl", "p_br") -> true
            | _ -> false))

[<Fact>]
let ``Deleting two sketch lines also removes their now-unused endpoint points`` () =
    let doc = Document.defaultDocument () |> Document.select "sketch1"
    let sketch =
        match doc.Actions |> List.find (fun a -> a.Id = "sketch1") with
        | { Kind = Sketch(_, _, sketch) } -> sketch
        | _ -> failwith "Expected sketch1 to be a sketch"

    let next = SketchAuthoring.deleteTargets [ TargetLine("sketch1", "l_bottom"); TargetLine("sketch1", "l_left") ] sketch

    Assert.False(
        next.Entities
        |> List.exists (function
            | REPoint("p_bl", _, _) -> true
            | _ -> false))

[<Fact>]
let ``Distance draft with one clicked line yields immediate endpoint distance preview`` () =
    let doc = Document.defaultDocument () |> Document.select "sketch1"
    let draft = Some { SketchId = "sketch1"; Kind = "distance"; ClickedRefs = [ RefLine "l_bottom" ] }
    let state =
        SketchAuthoring.availabilityForSelection doc true "none" (Some "distance") [] (Some(8.0, -2.0)) draft None

    match state.PendingConstraintPlacement with
    | Some pending ->
        match pending.Constraint with
        | Distance(a, b, distance, None) ->
            Assert.Equal("p_bl", a)
            Assert.Equal("p_br", b)
            Assert.True(distance > 0.0)
        | other ->
            failwithf "Expected immediate line length Distance preview, got %A" other
    | None ->
        failwith "Expected pending distance placement from one clicked line"

[<Fact>]
let ``Distance draft with one clicked arc yields diameter preview`` () =
    let doc = arcDoc ()
    let draft = Some { SketchId = "sketchArc"; Kind = "distance"; ClickedRefs = [ RefArc "arc1" ] }
    let state =
        SketchAuthoring.availabilityForSelection doc true "none" (Some "distance") [] (Some(6.0, 6.0)) draft None

    match state.PendingConstraintPlacement with
    | Some pending ->
        match pending.Constraint with
        | CircleDiameter("arc1", "c", diameter, None) ->
            Assert.True(diameter > 19.9 && diameter < 20.1)
        | other ->
            failwithf "Expected arc diameter preview, got %A" other
    | None ->
        failwith "Expected pending arc diameter placement"

[<Fact>]
let ``Tangent constraint can be built from one line and one arc`` () =
    let doc = tangentArcDoc ()
    let next =
        SketchAuthoring.addConstraintFromSelection doc [ TargetLine("sketchT", "line1"); TargetArc("sketchT", "arc1") ] "Tangent"
        |> Option.defaultWith (fun () -> failwith "Expected tangent constraint to be added")
    let sketch =
        match next.Actions |> List.find (fun a -> a.Id = "sketchT") with
        | { Kind = Sketch(_, _, sketch) } -> sketch
        | _ -> failwith "Expected sketchT to be a sketch"

    match List.last sketch.Constraints with
    | Tangent("l1", "l2", "c", "arc1", "line1", radius) ->
        Assert.True(radius > 9.9 && radius < 10.1)
    | other ->
        failwithf "Expected line-arc Tangent, got %A" other

[<Fact>]
let ``Curve tangent can be built from one circle and one arc`` () =
    let doc = curveTangentDoc ()
    let next =
        SketchAuthoring.addConstraintFromSelection doc [ TargetCircle("sketchCT", "circle1"); TargetArc("sketchCT", "arc1") ] "Tangent"
        |> Option.defaultWith (fun () -> failwith "Expected curve tangent constraint to be added")
    let sketch =
        match next.Actions |> List.find (fun a -> a.Id = "sketchCT") with
        | { Kind = Sketch(_, _, sketch) } -> sketch
        | _ -> failwith "Expected sketchCT to be a sketch"

    match List.last sketch.Constraints with
    | CurveTangent("circle1", "c0", "arc1", "c1", false) -> ()
    | other ->
        failwithf "Expected circle-arc CurveTangent, got %A" other

[<Fact>]
let ``Rectangle tool adds orthogonal constraints`` () =
    let sketch = ActionSketch.empty
    let next =
        SketchAuthoring.applyToolClick "rectangle" [ { X = 0.0; Y = 0.0 }; { X = 10.0; Y = 5.0 } ] sketch
        |> Option.defaultWith (fun () -> failwith "Expected rectangle to be created")

    let lineCount =
        next.Entities
        |> List.filter (function | RELine _ -> true | _ -> false)
        |> List.length
    let horizontalCount =
        next.Constraints
        |> List.filter (function | Horizontal _ -> true | _ -> false)
        |> List.length
    let verticalCount =
        next.Constraints
        |> List.filter (function | Vertical _ -> true | _ -> false)
        |> List.length
    let perpendicularCount =
        next.Constraints
        |> List.filter (function | Perpendicular _ -> true | _ -> false)
        |> List.length

    Assert.Equal(4, lineCount)
    Assert.Equal(2, horizontalCount)
    Assert.Equal(2, verticalCount)
    Assert.Equal(4, perpendicularCount)

[<Fact>]
let ``Rounded rectangle tool creates arcs and tangent constraints`` () =
    let sketch = ActionSketch.empty
    let next =
        SketchAuthoring.applyToolClick "roundedRectangle" [ { X = 0.0; Y = 0.0 }; { X = 10.0; Y = 5.0 } ] sketch
        |> Option.defaultWith (fun () -> failwith "Expected rounded rectangle to be created")

    let arcCount =
        next.Entities
        |> List.filter (function | REArc _ -> true | _ -> false)
        |> List.length
    let lineCount =
        next.Entities
        |> List.filter (function | RELine _ -> true | _ -> false)
        |> List.length
    let tangentCount =
        next.Constraints
        |> List.filter (function | Tangent _ -> true | _ -> false)
        |> List.length
    let equalRadiusCount =
        next.Constraints
        |> List.filter (function | EqualRadius _ -> true | _ -> false)
        |> List.length

    Assert.Equal(4, arcCount)
    Assert.Equal(4, lineCount)
    Assert.Equal(8, tangentCount)
    Assert.Equal(3, equalRadiusCount)

[<Fact>]
let ``Point and frame origin can build a frame coincident constraint`` () =
    let doc = frameConstraintDoc ()
    let next =
        SketchAuthoring.addConstraintFromSelection doc [ TargetPoint("sketchF", "p0"); TargetFrameOrigin("f1") ] "Coincident"
        |> Option.defaultWith (fun () -> failwith "Expected frame coincident constraint to be added")
    let sketch =
        match next.Actions |> List.find (fun a -> a.Id = "sketchF") with
        | { Kind = Sketch(_, _, sketch) } -> sketch
        | _ -> failwith "Expected sketchF to be a sketch"

    match List.last sketch.Constraints with
    | FrameCoincident("p0", "f1", "origin") -> ()
    | other -> failwithf "Expected FrameCoincident, got %A" other

[<Fact>]
let ``Line and frame axis can build a frame parallel constraint`` () =
    let doc = frameConstraintDoc ()
    let next =
        SketchAuthoring.addConstraintFromSelection doc [ TargetLine("sketchF", "l1"); TargetFrameAxis("f1", "xAxis") ] "Parallel"
        |> Option.defaultWith (fun () -> failwith "Expected frame parallel constraint to be added")
    let sketch =
        match next.Actions |> List.find (fun a -> a.Id = "sketchF") with
        | { Kind = Sketch(_, _, sketch) } -> sketch
        | _ -> failwith "Expected sketchF to be a sketch"

    match List.last sketch.Constraints with
    | FrameParallel("p0", "p1", "l1", "f1", "xAxis") -> ()
    | other -> failwithf "Expected FrameParallel, got %A" other

[<Fact>]
let ``Distance draft with clicked line and hovered frame origin yields frame origin line distance`` () =
    let doc = frameConstraintDoc ()
    let draft = Some { SketchId = "sketchF"; Kind = "distance"; ClickedRefs = [ RefLine "l1" ] }
    let state =
        SketchAuthoring.availabilityForSelection
            doc true "none" (Some "distance") []
            None
            draft
            (Some(TargetFrameOrigin("f1")))

    match state.PendingConstraintPlacement with
    | Some pending ->
        match pending.Constraint with
        | FrameLineDistance("l1", "p0", "p1", "f1", "origin", _, None) -> ()
        | other -> failwithf "Expected FrameLineDistance to frame origin, got %A" other
    | None ->
        failwith "Expected pending frame line distance placement"

[<Fact>]
let ``Distance from selection with line and frame origin uses frame origin`` () =
    let doc = frameConstraintDoc ()
    let next =
        SketchAuthoring.addConstraintFromSelection doc [ TargetLine("sketchF", "l1"); TargetFrameOrigin("f1") ] "distance"
        |> Option.defaultWith (fun () -> failwith "Expected frame line distance constraint to be added")
    let sketch =
        match next.Actions |> List.find (fun a -> a.Id = "sketchF") with
        | { Kind = Sketch(_, _, sketch) } -> sketch
        | _ -> failwith "Expected sketchF to be a sketch"

    match List.last sketch.Constraints with
    | FrameLineDistance("l1", "p0", "p1", "f1", "origin", _, None) -> ()
    | other -> failwithf "Expected FrameLineDistance to frame origin, got %A" other

[<Fact>]
let ``Distance draft with clicked point and hovered frame origin yields frame distance`` () =
    let doc = frameConstraintDoc ()
    let draft = Some { SketchId = "sketchF"; Kind = "distance"; ClickedRefs = [ RefPoint "p0" ] }
    let state =
        SketchAuthoring.availabilityForSelection
            doc true "none" (Some "distance") []
            None
            draft
            (Some(TargetFrameOrigin("f1")))

    match state.PendingConstraintPlacement with
    | Some pending ->
        match pending.Constraint with
        | FrameDistance("p0", "f1", "origin", _, None) -> ()
        | other -> failwithf "Expected FrameDistance, got %A" other
    | None ->
        failwith "Expected pending frame distance placement"
