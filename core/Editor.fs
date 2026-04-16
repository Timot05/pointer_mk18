namespace Server

open System
#if !FABLE_COMPILER
open System.Text.Json
open System.Text.Json.Nodes
#endif
open System.Text.Json.Serialization

type EditorState =
    { Doc: Document
      Compiled: PipelineResult
      PaletteSession: PaletteSession
      HoveredTarget: SelectionTarget option
      SelectedTargets: SelectionTarget list
      SketchEditMode: bool
      SketchTool: string
      SketchToolPoints: LabelPos list
      EditingDimension: EditingDimension option
      ConstraintPlacementMode: string option
      ConstraintPlacementDraft: ConstraintPlacementDraft option
      ConstraintPlacementCursor: (string * LabelPos) option }

type SerializedModel =
    { Name: string
      Actions: DocAction list }

type ActionErrorView =
    { ActionId: string
      Key: string
      Error: string }

type SketchLoopView =
    { Id: string
      EntityIds: string list }

type DocumentView =
    { Name: string
      Actions: DocAction list
      SelectedId: string option
      SelectedTargets: SelectionTarget list
      SketchUi: SketchUiState
      RefOptions: Map<string, string list>
      SketchLoops: Map<string, SketchLoopView list>
      Errors: ActionErrorView list }

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

type PickCandidateInput =
    { PickId: int
      Score: float32 }

type Message =
    | SelectAction of string
    | SetHoveredTarget of SelectionTarget option
    | SetSelectedTargets of SelectionTarget list
    | AddDefaultAction of string * string
    | AddAction of DocAction
    | UpdateAction of string * DocAction
    | RemoveAction of string
    | ReorderActions of string list
    | ToggleActionVisible of string
    | ToggleDisplay of string
    | PatchDisplayValue of string * string * ParamValue
    | ToggleFieldSlice of string
    | PatchFieldSliceValue of string * string * ParamValue
    | PatchActionParamValue of string * string * ParamValue
    | DeleteIntent
    | ViewerHover of PickCandidateInput list
    | ViewerPick of string * PickCandidateInput list
    | StartEditingDimension of int
    | CancelEditingDimension
    | CommitEditingDimension of float
    | ViewerDimensionClickTarget
    | ReplaceSketch of string * ActionSketch
    | PatchSketchParams of string * (string * float) list
    | ViewerToolClick of float * float
    | ViewerPlaceConstraint of float * float
    | ToggleSketchEdit
    | SetSketchTool of string
    | ToggleConstraintPlacement of string
    | AddConstraintFromSelection of string
    | DeleteSketchConstraint of int
    | SetConstraintPlacementCursor of (string * LabelPos) option
    | PaletteOpen
    | PaletteSetQuery of string
    | PalettePick of string
    | PaletteSetScalarField of string * float
    | PaletteCommitScalars
    | PaletteFinish of string
    | PaletteBack
    | PaletteClose
    | ReplaceDocument of Document
    | LoadModel of SerializedModel
    | ClearModel

module Editor =

#if !FABLE_COMPILER
    let jsonOpts =
        let o = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
        o.Converters.Add(
            JsonFSharpConverter(
                JsonUnionEncoding.InternalTag ||| JsonUnionEncoding.NamedFields ||| JsonUnionEncoding.UnwrapOption,
                unionTagName = "case",
                unionFieldNamingPolicy = JsonNamingPolicy.CamelCase
            )
        )
        o
