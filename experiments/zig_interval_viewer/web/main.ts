import { lookAt, perspective, mul, type Mat4 } from "./mat4";
import { Mesher, type MeshResult } from "./mesher";

// ── UI plumbing ─────────────────────────────────────────────────────────
const canvas = document.getElementById("canvas") as HTMLCanvasElement;
const panel = document.getElementById("panel")!;
function status(msg: string) { panel.textContent = msg; }

// Camera state.
let azimuth = 0.7;
let elevation = 0.4;
let distance = 4.0;
let maxDepth = 7;

let dragging = false, lastX = 0, lastY = 0;
let rebuildTimer: number | undefined;

async function main() {
  if (!("gpu" in navigator)) {
    status("ERROR: WebGPU not available (navigator.gpu is undefined).");
    return;
  }
  status("starting worker…");

  // ── GPU setup ─────────────────────────────────────────────────────────
  const adapter = await navigator.gpu.requestAdapter();
  if (!adapter) { status("ERROR: requestAdapter returned null."); return; }
  const device = await adapter.requestDevice();

  const ctx = canvas.getContext("webgpu") as GPUCanvasContext;
  const format = navigator.gpu.getPreferredCanvasFormat();
  const dpr = window.devicePixelRatio || 1;
  function resize() {
    canvas.width = Math.max(1, Math.floor(window.innerWidth * dpr));
    canvas.height = Math.max(1, Math.floor(window.innerHeight * dpr));
    canvas.style.width = `${window.innerWidth}px`;
    canvas.style.height = `${window.innerHeight}px`;
  }
  resize();
  ctx.configure({ device, format, alphaMode: "opaque" });

  let depthTex = device.createTexture({
    size: { width: canvas.width, height: canvas.height, depthOrArrayLayers: 1 },
    format: "depth24plus",
    usage: GPUTextureUsage.RENDER_ATTACHMENT,
  });

  const wgsl = /* wgsl */ `
struct Uniforms {
    mvp: mat4x4<f32>,
    lightDir: vec3<f32>,
    _pad: f32,
};
@group(0) @binding(0) var<uniform> u: Uniforms;
struct VOut {
    @builtin(position) pos: vec4<f32>,
    @location(0) worldPos: vec3<f32>,
    @location(1) normal: vec3<f32>,
};
@vertex
fn vs(@location(0) pos: vec3<f32>, @location(1) normal: vec3<f32>) -> VOut {
    var o: VOut;
    o.pos = u.mvp * vec4<f32>(pos, 1.0);
    o.worldPos = pos;
    o.normal = normal;
    return o;
}
@fragment
fn fs(in: VOut) -> @location(0) vec4<f32> {
    let n = normalize(in.normal);
    let l = normalize(u.lightDir);
    let diffuse = max(dot(n, l), 0.0);
    let ambient = 0.25;
    let shade = ambient + diffuse * 0.75;
    let baseColor = in.worldPos * 0.35 + vec3<f32>(0.5);
    return vec4<f32>(baseColor * shade, 1.0);
}
`;
  const shader = device.createShaderModule({ code: wgsl });
  const pipeline = device.createRenderPipeline({
    layout: "auto",
    vertex: {
      module: shader, entryPoint: "vs",
      buffers: [{
        arrayStride: 6 * 4, stepMode: "vertex",
        attributes: [
          { shaderLocation: 0, offset: 0, format: "float32x3" },
          { shaderLocation: 1, offset: 12, format: "float32x3" },
        ],
      }],
    },
    fragment: { module: shader, entryPoint: "fs", targets: [{ format }] },
    primitive: { topology: "triangle-list", cullMode: "none" },
    depthStencil: { format: "depth24plus", depthWriteEnabled: true, depthCompare: "less" },
  });
  const uniformBuffer = device.createBuffer({
    size: 80, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
  });
  const bindGroup = device.createBindGroup({
    layout: pipeline.getBindGroupLayout(0),
    entries: [{ binding: 0, resource: { buffer: uniformBuffer } }],
  });

  let vertexBuffer: GPUBuffer | null = null;
  let vertexCount = 0;
  let lastMesh: MeshResult | null = null;

  // ── Mesher (runs the WASM in a Web Worker) ───────────────────────────
  const mesher = new Mesher({
    onMesh: (r) => {
      lastMesh = r;
      vertexCount = r.vertexCount;
      const bytes = Math.max(24, r.vertexCount * 6 * 4);
      if (vertexBuffer === null || vertexBuffer.size < bytes) {
        vertexBuffer?.destroy();
        vertexBuffer = device.createBuffer({
          size: Math.max(bytes, 1024 * 1024),
          usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST,
        });
      }
      if (r.vertexCount > 0) {
        // Cast: the buffer came from a transferred ArrayBuffer (not shared),
        // but the transferable type widens to ArrayBufferLike at the TS level.
        device.queue.writeBuffer(
          vertexBuffer,
          0,
          r.vertices.buffer as ArrayBuffer,
          r.vertices.byteOffset,
          r.vertices.byteLength,
        );
      }
    },
    onError: (e) => status(`worker error: ${e}`),
  });

  status("loading default scene…");
  await mesher.useDefaultScene();
  const nativeMaxDepth = await mesher.maxDepth();
  if (maxDepth > nativeMaxDepth) maxDepth = nativeMaxDepth;
  status("building initial mesh…");
  function currentHalf(): number { return distance * 0.75; }
  mesher.requestBuild({ half: currentHalf(), maxDepth });

  // ── Event handlers ───────────────────────────────────────────────────
  window.addEventListener("resize", () => {
    resize();
    ctx.configure({ device, format, alphaMode: "opaque" });
    depthTex.destroy();
    depthTex = device.createTexture({
      size: { width: canvas.width, height: canvas.height, depthOrArrayLayers: 1 },
      format: "depth24plus",
      usage: GPUTextureUsage.RENDER_ATTACHMENT,
    });
  });

  canvas.addEventListener("mousedown", (e) => {
    dragging = true; lastX = e.clientX; lastY = e.clientY;
  });
  window.addEventListener("mouseup", () => { dragging = false; });
  window.addEventListener("mousemove", (e) => {
    if (!dragging) return;
    azimuth -= (e.clientX - lastX) * 0.006;
    elevation = Math.max(-1.4, Math.min(1.4, elevation + (e.clientY - lastY) * 0.006));
    lastX = e.clientX; lastY = e.clientY;
  });
  canvas.addEventListener("wheel", (e) => {
    e.preventDefault();
    distance = Math.max(0.6, Math.min(30, distance * (1 + e.deltaY * 0.001)));
    if (rebuildTimer !== undefined) clearTimeout(rebuildTimer);
    rebuildTimer = setTimeout(() => {
      rebuildTimer = undefined;
      mesher.requestBuild({ half: currentHalf(), maxDepth });
    }, 150);
  }, { passive: false });

  window.addEventListener("keydown", (e) => {
    if (e.key === "[") {
      maxDepth = Math.max(1, maxDepth - 1);
      mesher.requestBuild({ half: currentHalf(), maxDepth });
    } else if (e.key === "]") {
      maxDepth = Math.min(nativeMaxDepth, maxDepth + 1);
      mesher.requestBuild({ half: currentHalf(), maxDepth });
    }
  });

  // ── Render loop ──────────────────────────────────────────────────────
  function fmt(n: number): string {
    if (n >= 1e6) return (n / 1e6).toFixed(2) + "M";
    if (n >= 1e3) return (n / 1e3).toFixed(1) + "k";
    return `${n}`;
  }

  function frame() {
    const w = canvas.width, h = canvas.height;
    const aspect = w / h;

    const cosE = Math.cos(elevation);
    const eye: [number, number, number] = [
      distance * Math.sin(azimuth) * cosE,
      distance * Math.sin(elevation),
      distance * Math.cos(azimuth) * cosE,
    ];
    const view = lookAt(eye, [0, 0, 0], [0, 1, 0]);
    const proj = perspective(Math.PI / 3, aspect, 0.05, 200);
    const mvp: Mat4 = mul(proj, view);
    const lx = 0.6, ly = 0.7, lz = 0.4;
    const lm = Math.hypot(lx, ly, lz);
    const uniformData = new Float32Array(20);
    uniformData.set(mvp, 0);
    uniformData[16] = lx / lm; uniformData[17] = ly / lm; uniformData[18] = lz / lm;
    device.queue.writeBuffer(uniformBuffer, 0, uniformData);

    const encoder = device.createCommandEncoder();
    const pass = encoder.beginRenderPass({
      colorAttachments: [{
        view: ctx.getCurrentTexture().createView(),
        clearValue: { r: 0.04, g: 0.04, b: 0.05, a: 1 },
        loadOp: "clear", storeOp: "store",
      }],
      depthStencilAttachment: {
        view: depthTex.createView(),
        depthClearValue: 1, depthLoadOp: "clear", depthStoreOp: "store",
      },
    });
    pass.setPipeline(pipeline);
    pass.setBindGroup(0, bindGroup);
    if (vertexBuffer && vertexCount > 0) {
      pass.setVertexBuffer(0, vertexBuffer);
      pass.draw(vertexCount);
    }
    pass.end();
    device.queue.submit([encoder.finish()]);

    if (lastMesh) {
      const s = lastMesh.stats;
      const rootSide = currentHalf() * 2;
      const finestTile = rootSide / Math.pow(2, maxDepth);
      const finestVoxelCount = Math.pow(8, maxDepth);
      const pruning = finestVoxelCount / Math.max(1, s.evalCount);
      const avgSimp = s.simplifyCalls > 0
        ? (s.totalSimplifiedOps / s.simplifyCalls).toFixed(1) : "—";
      status(
`depth ${maxDepth}   half ${currentHalf().toFixed(2)}   finest tile ${finestTile.toFixed(4)}
tape: ${s.originalTapeOps} ops   simplified → min ${s.minSimplifiedOps}  avg ${avgSimp}  max ${s.maxSimplifiedOps}  (${fmt(s.simplifyCalls)} calls)
octree: ${fmt(s.evalCount)} evals  (${pruning.toFixed(1)}× fewer than 8^${maxDepth}=${fmt(finestVoxelCount)})
leaves: ${fmt(s.leafCount)} amb.  ${fmt(s.leafInside)} in + ${fmt(s.leafOutside)} out pruned
mesh: ${fmt(s.trianglesEmitted)} triangles (${fmt(lastMesh.vertexCount)} verts)
build: ${lastMesh.buildMs.toFixed(1)} ms   (Web Worker)
camera: dist=${distance.toFixed(2)}  az=${azimuth.toFixed(2)}  el=${elevation.toFixed(2)}
drag to orbit · scroll to zoom · [ / ]  depth ${maxDepth}`
      );
    }
    requestAnimationFrame(frame);
  }
  requestAnimationFrame(frame);
}

main().catch((e) => status(`error: ${e}\n${e?.stack ?? ""}`));
