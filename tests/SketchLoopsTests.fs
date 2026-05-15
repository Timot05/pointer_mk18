module SketchLoopsTests

open Xunit
open Server

// ── Helpers ──────────────────────────────────────────────────────────────

let point id x y = REPoint(id, x, y)
let line id s e = RELine(id, s, e)

// ── Tests ─────────────────────────────────────────────────────────────────

[<Fact>]
let ``Triangle detects one CCW loop`` () =
    let entities =
        [ point "a" 0.0 0.0
          point "b" 10.0 0.0
          point "c" 5.0 10.0
          line "ab" "a" "b"
          line "bc" "b" "c"
          line "ca" "c" "a" ]
    let loops = SketchLoops.detectLoops entities
    Assert.Equal(1, loops.Length)
    let face = loops.[0]
    Assert.Equal(3, face.EntityIds.Length)
    Assert.True(face.SignedArea > 0.0)

[<Fact>]
let ``Square detects one loop with 4 lines`` () =
    let entities =
        [ point "p_bl" 0.0 0.0
          point "p_br" 10.0 0.0
          point "p_tr" 10.0 10.0
          point "p_tl" 0.0 10.0
          line "l_bottom" "p_bl" "p_br"
          line "l_right" "p_br" "p_tr"
          line "l_top" "p_tr" "p_tl"
          line "l_left" "p_tl" "p_bl" ]
    let loops = SketchLoops.detectLoops entities
    Assert.Equal(1, loops.Length)
    let face = loops.[0]
    Assert.Equal(4, face.EntityIds.Length)
    Assert.True(face.SignedArea > 0.0)
    Assert.Equal(100.0, face.SignedArea, 1e-6)

[<Fact>]
let ``Circle is a trivial loop`` () =
    let entities =
        [ point "c0" 0.0 0.0
          RECircle("c1", "c0", 5.0) ]
    let loops = SketchLoops.detectLoops entities
    Assert.Equal(1, loops.Length)
    Assert.Equal<string list>([ "c1" ], loops.[0].EntityIds)
    Assert.True(loops.[0].SignedArea > 0.0)

[<Fact>]
let ``Square plus circle detects two loops`` () =
    let entities =
        [ point "p_bl" 0.0 0.0
          point "p_br" 10.0 0.0
          point "p_tr" 10.0 10.0
          point "p_tl" 0.0 10.0
          line "l_bottom" "p_bl" "p_br"
          line "l_right" "p_br" "p_tr"
          line "l_top" "p_tr" "p_tl"
          line "l_left" "p_tl" "p_bl"
          point "c0" 100.0 100.0
          RECircle("c1", "c0", 5.0) ]
    let loops = SketchLoops.detectLoops entities
    Assert.Equal(2, loops.Length)

[<Fact>]
let ``Open chain has no loop`` () =
    let entities =
        [ point "a" 0.0 0.0
          point "b" 10.0 0.0
          point "c" 10.0 10.0
          line "ab" "a" "b"
          line "bc" "b" "c" ]
    let loops = SketchLoops.detectLoops entities
    Assert.Empty(loops)

[<Fact>]
let ``Coincident points cluster into one vertex`` () =
    // Two triangles sharing an edge represented by two point pairs at
    // identical coordinates. Clustering must merge them.
    let entities =
        [ point "a" -76.6 69.874
          point "b" -91.012 -23.138
          point "c" -28.418 -7.566
          point "d" -76.6 69.874     // coincident with a
          point "e" -28.418 -7.566    // coincident with c
          point "f" -16.108 75.488
          line "ab" "a" "b"
          line "bc" "b" "c"
          line "ca" "c" "a"
          line "df" "d" "f"
          line "fe" "f" "e" ]
    let loops = SketchLoops.detectLoops entities
    Assert.Equal(2, loops.Length)

[<Fact>]
let ``Two adjacent quads share an edge and produce two loops`` () =
    // a---b---d
    // |   |   |
    // f---c---e
    let entities =
        [ point "a" 0.0 10.0
          point "b" 10.0 10.0
          point "c" 10.0 0.0
          point "f" 0.0 0.0
          point "d" 20.0 10.0
          point "e" 20.0 0.0
          line "ab" "a" "b"
          line "bc" "b" "c"
          line "cf" "c" "f"
          line "fa" "f" "a"
          line "bd" "b" "d"
          line "de" "d" "e"
          line "ec" "e" "c" ]
    let loops = SketchLoops.detectLoops entities
    Assert.Equal(2, loops.Length)

// ── Helpers exposed for signed-area tests ─────────────────────────────

[<Fact>]
let ``polygonSignedArea positive for CCW triangle`` () =
    let pts = [ 0.0, 0.0; 1.0, 0.0; 0.0, 1.0; 0.0, 0.0 ]
    Assert.True(SketchLoops.polygonSignedArea pts > 0.0)

