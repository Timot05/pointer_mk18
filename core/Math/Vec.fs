namespace Server

// 2D and 3D vector primitives shared across core, viewer, and UI.

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
        else
            // Inlined (1/len) * v — Fable miscompiles the scalar*struct
            // operator when invoked from an instance-member body, emitting
            // a plain JS multiply of number * object (= NaN). Safe to remove
            // once Fable fixes the resolution.
            let inv = 1.0 / len
            { X = inv * v.X; Y = inv * v.Y; Z = inv * v.Z }

    static member Dot (a: Vec3, b: Vec3) =
        a.X * b.X + a.Y * b.Y + a.Z * b.Z

    static member Cross (a: Vec3, b: Vec3) =
        { X = a.Y * b.Z - a.Z * b.Y
          Y = a.Z * b.X - a.X * b.Z
          Z = a.X * b.Y - a.Y * b.X }

module Vec2 =

    /// Euclidean distance between two 2D points represented as (x, y) tuples.
    let distance ((ax, ay): float * float) ((bx, by): float * float) =
        let dx = bx - ax
        let dy = by - ay
        sqrt (dx * dx + dy * dy)

    /// Perpendicular distance from point p to the line through a–b.
    /// Returns 0 if a and b coincide.
    let pointLineDistance ((px, py): float * float) ((ax, ay): float * float) ((bx, by): float * float) =
        let dx = bx - ax
        let dy = by - ay
        let len = sqrt (dx * dx + dy * dy)
        if len < 1e-9 then 0.0
        else abs ((dx * (py - ay) - dy * (px - ax)) / len)
