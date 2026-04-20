namespace Server

// SE(3) rigid transform: rotation then translation.

[<Struct>]
type RigidTransform =
    { Rot: Quat; Trans: Vec3 }

    static member Identity =
        { Rot = Quat.Identity; Trans = Vec3.Zero }

    /// Compose: T1 * T2 means apply T2 first, then T1.
    /// (R1*R2, R1*t2 + t1)
    static member (*) (a: RigidTransform, b: RigidTransform) =
        { Rot = a.Rot * b.Rot
          Trans = a.Rot.Rotate(b.Trans) + a.Trans }

    /// Inverse: T⁻¹ = (R⁻¹, -R⁻¹ * t)
    member t.Inverse =
        let ri = t.Rot.Inverse
        { Rot = ri; Trans = -ri.Rotate(t.Trans) }

    /// Apply transform to a point.
    member t.Apply (p: Vec3) : Vec3 =
        t.Rot.Rotate(p) + t.Trans

module RigidTransform =

    /// Pure translation (no rotation).
    let translate (v: Vec3) : RigidTransform =
        { Rot = Quat.Identity; Trans = v }

    /// Rotation from axis-angle in radians. Axis need not be unit length;
    /// its length is ignored and angle is separate.
    let fromAxisAngle (axis: Vec3) (angleRad: float) : RigidTransform =
        let half = angleRad * 0.5
        let a = axis.Normalized
        let s = sin half
        { Rot = { W = cos half; X = a.X * s; Y = a.Y * s; Z = a.Z * s }
          Trans = Vec3.Zero }
