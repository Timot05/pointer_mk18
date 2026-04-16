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
      SketchFrames: FrameView list
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
    | SetSketchToolPoints of LabelPos list
    | SetEditingDimension of EditingDimension option
    | SetConstraintPlacementMode of string option
    | SetConstraintPlacementDraft of ConstraintPlacementDraft option
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

    let effectivePlacementTargets (state: EditorState) =
        match state.HoveredTarget with
        | Some target when state.SelectedTargets |> List.contains target |> not -> state.SelectedTargets @ [ target ]
        | _ -> state.SelectedTargets

    let activeSketchEditId (state: EditorState) =
        match state.SketchEditMode, state.Doc.SelectedId with
        | true, Some selectedId ->
            match state.Doc.Actions |> List.tryFind (fun a -> a.Id = selectedId) with
            | Some { Kind = Sketch _ } -> Some selectedId
            | _ -> None
        | _ -> None

    let sketchEditFrames (state: EditorState) =
        match activeSketchEditId state with
        | None -> []
        | Some sketchId ->
            let sketchIndex = state.Doc.Actions |> List.findIndex (fun a -> a.Id = sketchId)
            state.Doc.Actions
            |> List.take sketchIndex
            |> List.choose (fun a ->
                match Map.tryFind a.Id state.Compiled.TypeMap with
                | Some FieldType.Frame ->
                    Map.tryFind a.Id state.Compiled.Frames
                    |> Option.map (fun t -> { Id = a.Id; Transform = t })
                | _ -> None)

    let isAllowedSketchEditFrameTarget (state: EditorState) =
        let allowedFrameIds =
            sketchEditFrames state |> List.map (fun f -> f.Id) |> Set.ofList
        function
        | TargetFrameOrigin frameId ->
            Set.contains frameId allowedFrameIds
        | _ -> false

    let isValidSelectionTarget (state: EditorState) target =
        match target with
        | TargetFrameOrigin _ -> isAllowedSketchEditFrameTarget state target
        | _ -> state.Compiled.Pickables |> List.exists (Pickable.sameTarget target)

    let actionSelectionForTarget (state: EditorState) target actionId =
        match state.SketchEditMode, activeSketchEditId state with
        | true, Some sketchId ->
            match target with
            | TargetPoint(targetSketchId, _)
            | TargetLine(targetSketchId, _)
            | TargetCircle(targetSketchId, _)
            | TargetArc(targetSketchId, _)
            | TargetLoop(targetSketchId, _)
            | TargetDimension(targetSketchId, _) when targetSketchId = sketchId ->
                Some sketchId
            | TargetFrameOrigin _ when isAllowedSketchEditFrameTarget state target ->
                Some sketchId
            | _ ->
                actionId
        | _ ->
            actionId

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

    let pointLineDistance ((px, py): float * float) ((ax, ay): float * float) ((bx, by): float * float) =
        let dx = bx - ax
        let dy = by - ay
        let len = sqrt (dx * dx + dy * dy)
        if len < 1e-9 then 0.0
        else abs ((dx * (py - ay) - dy * (px - ax)) / len)

    let pointDistance ((ax, ay): float * float) ((bx, by): float * float) =
        let dx = bx - ax
        let dy = by - ay
        sqrt (dx * dx + dy * dy)

    let slotValue (state: EditorState) (slot: Slot) =
        state.Compiled.Slots.Values.[slot]

    let localSliceBasis plane =
        match plane with
        | "X" -> { X = 0.0; Y = 1.0; Z = 0.0 }, { X = 0.0; Y = 0.0; Z = 1.0 }, { X = 1.0; Y = 0.0; Z = 0.0 }
        | "Y" -> { X = 1.0; Y = 0.0; Z = 0.0 }, { X = 0.0; Y = 0.0; Z = 1.0 }, { X = 0.0; Y = 1.0; Z = 0.0 }
        | _ -> { X = 1.0; Y = 0.0; Z = 0.0 }, { X = 0.0; Y = 1.0; Z = 0.0 }, { X = 0.0; Y = 0.0; Z = 1.0 }

    let rec leadingFieldTransform (state: EditorState) (field: FieldNode) (acc: RigidTransform) =
        match field with
        | FTranslate(x, y, z, child) ->
            let step = RigidTransform.translate { X = slotValue state x; Y = slotValue state y; Z = slotValue state z }
            leadingFieldTransform state child (acc * step)
        | FRotate(ax, ay, az, angle, child) ->
            let step =
                RigidTransform.fromAxisAngle
                    { X = slotValue state ax; Y = slotValue state ay; Z = slotValue state az }
                    (slotValue state angle)
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
                            | Some p, Some fp -> FrameDistance(pointId, frameId, "origin", pointDistance p fp, lp)
                            | _ -> pending.Constraint
                        | FrameLineDistance(lineId, aStart, aEnd, frameId, "origin", _distance, lp) ->
                            match tryLine2 sketch aStart aEnd, tryFrameOrigin2 state sketchOrigin frameId with
                            | Some(a, b), Some fp -> FrameLineDistance(lineId, aStart, aEnd, frameId, "origin", pointLineDistance fp a b, lp)
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
            match state.ConstraintPlacementMode with
            | Some _ -> effectivePlacementTargets state
            | None -> state.SelectedTargets
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

    let clearEditorTransientState (state: EditorState) =
        { state with
            HoveredTarget = None
            SelectedTargets = []
            SketchToolPoints = []
            EditingDimension = None
            ConstraintPlacementDraft = None
            ConstraintPlacementCursor = None }

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
                |> clearEditorTransientState
                |> recompileState
            | _ ->
                state
        else
            match state.Doc.SelectedId with
            | Some id when id <> "origin" ->
                { state with Doc = Document.removeAction id state.Doc }
                |> clearEditorTransientState
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
            let actionKind =
                match kindCase with
                | "Sphere" -> Some(ActionKind.Sphere 8.0)
                | "Cylinder" -> Some(ActionKind.Cylinder(5.0, 20.0))
                | "Box" -> Some(ActionKind.Box(10.0, 10.0, 10.0))
                | "HalfPlane" -> Some(ActionKind.HalfPlane("Z", 0.0, false))
                | "Translate" -> Some(ActionKind.Translate(None, 0.0, 0.0, 0.0))
                | "Rotate" -> Some(ActionKind.Rotate(None, 0.0, 0.0, 1.0, 0.0))
                | "Move" -> Some(ActionKind.Move(None, None))
                | "Union" -> Some(ActionKind.Union(None, None, 0.0))
                | "Subtract" -> Some(ActionKind.Subtract(None, None, 0.0))
                | "Intersect" -> Some(ActionKind.Intersect(None, None, 0.0))
                | "Sketch" -> Some(ActionKind.Sketch(Some "origin", XY, ActionSketch.empty))
                | "FromSketch" -> Some(ActionKind.FromSketch(None, false, FromSketchSelection.defaults))
                | "Thicken" -> Some(ActionKind.Thicken(None, 2.0))
                | "Shell" -> Some(ActionKind.Shell(None, 1.0))
                | "Mesh" -> Some(ActionKind.Mesh(None, 0.2, 96))
                | _ -> None
            match actionKind with
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
            { state with
                EditingDimension =
                    match SketchAuthoring.trySelectedSketch state.Doc with
                    | Some selected when state.SketchEditMode && state.Doc.SelectedId = Some selected.Action.Id ->
                        SketchAuthoring.tryEditableDimension selected.Action.Id selected.Sketch index
                    | _ -> None
                SketchTool = "none"
                SketchToolPoints = []
                ConstraintPlacementDraft = None
                ConstraintPlacementMode = None
                ConstraintPlacementCursor = None }
            |> normalizeState
        | CancelEditingDimension ->
            { state with
                EditingDimension = None
                ConstraintPlacementDraft = None
                ConstraintPlacementCursor = None }
            |> normalizeState
        | CommitEditingDimension value ->
            match state.EditingDimension with
            | Some current ->
                let key = $"sketch.constraint.{current.ConstraintIndex}.{current.Key}"
                { state with
                    Doc = Document.patchParamValue current.SketchId key (VFloat value) state.Doc
                    EditingDimension = None
                    ConstraintPlacementDraft = None
                    ConstraintPlacementCursor = None }
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
                |> clearEditorTransientState
                |> recompileState
            | _ ->
                state
        | PatchSketchParams(actionId, updates) ->
            let nextDoc =
                updates
                |> List.fold (fun current (key, value) -> Document.patchParamValue actionId key (VFloat value) current) state.Doc
            { state with Doc = nextDoc } |> recompileState
        | ViewerToolClick(x, y) ->
            let nextPoint = { X = x; Y = y }
            match SketchAuthoring.trySelectedSketch state.Doc with
            | Some selected when state.SketchEditMode && state.SketchTool <> "none" ->
                let nextPoints = state.SketchToolPoints @ [ nextPoint ]
                if nextPoints.Length >= SketchAuthoring.requiredToolPoints state.SketchTool then
                    match SketchAuthoring.applyToolClick state.SketchTool nextPoints selected.Sketch with
                    | Some nextSketch ->
                        { state with
                            Doc = SketchAuthoring.withUpdatedSketch state.Doc selected.Action.Id nextSketch
                            SketchToolPoints = []
                            HoveredTarget = None
                            SelectedTargets = []
                            EditingDimension = None
                            ConstraintPlacementDraft = None
                            ConstraintPlacementCursor = None }
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
                    { state with
                        Doc = nextDoc
                        HoveredTarget = None
                        SelectedTargets = []
                        SketchToolPoints = []
                        EditingDimension = None
                        ConstraintPlacementDraft = None
                        ConstraintPlacementMode = None
                        ConstraintPlacementCursor = None }
                    |> recompileState
                | None ->
                    state
            | None ->
                state
        | ToggleSketchEdit ->
            let nextEditMode = not state.SketchEditMode
            { state with
                SketchEditMode = nextEditMode
                SketchTool = if nextEditMode then state.SketchTool else "none"
                SketchToolPoints = if nextEditMode then state.SketchToolPoints else []
                EditingDimension = if nextEditMode then state.EditingDimension else None
                ConstraintPlacementMode = if nextEditMode then state.ConstraintPlacementMode else None
                ConstraintPlacementDraft = if nextEditMode then state.ConstraintPlacementDraft else None
                ConstraintPlacementCursor = if nextEditMode then state.ConstraintPlacementCursor else None }
            |> normalizeState
        | SetSketchTool tool ->
            { state with
                SketchEditMode = true
                SketchTool = if String.IsNullOrWhiteSpace(tool) then "none" else tool
                SketchToolPoints = []
                EditingDimension = None
                ConstraintPlacementMode = None
                ConstraintPlacementDraft = None
                ConstraintPlacementCursor = None }
            |> normalizeState
        | ToggleConstraintPlacement kind ->
            { state with
                SketchEditMode = true
                SketchTool = "none"
                SketchToolPoints = []
                EditingDimension = None
                ConstraintPlacementDraft = None
                ConstraintPlacementMode =
                    match state.ConstraintPlacementMode with
                    | Some active when active = kind -> None
                    | _ -> Some kind
                ConstraintPlacementCursor = None }
            |> normalizeState
        | AddConstraintFromSelection kind ->
            match SketchAuthoring.addConstraintFromSelection state.Doc state.SelectedTargets kind with
            | Some nextDoc ->
                { state with Doc = nextDoc }
                |> clearEditorTransientState
                |> recompileState
            | None ->
                state
        | DeleteSketchConstraint index ->
            match SketchAuthoring.trySelectedSketch state.Doc with
            | Some ctx ->
                let nextDoc =
                    SketchAuthoring.withUpdatedSketch state.Doc ctx.Action.Id (SketchAuthoring.removeConstraintAt index ctx.Sketch)
                { state with Doc = nextDoc }
                |> clearEditorTransientState
                |> recompileState
            | None ->
                state
        | SetSketchToolPoints toolPoints ->
            { state with SketchToolPoints = toolPoints }
        | SetEditingDimension editingDimension ->
            { state with EditingDimension = editingDimension } |> normalizeState
        | SetConstraintPlacementMode placementMode ->
            { state with ConstraintPlacementMode = placementMode } |> normalizeState
        | SetConstraintPlacementDraft draft ->
            { state with ConstraintPlacementDraft = draft } |> normalizeState
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
                |> List.tryFind (fun action -> action.Id = "origin")
                |> Option.map (fun action -> action.Id)
                |> Option.orElseWith (fun () -> model.Actions |> List.tryHead |> Option.map (fun action -> action.Id))
            let next =
                { Name = model.Name
                  Actions = model.Actions
                  SelectedId = selectedId }
            { state with
                Doc = next
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
        | ClearModel ->
            let next = Document.emptyDocument ()
            { state with
                Doc = next
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

    let msgSelectAction id = SelectAction id
    let msgSetSelectedTargets targets = SetSelectedTargets targets
    let msgAddDefaultAction kindCase id = AddDefaultAction(kindCase, id)
    let msgAddAction action = AddAction action
    let msgRemoveAction id = RemoveAction id
    let msgUpdateAction id action = UpdateAction(id, action)
    let msgReorderActions ids = ReorderActions ids
    let msgToggleActionVisible id = ToggleActionVisible id
    let msgToggleDisplay id = ToggleDisplay id
    let msgPatchDisplayValue id key value = PatchDisplayValue(id, key, value)
    let msgToggleFieldSlice id = ToggleFieldSlice id
    let msgPatchFieldSliceValue id key value = PatchFieldSliceValue(id, key, value)
    let msgPatchActionParamValue id key value = PatchActionParamValue(id, key, value)
    let msgDeleteIntent = DeleteIntent
    let msgViewerHover candidates = ViewerHover candidates
    let msgViewerPick intent candidates = ViewerPick(intent, candidates)
    let msgStartEditingDimension index = StartEditingDimension index
    let msgCancelEditingDimension = CancelEditingDimension
    let msgCommitEditingDimension value = CommitEditingDimension value
    let msgViewerDimensionClickTarget = ViewerDimensionClickTarget
    let msgReplaceSketch actionId sketch = ReplaceSketch(actionId, sketch)
    let msgPatchSketchParams actionId updates = PatchSketchParams(actionId, updates)
    let msgViewerToolClick x y = ViewerToolClick(x, y)
    let msgViewerPlaceConstraint x y = ViewerPlaceConstraint(x, y)
    let msgToggleSketchEdit = ToggleSketchEdit
    let msgSetSketchTool tool = SetSketchTool tool
    let msgSetConstraintPlacementCursor cursor = SetConstraintPlacementCursor cursor
    let msgToggleConstraintPlacement kind = ToggleConstraintPlacement kind
    let msgAddConstraintFromSelection kind = AddConstraintFromSelection kind
    let msgDeleteSketchConstraint index = DeleteSketchConstraint index
    let msgPaletteOpen = PaletteOpen
    let msgPaletteSetQuery query = PaletteSetQuery query
    let msgPalettePick id = PalettePick id
    let msgPaletteSetScalarField key value = PaletteSetScalarField(key, value)
    let msgPaletteCommitScalars = PaletteCommitScalars
    let msgPaletteFinish idSuffix = PaletteFinish idSuffix
    let msgPaletteBack = PaletteBack
    let msgPaletteClose = PaletteClose
    let msgLoadModel model = LoadModel model
    let msgClearModel = ClearModel

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
        let dragTarget =
            let isActiveSketchTarget =
                function
                | TargetPoint(sketchId, _)
                | TargetDimension(sketchId, _) ->
                    state.SketchEditMode && state.Doc.SelectedId = Some sketchId
                | _ -> false
            state.HoveredTarget |> Option.filter isActiveSketchTarget

        let highlightedTargetAllowed =
            let frameHighlightAllowed =
                match state.ConstraintPlacementMode with
                | Some "angle" -> false
                | _ -> true
            function
            | TargetPoint(sketchId, _)
            | TargetLine(sketchId, _)
            | TargetCircle(sketchId, _)
            | TargetArc(sketchId, _)
            | TargetLoop(sketchId, _)
            | TargetDimension(sketchId, _) ->
                state.SketchEditMode && state.Doc.SelectedId = Some sketchId
            | TargetFrameOrigin _ as target ->
                frameHighlightAllowed && activeSketchEditId state |> Option.isSome && isAllowedSketchEditFrameTarget state target
            | TargetFrameAxis _ ->
                false
            | TargetSurface _ ->
                true

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
            |> List.choose (fun a ->
                match a.Kind with
                | Sketch(_, _, sk) ->
                    sk.Constraints
                    |> List.mapi (fun i c ->
                        let lp =
                            match c with
                            | Distance(_, _, _, lp)
                            | FrameDistance(_, _, _, _, lp)
                            | LineDistance(_, _, _, _, _, _, _, lp)
                            | FrameLineDistance(_, _, _, _, _, _, lp)
                            | PointLineDistance(_, _, _, _, _, lp)
                            | PointCircleDistance(_, _, _, _, lp)
                            | LineCircleDistance(_, _, _, _, _, _, lp)
                            | CircleCircleDistance(_, _, _, _, _, _, lp)
                            | CircleDiameter(_, _, _, lp)
                            | Angle(_, _, _, _, _, _, _, _, _, _, lp) -> lp
                            | _ -> None
                        lp |> Option.map (fun pos -> { SketchId = a.Id; ConstraintIndex = i; Position = pos }))
                    |> List.choose id
                    |> Some
                | _ -> None)
            |> List.concat

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
          SketchFrames = sketchFrames
          FieldSlices = activeFieldSlices state
          Visible = visibleByAction
          ConstraintLabelPositions = constraintLabelPositions
          Display = displayByAction
          Errors = formatErrors state.Compiled.Errors }
