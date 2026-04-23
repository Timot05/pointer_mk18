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

/// In-flight translate-gizmo drag. Values are applied via direct
/// slot patches (same realtime trick label drags use), and
/// `Initial*` is kept so Cancel can restore the pre-drag state.
type GizmoDrag =
    { ActionId: ActionId
      InitialX: float
      InitialY: float
      InitialZ: float }

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
      /// In-flight translate gizmo drag (axis or plane handle).
      /// Slot values are patched in real time while this is Some.
      ActiveGizmoDrag: GizmoDrag option
      ConstraintPlacementMode: ConstraintPlacementKind option
      ConstraintPlacementDraft: ConstraintPlacementDraft option
      ConstraintPlacementCursor: (string * LabelPos) option
      /// When Some id, the action list is in "edit this action's
      /// inputs" mode for that action: one row per editable input is
      /// expanded directly below it in the list.
      WiringActionId: ActionId option
      /// Which row of the edit expansion is currently focused.
      /// `0` = the action row itself; `1..N` = the N input rows. Only
      /// meaningful when `WiringActionId` is Some.
      EditFocusIdx: int
      /// When Some, an input row is in sub-edit mode:
      ///   * Scalar → ActionList renders an inline number input.
      ///   * Ref    → arrow keys cycle upstream candidates via
      ///              `RefPickIdx`; Enter commits the pick.
      /// Check / Select toggles are applied immediately on Enter and
      /// never enter this sub-mode.
      EditingInputField: ActionParamField option
      /// If Some, the scalar input renders pre-filled with this string
      /// (instead of the current value) and places the cursor at the
      /// end. Set when the user types a digit / '-' / '.' over a
      /// focused scalar row, so the first keystroke is not lost.
      EditingInputInitial: string option
      /// Cursor within the ref-pick candidate list. Meaningful only
      /// when `EditingInputField` identifies a ref field.
      RefPickIdx: int
      /// Toggled by Cmd+K. When true, an inline fuzzy-match picker
      /// appears at the bottom of the action list for quickly adding
      /// an action template with its defaults.
      ActionPickerOpen: bool
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

type Effect =
    | RunSketchSolve of SketchDrag
    | FinalizeSketchDrag of SketchDrag
    | ResolveAllSketches

