module SketchOverlayRender

// 2D sketch overlay rendering — line-list rendering of sketch entities
// (line segments, tessellated circles + arcs) on the sketch's plane in 3D
// world space.
//
// Phase 5b.1: lines/circles/arcs only. Skips points, loops, highlights,
// labels, and selection-driven colouring.

open Server

/// Colour used for sketch geometry.
let private SKETCH_LINE : float32[] = [| 0.231f; 0.231f; 0.231f; 1.0f |]

/// Accent colour for selected or hovered entities.
let private ACCENT : float32[] = [| 0.502f; 0.745f; 0.549f; 1.0f |]

let private isEntityActive
    (sketchId: ActionId) (entityKind: string) (entityId: string)
    (hovered: SelectionTarget option) (selected: SelectionTarget list) : bool =
    let matches (target: SelectionTarget) =
        match entityKind, target with
        | "point", TargetPoint(sid, eid) -> sid = sketchId && eid = entityId
        | "line", TargetLine(sid, eid) -> sid = sketchId && eid = entityId
        | "circle", TargetCircle(sid, eid) -> sid = sketchId && eid = entityId
        | "arc", TargetArc(sid, eid) -> sid = sketchId && eid = entityId
        | _ -> false
    (match hovered with Some h -> matches h | None -> false)
    || List.exists matches selected

/// Number of line segments used to tessellate a full circle.
let private CIRCLE_SEGMENTS = 64

let private pushVertex (out: ResizeArray<float32>) (x: float) (y: float) (color: float32[]) =
    out.Add(float32 x)
    out.Add(float32 y)
    out.Add color.[0]
    out.Add color.[1]
    out.Add color.[2]
    out.Add color.[3]

/// Appends two vertices for one line segment.
let private pushSegment
    (out: ResizeArray<float32>)
    (a: float * float) (b: float * float) (color: float32[]) =
    let (ax, ay) = a
    let (bx, by) = b
    pushVertex out ax ay color
    pushVertex out bx by color

/// Tessellate a full circle into CIRCLE_SEGMENTS line segments.
let private pushCircle
    (out: ResizeArray<float32>)
    (center: float * float) (radius: float) (color: float32[]) =
    let (cx, cy) = center
    let n = CIRCLE_SEGMENTS
    let twoPi = 2.0 * System.Math.PI
    let mutable prev = (cx + radius, cy)
    for i in 1 .. n do
        let t = twoPi * float i / float n
        let next = (cx + radius * cos t, cy + radius * sin t)
        pushSegment out prev next color
        prev <- next

/// Tessellate an arc (centre-defined) into line segments.
/// Mirrors GpuSdf's arc angle convention — clockwise goes from start to end
/// the short way if clockwise bit matches.
let private pushArc
    (out: ResizeArray<float32>)
    (startP: float * float) (endP: float * float) (center: float * float)
    (clockwise: bool) (color: float32[]) =
    let (sx, sy) = startP
    let (ex, ey) = endP
    let (cx, cy) = center
    let radius = sqrt ((sx - cx) * (sx - cx) + (sy - cy) * (sy - cy))
    if radius < 1e-9 then
        pushSegment out startP endP color
    else
        let startAngle = atan2 (sy - cy) (sx - cx)
        let endAngle = atan2 (ey - cy) (ex - cx)
        let tau = 2.0 * System.Math.PI
        // Signed arc length (radians). For clockwise we go the other way.
        let sweep =
            if clockwise then
                let mutable d = startAngle - endAngle
                while d < 0.0 do d <- d + tau
                -d  // negative sweep
            else
                let mutable d = endAngle - startAngle
                while d < 0.0 do d <- d + tau
                d
        let segments = max 4 (int (abs sweep / (tau / float CIRCLE_SEGMENTS)))
        let mutable prev = startP
        for i in 1 .. segments do
            let t = sweep * float i / float segments
            let ang = startAngle + t
            let next = (cx + radius * cos ang, cy + radius * sin ang)
            pushSegment out prev next color
            prev <- next

/// Slot-backed 2D point lookup. Reads (x, y) for a sketch point from the
/// sketch's `sketch.entity.{id}.{x|y}` slot values, falling back to the
/// baseline coords carried in REPoint when the slot isn't resolved.
let resolvePointMap
    (slotLookup: Map<SlotRef, Slot>)
    (paramValues: float[])
    (sketchId: ActionId)
    (entities: RenderEntity list) : Map<string, float * float> =
    entities
    |> List.choose (fun entity ->
        match entity with
        | REPoint(id, x, y) ->
            let readSlot path fallback =
                let ref = { ActionId = sketchId; Path = path }
                match Map.tryFind ref slotLookup with
                | Some s when s < paramValues.Length -> paramValues.[s]
                | _ -> fallback
            let rx = readSlot (sprintf "sketch.entity.%s.x" id) x
            let ry = readSlot (sprintf "sketch.entity.%s.y" id) y
            Some (id, (rx, ry))
        | _ -> None)
    |> Map.ofList

let private resolveScalar
    (slotLookup: Map<SlotRef, Slot>)
    (paramValues: float[])
    (sketchId: ActionId)
    (path: string) (fallback: float) : float =
    let ref = { ActionId = sketchId; Path = path }
    match Map.tryFind ref slotLookup with
    | Some s when s < paramValues.Length -> paramValues.[s]
    | _ -> fallback

/// Colour used for sketch points.
let private SKETCH_POINT : float32[] = [| 0.231f; 0.231f; 0.231f; 1.0f |]

let private GRID_MINOR : float32[] = [| 0.835f; 0.816f; 0.769f; 0.35f |]
let private GRID_MAJOR : float32[] = [| 0.835f; 0.816f; 0.769f; 0.75f |]

let private AXIS_X_COLOUR : float32[] = [| 0.75f; 0.30f; 0.30f; 0.95f |]
let private AXIS_Y_COLOUR : float32[] = [| 0.30f; 0.65f; 0.30f; 0.95f |]

let private DIM_COLOUR : float32[]   = [| 0.427f; 0.341f; 0.192f; 1.0f |]
let private DIM_HOVER_COLOUR : float32[] = [| 0.725f; 0.510f; 0.170f; 1.0f |]
let private FIXED_COLOUR : float32[] = [| 0.690f; 0.350f; 0.416f; 1.0f |]

let private DIM_OFFSET = 1.8  // perpendicular distance for default label

/// Default anchor for a linear Distance-like constraint between two points.
/// Matches the TS viewer's `fallbackAnchor = mid + perp(b-a) * 1.8`.
let distanceAnchorFallback (a: float * float) (b: float * float) : float * float =
    let (ax, ay) = a
    let (bx, by) = b
    let dx, dy = bx - ax, by - ay
    let len = sqrt (dx * dx + dy * dy)
    let nx, ny =
        if len < 1e-9 then 0.0, 1.0
        else -dy / len, dx / len  // perpendicular
    let mx, my = (ax + bx) * 0.5, (ay + by) * 0.5
    (mx + nx * DIM_OFFSET, my + ny * DIM_OFFSET)

