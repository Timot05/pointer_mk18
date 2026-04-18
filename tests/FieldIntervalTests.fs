module FieldIntervalTests

open Xunit
open Server

// ── Helpers ──────────────────────────────────────────────────────────────

/// Build a SlotTable from raw float values. Slot indices are positional
/// (0..n-1). Index map is empty because eval only reads Values.[slot].
let slots (vals: float list) : SlotTable =
    { Values = Array.ofList vals; Index = Map.empty }

/// Construct a box of intervals directly.
let box3 (xLo, xHi) (yLo, yHi) (zLo, zHi) : IntervalBox =
    { XI = Interval.make xLo xHi
      YI = Interval.make yLo yHi
      ZI = Interval.make zLo zHi }

/// Reference point evaluator that mirrors GpuSdf semantics exactly, for
/// the containment-property tests. Only covers the subset FieldInterval
/// actually bounds tightly (sphere, halfplane, translate, boolean sharp).
let rec refSdf (st: SlotTable) (p: float * float * float) (node: FieldNode) : float =
    let (x, y, z) = p
    match node with
    | FPrimitive(PrimSphere rSlot) ->
        let r = st.Values.[rSlot]
        sqrt (x * x + y * y + z * z) - r
    | FPrimitive(PrimHalfPlane(axis, offSlot, flip)) ->
        let off = st.Values.[offSlot]
        let v =
            match axis with
            | "X" -> x - off
            | "Y" -> y - off
            | _ -> z - off
        if flip then -v else v
    | FPrimitive(PrimBox(wSlot, hSlot, dSlot)) ->
        let hx = st.Values.[wSlot] * 0.5
        let hy = st.Values.[hSlot] * 0.5
        let hz = st.Values.[dSlot] * 0.5
        let qx = abs x - hx
        let qy = abs y - hy
        let qz = abs z - hz
        let outside =
            sqrt ((max qx 0.0) ** 2.0 + (max qy 0.0) ** 2.0 + (max qz 0.0) ** 2.0)
        let inside = min (max qx (max qy qz)) 0.0
        outside + inside
    | FPrimitive(PrimCylinder(rSlot, hSlot)) ->
        let r = st.Values.[rSlot]
        let halfH = st.Values.[hSlot] * 0.5
        let dRadial = sqrt (x * x + y * y) - r
        let dAxial = abs z - halfH
        if dRadial > 0.0 && dAxial > 0.0 then
            sqrt (dRadial * dRadial + dAxial * dAxial)
        else
            max dRadial dAxial
    | FTranslate(sx, sy, sz, child) ->
        let dx = st.Values.[sx]
        let dy = st.Values.[sy]
        let dz = st.Values.[sz]
        refSdf st (x - dx, y - dy, z - dz) child
    | FBoolean(op, rSlot, a, b) ->
        let ra = refSdf st p a
        let rb = refSdf st p b
        let k = st.Values.[rSlot]
        let smin u v =
            if k <= 1e-6 then min u v
            else
                let h = max (k - abs (u - v)) 0.0 / k
                (min u v) - h * h * h * k / 6.0
        match op with
        | BoolUnion -> smin ra rb
        | BoolIntersect -> -(smin -ra -rb)
        | BoolSubtract -> -(smin -ra rb)
    | FFieldOp(OpThicken, vSlot, child) ->
        (refSdf st p child) - st.Values.[vSlot]
    | FFieldOp(OpShell, vSlot, child) ->
        let c = refSdf st p child
        max c (-(c + st.Values.[vSlot]))
    | _ -> failwith "refSdf: unsupported node in test"

let inline approxEq (expected: float) (actual: float) (eps: float) =
    Assert.True(abs (expected - actual) < eps, sprintf "expected %g, got %g (eps=%g)" expected actual eps)

// ── Interval arithmetic sanity ───────────────────────────────────────────

