module EditorTests

open Xunit
open Server
open Server.Editor

let updateMany messages state =
    messages |> List.fold (fun current message -> Editor.update message current |> fst) state

let slotFor actionId path (state: EditorState) =
    state.Compiled.Slots.Index.[{ ActionId = actionId; Path = path }]

[<Fact>]
let ``Editor init state starts with compiled default document`` () =
    let state = Editor.initState ()

    Assert.Equal("untitled", state.Doc.Name)
    Assert.Equal(Some "origin", state.Doc.SelectedId)
    Assert.NotEmpty(state.Doc.Actions)
    Assert.NotEmpty(state.Compiled.Pickables)
    Assert.False(state.SketchEditMode)
    Assert.Equal("none", state.SketchTool)

[<Fact>]
let ``Sketch tool and constraint placement are normalized through editor update`` () =
    let state =
        Editor.initState ()
        |> updateMany
            [ SelectAction "sketch1"
              ToggleSketchEdit
              SetSketchTool LineTool
              ToggleConstraintPlacement DistancePlacement ]

    Assert.Equal(Some "sketch1", state.Doc.SelectedId)
    Assert.True(state.SketchEditMode)
    Assert.Equal("none", state.SketchTool)
    Assert.Equal(Some DistancePlacement, state.ConstraintPlacementMode)
    Assert.Empty(state.SketchToolPoints)

[<Fact>]
let ``Delete intent is a no-op in sketch edit mode with no selected sketch targets`` () =
    let before =
        Editor.initState ()
        |> updateMany
            [ SelectAction "sketch1"
              ToggleSketchEdit ]

    let after = Editor.update DeleteIntent before |> fst

    Assert.Equal(before.Doc, after.Doc)
    Assert.Equal(before.Compiled, after.Compiled)
    Assert.True(after.SketchEditMode)
    Assert.Equal(Some "sketch1", after.Doc.SelectedId)

[<Fact>]
let ``Frame pick during sketch edit keeps the active sketch selected`` () =
    let before =
        Editor.initState ()
        |> updateMany
            [ SelectAction "sketch1"
              ToggleSketchEdit ]

    let framePickId =
        before.Compiled.Pickables
        |> List.find (function | PickFrameOrigin(_, "origin") -> true | _ -> false)
        |> Pickable.pickId

    let after =
        Editor.update (ViewerPick("replace", [ { PickId = framePickId; Score = 0.0f } ])) before |> fst

    Assert.Equal(Some "sketch1", after.Doc.SelectedId)
    Assert.Contains(TargetFrameOrigin "origin", after.SelectedTargets)

[<Fact>]
let ``Line tool chains segments through a shared endpoint`` () =
    let before =
        Editor.initState ()
        |> updateMany
            [ SelectAction "sketch1"
              ToggleSketchEdit
              SetSketchTool LineTool ]

    let beforeSketch =
        before.Doc.Actions
        |> List.find (fun action -> action.Id = "sketch1")
        |> fun action ->
            match action.Kind with
            | Sketch(_, _, sketch) -> sketch
            | _ -> failwith "Expected sketch1 to be a sketch"

    let countPoints sketch =
        sketch.Entities |> List.filter (function REPoint _ -> true | _ -> false) |> List.length

    let countLines sketch =
        sketch.Entities |> List.filter (function RELine _ -> true | _ -> false) |> List.length

    let after =
        before
        |> updateMany
            [ ViewerToolClick(10.0, 10.0)
              ViewerToolClick(20.0, 10.0)
              ViewerToolClick(30.0, 10.0)
              ViewerToolClick(40.0, 10.0) ]

    let afterSketch =
        after.Doc.Actions
        |> List.find (fun action -> action.Id = "sketch1")
        |> fun action ->
            match action.Kind with
            | Sketch(_, _, sketch) -> sketch
            | _ -> failwith "Expected sketch1 to be a sketch"

    Assert.Equal(countLines beforeSketch + 3, countLines afterSketch)
    Assert.Equal(countPoints beforeSketch + 4, countPoints afterSketch)
    Assert.Equal("line", after.SketchTool)
    Assert.Single(after.SketchToolPoints) |> ignore
    Assert.True(after.LineChainStartPointId.IsSome)

[<Fact>]
let ``First snapped line tool point stores the reused point coordinates`` () =
    let before =
        Editor.initState ()
        |> updateMany
            [ SelectAction "sketch1"
              ToggleSketchEdit
              SetSketchTool LineTool
              SetHoveredTarget (Some(TargetPoint("sketch1", "p_bl"))) ]

    let after = Editor.update (ViewerToolClick(3.2, 4.7)) before |> fst

    Assert.Single(after.SketchToolPoints) |> ignore
    Assert.Single(after.SketchToolPointRefs) |> ignore
    Assert.Equal(Some "p_bl", List.head after.SketchToolPointRefs)

    match after.SketchToolPoints with
    | [ point ] ->
        Assert.Equal(0.0, point.X, 6)
        Assert.Equal(0.0, point.Y, 6)
    | other ->
        failwithf "Expected one staged tool point, got %A" other

