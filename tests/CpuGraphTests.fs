module CpuGraphTests

open Xunit
open Server

let private exampleGraph () =
    let builder = GraphBuilder()
    let x = builder.Param 3.0
    let y = builder.Param 4.0
    let sum = builder.Add(x, y)
    let prod = builder.Mul(x, y)
    builder.Build([| sum; prod |], [| 0; 1 |])

[<Fact>]
let ``evaluateOutputs computes graph outputs on CPU`` () =
    let graph = exampleGraph ()
    let outputs = CpuGraph.evaluateOutputs graph [| 3.0f; 4.0f |]

    Assert.Equal<float array>([| 7.0; 12.0 |], outputs)

[<Fact>]
let ``jacobianReverse computes reverse-mode jacobian for simple graph`` () =
    let graph = exampleGraph ()
    let jacobian = CpuGraph.jacobianReverse graph [| 3.0f; 4.0f |]

    Assert.Equal<float array>([| 1.0; 1.0; 4.0; 3.0 |], jacobian)
