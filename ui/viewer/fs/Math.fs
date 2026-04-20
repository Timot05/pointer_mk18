module ViewerMath

// Additional math for the viewer layer.
//
// Reuses `Server.Vec3` (struct record from core/Math.fs) for 3-vectors.
// Adds:
//   - `Vec2` struct for 2-vectors (sketch overlay, pixel coords)
//   - `Mat4` alias + perspective / lookAt / mul helpers (WebGPU expects
//     column-major — helper emits in the right layout).

open Server  // Vec3

[<Struct>]
type Vec2 = { X: float; Y: float }

module Vec2 =
    let zero : Vec2 = { X = 0.0; Y = 0.0 }
    let make (x: float) (y: float) : Vec2 = { X = x; Y = y }
    let add (a: Vec2) (b: Vec2) : Vec2 = { X = a.X + b.X; Y = a.Y + b.Y }
    let sub (a: Vec2) (b: Vec2) : Vec2 = { X = a.X - b.X; Y = a.Y - b.Y }
    let scale (v: Vec2) (s: float) : Vec2 = { X = v.X * s; Y = v.Y * s }
    let dot (a: Vec2) (b: Vec2) : float = a.X * b.X + a.Y * b.Y
    let lenSq (v: Vec2) : float = v.X * v.X + v.Y * v.Y
    let length (v: Vec2) : float = sqrt (lenSq v)
    let normalised (v: Vec2) : Vec2 =
        let l = length v
        if l < 1e-9 then zero else { X = v.X / l; Y = v.Y / l }
    let perp (v: Vec2) : Vec2 = { X = -v.Y; Y = v.X }

/// Clamp a scalar to [lo, hi].
let clamp (lo: float) (hi: float) (v: float) : float =
    if v < lo then lo elif v > hi then hi else v

// ── Mat4 (row-major 16-float array). Upload helper transposes to column. ──

type Mat4 = float32[]  // length 16, row-major for legibility

let identity () : Mat4 =
    [| 1.0f; 0.0f; 0.0f; 0.0f
       0.0f; 1.0f; 0.0f; 0.0f
       0.0f; 0.0f; 1.0f; 0.0f
       0.0f; 0.0f; 0.0f; 1.0f |]

let mul (a: Mat4) (b: Mat4) : Mat4 =
    [| for r in 0 .. 3 do
         for c in 0 .. 3 do
           yield
             [ 0 .. 3 ]
             |> List.sumBy (fun k -> a.[r * 4 + k] * b.[k * 4 + c]) |]

/// Right-handed perspective with depth range 0..1 (WebGPU convention).
let perspective (fovYRad: float32) (aspect: float32) (near: float32) (far: float32) : Mat4 =
    let f = 1.0f / tan (fovYRad * 0.5f)
    [| f / aspect; 0.0f; 0.0f;               0.0f
       0.0f;        f;    0.0f;               0.0f
       0.0f;        0.0f; far / (near - far); (near * far) / (near - far)
       0.0f;        0.0f; -1.0f;              0.0f |]

let private norm3T (x, y, z) =
    let l = sqrt (x * x + y * y + z * z)
    x / l, y / l, z / l

let private cross3T (ax, ay, az) (bx, by, bz) =
    ay * bz - az * by, az * bx - ax * bz, ax * by - ay * bx

let private dot3T (ax, ay, az) (bx, by, bz) =
    ax * bx + ay * by + az * bz

let lookAt
    (eye: float32 * float32 * float32)
    (target: float32 * float32 * float32)
    (up: float32 * float32 * float32)
    : Mat4 =
    let (ex, ey, ez) = eye
    let (tx, ty, tz) = target
    let fwd = norm3T (tx - ex, ty - ey, tz - ez)
    let right = norm3T (cross3T fwd up)
    let camUp = cross3T right fwd
    let (fx, fy, fz) = fwd
    let (rx, ry, rz) = right
    let (ux, uy, uz) = camUp
    [| rx;   ry;   rz;   -(dot3T right eye);
       ux;   uy;   uz;   -(dot3T camUp eye);
      -fx;  -fy;  -fz;     dot3T fwd eye;
       0.0f; 0.0f; 0.0f;   1.0f |]

/// WGSL reads `mat4x4<f32>` in column-major order. Transpose on upload.
let toColumnMajorFloat32 (m: Mat4) : float32[] =
    [| m.[0]; m.[4]; m.[8];  m.[12]
       m.[1]; m.[5]; m.[9];  m.[13]
       m.[2]; m.[6]; m.[10]; m.[14]
       m.[3]; m.[7]; m.[11]; m.[15] |]
