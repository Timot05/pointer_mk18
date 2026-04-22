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
      Eyes = []
      Actions =
        [ { Id = "origin"; Name = None; Kind = Origin }
          { Id = "sketchA"; Name = None; Kind = Sketch(Some "origin", XY, sketch) } ] }

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
      Eyes = []
      Actions =
        [ { Id = "origin"; Name = None; Kind = Origin }
          { Id = "sketchArc"; Name = None; Kind = Sketch(Some "origin", XY, sketch) } ] }

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
      Eyes = []
      Actions =
        [ { Id = "origin"; Name = None; Kind = Origin }
          { Id = "sketchT"; Name = None; Kind = Sketch(Some "origin", XY, sketch) } ] }

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
      Eyes = []
      Actions =
        [ { Id = "origin"; Name = None; Kind = Origin }
          { Id = "sketchCT"; Name = None; Kind = Sketch(Some "origin", XY, sketch) } ] }

let frameConstraintDoc () =
    let sketch =
        { Entities =
            [ REPoint("p0", 0.0, 0.0)
              REPoint("p1", 10.0, 0.0)
              RELine("l1", "p0", "p1") ]
          Constraints = [] }
    { Name = "frame-constraints"
      SelectedId = Some "sketchF"
      Eyes = []
      Actions =
        [ { Id = "origin"; Name = None; Kind = Origin }
          { Id = "f1"; Name = None; Kind = Translate(Some "origin", 5.0, 0.0, 0.0) }
          { Id = "sketchF"; Name = None; Kind = Sketch(Some "origin", XY, sketch) } ] }

let pendingPlacementDoc () =
    let sketch =
        { Entities =
            [ REPoint("p0", 0.0, 0.0)
              REPoint("p1", 10.0, 0.0) ]
          Constraints = [] }
    { Name = "pending-placement"
      SelectedId = Some "sketchP"
      Eyes = []
      Actions =
        [ { Id = "origin"; Name = None; Kind = Origin }
          { Id = "sketchP"; Name = None; Kind = Sketch(Some "origin", XY, sketch) } ] }

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
        | Angle(_, _, _, _, lineA, lineB, angle, _, _, _, None) ->
            Assert.Equal("l_bottom", lineA)
            Assert.Equal("l_right", lineB)
            Assert.True(angle > 0.0)
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
        | Some { Constraint = Angle(_, _, _, _, _, _, angle, _, _, _, None) } -> angle
        | other -> failwithf "Expected pending Angle, got %A" other

    let acute = angleValue acuteState.PendingConstraintPlacement
    let obtuse = angleValue obtuseState.PendingConstraintPlacement

    Assert.True(acute < System.Math.PI * 0.5)
    Assert.True(obtuse > System.Math.PI * 0.5)

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
let ``removeConstraintAt removes exactly the indexed constraint`` () =
    let sketch =
        { Entities = []
          Constraints =
            [ Horizontal("a", "b")
              Vertical("c", "d")
              Coincident("e", "f") ] }

    let next = SketchAuthoring.removeConstraintAt 1 sketch

    Assert.Equal<SketchConstraint list>(
        [ Horizontal("a", "b")
          Coincident("e", "f") ],
        next.Constraints)

[<Fact>]
let ``tryEditableDimension recognizes distance diameter and angle constraints`` () =
    let sketch =
        { Entities = []
          Constraints =
            [ Distance("p0", "p1", 12.0, None)
              CircleDiameter("c0", "center0", 8.0, None)
              Angle("a0", "a1", "b0", "b1", "la", "lb", System.Math.PI * 0.25, false, false, true, None) ] }

    let distance = SketchAuthoring.tryEditableDimension "sketchX" sketch 0
    let diameter = SketchAuthoring.tryEditableDimension "sketchX" sketch 1
    let angle = SketchAuthoring.tryEditableDimension "sketchX" sketch 2

    Assert.Equal(Some { SketchId = "sketchX"; ConstraintIndex = 0; Key = "distance"; Value = 12.0 }, distance)
    Assert.Equal(Some { SketchId = "sketchX"; ConstraintIndex = 1; Key = "diameter"; Value = 8.0 }, diameter)
    Assert.Equal(Some { SketchId = "sketchX"; ConstraintIndex = 2; Key = "angle"; Value = System.Math.PI * 0.25 }, angle)

