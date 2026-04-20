module Kernel.FieldsViewer

// Standalone voxel/field viewer powered by the Zig WASM kernel. Loads
// `/kernel/viewer.wasm`, uploads a field IR, runs a progressive render
// loop (coarse → fine voxel levels) and blits into a 2D canvas via
// ImageData. Orbit on pointer-drag, zoom on wheel.
//
// This is ported from the original TypeScript harness in
// `rendering_experiments/zig_interval_viewer/web/main.ts`. It's
// self-contained — no dependency on the WebGPU Viewer.

open Fable.Core
open Fable.Core.JsInterop
open Browser.Dom
open Browser.Types

// World-space half-extent of the voxel walker.
let private HALF = 3.0
// Visible view half at zoom=1.
let private BASE_VIEW_HALF = 1.25
// Coarsest level we ever start from (higher = less blocky first frame).
let private START_LEVEL = 3

// ── JS interop helpers ─────────────────────────────────────────────────

[<Emit("new URL($0, import.meta.url)")>]
let private urlRelative (path: string) : obj = jsNative

[<Emit("fetch($0)")>]
let private fetch (url: obj) : JS.Promise<obj> = jsNative

[<Emit("WebAssembly.instantiateStreaming($0, {})")>]
let private wasmInstantiate (response: obj) : JS.Promise<obj> = jsNative

[<Emit("new Uint8Array($0, $1, $2)")>]
let private uint8View (buffer: obj) (offset: int) (length: int) : obj = jsNative

[<Emit("new Uint8ClampedArray($0, $1, $2)")>]
let private uint8Clamped (buffer: obj) (offset: int) (length: int) : obj = jsNative

[<Emit("new Uint8ClampedArray($0)")>]
let private uint8ClampedFrom (source: obj) : obj = jsNative

[<Emit("$0.set($1)")>]
let private copyInto (dst: obj) (src: obj) : unit = jsNative

[<Emit("new ImageData($0, $1, $2)")>]
let private makeImageData (pixels: obj) (w: int) (h: int) : ImageData = jsNative

[<Emit("performance.now()")>]
let private nowMs () : float = jsNative

[<Emit("requestAnimationFrame($0)")>]
let private raf (cb: float -> unit) : int = jsNative

[<Emit("$0.addEventListener($1, $2, { passive: false })")>]
let private addEventPassiveFalse (target: obj) (name: string) (h: obj -> unit) : unit = jsNative

[<Emit("$0.addEventListener($1, $2)")>]
let private addEvent (target: obj) (name: string) (h: obj -> unit) : unit = jsNative

[<Emit("$0.preventDefault()")>]
let private preventDefault (e: obj) : unit = jsNative

[<Emit("$0.setPointerCapture($1)")>]
let private setPointerCapture (target: obj) (pointerId: int) : unit = jsNative

[<Emit("$0.releasePointerCapture($1)")>]
let private releasePointerCapture (target: obj) (pointerId: int) : unit = jsNative

// ── WASM exports (shape of `instance.exports`) ─────────────────────────

type private Exports =
    abstract memory: obj with get
    abstract ir_upload_buffer_ptr: unit -> int
    abstract ir_upload: byteLen: int -> int
    abstract set_camera:
        ex: float * ey: float * ez: float *
        bxx: float * bxy: float * bxz: float *
        byx: float * byy: float * byz: float *
        bzx: float * bzy: float * bzz: float -> int
    abstract pixel_buffer_ptr: unit -> int
    abstract render_voxels:
        w: int * h: int *
        viewHalfW: float * viewHalfH: float *
        half: float * level: int -> int
    abstract max_voxel_width: unit -> int
    abstract max_voxel_height: unit -> int
    abstract max_render_level: unit -> int

// ── Camera: orbit → basis matrix ───────────────────────────────────────

/// Build a camera frame from orbit params (eye at origin, basis rotated).
/// Returns 12 numbers: eye(3), basis_x(3), basis_y(3), basis_z(3).
let private orbitFrame (az: float) (el: float) : float[] =
    let ce, se = cos el, sin el
    let ca, sa = cos az, sin az
    let bzx, bzy, bzz = ce * sa, se, ce * ca
    // basis_x = normalize(cross(world_up, basis_z))
    // cross((0,1,0), (bzx, bzy, bzz)) = (bzz, 0, -bzx)
    let mutable bxx = bzz
    let mutable bxy = 0.0
    let mutable bxz = -bzx
    let bxLen = sqrt (bxx * bxx + bxy * bxy + bxz * bxz)
    if bxLen < 1e-6 then
        bxx <- 1.0
        bxy <- 0.0
        bxz <- 0.0
    else
        bxx <- bxx / bxLen
        bxz <- bxz / bxLen
    // basis_y = cross(basis_z, basis_x)
    let byx = bzy * bxz - bzz * bxy
    let byy = bzz * bxx - bzx * bxz
    let byz = bzx * bxy - bzy * bxx
    [| 0.0; 0.0; 0.0; bxx; bxy; bxz; byx; byy; byz; bzx; bzy; bzz |]

// ── Demo scene builder ─────────────────────────────────────────────────

let private buildDemoScene () : obj =
    let ir = IrCodec.create ()
    let outer = IrCodec.sphere ir 1.0
    let cut = IrCodec.translate ir 0.5 0.0 0.5 (IrCodec.sphere ir 0.6)
    let root = IrCodec.subtract ir outer cut
    IrCodec.serialize ir root

