module TranslateGizmo

// Translate-gizmo rendering only. Pick hit-testing runs on the GPU
// through `PickCompute`, and pointer-drag state + slot patching live
// in the core `SceneInteraction` reducer (see Editor.fs). This module
// builds the per-frame vertex buffers that draw the handles, plus a
// small helper that produces the *ephemeral* pickables the GPU picker
// needs to hit-test them.

open Server

/// Snapshot of the selected translate gizmo's pose + current values.
/// Derived from EditorState on demand — cheap enough not to cache.
type Context =
    { ActionId: ActionId
      /// World origin of the gizmo (= translate's resolved world position).
      Origin: Vec3
      /// World-space unit vectors for the local x / y / z axes.
      AxisX: Vec3
      AxisY: Vec3
      AxisZ: Vec3 }

// ── Screen-size constants ─────────────────────────────────────────────
// All measured in CSS pixels at the current zoom. Converted to world
// distance via `world_per_px = (2 * viewHalfH) / viewportHeight`.

/// Axis shaft length in CSS pixels (origin → base of arrowhead).
let AXIS_LENGTH_PX = 72.0f
let AXIS_THICKNESS_PX = 3.0f
/// Arrowhead at the tip.
let ARROW_LENGTH_PX = 14.0f
let ARROW_WIDTH_PX = 9.0f
/// Inner / outer corners of the plane handles along each of the two
/// plane axes. Keeping them off the origin means the plane quads don't
/// overlap the axis shafts.
let PLANE_NEAR_PX = 16.0f
let PLANE_FAR_PX = 44.0f
/// Minimum dashed guide half-extent shown while an axis is being dragged.
/// Render passes the current viewport size so the actual guide spans at
/// least the full screen.
let DASH_MIN_EXTENT_PX = 600.0f
let DASH_CYCLE_PX = 12.0f
let DASH_THICKNESS_PX = 2.0f
/// Pick hit tolerances — used by the GPU pick shader that runs the
/// same axis-segment / plane-quad SDFs.
let AXIS_HIT_PX = 10.0f
let PLANE_HIT_PADDING_PX = 3.0f

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

/// Returns the gizmo context when the selected action is a Translate.
/// Delegates transform resolution to `Editor.resolveActionTransform`
/// so the same math is used by the core drag reducer.
let contextOf (state: EditorState) : Context option =
    match state.Doc.SelectedId with
    | None -> None
    | Some selId ->
        match state.Doc.Actions |> List.tryFind (fun a -> a.Id = selId) with
        | Some { Kind = Translate _ } ->
            let xform = Editor.resolveActionTransform state selId
            let rot = xform.Rot
            Some
                { ActionId = selId
                  Origin = xform.Trans
                  AxisX = rot.Rotate({ X = 1.0; Y = 0.0; Z = 0.0 })
                  AxisY = rot.Rotate({ X = 0.0; Y = 1.0; Z = 0.0 })
                  AxisZ = rot.Rotate({ X = 0.0; Y = 0.0; Z = 1.0 }) }
        | _ -> None

// ── Ephemeral pickables for the GPU pick shader ──────────────────────
//
// The gizmo's handles aren't part of the compiled pickable list (it's
// topology-only). Each frame the viewer merges this short list into
// its pick-id map so the compute-shader pick candidates resolve
// cleanly back to `Pickable.PickGizmoHandle`.

/// The six handles in a stable order. Index into this array is also
/// the offset added to the pick-id base to build each handle's
/// ephemeral id.
let handles : GizmoHandle[] =
    [| GAxis 0; GAxis 1; GAxis 2
       GPlane 0; GPlane 1; GPlane 2 |]

/// Ephemeral pickables for the selected translate action. `baseId` is
/// the first free pick id (typically `state.Compiled.Pickables.Length`).
let ephemeralPickables (actionId: ActionId) (baseId: int) : Pickable list =
    handles
    |> Array.mapi (fun i h -> PickGizmoHandle(baseId + i, actionId, h))
    |> Array.toList

/// Convenience wrapper that pulls the selected Translate action + id
/// base from the editor state. Empty when no Translate is selected.
let ephemeralPickablesForState (state: EditorState) : Pickable list =
    match state.Doc.SelectedId with
    | Some id when state.Doc.Actions |> List.exists (fun a -> a.Id = id && (match a.Kind with Translate _ -> true | _ -> false)) ->
        ephemeralPickables id state.Compiled.Pickables.Length
    | _ -> []

// ── Vertex buffers ───────────────────────────────────────────────────
//
// Two draws per frame:
//   1. Plane outlines use the existing Gizmo.wgsl line-list pipeline
//      — 12 floats per vertex, axis_px = 0, world positions encoded
//      directly as `origin`.
//   2. Axes + arrowheads + dashed drag guide use the new
//      `TranslateGizmoThick` pipeline — 13 floats per vertex
//      (triangle-list), generating camera-facing thick quads from
//      anchor + direction + 2D pixel offsets. See
//      `viewer/Shaders/TranslateGizmoThick.wgsl`.

