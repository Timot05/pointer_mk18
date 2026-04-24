namespace Server

// Unified scene-interaction state. The viewer now flows all pointer
// events aimed at the 3D scene through a small family of Scene*
// messages (see Editor.fs Message DU). When a pointer-down resolves
// to a pickable that should enter a drag, the reducer promotes that
// into a `SceneSession` on `EditorState.ActiveSession`. The per-frame
// mousemove dispatches `ScenePointerMove`, which dispatches by session
// case to compute new slot values.
//
// This pass migrates translate + rotate gizmo drags. Sketch-point /
// sketch-label drags still use their dedicated SketchDrag message family;
// they'll move into `SceneSession` in a follow-up (they need careful
// treatment of the async solver effect chain).

[<Struct>]
type PointerMods =
    { Shift: bool
      Meta: bool
      Alt: bool }

module PointerMods =
    let none : PointerMods = { Shift = false; Meta = false; Alt = false }

/// Captured at pointer-down for an axis-handle drag. `Anchor` and
/// `AxisDir` freeze the drag reference frame at drag-start; each
/// mousemove projects the current pointer ray onto that line and the
/// delta `t - InitialT` becomes the new slot offset along the chosen
/// axis.
type GizmoAxisDragSession =
    { ActionId: ActionId
      /// 0 = X, 1 = Y, 2 = Z (local axis of the translate action's frame).
      AxisIndex: int
      /// World-space unit direction of that axis at drag-start.
      AxisDir: Vec3
      /// World position of the gizmo origin at drag-start.
      Anchor: Vec3
      /// Slot values captured at drag-start.
      InitialX: float
      InitialY: float
      InitialZ: float
      /// Ray-onto-axis projection at drag-start.
      InitialT: float }

/// Plane-handle drag — moves along two of the translate's axes at once.
type GizmoPlaneDragSession =
    { ActionId: ActionId
      /// 0 = XY, 1 = YZ, 2 = XZ.
      PlaneIndex: int
      AxisU: Vec3
      AxisV: Vec3
      Anchor: Vec3
      InitialX: float
      InitialY: float
      InitialZ: float
      /// (u, v) on the plane at drag-start (in world units along AxisU / AxisV).
      InitialU: float
      InitialV: float }

/// Dragging the rotate gizmo's axis-direction sphere handle. `WorldToLocal`
/// converts the dragged world-space unit direction back into the rotate
/// action's local slot frame before patching (ax, ay, az).
type RotateAxisDragSession =
    { ActionId: ActionId
      Anchor: Vec3
      WorldToLocal: Quat
      FallbackWorldAxis: Vec3
      InitialAxisX: float
      InitialAxisY: float
      InitialAxisZ: float }

/// Dragging the rotate gizmo's angle handle. The handle moves in the plane
/// orthogonal to the current axis and centered at the axis tip. `BasisU/V`
/// define the zero-angle reference frame in that plane.
type RotateAngleDragSession =
    { ActionId: ActionId
      Center: Vec3
      AxisWorld: Vec3
      BasisU: Vec3
      BasisV: Vec3
      InitialAngle: float }

type HalfPlaneOffsetDragSession =
    { ActionId: ActionId
      AxisDir: Vec3
      Anchor: Vec3
      InitialOffset: float
      InitialT: float }

/// Active pointer-drag session on the 3D scene. `None` means no drag
/// is in flight — idle state, or merely hovering / selecting.
type SceneSession =
    | GizmoAxisDrag of GizmoAxisDragSession
    | GizmoPlaneDrag of GizmoPlaneDragSession
    | RotateAxisDrag of RotateAxisDragSession
    | RotateAngleDrag of RotateAngleDragSession
    | HalfPlaneOffsetDrag of HalfPlaneOffsetDragSession

module SceneSession =

    /// Which action the session is targeting. Lets `normalizeState`
    /// drop sessions whose action has been deleted underneath us.
    let actionId =
        function
        | GizmoAxisDrag s -> s.ActionId
        | GizmoPlaneDrag s -> s.ActionId
        | RotateAxisDrag s -> s.ActionId
        | RotateAngleDrag s -> s.ActionId
        | HalfPlaneOffsetDrag s -> s.ActionId
