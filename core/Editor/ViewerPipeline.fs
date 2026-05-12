namespace Server

// ---------------------------------------------------------------------------
// ViewerPipeline — projects EditorState into the model the 3D viewer
// renders. Notebook-mode only. Sketch authoring on SketchBlocks is the
// only live path; the action-graph FieldSurface / Frame chain / field
// slice render were retired with the action graph.
// ---------------------------------------------------------------------------

type FrameView =
    { Id: string
      Transform: RigidTransform }

type ConstraintLabelPositionView =
    { SketchId: string
      ConstraintIndex: int
      Position: LabelPos }

type ViewerSketchView =
    { Id: string
      Origin: string option
      Sketch: ActionSketch
      Graph: Graph }

type SketchLoopsStateView =
    { SketchId: string
      Loops: SketchLoopView list }

/// Topology-side viewer projection (slow). Only sketch authoring is
/// live now; field-surface rendering is retired.
type ViewerModel =
    { Sketches: ViewerSketchView list
      NumSlots: int
      SlotIndex:
          {| ActionId: string
             Path: string
             Slot: int |} list
      Pickables: Pickable list }

/// Per-frame viewer projection (fast). Sketch entity coords resolved
/// against the slot table + solver overlay.
type ViewerState =
    { Params: float array
      HoveredTarget: SelectionTarget option
      HighlightedTarget: SelectionTarget option
      DragTarget: SelectionTarget option
      SelectedTargets: SelectionTarget list
      HighlightedTargets: SelectionTarget list
      VisibleDimensionSketchIds: string list
      SketchUi: SketchUiState
      SketchTransforms: FrameView list
      SketchLoops: SketchLoopsStateView list
      ConstraintLabelPositions: ConstraintLabelPositionView list }