[<Fact>]
let ``Clear model resets editor transient state and leaves only origin`` () =
    let dirty =
        Editor.initState ()
        |> updateMany
            [ SelectAction "sketch1"
              ToggleSketchEdit
              SetSketchTool LineTool
              SetSelectedTargets [ TargetLine("sketch1", "l_bottom") ]
              SetConstraintPlacementCursor (Some("sketch1", { X = 3.0; Y = 4.0 })) ]

    let cleared = Editor.update ClearModel dirty |> fst

    Assert.Equal(Some "origin", cleared.Doc.SelectedId)
    Assert.Single(cleared.Doc.Actions) |> ignore
    Assert.False(cleared.SketchEditMode)
    Assert.Equal("none", cleared.SketchTool)
    Assert.Empty(cleared.SelectedTargets)
    Assert.Null(box cleared.ConstraintPlacementMode)
    Assert.Null(box cleared.ConstraintPlacementCursor)

[<Fact>]
let ``Editor selectors expose coherent document and viewer state`` () =
    let state =
        Editor.initState ()
        |> updateMany
            [ SelectAction "sketch1"
              ToggleSketchEdit ]

    let document = DocumentPipeline.documentView state
    let viewerModel = ViewerPipeline.viewerModel state
    let viewerState = ViewerPipeline.viewerState state

    Assert.Equal(Some "sketch1", document.SelectedId)
    Assert.True(document.SketchUi.EditMode)
    Assert.Contains(viewerModel.Sketches, fun sketch -> sketch.Id = "sketch1")
    Assert.Contains(viewerState.SketchTransforms, fun frame -> frame.Id = "sketch1")
    Assert.Equal(Some "sketch1", viewerState.SelectedId)
    Assert.True(viewerState.SketchUi.EditMode)

[<Fact>]
let ``ViewerState sketch loops are computed from live slot values, not unsolved model geometry`` () =
    let openLoopSketch =
        { Entities =
            [ REPoint("p1", 0.0, 0.0)
              REPoint("p2", 10.0, 0.0)
              REPoint("p3", 10.0, 0.0)
              REPoint("p4", 10.0, 10.0)
              REPoint("p5", 10.0, 10.0)
              REPoint("p6", 0.0, 10.0)
              REPoint("p7", 0.0, 10.0)
              REPoint("p8", 0.0, 1.0)
              RELine("l1", "p1", "p2")
              RELine("l2", "p3", "p4")
              RELine("l3", "p5", "p6")
              RELine("l4", "p7", "p8") ]
          Constraints = [] }

    let baseState =
        Editor.initState ()
        |> updateMany [ ReplaceSketch("sketch1", openLoopSketch) ]

    let closedSlots = Array.copy baseState.SlotValues
    closedSlots.[slotFor "sketch1" "sketch.entity.p8.y" baseState] <- 0.0

    let liveState = { baseState with SlotValues = closedSlots }

    let viewerModel = ViewerPipeline.viewerModel liveState
    let viewerState = ViewerPipeline.viewerState liveState

    let modelSketch = viewerModel.Sketches |> List.find (fun sketch -> sketch.Id = "sketch1")
    let stateLoops = viewerState.SketchLoops |> List.find (fun sketch -> sketch.SketchId = "sketch1")

    Assert.Equal("sketch1", modelSketch.Id)
    Assert.Empty(SketchLoops.detectLoops openLoopSketch.Entities)
    Assert.Single(stateLoops.Loops) |> ignore
    let expectedIds = [ "l1"; "l2"; "l3"; "l4" ] |> List.sort
    let actualIds = (List.head stateLoops.Loops).EntityIds |> List.sort
    Assert.Equal(String.concat "," expectedIds, String.concat "," actualIds)

