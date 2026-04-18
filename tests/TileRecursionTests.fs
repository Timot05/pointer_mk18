module TileRecursionTests

open Xunit
open Server

// ── Helpers ──────────────────────────────────────────────────────────────

let slots (vals: float list) : SlotTable =
    { Values = Array.ofList vals; Index = Map.empty }

let box3 (xLo, xHi) (yLo, yHi) (zLo, zHi) : IntervalBox =
    { XI = Interval.make xLo xHi
      YI = Interval.make yLo yHi
      ZI = Interval.make zLo zHi }

/// Reference point SDF (subset of FieldNodes covered by these tests).
let rec refSdf (st: SlotTable) (p: float * float * float) (node: FieldNode) : float =
    let (x, y, z) = p
    match node with
    | FPrimitive(PrimSphere rSlot) ->
        let r = st.Values.[rSlot]
        sqrt (x * x + y * y + z * z) - r
    | FTranslate(sx, sy, sz, child) ->
        refSdf st (x - st.Values.[sx], y - st.Values.[sy], z - st.Values.[sz]) child
    | FBoolean(BoolUnion, rSlot, a, b) ->
        let ra = refSdf st p a
        let rb = refSdf st p b
        let k = st.Values.[rSlot]
        if k <= 1e-6 then min ra rb
        else
            let h = max (k - abs (ra - rb)) 0.0 / k
            (min ra rb) - h * h * h * k / 6.0
    | _ -> failwith "refSdf: unsupported node in test"

let private sampleInBox (rng: System.Random) (b: IntervalBox) =
    let r (i: Interval) = i.Lo + rng.NextDouble() * (i.Hi - i.Lo)
    (r b.XI, r b.YI, r b.ZI)

// ── Octree split covers parent exactly ──────────────────────────────────

[<Fact>]
let ``split produces exactly 8 children`` () =
    let b = box3 (0.0, 1.0) (0.0, 1.0) (0.0, 1.0)
    let cs = TileRecursion.split b
    Assert.Equal(8, cs.Length)

[<Fact>]
let ``split children tile the parent without gaps or overlap beyond edges`` () =
    let b = box3 (-1.0, 1.0) (-2.0, 2.0) (0.0, 4.0)
    let cs = TileRecursion.split b
    // Sum of child volumes equals parent volume.
    let volume (ib: IntervalBox) =
        (ib.XI.Hi - ib.XI.Lo) * (ib.YI.Hi - ib.YI.Lo) * (ib.ZI.Hi - ib.ZI.Lo)
    let childTotal = cs |> Array.sumBy volume
    Assert.Equal(volume b, childTotal, 9)
    // Every child's box sits inside the parent.
    cs
    |> Array.iter (fun c ->
        Assert.True(c.XI.Lo >= b.XI.Lo && c.XI.Hi <= b.XI.Hi)
        Assert.True(c.YI.Lo >= b.YI.Lo && c.YI.Hi <= b.YI.Hi)
        Assert.True(c.ZI.Lo >= b.ZI.Lo && c.ZI.Hi <= b.ZI.Hi))

// ── Degenerate cases: root is fully classified ───────────────────────────

[<Fact>]
let ``root entirely outside emits 1 Outside leaf with 1 eval`` () =
    let st = slots [ 0.1 ]  // tiny sphere at origin
    let node = FPrimitive(PrimSphere 0)
    let b = box3 (5.0, 6.0) (5.0, 6.0) (5.0, 6.0)
    let stats = TileRecursion.recurse st b node 4
    Assert.Equal(1, stats.EvalCount)
    Assert.Equal(1, stats.LeafTiles.Length)
    Assert.Equal(Outside, stats.LeafTiles.[0].Class)
    Assert.Equal(0, stats.MaxDepthReached)

[<Fact>]
let ``root entirely inside emits 1 Inside leaf with 1 eval`` () =
    let st = slots [ 100.0 ]  // giant sphere
    let node = FPrimitive(PrimSphere 0)
    let b = box3 (-1.0, 1.0) (-1.0, 1.0) (-1.0, 1.0)
    let stats = TileRecursion.recurse st b node 4
    Assert.Equal(1, stats.EvalCount)
    Assert.Equal(1, stats.LeafTiles.Length)
    Assert.Equal(Inside, stats.LeafTiles.[0].Class)

[<Fact>]
let ``maxDepth 0 emits root as ambiguous leaf without subdividing`` () =
    let st = slots [ 1.0 ]
    let node = FPrimitive(PrimSphere 0)
    let b = box3 (-2.0, 2.0) (-2.0, 2.0) (-2.0, 2.0)
    let stats = TileRecursion.recurse st b node 0
    Assert.Equal(1, stats.EvalCount)
    Assert.Equal(1, stats.LeafTiles.Length)
    Assert.Equal(Ambiguous, stats.LeafTiles.[0].Class)

// ── Soundness: classified tiles really are that class ───────────────────

