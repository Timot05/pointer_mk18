import { Record } from "../../ui/fable_modules/fable-library-js.4.29.0/Types.js";
import { record_type, float64_type, int32_type } from "../../ui/fable_modules/fable-library-js.4.29.0/Reflection.js";
import { copyTo, map, copy, fill, setItem, item, sumBy } from "../../ui/fable_modules/fable-library-js.4.29.0/Array.js";
import { disposeSafe, getEnumerator } from "../../ui/fable_modules/fable-library-js.4.29.0/Util.js";
import { rangeDouble } from "../../ui/fable_modules/fable-library-js.4.29.0/Range.js";
import { length, iterateIndexed } from "../../ui/fable_modules/fable-library-js.4.29.0/List.js";
import { PromiseBuilder__For_1565554B, PromiseBuilder__While_2044D34, PromiseBuilder__Delay_62FBFDE1, PromiseBuilder__Run_212F1D4B } from "../../ui/fable_modules/Fable.Promise.3.2.0/Promise.fs.js";
import { promise } from "../../ui/fable_modules/Fable.Promise.3.2.0/PromiseImpl.fs.js";
import { max } from "../../ui/fable_modules/fable-library-js.4.29.0/Double.js";

export class SolverConfig extends Record {
    constructor(MaxIterations, ResidualTol, GradientTol, StepTol, LambdaInit, LambdaUp, LambdaDown) {
        super();
        this.MaxIterations = (MaxIterations | 0);
        this.ResidualTol = ResidualTol;
        this.GradientTol = GradientTol;
        this.StepTol = StepTol;
        this.LambdaInit = LambdaInit;
        this.LambdaUp = LambdaUp;
        this.LambdaDown = LambdaDown;
    }
}

export function SolverConfig_$reflection() {
    return record_type("Server.SolverConfig", [], SolverConfig, () => [["MaxIterations", int32_type], ["ResidualTol", float64_type], ["GradientTol", float64_type], ["StepTol", float64_type], ["LambdaInit", float64_type], ["LambdaUp", float64_type], ["LambdaDown", float64_type]]);
}

export class SolverPin extends Record {
    constructor(LocalSlot, VarIndex, Target, Weight) {
        super();
        this.LocalSlot = (LocalSlot | 0);
        this.VarIndex = (VarIndex | 0);
        this.Target = Target;
        this.Weight = Weight;
    }
}

export function SolverPin_$reflection() {
    return record_type("Server.SolverPin", [], SolverPin, () => [["LocalSlot", int32_type], ["VarIndex", int32_type], ["Target", float64_type], ["Weight", float64_type]]);
}

export const GpuLmSolver_defaultSolverConfig = new SolverConfig(24, 1E-05, 1E-05, 1E-06, 0.001, 10, 0.25);

export function GpuLmSolver_norm(values) {
    const value_1 = sumBy((value) => (value * value), values, {
        GetZero: () => 0,
        Add: (x, y) => (x + y),
    });
    return Math.sqrt(value_1);
}

export function GpuLmSolver_luSolveInPlace(matrix, rhs, n) {
    let singular = false;
    for (let k = 0; k <= (n - 1); k++) {
        let pivot = k;
        let maxAbs = Math.abs(item((k * n) + k, matrix));
        for (let r = k + 1; r <= (n - 1); r++) {
            const value = Math.abs(item((r * n) + k, matrix));
            if (value > maxAbs) {
                maxAbs = value;
                pivot = (r | 0);
            }
        }
        if (maxAbs === 0) {
            singular = true;
        }
        else if (pivot !== k) {
            for (let c = 0; c <= (n - 1); c++) {
                const temp = item((k * n) + c, matrix);
                setItem(matrix, (k * n) + c, item((pivot * n) + c, matrix));
                setItem(matrix, (pivot * n) + c, temp);
            }
            const rhsTemp = item(k, rhs);
            setItem(rhs, k, item(pivot, rhs));
            setItem(rhs, pivot, rhsTemp);
        }
        if (!singular) {
            const pivotValue = item((k * n) + k, matrix);
            for (let r_1 = k + 1; r_1 <= (n - 1); r_1++) {
                const factor = item((r_1 * n) + k, matrix) / pivotValue;
                setItem(matrix, (r_1 * n) + k, factor);
                for (let c_1 = k + 1; c_1 <= (n - 1); c_1++) {
                    setItem(matrix, (r_1 * n) + c_1, item((r_1 * n) + c_1, matrix) - (factor * item((k * n) + c_1, matrix)));
                }
                setItem(rhs, r_1, item(r_1, rhs) - (factor * item(k, rhs)));
            }
        }
    }
    if (singular) {
        return false;
    }
    else {
        const enumerator = getEnumerator(rangeDouble(n - 1, -1, 0));
        try {
            while (enumerator["System.Collections.IEnumerator.MoveNext"]()) {
                const i = enumerator["System.Collections.Generic.IEnumerator`1.get_Current"]() | 0;
                let sum = item(i, rhs);
                for (let c_2 = i + 1; c_2 <= (n - 1); c_2++) {
                    sum = (sum - (item((i * n) + c_2, matrix) * item(c_2, rhs)));
                }
                setItem(rhs, i, sum / item((i * n) + i, matrix));
            }
        }
        finally {
            disposeSafe(enumerator);
        }
        return true;
    }
}

