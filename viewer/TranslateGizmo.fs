module TranslateGizmo

// Interactive 3D translate gizmo — three axis arrows + three plane
// outlines, anchored at the selected Translate action's resolved
// world origin. Drag math is CPU-side (ray-axis / ray-plane
// projection) and commits realtime slot patches to the Translate's
// x/y/z fields (the same trick sketch-label drags use).
//
// Everything here is pure math + buffer building; state lives in
// the main Editor store, and input wiring lives in Input.fs.

open Server

type Handle =
    | AxisX | AxisY | AxisZ
    | PlaneXY | PlaneYZ | PlaneXZ

/// Snapshot of the selected translate gizmo's pose + values. Derived
/// from EditorState on demand — cheap enough not to cache.
type Context =
    { ActionId: ActionId
      /// World origin of the gizmo (= translate's resolved world position).
      Origin: Vec3
      /// World-space unit vectors for the local x / y / z axes.
      AxisX: Vec3
      AxisY: Vec3
      AxisZ: Vec3
      /// Current slot values, so drag can compute absolute new x/y/z.
      CurrentX: float
      CurrentY: float
      CurrentZ: float }

// ── Screen-size constants ─────────────────────────────────────────────
// All measured in CSS pixels at the current zoom. Converted to world
// distance via `world_per_px = (2 * viewHalfH) / viewportHeight`.

let AXIS_LENGTH_PX = 60.0f
/// Inner / outer corner of the plane outlines, along each of the two
/// plane axes. Keeping them off the origin means the plane handles
/// don't overlap the axis arrows.
let PLANE_NEAR_PX = 14.0f
let PLANE_FAR_PX = 40.0f
/// Hit thresholds in CSS pixels.
let AXIS_HIT_PX = 8.0
let PLANE_HIT_PADDING_PX = 3.0

// ── Colours (match the frame-gizmo convention) ───────────────────────
// Per CAD convention plane colour = colour of the axis perpendicular
// to the plane.

let private cAxisX : float32[] = [| 0.88f; 0.42f; 0.42f; 1.0f |]
let private cAxisY : float32[] = [| 0.48f; 0.78f; 0.54f; 1.0f |]
let private cAxisZ : float32[] = [| 0.45f; 0.56f; 0.92f; 1.0f |]
let private cPlaneXY = cAxisZ
let private cPlaneYZ = cAxisX
let private cPlaneXZ = cAxisY

// ── Context ──────────────────────────────────────────────────────────

let private readSlot (state: EditorState) (actionId: ActionId) (path: string) : float =
    match SlotTable.tryFindSlot state.Compiled.Slots { ActionId = actionId; Path = path } with
    | Some slot when slot < state.SlotValues.Length -> state.SlotValues.[slot]
    | _ -> 0.0

/// Returns the gizmo context when the selected action is a Translate
/// (and its frame chain has been compiled). None otherwise.
let contextOf (state: EditorState) : Context option =
    match state.Doc.SelectedId with
    | None -> None
    | Some selId ->
        match state.Doc.Actions |> List.tryFind (fun a -> a.Id = selId) with
        | Some { Kind = Translate _ } ->
            match Map.tryFind selId state.Compiled.Frames with
            | Some chain ->
                let xform = Frames.foldChain state.Compiled.Slots state.SlotValues chain
                let rot = xform.Rot
                Some
                    { ActionId = selId
                      Origin = xform.Trans
                      AxisX = rot.Rotate({ X = 1.0; Y = 0.0; Z = 0.0 })
                      AxisY = rot.Rotate({ X = 0.0; Y = 1.0; Z = 0.0 })
                      AxisZ = rot.Rotate({ X = 0.0; Y = 0.0; Z = 1.0 })
                      CurrentX = readSlot state selId "x"
                      CurrentY = readSlot state selId "y"
                      CurrentZ = readSlot state selId "z" }
            | None -> None
        | _ -> None

// ── Vertex buffer for the existing Gizmo.wgsl pipeline ───────────────
//
// Format (per vertex, 12 floats):
//   origin (vec3), axis (vec3), axis_px (f32), endpoint (f32), color (vec4)
// The shader computes world = origin + axis * axis_px * world_per_px * endpoint.
//
// Axes are rendered with `axis_px = AXIS_LENGTH_PX`, endpoint 0/1.
// Plane outlines use `axis_px = 0` and pass pre-computed world
// positions directly as `origin` — so we need the current world-per-px
// here to place plane corners at a screen-constant size.

