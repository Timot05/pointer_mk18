// Field background: samples the kernel's G-buffer (normal.xyz + wcz), does
// key+fill+ambient shading, reconstructs the hit's world position from
// (uv, wcz, field camera), and writes a perspective-correct `frag_depth`
// so sketch overlay geometry z-tests against the field surface.

struct Camera {
    eye: vec3<f32>, _p0: f32,
    forward: vec3<f32>, _p1: f32,
    right: vec3<f32>, _p2: f32,
    up: vec3<f32>, aspect: f32,
};

struct FieldCamera {
    // xyz = kernel "eye" = centre of the orthographic slab (= viewer's
    // look-at target).
    center: vec4<f32>,
    basis_x: vec4<f32>,  // world-space camera right
    basis_y: vec4<f32>,  // world-space camera up
    basis_z: vec4<f32>,  // world-space camera forward
    // (view_half_w, view_half_h, _, _).
    view: vec4<f32>,
};

@group(0) @binding(0) var gbuffer: texture_2d<f32>;
@group(0) @binding(1) var samp: sampler;
@group(0) @binding(2) var<uniform> field: FieldCamera;

@group(1) @binding(0) var<uniform> cam: Camera;

struct VsOut {
    @builtin(position) clip_pos: vec4<f32>,
    @location(0) uv: vec2<f32>,
};

struct FsOut {
    @location(0) color: vec4<f32>,
    @builtin(frag_depth) depth: f32,
};

@vertex
fn vs(@builtin(vertex_index) id: u32) -> VsOut {
    // Fullscreen triangle. Vertex 0 = (-1, -1), 1 = (3, -1), 2 = (-1, 3).
    let x = f32((id << 1u) & 2u) * 2.0 - 1.0;
    let y = f32(id & 2u) * 2.0 - 1.0;
    var out: VsOut;
    out.clip_pos = vec4<f32>(x, y, 0.0, 1.0);
    out.uv = vec2<f32>((x + 1.0) * 0.5, (1.0 - y) * 0.5);
    return out;
}

// Same projection matrix the sketch pipelines use — keep in sync with
// Line.wgsl / Point.wgsl / etc. so field depth integrates seamlessly.
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

@fragment
fn fs(in: VsOut) -> FsOut {
    let g = textureSample(gbuffer, samp, in.uv);
    let n = g.xyz;
    let wcz = g.w;

    // Miss pixels write wcz = -inf from the kernel.
    if (wcz < -1e30) { discard; }

    // Reconstruct world position of the hit from (uv, wcz).
    let wcx = (in.uv.x * 2.0 - 1.0) * field.view.x;
    let wcy = (1.0 - in.uv.y * 2.0) * field.view.y;
    let world = field.center.xyz
        + field.basis_x.xyz * wcx
        + field.basis_y.xyz * wcy
        + field.basis_z.xyz * wcz;

    let clip = project_world(world);

    // Three-light diffuse rig: ambient + key + fill — same as the old
    // raymarch shader.
    let key_dir = normalize(vec3<f32>(0.4, 0.3, 0.8));
    let fill_dir = normalize(vec3<f32>(-0.5, -0.4, 0.3));
    let key = max(dot(n, key_dir), 0.0) * 0.5;
    let fill = max(dot(n, fill_dir), 0.0) * 0.2;
    let ambient = 0.45;
    let shade = ambient + key + fill;
    let base = vec3<f32>(0.62, 0.55, 0.48);

    var out: FsOut;
    out.color = vec4<f32>(base * shade, 1.0);
    out.depth = clip.z / clip.w;
    return out;
}