[<Fact>]
let ``updatePlacementDraft upgrades clicked line with hovered frame origin in distance mode`` () =
    let draft = Some { SketchId = "sketchF"; Kind = "distance"; ClickedRefs = [ RefLine "l1" ] }

    let next =
        SketchAuthoring.updatePlacementDraft
            "sketchF"
            "distance"
            (Some(TargetFrameOrigin("f1")))
            draft

    Assert.Equal(
        Some { SketchId = "sketchF"; Kind = "distance"; ClickedRefs = [ RefLine "l1"; RefFrameOrigin "f1" ] },
        next)

[<Fact>]
let ``placePendingConstraint writes the chosen label position onto the committed constraint`` () =
    let doc = pendingPlacementDoc ()
    let pending =
        { SketchId = "sketchP"
          Constraint = Distance("p0", "p1", 10.0, None) }

    let next =
        SketchAuthoring.placePendingConstraint doc pending { X = 3.0; Y = 4.0 }
        |> Option.defaultWith (fun () -> failwith "Expected pending constraint to be committed")

    let sketch =
        match next.Actions |> List.find (fun a -> a.Id = "sketchP") with
        | { Kind = Sketch(_, _, sketch) } -> sketch
        | _ -> failwith "Expected sketchP to be a sketch"

    match List.last sketch.Constraints with
    | Distance("p0", "p1", 10.0, Some { X = 3.0; Y = 4.0 }) -> ()
    | other -> failwithf "Expected committed labeled Distance, got %A" other

[<Fact>]
let ``requiredToolPoints matches the supported sketch tools`` () =
    Assert.Equal(2, SketchAuthoring.requiredToolPoints "line")
    Assert.Equal(2, SketchAuthoring.requiredToolPoints "rectangle")
    Assert.Equal(2, SketchAuthoring.requiredToolPoints "roundedRectangle")
    Assert.Equal(2, SketchAuthoring.requiredToolPoints "circle")
    Assert.Equal(3, SketchAuthoring.requiredToolPoints "arc")
    Assert.Equal(0, SketchAuthoring.requiredToolPoints "none")

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
        SketchAuthoring.applyToolClick "rectangle" [ { X = 0.0; Y = 0.0 }; { X = 10.0; Y = 5.0 } ] [] sketch None
        |> Option.defaultWith (fun () -> failwith "Expected rectangle to be created")
        |> fun result -> result.Sketch

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
let ``Line tool continuation uses snapped point coordinates when reusing an existing endpoint`` () =
    let sketch =
        { ActionSketch.empty with
            Entities = [ REPoint("p_existing", 25.0, 15.0) ] }

    let result =
        SketchAuthoring.applyToolClick
            "line"
            [ { X = 0.0; Y = 0.0 }; { X = 24.2; Y = 14.8 } ]
            [ None; Some "p_existing" ]
            sketch
            None
        |> Option.defaultWith (fun () -> failwith "Expected line to be created")

    let createdLine =
        result.Sketch.Entities
        |> List.rev
        |> List.tryPick (function
            | RELine(_, startId, endId) -> Some(startId, endId)
            | _ -> None)
        |> Option.defaultWith (fun () -> failwith "Expected a line entity")

    Assert.Equal("p_existing", snd createdLine)

    match result.ContinueFrom with
    | Some(pointId, point) ->
        Assert.Equal("p_existing", pointId)
        Assert.Equal(25.0, point.X, 6)
        Assert.Equal(15.0, point.Y, 6)
    | None ->
        failwith "Expected line continuation point"

[<Fact>]
let ``Rounded rectangle tool creates arcs and tangent constraints`` () =
    let sketch = ActionSketch.empty
    let next =
        SketchAuthoring.applyToolClick "roundedRectangle" [ { X = 0.0; Y = 0.0 }; { X = 10.0; Y = 5.0 } ] [] sketch None
        |> Option.defaultWith (fun () -> failwith "Expected rounded rectangle to be created")
        |> fun result -> result.Sketch

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

// Frame-axis picking is currently retired — the pick pipeline no longer
// emits `TargetFrameAxis`, so `FrameParallel` / `FramePerpendicular`
// constraints can't be built from selection. The `FrameParallel`
// constraint DU case is preserved for when per-axis picking returns;
// restore this test alongside it.
[<Fact(Skip = "Frame-axis picking retired; per-axis frame constraints unreachable until it returns")>]
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
