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
    | BezierTool

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
      Compiled: BlockCompiled
      SlotValues: float array
      SolvedSketchParams: Map<string, float32[]>
      HoveredTarget: SelectionTarget option
      SelectedTargets: SelectionTarget list
      SketchEditMode: bool
      /// Whether the Monaco-backed script editor panel is open. Toggled
      /// by `ToggleScriptEditor`. The Shell mounts a 3-column layout
      /// (block list + script panel + viewer) when true.
      ScriptEditorOpen: bool
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
      /// Block IDs whose inline input rows are currently expanded under
      /// the block list.
      ExpandedBlockIds: Set<Server.Lang.Notebook.BlockId>
      /// Last notebook eval error message, or None. Surfaced by the
      /// BlockList panel.
      LastNotebookError: string option
      /// Cached MathIR bytes from the last successful `RunNotebook`.
      /// Viewer.fs subscribes to this and uploads on ref-change.
      /// `obj` boxes a JS Uint8Array (Fable interop).
      LastNotebookBytes: obj option
      /// Per-block typecheck errors from the last `recompileNotebook`.
      /// Indexed by `BlockId`; entry-list never empty when present.
      /// Surfaced as `.has-error` row styling + tooltip in BlockList.
      NotebookBlockErrors: Map<Server.Lang.Notebook.BlockId, string list>
      /// Per-block resolved output type from the last `recompileNotebook`.
      /// Used for ref-drop type filtering — the BlockList drag-over
      /// handler consults this to gate drops by `Type.T` compatibility.
      NotebookBlockOutputs: Map<Server.Lang.Notebook.BlockId, Server.Lang.Type.T>
      /// MathIR from the last successful compile. Held in F# memory so the
      /// viewer's per-block GPU pipelines (e.g. field-slice overlay) can
      /// emit WGSL evaluators without re-parsing `LastNotebookBytes`.
      NotebookIr: Server.Lang.MathIr.MathIR option
      /// MathIR root expression per Field block. Keyed by `BlockId`. Used
      /// alongside `NotebookIr` by the field-slice renderer to compile
      /// one shader function per visible-as-field-lines block.
      NotebookFieldExprByBlock: Map<Server.Lang.Notebook.BlockId, Server.Lang.MathIr.Expr>
      /// Click-to-pick state for block ref inputs. When `Some(blockId,
      /// paramName)`, the BlockList renders that bubble as the active
      /// pick target and treats subsequent clicks on type-compatible
      /// upstream rows as a wire commit. Cleared by Escape, by clicking
      /// the same bubble again, or once the pick commits.
      EditingBlockRef: (Server.Lang.Notebook.BlockId * string) option }

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
    | SetHoveredTarget of SelectionTarget option
    | SetSelectedTargets of SelectionTarget list
    | DeleteIntent
    | ViewerHover of PickCandidateInput list
    | ViewerPick of string * PickCandidateInput list
    | StartEditingDimension of int
    | CancelEditingDimension
    | CommitEditingDimension of float
    | ViewerDimensionClickTarget
    | BeginSketchDrag of SketchDrag
    | UpdateSketchDragTarget of LabelPos
    | ApplySketchSolveResult of SketchDrag * float32[]
    | ApplyResolvedSketchResult of string * float32[]
    | FinishSketchDrag
    | CancelSketchDrag
    | ScenePointerDown of target: Pickable * ray: PointerRay * mods: PointerMods
    | ScenePointerMove of ray: PointerRay
    | ScenePointerUp of ray: PointerRay
    | SceneCancel
    | ViewerToolClick of float * float
    | ViewerPlaceConstraint of float * float
    | ToggleSketchEdit
    /// Show / hide the Monaco-backed script editor panel between the
    /// block list and the viewer. The panel mounts the document's
    /// `ScriptSourceText`; closing the panel doesn't tear down the
    /// editor instance (the host element is re-parented, not destroyed).
    | ToggleScriptEditor
    /// Replace `Doc.ScriptSourceText`. Dispatched (debounced) from the
    /// Monaco editor's onDidChangeModelContent. Triggers a notebook
    /// recompile so user-defined specs in the new source land in the
    /// BlockList palette and downstream blocks reference the new bodies.
    | UpdateScriptSource of string
    | SetSketchTool of SketchToolKind
    | ToggleConstraintPlacement of ConstraintPlacementKind
    | AddConstraintFromSelection of GeometricConstraintKind
    | DeleteSketchConstraint of int
    | SetConstraintPlacementCursor of (string * LabelPos) option
    | ReplaceDocument of Document
    | ClearModel
    // ── Typed-block notebook ─────────────────────────────────────────────
    /// Add a native block instantiating the named `BlockSpec` (e.g.
    /// "sphere", "translate"). Defaults from the spec populate scalar
    /// args; refs default to unwired.
    | AddNativeBlock of specName: string
    /// Add an empty Sketch block (XY plane, no entities).
    | AddSketchBlock
    /// Add a reference-image block (XY plane, empty URL, 5×5 world
    /// units, fully opaque). The user fills in the URL via the
    /// block-panel editor; the viewer fetches and caches the texture
    /// asynchronously.
    | AddImageBlock
    /// Replace the URL on an image block. Empty string clears the
    /// texture; non-empty string triggers a fetch in the viewer.
    | SetBlockImageUrl of Server.Lang.Notebook.BlockId * string
    /// Replace the sketch-plane (XY/XZ/YZ) on an image block.
    | SetBlockImagePlane of Server.Lang.Notebook.BlockId * Server.SketchPlane
    /// Replace the world-coord origin on an image block.
    | SetBlockImageOrigin of Server.Lang.Notebook.BlockId * Server.Vec3
    /// Replace the (width, height) world-unit size on an image block.
    | SetBlockImageSize of Server.Lang.Notebook.BlockId * width: float * height: float
    /// Replace the opacity (0..1) on an image block.
    | SetBlockImageOpacity of Server.Lang.Notebook.BlockId * float
    | SelectBlock of Server.Lang.Notebook.BlockId
    /// Replace a single named arg on a block (scalar drag-edit, or
    /// rewiring a ref bubble). The arg is a full DSL expression —
    /// `ENumber n` for scalars, `EVar name` / `EPath [name; ...]` for
    /// wires, anything else for richer cases.
    | SetBlockArg of Server.Lang.Notebook.BlockId * paramName: string * Server.Lang.Ast.Expr
    /// Remove a block's arg entry (disconnect a wire / clear a scalar).
    /// Absent keys behave as unwired at compose time.
    | RemoveBlockArg of Server.Lang.Notebook.BlockId * paramName: string
    | ExpandBlock of Server.Lang.Notebook.BlockId
    | CollapseBlock of Server.Lang.Notebook.BlockId
    | RenameBlock of Server.Lang.Notebook.BlockId * string
    | DeleteBlock of Server.Lang.Notebook.BlockId
    /// Reorder a block in `Doc.Blocks`. `InsertBefore` names the target
    /// block; the moved block lands immediately before it. `None` moves
    /// the block to the end of the list. Used by the sidebar's
    /// drag-and-drop reordering (BlockList row dragstart → row drop).
    | MoveBlock of source: Server.Lang.Notebook.BlockId * insertBefore: Server.Lang.Notebook.BlockId option
    /// Cycle the block's `Visibility` through Hidden → Visible → FieldLines
    /// → Isosurface → Hidden. The badge in the block list is the visible
    /// trigger; the keyboard shortcut hooks the same message.
    | CycleBlockVisibility of Server.Lang.Notebook.BlockId
    /// Set the block's isosurface display colour palette index.
    | SetBlockColor of Server.Lang.Notebook.BlockId * colorIndex: int
    /// Replace a sketch block's `ActionSketch` payload (used by
    /// SketchAuthoring tools, gizmo drag commits, dimension edits).
    | UpdateSketchBlockSketch of Server.Lang.Notebook.BlockId * ActionSketch
    /// Change the plane (XY / XZ / YZ) a sketch block draws on. Existing
    /// 2D entity coords are reinterpreted in the new plane's local axes —
    /// the sketch keeps its shape but moves to the new world orientation.
    | SetSketchPlane of Server.Lang.Notebook.BlockId * SketchPlane
    /// Click-to-pick: enter pick mode for a block ref input. The
    /// BlockList highlights compatible upstream rows; the next click on
    /// such a row commits via `SetBlockArg` and exits pick mode.
    | BeginPickBlockRef of Server.Lang.Notebook.BlockId * paramName: string
    /// Leave pick mode without committing (Escape, click same bubble).
    | CancelPickBlockRef