export function GpuLmSolver_normalEquations(jacobian, residual, lambda, nRes, nVar, outMatrix, jtrOut) {
    fill(outMatrix, 0, outMatrix.length, 0);
    for (let i = 0; i <= (nVar - 1); i++) {
        for (let j = 0; j <= (nVar - 1); j++) {
            let sum = 0;
            for (let k = 0; k <= (nRes - 1); k++) {
                sum = (sum + (item((k * nVar) + i, jacobian) * item((k * nVar) + j, jacobian)));
            }
            setItem(outMatrix, (i * nVar) + j, sum);
        }
        setItem(outMatrix, (i * nVar) + i, item((i * nVar) + i, outMatrix) + lambda);
    }
    for (let i_1 = 0; i_1 <= (nVar - 1); i_1++) {
        let sum_1 = 0;
        for (let k_1 = 0; k_1 <= (nRes - 1); k_1++) {
            sum_1 = (sum_1 + (item((k_1 * nVar) + i_1, jacobian) * item(k_1, residual)));
        }
        setItem(jtrOut, i_1, sum_1);
    }
}

export function GpuLmSolver_fillPins(pins, destination, offset, sourceParams) {
    iterateIndexed((index, pin) => {
        setItem(destination, offset + index, (item(pin.LocalSlot, sourceParams) - pin.Target) * pin.Weight);
    }, pins);
}

export function GpuLmSolver_buildJacobian(pins, nRes, nVar, rawJac) {
    const jacobian = new Float64Array((nRes + length(pins)) * nVar);
    for (let row = 0; row <= (nRes - 1); row++) {
        for (let col = 0; col <= (nVar - 1); col++) {
            setItem(jacobian, (row * nVar) + col, item((row * nVar) + col, rawJac));
        }
    }
    iterateIndexed((index, pin) => {
        setItem(jacobian, ((nRes + index) * nVar) + pin.VarIndex, pin.Weight);
    }, pins);
    return jacobian;
}

