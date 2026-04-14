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
