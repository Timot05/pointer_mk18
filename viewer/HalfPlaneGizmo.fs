module HalfPlaneGizmo

open Server

type Context =
    { ActionId: ActionId
      AxisIndex: int
      AxisDir: Vec3
      Offset: float }

let AXIS_LENGTH_PX = 56.0f
let AXIS_THICKNESS_PX = 3.0f
let OFFSET_THICKNESS_PX = 4.0f
let ARROW_LENGTH_PX = 14.0f
let ARROW_WIDTH_PX = 9.0f
let OFFSET_MIN_LENGTH_PX = 28.0f
let DASH_MIN_EXTENT_PX = 600.0f
let DASH_CYCLE_PX = 12.0f
let DASH_THICKNESS_PX = 2.0f

let private cAxisX : float32[] = [| 0.88f; 0.42f; 0.42f; 1.0f |]
let private cAxisY : float32[] = [| 0.48f; 0.78f; 0.54f; 1.0f |]
let private cAxisZ : float32[] = [| 0.45f; 0.56f; 0.92f; 1.0f |]

let private axisColor axisIndex =
    match axisIndex with
    | 0 -> cAxisX
    | 1 -> cAxisY
    | _ -> cAxisZ

let private selectorColor axisIndex (selectedAxis: int) =
    let baseColor = axisColor axisIndex
    if axisIndex = selectedAxis then
        [| baseColor.[0]; baseColor.[1]; baseColor.[2]; 1.0f |]
    else
        [| baseColor.[0]; baseColor.[1]; baseColor.[2]; 0.55f |]

let localAxis axisIndex : Vec3 =
    match axisIndex with
    | 0 -> { X = 1.0; Y = 0.0; Z = 0.0 }
    | 1 -> { X = 0.0; Y = 1.0; Z = 0.0 }
    | _ -> { X = 0.0; Y = 0.0; Z = 1.0 }

let contextOf (state: EditorState) : Context option =
    match state.Doc.SelectedId with
    | None -> None
    | Some selId ->
        match state.Doc.Actions |> List.tryFind (fun a -> a.Id = selId) with
        | Some { Kind = HalfPlane _ } ->
            Some
                { ActionId = selId
                  AxisIndex = Editor.halfPlaneAxisIndex state selId
                  AxisDir = Editor.halfPlaneAxisDir state selId
                  Offset = Editor.halfPlaneOffsetValue state selId }
        | _ -> None

let ephemeralPickables (actionId: ActionId) (baseId: int) : Pickable list =
    [ PickGizmoHandle(baseId + 0, actionId, GHalfPlaneAxis 0)
      PickGizmoHandle(baseId + 1, actionId, GHalfPlaneAxis 1)
      PickGizmoHandle(baseId + 2, actionId, GHalfPlaneAxis 2)
      PickGizmoHandle(baseId + 3, actionId, GHalfPlaneOffset) ]

let ephemeralPickablesForState (state: EditorState) : Pickable list =
    match state.Doc.SelectedId with
    | Some id when state.Doc.Actions |> List.exists (fun a -> a.Id = id && (match a.Kind with HalfPlane _ -> true | _ -> false)) ->
        ephemeralPickables id state.Compiled.Pickables.Length
    | _ -> []

let private emitThick
        (out: ResizeArray<float32>)
        (anchor: Vec3) (dir: Vec3)
        (pxAlong: float32) (pxPerp: float32)
        (color: float32[])
        (dashScale: float32) =
    out.Add(float32 anchor.X)
    out.Add(float32 anchor.Y)
    out.Add(float32 anchor.Z)
    out.Add(float32 dir.X)
    out.Add(float32 dir.Y)
    out.Add(float32 dir.Z)
    out.Add pxAlong
    out.Add pxPerp
    out.Add color.[0]
    out.Add color.[1]
    out.Add color.[2]
    out.Add color.[3]
    out.Add dashScale

let private pushThickQuad
        (out: ResizeArray<float32>)
        (anchor: Vec3) (dir: Vec3)
        (pxStart: float32) (pxEnd: float32) (thicknessPx: float32)
        (color: float32[]) (dashScale: float32) =
    let h = thicknessPx * 0.5f
    emitThick out anchor dir pxStart -h color dashScale
    emitThick out anchor dir pxEnd -h color dashScale
    emitThick out anchor dir pxStart +h color dashScale
    emitThick out anchor dir pxEnd -h color dashScale
    emitThick out anchor dir pxEnd +h color dashScale
    emitThick out anchor dir pxStart +h color dashScale

let private pushArrow
        (out: ResizeArray<float32>)
        (anchor: Vec3) (dir: Vec3)
        (tipPx: float32) (widthPx: float32) (lengthPx: float32)
        (color: float32[]) =
    let basePx = tipPx - lengthPx
    let halfW = widthPx * 0.5f
    emitThick out anchor dir basePx -halfW color 0.0f
    emitThick out anchor dir tipPx 0.0f color 0.0f
    emitThick out anchor dir basePx +halfW color 0.0f

let private dashColor (color: float32[]) : float32[] =
    [| color.[0]; color.[1]; color.[2]; 0.85f |]

let displayedOffsetPx (ctx: Context) (worldPerPx: float) =
    let rawPx = float32 (abs ctx.Offset / max worldPerPx 1e-6)
    max OFFSET_MIN_LENGTH_PX rawPx

let offsetAnchor (ctx: Context) (worldPerPx: float) : Vec3 =
    (float AXIS_LENGTH_PX * worldPerPx) * ctx.AxisDir

let buildThickVertices (ctx: Context) (worldPerPx: float) (dragActive: bool) (viewportExtentPx: float32) : float32[] =
    let out = ResizeArray<float32>()
    for axisIndex in 0 .. 2 do
        let dir = localAxis axisIndex
        let color = selectorColor axisIndex ctx.AxisIndex
        let thickness = if axisIndex = ctx.AxisIndex then AXIS_THICKNESS_PX + 1.0f else AXIS_THICKNESS_PX
        pushThickQuad out Vec3.Zero dir 0.0f AXIS_LENGTH_PX thickness color 0.0f

    let axisColor = axisColor ctx.AxisIndex
    let dir = if ctx.Offset < 0.0 then (-1.0) * ctx.AxisDir else ctx.AxisDir
    let pxEnd = displayedOffsetPx ctx worldPerPx
    let anchor = offsetAnchor ctx worldPerPx
    pushThickQuad out anchor dir 0.0f pxEnd OFFSET_THICKNESS_PX axisColor 0.0f
    pushArrow out anchor dir (pxEnd + ARROW_LENGTH_PX) ARROW_WIDTH_PX ARROW_LENGTH_PX axisColor

    if dragActive then
        let dashExtentPx = max DASH_MIN_EXTENT_PX viewportExtentPx
        pushThickQuad out Vec3.Zero ctx.AxisDir (-dashExtentPx) dashExtentPx DASH_THICKNESS_PX (dashColor axisColor) DASH_CYCLE_PX

    out.ToArray()