// ── Mount ──────────────────────────────────────────────────────────────

/// Mount the fields viewer onto `canvas` + `statsEl` (optional stats div).
/// Kicks off the WASM load + render loop. Returns a promise that resolves
/// once the first frame has been rendered.
let mount (canvas: HTMLCanvasElement) (statsEl: HTMLElement option) : JS.Promise<unit> =
    promise {
        let ctx : CanvasRenderingContext2D =
            unbox (canvas.getContext "2d")

        let! response = fetch (urlRelative "/kernel/viewer.wasm")
        let! result = wasmInstantiate response
        let instance : obj = result?instance
        let x : Exports = unbox instance?exports

        let maxW = x.max_voxel_width ()
        let maxH = x.max_voxel_height ()
        let maxLevel = x.max_render_level ()
        let dpr = window.devicePixelRatio
        let W = min maxW (int (window.innerWidth * dpr))
        let H = min maxH (int (window.innerHeight * dpr))
        canvas.width <- float W
        canvas.height <- float H
        canvas?style?width <- sprintf "%dpx" (int (float W / dpr))
        canvas?style?height <- sprintf "%dpx" (int (float H / dpr))

        // Upload the scene IR once.
        let ir = buildDemoScene ()
        let irLen : int = ir?length
        let buf : obj = x.memory?buffer
        let dst = uint8View buf (x.ir_upload_buffer_ptr ()) irLen
        copyInto dst ir
        let code = x.ir_upload irLen
        if code <> 0 then failwithf "ir_upload failed: code %d" code

        // Camera state.
        let mutable azimuth = 0.0
        let mutable elevation = 0.0
        let mutable zoom = 1.0

        let applyCamera () =
            let f = orbitFrame azimuth elevation
            x.set_camera
                (f.[0], f.[1], f.[2],
                 f.[3], f.[4], f.[5],
                 f.[6], f.[7], f.[8],
                 f.[9], f.[10], f.[11]) |> ignore
        applyCamera ()

        let viewHalves () =
            let aspect = float W / float H
            let baseHalf = BASE_VIEW_HALF / zoom
            if aspect >= 1.0 then baseHalf * aspect, baseHalf
            else baseHalf, baseHalf / aspect

        // Progressive refinement state.
        let mutable level = START_LEVEL
        let mutable dirty = true
        let mutable rafScheduled = false
        let lastLevelMs = Array.create (maxLevel + 1) nan

        let formatStats () =
            let lines = ResizeArray<string>()
            for i in START_LEVEL .. maxLevel do
                let t = lastLevelMs.[i]
                let shown =
                    if System.Double.IsNaN t then "  —  "
                    else sprintf "%6.1f ms" t
                let marker = if i = level - 1 then "▸" else " "
                lines.Add(sprintf "%s L%d %s" marker i shown)
            lines.Add(sprintf "  %d×%d" W H)
            System.String.Join("\n", lines)

        let rec step (_: float) =
            rafScheduled <- false
            if dirty then
                level <- START_LEVEL
                dirty <- false
            if level > maxLevel then () else
                let vhw, vhh = viewHalves ()
                let t0 = nowMs ()
                x.render_voxels (W, H, vhw, vhh, HALF, level) |> ignore
                let t1 = nowMs ()
                lastLevelMs.[level] <- t1 - t0

                let src = uint8Clamped (x.memory?buffer) (x.pixel_buffer_ptr ()) (W * H * 4)
                let img = makeImageData (uint8ClampedFrom src) W H
                ctx.putImageData(img, 0.0, 0.0)

                level <- level + 1
                match statsEl with
                | Some el -> el.textContent <- formatStats ()
                | None -> ()
                schedule ()
        and schedule () =
            if rafScheduled then () else
            if not dirty && level > maxLevel then () else
                rafScheduled <- true
                raf step |> ignore

        // ── Mouse drag to orbit ──────────────────────────────────────
        let mutable dragging = false
        let mutable lastX = 0.0
        let mutable lastY = 0.0
        addEvent canvas "pointerdown" (fun e ->
            dragging <- true
            lastX <- e?clientX
            lastY <- e?clientY
            setPointerCapture canvas (int (unbox<float> e?pointerId)))
        addEvent canvas "pointermove" (fun e ->
            if dragging then
                let cx : float = e?clientX
                let cy : float = e?clientY
                let dx = cx - lastX
                let dy = cy - lastY
                lastX <- cx
                lastY <- cy
                azimuth <- azimuth - dx * 0.01
                elevation <-
                    max (-System.Math.PI / 2.0 + 0.01)
                        (min (System.Math.PI / 2.0 - 0.01) (elevation + dy * 0.01))
                applyCamera ()
                dirty <- true
                schedule ())
        addEvent canvas "pointerup" (fun e ->
            dragging <- false
            releasePointerCapture canvas (int (unbox<float> e?pointerId)))
        addEvent canvas "pointercancel" (fun _ ->
            dragging <- false)

        // Wheel zoom.
        addEventPassiveFalse canvas "wheel" (fun e ->
            preventDefault e
            let dy : float = e?deltaY
            let factor = exp (-dy * 0.001)
            zoom <- max 0.1 (min 10.0 (zoom * factor))
            dirty <- true
            schedule ())

        schedule ()
    }