module ViewerPipeline =

    let private slotValue (slots: SlotTable) (values: float array) (actionId: string) (path: string) (defaultValue: float) =
        match Map.tryFind { ActionId = actionId; Path = path } slots.Index with
        | Some slot when slot < values.Length -> values.[slot]
        | _ -> defaultValue

    let private resolveSketchEntities (slots: SlotTable) (values: float array) (sketchId: string) (sketch: ActionSketch) : RenderEntity list =
        sketch.Entities
        |> List.map (function
            | REPoint(id, x, y) ->
                REPoint(
                    id,
                    slotValue slots values sketchId (sprintf "sketch.entity.%s.x" id) x,
                    slotValue slots values sketchId (sprintf "sketch.entity.%s.y" id) y
                )
            | RECircle(id, centerId, radius) ->
                RECircle(
                    id,
                    centerId,
                    slotValue slots values sketchId (sprintf "sketch.entity.%s.radius" id) radius
                )
            | REArc(id, startId, endId, ArcThreePoint through) ->
                REArc(
                    id,
                    startId,
                    endId,
                    ArcThreePoint
                        { X = slotValue slots values sketchId (sprintf "sketch.entity.%s.through.x" id) through.X
                          Y = slotValue slots values sketchId (sprintf "sketch.entity.%s.through.y" id) through.Y }
                )
            | other ->
                other)

    let viewerModel (state: EditorState) : ViewerModel =
        let indexList =
            state.Compiled.Slots.Index
            |> Map.toList
            |> List.map (fun (r, s) ->
                {| ActionId = r.ActionId
                   Path = r.Path
                   Slot = s |})

        let blockSketches =
            state.Doc.Blocks
            |> List.choose (fun b ->
                match b.Body with
                | Server.Lang.Notebook.SketchBody data ->
                    let sketchOrigin = Editor.resolveSketchTransform state None data.Plane

                    let ctx: SketchCompileContext =
                        { SketchOrigin = sketchOrigin
                          Frames = Map.empty }

                    let graph = SketchCompile.compile data.Sketch ctx

                    Some
                        { Id = SketchAuthoring.blockSketchId b.Id
                          Origin = None
                          Sketch = data.Sketch
                          Graph = graph }
                | _ -> None)

        { Sketches = blockSketches
          NumSlots = state.Compiled.Slots.Values.Length
          SlotIndex = indexList
          Pickables = state.Compiled.Pickables }

    let viewerState (state: EditorState) : ViewerState =
        let effectiveParams =
            (state.SlotValues, state.Doc.Blocks)
            ||> List.fold (fun current b ->
                match b.Body with
                | Server.Lang.Notebook.SketchBody data ->
                    let sid = SketchAuthoring.blockSketchId b.Id
                    match Map.tryFind sid state.SolvedSketchParams with
                    | Some solvedLocal ->
                        SketchSolve.overlaySolvedSketch current state.Compiled.Slots sid data.Sketch solvedLocal
                    | None -> current
                | _ -> current)

        let blockSketchLoops =
            state.Doc.Blocks
            |> List.choose (fun b ->
                match b.Body with
                | Server.Lang.Notebook.SketchBody data ->
                    let synthId = SketchAuthoring.blockSketchId b.Id
                    let liveEntities = resolveSketchEntities state.Compiled.Slots effectiveParams synthId data.Sketch
                    let loops =
                        SketchLoops.detectLoops liveEntities
                        |> List.map (fun l -> { Id = l.Id; EntityIds = l.EntityIds })
                    Some
                        { SketchId = synthId
                          Loops = loops }
                | _ -> None)

        let isDraggable =
            function
            | TargetPoint _
            | TargetDimension _ as t -> Editor.belongsToActiveSketch state t
            | _ -> false

        let dragTarget = state.HoveredTarget |> Option.filter isDraggable

        let frameHighlightAllowed = state.ConstraintPlacementMode <> Some AnglePlacement

        let highlightedTargetAllowed target =
            match target with
            | TargetFrameOrigin _ -> frameHighlightAllowed && Editor.belongsToActiveSketch state target
            | _ -> Editor.belongsToActiveSketch state target

        let visibleDimensionSketchIds =
            if not state.SketchEditMode then []
            else
                match state.Doc.SelectedBlockId with
                | Some bid ->
                    state.Doc.Blocks
                    |> List.tryFind (fun b -> b.Id = bid)
                    |> Option.bind (fun b ->
                        match b.Body with
                        | Server.Lang.Notebook.SketchBody _ ->
                            Some (SketchAuthoring.blockSketchId bid)
                        | _ -> None)
                    |> Option.toList
                | None -> []

        let blockSketchTransforms =
            state.Doc.Blocks
            |> List.choose (fun b ->
                match b.Body with
                | Server.Lang.Notebook.SketchBody data ->
                    Some
                        { Id = SketchAuthoring.blockSketchId b.Id
                          Transform = Editor.resolveSketchTransform state None data.Plane }
                | _ -> None)

        let constraintLabelPositions =
            state.Doc.Blocks
            |> List.collect (fun b ->
                match b.Body with
                | Server.Lang.Notebook.SketchBody data ->
                    let sid = SketchAuthoring.blockSketchId b.Id
                    data.Sketch.Constraints
                    |> List.mapi (fun i c ->
                        SketchConstraint.labelPos c
                        |> Option.map (fun pos ->
                            { SketchId = sid
                              ConstraintIndex = i
                              Position = pos }))
                    |> List.choose id
                | _ -> [])

        { Params = effectiveParams
          HoveredTarget = state.HoveredTarget
          HighlightedTarget = state.HoveredTarget |> Option.filter highlightedTargetAllowed
          DragTarget = dragTarget
          SelectedTargets = state.SelectedTargets
          HighlightedTargets = state.SelectedTargets |> List.filter highlightedTargetAllowed
          VisibleDimensionSketchIds = visibleDimensionSketchIds
          SketchUi = Editor.sketchUiState state
          SketchTransforms = blockSketchTransforms
          SketchLoops = blockSketchLoops
          ConstraintLabelPositions = constraintLabelPositions }
