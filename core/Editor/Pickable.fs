namespace Server

// ---------------------------------------------------------------------------
// Pickables — a flat list of what the GPU pick shader should hit-test.
//
// Each pickable carries (a) a sequential integer PickId that the GPU emits,
// (b) a semantic reference so the editor can map a pick back to a sketch
// entity, and (c) slot refs for coords so the pick shader samples the
// same params uniform buffer as the SDF shader.
//
// The list is produced once per compile in `BlockCompile.compile` and
// rebuilt on every block edit. Ids are stable for the duration of one
// compiled state but not across recompiles.
// ---------------------------------------------------------------------------

type PickId = int

/// Translate-gizmo handle kinds. Indices: 0=X 1=Y 2=Z for axes; 0=XY
/// 1=YZ 2=XZ for planes. The handle carries no geometry — all
/// positions are derived at render time from the action's resolved
/// world transform + the camera's world-per-pixel. The pick-compute
/// dispatch builds matching geometry buffers and shares the pick id.
type GizmoHandle =
    | GAxis of axis: int
    | GPlane of plane: int
    | GRotateAxis
    | GRotateAngle
    | GHalfPlaneAxis of axis: int
    | GHalfPlaneOffset

type Pickable =
    // Sketch entities — coords live in existing sketch entity slots.
    | PickPoint of pickId: PickId * sketchId: ActionId * entityId: string * xSlot: Slot * ySlot: Slot
    | PickLine of pickId: PickId * sketchId: ActionId * entityId: string * startP: SlotPt2 * endP: SlotPt2
    | PickCircle of pickId: PickId * sketchId: ActionId * entityId: string * center: SlotPt2 * radiusSlot: Slot
    | PickArc of pickId: PickId * sketchId: ActionId * entityId: string * startP: SlotPt2 * endP: SlotPt2 * center: SlotPt2 * clockwise: bool
    // Cubic Bezier. Carries the four control points' slot pairs so the
    // GPU pick pass can tessellate the curve in lock-step with the SDF
    // pipeline (same control-point coords).
    | PickSpline of pickId: PickId * sketchId: ActionId * entityId: string * p0: SlotPt2 * p1: SlotPt2 * p2: SlotPt2 * p3: SlotPt2
    // Loops — referenced by ordered entity ids.
    | PickLoop of pickId: PickId * sketchId: ActionId * loopId: string * entityIds: string list
    // Dimensions — label anchor lives in existing labelPosition slots.
    | PickDimension of pickId: PickId * sketchId: ActionId * constraintIndex: int * anchor: SlotPt2
    // Frame gizmos.
    | PickFrameOrigin of pickId: PickId * frameId: ActionId
    | PickFrameAxis of pickId: PickId * frameId: ActionId * part: string
    // Translate-gizmo handles (ephemeral — rebuilt per frame for the
    // selected Translate action, not baked into the compile result).
    | PickGizmoHandle of pickId: PickId * actionId: ActionId * handle: GizmoHandle

type SelectionTarget =
    | TargetPoint of sketchId: ActionId * entityId: string
    | TargetLine of sketchId: ActionId * entityId: string
    | TargetCircle of sketchId: ActionId * entityId: string
    | TargetArc of sketchId: ActionId * entityId: string
    | TargetSpline of sketchId: ActionId * entityId: string
    | TargetLoop of sketchId: ActionId * loopId: string
    | TargetDimension of sketchId: ActionId * constraintIndex: int
    | TargetFrameOrigin of frameId: ActionId
    | TargetFrameAxis of frameId: ActionId * part: string
    /// Gizmo handles are not real "selectable" things — they trigger
    /// drag sessions on click. This target exists only so the
    /// reduceCandidates / priority plumbing can flow them through the
    /// same pipeline as everything else; they never end up in
    /// `SelectedTargets`.
    | TargetGizmoHandle of actionId: ActionId * handle: GizmoHandle

