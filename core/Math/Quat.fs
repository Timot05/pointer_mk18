namespace Server

// Unit quaternion representing SO(3) rotation (w + xi + yj + zk).

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
