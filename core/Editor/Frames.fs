namespace Server

module Frames =

    let private slotValue (slots: SlotTable) (values: float array) (slotRef: SlotRef) (defaultValue: float) =
        match SlotTable.tryFindSlot slots slotRef with
        | Some slot -> values.[slot]
        | None -> defaultValue

    let stepTransform (slots: SlotTable) (values: float array) (step: FrameStep) : RigidTransform =
        match step with
        | FrameTranslate(_, x, y, z, xDefault, yDefault, zDefault) ->
            RigidTransform.translate
                { X = slotValue slots values x xDefault
                  Y = slotValue slots values y yDefault
                  Z = slotValue slots values z zDefault }
        | FrameRotate(_, ax, ay, az, angle, axDefault, ayDefault, azDefault, angleDefault) ->
            RigidTransform.fromAxisAngle
                { X = slotValue slots values ax axDefault
                  Y = slotValue slots values ay ayDefault
                  Z = slotValue slots values az azDefault }
                (slotValue slots values angle angleDefault)

    /// Fold a child/local-first frame chain into a concrete world transform.
    let foldChain (slots: SlotTable) (values: float array) (chain: FrameChain) : RigidTransform =
        chain
        |> List.fold (fun acc step -> acc * stepTransform slots values step) RigidTransform.Identity