/// Emit the extension + dimension + leader lines for a linear distance
/// constraint. Mirrors the TS viewer's `pushPointDistanceGeometry`.
/// Anchor comes from the constraint's labelPosition (Some) or the default
/// fallback above (None). `colour` is DIM_COLOUR or DIM_HOVER_COLOUR
/// depending on whether the dimension is active.
let private pushDistanceLines
    (out: ResizeArray<float32>) (a: float * float) (b: float * float)
    (anchor: float * float) (colour: float32[]) =
    let (ax, ay) = a
    let (bx, by) = b
    let (anX, anY) = anchor
    let dx, dy = bx - ax, by - ay
    let len = sqrt (dx * dx + dy * dy)
    if len < 1e-9 then () else
    // Unit along a→b and its 90° perp (ccw).
    let axX, axY = dx / len, dy / len
    let nX, nY = -axY, axX
    let mX, mY = (ax + bx) * 0.5, (ay + by) * 0.5
    // Signed distance from midpoint to anchor along the perpendicular.
    let offsetAmount = (anX - mX) * nX + (anY - mY) * nY
    let offX, offY = nX * offsetAmount, nY * offsetAmount
    let aaX, aaY = ax + offX, ay + offY
    let bbX, bbY = bx + offX, by + offY
    // Projection of the anchor onto the aa–bb line.
    let projParam = (anX - aaX) * axX + (anY - aaY) * axY
    let projX, projY = aaX + axX * projParam, aaY + axY * projParam
    let extentA = (projX - aaX) * axX + (projY - aaY) * axY
    let extentB = (projX - bbX) * axX + (projY - bbY) * axY
    // Extension lines a → aa, b → bb.
    pushSegment out (ax, ay) (aaX, aaY) colour
    pushSegment out (bx, by) (bbX, bbY) colour
    // Main dimension line aa → bb.
    pushSegment out (aaX, aaY) (bbX, bbY) colour
    // Extend dimension line past endpoint if anchor is outside.
    if extentA < 0.0 then pushSegment out (projX, projY) (aaX, aaY) colour
    elif extentB > 0.0 then pushSegment out (bbX, bbY) (projX, projY) colour
    // Leader line from projection to the label anchor.
    pushSegment out (projX, projY) (anX, anY) colour

/// Emit a small + tick for a Fixed-point constraint.
let private pushFixedTick (out: ResizeArray<float32>) (p: float * float) =
    let (px, py) = p
    let d = 0.75
    pushSegment out (px - d, py - d) (px + d, py + d) FIXED_COLOUR
    pushSegment out (px - d, py + d) (px + d, py - d) FIXED_COLOUR

let private isDimActive
    (sketchId: ActionId) (idx: int)
    (hovered: SelectionTarget option) (selected: SelectionTarget list) : bool =
    let matches (t: SelectionTarget) =
        match t with
        | TargetDimension(sid, i) -> sid = sketchId && i = idx
        | _ -> false
    (match hovered with Some h -> matches h | None -> false)
    || List.exists matches selected

/// Foot of the perpendicular from point p onto segment a–b (extended to
/// a line). Returns a coincident with a when a=b.
let private perpFoot (p: float * float) (a: float * float) (b: float * float) : float * float =
    let pX, pY = p
    let aX, aY = a
    let bX, bY = b
    let dx, dy = bX - aX, bY - aY
    let len2 = dx * dx + dy * dy
    if len2 < 1e-18 then a
    else
        let t = ((pX - aX) * dx + (pY - aY) * dy) / len2
        (aX + dx * t, aY + dy * t)

/// Closest point on a circle to an external point p.
let private closestOnCircle (p: float * float) (center: float * float) (radius: float) : float * float =
    let pX, pY = p
    let cX, cY = center
    let dx, dy = pX - cX, pY - cY
    let len = sqrt (dx * dx + dy * dy)
    if len < 1e-9 then (cX + radius, cY)
    else (cX + dx / len * radius, cY + dy / len * radius)

/// Intersection point of two lines (each given by two points).
/// Returns None for parallel/degenerate lines.
let private lineIntersection
    (a1: float * float) (a2: float * float)
    (b1: float * float) (b2: float * float) : (float * float) option =
    let a1X, a1Y = a1
    let a2X, a2Y = a2
    let b1X, b1Y = b1
    let b2X, b2Y = b2
    let dxA, dyA = a2X - a1X, a2Y - a1Y
    let dxB, dyB = b2X - b1X, b2Y - b1Y
    let denom = dxA * dyB - dyA * dxB
    if abs denom < 1e-9 then None
    else
        let t = ((b1X - a1X) * dyB - (b1Y - a1Y) * dxB) / denom
        Some (a1X + dxA * t, a1Y + dyA * t)

/// Resolve a circle's current radius (live from slots, fallback to baseline).
let private circleRadius
    (slotLookup: Map<SlotRef, Slot>)
    (paramValues: float[])
    (sketchId: ActionId)
    (entities: RenderEntity list)
    (circleId: string) : float option =
    entities
    |> List.tryPick (fun e ->
        match e with
        | RECircle(id, _, baseR) when id = circleId ->
            Some (resolveScalar slotLookup paramValues sketchId
                    (sprintf "sketch.entity.%s.radius" id) baseR)
        | _ -> None)

/// Curried radius lookup bound to a sketch, for reuse across multiple
/// constraint-rendering calls.
let circleRadiusLookup
    (slotLookup: Map<SlotRef, Slot>)
    (paramValues: float[])
    (sketchId: ActionId)
    (entities: RenderEntity list) : string -> float option =
    circleRadius slotLookup paramValues sketchId entities

/// Signed sweep from startAngle to endAngle in the given direction (ccw or
/// cw). Always returns a non-negative number when travelling in the chosen
/// direction — used by angle-dimension rendering to match the old TS
/// viewer's `normalizedSweep`.
let private normalizedSweep (startAngle: float) (endAngle: float) (ccw: bool) : float =
    let tau = 2.0 * System.Math.PI
    let mutable sweep = endAngle - startAngle
    if ccw then
        while sweep < 0.0 do sweep <- sweep + tau
    else
        while sweep > 0.0 do sweep <- sweep - tau
    sweep

/// Draw a polyline arc between two angles at `apex`, travelling in the
/// ccw/cw direction implied by `ccw`.
let private pushAngleArc
    (out: ResizeArray<float32>)
    (apex: float * float)
    (radius: float)
    (startAngle: float) (endAngle: float)
    (ccw: bool)
    (colour: float32[]) =
    let aX, aY = apex
    let sweep = normalizedSweep startAngle endAngle ccw
    let segments =
        let bySweep = int (ceil (abs sweep * 12.0 / System.Math.PI))
        max 12 bySweep
    let mutable prev = (aX + radius * cos startAngle, aY + radius * sin startAngle)
    for i in 1 .. segments do
        let t = float i / float segments
        let ang = startAngle + sweep * t
        let next = (aX + radius * cos ang, aY + radius * sin ang)
        pushSegment out prev next colour
        prev <- next

/// Line-line intersection parametrised as origin+dir (not the two-points
/// version). Returns None when parallel.
let private lineIntersectionDir
    (originA: float * float) (dirA: float * float)
    (originB: float * float) (dirB: float * float) : (float * float) option =
    let oAx, oAy = originA
    let dAx, dAy = dirA
    let oBx, oBy = originB
    let dBx, dBy = dirB
    let denom = dAx * dBy - dAy * dBx
    if abs denom < 1e-9 then None
    else
        let deltaX, deltaY = oBx - oAx, oBy - oAy
        let t = (deltaX * dBy - deltaY * dBx) / denom
        Some (oAx + dAx * t, oAy + dAy * t)

/// Vertex + directed rays + bisector direction for an Angle constraint.
/// Mirrors the old TS `resolveAngleGeometry`.
let private resolveAngleGeometry
    (aStart: float * float) (aEnd: float * float)
    (bStart: float * float) (bEnd: float * float)
    (aReverse: bool) (bReverse: bool) (ccw: bool)
    : (float * float) * (float * float) * (float * float) * (float * float) option =
    let sub (x1, y1) (x2, y2) = (x1 - x2, y1 - y2)
    let len (x, y) = sqrt (x * x + y * y)
    let normalize ((x, y) as v) =
        let l = len v
        if l < 1e-6 then (0.0, 0.0) else (x / l, y / l)
    let aVertex = if aReverse then aEnd else aStart
    let bVertex = if bReverse then bEnd else bStart
    let rayA = normalize (if aReverse then sub aStart aEnd else sub aEnd aStart)
    let rayB = normalize (if bReverse then sub bStart bEnd else sub bEnd bStart)
    if len rayA < 1e-6 || len rayB < 1e-6 then
        aVertex, rayA, rayB, None
    else
        let vertex =
            if len (sub aVertex bVertex) < 1e-4 then aVertex
            else
                match lineIntersectionDir aVertex rayA bVertex rayB with
                | Some v -> v
                | None -> aVertex
        let angA = atan2 (snd rayA) (fst rayA)
        let angB = atan2 (snd rayB) (fst rayB)
        let sweep = normalizedSweep angA angB ccw
        let midAngle = angA + sweep * 0.5
        vertex, rayA, rayB, Some (cos midAngle, sin midAngle)

