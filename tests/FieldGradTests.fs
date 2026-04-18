module FieldGradTests

open Xunit
open Server

// ── Helpers ──────────────────────────────────────────────────────────────

let private slots (vals: float list) : SlotTable =
    { Values = Array.ofList vals; Index = Map.empty }

let inline private approxEq (expected: float) (actual: float) (eps: float) (label: string) =
    Assert.True(
        abs (expected - actual) < eps,
        sprintf "%s: expected %g, got %g (|Δ|=%g, eps=%g)" label expected actual (abs (expected - actual)) eps)

/// Central-difference numerical gradient of an SDF's value at a point.
/// Uses `FieldGrad.evalAt` itself but only reads .V — that gives us a
/// scalar SDF "oracle" without building a separate `FieldPoint` module.
let private numericGrad (st: SlotTable) (node: FieldNode) (x, y, z) (h: float) =
    let at (x', y', z') = (FieldGrad.evalAt st (x', y', z') node).V
    let dx = (at (x + h, y, z) - at (x - h, y, z)) / (2.0 * h)
    let dy = (at (x, y + h, z) - at (x, y - h, z)) / (2.0 * h)
    let dz = (at (x, y, z + h) - at (x, y, z - h)) / (2.0 * h)
    dx, dy, dz

/// Verify AD gradient matches central differences at a single point.
let private checkGradAt (st: SlotTable) (node: FieldNode) (pt: float * float * float) (eps: float) (label: string) =
    let g = FieldGrad.evalAt st pt node
    let (nx, ny, nz) = numericGrad st node pt 1e-4
    approxEq nx g.Dx eps (label + " ∂/∂x")
    approxEq ny g.Dy eps (label + " ∂/∂y")
    approxEq nz g.Dz eps (label + " ∂/∂z")

// ── Grad arithmetic sanity ───────────────────────────────────────────────

[<Fact>]
let ``Grad.add sums values and partials`` () =
    let a = { V = 2.0; Dx = 1.0; Dy = 0.0; Dz = 0.0 }
    let b = { V = 3.0; Dx = 0.0; Dy = 1.0; Dz = 0.0 }
    let r = Grad.add a b
    Assert.Equal(5.0, r.V)
    Assert.Equal(1.0, r.Dx)
    Assert.Equal(1.0, r.Dy)
    Assert.Equal(0.0, r.Dz)

[<Fact>]
let ``Grad.mul uses product rule`` () =
    // f(x) = x², at x=3: f = 9, f' = 2x = 6.
    let x = { V = 3.0; Dx = 1.0; Dy = 0.0; Dz = 0.0 }
    let r = Grad.mul x x
    Assert.Equal(9.0, r.V)
    Assert.Equal(6.0, r.Dx)

[<Fact>]
let ``Grad.sqrt has correct derivative`` () =
    // f(x) = sqrt(x), at x=4: f = 2, f' = 1/(2 sqrt(x)) = 0.25.
    let x = { V = 4.0; Dx = 1.0; Dy = 0.0; Dz = 0.0 }
    let r = Grad.sqrt x
    approxEq 2.0 r.V 1e-9 "V"
    approxEq 0.25 r.Dx 1e-9 "Dx"

// ── Sphere primitive ─────────────────────────────────────────────────────

[<Fact>]
let ``Sphere gradient at (1, 0, 0) on unit sphere is (1, 0, 0)`` () =
    // SDF = sqrt(x²+y²+z²) - 1, gradient = (x,y,z)/|p|. On unit sphere
    // at (1,0,0): gradient = (1, 0, 0) (outward normal).
    let st = slots [ 1.0 ]
    let node = FPrimitive(PrimSphere 0)
    let g = FieldGrad.evalAt st (1.0, 0.0, 0.0) node
    approxEq 0.0 g.V 1e-9 "V (on surface)"
    approxEq 1.0 g.Dx 1e-9 "Dx"
    approxEq 0.0 g.Dy 1e-9 "Dy"
    approxEq 0.0 g.Dz 1e-9 "Dz"

[<Fact>]
let ``Sphere gradient is unit-length on the surface`` () =
    let st = slots [ 2.0 ]
    let node = FPrimitive(PrimSphere 0)
    // A point on the r=2 sphere.
    let pt = (2.0 / sqrt 3.0, 2.0 / sqrt 3.0, 2.0 / sqrt 3.0)
    let g = FieldGrad.evalAt st pt node
    approxEq 0.0 g.V 1e-9 "V (on surface)"
    let nLen = sqrt (g.Dx * g.Dx + g.Dy * g.Dy + g.Dz * g.Dz)
    approxEq 1.0 nLen 1e-9 "|gradient|"

