namespace Server

// ---------------------------------------------------------------------------
// ViewerPipeline — projects EditorState into the model the 3D viewer
// renders. Pure consumer of Editor; never mutates state.
// ---------------------------------------------------------------------------

type FrameView =
    { Id: string
      Transform: RigidTransform }

type ConstraintLabelPositionView =
    { SketchId: string
      ConstraintIndex: int
      Position: LabelPos }

type DisplayStateView =
    { Display: DisplaySettings
      FieldSlice: FieldSliceSettings }

type FieldSliceView =
    { SurfaceIndex: int
      PlaneOrigin: Vec3
      PlaneX: Vec3
      PlaneY: Vec3
      Extent: float }

type ViewerSketchView =
    { Id: string
      Origin: string option
      Transform: RigidTransform
      Sketch: ActionSketch
      Graph: Graph
      Loops: SketchLoopView list }

type ViewerModel =
    { Surfaces: FieldSurface list
      FieldWgsl: string option
      FieldSliceWgsl: string option
      FieldSurfaceActionIds: string list
      Sketches: ViewerSketchView list
      NumSlots: int
      SlotIndex: {| ActionId: string; Path: string; Slot: int |} list
      Pickables: Pickable list }

type ViewerState =
    { Params: float array
      SelectedId: string option
      HoveredTarget: SelectionTarget option
      HighlightedTarget: SelectionTarget option
      DragTarget: SelectionTarget option
      SelectedTargets: SelectionTarget list
      HighlightedTargets: SelectionTarget list
      VisibleDimensionSketchIds: string list
      SketchUi: SketchUiState
      Frames: FrameView list
      SketchEditFrames: FrameView list
      SketchOriginFrames: FrameView list
      FieldSlices: FieldSliceView list
      Visible: Map<string, bool>
      ConstraintLabelPositions: ConstraintLabelPositionView list
      Display: Map<string, DisplayStateView>
      Errors: ActionErrorView list }

