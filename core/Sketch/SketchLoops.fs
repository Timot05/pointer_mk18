namespace Server

// ---------------------------------------------------------------------------
// Planar loop / face detection for sketch entities.
//
// A "loop" is a closed region bounded by a cycle of lines and arcs in the
// sketch. Detection is purely topological: we build an undirected planar
// graph where each unique point (coincident points share a vertex) is a
// node and each line/arc is an edge, then walk the planar faces using the
// "next CCW half-edge" rule.
//
// Circles are not part of the graph — each circle is a trivial 1-edge
// closed loop, emitted directly.
//
// Ported from pointer_mk17/src/sketch_loops.rs. Key differences:
//   - No sampled boundary polyline — we keep only the ordered entity ids.
//     The slot-backed renderer will derive point positions at render time.
//   - Outgoing arc angles are computed analytically from the arc's center
//     and the endpoint (tangent = perpendicular to radius), instead of
//     sampling the arc and using the first interior sample.
//   - Only ArcCenter mode is supported. ArcThreePoint is authoring-only
//     per user intent; such arcs are skipped at detection time.
// ---------------------------------------------------------------------------

open System.Collections.Generic

// Stable id + ordered entities + signed area (CCW = positive).
type SketchLoop =
    { Id: string
      EntityIds: string list
      SignedArea: float }