module Editor =

    let private trySketchPointPosition (sketch: ActionSketch) (pointId: string) =
        sketch.Entities
        |> List.tryPick (function
            | REPoint(id, x, y) when id = pointId -> Some { X = x; Y = y }
            | _ -> None)

    let sketchToolName =
        function
        | NoSketchTool -> "none"
        | LineTool -> "line"
        | RectangleTool -> "rectangle"
        | RoundedRectangleTool -> "roundedRectangle"
        | CircleTool -> "circle"
        | ArcTool -> "arc"
        | BezierTool -> "bezier"

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

    /// Compile the notebook end-to-end: typecheck, eval, serialise. The
    /// returned `CompileResult` carries the kernel bytes (when the whole
    /// pipeline succeeds), per-block error and output-type maps, and a
    /// panel-level summary message. Used both for the initial seed and
    /// for every block-mutating reducer step via `recompileNotebook`.
    ///
    /// `ScriptSourceText` from the document is analysed every call —
    /// `UserScript.analyze` is cheap (parse + lambda walk over a tiny
    /// source) so a memo is overkill for first cut. Add one if profiling
    /// finds it on a hot path.
    let private compileNotebook (doc: Server.Document) : Server.Lang.NotebookCompose.CompileResult =
        let nb : Server.Lang.Notebook.Notebook = { NextId = doc.NextBlockId; Blocks = doc.Blocks }
        let userScript = Server.Lang.UserScript.analyze doc.ScriptSourceText
        Server.Lang.NotebookCompose.compileWith nb userScript

    let initState () =
        let doc = Document.emptyDocument ()
        let compiled = BlockCompile.compile doc.Blocks
        let nbResult = compileNotebook doc
        { Doc = doc
          Compiled = compiled
          SlotValues = Array.copy compiled.Slots.Values
          SolvedSketchParams = Map.empty
          HoveredTarget = None
          SelectedTargets = []
          SketchEditMode = false
          ScriptEditorOpen = false
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
          ExpandedBlockIds = Set.empty
          LastNotebookError = nbResult.Summary
          LastNotebookBytes = nbResult.Bytes
          NotebookBlockErrors = nbResult.BlockErrors
          NotebookBlockOutputs = nbResult.BlockOutputs
          NotebookIr = nbResult.Ir
          NotebookFieldExprByBlock = nbResult.FieldExprByBlock
          EditingBlockRef = None }

    /// Sketch-local scalar fields are always slot-backed; that's the only
    /// `ActionParamField` shape left after the action graph was retired.
    let isSlotBackedActionParamField =
        function
        | SketchEntityField _
        | SketchConstraintField _ -> true

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

    let resolveSketchTransform (_state: EditorState) (_origin: string option) (plane: SketchPlane) =
        // Block-sourced sketches sit at the world origin in their declared
        // plane; the action-graph frame chains that produced per-action
        // origins are gone. Frame origins per-block are a future feature.
        sketchPlaneTransform RigidTransform.Identity plane

    let resolvedFrames (_state: EditorState) : Map<ActionId, RigidTransform> =
        Map.empty

    /// ID of the sketch currently being edited, if any. Always a synthetic
    /// `@block_<n>` id sourced from `Doc.SelectedBlockId` — the action
    /// graph and its real action ids are gone.
    let activeSketchEditId (state: EditorState) =
        if not state.SketchEditMode then None
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
            | None -> None

    /// Ids of frame actions that appear before the active sketch.
    /// Always empty in notebook mode (no per-action frame chains).
    let sketchEditFrameIds (_state: EditorState) : Set<string> =
        Set.empty

    /// True when `target` belongs to the actively-edited sketch: either its
    /// own geometry (point/line/circle/arc/loop/dimension) or a frame origin
    /// the sketch is allowed to reference.
    let belongsToActiveSketch (state: EditorState) (target: SelectionTarget) =
        match activeSketchEditId state with
        | None -> false
        | Some sid ->
            match target with
            | TargetPoint(s, _) | TargetLine(s, _) | TargetCircle(s, _)
            | TargetArc(s, _) | TargetSpline(s, _) | TargetLoop(s, _) | TargetDimension(s, _) -> s = sid
            | TargetFrameOrigin f -> Set.contains f (sketchEditFrameIds state)
            | _ -> false

    let private isSketchTarget =
        function
        | TargetPoint _
        | TargetLine _
        | TargetCircle _
        | TargetArc _
        | TargetSpline _
        | TargetLoop _
        | TargetDimension _ -> true
        | _ -> false

    let isValidSelectionTarget (state: EditorState) target =
        // Pick mode (wiring a block ref via viewport click) overrides
        // the normal "sketch targets only inside sketch-edit mode" gate
        // for Loop and Primitive (line/arc/circle/spline) targets — a
        // user wiring a `Loop`- or `Primitive`-typed input must be able
        // to click any loop or primitive in any sketch in the scene,
        // not just the actively-edited one.
        let pickModeAllowsSketchTarget =
            state.EditingBlockRef.IsSome
            && (match target with
                | TargetLoop _ | TargetLine _ | TargetCircle _ | TargetArc _ | TargetSpline _ -> true
                | _ -> false)
        match target with
        | _ when pickModeAllowsSketchTarget -> true
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
    /// compiled state. Used as a lookup callback by SketchAuthoring. The
    /// only sketch source is `@block_<n>` SketchBlocks; they sit at the
    /// world origin in their declared plane.
    let trySketchContext (state: EditorState) (sketchId: string) =
        if sketchId.StartsWith "@block_" then
            let rest = sketchId.Substring 7
            match System.Int32.TryParse rest with
            | true, bid ->
                state.Doc.Blocks
                |> List.tryFind (fun b -> b.Id = bid)
                |> Option.bind (fun b ->
                    match b.Body with
                    | Server.Lang.Notebook.SketchBody data ->
                        Some(data.Sketch, resolveSketchTransform state None data.Plane)
                    | _ -> None)
            | _ -> None
        else None

    /// Action-graph type errors are gone. Notebook errors flow through
    /// `EditorState.LastNotebookError` / `NotebookBlockErrors`.
    let formatErrors (_errs: obj list) : ActionErrorView list = []

    let sketchUiState (state: EditorState) =
        let activeSketchId = activeSketchEditId state
        let placementCursor =
            match state.ConstraintPlacementCursor, activeSketchId with
            | Some(sketchId, position), Some activeId when state.SketchEditMode && activeId = sketchId -> Some(position.X, position.Y)
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
                | Some selected when next.EditMode && selected.Id = current.SketchId ->
                    SketchAuthoring.tryEditableDimension current.SketchId selected.Sketch current.ConstraintIndex
                | _ -> None)
        let activeSketchId = activeSketchEditId state
        let constraintPlacementCursor =
            match next.ConstraintPlacementMode |> Option.bind tryConstraintPlacementKind, state.ConstraintPlacementCursor, activeSketchId with
            | Some _, Some(sketchId, pos), Some activeId when next.EditMode && next.Tool = "none" && activeId = sketchId -> Some(sketchId, pos)
            | _ -> None
        let constraintPlacementDraft =
            match next.ConstraintPlacementMode |> Option.bind tryConstraintPlacementKind, state.ConstraintPlacementDraft, activeSketchId with
            | Some kind, Some draft, Some activeId when next.EditMode && next.Tool = "none" && draft.SketchId = activeId && draft.Kind = constraintPlacementName kind -> Some draft
            | _ -> None
        let activeSketchDrag =
            match state.ActiveSketchDrag, activeSketchId with
            | Some drag, Some activeId when next.EditMode && activeId = drag.SketchId -> Some drag
            | _ -> None
        let activeSession =
            state.ActiveSession
            |> Option.filter (fun _session ->
                // Action gizmo sessions are dormant — never alive in
                // notebook mode; drop them on every normalise pass.
                false)
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
            ExpandedBlockIds = state.ExpandedBlockIds }

    /// Re-run the notebook compose+typecheck+evaluate pipeline against
    /// the current `Doc.Blocks` and refresh the cached error/output/bytes
    /// fields. Called by every reducer arm that mutates blocks so the UI
    /// (BlockList row styling, ref-drop type filter) and the kernel push
    /// (Viewer subscribes to `LastNotebookBytes`) stay in sync.
    let recompileNotebook (state: EditorState) : EditorState =
        let nbResult = compileNotebook state.Doc
        { state with
            LastNotebookError = nbResult.Summary
            LastNotebookBytes = nbResult.Bytes
            NotebookBlockErrors = nbResult.BlockErrors
            NotebookBlockOutputs = nbResult.BlockOutputs
            NotebookIr = nbResult.Ir
            NotebookFieldExprByBlock = nbResult.FieldExprByBlock }

    let recompileState (state: EditorState) =
        let compiled = BlockCompile.compile state.Doc.Blocks
        let next =
            { state with
                Compiled = compiled
                SlotValues = Array.copy compiled.Slots.Values
                SolvedSketchParams = Map.empty
                HoveredTarget = state.HoveredTarget |> Option.filter (isValidSelectionTarget { state with Compiled = compiled })
                SelectedTargets = state.SelectedTargets |> List.filter (isValidSelectionTarget { state with Compiled = compiled }) }
        normalizeState next

    let private patchSlotValues (slotValues: float array) (compiled: BlockCompiled) (updates: (SlotRef * float) list) =
        let resolved =
            updates
            |> List.map (fun (slotRef, value) ->
                match SlotTable.tryFindSlot compiled.Slots slotRef with
                | Some slot -> slot, value
                | None -> failwithf "Missing slot for %s/%s" slotRef.ActionId slotRef.Path)

        SlotTable.patchedValues slotValues resolved

    let private floatValueForSlotField _field value =
        ParamValue.asFloat value

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

    /// Wholesale document replacement with a full UI reset. Used by ClearModel.
    let loadDoc (doc: Document) (state: EditorState) =
        { state with
            Doc = doc
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
            ExpandedBlockIds = Set.empty }
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
        // Sketch edit mode with entities highlighted: delete the entities.
        if state.SketchEditMode && not state.SelectedTargets.IsEmpty then
            match SketchAuthoring.trySelectedSketch state.Doc with
            | Some ctx ->
                let nextDoc =
                    SketchAuthoring.withUpdatedSketch state.Doc ctx.Id (SketchAuthoring.deleteTargets state.SelectedTargets ctx.Sketch)
                { state with Doc = nextDoc }
                |> clearTransient
                |> recompileState
            | None -> state
        else
            // No entities highlighted — fall through to block / action delete.
            match state.Doc.SelectedBlockId with
            | Some bid ->
                let blocks = state.Doc.Blocks |> List.filter (fun b -> b.Id <> bid)
                { state with
                    Doc =
                        { state.Doc with
                            Blocks = blocks
                            SelectedBlockId = None }
                    SketchEditMode = false
                    EditingBlockRef = None }
                |> clearTransient
                |> recompileNotebook
                |> recompileState
            | None -> state

    let noEffects : Effect list = []

    let private isLabelDrag =
        function
        | { Kind = DragConstraintLabel _ } -> true
        | _ -> false

    /// Action-anchored gizmos (Translate/Rotate/HalfPlane) are dormant in
    /// notebook mode. The viewer modules still call `resolveActionTransform`
    /// (e.g. for context queries) but every call returns identity since
    /// there are no actions to anchor on.
    let resolveActionTransform (_state: EditorState) (_actionId: ActionId) : RigidTransform =
        RigidTransform.Identity

    // ── Scene interaction dispatch ────────────────────────────────────
    //
    // Action gizmo drag sessions are gone alongside the action graph.
    // The Scene* pointer messages still flow through the reducer for
    // future block-anchored interactions, but currently every entry
    // returns the state unchanged.

    let rotateAxisHandlePx = 76.0
    let rotateAngleHandlePx = 52.0

    let halfPlaneAxisDir (_state: EditorState) (_actionId: ActionId) : Vec3 =
        { X = 0.0; Y = 0.0; Z = 1.0 }

    let halfPlaneAxisIndex (_state: EditorState) (_actionId: ActionId) : int = 2

    let halfPlaneOffsetValue (_state: EditorState) (_actionId: ActionId) : float = 0.0

    let normalizedRotateAxisLocal (_state: EditorState) (_actionId: ActionId) : Vec3 =
        { X = 0.0; Y = 0.0; Z = 1.0 }

    let rotateAngleValue (_state: EditorState) (_actionId: ActionId) : float = 0.0

    let orthonormalBasisFromAxis (axis: Vec3) : Vec3 * Vec3 =
        let helper =
            if abs axis.Z < 0.9 then { X = 0.0; Y = 0.0; Z = 1.0 }
            else { X = 1.0; Y = 0.0; Z = 0.0 }
        let u = Vec3.Cross(helper, axis).Normalized
        let v = Vec3.Cross(axis, u).Normalized
        u, v

    let sceneOnPointerDown (state: EditorState) (_target: Pickable) (_ray: PointerRay) : EditorState * Effect list =
        state, noEffects

    let sceneOnPointerMove (state: EditorState) (_ray: PointerRay) : EditorState * Effect list =
        state, noEffects

    let sceneOnPointerUp (state: EditorState) : EditorState * Effect list =
        { state with ActiveSession = None }, noEffects

    let sceneOnCancel (state: EditorState) : EditorState * Effect list =
        { state with ActiveSession = None }, noEffects

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
                    let sketchOpt =
                        match SketchAuthoring.trySelectedSketch state.Doc with
                        | Some ctx when ctx.Id = drag.SketchId -> Some ctx.Sketch
                        | _ -> None
                    match sketchOpt with
                    | Some sketch ->
                        let fields = SketchSolve.localFields sketch
                        let count = min fields.Length solvedLocal.Length
                        let nextDoc =
                            ((state.Doc, [ 0 .. count - 1 ])
                             ||> List.fold (fun current index ->
                                 Document.patchParamValue drag.SketchId fields.[index] (VFloat(float solvedLocal.[index])) current))
                        // `recompileNotebook` rebuilds `LastNotebookBytes`
                        // (the IR shipped to the kernel). Without it, the
                        // patched sketch coords are visible in the overlay
                        // but constant-baked downstream IR — e.g.
                        // wing-remap-preview's endpoint constants — keeps
                        // the pre-drag values.
                        { state with
                            Doc = nextDoc
                            ActiveSketchDrag = None
                            PendingSketchDragCommit = false
                            SolvedSketchParams = Map.empty }
                        |> recompileNotebook
                        |> recompileState,
                        noEffects
                    | None ->
                        { state with
                            ActiveSketchDrag = None
                            PendingSketchDragCommit = false
                            SolvedSketchParams = Map.empty }, noEffects
                else
                    { state with SolvedSketchParams = state.SolvedSketchParams |> Map.add drag.SketchId solvedLocal }, noEffects
            | _ ->
                state, noEffects
        | ApplyResolvedSketchResult(sketchId, solvedLocal) ->
            match state.ActiveSketchDrag with
            | Some active when active.SketchId = sketchId ->
                state, noEffects
            | _ ->
                match SketchAuthoring.trySelectedSketch state.Doc with
                | Some ctx when ctx.Id = sketchId ->
                    { state with
                        SlotValues = SketchSolve.patchSolvedSketchSlots state.SlotValues state.Compiled.Slots sketchId ctx.Sketch solvedLocal
                        SolvedSketchParams = state.SolvedSketchParams |> Map.add sketchId solvedLocal },
                    noEffects
                | _ ->
                    state, noEffects
        | FinishSketchDrag ->
            // Drag-and-release shouldn't leave the point/label selected —
            // the click that started the drag put it in `SelectedTargets`
            // (via the mousedown's `ViewerPick`), but a drag is a transient
            // interaction, not a "pick this for further edits" gesture.
            // `FinishSketchDrag` only fires after `dragActive` flipped past
            // `DRAG_THRESHOLD_PX`, so plain click-to-select on a point
            // (which never crosses threshold and dispatches no
            // `FinishSketchDrag`) keeps the new selection.
            match state.ActiveSketchDrag with
            | Some drag ->
                if isLabelDrag drag then
                    { state with
                        ActiveSketchDrag = None
                        PendingSketchDragCommit = false
                        SelectedTargets = [] }, noEffects
                else
                    { state with
                        PendingSketchDragCommit = true
                        SelectedTargets = [] }, [ FinalizeSketchDrag drag ]
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
                | SetHoveredTarget hoveredTarget ->
                    { state with HoveredTarget = hoveredTarget }
                | SetSelectedTargets selectedTargets ->
                    { state with SelectedTargets = selectedTargets }
                | DeleteIntent ->
                    applyDeleteIntent state
                | ViewerHover candidates ->
                    { state with
                        HoveredTarget =
                            reduceSelectionCandidates state candidates
                            |> Option.map (fun (target, _score, _action) -> target) }
                    |> normalizeState
                | ViewerPick(intent, candidates) ->
                    let resolved = reduceSelectionCandidates state candidates
                    // Pick mode (wiring a block-input ref): if the user
                    // clicks a sketch loop OR a sketch primitive (line/
                    // circle/arc) in the viewport while `EditingBlockRef`
                    // is set, commit an `EPath [sketchName; memberId]`
                    // to the slot and exit pick mode. Sketch ActionId →
                    // BlockId via `SketchAuthoring.tryParseBlockId`.
                    // For primitives, the raw entity id (e.g. "leading")
                    // is mapped to the stable primitive id (e.g. "line_0")
                    // via the same `reconcilePrimitives` pass that the
                    // compose stage uses to build the sketch refinement.
                    let entityIdOf (e: Server.RenderEntity) =
                        match e with
                        | Server.REPoint(id, _, _)
                        | Server.RELine(id, _, _)
                        | Server.RECircle(id, _, _)
                        | Server.REArc(id, _, _, _)
                        | Server.REBezierCubic(id, _, _, _, _) -> id
                    let resolvePrimitiveId (sketchData: Server.Lang.Notebook.SketchData) (entityId: string) : string option =
                        let entitiesById =
                            sketchData.Sketch.Entities
                            |> List.map (fun e -> entityIdOf e, e)
                            |> Map.ofList
                        let allCurveIds =
                            sketchData.Sketch.Entities
                            |> List.choose (function
                                | Server.RELine(id, _, _)
                                | Server.RECircle(id, _, _)
                                | Server.REArc(id, _, _, _)
                                | Server.REBezierCubic(id, _, _, _, _) -> Some id
                                | _ -> None)
                        Server.SketchLoops.reconcilePrimitives [] entitiesById allCurveIds
                        |> List.tryFind (fun pr -> pr.EntityId = entityId)
                        |> Option.map (fun pr -> pr.Id)
                    let pickCommit =
                        match state.EditingBlockRef, resolved with
                        | Some(srcId, paramName), Some(TargetLoop(sketchActionId, loopId), _, _) ->
                            SketchAuthoring.tryParseBlockId sketchActionId
                            |> Option.bind (fun bid ->
                                state.Doc.Blocks
                                |> List.tryFind (fun b -> b.Id = bid)
                                |> Option.map (fun b -> srcId, paramName, b.Name, loopId))
                        | Some(srcId, paramName), Some(target, _, _) ->
                            let primitiveEntity =
                                match target with
                                | TargetLine(s, e) | TargetCircle(s, e) | TargetArc(s, e) | TargetSpline(s, e) -> Some(s, e)
                                | _ -> None
                            primitiveEntity
                            |> Option.bind (fun (sketchActionId, entityId) ->
                                SketchAuthoring.tryParseBlockId sketchActionId
                                |> Option.bind (fun bid ->
                                    state.Doc.Blocks
                                    |> List.tryFind (fun b -> b.Id = bid)
                                    |> Option.bind (fun b ->
                                        match b.Body with
                                        | Server.Lang.Notebook.SketchBody data ->
                                            resolvePrimitiveId data entityId
                                            |> Option.map (fun primId -> srcId, paramName, b.Name, primId)
                                        | _ -> None)))
                        | _ -> None
                    match pickCommit with
                    | Some(srcId, paramName, sketchName, loopId) ->
                        let path =
                            Server.Lang.AstBuilder.pathE [ sketchName; loopId ]
                        let nextBlocks =
                            state.Doc.Blocks
                            |> List.map (fun b ->
                                if b.Id <> srcId then b
                                else
                                    match b.Body with
                                    | Server.Lang.Notebook.NativeBody(specName, args) ->
                                        let nextArgs = Map.add paramName path args
                                        { b with Body = Server.Lang.Notebook.NativeBody(specName, nextArgs) }
                                    | _ -> b)
                        { state with
                            Doc = { state.Doc with Blocks = nextBlocks }
                            EditingBlockRef = None
                            HoveredTarget = None }
                        |> recompileNotebook
                    | None ->
                    match resolved with
                    | Some(target, _score, _actionId) ->
                        { state with
                            HoveredTarget = Some target
                            SelectedTargets = applySelectionIntent intent target state.SelectedTargets }
                        |> normalizeState
                    | None ->
                        { state with
                            HoveredTarget = None
                            SelectedTargets = if intent = "replace" then [] else state.SelectedTargets }
                        |> normalizeState
                | StartEditingDimension index ->
                    let editing =
                        match SketchAuthoring.trySelectedSketch state.Doc with
                        | Some selected when state.SketchEditMode ->
                            SketchAuthoring.tryEditableDimension selected.Id selected.Sketch index
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
                                    selected.Id
                                    (constraintPlacementName kind)
                                    state.HoveredTarget
                                    state.ConstraintPlacementDraft }
                        |> normalizeState
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
                            | Some(TargetPoint(sketchId, pointId)) when sketchId = selected.Id -> Some pointId
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
                                        Doc = SketchAuthoring.withUpdatedSketch state.Doc selected.Id result.Sketch
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
                | ToggleScriptEditor ->
                    { state with ScriptEditorOpen = not state.ScriptEditorOpen }
                | UpdateScriptSource source ->
                    // Mutate the document source then recompile so user-spec
                    // additions/deletions immediately flow through
                    // typeEnv-seeding and the BlockList palette.
                    { state with Doc = { state.Doc with ScriptSourceText = source } }
                    |> recompileNotebook
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
                            SketchAuthoring.withUpdatedSketch state.Doc ctx.Id (SketchAuthoring.removeConstraintAt index ctx.Sketch)
                        { state with Doc = nextDoc }
                        |> clearTransient
                        |> recompileState
                    | None ->
                        state
                | SetConstraintPlacementCursor cursor ->
                    { state with ConstraintPlacementCursor = cursor } |> normalizeState
                | ReplaceDocument doc ->
                    { state with Doc = doc } |> recompileState
                | ClearModel ->
                    loadDoc (Document.emptyDocument ()) state
                // ── Typed-block notebook ──────────────────────────────────
                | AddNativeBlock specName ->
                    // Try user-defined specs first, then built-ins. User
                    // specs have no scalar defaults today (the syntax
                    // doesn't carry them) so scalar params land at 0.0.
                    let userScript =
                        Server.Lang.UserScript.analyze state.Doc.ScriptSourceText
                    let typed, scalarDefaults =
                        match Map.tryFind specName userScript.Specs with
                        | Some us ->
                            ({ Server.Lang.TypeExtract.Params = us.Params
                               Server.Lang.TypeExtract.Output = us.Output } : Server.Lang.TypeExtract.ExtractedSpec),
                            Map.empty
                        | None ->
                            let spec = Server.Lang.BlockSpec.find specName
                            Server.Lang.BlockSpec.typedInterface spec, spec.ScalarDefaults
                    // Seed scalar params with their spec default (or 0.0)
                    // as an `ENumber` literal. Ref params start unwired —
                    // we omit them from the map; compose falls back to
                    // the unwired sentinel which surfaces a clean
                    // typecheck error.
                    let args =
                        typed.Params
                        |> List.choose (fun p ->
                            match p.Type with
                            | Server.Lang.Type.Scalar ->
                                let v =
                                    match Map.tryFind p.Name scalarDefaults with
                                    | Some d -> d
                                    | None -> 0.0
                                Some (p.Name, Server.Lang.AstBuilder.numE v)
                            | _ -> None)
                        |> Map.ofList
                    let id = state.Doc.NextBlockId
                    let block : Server.Lang.Notebook.Block = {
                        Id = id
                        Name = sprintf "%s_%d" specName id
                        Body = Server.Lang.Notebook.NativeBody(specName, args)
                        Visibility = Server.Lang.Notebook.VIsosurface
                        ColorIndex = 0
                        SlicePlane = Server.Lang.Notebook.defaultSlicePlane
                    }
                    { state with
                        Doc = {
                            state.Doc with
                                Blocks = state.Doc.Blocks @ [ block ]
                                NextBlockId = id + 1
                                SelectedBlockId = Some id } }
                    |> recompileNotebook
                | AddSketchBlock ->
                    let id = state.Doc.NextBlockId
                    let block : Server.Lang.Notebook.Block = {
                        Id = id
                        Name = sprintf "sketch_%d" id
                        Body = Server.Lang.Notebook.SketchBody {
                            Sketch = ActionSketch.empty
                            Plane = SketchPlane.defaults
                        }
                        Visibility = Server.Lang.Notebook.VIsosurface
                        ColorIndex = 0
                        SlicePlane = Server.Lang.Notebook.defaultSlicePlane
                    }
                    { state with
                        Doc = {
                            state.Doc with
                                Blocks = state.Doc.Blocks @ [ block ]
                                NextBlockId = id + 1
                                SelectedBlockId = Some id
                        }
                        SketchEditMode = true }
                    |> recompileNotebook
                | AddImageBlock ->
                    let id = state.Doc.NextBlockId
                    let block : Server.Lang.Notebook.Block = {
                        Id = id
                        Name = sprintf "image_%d" id
                        Body = Server.Lang.Notebook.ImageBody {
                            Url = ""
                            Plane = SketchPlane.defaults
                            Origin = { X = 0.0; Y = 0.0; Z = 0.0 }
                            Width = 5.0
                            Height = 5.0
                            Opacity = 1.0
                        }
                        Visibility = Server.Lang.Notebook.VIsosurface
                        ColorIndex = 0
                        SlicePlane = Server.Lang.Notebook.defaultSlicePlane
                    }
                    { state with
                        Doc = {
                            state.Doc with
                                Blocks = state.Doc.Blocks @ [ block ]
                                NextBlockId = id + 1
                                SelectedBlockId = Some id } }
                    |> recompileNotebook
                | SetBlockImageUrl(blockId, url) ->
                    let updated =
                        state.Doc.Blocks
                        |> List.map (fun b ->
                            if b.Id <> blockId then b else
                                match b.Body with
                                | Server.Lang.Notebook.ImageBody data ->
                                    { b with Body = Server.Lang.Notebook.ImageBody { data with Url = url } }
                                | _ -> b)
                    { state with Doc = { state.Doc with Blocks = updated } }
                    |> recompileNotebook
                | SetBlockImagePlane(blockId, plane) ->
                    let updated =
                        state.Doc.Blocks
                        |> List.map (fun b ->
                            if b.Id <> blockId then b else
                                match b.Body with
                                | Server.Lang.Notebook.ImageBody data ->
                                    { b with Body = Server.Lang.Notebook.ImageBody { data with Plane = plane } }
                                | _ -> b)
                    { state with Doc = { state.Doc with Blocks = updated } }
                    |> recompileNotebook
                | SetBlockImageOrigin(blockId, origin) ->
                    let updated =
                        state.Doc.Blocks
                        |> List.map (fun b ->
                            if b.Id <> blockId then b else
                                match b.Body with
                                | Server.Lang.Notebook.ImageBody data ->
                                    { b with Body = Server.Lang.Notebook.ImageBody { data with Origin = origin } }
                                | _ -> b)
                    { state with Doc = { state.Doc with Blocks = updated } }
                    |> recompileNotebook
                | SetBlockImageSize(blockId, w, h) ->
                    let updated =
                        state.Doc.Blocks
                        |> List.map (fun b ->
                            if b.Id <> blockId then b else
                                match b.Body with
                                | Server.Lang.Notebook.ImageBody data ->
                                    { b with Body = Server.Lang.Notebook.ImageBody { data with Width = w; Height = h } }
                                | _ -> b)
                    { state with Doc = { state.Doc with Blocks = updated } }
                    |> recompileNotebook
                | SetBlockImageOpacity(blockId, op) ->
                    let updated =
                        state.Doc.Blocks
                        |> List.map (fun b ->
                            if b.Id <> blockId then b else
                                match b.Body with
                                | Server.Lang.Notebook.ImageBody data ->
                                    { b with Body = Server.Lang.Notebook.ImageBody { data with Opacity = op } }
                                | _ -> b)
                    { state with Doc = { state.Doc with Blocks = updated } }
                    |> recompileNotebook
                | SelectBlock id ->
                    let block = state.Doc.Blocks |> List.tryFind (fun b -> b.Id = id)
                    let sketchEdit =
                        match block with
                        | Some b ->
                            match b.Body with
                            | Server.Lang.Notebook.SketchBody _ -> true
                            | _ -> false
                        | None -> state.SketchEditMode
                    { state with
                        Doc = { state.Doc with SelectedBlockId = Some id }
                        SketchEditMode = sketchEdit }
                | MoveBlock(sourceId, insertBefore) ->
                    // Pluck `sourceId` out of the list and reinsert it at the
                    // position indicated by `insertBefore`. No-op if the
                    // source doesn't exist, if it's already in the right
                    // place, or if `insertBefore = Some sourceId` (would
                    // collapse to identity). Recompile because reordering
                    // can change the visible-field declaration order, which
                    // matters for the kernel union root.
                    match insertBefore with
                    | Some t when t = sourceId -> state
                    | _ ->
                        let source = state.Doc.Blocks |> List.tryFind (fun b -> b.Id = sourceId)
                        match source with
                        | None -> state
                        | Some src ->
                            let without = state.Doc.Blocks |> List.filter (fun b -> b.Id <> sourceId)
                            let next =
                                match insertBefore with
                                | None -> without @ [ src ]
                                | Some t ->
                                    let idx = without |> List.tryFindIndex (fun b -> b.Id = t)
                                    match idx with
                                    | Some i ->
                                        let head, tail = List.splitAt i without
                                        head @ [ src ] @ tail
                                    | None -> without @ [ src ]
                            { state with Doc = { state.Doc with Blocks = next } }
                            |> recompileNotebook
                | SetBlockArg(id, paramName, newArg) ->
                    let updated =
                        state.Doc.Blocks
                        |> List.map (fun b ->
                            if b.Id <> id then b
                            else
                                match b.Body with
                                | Server.Lang.Notebook.NativeBody(specName, args) ->
                                    let nextArgs = Map.add paramName newArg args
                                    { b with Body = Server.Lang.Notebook.NativeBody(specName, nextArgs) }
                                | _ -> b)
                    // If the new arg references a downstream block by name
                    // (simple `EVar` or `EPath` head), hoist that block to
                    // sit just before the target so the notebook stays a
                    // DAG without forcing the user to think about
                    // declaration order. Richer Exprs (arithmetic, etc.)
                    // are skipped for hoisting.
                    let referencedName : string option =
                        match newArg.Node with
                        | Server.Lang.Ast.EVar id -> Some id.Name
                        | Server.Lang.Ast.EPath (head :: _) -> Some head.Name
                        | _ -> None
                    let blocks =
                        match referencedName with
                        | Some name ->
                            let idxOf bid =
                                updated
                                |> List.tryFindIndex (fun b -> b.Id = bid)
                            let refIdOpt =
                                updated
                                |> List.tryFind (fun b -> b.Name = name)
                                |> Option.map (fun b -> b.Id)
                            match refIdOpt with
                            | Some refId ->
                                match idxOf refId, idxOf id with
                                | Some refIdx, Some tgtIdx when refIdx > tgtIdx ->
                                    let refBlock = updated.[refIdx]
                                    let withoutRef =
                                        updated |> List.filter (fun b -> b.Id <> refId)
                                    let newTgtIdx =
                                        withoutRef |> List.findIndex (fun b -> b.Id = id)
                                    let before, after = List.splitAt newTgtIdx withoutRef
                                    before @ [ refBlock ] @ after
                                | _ -> updated
                            | None -> updated
                        | None -> updated
                    // SetBlockArg commits a pick (or any other arg edit);
                    // either way, exit pick mode if we were in it.
                    { state with
                        Doc = { state.Doc with Blocks = blocks }
                        EditingBlockRef = None }
                    |> recompileNotebook
                | RemoveBlockArg(id, paramName) ->
                    let updated =
                        state.Doc.Blocks
                        |> List.map (fun b ->
                            if b.Id <> id then b
                            else
                                match b.Body with
                                | Server.Lang.Notebook.NativeBody(specName, args) ->
                                    let nextArgs = Map.remove paramName args
                                    { b with Body = Server.Lang.Notebook.NativeBody(specName, nextArgs) }
                                | _ -> b)
                    { state with
                        Doc = { state.Doc with Blocks = updated }
                        EditingBlockRef = None }
                    |> recompileNotebook
                | ExpandBlock id ->
                    { state with ExpandedBlockIds = Set.add id state.ExpandedBlockIds }
                | CollapseBlock id ->
                    { state with ExpandedBlockIds = Set.remove id state.ExpandedBlockIds }
                | RenameBlock(id, newName) ->
                    let blocks =
                        state.Doc.Blocks
                        |> List.map (fun b -> if b.Id = id then { b with Name = newName } else b)
                    { state with Doc = { state.Doc with Blocks = blocks } }
                    |> recompileNotebook
                | DeleteBlock id ->
                    let blocks = state.Doc.Blocks |> List.filter (fun b -> b.Id <> id)
                    let selectedBlock =
                        if state.Doc.SelectedBlockId = Some id then None
                        else state.Doc.SelectedBlockId
                    { state with
                        Doc = {
                            state.Doc with
                                Blocks = blocks
                                SelectedBlockId = selectedBlock } }
                    |> recompileNotebook
                | CycleBlockVisibility id ->
                    let nextVis (v: Server.Lang.Notebook.BlockVisibility) =
                        match v with
                        | Server.Lang.Notebook.VHidden     -> Server.Lang.Notebook.VIsosurface
                        | Server.Lang.Notebook.VIsosurface -> Server.Lang.Notebook.VFieldLines
                        | Server.Lang.Notebook.VFieldLines -> Server.Lang.Notebook.VHidden
                    let blocks =
                        state.Doc.Blocks
                        |> List.map (fun b ->
                            if b.Id = id then { b with Visibility = nextVis b.Visibility }
                            else b)
                    { state with Doc = { state.Doc with Blocks = blocks } }
                    |> recompileNotebook
                | SetBlockColor(id, colorIndex) ->
                    let blocks =
                        state.Doc.Blocks
                        |> List.map (fun b ->
                            if b.Id = id then { b with ColorIndex = colorIndex }
                            else b)
                    { state with Doc = { state.Doc with Blocks = blocks } }
                    |> recompileNotebook
                | UpdateSketchBlockSketch(id, sketch) ->
                    let blocks =
                        state.Doc.Blocks
                        |> List.map (fun b ->
                            if b.Id <> id then b
                            else
                                match b.Body with
                                | Server.Lang.Notebook.SketchBody data ->
                                    let normalized = Server.SketchLoops.normalize sketch
                                    { b with Body = Server.Lang.Notebook.SketchBody { data with Sketch = normalized } }
                                | _ -> b)
                    { state with Doc = { state.Doc with Blocks = blocks } }
                    |> recompileNotebook
                | SetSketchPlane(id, plane) ->
                    // `recompileState` (after `recompileNotebook`) so the
                    // viewer-side sketch transform (`resolveSketchTransform`,
                    // which reads `Plane`) is recomputed against the new
                    // orientation. Sketches are pre-bound in the eval env so
                    // both the notebook IR and the overlay see the change.
                    let blocks =
                        state.Doc.Blocks
                        |> List.map (fun b ->
                            if b.Id <> id then b
                            else
                                match b.Body with
                                | Server.Lang.Notebook.SketchBody data ->
                                    { b with Body = Server.Lang.Notebook.SketchBody { data with Plane = plane } }
                                | _ -> b)
                    { state with Doc = { state.Doc with Blocks = blocks } }
                    |> recompileNotebook
                    |> recompileState
                | BeginPickBlockRef(id, paramName) ->
                    // Toggle: clicking the same bubble cancels.
                    let next =
                        match state.EditingBlockRef with
                        | Some(prevId, prevName) when prevId = id && prevName = paramName -> None
                        | _ -> Some(id, paramName)
                    { state with EditingBlockRef = next }
                | CancelPickBlockRef ->
                    { state with EditingBlockRef = None }
            let effects =
                match message with
                | CommitEditingDimension _
                | ViewerPlaceConstraint _
                | AddConstraintFromSelection _ -> [ ResolveAllSketches ]
                | _ -> noEffects
            next, effects
