module Viewer

// Viewer entry point. Mounts a canvas into the provided host element,
// builds a `Scene` (device + pipelines + atlas + camera + buffer pool),
// wires up input + render + dimension-editor, and kicks a RAF loop.

open Fable.Core
open Fable.Core.JsInterop
open Browser.Dom
open Browser.Types
open Server
open PointerMk18.Ui
open WebGPU

[<Emit("new ResizeObserver($0)")>]
let private makeResizeObserver (cb: obj -> unit) : obj = jsNative
[<Emit("$0.observe($1)")>]
let private observe (observer: obj) (target: obj) : unit = jsNative

let private mountCanvas (root: HTMLElement) : HTMLElement * HTMLCanvasElement =
    let shadow =
        if isNull root.shadowRoot then root?attachShadow({| mode = "open" |})
        else root.shadowRoot
    shadow?innerHTML <- ""

    let container = document.createElement "div"
    container?style?width <- "100%"
    container?style?height <- "100%"
    container?style?position <- "relative"
    container?style?background <- ViewerColors.PAGE_BG
    shadow?appendChild container |> ignore

    let canvas : HTMLCanvasElement = unbox (document.createElement "canvas")
    canvas?style?width <- "100%"
    canvas?style?height <- "100%"
    canvas?style?display <- "block"
    canvas?style?cursor <- "default"
    container.appendChild canvas |> ignore

    container, canvas

let mount (root: HTMLElement) : JS.Promise<obj> =
    promise {
        let container, canvas = mountCanvas root

        match WebGPU.gpu () with
        | None ->
            console.error "viewer: navigator.gpu missing"
            return box container
        | Some g ->
            let! adapter = g.requestAdapter()
            if isNull adapter then
                console.error "viewer: requestAdapter returned null"
                return box container
            else
                let! device = adapter.requestDevice()
                let! atlas = MsdfAtlas.loadAtlas device "/fonts/dekal.png"
                let! fontMetrics = MsdfAtlas.loadMetrics "/fonts/dekal.json"

                let ctx = WebGPU.getWebgpuContext canvas
                let format = g.getPreferredCanvasFormat()
                ctx.configure
                    { device = box device
                      format = format
                      alphaMode = "opaque" }

                let dpr = window.devicePixelRatio
                let scene = Scene.create device ctx canvas dpr format atlas fontMetrics

                // Resize: update canvas size + recreate depth/pick textures.
                let resize () =
                    let w = int (canvas.clientWidth * dpr)
                    let h = int (canvas.clientHeight * dpr)
                    if w > 0 && h > 0 then
                        canvas?width <- w
                        canvas?height <- h
                        Scene.remakeAttachments scene
                resize ()
                let observer = makeResizeObserver (fun _ -> resize ())
                observe observer canvas

                // Keep `pickableById` in sync with the compiled doc so input
                // handlers can resolve pick IDs → Pickable records.
                let mutable pickableById : Map<int, Pickable> = Map.empty
                let mutable lastCompiled : obj = null
                let refreshPickables () =
                    let state = AppStore.store.State
                    let compiled = box state.Compiled
                    if compiled <> lastCompiled then
                        lastCompiled <- compiled
                        let model = ViewerPipeline.viewerModel state
                        pickableById <-
                            model.Pickables
                            |> List.map (fun p -> Pickable.pickId p, p)
                            |> Map.ofList
                Store.subscribe AppStore.store refreshPickables
                refreshPickables ()

                // Async 1×1 pick readback. Shared with input handlers.
                let mutable pickInFlight = false
                let pickAt (px: int) (py: int) : JS.Promise<uint32> =
                    promise {
                        if pickInFlight then return 0u
                        else
                            pickInFlight <- true
                            let encoder = device.createCommandEncoder()
                            WebGPU.copyTextureToBuffer1x1 encoder scene.PickTex px py scene.PickReadBuffer
                            device.queue.submit [| encoder.finish() |]
                            do! scene.PickReadBuffer.mapAsync GPUMapMode.Read
                            let arr = scene.PickReadBuffer.getMappedRange()
                            let id = WebGPU.readFirstU32 arr
                            scene.PickReadBuffer.unmap()
                            pickInFlight <- false
                            return id
                    }

                // Tool cursor (sketch-local u,v) is updated by mousemove and
                // read by the render loop for preview geometry.
                let toolCursor : (ActionId * float * float) option ref = ref None

                Input.install canvas dpr scene.Camera
                    { PickAt = pickAt
                      PickableById = fun () -> pickableById
                      ToolCursor = toolCursor }

                DimensionEditor.install container canvas scene.Camera

                // Render loop.
                let rec frame (_: float) =
                    Render.renderFrame scene toolCursor.Value
                    WebGPU.requestAnimationFrame frame |> ignore
                WebGPU.requestAnimationFrame frame |> ignore

                return box container
    }