module SketchLoops =

    // Cluster tolerance — loose enough that solver noise and float jitter
    // don't prevent coincident-via-constraint points from merging.
    let private CLUSTER_TOL = 1e-3
    let private CLUSTER_TOL_SQ = CLUSTER_TOL * CLUSTER_TOL

    // ── Helpers ────────────────────────────────────────────────────────

    let private normalizeAngle (a: float) =
        let twoPi = System.Math.PI * 2.0
        ((a % twoPi) + twoPi) % twoPi

    /// Shoelace formula. Positive = CCW winding, negative = CW.
    let polygonSignedArea (points: (float * float) list) : float =
        match points with
        | _ when points.Length < 3 -> 0.0
        | _ ->
            let arr = List.toArray points
            let mutable sum = 0.0
            for i in 0 .. arr.Length - 2 do
                let (ax, ay) = arr.[i]
                let (bx, by) = arr.[i + 1]
                sum <- sum + ax * by - bx * ay
            sum * 0.5

    let private loopIdFromEntities (ids: string list) : string =
        let sorted = ids |> List.sort
        "loop:" + (String.concat "," sorted)

    // ── Point collection & clustering ──────────────────────────────────

    let private collectPoints (entities: RenderEntity list) : Map<string, FreePoint> =
        entities
        |> List.choose (function
            | REPoint(id, x, y) -> Some (id, ({ X = x; Y = y } : FreePoint))
            | _ -> None)
        |> Map.ofList

    /// Cluster points by spatial proximity. Iteration is deterministic
    /// (sorted by id) so assignments don't flicker across frames.
    /// Returns (pointId → clusterIndex, clusterPositions).
    let private clusterPoints (points: Map<string, FreePoint>) : Map<string, int> * FreePoint[] =
        let clusters = ResizeArray<FreePoint>()
        let pointToCluster = Dictionary<string, int>()

        let ordered =
            points
            |> Map.toList
            |> List.sortBy fst

        for (id, p) in ordered do
            let mutable found = -1
            let mutable i = 0
            while found < 0 && i < clusters.Count do
                let cp = clusters.[i]
                let dx = cp.X - p.X
                let dy = cp.Y - p.Y
                if dx * dx + dy * dy < CLUSTER_TOL_SQ then found <- i
                i <- i + 1
            let idx =
                if found >= 0 then found
                else
                    clusters.Add(p)
                    clusters.Count - 1
            pointToCluster.[id] <- idx

        let mapping =
            pointToCluster
            |> Seq.map (fun kv -> kv.Key, kv.Value)
            |> Map.ofSeq

        mapping, clusters.ToArray()

    // ── Half-edge representation ───────────────────────────────────────

    [<Struct>]
    type private HalfEdge =
        { From: int
          To: int
          EntityId: string
          OutAngle: float
          Twin: int
          /// True if this half-edge comes from an arc. The face walker uses
          /// this to decide whether a single-edge cycle is degenerate
          /// (straight edge) or legitimate (arc).
          IsArc: bool }

    let private sampleCircleSignedArea (c: FreePoint) (radius: float) (segments: int) : float =
        // Area of a regular CCW polygon inscribed in the circle. Used for the
        // trivial circle loop's signed_area without materializing the sampled
        // boundary.
        let pts =
            [ for i in 0 .. segments ->
                let angle = (float i / float segments) * System.Math.PI * 2.0
                (c.X + cos angle * radius, c.Y + sin angle * radius) ]
        polygonSignedArea pts

    // ── Main detection ─────────────────────────────────────────────────

    let detectLoops (entities: RenderEntity list) : SketchLoop list =
        let pointsById = collectPoints entities

        // Step 1: each circle is a trivial loop.
        let circleLoops =
            entities
            |> List.choose (function
                | RECircle(id, centerId, radius) ->
                    match Map.tryFind centerId pointsById with
                    | Some c ->
                        Some
                            { Id = sprintf "circle:%s" id
                              EntityIds = [ id ]
                              SignedArea = sampleCircleSignedArea c radius 24 }
                    | None -> None
                | _ -> None)

        // Step 2: planar graph + face walking.
        let pointCluster, clusterPositions = clusterPoints pointsById

        let edges = ResizeArray<HalfEdge>()
        let tryClusterIdx (id: string) = Map.tryFind id pointCluster

        for entity in entities do
            match entity with
            | RELine(id, startId, endId) ->
                match tryClusterIdx startId, tryClusterIdx endId with
                | Some si, Some ei when si <> ei ->
                    let sp = clusterPositions.[si]
                    let ep = clusterPositions.[ei]
                    let fwd = normalizeAngle (atan2 (ep.Y - sp.Y) (ep.X - sp.X))
                    let rev = normalizeAngle (fwd + System.Math.PI)
                    let idxA = edges.Count
                    let idxB = idxA + 1
                    edges.Add(
                        { From = si; To = ei; EntityId = id
                          OutAngle = fwd; Twin = idxB; IsArc = false })
                    edges.Add(
                        { From = ei; To = si; EntityId = id
                          OutAngle = rev; Twin = idxA; IsArc = false })
                | _ -> ()

            | REArc(id, startId, endId, ArcCenter(centerId, clockwise)) ->
                match tryClusterIdx startId, tryClusterIdx endId, Map.tryFind centerId pointsById with
                | Some si, Some ei, Some c when si <> ei ->
                    let sp = clusterPositions.[si]
                    let ep = clusterPositions.[ei]
                    // Tangent = perpendicular to radius (center → endpoint).
                    // CCW (clockwise = false): rotate radius 90° CCW.
                    // CW  (clockwise = true):  rotate radius 90° CW.
                    let tangent (p: FreePoint) =
                        let dx = p.X - c.X
                        let dy = p.Y - c.Y
                        if clockwise then (dy, -dx) else (-dy, dx)
                    // Outgoing angle at start = direction of motion at start.
                    let (stx, sty) = tangent sp
                    let fwd = normalizeAngle (atan2 sty stx)
                    // Reverse half-edge leaves `end` back along the arc ⇒
                    // opposite of tangent direction at end.
                    let (etx, ety) = tangent ep
                    let rev = normalizeAngle (atan2 -ety -etx)
                    let idxA = edges.Count
                    let idxB = idxA + 1
                    edges.Add(
                        { From = si; To = ei; EntityId = id
                          OutAngle = fwd; Twin = idxB; IsArc = true })
                    edges.Add(
                        { From = ei; To = si; EntityId = id
                          OutAngle = rev; Twin = idxA; IsArc = true })
                | _ -> ()

            | REArc(_, _, _, ArcThreePoint _) -> ()
            | _ -> ()

        if edges.Count = 0 then
            circleLoops
        else
            // Outgoing half-edges per vertex, sorted CCW by angle.
            let vertexOut = Dictionary<int, ResizeArray<int>>()
            for i in 0 .. edges.Count - 1 do
                let fromV = edges.[i].From
                if not (vertexOut.ContainsKey(fromV)) then
                    vertexOut.[fromV] <- ResizeArray()
                vertexOut.[fromV].Add(i)
            for kv in vertexOut do
                kv.Value.Sort(fun a b -> compare edges.[a].OutAngle edges.[b].OutAngle)

            // next(edge) = at destination vertex, find twin in sorted outgoing
            // list, take PREVIOUS (wrapping) → CCW face walk.
            let nextEdge = Array.zeroCreate<int> edges.Count
            for i in 0 .. edges.Count - 1 do
                let e = edges.[i]
                let outList = vertexOut.[e.To]
                let twinPos = outList.IndexOf(e.Twin)
                let prevPos = if twinPos = 0 then outList.Count - 1 else twinPos - 1
                nextEdge.[i] <- outList.[prevPos]

            // Walk faces.
            let visited = Array.zeroCreate<bool> edges.Count
            let faceLoops = ResizeArray<SketchLoop>()

            for startEdge in 0 .. edges.Count - 1 do
                if not visited.[startEdge] then
                    let faceEdges = ResizeArray<int>()
                    let mutable cur = startEdge
                    let mutable running = true
                    while running do
                        if visited.[cur] then running <- false
                        else
                            visited.[cur] <- true
                            faceEdges.Add(cur)
                            cur <- nextEdge.[cur]
                            if cur = startEdge then running <- false

                    // A single straight edge with no arc curvature can't bound
                    // an area. Discard unless it's an arc (arcs can form
                    // single-edge-with-interior loops that aren't handled here
                    // — but this is the safety net).
                    let skip =
                        faceEdges.Count < 2
                        && not (edges.[faceEdges.[0]].IsArc)

                    if not skip then
                        // Materialize a boundary polyline for signed-area
                        // computation. Only used here to reject the outer
                        // face — we don't keep it around.
                        let boundary =
                            [ for ei in faceEdges ->
                                let e = edges.[ei]
                                let fromV = clusterPositions.[e.From]
                                (fromV.X, fromV.Y) ]
                        let closed =
                            match boundary with
                            | head :: _ -> boundary @ [ head ]
                            | [] -> []
                        let signedArea = polygonSignedArea closed
                        if signedArea > 0.0 then
                            let entityIds =
                                [ for ei in faceEdges -> edges.[ei].EntityId ]
                            faceLoops.Add(
                                { Id = loopIdFromEntities entityIds
                                  EntityIds = entityIds
                                  SignedArea = signedArea })

            circleLoops @ (List.ofSeq faceLoops)
