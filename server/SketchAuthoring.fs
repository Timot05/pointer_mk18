namespace Server

open System

type SketchUiState =
    { EditMode: bool
      Tool: string
      ConstraintPlacementMode: string option
      ConstraintAvailability: Map<string, bool>
      DimensionPlacementAvailability: Map<string, bool> }

module SketchAuthoring =

    let emptyUiState =
        { EditMode = false
          Tool = "none"
          ConstraintPlacementMode = None
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
                | Sketch(_, sketch) -> Some { Action = action; Sketch = sketch }
                | _ -> None)

    let withUpdatedSketch (doc: Document) (actionId: string) (nextSketch: ActionSketch) =
        match doc.Actions |> List.tryFind (fun action -> action.Id = actionId) with
        | Some action ->
            let nextAction =
                { action with
                    Kind =
                        match action.Kind with
                        | Sketch(origin, _) -> Sketch(origin, nextSketch)
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

    let private entityMap (sketch: ActionSketch) =
        sketch.Entities
        |> List.map (fun entity ->
            let id =
                match entity with
                | REPoint(id, _, _)
                | RELine(id, _, _)
                | RECircle(id, _, _)
                | REArc(id, _, _, _) -> id
            id, entity)
        |> Map.ofList

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

    let private dist (ax, ay) (bx, by) =
        let dx = bx - ax
        let dy = by - ay
        sqrt (dx * dx + dy * dy)

    let private dot (ax, ay) (bx, by) = ax * bx + ay * by
    let private cross (ax, ay) (bx, by) = ax * by - ay * bx
    let private sub (ax, ay) (bx, by) = (ax - bx, ay - by)
    let private clamp minv maxv value = max minv (min maxv value)

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
            acos c * 180.0 / Math.PI
        | _ -> 90.0

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

    let private buildConstraint sketch sketchId kind targets =
        let selection = selectionForSketch sketchId targets
        match kind with
        | "Coincident" ->
            match selection.Points with
            | [ a; b ] -> Some(Coincident(a, b))
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
            match selection.Lines with
            | [ lineA; lineB ] ->
                Option.map2 (fun (aStart, aEnd) (bStart, bEnd) -> Parallel(aStart, aEnd, bStart, bEnd, lineA, lineB))
                    (tryLine sketch lineA)
                    (tryLine sketch lineB)
            | _ -> None
        | "Perpendicular" ->
            match selection.Lines with
            | [ lineA; lineB ] ->
                Option.map2 (fun (aStart, aEnd) (bStart, bEnd) -> Perpendicular(aStart, aEnd, bStart, bEnd, lineA, lineB))
                    (tryLine sketch lineA)
                    (tryLine sketch lineB)
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
            match selection.Lines, selection.Circles with
            | [ lineId ], [ circleId ] ->
                match tryLine sketch lineId, tryCircle sketch circleId with
                | Some(aStart, aEnd), Some(centerId, radius) ->
                    Some(Tangent(aStart, aEnd, centerId, circleId, lineId, radius))
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
            match selection.Points, selection.Lines, selection.Circles with
            | [ a; b ], _, _ ->
                Option.map2 (fun pa pb -> Distance(a, b, dist pa pb, None)) (tryPoint sketch a) (tryPoint sketch b)
            | _, [ lineA; lineB ], _ ->
                match tryLine sketch lineA, tryLine sketch lineB with
                | Some(aStart, aEnd), Some(bStart, bEnd) ->
                    Some(LineDistance(aStart, aEnd, bStart, bEnd, lineA, lineB, lineDistanceValue sketch lineA lineB, None))
                | _ -> None
            | _, _, [ circleId ] ->
                match tryCircle sketch circleId with
                | Some(centerId, radius) -> Some(CircleDiameter(circleId, centerId, radius * 2.0, None))
                | _ -> None
            | _ -> None
        | "angle" ->
            match selection.Lines with
            | [ lineA; lineB ] ->
                match tryLine sketch lineA, tryLine sketch lineB with
                | Some(aStart, aEnd), Some(bStart, bEnd) ->
                    let degrees = angleValue sketch lineA lineB
                    let ccw =
                        match tryPoint sketch aStart, tryPoint sketch aEnd, tryPoint sketch bStart, tryPoint sketch bEnd with
                        | Some pa0, Some pa1, Some pb0, Some pb1 ->
                            cross (sub pa1 pa0) (sub pb1 pb0) >= 0.0
                        | _ -> true
                    Some(Angle(aStart, aEnd, bStart, bEnd, lineA, lineB, degrees, false, false, ccw, None))
                | _ -> None
            | _ -> None
        | _ -> None

    let addConstraintFromSelection doc targets kind =
        trySelectedSketch doc
        |> Option.bind (fun ctx ->
            buildConstraint ctx.Sketch ctx.Action.Id kind targets
            |> Option.map (fun constraint_ ->
                let nextSketch = { ctx.Sketch with Constraints = ctx.Sketch.Constraints @ [ constraint_ ] }
                withUpdatedSketch doc ctx.Action.Id nextSketch))

    let placeConstraintFromSelection doc targets placementKind labelPosition =
        trySelectedSketch doc
        |> Option.bind (fun ctx ->
            buildConstraint ctx.Sketch ctx.Action.Id placementKind targets
            |> Option.map (fun constraint_ ->
                let withLabel =
                    match constraint_ with
                    | Distance(a, b, distance, _) -> Distance(a, b, distance, Some labelPosition)
                    | LineDistance(aStart, aEnd, bStart, bEnd, lineA, lineB, distance, _) -> LineDistance(aStart, aEnd, bStart, bEnd, lineA, lineB, distance, Some labelPosition)
                    | CircleDiameter(circle, center, diameter, _) -> CircleDiameter(circle, center, diameter, Some labelPosition)
                    | Angle(aStart, aEnd, bStart, bEnd, lineA, lineB, degrees, aReverse, bReverse, ccw, _) ->
                        Angle(aStart, aEnd, bStart, bEnd, lineA, lineB, degrees, aReverse, bReverse, ccw, Some labelPosition)
                    | other -> other
                let nextSketch = { ctx.Sketch with Constraints = ctx.Sketch.Constraints @ [ withLabel ] }
                withUpdatedSketch doc ctx.Action.Id nextSketch))

    let availabilityForSelection doc editMode tool placementMode targets =
        match trySelectedSketch doc with
        | None ->
            { emptyUiState with
                EditMode = false
                Tool = "none"
                ConstraintPlacementMode = None }
        | Some ctx ->
            let can kind = buildConstraint ctx.Sketch ctx.Action.Id kind targets |> Option.isSome
            { EditMode = editMode
              Tool = if editMode then tool else "none"
              ConstraintPlacementMode = if editMode then placementMode else None
              ConstraintAvailability =
                [ "Coincident"; "Horizontal"; "Vertical"; "Midpoint"; "Parallel"; "Perpendicular"; "Equal"; "Tangent"; "Concentric"; "Fixed" ]
                |> List.map (fun kind -> kind, can kind)
                |> Map.ofList
              DimensionPlacementAvailability =
                [ "distance"; "angle" ]
                |> List.map (fun kind -> kind, can kind)
                |> Map.ofList }