/// Default label anchor for a constraint when the user hasn't dragged it.
/// Kept here (rather than in LabelBuilder) so the constraint-line renderer
/// and the label renderer stay in sync — both use this for lp=None.
let dimensionFallbackAnchor
    (points: Map<string, float * float>)
    (radiusLookup: string -> float option)
    (c: SketchConstraint) : LabelPos option =
    let pt id = Map.tryFind id points
    let mid (a: float * float) (b: float * float) =
        ((fst a + fst b) * 0.5, (snd a + snd b) * 0.5)
    let linear (a: float * float) (b: float * float) =
        let (x, y) = distanceAnchorFallback a b
        Some { X = x; Y = y }
    match c with
    | Distance(a, b, _, _) ->
        match pt a, pt b with
        | Some pa, Some pb -> linear pa pb
        | _ -> None
    | LineDistance(aS, aE, bS, bE, _, _, _, _) ->
        match pt aS, pt aE, pt bS, pt bE with
        | Some a1, Some a2, Some b1, Some b2 -> linear (mid a1 a2) (mid b1 b2)
        | _ -> None
    | PointLineDistance(point, _, aS, aE, _, _) ->
        match pt point, pt aS, pt aE with
        | Some p, Some a, Some b -> linear p (perpFoot p a b)
        | _ -> None
    | PointCircleDistance(point, circleId, centerId, _, _) ->
        match pt point, pt centerId, radiusLookup circleId with
        | Some p, Some c, Some r -> linear p (closestOnCircle p c r)
        | _ -> None
    | LineCircleDistance(_, aS, aE, circleId, centerId, _, _) ->
        match pt aS, pt aE, pt centerId, radiusLookup circleId with
        | Some a, Some b, Some c, Some r ->
            let foot = perpFoot c a b
            linear foot (closestOnCircle foot c r)
        | _ -> None
    | CircleCircleDistance(circleA, centerA, circleB, centerB, _, _, _) ->
        match pt centerA, pt centerB, radiusLookup circleA, radiusLookup circleB with
        | Some cA, Some cB, Some rA, Some rB ->
            linear (closestOnCircle cB cA rA) (closestOnCircle cA cB rB)
        | _ -> None
    | CircleDiameter(circleId, centerId, _, _) ->
        match pt centerId, radiusLookup circleId with
        | Some (cx, cy), Some r -> Some { X = cx + r + DIM_OFFSET; Y = cy }
        | _ -> None
    | Angle(aStart, aEnd, bStart, bEnd, _, _, _, aReverse, bReverse, ccw, _) ->
        match pt aStart, pt aEnd, pt bStart, pt bEnd with
        | Some pa1, Some pa2, Some pb1, Some pb2 ->
            let (vX, vY), _, _, midOpt =
                resolveAngleGeometry pa1 pa2 pb1 pb2 aReverse bReverse ccw
            match midOpt with
            | Some (mX, mY) -> Some { X = vX + mX * 4.4; Y = vY + mY * 4.4 }
            | None -> None
        | _ -> None
    // FrameDistance / FrameLineDistance need a frame position lookup that
    // isn't available here yet — keep the at-point fallback.
    | FrameDistance(point, _, _, _, _) ->
        pt point |> Option.map (fun (x, y) -> { X = x + DIM_OFFSET; Y = y })
    | FrameLineDistance(_, aS, aE, _, _, _, _) ->
        match pt aS, pt aE with
        | Some a, Some b -> linear a b
        | _ -> None
    | _ -> None

/// Emit the geometry for a single constraint using the given colour.
/// Shared between the real constraint renderer and the placement preview.
let private pushConstraintGeometry
    (out: ResizeArray<float32>)
    (points: Map<string, float * float>)
    (radiusOf: string -> float option)
    (showDimensions: bool)
    (colour: float32[])
    (c: SketchConstraint) : unit =
    let pt id = Map.tryFind id points
    let midpoint (a: float * float) (b: float * float) =
        ((fst a + fst b) * 0.5, (snd a + snd b) * 0.5)
    let anchorFor (lp: LabelPos option) (defaultAnchor: float * float) =
        match lp with
        | Some p -> p.X, p.Y
        | None -> defaultAnchor
    let linear (a: float * float) (b: float * float) (lp: LabelPos option) =
        if showDimensions then
            let anchor = anchorFor lp (distanceAnchorFallback a b)
            pushDistanceLines out a b anchor colour

    match c with
    | Fixed(p, _, _) ->
        match pt p with
        | Some point -> pushFixedTick out point
        | None -> ()
    | _ when not showDimensions -> ()
    | Distance(a, b, _, lp) ->
        match pt a, pt b with
        | Some pa, Some pb -> linear pa pb lp
        | _ -> ()
    | LineDistance(aS, aE, bS, bE, _, _, _, lp) ->
        match pt aS, pt aE, pt bS, pt bE with
        | Some pa1, Some pa2, Some pb1, Some pb2 ->
            linear (midpoint pa1 pa2) (midpoint pb1 pb2) lp
        | _ -> ()
    | PointLineDistance(point, _, aS, aE, _, lp) ->
        match pt point, pt aS, pt aE with
        | Some p, Some a, Some b -> linear p (perpFoot p a b) lp
        | _ -> ()
    | PointCircleDistance(point, circleId, centerId, _, lp) ->
        match pt point, pt centerId, radiusOf circleId with
        | Some p, Some c, Some r -> linear p (closestOnCircle p c r) lp
        | _ -> ()
    | LineCircleDistance(_, aS, aE, circleId, centerId, _, lp) ->
        match pt aS, pt aE, pt centerId, radiusOf circleId with
        | Some a, Some b, Some c, Some r ->
            let foot = perpFoot c a b
            linear foot (closestOnCircle foot c r) lp
        | _ -> ()
    | CircleCircleDistance(circleA, centerA, circleB, centerB, _, _, lp) ->
        match pt centerA, pt centerB, radiusOf circleA, radiusOf circleB with
        | Some cA, Some cB, Some rA, Some rB ->
            linear (closestOnCircle cB cA rA) (closestOnCircle cA cB rB) lp
        | _ -> ()
    | CircleDiameter(circleId, centerId, _, lp) ->
        match pt centerId, radiusOf circleId with
        | Some (cx, cy), Some r ->
            let dirX, dirY =
                match lp with
                | Some p ->
                    let dx, dy = p.X - cx, p.Y - cy
                    let len = sqrt (dx * dx + dy * dy)
                    if len < 1e-9 then 1.0, 0.0 else dx / len, dy / len
                | None -> 1.0, 0.0
            let a = (cx - dirX * r, cy - dirY * r)
            let b = (cx + dirX * r, cy + dirY * r)
            let anchor =
                match lp with
                | Some p -> p.X, p.Y
                | None -> (cx + dirX * (r + DIM_OFFSET), cy + dirY * (r + DIM_OFFSET))
            pushSegment out a b colour
            pushSegment out b anchor colour
        | _ -> ()
    | Angle(aStart, aEnd, bStart, bEnd, _, _, _, aReverse, bReverse, ccw, lp) ->
        match pt aStart, pt aEnd, pt bStart, pt bEnd with
        | Some pa1, Some pa2, Some pb1, Some pb2 ->
            let vertex, rayA, rayB, midOpt =
                resolveAngleGeometry pa1 pa2 pb1 pb2 aReverse bReverse ccw
            match midOpt with
            | Some midDir ->
                let vX, vY = vertex
                let mX, mY = midDir
                let fallbackAnchor = (vX + mX * 4.4, vY + mY * 4.4)
                let aX, aY =
                    match lp with
                    | Some p -> p.X, p.Y
                    | None -> fallbackAnchor
                let anchorVx, anchorVy = aX - vX, aY - vY
                let anchorRadius = sqrt (anchorVx * anchorVx + anchorVy * anchorVy)
                if anchorRadius > 1e-6 then
                    let anchorAngle = atan2 anchorVy anchorVx
                    let startAngle = atan2 (snd rayA) (fst rayA)
                    let endAngle = atan2 (snd rayB) (fst rayB)
                    let arcSweep = abs (normalizedSweep startAngle endAngle ccw)
                    let anchorSweep = abs (normalizedSweep startAngle anchorAngle ccw)
                    let anchorInsideSector = anchorSweep <= arcSweep + 1e-6
                    let r =
                        if anchorInsideSector then anchorRadius - 0.8
                        else anchorRadius
                    if r > 1e-6 then
                        let extendAfterEnd = abs (normalizedSweep endAngle anchorAngle ccw)
                        let extendBeforeStart = abs (normalizedSweep anchorAngle startAngle ccw)
                        let extendStart = (not anchorInsideSector) && extendBeforeStart < extendAfterEnd
                        let arcStartAngle =
                            if extendStart then anchorAngle else startAngle
                        let arcEndAngle =
                            if (not anchorInsideSector) && (not extendStart) then anchorAngle
                            else endAngle
                        // Rays out from the apex along each line.
                        pushSegment out vertex (vX + fst rayA * r, vY + snd rayA * r) colour
                        pushSegment out vertex (vX + fst rayB * r, vY + snd rayB * r) colour
                        // The arc between them.
                        pushAngleArc out vertex r arcStartAngle arcEndAngle ccw colour
            | None -> ()
        | _ -> ()
    | _ -> ()

