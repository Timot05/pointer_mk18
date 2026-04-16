module GpuLmSolverTests

open Xunit
open Server

[<Fact>]
let ``luSolveInPlace solves a small dense linear system`` () =
    let a =
        [| 4.0; 1.0
           1.0; 3.0 |]

    let rhs = [| 1.0; 2.0 |]
    let ok = GpuLmSolver.luSolveInPlace a rhs 2

    Assert.True(ok)
    Assert.InRange(rhs.[0], 0.0908, 0.0910)
    Assert.InRange(rhs.[1], 0.6363, 0.6365)

[<Fact>]
let ``normalEquations builds JTJ plus damping and JTr`` () =
    let jacobian =
        [| 1.0; 2.0
           3.0; 4.0 |]

    let residual = [| 5.0; 6.0 |]
    let matrix = Array.zeroCreate<float> 4
    let jtr = Array.zeroCreate<float> 2

    GpuLmSolver.normalEquations jacobian residual 0.5 2 2 matrix jtr

    Assert.Equal<float array>([| 10.5; 14.0; 14.0; 20.5 |], matrix)
    Assert.Equal<float array>([| 23.0; 34.0 |], jtr)

[<Fact>]
let ``buildJacobian appends pin rows after residual rows`` () =
    let rawJac =
        [| 1.0f; 2.0f
           3.0f; 4.0f |]

    let pins =
        [ { LocalSlot = 0
            VarIndex = 1
            Target = 0.0
            Weight = 7.0 } ]

    let jacobian = GpuLmSolver.buildJacobian pins 2 2 rawJac

    Assert.Equal<float array>([| 1.0; 2.0; 3.0; 4.0; 0.0; 7.0 |], jacobian)

[<Fact>]
let ``fillPins writes weighted residuals at the requested offset`` () =
    let destination = Array.zeroCreate<float> 4
    let source = [| 2.0f; 5.0f |]

    let pins =
        [ { LocalSlot = 0
            VarIndex = 0
            Target = 1.5
            Weight = 2.0 }
          { LocalSlot = 1
            VarIndex = 1
            Target = 4.0
            Weight = 3.0 } ]

    GpuLmSolver.fillPins pins destination 2 source

    Assert.Equal<float array>([| 0.0; 0.0; 1.0; 3.0 |], destination)
