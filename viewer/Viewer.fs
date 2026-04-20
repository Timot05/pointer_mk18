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

                // Field background renderer. Loads async; when ready, the
                // render loop calls into it each frame. Once initialized,
                // push the current compiled IR so the coarse level renders
                // immediately — otherwise we'd wait for the next store
                // dispatch to trigger the subscription below.
                let background : Kernel.Background.Background option ref = ref None
                let mutable lastCompiled : obj = null
                let mutable lastSlotValues : obj = null
                // Doc reference changes on every action edit, including
                // toggles that don't touch Compiled or SlotValues (e.g.
                // flipping DisplaySettings.Enabled).
                let mutable lastDoc : obj = null
                let pushIr (bg: Kernel.Background.Background) =
                    let state = AppStore.store.State
                    // Include only surfaces whose action has the "show
                    // iso-surface" display toggle on *and* is visible.
                    // Default `DisplaySettings.Enabled` is `false`, so a
                    // freshly loaded doc renders nothing until the user
                    // opts in — matching the toggle state in the UI.
                    let enabledActionIds =
                        state.Doc.Actions
                        |> List.choose (fun a ->
                            let d = a.Display |> Option.defaultValue DisplaySettings.defaults
                            if a.Visible && d.Enabled then Some a.Id else None)
                        |> Set.ofList
                    let surfaces =
                        state.Compiled.Surfaces
                        |> List.filter (fun s -> Set.contains s.ActionId enabledActionIds)
                    match Kernel.FieldToIr.build surfaces state.SlotValues with
                    | Some bytes -> Kernel.Background.updateIr bg bytes
                    | None -> Kernel.Background.clear bg
                    lastCompiled <- box state.Compiled
                    lastSlotValues <- box state.SlotValues
                    lastDoc <- box state.Doc
                Kernel.Background.create scene
                |> Promise.iter (fun bg ->
                    background.Value <- Some bg
                    pushIr bg)

                // Resize: update canvas size + recreate depth/pick textures.
                let resize () =
                    let w = int (canvas.clientWidth * dpr)
                    let h = int (canvas.clientHeight * dpr)
                    if w > 0 && h > 0 then
                        canvas?width <- w
                        canvas?height <- h
                        Scene.remakeAttachments scene
                        match background.Value with
                        | Some bg -> Kernel.Background.resize bg w h
                        | None -> ()
                resize ()
                let observer = makeResizeObserver (fun _ -> resize ())
                observe observer canvas

                // Keep `pickableById` in sync with the compiled doc so input
                // handlers can resolve pick IDs → Pickable records. Same
                // subscription pushes fresh IR to the background renderer
                // whenever surface topology *or* slot values change.
                let mutable pickableById : Map<int, Pickable> = Map.empty
                let onStoreChange () =
                    let state = AppStore.store.State
                    let compiled = box state.Compiled
                    let slots = box state.SlotValues
                    let compiledChanged = compiled <> lastCompiled
                    if compiledChanged then
                        let model = ViewerPipeline.viewerModel state
                        pickableById <-
                            model.Pickables
                            |> List.map (fun p -> Pickable.pickId p, p)
                            |> Map.ofList
                    // IR depends on topology (Compiled), values
                    // (SlotValues — replaced on every drag/edit), *and*
                    // the Doc (display toggles don't touch the first two).
                    let doc = box state.Doc
                    if compiledChanged || slots <> lastSlotValues || doc <> lastDoc then
                        match background.Value with
                        | Some bg -> pushIr bg
                        | None ->
                            // Background not mounted yet; pushIr runs on
                            // create-resolve below and captures current state.
                            lastCompiled <- compiled
                            lastSlotValues <- slots
                            lastDoc <- doc
                Store.subscribe AppStore.store onStoreChange
                onStoreChange ()

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

                // GPU raymarcher — alternative to the voxel kernel,
                // toggled at runtime by `state.ViewerMode`. Cheap to
                // create (empty buffers, no pipeline until first
                // compile), so we instantiate unconditionally.
                let raymarch = Raymarch.create scene

                // Iso-line overlay. Runs in both modes; pipeline + buffer
                // grow on demand.
                let fieldSlice = FieldSlice.create scene

                // Render loop.
                let rec frame (_: float) =
                    Render.renderFrame scene toolCursor.Value
                        background.Value (Some raymarch) (Some fieldSlice)
                    WebGPU.requestAnimationFrame frame |> ignore
                WebGPU.requestAnimationFrame frame |> ignore

                return box container
    }
