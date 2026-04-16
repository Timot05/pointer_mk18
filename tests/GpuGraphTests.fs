module GpuGraphTests

open Xunit
open Server

[<Fact>]
let ``slotToNodeIndex maps param slots to their node indices`` () =
    let graph =
        { Nodes =
            [| { Op = Constant 2.0; Inputs = [||] }
               { Op = Param 0; Inputs = [||] }
               { Op = Param 1; Inputs = [||] }
               { Op = Add; Inputs = [| 1; 2 |] } |]
          Params = [| 3.0; 4.0 |]
          Outputs = [| 3 |]
          VarSlots = [| 1; 0 |] }

    let slotToNode = GpuGraph.slotToNodeIndex graph

    Assert.Equal<int array>([| 1; 2 |], slotToNode)

[<Fact>]
let ``packGraph encodes opcodes constants and variable slot nodes`` () =
    let graph =
        { Nodes =
            [| { Op = Constant 2.5; Inputs = [||] }
               { Op = Param 0; Inputs = [||] }
               { Op = Param 1; Inputs = [||] }
               { Op = Mul; Inputs = [| 0; 1 |] }
               { Op = Add; Inputs = [| 3; 2 |] } |]
          Params = [| 7.0; 8.0 |]
          Outputs = [| 4 |]
          VarSlots = [| 1; 0 |] }

    let packed = GpuGraph.packGraph graph

    Assert.Equal<uint32 array>(
        [| 0u; 0u; 0u; 0u
           1u; 0u; 0u; 0u
           1u; 0u; 0u; 1u
           8u; 0u; 1u; 0u
           6u; 3u; 2u; 0u |],
        packed.PackedNodes
    )

    Assert.Equal<float32 array>([| 2.5f |], packed.Consts)
    Assert.Equal<uint32 array>([| 2u; 1u |], packed.VarSlotNodes)