#endif

    let initState () =
        let doc = Document.defaultDocument ()
        { Doc = doc
          Compiled = Pipeline.compile doc.Actions
          PaletteSession = Palette.empty
          HoveredTarget = None
          SelectedTargets = []
          SketchEditMode = false
          SketchTool = "none"
          SketchToolPoints = []
          EditingDimension = None
          ConstraintPlacementMode = None
          ConstraintPlacementDraft = None
          ConstraintPlacementCursor = None }

    let sketchPlaneTransform (originFrame: RigidTransform) (plane: SketchPlane) =
        let localRotation =
            match plane with
            | XY -> Quat.Identity
            | XZ ->
                Quat.fromBasis
                    { X = 1.0; Y = 0.0; Z = 0.0 }
                    { X = 0.0; Y = 0.0; Z = 1.0 }
                    { X = 0.0; Y = -1.0; Z = 0.0 }
            | YZ ->
                Quat.fromBasis
                    { X = 0.0; Y = 1.0; Z = 0.0 }
                    { X = 0.0; Y = 0.0; Z = 1.0 }
                    { X = 1.0; Y = 0.0; Z = 0.0 }
        originFrame * { Rot = localRotation; Trans = Vec3.Zero }

    let resolveSketchTransform (state: EditorState) (origin: string option) (plane: SketchPlane) =
        let originFrame =
            origin
            |> Option.bind (fun id -> Map.tryFind id state.Compiled.Frames)
            |> Option.defaultValue RigidTransform.Identity
        sketchPlaneTransform originFrame plane

    /// ID of the sketch currently being edited, if any.
    let activeSketchEditId (state: EditorState) =
        match state.SketchEditMode, state.Doc.SelectedId with
        | true, Some id ->
            state.Doc.Actions
            |> List.tryFind (fun a -> a.Id = id)
            |> Option.bind (fun a -> match a.Kind with Sketch _ -> Some id | _ -> None)
        | _ -> None

    /// Frame actions that appear before the active sketch, with their transforms.
    /// These are the frame origins that sketch can legitimately reference.
    let sketchEditFrames (state: EditorState) =
        match activeSketchEditId state with
        | None -> []
        | Some sketchId ->
            let i = state.Doc.Actions |> List.findIndex (fun a -> a.Id = sketchId)
            state.Doc.Actions
            |> List.take i
            |> List.choose (fun a ->
                match Map.tryFind a.Id state.Compiled.TypeMap with
                | Some FieldType.Frame ->
                    Map.tryFind a.Id state.Compiled.Frames
                    |> Option.map (fun t -> { Id = a.Id; Transform = t })
                | _ -> None)

    /// True when `target` belongs to the actively-edited sketch: either its
    /// own geometry (point/line/circle/arc/loop/dimension) or a frame origin
    /// the sketch is allowed to reference.
    let belongsToActiveSketch (state: EditorState) (target: SelectionTarget) =
        match activeSketchEditId state with
        | None -> false
        | Some sid ->
            match target with
            | TargetPoint(s, _) | TargetLine(s, _) | TargetCircle(s, _)
            | TargetArc(s, _) | TargetLoop(s, _) | TargetDimension(s, _) -> s = sid
            | TargetFrameOrigin f ->
                sketchEditFrames state |> List.exists (fun frame -> frame.Id = f)
            | _ -> false

    let isValidSelectionTarget (state: EditorState) target =
        match target with
        | TargetFrameOrigin _ -> belongsToActiveSketch state target
        | _ -> state.Compiled.Pickables |> List.exists (Pickable.sameTarget target)

    /// When clicking a target in sketch-edit mode, keep the active sketch
    /// selected if the target belongs to it (geometry or allowed frame);
    /// otherwise fall back to whichever action the target normally belongs to.
    let actionSelectionForTarget (state: EditorState) target actionId =
        match activeSketchEditId state with
        | Some sketchId when belongsToActiveSketch state target -> Some sketchId
        | _ -> actionId

    let trySketchContext (state: EditorState) (sketchId: string) =
        state.Doc.Actions
        |> List.tryFind (fun action -> action.Id = sketchId)
        |> Option.bind (fun action ->
            match action.Kind with
            | Sketch(origin, plane, sketch) ->
                Some(sketch, resolveSketchTransform state origin plane)
            | _ -> None)

    let tryPoint2 (sketch: ActionSketch) (pointId: string) =
        sketch.Entities
        |> List.tryPick (function
            | REPoint(id, x, y) when id = pointId -> Some(x, y)
            | _ -> None)

    let tryFrameOrigin2 (state: EditorState) (sketchOrigin: RigidTransform) (frameId: string) =
        Map.tryFind frameId state.Compiled.Frames
        |> Option.map (fun frameT ->
            let local = sketchOrigin.Inverse.Apply frameT.Trans
            local.X, local.Y)

    let tryLine2 (sketch: ActionSketch) (startId: string) (endId: string) =
        match tryPoint2 sketch startId, tryPoint2 sketch endId with
        | Some a, Some b -> Some(a, b)
        | _ -> None

    let localSliceBasis plane =
        match plane with
        | "X" -> { X = 0.0; Y = 1.0; Z = 0.0 }, { X = 0.0; Y = 0.0; Z = 1.0 }, { X = 1.0; Y = 0.0; Z = 0.0 }
        | "Y" -> { X = 1.0; Y = 0.0; Z = 0.0 }, { X = 0.0; Y = 0.0; Z = 1.0 }, { X = 0.0; Y = 1.0; Z = 0.0 }
        | _ -> { X = 1.0; Y = 0.0; Z = 0.0 }, { X = 0.0; Y = 1.0; Z = 0.0 }, { X = 0.0; Y = 0.0; Z = 1.0 }

    let rec leadingFieldTransform (state: EditorState) (field: FieldNode) (acc: RigidTransform) =
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

    let activeFieldSlices (state: EditorState) =
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

    let formatErrors (errs: TypeError list) =
        errs |> List.map (fun e ->
            match e with
            | MissingRef(id, key) -> { ActionId = id; Key = key; Error = "missing" }
            | RefNotFound(id, key, target) -> { ActionId = id; Key = key; Error = $"not found: {target}" }
            | ForwardRef(id, key, target) -> { ActionId = id; Key = key; Error = $"forward ref: {target}" }
            | TypeMismatch(id, key, expected, got) ->
                let exp = expected |> List.map string |> String.concat "|"
                { ActionId = id; Key = key; Error = $"expected {exp}, got {got}" })

    let withResolvedPendingConstraintValue (state: EditorState) (sketchUi: SketchUiState) =
        let resolved =
            sketchUi.PendingConstraintPlacement
            |> Option.bind (fun pending ->
                trySketchContext state pending.SketchId
                |> Option.map (fun (sketch, sketchOrigin) ->
                    let nextConstraint =
                        match pending.Constraint with
                        | FrameDistance(pointId, frameId, "origin", _distance, lp) ->
                            match tryPoint2 sketch pointId, tryFrameOrigin2 state sketchOrigin frameId with
                            | Some p, Some fp -> FrameDistance(pointId, frameId, "origin", Vec2.distance p fp, lp)
                            | _ -> pending.Constraint
                        | FrameLineDistance(lineId, aStart, aEnd, frameId, "origin", _distance, lp) ->
                            match tryLine2 sketch aStart aEnd, tryFrameOrigin2 state sketchOrigin frameId with
                            | Some(a, b), Some fp -> FrameLineDistance(lineId, aStart, aEnd, frameId, "origin", Vec2.pointLineDistance fp a b, lp)
                            | _ -> pending.Constraint
                        | _ -> pending.Constraint
                    { pending with Constraint = nextConstraint }))
        { sketchUi with PendingConstraintPlacement = resolved }

    let sketchUiState (state: EditorState) =
        let placementCursor =
            match state.ConstraintPlacementCursor, state.Doc.SelectedId with
            | Some(sketchId, position), Some selectedId when state.SketchEditMode && selectedId = sketchId -> Some(position.X, position.Y)
            | _ -> None
        let placementTargets =
            match state.ConstraintPlacementMode, state.HoveredTarget with
            | Some _, Some hover when not (List.contains hover state.SelectedTargets) ->
                state.SelectedTargets @ [ hover ]
            | Some _, _ -> state.SelectedTargets
            | None, _ -> state.SelectedTargets
        let baseState =
            SketchAuthoring.availabilityForSelection
                state.Doc
                state.SketchEditMode
                state.SketchTool
                state.ConstraintPlacementMode
                placementTargets
                placementCursor
                state.ConstraintPlacementDraft
                state.HoveredTarget
        { baseState with
            ToolPoints = if baseState.Tool = "none" then [] else state.SketchToolPoints
            EditingDimension = state.EditingDimension }
        |> withResolvedPendingConstraintValue state

    let normalizeState (state: EditorState) =
        let next = sketchUiState state
        let editingDimension =
            state.EditingDimension
            |> Option.bind (fun current ->
                match SketchAuthoring.trySelectedSketch state.Doc with
                | Some selected when next.EditMode && selected.Action.Id = current.SketchId ->
                    SketchAuthoring.tryEditableDimension current.SketchId selected.Sketch current.ConstraintIndex
                | _ -> None)
        let constraintPlacementCursor =
            match next.ConstraintPlacementMode, state.ConstraintPlacementCursor, state.Doc.SelectedId with
            | Some _, Some(sketchId, pos), Some selectedId when next.EditMode && next.Tool = "none" && selectedId = sketchId -> Some(sketchId, pos)
            | _ -> None
        let constraintPlacementDraft =
            match next.ConstraintPlacementMode, state.ConstraintPlacementDraft, state.Doc.SelectedId with
            | Some kind, Some draft, Some selectedId when next.EditMode && next.Tool = "none" && draft.SketchId = selectedId && draft.Kind = kind -> Some draft
            | _ -> None
        { state with
            SketchEditMode = next.EditMode
            SketchTool = next.Tool
            SketchToolPoints = if next.Tool = "none" then [] else state.SketchToolPoints
            EditingDimension = editingDimension
            ConstraintPlacementMode = next.ConstraintPlacementMode
            ConstraintPlacementCursor = constraintPlacementCursor
            ConstraintPlacementDraft = constraintPlacementDraft }

    let recompileState (state: EditorState) =
        let compiled = Pipeline.compile state.Doc.Actions
        let next =
            { state with
                Compiled = compiled
                HoveredTarget = state.HoveredTarget |> Option.filter (isValidSelectionTarget { state with Compiled = compiled })
                SelectedTargets = state.SelectedTargets |> List.filter (isValidSelectionTarget { state with Compiled = compiled }) }
        normalizeState next

    /// Clear only the "in-progress placement" scratch state — dimension
    /// being edited and the constraint-placement draft/cursor. Used when
    /// cancelling or committing a single widget while leaving tool and mode
    /// selection alone.
    let clearDrafts (state: EditorState) =
        { state with
            EditingDimension = None
            ConstraintPlacementDraft = None
            ConstraintPlacementCursor = None }

    /// Clear tool-related transient state (tool points, pending edits,
    /// placement mode/draft/cursor) while preserving selection and hover.
    /// Used when switching tool or placement mode.
    let clearToolState (state: EditorState) =
        { state with
            SketchToolPoints = []
            EditingDimension = None
            ConstraintPlacementMode = None
            ConstraintPlacementDraft = None
            ConstraintPlacementCursor = None }

    /// Full reset of transient UI state after a committing action (add
    /// constraint, delete, replace sketch, etc.). Leaves SketchEditMode
    /// and SketchTool intact; everything else goes back to idle.
    let clearTransient (state: EditorState) =
        { state with
            HoveredTarget = None
            SelectedTargets = []
            SketchToolPoints = []
            EditingDimension = None
            ConstraintPlacementMode = None
            ConstraintPlacementDraft = None
            ConstraintPlacementCursor = None }

    /// Wholesale document replacement with a full UI reset. Used by
    /// LoadModel and ClearModel.
    let loadDoc (doc: Document) (state: EditorState) =
        { state with
            Doc = doc
            PaletteSession = Palette.empty
            HoveredTarget = None
            SelectedTargets = []
            SketchEditMode = false
            SketchTool = "none"
            SketchToolPoints = []
            EditingDimension = None
            ConstraintPlacementMode = None
            ConstraintPlacementDraft = None
            ConstraintPlacementCursor = None }
        |> recompileState

    let applySelectionIntent intent target current =
        match intent with
        | "toggle" ->
            if current |> List.exists ((=) target) then
                current |> List.filter ((<>) target)
            else
                target :: current
        | _ ->
            [ target ]

    let reduceSelectionCandidates (state: EditorState) pickCandidates =
        let pickableById = state.Compiled.Pickables |> List.map (fun p -> Pickable.pickId p, p) |> Map.ofList
        pickCandidates
        |> List.choose (fun candidate ->
            Map.tryFind candidate.PickId pickableById
            |> Option.map (fun pickable ->
                Pickable.selectionTarget pickable, candidate.Score, Some(Pickable.targetAction pickable))
            |> Option.filter (fun (target, _score, _action) -> isValidSelectionTarget state target))
        |> List.sortBy (fun (target, score, _action) -> Pickable.selectionPriority target, score)
        |> List.tryHead

    let applyDeleteIntent (state: EditorState) =
        if state.SketchEditMode then
            match SketchAuthoring.trySelectedSketch state.Doc with
            | Some ctx when not state.SelectedTargets.IsEmpty ->
                let nextDoc =
                    SketchAuthoring.withUpdatedSketch state.Doc ctx.Action.Id (SketchAuthoring.deleteTargets state.SelectedTargets ctx.Sketch)
                { state with Doc = nextDoc }
                |> clearTransient
                |> recompileState
            | _ ->
                state
        else
            match state.Doc.SelectedId with
            | Some id when id <> "origin" ->
                { state with Doc = Document.removeAction id state.Doc }
                |> clearTransient
                |> recompileState
            | _ ->
                state

    let paletteMaybeBuild (idSuffix: string) (state: EditorState) =
        let paletteState = Palette.toState state.PaletteSession state.Compiled.TypeMap state.Doc
        if paletteState.Mode = "done" then
            match Palette.buildAction state.PaletteSession idSuffix with
            | Some action ->
                { state with
                    Doc = Document.addAction action state.Doc
                    PaletteSession = Palette.empty }
                |> recompileState
            | None ->
                { state with PaletteSession = Palette.empty }
        else
            state

    let serializedModel (state: EditorState) =
        { Name = state.Doc.Name
          Actions = state.Doc.Actions }

