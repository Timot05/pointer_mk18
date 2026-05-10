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

let private axesOf (transform: RigidTransform) =
    let pos = transform.Trans
    let xAxis = transform.Rot.Rotate({ X = 1.0; Y = 0.0; Z = 0.0 })
    let yAxis = transform.Rot.Rotate({ X = 0.0; Y = 1.0; Z = 0.0 })
    pos, xAxis, yAxis

/// Per-sketch frame uniform block — matches the `SketchFrame` WGSL
/// struct in Line.wgsl (16 floats / 64 bytes).
let private sketchFrameData (pos: Vec3) (xAxis: Vec3) (yAxis: Vec3) : float32[] =
    [| float32 pos.X;   float32 pos.Y;   float32 pos.Z;   0.0f
       float32 xAxis.X; float32 xAxis.Y; float32 xAxis.Z; 0.0f
       float32 yAxis.X; float32 yAxis.Y; float32 yAxis.Z; 0.0f
       0.0f; 0.0f; 0.0f; 0.0f |]

let private sketchLabelData
        (canvasWpx: float32) (canvasHpx: float32)
        (pos: Vec3) (xAxis: Vec3) (yAxis: Vec3) : float32[] =
    [| canvasWpx; canvasHpx; 0.0f; 0.0f
       float32 pos.X;   float32 pos.Y;   float32 pos.Z;   0.0f
       float32 xAxis.X; float32 xAxis.Y; float32 xAxis.Z; 0.0f
       float32 yAxis.X; float32 yAxis.Y; float32 yAxis.Z; 0.0f |]

/// Write every visible sketch's frame + label uniform blocks into the
/// shared per-sketch buffers at distinct dynamic-offset slots, *before*
/// the command encoder records any draws. Returns a map from sketch id
/// → byte offset so each draw can select its own block via
/// `setBindGroupWithOffset`. See `Scene.fs` for the reason this has to
/// happen up front (`queue.writeBuffer` is serialised against
/// `submit()`, not interleaved with commands inside a single command
/// buffer).
let private writeSketchUniforms
        (scene: Scene.Scene)
        (sketchTransforms: FrameView list) : Map<ActionId, int> =
    let canvasWpx = float32 (scene.Canvas.clientWidth * scene.Dpr)
    let canvasHpx = float32 (scene.Canvas.clientHeight * scene.Dpr)
    let used = min sketchTransforms.Length Scene.FRAME_CAPACITY
    sketchTransforms
    |> List.truncate used
    |> List.mapi (fun i f ->
        let pos, xAxis, yAxis = axesOf f.Transform
        let offset = i * Scene.FRAME_STRIDE
        WebGPU.writeFloat32 scene.Device.queue scene.FrameBuffer offset
            (sketchFrameData pos xAxis yAxis)
        WebGPU.writeFloat32 scene.Device.queue scene.LabelUniformBuffer offset
            (sketchLabelData canvasWpx canvasHpx pos xAxis yAxis)
        f.Id, offset)
    |> Map.ofList