type Message =
    | SelectAction of string
    /// Enter "edit this action's inputs" mode: the action list expands
    /// one row per editable input below the action.
    | StartWiring of ActionId
    | StopWiring
    /// Move the focus cursor within the edit expansion. `0` = action
    /// row; `1..N` = input rows.
    | SetEditFocus of int
    /// Enter the input-field sub-edit mode for the given field. Also
    /// seeds `RefPickIdx` (caller picks the initial candidate index
    /// for ref fields; 0 for scalars) and optionally an initial
    /// string value that overrides the input's current rendering (used
    /// to forward the first keystroke when typing over a scalar row).
    | StartEditingInputField of ActionParamField * int * string option
    /// Leave the input-field sub-edit mode. Discards any pending pick.
    | StopEditingInputField
    /// Move the ref-pick cursor while sub-editing a ref field.
    | SetRefPickIdx of int
    /// Inline action-picker at the bottom of the action list.
    | OpenActionPicker
    | CloseActionPicker
    /// Add a fresh action from a template with default params.
    | QuickAddAction of ActionTemplate
    | SetHoveredTarget of SelectionTarget option
    | SetSelectedTargets of SelectionTarget list
    | AddDefaultAction of ActionTemplate * string
    | AddAction of DocAction
    | UpdateAction of string * DocAction
    | RemoveAction of string
    | ReorderActions of string list
    /// `v` shortcut: cycle the selected action's visibility through
    /// the modes its kind supports (see Document.cycleVisibility).
    | CycleActionVisibility of ActionId
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
    /// Start a translate gizmo drag on the given action. The viewer
    /// captures initial x/y/z so Cancel can revert.
    | BeginGizmoDrag of GizmoDrag
    /// Apply new absolute x/y/z values to the dragging Translate.
    /// Patches Doc + slot values in place — no recompile.
    | UpdateGizmoDrag of x: float * y: float * z: float
    | FinishGizmoDrag
    | CancelGizmoDrag
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

    /// The kind name (case-preserved) used by `freshActionId` to pick a
    /// filename-safe prefix. Order matches the `ActionTemplate` DU.
    let templateKindName =
        function
        | SphereTemplate -> "Sphere"
        | CylinderTemplate -> "Cylinder"
        | BoxTemplate -> "Box"
        | HalfPlaneTemplate -> "HalfPlane"
        | TranslateTemplate -> "Translate"
        | RotateTemplate -> "Rotate"
        | MoveTemplate -> "Move"
        | UnionTemplate -> "Union"
        | SubtractTemplate -> "Subtract"
        | IntersectTemplate -> "Intersect"
        | SketchTemplate -> "Sketch"
        | FromSketchTemplate -> "FromSketch"
        | ThickenTemplate -> "Thicken"
        | ShellTemplate -> "Shell"
        | MeshTemplate -> "Mesh"

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
        let doc = Document.emptyDocument ()
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
          ActiveGizmoDrag = None
          ConstraintPlacementMode = None
          ConstraintPlacementDraft = None
          ConstraintPlacementCursor = None
          WiringActionId = None
          EditFocusIdx = 0
          EditingInputField = None
          EditingInputInitial = None
          RefPickIdx = 0
          ActionPickerOpen = false
          ViewerMode = Raymarch }

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

    let private isSketchTarget =
        function
        | TargetPoint _
        | TargetLine _
        | TargetCircle _
        | TargetArc _
        | TargetLoop _
        | TargetDimension _ -> true
        | _ -> false

    let isValidSelectionTarget (state: EditorState) target =
        match target with
        | TargetFrameOrigin _ -> belongsToActiveSketch state target
        | _ when isSketchTarget target -> belongsToActiveSketch state target
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
        let activeGizmoDrag =
            state.ActiveGizmoDrag
            |> Option.filter (fun drag ->
                state.Doc.Actions |> List.exists (fun a -> a.Id = drag.ActionId))
        // If the action being wired was
        // removed, exit wiring mode so the action list doesn't keep
        // rendering phantom bubbles.
        let wiringActionId =
            state.WiringActionId
            |> Option.filter (fun id -> state.Doc.Actions |> List.exists (fun a -> a.Id = id))
        let editFocusIdx =
            match wiringActionId with
            | Some _ -> max 0 state.EditFocusIdx
            | None -> 0
        let editingInputField =
            match wiringActionId with
            | Some _ -> state.EditingInputField
            | None -> None
        let editingInputInitial =
            match editingInputField with
            | Some _ -> state.EditingInputInitial
            | None -> None
        let refPickIdx =
            match editingInputField with
            | Some _ -> max 0 state.RefPickIdx
            | None -> 0
        { state with
            SketchEditMode = next.EditMode
            SketchTool = next.Tool
            SketchToolPoints = if next.Tool = "none" then [] else state.SketchToolPoints
            SketchToolPointRefs = if next.Tool = "none" then [] else state.SketchToolPointRefs
            LineChainStartPointId = if next.Tool = "line" then state.LineChainStartPointId else None
            EditingDimension = editingDimension
            ActiveSketchDrag = activeSketchDrag
            PendingSketchDragCommit = if activeSketchDrag.IsSome then state.PendingSketchDragCommit else false
            ActiveGizmoDrag = activeGizmoDrag
            ConstraintPlacementMode = next.ConstraintPlacementMode |> Option.bind tryConstraintPlacementKind
            ConstraintPlacementCursor = constraintPlacementCursor
            ConstraintPlacementDraft = constraintPlacementDraft
            WiringActionId = wiringActionId
            EditFocusIdx = editFocusIdx
            EditingInputField = editingInputField
            EditingInputInitial = editingInputInitial
            RefPickIdx = refPickIdx }

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
            ActiveGizmoDrag = None
            SolvedSketchParams = Map.empty
            ConstraintPlacementMode = None
            ConstraintPlacementDraft = None
            ConstraintPlacementCursor = None
            WiringActionId = None
            EditFocusIdx = 0
            EditingInputField = None
            EditingInputInitial = None
            RefPickIdx = 0
            ActionPickerOpen = false }
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

    let paletteMaybeBuild (state: EditorState) =
        let paletteState = Palette.toState state.PaletteSession state.Compiled.TypeMap state.Doc
        if paletteState.Mode = "done" then
            match state.PaletteSession.PickedKind with
            | Some kind ->
                let actionId = Document.freshActionId kind state.Doc
                match Palette.buildAction state.PaletteSession actionId with
                | Some action ->
                    // Drop straight into sketch-edit mode on a fresh
                    // Sketch action — same QoL as the sidebar's
                    // AddDefaultAction path.
                    let enterSketchEdit =
                        match action.Kind with
                        | Sketch _ -> true
                        | _ -> false
                    { state with
                        Doc = Document.addAction action state.Doc
                        PaletteSession = Palette.empty
                        SketchEditMode =
                            if enterSketchEdit then true else state.SketchEditMode }
                    |> recompileState
                | None ->
                    { state with PaletteSession = Palette.empty }
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
        | BeginGizmoDrag drag ->
            { state with ActiveGizmoDrag = Some drag }, noEffects
        | UpdateGizmoDrag(x, y, z) ->
            match state.ActiveGizmoDrag with
            | Some drag ->
                let nextDoc =
                    state.Doc
                    |> Document.patchParamValue drag.ActionId TranslateX (VFloat x)
                    |> Document.patchParamValue drag.ActionId TranslateY (VFloat y)
                    |> Document.patchParamValue drag.ActionId TranslateZ (VFloat z)
                let updates =
                    [ { ActionId = drag.ActionId; Path = "x" }, x
                      { ActionId = drag.ActionId; Path = "y" }, y
                      { ActionId = drag.ActionId; Path = "z" }, z ]
                { state with
                    Doc = nextDoc
                    SlotValues = patchSlotValues state.SlotValues state.Compiled updates },
                noEffects
            | None -> state, noEffects
        | FinishGizmoDrag ->
            { state with ActiveGizmoDrag = None }, noEffects
        | CancelGizmoDrag ->
            match state.ActiveGizmoDrag with
            | Some drag ->
                let nextDoc =
                    state.Doc
                    |> Document.patchParamValue drag.ActionId TranslateX (VFloat drag.InitialX)
                    |> Document.patchParamValue drag.ActionId TranslateY (VFloat drag.InitialY)
                    |> Document.patchParamValue drag.ActionId TranslateZ (VFloat drag.InitialZ)
                let updates =
                    [ { ActionId = drag.ActionId; Path = "x" }, drag.InitialX
                      { ActionId = drag.ActionId; Path = "y" }, drag.InitialY
                      { ActionId = drag.ActionId; Path = "z" }, drag.InitialZ ]
                { state with
                    Doc = nextDoc
                    SlotValues = patchSlotValues state.SlotValues state.Compiled updates
                    ActiveGizmoDrag = None },
                noEffects
            | None -> state, noEffects
        | _ ->
            let next =
                match message with
                | SelectAction id ->
                    // Selecting an action exits wiring mode (which is
                    // scoped to one action).
                    { state with
                        Doc = Document.select id state.Doc
                        WiringActionId = None
                        EditFocusIdx = 0
                        EditingInputField = None
                        EditingInputInitial = None
                        RefPickIdx = 0 }
                | StartWiring actionId ->
                    match state.Doc.Actions |> List.tryFind (fun a -> a.Id = actionId) with
                    | Some action ->
                        // Entering edit mode on a Sketch action also
                        // drops us into sketch-edit mode so the user
                        // can start drawing immediately.
                        let enterSketchEdit =
                            match action.Kind with
                            | Sketch _ -> true
                            | _ -> false
                        { state with
                            WiringActionId = Some actionId
                            EditFocusIdx = 0
                            EditingInputField = None
                            EditingInputInitial = None
                            RefPickIdx = 0
                            SketchEditMode = if enterSketchEdit then true else state.SketchEditMode }
                        |> normalizeState
                    | None -> state
                | StopWiring ->
                    { state with
                        WiringActionId = None
                        EditFocusIdx = 0
                        EditingInputField = None
                        EditingInputInitial = None
                        RefPickIdx = 0 }
                | SetEditFocus idx ->
                    { state with
                        EditFocusIdx = max 0 idx
                        EditingInputField = None
                        EditingInputInitial = None
                        RefPickIdx = 0 }
                | StartEditingInputField(field, idx, initial) ->
                    { state with
                        EditingInputField = Some field
                        EditingInputInitial = initial
                        RefPickIdx = max 0 idx }
                | StopEditingInputField ->
                    { state with
                        EditingInputField = None
                        EditingInputInitial = None
                        RefPickIdx = 0 }
                | SetRefPickIdx idx ->
                    { state with RefPickIdx = max 0 idx }
                | OpenActionPicker ->
                    { state with ActionPickerOpen = true }
                | CloseActionPicker ->
                    { state with ActionPickerOpen = false }
                | QuickAddAction template ->
                    // Same shape as `AddDefaultAction` but the id is
                    // generated from the current doc (so the UI doesn't
                    // have to thread it through). Closes the picker and
                    // drops straight into edit mode on the new action so
                    // the user can start tweaking inputs immediately.
                    let kindName = templateKindName template
                    let id = Document.freshActionId kindName state.Doc
                    let kind = actionTemplateKind template
                    let action : DocAction =
                        { Id = id
                          Name = None
                          Kind = kind
                          Visibility = Document.defaultVisibility kind }
                    let enterSketchEdit =
                        match action.Kind with
                        | Sketch _ -> true
                        | _ -> false
                    { state with
                        Doc = Document.addAction action state.Doc
                        ActionPickerOpen = false
                        WiringActionId = Some id
                        EditFocusIdx = 0
                        EditingInputField = None
                        EditingInputInitial = None
                        RefPickIdx = 0
                        SketchEditMode =
                            if enterSketchEdit then true else state.SketchEditMode }
                    |> recompileState
                | SetHoveredTarget hoveredTarget ->
                    { state with HoveredTarget = hoveredTarget }
                | SetSelectedTargets selectedTargets ->
                    { state with SelectedTargets = selectedTargets }
                | AddDefaultAction(template, id) ->
                    let kind = actionTemplateKind template
                    let action : DocAction =
                        { Id = id
                          Name = None
                          Kind = kind
                          Visibility = Document.defaultVisibility kind }
                    let next = { state with Doc = Document.addAction action state.Doc }
                    // Drop straight into sketch-edit mode on a fresh
                    // Sketch action — the user otherwise has to click
                    // the edit toggle as their first action every time.
                    let next =
                        match template with
                        | SketchTemplate -> { next with SketchEditMode = true }
                        | _ -> next
                    next |> recompileState
                | AddAction action ->
                    { state with Doc = Document.addAction action state.Doc } |> recompileState
                | UpdateAction(id, action) ->
                    { state with Doc = Document.updateAction id action state.Doc } |> recompileState
                | RemoveAction id ->
                    { state with Doc = Document.removeAction id state.Doc } |> recompileState
                | ReorderActions ids ->
                    { state with Doc = Document.reorder ids state.Doc } |> recompileState
                | CycleActionVisibility actionId ->
                    match state.Doc.Actions |> List.tryFind (fun a -> a.Id = actionId) with
                    | Some action ->
                        let next = Document.cycleVisibility action.Kind action.Visibility
                        { state with Doc = Document.setVisibility actionId next state.Doc }
                        |> normalizeState
                    | None -> state
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
                | CancelSketchDrag
                | BeginGizmoDrag _
                | UpdateGizmoDrag _
                | FinishGizmoDrag
                | CancelGizmoDrag ->
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
                    |> paletteMaybeBuild
                | PaletteSetScalarField(key, value) ->
                    { state with PaletteSession = Palette.setScalarField key value state.PaletteSession }
                | PaletteCommitScalars ->
                    { state with PaletteSession = Palette.commitScalars state.PaletteSession }
                    |> paletteMaybeBuild
                | PaletteFinish idSuffix ->
                    { state with PaletteSession = Palette.skipToEnd state.PaletteSession }
                    |> paletteMaybeBuild
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
                | SetActionSlotValue _
                | CommitEditingDimension _
                | ViewerPlaceConstraint _
                | AddConstraintFromSelection _ -> [ ResolveAllSketches ]
                | _ -> noEffects
            next, effects
