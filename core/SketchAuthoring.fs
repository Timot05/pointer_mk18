namespace Server

open System

type SketchUiState =
    { EditMode: bool
      Tool: string
      ToolPoints: LabelPos list
      EditingDimension: EditingDimension option
      ConstraintPlacementMode: string option
      ConstraintPlacementDraft: ConstraintPlacementDraft option
      PendingConstraintPlacement: PendingConstraintPlacement option
      ConstraintAvailability: Map<string, bool>
      DimensionPlacementAvailability: Map<string, bool> }

and EditingDimension =
    { SketchId: string
      ConstraintIndex: int
      Key: string
      Value: float }

and PendingConstraintPlacement =
    { SketchId: string
      Constraint: SketchConstraint }

and ConstraintPlacementRef =
    | RefPoint of string
    | RefLine of string
    | RefCircle of string
    | RefArc of string
    | RefFrameOrigin of string
    | RefFrameAxis of string * string

and ConstraintPlacementDraft =
    { SketchId: string
      Kind: string
      ClickedRefs: ConstraintPlacementRef list }

module SketchAuthoring =

    let emptyUiState =
        { EditMode = false
          Tool = "none"
          ToolPoints = []
          EditingDimension = None
          ConstraintPlacementMode = None
          ConstraintPlacementDraft = None
          PendingConstraintPlacement = None
          ConstraintAvailability = Map.empty
          DimensionPlacementAvailability = Map.empty }

    type SelectedSketchContext =
        { Action: DocAction
          Sketch: ActionSketch }

    let trySelectedSketch (doc: Document) =
        match doc.SelectedId with
        | None -> None
        | Some id ->
            doc.Actions
            |> List.tryFind (fun action -> action.Id = id)
            |> Option.bind (fun action ->
                match action.Kind with
                | Sketch(_, _, sketch) -> Some { Action = action; Sketch = sketch }
                | _ -> None)

    let withUpdatedSketch (doc: Document) (actionId: string) (nextSketch: ActionSketch) =
        match doc.Actions |> List.tryFind (fun action -> action.Id = actionId) with
        | Some action ->
            let nextAction =
                { action with
                    Kind =
                        match action.Kind with
                        | Sketch(origin, plane, _) -> Sketch(origin, plane, nextSketch)
                        | other -> other }
            Document.updateAction actionId nextAction doc
        | None ->
            doc

    let removeConstraintAt (index: int) (sketch: ActionSketch) =
        { sketch with
            Constraints =
                sketch.Constraints
                |> List.mapi (fun i constraint_ -> i, constraint_)
                |> List.choose (fun (i, constraint_) -> if i = index then None else Some constraint_) }

    let private entityIdOf =
        function
        | REPoint(id, _, _)
        | RELine(id, _, _)
        | RECircle(id, _, _)
        | REArc(id, _, _, _) -> id

    let private entityMap (sketch: ActionSketch) =
        sketch.Entities
        |> List.map (fun entity ->
            let id = entityIdOf entity
            id, entity)
        |> Map.ofList

    let private entityRefsEntity entityId =
        function
        | RELine(_, startId, endId) -> startId = entityId || endId = entityId
        | RECircle(_, centerId, _) -> centerId = entityId
        | REArc(_, startId, endId, ArcCenter(centerId, _)) -> startId = entityId || endId = entityId || centerId = entityId
        | REArc(_, startId, endId, ArcThreePoint _) -> startId = entityId || endId = entityId
        | _ -> false

    let private entityReferencedPointIds =
        function
        | RELine(_, startId, endId) -> [ startId; endId ]
        | RECircle(_, centerId, _) -> [ centerId ]
        | REArc(_, startId, endId, ArcCenter(centerId, _)) -> [ startId; endId; centerId ]
        | REArc(_, startId, endId, ArcThreePoint _) -> [ startId; endId ]
        | REPoint _ -> []

    let private normalizePair a b = if a < b then (a, b) else (b, a)

    let private constraintRefsAnyEntity deletedEntityIds deletedLinePairs =
        let hasEntity id = Set.contains id deletedEntityIds
        let hasLinePair a b = Set.contains (normalizePair a b) deletedLinePairs
        function
        | Fixed(point, _, _) -> hasEntity point
        | EqualRadius(entityA, entityB) -> hasEntity entityA || hasEntity entityB
        | Coincident(a, b)
        | Horizontal(a, b)
        | Vertical(a, b) -> hasEntity a || hasEntity b || hasLinePair a b
        | FrameCoincident(point, _, _)
        | FrameDistance(point, _, _, _, _) -> hasEntity point
        | Concentric(entityA, entityB, centerA, centerB) ->
            hasEntity entityA || hasEntity entityB || hasEntity centerA || hasEntity centerB
        | Distance(a, b, _, _) -> hasEntity a || hasEntity b || hasLinePair a b
        | Equal(aStart, aEnd, bStart, bEnd, lineA, lineB)
        | Parallel(aStart, aEnd, bStart, bEnd, lineA, lineB)
        | Perpendicular(aStart, aEnd, bStart, bEnd, lineA, lineB)
        | LineDistance(aStart, aEnd, bStart, bEnd, lineA, lineB, _, _) ->
            [ aStart; aEnd; bStart; bEnd; lineA; lineB ] |> List.exists hasEntity
        | Midpoint(point, lineA, aStart, aEnd) ->
            [ point; lineA; aStart; aEnd ] |> List.exists hasEntity
        | FrameParallel(aStart, aEnd, lineA, _, _)
        | FramePerpendicular(aStart, aEnd, lineA, _, _)
        | FrameLineDistance(lineA, aStart, aEnd, _, _, _, _) ->
            [ lineA; aStart; aEnd ] |> List.exists hasEntity
        | Tangent(aStart, aEnd, center, circle, lineA, _) ->
            [ aStart; aEnd; center; circle; lineA ] |> List.exists hasEntity
        | CurveTangent(entityA, centerA, entityB, centerB, _) ->
            [ entityA; centerA; entityB; centerB ] |> List.exists hasEntity
        | CircleDiameter(circle, center, _, _) -> hasEntity circle || hasEntity center
        | PointLineDistance(point, lineA, aStart, aEnd, _, _) ->
            [ point; lineA; aStart; aEnd ] |> List.exists hasEntity
        | PointCircleDistance(point, circle, center, _, _) ->
            [ point; circle; center ] |> List.exists hasEntity
        | LineCircleDistance(lineA, aStart, aEnd, circle, center, _, _) ->
            [ lineA; aStart; aEnd; circle; center ] |> List.exists hasEntity
        | CircleCircleDistance(circleA, centerA, circleB, centerB, _, _, _) ->
            [ circleA; centerA; circleB; centerB ] |> List.exists hasEntity
        | Angle(aStart, aEnd, bStart, bEnd, lineA, lineB, _, _, _, _, _) ->
            [ aStart; aEnd; bStart; bEnd; lineA; lineB ] |> List.exists hasEntity

    let deleteTargets (targets: SelectionTarget list) (sketch: ActionSketch) =
        let constraintIndicesToDelete =
            targets
            |> List.choose (function
                | TargetDimension(_, index) -> Some index
                | _ -> None)
            |> Set.ofList

        let directlyDeletedEntityIds =
            targets
            |> List.choose (function
                | TargetPoint(_, entityId)
                | TargetLine(_, entityId)
                | TargetCircle(_, entityId)
                | TargetArc(_, entityId) -> Some entityId
                | _ -> None)
            |> Set.ofList

        let candidatePointIds =
            directlyDeletedEntityIds
            |> Set.toList
            |> List.choose (fun entityId -> sketch.Entities |> List.tryFind (fun entity -> entityIdOf entity = entityId))
            |> List.collect entityReferencedPointIds
            |> Set.ofList

        let deletedLinePairs =
            targets
            |> List.choose (function
                | TargetLine(_, entityId) ->
                    match sketch.Entities |> List.tryFind (fun entity -> entityIdOf entity = entityId) with
                    | Some(RELine(_, a, b)) -> Some(normalizePair a b)
                    | _ -> None
                | _ -> None)
            |> Set.ofList

        let rec expand deleted =
            let next =
                sketch.Entities
                |> List.filter (fun entity ->
                    let id = entityIdOf entity
                    Set.contains id deleted |> not && entityRefsEntityAny deleted entity)
                |> List.map entityIdOf
                |> Set.ofList
            let combined = Set.union deleted next
            if combined.Count = deleted.Count then deleted else expand combined

        and entityRefsEntityAny deleted entity =
            deleted |> Set.exists (fun id -> entityRefsEntity id entity)

        let deletedEntityIds = expand directlyDeletedEntityIds
        let afterDirectDelete =
            { sketch with
                Entities =
                    sketch.Entities
                    |> List.filter (fun entity -> Set.contains (entityIdOf entity) deletedEntityIds |> not)
                Constraints =
                    sketch.Constraints
                    |> List.mapi (fun i constraint_ -> i, constraint_)
                    |> List.choose (fun (i, constraint_) ->
                        if Set.contains i constraintIndicesToDelete || constraintRefsAnyEntity deletedEntityIds deletedLinePairs constraint_ then None
                        else Some constraint_) }

        let remainingReferencedPointIds =
            afterDirectDelete.Entities
            |> List.collect entityReferencedPointIds
            |> Set.ofList

        let orphanCandidatePointIds =
            candidatePointIds
            |> Set.filter (fun pointId -> Set.contains pointId remainingReferencedPointIds |> not)

        if Set.isEmpty orphanCandidatePointIds then
            afterDirectDelete
        else
            { afterDirectDelete with
                Entities =
                    afterDirectDelete.Entities
                    |> List.filter (fun entity ->
                        match entity with
                        | REPoint(id, _, _) -> Set.contains id orphanCandidatePointIds |> not
                        | _ -> true)
                Constraints =
                    afterDirectDelete.Constraints
                    |> List.filter (fun constraint_ ->
                        constraintRefsAnyEntity orphanCandidatePointIds Set.empty constraint_ |> not) }

    let private tryPoint (sketch: ActionSketch) id =
        match entityMap sketch |> Map.tryFind id with
        | Some(REPoint(_, x, y)) -> Some(x, y)
        | _ -> None

    let private tryLine (sketch: ActionSketch) id =
        match entityMap sketch |> Map.tryFind id with
        | Some(RELine(_, startId, endId)) -> Some(startId, endId)
        | _ -> None

    let private tryCircle (sketch: ActionSketch) id =
        match entityMap sketch |> Map.tryFind id with
        | Some(RECircle(_, centerId, radius)) -> Some(centerId, radius)
        | _ -> None

    let private tryArcCenter (sketch: ActionSketch) id =
        match entityMap sketch |> Map.tryFind id with
        | Some(REArc(_, _, _, ArcCenter(centerId, _))) -> Some centerId
        | _ -> None

    let private tryDiameterEntity (sketch: ActionSketch) id =
        match tryCircle sketch id with
        | Some(centerId, radius) -> Some(centerId, radius)
        | None ->
            match entityMap sketch |> Map.tryFind id with
            | Some(REArc(_, startId, _, ArcCenter(centerId, _))) ->
                match tryPoint sketch centerId, tryPoint sketch startId with
                | Some(cx, cy), Some(sx, sy) ->
                    let dx = sx - cx
                    let dy = sy - cy
                    Some(centerId, sqrt (dx * dx + dy * dy))
                | _ -> None
            | _ -> None

    let private tryCurve (sketch: ActionSketch) id =
        tryDiameterEntity sketch id

    let private dist (ax, ay) (bx, by) =
        let dx = bx - ax
        let dy = by - ay
        sqrt (dx * dx + dy * dy)

    let private dot (ax, ay) (bx, by) = ax * bx + ay * by
    let private cross (ax, ay) (bx, by) = ax * by - ay * bx
    let private sub (ax, ay) (bx, by) = (ax - bx, ay - by)
    let private clamp minv maxv value = max minv (min maxv value)
    let private angleOf (x, y) = Math.Atan2(y, x)
    let private tau = Math.PI * 2.0

    let private normalizePositive angle =
        let mutable a = angle
        while a < 0.0 do a <- a + tau
        while a >= tau do a <- a - tau
        a

    let private clockwiseSweep fromAngle toAngle =
        let ccw = normalizePositive (toAngle - fromAngle)
        if ccw <= 0.0 then 0.0 else tau - ccw

    let private pointInSector cursorAngle startAngle sweep ccw =
        let delta =
            if ccw then
                normalizePositive (cursorAngle - startAngle)
            else
                clockwiseSweep startAngle cursorAngle
        delta <= sweep + 1e-6

    let private lineDirection (sketch: ActionSketch) lineId =
        tryLine sketch lineId
        |> Option.bind (fun (startId, endId) ->
            Option.map2 (fun a b -> sub b a, (startId, endId)) (tryPoint sketch startId) (tryPoint sketch endId))

    let private lineDistanceValue sketch lineA lineB =
        match lineDirection sketch lineA, lineDirection sketch lineB with
        | Some((adx, ady), (aStart, _)), Some((_bdx, _bdy), (bStart, _)) ->
            match tryPoint sketch aStart, tryPoint sketch bStart with
            | Some pa, Some pb ->
                let denom = max 1e-6 (sqrt (adx * adx + ady * ady))
                abs (cross (sub pb pa) (adx, ady)) / denom
            | _ -> 10.0
        | _ -> 10.0

    let private angleValue sketch lineA lineB =
        match lineDirection sketch lineA, lineDirection sketch lineB with
        | Some((adx, ady), _), Some((bdx, bdy), _) ->
            let la = max 1e-6 (sqrt (adx * adx + ady * ady))
            let lb = max 1e-6 (sqrt (bdx * bdx + bdy * bdy))
            let c = clamp -1.0 1.0 ((dot (adx, ady) (bdx, bdy)) / (la * lb))
            acos c
        | _ -> Math.PI * 0.5

    let private selectionForSketch sketchId (targets: SelectionTarget list) =
        let matching =
            targets
            |> List.choose (fun target ->
                match target with
                | TargetPoint(id, entityId) when id = sketchId -> Some("point", entityId)
                | TargetLine(id, entityId) when id = sketchId -> Some("line", entityId)
                | TargetCircle(id, entityId) when id = sketchId -> Some("circle", entityId)
                | TargetArc(id, entityId) when id = sketchId -> Some("arc", entityId)
                | TargetLoop(id, loopId) when id = sketchId -> Some("loop", loopId)
                | TargetDimension(id, index) when id = sketchId -> Some("dimension", string index)
                | _ -> None)
        {| Points = matching |> List.choose (fun (kind, id) -> if kind = "point" then Some id else None)
           Lines = matching |> List.choose (fun (kind, id) -> if kind = "line" then Some id else None)
           Circles = matching |> List.choose (fun (kind, id) -> if kind = "circle" then Some id else None)
           Arcs = matching |> List.choose (fun (kind, id) -> if kind = "arc" then Some id else None) |}

    let private selectionForFrames (targets: SelectionTarget list) =
        {| Origins =
            targets
            |> List.choose (function
                | TargetFrameOrigin(frameId) -> Some frameId
                | _ -> None)
           Axes =
            targets
            |> List.choose (function
                | TargetFrameAxis(frameId, part) -> Some(frameId, part)
                | _ -> None) |}

    let private frameOriginFromSelection (origins: string list) (axes: (string * string) list) =
        match
            (origins @ (axes |> List.map fst))
            |> List.distinct
        with
        | [ frameId ] -> Some frameId
        | _ -> None

    let tryEditableDimension (sketchId: string) (sketch: ActionSketch) (index: int) =
        sketch.Constraints
        |> List.tryItem index
        |> Option.bind (fun constraint_ ->
            match constraint_ with
            | Distance(_, _, distance, _)
            | FrameDistance(_, _, _, distance, _)
            | LineDistance(_, _, _, _, _, _, distance, _)
            | FrameLineDistance(_, _, _, _, _, distance, _)
            | PointLineDistance(_, _, _, _, distance, _)
            | PointCircleDistance(_, _, _, distance, _)
            | LineCircleDistance(_, _, _, _, _, distance, _)
            | CircleCircleDistance(_, _, _, _, distance, _, _) ->
                Some { SketchId = sketchId; ConstraintIndex = index; Key = "distance"; Value = distance }
            | CircleDiameter(_, _, diameter, _) ->
                Some { SketchId = sketchId; ConstraintIndex = index; Key = "diameter"; Value = diameter }
            | Angle(_, _, _, _, _, _, angle, _, _, _, _) ->
                Some { SketchId = sketchId; ConstraintIndex = index; Key = "angle"; Value = angle }
            | _ ->
                None)

    let private chooseAngleConstraint sketch lineA lineB cursor =
        match tryLine sketch lineA, tryLine sketch lineB with
        | Some(aStart, aEnd), Some(bStart, bEnd) ->
            match tryPoint sketch aStart, tryPoint sketch aEnd, tryPoint sketch bStart, tryPoint sketch bEnd with
            | Some pa0, Some pa1, Some pb0, Some pb1 ->
                let lineIntersection =
                    let ad = sub pa1 pa0
                    let bd = sub pb1 pb0
                    let det = cross ad bd
                    if abs det < 1e-6 then None
                    else
                        let t = cross (sub pb0 pa0) bd / det
                        Some(fst pa0 + fst ad * t, snd pa0 + snd ad * t)

                let sharedVertex =
                    [ (aStart, bStart, pa0, false, false)
                      (aStart, bEnd, pa0, false, true)
                      (aEnd, bStart, pa1, true, false)
                      (aEnd, bEnd, pa1, true, true) ]
                    |> List.tryFind (fun (aVertex, bVertex, _, _, _) -> aVertex = bVertex)

                let vertex =
                    match sharedVertex with
                    | Some(_, _, vertex, _, _) -> vertex
                    | None -> lineIntersection |> Option.defaultValue pa0

                let candidates =
                    [ false, false
                      false, true
                      true, false
                      true, true ]
                    |> List.choose (fun (aReverse, bReverse) ->
                        let rayA = if aReverse then sub pa0 pa1 else sub pa1 pa0
                        let rayB = if bReverse then sub pb0 pb1 else sub pb1 pb0
                        let lenA = sqrt (dot rayA rayA)
                        let lenB = sqrt (dot rayB rayB)
                        if lenA < 1e-6 || lenB < 1e-6 then None
                        else
                            let angleA = angleOf rayA
                            let angleB = angleOf rayB
                            let ccwSweep = normalizePositive (angleB - angleA)
                            let ccw =
                                if ccwSweep <= Math.PI then true else false
                            let sweep =
                                if ccw then ccwSweep else tau - ccwSweep
                            let midAngle =
                                if ccw then angleA + sweep * 0.5 else angleA - sweep * 0.5
                            Some(aReverse, bReverse, ccw, sweep, normalizePositive midAngle, angleA, sweep))

                let chosen =
                    match cursor with
                    | Some cursorPoint ->
                        let cursorAngle = angleOf (sub cursorPoint vertex)
                        candidates
                        |> List.tryFind (fun (_, _, ccw, _, _, angleA, sweep) ->
                            pointInSector cursorAngle angleA sweep ccw)
                    | None ->
                        None

                let aReverse, bReverse, ccw, angle =
                    match chosen, candidates with
                    | Some(aReverse, bReverse, ccw, angle, _, _, _), _ ->
                        aReverse, bReverse, ccw, angle
                    | None, first :: _ ->
                        let (aReverse, bReverse, ccw, angle, _, _, _) = first
                        aReverse, bReverse, ccw, angle
                    | None, [] ->
                        false, false, true, angleValue sketch lineA lineB

                Some(Angle(aStart, aEnd, bStart, bEnd, lineA, lineB, angle, aReverse, bReverse, ccw, None))
            | _ -> None
        | _ -> None

    let private buildConstraint sketch sketchId kind targets cursor =
        let selection = selectionForSketch sketchId targets
        let frameSelection = selectionForFrames targets
        let frameOrigin = frameOriginFromSelection frameSelection.Origins frameSelection.Axes
        match kind with
        | "Coincident" ->
            match selection.Points with
            | [ a; b ] -> Some(Coincident(a, b))
            | [ pointId ] ->
                match frameSelection.Origins with
                | [ frameId ] -> Some(FrameCoincident(pointId, frameId, "origin"))
                | _ -> None
            | _ -> None
        | "Horizontal" ->
            match selection.Points, selection.Lines with
            | [ a; b ], _ -> Some(Horizontal(a, b))
            | _, [ lineId ] ->
                tryLine sketch lineId |> Option.map (fun (a, b) -> Horizontal(a, b))
            | _ -> None
        | "Vertical" ->
            match selection.Points, selection.Lines with
            | [ a; b ], _ -> Some(Vertical(a, b))
            | _, [ lineId ] ->
                tryLine sketch lineId |> Option.map (fun (a, b) -> Vertical(a, b))
            | _ -> None
        | "Midpoint" ->
            match selection.Points, selection.Lines with
            | [ pointId ], [ lineId ] ->
                tryLine sketch lineId |> Option.map (fun (aStart, aEnd) -> Midpoint(pointId, lineId, aStart, aEnd))
            | _ -> None
        | "Parallel" ->
            match selection.Lines, frameSelection.Axes with
            | [ lineA; lineB ], _ ->
                Option.map2 (fun (aStart, aEnd) (bStart, bEnd) -> Parallel(aStart, aEnd, bStart, bEnd, lineA, lineB))
                    (tryLine sketch lineA)
                    (tryLine sketch lineB)
            | [ lineA ], [ (frameId, part) ] when part <> "origin" ->
                tryLine sketch lineA |> Option.map (fun (aStart, aEnd) -> FrameParallel(aStart, aEnd, lineA, frameId, part))
            | _ -> None
        | "Perpendicular" ->
            match selection.Lines, frameSelection.Axes with
            | [ lineA; lineB ], _ ->
                Option.map2 (fun (aStart, aEnd) (bStart, bEnd) -> Perpendicular(aStart, aEnd, bStart, bEnd, lineA, lineB))
                    (tryLine sketch lineA)
                    (tryLine sketch lineB)
            | [ lineA ], [ (frameId, part) ] when part <> "origin" ->
                tryLine sketch lineA |> Option.map (fun (aStart, aEnd) -> FramePerpendicular(aStart, aEnd, lineA, frameId, part))
            | _ -> None
        | "Equal" ->
            match selection.Lines, selection.Circles, selection.Arcs with
            | [ lineA; lineB ], _, _ ->
                Option.map2 (fun (aStart, aEnd) (bStart, bEnd) -> Equal(aStart, aEnd, bStart, bEnd, lineA, lineB))
                    (tryLine sketch lineA)
                    (tryLine sketch lineB)
            | _, [ circleA; circleB ], _ -> Some(EqualRadius(circleA, circleB))
            | _, _, [ arcA; arcB ] -> Some(EqualRadius(arcA, arcB))
            | _ -> None
        | "Tangent" ->
            let curveIds : string list = selection.Circles @ selection.Arcs
            match selection.Lines, curveIds with
            | [ lineId ], [ curveId ] ->
                match tryLine sketch lineId, tryCurve sketch curveId with
                | Some(aStart, aEnd), Some(centerId, radius) ->
                    Some(Tangent(aStart, aEnd, centerId, curveId, lineId, radius))
                | _ -> None
            | [], [ curveA; curveB ] ->
                match tryCurve sketch curveA, tryCurve sketch curveB with
                | Some(centerA, radiusA), Some(centerB, radiusB) ->
                    match tryPoint sketch centerA, tryPoint sketch centerB with
                    | Some pa, Some pb ->
                        let centerDistance = dist pa pb
                        let externalDistance = radiusA + radiusB
                        let internalDistance = abs (radiusA - radiusB)
                        Some(CurveTangent(curveA, centerA, curveB, centerB, abs (centerDistance - internalDistance) < abs (centerDistance - externalDistance)))
                    | _ -> None
                | _ -> None
            | _ -> None
        | "Concentric" ->
            match selection.Circles with
            | [ circleA; circleB ] ->
                match tryCircle sketch circleA, tryCircle sketch circleB with
                | Some(centerA, _), Some(centerB, _) -> Some(Concentric(circleA, circleB, centerA, centerB))
                | _ -> None
            | _ -> None
        | "Fixed" ->
            match selection.Points with
            | [ pointId ] ->
                tryPoint sketch pointId
                |> Option.map (fun (x, y) -> Fixed(pointId, x, y))
            | _ -> None
        | "distance" ->
            match selection.Points, selection.Lines, selection.Circles, selection.Arcs, frameOrigin with
            | [ a; b ], _, _, _, _ ->
                Option.map2 (fun pa pb -> Distance(a, b, dist pa pb, None)) (tryPoint sketch a) (tryPoint sketch b)
            | [ pointId ], _, _, _, Some frameId ->
                Some(FrameDistance(pointId, frameId, "origin", 0.0, None))
            | _, [ lineA; lineB ], _, _, _ ->
                match tryLine sketch lineA, tryLine sketch lineB with
                | Some(aStart, aEnd), Some(bStart, bEnd) ->
                    Some(LineDistance(aStart, aEnd, bStart, bEnd, lineA, lineB, lineDistanceValue sketch lineA lineB, None))
                | _ -> None
            | _, [ lineA ], _, _, Some frameId ->
                tryLine sketch lineA |> Option.map (fun (aStart, aEnd) -> FrameLineDistance(lineA, aStart, aEnd, frameId, "origin", 0.0, None))
            | _, _, [ circleId ], [], _ ->
                match tryDiameterEntity sketch circleId with
                | Some(centerId, radius) -> Some(CircleDiameter(circleId, centerId, radius * 2.0, None))
                | _ -> None
            | _, _, [], [ arcId ], _ ->
                match tryDiameterEntity sketch arcId with
                | Some(centerId, radius) -> Some(CircleDiameter(arcId, centerId, radius * 2.0, None))
                | _ -> None
            | _ -> None
        | "angle" ->
            match selection.Lines with
            | [ lineA; lineB ] ->
                chooseAngleConstraint sketch lineA lineB cursor
            | _ -> None
        | _ -> None

    let private buildDistanceConstraintFromDraft sketch draft hoveredRef =
        let normalizeFrameRef =
            function
            | RefFrameOrigin frameId -> RefFrameOrigin frameId
            | RefFrameAxis(frameId, _part) -> RefFrameOrigin frameId
            | other -> other

        let hoveredRef = hoveredRef |> Option.map normalizeFrameRef
        let clickedRefs = draft.ClickedRefs |> List.map normalizeFrameRef
        match clickedRefs, hoveredRef with
        | [ RefLine lineA ], Some(RefFrameOrigin frameId)
        | [ RefFrameOrigin frameId ], Some(RefLine lineA) ->
            match tryLine sketch lineA with
            | Some(aStart, aEnd) -> Some(FrameLineDistance(lineA, aStart, aEnd, frameId, "origin", 0.0, None))
            | _ -> None
        | [ RefPoint pointId ], Some(RefFrameOrigin frameId)
        | [ RefFrameOrigin frameId ], Some(RefPoint pointId) ->
            Some(FrameDistance(pointId, frameId, "origin", 0.0, None))
        | [ RefLine lineA ], Some(RefLine lineB) when lineA <> lineB ->
            match tryLine sketch lineA, tryLine sketch lineB with
            | Some(aStart, aEnd), Some(bStart, bEnd) ->
                Some(LineDistance(aStart, aEnd, bStart, bEnd, lineA, lineB, lineDistanceValue sketch lineA lineB, None))
            | _ -> None
        | [ RefLine lineId ], _ ->
            tryLine sketch lineId
            |> Option.bind (fun (a, b) ->
                Option.map2 (fun pa pb -> Distance(a, b, dist pa pb, None)) (tryPoint sketch a) (tryPoint sketch b))
        | [ RefCircle circleId ], _ ->
            match tryDiameterEntity sketch circleId with
            | Some(centerId, radius) -> Some(CircleDiameter(circleId, centerId, radius * 2.0, None))
            | _ -> None
        | [ RefArc arcId ], _ ->
            match tryDiameterEntity sketch arcId with
            | Some(centerId, radius) -> Some(CircleDiameter(arcId, centerId, radius * 2.0, None))
            | _ -> None
        | [ RefPoint a ], Some(RefPoint b) when a <> b ->
            Option.map2 (fun pa pb -> Distance(a, b, dist pa pb, None)) (tryPoint sketch a) (tryPoint sketch b)
        | [ RefLine lineA; RefFrameOrigin frameId ], _ ->
            match tryLine sketch lineA with
            | Some(aStart, aEnd) -> Some(FrameLineDistance(lineA, aStart, aEnd, frameId, "origin", 0.0, None))
            | _ -> None
        | [ RefPoint pointId; RefFrameOrigin frameId ], _ ->
            Some(FrameDistance(pointId, frameId, "origin", 0.0, None))
        | [ RefLine lineA; RefLine lineB ], _ when lineA <> lineB ->
            match tryLine sketch lineA, tryLine sketch lineB with
            | Some(aStart, aEnd), Some(bStart, bEnd) ->
                Some(LineDistance(aStart, aEnd, bStart, bEnd, lineA, lineB, lineDistanceValue sketch lineA lineB, None))
            | _ -> None
        | [ RefPoint a; RefPoint b ], _ when a <> b ->
            Option.map2 (fun pa pb -> Distance(a, b, dist pa pb, None)) (tryPoint sketch a) (tryPoint sketch b)
        | _ -> None

    let private buildAngleConstraintFromDraft sketch draft hoveredRef cursor =
        match draft.ClickedRefs, hoveredRef with
        | [ RefLine lineA ], Some(RefLine lineB) when lineA <> lineB ->
            chooseAngleConstraint sketch lineA lineB cursor
        | [ RefLine lineA; RefLine lineB ], _ when lineA <> lineB ->
            chooseAngleConstraint sketch lineA lineB cursor
        | _ -> None

    let pendingConstraintForDraft sketch draft hoveredRef cursor =
        match draft.Kind with
        | "distance" -> buildDistanceConstraintFromDraft sketch draft hoveredRef
        | "angle" -> buildAngleConstraintFromDraft sketch draft hoveredRef cursor
        | _ -> None

    let placementRefFromTarget sketchId =
        function
        | TargetPoint(id, entityId) when id = sketchId -> Some(RefPoint entityId)
        | TargetLine(id, entityId) when id = sketchId -> Some(RefLine entityId)
        | TargetCircle(id, entityId) when id = sketchId -> Some(RefCircle entityId)
        | TargetArc(id, entityId) when id = sketchId -> Some(RefArc entityId)
        | TargetFrameOrigin(frameId) -> Some(RefFrameOrigin frameId)
        | TargetFrameAxis(frameId, part) -> Some(RefFrameAxis(frameId, part))
        | _ -> None

    let updatePlacementDraft sketchId kind hoveredTarget draft =
        let clickedRef = hoveredTarget |> Option.bind (placementRefFromTarget sketchId)
        match kind, clickedRef, draft with
        | _, None, _ -> draft
        | "distance", Some ref_, _ ->
            let ref_ =
                match ref_ with
                | RefFrameAxis(frameId, _part) -> RefFrameOrigin frameId
                | other -> other
            let nextRefs =
                match ref_, draft |> Option.bind (fun d -> if d.Kind = kind then Some d.ClickedRefs else None) with
                | RefLine lineB, Some [ RefLine lineA ] when lineA <> lineB -> [ RefLine lineA; RefLine lineB ]
                | RefPoint b, Some [ RefPoint a ] when a <> b -> [ RefPoint a; RefPoint b ]
                | RefFrameOrigin frameId, Some [ RefLine lineA ] -> [ RefLine lineA; RefFrameOrigin frameId ]
                | RefLine lineA, Some [ RefFrameOrigin frameId ] -> [ RefLine lineA; RefFrameOrigin frameId ]
                | RefFrameOrigin frameId, Some [ RefPoint pointId ] -> [ RefPoint pointId; RefFrameOrigin frameId ]
                | RefPoint pointId, Some [ RefFrameOrigin frameId ] -> [ RefPoint pointId; RefFrameOrigin frameId ]
                | _, _ -> [ ref_ ]
            Some { SketchId = sketchId; Kind = kind; ClickedRefs = nextRefs }
        | "angle", Some(RefLine line), _ ->
            let nextRefs =
                match draft |> Option.bind (fun d -> if d.Kind = kind then Some d.ClickedRefs else None) with
                | Some [ RefLine lineA ] when lineA <> line -> [ RefLine lineA; RefLine line ]
                | _ -> [ RefLine line ]
            Some { SketchId = sketchId; Kind = kind; ClickedRefs = nextRefs }
        | _ -> draft

    let addConstraintFromSelection doc targets kind =
        trySelectedSketch doc
        |> Option.bind (fun ctx ->
            buildConstraint ctx.Sketch ctx.Action.Id kind targets None
            |> Option.map (fun constraint_ ->
                let nextSketch = { ctx.Sketch with Constraints = ctx.Sketch.Constraints @ [ constraint_ ] }
                withUpdatedSketch doc ctx.Action.Id nextSketch))

    let placeConstraintFromSelection doc targets placementKind labelPosition =
        trySelectedSketch doc
        |> Option.bind (fun ctx ->
            buildConstraint ctx.Sketch ctx.Action.Id placementKind targets (Some(labelPosition.X, labelPosition.Y))
            |> Option.map (fun constraint_ ->
                let withLabel =
                    match constraint_ with
                    | Distance(a, b, distance, _) -> Distance(a, b, distance, Some labelPosition)
                    | LineDistance(aStart, aEnd, bStart, bEnd, lineA, lineB, distance, _) -> LineDistance(aStart, aEnd, bStart, bEnd, lineA, lineB, distance, Some labelPosition)
                    | CircleDiameter(circle, center, diameter, _) -> CircleDiameter(circle, center, diameter, Some labelPosition)
                    | Angle(aStart, aEnd, bStart, bEnd, lineA, lineB, angle, aReverse, bReverse, ccw, _) ->
                        Angle(aStart, aEnd, bStart, bEnd, lineA, lineB, angle, aReverse, bReverse, ccw, Some labelPosition)
                    | other -> other
                let nextSketch = { ctx.Sketch with Constraints = ctx.Sketch.Constraints @ [ withLabel ] }
                withUpdatedSketch doc ctx.Action.Id nextSketch))

    let placePendingConstraint doc pending labelPosition =
        let withLabel =
            match pending.Constraint with
            | Distance(a, b, distance, _) -> Distance(a, b, distance, Some labelPosition)
            | LineDistance(aStart, aEnd, bStart, bEnd, lineA, lineB, distance, _) -> LineDistance(aStart, aEnd, bStart, bEnd, lineA, lineB, distance, Some labelPosition)
            | CircleDiameter(circle, center, diameter, _) -> CircleDiameter(circle, center, diameter, Some labelPosition)
            | Angle(aStart, aEnd, bStart, bEnd, lineA, lineB, angle, aReverse, bReverse, ccw, _) ->
                Angle(aStart, aEnd, bStart, bEnd, lineA, lineB, angle, aReverse, bReverse, ccw, Some labelPosition)
            | other -> other

        trySelectedSketch doc
        |> Option.filter (fun ctx -> ctx.Action.Id = pending.SketchId)
        |> Option.map (fun ctx ->
            let nextSketch = { ctx.Sketch with Constraints = ctx.Sketch.Constraints @ [ withLabel ] }
            withUpdatedSketch doc ctx.Action.Id nextSketch)

    let availabilityForSelection doc editMode tool placementMode targets placementCursor placementDraft hoveredTarget =
        match trySelectedSketch doc with
        | None ->
            { emptyUiState with
                EditMode = false
                Tool = "none"
                ToolPoints = []
                ConstraintPlacementMode = None
                ConstraintPlacementDraft = None
                PendingConstraintPlacement = None }
        | Some ctx ->
            let can kind = buildConstraint ctx.Sketch ctx.Action.Id kind targets None |> Option.isSome
            let activeDraft =
                if editMode then
                    placementDraft |> Option.filter (fun d -> d.SketchId = ctx.Action.Id && placementMode = Some d.Kind)
                else
                    None
            let hoveredRef = hoveredTarget |> Option.bind (placementRefFromTarget ctx.Action.Id)
            let pendingConstraintPlacement =
                if editMode then
                    match activeDraft with
                    | Some draft ->
                        pendingConstraintForDraft ctx.Sketch draft hoveredRef placementCursor
                        |> Option.map (fun constraint_ -> { SketchId = ctx.Action.Id; Constraint = constraint_ })
                    | None ->
                        placementMode
                        |> Option.bind (fun kind ->
                            buildConstraint ctx.Sketch ctx.Action.Id kind targets placementCursor
                            |> Option.map (fun constraint_ ->
                                { SketchId = ctx.Action.Id
                                  Constraint = constraint_ }))
                else
                    None
            { EditMode = editMode
              Tool = if editMode then tool else "none"
              ToolPoints = []
              EditingDimension = None
              ConstraintPlacementMode = if editMode then placementMode else None
              ConstraintPlacementDraft = activeDraft
              PendingConstraintPlacement = pendingConstraintPlacement
              ConstraintAvailability =
                [ "Coincident"; "Horizontal"; "Vertical"; "Midpoint"; "Parallel"; "Perpendicular"; "Equal"; "Tangent"; "Concentric"; "Fixed" ]
                |> List.map (fun kind -> kind, can kind)
                |> Map.ofList
              DimensionPlacementAvailability =
                [ "distance"; "angle" ]
                |> List.map (fun kind -> kind, editMode)
                |> Map.ofList }

    let requiredToolPoints tool =
        match tool with
        | "line"
        | "rectangle"
        | "roundedRectangle"
        | "circle" -> 2
        | "arc" -> 3
        | _ -> 0

    let private nextEntityId (sketch: ActionSketch) prefix =
        let taken =
            sketch.Entities
            |> List.map (function
                | REPoint(id, _, _)
                | RELine(id, _, _)
                | RECircle(id, _, _)
                | REArc(id, _, _, _) -> id)
            |> Set.ofList
        let rec loop i =
            let id = $"{prefix}{i}"
            if Set.contains id taken then loop (i + 1) else id
        loop 1

    let private addPoint (sketch: ActionSketch) (x, y) =
        let pointId = nextEntityId sketch "p"
        { sketch with Entities = sketch.Entities @ [ REPoint(pointId, x, y) ] }, pointId

    let private addLineEntity (sketch: ActionSketch) startId endId =
        let lineId = nextEntityId sketch "l"
        { sketch with Entities = sketch.Entities @ [ RELine(lineId, startId, endId) ] }, lineId

    let private addRectangleToSketch (sketch: ActionSketch) (cornerA: float * float) (cornerB: float * float) =
        let minX = min (fst cornerA) (fst cornerB)
        let maxX = max (fst cornerA) (fst cornerB)
        let minY = min (snd cornerA) (snd cornerB)
        let maxY = max (snd cornerA) (snd cornerB)
        if abs (maxX - minX) < 1e-9 || abs (maxY - minY) < 1e-9 then
            None
        else
            let mutable next = sketch
            let next1, p1 = addPoint next (minX, minY)
            next <- next1
            let next2, p2 = addPoint next (maxX, minY)
            next <- next2
            let next3, p3 = addPoint next (maxX, maxY)
            next <- next3
            let next4, p4 = addPoint next (minX, maxY)
            next <- next4

            let next5, l1 = addLineEntity next p1 p2
            next <- next5
            let next6, l2 = addLineEntity next p2 p3
            next <- next6
            let next7, l3 = addLineEntity next p3 p4
            next <- next7
            let next8, l4 = addLineEntity next p4 p1
            next <- next8

            let constraints =
                [ Horizontal(p1, p2)
                  Vertical(p2, p3)
                  Horizontal(p4, p3)
                  Vertical(p1, p4)
                  Perpendicular(p1, p2, p2, p3, l1, l2)
                  Perpendicular(p2, p3, p3, p4, l2, l3)
                  Perpendicular(p3, p4, p4, p1, l3, l4)
                  Perpendicular(p4, p1, p1, p2, l4, l1) ]

            Some { next with Constraints = next.Constraints @ constraints }

    let private roundedRectRadius (minX: float) (maxX: float) (minY: float) (maxY: float) =
        let width = maxX - minX
        let height = maxY - minY
        (min width height * 0.2)
            |> max 0.002
            |> min (width * 0.5 - 1e-6)
            |> min (height * 0.5 - 1e-6)

    let private addRoundedRectangleToSketch (sketch: ActionSketch) (cornerA: float * float) (cornerB: float * float) =
        let minX = min (fst cornerA) (fst cornerB)
        let maxX = max (fst cornerA) (fst cornerB)
        let minY = min (snd cornerA) (snd cornerB)
        let maxY = max (snd cornerA) (snd cornerB)
        let width = maxX - minX
        let height = maxY - minY
        if abs width < 1e-9 || abs height < 1e-9 then
            None
        else
            let radius = roundedRectRadius minX maxX minY maxY
            if radius <= 1e-6 then
                addRectangleToSketch sketch cornerA cornerB
            else
                let mutable next = sketch
                let addP x y =
                    let updated, pointId = addPoint next (x, y)
                    next <- updated
                    pointId

                let topLeftStart = addP (minX + radius) maxY
                let topRightStart = addP (maxX - radius) maxY
                let rightTopStart = addP maxX (maxY - radius)
                let rightBottomStart = addP maxX (minY + radius)
                let bottomRightStart = addP (maxX - radius) minY
                let bottomLeftStart = addP (minX + radius) minY
                let leftBottomStart = addP minX (minY + radius)
                let leftTopStart = addP minX (maxY - radius)

                let next1, topLine = addLineEntity next topLeftStart topRightStart
                next <- next1
                let next2, rightLine = addLineEntity next rightTopStart rightBottomStart
                next <- next2
                let next3, bottomLine = addLineEntity next bottomRightStart bottomLeftStart
                next <- next3
                let next4, leftLine = addLineEntity next leftBottomStart leftTopStart
                next <- next4

                let tlCenter = addP (minX + radius) (maxY - radius)
                let trCenter = addP (maxX - radius) (maxY - radius)
                let brCenter = addP (maxX - radius) (minY + radius)
                let blCenter = addP (minX + radius) (minY + radius)

                let trArcId = nextEntityId next "a"
                next <- { next with Entities = next.Entities @ [ REArc(trArcId, topRightStart, rightTopStart, ArcCenter(trCenter, true)) ] }
                let brArcId = nextEntityId next "a"
                next <- { next with Entities = next.Entities @ [ REArc(brArcId, rightBottomStart, bottomRightStart, ArcCenter(brCenter, true)) ] }
                let blArcId = nextEntityId next "a"
                next <- { next with Entities = next.Entities @ [ REArc(blArcId, bottomLeftStart, leftBottomStart, ArcCenter(blCenter, true)) ] }
                let tlArcId = nextEntityId next "a"
                next <- { next with Entities = next.Entities @ [ REArc(tlArcId, leftTopStart, topLeftStart, ArcCenter(tlCenter, true)) ] }

                let constraints =
                    [ Horizontal(topLeftStart, topRightStart)
                      Vertical(rightTopStart, rightBottomStart)
                      Horizontal(bottomLeftStart, bottomRightStart)
                      Vertical(leftBottomStart, leftTopStart)
                      Perpendicular(topLeftStart, topRightStart, rightTopStart, rightBottomStart, topLine, rightLine)
                      Perpendicular(rightTopStart, rightBottomStart, bottomRightStart, bottomLeftStart, rightLine, bottomLine)
                      Perpendicular(bottomRightStart, bottomLeftStart, leftBottomStart, leftTopStart, bottomLine, leftLine)
                      Perpendicular(leftBottomStart, leftTopStart, topLeftStart, topRightStart, leftLine, topLine)
                      Vertical(trCenter, topRightStart)
                      Horizontal(trCenter, rightTopStart)
                      Horizontal(brCenter, rightBottomStart)
                      Vertical(brCenter, bottomRightStart)
                      Vertical(blCenter, bottomLeftStart)
                      Horizontal(blCenter, leftBottomStart)
                      Horizontal(tlCenter, leftTopStart)
                      Vertical(tlCenter, topLeftStart)
                      EqualRadius(trArcId, brArcId)
                      EqualRadius(brArcId, blArcId)
                      EqualRadius(blArcId, tlArcId)
                      Tangent(topLeftStart, topRightStart, trCenter, trArcId, topLine, radius)
                      Tangent(topLeftStart, topRightStart, tlCenter, tlArcId, topLine, radius)
                      Tangent(rightTopStart, rightBottomStart, trCenter, trArcId, rightLine, radius)
                      Tangent(rightTopStart, rightBottomStart, brCenter, brArcId, rightLine, radius)
                      Tangent(bottomRightStart, bottomLeftStart, brCenter, brArcId, bottomLine, radius)
                      Tangent(bottomRightStart, bottomLeftStart, blCenter, blArcId, bottomLine, radius)
                      Tangent(leftBottomStart, leftTopStart, blCenter, blArcId, leftLine, radius)
                      Tangent(leftBottomStart, leftTopStart, tlCenter, tlArcId, leftLine, radius) ]

                Some { next with Constraints = next.Constraints @ constraints }

    let private projectPointToCircle (cx, cy) (sx, sy) (px, py) =
        let radius = max 1e-6 (dist (cx, cy) (sx, sy))
        let dx = px - cx
        let dy = py - cy
        let length = sqrt (dx * dx + dy * dy)
        if length < 1e-6 then (cx + radius, cy)
        else (cx + (dx / length) * radius, cy + (dy / length) * radius)

    let applyToolClick tool points sketch =
        let coords = points |> List.map (fun p -> (p.X, p.Y))
        match tool, coords with
        | "line", [ startPoint; endPoint ] ->
            let next, startId = addPoint sketch startPoint
            let next, endId = addPoint next endPoint
            let next, _ = addLineEntity next startId endId
            Some next
        | "rectangle", [ (x0, y0); (x1, y1) ] ->
            addRectangleToSketch sketch (x0, y0) (x1, y1)
        | "roundedRectangle", [ (x0, y0); (x1, y1) ] ->
            addRoundedRectangleToSketch sketch (x0, y0) (x1, y1)
        | "circle", [ centerPoint; radiusPoint ] ->
            let next, centerId = addPoint sketch centerPoint
            let circleId = nextEntityId next "c"
            let radius = max 1e-6 (dist centerPoint radiusPoint)
            Some { next with Entities = next.Entities @ [ RECircle(circleId, centerId, radius) ] }
        | "arc", [ centerPoint; startPoint; endPoint ] ->
            let next, centerId = addPoint sketch centerPoint
            let next, startId = addPoint next startPoint
            let projectedEnd = projectPointToCircle centerPoint startPoint endPoint
            let next, endId = addPoint next projectedEnd
            let arcId = nextEntityId next "a"
            let clockwise = cross (sub startPoint centerPoint) (sub endPoint centerPoint) < 0.0
            Some { next with Entities = next.Entities @ [ REArc(arcId, startId, endId, ArcCenter(centerId, clockwise)) ] }
        | _ -> None
