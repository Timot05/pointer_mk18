module Kernel.Worker

// Web Worker entry point. Each worker owns its own copy of the kernel
// WASM + IR. Main thread posts messages to dispatch render work; worker
// replies with transferable g-buffers. See `Background.fs` for the pool
// manager on the main thread.
//
// Protocol (main → worker):
//   { kind: "init" }                                  → load WASM
//   { kind: "ir", bytes: Uint8Array }                 → IR.upload
//   { kind: "camera", values: Float32Array }          → set_camera
//   { kind: "render", epoch, level, tileX/Y/W/H,
//                     fullW/H, viewHalfW/H, half }    → render_voxels + reply
//
// Protocol (worker → main):
//   { kind: "ready" }
//   { kind: "ir-done", code }
//   { kind: "rendered", epoch, level, tileX/Y/W/H, buffer }  // buffer transferable

open Fable.Core
open Fable.Core.JsInterop

[<Emit("self.onmessage = $0")>]
let private setOnMessage (h: obj -> unit) : unit = jsNative

[<Emit("self.postMessage($0, $1)")>]
let private postMessageTransfer (msg: obj) (transferables: obj[]) : unit = jsNative

[<Emit("self.postMessage($0)")>]
let private postMessagePlain (msg: obj) : unit = jsNative

/// Copy a Float32Array view (into the WASM heap) into a standalone
/// ArrayBuffer we can transfer without copying data again.
[<Emit("new Float32Array($0).buffer")>]
let private copyToArrayBuffer (view: obj) : obj = jsNative

let mutable private exports : Wasm.Exports option = None

let private handle (ev: obj) =
    let data : obj = ev?data
    let kind : string = data?kind
    match kind with
    | "init" ->
        Wasm.load "/kernel/viewer.wasm"
        |> Promise.iter (fun x ->
            exports <- Some x
            postMessagePlain {| kind = "ready" |})
    | "ir" ->
        match exports with
        | Some x ->
            let code = Wasm.uploadIr x (data?bytes)
            postMessagePlain {| kind = "ir-done"; code = code |}
        | None ->
            postMessagePlain {| kind = "error"; reason = "ir before init" |}
    | "camera" ->
        match exports with
        | Some x -> Wasm.setCamera x (data?values) |> ignore
        | None -> ()
    | "render" ->
        match exports with
        | Some x ->
            let epoch : int = data?epoch
            let level : int = data?level
            let tileX : int = data?tileX
            let tileY : int = data?tileY
            let tileW : int = data?tileW
            let tileH : int = data?tileH
            let fullW : int = data?fullW
            let fullH : int = data?fullH
            let vhw : float = data?viewHalfW
            let vhh : float = data?viewHalfH
            let half : float = data?half
            let written =
                x.render_voxels
                    (tileW, tileH, fullW, fullH, tileX, tileY,
                     vhw, vhh, half, level)
            let buffer =
                if written > 0 then copyToArrayBuffer (Wasm.gbufferView x tileW tileH)
                else box null
            let msg =
                {| kind = "rendered"
                   epoch = epoch
                   level = level
                   tileX = tileX
                   tileY = tileY
                   tileW = tileW
                   tileH = tileH
                   written = written
                   buffer = buffer |}
            if written > 0 then
                postMessageTransfer msg [| buffer |]
            else
                postMessagePlain msg
        | None ->
            postMessagePlain {| kind = "error"; reason = "render before init" |}
    | _ -> ()

setOnMessage handle