let private emit
        (out: ResizeArray<float32>)
        (origin: Vec3) (axis: Vec3) (axisPx: float32) (endpoint: float32)
        (color: float32[]) =
    out.Add(float32 origin.X)
    out.Add(float32 origin.Y)
    out.Add(float32 origin.Z)
    out.Add(float32 axis.X)
    out.Add(float32 axis.Y)
    out.Add(float32 axis.Z)
    out.Add axisPx
    out.Add endpoint
    out.Add color.[0]
    out.Add color.[1]
    out.Add color.[2]
    out.Add color.[3]

let private pushAxis (out: ResizeArray<float32>) (ctx: Context) (axis: Vec3) (color: float32[]) =
    emit out ctx.Origin axis AXIS_LENGTH_PX 0.0f color
    emit out ctx.Origin axis AXIS_LENGTH_PX 1.0f color

let private corner (origin: Vec3) (u: Vec3) (v: Vec3) (pxU: float) (pxV: float) (worldPerPx: float) : Vec3 =
    let du = pxU * worldPerPx
    let dv = pxV * worldPerPx
    { X = origin.X + du * u.X + dv * v.X
      Y = origin.Y + du * u.Y + dv * v.Y
      Z = origin.Z + du * u.Z + dv * v.Z }

let private pushPlaneEdge (out: ResizeArray<float32>) (a: Vec3) (b: Vec3) (color: float32[]) =
    // axis_px = 0 → world = origin. Pass the two endpoints as `origin`.
    let zeroAxis : Vec3 = { X = 1.0; Y = 0.0; Z = 0.0 }
    emit out a zeroAxis 0.0f 0.0f color
    emit out b zeroAxis 0.0f 0.0f color

let private pushPlane
        (out: ResizeArray<float32>) (ctx: Context)
        (axisU: Vec3) (axisV: Vec3) (color: float32[]) (worldPerPx: float) =
    let c00 = corner ctx.Origin axisU axisV (float PLANE_NEAR_PX) (float PLANE_NEAR_PX) worldPerPx
    let c10 = corner ctx.Origin axisU axisV (float PLANE_FAR_PX) (float PLANE_NEAR_PX) worldPerPx
    let c11 = corner ctx.Origin axisU axisV (float PLANE_FAR_PX) (float PLANE_FAR_PX) worldPerPx
    let c01 = corner ctx.Origin axisU axisV (float PLANE_NEAR_PX) (float PLANE_FAR_PX) worldPerPx
    pushPlaneEdge out c00 c10 color
    pushPlaneEdge out c10 c11 color
    pushPlaneEdge out c11 c01 color
    pushPlaneEdge out c01 c00 color

/// Vertex data (line-list) for axes + plane outlines. Empty when no
/// gizmo should show.
let buildVertices (ctx: Context) (worldPerPx: float) : float32[] =
    let out = ResizeArray<float32>()
    pushAxis out ctx ctx.AxisX cAxisX
    pushAxis out ctx ctx.AxisY cAxisY
    pushAxis out ctx ctx.AxisZ cAxisZ
    pushPlane out ctx ctx.AxisX ctx.AxisY cPlaneXY worldPerPx
    pushPlane out ctx ctx.AxisY ctx.AxisZ cPlaneYZ worldPerPx
    pushPlane out ctx ctx.AxisX ctx.AxisZ cPlaneXZ worldPerPx
    out.ToArray()

// ── CPU picker ────────────────────────────────────────────────────────
//
// Runs before the GPU picker on mousedown. Projects axis endpoints and
// plane corners to screen space via Camera.worldToScreen, then does
// distance-to-segment / point-in-quad tests in CSS pixels.

let private worldPerPixel (camera: Camera.CameraState) (canvasH: float) : float =
    (2.0 * Camera.viewHalfH camera) / max canvasH 1.0

let private axisScreenEndpoints
        (camera: Camera.CameraState) (canvasW: float) (canvasH: float)
        (ctx: Context) (axis: Vec3) : ((float * float) * (float * float)) option =
    let wpp = worldPerPixel camera canvasH
    let tip =
        { X = ctx.Origin.X + float AXIS_LENGTH_PX * wpp * axis.X
          Y = ctx.Origin.Y + float AXIS_LENGTH_PX * wpp * axis.Y
          Z = ctx.Origin.Z + float AXIS_LENGTH_PX * wpp * axis.Z }
    match Camera.worldToScreen canvasW canvasH camera ctx.Origin,
          Camera.worldToScreen canvasW canvasH camera tip with
    | Some a, Some b -> Some (a, b)
    | _ -> None