[<Fact>]
let ``Interval.add sums endpoints`` () =
    let r = Interval.add (Interval.make 1.0 2.0) (Interval.make 3.0 4.0)
    Assert.Equal(4.0, r.Lo)
    Assert.Equal(6.0, r.Hi)

[<Fact>]
let ``Interval.sub flips the subtrahend endpoints`` () =
    let r = Interval.sub (Interval.make 1.0 2.0) (Interval.make 3.0 4.0)
    Assert.Equal(-3.0, r.Lo)
    Assert.Equal(-1.0, r.Hi)

[<Fact>]
let ``Interval.mul covers all four corner products`` () =
    let r = Interval.mul (Interval.make -1.0 2.0) (Interval.make -3.0 4.0)
    Assert.Equal(-6.0, r.Lo)
    Assert.Equal(8.0, r.Hi)

[<Fact>]
let ``Interval.square all-positive stays monotone`` () =
    let r = Interval.square (Interval.make 2.0 3.0)
    Assert.Equal(4.0, r.Lo)
    Assert.Equal(9.0, r.Hi)

[<Fact>]
let ``Interval.square all-negative flips the ordering`` () =
    let r = Interval.square (Interval.make -3.0 -2.0)
    Assert.Equal(4.0, r.Lo)
    Assert.Equal(9.0, r.Hi)

[<Fact>]
let ``Interval.square straddling zero has Lo = 0`` () =
    let r = Interval.square (Interval.make -1.0 2.0)
    Assert.Equal(0.0, r.Lo)
    Assert.Equal(4.0, r.Hi)

[<Fact>]
let ``Interval.sqrt clamps negative Lo to 0`` () =
    let r = Interval.sqrt (Interval.make -1.0 4.0)
    Assert.Equal(0.0, r.Lo)
    Assert.Equal(2.0, r.Hi)

[<Fact>]
let ``Interval.abs straddling zero has Lo = 0`` () =
    let r = Interval.abs (Interval.make -2.0 3.0)
    Assert.Equal(0.0, r.Lo)
    Assert.Equal(3.0, r.Hi)

[<Fact>]
let ``Interval.neg flips endpoints`` () =
    let r = Interval.neg (Interval.make -2.0 3.0)
    Assert.Equal(-3.0, r.Lo)
    Assert.Equal(2.0, r.Hi)

// ── Sphere primitive ─────────────────────────────────────────────────────

[<Fact>]
let ``Sphere eval: box fully outside returns strictly positive interval`` () =
    let st = slots [ 1.0 ]  // radius = 1
    let node = FPrimitive(PrimSphere 0)
    let b = box3 (2.0, 3.0) (2.0, 3.0) (2.0, 3.0)
    let r = FieldInterval.eval st b node
    Assert.True(r.Lo > 0.0, sprintf "expected Lo > 0, got %g" r.Lo)

[<Fact>]
let ``Sphere eval: box fully inside returns strictly negative interval`` () =
    let st = slots [ 1.0 ]
    let node = FPrimitive(PrimSphere 0)
    let b = box3 (-0.1, 0.1) (-0.1, 0.1) (-0.1, 0.1)
    let r = FieldInterval.eval st b node
    Assert.True(r.Hi < 0.0, sprintf "expected Hi < 0, got %g" r.Hi)

[<Fact>]
let ``Sphere eval: box straddling contains 0`` () =
    let st = slots [ 1.0 ]
    let node = FPrimitive(PrimSphere 0)
    let b = box3 (-2.0, 2.0) (-2.0, 2.0) (-2.0, 2.0)
    let r = FieldInterval.eval st b node
    Assert.True(Interval.contains r 0.0, sprintf "expected 0 in [%g, %g]" r.Lo r.Hi)

// ── Box primitive ────────────────────────────────────────────────────────