[<Fact>]
let ``Sketch drag messages update editor drag state and emit solve effects`` () =
    let drag =
        { SketchId = "sketch1"
          Kind = DragPoint "p0"
          XField = SketchEntityField("p0", PointX)
          YField = SketchEntityField("p0", PointY)
          Target = { X = 1.0; Y = 2.0 } }

    let started, startEffects = Editor.update (BeginSketchDrag drag) (Editor.initState ())
    let moved, moveEffects = Editor.update (UpdateSketchDragTarget { X = 3.0; Y = 4.0 }) started
    let finished, finishEffects = Editor.update FinishSketchDrag moved

    Assert.Equal(Some drag, started.ActiveSketchDrag)
    Assert.Single(startEffects) |> ignore
    Assert.Equal(RunSketchSolve drag, List.head startEffects)
    Assert.Equal(Some { drag with Target = { X = 3.0; Y = 4.0 } }, moved.ActiveSketchDrag)
    Assert.Single(moveEffects) |> ignore
    Assert.Equal(RunSketchSolve { drag with Target = { X = 3.0; Y = 4.0 } }, List.head moveEffects)
    Assert.Single(finishEffects) |> ignore
    Assert.Equal(FinalizeSketchDrag { drag with Target = { X = 3.0; Y = 4.0 } }, List.head finishEffects)
    Assert.True(finished.PendingSketchDragCommit)
    Assert.Equal(Some { drag with Target = { X = 3.0; Y = 4.0 } }, finished.ActiveSketchDrag)

[<Fact>]
let ``Clear model cancels any active sketch drag`` () =
    let dragging =
        let drag =
            { SketchId = "sketch1"
              Kind = DragConstraintLabel 0
              XField = SketchConstraintField(0, ConstraintLabelX)
              YField = SketchConstraintField(0, ConstraintLabelY)
              Target = { X = 5.0; Y = 6.0 } }

        Editor.initState ()
        |> updateMany
            [ SelectAction "sketch1"
              ToggleSketchEdit
              BeginSketchDrag drag ]

    let cleared = Editor.update ClearModel dragging |> fst

    Assert.True(cleared.ActiveSketchDrag.IsNone)

[<Fact>]
let ``ViewerPlaceConstraint stores a labelPosition on the new distance constraint`` () =
    // Baseline: default sketch1 has two existing Distance constraints (both
    // with labelPosition = None). We place a new one and verify its label
    // position gets stored — otherwise ViewerPipeline.viewerState drops it
    // from ConstraintLabelPositions and the viewer renders no number.
    let baseline =
        Editor.initState ()
        |> updateMany
            [ SelectAction "sketch1"
              ToggleSketchEdit
              ToggleConstraintPlacement DistancePlacement
              SetSelectedTargets
                  [ TargetPoint("sketch1", "p_bl")
                    TargetPoint("sketch1", "p_tr") ] ]

    let countDistances (state: EditorState) =
        state.Doc.Actions
        |> List.tryFind (fun a -> a.Id = "sketch1")
        |> Option.map (fun action ->
            match action.Kind with
            | Sketch(_, _, sk) ->
                sk.Constraints |> List.filter (function Distance _ -> true | _ -> false)
            | _ -> [])
        |> Option.defaultValue []

    let before = countDistances baseline

    let placed =
        Editor.update (ViewerPlaceConstraint(5.5, 3.25)) baseline |> fst

    let after = countDistances placed

    // A new Distance constraint was actually added
    Assert.Equal(before.Length + 1, after.Length)

    // The newly-added one (last in the list, since placePendingConstraint
    // appends) must carry a label position — otherwise viewer drops it
    let newest = List.last after
    match newest with
    | Distance(_, _, _, Some pos) ->
        Assert.Equal(5.5, pos.X, 6)
        Assert.Equal(3.25, pos.Y, 6)
    | Distance(_, _, _, None) ->
        failwith "Distance constraint was placed without a labelPosition — viewer will drop it from ConstraintLabelPositions"
    | other ->
        failwithf "Expected a Distance constraint, got %A" other

[<Fact>]
let ``Applying a matching solved drag result during finish commits sketch params`` () =
    let drag =
        { SketchId = "sketch1"
          Kind = DragPoint "p_bl"
          XField = SketchEntityField("p_bl", PointX)
          YField = SketchEntityField("p_bl", PointY)
          Target = { X = 42.0; Y = 24.0 } }

    let finishing =
        Editor.initState ()
        |> updateMany
            [ SelectAction "sketch1"
              ToggleSketchEdit
              BeginSketchDrag drag
              FinishSketchDrag ]

    let applied =
        Editor.update (ApplySketchSolveResult(drag, [| 42.0f; 24.0f |])) finishing |> fst

    match applied.Doc.Actions |> List.find (fun action -> action.Id = "sketch1") with
    | { Kind = Sketch(_, _, sketch) } ->
        match sketch.Entities |> List.find (function REPoint("p_bl", _, _) -> true | _ -> false) with
        | REPoint(_, x, y) ->
            Assert.Equal(42.0, x, 6)
            Assert.Equal(24.0, y, 6)
        | _ ->
            failwith "expected point p_bl"
    | _ ->
        failwith "expected sketch1 to remain a sketch"

    Assert.True(applied.ActiveSketchDrag.IsNone)
    Assert.False(applied.PendingSketchDragCommit)

