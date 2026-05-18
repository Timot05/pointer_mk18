struct Camera {
    eye: vec3<f32>, _p0: f32,
    forward: vec3<f32>, _p1: f32,
    right: vec3<f32>, view_half_h: f32,
    up: vec3<f32>, aspect: f32,
};

// Per-quad placement + alpha. `x_axis` / `y_axis` are unit-length
// world-space directions for the plane the image sits on; the quad
// stretches `±half_width` along `x_axis` and `±half_height` along
// `y_axis`, centred on `origin`.
struct ImageQuad {
    origin:      vec4<f32>,
    x_axis:      vec4<f32>,
    y_axis:      vec4<f32>,
    half_width:  f32,
    half_height: f32,
    opacity:     f32,
    _pad:        f32,
};

@group(0) @binding(0) var<uniform> cam: Camera;
@group(1) @binding(0) var<uniform> quad: ImageQuad;
@group(1) @binding(1) var image: texture_2d<f32>;
@group(1) @binding(2) var image_sampler: sampler;

struct VsOut {
    @builtin(position) clip_pos: vec4<f32>,
    @location(0) uv: vec2<f32>,
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
fn vs(@builtin(vertex_index) vi: u32) -> VsOut {
    // Two triangles, CCW: (-1,-1) (1,-1) (1,1)  /  (-1,-1) (1,1) (-1,1).
    var corners = array<vec2<f32>, 6>(
        vec2<f32>(-1.0, -1.0),
        vec2<f32>( 1.0, -1.0),
        vec2<f32>( 1.0,  1.0),
        vec2<f32>(-1.0, -1.0),
        vec2<f32>( 1.0,  1.0),
        vec2<f32>(-1.0,  1.0)
    );
    let c = corners[vi];
    let world =
        quad.origin.xyz
        + c.x * quad.half_width  * quad.x_axis.xyz
        + c.y * quad.half_height * quad.y_axis.xyz;

    var out: VsOut;
    out.clip_pos = project_world(world);
    // Map quad corners [-1,+1] to UV [0,1]; flip Y because image
    // pixel rows run top-to-bottom while the quad's local Y points
    // up in world space.
    out.uv = vec2<f32>((c.x + 1.0) * 0.5, 1.0 - (c.y + 1.0) * 0.5);
    return out;
}

@fragment
fn fs(input: VsOut) -> @location(0) vec4<f32> {
    let sampled = textureSample(image, image_sampler, input.uv);
    return vec4<f32>(sampled.rgb, sampled.a * quad.opacity);
}