[<Fact>]
let ``Box eval: tile fully outside is positive`` () =
    let st = slots [ 2.0; 2.0; 2.0 ]  // 2×2×2 box at origin
    let node = FPrimitive(PrimBox(0, 1, 2))
    let b = box3 (3.0, 4.0) (3.0, 4.0) (3.0, 4.0)
    let r = FieldInterval.eval st b node
    Assert.True(r.Lo > 0.0, sprintf "expected Lo > 0, got %g" r.Lo)

[<Fact>]
let ``Box eval: tile fully inside is negative`` () =
    let st = slots [ 2.0; 2.0; 2.0 ]
    let node = FPrimitive(PrimBox(0, 1, 2))
    let b = box3 (-0.3, 0.3) (-0.3, 0.3) (-0.3, 0.3)
    let r = FieldInterval.eval st b node
    Assert.True(r.Hi < 0.0, sprintf "expected Hi < 0, got %g" r.Hi)

[<Fact>]
let ``Box eval: tile straddling contains 0`` () =
    let st = slots [ 2.0; 2.0; 2.0 ]
    let node = FPrimitive(PrimBox(0, 1, 2))
    let b = box3 (0.5, 1.5) (-0.3, 0.3) (-0.3, 0.3)
    let r = FieldInterval.eval st b node
    Assert.True(Interval.contains r 0.0)

// ── Cylinder primitive ───────────────────────────────────────────────────

[<Fact>]
let ``Cylinder eval: tile fully outside is positive`` () =
    let st = slots [ 0.5; 1.0 ]  // r=0.5, h=1 along Z
    let node = FPrimitive(PrimCylinder(0, 1))
    let b = box3 (2.0, 3.0) (2.0, 3.0) (0.0, 0.2) // far away laterally
    let r = FieldInterval.eval st b node
    Assert.True(r.Lo > 0.0, sprintf "expected Lo > 0, got %g" r.Lo)

[<Fact>]
let ``Cylinder eval: tile fully inside is negative`` () =
    let st = slots [ 0.5; 1.0 ]
    let node = FPrimitive(PrimCylinder(0, 1))
    let b = box3 (-0.1, 0.1) (-0.1, 0.1) (-0.2, 0.2)
    let r = FieldInterval.eval st b node
    Assert.True(r.Hi < 0.0, sprintf "expected Hi < 0, got %g" r.Hi)

[<Fact>]
let ``Cylinder eval: tile straddling contains 0`` () =
    let st = slots [ 0.5; 1.0 ]
    let node = FPrimitive(PrimCylinder(0, 1))
    let b = box3 (0.3, 0.8) (-0.3, 0.3) (-0.2, 0.2)
    let r = FieldInterval.eval st b node
    Assert.True(Interval.contains r 0.0)

// ── HalfPlane primitive ──────────────────────────────────────────────────

[<Fact>]
let ``HalfPlane X at offset 0: box on positive side is positive`` () =
    let st = slots [ 0.0 ]  // offset = 0
    let node = FPrimitive(PrimHalfPlane("X", 0, false))
    let b = box3 (1.0, 2.0) (-1.0, 1.0) (-1.0, 1.0)
    let r = FieldInterval.eval st b node
    Assert.Equal(1.0, r.Lo)
    Assert.Equal(2.0, r.Hi)

[<Fact>]
let ``HalfPlane X flipped: positive-x box reads negative`` () =
    let st = slots [ 0.0 ]
    let node = FPrimitive(PrimHalfPlane("X", 0, true))
    let b = box3 (1.0, 2.0) (-1.0, 1.0) (-1.0, 1.0)
    let r = FieldInterval.eval st b node
    Assert.Equal(-2.0, r.Lo)
    Assert.Equal(-1.0, r.Hi)

[<Fact>]
let ``HalfPlane Z at offset 5: box at z=[4,6] straddles`` () =
    let st = slots [ 5.0 ]
    let node = FPrimitive(PrimHalfPlane("Z", 0, false))
    let b = box3 (-1.0, 1.0) (-1.0, 1.0) (4.0, 6.0)
    let r = FieldInterval.eval st b node
    Assert.Equal(-1.0, r.Lo)
    Assert.Equal(1.0, r.Hi)

