module PointerMk18.Ui.AppStore

open Server
open PointerMk18.Ui
open Browser.Dom

// ---------------------------------------------------------------------------
// Singleton store for the app. Lives in its own module so both F# UI code
// and TS viewer-bridge.ts can import the same instance.
//
// ES module semantics guarantee the `store` binding is created exactly
// once (on first import) and every subsequent import sees the same value.
// ---------------------------------------------------------------------------

let mutable private solverCache : Map<string, string * IGpuSolver> = Map.empty

let private activeSketchGraphKeys (state: EditorState) =
    ViewerPipeline.viewerModel state
    |> fun model ->
        model.Sketches
        |> List.map (fun sketch -> sketch.Id, GpuGraph.graphKey sketch.Graph)
        |> Map.ofList

let private pruneSolverCache (state: EditorState) =
    let active = activeSketchGraphKeys state

    solverCache
    |> Map.iter (fun sketchId (cachedKey, solver) ->
        match Map.tryFind sketchId active with
        | Some activeKey when activeKey = cachedKey -> ()
        | _ -> solver.Destroy())

    solverCache <-
        solverCache
        |> Map.filter (fun sketchId (cachedKey, _) ->
            match Map.tryFind sketchId active with
            | Some activeKey when activeKey = cachedKey -> true
            | _ -> false)

let private ensureSolver (sketchId: string) (graphKey: string) (graph: Graph) =
    promise {
        match Map.tryFind sketchId solverCache with
        | Some(existingKey, solver) when existingKey = graphKey ->
            return solver
        | Some(_, solver) ->
            solver.Destroy()
            let! next = GpuSolver.createGpuSolver graph 1
            solverCache <- solverCache |> Map.add sketchId (graphKey, next)
            return next
        | None ->
            let! next = GpuSolver.createGpuSolver graph 1
            solverCache <- solverCache |> Map.add sketchId (graphKey, next)
            return next
    }

let private runEffect (store: Store.Store<EditorState, Message>) (effect: Effect) : unit =
    match effect with
    | RunSketchSolve drag ->
        promise {
            let state = store.State
            pruneSolverCache state
            let model = ViewerPipeline.viewerModel state

            match model.Sketches |> List.tryFind (fun sketch -> sketch.Id = drag.SketchId) with
            | Some sketch ->
                let graphKey = GpuGraph.graphKey sketch.Graph
                let binding = SketchSolve.binding state.Compiled.Slots sketch.Id sketch.Sketch sketch.Graph.VarSlots
                let! solver = ensureSolver sketch.Id graphKey sketch.Graph

                let initialLocal =
                    match Map.tryFind drag.SketchId state.SolvedSketchParams with
                    | Some solved -> Array.copy solved
                    | None -> sketch.Graph.Params |> Array.map float32

                let count = min binding.LocalToGlobal.Length initialLocal.Length

                for i in 0 .. count - 1 do
                    let globalSlot = binding.LocalToGlobal.[i]
                    initialLocal.[i] <- float32 state.Compiled.Slots.Values.[globalSlot]

                let pins = SketchSolve.buildPins drag.XField drag.YField drag.Target binding
                let! solved = GpuLmSolver.solveGraphWithGpu sketch.Graph solver initialLocal pins GpuLmSolver.defaultSolverConfig
                Store.dispatch store (ApplySketchSolveResult(drag, solved))
            | None ->
                ()
        }
        |> Promise.catchEnd (fun error -> console.error("RunSketchSolve failed", error))

let store = Store.create Editor.update runEffect (Editor.initState ())

Store.subscribe store (fun () -> pruneSolverCache store.State)
