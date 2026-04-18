namespace Server

// ---------------------------------------------------------------------------
// TileRecursion — octree subdivision driven by FieldInterval.eval.
//
// Classifies an IntervalBox using the interval-bound on the SDF:
//   Lo > 0  → Outside  (entire tile is exterior, surface does not cross)
//   Hi < 0  → Inside   (entire tile is interior)
//   else    → Ambiguous (surface may cross; subdivide or declare leaf)
//
// Tiles classified Outside/Inside are emitted as leaves immediately (pruned).
// Ambiguous tiles split 8-ways and recurse until maxDepth, then emit.
//
// No tape simplification yet — every eval walks the full FieldNode. The
// point of this slice is to measure whether interval pruning alone cuts
// tile counts on realistic scenes.
// ---------------------------------------------------------------------------

type TileClass =
    | Outside
    | Inside
    | Ambiguous

type LeafTile =
    { Box: IntervalBox
      Class: TileClass
      Depth: int
      Bound: Interval
      /// The simplified FieldNode for this tile — the pruned tape valid
      /// over this tile's box. Callers doing per-leaf work (surface-point
      /// extraction, meshing) should evaluate against this, not the
      /// original tree.
      Node: FieldNode }

type TileStats =
    { LeafTiles: LeafTile list
      EvalCount: int
      MaxDepthReached: int }

module TileRecursion =

    /// Split an IntervalBox into 8 equal children (octree).
    let split (b: IntervalBox) : IntervalBox array =
        let halves (i: Interval) =
            let m = (i.Lo + i.Hi) * 0.5
            [ Interval.make i.Lo m; Interval.make m i.Hi ]
        halves b.XI
        |> List.collect (fun x ->
            halves b.YI
            |> List.collect (fun y ->
                halves b.ZI
                |> List.map (fun z -> { XI = x; YI = y; ZI = z })))
        |> List.toArray

    let classify (i: Interval) : TileClass =
        match i with
        | _ when i.Lo > 0.0 -> Outside
        | _ when i.Hi < 0.0 -> Inside
        | _ -> Ambiguous

    let private merge (a: TileStats) (b: TileStats) : TileStats =
        { LeafTiles = b.LeafTiles @ a.LeafTiles
          EvalCount = a.EvalCount + b.EvalCount
          MaxDepthReached = max a.MaxDepthReached b.MaxDepthReached }

    /// Recursively subdivide. At each tile we `simplify` the node (computing
    /// the interval bound AND a potentially smaller FieldNode with dominated
    /// boolean branches removed) and pass the simplified tree to the 8 child
    /// recursions. Pruned tiles (Outside/Inside) become leaves immediately;
    /// Ambiguous tiles subdivide until maxDepth is reached.
    let recurse (slots: SlotTable) (root: IntervalBox) (node: FieldNode) (maxDepth: int) : TileStats =
        let rec go (b: IntervalBox) (n: FieldNode) (depth: int) : TileStats =
            let (bound, simplified) = FieldInterval.simplify slots b n
            let cls = classify bound
            let self =
                { LeafTiles = []
                  EvalCount = 1
                  MaxDepthReached = depth }
            if cls <> Ambiguous || depth >= maxDepth then
                { self with LeafTiles = [ { Box = b; Class = cls; Depth = depth; Bound = bound; Node = simplified } ] }
            else
                split b
                |> Array.fold (fun acc child -> merge acc (go child simplified (depth + 1))) self
        go root node 0

    /// Convenience: count leaves by class as (outside, inside, ambiguous).
    let countByClass (stats: TileStats) : int * int * int =
        ((0, 0, 0), stats.LeafTiles)
        ||> List.fold (fun (o, i, a) t ->
            match t.Class with
            | Outside -> (o + 1, i, a)
            | Inside -> (o, i + 1, a)
            | Ambiguous -> (o, i, a + 1))