// ── Translate composition ───────────────────────────────────────────────

[<Fact>]
let ``Translate shifts sphere bounds equivalently`` () =
    // Sphere r=1 translated by (5,0,0), evaluated over box [4,6]×[-1,1]×[-1,1].
    // Should match untranslated sphere over [-1,1]³.
    let st = slots [ 1.0; 5.0; 0.0; 0.0 ]
    //                r    tx   ty   tz
    let child = FPrimitive(PrimSphere 0)
    let node = FTranslate(1, 2, 3, child)
    let bTranslated = box3 (4.0, 6.0) (-1.0, 1.0) (-1.0, 1.0)
    let rT = FieldInterval.eval st bTranslated node
    let rPlain = FieldInterval.eval st (box3 (-1.0, 1.0) (-1.0, 1.0) (-1.0, 1.0)) child
    Assert.Equal(rPlain.Lo, rT.Lo)
    Assert.Equal(rPlain.Hi, rT.Hi)

// ── Booleans (sharp, radius = 0) ─────────────────────────────────────────

[<Fact>]
let ``Sharp Union: query box near one sphere picks imin of the two`` () =
    // Sphere A at origin r=1, Sphere B at (10,0,0) r=1.
    // Query box near A: [-0.5, 0.5]³. A's interval is negative, B's is ~[9,11] (large positive).
    // Union = imin -> dominated by A.
    let st = slots [ 1.0; 10.0; 0.0; 0.0; 1.0; 0.0 ]
    //                rA   tx    ty   tz   rB   kUnion
    let sphereA = FPrimitive(PrimSphere 0)
    let sphereB = FTranslate(1, 2, 3, FPrimitive(PrimSphere 4))
    let union = FBoolean(BoolUnion, 5, sphereA, sphereB)
    let b = box3 (-0.5, 0.5) (-0.5, 0.5) (-0.5, 0.5)
    let rA = FieldInterval.eval st b sphereA
    let rU = FieldInterval.eval st b union
    Assert.Equal(rA.Lo, rU.Lo)
    Assert.Equal(rA.Hi, rU.Hi)

[<Fact>]
let ``Sharp Subtract: inside big minus outside small still negative`` () =
    // Big sphere at origin r=5, small sphere at (10,0,0) r=1. Query box [-1,1]³.
    // Query box is fully inside big sphere, fully outside small sphere.
    // Subtract = A - B = max(A, -B). A is negative (~[-5,-4]), -B is negative
    // (~[-11,-9]). max picks A's interval.
    let st = slots [ 5.0; 10.0; 0.0; 0.0; 1.0; 0.0 ]
    //                rA   tx    ty   tz   rB   kSub
    let big = FPrimitive(PrimSphere 0)
    let small = FTranslate(1, 2, 3, FPrimitive(PrimSphere 4))
    let sub = FBoolean(BoolSubtract, 5, big, small)
    let b = box3 (-1.0, 1.0) (-1.0, 1.0) (-1.0, 1.0)
    let r = FieldInterval.eval st b sub
    Assert.True(r.Hi < 0.0, sprintf "expected Hi < 0, got %g" r.Hi)

// ── Smooth boolean widens on Lo side by k/6 ──────────────────────────────

