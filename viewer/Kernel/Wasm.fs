module Kernel.Wasm

// Thin bindings over the Zig kernel's WASM exports. Not much logic here —
// this module only loads the module and exposes its exported functions
// via a typed record.

open Fable.Core
open Fable.Core.JsInterop

[<Emit("new URL($0, import.meta.url)")>]
let private urlRelative (path: string) : obj = jsNative

[<Emit("fetch($0)")>]
let private fetch (url: obj) : JS.Promise<obj> = jsNative

[<Emit("WebAssembly.instantiateStreaming($0, {})")>]
let private instantiateStreaming (response: obj) : JS.Promise<obj> = jsNative

[<Emit("WebAssembly.compileStreaming($0)")>]
let private compileStreaming (response: obj) : JS.Promise<obj> = jsNative

[<Emit("WebAssembly.instantiate($0, {})")>]
let private instantiateModule (m: obj) : JS.Promise<obj> = jsNative

[<Emit("new Uint8Array($0, $1, $2)")>]
let private uint8View (buffer: obj) (offset: int) (length: int) : obj = jsNative

[<Emit("new Float32Array($0, $1, $2)")>]
let private f32View (buffer: obj) (offset: int) (length: int) : obj = jsNative

[<Emit("new Uint32Array($0, $1, $2)")>]
let private u32View (buffer: obj) (offset: int) (length: int) : obj = jsNative

[<Emit("Array.from($0)")>]
let private arrayFrom<'T> (x: obj) : 'T[] = jsNative

[<Emit("$0.set($1)")>]
let private copyInto (dst: obj) (src: obj) : unit = jsNative

/// Typed shape of the kernel's WASM `instance.exports`. Keep in sync with
/// `kernel/src/main.zig`.
type Exports =
    abstract memory: obj with get
    abstract ir_upload_buffer_ptr: unit -> int
    abstract ir_upload: byteLen: int -> int
    abstract camera_buffer_ptr: unit -> int
    abstract set_camera: unit -> int
    abstract gbuffer_ptr: unit -> int
    abstract palette_buffer_ptr: unit -> int
    abstract mesh_build: halfExtent: float * maxDepth: int -> int
    abstract mesh_build_auto: maxDepth: int -> int
    abstract mesh_vertices_ptr: unit -> int
    abstract mesh_vertices_len: unit -> int
    abstract mesh_triangles_ptr: unit -> int
    abstract mesh_triangles_len: unit -> int
    abstract render_voxels:
        tile_width: int * tile_height: int *
        full_width: int * full_height: int *
        tile_x: int * tile_y: int *
        view_half_w: float * view_half_h: float *
        half: float * level: int -> int
    abstract max_voxel_width: unit -> int
    abstract max_voxel_height: unit -> int
    abstract max_render_level: unit -> int

/// Load the kernel's WASM from the given URL (typically `/kernel/viewer.wasm`).
let load (url: string) : JS.Promise<Exports> =
    promise {
        let! response = fetch (urlRelative url)
        let! result = instantiateStreaming response
        let instance : obj = result?instance
        return unbox instance?exports
    }

/// Fetch + compile the WASM module once. The resulting `WebAssembly.Module`
/// is structured-cloneable — post it to workers so they can skip the
/// fetch + compile round-trip and just instantiate. Makes worker respawn
/// a few ms instead of ~30 ms.
let compile (url: string) : JS.Promise<obj> =
    promise {
        let! response = fetch (urlRelative url)
        let! m = compileStreaming response
        return m
    }

/// Instantiate a pre-compiled module. Async because instantiation can
/// still take a few ms on large modules, but no fetch or compile.
let instantiate (m: obj) : JS.Promise<Exports> =
    promise {
        let! inst = instantiateModule m
        return unbox inst?exports
    }

/// Write an IR blob (produced by `IrCodec.serialize`) into the kernel's
/// upload buffer and invoke `ir_upload`. Returns the kernel's status code.
let uploadIr (x: Exports) (ir: obj) : int =
    let len : int = ir?length
    let dst = uint8View x.memory?buffer (x.ir_upload_buffer_ptr ()) len
    copyInto dst ir
    x.ir_upload len

/// Write the 12 camera floats (eye, basis_x, basis_y, basis_z) into the
/// kernel's camera buffer and invoke `set_camera`.
let setCamera (x: Exports) (values: float32[]) : int =
    if values.Length <> 12 then
        failwithf "setCamera expects 12 floats, got %d" values.Length
    let dst = f32View x.memory?buffer (x.camera_buffer_ptr ()) 12
    copyInto dst values
    x.set_camera ()

/// View into the kernel's G-buffer output for a tile of `w × h` pixels —
/// 4 floats per pixel (nx, ny, nz, wcz). Same-memory view, no copy;
/// pass directly to `device.queue.writeTexture`.
let gbufferView (x: Exports) (w: int) (h: int) : obj =
    f32View x.memory?buffer (x.gbuffer_ptr ()) (w * h * 4)

/// View into the kernel's per-pixel palette idx buffer (one u32 per
/// tile pixel). Uploaded as an `r32uint` texture; `Background.wgsl`
/// uses it to pick the surface base color.
let paletteView (x: Exports) (w: int) (h: int) : obj =
    u32View x.memory?buffer (x.palette_buffer_ptr ()) (w * h)

/// Copy mesh vertex storage out of WASM as a flat float array:
/// `[x0; y0; z0; x1; y1; z1; ...]`.
let meshVertices (x: Exports) : float[] =
    let len = x.mesh_vertices_len ()
    if len <= 0 then [||]
    else
        f32View x.memory?buffer (x.mesh_vertices_ptr ()) len
        |> arrayFrom<float>

/// Copy mesh triangle storage out of WASM as flat indices:
/// `[a0; b0; c0; a1; b1; c1; ...]`.
let meshTriangles (x: Exports) : int[] =
    let len = x.mesh_triangles_len ()
    if len <= 0 then [||]
    else
        u32View x.memory?buffer (x.mesh_triangles_ptr ()) len
        |> arrayFrom<int>
