namespace Server

open System

type ActionTemplate =
    | SphereTemplate
    | CylinderTemplate
    | BoxTemplate
    | HalfPlaneTemplate
    | TranslateTemplate
    | RotateTemplate
    | MoveTemplate
    | UnionTemplate
    | SubtractTemplate
    | IntersectTemplate
    | SketchTemplate
    | FromSketchTemplate
    | ThickenTemplate
    | ShellTemplate
    | MeshTemplate

type SketchToolKind =
    | NoSketchTool
    | LineTool
    | RectangleTool
    | RoundedRectangleTool
    | CircleTool
    | ArcTool

type ConstraintPlacementKind =
    | DistancePlacement
    | AnglePlacement

/// Constraints applied immediately from the current selection (no cursor
/// placement needed). Distinct from ConstraintPlacementKind, which covers
/// dimension constraints that require a label position click.
type GeometricConstraintKind =
    | CoincidentConstraint
    | HorizontalConstraint
    | VerticalConstraint
    | MidpointConstraint
    | ParallelConstraint
    | PerpendicularConstraint
    | EqualConstraint
    | TangentConstraint
    | ConcentricConstraint
    | FixedConstraint

type SketchDragKind =
    | DragPoint of pointId: string
    | DragConstraintLabel of constraintIndex: int

type SketchDrag =
    { SketchId: string
      Kind: SketchDragKind
      XField: ActionParamField
      YField: ActionParamField
      Target: LabelPos }

type EditorState =
    { Doc: Document
      Compiled: PipelineResult
      SlotValues: float array
      SolvedSketchParams: Map<string, float32[]>
      PaletteSession: PaletteSession
      HoveredTarget: SelectionTarget option
      SelectedTargets: SelectionTarget list
      SketchEditMode: bool
      SketchTool: string
      SketchToolPoints: LabelPos list
      SketchToolPointRefs: string option list
      LineChainStartPointId: string option
      EditingDimension: EditingDimension option
      ActiveSketchDrag: SketchDrag option
      PendingSketchDragCommit: bool
      ConstraintPlacementMode: ConstraintPlacementKind option
      ConstraintPlacementDraft: ConstraintPlacementDraft option
      ConstraintPlacementCursor: (string * LabelPos) option
      ViewerMode: ViewerMode }

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

type PickCandidateInput =
    { PickId: int
      Score: float32 }

type Effect =
    | RunSketchSolve of SketchDrag
    | FinalizeSketchDrag of SketchDrag
    | ResolveAllSketches

type Message =
    | SelectAction of string
    | SetHoveredTarget of SelectionTarget option
    | SetSelectedTargets of SelectionTarget list
    | AddDefaultAction of ActionTemplate * string
    | AddAction of DocAction
    | UpdateAction of string * DocAction
    | RemoveAction of string
    | ReorderActions of string list
    | ToggleActionVisible of string
    | ToggleDisplay of string
    | SetDisplayValue of string * DisplayField * ParamValue
    | ToggleFieldSlice of string
    | SetFieldSliceValue of string * FieldSliceField * ParamValue
    | SetActionSlotValue of string * ActionParamField * ParamValue
    | SetActionStructureValue of string * ActionParamField * ParamValue
    | DeleteIntent
    | ViewerHover of PickCandidateInput list
    | ViewerPick of string * PickCandidateInput list
    | StartEditingDimension of int
    | CancelEditingDimension
    | CommitEditingDimension of float
    | ViewerDimensionClickTarget
    | ReplaceSketch of string * ActionSketch
    | BeginSketchDrag of SketchDrag
    | UpdateSketchDragTarget of LabelPos
    | ApplySketchSolveResult of SketchDrag * float32[]
    | ApplyResolvedSketchResult of string * float32[]
    | FinishSketchDrag
    | CancelSketchDrag
    | ViewerToolClick of float * float
    | ViewerPlaceConstraint of float * float
    | ToggleSketchEdit
    | SetSketchTool of SketchToolKind
    | ToggleConstraintPlacement of ConstraintPlacementKind
    | AddConstraintFromSelection of GeometricConstraintKind
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
    | SetViewerMode of ViewerMode

