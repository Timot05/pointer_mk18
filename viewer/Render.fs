module Render

// One-shot `renderFrame scene` — encodes a color pass + a pick pass in a
// single command buffer and submits. Called each requestAnimationFrame
// tick from `Viewer.mount`.

open Fable.Core
open Fable.Core.JsInterop
open Browser.Dom
open Server
open PointerMk18.Ui
open WebGPU
open BufferPool

let private emitSketchFrameUniforms (scene: Scene.Scene) (pos: Vec3) (xAxis: Vec3) (yAxis: Vec3) =
    let frameData =
        [| float32 pos.X;   float32 pos.Y;   float32 pos.Z;   0.0f
           float32 xAxis.X; float32 xAxis.Y; float32 xAxis.Z; 0.0f
           float32 yAxis.X; float32 yAxis.Y; float32 yAxis.Z; 0.0f
           0.0f; 0.0f; 0.0f; 0.0f |]
    WebGPU.writeFloat32 scene.Device.queue scene.FrameBuffer 0 frameData

    let canvasWpx = float32 (scene.Canvas.clientWidth * scene.Dpr)
    let canvasHpx = float32 (scene.Canvas.clientHeight * scene.Dpr)
    let labelUniform =
        [| canvasWpx; canvasHpx; 0.0f; 0.0f
           float32 pos.X;   float32 pos.Y;   float32 pos.Z;   0.0f
           float32 xAxis.X; float32 xAxis.Y; float32 xAxis.Z; 0.0f
           float32 yAxis.X; float32 yAxis.Y; float32 yAxis.Z; 0.0f |]
    WebGPU.writeFloat32 scene.Device.queue scene.LabelUniformBuffer 0 labelUniform

let private axesOf (transform: RigidTransform) =
    let pos = transform.Trans
    let xAxis = transform.Rot.Rotate({ X = 1.0; Y = 0.0; Z = 0.0 })
    let yAxis = transform.Rot.Rotate({ X = 0.0; Y = 1.0; Z = 0.0 })
    pos, xAxis, yAxis

