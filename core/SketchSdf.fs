namespace Server

// ----------------------------------------------------------------------------
// SketchSdf — scalar 2D signed-distance function for Sketch2d.
//
// Mirrors the WGSL helpers in GpuSdf so the CPU mesher's surface matches
// the GPU raymarcher's exactly. Used by FieldInterval (for a cheap
// Lipschitz-1 bound) and FieldGrad (numerical differentiation of this
// scalar → gradient).
// ----------------------------------------------------------------------------

module SketchSdf =

    let private positiveAngleDelta (a: float) (b: float) : float =
        let tau = 2.0 * System.Math.PI
        let d = b - a
        d - tau * floor (d / tau)

    let private arcContains (startA: float) (endA: float) (query: float) (cw: bool) : bool =
        if cw then
            positiveAngleDelta endA query <= positiveAngleDelta endA startA
        else
            positiveAngleDelta startA query <= positiveAngleDelta startA endA

    let segDist (p: float * float) (a: float * float) (b: float * float) : float =
        let (px, py) = p
        let (ax, ay) = a
        let (bx, by) = b
        let ex = bx - ax
        let ey = by - ay
        let wx = px - ax
        let wy = py - ay
        let l = ex * ex + ey * ey + 1e-20
        let t = max 0.0 (min 1.0 ((wx * ex + wy * ey) / l))
        let dx = wx - ex * t
        let dy = wy - ey * t
        sqrt (dx * dx + dy * dy)

    let circleCurveDist (p: float * float) (center: float * float) (radius: float) : float =
        let (px, py) = p
        let (cx, cy) = center
        let dx = px - cx
        let dy = py - cy
        abs (sqrt (dx * dx + dy * dy) - radius)

    let arcCurveDist
        (p: float * float)
        (startP: float * float) (endP: float * float)
        (center: float * float) (cw: bool) : float =
        let (px, py) = p
        let (sx, sy) = startP
        let (ex, ey) = endP
        let (cx, cy) = center
        let radius = sqrt ((sx - cx) * (sx - cx) + (sy - cy) * (sy - cy))
        if radius < 1e-6 then segDist p startP endP
        else
            let qx = px - cx
            let qy = py - cy
            let startAngle = atan2 (sy - cy) (sx - cx)
            let endAngle = atan2 (ey - cy) (ex - cx)
            let queryAngle = atan2 qy qx
            if arcContains startAngle endAngle queryAngle cw then
                abs (sqrt (qx * qx + qy * qy) - radius)
            else
                let d1 = sqrt ((px - sx) * (px - sx) + (py - sy) * (py - sy))
                let d2 = sqrt ((px - ex) * (px - ex) + (py - ey) * (py - ey))
                min d1 d2

    let rayCrossLineSegment (p: float * float) (a: float * float) (b: float * float) : int =
        let (px, py) = p
        let (ax, ay) = a
        let (bx, by) = b
        let aAbove = ay > py
        let bAbove = by > py
        if aAbove = bAbove then 0
        else
            let t = (py - ay) / (by - ay)
            let x = ax + t * (bx - ax)
            if x > px then 1 else 0

    let rayCrossCircle (p: float * float) (center: float * float) (radius: float) : int =
        let (px, py) = p
        let (cx, cy) = center
        let dy = py - cy
        let disc = radius * radius - dy * dy
        if disc <= 1e-7 then 0
        else
            let h = sqrt disc
            let left = if cx - h > px then 1 else 0
            let right = if cx + h > px then 1 else 0
            left + right

    let rayCrossArc
        (p: float * float)
        (startP: float * float) (endP: float * float)
        (center: float * float) (cw: bool) : int =
        let (px, py) = p
        let (sx, sy) = startP
        let (ex, ey) = endP
        let (cx, cy) = center
        let radius = sqrt ((sx - cx) * (sx - cx) + (sy - cy) * (sy - cy))
        if radius < 1e-6 then 0
        else
            let dy = py - cy
            let disc = radius * radius - dy * dy
            if disc <= 1e-7 then 0
            else
                let h = sqrt disc
                let startAngle = atan2 (sy - cy) (sx - cx)
                let endAngle = atan2 (ey - cy) (ex - cx)
                let countAt (xx: float) =
                    if xx > px then
                        let angle = atan2 (py - cy) (xx - cx)
                        if arcContains startAngle endAngle angle cw
                           && (abs (xx - ex) > 1e-5 || abs (py - ey) > 1e-5)
                        then 1 else 0
                    else 0
                countAt (cx - h) + countAt (cx + h)

    /// Scalar 2D signed distance for a sketch: min distance to boundary,
    /// flipped sign via ray-crossings count if closed.
    let evalAt (slots: SlotTable) (sketch: Sketch2d) (p: float * float) : float =
        let slot s = slots.Values.[s]
        let pt (sp: SlotPt2) = (slot sp.XSlot, slot sp.YSlot)

        let minD =
            sketch.Primitives
            |> List.map (fun prim ->
                match prim with
                | SpLineSegment(a, b) -> segDist p (pt a) (pt b)
                | SpCircle(c, rSlot) -> circleCurveDist p (pt c) (slot rSlot)
                | SpArcCenter(a, b, c, cw) -> arcCurveDist p (pt a) (pt b) (pt c) cw)
            |> List.min

        if not sketch.Closed then minD
        else
            let crossings =
                sketch.Primitives
                |> List.sumBy (fun prim ->
                    match prim with
                    | SpLineSegment(a, b) -> rayCrossLineSegment p (pt a) (pt b)
                    | SpCircle(c, rSlot) -> rayCrossCircle p (pt c) (slot rSlot)
                    | SpArcCenter(a, b, c, cw) -> rayCrossArc p (pt a) (pt b) (pt c) cw)
            let inside = (crossings &&& 1) <> 0
            match sketch.Flip, inside with
            | false, true -> -minD
            | false, false -> minD
            | true, true -> minD
            | true, false -> -minD
