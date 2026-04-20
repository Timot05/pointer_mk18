import { contains, remove, add, FSharpSet__get_Count, empty } from "./fable_modules/fable-library-js.4.29.0/Set.js";
import { comparePrimitives } from "./fable_modules/fable-library-js.4.29.0/Util.js";
import { add as add_1, remove as remove_1, containsKey, tryFind, FSharpMap__get_Count, empty as empty_1 } from "./fable_modules/fable-library-js.4.29.0/Map.js";
import { printf, toText } from "./fable_modules/fable-library-js.4.29.0/String.js";
import { some } from "./fable_modules/fable-library-js.4.29.0/Option.js";
import { PromiseBuilder__For_1565554B, PromiseBuilder__Delay_62FBFDE1, PromiseBuilder__Run_212F1D4B } from "./fable_modules/Fable.Promise.3.2.0/Promise.fs.js";
import { SketchSolve_buildPins, SketchSolve_binding } from "../core/Sketch/SketchSolve.fs.js";
import { setItem, item, copy, map } from "./fable_modules/fable-library-js.4.29.0/Array.js";
import { promise } from "./fable_modules/Fable.Promise.3.2.0/PromiseImpl.fs.js";
import { rangeDouble } from "./fable_modules/fable-library-js.4.29.0/Range.js";
import { min } from "./fable_modules/fable-library-js.4.29.0/Double.js";
import { tryFind as tryFind_1, empty as empty_2 } from "./fable_modules/fable-library-js.4.29.0/List.js";
import { solveGraphWithCpu } from "../core/Solve/CpuLmSolver.fs.js";
import { GpuLmSolver_defaultSolverConfig } from "../core/Solve/GpuLmSolver.fs.js";
import { ViewerPipeline_viewerModel } from "../core/Editor/ViewerPipeline.fs.js";
import { create, dispatch } from "./Store.fs.js";
import { Editor_initState, Editor_update, Message } from "../core/Editor/Editor.fs.js";

let solveInFlight = empty({
    Compare: comparePrimitives,
});

let pendingSolveBySketch = empty_1({
    Compare: comparePrimitives,
});

function logSlowSolve(sketchId, usePins, elapsedMs) {
    let arg_3, arg_4;
    const phase = usePins ? "live" : "final";
    console.log(some((arg_3 = (FSharpSet__get_Count(solveInFlight) | 0), (arg_4 = (FSharpMap__get_Count(pendingSolveBySketch) | 0), toText(printf("[drag-solve] sketch=%s phase=%s elapsed=%.1fms inFlight=%d queued=%d"))(sketchId)(phase)(elapsedMs)(arg_3)(arg_4)))));
}

function solveSketch(state, sketchId, sketch, usePins, dragOpt) {
    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => {
        const binding = SketchSolve_binding(state.Compiled.Slots, sketch.Id, sketch.Sketch, sketch.Graph.VarSlots);
        let initialLocal;
        const matchValue = tryFind(sketchId, state.SolvedSketchParams);
        initialLocal = ((matchValue == null) ? map((value) => value, sketch.Graph.Params, Float32Array) : copy(matchValue));
        return ((usePins ? true : !containsKey(sketchId, state.SolvedSketchParams)) ? PromiseBuilder__For_1565554B(promise, rangeDouble(0, 1, min(binding.LocalToGlobal.length, initialLocal.length) - 1), (_arg) => {
            const i = _arg | 0;
            const globalSlot = item(i, binding.LocalToGlobal) | 0;
            setItem(initialLocal, i, item(globalSlot, state.SlotValues));
            return Promise.resolve();
        }) : (Promise.resolve())).then(() => PromiseBuilder__Delay_62FBFDE1(promise, () => {
            let pins;
            let matchResult;
            if (usePins) {
                if (dragOpt != null) {
                    matchResult = 0;
                }
                else {
                    matchResult = 1;
                }
            }
            else {
                matchResult = 1;
            }
            switch (matchResult) {
                case 0: {
                    const drag = dragOpt;
                    pins = SketchSolve_buildPins(0.1, drag.XField, drag.YField, drag.Target, binding);
                    break;
                }
                default:
                    pins = empty_2();
            }
            return solveGraphWithCpu(sketch.Graph, initialLocal, pins, GpuLmSolver_defaultSolverConfig).then((_arg_1) => (Promise.resolve(_arg_1)));
        }));
    }));
}