[<Fact>]
let ``Smooth Union with k>0 widens Lo downward`` () =
    let st = slots [ 1.0; 10.0; 0.0; 0.0; 1.0; 0.6 ]  // k = 0.6
    let sphereA = FPrimitive(PrimSphere 0)
    let sphereB = FTranslate(1, 2, 3, FPrimitive(PrimSphere 4))
    let union = FBoolean(BoolUnion, 5, sphereA, sphereB)
    let b = box3 (-0.5, 0.5) (-0.5, 0.5) (-0.5, 0.5)
    let rU = FieldInterval.eval st b union
    // Lo should have been lowered by k/6 = 0.1 relative to sharp.
    let stSharp = slots [ 1.0; 10.0; 0.0; 0.0; 1.0; 0.0 ]
    let rSharp = FieldInterval.eval stSharp b union
    approxEq (rSharp.Lo - 0.1) rU.Lo 1e-9
    Assert.Equal(rSharp.Hi, rU.Hi)

// ── Containment property (the actual soundness check) ────────────────────

let private sampleInBox (rng: System.Random) (b: IntervalBox) =
    let r (i: Interval) = i.Lo + rng.NextDouble() * (i.Hi - i.Lo)
    (r b.XI, r b.YI, r b.ZI)

let private checkContainment (st: SlotTable) (node: FieldNode) (b: IntervalBox) (seed: int) (n: int) =
    let bound = FieldInterval.eval st b node
    let rng = System.Random(seed)
    [ 1 .. n ]
    |> List.iter (fun _ ->
        let p = sampleInBox rng b
        let v = refSdf st p node
        Assert.True(
            v >= bound.Lo - 1e-9 && v <= bound.Hi + 1e-9,
            sprintf "point %A gave SDF %g outside bound [%g, %g]" p v bound.Lo bound.Hi))

[<Fact>]
let ``Containment: sphere over straddling box`` () =
    let st = slots [ 1.5 ]
    let node = FPrimitive(PrimSphere 0)
    let b = box3 (-2.0, 2.0) (-2.0, 2.0) (-2.0, 2.0)
    checkContainment st node b 42 500

[<Fact>]
let ``Containment: translated sphere`` () =
    let st = slots [ 2.0; 3.0; -1.0; 0.5 ]
    let node = FTranslate(1, 2, 3, FPrimitive(PrimSphere 0))
    let b = box3 (0.0, 5.0) (-3.0, 2.0) (-2.0, 3.0)
    checkContainment st node b 123 500

[<Fact>]
let ``Containment: sharp union of two spheres`` () =
    let st = slots [ 1.0; 2.5; 0.0; 0.0; 1.0; 0.0 ]
    let sphereA = FPrimitive(PrimSphere 0)
    let sphereB = FTranslate(1, 2, 3, FPrimitive(PrimSphere 4))
    let node = FBoolean(BoolUnion, 5, sphereA, sphereB)
    let b = box3 (-2.0, 4.0) (-2.0, 2.0) (-2.0, 2.0)
    checkContainment st node b 7 1000

[<Fact>]
let ``Containment: smooth union respects widening`` () =
    let st = slots [ 1.0; 1.5; 0.0; 0.0; 1.0; 0.4 ]
    let sphereA = FPrimitive(PrimSphere 0)
    let sphereB = FTranslate(1, 2, 3, FPrimitive(PrimSphere 4))
    let node = FBoolean(BoolUnion, 5, sphereA, sphereB)
    let b = box3 (-2.0, 3.0) (-2.0, 2.0) (-2.0, 2.0)
    checkContainment st node b 999 1000

[<Fact>]
let ``Containment: halfplane flipped`` () =
    let st = slots [ 1.5 ]
    let node = FPrimitive(PrimHalfPlane("Y", 0, true))
    let b = box3 (-5.0, 5.0) (-5.0, 5.0) (-5.0, 5.0)
    checkContainment st node b 31 500

[<Fact>]
let ``Containment: shell of a sphere`` () =
    let st = slots [ 2.0; 0.2 ]  // r=2, thickness=0.2
    let node = FFieldOp(OpShell, 1, FPrimitive(PrimSphere 0))
    let b = box3 (-3.0, 3.0) (-3.0, 3.0) (-3.0, 3.0)
    checkContainment st node b 88 500

