import type { Graph } from "./graph";
import type { GpuSolver } from "./gpu-solver";

export interface SolverConfig {
  maxIterations: number;
  residualTol: number;
  gradientTol: number;
  stepTol: number;
  lambdaInit: number;
  lambdaUp: number;
  lambdaDown: number;
}

export interface SolverPin {
  localSlot: number;
  varIndex: number;
  target: number;
  weight: number;
}

export const defaultSolverConfig: SolverConfig = {
  maxIterations: 24,
  residualTol: 1e-5,
  gradientTol: 1e-5,
  stepTol: 1e-6,
  lambdaInit: 1e-3,
  lambdaUp: 10,
  lambdaDown: 0.25,
};

function norm(v: Float64Array): number {
  let s = 0;
  for (let i = 0; i < v.length; i++) s += v[i] * v[i];
  return Math.sqrt(s);
}

function luSolveInPlace(A: Float64Array, b: Float64Array, n: number): boolean {
  for (let k = 0; k < n; k++) {
    let piv = k;
    let maxAbs = Math.abs(A[k * n + k]);
    for (let r = k + 1; r < n; r++) {
      const v = Math.abs(A[r * n + k]);
      if (v > maxAbs) { maxAbs = v; piv = r; }
    }
    if (maxAbs === 0) return false;
    if (piv !== k) {
      for (let c = 0; c < n; c++) {
        const t = A[k * n + c]; A[k * n + c] = A[piv * n + c]; A[piv * n + c] = t;
      }
      const tb = b[k]; b[k] = b[piv]; b[piv] = tb;
    }
    const pivVal = A[k * n + k];
    for (let r = k + 1; r < n; r++) {
      const f = A[r * n + k] / pivVal;
      A[r * n + k] = f;
      for (let c = k + 1; c < n; c++) A[r * n + c] -= f * A[k * n + c];
      b[r] -= f * b[k];
    }
  }
  for (let i = n - 1; i >= 0; i--) {
    let s = b[i];
    for (let c = i + 1; c < n; c++) s -= A[i * n + c] * b[c];
    b[i] = s / A[i * n + i];
  }
  return true;
}

function normalEquations(J: Float64Array, r: Float64Array, lambda: number, nRes: number, nVar: number, out: Float64Array, jtrOut: Float64Array): void {
  out.fill(0);
  for (let i = 0; i < nVar; i++) {
    for (let j = 0; j < nVar; j++) {
      let s = 0;
      for (let k = 0; k < nRes; k++) s += J[k * nVar + i] * J[k * nVar + j];
      out[i * nVar + j] = s;
    }
    out[i * nVar + i] += lambda;
  }
  for (let i = 0; i < nVar; i++) {
    let s = 0;
    for (let k = 0; k < nRes; k++) s += J[k * nVar + i] * r[k];
    jtrOut[i] = s;
  }
}

export async function solveGraphWithGpu(
  graph: Graph,
  solver: GpuSolver,
  initialParams: Float32Array,
  pins: SolverPin[] = [],
  config: SolverConfig = defaultSolverConfig,
): Promise<Float32Array> {
  const nVar = graph.varSlots.length;
  const nRes = graph.outputs.length;
  const nExtra = pins.length;
  const nResTotal = nRes + nExtra;
  if (nVar === 0 || nResTotal === 0) return new Float32Array(initialParams);

  const params = new Float32Array(initialParams);
  const x = new Float64Array(nVar);
  for (let i = 0; i < nVar; i++) x[i] = params[graph.varSlots[i]];

  const r = new Float64Array(nResTotal);
  const rNew = new Float64Array(nResTotal);
  const A = new Float64Array(nVar * nVar);
  const jtr = new Float64Array(nVar);
  const negJtr = new Float64Array(nVar);
  const delta = new Float64Array(nVar);
  let lambda = config.lambdaInit;

  const fillPins = (dstR: Float64Array, offset: number, sourceParams: Float32Array) => {
    for (let i = 0; i < pins.length; i++) {
      const pin = pins[i];
      dstR[offset + i] = (sourceParams[pin.localSlot] - pin.target) * pin.weight;
    }
  };

  const buildJacobian = (rawJac: Float32Array) => {
    const J = new Float64Array(nResTotal * nVar);
    for (let row = 0; row < nRes; row++) {
      for (let col = 0; col < nVar; col++) {
        J[row * nVar + col] = rawJac[row * nVar + col];
      }
    }
    for (let i = 0; i < pins.length; i++) {
      const pin = pins[i];
      J[(nRes + i) * nVar + pin.varIndex] = pin.weight;
    }
    return J;
  };

  for (let iter = 0; iter < config.maxIterations; iter++) {
    for (let i = 0; i < nVar; i++) params[graph.varSlots[i]] = x[i];

    const { values, jac } = await solver.evaluate(params, 1);
    for (let i = 0; i < nRes; i++) r[i] = values[graph.outputs[i]];
    fillPins(r, nRes, params);
    const rNorm = norm(r);
    if (rNorm < config.residualTol) return params;

    const J = buildJacobian(jac);

    let gNorm2 = 0;
    for (let i = 0; i < nVar; i++) {
      let s = 0;
      for (let k = 0; k < nResTotal; k++) s += J[k * nVar + i] * r[k];
      gNorm2 += s * s;
    }
    if (Math.sqrt(gNorm2) < config.gradientTol) return params;

    const cost = r.reduce((s, v) => s + v * v, 0);
    while (true) {
      normalEquations(J, r, lambda, nResTotal, nVar, A, jtr);
      for (let i = 0; i < nVar; i++) negJtr[i] = -jtr[i];
      const aCopy = new Float64Array(A);
      const rhs = new Float64Array(negJtr);
      if (!luSolveInPlace(aCopy, rhs, nVar)) {
        lambda *= config.lambdaUp;
        if (lambda > 1e16) return params;
        continue;
      }
      for (let i = 0; i < nVar; i++) delta[i] = rhs[i];

      const trial = new Float32Array(params);
      for (let i = 0; i < nVar; i++) trial[graph.varSlots[i]] = x[i] + delta[i];
      const { values: valuesNew } = await solver.evaluate(trial, 1);
      for (let i = 0; i < nRes; i++) rNew[i] = valuesNew[graph.outputs[i]];
      fillPins(rNew, nRes, trial);
      const costNew = rNew.reduce((s, v) => s + v * v, 0);

      if (costNew < cost) {
        for (let i = 0; i < nVar; i++) x[i] += delta[i];
        params.set(trial);
        lambda = Math.max(lambda * config.lambdaDown, 1e-15);
        const dNorm = norm(delta);
        const xNorm = norm(x);
        if (dNorm < config.stepTol * (1 + xNorm)) return params;
        break;
      }

      lambda *= config.lambdaUp;
      if (lambda > 1e16) return params;
    }
  }

  for (let i = 0; i < nVar; i++) params[graph.varSlots[i]] = x[i];
  return params;
}