/// Rewrite a dimensional constraint's labelPosition. Used when previewing
/// pending placements so the label follows the cursor.
let withLabelPosition (lp: LabelPos) (c: SketchConstraint) : SketchConstraint =
    match c with
    | Distance(a, b, d, _) -> Distance(a, b, d, Some lp)
    | FrameDistance(p, f, part, d, _) -> FrameDistance(p, f, part, d, Some lp)
    | LineDistance(aS, aE, bS, bE, lA, lB, d, _) -> LineDistance(aS, aE, bS, bE, lA, lB, d, Some lp)
    | FrameLineDistance(lA, aS, aE, f, part, d, _) -> FrameLineDistance(lA, aS, aE, f, part, d, Some lp)
    | PointLineDistance(p, l, aS, aE, d, _) -> PointLineDistance(p, l, aS, aE, d, Some lp)
    | PointCircleDistance(p, c2, ctr, d, _) -> PointCircleDistance(p, c2, ctr, d, Some lp)
    | LineCircleDistance(lA, aS, aE, circ, ctr, d, _) -> LineCircleDistance(lA, aS, aE, circ, ctr, d, Some lp)
    | CircleCircleDistance(cA, ctrA, cB, ctrB, d, i, _) -> CircleCircleDistance(cA, ctrA, cB, ctrB, d, i, Some lp)
    | CircleDiameter(cId, ctr, d, _) -> CircleDiameter(cId, ctr, d, Some lp)
    | Angle(aS, aE, bS, bE, lA, lB, ang, ar, br, ccw, _) -> Angle(aS, aE, bS, bE, lA, lB, ang, ar, br, ccw, Some lp)
    | other -> other

/// Preview colour for in-progress dimension placement.
let private PLACEMENT_PREVIEW_COLOUR : float32[] = [| 0.502f; 0.745f; 0.549f; 0.85f |]

/// Line-buffer for an in-progress constraint placement — same geometry as
/// the eventual constraint, drawn at the cursor position.
let buildPendingConstraintLineBuffer
    (sketchId: ActionId)
    (entities: RenderEntity list)
    (slotLookup: Map<SlotRef, Slot>)
    (paramValues: float[])
    (pending: SketchConstraint)
    (cursor: LabelPos) : float32[] =
    let points = resolvePointMap slotLookup paramValues sketchId entities
    let radiusOf = circleRadius slotLookup paramValues sketchId entities
    let out = ResizeArray<float32>()
    pushConstraintGeometry out points radiusOf true PLACEMENT_PREVIEW_COLOUR
        (withLabelPosition cursor pending)
    out.ToArray()

/// Build the vertex buffer of constraint-visualization line segments
/// (dimension lines, extension lines, H/V ticks, Fixed crosshairs).
/// Vertex format identical to the sketch-line buffer. Active dimensions
/// render in the hover/selected colour.
let buildSketchConstraintLinesBuffer
    (sketchId: ActionId)
    (sketch: ActionSketch)
    (slotLookup: Map<SlotRef, Slot>)
    (paramValues: float[])
    (showDimensions: bool)
    (hovered: SelectionTarget option)
    (selected: SelectionTarget list) : float32[] =
    let points = resolvePointMap slotLookup paramValues sketchId sketch.Entities
    let radiusOf = circleRadius slotLookup paramValues sketchId sketch.Entities
    let out = ResizeArray<float32>()
    sketch.Constraints
    |> List.iteri (fun i c ->
        let active = isDimActive sketchId i hovered selected
        let colour = if active then DIM_HOVER_COLOUR else DIM_COLOUR
        pushConstraintGeometry out points radiusOf showDimensions colour c)
    out.ToArray()


// ── Tool preview ────────────────────────────────────────────────────────

let private PREVIEW_LINE : float32[]  = [| 0.502f; 0.745f; 0.549f; 0.72f |]
let private PREVIEW_POINT : float32[] = [| 0.502f; 0.745f; 0.549f; 0.92f |]

/// Line-list preview buffer for the currently-active tool.
let buildToolPreviewLineBuffer
    (tool: string)
    (toolPoints: LabelPos list)
    (cursor: (float * float) option) : float32[] =
    let out = ResizeArray<float32>()
    let points = toolPoints |> List.map (fun p -> p.X, p.Y)

    match tool, points, cursor with
    | "line", p0 :: _, Some c ->
        pushSegment out p0 c PREVIEW_LINE
    | "rectangle", p0 :: _, Some c ->
        let (x0, y0), (x1, y1) = p0, c
        let corners = [ (x0, y0); (x1, y0); (x1, y1); (x0, y1) ]
        corners
        |> List.iteri (fun i a ->
            let b = corners.[(i + 1) % 4]
            pushSegment out a b PREVIEW_LINE)
    | "circle", p0 :: _, Some c ->
        let (cx, cy), (mx, my) = p0, c
        let dx, dy = mx - cx, my - cy
        let radius = max 1e-6 (sqrt (dx * dx + dy * dy))
        pushCircle out p0 radius PREVIEW_LINE
    | "arc", [ center; startPt ], Some c ->
        // Two points fixed (center and start). Preview a curved arc that
        // ends at the cursor's projection onto the start's radius.
        let (cx, cy), (sx, sy), (mx, my) = center, startPt, c
        let dx, dy = mx - cx, my - cy
        let len = sqrt (dx * dx + dy * dy)
        if len > 1e-9 then
            let r0 = sqrt ((sx - cx) * (sx - cx) + (sy - cy) * (sy - cy))
            let projX = cx + dx / len * r0
            let projY = cy + dy / len * r0
            let clockwise =
                let vx, vy = sx - cx, sy - cy
                let wx, wy = mx - cx, my - cy
                (vx * wy - vy * wx) < 0.0
            pushArc out startPt (projX, projY) center clockwise PREVIEW_LINE
    | "arc", [ p0 ], Some c ->
        pushSegment out p0 c PREVIEW_LINE
    | _ -> ()

    out.ToArray()