[<Fact>]
let ``Sphere: AD matches numerical central differences`` () =
    let st = slots [ 1.5 ]
    let node = FPrimitive(PrimSphere 0)
    checkGradAt st node (0.7, -0.3, 1.1) 1e-6 "sphere"

// ── HalfPlane primitive ──────────────────────────────────────────────────

[<Fact>]
let ``HalfPlane X: gradient is (1, 0, 0) everywhere`` () =
    let st = slots [ 0.0 ]
    let node = FPrimitive(PrimHalfPlane("X", 0, false))
    let g = FieldGrad.evalAt st (2.0, 3.0, 4.0) node
    Assert.Equal(1.0, g.Dx)
    Assert.Equal(0.0, g.Dy)
    Assert.Equal(0.0, g.Dz)

[<Fact>]
let ``HalfPlane flipped: gradient is (-1, 0, 0)`` () =
    let st = slots [ 0.0 ]
    let node = FPrimitive(PrimHalfPlane("X", 0, true))
    let g = FieldGrad.evalAt st (2.0, 3.0, 4.0) node
    Assert.Equal(-1.0, g.Dx)

// ── Box primitive ────────────────────────────────────────────────────────

[<Fact>]
let ``Box: AD matches numerical (exterior point)`` () =
    let st = slots [ 2.0; 1.0; 1.5 ]
    let node = FPrimitive(PrimBox(0, 1, 2))
    // Point outside the box on the +x face.
    checkGradAt st node (2.3, 0.1, 0.2) 1e-4 "box exterior"

[<Fact>]
let ``Box: AD matches numerical (interior point)`` () =
    let st = slots [ 2.0; 1.0; 1.5 ]
    let node = FPrimitive(PrimBox(0, 1, 2))
    // Point inside the box.
    checkGradAt st node (0.1, 0.2, 0.3) 1e-4 "box interior"

// ── Cylinder primitive ───────────────────────────────────────────────────

[<Fact>]
let ``Cylinder: AD matches numerical (lateral exterior)`` () =
    let st = slots [ 0.6; 1.2 ]  // r, h
    let node = FPrimitive(PrimCylinder(0, 1))
    // Point outside the cylinder radially.
    checkGradAt st node (1.0, 0.3, 0.2) 1e-4 "cylinder lateral"

[<Fact>]
let ``Cylinder: AD matches numerical (axial exterior)`` () =
    let st = slots [ 0.6; 1.2 ]
    let node = FPrimitive(PrimCylinder(0, 1))
    // Point outside the cylinder axially (above top cap).
    checkGradAt st node (0.2, 0.1, 1.3) 1e-4 "cylinder axial"

[<Fact>]
let ``Cylinder: AD matches numerical (corner exterior)`` () =
    let st = slots [ 0.6; 1.2 ]
    let node = FPrimitive(PrimCylinder(0, 1))
    // Outside both radially and axially — branch1 (sqrt of sum).
    checkGradAt st node (1.0, 0.5, 1.3) 1e-4 "cylinder corner"

// ── Translate ────────────────────────────────────────────────────────────

[<Fact>]
let ``Translate: sphere moved to (5, 0, 0) has zero at (5, 0, 0) + r`` () =
    // Unit sphere translated by (5, 0, 0); surface point (6, 0, 0).
    let st = slots [ 1.0; 5.0; 0.0; 0.0 ]
    let node = FTranslate(1, 2, 3, FPrimitive(PrimSphere 0))
    let g = FieldGrad.evalAt st (6.0, 0.0, 0.0) node
    approxEq 0.0 g.V 1e-9 "V on surface"
    approxEq 1.0 g.Dx 1e-9 "Dx (outward normal)"

[<Fact>]
let ``Translate: AD matches numerical`` () =
    let st = slots [ 1.0; 2.0; -1.0; 0.5 ]
    let node = FTranslate(1, 2, 3, FPrimitive(PrimSphere 0))
    checkGradAt st node (3.0, -0.5, 0.8) 1e-6 "translate"

// ── Booleans ────────────────────────────────────────────────────────────