type PickCandidateInput =
    { PickId: PickId
      Score: float32 }

module Pickable =

    /// Extract the PickId from any variant.
    let pickId =
        function
        | PickPoint(id, _, _, _, _) -> id
        | PickLine(id, _, _, _, _) -> id
        | PickCircle(id, _, _, _, _) -> id
        | PickArc(id, _, _, _, _, _, _) -> id
        | PickSpline(id, _, _, _, _, _, _) -> id
        | PickLoop(id, _, _, _) -> id
        | PickDimension(id, _, _, _) -> id
        | PickFrameOrigin(id, _) -> id
        | PickFrameAxis(id, _, _) -> id
        | PickGizmoHandle(id, _, _) -> id

    /// Resolve a pickable to the ActionId the server should select when
    /// this pickable is clicked.
    let targetAction =
        function
        | PickPoint(_, sketchId, _, _, _) -> sketchId
        | PickLine(_, sketchId, _, _, _) -> sketchId
        | PickCircle(_, sketchId, _, _, _) -> sketchId
        | PickArc(_, sketchId, _, _, _, _, _) -> sketchId
        | PickSpline(_, sketchId, _, _, _, _, _) -> sketchId
        | PickLoop(_, sketchId, _, _) -> sketchId
        | PickDimension(_, sketchId, _, _) -> sketchId
        | PickFrameOrigin(_, frameId) -> frameId
        | PickFrameAxis(_, frameId, _) -> frameId
        | PickGizmoHandle(_, actionId, _) -> actionId

    let selectionTarget =
        function
        | PickPoint(_, sketchId, entityId, _, _) -> TargetPoint(sketchId, entityId)
        | PickLine(_, sketchId, entityId, _, _) -> TargetLine(sketchId, entityId)
        | PickCircle(_, sketchId, entityId, _, _) -> TargetCircle(sketchId, entityId)
        | PickArc(_, sketchId, entityId, _, _, _, _) -> TargetArc(sketchId, entityId)
        | PickSpline(_, sketchId, entityId, _, _, _, _) -> TargetSpline(sketchId, entityId)
        | PickLoop(_, sketchId, loopId, _) -> TargetLoop(sketchId, loopId)
        | PickDimension(_, sketchId, constraintIndex, _) -> TargetDimension(sketchId, constraintIndex)
        | PickFrameOrigin(_, frameId) -> TargetFrameOrigin(frameId)
        | PickFrameAxis(_, frameId, part) -> TargetFrameAxis(frameId, part)
        | PickGizmoHandle(_, actionId, handle) -> TargetGizmoHandle(actionId, handle)

    let sameTarget target pickable =
        selectionTarget pickable = target

    /// Selection priority ordering, lower wins. Gizmo handles are the
    /// narrowest interactive targets — they sit above everything else
    /// so clicks on an axis shaft never get stolen by an underlying
    /// sketch point. Frames sit below sketch entities but above loops
    /// — fat frame gizmos shouldn't steal clicks from sketch geometry,
    /// but still beat the filled-face hit region of a loop.
    let selectionPriority =
        function
        | TargetGizmoHandle _ -> 0
        | TargetPoint _ -> 1
        | TargetLine _
        | TargetCircle _
        | TargetArc _
        | TargetSpline _ -> 2
        | TargetDimension _ -> 3
        | TargetFrameOrigin _
        | TargetFrameAxis _ -> 4
        | TargetLoop _ -> 5

    let reduceCandidates (pickables: Pickable list) (candidates: PickCandidateInput list) : Pickable option =
        let byId = pickables |> List.map (fun p -> pickId p, p) |> Map.ofList
        candidates
        |> List.choose (fun c -> Map.tryFind c.PickId byId |> Option.map (fun p -> c, p))
        |> List.sortBy (fun (c, p) -> selectionPriority (selectionTarget p), c.Score, pickId p)
        |> List.tryHead
        |> Option.map snd
