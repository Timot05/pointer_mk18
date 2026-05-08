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

let private mountCanvas (root: HTMLElement) : HTMLElement * HTMLCanvasElement * HTMLElement =
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

    // FPS HUD — absolute-positioned overlay in the top-right of the
    // viewer. Updated from the RAF callback; pointer-events: none so it
    // never intercepts mouse interaction.
    let fps : HTMLElement = unbox (document.createElement "div")
    fps?style?position <- "absolute"
    fps?style?top <- "8px"
    fps?style?right <- "8px"
    fps?style?padding <- "4px 8px"
    fps?style?fontFamily <- "ui-monospace, SFMono-Regular, Menlo, monospace"
    fps?style?fontSize <- "11px"
    fps?style?lineHeight <- "1.4"
    fps?style?color <- "#1a1a1a"
    fps?style?background <- "rgba(255, 255, 255, 0.75)"
    fps?style?borderRadius <- "4px"
    fps?style?pointerEvents <- "none"
    fps?style?zIndex <- "10"
    fps?textContent <- "— FPS"
    container.appendChild fps |> ignore

    container, canvas, fps

let mount (root: HTMLElement) : JS.Promise<obj> =
    promise {
        let container, canvas, fpsEl = mountCanvas root

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
                let mutable lastNotebookBytes : obj option = None
                /// Push the latest MathIR bytes (from `RunNotebook`) to the
                /// kernel. No-op if the user hasn't run the notebook yet.
                let pushIr (bg: Kernel.Background.Background) =
                    let state = AppStore.store.State
                    match state.LastNotebookBytes with
                    | Some bytes ->
                        Kernel.Background.updateIr bg bytes
                        lastNotebookBytes <- Some bytes
                    | None ->
                        Kernel.Background.clear bg
                        lastNotebookBytes <- None
                Kernel.Background.create scene
                |> Promise.iter (fun bg ->
                    background.Value <- Some bg
                    pushIr bg)

                // Adaptive render-resolution scale. Dropped to
                // `LOW_RES_SCALE` while the camera's moving so the heavy
                // raymarch fragment runs on a quarter as many pixels; the
                // CSS `width: 100%` on the canvas element upscales in the
                // browser for free. Returned to 1.0 once the camera has
                // been idle for `HIGH_RES_DELAY_MS`.
                let LOW_RES_SCALE = 0.5
                let HIGH_RES_DELAY_MS = 150.0
                let mutable renderScale = 1.0

                // Resize: update canvas size + recreate depth/pick textures.
                // `renderScale` multiplies the device-pixel target so the
                // motion path reuses this exact reallocation code.
                let resize () =
                    let w = int (canvas.clientWidth * dpr * renderScale)
                    let h = int (canvas.clientHeight * dpr * renderScale)
                    if w > 0 && h > 0 then
                        canvas?width <- w
                        canvas?height <- h
                        Scene.remakeAttachments scene
                        match background.Value with
                        | Some bg -> Kernel.Background.resize bg w h
                        | None -> ()

                let setRenderScale (s: float) =
                    if s <> renderScale then
                        renderScale <- s
                        resize ()

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
                    let model = ViewerPipeline.viewerModel state
                    pickableById <-
                        (model.Pickables
                         @ TranslateGizmo.ephemeralPickablesForState state
                         @ RotateGizmo.ephemeralPickablesForState state)
                         @ HalfPlaneGizmo.ephemeralPickablesForState state
                        |> List.map (fun p -> Pickable.pickId p, p)
                        |> Map.ofList
                    // Notebook-driven push: every successful `RunNotebook`
                    // refreshes `state.LastNotebookBytes`. Compare by
                    // reference; mismatch → upload to the kernel worker
                    // pool. Action-graph push is gone (action lowering
                    // pipeline is dormant; FieldNode no longer flows).
                    let nbBytes = state.LastNotebookBytes
                    let bytesChanged =
                        match lastNotebookBytes, nbBytes with
                        | Some a, Some b -> not (obj.ReferenceEquals(a, b))
                        | None, None -> false
                        | _ -> true
                    if bytesChanged then
                        match background.Value with
                        | Some bg -> pushIr bg
                        | None ->
                            // Background not mounted yet; pushIr runs on
                            // create-resolve and captures current state.
                            lastNotebookBytes <- nbBytes
                Store.subscribe AppStore.store onStoreChange
                onStoreChange ()

                // Compute-shader picker. Dispatched on mouse events, reads
                // back a 5×5 window of candidates, and forwards the full
                // deduped list — the core reducer (`reduceSelectionCandidates`)
                // picks the winner by priority + score.
                let pickCompute = PickCompute.create scene
                let pickAt (px: int) (py: int) : JS.Promise<PickCandidateInput list> =
                    PickCompute.pickAt pickCompute px py

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

                // Render loop. Two separate concerns:
                //   1. Dirty-check — skip the render call entirely when
                //      nothing visible has changed. rAF stays ticking
                //      (microsecond-cheap when idle), but no GPU work
                //      runs.
                //   2. 60 FPS cap — even on high-refresh displays, space
                //      renders at least MIN_FRAME_MS apart.
                // Dirty signals: camera fields, store state ref,
                // tool-cursor value, background transition None→Some.
                // Everything else the viewer reads goes through the
                // store, so a new state ref covers it.
                //
                // Motion detection (for the adaptive scale) runs only
                // when we actually render; that's fine because the low-
                // res mode kicks in *on the first moving frame* and no
                // motion happens during skipped frames anyway.
                let MIN_FRAME_MS = 16.0
                let mutable emaMs = 0.0
                let mutable lastHudTs = 0.0
                let mutable lastRenderTs = 0.0
                let mutable lastCamAz = System.Double.NaN
                let mutable lastCamEl = 0.0
                let mutable lastCamDist = 0.0
                let mutable lastCamTx = 0.0
                let mutable lastCamTy = 0.0
                let mutable lastCamTz = 0.0
                let mutable lastMotionTs = 0.0
                let mutable lastStateRef : obj = null
                let mutable lastToolCursor : (ActionId * float * float) option = None
                let mutable lastBackgroundSome = false
                let rec frame (now: float) =
                    let cam = scene.Camera
                    let state = AppStore.store.State
                    let toolCursorNow = toolCursor.Value
                    let backgroundSome = Option.isSome background.Value

                    let camChanged =
                        not (System.Double.IsNaN lastCamAz) && (
                            cam.Azimuth <> lastCamAz
                            || cam.Elevation <> lastCamEl
                            || cam.Distance <> lastCamDist
                            || cam.Target.X <> lastCamTx
                            || cam.Target.Y <> lastCamTy
                            || cam.Target.Z <> lastCamTz)
                    let stateChanged =
                        not (System.Object.ReferenceEquals(state, lastStateRef))
                    // Structural equality — the value is a small tuple,
                    // not a reference-tracked object.
                    let toolChanged = toolCursorNow <> lastToolCursor
                    let bgChanged = backgroundSome <> lastBackgroundSome
                    let firstFrame = System.Double.IsNaN lastCamAz

                    // If the camera just stopped moving, the scale needs
                    // to transition LOW_RES → 1.0 one more time — but
                    // nothing else is dirty. Treat the pending scale
                    // change itself as a dirty signal so we always fire
                    // exactly one high-res render after motion ends.
                    let moving =
                        lastMotionTs > 0.0 && now - lastMotionTs < HIGH_RES_DELAY_MS
                    let desiredScale = if moving then LOW_RES_SCALE else 1.0
                    let scalePending = desiredScale <> renderScale

                    let dirty =
                        firstFrame || camChanged || stateChanged
                        || toolChanged || bgChanged || scalePending

                    let throttled = now - lastRenderTs < MIN_FRAME_MS

                    if dirty && not throttled then
                        // Refresh tracking before rendering so any
                        // store mutation during render is caught next
                        // frame (not silently absorbed).
                        lastCamAz <- cam.Azimuth
                        lastCamEl <- cam.Elevation
                        lastCamDist <- cam.Distance
                        lastCamTx <- cam.Target.X
                        lastCamTy <- cam.Target.Y
                        lastCamTz <- cam.Target.Z
                        lastStateRef <- state
                        lastToolCursor <- toolCursorNow
                        lastBackgroundSome <- backgroundSome
                        if camChanged then lastMotionTs <- now

                        setRenderScale desiredScale

                        // Measure the CPU-side cost of building + submitting
                        // this frame's command buffer. GPU execution is
                        // asynchronous so this understates total GPU cost,
                        // but it's the cost that blocks the main thread —
                        // which is what the user sees as "frame cost".
                        let renderStart = WebGPU.performanceNow ()
                        Render.renderFrame scene toolCursor.Value
                            background.Value (Some raymarch) (Some fieldSlice)
                        // Keep the compute-pick geometry buffers in sync
                        // with whatever Render just drew, using the same
                        // `viewState` source. Sketch order inside the
                        // picker matches Render's `writeSketchUniforms`
                        // truncation so the dynamic frame-uniform offsets
                        // line up.
                        let vs = ViewerPipeline.viewerState state
                        PickCompute.update pickCompute state vs
                        let renderEnd = WebGPU.performanceNow ()
                        let renderMs = renderEnd - renderStart
                        let alpha = 0.2
                        emaMs <-
                            if emaMs = 0.0 then renderMs
                            else (1.0 - alpha) * emaMs + alpha * renderMs
                        lastRenderTs <- now

                        if now - lastHudTs > 250.0 && emaMs > 0.0 then
                            let suffix = if moving then " · LOW" else ""
                            fpsEl?textContent <- sprintf "%.1f ms%s" emaMs suffix
                            lastHudTs <- now

                    WebGPU.requestAnimationFrame frame |> ignore
                WebGPU.requestAnimationFrame frame |> ignore

                return box container
    }
