// Main-thread facade for the meshing worker.
//
// Request coalescing: while a build is in flight, the latest build request
// overwrites any queued one. Only one build runs at a time, and the
// freshest parameters always win. This matches the F# reference viewer's
// `pendingRebuild` pattern and prevents a backlog from rapid camera moves.

export type Stats = {
  evalCount: number;
  leafCount: number;
  leafOutside: number;
  leafInside: number;
  leafAmbiguous: number;
  trianglesEmitted: number;
  originalTapeOps: number;
  minSimplifiedOps: number;
  maxSimplifiedOps: number;
  totalSimplifiedOps: number;
  simplifyCalls: number;
};

function readStats(buf: ArrayBuffer): Stats {
  const u = new Uint32Array(buf);
  return {
    evalCount: u[0],
    leafCount: u[1],
    leafOutside: u[2],
    leafInside: u[3],
    leafAmbiguous: u[4],
    trianglesEmitted: u[5],
    originalTapeOps: u[6],
    minSimplifiedOps: u[7],
    maxSimplifiedOps: u[8],
    totalSimplifiedOps: u[9],
    simplifyCalls: u[10],
  };
}

export type BuildParams = { half: number; maxDepth: number };

export type MeshResult = {
  vertices: Float32Array;   // interleaved (px py pz nx ny nz) per vertex
  vertexCount: number;
  stats: Stats;
  buildMs: number;
};

export type MesherOptions = {
  onMesh: (r: MeshResult) => void;
  onError?: (e: unknown) => void;
};

export class Mesher {
  private worker: Worker;
  private pending: BuildParams | null = null;
  private inFlight = false;
  private pendingSceneResolve: ((ok: boolean) => void) | null = null;
  private pendingMaxDepth: ((v: number) => void) | null = null;
  private readonly onMesh: (r: MeshResult) => void;
  private readonly onError?: (e: unknown) => void;

  constructor(opts: MesherOptions) {
    this.onMesh = opts.onMesh;
    this.onError = opts.onError;
    this.worker = new Worker(new URL("./worker.ts", import.meta.url), { type: "module" });
    this.worker.onmessage = (e) => this.handle(e);
    this.worker.onerror = (e) => this.onError?.(e);
  }

  // Returns the hard upper bound the native code enforces on octree depth.
  maxDepth(): Promise<number> {
    return new Promise((resolve) => {
      this.pendingMaxDepth = resolve;
      this.worker.postMessage({ type: "max-depth" });
    });
  }

  // Upload a serialised tape (see tape_codec.ts for the format).
  setScene(bytes: ArrayBuffer): Promise<void> {
    return new Promise((resolve, reject) => {
      this.pendingSceneResolve = (ok) => {
        if (ok) resolve(); else reject(new Error("scene upload failed"));
      };
      this.worker.postMessage({ type: "set-scene", bytes }, [bytes]);
    });
  }

  // Load the Zig-side hardcoded demo scene (used by the viewer).
  useDefaultScene(): Promise<void> {
    return new Promise((resolve) => {
      this.pendingSceneResolve = () => resolve();
      this.worker.postMessage({ type: "use-default-scene" });
    });
  }

  // Queue a build. If a build is already in flight, this *replaces* any
  // earlier queued build so the worker never runs stale parameters.
  requestBuild(params: BuildParams): void {
    this.pending = params;
    this.flush();
  }

  dispose(): void {
    this.worker.terminate();
  }

  private flush() {
    if (this.inFlight || !this.pending) return;
    const req = this.pending;
    this.pending = null;
    this.inFlight = true;
    this.worker.postMessage({ type: "build", half: req.half, maxDepth: req.maxDepth });
  }

  private handle(e: MessageEvent) {
    const m = e.data;
    switch (m.type) {
      case "scene-set":
        this.pendingSceneResolve?.(m.ok === true);
        this.pendingSceneResolve = null;
        return;
      case "max-depth":
        this.pendingMaxDepth?.(m.value as number);
        this.pendingMaxDepth = null;
        return;
      case "mesh":
        this.inFlight = false;
        try {
          this.onMesh({
            vertices: new Float32Array(m.vertices),
            vertexCount: m.vertexCount,
            stats: readStats(m.stats),
            buildMs: m.buildMs,
          });
        } catch (err) {
          this.onError?.(err);
        }
        this.flush();
        return;
    }
  }
}
