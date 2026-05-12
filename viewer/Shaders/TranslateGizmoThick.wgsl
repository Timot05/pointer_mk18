// Thick-line + arrow renderer used by the translate gizmo. CPU
// generates triangle-list geometry in an (along, perp) pixel frame
// anchored at the gizmo origin. The shader rotates that frame into
// world space using the axis direction + the camera's forward vector
// (for the camera-perpendicular), so axes stay screen-constant width
// from any angle.

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

struct VsIn {
    @location(0) anchor: vec3<f32>,
    @location(1) dir: vec3<f32>,
    /// offset.x — distance along `dir` in pixels.
    /// offset.y — perpendicular offset in pixels (camera-facing).
    @location(2) offset: vec2<f32>,
    @location(3) color: vec4<f32>,
    /// dash_scale: pixels-per-dash-cycle for dashed lines. `0` disables
    /// the dash pattern.
    @location(4) dash_scale: f32,
};

struct VsOut {
    @builtin(position) clip_pos: vec4<f32>,
    @location(0) color: vec4<f32>,
    @location(1) dash_info: vec2<f32>,  // (x = distance-along-axis-px, y = dash_scale)
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
    let world_per_px = (2.0 * cam.view_half_h) / max(viewport.size.y, 1.0);
    // Axis direction in world. Lines perpendicular to this that still
    // face the camera are `cross(dir, forward)` — renormalise because
    // either can be non-unit or near-parallel.
    let dir_n = normalize(input.dir);
    var perp = cross(dir_n, cam.forward);
    let perp_len = length(perp);
    // Fall back to world-up if the axis is parallel to the view ray
    // (would otherwise produce a zero-length perpendicular).
    if (perp_len < 1e-4) {
        perp = vec3<f32>(0.0, 0.0, 1.0);
    } else {
        perp = perp / perp_len;
    }
    let world =
        input.anchor
        + dir_n * (input.offset.x * world_per_px)
        + perp  * (input.offset.y * world_per_px);
    var out: VsOut;
    out.clip_pos = project_world(world);
    out.color = input.color;
    out.dash_info = vec2<f32>(input.offset.x, input.dash_scale);
    return out;
}

@fragment
fn fs(input: VsOut) -> @location(0) vec4<f32> {
    let scale = input.dash_info.y;
    if (scale > 0.0) {
        // 60% dash, 40% gap (tuned visually).
        let t = fract(input.dash_info.x / scale);
        if (t > 0.6) { discard; }
    }
    return input.color;
}
