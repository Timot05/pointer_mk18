module RotateGizmo

open Server

type Context =
    { ActionId: ActionId
      Origin: Vec3
      AxisWorld: Vec3
      BasisU: Vec3
      BasisV: Vec3
      Angle: float }

let private axisColour : float32[] = [| 0.96f; 0.56f; 0.24f; 1.0f |]
let private angleColour : float32[] = [| 0.24f; 0.62f; 0.96f; 1.0f |]
let private armThicknessPx = 4.0f

let private worldAxisEnd (ctx: Context) (worldPerPx: float) =
    ctx.Origin + (Editor.rotateAxisHandlePx * worldPerPx) * ctx.AxisWorld

let private angleDir (ctx: Context) =
    ((cos ctx.Angle) * ctx.BasisU + (sin ctx.Angle) * ctx.BasisV).Normalized

let private worldAngleEnd (ctx: Context) (worldPerPx: float) =
    ctx.Origin + (Editor.rotateAngleHandlePx * worldPerPx) * (angleDir ctx)

let private emitThin
        (out: ResizeArray<float32>)
        (world: Vec3)
        (colour: float32[]) =
    out.Add(float32 world.X)
    out.Add(float32 world.Y)
    out.Add(float32 world.Z)
    out.Add(1.0f)
    out.Add(0.0f)
    out.Add(0.0f)
    out.Add(0.0f)
    out.Add(0.0f)
    out.Add(colour.[0])
    out.Add(colour.[1])
    out.Add(colour.[2])
    out.Add(colour.[3])

let private pushLine
        (out: ResizeArray<float32>)
        (a: Vec3)
        (b: Vec3)
        (colour: float32[]) =
    emitThin out a colour
    emitThin out b colour

let private pushAngleArc
        (out: ResizeArray<float32>)
        (ctx: Context)
        (worldPerPx: float) =
    let radius = Editor.rotateAngleHandlePx * worldPerPx
    let angle = ctx.Angle
    let steps =
        max 1 (min 48 (int (ceil ((abs angle / System.Math.PI) * 24.0))))
    let pt i =
        let t = (float i / float steps) * angle
        ctx.Origin + radius * ((cos t) * ctx.BasisU + (sin t) * ctx.BasisV)
    if abs angle > 1e-4 then
        for i in 0 .. steps - 1 do
            pushLine out (pt i) (pt (i + 1)) angleColour

let contextOf (state: EditorState) : Context option =
    match state.Doc.SelectedId with
    | None -> None
    | Some selId ->
        match state.Doc.Actions |> List.tryFind (fun a -> a.Id = selId) with
        | Some { Kind = Rotate _ } ->
            let xform = Editor.resolveActionTransform state selId
            let axisLocal = Editor.normalizedRotateAxisLocal state selId
            let axisWorld = xform.Rot.Rotate(axisLocal).Normalized
            let basisU, basisV = Editor.orthonormalBasisFromAxis axisWorld
            Some
                { ActionId = selId
                  Origin = xform.Trans
                  AxisWorld = axisWorld
                  BasisU = basisU
                  BasisV = basisV
                  Angle = Editor.rotateAngleValue state selId }
        | _ -> None

let handles : GizmoHandle[] =
    [| GRotateAxis; GRotateAngle |]

let ephemeralPickables (actionId: ActionId) (baseId: int) : Pickable list =
    handles
    |> Array.mapi (fun i h -> PickGizmoHandle(baseId + i, actionId, h))
    |> Array.toList

let ephemeralPickablesForState (state: EditorState) : Pickable list =
    match state.Doc.SelectedId with
    | Some id when state.Doc.Actions |> List.exists (fun a -> a.Id = id && (match a.Kind with Rotate _ -> true | _ -> false)) ->
        ephemeralPickables id state.Compiled.Pickables.Length
    | _ -> []

let buildLineVertices (ctx: Context) (worldPerPx: float) : float32[] =
    let out = ResizeArray<float32>()
    let axisEnd = worldAxisEnd ctx worldPerPx
    let angleEnd = worldAngleEnd ctx worldPerPx
    pushLine out ctx.Origin axisEnd axisColour
    pushAngleArc out ctx worldPerPx
    pushLine out ctx.Origin angleEnd angleColour
    out.ToArray()

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
        (color: float32[]) =
    let h = thicknessPx * 0.5f
    emitThick out anchor dir pxStart -h color 0.0f
    emitThick out anchor dir pxEnd   -h color 0.0f
    emitThick out anchor dir pxStart +h color 0.0f
    emitThick out anchor dir pxEnd   -h color 0.0f
    emitThick out anchor dir pxEnd   +h color 0.0f
    emitThick out anchor dir pxStart +h color 0.0f

let buildThickVertices (ctx: Context) : float32[] =
    let out = ResizeArray<float32>()
    pushThickQuad out ctx.Origin ctx.AxisWorld 0.0f (float32 Editor.rotateAxisHandlePx) armThicknessPx axisColour
    pushThickQuad out ctx.Origin (angleDir ctx) 0.0f (float32 Editor.rotateAngleHandlePx) armThicknessPx angleColour
    out.ToArray()

let buildPointVertices (ctx: Context) (worldPerPx: float) (activeHandle: GizmoHandle option) : float32[] =
    let out = ResizeArray<float32>()
    let push (pos: Vec3) (colour: float32[]) active =
        let radius = if active then 14.0f else 11.0f
        out.Add(float32 pos.X)
        out.Add(float32 pos.Y)
        out.Add(float32 pos.Z)
        out.Add(radius)
        out.Add(colour.[0])
        out.Add(colour.[1])
        out.Add(colour.[2])
        out.Add(colour.[3])
    push (worldAxisEnd ctx worldPerPx) axisColour (activeHandle = Some GRotateAxis)
    push (worldAngleEnd ctx worldPerPx) angleColour (activeHandle = Some GRotateAngle)
    out.ToArray()
