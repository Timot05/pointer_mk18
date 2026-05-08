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
      /// Unified pointer-drag session on the 3D scene (currently
      /// translate-gizmo axis/plane drags). Sketch point / label drags
      /// still live in `ActiveSketchDrag` — they use a different
      /// effect flow (async solver) and will join `ActiveSession` in a
      /// follow-up pass.
      ActiveSession: SceneSession option
      ConstraintPlacementMode: ConstraintPlacementKind option
      ConstraintPlacementDraft: ConstraintPlacementDraft option
      ConstraintPlacementCursor: (string * LabelPos) option
      /// Action IDs whose inline input rows are currently expanded in
      /// the action list. Multiple actions can be open at once —
      /// `Right` arrow expands the currently-focused action, `Left`
      /// collapses it (see Shortcuts.fs). `normalizeState` trims any
      /// ids whose action no longer exists.
      ExpandedActionIds: Set<ActionId>
      /// Row within the currently-selected action that has keyboard
      /// focus. `0` = the action row itself; `1..N` = the N input rows
      /// (only meaningful while the selected action is in
      /// `ExpandedActionIds`; otherwise forced to 0).
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
      ViewerMode: ViewerMode
      /// Notebook-mode state. If Some, the script editor modal is mounted
      /// on this block id. Set by `OpenScriptEditor`, cleared by
      /// `CloseScriptEditor` or `DeleteBlock`.
      OpenedScriptBlockId: Server.Lang.Notebook.BlockId option
      /// Last notebook eval error message, or None. Surfaced by the
      /// ScriptEditor + BlockList panels.
      LastNotebookError: string option
      /// Cached MathIR bytes from the last successful `RunNotebook`.
      /// Viewer.fs subscribes to this and uploads on ref-change.
      /// `obj` boxes a JS Uint8Array (Fable interop).
      LastNotebookBytes: obj option }

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
    /// Expand the given action's input rows in the action list.
    /// Idempotent — re-expanding a visible action is a no-op.
    | ExpandAction of ActionId
    /// Collapse the given action's input rows. No-op when the action
    /// is already collapsed.
    | CollapseAction of ActionId
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
    /// Unified pointer-down on the 3D scene. The viewer resolves the
    /// top pickable under the cursor and hands us a `PointerRay`; the
    /// reducer decides whether to start a drag session or fall
    /// through to selection-style logic. Only gizmo handles start a
    /// `SceneSession` today — other pickable kinds continue to use
    /// their current messages (ViewerPick, BeginSketchDrag, …).
    | ScenePointerDown of target: Pickable * ray: PointerRay * mods: PointerMods
    /// Every mousemove during an active session. Delegated to the
    /// active session's update logic. No-op when `ActiveSession = None`.
    | ScenePointerMove of ray: PointerRay
    /// Mouseup — finishes the active session (commits in place; the
    /// slot values have already been patched incrementally by
    /// `ScenePointerMove`).
    | ScenePointerUp of ray: PointerRay
    /// Escape / external cancel — reverts the session's slot patches
    /// to the captured drag-start values.
    | SceneCancel
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
    // ── Notebook / blocks ─────────────────────────────────────────────────
    /// Add an empty Script block at the end of the list, name "block_<n>",
    /// open the script editor on it, and select it.
    | AddScriptBlock
    /// Add an empty Sketch block (XY plane, no entities). Selected but
    /// the script editor stays closed.
    | AddSketchBlock
    | SelectBlock of Server.Lang.Notebook.BlockId
    | OpenScriptEditor of Server.Lang.Notebook.BlockId
    | CloseScriptEditor
    | UpdateBlockSource of Server.Lang.Notebook.BlockId * string
    | UpdateBlockInputs of Server.Lang.Notebook.BlockId * (string * string) list
    | RenameBlock of Server.Lang.Notebook.BlockId * string
    | DeleteBlock of Server.Lang.Notebook.BlockId
    /// Replace a sketch block's `ActionSketch` payload (used by
    /// SketchAuthoring tools, gizmo drag commits, dimension edits).
    | UpdateSketchBlockSketch of Server.Lang.Notebook.BlockId * ActionSketch
    /// Re-evaluate the notebook → MathIR → bytes; either set
    /// `LastNotebookBytes` (for Viewer.fs to upload) or `LastNotebookError`.
    | RunNotebook

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

    /// Compile the seed notebook into MathIR bytes so the kernel renders
    /// something on first frame instead of a blank canvas. Failure here
    /// just produces an empty initial state — the user sees the seed
    /// blocks in the sidebar and can hit Run manually.
    let private compileInitialNotebook (blocks: Server.Lang.Notebook.Block list) (nextId: Server.Lang.Notebook.BlockId) =
        let nb : Server.Lang.Notebook.Notebook = { NextId = nextId; Blocks = blocks }
        match Server.Lang.NotebookEval.compileView nb None with
        | Ok(ir, root) ->
            // MathIrCodec uses Fable-only Uint8Array bindings; on .NET
            // (xUnit tests) those throw. Skip silently — tests don't need
            // the rendered bytes.
            try
                Some (Server.Lang.MathIrCodec.serialize ir root), None
            with _ ->
                None, None
        | Error msg -> None, Some msg

    let initState () =
        let doc = Document.emptyDocument ()
        let compiled = Pipeline.compile doc.Actions doc.Blocks
        let initialBytes, initialError = compileInitialNotebook doc.Blocks doc.NextBlockId
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
          ActiveSession = None
          ConstraintPlacementMode = None
          ConstraintPlacementDraft = None
          ConstraintPlacementCursor = None
          ExpandedActionIds = Set.empty
          EditFocusIdx = 0
          EditingInputField = None
          EditingInputInitial = None
          RefPickIdx = 0
          ActionPickerOpen = false
          ViewerMode = IntervalKernel
          OpenedScriptBlockId = None
          LastNotebookError = initialError
          LastNotebookBytes = initialBytes }

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

    /// ID of the sketch currently being edited, if any. Returns either a
    /// real action id (for legacy Sketch actions) or a synthetic
    /// `@block_<n>` id (for SketchBlocks); both round-trip through
    /// `SketchAuthoring.withUpdatedSketch` correctly.
    let activeSketchEditId (state: EditorState) =
        if not state.SketchEditMode then None
        else
            match state.Doc.SelectedBlockId with
            | Some bid ->
                state.Doc.Blocks
                |> List.tryFind (fun b -> b.Id = bid)
                |> Option.bind (fun b ->
                    match b.Kind with
                    | Server.Lang.Notebook.SketchBlock _ ->
                        Some (SketchAuthoring.blockSketchId bid)
                    | _ -> None)
                |> function
                   | Some _ as r -> r
                   | None ->
                        state.Doc.SelectedId
                        |> Option.bind (fun id ->
                            state.Doc.Actions
                            |> List.tryFind (fun a -> a.Id = id)
                            |> Option.bind (fun a -> match a.Kind with Sketch _ -> Some id | _ -> None))
            | None ->
                state.Doc.SelectedId
                |> Option.bind (fun id ->
                    state.Doc.Actions
                    |> List.tryFind (fun a -> a.Id = id)
                    |> Option.bind (fun a -> match a.Kind with Sketch _ -> Some id | _ -> None))

    /// Ids of frame actions that appear before the active sketch — the
    /// frame origins the sketch is allowed to reference. Block-sourced
    /// sketches have no upstream action context yet (they live at the
    /// world origin in their declared plane), so this is empty for them.
    let sketchEditFrameIds (state: EditorState) : Set<string> =
        match activeSketchEditId state with
        | None -> Set.empty
        | Some sketchId when sketchId.StartsWith "@block_" -> Set.empty
        | Some sketchId ->
            match state.Doc.Actions |> List.tryFindIndex (fun a -> a.Id = sketchId) with
            | None -> Set.empty
            | Some i ->
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
    /// compiled state. Used as a lookup callback by SketchAuthoring. Also
    /// handles synthetic `@block_<n>` ids for SketchBlock-sourced sketches —
    /// those have an identity transform (no upstream frame chain yet).
    let trySketchContext (state: EditorState) (sketchId: string) =
        if sketchId.StartsWith "@block_" then
            let rest = sketchId.Substring 7
            match System.Int32.TryParse rest with
            | true, bid ->
                state.Doc.Blocks
                |> List.tryFind (fun b -> b.Id = bid)
                |> Option.bind (fun b ->
                    match b.Kind with
                    | Server.Lang.Notebook.SketchBlock data ->
                        Some(data.Sketch, resolveSketchTransform state None data.Plane)
                    | _ -> None)
            | _ -> None
        else
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
        let activeSession =
            state.ActiveSession
            |> Option.filter (fun session ->
                let id = SceneSession.actionId session
                state.Doc.Actions |> List.exists (fun a -> a.Id = id))
        // Drop any expanded ids whose action has been deleted.
        let actionIds = state.Doc.Actions |> List.map (fun a -> a.Id) |> Set.ofList
        let expandedActionIds = Set.intersect state.ExpandedActionIds actionIds
        // Focus index is meaningful only while the selected action is
        // expanded — otherwise clamp to 0 (the action row).
        let selectedExpanded =
            match state.Doc.SelectedId with
            | Some id -> Set.contains id expandedActionIds
            | None -> false
        let editFocusIdx =
            if selectedExpanded then max 0 state.EditFocusIdx else 0
        let editingInputField =
            if selectedExpanded then state.EditingInputField else None
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
            ActiveSession = activeSession
            ConstraintPlacementMode = next.ConstraintPlacementMode |> Option.bind tryConstraintPlacementKind
            ConstraintPlacementCursor = constraintPlacementCursor
            ConstraintPlacementDraft = constraintPlacementDraft
            ExpandedActionIds = expandedActionIds
            EditFocusIdx = editFocusIdx
            EditingInputField = editingInputField
            EditingInputInitial = editingInputInitial
            RefPickIdx = refPickIdx }

    let recompileState (state: EditorState) =
        let compiled = Pipeline.compile state.Doc.Actions state.Doc.Blocks
        // Visibility is bound to the output type (Field vs Frame etc).
        // When a ref rewire flips the type — e.g. a Translate switches
        // from wrapping a field to wrapping a frame — snap the
        // visibility to a mode that still makes sense.
        let normalizedActions =
            state.Doc.Actions
            |> List.map (fun a ->
                let isField =
                    Map.tryFind a.Id compiled.TypeMap = Some FieldType.Field
                let normalized = Document.normalizeVisibility a.Kind isField a.Visibility
                if normalized = a.Visibility then a
                else { a with Visibility = normalized })
        let anyChanged =
            (state.Doc.Actions, normalizedActions)
            ||> List.exists2 (fun a b -> a.Visibility <> b.Visibility)
        let nextDoc =
            if anyChanged then { state.Doc with Actions = normalizedActions }
            else state.Doc
        let next =
            { state with
                Doc = nextDoc
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
            ActiveSession = None
            SolvedSketchParams = Map.empty
            ConstraintPlacementMode = None
            ConstraintPlacementDraft = None
            ConstraintPlacementCursor = None
            ExpandedActionIds = Set.empty
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

    /// Walks down a FieldNode accumulating its leading rigid transforms
    /// so the viewer's field-slice renderer and the translate-gizmo
    /// session can share one way of resolving "where does this field
    /// live in the world". For frame-output Translates the transform
    /// comes from `Compiled.Frames`; for field-output Translates the
    /// compiled `FieldSurface.Field` tree encodes the accumulated
    /// translate/rotate chain.
    let rec leadingFieldTransform (state: EditorState) (field: FieldNode) (acc: RigidTransform) =
        let slot (s: Slot) = state.SlotValues.[s]
        match field with
        | FTranslate(x, y, z, child) ->
            let step = RigidTransform.translate { X = slot x; Y = slot y; Z = slot z }
            leadingFieldTransform state child (acc * step)
        | FRotate(ax, ay, az, angle, child) ->
            let step =
                RigidTransform.fromAxisAngle
                    { X = slot ax; Y = slot ay; Z = slot az }
                    (slot angle)
            leadingFieldTransform state child (acc * step)
        | FFieldOp(_, _, child) -> leadingFieldTransform state child acc
        | _ -> acc

    /// Resolve the world pose of an action whose output can be expressed as a
    /// leading rigid transform (currently Translate/Rotate gizmos).
    let resolveActionTransform (state: EditorState) (actionId: ActionId) : RigidTransform =
        match Map.tryFind actionId state.Compiled.Frames with
        | Some chain -> Frames.foldChain state.Compiled.Slots state.SlotValues chain
        | None ->
            state.Compiled.Surfaces
            |> List.tryFind (fun s -> s.ActionId = actionId)
            |> Option.map (fun s -> leadingFieldTransform state s.Field RigidTransform.Identity)
            |> Option.defaultValue RigidTransform.Identity

    // ── Scene interaction dispatch ────────────────────────────────────
    //
    // Unified entry points for the `Scene*` pointer messages. Each
    // returns `(EditorState * Effect list)` — same contract as the
    // reducer itself so the top-level `update` just delegates.

    let rotateAxisHandlePx = 76.0
    let rotateAngleHandlePx = 52.0

    let private readTranslateSlot (state: EditorState) (actionId: ActionId) (path: string) : float =
        match SlotTable.tryFindSlot state.Compiled.Slots { ActionId = actionId; Path = path } with
        | Some s when s < state.SlotValues.Length -> state.SlotValues.[s]
        | _ -> 0.0

    let private readRotateSlot = readTranslateSlot

    let private patchRotateSlots
            (state: EditorState)
            (actionId: ActionId)
            (ax: float) (ay: float) (az: float)
            (angle: float) : Document * float array =
        let nextDoc =
            state.Doc
            |> Document.patchParamValue actionId RotateAxisX (VFloat ax)
            |> Document.patchParamValue actionId RotateAxisY (VFloat ay)
            |> Document.patchParamValue actionId RotateAxisZ (VFloat az)
            |> Document.patchParamValue actionId RotateAngle (VFloat angle)
        let updates =
            [ { ActionId = actionId; Path = "ax" }, ax
              { ActionId = actionId; Path = "ay" }, ay
              { ActionId = actionId; Path = "az" }, az
              { ActionId = actionId; Path = "angle" }, angle ]
        nextDoc, patchSlotValues state.SlotValues state.Compiled updates

    let halfPlaneAxisDir (state: EditorState) (actionId: ActionId) : Vec3 =
        match state.Doc.Actions |> List.tryFind (fun a -> a.Id = actionId) with
        | Some { Kind = HalfPlane(axis, _, _) } ->
            match axis with
            | "X" -> { X = 1.0; Y = 0.0; Z = 0.0 }
            | "Y" -> { X = 0.0; Y = 1.0; Z = 0.0 }
            | _ -> { X = 0.0; Y = 0.0; Z = 1.0 }
        | _ -> { X = 0.0; Y = 0.0; Z = 1.0 }

    let halfPlaneAxisIndex (state: EditorState) (actionId: ActionId) : int =
        match state.Doc.Actions |> List.tryFind (fun a -> a.Id = actionId) with
        | Some { Kind = HalfPlane(axis, _, _) } ->
            match axis with
            | "X" -> 0
            | "Y" -> 1
            | _ -> 2
        | _ -> 2

    let private halfPlaneAxisName axisIndex =
        match axisIndex with
        | 0 -> "X"
        | 1 -> "Y"
        | _ -> "Z"

    let halfPlaneOffsetValue (state: EditorState) (actionId: ActionId) : float =
        readTranslateSlot state actionId "offset"

    let private patchHalfPlaneOffset
            (state: EditorState)
            (actionId: ActionId)
            (offset: float) : Document * float array =
        let nextDoc =
            state.Doc
            |> Document.patchParamValue actionId HalfPlaneOffset (VFloat offset)
        let updates =
            [ { ActionId = actionId; Path = "offset" }, offset ]
        nextDoc, patchSlotValues state.SlotValues state.Compiled updates

    let private setHalfPlaneAxis
            (state: EditorState)
            (actionId: ActionId)
            (axisIndex: int) : EditorState * Effect list =
        let axisName = halfPlaneAxisName axisIndex
        let currentIndex = halfPlaneAxisIndex state actionId
        if currentIndex = axisIndex then state, noEffects
        else
            let nextDoc =
                state.Doc
                |> Document.patchParamValue actionId HalfPlaneAxis (VString axisName)
            { state with Doc = nextDoc } |> recompileState, noEffects

    let normalizedRotateAxisLocal (state: EditorState) (actionId: ActionId) : Vec3 =
        let axis =
            { X = readRotateSlot state actionId "ax"
              Y = readRotateSlot state actionId "ay"
              Z = readRotateSlot state actionId "az" }
        if axis.LengthSq < 1e-9 then { X = 0.0; Y = 0.0; Z = 1.0 }
        else axis.Normalized

    let rotateAngleValue (state: EditorState) (actionId: ActionId) : float =
        readRotateSlot state actionId "angle"

    let orthonormalBasisFromAxis (axis: Vec3) : Vec3 * Vec3 =
        let helper =
            if abs axis.Z < 0.9 then { X = 0.0; Y = 0.0; Z = 1.0 }
            else { X = 1.0; Y = 0.0; Z = 0.0 }
        let u = Vec3.Cross(helper, axis).Normalized
        let v = Vec3.Cross(axis, u).Normalized
        u, v

    let private signedAngleAboutAxis (axis: Vec3) (basisU: Vec3) (basisV: Vec3) (dir: Vec3) : float =
        let x = Vec3.Dot(dir, basisU)
        let y = Vec3.Dot(dir, basisV)
        System.Math.Atan2(y, x)

    let private localAxis axisIndex : Vec3 =
        match axisIndex with
        | 0 -> { X = 1.0; Y = 0.0; Z = 0.0 }
        | 1 -> { X = 0.0; Y = 1.0; Z = 0.0 }
        | _ -> { X = 0.0; Y = 0.0; Z = 1.0 }

    let private planeAxes planeIndex : Vec3 * Vec3 =
        match planeIndex with
        | 0 -> localAxis 0, localAxis 1  // XY
        | 1 -> localAxis 1, localAxis 2  // YZ
        | _ -> localAxis 0, localAxis 2  // XZ

    let private patchTranslateSlots (state: EditorState) (actionId: ActionId) (x: float) (y: float) (z: float) : Document * float array =
        let nextDoc =
            state.Doc
            |> Document.patchParamValue actionId TranslateX (VFloat x)
            |> Document.patchParamValue actionId TranslateY (VFloat y)
            |> Document.patchParamValue actionId TranslateZ (VFloat z)
        let updates =
            [ { ActionId = actionId; Path = "x" }, x
              { ActionId = actionId; Path = "y" }, y
              { ActionId = actionId; Path = "z" }, z ]
        nextDoc, patchSlotValues state.SlotValues state.Compiled updates

    let private beginGizmoAxisDrag
            (state: EditorState) (actionId: ActionId) (axisIdx: int) (ray: PointerRay)
            : EditorState * Effect list =
        let xform = resolveActionTransform state actionId
        let axisDirWorld = xform.Rot.Rotate(localAxis axisIdx)
        match PointerRay.projectOntoAxis ray xform.Trans axisDirWorld with
        | None -> state, noEffects
        | Some t0 ->
            let session =
                { ActionId = actionId
                  AxisIndex = axisIdx
                  AxisDir = axisDirWorld
                  Anchor = xform.Trans
                  InitialX = readTranslateSlot state actionId "x"
                  InitialY = readTranslateSlot state actionId "y"
                  InitialZ = readTranslateSlot state actionId "z"
                  InitialT = t0 }
            { state with ActiveSession = Some(GizmoAxisDrag session) }, noEffects

    let private beginGizmoPlaneDrag
            (state: EditorState) (actionId: ActionId) (planeIdx: int) (ray: PointerRay)
            : EditorState * Effect list =
        let xform = resolveActionTransform state actionId
        let localU, localV = planeAxes planeIdx
        let axisU = xform.Rot.Rotate(localU)
        let axisV = xform.Rot.Rotate(localV)
        match PointerRay.intersectPlane ray xform.Trans axisU axisV with
        | None -> state, noEffects
        | Some(u0, v0) ->
            let session =
                { ActionId = actionId
                  PlaneIndex = planeIdx
                  AxisU = axisU
                  AxisV = axisV
                  Anchor = xform.Trans
                  InitialX = readTranslateSlot state actionId "x"
                  InitialY = readTranslateSlot state actionId "y"
                  InitialZ = readTranslateSlot state actionId "z"
                  InitialU = u0
                  InitialV = v0 }
            { state with ActiveSession = Some(GizmoPlaneDrag session) }, noEffects

    let private beginRotateAxisDrag
            (state: EditorState) (actionId: ActionId) : EditorState * Effect list =
        let xform = resolveActionTransform state actionId
        let axisLocal = normalizedRotateAxisLocal state actionId
        let axisWorld = xform.Rot.Rotate(axisLocal).Normalized
        let session =
            { ActionId = actionId
              Anchor = xform.Trans
              WorldToLocal = xform.Rot.Inverse
              FallbackWorldAxis = axisWorld
              InitialAxisX = axisLocal.X
              InitialAxisY = axisLocal.Y
              InitialAxisZ = axisLocal.Z }
        { state with ActiveSession = Some(RotateAxisDrag session) }, noEffects

    let private beginRotateAngleDrag
            (state: EditorState) (actionId: ActionId) (ray: PointerRay) : EditorState * Effect list =
        let xform = resolveActionTransform state actionId
        let axisLocal = normalizedRotateAxisLocal state actionId
        let axisWorld = xform.Rot.Rotate(axisLocal).Normalized
        let basisU, basisV = orthonormalBasisFromAxis axisWorld
        // Angle drag lives in the plane orthogonal to the current axis and
        // centered at the rotate origin. The viewer renders the guide using
        // the same basis; only its screen-space radius is viewer-specific.
        match PointerRay.intersectPlane ray xform.Trans basisU basisV with
        | None -> state, noEffects
        | Some _ ->
            let session =
                { ActionId = actionId
                  Center = xform.Trans
                  AxisWorld = axisWorld
                  BasisU = basisU
                  BasisV = basisV
                  InitialAngle = rotateAngleValue state actionId }
            { state with ActiveSession = Some(RotateAngleDrag session) }, noEffects

    let private beginHalfPlaneOffsetDrag
            (state: EditorState) (actionId: ActionId) (ray: PointerRay)
            : EditorState * Effect list =
        let axisDir = halfPlaneAxisDir state actionId
        match PointerRay.projectOntoAxis ray Vec3.Zero axisDir with
        | None -> state, noEffects
        | Some t0 ->
            let session =
                { ActionId = actionId
                  AxisDir = axisDir
                  Anchor = Vec3.Zero
                  InitialOffset = halfPlaneOffsetValue state actionId
                  InitialT = t0 }
            { state with ActiveSession = Some(HalfPlaneOffsetDrag session) }, noEffects

    let private updateGizmoAxisDrag (state: EditorState) (s: GizmoAxisDragSession) (ray: PointerRay) : EditorState * Effect list =
        match PointerRay.projectOntoAxis ray s.Anchor s.AxisDir with
        | None -> state, noEffects
        | Some t ->
            let dt = t - s.InitialT
            let nx, ny, nz =
                match s.AxisIndex with
                | 0 -> s.InitialX + dt, s.InitialY, s.InitialZ
                | 1 -> s.InitialX, s.InitialY + dt, s.InitialZ
                | _ -> s.InitialX, s.InitialY, s.InitialZ + dt
            let nextDoc, nextSlots = patchTranslateSlots state s.ActionId nx ny nz
            { state with Doc = nextDoc; SlotValues = nextSlots }, noEffects

    let private updateGizmoPlaneDrag (state: EditorState) (s: GizmoPlaneDragSession) (ray: PointerRay) : EditorState * Effect list =
        match PointerRay.intersectPlane ray s.Anchor s.AxisU s.AxisV with
        | None -> state, noEffects
        | Some(u, v) ->
            let du = u - s.InitialU
            let dv = v - s.InitialV
            let nx, ny, nz =
                match s.PlaneIndex with
                | 0 -> s.InitialX + du, s.InitialY + dv, s.InitialZ
                | 1 -> s.InitialX, s.InitialY + du, s.InitialZ + dv
                | _ -> s.InitialX + du, s.InitialY, s.InitialZ + dv
            let nextDoc, nextSlots = patchTranslateSlots state s.ActionId nx ny nz
            { state with Doc = nextDoc; SlotValues = nextSlots }, noEffects

    let private updateRotateAxisDrag (state: EditorState) (s: RotateAxisDragSession) (ray: PointerRay) : EditorState * Effect list =
        let worldDir = PointerRay.projectToSphereDirection ray s.Anchor s.FallbackWorldAxis
        let localDir = s.WorldToLocal.Rotate(worldDir).Normalized
        let nextDoc, nextSlots =
            patchRotateSlots state s.ActionId localDir.X localDir.Y localDir.Z (rotateAngleValue state s.ActionId)
        { state with Doc = nextDoc; SlotValues = nextSlots }, noEffects

    let private updateRotateAngleDrag (state: EditorState) (s: RotateAngleDragSession) (ray: PointerRay) : EditorState * Effect list =
        match PointerRay.intersectPlane ray s.Center s.BasisU s.BasisV with
        | None -> state, noEffects
        | Some (u, v) ->
            let dir = ({ X = u; Y = v; Z = 0.0 }.LengthSq)
            if dir < 1e-9 then state, noEffects
            else
                let worldDir = (u * s.BasisU + v * s.BasisV).Normalized
                let nextAngle = signedAngleAboutAxis s.AxisWorld s.BasisU s.BasisV worldDir
                let axis = normalizedRotateAxisLocal state s.ActionId
                let nextDoc, nextSlots =
                    patchRotateSlots state s.ActionId axis.X axis.Y axis.Z nextAngle
                { state with Doc = nextDoc; SlotValues = nextSlots }, noEffects

    let private updateHalfPlaneOffsetDrag
            (state: EditorState)
            (s: HalfPlaneOffsetDragSession)
            (ray: PointerRay) : EditorState * Effect list =
        match PointerRay.projectOntoAxis ray s.Anchor s.AxisDir with
        | None -> state, noEffects
        | Some t ->
            let nextOffset = s.InitialOffset + (t - s.InitialT)
            let nextDoc, nextSlots = patchHalfPlaneOffset state s.ActionId nextOffset
            { state with Doc = nextDoc; SlotValues = nextSlots }, noEffects

    let sceneOnPointerDown (state: EditorState) (target: Pickable) (ray: PointerRay) : EditorState * Effect list =
        match target with
        | PickGizmoHandle(_, actionId, GAxis axisIdx) -> beginGizmoAxisDrag state actionId axisIdx ray
        | PickGizmoHandle(_, actionId, GPlane planeIdx) -> beginGizmoPlaneDrag state actionId planeIdx ray
        | PickGizmoHandle(_, actionId, GRotateAxis) -> beginRotateAxisDrag state actionId
        | PickGizmoHandle(_, actionId, GRotateAngle) -> beginRotateAngleDrag state actionId ray
        | PickGizmoHandle(_, actionId, GHalfPlaneAxis axisIdx) -> setHalfPlaneAxis state actionId axisIdx
        | PickGizmoHandle(_, actionId, GHalfPlaneOffset) -> beginHalfPlaneOffsetDrag state actionId ray
        | _ ->
            // No non-gizmo pickables flow through Scene* this pass —
            // selection / sketch drag / etc. still use their existing
            // messages. Silently ignore anything else so future call
            // sites can widen the match without a breaking change.
            state, noEffects

    let sceneOnPointerMove (state: EditorState) (ray: PointerRay) : EditorState * Effect list =
        match state.ActiveSession with
        | Some (GizmoAxisDrag s) -> updateGizmoAxisDrag state s ray
        | Some (GizmoPlaneDrag s) -> updateGizmoPlaneDrag state s ray
        | Some (RotateAxisDrag s) -> updateRotateAxisDrag state s ray
        | Some (RotateAngleDrag s) -> updateRotateAngleDrag state s ray
        | Some (HalfPlaneOffsetDrag s) -> updateHalfPlaneOffsetDrag state s ray
        | None -> state, noEffects

    let sceneOnPointerUp (state: EditorState) : EditorState * Effect list =
        { state with ActiveSession = None }, noEffects

    let sceneOnCancel (state: EditorState) : EditorState * Effect list =
        match state.ActiveSession with
        | Some (GizmoAxisDrag s) ->
            let nextDoc, nextSlots = patchTranslateSlots state s.ActionId s.InitialX s.InitialY s.InitialZ
            { state with
                Doc = nextDoc
                SlotValues = nextSlots
                ActiveSession = None }, noEffects
        | Some (GizmoPlaneDrag s) ->
            let nextDoc, nextSlots = patchTranslateSlots state s.ActionId s.InitialX s.InitialY s.InitialZ
            { state with
                Doc = nextDoc
                SlotValues = nextSlots
                ActiveSession = None }, noEffects
        | Some (RotateAxisDrag s) ->
            let nextDoc, nextSlots =
                patchRotateSlots state s.ActionId s.InitialAxisX s.InitialAxisY s.InitialAxisZ (rotateAngleValue state s.ActionId)
            { state with
                Doc = nextDoc
                SlotValues = nextSlots
                ActiveSession = None }, noEffects
        | Some (RotateAngleDrag s) ->
            let axis = normalizedRotateAxisLocal state s.ActionId
            let nextDoc, nextSlots =
                patchRotateSlots state s.ActionId axis.X axis.Y axis.Z s.InitialAngle
            { state with
                Doc = nextDoc
                SlotValues = nextSlots
                ActiveSession = None }, noEffects
        | Some (HalfPlaneOffsetDrag s) ->
            let nextDoc, nextSlots =
                patchHalfPlaneOffset state s.ActionId s.InitialOffset
            { state with
                Doc = nextDoc
                SlotValues = nextSlots
                ActiveSession = None }, noEffects
        | None -> state, noEffects

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
        | ScenePointerDown(target, ray, _mods) ->
            sceneOnPointerDown state target ray
        | ScenePointerMove ray ->
            sceneOnPointerMove state ray
        | ScenePointerUp _ ->
            sceneOnPointerUp state
        | SceneCancel ->
            sceneOnCancel state
        | _ ->
            let next =
                match message with
                | SelectAction id ->
                    // Selection doesn't touch the expansion set any
                    // more — users can have several actions open and
                    // jump between them without losing context.
                    { state with
                        Doc = Document.select id state.Doc
                        EditFocusIdx = 0
                        EditingInputField = None
                        EditingInputInitial = None
                        RefPickIdx = 0 }
                | ExpandAction actionId ->
                    match state.Doc.Actions |> List.tryFind (fun a -> a.Id = actionId) with
                    | Some action ->
                        // Expanding a Sketch action also drops into
                        // sketch-edit mode so the user can start
                        // drawing immediately.
                        let enterSketchEdit =
                            match action.Kind with
                            | Sketch _ -> true
                            | _ -> false
                        { state with
                            ExpandedActionIds = Set.add actionId state.ExpandedActionIds
                            SketchEditMode = if enterSketchEdit then true else state.SketchEditMode }
                        |> normalizeState
                    | None -> state
                | CollapseAction actionId ->
                    // Dropping the focused action's expansion resets
                    // focus to the action row + clears sub-edit state.
                    let clearFocus = state.Doc.SelectedId = Some actionId
                    { state with
                        ExpandedActionIds = Set.remove actionId state.ExpandedActionIds
                        EditFocusIdx = if clearFocus then 0 else state.EditFocusIdx
                        EditingInputField = if clearFocus then None else state.EditingInputField
                        EditingInputInitial = if clearFocus then None else state.EditingInputInitial
                        RefPickIdx = if clearFocus then 0 else state.RefPickIdx }
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
                        ExpandedActionIds = Set.add id state.ExpandedActionIds
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
                        let isField =
                            Map.tryFind actionId state.Compiled.TypeMap = Some FieldType.Field
                        let next = Document.cycleVisibility action.Kind isField action.Visibility
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
                | ScenePointerDown _
                | ScenePointerMove _
                | ScenePointerUp _
                | SceneCancel ->
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
                    loadDoc {
                        Name = model.Name
                        Actions = model.Actions
                        SelectedId = selectedId
                        Blocks = []
                        NextBlockId = 0
                        SelectedBlockId = None
                    } state
                | ClearModel ->
                    loadDoc (Document.emptyDocument ()) state
                | SetViewerMode mode ->
                    { state with ViewerMode = mode }
                // ── Notebook / blocks ────────────────────────────────────
                | AddScriptBlock ->
                    let id = state.Doc.NextBlockId
                    let block : Server.Lang.Notebook.Block = {
                        Id = id
                        Name = sprintf "block_%d" id
                        Kind = Server.Lang.Notebook.ScriptBlock {
                            Source = ""
                            Inputs = []
                        }
                    }
                    { state with
                        Doc = {
                            state.Doc with
                                Blocks = state.Doc.Blocks @ [ block ]
                                NextBlockId = id + 1
                                SelectedBlockId = Some id
                        }
                        OpenedScriptBlockId = Some id }
                | AddSketchBlock ->
                    let id = state.Doc.NextBlockId
                    let block : Server.Lang.Notebook.Block = {
                        Id = id
                        Name = sprintf "sketch_%d" id
                        Kind = Server.Lang.Notebook.SketchBlock {
                            Sketch = ActionSketch.empty
                            Plane = SketchPlane.defaults
                        }
                    }
                    { state with
                        Doc = {
                            state.Doc with
                                Blocks = state.Doc.Blocks @ [ block ]
                                NextBlockId = id + 1
                                SelectedBlockId = Some id
                        }
                        // Drop straight into sketch-edit mode on a fresh
                        // SketchBlock — same QoL as adding a sketch action.
                        SketchEditMode = true }
                | SelectBlock id ->
                    let block = state.Doc.Blocks |> List.tryFind (fun b -> b.Id = id)
                    let opened, sketchEdit =
                        match block with
                        | Some b ->
                            match b.Kind with
                            | Server.Lang.Notebook.ScriptBlock _ -> Some id, false
                            | Server.Lang.Notebook.SketchBlock _ -> None, true
                        | None -> None, state.SketchEditMode
                    { state with
                        Doc = { state.Doc with SelectedBlockId = Some id }
                        OpenedScriptBlockId = opened
                        SketchEditMode = sketchEdit }
                | OpenScriptEditor id ->
                    { state with OpenedScriptBlockId = Some id }
                | CloseScriptEditor ->
                    { state with OpenedScriptBlockId = None }
                | UpdateBlockSource(id, src) ->
                    let blocks =
                        state.Doc.Blocks
                        |> List.map (fun b ->
                            if b.Id <> id then b
                            else
                                match b.Kind with
                                | Server.Lang.Notebook.ScriptBlock s ->
                                    { b with Kind = Server.Lang.Notebook.ScriptBlock { s with Source = src } }
                                | _ -> b)
                    { state with Doc = { state.Doc with Blocks = blocks } }
                | UpdateBlockInputs(id, inputs) ->
                    let blocks =
                        state.Doc.Blocks
                        |> List.map (fun b ->
                            if b.Id <> id then b
                            else
                                match b.Kind with
                                | Server.Lang.Notebook.ScriptBlock s ->
                                    { b with Kind = Server.Lang.Notebook.ScriptBlock { s with Inputs = inputs } }
                                | _ -> b)
                    { state with Doc = { state.Doc with Blocks = blocks } }
                | RenameBlock(id, newName) ->
                    let blocks =
                        state.Doc.Blocks
                        |> List.map (fun b -> if b.Id = id then { b with Name = newName } else b)
                    { state with Doc = { state.Doc with Blocks = blocks } }
                | DeleteBlock id ->
                    let blocks = state.Doc.Blocks |> List.filter (fun b -> b.Id <> id)
                    let selectedBlock =
                        if state.Doc.SelectedBlockId = Some id then None
                        else state.Doc.SelectedBlockId
                    let opened =
                        if state.OpenedScriptBlockId = Some id then None
                        else state.OpenedScriptBlockId
                    { state with
                        Doc = {
                            state.Doc with
                                Blocks = blocks
                                SelectedBlockId = selectedBlock
                        }
                        OpenedScriptBlockId = opened }
                | UpdateSketchBlockSketch(id, sketch) ->
                    let blocks =
                        state.Doc.Blocks
                        |> List.map (fun b ->
                            if b.Id <> id then b
                            else
                                match b.Kind with
                                | Server.Lang.Notebook.SketchBlock data ->
                                    { b with Kind = Server.Lang.Notebook.SketchBlock { data with Sketch = sketch } }
                                | _ -> b)
                    { state with Doc = { state.Doc with Blocks = blocks } }
                | RunNotebook ->
                    let nb : Server.Lang.Notebook.Notebook = {
                        NextId = state.Doc.NextBlockId
                        Blocks = state.Doc.Blocks
                    }
                    match Server.Lang.NotebookEval.compileView nb None with
                    | Ok(ir, root) ->
                        let bytes = Server.Lang.MathIrCodec.serialize ir root
                        { state with
                            LastNotebookBytes = Some bytes
                            LastNotebookError = None }
                    | Error msg ->
                        { state with LastNotebookError = Some msg }
            let effects =
                match message with
                | SetActionSlotValue _
                | CommitEditingDimension _
                | ViewerPlaceConstraint _
                | AddConstraintFromSelection _ -> [ ResolveAllSketches ]
                | _ -> noEffects
            next, effects