[<Fact>]
let ``Containment: axis-aligned box`` () =
    let st = slots [ 1.2; 1.6; 0.8 ]
    let node = FPrimitive(PrimBox(0, 1, 2))
    let b = box3 (-2.0, 2.0) (-2.0, 2.0) (-2.0, 2.0)
    checkContainment st node b 17 1000

[<Fact>]
let ``Containment: translated box`` () =
    let st = slots [ 1.0; 1.0; 1.0; 2.0; -1.0; 0.5 ]
    let node = FTranslate(3, 4, 5, FPrimitive(PrimBox(0, 1, 2)))
    let b = box3 (0.0, 4.0) (-3.0, 1.0) (-1.0, 2.0)
    checkContainment st node b 73 1000

[<Fact>]
let ``Containment: cylinder`` () =
    let st = slots [ 0.7; 1.4 ]  // r, h
    let node = FPrimitive(PrimCylinder(0, 1))
    let b = box3 (-1.5, 1.5) (-1.5, 1.5) (-1.5, 1.5)
    checkContainment st node b 54 1000

[<Fact>]
let ``Containment: cylinder subtracted from box`` () =
    // box 2×2×2 with a cylindrical hole through it.
    let st = slots [ 2.0; 2.0; 2.0; 0.5; 3.0; 0.0 ]
    //                bw   bh   bd   cr   ch   subK
    let boxN = FPrimitive(PrimBox(0, 1, 2))
    let cyl = FPrimitive(PrimCylinder(3, 4))
    let node = FBoolean(BoolSubtract, 5, boxN, cyl)
    let b = box3 (-1.5, 1.5) (-1.5, 1.5) (-1.5, 1.5)
    checkContainment st node b 200 1500

// ── Pruning classification (the actual point of all this) ────────────────

type PruneClass = FullyOutside | FullyInside | Ambiguous

let classify (i: Interval) : PruneClass =
    match i with
    | _ when i.Lo > 0.0 -> FullyOutside
    | _ when i.Hi < 0.0 -> FullyInside
    | _ -> Ambiguous

[<Fact>]
let ``Prune: sphere box far away is classified Outside`` () =
    let st = slots [ 1.0 ]
    let node = FPrimitive(PrimSphere 0)
    let b = box3 (5.0, 6.0) (5.0, 6.0) (5.0, 6.0)
    Assert.Equal(FullyOutside, classify (FieldInterval.eval st b node))

[<Fact>]
let ``Prune: sphere box at origin is classified Inside`` () =
    let st = slots [ 1.0 ]
    let node = FPrimitive(PrimSphere 0)
    let b = box3 (-0.3, 0.3) (-0.3, 0.3) (-0.3, 0.3)
    Assert.Equal(FullyInside, classify (FieldInterval.eval st b node))

[<Fact>]
let ``Prune: sphere box straddling surface is Ambiguous`` () =
    let st = slots [ 1.0 ]
    let node = FPrimitive(PrimSphere 0)
    let b = box3 (0.5, 1.5) (-0.3, 0.3) (-0.3, 0.3)
    Assert.Equal(Ambiguous, classify (FieldInterval.eval st b node))

[<Fact>]
let ``Prune: Rotate is always Ambiguous (punted)`` () =
    let st = slots [ 1.0; 0.0; 0.0; 1.0; 0.5 ]
    let node = FRotate(1, 2, 3, 4, FPrimitive(PrimSphere 0))
    let b = box3 (5.0, 6.0) (5.0, 6.0) (5.0, 6.0)
    Assert.Equal(Ambiguous, classify (FieldInterval.eval st b node))

// ── Simplify: tape-pruning Fidget-style ─────────────────────────────────

[<Fact>]
let ``Simplify: primitive is returned unchanged`` () =
    let st = slots [ 1.0 ]
    let node = FPrimitive(PrimSphere 0)
    let b = box3 (-1.0, 1.0) (-1.0, 1.0) (-1.0, 1.0)
    let (_, simplified) = FieldInterval.simplify st b node
    Assert.Equal<FieldNode>(node, simplified)