let private assertSoundLeaves (st: SlotTable) (node: FieldNode) (stats: TileStats) (seed: int) (samplesPerTile: int) =
    let rng = System.Random(seed)
    let checkSample (tile: LeafTile) =
        let p = sampleInBox rng tile.Box
        let v = refSdf st p node
        match tile.Class with
        | Outside ->
            Assert.True(v > -1e-9,
                sprintf "Outside tile %A but point %A has SDF %g" tile.Box p v)
        | Inside ->
            Assert.True(v < 1e-9,
                sprintf "Inside tile %A but point %A has SDF %g" tile.Box p v)
        | Ambiguous -> ()
    stats.LeafTiles
    |> List.iter (fun tile ->
        [ 1 .. samplesPerTile ] |> List.iter (fun _ -> checkSample tile))

[<Fact>]
let ``Sphere: classified tiles are sound under random sampling`` () =
    let st = slots [ 1.0 ]
    let node = FPrimitive(PrimSphere 0)
    let b = box3 (-2.0, 2.0) (-2.0, 2.0) (-2.0, 2.0)
    let stats = TileRecursion.recurse st b node 3
    assertSoundLeaves st node stats 42 20

[<Fact>]
let ``Union of two spheres: classified tiles are sound`` () =
    // Sphere A at origin r=1, Sphere B at (3,0,0) r=1, sharp union.
    let st = slots [ 1.0; 3.0; 0.0; 0.0; 1.0; 0.0 ]
    let a = FPrimitive(PrimSphere 0)
    let b = FTranslate(1, 2, 3, FPrimitive(PrimSphere 4))
    let node = FBoolean(BoolUnion, 5, a, b)
    let root = box3 (-2.0, 5.0) (-2.0, 2.0) (-2.0, 2.0)
    let stats = TileRecursion.recurse st root node 3
    assertSoundLeaves st node stats 99 20

// ── Pruning signal: the actual reason we're doing any of this ───────────

[<Fact>]
let ``Pruning sphere scene evaluates far fewer than 8^depth tiles`` () =
    // Root spans [-2, 2]^3, sphere r=1. At depth 4 the naive count is
    // 1 + 8 + 64 + 512 + 4096 + 32768 = 37449. Interval pruning should
    // cut this by a lot — only the shell around r=1 subdivides.
    let st = slots [ 1.0 ]
    let node = FPrimitive(PrimSphere 0)
    let b = box3 (-2.0, 2.0) (-2.0, 2.0) (-2.0, 2.0)
    let stats = TileRecursion.recurse st b node 4
    let naive = 1 + 8 + 64 + 512 + 4096 + 32768
    Assert.True(stats.EvalCount < naive / 4,
        sprintf "expected heavy pruning: %d evals vs %d naive" stats.EvalCount naive)
    // Should produce a healthy mix of all three classes.
    let out, inn, amb = TileRecursion.countByClass stats
    Assert.True(out > 0, "expected some Outside leaves")
    Assert.True(inn > 0, "expected some Inside leaves")
    Assert.True(amb > 0, "expected some Ambiguous leaves")

[<Fact>]
let ``Well-separated union prunes the empty space between spheres`` () =
    // Two spheres very far apart; mostly empty root box.
    let st = slots [ 1.0; 20.0; 0.0; 0.0; 1.0; 0.0 ]
    let a = FPrimitive(PrimSphere 0)
    let b = FTranslate(1, 2, 3, FPrimitive(PrimSphere 4))
    let node = FBoolean(BoolUnion, 5, a, b)
    let root = box3 (-3.0, 23.0) (-3.0, 3.0) (-3.0, 3.0)
    let stats = TileRecursion.recurse st root node 3
    // A naive walk to depth 3 is 1+8+64+512 = 585 evals. Here the
    // middle region between the two spheres is all Outside at depth 1,
    // so most of the tree is pruned.
    Assert.True(stats.EvalCount < 300,
        sprintf "expected well-separated pruning: %d evals" stats.EvalCount)
    let _, _, amb = TileRecursion.countByClass stats
    // Ambiguous tiles should cluster around the two surfaces only.
    stats.LeafTiles
    |> List.filter (fun t -> t.Class = Ambiguous)
    |> List.iter (fun tile ->
        let nearA = tile.Box.XI.Lo <= 2.0 && tile.Box.XI.Hi >= -2.0
        let nearB = tile.Box.XI.Lo <= 22.0 && tile.Box.XI.Hi >= 18.0
        Assert.True(nearA || nearB,
            sprintf "Ambiguous tile far from both surfaces: %A" tile.Box))
    Assert.True(amb > 0)

// ── Depth tracking ──────────────────────────────────────────────────────

[<Fact>]
let ``MaxDepthReached reflects actual deepest subdivision`` () =
    let st = slots [ 1.0 ]
    let node = FPrimitive(PrimSphere 0)
    let b = box3 (-2.0, 2.0) (-2.0, 2.0) (-2.0, 2.0)
    let stats = TileRecursion.recurse st b node 3
    Assert.Equal(3, stats.MaxDepthReached)
    // At maxDepth=3, ambiguous leaves must sit at depth 3.
    stats.LeafTiles
    |> List.filter (fun t -> t.Class = Ambiguous)
    |> List.iter (fun t -> Assert.Equal(3, t.Depth))