module Editor =

    let private trySketchPointPosition (sketch: ActionSketch) (pointId: string) =
        sketch.Entities
        |> List.tryPick (function
            | REPoint(id, x, y) when id = pointId -> Some { X = x; Y = y }
            | _ -> None)

    let actionTemplateKind =
        function
        | SphereTemplate -> Sphere 8.0
        | CylinderTemplate -> Cylinder(5.0, 20.0)
        | BoxTemplate -> Box(10.0, 10.0, 10.0)
        | HalfPlaneTemplate -> HalfPlane("Z", 0.0, false)
        | TranslateTemplate -> Translate(None, 0.0, 0.0, 0.0)
        | RotateTemplate -> Rotate(None, 0.0, 0.0, 1.0, 0.0)
        | MoveTemplate -> Move(None, None)
        | UnionTemplate -> Union(None, None, 0.0)
        | SubtractTemplate -> Subtract(None, None, 0.0)
        | IntersectTemplate -> Intersect(None, None, 0.0)
        | SketchTemplate -> Sketch(Some "origin", XY, ActionSketch.empty)
        | FromSketchTemplate -> FromSketch(None, false, FromSketchSelection.defaults)
        | ThickenTemplate -> Thicken(None, 2.0)
        | ShellTemplate -> Shell(None, 1.0)
        | MeshTemplate -> Mesh(None, 0.2, 96)

    let sketchToolName =
        function
        | NoSketchTool -> "none"
        | LineTool -> "line"
        | RectangleTool -> "rectangle"
        | RoundedRectangleTool -> "roundedRectangle"
        | CircleTool -> "circle"
        | ArcTool -> "arc"

    let constraintPlacementName =
        function
        | DistancePlacement -> "distance"
        | AnglePlacement -> "angle"

    let tryConstraintPlacementKind =
        function
        | "distance" -> Some DistancePlacement
        | "angle" -> Some AnglePlacement
        | _ -> None

    /// String key the sketch authoring module expects for each geometric
    /// constraint kind (must match SketchAuthoring.buildConstraint's match).
    let geometricConstraintName =
        function
        | CoincidentConstraint -> "Coincident"
        | HorizontalConstraint -> "Horizontal"
        | VerticalConstraint -> "Vertical"
        | MidpointConstraint -> "Midpoint"
        | ParallelConstraint -> "Parallel"
        | PerpendicularConstraint -> "Perpendicular"
        | EqualConstraint -> "Equal"
        | TangentConstraint -> "Tangent"
        | ConcentricConstraint -> "Concentric"
        | FixedConstraint -> "Fixed"

    let initState () =
        let doc = Document.stressDocument ()
        let compiled = Pipeline.compile doc.Actions
        { Doc = doc
          Compiled = compiled
          SlotValues = Array.copy compiled.Slots.Values
          SolvedSketchParams = Map.empty
          PaletteSession = Palette.empty
          HoveredTarget = None
          SelectedTargets = []
          SketchEditMode = false
          SketchTool = "none"
          SketchToolPoints = []
          SketchToolPointRefs = []
          LineChainStartPointId = None
          EditingDimension = None
          ActiveSketchDrag = None
          PendingSketchDragCommit = false
          ConstraintPlacementMode = None
          ConstraintPlacementDraft = None
          ConstraintPlacementCursor = None
          ViewerMode = IntervalKernel }

    let isSlotBackedActionParamField =
        function
        | CylinderRadius
        | CylinderHeight
        | SphereRadius
        | BoxWidth
        | BoxHeight
        | BoxDepth
        | TranslateX
        | TranslateY
        | TranslateZ
        | RotateAxisX
        | RotateAxisY
        | RotateAxisZ
        | RotateAngle
        | HalfPlaneOffset
        | UnionRadius
        | SubtractRadius
        | IntersectRadius
        | SketchEntityField _
        | SketchConstraintField _
        | ThickenAmount
        | ShellThickness
        | MeshSize
        | MeshResolution -> true
        | TranslateChild
        | RotateChild
        | HalfPlaneAxis
        | HalfPlaneFlip
        | MoveChild
        | MoveFrame
        | UnionA
        | UnionB
        | SubtractA
        | SubtractB
        | IntersectA
        | IntersectB
        | SketchOrigin
        | SketchPlane
        | FromSketchChild
        | FromSketchFlip
        | FromSketchSelection
        | ThickenChild
        | ShellChild
        | MeshChild -> false

    let setActionParamValue id field value =
        if isSlotBackedActionParamField field then
            SetActionSlotValue(id, field, value)
        else
            SetActionStructureValue(id, field, value)

    let setDisplayValue id field value = SetDisplayValue(id, field, value)
    let setFieldSliceValue id field value = SetFieldSliceValue(id, field, value)

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
            |> Option.map (Frames.foldChain state.Compiled.Slots state.SlotValues)
            |> Option.defaultValue RigidTransform.Identity
        sketchPlaneTransform originFrame plane

    let resolvedFrames (state: EditorState) =
        state.Compiled.Frames
        |> Map.map (fun _ chain -> Frames.foldChain state.Compiled.Slots state.SlotValues chain)

    /// ID of the sketch currently being edited, if any.
    let activeSketchEditId (state: EditorState) =
        match state.SketchEditMode, state.Doc.SelectedId with
        | true, Some id ->
            state.Doc.Actions
            |> List.tryFind (fun a -> a.Id = id)
            |> Option.bind (fun a -> match a.Kind with Sketch _ -> Some id | _ -> None)
        | _ -> None

    /// Ids of frame actions that appear before the active sketch — the
    /// frame origins the sketch is allowed to reference.
    let sketchEditFrameIds (state: EditorState) : Set<string> =
        match activeSketchEditId state with
        | None -> Set.empty
        | Some sketchId ->
            let i = state.Doc.Actions |> List.findIndex (fun a -> a.Id = sketchId)
            state.Doc.Actions
            |> List.take i
            |> List.choose (fun a ->
                match Map.tryFind a.Id state.Compiled.TypeMap with
                | Some FieldType.Frame -> Some a.Id
                | _ -> None)
            |> Set.ofList

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
            | TargetFrameOrigin f -> Set.contains f (sketchEditFrameIds state)
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

    /// Resolve a sketch id to its content + origin transform in the current
    /// compiled state. Used as a lookup callback by SketchAuthoring.
    let trySketchContext (state: EditorState) (sketchId: string) =
        state.Doc.Actions
        |> List.tryFind (fun action -> action.Id = sketchId)
        |> Option.bind (fun action ->
            match action.Kind with
            | Sketch(origin, plane, sketch) ->
                Some(sketch, resolveSketchTransform state origin plane)
            | _ -> None)

    let formatErrors (errs: TypeError list) =
        errs |> List.map (fun e ->
            match e with
            | MissingRef(id, key) -> { ActionId = id; Key = key; Error = "missing" }
            | RefNotFound(id, key, target) -> { ActionId = id; Key = key; Error = $"not found: {target}" }
            | ForwardRef(id, key, target) -> { ActionId = id; Key = key; Error = $"forward ref: {target}" }
            | TypeMismatch(id, key, expected, got) ->
                let exp = expected |> List.map string |> String.concat "|"
                { ActionId = id; Key = key; Error = $"expected {exp}, got {got}" })

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
                (state.ConstraintPlacementMode |> Option.map constraintPlacementName)
                placementTargets
                placementCursor
                state.ConstraintPlacementDraft
                state.HoveredTarget
        { baseState with
            ToolPoints = if baseState.Tool = "none" then [] else state.SketchToolPoints
            EditingDimension = state.EditingDimension }
        |> SketchAuthoring.withResolvedPendingConstraintValue (trySketchContext state) (resolvedFrames state)

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
            match next.ConstraintPlacementMode |> Option.bind tryConstraintPlacementKind, state.ConstraintPlacementCursor, state.Doc.SelectedId with
            | Some _, Some(sketchId, pos), Some selectedId when next.EditMode && next.Tool = "none" && selectedId = sketchId -> Some(sketchId, pos)
            | _ -> None
        let constraintPlacementDraft =
            match next.ConstraintPlacementMode |> Option.bind tryConstraintPlacementKind, state.ConstraintPlacementDraft, state.Doc.SelectedId with
            | Some kind, Some draft, Some selectedId when next.EditMode && next.Tool = "none" && draft.SketchId = selectedId && draft.Kind = constraintPlacementName kind -> Some draft
            | _ -> None
        let activeSketchDrag =
            match state.ActiveSketchDrag, state.Doc.SelectedId with
            | Some drag, Some selectedId when next.EditMode && selectedId = drag.SketchId -> Some drag
            | _ -> None
        { state with
            SketchEditMode = next.EditMode
            SketchTool = next.Tool
            SketchToolPoints = if next.Tool = "none" then [] else state.SketchToolPoints
            SketchToolPointRefs = if next.Tool = "none" then [] else state.SketchToolPointRefs
            LineChainStartPointId = if next.Tool = "line" then state.LineChainStartPointId else None
            EditingDimension = editingDimension
            ActiveSketchDrag = activeSketchDrag
            PendingSketchDragCommit = if activeSketchDrag.IsSome then state.PendingSketchDragCommit else false
            ConstraintPlacementMode = next.ConstraintPlacementMode |> Option.bind tryConstraintPlacementKind
            ConstraintPlacementCursor = constraintPlacementCursor
            ConstraintPlacementDraft = constraintPlacementDraft }

    let recompileState (state: EditorState) =
        let compiled = Pipeline.compile state.Doc.Actions
        let next =
            { state with
                Compiled = compiled
                SlotValues = Array.copy compiled.Slots.Values
                SolvedSketchParams = Map.empty
                HoveredTarget = state.HoveredTarget |> Option.filter (isValidSelectionTarget { state with Compiled = compiled })
                SelectedTargets = state.SelectedTargets |> List.filter (isValidSelectionTarget { state with Compiled = compiled }) }
        normalizeState next

    let private patchSlotValues (slotValues: float array) (compiled: PipelineResult) (updates: (SlotRef * float) list) =
        let resolved =
            updates
            |> List.map (fun (slotRef, value) ->
                match SlotTable.tryFindSlot compiled.Slots slotRef with
                | Some slot -> slot, value
                | None -> failwithf "Missing slot for %s/%s" slotRef.ActionId slotRef.Path)

        SlotTable.patchedValues slotValues resolved

    let private floatValueForSlotField field value =
        match field with
        | MeshResolution -> ParamValue.asInt value |> Option.map float
        | _ -> ParamValue.asFloat value

    let private patchActionSlotValues (state: EditorState) (actionId: string) (field: ActionParamField) (value: ParamValue) =
        if not (isSlotBackedActionParamField field) then
            failwithf "Expected slot-backed action field, got %A" field

        match floatValueForSlotField field value with
        | Some number ->
            let slotRef =
                { ActionId = actionId
                  Path = Document.pathOfParamField field }

            patchSlotValues state.SlotValues state.Compiled [ slotRef, number ]
        | None ->
            failwithf "Expected numeric slot value for %A, got %A" field value

    let private patchDisplaySlotValues (state: EditorState) (actionId: string) (field: DisplayField) (value: ParamValue) =
        let updates =
            match field with
            | DisplayColor ->
                match ParamValue.asFloatArray value with
                | Some color when color.Length = 3 ->
                    (Document.pathOfDisplayField field, color |> Array.toList)
                    ||> List.zip
                    |> List.map (fun (path, colorValue) ->
                        { ActionId = actionId; Path = path }, colorValue)
                | Some color ->
                    failwithf "Expected RGB display color, got %d components" color.Length
                | None ->
                    failwithf "Expected display color array, got %A" value
            | DisplayOpacity
            | DisplayIsoValue ->
                match ParamValue.asFloat value with
                | Some number ->
                    Document.pathOfDisplayField field
                    |> List.map (fun path -> { ActionId = actionId; Path = path }, number)
                | None ->
                    failwithf "Expected numeric display value for %A, got %A" field value

        patchSlotValues state.SlotValues state.Compiled updates

    let private patchFieldSliceSlotValues (state: EditorState) (actionId: string) (field: FieldSliceField) (value: ParamValue) =
        let updates =
            match field with
            | SlicePlane -> []
            | SliceOffset ->
                match ParamValue.asFloat value with
                | Some number ->
                    Document.pathOfFieldSliceField field
                    |> List.map (fun path -> { ActionId = actionId; Path = path }, number)
                | None ->
                    failwithf "Expected numeric field-slice value for %A, got %A" field value

        if updates.IsEmpty then
            state.SlotValues
        else
            patchSlotValues state.SlotValues state.Compiled updates

    /// Clear only the "in-progress placement" scratch state — dimension
    /// being edited and the constraint-placement draft/cursor. Used when
    /// cancelling or committing a single widget while leaving tool and mode
    /// selection alone.
    let clearDrafts (state: EditorState) =
        { state with
            EditingDimension = None
            ActiveSketchDrag = None
            PendingSketchDragCommit = false
            SolvedSketchParams = Map.empty
            ConstraintPlacementDraft = None
            ConstraintPlacementCursor = None }

    /// Clear tool-related transient state (tool points, pending edits,
    /// placement mode/draft/cursor) while preserving selection and hover.
    /// Used when switching tool or placement mode.
    let clearToolState (state: EditorState) =
        { state with
            SketchToolPoints = []
            SketchToolPointRefs = []
            LineChainStartPointId = None
            EditingDimension = None
            ActiveSketchDrag = None
            PendingSketchDragCommit = false
            SolvedSketchParams = Map.empty
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
            SketchToolPointRefs = []
            LineChainStartPointId = None
            EditingDimension = None
            ActiveSketchDrag = None
            PendingSketchDragCommit = false
            SolvedSketchParams = Map.empty
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
            SketchToolPointRefs = []
            LineChainStartPointId = None
            EditingDimension = None
            ActiveSketchDrag = None
            PendingSketchDragCommit = false
            SolvedSketchParams = Map.empty
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

    let noEffects : Effect list = []

    let private isLabelDrag =
        function
        | { Kind = DragConstraintLabel _ } -> true
        | _ -> false

    let private patchDragTargetSlotValues (state: EditorState) (drag: SketchDrag) (target: LabelPos) =
        let patched = Array.copy state.SlotValues

        let tryPatch field number =
            let slotRef =
                { ActionId = drag.SketchId
                  Path = Document.pathOfParamField field }

            match SlotTable.tryFindSlot state.Compiled.Slots slotRef with
            | Some slot when slot < patched.Length -> patched.[slot] <- number
            | _ -> ()

        tryPatch drag.XField target.X
        tryPatch drag.YField target.Y
        patched

    let update (message: Message) (state: EditorState) : EditorState * Effect list =
        match message with
        | BeginSketchDrag drag ->
            if isLabelDrag drag then
                { clearDrafts state with
                    ActiveSketchDrag = Some drag
                    PendingSketchDragCommit = false
                    Doc = Document.patchParamValue drag.SketchId drag.XField (VFloat drag.Target.X) state.Doc
                          |> Document.patchParamValue drag.SketchId drag.YField (VFloat drag.Target.Y)
                    SlotValues = patchDragTargetSlotValues state drag drag.Target },
                noEffects
            else
                { clearDrafts state with ActiveSketchDrag = Some drag; PendingSketchDragCommit = false }, [ RunSketchSolve drag ]
        | UpdateSketchDragTarget target ->
            match state.ActiveSketchDrag with
            | Some drag ->
                let nextDrag = { drag with Target = target }
                if isLabelDrag drag then
                    { state with
                        ActiveSketchDrag = Some nextDrag
                        PendingSketchDragCommit = false
                        Doc = Document.patchParamValue drag.SketchId drag.XField (VFloat target.X) state.Doc
                              |> Document.patchParamValue drag.SketchId drag.YField (VFloat target.Y)
                        SlotValues = patchDragTargetSlotValues state drag target },
                    noEffects
                else
                    { state with ActiveSketchDrag = Some nextDrag; PendingSketchDragCommit = false }, [ RunSketchSolve nextDrag ]
            | None ->
                state, noEffects
        | ApplySketchSolveResult(drag, solvedLocal) ->
            match state.ActiveSketchDrag with
            | Some active when active = drag ->
                if state.PendingSketchDragCommit then
                    { state with
                        Doc = SketchSolve.commitSolvedSketch drag.SketchId solvedLocal state.Doc
                        ActiveSketchDrag = None
                        PendingSketchDragCommit = false
                        SolvedSketchParams = Map.empty }
                    |> recompileState,
                    noEffects
                else
                    { state with SolvedSketchParams = state.SolvedSketchParams |> Map.add drag.SketchId solvedLocal }, noEffects
            | _ ->
                state, noEffects
        | ApplyResolvedSketchResult(sketchId, solvedLocal) ->
            match state.ActiveSketchDrag with
            | Some active when active.SketchId = sketchId ->
                state, noEffects
            | _ ->
                match state.Doc.Actions |> List.tryFind (fun action -> action.Id = sketchId) with
                | Some { Kind = Sketch(_, _, sketch) } ->
                    { state with
                        SlotValues = SketchSolve.patchSolvedSketchSlots state.SlotValues state.Compiled.Slots sketchId sketch solvedLocal
                        SolvedSketchParams = state.SolvedSketchParams |> Map.add sketchId solvedLocal },
                    noEffects
                | _ ->
                    state, noEffects
        | FinishSketchDrag ->
            match state.ActiveSketchDrag with
            | Some drag ->
                if isLabelDrag drag then
                    { state with ActiveSketchDrag = None; PendingSketchDragCommit = false }, noEffects
                else
                    { state with PendingSketchDragCommit = true }, [ FinalizeSketchDrag drag ]
            | None ->
                { state with PendingSketchDragCommit = false; SolvedSketchParams = Map.empty }, noEffects
        | CancelSketchDrag ->
            { state with ActiveSketchDrag = None; PendingSketchDragCommit = false; SolvedSketchParams = Map.empty }, noEffects
        | _ ->
            let next =
                match message with
                | SelectAction id ->
                    { state with Doc = Document.select id state.Doc }
                | SetHoveredTarget hoveredTarget ->
                    { state with HoveredTarget = hoveredTarget }
                | SetSelectedTargets selectedTargets ->
                    { state with SelectedTargets = selectedTargets }
                | AddDefaultAction(template, id) ->
                    let action =
                        { Id = id
                          Name = None
                          Kind = actionTemplateKind template
                          Visible = true
                          Display = None
                          FieldSlice = None }
                    { state with Doc = Document.addAction action state.Doc } |> recompileState
                | AddAction action ->
                    { state with Doc = Document.addAction action state.Doc } |> recompileState
                | UpdateAction(id, action) ->
                    { state with Doc = Document.updateAction id action state.Doc } |> recompileState
                | RemoveAction id ->
                    { state with Doc = Document.removeAction id state.Doc } |> recompileState
                | ReorderActions ids ->
                    { state with Doc = Document.reorder ids state.Doc } |> recompileState
                | ToggleActionVisible id ->
                    { state with Doc = Document.toggleVisible id state.Doc } |> normalizeState
                | ToggleDisplay id ->
                    { state with Doc = Document.toggleDisplay id state.Doc } |> normalizeState
                | SetDisplayValue(id, key, value) ->
                    { state with
                        Doc = Document.patchDisplayValue id key value state.Doc
                        SlotValues = patchDisplaySlotValues state id key value }
                    |> normalizeState
                | ToggleFieldSlice id ->
                    { state with Doc = Document.toggleFieldSlice id state.Doc } |> normalizeState
                | SetFieldSliceValue(id, key, value) ->
                    { state with
                        Doc = Document.patchFieldSliceValue id key value state.Doc
                        SlotValues = patchFieldSliceSlotValues state id key value }
                    |> normalizeState
                | SetActionSlotValue(id, key, value) ->
                    { state with
                        Doc = Document.patchParamValue id key value state.Doc
                        SlotValues = patchActionSlotValues state id key value }
                    |> normalizeState
                | SetActionStructureValue(id, key, value) ->
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
                        |> normalizeState
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
                        let field =
                            match current.Key with
                            | "distance" -> ConstraintDistance
                            | "diameter" -> ConstraintDiameter
                            | "angle" -> ConstraintAngle
                            | other -> failwithf "Unsupported editable dimension key: %s" other
                        { clearDrafts state with
                            Doc = Document.patchParamValue current.SketchId (SketchConstraintField(current.ConstraintIndex, field)) (VFloat value) state.Doc
                            SlotValues =
                                patchActionSlotValues
                                    (clearDrafts state)
                                    current.SketchId
                                    (SketchConstraintField(current.ConstraintIndex, field))
                                    (VFloat value) }
                        |> normalizeState
                    | None ->
                        state
                | ViewerDimensionClickTarget ->
                    match state.ConstraintPlacementMode, SketchAuthoring.trySelectedSketch state.Doc with
                    | Some kind, Some selected when state.SketchEditMode ->
                        { state with
                            ConstraintPlacementDraft =
                                SketchAuthoring.updatePlacementDraft
                                    selected.Action.Id
                                    (constraintPlacementName kind)
                                    state.HoveredTarget
                                    state.ConstraintPlacementDraft }
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
                | BeginSketchDrag _
                | UpdateSketchDragTarget _
                | ApplySketchSolveResult _
                | ApplyResolvedSketchResult _
                | FinishSketchDrag
                | CancelSketchDrag ->
                    state
                | ViewerToolClick(x, y) ->
                    match SketchAuthoring.trySelectedSketch state.Doc with
                    | Some selected when state.SketchEditMode && state.SketchTool <> "none" ->
                        let clickedPointRef =
                            match state.HoveredTarget with
                            | Some(TargetPoint(sketchId, pointId)) when sketchId = selected.Action.Id -> Some pointId
                            | _ -> None
                        let clickedPoint =
                            match clickedPointRef with
                            | Some pointId -> trySketchPointPosition selected.Sketch pointId |> Option.defaultValue { X = x; Y = y }
                            | None -> { X = x; Y = y }
                        let nextPoints = state.SketchToolPoints @ [ clickedPoint ]
                        let nextPointRefs = state.SketchToolPointRefs @ [ clickedPointRef ]
                        if nextPoints.Length >= SketchAuthoring.requiredToolPoints state.SketchTool then
                            match
                                SketchAuthoring.applyToolClick
                                    state.SketchTool
                                    nextPoints
                                    nextPointRefs
                                    selected.Sketch
                                    state.LineChainStartPointId
                            with
                            | Some result ->
                                let nextState =
                                    { clearTransient state with
                                        Doc = SketchAuthoring.withUpdatedSketch state.Doc selected.Action.Id result.Sketch
                                        ConstraintPlacementMode = state.ConstraintPlacementMode }
                                    |> recompileState

                                match state.SketchTool, result.ContinueFrom with
                                | "line", Some(nextPointId, nextPoint) ->
                                    { nextState with
                                        SketchTool = "line"
                                        SketchToolPoints = [ nextPoint ]
                                        SketchToolPointRefs = [ Some nextPointId ]
                                        LineChainStartPointId = Some nextPointId }
                                | _ ->
                                    nextState
                            | None ->
                                state
                        else
                            { state with
                                SketchToolPoints = nextPoints
                                SketchToolPointRefs = nextPointRefs }
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
                        SketchTool = sketchToolName tool }
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
                    match SketchAuthoring.addConstraintFromSelection state.Doc state.SelectedTargets (geometricConstraintName kind) with
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
                    |> paletteMaybeBuild id
                | PaletteSetScalarField(key, value) ->
                    { state with PaletteSession = Palette.setScalarField key value state.PaletteSession }
                | PaletteCommitScalars ->
                    { state with PaletteSession = Palette.commitScalars state.PaletteSession }
                    |> paletteMaybeBuild ""
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
                | SetViewerMode mode ->
                    { state with ViewerMode = mode }
            let effects =
                match message with
                | SetDisplayValue _
                | SetFieldSliceValue _
                | SetActionSlotValue _
                | CommitEditingDimension _
                | ViewerPlaceConstraint _
                | AddConstraintFromSelection _ -> [ ResolveAllSketches ]
                | _ -> noEffects
            next, effects