[<Fact>]
let ``polygonSignedArea negative for CW triangle`` () =
    let pts = [ 0.0, 0.0; 0.0, 1.0; 1.0, 0.0; 0.0, 0.0 ]
    Assert.True(SketchLoops.polygonSignedArea pts < 0.0)

// ── Reconciliation ────────────────────────────────────────────────────────

let private detected (entityIds: string list) : SketchLoop =
    { Id = "ignored"   // reconcile matches by EntityIds set, not by Id
      EntityIds = entityIds
      SignedArea = 1.0 }

let private record (id: string) (entityIds: string list) (userNamed: bool) : LoopRecord =
    { Id = id; EntityIds = entityIds; UserNamed = userNamed }

[<Fact>]
let ``reconcile: empty inputs yield empty registry`` () =
    let result = SketchLoops.reconcile [] []
    Assert.Empty(result)

[<Fact>]
let ``reconcile: first-time detection assigns loop_0`` () =
    let result = SketchLoops.reconcile [] [ detected [ "ab"; "bc"; "ca" ] ]
    Assert.Equal(1, List.length result)
    Assert.Equal("loop_0", result.[0].Id)
    Assert.False(result.[0].UserNamed)

[<Fact>]
let ``reconcile: matching entity set carries persisted ID forward`` () =
    let persisted = [ record "loop_0" [ "ab"; "bc"; "ca" ] false ]
    let result = SketchLoops.reconcile persisted [ detected [ "ab"; "bc"; "ca" ] ]
    Assert.Equal("loop_0", result.[0].Id)

[<Fact>]
let ``reconcile: matching is order-insensitive`` () =
    let persisted = [ record "loop_0" [ "ab"; "bc"; "ca" ] false ]
    // Detection might traverse the same loop in reverse order.
    let result = SketchLoops.reconcile persisted [ detected [ "ca"; "bc"; "ab" ] ]
    Assert.Equal("loop_0", result.[0].Id)

[<Fact>]
let ``reconcile: persisted user-named flag is preserved on match`` () =
    let persisted = [ record "outer_face" [ "ab"; "bc"; "ca" ] true ]
    let result = SketchLoops.reconcile persisted [ detected [ "ab"; "bc"; "ca" ] ]
    Assert.Equal("outer_face", result.[0].Id)
    Assert.True(result.[0].UserNamed)

[<Fact>]
let ``reconcile: vanished detections drop their persisted record`` () =
    let persisted = [ record "loop_0" [ "ab"; "bc"; "ca" ] false ]
    let result = SketchLoops.reconcile persisted []
    Assert.Empty(result)

[<Fact>]
let ``reconcile: new detection alongside existing one allocates next loop_N`` () =
    let persisted = [ record "loop_0" [ "ab"; "bc"; "ca" ] false ]
    let result =
        SketchLoops.reconcile persisted
            [ detected [ "ab"; "bc"; "ca" ]
              detected [ "de"; "ef"; "fd" ] ]
    Assert.Equal(2, List.length result)
    let ids = result |> List.map (fun r -> r.Id) |> Set.ofList
    Assert.Contains("loop_0", ids)
    Assert.Contains("loop_1", ids)

[<Fact>]
let ``reconcile: ID allocation skips numbers in use by other persisted records`` () =
    // Both records survive; the new detection gets loop_2 (next > max).
    let persisted =
        [ record "loop_0" [ "ab" ] false
          record "loop_1" [ "cd" ] false ]
    let result =
        SketchLoops.reconcile persisted
            [ detected [ "ab" ]
              detected [ "cd" ]
              detected [ "ef" ] ]
    let newRecord = result |> List.find (fun r -> r.EntityIds = [ "ef" ])
    Assert.Equal("loop_2", newRecord.Id)

[<Fact>]
let ``reconcile: output order matches detection order`` () =
    let persisted = [ record "loop_0" [ "ab" ] false ]
    let result =
        SketchLoops.reconcile persisted
            [ detected [ "cd" ]   // new — gets a fresh id
              detected [ "ab" ] ] // existing
    // Detection order preserved → "cd" loop comes first.
    Assert.Equal<string list>([ "cd" ], result.[0].EntityIds)
    Assert.Equal<string list>([ "ab" ], result.[1].EntityIds)
    Assert.Equal("loop_0", result.[1].Id)

[<Fact>]
let ``reconcile: user-named record without matching detection is dropped`` () =
    let persisted = [ record "outer" [ "ab"; "bc"; "ca" ] true ]
    let result = SketchLoops.reconcile persisted [ detected [ "de"; "ef"; "fd" ] ]
    // Strict matching policy: vanished loop is dropped even if user-named.
    // Loosening this is a later policy decision.
    Assert.Equal(1, List.length result)
    Assert.NotEqual<string>("outer", result.[0].Id)
    Assert.False(result.[0].UserNamed)