/// Render one frame. Call from a requestAnimationFrame loop in Viewer.
/// The 3D field can be produced by the Zig-WASM voxel kernel (`background`)
/// or by the GPU raymarcher (`raymarch`); `state.ViewerMode` selects.
let renderFrame
        (scene: Scene.Scene)
        (toolCursor: (ActionId * float * float) option)
        (background: Kernel.Background.Background option)
        (raymarch: Raymarch.Raymarch option) =
    let w : int = scene.Canvas?width
    let h : int = scene.Canvas?height

    // Camera uniform. Projection is orthographic everywhere — the slab's
    // vertical half-extent `viewHalfH` parameterises the projection, and
    // is shared with the Zig kernel (see `Kernel.Background.viewHalfV`)
    // so field + overlays land on the same NDC coords.
    let b = Camera.basis scene.Camera
    let aspect = float32 (float w / max (float h) 1.0)
    let viewHalfH = float32 (Camera.viewHalfH scene.Camera)
    let cameraData =
        [| float32 b.Eye.X;     float32 b.Eye.Y;     float32 b.Eye.Z;     0.0f
           float32 b.Forward.X; float32 b.Forward.Y; float32 b.Forward.Z; 0.0f
           float32 b.Right.X;   float32 b.Right.Y;   float32 b.Right.Z;   viewHalfH
           float32 b.Up.X;      float32 b.Up.Y;      float32 b.Up.Z;      aspect |]
    WebGPU.writeFloat32 scene.Device.queue scene.CameraBuffer 0 cameraData

    // Viewport uniform (for pixel-sized billboards + gizmo axes).
    WebGPU.writeFloat32 scene.Device.queue scene.ViewportBuffer 0
        [| float32 w; float32 h; 0.0f; 0.0f |]

    let colorView = scene.Ctx.getCurrentTexture().createView()
    let depthView = scene.DepthTex.createView()
    let pickView = scene.PickTex.createView()
    let encoder = scene.Device.createCommandEncoder()

    let colorPass =
        WebGPU.beginRenderPassClearColor encoder colorView 0.996 0.988 0.953 depthView

    let state = AppStore.store.State

    // Field background — drawn first so every sketch/overlay paints on
    // top. Either the Zig-WASM voxel kernel or the GPU sphere-marcher,
    // user-selectable via `state.ViewerMode`.
    match state.ViewerMode, background, raymarch with
    | IntervalKernel, Some bg, _ ->
        Kernel.Background.update bg
        Kernel.Background.draw bg colorPass
    | Raymarch, _, Some rm ->
        Raymarch.update rm state
        Raymarch.draw rm colorPass
    | _ -> ()
    let model = ViewerPipeline.viewerModel state
    let viewState = ViewerPipeline.viewerState state
    let frameById =
        viewState.SketchTransforms
        |> List.map (fun f -> f.Id, f.Transform)
        |> Map.ofList

    let isVisible (actionId: string) =
        Map.tryFind actionId viewState.Visible
        |> Option.defaultValue true

    let slots = scene.Slots

    let drawLine (colorPass: IGPURenderPassEncoder) (slot: Slot) (data: float32[]) =
        if data.Length > 0 then
            let buf = upload scene.Pool slot data
            colorPass.setPipeline scene.LinePipeline
            colorPass.setBindGroup(0, scene.CameraBindGroup)
            colorPass.setBindGroup(1, scene.FrameBindGroup)
            colorPass.setVertexBuffer(0, buf)
            colorPass.draw (data.Length / 6)

    let drawTri (colorPass: IGPURenderPassEncoder) (slot: Slot) (data: float32[]) =
        if data.Length > 0 then
            let buf = upload scene.Pool slot data
            colorPass.setPipeline scene.TriPipeline
            colorPass.setBindGroup(0, scene.CameraBindGroup)
            colorPass.setBindGroup(1, scene.FrameBindGroup)
            colorPass.setVertexBuffer(0, buf)
            colorPass.draw (data.Length / 6)

    let drawPoints (pass: IGPURenderPassEncoder) (pipeline: IGPURenderPipeline) (slot: Slot) (data: float32[]) (floatsPerInstance: int) =
        if data.Length > 0 then
            let buf = upload scene.Pool slot data
            pass.setPipeline pipeline
            pass.setBindGroup(0, scene.CameraBindGroup)
            pass.setBindGroup(1, scene.FrameBindGroup)
            pass.setBindGroup(2, scene.ViewportBindGroup)
            pass.setVertexBuffer(0, scene.PointQuadBuffer)
            pass.setVertexBuffer(1, buf)
            pass.drawInstanced(6, data.Length / floatsPerInstance)

    let drawLabel (colorPass: IGPURenderPassEncoder) (slot: Slot) (data: float32[]) =
        if data.Length > 0 then
            let buf = upload scene.Pool slot data
            colorPass.setPipeline scene.LabelPipeline
            colorPass.setBindGroup(0, scene.CameraBindGroup)
            colorPass.setBindGroup(1, scene.LabelBindGroup)
            colorPass.setVertexBuffer(0, buf)
            colorPass.draw (data.Length / 10)

    // ── Color pass: per-sketch draws ──────────────────────────────────
    for sketch in model.Sketches do
        match Map.tryFind sketch.Id frameById with
        | None -> ()
        | Some _ when not (isVisible sketch.Id) -> ()
        | Some transform ->
            let pos, xAxis, yAxis = axesOf transform
            emitSketchFrameUniforms scene pos xAxis yAxis

            let gridData =
                SketchOverlayRender.buildSketchGridBuffer
                    sketch.Id sketch.Sketch.Entities
                    state.Compiled.Slots.Index viewState.Params 1.0 10
            drawLine colorPass slots.Grid gridData

            let sketchLoops =
                viewState.SketchLoops
                |> List.tryFind (fun l -> l.SketchId = sketch.Id)
                |> Option.map (fun l -> l.Loops)
                |> Option.defaultValue []

            let loopFillData =
                SketchOverlayRender.buildSketchLoopFillBuffer
                    sketch.Id sketch.Sketch sketchLoops
                    state.Compiled.Slots.Index viewState.Params
                    viewState.HoveredTarget viewState.SelectedTargets
            drawTri colorPass slots.LoopFill loopFillData

            let sketchGizmoData = SketchOverlayRender.buildSketchGizmoBuffer ()
            drawLine colorPass slots.Gizmo sketchGizmoData

            let lineData =
                SketchOverlayRender.buildSketchLineBuffer
                    sketch.Id sketch.Sketch.Entities
                    state.Compiled.Slots.Index viewState.Params
                    viewState.HoveredTarget viewState.SelectedTargets
            drawLine colorPass slots.SketchLine lineData

            let showDimensions =
                List.contains sketch.Id viewState.VisibleDimensionSketchIds
            let constraintLineData =
                SketchOverlayRender.buildSketchConstraintLinesBuffer
                    sketch.Id sketch.Sketch
                    state.Compiled.Slots.Index viewState.Params
                    showDimensions
                    viewState.HoveredTarget viewState.SelectedTargets
            drawLine colorPass slots.ConstraintLine constraintLineData

            // Placement preview.
            match viewState.SketchUi.PendingConstraintPlacement with
            | Some pending when pending.SketchId = sketch.Id ->
                let cursorPos =
                    match toolCursor with
                    | Some (sid, u, v) when sid = sketch.Id -> Some { X = u; Y = v }
                    | _ -> None
                match cursorPos with
                | Some cursor ->
                    let previewLines =
                        SketchOverlayRender.buildPendingConstraintLineBuffer
                            sketch.Id sketch.Sketch.Entities
                            state.Compiled.Slots.Index viewState.Params
                            pending.Constraint cursor
                    drawLine colorPass slots.PlacementPreviewLine previewLines

                    let previewPoints =
                        SketchOverlayRender.resolvePointMap
                            state.Compiled.Slots.Index viewState.Params
                            sketch.Id sketch.Sketch.Entities
                    let previewRadius =
                        SketchOverlayRender.circleRadiusLookup
                            state.Compiled.Slots.Index viewState.Params
                            sketch.Id sketch.Sketch.Entities
                    let previewLabelData =
                        LabelBuilder.buildSketchLabelBuffer
                            scene.FontMetrics previewPoints previewRadius
                            sketch.Id
                            [ SketchOverlayRender.withLabelPosition cursor pending.Constraint ]
                            None []
                    drawLabel colorPass slots.PlacementPreviewLabel previewLabelData
                | None -> ()
            | _ -> ()

            // Tool preview.
            let isActiveEditSketch =
                viewState.SketchUi.EditMode
                && state.Doc.SelectedId = Some sketch.Id
            if isActiveEditSketch
               && viewState.SketchUi.Tool <> ""
               && viewState.SketchUi.Tool <> "none" then
                let cursorForSketch =
                    match toolCursor with
                    | Some (sid, u, v) when sid = sketch.Id -> Some (u, v)
                    | _ -> None
                let toolLineData =
                    SketchOverlayRender.buildToolPreviewLineBuffer
                        viewState.SketchUi.Tool viewState.SketchUi.ToolPoints cursorForSketch
                drawLine colorPass slots.ToolPreviewLine toolLineData

                let toolPointData =
                    SketchOverlayRender.buildToolPreviewPointBuffer
                        viewState.SketchUi.Tool viewState.SketchUi.ToolPoints cursorForSketch
                drawPoints colorPass scene.PointPipeline slots.ToolPreviewPoint toolPointData 7

            // Points.
            let pointData =
                SketchOverlayRender.buildSketchPointBuffer
                    sketch.Id sketch.Sketch.Entities
                    state.Compiled.Slots.Index viewState.Params
                    viewState.HoveredTarget viewState.SelectedTargets
            drawPoints colorPass scene.PointPipeline slots.SketchPoint pointData 7

            // Labels.
            let points =
                SketchOverlayRender.resolvePointMap
                    state.Compiled.Slots.Index viewState.Params
                    sketch.Id sketch.Sketch.Entities
            let radiusLookup =
                SketchOverlayRender.circleRadiusLookup
                    state.Compiled.Slots.Index viewState.Params
                    sketch.Id sketch.Sketch.Entities
            let labelData =
                if showDimensions then
                    LabelBuilder.buildSketchLabelBuffer
                        scene.FontMetrics points radiusLookup
                        sketch.Id sketch.Sketch.Constraints
                        viewState.HoveredTarget viewState.SelectedTargets
                else [||]
            drawLabel colorPass slots.Label labelData

    // ── Color pass: frame gizmos + origin dots (world-space, no frame uniform) ──
    let visibleFrames =
        viewState.Frames |> List.filter (fun f -> isVisible f.Id)

    let gizmoData =
        SketchOverlayRender.buildFramesGizmoBuffer
            visibleFrames viewState.HoveredTarget viewState.SelectedTargets
            state.Doc.SelectedId
    if gizmoData.Length > 0 then
        let buf = upload scene.Pool slots.FrameGizmo gizmoData
        colorPass.setPipeline scene.GizmoPipeline
        colorPass.setBindGroup(0, scene.CameraBindGroup)
        colorPass.setBindGroup(1, scene.ViewportBindGroup)
        colorPass.setVertexBuffer(0, buf)
        colorPass.draw (gizmoData.Length / 12)

    let frameOriginData =
        SketchOverlayRender.buildFrameOriginsPointBuffer
            visibleFrames viewState.HoveredTarget viewState.SelectedTargets
    if frameOriginData.Length > 0 then
        let buf = upload scene.Pool slots.FrameOriginPoint frameOriginData
        colorPass.setPipeline scene.WorldPointPipeline
        colorPass.setBindGroup(0, scene.CameraBindGroup)
        colorPass.setBindGroup(1, scene.ViewportBindGroup)
        colorPass.setVertexBuffer(0, scene.PointQuadBuffer)
        colorPass.setVertexBuffer(1, buf)
        colorPass.drawInstanced(6, frameOriginData.Length / 8)

    colorPass.endPass()

    // ── Pick pass ─────────────────────────────────────────────────────
    let pickPass =
        encoder.beginRenderPass
            (box
                {| colorAttachments =
                    [| {| view = pickView
                          loadOp = "clear"
                          storeOp = "store"
                          clearValue = {| r = 0; g = 0; b = 0; a = 0 |} |} |]
                   depthStencilAttachment =
                    {| view = depthView
                       depthLoadOp = "clear"
                       depthStoreOp = "store"
                       depthClearValue = 1.0 |} |})

    for sketch in model.Sketches do
        match Map.tryFind sketch.Id frameById with
        | None -> ()
        | Some _ when not (isVisible sketch.Id) -> ()
        | Some transform ->
            let pos, xAxis, yAxis = axesOf transform
            WebGPU.writeFloat32 scene.Device.queue scene.FrameBuffer 0
                [| float32 pos.X;   float32 pos.Y;   float32 pos.Z;   0.0f
                   float32 xAxis.X; float32 xAxis.Y; float32 xAxis.Z; 0.0f
                   float32 yAxis.X; float32 yAxis.Y; float32 yAxis.Z; 0.0f
                   0.0f; 0.0f; 0.0f; 0.0f |]

            let sketchPickLoops =
                viewState.SketchLoops
                |> List.tryFind (fun l -> l.SketchId = sketch.Id)
                |> Option.map (fun l -> l.Loops)
                |> Option.defaultValue []

            let loopPickData =
                SketchOverlayRender.buildSketchLoopPickBuffer
                    sketch.Id sketch.Sketch sketchPickLoops
                    state.Compiled.Slots.Index viewState.Params
                    model.Pickables
            if loopPickData.Length > 0 then
                let buf = upload scene.Pool slots.LoopPick loopPickData
                pickPass.setPipeline scene.LoopPickPipeline
                pickPass.setBindGroup(0, scene.CameraBindGroup)
                pickPass.setBindGroup(1, scene.FrameBindGroup)
                pickPass.setVertexBuffer(0, buf)
                pickPass.draw (loopPickData.Length / 3)

            let linePickData =
                SketchOverlayRender.buildSketchPickLineBuffer
                    sketch.Id sketch.Sketch.Entities
                    state.Compiled.Slots.Index viewState.Params
                    model.Pickables
            if linePickData.Length > 0 then
                let buf = upload scene.Pool slots.LinePick linePickData
                pickPass.setPipeline scene.LinePickPipeline
                pickPass.setBindGroup(0, scene.CameraBindGroup)
                pickPass.setBindGroup(1, scene.FrameBindGroup)
                pickPass.setVertexBuffer(0, scene.LinePickCornerBuffer)
                pickPass.setVertexBuffer(1, buf)
                pickPass.drawInstanced(6, linePickData.Length / 5)

            let pointPickData =
                SketchOverlayRender.buildSketchPointPickBuffer
                    sketch.Id sketch.Sketch.Entities
                    state.Compiled.Slots.Index viewState.Params
                    model.Pickables
            drawPoints pickPass scene.PointPickPipeline slots.PointPick pointPickData 4

            let pickShowDims =
                List.contains sketch.Id viewState.VisibleDimensionSketchIds
            let dimPickData =
                if pickShowDims then
                    SketchOverlayRender.buildSketchDimensionPickBuffer
                        sketch.Id sketch.Sketch
                        state.Compiled.Slots.Index viewState.Params
                        model.Pickables
                else [||]
            drawPoints pickPass scene.PointPickPipeline slots.DimPick dimPickData 4

    // Frame-origin + frame-axis picks (world-space).
    let frameOriginPickData =
        SketchOverlayRender.buildFrameOriginsPickBuffer visibleFrames model.Pickables
    if frameOriginPickData.Length > 0 then
        let buf = upload scene.Pool slots.FrameOriginPick frameOriginPickData
        pickPass.setPipeline scene.WorldPointPickPipeline
        pickPass.setBindGroup(0, scene.CameraBindGroup)
        pickPass.setBindGroup(1, scene.ViewportBindGroup)
        pickPass.setVertexBuffer(0, scene.PointQuadBuffer)
        pickPass.setVertexBuffer(1, buf)
        pickPass.drawInstanced(6, frameOriginPickData.Length / 5)

    let frameAxisPickData =
        SketchOverlayRender.buildFrameAxesPickBuffer
            visibleFrames model.Pickables
            (Camera.viewHalfH scene.Camera) (float h)
    if frameAxisPickData.Length > 0 then
        let buf = upload scene.Pool slots.FrameAxisPick frameAxisPickData
        pickPass.setPipeline scene.WorldPointPickPipeline
        pickPass.setBindGroup(0, scene.CameraBindGroup)
        pickPass.setBindGroup(1, scene.ViewportBindGroup)
        pickPass.setVertexBuffer(0, scene.PointQuadBuffer)
        pickPass.setVertexBuffer(1, buf)
        pickPass.drawInstanced(6, frameAxisPickData.Length / 5)

    pickPass.endPass()

    // Submit + reclaim old buffers.
    scene.Device.queue.submit [| encoder.finish() |]
    markFrameSubmitted scene.Pool
    flushRetired scene.Pool
