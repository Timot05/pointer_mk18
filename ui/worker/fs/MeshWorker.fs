module MeshWorker

// ----------------------------------------------------------------------------
// Mesh-builder worker for the F# viewer.
//
// Two-message protocol:
//   { kind: "topology", surfaces: FieldSurface[] }
//       - Stored. Used on every subsequent rebuild. Sent when the FieldNode
//         topology changes (primitive added/removed, shape tree edited).
//
//   { kind: "rebuild", slotValues: Float32Array, halfExtent: number,
//                      maxDepth: int }
//       - Runs TileRecursion + MarchingCubes per stored surface, concatenates
//         per-surface vertex blocks with a pickId tag, posts back the
//         Float32Array (transferred, zero-copy).
// ----------------------------------------------------------------------------

open Fable.Core
open Fable.Core.JsInterop
open Server

[<Emit("performance.now()")>]
let private performanceNow () : float = jsNative

[<Emit("self.postMessage($0, [$1])")>]
let private postMessageTransfer (data: obj) (transfer: obj) : unit = jsNative

[<Emit("self.postMessage($0)")>]
let private postMessage (data: obj) : unit = jsNative

[<Emit("self.onmessage = $0")>]
let private setOnMessage (handler: obj -> unit) : unit = jsNative

[<Emit("new Float32Array($0)")>]
let private toFloat32Array (arr: float32[]) : obj = jsNative

[<Emit("$0.buffer")>]
let private getBuffer (typedArr: obj) : obj = jsNative

// ── State: stored between messages ──────────────────────────────────────

let mutable private currentSurfaces : FieldSurface list = []
// PickId per surface, sent by main thread alongside topology. Written as
// the `pickId` vertex attribute so the pick pass outputs real PickIds.
let mutable private currentPickIds : int[] = [||]

// ── Mesh build ──────────────────────────────────────────────────────────

/// One vertex = 7 floats: (px, py, pz, nx, ny, nz, pickId). Three
/// vertices per triangle.
let private VERTEX_STRIDE = 7

let private pushVertex
    (acc: ResizeArray<float32>)
    (p: float * float * float) (n: float * float * float)
    (pickId: int) =
    let (px, py, pz) = p
    let (nx, ny, nz) = n
    acc.Add(float32 px)
    acc.Add(float32 py)
    acc.Add(float32 pz)
    acc.Add(float32 nx)
    acc.Add(float32 ny)
    acc.Add(float32 nz)
    acc.Add(float32 pickId)

/// Build a flat vertex buffer for one surface at the given root box + depth.
/// `pickId` is written as the per-vertex pickId attribute so the pick
/// pass can emit real Pickable.PickIds.
let private surfaceMesh
    (slots: SlotTable) (root: IntervalBox) (maxDepth: int)
    (node: FieldNode) (pickId: int)
    (out: ResizeArray<float32>) : TileStats =
    let stats = TileRecursion.recurse slots root node maxDepth
    stats.LeafTiles
    |> List.filter (fun t -> t.Class = Ambiguous)
    |> List.iter (fun t ->
        MarchingCubes.triangulate slots t.Node t.Box
        |> List.iter (fun tri ->
            pushVertex out tri.V0 tri.N0 pickId
            pushVertex out tri.V1 tri.N1 pickId
            pushVertex out tri.V2 tri.N2 pickId))
    stats

// ── Message handler ─────────────────────────────────────────────────────

let private handleRebuild (slotValues: float[]) (halfExtent: float) (maxDepth: int) =
    let tStart = performanceNow()

    // Fresh SlotTable from incoming values. `Index` isn't needed for
    // downstream recursion (it reads Values.[slot] only), but the record
    // requires it — empty Map works.
    let slots : SlotTable =
        { Values = slotValues
          Index = Map.empty }

    let root =
        { XI = Interval.make -halfExtent halfExtent
          YI = Interval.make -halfExtent halfExtent
          ZI = Interval.make -halfExtent halfExtent }

    let out = ResizeArray<float32>()
    let mutable totalEvals = 0
    let mutable totalTiles = 0
    let mutable totalOut = 0
    let mutable totalIn = 0
    let mutable totalAmb = 0

    currentSurfaces
    |> List.iteri (fun i surface ->
        let pickId =
            if i < currentPickIds.Length then currentPickIds.[i] else i
        let stats = surfaceMesh slots root maxDepth surface.Field pickId out
        let o, ii, a = TileRecursion.countByClass stats
        totalEvals <- totalEvals + stats.EvalCount
        totalTiles <- totalTiles + (o + ii + a)
        totalOut <- totalOut + o
        totalIn <- totalIn + ii
        totalAmb <- totalAmb + a)

    let buildMs = performanceNow() - tStart
    let vertices = toFloat32Array (out.ToArray())

    let vertexCount = out.Count / VERTEX_STRIDE
    let triangleCount = vertexCount / 3

    // Debug: compute mesh bounding box in world coords.
    let mutable minX = System.Double.PositiveInfinity
    let mutable minY = System.Double.PositiveInfinity
    let mutable minZ = System.Double.PositiveInfinity
    let mutable maxX = System.Double.NegativeInfinity
    let mutable maxY = System.Double.NegativeInfinity
    let mutable maxZ = System.Double.NegativeInfinity
    for i in 0 .. vertexCount - 1 do
        let baseIdx = i * VERTEX_STRIDE
        let x = float out.[baseIdx]
        let y = float out.[baseIdx + 1]
        let z = float out.[baseIdx + 2]
        if x < minX then minX <- x
        if y < minY then minY <- y
        if z < minZ then minZ <- z
        if x > maxX then maxX <- x
        if y > maxY then maxY <- y
        if z > maxZ then maxZ <- z

    let response =
        {| kind = "mesh"
           vertices = vertices
           surfaceCount = currentSurfaces.Length
           halfExtent = halfExtent
           maxDepth = maxDepth
           vertexCount = vertexCount
           triangleCount = triangleCount
           evalCount = totalEvals
           totalTiles = totalTiles
           outCount = totalOut
           inCount = totalIn
           ambCount = totalAmb
           buildMs = buildMs
           bboxMinX = minX; bboxMinY = minY; bboxMinZ = minZ
           bboxMaxX = maxX; bboxMaxY = maxY; bboxMaxZ = maxZ |}

    postMessageTransfer response (getBuffer vertices)

let private handleMessage (e: obj) =
    let data = e?data
    let kind : string = data?kind
    match kind with
    | "topology" ->
        // Fable compiles F# lists to a linked-list structure that survives
        // structured clone. Pattern matching on the incoming surfaces works
        // because it uses the `.tag` field, which is a plain int.
        currentSurfaces <- unbox (data?surfaces)
        currentPickIds <- unbox (data?pickIds)
        postMessage {| kind = "topology-ack"; surfaceCount = currentSurfaces.Length |}
    | "rebuild" ->
        let slotValues : float[] = unbox (data?slotValues)
        let halfExtent : float = unbox (data?halfExtent)
        let maxDepth : int = unbox (data?maxDepth)
        handleRebuild slotValues halfExtent maxDepth
    | _ -> ()

setOnMessage handleMessage
postMessage {| kind = "ready" |}
