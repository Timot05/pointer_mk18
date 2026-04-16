module EditorTests

open Xunit
open Server
open Server.Editor

let updateMany messages state =
    messages |> List.fold (fun current message -> Editor.update message current) state

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
            [ Editor.msgSelectAction "sketch1"
              Editor.msgToggleSketchEdit
              Editor.msgSetSketchTool "line"
              Editor.msgToggleConstraintPlacement "distance" ]

    Assert.Equal(Some "sketch1", state.Doc.SelectedId)
    Assert.True(state.SketchEditMode)
    Assert.Equal("none", state.SketchTool)
    Assert.Equal(Some "distance", state.ConstraintPlacementMode)
    Assert.Empty(state.SketchToolPoints)

[<Fact>]
let ``Delete intent is a no-op in sketch edit mode with no selected sketch targets`` () =
    let before =
        Editor.initState ()
        |> updateMany
            [ Editor.msgSelectAction "sketch1"
              Editor.msgToggleSketchEdit ]

    let after = Editor.update Editor.msgDeleteIntent before

    Assert.Equal(before.Doc, after.Doc)
    Assert.Equal(before.Compiled, after.Compiled)
    Assert.True(after.SketchEditMode)
    Assert.Equal(Some "sketch1", after.Doc.SelectedId)

[<Fact>]
let ``Frame pick during sketch edit keeps the active sketch selected`` () =
    let before =
        Editor.initState ()
        |> updateMany
            [ Editor.msgSelectAction "sketch1"
              Editor.msgToggleSketchEdit ]

    let framePickId =
        before.Compiled.Pickables
        |> List.find (function | PickFrameOrigin(_, "origin") -> true | _ -> false)
        |> Pickable.pickId

    let after =
        Editor.update (Editor.msgViewerPick "replace" [ { PickId = framePickId; Score = 0.0f } ]) before

    Assert.Equal(Some "sketch1", after.Doc.SelectedId)
    Assert.Contains(TargetFrameOrigin "origin", after.SelectedTargets)

[<Fact>]
let ``Clear model resets editor transient state and leaves only origin`` () =
    let dirty =
        Editor.initState ()
        |> updateMany
            [ Editor.msgSelectAction "sketch1"
              Editor.msgToggleSketchEdit
              Editor.msgSetSketchTool "line"
              Editor.msgSetSelectedTargets [ TargetLine("sketch1", "l_bottom") ]
              Editor.msgSetConstraintPlacementCursor (Some("sketch1", { X = 3.0; Y = 4.0 })) ]

    let cleared = Editor.update Editor.msgClearModel dirty

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
            [ Editor.msgSelectAction "sketch1"
              Editor.msgToggleSketchEdit ]

    let document = Editor.documentView state
    let viewerModel = Editor.viewerModel state
    let viewerState = Editor.viewerState state

    Assert.Equal(Some "sketch1", document.SelectedId)
    Assert.True(document.SketchUi.EditMode)
    Assert.Contains(viewerModel.Sketches, fun sketch -> sketch.Id = "sketch1")
    Assert.Contains(viewerState.SketchFrames, fun frame -> frame.Id = "sketch1")
    Assert.Equal(Some "sketch1", viewerState.SelectedId)
    Assert.True(viewerState.SketchUi.EditMode)
