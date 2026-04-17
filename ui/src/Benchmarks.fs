module PointerMk18.Ui.Benchmarks

open Fable.Core
open Fable.Core.JsInterop
open Server

[<Emit("performance.now()")>]
let private nowMs () : float = jsNative

[<Emit("globalThis.__pointerBenchmarkSeedSketchJacobian = $0")>]
let private installSeedSketchBenchmarkGlobal (fn: int -> JS.Promise<obj>) : unit = jsNative

let private getSeedSketchGraph () =
    let state = Editor.initState ()
    let model = ViewerPipeline.viewerModel state

    match model.Sketches |> List.tryFind (fun sketch -> sketch.Id = "sketch1") with
    | Some sketch -> sketch.Graph
    | None -> failwith "default model is missing sketch1"

let private maxAbsJacobianDiff (cpuJac: float[]) (gpuJac: float32[]) =
    let count = min cpuJac.Length gpuJac.Length
    let mutable maxDiff = 0.0

    for i in 0 .. count - 1 do
        let diff = abs (cpuJac.[i] - float gpuJac.[i])
        if diff > maxDiff then
            maxDiff <- diff

    maxDiff

let benchmarkSeedSketchJacobian (iterations: int) : JS.Promise<obj> =
    promise {
        let iterations = max 1 iterations
        let graph = getSeedSketchGraph ()
        let paramValues = graph.Params |> Array.map float32

        let _cpuWarmup = CpuGraph.jacobianReverse graph paramValues

        let cpuStart = nowMs ()
        let mutable cpuJac = [||]

        for _ in 1 .. iterations do
            cpuJac <- CpuGraph.jacobianReverse graph paramValues

        let cpuElapsed = nowMs () - cpuStart

        let! solver = GpuSolver.createGpuSolver graph 1

        try
            let! warmup = solver.Evaluate(paramValues, 1)
            let _ = warmup.Jac

            let gpuStart = nowMs ()
            let mutable gpuJac = [||]

            for _ in 1 .. iterations do
                let! eval = solver.Evaluate(paramValues, 1)
                gpuJac <- eval.Jac

            let gpuElapsed = nowMs () - gpuStart
            let maxDiff = maxAbsJacobianDiff cpuJac gpuJac

            let result =
                box
                    {| sketchId = "sketch1"
                       iterations = iterations
                       residuals = graph.Outputs.Length
                       vars = graph.VarSlots.Length
                       cpuMs = cpuElapsed
                       cpuPerIterMs = cpuElapsed / float iterations
                       gpuMs = gpuElapsed
                       gpuPerIterMs = gpuElapsed / float iterations
                       maxJacobianDiff = maxDiff |}

            JS.console.log("[benchmark] seed sketch jacobian", result)
            return result
        finally
            solver.Destroy()
    }

let installGlobals () =
    installSeedSketchBenchmarkGlobal benchmarkSeedSketchJacobian
