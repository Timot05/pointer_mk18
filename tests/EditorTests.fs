module EditorTests

open Xunit
open Server
open Server.Editor

let updateMany messages state =
    messages |> List.fold (fun current message -> Editor.update message current |> fst) state

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
    Assert.Contains(viewerState.SketchOriginFrames, fun frame -> frame.Id = "sketch1")
    Assert.Equal(Some "sketch1", viewerState.SelectedId)
    Assert.True(viewerState.SketchUi.EditMode)

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
    Assert.Equal(RunSketchSolve { drag with Target = { X = 3.0; Y = 4.0 } }, List.head finishEffects)
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
