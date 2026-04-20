namespace Server

// Graph packing/runtime preparation for the GPU solver.
// This is the first half of the TS solver port: everything here is pure
// and testable without WebGPU.

type PackedGpuGraph =
    { PackedNodes: uint32[]
      Consts: float32[]
      VarSlotNodes: uint32[] }

module GpuGraph =

    let private opCode =
        function
        | Constant _ -> 0u
        | Param _ -> 1u
        | Neg -> 2u
        | Sin -> 3u
        | Cos -> 4u
        | Sqrt -> 5u
        | Add -> 6u
        | Sub -> 7u
        | Mul -> 8u
        | Div -> 9u
        | Atan2 -> 10u

    let slotToNodeIndex (graph: Graph) : int[] =
        let slotCount = graph.Params.Length
        let slotToNode = Array.zeroCreate slotCount

        graph.Nodes
        |> Array.iteri (fun nodeIndex node ->
            match node.Op with
            | Param slot -> slotToNode.[slot] <- nodeIndex
            | _ -> ())

        slotToNode

    let packGraph (graph: Graph) : PackedGpuGraph =
        let packed = Array.zeroCreate<uint32> (graph.Nodes.Length * 4)
        let consts = ResizeArray<float32>()

        graph.Nodes
        |> Array.iteri (fun index node ->
            let a = node.Inputs |> Array.tryItem 0 |> Option.defaultValue 0 |> uint32
            let b = node.Inputs |> Array.tryItem 1 |> Option.defaultValue 0 |> uint32

            let aux =
                match node.Op with
                | Constant value ->
                    let offset = uint32 consts.Count
                    consts.Add(float32 value)
                    offset
                | Param slot ->
                    uint32 slot
                | _ ->
                    0u

            packed.[index * 4] <- opCode node.Op
            packed.[index * 4 + 1] <- a
            packed.[index * 4 + 2] <- b
            packed.[index * 4 + 3] <- aux)

        let slotToNode = slotToNodeIndex graph
        let varSlotNodes = graph.VarSlots |> Array.map (fun slot -> uint32 slotToNode.[slot])

        { PackedNodes = packed
          Consts = consts.ToArray()
          VarSlotNodes = varSlotNodes }

    let graphKey (graph: Graph) =
        let packed = packGraph graph

        String.concat "|"
            [ string graph.Nodes.Length
              string graph.Params.Length
              string graph.Outputs.Length
              string graph.VarSlots.Length
              packed.PackedNodes |> Array.map string |> String.concat ","
              packed.Consts |> Array.map string |> String.concat ","
              graph.Outputs |> Array.map string |> String.concat ","
              graph.VarSlots |> Array.map string |> String.concat "," ]
