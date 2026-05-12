struct Camera {
    eye: vec3<f32>, _p0: f32,
    forward: vec3<f32>, _p1: f32,
    right: vec3<f32>, view_half_h: f32,
    up: vec3<f32>, aspect: f32,
};

struct LabelUniforms {
    viewport: vec4<f32>,
    frame_pos: vec4<f32>,
    frame_x: vec4<f32>,
    frame_y: vec4<f32>,
    tint: vec4<f32>,
};

const ATLAS_PX_RANGE: f32 = 4.0;
const ATLAS_SIZE: f32 = 256.0;

@group(0) @binding(0) var<uniform> cam: Camera;
@group(1) @binding(0) var<uniform> label: LabelUniforms;
@group(1) @binding(1) var atlas: texture_2d<f32>;
@group(1) @binding(2) var atlas_sampler: sampler;

struct VsIn {
    @location(0) anchor_2d: vec2<f32>,
    @location(1) offset_px: vec2<f32>,
    @location(2) uv: vec2<f32>,
    @location(3) color: vec4<f32>,
};

struct VsOut {
    @builtin(position) clip_pos: vec4<f32>,
    @location(0) uv: vec2<f32>,
    @location(1) color: vec4<f32>,
    @location(2) tint: vec4<f32>,
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
    let near = -1000.0;
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
fn vs(input: VsIn) -> VsOut {
    let frame_pos = label.frame_pos.xyz;
    let frame_x = label.frame_x.xyz;
    let frame_y = label.frame_y.xyz;

    let anchor_world = frame_pos + input.anchor_2d.x * frame_x + input.anchor_2d.y * frame_y;
    // Ortho projection: one pixel is a constant world distance across
    // the scene — the slab's full height is `2 * view_half_h`.
    let world_per_px = 2.0 * cam.view_half_h / label.viewport.y;

    let proj_fx = frame_x - dot(frame_x, cam.forward) * cam.forward;
    let proj_fy = frame_y - dot(frame_y, cam.forward) * cam.forward;
    let x_sign = select(-1.0, 1.0, dot(proj_fx, cam.right) > 0.0);
    let y_sign = select(-1.0, 1.0, dot(proj_fy, cam.up) > 0.0);
    let plane_offset_2d = vec2<f32>(
        input.offset_px.x * x_sign,
        -input.offset_px.y * y_sign
    ) * world_per_px;
    let plane_offset_world = plane_offset_2d.x * frame_x + plane_offset_2d.y * frame_y;

    var out: VsOut;
    out.clip_pos = project_world(anchor_world + plane_offset_world);
    out.uv = input.uv;
    out.color = input.color;
    out.tint = label.tint;
    return out;
}

fn median3(r: f32, g: f32, b: f32) -> f32 {
    return max(min(r, g), min(max(r, g), b));
}

@fragment
fn fs(input: VsOut) -> @location(0) vec4<f32> {
    let msd = textureSample(atlas, atlas_sampler, input.uv).rgb;
    let sd = median3(msd.r, msd.g, msd.b);
    let unit_range = ATLAS_PX_RANGE / ATLAS_SIZE;
    let uv_deriv = fwidth(input.uv);
    let screen_px_range = max(0.5 * (unit_range / uv_deriv.x + unit_range / uv_deriv.y), 1.0);
    let screen_px_distance = screen_px_range * (sd - 0.5);
    let alpha = clamp(screen_px_distance + 0.5, 0.0, 1.0);
    return vec4<f32>(input.color.rgb * input.tint.rgb, input.color.a * alpha * input.tint.a);
}
