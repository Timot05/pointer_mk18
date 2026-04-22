module Input

// Mouse/wheel/dblclick handlers. Each handler dispatches AppStore messages
// or mutates camera state. Shared state (drag, pick-in-flight, pickables,
// tool cursor) lives in refs inside `install`.

open Fable.Core
open Fable.Core.JsInterop
open Browser.Dom
open Browser.Types
open Server
open PointerMk18.Ui
open WebGPU

[<Emit("$0.clientX")>]
let private eClientX (e: obj) : float = jsNative
[<Emit("$0.clientY")>]
let private eClientY (e: obj) : float = jsNative
[<Emit("$0.deltaY")>]
let private eDeltaY (e: obj) : float = jsNative
[<Emit("$0.button")>]
let private eButton (e: obj) : int = jsNative
[<Emit("$0.preventDefault()")>]
let private ePreventDefault (e: obj) : unit = jsNative
[<Emit("$0.addEventListener($1, $2, { passive: false })")>]
let private addEventPassiveFalse (target: obj) (name: string) (h: obj -> unit) : unit = jsNative
[<Emit("$0.addEventListener($1, $2)")>]
let private addEvent (target: obj) (name: string) (h: obj -> unit) : unit = jsNative

let private DRAG_THRESHOLD_PX = 4.0

/// Hooks the viewer needs to expose back to the input subsystem. `PickAt`
/// triggers the 1-pixel GPU readback; `ToolCursor` is updated by this
/// module and read by the render loop.
type Hooks =
    { PickAt: int -> int -> JS.Promise<uint32>
      PickableById: unit -> Map<int, Pickable>
      ToolCursor: (ActionId * float * float) option ref }

let private sketchPlane (sketchId: ActionId) : (Vec3 * Vec3 * Vec3) option =
    let state = AppStore.store.State
    let viewState = ViewerPipeline.viewerState state
    viewState.SketchTransforms
    |> List.tryFind (fun f -> f.Id = sketchId)
    |> Option.map (fun f ->
        let origin = f.Transform.Trans
        let xAxis = f.Transform.Rot.Rotate({ X = 1.0; Y = 0.0; Z = 0.0 })
        let yAxis = f.Transform.Rot.Rotate({ X = 0.0; Y = 1.0; Z = 0.0 })
        origin, xAxis, yAxis)

let private mouseToSketchLocal
        (canvas: HTMLCanvasElement) (dpr: float) (camera: Camera.CameraState)
        (sketchId: ActionId) (mx: float) (my: float) : (float * float) option =
    match sketchPlane sketchId with
    | None -> None
    | Some (origin, xAxis, yAxis) ->
        let rect = canvas?getBoundingClientRect ()
        let localX = (mx - rect?left) * dpr
        let localY = (my - rect?top) * dpr
        let w = canvas.clientWidth * dpr
        let h = canvas.clientHeight * dpr
        let ray = Camera.screenToRay w h camera localX localY
        Camera.rayPlaneIntersection ray origin xAxis yAxis

let private activeEditSketchId (state: EditorState) : ActionId option =
    let vs = ViewerPipeline.viewerState state
    if not vs.SketchUi.EditMode then None
    else
        state.Doc.SelectedId
        |> Option.filter (fun id ->
            vs.SketchTransforms |> List.exists (fun t -> t.Id = id))