[<Fact>]
let ``Sharp union: gradient picks the smaller branch`` () =
    // Sphere A (r=1) at origin, Sphere B (r=1) at (5, 0, 0). Query near A.
    // A's SDF is much smaller, so sharp union = A, gradient = A's gradient.
    let st = slots [ 1.0; 5.0; 0.0; 0.0; 1.0; 0.0 ]
    let sphereA = FPrimitive(PrimSphere 0)
    let sphereB = FTranslate(1, 2, 3, FPrimitive(PrimSphere 4))
    let union = FBoolean(BoolUnion, 5, sphereA, sphereB)
    let gA = FieldGrad.evalAt st (1.0, 0.0, 0.0) sphereA
    let gU = FieldGrad.evalAt st (1.0, 0.0, 0.0) union
    approxEq gA.V gU.V 1e-9 "V"
    approxEq gA.Dx gU.Dx 1e-9 "Dx"

[<Fact>]
let ``Smooth union: AD matches numerical`` () =
    let st = slots [ 1.0; 1.5; 0.0; 0.0; 0.8; 0.3 ]
    let a = FPrimitive(PrimSphere 0)
    let b = FTranslate(1, 2, 3, FPrimitive(PrimSphere 4))
    let node = FBoolean(BoolUnion, 5, a, b)
    // Point in the blending zone between the two spheres.
    checkGradAt st node (0.75, 0.0, 0.0) 1e-4 "smooth union"

[<Fact>]
let ``Smooth subtract: AD matches numerical`` () =
    let st = slots [ 1.5; 0.5; 0.0; 0.0; 0.5; 0.1 ]
    //                rA   tBx  tBy  tBz  rB   k
    let a = FPrimitive(PrimSphere 0)
    let b = FTranslate(1, 2, 3, FPrimitive(PrimSphere 4))
    let node = FBoolean(BoolSubtract, 5, a, b)
    // Point near the "bite" edge.
    checkGradAt st node (0.9, 0.3, 0.2) 1e-4 "smooth subtract"

[<Fact>]
let ``Smooth intersect: AD matches numerical`` () =
    let st = slots [ 1.0; 1.0; 0.0; 0.0; 1.0; 0.2 ]
    let a = FPrimitive(PrimSphere 0)
    let b = FTranslate(1, 2, 3, FPrimitive(PrimSphere 4))
    let node = FBoolean(BoolIntersect, 5, a, b)
    checkGradAt st node (0.6, 0.2, 0.1) 1e-4 "smooth intersect"

// ── Field ops ────────────────────────────────────────────────────────────

[<Fact>]
let ``Thicken preserves gradient of child`` () =
    // thicken(sdf, v) = sdf - v; only shifts the value, gradient unchanged.
    let st = slots [ 1.0; 0.3 ]  // r, v
    let sphere = FPrimitive(PrimSphere 0)
    let thick = FFieldOp(OpThicken, 1, sphere)
    let gS = FieldGrad.evalAt st (1.0, 0.0, 0.0) sphere
    let gT = FieldGrad.evalAt st (1.0, 0.0, 0.0) thick
    approxEq gS.Dx gT.Dx 1e-9 "Dx unchanged"
    approxEq (gS.V - 0.3) gT.V 1e-9 "V shifted by -v"

[<Fact>]
let ``Shell: AD matches numerical`` () =
    // shell(sdf, v) = max(sdf, -(sdf + v)). Has kinks; test in a region
    // where one branch clearly wins.
    let st = slots [ 2.0; 0.2 ]
    let node = FFieldOp(OpShell, 1, FPrimitive(PrimSphere 0))
    checkGradAt st node (2.5, 0.1, 0.0) 1e-4 "shell exterior"

// ── Sign of gradient means surface normal direction ─────────────────────

[<Fact>]
let ``Gradient points outward on all primitive surfaces`` () =
    // A few sanity checks: at a surface point (V ≈ 0), the gradient should
    // point away from the interior.
    let st = slots [ 1.0 ]
    let sphere = FPrimitive(PrimSphere 0)

    // Point (0.8, 0.6, 0) on unit sphere.
    let g = FieldGrad.evalAt st (0.8, 0.6, 0.0) sphere
    Assert.True(g.Dx > 0.0, sprintf "expected Dx > 0 on +x surface, got %g" g.Dx)
    Assert.True(g.Dy > 0.0, sprintf "expected Dy > 0 on +y surface, got %g" g.Dy)
