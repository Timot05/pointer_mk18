namespace Server

// ---------------------------------------------------------------------------
// SO(3) and SE(3) math — quaternion rotations and rigid transforms
// ---------------------------------------------------------------------------

open System

[<Struct>]
type Vec3 =
    { X: float; Y: float; Z: float }

    static member Zero = { X = 0.0; Y = 0.0; Z = 0.0 }

    static member (+) (a: Vec3, b: Vec3) =
        { X = a.X + b.X; Y = a.Y + b.Y; Z = a.Z + b.Z }

    static member (-) (a: Vec3, b: Vec3) =
        { X = a.X - b.X; Y = a.Y - b.Y; Z = a.Z - b.Z }

    static member (~-) (a: Vec3) =
        { X = -a.X; Y = -a.Y; Z = -a.Z }

    static member (*) (s: float, v: Vec3) =
        { X = s * v.X; Y = s * v.Y; Z = s * v.Z }

    member v.LengthSq = v.X * v.X + v.Y * v.Y + v.Z * v.Z
    member v.Length = sqrt v.LengthSq

    member v.Normalized =
        let len = v.Length
        if len < 1e-12 then Vec3.Zero
        else (1.0 / len) * v

    static member Dot (a: Vec3, b: Vec3) =
        a.X * b.X + a.Y * b.Y + a.Z * b.Z

    static member Cross (a: Vec3, b: Vec3) =
        { X = a.Y * b.Z - a.Z * b.Y
          Y = a.Z * b.X - a.X * b.Z
          Z = a.X * b.Y - a.Y * b.X }

/// Unit quaternion representing SO(3) rotation.
/// Convention: w + xi + yj + zk
[<Struct>]
type Quat =
    { W: float; X: float; Y: float; Z: float }

    static member Identity = { W = 1.0; X = 0.0; Y = 0.0; Z = 0.0 }

    /// Hamilton product (rotation composition).
    static member (*) (a: Quat, b: Quat) =
        { W = a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z
          X = a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y
          Y = a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X
          Z = a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W }

    /// Conjugate = inverse for unit quaternions.
    member q.Inverse = { W = q.W; X = -q.X; Y = -q.Y; Z = -q.Z }

    /// Rotate a vector: q * v * q⁻¹
    member q.Rotate (v: Vec3) : Vec3 =
        // Optimized: t = 2 * (q_xyz × v), result = v + w*t + q_xyz × t
        let tx = 2.0 * (q.Y * v.Z - q.Z * v.Y)
        let ty = 2.0 * (q.Z * v.X - q.X * v.Z)
        let tz = 2.0 * (q.X * v.Y - q.Y * v.X)
        { X = v.X + q.W * tx + (q.Y * tz - q.Z * ty)
          Y = v.Y + q.W * ty + (q.Z * tx - q.X * tz)
          Z = v.Z + q.W * tz + (q.X * ty - q.Y * tx) }

module Quat =

    let fromBasis (xAxis: Vec3) (yAxis: Vec3) (zAxis: Vec3) : Quat =
        let m00, m01, m02 = xAxis.X, yAxis.X, zAxis.X
        let m10, m11, m12 = xAxis.Y, yAxis.Y, zAxis.Y
        let m20, m21, m22 = xAxis.Z, yAxis.Z, zAxis.Z
        let trace = m00 + m11 + m22
        if trace > 0.0 then
            let s = sqrt (trace + 1.0) * 2.0
            { W = 0.25 * s
              X = (m21 - m12) / s
              Y = (m02 - m20) / s
              Z = (m10 - m01) / s }
        elif m00 > m11 && m00 > m22 then
            let s = sqrt (1.0 + m00 - m11 - m22) * 2.0
            { W = (m21 - m12) / s
              X = 0.25 * s
              Y = (m01 + m10) / s
              Z = (m02 + m20) / s }
        elif m11 > m22 then
            let s = sqrt (1.0 + m11 - m00 - m22) * 2.0
            { W = (m02 - m20) / s
              X = (m01 + m10) / s
              Y = 0.25 * s
              Z = (m12 + m21) / s }
        else
            let s = sqrt (1.0 + m22 - m00 - m11) * 2.0
            { W = (m10 - m01) / s
              X = (m02 + m20) / s
              Y = (m12 + m21) / s
              Z = 0.25 * s }

/// Rigid transform in SE(3): rotation then translation.
/// Applies as: T(p) = R(p) + t
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