/// Install mouse/dblclick/wheel handlers on the canvas. The `hooks` carry
/// the async GPU pick (which the render module can't expose without
/// leaking its pass machinery) and the tool-cursor ref shared with the
/// render loop.
let install
        (canvas: HTMLCanvasElement)
        (dpr: float)
        (camera: Camera.CameraState)
        (hooks: Hooks) : unit =
    let selectionIntent (e: obj) =
        if e?shiftKey || e?metaKey || e?ctrlKey then "toggle" else "replace"

    let mutable dragButton : int option = None
    let mutable dragStart : float * float = 0.0, 0.0
    let mutable dragLast : float * float = 0.0, 0.0
    let mutable dragPickable : Pickable option = None
    let mutable dragActive = false

    let pickableById () = hooks.PickableById ()
    let toSketchLocal sid mx my = mouseToSketchLocal canvas dpr camera sid mx my

    addEventPassiveFalse canvas "mousedown" (fun e ->
        let button = eButton e
        if button = 1 then ePreventDefault e
        let x, y = eClientX e, eClientY e
        dragButton <- Some button
        dragStart <- x, y
        dragLast <- x, y
        dragPickable <- None
        dragActive <- false
        if button = 0 then
            let state = AppStore.store.State
            let viewState = ViewerPipeline.viewerState state
            let toolActive =
                viewState.SketchUi.EditMode
                && viewState.SketchUi.Tool <> "none"
                && viewState.SketchUi.Tool <> ""
                && viewState.SketchUi.Tool <> "select"
            let placementActive =
                viewState.SketchUi.EditMode
                && viewState.SketchUi.ConstraintPlacementMode.IsSome

            let rect = canvas?getBoundingClientRect ()
            let px = int ((x - rect?left) * dpr)
            let py = int ((y - rect?top) * dpr)
            hooks.PickAt px py
            |> Promise.iter (fun id ->
                if placementActive then
                    dragPickable <- None
                    let hovered =
                        if id = 0u then None
                        else Map.tryFind (int id - 1) (pickableById ())
                    let isTargetable =
                        match hovered with
                        | Some (PickPoint _) | Some (PickLine _)
                        | Some (PickCircle _) | Some (PickArc _)
                        | Some (PickFrameOrigin _) -> true
                        | _ -> false
                    if isTargetable then
                        let pickId = int id - 1
                        Store.dispatch AppStore.store
                            (ViewerHover [ { PickId = pickId; Score = 0.0f } ])
                        Store.dispatch AppStore.store ViewerDimensionClickTarget
                    else
                        let latest = ViewerPipeline.viewerState AppStore.store.State
                        match latest.SketchUi.PendingConstraintPlacement, hooks.ToolCursor.Value with
                        | Some _, Some (_, u, v) ->
                            Store.dispatch AppStore.store (ViewerPlaceConstraint(u, v))
                        | _ -> ()
                elif toolActive then
                    dragPickable <- None
                    match hooks.ToolCursor.Value with
                    | Some (_, u, v) ->
                        Store.dispatch AppStore.store (ViewerToolClick(u, v))
                    | None -> ()
                elif id = 0u then
                    dragPickable <- None
                    Store.dispatch AppStore.store (ViewerPick(selectionIntent e, []))
                else
                    let pickId = int id - 1
                    dragPickable <- Map.tryFind pickId (pickableById ())
                    Store.dispatch AppStore.store
                        (ViewerPick(selectionIntent e, [ { PickId = pickId; Score = 0.0f } ]))))

    addEvent canvas "dblclick" (fun e ->
        ePreventDefault e
        let rect = canvas?getBoundingClientRect ()
        let px = int ((eClientX e - rect?left) * dpr)
        let py = int ((eClientY e - rect?top) * dpr)
        hooks.PickAt px py
        |> Promise.iter (fun id ->
            if id <> 0u then
                let pickId = int id - 1
                match Map.tryFind pickId (pickableById ()) with
                | Some (PickDimension(_, sid, idx, _)) ->
                    let vs = ViewerPipeline.viewerState AppStore.store.State
                    if not vs.SketchUi.EditMode then
                        Store.dispatch AppStore.store (SelectAction sid)
                        Store.dispatch AppStore.store ToggleSketchEdit
                    Store.dispatch AppStore.store (StartEditingDimension idx)
                | Some p ->
                    let sketchIdOpt =
                        match p with
                        | PickPoint(_, sid, _, _, _)
                        | PickLine(_, sid, _, _, _)
                        | PickCircle(_, sid, _, _, _)
                        | PickArc(_, sid, _, _, _, _, _)
                        | PickLoop(_, sid, _, _) -> Some sid
                        | _ -> None
                    match sketchIdOpt with
                    | Some sid ->
                        let vs = ViewerPipeline.viewerState AppStore.store.State
                        Store.dispatch AppStore.store (SelectAction sid)
                        if not vs.SketchUi.EditMode then
                            Store.dispatch AppStore.store ToggleSketchEdit
                    | None -> ()
                | None -> ()))

    addEvent window "mousemove" (fun e ->
        let state = AppStore.store.State
        match activeEditSketchId state with
        | Some sid ->
            let mx, my = eClientX e, eClientY e
            match toSketchLocal sid mx my with
            | Some (u, v) ->
                hooks.ToolCursor.Value <- Some (sid, u, v)
                let vs = ViewerPipeline.viewerState state
                if vs.SketchUi.PendingConstraintPlacement.IsSome then
                    Store.dispatch AppStore.store
                        (SetConstraintPlacementCursor (Some (sid, { X = u; Y = v })))
            | None -> ()
        | None -> hooks.ToolCursor.Value <- None

        // Hover dispatch when no drag is in progress.
        if dragButton.IsNone then
            let rect = canvas?getBoundingClientRect ()
            let px = int ((eClientX e - rect?left) * dpr)
            let py = int ((eClientY e - rect?top) * dpr)
            let w : int = canvas?width
            let h : int = canvas?height
            if px >= 0 && py >= 0 && px < w && py < h then
                hooks.PickAt px py
                |> Promise.iter (fun id ->
                    if id = 0u then
                        Store.dispatch AppStore.store (ViewerHover [])
                    else
                        let pickId = int id - 1
                        Store.dispatch AppStore.store
                            (ViewerHover [ { PickId = pickId; Score = 0.0f } ]))

        match dragButton with
        | None -> ()
        | Some button ->
            let x, y = eClientX e, eClientY e
            let (lx, ly) = dragLast
            let dx = x - lx
            let dy = y - ly
            let (sx, sy) = dragStart
            let movedPx = sqrt ((x - sx) * (x - sx) + (y - sy) * (y - sy))

            let beginPointDrag sid pid u v =
                BeginSketchDrag
                    { SketchId = sid
                      Kind = DragPoint pid
                      XField = SketchEntityField(pid, PointX)
                      YField = SketchEntityField(pid, PointY)
                      Target = { X = u; Y = v } }
            let beginConstraintLabelDrag sid cix u v =
                BeginSketchDrag
                    { SketchId = sid
                      Kind = DragConstraintLabel cix
                      XField = SketchConstraintField(cix, ConstraintLabelX)
                      YField = SketchConstraintField(cix, ConstraintLabelY)
                      Target = { X = u; Y = v } }
            let updateDragTo u v = UpdateSketchDragTarget { X = u; Y = v }

            match button, dragPickable with
            | 0, Some (PickPoint(_, sid, pid, _, _)) ->
                if not dragActive && movedPx > DRAG_THRESHOLD_PX then
                    match toSketchLocal sid x y with
                    | Some (u, v) ->
                        dragActive <- true
                        Store.dispatch AppStore.store (beginPointDrag sid pid u v)
                    | None -> ()
                elif dragActive then
                    match toSketchLocal sid x y with
                    | Some (u, v) -> Store.dispatch AppStore.store (updateDragTo u v)
                    | None -> ()
            | 0, Some (PickDimension(_, sid, cix, _)) ->
                if not dragActive && movedPx > DRAG_THRESHOLD_PX then
                    match toSketchLocal sid x y with
                    | Some (u, v) ->
                        dragActive <- true
                        Store.dispatch AppStore.store (beginConstraintLabelDrag sid cix u v)
                    | None -> ()
                elif dragActive then
                    match toSketchLocal sid x y with
                    | Some (u, v) -> Store.dispatch AppStore.store (updateDragTo u v)
                    | None -> ()
            | 1, _ -> Camera.pan camera dx dy (canvas.clientHeight * dpr)
            | 2, _ -> Camera.orbit camera dx dy
            | _ -> ()

            dragLast <- x, y)

    addEvent window "mouseup" (fun _ ->
        if dragActive then
            Store.dispatch AppStore.store FinishSketchDrag
        dragButton <- None
        dragPickable <- None
        dragActive <- false)

    addEventPassiveFalse canvas "contextmenu" (fun e -> ePreventDefault e)

    addEventPassiveFalse canvas "wheel" (fun e ->
        ePreventDefault e
        let dy = eDeltaY e
        let rect = canvas?getBoundingClientRect ()
        let localX = (eClientX e - rect?left) * dpr
        let localY = (eClientY e - rect?top) * dpr
        Camera.zoomTowardsPointer camera
            (canvas.clientWidth * dpr) (canvas.clientHeight * dpr)
            localX localY dy)
