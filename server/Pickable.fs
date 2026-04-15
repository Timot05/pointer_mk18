namespace Server

// ---------------------------------------------------------------------------
// Pickables — a flat list of what the GPU pick shader should hit-test.
//
// Each pickable carries (a) a sequential integer PickId that the GPU emits,
// (b) a semantic reference so the server can map a pick back to an action,
// and (c) slot refs for coords so the pick shader samples the same params
// uniform buffer as the SDF shader.
//
// The list is produced once per compile in Pipeline.buildPickables and sent
// to the viewer as part of /api/viewer/model. Ids are stable across
// recompiles as long as topology doesn't change.
// ---------------------------------------------------------------------------

type PickId = int

type Pickable =
    // Sketch entities — coords live in existing sketch entity slots.
    | PickPoint of pickId: PickId * sketchId: ActionId * entityId: string * xSlot: Slot * ySlot: Slot
    | PickLine of pickId: PickId * sketchId: ActionId * entityId: string * startP: SlotPt2 * endP: SlotPt2
    | PickCircle of pickId: PickId * sketchId: ActionId * entityId: string * center: SlotPt2 * radiusSlot: Slot
    | PickArc of pickId: PickId * sketchId: ActionId * entityId: string * startP: SlotPt2 * endP: SlotPt2 * center: SlotPt2 * clockwise: bool
    // Loops — referenced by ordered entity ids.
    | PickLoop of pickId: PickId * sketchId: ActionId * loopId: string * entityIds: string list
    // Dimensions — label anchor lives in existing labelPosition slots.
    | PickDimension of pickId: PickId * sketchId: ActionId * constraintIndex: int * anchor: SlotPt2
    // Frame gizmos.
    | PickFrameOrigin of pickId: PickId * frameId: ActionId
    | PickFrameAxis of pickId: PickId * frameId: ActionId * part: string
    // Coarse SDF surface — the whole surface of a Field-producing action.
    | PickSurface of pickId: PickId * actionId: ActionId

type SelectionTarget =
    | TargetPoint of sketchId: ActionId * entityId: string
    | TargetLine of sketchId: ActionId * entityId: string
    | TargetCircle of sketchId: ActionId * entityId: string
    | TargetArc of sketchId: ActionId * entityId: string
    | TargetLoop of sketchId: ActionId * loopId: string
    | TargetDimension of sketchId: ActionId * constraintIndex: int
    | TargetFrameOrigin of frameId: ActionId
    | TargetFrameAxis of frameId: ActionId * part: string
    | TargetSurface of actionId: ActionId

type PickCandidate =
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
        | PickLoop(id, _, _, _) -> id
        | PickDimension(id, _, _, _) -> id
        | PickFrameOrigin(id, _) -> id
        | PickFrameAxis(id, _, _) -> id
        | PickSurface(id, _) -> id

    /// Resolve a pickable to the ActionId the server should select when
    /// this pickable is clicked.
    let targetAction =
        function
        | PickPoint(_, sketchId, _, _, _) -> sketchId
        | PickLine(_, sketchId, _, _, _) -> sketchId
        | PickCircle(_, sketchId, _, _, _) -> sketchId
        | PickArc(_, sketchId, _, _, _, _, _) -> sketchId
        | PickLoop(_, sketchId, _, _) -> sketchId
        | PickDimension(_, sketchId, _, _) -> sketchId
        | PickFrameOrigin(_, frameId) -> frameId
        | PickFrameAxis(_, frameId, _) -> frameId
        | PickSurface(_, actionId) -> actionId

    let selectionTarget =
        function
        | PickPoint(_, sketchId, entityId, _, _) -> TargetPoint(sketchId, entityId)
        | PickLine(_, sketchId, entityId, _, _) -> TargetLine(sketchId, entityId)
        | PickCircle(_, sketchId, entityId, _, _) -> TargetCircle(sketchId, entityId)
        | PickArc(_, sketchId, entityId, _, _, _, _) -> TargetArc(sketchId, entityId)
        | PickLoop(_, sketchId, loopId, _) -> TargetLoop(sketchId, loopId)
        | PickDimension(_, sketchId, constraintIndex, _) -> TargetDimension(sketchId, constraintIndex)
        | PickFrameOrigin(_, frameId) -> TargetFrameOrigin(frameId)
        | PickFrameAxis(_, frameId, part) -> TargetFrameAxis(frameId, part)
        | PickSurface(_, actionId) -> TargetSurface(actionId)

    let sameTarget target pickable =
        selectionTarget pickable = target

    let private priority =
        function
        | PickPoint _ -> 0
        | PickLine _ | PickCircle _ | PickArc _ -> 1
        | PickDimension _ -> 2
        | PickLoop _ -> 3
        | PickFrameOrigin _ | PickFrameAxis _ -> 4
        | PickSurface _ -> 5

    let reduceCandidates (pickables: Pickable list) (candidates: PickCandidate list) : Pickable option =
        let byId = pickables |> List.map (fun p -> pickId p, p) |> Map.ofList
        candidates
        |> List.choose (fun c -> Map.tryFind c.PickId byId |> Option.map (fun p -> c, p))
        |> List.sortBy (fun (c, p) -> priority p, c.Score, pickId p)
        |> List.tryHead
        |> Option.map snd

    let selectionPriority =
        function
        | TargetPoint _
        | TargetFrameOrigin _ -> 0
        | TargetLine _
        | TargetCircle _
        | TargetArc _
        | TargetFrameAxis _ -> 1
        | TargetDimension _ -> 2
        | TargetLoop _ -> 3
        | TargetSurface _ -> 4
