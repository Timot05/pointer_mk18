namespace Server

module Frames =

    let stepTransform (step: FrameStep) : RigidTransform =
        match step with
        | FrameTranslate(_, x, y, z) ->
            RigidTransform.translate { X = x; Y = y; Z = z }
        | FrameRotate(_, ax, ay, az, angle) ->
            RigidTransform.fromAxisAngle { X = ax; Y = ay; Z = az } angle

    /// Fold a child/local-first frame chain into a concrete world transform.
    let foldChain (chain: FrameChain) : RigidTransform =
        chain
        |> List.fold (fun acc step -> acc * stepTransform step) RigidTransform.Identity
