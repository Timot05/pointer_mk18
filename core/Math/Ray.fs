namespace Server

// Shared ray primitives — used by the viewer's camera code, the scene
// interaction dispatch in core, and any tool module that needs to
// project pointer rays onto axes / planes.

[<Struct>]
type PointerRay =
    { Origin: Vec3
      Direction: Vec3 }

module PointerRay =

    /// Closest-point parameter on the line through `anchor` in
    /// direction `axis`, relative to `ray`. Returns None when the
    /// axis and ray are parallel (denominator near zero).
    ///
    /// Given P(s) = anchor + s*u and Q(t) = rayOrigin + t*v, with
    /// w = anchor - rayOrigin, s = (b*e - c*d) / (a*c - b*b) where
    /// a = u·u, b = u·v, c = v·v, d = u·w, e = v·w. Getting the sign
    /// of `w` wrong flips the direction, which makes axis drags track
    /// the cursor backwards.
    let projectOntoAxis (ray: PointerRay) (anchor: Vec3) (axis: Vec3) : float option =
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

    /// Intersect the ray with a 2D plane described by an origin and two
    /// in-plane axes. Returns (u, v) local coordinates on the plane, or
    /// None when the ray is parallel or behind the camera.
    let intersectPlane
            (ray: PointerRay)
            (origin: Vec3)
            (xAxis: Vec3)
            (yAxis: Vec3) : (float * float) option =
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

    /// Map a pointer ray onto the unit sphere around `center`. Uses the
    /// closest positive ray point when the ray misses the sphere, so drag
    /// remains stable even when the cursor moves slightly outside the visual
    /// sphere handle. Returns a world-space unit direction from `center`.
    let projectToSphereDirection
            (ray: PointerRay)
            (center: Vec3)
            (fallback: Vec3) : Vec3 =
        let oc = ray.Origin - center
        let b = Vec3.Dot(oc, ray.Direction)
        let c = Vec3.Dot(oc, oc) - 1.0
        let disc = b * b - c
        let hitDir =
            if disc >= 0.0 then
                let s = sqrt disc
                let t0 = -b - s
                let t1 = -b + s
                let t =
                    if t0 > 1e-6 then t0
                    elif t1 > 1e-6 then t1
                    else max 0.0 (-b)
                (ray.Origin + t * ray.Direction) - center
            else
                let t = max 0.0 (-b)
                (ray.Origin + t * ray.Direction) - center

        let dir = hitDir.Normalized
        if dir.LengthSq < 1e-12 then fallback.Normalized else dir
