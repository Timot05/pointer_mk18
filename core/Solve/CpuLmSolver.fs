namespace Server

module CpuLmSolver =

    let solveGraphWithCpuSync
        (graph: Graph)
        (initialParams: float32[])
        (pins: SolverPin list)
        (config: SolverConfig)
        =
        let nVar = graph.VarSlots.Length
        let nRes = graph.Outputs.Length
        let nExtra = pins.Length
        let nResTotal = nRes + nExtra

        if nVar = 0 || nResTotal = 0 then
            Array.copy initialParams
        else
            let paramValues = Array.copy initialParams
            let x = graph.VarSlots |> Array.map (fun slot -> float paramValues.[slot])
            let residual = Array.zeroCreate<float> nResTotal
            let residualNew = Array.zeroCreate<float> nResTotal
            let a = Array.zeroCreate<float> (nVar * nVar)
            let jtr = Array.zeroCreate<float> nVar
            let negJtr = Array.zeroCreate<float> nVar
            let delta = Array.zeroCreate<float> nVar
            let mutable lambda = config.LambdaInit
            let mutable finished = false
            let mutable iteration = 0

            while not finished && iteration < config.MaxIterations do
                for i in 0 .. nVar - 1 do
                    paramValues.[graph.VarSlots.[i]] <- float32 x.[i]

                let values = CpuGraph.evaluateOutputs graph paramValues

                for i in 0 .. nRes - 1 do
                    residual.[i] <- values.[i]

                GpuLmSolver.fillPins pins residual nRes paramValues

                if GpuLmSolver.norm residual < config.ResidualTol then
                    finished <- true
                else
                    let rawJac =
                        CpuGraph.jacobianReverse graph paramValues
                        |> Array.map float32

                    let jacobian = GpuLmSolver.buildJacobian pins nRes nVar rawJac

                    let mutable gradientNormSquared = 0.0

                    for i in 0 .. nVar - 1 do
                        let mutable sum = 0.0

                        for k in 0 .. nResTotal - 1 do
                            sum <- sum + jacobian.[k * nVar + i] * residual.[k]

                        gradientNormSquared <- gradientNormSquared + sum * sum

                    if sqrt gradientNormSquared < config.GradientTol then
                        finished <- true
                    else
                        let cost = residual |> Array.sumBy (fun value -> value * value)
                        let mutable accepted = false

                        while not accepted && not finished do
                            GpuLmSolver.normalEquations jacobian residual lambda nResTotal nVar a jtr

                            for i in 0 .. nVar - 1 do
                                negJtr.[i] <- -jtr.[i]

                            let aCopy = Array.copy a
                            let rhs = Array.copy negJtr

                            if not (GpuLmSolver.luSolveInPlace aCopy rhs nVar) then
                                lambda <- lambda * config.LambdaUp

                                if lambda > 1e16 then
                                    finished <- true
                            else
                                Array.blit rhs 0 delta 0 nVar
                                let trial = Array.copy paramValues

                                for i in 0 .. nVar - 1 do
                                    trial.[graph.VarSlots.[i]] <- float32 (x.[i] + delta.[i])

                                let trialValues = CpuGraph.evaluateOutputs graph trial

                                for i in 0 .. nRes - 1 do
                                    residualNew.[i] <- trialValues.[i]

                                GpuLmSolver.fillPins pins residualNew nRes trial

                                let costNew = residualNew |> Array.sumBy (fun value -> value * value)

                                if costNew < cost then
                                    for i in 0 .. nVar - 1 do
                                        x.[i] <- x.[i] + delta.[i]

                                    Array.blit trial 0 paramValues 0 paramValues.Length
                                    lambda <- max (lambda * config.LambdaDown) 1e-15
                                    let deltaNorm = GpuLmSolver.norm delta
                                    let xNorm = GpuLmSolver.norm x

                                    if deltaNorm < config.StepTol * (1.0 + xNorm) then
                                        finished <- true

                                    accepted <- true
                                else
                                    lambda <- lambda * config.LambdaUp

                                    if lambda > 1e16 then
                                        finished <- true

                iteration <- iteration + 1

            for i in 0 .. nVar - 1 do
                paramValues.[graph.VarSlots.[i]] <- float32 x.[i]

            paramValues

    let solveGraphWithCpu
        (graph: Graph)
        (initialParams: float32[])
        (pins: SolverPin list)
        (config: SolverConfig)
        =
        promise { return solveGraphWithCpuSync graph initialParams pins config }