#if !FABLE_COMPILER
    let deserializeSerializedModel (rawText: string) =
        let root = JsonNode.Parse(rawText).AsObject()
        match root["actions"] with
        | :? JsonArray as actions ->
            for actionNode in actions do
                match actionNode with
                | :? JsonObject as actionObj ->
                    match actionObj["kind"] with
                    | :? JsonObject as kindObj when kindObj["case"] <> null ->
                        match kindObj["case"].GetValue<string>() with
                        | "Sketch" ->
                            match kindObj["plane"] with
                            | :? JsonValue as planeValue ->
                                let mutable plane = ""
                                if planeValue.TryGetValue<string>(&plane) then
                                    let planeObj = JsonObject()
                                    planeObj["case"] <- JsonValue.Create(plane)
                                    kindObj["plane"] <- planeObj
                            | _ -> ()
                        | _ -> ()
                    | _ -> ()
                | _ -> ()
        | _ -> ()
        JsonSerializer.Deserialize<SerializedModel>(root.ToJsonString(), jsonOpts)
#endif

    let update (message: Message) (state: EditorState) =
        match message with
        | SelectAction id ->
            { state with Doc = Document.select id state.Doc }
        | SetHoveredTarget hoveredTarget ->
            { state with HoveredTarget = hoveredTarget }
        | SetSelectedTargets selectedTargets ->
            { state with SelectedTargets = selectedTargets }
        | AddDefaultAction(kindCase, id) ->
            match ActionKind.defaultFor kindCase with
            | Some kind ->
                let action =
                    { Id = id
                      Name = None
                      Kind = kind
                      Visible = true
                      Display = None
                      FieldSlice = None }
                { state with Doc = Document.addAction action state.Doc } |> recompileState
            | None ->
                state
        | AddAction action ->
            { state with Doc = Document.addAction action state.Doc } |> recompileState
        | UpdateAction(id, action) ->
            { state with Doc = Document.updateAction id action state.Doc } |> recompileState
        | RemoveAction id ->
            { state with Doc = Document.removeAction id state.Doc } |> recompileState
        | ReorderActions ids ->
            { state with Doc = Document.reorder ids state.Doc } |> recompileState
        | ToggleActionVisible id ->
            { state with Doc = Document.toggleVisible id state.Doc } |> recompileState
        | ToggleDisplay id ->
            { state with Doc = Document.toggleDisplay id state.Doc } |> recompileState
        | PatchDisplayValue(id, key, value) ->
            { state with Doc = Document.patchDisplayValue id key value state.Doc } |> recompileState
        | ToggleFieldSlice id ->
            { state with Doc = Document.toggleFieldSlice id state.Doc } |> recompileState
        | PatchFieldSliceValue(id, key, value) ->
            { state with Doc = Document.patchFieldSliceValue id key value state.Doc } |> recompileState
        | PatchActionParamValue(id, key, value) ->
            { state with Doc = Document.patchParamValue id key value state.Doc } |> recompileState
        | DeleteIntent ->
            applyDeleteIntent state
        | ViewerHover candidates ->
            { state with
                HoveredTarget =
                    reduceSelectionCandidates state candidates
                    |> Option.map (fun (target, _score, _action) -> target) }
            |> normalizeState
        | ViewerPick(intent, candidates) ->
            match reduceSelectionCandidates state candidates with
            | Some(target, _score, actionId) ->
                { state with
                    HoveredTarget = Some target
                    SelectedTargets = applySelectionIntent intent target state.SelectedTargets
                    Doc =
                        match actionSelectionForTarget state target actionId with
                        | Some id -> Document.select id state.Doc
                        | None -> state.Doc }
                |> recompileState
            | None ->
                { state with
                    HoveredTarget = None
                    SelectedTargets = if intent = "replace" then [] else state.SelectedTargets }
                |> normalizeState
        | StartEditingDimension index ->
            let editing =
                match SketchAuthoring.trySelectedSketch state.Doc with
                | Some selected when state.SketchEditMode && state.Doc.SelectedId = Some selected.Action.Id ->
                    SketchAuthoring.tryEditableDimension selected.Action.Id selected.Sketch index
                | _ -> None
            { clearToolState state with
                SketchTool = "none"
                EditingDimension = editing }
            |> normalizeState
        | CancelEditingDimension ->
            clearDrafts state |> normalizeState
        | CommitEditingDimension value ->
            match state.EditingDimension with
            | Some current ->
                let key = $"sketch.constraint.{current.ConstraintIndex}.{current.Key}"
                { clearDrafts state with
                    Doc = Document.patchParamValue current.SketchId key (VFloat value) state.Doc }
                |> recompileState
            | None ->
                state
        | ViewerDimensionClickTarget ->
            match state.ConstraintPlacementMode, SketchAuthoring.trySelectedSketch state.Doc with
            | Some kind, Some selected when state.SketchEditMode ->
                { state with
                    ConstraintPlacementDraft =
                        SketchAuthoring.updatePlacementDraft selected.Action.Id kind state.HoveredTarget state.ConstraintPlacementDraft }
                |> normalizeState
            | _ ->
                state
        | ReplaceSketch(actionId, sketch) ->
            match state.Doc.Actions |> List.tryFind (fun action -> action.Id = actionId) with
            | Some { Kind = Sketch(_, _, _) } ->
                { state with
                    Doc = SketchAuthoring.withUpdatedSketch state.Doc actionId sketch }
                |> clearTransient
                |> recompileState
            | _ ->
                state
        | PatchSketchParams(actionId, updates) ->
            let nextDoc =
                updates
                |> List.fold (fun current (key, value) -> Document.patchParamValue actionId key (VFloat value) current) state.Doc
            { state with Doc = nextDoc } |> recompileState
        | ViewerToolClick(x, y) ->
            match SketchAuthoring.trySelectedSketch state.Doc with
            | Some selected when state.SketchEditMode && state.SketchTool <> "none" ->
                let nextPoints = state.SketchToolPoints @ [ { X = x; Y = y } ]
                if nextPoints.Length >= SketchAuthoring.requiredToolPoints state.SketchTool then
                    match SketchAuthoring.applyToolClick state.SketchTool nextPoints selected.Sketch with
                    | Some nextSketch ->
                        { clearTransient state with
                            Doc = SketchAuthoring.withUpdatedSketch state.Doc selected.Action.Id nextSketch
                            ConstraintPlacementMode = state.ConstraintPlacementMode }
                        |> recompileState
                    | None ->
                        state
                else
                    { state with SketchToolPoints = nextPoints }
            | _ ->
                state
        | ViewerPlaceConstraint(x, y) ->
            match (sketchUiState state).PendingConstraintPlacement with
            | Some pending ->
                match SketchAuthoring.placePendingConstraint state.Doc pending { X = x; Y = y } with
                | Some nextDoc ->
                    { clearTransient state with Doc = nextDoc }
                    |> recompileState
                | None ->
                    state
            | None ->
                state
        | ToggleSketchEdit ->
            { state with SketchEditMode = not state.SketchEditMode }
            |> normalizeState
        | SetSketchTool tool ->
            { clearToolState state with
                SketchEditMode = true
                SketchTool = if String.IsNullOrWhiteSpace(tool) then "none" else tool }
            |> normalizeState
        | ToggleConstraintPlacement kind ->
            let nextMode =
                match state.ConstraintPlacementMode with
                | Some active when active = kind -> None
                | _ -> Some kind
            { clearToolState state with
                SketchEditMode = true
                SketchTool = "none"
                ConstraintPlacementMode = nextMode }
            |> normalizeState
        | AddConstraintFromSelection kind ->
            match SketchAuthoring.addConstraintFromSelection state.Doc state.SelectedTargets kind with
            | Some nextDoc ->
                { state with Doc = nextDoc }
                |> clearTransient
                |> recompileState
            | None ->
                state
        | DeleteSketchConstraint index ->
            match SketchAuthoring.trySelectedSketch state.Doc with
            | Some ctx ->
                let nextDoc =
                    SketchAuthoring.withUpdatedSketch state.Doc ctx.Action.Id (SketchAuthoring.removeConstraintAt index ctx.Sketch)
                { state with Doc = nextDoc }
                |> clearTransient
                |> recompileState
            | None ->
                state
        | SetConstraintPlacementCursor cursor ->
            { state with ConstraintPlacementCursor = cursor } |> normalizeState
        | PaletteOpen ->
            { state with PaletteSession = Palette.openSession () }
        | PaletteSetQuery query ->
            { state with PaletteSession = Palette.setQuery query state.PaletteSession }
        | PalettePick id ->
            let nextPalette =
                match state.PaletteSession.PickedKind with
                | None -> Palette.pickCommand id state.PaletteSession
                | Some _ -> Palette.pickItem id state.PaletteSession
            { state with PaletteSession = nextPalette }
        | PaletteSetScalarField(key, value) ->
            { state with PaletteSession = Palette.setScalarField key value state.PaletteSession }
        | PaletteCommitScalars ->
            { state with PaletteSession = Palette.commitScalars state.PaletteSession }
        | PaletteFinish idSuffix ->
            { state with PaletteSession = Palette.skipToEnd state.PaletteSession }
            |> paletteMaybeBuild idSuffix
        | PaletteBack ->
            { state with PaletteSession = Palette.back state.PaletteSession }
        | PaletteClose ->
            { state with PaletteSession = Palette.empty }
        | ReplaceDocument doc ->
            { state with Doc = doc } |> recompileState
        | LoadModel model ->
            let selectedId =
                model.Actions
                |> List.tryFind (fun a -> a.Id = "origin")
                |> Option.orElseWith (fun () -> model.Actions |> List.tryHead)
                |> Option.map (fun a -> a.Id)
            loadDoc { Name = model.Name; Actions = model.Actions; SelectedId = selectedId } state
        | ClearModel ->
            loadDoc (Document.emptyDocument ()) state

    let documentView (state: EditorState) =
        let tm = state.Compiled.TypeMap
        let errors = formatErrors state.Compiled.Errors
        let refOptions =
            match state.Doc.SelectedId with
            | None -> Map.empty
            | Some selId ->
                match state.Doc.Actions |> List.tryFind (fun a -> a.Id = selId) with
                | None -> Map.empty
                | Some sel ->
                    let selIdx = state.Doc.Actions |> List.findIndex (fun a -> a.Id = selId)
                    let before = state.Doc.Actions |> List.take selIdx
                    let accepted = TypeCheck.acceptedInputs sel.Kind
                    accepted
                    |> Map.map (fun _key types ->
                        before
                        |> List.choose (fun a ->
                            match Map.tryFind a.Id tm with
                            | Some t when List.contains t types -> Some a.Id
                            | _ -> None))

        let actions =
            state.Doc.Actions
            |> List.map (fun a ->
                match Map.tryFind a.Id tm with
                | Some FieldType.Field ->
                    { a with
                        Display = Some(a.Display |> Option.defaultValue DisplaySettings.defaults)
                        FieldSlice = Some(a.FieldSlice |> Option.defaultValue FieldSliceSettings.defaults) }
                | _ ->
                    { a with Display = None; FieldSlice = None })

        let sketchLoops =
            actions
            |> List.choose (fun a ->
                match a.Kind with
                | Sketch(_, _, sketch) ->
                    let loops =
                        SketchLoops.detectLoops sketch.Entities
                        |> List.map (fun loop -> { Id = loop.Id; EntityIds = loop.EntityIds })
                    Some(a.Id, loops)
                | _ -> None)
            |> Map.ofList

        { Name = state.Doc.Name
          Actions = actions
          SelectedId = state.Doc.SelectedId
          SelectedTargets = state.SelectedTargets
          SketchUi = sketchUiState state
          RefOptions = refOptions
          SketchLoops = sketchLoops
          Errors = errors }

    let paletteView (state: EditorState) =
        Palette.toState state.PaletteSession state.Compiled.TypeMap state.Doc

    let viewerModel (state: EditorState) =
        let indexList =
            state.Compiled.Slots.Index
            |> Map.toList
            |> List.map (fun (r, s) -> {| ActionId = r.ActionId; Path = r.Path; Slot = s |})
        let sketches =
            state.Doc.Actions
            |> List.choose (fun a ->
                match a.Kind with
                | Sketch(origin, plane, sk) ->
                    let sketchOrigin = resolveSketchTransform state origin plane
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

    let viewerState (state: EditorState) =
        let isDraggable =
            function
            | TargetPoint _ | TargetDimension _ as t -> belongsToActiveSketch state t
            | _ -> false
        let dragTarget = state.HoveredTarget |> Option.filter isDraggable

        let frameHighlightAllowed = state.ConstraintPlacementMode <> Some "angle"
        let highlightedTargetAllowed target =
            match target with
            | TargetSurface _ -> true
            | TargetFrameAxis _ -> false
            | TargetFrameOrigin _ -> frameHighlightAllowed && belongsToActiveSketch state target
            | _ -> belongsToActiveSketch state target

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

        let sketchFrames =
            state.Doc.Actions
            |> List.choose (fun a ->
                match a.Kind with
                | Sketch(origin, plane, _) ->
                    Some { Id = a.Id; Transform = resolveSketchTransform state origin plane }
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
          SketchUi = sketchUiState state
          Frames = frames
          SketchEditFrames = sketchEditFrames state
          SketchOriginFrames = sketchFrames
          FieldSlices = activeFieldSlices state
          Visible = visibleByAction
          ConstraintLabelPositions = constraintLabelPositions
          Display = displayByAction
          Errors = formatErrors state.Compiled.Errors }
