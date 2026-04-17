module PointerMk18.Ui.AppStore

open Fable.Core
open Fable.Core.JsInterop
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

let mutable private solveInFlight : Set<string> = Set.empty
let mutable private pendingSolveBySketch : Map<string, SketchDrag * bool> = Map.empty

[<Emit("performance.now()")>]
let private nowMs () : float = jsNative

let private logSlowSolve (sketchId: string) (usePins: bool) (elapsedMs: float) =
    let phase = if usePins then "live" else "final"
    console.log(
        $"[drag-solve] sketch={sketchId} phase={phase} elapsed={elapsedMs:F1}ms inFlight={solveInFlight.Count} queued={pendingSolveBySketch.Count}"
    )

// pins would typically be the cursor position
let rec private startSketchSolve (store: Store.Store<EditorState, Message>) (drag: SketchDrag) (usePins: bool) : unit =
    solveInFlight <- solveInFlight |> Set.add drag.SketchId

    promise {
        let t0 = nowMs ()
        let mutable solverOpt : IGpuSolver option = None
        try
            let state = store.State
            let model = ViewerPipeline.viewerModel state

            match model.Sketches |> List.tryFind (fun sketch -> sketch.Id = drag.SketchId) with
            | Some sketch ->
                let binding = SketchSolve.binding state.Compiled.Slots sketch.Id sketch.Sketch sketch.Graph.VarSlots
                let! solver = GpuSolver.createGpuSolver sketch.Graph 1
                solverOpt <- Some solver

                let initialLocal =
                    match Map.tryFind drag.SketchId state.SolvedSketchParams with
                    | Some solved -> Array.copy solved
                    | None -> sketch.Graph.Params |> Array.map float32

                if usePins || not (Map.containsKey drag.SketchId state.SolvedSketchParams) then
                    let count = min binding.LocalToGlobal.Length initialLocal.Length

                    for i in 0 .. count - 1 do
                        let globalSlot = binding.LocalToGlobal.[i]
                        initialLocal.[i] <- float32 state.SlotValues.[globalSlot]

                let pins =
                    if usePins then
                        SketchSolve.buildPins 0.1 drag.XField drag.YField drag.Target binding
                    else
                        []
                let! solved = GpuLmSolver.solveGraphWithGpu sketch.Graph solver initialLocal pins GpuLmSolver.defaultSolverConfig
                Store.dispatch store (ApplySketchSolveResult(drag, solved))
            | None ->
                ()
        with error ->
            console.error("RunSketchSolve failed", error)
        match solverOpt with
        | Some solver -> solver.Destroy()
        | None -> ()

        let elapsed = nowMs () - t0
        logSlowSolve drag.SketchId usePins elapsed
        completeSketchSolve store drag.SketchId
    }
    |> ignore

and private completeSketchSolve (store: Store.Store<EditorState, Message>) (sketchId: string) =
    solveInFlight <- solveInFlight |> Set.remove sketchId

    match Map.tryFind sketchId pendingSolveBySketch with
    | Some(next, usePins) ->
        pendingSolveBySketch <- pendingSolveBySketch |> Map.remove sketchId
        startSketchSolve store next usePins
    | None ->
        ()

let private runEffect (store: Store.Store<EditorState, Message>) (effect: Effect) : unit =
    match effect with
    | RunSketchSolve drag ->
        if Set.contains drag.SketchId solveInFlight then
            pendingSolveBySketch <- pendingSolveBySketch |> Map.add drag.SketchId (drag, true)
        else
            startSketchSolve store drag true
    | FinalizeSketchDrag drag ->
        if Set.contains drag.SketchId solveInFlight then
            pendingSolveBySketch <- pendingSolveBySketch |> Map.add drag.SketchId (drag, false)
        else
            startSketchSolve store drag false

let store = Store.create Editor.update runEffect (Editor.initState ())