module ViewerPipeline =

    /// The three orthonormal axes for a cut-plane slice, given a plane key.
    let private localSliceBasis plane =
        match plane with
        | "X" -> { X = 0.0; Y = 1.0; Z = 0.0 }, { X = 0.0; Y = 0.0; Z = 1.0 }, { X = 1.0; Y = 0.0; Z = 0.0 }
        | "Y" -> { X = 1.0; Y = 0.0; Z = 0.0 }, { X = 0.0; Y = 0.0; Z = 1.0 }, { X = 0.0; Y = 1.0; Z = 0.0 }
        | _ -> { X = 1.0; Y = 0.0; Z = 0.0 }, { X = 0.0; Y = 1.0; Z = 0.0 }, { X = 0.0; Y = 0.0; Z = 1.0 }

    /// Walks down a FieldNode accumulating its leading rigid transforms so
    /// the field slice can be rendered in world space.
    let rec private leadingFieldTransform (state: EditorState) (field: FieldNode) (acc: RigidTransform) =
        let slot (s: Slot) = state.Compiled.Slots.Values.[s]
        match field with
        | FTranslate(x, y, z, child) ->
            let step = RigidTransform.translate { X = slot x; Y = slot y; Z = slot z }
            leadingFieldTransform state child (acc * step)
        | FRotate(ax, ay, az, angle, child) ->
            let step = RigidTransform.fromAxisAngle { X = slot ax; Y = slot ay; Z = slot az } (slot angle)
            leadingFieldTransform state child (acc * step)
        | FFieldOp(_, _, child) ->
            leadingFieldTransform state child acc
        | _ ->
            acc

    /// Per-surface field-slice placement (origin + basis) for every visible
    /// surface with a slice enabled.
    let private activeFieldSlices (state: EditorState) : FieldSliceView list =
        let surfaceIndexByAction =
            state.Compiled.Surfaces
            |> List.mapi (fun index surface -> surface.ActionId, (index, surface.Field))
            |> Map.ofList

        state.Doc.Actions
        |> List.choose (fun action ->
            let fs = action.FieldSlice |> Option.defaultValue FieldSliceSettings.defaults
            if not action.Visible || not fs.Enabled then
                None
            else
                match Map.tryFind action.Id surfaceIndexByAction with
                | None -> None
                | Some(surfaceIndex, field) ->
                    let frame = leadingFieldTransform state field RigidTransform.Identity
                    let localX, localY, localN = localSliceBasis fs.Plane
                    let planeX = frame.Rot.Rotate(localX)
                    let planeY = frame.Rot.Rotate(localY)
                    let planeN = frame.Rot.Rotate(localN)
                    let origin = frame.Trans + fs.Offset * planeN
                    Some
                        { SurfaceIndex = surfaceIndex
                          PlaneOrigin = origin
                          PlaneX = planeX
                          PlaneY = planeY
                          Extent = fs.Extent })

    /// Frame actions that appear before the active sketch, with transforms
    /// resolved for rendering. Editor exposes the ids only; this rebuilds
    /// the full FrameView the viewer needs.
    let private sketchEditFrames (state: EditorState) : FrameView list =
        Editor.sketchEditFrameIds state
        |> Set.toList
        |> List.choose (fun id ->
            Map.tryFind id state.Compiled.Frames
            |> Option.map (fun t -> { Id = id; Transform = t }))

    let viewerModel (state: EditorState) : ViewerModel =
        let indexList =
            state.Compiled.Slots.Index
            |> Map.toList
            |> List.map (fun (r, s) -> {| ActionId = r.ActionId; Path = r.Path; Slot = s |})
        let sketches =
            state.Doc.Actions
            |> List.choose (fun a ->
                match a.Kind with
                | Sketch(origin, plane, sk) ->
                    let sketchOrigin = Editor.resolveSketchTransform state origin plane
                    let ctx: SketchCompileContext =
                        { SketchOrigin = sketchOrigin; Frames = state.Compiled.Frames }
                    let graph = SketchCompile.compile sk ctx
                    let loops =
                        SketchLoops.detectLoops sk.Entities
                        |> List.map (fun l -> { Id = l.Id; EntityIds = l.EntityIds })
                    Some
                        { Id = a.Id
                          Origin = origin
                          Transform = sketchOrigin
                          Sketch = sk
                          Graph = graph
                          Loops = loops }
                | _ -> None)
        { Surfaces = state.Compiled.Surfaces
          FieldWgsl = GpuIsosurface.combinedIsosurfaceWgsl state.Compiled.Surfaces
          FieldSliceWgsl = GpuFieldSlice.combinedFieldSliceWgsl state.Compiled.Surfaces
          FieldSurfaceActionIds = state.Compiled.Surfaces |> List.map (fun s -> s.ActionId)
          Sketches = sketches
          NumSlots = state.Compiled.Slots.Values.Length
          SlotIndex = indexList
          Pickables = state.Compiled.Pickables }

    let viewerState (state: EditorState) : ViewerState =
        let isDraggable =
            function
            | TargetPoint _ | TargetDimension _ as t -> Editor.belongsToActiveSketch state t
            | _ -> false
        let dragTarget = state.HoveredTarget |> Option.filter isDraggable

        let frameHighlightAllowed = state.ConstraintPlacementMode <> Some AnglePlacement
        let highlightedTargetAllowed target =
            match target with
            | TargetSurface _ -> true
            | TargetFrameAxis _ -> false
            | TargetFrameOrigin _ -> frameHighlightAllowed && Editor.belongsToActiveSketch state target
            | _ -> Editor.belongsToActiveSketch state target

        let visibleDimensionSketchIds =
            match state.SketchEditMode, state.Doc.SelectedId with
            | true, Some selectedId ->
                match state.Doc.Actions |> List.tryFind (fun a -> a.Id = selectedId) with
                | Some { Kind = Sketch _ } -> [ selectedId ]
                | _ -> []
            | _ -> []

        let displayByAction =
            state.Doc.Actions
            |> List.choose (fun a ->
                match Map.tryFind a.Id state.Compiled.TypeMap with
                | Some FieldType.Field ->
                    let d = a.Display |> Option.defaultValue DisplaySettings.defaults
                    let fs = a.FieldSlice |> Option.defaultValue FieldSliceSettings.defaults
                    Some(a.Id, { Display = d; FieldSlice = fs })
                | _ -> None)
            |> Map.ofList

        let frames =
            state.Doc.Actions
            |> List.choose (fun a ->
                match Map.tryFind a.Id state.Compiled.TypeMap with
                | Some FieldType.Frame ->
                    Map.tryFind a.Id state.Compiled.Frames
                    |> Option.map (fun t -> { Id = a.Id; Transform = t })
                | _ -> None)

        let sketchOriginFrames =
            state.Doc.Actions
            |> List.choose (fun a ->
                match a.Kind with
                | Sketch(origin, plane, _) ->
                    Some { Id = a.Id; Transform = Editor.resolveSketchTransform state origin plane }
                | _ -> None)

        let visibleByAction =
            state.Doc.Actions |> List.map (fun a -> a.Id, a.Visible) |> Map.ofList

        let constraintLabelPositions =
            state.Doc.Actions
            |> List.collect (fun a ->
                match a.Kind with
                | Sketch(_, _, sk) ->
                    sk.Constraints
                    |> List.mapi (fun i c ->
                        SketchConstraint.labelPos c
                        |> Option.map (fun pos -> { SketchId = a.Id; ConstraintIndex = i; Position = pos }))
                    |> List.choose id
                | _ -> [])

        { Params = state.Compiled.Slots.Values
          SelectedId = state.Doc.SelectedId
          HoveredTarget = state.HoveredTarget
          HighlightedTarget = state.HoveredTarget |> Option.filter highlightedTargetAllowed
          DragTarget = dragTarget
          SelectedTargets = state.SelectedTargets
          HighlightedTargets = state.SelectedTargets |> List.filter highlightedTargetAllowed
          VisibleDimensionSketchIds = visibleDimensionSketchIds
          SketchUi = Editor.sketchUiState state
          Frames = frames
          SketchEditFrames = sketchEditFrames state
          SketchOriginFrames = sketchOriginFrames
          FieldSlices = activeFieldSlices state
          Visible = visibleByAction
          ConstraintLabelPositions = constraintLabelPositions
          Display = displayByAction
          Errors = Editor.formatErrors state.Compiled.Errors }
