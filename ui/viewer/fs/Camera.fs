module Camera

// F# port of ui/viewer/camera.ts. Orbital camera state + standard
// orbit/pan/zoom/screen-ray operations. Same behaviour as the TS version
// so during the transition both viewers can share the same UX.

open Server       // Vec3
open ViewerMath   // Vec2, clamp

/// Same as HALF_FOV in camera.ts. Radians — ≈22.5°, giving a 45° FOV.
let HALF_FOV = 0.3927

type CameraState =
    { mutable Azimuth: float
      mutable Elevation: float
      mutable Distance: float
      mutable Target: Vec3 }

let create () : CameraState =
    { Azimuth = 0.6
      Elevation = 0.3
      Distance = 80.0
      Target = Vec3.Zero }

let eye (c: CameraState) : Vec3 =
    let ce = cos c.Elevation
    let sa = sin c.Azimuth
    let ca = cos c.Azimuth
    let se = sin c.Elevation
    { X = c.Target.X + c.Distance * ce * ca
      Y = c.Target.Y + c.Distance * ce * sa
      Z = c.Target.Z + c.Distance * se }

type Basis = { Eye: Vec3; Forward: Vec3; Right: Vec3; Up: Vec3 }

/// Camera basis: forward points from eye toward target, right is
/// forward × world-up (= Z), up is right × forward. Matches camera.ts.
let basis (c: CameraState) : Basis =
    let e = eye c
    let forward = (c.Target - e).Normalized
    let worldUp : Vec3 = { X = 0.0; Y = 0.0; Z = 1.0 }
    let right = Vec3.Cross(forward, worldUp).Normalized
    let up = Vec3.Cross(right, forward).Normalized
    { Eye = e; Forward = forward; Right = right; Up = up }

let orbit (c: CameraState) (dx: float) (dy: float) : unit =
    c.Azimuth <- c.Azimuth - dx * 0.01
    c.Elevation <- clamp -1.4 1.4 (c.Elevation + dy * 0.01)

let pan (c: CameraState) (dx: float) (dy: float) (height: float) : unit =
    let b = basis c
    let worldPerPx = (2.0 * c.Distance * tan HALF_FOV) / max height 1.0
    c.Target <-
        c.Target
        + (-dx * worldPerPx) * b.Right
        + (dy * worldPerPx) * b.Up

let zoom (c: CameraState) (deltaY: float) : unit =
    let next = c.Distance * exp (deltaY * 0.0012)
    c.Distance <- clamp 6.0 800.0 next

type Ray = { Origin: Vec3; Direction: Vec3 }

let private rayPlaneHit (ray: Ray) (planeOrigin: Vec3) (planeNormal: Vec3) : Vec3 option =
    let denom = Vec3.Dot(ray.Direction, planeNormal)
    if abs denom < 1e-6 then None
    else
        let t = Vec3.Dot(planeOrigin - ray.Origin, planeNormal) / denom
        if t <= 0.0 then None
        else Some (ray.Origin + t * ray.Direction)

let screenToRay (width: float) (height: float) (c: CameraState) (x: float) (y: float) : Ray =
    let ndcX = (x / max width 1.0) * 2.0 - 1.0
    let ndcY = 1.0 - (y / max height 1.0) * 2.0
    let aspect = width / max height 1.0
    let tanHalf = tan HALF_FOV
    let b = basis c
    let dir =
        (b.Forward
         + (ndcX * aspect * tanHalf) * b.Right
         + (ndcY * tanHalf) * b.Up).Normalized
    { Origin = b.Eye; Direction = dir }

/// Zoom while keeping whatever's under (x, y) fixed on screen. Adjusts
/// target as well as distance — matches camera.ts's zoomTowardsPointer.
let zoomTowardsPointer
    (c: CameraState) (width: float) (height: float)
    (x: float) (y: float) (deltaY: float) : unit =
    let forwardBefore = (basis c).Forward
    let targetBefore = c.Target
    let rayBefore = screenToRay width height c x y
    let hitBefore = rayPlaneHit rayBefore targetBefore forwardBefore

    zoom c deltaY

    match hitBefore with
    | None -> ()
    | Some hb ->
        let rayAfter = screenToRay width height c x y
        match rayPlaneHit rayAfter targetBefore forwardBefore with
        | None -> ()
        | Some ha -> c.Target <- c.Target + (hb - ha)

/// Project a 3D world position onto 2D screen coords (CSS pixels). Returns
/// None when the point is behind the camera or the viewport is degenerate.
let worldToScreen
    (width: float) (height: float) (c: CameraState) (world: Vec3)
    : (float * float) option =
    let w = max width 1.0
    let h = max height 1.0
    let b = basis c
    let rel = world - b.Eye
    let z = Vec3.Dot(rel, b.Forward)
    if z <= 1e-6 then None
    else
        let aspect = w / h
        let tanHalf = tan HALF_FOV
        let ndcX = Vec3.Dot(rel, b.Right) / (z * tanHalf * aspect)
        let ndcY = Vec3.Dot(rel, b.Up) / (z * tanHalf)
        Some (((ndcX + 1.0) * 0.5) * w, ((1.0 - ndcY) * 0.5) * h)

/// Intersect a ray with a 2D plane described by an origin + two axes. Returns
/// the (u, v) coordinates in the local plane frame, or None if behind or parallel.
let rayPlaneIntersection (ray: Ray) (origin: Vec3) (xAxis: Vec3) (yAxis: Vec3) : (float * float) option =
    let normal = Vec3.Cross(xAxis, yAxis).Normalized
    let denom = Vec3.Dot(ray.Direction, normal)
    if abs denom < 1e-6 then None
    else
        let t = Vec3.Dot(origin - ray.Origin, normal) / denom
        if t <= 0.0 then None
        else
            let hit = ray.Origin + t * ray.Direction
            let localV = hit - origin
            Some (Vec3.Dot(localV, xAxis), Vec3.Dot(localV, yAxis))
