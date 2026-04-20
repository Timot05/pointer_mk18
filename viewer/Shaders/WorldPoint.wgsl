struct Camera {
    eye: vec3<f32>, _p0: f32,
    forward: vec3<f32>, _p1: f32,
    right: vec3<f32>, view_half_h: f32,
    up: vec3<f32>, aspect: f32,
};

struct Viewport {
    size: vec2<f32>,
    _pad: vec2<f32>,
};

@group(0) @binding(0) var<uniform> cam: Camera;
@group(1) @binding(0) var<uniform> viewport: Viewport;

struct QuadIn {
    @location(0) corner: vec2<f32>,
};

struct InstanceIn {
    @location(1) center_world: vec3<f32>,
    @location(2) radius_px: f32,
    @location(3) color: vec4<f32>,
};

struct VsOut {
    @builtin(position) clip_pos: vec4<f32>,
    @location(0) color: vec4<f32>,
    @location(1) local_pos: vec2<f32>,
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

@vertex
fn vs(quad: QuadIn, instance: InstanceIn) -> VsOut {
    let center_clip = project_world(instance.center_world);
    let size = max(viewport.size, vec2<f32>(1.0, 1.0));
    let offset_ndc = vec2<f32>(
        quad.corner.x * instance.radius_px * 2.0 / size.x,
        quad.corner.y * instance.radius_px * 2.0 / size.y
    );
    var out: VsOut;
    out.clip_pos = vec4<f32>(
        center_clip.xy + offset_ndc * center_clip.w,
        center_clip.z,
        center_clip.w);
    out.color = instance.color;
    out.local_pos = quad.corner;
    return out;
}

@fragment
fn fs(in: VsOut) -> @location(0) vec4<f32> {
    if (dot(in.local_pos, in.local_pos) > 1.0) { discard; }
    return in.color;
}
