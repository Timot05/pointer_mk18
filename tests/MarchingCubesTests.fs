module MarchingCubesTests

open Xunit
open Server

let private slots (vals: float list) : SlotTable =
    { Values = Array.ofList vals; Index = Map.empty }

let private box3 (xLo, xHi) (yLo, yHi) (zLo, zHi) : IntervalBox =
    { XI = Interval.make xLo xHi
      YI = Interval.make yLo yHi
      ZI = Interval.make zLo zHi }

let private dist3 (ax, ay, az) (bx, by, bz) =
    let dx = ax - bx
    let dy = ay - by
    let dz = az - bz
    sqrt (dx * dx + dy * dy + dz * dz)

// ── Degenerate cases ────────────────────────────────────────────────────

[<Fact>]
let ``All corners outside → no triangles`` () =
    // Unit sphere at origin; tile far away.
    let st = slots [ 1.0 ]
    let node = FPrimitive(PrimSphere 0)
    let tile = box3 (5.0, 6.0) (5.0, 6.0) (5.0, 6.0)
    let tris = MarchingCubes.triangulate st node tile
    Assert.Empty(tris)

[<Fact>]
let ``All corners inside → no triangles`` () =
    // Huge sphere; tile entirely inside.
    let st = slots [ 100.0 ]
    let node = FPrimitive(PrimSphere 0)
    let tile = box3 (-0.1, 0.1) (-0.1, 0.1) (-0.1, 0.1)
    let tris = MarchingCubes.triangulate st node tile
    Assert.Empty(tris)

// ── Surface crosses the cube ────────────────────────────────────────────

[<Fact>]
let ``Sphere crossing the tile yields triangles`` () =
    // Unit sphere; tile straddles the surface.
    let st = slots [ 1.0 ]
    let node = FPrimitive(PrimSphere 0)
    let tile = box3 (0.5, 1.5) (-0.5, 0.5) (-0.5, 0.5)
    let tris = MarchingCubes.triangulate st node tile
    Assert.NotEmpty(tris)

[<Fact>]
let ``Generated vertices lie on the surface`` () =
    // For each emitted vertex, |SDF| should be small — MC's edge
    // interpolation lands close to where V=0 on the corner-line.
    let st = slots [ 1.0 ]
    let node = FPrimitive(PrimSphere 0)
    let tile = box3 (0.5, 1.5) (-0.5, 0.5) (-0.5, 0.5)
    let tris = MarchingCubes.triangulate st node tile
    let vertices =
        tris
        |> List.collect (fun t -> [ t.V0; t.V1; t.V2 ])
    vertices
    |> List.iter (fun p ->
        let v = (FieldGrad.evalAt st p node).V
        Assert.True(abs v < 0.1, sprintf "vertex %A off surface by %g" p v))

[<Fact>]
let ``Normals are unit length`` () =
    let st = slots [ 1.0 ]
    let node = FPrimitive(PrimSphere 0)
    let tile = box3 (0.5, 1.5) (-0.5, 0.5) (-0.5, 0.5)
    let tris = MarchingCubes.triangulate st node tile
    let normals =
        tris
        |> List.collect (fun t -> [ t.N0; t.N1; t.N2 ])
    normals
    |> List.iter (fun (nx, ny, nz) ->
        let mag = sqrt (nx * nx + ny * ny + nz * nz)
        Assert.True(abs (mag - 1.0) < 1e-6, sprintf "normal %A has |n|=%g" (nx, ny, nz) mag))

// ── Sign convention: one corner inside → exactly one triangle ──────────

[<Fact>]
let ``Single inside corner (pattern 0x01) produces one triangle`` () =
    // Sphere r=0.3 centered on corner 0 of unit tile [0,1]³.
    // Corner 0 at (0,0,0) is inside (V = -0.3). All others outside.
    let st = slots [ 0.3 ]
    let node = FPrimitive(PrimSphere 0)
    let tile = box3 (0.0, 1.0) (0.0, 1.0) (0.0, 1.0)
    let tris = MarchingCubes.triangulate st node tile
    // Case 0x01 has one triangle (edges 0, 8, 3).
    Assert.Equal(1, tris.Length)

[<Fact>]
let ``Triangle for single-corner case sits near that corner`` () =
    let st = slots [ 0.3 ]
    let node = FPrimitive(PrimSphere 0)
    let tile = box3 (0.0, 1.0) (0.0, 1.0) (0.0, 1.0)
    let tris = MarchingCubes.triangulate st node tile
    let tri = tris.[0]
    // All three vertices should be within ~0.3 of corner (0,0,0).
    [ tri.V0; tri.V1; tri.V2 ]
    |> List.iter (fun v ->
        let d = dist3 v (0.0, 0.0, 0.0)
        Assert.True(d < 0.4, sprintf "vertex %A too far from corner 0: d=%g" v d))

// ── Booleans work through MC ───────────────────────────────────────────

[<Fact>]
let ``Union of two spheres produces triangles over their joint shell`` () =
    // Sphere A r=1 at origin, tiny sphere B far away. Tile straddles A.
    let st = slots [ 1.0; 10.0; 0.0; 0.0; 0.1; 0.0 ]
    let a = FPrimitive(PrimSphere 0)
    let b = FTranslate(1, 2, 3, FPrimitive(PrimSphere 4))
    let node = FBoolean(BoolUnion, 5, a, b)
    let tile = box3 (0.5, 1.5) (-0.5, 0.5) (-0.5, 0.5)
    let tris = MarchingCubes.triangulate st node tile
    // Hard to predict exact triangle count; just assert the emitted
    // vertices are on the (smooth) union surface.
    Assert.NotEmpty(tris)
    tris
    |> List.collect (fun t -> [ t.V0; t.V1; t.V2 ])
    |> List.iter (fun p ->
        let v = (FieldGrad.evalAt st p node).V
        Assert.True(abs v < 0.2, sprintf "vertex %A off surface by %g" p v))
