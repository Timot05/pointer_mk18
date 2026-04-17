module CpuLmSolverTests

open Xunit
open Server

[<Fact>]
let ``solveGraphWithCpu drives a simple residual graph toward zero`` () =
    let builder = GraphBuilder()
    let x = builder.Param 5.0
    let three = builder.Constant 3.0
    let residual = builder.Sub(x, three)
    let graph = builder.Build([| residual |], [| 0 |])

    let solved = CpuLmSolver.solveGraphWithCpuSync graph [| 5.0f |] [] GpuLmSolver.defaultSolverConfig

    Assert.InRange(float solved.[0], 2.999, 3.001)
