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
          { Id = "sketchA"; Name = None; Kind = Sketch(Some "origin", sketch); Visible = true; Display = None; FieldSlice = None } ] }

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
          { Id = "sketchArc"; Name = None; Kind = Sketch(Some "origin", sketch); Visible = true; Display = None; FieldSlice = None } ] }

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
        | { Kind = Sketch(_, sketch) } -> sketch
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
        | { Kind = Sketch(_, sketch) } -> sketch
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
