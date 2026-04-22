struct Camera {
    eye: vec3<f32>, _p0: f32,
    forward: vec3<f32>, _p1: f32,
    right: vec3<f32>, view_half_h: f32,
    up: vec3<f32>, aspect: f32,
};

struct SketchFrame {
    pos: vec4<f32>,
    x_axis: vec4<f32>,
    y_axis: vec4<f32>,
};

@group(0) @binding(0) var<uniform> cam: Camera;
@group(1) @binding(0) var<uniform> frame: SketchFrame;

struct VsOut {
    @builtin(position) clip_pos: vec4<f32>,
    @location(0) pick_id: f32,
};

fn project_world(pos: vec3<f32>) -> vec4<f32> {
    let f = cam.forward;
    let r = cam.right;
    let u = cam.up;
    let view = mat4x4<f32>(
        vec4<f32>(r.x, u.x, -f.x, 0.0),
        vec4<f32>(r.y, u.y, -f.y, 0.0),
        vec4<f32>(r.z, u.z, -f.z, 0.0),
        vec4<f32>(-dot(r, cam.eye), -dot(u, cam.eye), dot(f, cam.eye), 1.0),
    );
    let near = 0.001;
    let far = 1000.0;
    let h = cam.view_half_h;
    let w = cam.aspect * h;
    let proj = mat4x4<f32>(
        vec4<f32>(1.0 / w, 0.0, 0.0, 0.0),
        vec4<f32>(0.0, 1.0 / h, 0.0, 0.0),
        vec4<f32>(0.0, 0.0, -1.0 / (far - near), 0.0),
        vec4<f32>(0.0, 0.0, -near / (far - near), 1.0),
    );
    return proj * view * vec4<f32>(pos, 1.0);
}

// Generous hit tube around each sketch line segment. Bigger than
// visually rendered thickness so the user can pick without landing
// exactly on the centerline. Keep in sync with `LINE_PICK_THICKNESS`
// in `viewer/SketchOverlayRender.fs`.
const THICKNESS: f32 = 0.5;

@vertex
fn vs(
    @location(0) corner: vec2<f32>,   // (t, s) where t ∈ {0,1}, s ∈ {-1,+1}
    @location(1) a_2d: vec2<f32>,
    @location(2) b_2d: vec2<f32>,
    @location(3) pick_id: f32,
) -> VsOut {
    let t = corner.x;
    let s = corner.y;
    let p = mix(a_2d, b_2d, t);
    var dir = b_2d - a_2d;
    let len = length(dir);
    if (len > 1e-9) {
        dir = dir / len;
    } else {
        dir = vec2<f32>(1.0, 0.0);
    }
    let perp = vec2<f32>(-dir.y, dir.x);
    let offset = perp * s * THICKNESS;
    let p2 = p + offset;
    let world = frame.pos.xyz + p2.x * frame.x_axis.xyz + p2.y * frame.y_axis.xyz;
    var out: VsOut;
    out.clip_pos = project_world(world);
    out.pick_id = pick_id;
    return out;
}

@fragment
fn fs(in: VsOut) -> @location(0) u32 {
    return u32(in.pick_id) + 1u;
}