/// Point instance buffer (7 floats per instance) for the currently-active tool.
let buildToolPreviewPointBuffer
    (tool: string)
    (toolPoints: LabelPos list)
    (cursor: (float * float) option) : float32[] =
    let pushInstance (out: ResizeArray<float32>) ((x, y): float * float) =
        out.Add(float32 x)
        out.Add(float32 y)
        out.Add 5.5f
        out.Add PREVIEW_POINT.[0]
        out.Add PREVIEW_POINT.[1]
        out.Add PREVIEW_POINT.[2]
        out.Add PREVIEW_POINT.[3]

    let pts = toolPoints |> List.map (fun p -> p.X, p.Y)
    let out = ResizeArray<float32>()

    match tool, pts, cursor with
    | ("line" | "rectangle" | "roundedRectangle" | "circle"), _, _ ->
        let all =
            match cursor with
            | Some c -> pts @ [ c ]
            | None -> pts
        all |> List.iter (pushInstance out)
    | "arc", ps, cOpt ->
        ps |> List.iter (pushInstance out)
        match cOpt with
        | Some c -> pushInstance out c
        | None -> ()
    | _ -> ()

    out.ToArray()

/// Gizmo: short X and Y axis line segments at the sketch origin.
/// Axis length is a fixed value in sketch-local coords (~10 units). Drawn
/// via the normal line pipeline (same vertex format).
let buildSketchGizmoBuffer () : float32[] =
    let length = 10.0
    let out = ResizeArray<float32>()
    pushSegment out (0.0, 0.0) (length, 0.0) AXIS_X_COLOUR
    pushSegment out (0.0, 0.0) (0.0, length) AXIS_Y_COLOUR
    out.ToArray()

let private computeSketchBounds
    (points: Map<string, float * float>)
    (entities: RenderEntity list) : (float * float) * (float * float) =
    let contributions =
        entities
        |> List.collect (fun e ->
            match e with
            | REPoint(id, _, _) ->
                match Map.tryFind id points with
                | Some p -> [ p ]
                | None -> []
            | RECircle(_, centerId, fallbackR) ->
                match Map.tryFind centerId points with
                | Some (cx, cy) ->
                    [ (cx - fallbackR, cy - fallbackR)
                      (cx + fallbackR, cy + fallbackR) ]
                | None -> []
            | _ -> [])
    if List.isEmpty contributions then
        (-10.0, -10.0), (10.0, 10.0)
    else
        let xs = contributions |> List.map fst
        let ys = contributions |> List.map snd
        (List.min xs, List.min ys), (List.max xs, List.max ys)

/// Build a line-list grid covering the sketch's extent plus a margin.
/// Minor lines at every `step` unit, major every `majorEvery * step`.
let buildSketchGridBuffer
    (sketchId: ActionId)
    (entities: RenderEntity list)
    (slotLookup: Map<SlotRef, Slot>)
    (paramValues: float[])
    (step: float) (majorEvery: int) : float32[] =
    let points = resolvePointMap slotLookup paramValues sketchId entities
    let (minP, maxP) = computeSketchBounds points entities
    let (minX, minY) = minP
    let (maxX, maxY) = maxP
    let margin = step * float majorEvery
    let loX, hiX = minX - margin, maxX + margin
    let loY, hiY = minY - margin, maxY + margin

    let out = ResizeArray<float32>()

    [ int (floor (loX / step)) .. int (ceil (hiX / step)) ]
    |> List.iter (fun i ->
        let x = float i * step
        let colour = if i % majorEvery = 0 then GRID_MAJOR else GRID_MINOR
        pushSegment out (x, loY) (x, hiY) colour)

    [ int (floor (loY / step)) .. int (ceil (hiY / step)) ]
    |> List.iter (fun i ->
        let y = float i * step
        let colour = if i % majorEvery = 0 then GRID_MAJOR else GRID_MINOR
        pushSegment out (loX, y) (hiX, y) colour)

    out.ToArray()

/// Default point radius in pixels.
let private POINT_RADIUS_PX = 5.0f

/// Build an instance buffer for one sketch's points.
/// Each instance = 7 floats: (cx, cy, radiusPx, r, g, b, a).
/// Active (hovered / selected) points render bigger in accent colour.
let buildSketchPointBuffer
    (sketchId: ActionId)
    (entities: RenderEntity list)
    (slotLookup: Map<SlotRef, Slot>)
    (paramValues: float[])
    (hovered: SelectionTarget option)
    (selected: SelectionTarget list) : float32[] =
    entities
    |> List.collect (fun entity ->
        match entity with
        | REPoint(id, x, y) ->
            let readSlot path fallback =
                let ref = { ActionId = sketchId; Path = path }
                match Map.tryFind ref slotLookup with
                | Some s when s < paramValues.Length -> paramValues.[s]
                | _ -> fallback
            let rx = readSlot (sprintf "sketch.entity.%s.x" id) x
            let ry = readSlot (sprintf "sketch.entity.%s.y" id) y
            let active = isEntityActive sketchId "point" id hovered selected
            let colour, radius =
                if active then ACCENT, POINT_RADIUS_PX * 1.5f
                else SKETCH_POINT, POINT_RADIUS_PX
            [ float32 rx; float32 ry; radius
              colour.[0]; colour.[1]; colour.[2]; colour.[3] ]
        | _ -> [])
    |> List.toArray

/// Thickness (in 2D sketch coords) used for thick-line pick geometry.
/// Append one instance worth of data (ax, ay, bx, by, pickId) per line
/// segment for the thick-line pick pipeline.
let private pushPickSegment (out: ResizeArray<float32>) (a: float * float) (b: float * float) (pickId: int) =
    let (ax, ay) = a
    let (bx, by) = b
    out.Add(float32 ax)
    out.Add(float32 ay)
    out.Add(float32 bx)
    out.Add(float32 by)
    out.Add(float32 pickId)

/// Tessellate a full circle as consecutive line segments, each carrying
/// the circle's pickId.
let private pushPickCircle (out: ResizeArray<float32>) (center: float * float) (radius: float) (pickId: int) =
    let (cx, cy) = center
    let n = CIRCLE_SEGMENTS
    let twoPi = 2.0 * System.Math.PI
    let mutable prev = (cx + radius, cy)
    for i in 1 .. n do
        let t = twoPi * float i / float n
        let next = (cx + radius * cos t, cy + radius * sin t)
        pushPickSegment out prev next pickId
        prev <- next

let private pushPickArc
    (out: ResizeArray<float32>)
    (startP: float * float) (endP: float * float) (center: float * float)
    (clockwise: bool) (pickId: int) =
    let (sx, sy) = startP
    let (ex, ey) = endP
    let (cx, cy) = center
    let radius = sqrt ((sx - cx) * (sx - cx) + (sy - cy) * (sy - cy))
    if radius < 1e-9 then
        pushPickSegment out startP endP pickId
    else
        let startAngle = atan2 (sy - cy) (sx - cx)
        let endAngle = atan2 (ey - cy) (ex - cx)
        let tau = 2.0 * System.Math.PI
        let sweep =
            if clockwise then
                let mutable d = startAngle - endAngle
                while d < 0.0 do d <- d + tau
                -d
            else
                let mutable d = endAngle - startAngle
                while d < 0.0 do d <- d + tau
                d
        let segments = max 4 (int (abs sweep / (tau / float CIRCLE_SEGMENTS)))
        let mutable prev = startP
        for i in 1 .. segments do
            let t = sweep * float i / float segments
            let ang = startAngle + t
            let next = (cx + radius * cos ang, cy + radius * sin ang)
            pushPickSegment out prev next pickId
            prev <- next

