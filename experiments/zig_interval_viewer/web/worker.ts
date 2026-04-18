// Web Worker that owns the WASM instance and services build requests.
//
// Protocol:
//   from main thread:  { type: "set-scene", bytes: ArrayBuffer }
//                       { type: "use-default-scene" }
//                       { type: "build", half: number, maxDepth: number }
//   to main thread:    { type: "scene-set", ok, code? }
//                       { type: "mesh", vertices: ArrayBuffer,
//                                       vertexCount, stats: ArrayBuffer,
//                                       buildMs }

type Exports = {
  memory: WebAssembly.Memory;
  vertex_buffer_ptr: () => number;
  vertex_buffer_capacity_floats: () => number;
  stats_ptr: () => number;
  stats_size: () => number;
  tape_upload_buffer_ptr: () => number;
  tape_upload_buffer_capacity: () => number;
  tape_upload: (byteLen: number) => number;
  use_default_scene: () => void;
  scene_tape_op_count: () => number;
  max_supported_depth: () => number;
  build_mesh: (half: number, maxDepth: number) => number;
};

let ex: Exports | null = null;
const STATS_BYTES = 11 * 4;

async function ensureReady(): Promise<Exports> {
  if (ex) return ex;
  const resp = await fetch(new URL("./viewer.wasm", import.meta.url));
  const { instance } = await WebAssembly.instantiateStreaming(resp, {});
  ex = instance.exports as unknown as Exports;
  return ex;
}

self.onmessage = async (e: MessageEvent) => {
  const m = e.data;
  const x = await ensureReady();

  switch (m.type) {
    case "set-scene": {
      const bytes: ArrayBuffer = m.bytes;
      const cap = x.tape_upload_buffer_capacity();
      if (bytes.byteLength > cap) {
        (self as any).postMessage({ type: "scene-set", ok: false, code: 99 });
        return;
      }
      const dst = new Uint8Array(x.memory.buffer, x.tape_upload_buffer_ptr(), bytes.byteLength);
      dst.set(new Uint8Array(bytes));
      const code = x.tape_upload(bytes.byteLength);
      (self as any).postMessage({ type: "scene-set", ok: code === 0, code });
      return;
    }
    case "use-default-scene": {
      x.use_default_scene();
      (self as any).postMessage({ type: "scene-set", ok: true, code: 0 });
      return;
    }
    case "max-depth": {
      (self as any).postMessage({ type: "max-depth", value: x.max_supported_depth() });
      return;
    }
    case "build": {
      const t0 = performance.now();
      const vc = x.build_mesh(m.half, m.maxDepth);
      const t1 = performance.now();

      // Copy vertex data + stats out of WASM memory into transferable
      // ArrayBuffers. (WASM memory can't be transferred directly.)
      const floats = vc * 6;
      const verts = new Float32Array(floats);
      if (floats > 0) {
        verts.set(new Float32Array(x.memory.buffer, x.vertex_buffer_ptr(), floats));
      }
      const stats = new Uint8Array(STATS_BYTES);
      stats.set(new Uint8Array(x.memory.buffer, x.stats_ptr(), STATS_BYTES));

      (self as any).postMessage(
        {
          type: "mesh",
          vertices: verts.buffer,
          vertexCount: vc,
          stats: stats.buffer,
          buildMs: t1 - t0,
        },
        [verts.buffer, stats.buffer],
      );
      return;
    }
  }
};