/// Render one frame. Call from a requestAnimationFrame loop in Viewer.
/// The 3D field is produced by the Zig-WASM voxel kernel (`background`).
/// The GPU raymarcher path was retired alongside the action graph — its
/// file (`Raymarch.fs`) stays on disk for future block-targeted work.
let renderFrame
        (scene: Scene.Scene)
        (toolCursor: (ActionId * float * float) option)
        (background: Kernel.Background.Background option) =
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
    let encoder = scene.Device.createCommandEncoder()

    let state = AppStore.store.State

    let colorPass =
        WebGPU.beginRenderPassClearColor encoder colorView 0.996 0.988 0.953 depthView

    // Field background — drawn first so every sketch/overlay paints on
    // top. Powered by the Zig-WASM voxel kernel.
    match background with
    | Some bg ->
        Kernel.Background.update bg
        Kernel.Background.draw bg colorPass
    | None -> ()
    let model = ViewerPipeline.viewerModel state
    let viewState = ViewerPipeline.viewerState state

    let frameById =
        viewState.SketchTransforms
        |> List.map (fun f -> f.Id, f.Transform)
        |> Map.ofList

    // Reserve a unique dynamic-offset slot per sketch + upload its frame
    // uniforms once, before any draws are recorded. Every per-sketch
    // draw below looks the offset up by id.
    let sketchOffsets = writeSketchUniforms scene viewState.SketchTransforms

    // Action visibility is gone with the action graph; everything renders.
    let isVisible (_actionId: string) = true

    let slots = scene.Slots

    // All per-sketch draw helpers take an explicit `frameOffset` — the
    // byte offset into the shared frame uniform buffer reserved for the
    // current sketch. See `writeSketchUniforms` below.
    let drawLine (colorPass: IGPURenderPassEncoder) (frameOffset: int) (slot: Slot) (data: float32[]) =
        if data.Length > 0 then
            let buf = upload scene.Pool slot data
            colorPass.setPipeline scene.LinePipeline
            colorPass.setBindGroup(0, scene.CameraBindGroup)
            colorPass.setBindGroupWithOffset(1, scene.FrameBindGroup, frameOffset)
            colorPass.setVertexBuffer(0, buf)
            colorPass.draw (data.Length / 6)

    let drawTri (colorPass: IGPURenderPassEncoder) (frameOffset: int) (slot: Slot) (data: float32[]) =
        if data.Length > 0 then
            let buf = upload scene.Pool slot data
            colorPass.setPipeline scene.TriPipeline
            colorPass.setBindGroup(0, scene.CameraBindGroup)
            colorPass.setBindGroupWithOffset(1, scene.FrameBindGroup, frameOffset)
            colorPass.setVertexBuffer(0, buf)
            colorPass.draw (data.Length / 6)

    let drawPoints (pass: IGPURenderPassEncoder) (pipeline: IGPURenderPipeline) (frameOffset: int) (slot: Slot) (data: float32[]) (floatsPerInstance: int) =
        if data.Length > 0 then
            let buf = upload scene.Pool slot data
            pass.setPipeline pipeline
            pass.setBindGroup(0, scene.CameraBindGroup)
            pass.setBindGroupWithOffset(1, scene.FrameBindGroup, frameOffset)
            pass.setBindGroup(2, scene.ViewportBindGroup)
            pass.setVertexBuffer(0, scene.PointQuadBuffer)
            pass.setVertexBuffer(1, buf)
            pass.drawInstanced(6, data.Length / floatsPerInstance)

    let drawLabel (colorPass: IGPURenderPassEncoder) (frameOffset: int) (slot: Slot) (data: float32[]) =
        if data.Length > 0 then
            let buf = upload scene.Pool slot data
            colorPass.setPipeline scene.LabelPipeline
            colorPass.setBindGroup(0, scene.CameraBindGroup)
            colorPass.setBindGroupWithOffset(1, scene.LabelBindGroup, frameOffset)
            colorPass.setVertexBuffer(0, buf)
            colorPass.draw (data.Length / 10)

    // ── Color pass: per-sketch draws ──────────────────────────────────
    for sketch in model.Sketches do
        match Map.tryFind sketch.Id frameById, Map.tryFind sketch.Id sketchOffsets with
        | None, _ | _, None -> ()
        | Some _, _ when not (isVisible sketch.Id) -> ()
        | Some _, Some frameOffset ->
            let isActiveEditSketch =
                viewState.SketchUi.EditMode
                && (match state.Doc.SelectedBlockId with
                    | Some bid -> Server.SketchAuthoring.blockSketchId bid = sketch.Id
                    | None -> false)

            // Per-sketch buffer slot lookup — each category resolves to
            // this sketch's own `Slot` so sketches can't stomp each
            // other's vertex data via `queue.writeBuffer` ordering.
            let gridSlot = getSketchSlot slots.Grid sketch.Id
            let loopFillSlot = getSketchSlot slots.LoopFill sketch.Id
            let gizmoSlot = getSketchSlot slots.Gizmo sketch.Id
            let sketchLineSlot = getSketchSlot slots.SketchLine sketch.Id
            let constraintLineSlot = getSketchSlot slots.ConstraintLine sketch.Id
            let placementPreviewLineSlot = getSketchSlot slots.PlacementPreviewLine sketch.Id
            let placementPreviewLabelSlot = getSketchSlot slots.PlacementPreviewLabel sketch.Id
            let toolPreviewLineSlot = getSketchSlot slots.ToolPreviewLine sketch.Id
            let toolPreviewPointSlot = getSketchSlot slots.ToolPreviewPoint sketch.Id
            let sketchPointSlot = getSketchSlot slots.SketchPoint sketch.Id
            let labelSlot = getSketchSlot slots.Label sketch.Id

            if isActiveEditSketch then
                let gridData =
                    SketchOverlayRender.buildSketchGridBuffer
                        sketch.Id sketch.Sketch.Entities
                        state.Compiled.Slots.Index viewState.Params 1.0 10
                drawLine colorPass frameOffset gridSlot gridData

            let sketchLoops =
                viewState.SketchLoops
                |> List.tryFind (fun l -> l.SketchId = sketch.Id)
                |> Option.map (fun l -> l.Loops)
                |> Option.defaultValue []

            let loopFillData =
                SketchOverlayRender.buildSketchLoopFillBuffer
                    sketch.Id sketch.Sketch sketchLoops
                    state.Compiled.Slots.Index viewState.Params
                    viewState.HighlightedTarget viewState.HighlightedTargets
            drawTri colorPass frameOffset loopFillSlot loopFillData

            let sketchGizmoData = SketchOverlayRender.buildSketchGizmoBuffer ()
            drawLine colorPass frameOffset gizmoSlot sketchGizmoData

            let lineData =
                SketchOverlayRender.buildSketchLineBuffer
                    sketch.Id sketch.Sketch.Entities
                    state.Compiled.Slots.Index viewState.Params
                    viewState.HighlightedTarget viewState.HighlightedTargets
            drawLine colorPass frameOffset sketchLineSlot lineData

            let showDimensions =
                List.contains sketch.Id viewState.VisibleDimensionSketchIds
            let constraintLineData =
                SketchOverlayRender.buildSketchConstraintLinesBuffer
                    sketch.Id sketch.Sketch
                    state.Compiled.Slots.Index viewState.Params
                    showDimensions
                    viewState.HighlightedTarget viewState.HighlightedTargets
            drawLine colorPass frameOffset constraintLineSlot constraintLineData

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
                    drawLine colorPass frameOffset placementPreviewLineSlot previewLines

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
                    drawLabel colorPass frameOffset placementPreviewLabelSlot previewLabelData
                | None -> ()
            | _ -> ()

            // Tool preview.
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
                drawLine colorPass frameOffset toolPreviewLineSlot toolLineData

                let toolPointData =
                    SketchOverlayRender.buildToolPreviewPointBuffer
                        viewState.SketchUi.Tool viewState.SketchUi.ToolPoints cursorForSketch
                drawPoints colorPass scene.PointPipeline frameOffset toolPreviewPointSlot toolPointData 7

            // Points.
            let pointData =
                SketchOverlayRender.buildSketchPointBuffer
                    sketch.Id sketch.Sketch.Entities
                    state.Compiled.Slots.Index viewState.Params
                    viewState.HighlightedTarget viewState.HighlightedTargets
            drawPoints colorPass scene.PointPipeline frameOffset sketchPointSlot pointData 7

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
                        viewState.HighlightedTarget viewState.HighlightedTargets
                else [||]
            drawLabel colorPass frameOffset labelSlot labelData

    // ── Color pass: frame gizmos + origin dots (world-space, no frame uniform) ──
    let visibleFrames =
        ([] : FrameView list)

    let frameGizmoData =
        SketchOverlayRender.buildFramesGizmoBuffer
            visibleFrames viewState.HighlightedTarget viewState.HighlightedTargets
            None

    let translateCtx = TranslateGizmo.contextOf state
    let rotateCtx = RotateGizmo.contextOf state
    let halfPlaneCtx = HalfPlaneGizmo.contextOf state
    let worldPerPx = (2.0 * Camera.viewHalfH scene.Camera) / max (float h) 1.0

    // Thin line pipeline: frame gizmos + translate plane outlines
    // (same Gizmo.wgsl format, line-list).
    let translateThinData =
        match translateCtx with
        | Some ctx ->
            TranslateGizmo.buildThinVertices ctx worldPerPx
        | None -> [||]
    let rotateLineData =
        match rotateCtx with
        | Some ctx -> RotateGizmo.buildLineVertices ctx worldPerPx
        | None -> [||]
    let gizmoData = Array.concat [| frameGizmoData; translateThinData; rotateLineData |]
    if gizmoData.Length > 0 then
        let buf = upload scene.Pool slots.FrameGizmo gizmoData
        colorPass.setPipeline scene.GizmoPipeline
        colorPass.setBindGroup(0, scene.CameraBindGroup)
        colorPass.setBindGroup(1, scene.ViewportBindGroup)
        colorPass.setVertexBuffer(0, buf)
        colorPass.draw (gizmoData.Length / 12)

    let rotateActiveHandle =
        match state.ActiveSession with
        | Some (RotateAxisDrag s) -> Some(TargetGizmoHandle(s.ActionId, GRotateAxis))
        | Some (RotateAngleDrag s) -> Some(TargetGizmoHandle(s.ActionId, GRotateAngle))
        | _ -> None
    let rotatePointData =
        match rotateCtx with
        | Some ctx ->
            let activeHandle =
                match rotateActiveHandle with
                | Some(TargetGizmoHandle(aid, h)) when aid = ctx.ActionId -> Some h
                | _ -> None
            RotateGizmo.buildPointVertices ctx worldPerPx activeHandle
        | None -> [||]
    if rotatePointData.Length > 0 then
        let buf = upload scene.Pool slots.FrameOriginPoint rotatePointData
        colorPass.setPipeline scene.WorldPointPipeline
        colorPass.setBindGroup(0, scene.CameraBindGroup)
        colorPass.setBindGroup(1, scene.ViewportBindGroup)
        colorPass.setVertexBuffer(0, scene.PointQuadBuffer)
        colorPass.setVertexBuffer(1, buf)
        colorPass.drawInstanced(6, rotatePointData.Length / 8)

    // Thick pipeline: translate-gizmo axes + arrow tips + (optional)
    // dashed drag guide. Separate pipeline because the thick quads are
    // rendered as triangle-list camera-facing geometry (see
    // `viewer/Shaders/TranslateGizmoThick.wgsl`).
    match translateCtx with
    | Some ctx ->
        let activeAxis =
            match state.ActiveSession with
            | Some (GizmoAxisDrag s) when s.ActionId = ctx.ActionId -> Some s.AxisIndex
            | _ -> None
        let viewportExtentPx = float32 (max w h)
        let thickData = TranslateGizmo.buildThickVertices ctx activeAxis viewportExtentPx
        if thickData.Length > 0 then
            let buf = upload scene.Pool slots.TranslateGizmo thickData
            colorPass.setPipeline scene.TranslateGizmoPipeline
            colorPass.setBindGroup(0, scene.CameraBindGroup)
            colorPass.setBindGroup(1, scene.ViewportBindGroup)
            colorPass.setVertexBuffer(0, buf)
            colorPass.draw (thickData.Length / 13)
    | None -> ()

    match rotateCtx with
    | Some ctx ->
        let thickData = RotateGizmo.buildThickVertices ctx
        if thickData.Length > 0 then
            let buf = upload scene.Pool slots.TranslateGizmo thickData
            colorPass.setPipeline scene.TranslateGizmoPipeline
            colorPass.setBindGroup(0, scene.CameraBindGroup)
            colorPass.setBindGroup(1, scene.ViewportBindGroup)
            colorPass.setVertexBuffer(0, buf)
            colorPass.draw (thickData.Length / 13)
    | None -> ()

    match halfPlaneCtx with
    | Some ctx ->
        let dragActive =
            match state.ActiveSession with
            | Some (HalfPlaneOffsetDrag s) when s.ActionId = ctx.ActionId -> true
            | _ -> false
        let viewportExtentPx = float32 (max w h)
        let thickData = HalfPlaneGizmo.buildThickVertices ctx worldPerPx dragActive viewportExtentPx
        if thickData.Length > 0 then
            let buf = upload scene.Pool slots.TranslateGizmo thickData
            colorPass.setPipeline scene.TranslateGizmoPipeline
            colorPass.setBindGroup(0, scene.CameraBindGroup)
            colorPass.setBindGroup(1, scene.ViewportBindGroup)
            colorPass.setVertexBuffer(0, buf)
            colorPass.draw (thickData.Length / 13)
    | None -> ()

    colorPass.endPass()

    // Pick pass retired — the compute-shader picker in `PickCompute.fs`
    // replaces the raster pick render entirely. It dispatches its own
    // command buffer on demand (mouse events), so nothing pick-related
    // runs in this frame.

    // Submit + reclaim old buffers.
    scene.Device.queue.submit [| encoder.finish() |]
    markFrameSubmitted scene.Pool
    flushRetired scene.Pool
