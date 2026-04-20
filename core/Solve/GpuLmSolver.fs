namespace Server

type SolverConfig =
    { MaxIterations: int
      ResidualTol: float
      GradientTol: float
      StepTol: float
      LambdaInit: float
      LambdaUp: float
      LambdaDown: float }

type SolverPin =
    { LocalSlot: int
      VarIndex: int
      Target: float
      Weight: float }

module GpuLmSolver =

    let defaultSolverConfig =
        { MaxIterations = 24
          ResidualTol = 1e-5
          GradientTol = 1e-5
          StepTol = 1e-6
          LambdaInit = 1e-3
          LambdaUp = 10.0
          LambdaDown = 0.25 }

    let norm (values: float[]) =
        values |> Array.sumBy (fun value -> value * value) |> sqrt

    let luSolveInPlace (matrix: float[]) (rhs: float[]) (n: int) =
        let mutable singular = false

        for k in 0 .. n - 1 do
            let mutable pivot = k
            let mutable maxAbs = abs matrix.[k * n + k]

            for r in k + 1 .. n - 1 do
                let value = abs matrix.[r * n + k]

                if value > maxAbs then
                    maxAbs <- value
                    pivot <- r

            if maxAbs = 0.0 then
                singular <- true
            elif pivot <> k then
                for c in 0 .. n - 1 do
                    let temp = matrix.[k * n + c]
                    matrix.[k * n + c] <- matrix.[pivot * n + c]
                    matrix.[pivot * n + c] <- temp

                let rhsTemp = rhs.[k]
                rhs.[k] <- rhs.[pivot]
                rhs.[pivot] <- rhsTemp

            if not singular then
                let pivotValue = matrix.[k * n + k]

                for r in k + 1 .. n - 1 do
                    let factor = matrix.[r * n + k] / pivotValue
                    matrix.[r * n + k] <- factor

                    for c in k + 1 .. n - 1 do
                        matrix.[r * n + c] <- matrix.[r * n + c] - factor * matrix.[k * n + c]

                    rhs.[r] <- rhs.[r] - factor * rhs.[k]

        if singular then
            false
        else
            for i in n - 1 .. -1 .. 0 do
                let mutable sum = rhs.[i]

                for c in i + 1 .. n - 1 do
                    sum <- sum - matrix.[i * n + c] * rhs.[c]

                rhs.[i] <- sum / matrix.[i * n + i]

            true

    let normalEquations
        (jacobian: float[])
        (residual: float[])
        (lambda: float)
        (nRes: int)
        (nVar: int)
        (outMatrix: float[])
        (jtrOut: float[])
        =
        Array.fill outMatrix 0 outMatrix.Length 0.0

        for i in 0 .. nVar - 1 do
            for j in 0 .. nVar - 1 do
                let mutable sum = 0.0

                for k in 0 .. nRes - 1 do
                    sum <- sum + jacobian.[k * nVar + i] * jacobian.[k * nVar + j]

                outMatrix.[i * nVar + j] <- sum

            outMatrix.[i * nVar + i] <- outMatrix.[i * nVar + i] + lambda

        for i in 0 .. nVar - 1 do
            let mutable sum = 0.0

            for k in 0 .. nRes - 1 do
                sum <- sum + jacobian.[k * nVar + i] * residual.[k]

            jtrOut.[i] <- sum

    let fillPins (pins: SolverPin list) (destination: float[]) (offset: int) (sourceParams: float32[]) =
        pins
        |> List.iteri (fun index pin ->
            destination.[offset + index] <- (float sourceParams.[pin.LocalSlot] - pin.Target) * pin.Weight)

    let buildJacobian (pins: SolverPin list) (nRes: int) (nVar: int) (rawJac: float32[]) =
        let totalRows = nRes + pins.Length
        let jacobian = Array.zeroCreate<float> (totalRows * nVar)

        for row in 0 .. nRes - 1 do
            for col in 0 .. nVar - 1 do
                jacobian.[row * nVar + col] <- float rawJac.[row * nVar + col]

        pins
        |> List.iteri (fun index pin ->
            jacobian.[(nRes + index) * nVar + pin.VarIndex] <- pin.Weight)

        jacobian

    let solveGraphWithGpu
        (graph: Graph)
        (solver: IGpuSolver)
        (initialParams: float32[])
        (pins: SolverPin list)
        (config: SolverConfig)
        =
        promise {
            let nVar = graph.VarSlots.Length
            let nRes = graph.Outputs.Length
            let nExtra = pins.Length
            let nResTotal = nRes + nExtra

            if nVar = 0 || nResTotal = 0 then
                return Array.copy initialParams
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

                    let! eval = solver.Evaluate(paramValues, 1)

                    for i in 0 .. nRes - 1 do
                        residual.[i] <- float eval.Values.[graph.Outputs.[i]]

                    fillPins pins residual nRes paramValues

                    if norm residual < config.ResidualTol then
                        finished <- true
                    else
                        let jacobian = buildJacobian pins nRes nVar eval.Jac

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
                                normalEquations jacobian residual lambda nResTotal nVar a jtr

                                for i in 0 .. nVar - 1 do
                                    negJtr.[i] <- -jtr.[i]

                                let aCopy = Array.copy a
                                let rhs = Array.copy negJtr

                                if not (luSolveInPlace aCopy rhs nVar) then
                                    lambda <- lambda * config.LambdaUp

                                    if lambda > 1e16 then
                                        finished <- true
                                else
                                    Array.blit rhs 0 delta 0 nVar
                                    let trial = Array.copy paramValues

                                    for i in 0 .. nVar - 1 do
                                        trial.[graph.VarSlots.[i]] <- float32 (x.[i] + delta.[i])

                                    let! trialEval = solver.Evaluate(trial, 1)

                                    for i in 0 .. nRes - 1 do
                                        residualNew.[i] <- float trialEval.Values.[graph.Outputs.[i]]

                                    fillPins pins residualNew nRes trial

                                    let costNew = residualNew |> Array.sumBy (fun value -> value * value)

                                    if costNew < cost then
                                        for i in 0 .. nVar - 1 do
                                            x.[i] <- x.[i] + delta.[i]

                                        Array.blit trial 0 paramValues 0 paramValues.Length
                                        lambda <- max (lambda * config.LambdaDown) 1e-15
                                        let deltaNorm = norm delta
                                        let xNorm = norm x

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

                return paramValues
        }
