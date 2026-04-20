import { tryFind } from "./fable_modules/fable-library-js.4.29.0/List.js";
import { ViewerPipeline_viewerModel } from "../core/Editor/ViewerPipeline.fs.js";
import { Editor_initState } from "../core/Editor/Editor.fs.js";
import { max, min } from "./fable_modules/fable-library-js.4.29.0/Double.js";
import { map, item } from "./fable_modules/fable-library-js.4.29.0/Array.js";
import { PromiseBuilder__TryFinally_7D49A2FD, PromiseBuilder__For_1565554B, PromiseBuilder__Delay_62FBFDE1, PromiseBuilder__Run_212F1D4B } from "./fable_modules/Fable.Promise.3.2.0/Promise.fs.js";
import { jacobianReverse } from "../core/Solve/CpuGraph.fs.js";
import { promise } from "./fable_modules/Fable.Promise.3.2.0/PromiseImpl.fs.js";
import { rangeDouble } from "./fable_modules/fable-library-js.4.29.0/Range.js";
import { GpuSolver_createGpuSolver } from "../core/Solve/GpuSolver.fs.js";
import { some } from "./fable_modules/fable-library-js.4.29.0/Option.js";

function getSeedSketchGraph() {
    const matchValue = tryFind((sketch) => (sketch.Id === "sketch1"), ViewerPipeline_viewerModel(Editor_initState()).Sketches);
    if (matchValue == null) {
        throw new Error("default model is missing sketch1");
    }
    else {
        return matchValue.Graph;
    }
}

function maxAbsJacobianDiff(cpuJac, gpuJac) {
    const count = min(cpuJac.length, gpuJac.length) | 0;
    let maxDiff = 0;
    for (let i = 0; i <= (count - 1); i++) {
        const diff = Math.abs(item(i, cpuJac) - item(i, gpuJac));
        if (diff > maxDiff) {
            maxDiff = diff;
        }
    }
    return maxDiff;
}

export function benchmarkSeedSketchJacobian(iterations) {
    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => {
        const iterations_1 = max(1, iterations) | 0;
        const graph = getSeedSketchGraph();
        const paramValues = map((value) => value, graph.Params, Float32Array);
        const _cpuWarmup = jacobianReverse(graph, paramValues);
        const cpuStart = performance.now();
        let cpuJac = new Float64Array([]);
        return PromiseBuilder__For_1565554B(promise, rangeDouble(1, 1, iterations_1), (_arg) => {
            cpuJac = jacobianReverse(graph, paramValues);
            return Promise.resolve();
        }).then(() => PromiseBuilder__Delay_62FBFDE1(promise, () => {
            const cpuElapsed = (performance.now()) - cpuStart;
            return GpuSolver_createGpuSolver(graph, 1).then((_arg_1) => {
                const solver = _arg_1;
                return PromiseBuilder__TryFinally_7D49A2FD(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => (solver.Evaluate(paramValues, 1).then((_arg_2) => {
                    _arg_2.Jac;
                    const gpuStart = performance.now();
                    let gpuJac = new Float32Array([]);
                    return PromiseBuilder__For_1565554B(promise, rangeDouble(1, 1, iterations_1), (_arg_3) => (solver.Evaluate(paramValues, 1).then((_arg_4) => {
                        gpuJac = _arg_4.Jac;
                        return Promise.resolve();
                    }))).then(() => PromiseBuilder__Delay_62FBFDE1(promise, () => {
                        const gpuElapsed = (performance.now()) - gpuStart;
                        const result = {
                            cpuMs: cpuElapsed,
                            cpuPerIterMs: cpuElapsed / iterations_1,
                            gpuMs: gpuElapsed,
                            gpuPerIterMs: gpuElapsed / iterations_1,
                            iterations: iterations_1,
                            maxJacobianDiff: maxAbsJacobianDiff(cpuJac, gpuJac),
                            residuals: graph.Outputs.length,
                            sketchId: "sketch1",
                            vars: graph.VarSlots.length,
                        };
                        console.log(some("[benchmark] seed sketch jacobian"), result);
                        return Promise.resolve(result);
                    }));
                }))), () => {
                    solver.Destroy();
                });
            });
        }));
    }));
}

export function installGlobals() {
    globalThis.__pointerBenchmarkSeedSketchJacobian = (benchmarkSeedSketchJacobian);
}

