namespace Server

module CpuGraph =

    let evaluateValues (graph: Graph) (paramValues: float32[]) : float[] =
        let values = Array.zeroCreate<float> graph.Nodes.Length

        graph.Nodes
        |> Array.iteri (fun index node ->
            let value =
                match node.Op with
                | Constant constant -> constant
                | Param slot -> float paramValues.[slot]
                | Neg -> -values.[node.Inputs.[0]]
                | Sin -> sin values.[node.Inputs.[0]]
                | Cos -> cos values.[node.Inputs.[0]]
                | Sqrt -> sqrt values.[node.Inputs.[0]]
                | Add -> values.[node.Inputs.[0]] + values.[node.Inputs.[1]]
                | Sub -> values.[node.Inputs.[0]] - values.[node.Inputs.[1]]
                | Mul -> values.[node.Inputs.[0]] * values.[node.Inputs.[1]]
                | Div -> values.[node.Inputs.[0]] / values.[node.Inputs.[1]]
                | Atan2 -> atan2 values.[node.Inputs.[0]] values.[node.Inputs.[1]]

            values.[index] <- value)

        values

    let evaluateOutputs (graph: Graph) (paramValues: float32[]) : float[] =
        let values = evaluateValues graph paramValues
        graph.Outputs |> Array.map (fun output -> values.[output])

    let jacobianReverse (graph: Graph) (paramValues: float32[]) : float[] =
        let values = evaluateValues graph paramValues
        let varSlotNodes = GpuGraph.slotToNodeIndex graph |> fun slotToNode -> graph.VarSlots |> Array.map (fun slot -> slotToNode.[slot])
        let nRes = graph.Outputs.Length
        let nVar = graph.VarSlots.Length
        let jacobian = Array.zeroCreate<float> (nRes * nVar)

        for residualIndex in 0 .. nRes - 1 do
            let adjoints = Array.zeroCreate<float> graph.Nodes.Length
            adjoints.[graph.Outputs.[residualIndex]] <- 1.0

            for reverseIndex in graph.Nodes.Length - 1 .. -1 .. 0 do
                let ai = adjoints.[reverseIndex]

                if ai <> 0.0 then
                    match graph.Nodes.[reverseIndex].Op with
                    | Constant _
                    | Param _ ->
                        ()
                    | Neg ->
                        let a = graph.Nodes.[reverseIndex].Inputs.[0]
                        adjoints.[a] <- adjoints.[a] - ai
                    | Sin ->
                        let a = graph.Nodes.[reverseIndex].Inputs.[0]
                        adjoints.[a] <- adjoints.[a] + ai * cos values.[a]
                    | Cos ->
                        let a = graph.Nodes.[reverseIndex].Inputs.[0]
                        adjoints.[a] <- adjoints.[a] - ai * sin values.[a]
                    | Sqrt ->
                        let a = graph.Nodes.[reverseIndex].Inputs.[0]
                        let v = values.[a]

                        if v > 0.0 then
                            adjoints.[a] <- adjoints.[a] + ai / (2.0 * sqrt v)
                    | Add ->
                        let a = graph.Nodes.[reverseIndex].Inputs.[0]
                        let b = graph.Nodes.[reverseIndex].Inputs.[1]
                        adjoints.[a] <- adjoints.[a] + ai
                        adjoints.[b] <- adjoints.[b] + ai
                    | Sub ->
                        let a = graph.Nodes.[reverseIndex].Inputs.[0]
                        let b = graph.Nodes.[reverseIndex].Inputs.[1]
                        adjoints.[a] <- adjoints.[a] + ai
                        adjoints.[b] <- adjoints.[b] - ai
                    | Mul ->
                        let a = graph.Nodes.[reverseIndex].Inputs.[0]
                        let b = graph.Nodes.[reverseIndex].Inputs.[1]
                        adjoints.[a] <- adjoints.[a] + ai * values.[b]
                        adjoints.[b] <- adjoints.[b] + ai * values.[a]
                    | Div ->
                        let a = graph.Nodes.[reverseIndex].Inputs.[0]
                        let b = graph.Nodes.[reverseIndex].Inputs.[1]
                        let bv = values.[b]
                        adjoints.[a] <- adjoints.[a] + ai / bv
                        adjoints.[b] <- adjoints.[b] - ai * values.[a] / (bv * bv)
                    | Atan2 ->
                        let y = graph.Nodes.[reverseIndex].Inputs.[0]
                        let x = graph.Nodes.[reverseIndex].Inputs.[1]
                        let yv = values.[y]
                        let xv = values.[x]
                        let denom = xv * xv + yv * yv

                        if denom > 0.0 then
                            adjoints.[y] <- adjoints.[y] + ai * xv / denom
                            adjoints.[x] <- adjoints.[x] - ai * yv / denom

            for varIndex in 0 .. nVar - 1 do
                jacobian.[residualIndex * nVar + varIndex] <- adjoints.[varSlotNodes.[varIndex]]

        jacobian
