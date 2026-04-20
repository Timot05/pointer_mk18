struct Camera {
    eye: vec3<f32>, _p0: f32,
    forward: vec3<f32>, _p1: f32,
    right: vec3<f32>, _p2: f32,
    up: vec3<f32>, aspect: f32,
};

struct SketchFrame {
    pos: vec4<f32>,
    x_axis: vec4<f32>,
    y_axis: vec4<f32>,
};

@group(0) @binding(0) var<uniform> cam: Camera;
@group(1) @binding(0) var<uniform> frame: SketchFrame;

struct VsIn {
    @location(0) position_2d: vec2<f32>,
    @location(1) color: vec4<f32>,
};

struct VsOut {
    @builtin(position) clip_pos: vec4<f32>,
    @location(0) color: vec4<f32>,
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
    let t = tan(0.3927);
    let proj = mat4x4<f32>(
        vec4<f32>(1.0 / (cam.aspect * t), 0.0, 0.0, 0.0),
        vec4<f32>(0.0, 1.0 / t, 0.0, 0.0),
        vec4<f32>(0.0, 0.0, -(far + near) / (far - near), -1.0),
        vec4<f32>(0.0, 0.0, -2.0 * far * near / (far - near), 0.0),
    );
    return proj * view * vec4<f32>(pos, 1.0);
}

@vertex
fn vs(input: VsIn) -> VsOut {
    let world = frame.pos.xyz
        + input.position_2d.x * frame.x_axis.xyz
        + input.position_2d.y * frame.y_axis.xyz;
    var out: VsOut;
    out.clip_pos = project_world(world);
    out.color = input.color;
    return out;
}

@fragment
fn fs(input: VsOut) -> @location(0) vec4<f32> {
    return input.color;
}
