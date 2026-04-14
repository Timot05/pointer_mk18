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
    // Coarse SDF surface — the whole surface of a Field-producing action.
    | PickSurface of pickId: PickId * actionId: ActionId

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
        | PickSurface(_, actionId) -> actionId