export function GpuLmSolver_solveGraphWithGpu(graph, solver, initialParams, pins, config) {
    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => {
        const nVar = graph.VarSlots.length | 0;
        const nRes = graph.Outputs.length | 0;
        const nResTotal = (nRes + length(pins)) | 0;
        if ((nVar === 0) ? true : (nResTotal === 0)) {
            return Promise.resolve(copy(initialParams));
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
            return PromiseBuilder__While_2044D34(promise, () => (!finished && (iteration < config.MaxIterations)), PromiseBuilder__Delay_62FBFDE1(promise, () => (PromiseBuilder__For_1565554B(promise, rangeDouble(0, 1, nVar - 1), (_arg) => {
                const i = _arg | 0;
                setItem(paramValues, item(i, graph.VarSlots), item(i, x));
                return Promise.resolve();
            }).then(() => PromiseBuilder__Delay_62FBFDE1(promise, () => (solver.Evaluate(paramValues, 1).then((_arg_1) => {
                const eval$ = _arg_1;
                return PromiseBuilder__For_1565554B(promise, rangeDouble(0, 1, nRes - 1), (_arg_2) => {
                    const i_1 = _arg_2 | 0;
                    setItem(residual, i_1, item(item(i_1, graph.Outputs), eval$.Values));
                    return Promise.resolve();
                }).then(() => PromiseBuilder__Delay_62FBFDE1(promise, () => {
                    let jacobian, gradientNormSquared;
                    GpuLmSolver_fillPins(pins, residual, nRes, paramValues);
                    return ((GpuLmSolver_norm(residual) < config.ResidualTol) ? ((finished = true, Promise.resolve())) : ((jacobian = GpuLmSolver_buildJacobian(pins, nRes, nVar, eval$.Jac), (gradientNormSquared = 0, PromiseBuilder__For_1565554B(promise, rangeDouble(0, 1, nVar - 1), (_arg_3) => {
                        let sum = 0;
                        return PromiseBuilder__For_1565554B(promise, rangeDouble(0, 1, nResTotal - 1), (_arg_4) => {
                            const k = _arg_4 | 0;
                            sum = (sum + (item((k * nVar) + _arg_3, jacobian) * item(k, residual)));
                            return Promise.resolve();
                        }).then(() => PromiseBuilder__Delay_62FBFDE1(promise, () => {
                            gradientNormSquared = (gradientNormSquared + (sum * sum));
                            return Promise.resolve();
                        }));
                    }).then(() => PromiseBuilder__Delay_62FBFDE1(promise, () => {
                        if (Math.sqrt(gradientNormSquared) < config.GradientTol) {
                            finished = true;
                            return Promise.resolve();
                        }
                        else {
                            const cost = sumBy((value) => (value * value), residual, {
                                GetZero: () => 0,
                                Add: (x_1, y) => (x_1 + y),
                            });
                            let accepted = false;
                            return PromiseBuilder__While_2044D34(promise, () => (!accepted && !finished), PromiseBuilder__Delay_62FBFDE1(promise, () => {
                                GpuLmSolver_normalEquations(jacobian, residual, lambda, nResTotal, nVar, a, jtr);
                                return PromiseBuilder__For_1565554B(promise, rangeDouble(0, 1, nVar - 1), (_arg_5) => {
                                    const i_3 = _arg_5 | 0;
                                    setItem(negJtr, i_3, -item(i_3, jtr));
                                    return Promise.resolve();
                                }).then(() => PromiseBuilder__Delay_62FBFDE1(promise, () => {
                                    const aCopy = copy(a);
                                    const rhs = copy(negJtr);
                                    if (!GpuLmSolver_luSolveInPlace(aCopy, rhs, nVar)) {
                                        lambda = (lambda * config.LambdaUp);
                                        if (lambda > 10000000000000000) {
                                            finished = true;
                                            return Promise.resolve();
                                        }
                                        else {
                                            return Promise.resolve();
                                        }
                                    }
                                    else {
                                        copyTo(rhs, 0, delta, 0, nVar);
                                        const trial = copy(paramValues);
                                        return PromiseBuilder__For_1565554B(promise, rangeDouble(0, 1, nVar - 1), (_arg_6) => {
                                            const i_4 = _arg_6 | 0;
                                            setItem(trial, item(i_4, graph.VarSlots), item(i_4, x) + item(i_4, delta));
                                            return Promise.resolve();
                                        }).then(() => PromiseBuilder__Delay_62FBFDE1(promise, () => (solver.Evaluate(trial, 1).then((_arg_7) => (PromiseBuilder__For_1565554B(promise, rangeDouble(0, 1, nRes - 1), (_arg_8) => {
                                            const i_5 = _arg_8 | 0;
                                            setItem(residualNew, i_5, item(item(i_5, graph.Outputs), _arg_7.Values));
                                            return Promise.resolve();
                                        }).then(() => PromiseBuilder__Delay_62FBFDE1(promise, () => {
                                            GpuLmSolver_fillPins(pins, residualNew, nRes, trial);
                                            if (sumBy((value_1) => (value_1 * value_1), residualNew, {
                                                GetZero: () => 0,
                                                Add: (x_2, y_1) => (x_2 + y_1),
                                            }) < cost) {
                                                return PromiseBuilder__For_1565554B(promise, rangeDouble(0, 1, nVar - 1), (_arg_9) => {
                                                    const i_6 = _arg_9 | 0;
                                                    setItem(x, i_6, item(i_6, x) + item(i_6, delta));
                                                    return Promise.resolve();
                                                }).then(() => PromiseBuilder__Delay_62FBFDE1(promise, () => {
                                                    copyTo(trial, 0, paramValues, 0, paramValues.length);
                                                    lambda = max(lambda * config.LambdaDown, 1E-15);
                                                    const deltaNorm = GpuLmSolver_norm(delta);
                                                    const xNorm = GpuLmSolver_norm(x);
                                                    return ((deltaNorm < (config.StepTol * (1 + xNorm))) ? ((finished = true, Promise.resolve())) : (Promise.resolve())).then(() => PromiseBuilder__Delay_62FBFDE1(promise, () => {
                                                        accepted = true;
                                                        return Promise.resolve();
                                                    }));
                                                }));
                                            }
                                            else {
                                                lambda = (lambda * config.LambdaUp);
                                                if (lambda > 10000000000000000) {
                                                    finished = true;
                                                    return Promise.resolve();
                                                }
                                                else {
                                                    return Promise.resolve();
                                                }
                                            }
                                        })))))));
                                    }
                                }));
                            }));
                        }
                    })))))).then(() => PromiseBuilder__Delay_62FBFDE1(promise, () => {
                        iteration = ((iteration + 1) | 0);
                        return Promise.resolve();
                    }));
                }));
            }))))))).then(() => PromiseBuilder__Delay_62FBFDE1(promise, () => (PromiseBuilder__For_1565554B(promise, rangeDouble(0, 1, nVar - 1), (_arg_10) => {
                const i_7 = _arg_10 | 0;
                setItem(paramValues, item(i_7, graph.VarSlots), item(i_7, x));
                return Promise.resolve();
            }).then(() => PromiseBuilder__Delay_62FBFDE1(promise, () => (Promise.resolve(paramValues)))))));
        }
    }));
}

