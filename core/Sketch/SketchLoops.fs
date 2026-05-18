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

    // Cluster tolerance. Kept tight so only points the solver has driven
    // to coincidence (via shared slots or coincidence constraints) merge
    // — authored points that just happen to sit close together stay
    // separate, so open polylines don't get read as closed loops.
    let private CLUSTER_TOL = 1e-9
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

    // Signed-area threshold below which a face is considered degenerate.
    // Open polylines trace their boundary twice (forward along each edge,
    // then backward), so exact shoelace = 0 — but floating-point noise
    // puts the result around 1e-15 for modestly-sized coords, which used
    // to cross the old `> 0.0` check and emit phantom loops. 1e-9 is
    // comfortably above the noise floor and below any real loop a user
    // would draw in the sketch plane.
    let private MIN_LOOP_AREA = 1e-9

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
            // v1: splines do not participate in loop detection. A
            // "rounded rectangle" composed from lines + splines would
            // not become a closed loop; users can still expose splines
            // as top-level `Primitive`s on the sketch refinement and
            // feed them into `wing_loft`-style scripts.
            | REBezierCubic _ -> ()
            // Points / circles: handled separately (circles via
            // `circleLoops` above; points carry no edge contribution).
            | REPoint _ | RECircle _ -> ()

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
                        if signedArea > MIN_LOOP_AREA then
                            let entityIds =
                                [ for ei in faceEdges -> edges.[ei].EntityId ]
                            faceLoops.Add(
                                { Id = loopIdFromEntities entityIds
                                  EntityIds = entityIds
                                  SignedArea = signedArea })

            circleLoops @ (List.ofSeq faceLoops)

    // ── Reconciliation ─────────────────────────────────────────────────
    //
    // `detectLoops` is a pure function from the entity graph, but its
    // output IDs are content-derived (`loop:e0,e1,...`). Those change
    // whenever an entity id changes, so they aren't suitable for
    // DSL-facing references that need to survive sketch edits.
    //
    // `reconcile` projects a list of detected loops onto a persistent
    // registry of `LoopRecord`s with stable, user-facing IDs
    // (`loop_0`, `loop_1`, ...). The matching is by entity-id *set*
    // (order-insensitive — face-walking can flip traversal direction
    // without changing the loop). Records that no longer match anything
    // are dropped; freshly-detected loops with no match get the next
    // available auto ID.
    //
    // `UserNamed` is preserved when a record carries forward. The
    // reconciler doesn't promote auto-named records to user-named or
    // vice versa — that's the editor's job when the user renames.

    /// Parse a `loop_<N>` auto-id back to its integer index. Anything
    /// else (user-named, legacy `circle:…` / `loop:…` etc.) returns
    /// `None` and doesn't participate in next-id computation.
    let private tryParseAutoIndex (id: string) : int option =
        if id.StartsWith "loop_" then
            let rest = id.Substring 5
            match System.Int32.TryParse rest with
            | true, n when n >= 0 -> Some n
            | _ -> None
        else None

    /// Next unused `loop_N` index given a list of records that may or
    /// may not include any auto-named entries.
    let private nextAutoIndex (records: LoopRecord list) : int =
        records
        |> List.choose (fun r -> tryParseAutoIndex r.Id)
        |> function
           | [] -> 0
           | xs -> (List.max xs) + 1

    /// Order-insensitive entity-id-set equality.
    let private sameEntitySet (a: string list) (b: string list) : bool =
        Set.ofList a = Set.ofList b

    /// Match freshly-detected loops against the persisted registry and
    /// return the updated registry. Output order follows detection
    /// order so the UI's loop list is stable per-cycle.
    let reconcile
            (persisted: LoopRecord list)
            (detected: SketchLoop list) : LoopRecord list =
        // We allocate auto IDs by walking detected loops in order and
        // pulling from a "fresh number" counter. To avoid colliding
        // with persisted auto-named records that we're carrying
        // forward, seed the counter past the existing max.
        let mutable nextN = nextAutoIndex persisted
        let used = System.Collections.Generic.HashSet<int>()

        // Pre-mark all persisted auto-numbered IDs as used so reused
        // entries don't collide with freshly-allocated ones.
        for r in persisted do
            match tryParseAutoIndex r.Id with
            | Some n -> used.Add n |> ignore
            | None -> ()

        let allocFreshId () : string =
            while used.Contains nextN do nextN <- nextN + 1
            used.Add nextN |> ignore
            let id = sprintf "loop_%d" nextN
            nextN <- nextN + 1
            id

        detected
        |> List.map (fun d ->
            match persisted |> List.tryFind (fun r -> sameEntitySet r.EntityIds d.EntityIds) with
            | Some r ->
                // Carry the stable ID forward; update EntityIds in
                // case the traversal order changed.
                { r with EntityIds = d.EntityIds }
            | None ->
                { Id = allocFreshId ()
                  EntityIds = d.EntityIds
                  UserNamed = false
                  Primitives = [] })   // primitives populated by `normalize`

    // ── Primitive reconciliation ──────────────────────────────────────
    //
    // Each entity inside a loop (line/arc/circle) gets a stable
    // user-facing id (`line_0`, `arc_0`, `circle_0`) keyed by variant.
    // We scope the index space per (loop, variant) so a loop with three
    // lines and one arc gets `line_0`, `line_1`, `line_2`, `arc_0` — not
    // tangled with the next loop's ids.
    //
    // Reconciliation: match by `EntityId`. Carried-forward records keep
    // their id; new entities get the next free per-variant index.

    /// Variant prefix for the entity kind, or `None` if the entity has
    /// no role inside a loop (points, etc.).
    let private primitivePrefix (e: RenderEntity) : string option =
        match e with
        | RELine _   -> Some "line"
        | REArc _    -> Some "arc"
        | RECircle _ -> Some "circle"
        | REBezierCubic _ -> Some "spline"
        | REPoint _  -> None

    /// Parse `<prefix>_<n>` back to its integer index; returns `None`
    /// for user-named records so the next-id computation skips them.
    let private tryParsePrimitiveIndex (prefix: string) (id: string) : int option =
        let lead = prefix + "_"
        if id.StartsWith lead then
            let rest = id.Substring lead.Length
            match System.Int32.TryParse rest with
            | true, n when n >= 0 -> Some n
            | _ -> None
        else None

    /// Per-loop primitive reconciliation. Carries forward records whose
    /// `EntityId` is still present; drops vanished ones; assigns fresh
    /// per-variant ids to new entities. Skips entities the loop refers
    /// to that aren't a recognized primitive kind (defensive — loops
    /// shouldn't reference non-curve entities).
    let reconcilePrimitives
            (persisted: PrimitiveRecord list)
            (entitiesById: Map<string, RenderEntity>)
            (entityIds: string list) : PrimitiveRecord list =
        let persistedByEntity =
            persisted |> List.map (fun r -> r.EntityId, r) |> Map.ofList
        let used = System.Collections.Generic.HashSet<string>()
        for r in persisted do used.Add r.Id |> ignore

        // Per-prefix counter that respects already-used ids when
        // allocating fresh names.
        let counters = System.Collections.Generic.Dictionary<string, int>()
        let nextIndexFor (prefix: string) : int =
            if counters.ContainsKey prefix then counters.[prefix]
            else
                // Seed past the highest existing index of this prefix.
                let seed =
                    persisted
                    |> List.choose (fun r -> tryParsePrimitiveIndex prefix r.Id)
                    |> function [] -> 0 | xs -> (List.max xs) + 1
                counters.[prefix] <- seed
                seed
        let allocId (prefix: string) : string =
            let mutable n = nextIndexFor prefix
            while used.Contains (sprintf "%s_%d" prefix n) do n <- n + 1
            let id = sprintf "%s_%d" prefix n
            used.Add id |> ignore
            counters.[prefix] <- n + 1
            id

        entityIds
        |> List.choose (fun eid ->
            match Map.tryFind eid entitiesById with
            | None -> None
            | Some ent ->
                match primitivePrefix ent with
                | None -> None
                | Some prefix ->
                    match Map.tryFind eid persistedByEntity with
                    | Some r -> Some r
                    | None ->
                        Some { Id = allocId prefix; EntityId = eid; UserNamed = false })

    /// Re-detect loops in `sketch.Entities`, reconcile against
    /// `sketch.Loops`, and bring each loop's `Primitives` registry up
    /// to date too. Single chokepoint every reducer that mutates
    /// `sketch.Entities` should route through. Pure.
    let normalize (sketch: ActionSketch) : ActionSketch =
        let detected = detectLoops sketch.Entities
        let reconciled = reconcile sketch.Loops detected
        let entitiesById =
            sketch.Entities
            |> List.map (fun e ->
                let id =
                    match e with
                    | REPoint(id, _, _) -> id
                    | RELine(id, _, _) -> id
                    | RECircle(id, _, _) -> id
                    | REArc(id, _, _, _) -> id
                    | REBezierCubic(id, _, _, _, _) -> id
                id, e)
            |> Map.ofList
        let withPrimitives =
            reconciled
            |> List.map (fun r ->
                let prevPrimitives =
                    sketch.Loops
                    |> List.tryFind (fun pr -> pr.Id = r.Id)
                    |> Option.map (fun p -> p.Primitives)
                    |> Option.defaultValue []
                { r with Primitives = reconcilePrimitives prevPrimitives entitiesById r.EntityIds })
        { sketch with Loops = withPrimitives }