let private hitAxis (mx: float) (my: float) (a: float * float) (b: float * float) : bool =
    let ax, ay = a
    let bx, by = b
    let dx = bx - ax
    let dy = by - ay
    let len2 = dx * dx + dy * dy
    if len2 < 1e-9 then false
    else
        let t = ((mx - ax) * dx + (my - ay) * dy) / len2
        let tc = max 0.0 (min 1.0 t)
        let cx = ax + tc * dx
        let cy = ay + tc * dy
        let ex = mx - cx
        let ey = my - cy
        sqrt (ex * ex + ey * ey) <= AXIS_HIT_PX

let private planeScreenCorners
        (camera: Camera.CameraState) (canvasW: float) (canvasH: float)
        (ctx: Context) (u: Vec3) (v: Vec3) : (float * float) list option =
    let wpp = worldPerPixel camera canvasH
    let c00 = corner ctx.Origin u v (float PLANE_NEAR_PX) (float PLANE_NEAR_PX) wpp
    let c10 = corner ctx.Origin u v (float PLANE_FAR_PX) (float PLANE_NEAR_PX) wpp
    let c11 = corner ctx.Origin u v (float PLANE_FAR_PX) (float PLANE_FAR_PX) wpp
    let c01 = corner ctx.Origin u v (float PLANE_NEAR_PX) (float PLANE_FAR_PX) wpp
    let opts =
        [ c00; c10; c11; c01 ]
        |> List.map (Camera.worldToScreen canvasW canvasH camera)
    if opts |> List.forall Option.isSome then
        Some (opts |> List.map Option.get)
    else None

/// Point-in-convex-quad test via cross-product sign consistency.
let private hitQuad (mx: float) (my: float) (corners: (float * float) list) : bool =
    match corners with
    | [ (x0, y0); (x1, y1); (x2, y2); (x3, y3) ] ->
        let side (ax, ay) (bx, by) =
            (bx - ax) * (my - ay) - (by - ay) * (mx - ax)
        let s1 = side (x0, y0) (x1, y1)
        let s2 = side (x1, y1) (x2, y2)
        let s3 = side (x2, y2) (x3, y3)
        let s4 = side (x3, y3) (x0, y0)
        (s1 >= -PLANE_HIT_PADDING_PX && s2 >= -PLANE_HIT_PADDING_PX && s3 >= -PLANE_HIT_PADDING_PX && s4 >= -PLANE_HIT_PADDING_PX)
        || (s1 <= PLANE_HIT_PADDING_PX && s2 <= PLANE_HIT_PADDING_PX && s3 <= PLANE_HIT_PADDING_PX && s4 <= PLANE_HIT_PADDING_PX)
    | _ -> false

/// Which handle (if any) is under the cursor. Axes take priority over
/// planes so a click near where an axis crosses a plane outline picks
/// the axis — the narrower target wins.
let pick
        (ctx: Context) (camera: Camera.CameraState)
        (canvasW: float) (canvasH: float)
        (mx: float) (my: float) : Handle option =
    let tryAxis axisVec handle =
        match axisScreenEndpoints camera canvasW canvasH ctx axisVec with
        | Some (a, b) when hitAxis mx my a b -> Some handle
        | _ -> None
    let tryPlane u v handle =
        match planeScreenCorners camera canvasW canvasH ctx u v with
        | Some corners when hitQuad mx my corners -> Some handle
        | _ -> None
    tryAxis ctx.AxisX AxisX
    |> Option.orElseWith (fun () -> tryAxis ctx.AxisY AxisY)
    |> Option.orElseWith (fun () -> tryAxis ctx.AxisZ AxisZ)
    |> Option.orElseWith (fun () -> tryPlane ctx.AxisX ctx.AxisY PlaneXY)
    |> Option.orElseWith (fun () -> tryPlane ctx.AxisY ctx.AxisZ PlaneYZ)
    |> Option.orElseWith (fun () -> tryPlane ctx.AxisX ctx.AxisZ PlaneXZ)

// ── Drag math ─────────────────────────────────────────────────────────
//
// Drag state carries the info captured at drag-begin so each mousemove
// can compute absolute new x/y/z without relying on prior deltas.

type DragKind =
    | DragAxis of axis: Vec3  // world-space unit vector
    | DragPlane of u: Vec3 * v: Vec3  // world-space unit vectors

