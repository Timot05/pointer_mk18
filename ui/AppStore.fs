module PointerMk18.Ui.AppStore

open Fable.Core
open Fable.Core.JsInterop
open Server
open PointerMk18.Ui
open Browser.Dom

// Singleton store wired to the Editor reducer + effect handlers. Held in a
// module so every import sees the same instance.

let mutable private solveInFlight : Set<string> = Set.empty
let mutable private pendingSolveBySketch : Map<string, SketchDrag * bool> = Map.empty

[<Emit("performance.now()")>]
let private nowMs () : float = jsNative

let private logSlowSolve (sketchId: string) (usePins: bool) (elapsedMs: float) =
    let phase = if usePins then "live" else "final"
    console.log(
        sprintf
            "[drag-solve] sketch=%s phase=%s elapsed=%.1fms inFlight=%d queued=%d"
            sketchId phase elapsedMs solveInFlight.Count pendingSolveBySketch.Count)

let private solveSketch
    (state: EditorState)
    (sketchId: string)
    (sketch: ViewerSketchView)
    (usePins: bool)
    (dragOpt: SketchDrag option)
    =
    promise {
        let binding =
            SketchSolve.binding state.Compiled.Slots sketch.Id sketch.Sketch sketch.Graph.VarSlots

        let initialLocal =
            match Map.tryFind sketchId state.SolvedSketchParams with
            | Some solved -> Array.copy solved
            | None -> sketch.Graph.Params |> Array.map float32

        if usePins || not (Map.containsKey sketchId state.SolvedSketchParams) then
            let count = min binding.LocalToGlobal.Length initialLocal.Length
            for i in 0 .. count - 1 do
                let globalSlot = binding.LocalToGlobal.[i]
                initialLocal.[i] <- float32 state.SlotValues.[globalSlot]

        let pins =
            match usePins, dragOpt with
            | true, Some drag ->
                SketchSolve.buildPins 0.1 drag.XField drag.YField drag.Target binding
            | _ -> []

        let! solved =
            CpuLmSolver.solveGraphWithCpu
                sketch.Graph initialLocal pins GpuLmSolver.defaultSolverConfig
        return Some solved
    }

let rec private startSketchSolve (store: Store.Store<EditorState, Message>) (drag: SketchDrag) (usePins: bool) : unit =
    solveInFlight <- solveInFlight |> Set.add drag.SketchId

    promise {
        let t0 = nowMs ()
        try
            let state = store.State
            let model = ViewerPipeline.viewerModel state

            match model.Sketches |> List.tryFind (fun sketch -> sketch.Id = drag.SketchId) with
            | Some sketch ->
                let! solvedOpt = solveSketch state drag.SketchId sketch usePins (Some drag)
                match solvedOpt with
                | Some solved -> Store.dispatch store (ApplySketchSolveResult(drag, solved))
                | None -> ()
            | None -> ()
        with error ->
            console.error("RunSketchSolve failed", error)

        logSlowSolve drag.SketchId usePins (nowMs () - t0)
        completeSketchSolve store drag.SketchId
    }
    |> ignore

and private completeSketchSolve (store: Store.Store<EditorState, Message>) (sketchId: string) =
    solveInFlight <- solveInFlight |> Set.remove sketchId
    match Map.tryFind sketchId pendingSolveBySketch with
    | Some(next, usePins) ->
        pendingSolveBySketch <- pendingSolveBySketch |> Map.remove sketchId
        startSketchSolve store next usePins
    | None -> ()

let private resolveAllSketches (store: Store.Store<EditorState, Message>) : unit =
    promise {
        try
            let state = store.State
            let model = ViewerPipeline.viewerModel state
            for sketch in model.Sketches do
                let! solvedOpt = solveSketch state sketch.Id sketch false None
                match solvedOpt with
                | Some solved -> Store.dispatch store (ApplyResolvedSketchResult(sketch.Id, solved))
                | None -> ()
        with error ->
            console.error("ResolveAllSketches failed", error)
    }
    |> ignore

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
    | ResolveAllSketches ->
        resolveAllSketches store

let store = Store.create Editor.update runEffect (Editor.initState ())