function startSketchSolve(store_1, drag, usePins) {
    solveInFlight = add(drag.SketchId, solveInFlight);
    PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => {
        const t0 = performance.now();
        return (PromiseBuilder__Delay_62FBFDE1(promise, () => {
            const state = store_1.State;
            const matchValue = tryFind_1((sketch) => (sketch.Id === drag.SketchId), ViewerPipeline_viewerModel(state).Sketches);
            if (matchValue == null) {
                return Promise.resolve();
            }
            else {
                const sketch_1 = matchValue;
                return solveSketch(state, drag.SketchId, sketch_1, usePins, drag).then((_arg) => {
                    const solvedOpt = _arg;
                    if (solvedOpt == null) {
                        return Promise.resolve();
                    }
                    else {
                        dispatch(store_1, new Message(25, [drag, solvedOpt]));
                        return Promise.resolve();
                    }
                });
            }
        }).catch((_arg_1) => {
            console.error(some("RunSketchSolve failed"), _arg_1);
            return Promise.resolve();
        })).then(() => PromiseBuilder__Delay_62FBFDE1(promise, () => {
            logSlowSolve(drag.SketchId, usePins, (performance.now()) - t0);
            completeSketchSolve(store_1, drag.SketchId);
            return Promise.resolve();
        }));
    }));
}

function completeSketchSolve(store_1, sketchId) {
    solveInFlight = remove(sketchId, solveInFlight);
    const matchValue = tryFind(sketchId, pendingSolveBySketch);
    if (matchValue == null) {
    }
    else {
        const usePins = matchValue[1];
        const next = matchValue[0];
        pendingSolveBySketch = remove_1(sketchId, pendingSolveBySketch);
        startSketchSolve(store_1, next, usePins);
    }
}

function resolveAllSketches(store_1) {
    PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => (PromiseBuilder__Delay_62FBFDE1(promise, () => {
        const state = store_1.State;
        return PromiseBuilder__For_1565554B(promise, ViewerPipeline_viewerModel(state).Sketches, (_arg) => {
            const sketch = _arg;
            return solveSketch(state, sketch.Id, sketch, false, undefined).then((_arg_1) => {
                const solvedOpt = _arg_1;
                if (solvedOpt == null) {
                    return Promise.resolve();
                }
                else {
                    dispatch(store_1, new Message(26, [sketch.Id, solvedOpt]));
                    return Promise.resolve();
                }
            });
        });
    }).catch((_arg_2) => {
        console.error(some("ResolveAllSketches failed"), _arg_2);
        return Promise.resolve();
    }))));
}

function runEffect(store_1, effect) {
    switch (effect.tag) {
        case 1: {
            const drag_1 = effect.fields[0];
            if (contains(drag_1.SketchId, solveInFlight)) {
                pendingSolveBySketch = add_1(drag_1.SketchId, [drag_1, false], pendingSolveBySketch);
            }
            else {
                startSketchSolve(store_1, drag_1, false);
            }
            break;
        }
        case 2: {
            resolveAllSketches(store_1);
            break;
        }
        default: {
            const drag = effect.fields[0];
            if (contains(drag.SketchId, solveInFlight)) {
                pendingSolveBySketch = add_1(drag.SketchId, [drag, true], pendingSolveBySketch);
            }
            else {
                startSketchSolve(store_1, drag, true);
            }
        }
    }
}

export const store = create(Editor_update, (store_1, effect) => {
    runEffect(store_1, effect);
}, Editor_initState());