/// Build an instance buffer for picking sketch lines, circles and arcs.
/// Each instance = 5 floats: (ax, ay, bx, by, pickIdAsFloat).
let buildSketchPickLineBuffer
    (sketchId: ActionId)
    (entities: RenderEntity list)
    (slotLookup: Map<SlotRef, Slot>)
    (paramValues: float[])
    (pickables: Pickable list) : float32[] =
    let idByKey =
        pickables
        |> List.choose (fun p ->
            match p with
            | PickLine(pid, sid, eid, _, _) when sid = sketchId -> Some ("line:" + eid, pid)
            | PickCircle(pid, sid, eid, _, _) when sid = sketchId -> Some ("circle:" + eid, pid)
            | PickArc(pid, sid, eid, _, _, _, _) when sid = sketchId -> Some ("arc:" + eid, pid)
            | _ -> None)
        |> Map.ofList
    let lookup key = Map.tryFind key idByKey

    let out = ResizeArray<float32>()
    let points = resolvePointMap slotLookup paramValues sketchId entities

    entities
    |> List.iter (fun entity ->
        match entity with
        | REPoint _ -> ()
        | RELine(id, startId, endId) ->
            match lookup ("line:" + id), Map.tryFind startId points, Map.tryFind endId points with
            | Some pid, Some a, Some b -> pushPickSegment out a b pid
            | _ -> ()
        | RECircle(id, centerId, fallbackRadius) ->
            match lookup ("circle:" + id), Map.tryFind centerId points with
            | Some pid, Some c ->
                let r = resolveScalar slotLookup paramValues sketchId
                                (sprintf "sketch.entity.%s.radius" id) fallbackRadius
                pushPickCircle out c r pid
            | _ -> ()
        | REArc(id, startId, endId, ArcCenter(centerId, cw)) ->
            match
                lookup ("arc:" + id),
                Map.tryFind startId points,
                Map.tryFind endId points,
                Map.tryFind centerId points with
            | Some pid, Some s, Some e, Some c -> pushPickArc out s e c cw pid
            | _ -> ()
        | REArc(_, _, _, ArcThreePoint _) -> ())

    out.ToArray()

// ─── Loop resolution + triangulation (for fill + picking) ───────────────

let private LOOP_FILL : float32[] = [| 0.741f; 0.694f; 0.575f; 0.18f |]
let private LOOP_HIGHLIGHT : float32[] = [| 0.502f; 0.745f; 0.549f; 0.2f |]

let private sampleCircleBoundary (center: float * float) (radius: float) (segments: int) : (float * float) list =
    let (cx, cy) = center
    [ for i in 0 .. segments ->
        let ang = float i / float segments * 2.0 * System.Math.PI
        (cx + radius * cos ang, cy + radius * sin ang) ]

let private sampleArcBoundary (startP: float * float) (endP: float * float) (center: float * float) (cw: bool) (segments: int) : (float * float) list =
    let (sx, sy) = startP
    let (ex, ey) = endP
    let (cx, cy) = center
    let startAngle = atan2 (sy - cy) (sx - cx)
    let endAngle = atan2 (ey - cy) (ex - cx)
    let radius = sqrt ((sx - cx) * (sx - cx) + (sy - cy) * (sy - cy))
    let mutable sweep = endAngle - startAngle
    if cw && sweep > 0.0 then sweep <- sweep - 2.0 * System.Math.PI
    elif not cw && sweep < 0.0 then sweep <- sweep + 2.0 * System.Math.PI
    [ for i in 0 .. segments ->
        let t = float i / float segments
        let ang = startAngle + sweep * t
        (cx + radius * cos ang, cy + radius * sin ang) ]

let private near2 (a: float * float) (b: float * float) =
    let (ax, ay) = a
    let (bx, by) = b
    let dx, dy = ax - bx, ay - by
    dx * dx + dy * dy < 1e-6

let private edgeForward
    (entity: RenderEntity)
    (points: Map<string, float * float>)
    (slotLookup: Map<SlotRef, Slot>) (paramValues: float[]) (sketchId: ActionId) : (float * float) list option =
    match entity with
    | RELine(_, startId, endId) ->
        match Map.tryFind startId points, Map.tryFind endId points with
        | Some s, Some e -> Some [s; e]
        | _ -> None
    | REArc(_, startId, endId, ArcCenter(centerId, cw)) ->
        match Map.tryFind startId points, Map.tryFind endId points, Map.tryFind centerId points with
        | Some s, Some e, Some c -> Some (sampleArcBoundary s e c cw 48)
        | _ -> None
    | _ -> None

let private resolveLoopBoundary
    (entityMap: Map<string, RenderEntity>)
    (points: Map<string, float * float>)
    (slotLookup: Map<SlotRef, Slot>) (paramValues: float[]) (sketchId: ActionId)
    (entityIds: string list) : (float * float) list option =
    match entityIds with
    | [ singleId ] ->
        match Map.tryFind singleId entityMap with
        | Some (RECircle(id, centerId, fallbackR)) ->
            match Map.tryFind centerId points with
            | Some c ->
                let r = resolveScalar slotLookup paramValues sketchId
                                (sprintf "sketch.entity.%s.radius" id) fallbackR
                Some (sampleCircleBoundary c r 48)
            | None -> None
        | _ -> None
    | _ ->
        let edges =
            entityIds
            |> List.choose (fun id ->
                Map.tryFind id entityMap
                |> Option.bind (fun e -> edgeForward e points slotLookup paramValues sketchId))
        if edges.Length <> entityIds.Length then None
        else
            let arr = edges |> List.toArray
            let used = System.Collections.Generic.HashSet<int>()
            used.Add 0 |> ignore
            let mutable ordered = arr.[0]
            let mutable tail = List.last ordered
            let mutable progress = true
            while used.Count < arr.Length && progress do
                progress <- false
                let mutable i = 0
                while i < arr.Length && not progress do
                    if not (used.Contains i) then
                        let edge = arr.[i]
                        let startsAtTail = near2 (List.head edge) tail
                        let endsAtTail = near2 (List.last edge) tail
                        if startsAtTail || endsAtTail then
                            let segment = if startsAtTail then edge else List.rev edge
                            ordered <- ordered @ List.tail segment
                            tail <- List.last segment
                            used.Add i |> ignore
                            progress <- true
                    i <- i + 1
            if used.Count = arr.Length then
                let closed =
                    if not (near2 (List.head ordered) tail) then ordered @ [ List.head ordered ]
                    else ordered
                if closed.Length >= 4 then Some closed else None
            else None

let private cross2 ((ax, ay): float * float) ((bx, by): float * float) = ax * by - ay * bx
let private sub2 ((ax, ay): float * float) ((bx, by): float * float) : float * float = (ax - bx, ay - by)

let private pointInTriangle (p: float * float) a b c =
    let s1 = cross2 (sub2 b a) (sub2 p a)
    let s2 = cross2 (sub2 c b) (sub2 p b)
    let s3 = cross2 (sub2 a c) (sub2 p c)
    let hasNeg = s1 < -1e-6 || s2 < -1e-6 || s3 < -1e-6
    let hasPos = s1 > 1e-6 || s2 > 1e-6 || s3 > 1e-6
    not (hasNeg && hasPos)

let private polygonSignedArea (points: (float * float)[]) : float =
    let n = points.Length
    let mutable area = 0.0
    for i in 0 .. n - 1 do
        let (ax, ay) = points.[i]
        let (bx, by) = points.[(i + 1) % n]
        area <- area + ax * by - bx * ay
    area * 0.5

let private sameP (a: float * float) (b: float * float) = near2 a b