// ── Plane outlines (thin pipeline) ───────────────────────────────────

let private emitThin
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

let private corner (origin: Vec3) (u: Vec3) (v: Vec3) (pxU: float) (pxV: float) (worldPerPx: float) : Vec3 =
    let du = pxU * worldPerPx
    let dv = pxV * worldPerPx
    { X = origin.X + du * u.X + dv * v.X
      Y = origin.Y + du * u.Y + dv * v.Y
      Z = origin.Z + du * u.Z + dv * v.Z }

let private pushPlaneEdge (out: ResizeArray<float32>) (a: Vec3) (b: Vec3) (color: float32[]) =
    let zeroAxis : Vec3 = { X = 1.0; Y = 0.0; Z = 0.0 }
    emitThin out a zeroAxis 0.0f 0.0f color
    emitThin out b zeroAxis 0.0f 0.0f color

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

/// Vertex data for plane outlines (line-list, shared Gizmo.wgsl format).
let buildThinVertices (ctx: Context) (worldPerPx: float) : float32[] =
    let out = ResizeArray<float32>()
    pushPlane out ctx ctx.AxisX ctx.AxisY cPlaneXY worldPerPx
    pushPlane out ctx ctx.AxisY ctx.AxisZ cPlaneYZ worldPerPx
    pushPlane out ctx ctx.AxisX ctx.AxisZ cPlaneXZ worldPerPx
    out.ToArray()

// ── Axes + arrowheads + drag guide (thick pipeline) ──────────────────
//
// Each vertex is 13 floats:
//   anchor(xyz) dir(xyz) offset(xy: along, perp) color(rgba) dash_scale

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

/// Thick rectangle between `pxStart..pxEnd` along `dir`, `thicknessPx`
/// wide (camera-facing). 6 vertices = 2 triangles.
let private pushThickQuad
        (out: ResizeArray<float32>)
        (anchor: Vec3) (dir: Vec3)
        (pxStart: float32) (pxEnd: float32) (thicknessPx: float32)
        (color: float32[]) (dashScale: float32) =
    let h = thicknessPx * 0.5f
    emitThick out anchor dir pxStart -h color dashScale
    emitThick out anchor dir pxEnd   -h color dashScale
    emitThick out anchor dir pxStart +h color dashScale
    emitThick out anchor dir pxEnd   -h color dashScale
    emitThick out anchor dir pxEnd   +h color dashScale
    emitThick out anchor dir pxStart +h color dashScale

/// Flat camera-facing arrowhead — 3 vertices.
let private pushArrow
        (out: ResizeArray<float32>)
        (anchor: Vec3) (dir: Vec3)
        (tipPx: float32) (widthPx: float32) (lengthPx: float32)
        (color: float32[]) =
    let basePx = tipPx - lengthPx
    let halfW = widthPx * 0.5f
    emitThick out anchor dir basePx -halfW color 0.0f
    emitThick out anchor dir tipPx   0.0f   color 0.0f
    emitThick out anchor dir basePx +halfW color 0.0f

let private pushAxisShaft
        (out: ResizeArray<float32>)
        (ctx: Context) (dir: Vec3) (color: float32[]) =
    pushThickQuad out ctx.Origin dir 0.0f AXIS_LENGTH_PX AXIS_THICKNESS_PX color 0.0f
    pushArrow out ctx.Origin dir
        (AXIS_LENGTH_PX + ARROW_LENGTH_PX) ARROW_WIDTH_PX ARROW_LENGTH_PX
        color

let private dashColor (color: float32[]) : float32[] =
    [| color.[0]; color.[1]; color.[2]; 0.85f |]

let private pushDragDash
        (out: ResizeArray<float32>)
        (ctx: Context) (dir: Vec3) (color: float32[])
        (extentPx: float32) =
    pushThickQuad out ctx.Origin dir
        (-extentPx) extentPx DASH_THICKNESS_PX
        (dashColor color) DASH_CYCLE_PX

/// Thick geometry for the current gizmo. When the active session is
/// an axis drag on this action, a dashed guide along that axis is
/// appended.
let buildThickVertices (ctx: Context) (activeAxis: int option) (viewportExtentPx: float32) : float32[] =
    let out = ResizeArray<float32>()
    pushAxisShaft out ctx ctx.AxisX cAxisX
    pushAxisShaft out ctx ctx.AxisY cAxisY
    pushAxisShaft out ctx ctx.AxisZ cAxisZ
    let dashExtentPx = max DASH_MIN_EXTENT_PX viewportExtentPx
    match activeAxis with
    | Some 0 -> pushDragDash out ctx ctx.AxisX cAxisX dashExtentPx
    | Some 1 -> pushDragDash out ctx ctx.AxisY cAxisY dashExtentPx
    | Some 2 -> pushDragDash out ctx ctx.AxisZ cAxisZ dashExtentPx
    | _ -> ()
    out.ToArray()
