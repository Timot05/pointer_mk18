// Field background: samples the kernel's G-buffer (normal.xyz + wcz), does
// key+fill+ambient shading, reconstructs the hit's world position from
// (uv, wcz, field camera), and writes a perspective-correct `frag_depth`
// so sketch overlay geometry z-tests against the field surface.

struct Camera {
    eye: vec3<f32>, _p0: f32,
    forward: vec3<f32>, _p1: f32,
    right: vec3<f32>, view_half_h: f32,
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
// Per-pixel palette idx written by `cpu_render` alongside the gbuffer.
// `0xFFFFFFFF` means "no view hit at this pixel" — use the fallback base.
@group(0) @binding(3) var palette_tex: texture_2d<u32>;

@group(1) @binding(0) var<uniform> cam: Camera;

// (base_rgb, lit_rgb) per palette index — mirror of `cpu_render.PALETTE`.
// Diffuse lighting lerps between base and lit. Keep in sync with
// `kernel/src/cpu_render.zig:PALETTE`.
const PALETTE_BASE: array<vec3<f32>, 9> = array<vec3<f32>, 9>(
    vec3<f32>(0.286, 0.374, 0.431),  // 0: #85AEC8
    vec3<f32>(0.113, 0.063, 0.269),  // 1: #341D7C
    vec3<f32>(0.518, 0.400, 0.075),  // 2: #F1BA23
    vec3<f32>(0.550, 0.550, 0.550),  // 3: #FFFFFF
    vec3<f32>(0.371, 0.220, 0.043),  // 4: #AC6614
    vec3<f32>(0.492, 0.462, 0.378),  // 5: #E4D6AF
    vec3<f32>(0.271, 0.216, 0.000),  // 6: #7D6400
    vec3<f32>(0.550, 0.550, 0.367),  // 7: #FFFFAA
    vec3<f32>(0.451, 0.000, 0.011),  // 8: #D10005
);
const PALETTE_LIT: array<vec3<f32>, 9> = array<vec3<f32>, 9>(
    vec3<f32>(0.522, 0.682, 0.784),
    vec3<f32>(0.204, 0.114, 0.486),
    vec3<f32>(0.945, 0.729, 0.137),
    vec3<f32>(1.000, 1.000, 1.000),
    vec3<f32>(0.675, 0.400, 0.078),
    vec3<f32>(0.894, 0.839, 0.686),
    vec3<f32>(0.490, 0.392, 0.000),
    vec3<f32>(1.000, 1.000, 0.667),
    vec3<f32>(0.820, 0.000, 0.020),
);

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

@fragment
fn fs(in: VsOut) -> FsOut {
    let g = textureSample(gbuffer, samp, in.uv);
    // The kernel evaluates the SDF in *camera* coordinates (the wrap-axes
    // pass replaces world XYZ with `eye + u·right + v·up + t·basis_z`),
    // so the gradient it writes into the gbuffer is camera-space. Project
    // it back onto the world axes — otherwise every surface gets the
    // same shade because the lights end up camera-attached, which reads
    // as flat lighting that doesn't tell you which face is "up". The
    // basis vectors are world-space and live on `FieldCamera`.
    let n_cam = g.xyz;
    let n = normalize(
        n_cam.x * field.basis_x.xyz
        + n_cam.y * field.basis_y.xyz
        + n_cam.z * field.basis_z.xyz);
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

    // Look up the per-block base color from the palette texture. The
    // r32uint texture stores the winning view's palette idx per pixel;
    // `0xFFFFFFFF` is the kernel's "miss/background" sentinel and falls
    // back to a neutral default.
    let palette_xy = vec2<i32>(
        i32(in.uv.x * f32(textureDimensions(palette_tex).x)),
        i32(in.uv.y * f32(textureDimensions(palette_tex).y)),
    );
    let palette_idx_raw = textureLoad(palette_tex, palette_xy, 0).r;
    let has_palette = palette_idx_raw != 0xFFFFFFFFu;
    let palette_idx = i32(palette_idx_raw % 9u);
    let base = select(
        vec3<f32>(0.62, 0.55, 0.48),
        PALETTE_BASE[palette_idx],
        has_palette);
    let lit = select(
        vec3<f32>(0.62, 0.55, 0.48),
        PALETTE_LIT[palette_idx],
        has_palette);

    // Three-light diffuse rig (key + fill + low ambient) plus a Fresnel
    // rim term that brightens silhouette pixels — where the surface normal
    // is perpendicular to the view direction the rim peaks. This pops the
    // outline against the page background and keeps interior contrast
    // high. The ambient was lowered from 0.25 → 0.15 to deepen the
    // shadow side; key was bumped 0.5 → 0.6 to widen the lit-to-shadow
    // gradient. Net effect: clearer surface curvature reading + a thin
    // bright halo on edges.
    let key_dir = normalize(vec3<f32>(0.4, 0.3, 0.8));
    let fill_dir = normalize(vec3<f32>(-0.5, -0.4, 0.3));
    let key = max(dot(n, key_dir), 0.0) * 0.6;
    let fill = max(dot(n, fill_dir), 0.0) * 0.2;
    let ambient = 0.15;
    let diffuse = clamp(ambient + key + fill, 0.0, 1.0);
    let shaded = mix(base, lit, diffuse);

    // Fresnel rim. `abs(dot(n, forward))` is 1 when the surface faces the
    // camera (or directly away) and 0 at the silhouette. A higher power
    // tightens the rim to a thinner band right at the edge; lower
    // strength keeps it from reading as a specular highlight.
    let view_align = abs(dot(n, cam.forward));
    let rim = pow(1.0 - clamp(view_align, 0.0, 1.0), 5.0);
    let color = shaded + rim * 0.2 * lit;

    var out: FsOut;
    out.color = vec4<f32>(min(color, vec3<f32>(1.0, 1.0, 1.0)), 1.0);
    out.depth = clip.z / clip.w;
    return out;
}