let private triangulatePolygon (polygon: (float * float) list) : ((float * float) * (float * float) * (float * float)) list =
    let points = polygon |> List.toArray
    if points.Length < 3 then []
    else
        let winding = if polygonSignedArea points >= 0.0 then 1 else -1
        let mutable indices = [ 0 .. points.Length - 1 ]
        let triangles = ResizeArray<_>()
        let mutable guard = 0
        let maxGuard = points.Length * points.Length + 2
        while indices.Length > 2 && guard < maxGuard do
            let idxArr = List.toArray indices
            let mutable clipped = false
            let mutable i = 0
            while i < idxArr.Length && not clipped do
                let i0 = idxArr.[(i + idxArr.Length - 1) % idxArr.Length]
                let i1 = idxArr.[i]
                let i2 = idxArr.[(i + 1) % idxArr.Length]
                let a = points.[i0]
                let b = points.[i1]
                let c = points.[i2]
                let turn = cross2 (sub2 b a) (sub2 c b)
                let ccwOk = if winding > 0 then turn > 1e-6 else turn < -1e-6
                let earOk =
                    ccwOk &&
                    indices
                    |> List.forall (fun k ->
                        let p = points.[k]
                        sameP p a || sameP p b || sameP p c || not (pointInTriangle p a b c))
                if earOk then
                    triangles.Add (if winding > 0 then (a, b, c) else (a, c, b))
                    indices <- indices |> List.filter (fun x -> x <> i1)
                    clipped <- true
                i <- i + 1
            if not clipped then guard <- maxGuard
            guard <- guard + 1
        List.ofSeq triangles

let private pushTriangleVertex (out: ResizeArray<float32>) ((x, y): float * float) (colour: float32[]) =
    out.Add(float32 x)
    out.Add(float32 y)
    out.Add colour.[0]
    out.Add colour.[1]
    out.Add colour.[2]
    out.Add colour.[3]

let private isLoopActive
    (sketchId: ActionId) (loopId: string)
    (hovered: SelectionTarget option) (selected: SelectionTarget list) : bool =
    let matches (t: SelectionTarget) =
        match t with
        | TargetLoop(sid, lid) -> sid = sketchId && lid = loopId
        | _ -> false
    (match hovered with Some h -> matches h | None -> false)
    || List.exists matches selected

/// Triangle-list pick buffer for loops. 3 floats per vertex: (x, y, pickId).
let buildSketchLoopPickBuffer
    (sketchId: ActionId)
    (sketch: ActionSketch)
    (loops: SketchLoopView list)
    (slotLookup: Map<SlotRef, Slot>)
    (paramValues: float[])
    (pickables: Pickable list) : float32[] =
    let pickByLoopId =
        pickables
        |> List.choose (fun p ->
            match p with
            | PickLoop(pid, sid, lid, _) when sid = sketchId -> Some (lid, pid)
            | _ -> None)
        |> Map.ofList

    let entityMap =
        sketch.Entities
        |> List.map (fun e ->
            match e with
            | REPoint(id, _, _) -> id, e
            | RELine(id, _, _) -> id, e
            | RECircle(id, _, _) -> id, e
            | REArc(id, _, _, _) -> id, e)
        |> Map.ofList
    let points = resolvePointMap slotLookup paramValues sketchId sketch.Entities

    let push (out: ResizeArray<float32>) (x, y) (pid: int) =
        out.Add(float32 x)
        out.Add(float32 y)
        out.Add(float32 pid)

    let out = ResizeArray<float32>()
    loops
    |> List.iter (fun loop ->
        match Map.tryFind loop.Id pickByLoopId with
        | None -> ()
        | Some pickId ->
            match resolveLoopBoundary entityMap points slotLookup paramValues sketchId loop.EntityIds with
            | None -> ()
            | Some boundary ->
                let polygon =
                    if boundary.Length >= 2 && near2 (List.head boundary) (List.last boundary)
                    then boundary |> List.take (boundary.Length - 1)
                    else boundary
                for (a, b, c) in triangulatePolygon polygon do
                    push out a pickId
                    push out b pickId
                    push out c pickId)
    out.ToArray()

/// Build a triangle-list vertex buffer (6 floats per vertex: pos.xy + rgba)
/// for all loop fills in one sketch, optionally via ear-clip triangulation.
let buildSketchLoopFillBuffer
    (sketchId: ActionId)
    (sketch: ActionSketch)
    (loops: SketchLoopView list)
    (slotLookup: Map<SlotRef, Slot>)
    (paramValues: float[])
    (hovered: SelectionTarget option)
    (selected: SelectionTarget list) : float32[] =
    let entityMap =
        sketch.Entities
        |> List.map (fun e ->
            match e with
            | REPoint(id, _, _) -> id, e
            | RELine(id, _, _) -> id, e
            | RECircle(id, _, _) -> id, e
            | REArc(id, _, _, _) -> id, e)
        |> Map.ofList
    let points = resolvePointMap slotLookup paramValues sketchId sketch.Entities

    let out = ResizeArray<float32>()
    loops
    |> List.iter (fun loop ->
        match resolveLoopBoundary entityMap points slotLookup paramValues sketchId loop.EntityIds with
        | None -> ()
        | Some boundary ->
            // Drop the closing point so the polygon has unique vertices.
            let polygon =
                if boundary.Length >= 2 && near2 (List.head boundary) (List.last boundary)
                then boundary |> List.take (boundary.Length - 1)
                else boundary
            let colour =
                if isLoopActive sketchId loop.Id hovered selected then LOOP_HIGHLIGHT
                else LOOP_FILL
            for (a, b, c) in triangulatePolygon polygon do
                pushTriangleVertex out a colour
                pushTriangleVertex out b colour
                pushTriangleVertex out c colour)

    out.ToArray()

/// Reconstruct the anchor used for a dimensional constraint's label —
/// identical formula to LabelBuilder / pushDistanceLines.
let private dimensionAnchor
    (points: Map<string, float * float>)
    (c: SketchConstraint) : (float * float) option =
    let perpOffset (a: string) (b: string) : (float * float) option =
        match Map.tryFind a points, Map.tryFind b points with
        | Some pa, Some pb ->
            Some (distanceAnchorFallback pa pb)
        | _ -> None
    let resolve (lp: LabelPos option) (fallback: (float * float) option) =
        match lp with
        | Some p -> Some (p.X, p.Y)
        | None -> fallback
    match c with
    | Distance(a, b, _, lp) -> resolve lp (perpOffset a b)
    | FrameLineDistance(_, a, b, _, _, _, lp) -> resolve lp (perpOffset a b)
    | LineDistance(_, a, b, _, _, _, _, lp) -> resolve lp (perpOffset a b)
    | CircleCircleDistance(_, ca, _, cb, _, _, lp) -> resolve lp (perpOffset ca cb)
    | Angle(a, _, _, b, _, _, _, _, _, _, lp) -> resolve lp (perpOffset a b)
    | FrameDistance(p, _, _, _, lp) ->
        resolve lp (Map.tryFind p points)
    | PointLineDistance(p, _, _, _, _, lp) ->
        resolve lp (Map.tryFind p points)
    | PointCircleDistance(p, _, _, _, lp) ->
        resolve lp (Map.tryFind p points)
    | LineCircleDistance(_, a, b, _, _, _, lp) ->
        resolve lp (perpOffset a b)
    | CircleDiameter(_, c, _, lp) ->
        resolve lp (Map.tryFind c points)
    | _ -> None