[<Fact>]
let ``Simplify: sharp Union with A dominating near A returns just A`` () =
    // Sphere A at origin r=1, Sphere B at (10,0,0) r=1. Query box near A.
    // Sharp union (k=0). A's interval is deeply negative; B's is way positive.
    // A.Hi + k ≤ B.Lo → drop B → simplify returns sphereA.
    let st = slots [ 1.0; 10.0; 0.0; 0.0; 1.0; 0.0 ]
    let sphereA = FPrimitive(PrimSphere 0)
    let sphereB = FTranslate(1, 2, 3, FPrimitive(PrimSphere 4))
    let union = FBoolean(BoolUnion, 5, sphereA, sphereB)
    let b = box3 (-0.5, 0.5) (-0.5, 0.5) (-0.5, 0.5)
    let (_, simp) = FieldInterval.simplify st b union
    Assert.Equal<FieldNode>(sphereA, simp)

[<Fact>]
let ``Simplify: sharp Union with B dominating returns just B`` () =
    // Same setup; query box near B.
    let st = slots [ 1.0; 10.0; 0.0; 0.0; 1.0; 0.0 ]
    let sphereA = FPrimitive(PrimSphere 0)
    let sphereB = FTranslate(1, 2, 3, FPrimitive(PrimSphere 4))
    let union = FBoolean(BoolUnion, 5, sphereA, sphereB)
    let b = box3 (9.5, 10.5) (-0.5, 0.5) (-0.5, 0.5)
    let (_, simp) = FieldInterval.simplify st b union
    Assert.Equal<FieldNode>(sphereB, simp)

[<Fact>]
let ``Simplify: ambiguous Union keeps both branches`` () =
    // Sphere A at origin, Sphere B at (1.2, 0, 0); query box straddles both.
    let st = slots [ 1.0; 1.2; 0.0; 0.0; 1.0; 0.0 ]
    let sphereA = FPrimitive(PrimSphere 0)
    let sphereB = FTranslate(1, 2, 3, FPrimitive(PrimSphere 4))
    let union = FBoolean(BoolUnion, 5, sphereA, sphereB)
    let b = box3 (0.0, 1.2) (-1.0, 1.0) (-1.0, 1.0)
    let (_, simp) = FieldInterval.simplify st b union
    match simp with
    | FBoolean(BoolUnion, _, _, _) -> ()
    | other -> failwithf "expected union preserved, got %A" other

[<Fact>]
let ``Simplify: smooth Union where both branches relevant keeps both`` () =
    // Spheres r=1 at origin and (2,0,0), k=0.5. Query box straddles both.
    // Both A and B have intervals that overlap heavily — neither dominates.
    let st = slots [ 1.0; 2.0; 0.0; 0.0; 1.0; 0.5 ]
    let sphereA = FPrimitive(PrimSphere 0)
    let sphereB = FTranslate(1, 2, 3, FPrimitive(PrimSphere 4))
    let union = FBoolean(BoolUnion, 5, sphereA, sphereB)
    let b = box3 (-0.5, 2.5) (-0.5, 0.5) (-0.5, 0.5)
    let (_, simp) = FieldInterval.simplify st b union
    match simp with
    | FBoolean _ -> ()
    | other -> failwithf "expected union preserved, got %A" other