type DragContext =
    { ActionId: ActionId
      Handle: Handle
      Kind: DragKind
      /// Gizmo origin in world at drag start.
      AnchorWorld: Vec3
      /// x/y/z at drag start.
      InitialX: float
      InitialY: float
      InitialZ: float
      /// Scalar (axis) or (u, v) (plane) of the initial cursor ray
      /// projected onto the drag surface. Updates apply the delta
      /// from this anchor.
      InitialProj: float * float }

let private axisRayProj (ray: Camera.Ray) (anchor: Vec3) (axis: Vec3) : float option =
    // Scalar t of the closest point on the axis line (through `anchor`)
    // to `ray`. None when the axis is parallel to the ray.
    // Standard formulation: for lines P(s) = anchor + s*u and
    // Q(t) = rayOrigin + t*v, with w = anchor - rayOrigin, the axis
    // parameter is s = (b·e - c·d) / (a·c - b²) where a=u·u, b=u·v,
    // c=v·v, d=u·w, e=v·w.  (Getting the sign of `w` wrong negates s —
    // i.e. drags the axis the wrong way.)
    let w = anchor - ray.Origin
    let a = Vec3.Dot(axis, axis)
    let b = Vec3.Dot(axis, ray.Direction)
    let c = Vec3.Dot(ray.Direction, ray.Direction)
    let d = Vec3.Dot(axis, w)
    let e = Vec3.Dot(ray.Direction, w)
    let denom = a * c - b * b
    if abs denom < 1e-9 then None
    else
        Some ((b * e - c * d) / denom)

/// Given the current mouse ray, return the new absolute x/y/z to patch.
/// None when the math is degenerate (axis parallel to view, etc.).
let applyDrag (drag: DragContext) (ray: Camera.Ray) : (float * float * float) option =
    match drag.Kind with
    | DragAxis axis ->
        match axisRayProj ray drag.AnchorWorld axis with
        | Some t ->
            let (t0, _) = drag.InitialProj
            let dt = t - t0
            match drag.Handle with
            | AxisX -> Some (drag.InitialX + dt, drag.InitialY, drag.InitialZ)
            | AxisY -> Some (drag.InitialX, drag.InitialY + dt, drag.InitialZ)
            | AxisZ -> Some (drag.InitialX, drag.InitialY, drag.InitialZ + dt)
            | _ -> None
        | None -> None
    | DragPlane(u, v) ->
        match Camera.rayPlaneIntersection ray drag.AnchorWorld u v with
        | Some (pu, pv) ->
            let (u0, v0) = drag.InitialProj
            let du = pu - u0
            let dv = pv - v0
            match drag.Handle with
            | PlaneXY -> Some (drag.InitialX + du, drag.InitialY + dv, drag.InitialZ)
            | PlaneYZ -> Some (drag.InitialX, drag.InitialY + du, drag.InitialZ + dv)
            | PlaneXZ -> Some (drag.InitialX + du, drag.InitialY, drag.InitialZ + dv)
            | _ -> None
        | None -> None

/// Build a DragContext capturing the starting projection. Called from
/// Input.fs on mousedown when `pick` returns a handle.
let beginDrag
        (ctx: Context) (handle: Handle)
        (ray: Camera.Ray) : DragContext option =
    let kind, initialProj =
        match handle with
        | AxisX ->
            DragAxis ctx.AxisX,
            axisRayProj ray ctx.Origin ctx.AxisX |> Option.map (fun t -> t, 0.0)
        | AxisY ->
            DragAxis ctx.AxisY,
            axisRayProj ray ctx.Origin ctx.AxisY |> Option.map (fun t -> t, 0.0)
        | AxisZ ->
            DragAxis ctx.AxisZ,
            axisRayProj ray ctx.Origin ctx.AxisZ |> Option.map (fun t -> t, 0.0)
        | PlaneXY ->
            DragPlane(ctx.AxisX, ctx.AxisY),
            Camera.rayPlaneIntersection ray ctx.Origin ctx.AxisX ctx.AxisY
        | PlaneYZ ->
            DragPlane(ctx.AxisY, ctx.AxisZ),
            Camera.rayPlaneIntersection ray ctx.Origin ctx.AxisY ctx.AxisZ
        | PlaneXZ ->
            DragPlane(ctx.AxisX, ctx.AxisZ),
            Camera.rayPlaneIntersection ray ctx.Origin ctx.AxisX ctx.AxisZ
    match initialProj with
    | Some proj ->
        Some
            { ActionId = ctx.ActionId
              Handle = handle
              Kind = kind
              AnchorWorld = ctx.Origin
              InitialX = ctx.CurrentX
              InitialY = ctx.CurrentY
              InitialZ = ctx.CurrentZ
              InitialProj = proj }
    | None -> None