[<Fact>]
let ``Slot-backed param edit updates live slot values without recompiling`` () =
    let before = Editor.initState ()
    let radiusSlot = slotFor "cyl1" "radius" before

    let after =
        Editor.update (Editor.setActionParamValue "cyl1" CylinderRadius (VFloat 12.5)) before
        |> fst

    Assert.True(obj.ReferenceEquals(before.Compiled, after.Compiled))
    Assert.Equal(12.5, after.SlotValues.[radiusSlot], 6)

    let viewerState = ViewerPipeline.viewerState after
    Assert.Equal(12.5, viewerState.Params.[radiusSlot], 6)

    match after.Doc.Actions |> List.find (fun action -> action.Id = "cyl1") with
    | { Kind = Cylinder(radius, _) } -> Assert.Equal(12.5, radius, 6)
    | _ -> failwith "expected cyl1 to remain a cylinder"

[<Fact>]
let ``Structural param edit recompiles topology and refreshes slot values`` () =
    let before = Editor.initState ()

    let after =
        Editor.update (Editor.setActionParamValue "frame1" TranslateChild (VString "cyl1")) before
        |> fst

    Assert.False(obj.ReferenceEquals(before.Compiled, after.Compiled))
    Assert.True(after.Compiled.Slots.Values = after.SlotValues)

    match after.Doc.Actions |> List.find (fun action -> action.Id = "frame1") with
    | { Kind = Translate(Some child, _, _, _) } -> Assert.Equal("cyl1", child)
    | _ -> failwith "expected frame1 to remain a translate action"

[<Fact>]
let ``Frame numeric params allocate slots in the compiled slot table`` () =
    let state = Editor.initState ()

    Assert.True(state.Compiled.Slots.Index.ContainsKey { ActionId = "frame1"; Path = "x" })
    Assert.True(state.Compiled.Slots.Index.ContainsKey { ActionId = "frame1"; Path = "y" })
    Assert.True(state.Compiled.Slots.Index.ContainsKey { ActionId = "frame1"; Path = "z" })

[<Fact>]
let ``Frame numeric param edit updates viewer frames without recompiling`` () =
    let before = Editor.initState ()
    let beforeViewer = ViewerPipeline.viewerState before
    let beforeFrame = beforeViewer.Frames |> List.find (fun frame -> frame.Id = "frame1")

    let after =
        Editor.update (Editor.setActionParamValue "frame1" TranslateX (VFloat 25.0)) before
        |> fst

    let afterViewer = ViewerPipeline.viewerState after
    let afterFrame = afterViewer.Frames |> List.find (fun frame -> frame.Id = "frame1")

    Assert.True(obj.ReferenceEquals(before.Compiled, after.Compiled))
    Assert.Equal(18.0, beforeFrame.Transform.Trans.X, 6)
    Assert.Equal(25.0, afterFrame.Transform.Trans.X, 6)

[<Fact>]
let ``Palette commit on final scalar step builds the action`` () =
    let before = Editor.initState ()

    let after =
        before
        |> updateMany
            [ PaletteOpen
              PalettePick "Sphere"
              PaletteSetScalarField("radius", 12.0)
              PaletteCommitScalars ]

    let spheres =
        after.Doc.Actions
        |> List.filter (fun action ->
            match action.Kind with
            | Sphere 12.0 -> true
            | _ -> false)

    Assert.Single(spheres) |> ignore
    Assert.False((DocumentPipeline.paletteView after).IsOpen)

[<Fact>]
let ``Palette-created sketches get fresh ids even when picking the same origin`` () =
    let before = Editor.initState ()

    let after =
        before
        |> updateMany
            [ PaletteOpen
              PalettePick "Sketch"
              PalettePick "origin"
              PaletteOpen
              PalettePick "Sketch"
              PalettePick "origin" ]

    let sketches =
        after.Doc.Actions
        |> List.filter (fun action ->
            match action.Kind with
            | Sketch(Some "origin", _, _) -> true
            | _ -> false)

    Assert.Equal(2, sketches.Length)
    Assert.Equal(2, sketches |> List.map (fun action -> action.Id) |> Set.ofList |> Set.count)

[<Fact>]
let ``Palette-created scalar actions get fresh ids on repeated commit`` () =
    let before = Editor.initState ()

    let after =
        before
        |> updateMany
            [ PaletteOpen
              PalettePick "Sphere"
              PaletteCommitScalars
              PaletteOpen
              PalettePick "Sphere"
              PaletteCommitScalars ]

    let spheres =
        after.Doc.Actions
        |> List.filter (fun action ->
            match action.Kind with
            | Sphere _ -> true
            | _ -> false)

    Assert.Equal(2, spheres.Length)
    Assert.Equal(2, spheres |> List.map (fun action -> action.Id) |> Set.ofList |> Set.count)