/// Build a pick-point instance buffer for dimension labels (one fat
/// billboard per label at its anchor). Each instance = 4 floats:
/// (cx, cy, radiusPx, pickIdAsFloat). Uses the existing pointPick pipeline.
let buildSketchDimensionPickBuffer
    (sketchId: ActionId)
    (sketch: ActionSketch)
    (slotLookup: Map<SlotRef, Slot>)
    (paramValues: float[])
    (pickables: Pickable list) : float32[] =
    let points = resolvePointMap slotLookup paramValues sketchId sketch.Entities
    // Map constraintIndex → PickId (for this sketch's PickDimensions only).
    let pickByIndex =
        pickables
        |> List.choose (fun p ->
            match p with
            | PickDimension(pid, sid, idx, _) when sid = sketchId -> Some (idx, pid)
            | _ -> None)
        |> Map.ofList

    sketch.Constraints
    |> List.mapi (fun i c -> i, c)
    |> List.collect (fun (i, c) ->
        match Map.tryFind i pickByIndex, dimensionAnchor points c with
        | Some pid, Some (ax, ay) ->
            [ float32 ax; float32 ay; 20.0f; float32 pid ]
        | _ -> [])
    |> List.toArray

/// Build an instance buffer for picking sketch points. Each instance =
/// 4 floats: (cx, cy, radiusPx, pickIdAsFloat). Uses a fatter radius than
/// the visual pipeline to make points easier to hit.
let buildSketchPointPickBuffer
    (sketchId: ActionId)
    (entities: RenderEntity list)
    (slotLookup: Map<SlotRef, Slot>)
    (paramValues: float[])
    (pickables: Pickable list) : float32[] =
    // Map pointId -> PickId from PickPoint pickables in this sketch.
    let pickByPoint =
        pickables
        |> List.choose (fun p ->
            match p with
            | PickPoint(pid, sid, eid, _, _) when sid = sketchId -> Some (eid, pid)
            | _ -> None)
        |> Map.ofList

    entities
    |> List.collect (fun entity ->
        match entity with
        | REPoint(id, x, y) ->
            match Map.tryFind id pickByPoint with
            | None -> []
            | Some pickId ->
                let readSlot path fallback =
                    let ref = { ActionId = sketchId; Path = path }
                    match Map.tryFind ref slotLookup with
                    | Some s when s < paramValues.Length -> paramValues.[s]
                    | _ -> fallback
                let rx = readSlot (sprintf "sketch.entity.%s.x" id) x
                let ry = readSlot (sprintf "sketch.entity.%s.y" id) y
                // Pick radius in pixels — deliberately fat (visual
                // point is 5 px, pick disc is 28 px) so clicks near the
                // point still register.
                [ float32 rx; float32 ry; 28.0f; float32 pickId ]
        | _ -> [])
    |> List.toArray

/// Build an interleaved line-list vertex buffer for one sketch.
/// Each vertex = 6 floats: (x, y, r, g, b, a). Draw mode: line-list.
let buildSketchLineBuffer
    (sketchId: ActionId)
    (entities: RenderEntity list)
    (slotLookup: Map<SlotRef, Slot>)
    (paramValues: float[])
    (hovered: SelectionTarget option)
    (selected: SelectionTarget list) : float32[] =
    let out = ResizeArray<float32>()
    let points = resolvePointMap slotLookup paramValues sketchId entities

    let colourFor kind id =
        if isEntityActive sketchId kind id hovered selected then ACCENT else SKETCH_LINE

    entities
    |> List.iter (fun entity ->
        match entity with
        | REPoint _ -> ()  // handled separately by the point pipeline later
        | RELine(id, startId, endId) ->
            match Map.tryFind startId points, Map.tryFind endId points with
            | Some a, Some b -> pushSegment out a b (colourFor "line" id)
            | _ -> ()
        | RECircle(id, centerId, fallbackRadius) ->
            match Map.tryFind centerId points with
            | Some c ->
                let r = resolveScalar slotLookup paramValues sketchId
                                (sprintf "sketch.entity.%s.radius" id) fallbackRadius
                pushCircle out c r (colourFor "circle" id)
            | None -> ()
        | REArc(id, startId, endId, ArcCenter(centerId, cw)) ->
            match Map.tryFind startId points, Map.tryFind endId points, Map.tryFind centerId points with
            | Some s, Some e, Some c -> pushArc out s e c cw (colourFor "arc" id)
            | _ -> ()
        | REArc(_, _, _, ArcThreePoint _) ->
            // Authoring-only; skip.
            ())

    out.ToArray()

// ── Frame origin handles ────────────────────────────────────────────────

let private FRAME_ORIGIN_COLOUR : float32[] = [| 0.30f; 0.30f; 0.30f; 1.0f |]

/// Instance buffer for all frame origin handles, stored as world-space
/// positions. One draw covers every frame in the scene, so no per-frame
/// uniform writes are needed. Layout per instance: 8 floats —
/// (wx, wy, wz, radiusPx, r, g, b, a).
let buildFrameOriginsPointBuffer
    (frames: FrameView list)
    (hovered: SelectionTarget option)
    (selected: SelectionTarget list) : float32[] =
    let out = ResizeArray<float32>()
    for frame in frames do
        let matches t =
            match t with
            | TargetFrameOrigin id -> id = frame.Id
            | _ -> false
        let active =
            (match hovered with Some h -> matches h | None -> false)
            || List.exists matches selected
        let colour, radius =
            if active then ACCENT, POINT_RADIUS_PX * 1.5f
            else FRAME_ORIGIN_COLOUR, POINT_RADIUS_PX
        let pos = frame.Transform.Trans
        out.Add(float32 pos.X)
        out.Add(float32 pos.Y)
        out.Add(float32 pos.Z)
        out.Add radius
        out.Add colour.[0]
        out.Add colour.[1]
        out.Add colour.[2]
        out.Add colour.[3]
    out.ToArray()

/// Per-frame axis gizmo vertices. Topology = line-list, two vertices per
/// axis × three axes per frame = 6 vertices per frame. Each vertex has 12
/// floats: (origin.xyz, axis.xyz, axis_px, endpoint, color.rgba).
let buildFramesGizmoBuffer
    (frames: FrameView list)
    (hovered: SelectionTarget option)
    (selected: SelectionTarget list)
    (selectedActionId: string option) : float32[] =
    let accent = ACCENT
    let axisColourX : float32[] = [| 0.88f; 0.42f; 0.42f; 1.0f |]
    let axisColourY : float32[] = [| 0.48f; 0.78f; 0.54f; 1.0f |]
    let axisColourZ : float32[] = [| 0.45f; 0.56f; 0.92f; 1.0f |]
    let out = ResizeArray<float32>()
    for frame in frames do
        let active =
            let matches t =
                match t with
                | TargetFrameOrigin id -> id = frame.Id
                | _ -> false
            (match hovered with Some h -> matches h | None -> false)
            || List.exists matches selected
            || selectedActionId = Some frame.Id
        let colourFor (base_: float32[]) : float32[] =
            if active then [| accent.[0]; accent.[1]; accent.[2]; 1.0f |]
            else base_
        let axisPx = if frame.Id = "origin" then 64.0f else 52.0f
        let origin = frame.Transform.Trans
        let pushAxis (axis: Vec3) (colour: float32[]) =
            let emit endpoint =
                out.Add(float32 origin.X)
                out.Add(float32 origin.Y)
                out.Add(float32 origin.Z)
                out.Add(float32 axis.X)
                out.Add(float32 axis.Y)
                out.Add(float32 axis.Z)
                out.Add axisPx
                out.Add endpoint
                out.Add colour.[0]
                out.Add colour.[1]
                out.Add colour.[2]
                out.Add colour.[3]
            emit 0.0f
            emit 1.0f
        let rot = frame.Transform.Rot
        pushAxis (rot.Rotate({ X = 1.0; Y = 0.0; Z = 0.0 })) (colourFor axisColourX)
        pushAxis (rot.Rotate({ X = 0.0; Y = 1.0; Z = 0.0 })) (colourFor axisColourY)
        pushAxis (rot.Rotate({ X = 0.0; Y = 0.0; Z = 1.0 })) (colourFor axisColourZ)
    out.ToArray()

