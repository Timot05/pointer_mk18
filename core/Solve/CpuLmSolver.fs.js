import { length } from "../../ui/fable_modules/fable-library-js.4.29.0/List.js";
import { copyTo, sumBy, setItem, item, map, copy } from "../../ui/fable_modules/fable-library-js.4.29.0/Array.js";
import { jacobianReverse, evaluateOutputs } from "./CpuGraph.fs.js";
import { GpuLmSolver_luSolveInPlace, GpuLmSolver_normalEquations, GpuLmSolver_buildJacobian, GpuLmSolver_norm, GpuLmSolver_fillPins } from "./GpuLmSolver.fs.js";
import { max } from "../../ui/fable_modules/fable-library-js.4.29.0/Double.js";
import { PromiseBuilder__Delay_62FBFDE1, PromiseBuilder__Run_212F1D4B } from "../../ui/fable_modules/Fable.Promise.3.2.0/Promise.fs.js";
import { promise } from "../../ui/fable_modules/Fable.Promise.3.2.0/PromiseImpl.fs.js";

export function solveGraphWithCpuSync(graph, initialParams, pins, config) {
    const nVar = graph.VarSlots.length | 0;
    const nRes = graph.Outputs.length | 0;
    const nResTotal = (nRes + length(pins)) | 0;
    if ((nVar === 0) ? true : (nResTotal === 0)) {
        return copy(initialParams);
    }
    else {
        const paramValues = copy(initialParams);
        const x = map((slot) => item(slot, paramValues), graph.VarSlots, Float64Array);
        const residual = new Float64Array(nResTotal);
        const residualNew = new Float64Array(nResTotal);
        const a = new Float64Array(nVar * nVar);
        const jtr = new Float64Array(nVar);
        const negJtr = new Float64Array(nVar);
        const delta = new Float64Array(nVar);
        let lambda = config.LambdaInit;
        let finished = false;
        let iteration = 0;
        while (!finished && (iteration < config.MaxIterations)) {
            for (let i = 0; i <= (nVar - 1); i++) {
                setItem(paramValues, item(i, graph.VarSlots), item(i, x));
            }
            const values = evaluateOutputs(graph, paramValues);
            for (let i_1 = 0; i_1 <= (nRes - 1); i_1++) {
                setItem(residual, i_1, item(i_1, values));
            }
            GpuLmSolver_fillPins(pins, residual, nRes, paramValues);
            if (GpuLmSolver_norm(residual) < config.ResidualTol) {
                finished = true;
            }
            else {
                const jacobian = GpuLmSolver_buildJacobian(pins, nRes, nVar, map((value) => value, jacobianReverse(graph, paramValues), Float32Array));
                let gradientNormSquared = 0;
                for (let i_2 = 0; i_2 <= (nVar - 1); i_2++) {
                    let sum = 0;
                    for (let k = 0; k <= (nResTotal - 1); k++) {
                        sum = (sum + (item((k * nVar) + i_2, jacobian) * item(k, residual)));
                    }
                    gradientNormSquared = (gradientNormSquared + (sum * sum));
                }
                if (Math.sqrt(gradientNormSquared) < config.GradientTol) {
                    finished = true;
                }
                else {
                    const cost = sumBy((value_1) => (value_1 * value_1), residual, {
                        GetZero: () => 0,
                        Add: (x_1, y) => (x_1 + y),
                    });
                    let accepted = false;
                    while (!accepted && !finished) {
                        GpuLmSolver_normalEquations(jacobian, residual, lambda, nResTotal, nVar, a, jtr);
                        for (let i_3 = 0; i_3 <= (nVar - 1); i_3++) {
                            setItem(negJtr, i_3, -item(i_3, jtr));
                        }
                        const aCopy = copy(a);
                        const rhs = copy(negJtr);
                        if (!GpuLmSolver_luSolveInPlace(aCopy, rhs, nVar)) {
                            lambda = (lambda * config.LambdaUp);
                            if (lambda > 10000000000000000) {
                                finished = true;
                            }
                        }
                        else {
                            copyTo(rhs, 0, delta, 0, nVar);
                            const trial = copy(paramValues);
                            for (let i_4 = 0; i_4 <= (nVar - 1); i_4++) {
                                setItem(trial, item(i_4, graph.VarSlots), item(i_4, x) + item(i_4, delta));
                            }
                            const trialValues = evaluateOutputs(graph, trial);
                            for (let i_5 = 0; i_5 <= (nRes - 1); i_5++) {
                                setItem(residualNew, i_5, item(i_5, trialValues));
                            }
                            GpuLmSolver_fillPins(pins, residualNew, nRes, trial);
                            if (sumBy((value_2) => (value_2 * value_2), residualNew, {
                                GetZero: () => 0,
                                Add: (x_2, y_1) => (x_2 + y_1),
                            }) < cost) {
                                for (let i_6 = 0; i_6 <= (nVar - 1); i_6++) {
                                    setItem(x, i_6, item(i_6, x) + item(i_6, delta));
                                }
                                copyTo(trial, 0, paramValues, 0, paramValues.length);
                                lambda = max(lambda * config.LambdaDown, 1E-15);
                                if (GpuLmSolver_norm(delta) < (config.StepTol * (1 + GpuLmSolver_norm(x)))) {
                                    finished = true;
                                }
                                accepted = true;
                            }
                            else {
                                lambda = (lambda * config.LambdaUp);
                                if (lambda > 10000000000000000) {
                                    finished = true;
                                }
                            }
                        }
                    }
                }
            }
            iteration = ((iteration + 1) | 0);
        }
        for (let i_7 = 0; i_7 <= (nVar - 1); i_7++) {
            setItem(paramValues, item(i_7, graph.VarSlots), item(i_7, x));
        }
        return paramValues;
    }
}

export function solveGraphWithCpu(graph, initialParams, pins, config) {
    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => (Promise.resolve(solveGraphWithCpuSync(graph, initialParams, pins, config)))));
}