[<Fact>]
let ``Simplify: sharp Intersect with A dominating returns just A`` () =
    // Two half-planes: X ≥ 0 and Y ≥ 0. Query box where X is clearly the binder.
    //   A = X-offset (X > 0 everywhere in box, interval [2,3])
    //   B = Y-offset (Y > 0 everywhere in box, interval [0.1, 0.2])
    //   Intersect = max(A, B). A.Lo = 2 ≥ B.Hi = 0.2 → drop B, keep A.
    let st = slots [ 0.0; 0.0; 0.0 ]  // both offsets 0, subtractK unused
    let hpX = FPrimitive(PrimHalfPlane("X", 0, false))
    let hpY = FPrimitive(PrimHalfPlane("Y", 1, false))
    let inter = FBoolean(BoolIntersect, 2, hpX, hpY)
    let b = box3 (2.0, 3.0) (0.1, 0.2) (-1.0, 1.0)
    let (_, simp) = FieldInterval.simplify st b inter
    Assert.Equal<FieldNode>(hpX, simp)

[<Fact>]
let ``Simplify: Subtract keeps A when A.Lo + B.Lo >= k`` () =
    // A is clearly outside B. Subtract = max(A, -B). If A.Lo ≥ -B.Lo (sharp),
    // result = A. Sphere A at origin r=1; Sphere B at (5,0,0) r=0.5.
    // Box near A: A's interval is say [-1, 0]; B's [~3, 5]. A.Lo + B.Lo = -1 + 3 = 2 ≥ 0.
    let st = slots [ 1.0; 5.0; 0.0; 0.0; 0.5; 0.0 ]
    let sphereA = FPrimitive(PrimSphere 0)
    let sphereB = FTranslate(1, 2, 3, FPrimitive(PrimSphere 4))
    let sub = FBoolean(BoolSubtract, 5, sphereA, sphereB)
    let b = box3 (-1.0, 1.0) (-1.0, 1.0) (-1.0, 1.0)
    let (_, simp) = FieldInterval.simplify st b sub
    Assert.Equal<FieldNode>(sphereA, simp)

[<Fact>]
let ``Simplify: bound agrees with eval of original`` () =
    // For the three-sphere smooth-union scene, simplify's returned interval
    // must equal eval of the original (modulo float error).
    let st = slots [ 1.0; 1.5; 0.0; 0.0; 0.8; -0.5; 0.3; 0.0; 0.6; 0.2 ]
    //                rA   tBx  tBy  tBz  rB   tCx   tCy  tCz  rC   k
    let a = FPrimitive(PrimSphere 0)
    let b = FTranslate(1, 2, 3, FPrimitive(PrimSphere 4))
    let c = FTranslate(5, 6, 7, FPrimitive(PrimSphere 8))
    let node = FBoolean(BoolUnion, 9, FBoolean(BoolUnion, 9, a, b), c)
    let box_ = box3 (-2.0, 2.0) (-2.0, 2.0) (-2.0, 2.0)
    let evalBound = FieldInterval.eval st box_ node
    let (simpBound, _) = FieldInterval.simplify st box_ node
    approxEq evalBound.Lo simpBound.Lo 1e-9
    approxEq evalBound.Hi simpBound.Hi 1e-9

[<Fact>]
let ``Simplify translate wraps the simplified child`` () =
    // Union of two spheres wrapped in a translate; simplify at a box where
    // (after translation) one dominates. The translate must stay at the top.
    let st = slots [ 1.0; 10.0; 0.0; 0.0; 1.0; 0.0; 5.0; 5.0; 5.0 ]
    //                rA   tBx  tBy  tBz  rB   k    outerTx ty tz
    let a = FPrimitive(PrimSphere 0)
    let b = FTranslate(1, 2, 3, FPrimitive(PrimSphere 4))
    let inner = FBoolean(BoolUnion, 5, a, b)
    let outer = FTranslate(6, 7, 8, inner)
    // Box in world coords that, post-translation by (5,5,5), sits near origin (A's position).
    let worldBox = box3 (4.5, 5.5) (4.5, 5.5) (4.5, 5.5)
    let (_, simp) = FieldInterval.simplify st worldBox outer
    match simp with
    | FTranslate(6, 7, 8, FPrimitive(PrimSphere 0)) -> ()
    | other -> failwithf "expected translate wrapping sphereA, got %A" other
