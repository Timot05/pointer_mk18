module Viewer

// F# viewer.
//
// Subscribes to AppStore, detects topology / slot-value changes, dispatches
// mesh rebuilds to the worker. Receives Float32Array vertex buffers and
// swaps the GPU vertex buffer. Renders the mesh with a simple Lambertian
// triangle-list pipeline.

/// Set to false while the new 3D display system is being built separately.
/// When false, the worker isn't asked for rebuilds and no mesh is ever
/// uploaded — the viewer renders only the sketch overlay + camera. Flip
/// to true to re-enable the interval-pruning + marching-cubes pipeline.
let private MESH_ENABLED = false

open Fable.Core
open Fable.Core.JsInterop
open Browser.Dom
open Browser.Types
open Server
open PointerMk18.Ui
open WebGPU

// ── Event access helpers ────────────────────────────────────────────────

[<Emit("$0.clientX")>]
let private eClientX (e: obj) : float = jsNative
[<Emit("$0.clientY")>]
let private eClientY (e: obj) : float = jsNative
[<Emit("$0.deltaY")>]
let private eDeltaY (e: obj) : float = jsNative
[<Emit("$0.button")>]
let private eButton (e: obj) : int = jsNative
[<Emit("$0.preventDefault()")>]
let private ePreventDefault (e: obj) : unit = jsNative
[<Emit("$0.addEventListener($1, $2, { passive: false })")>]
let private addEventPassiveFalse (target: obj) (name: string) (h: obj -> unit) : unit = jsNative
[<Emit("$0.addEventListener($1, $2)")>]
let private addEvent (target: obj) (name: string) (h: obj -> unit) : unit = jsNative
[<Emit("new ResizeObserver($0)")>]
let private makeResizeObserver (cb: obj -> unit) : obj = jsNative
[<Emit("$0.observe($1)")>]
let private observe (observer: obj) (target: obj) : unit = jsNative
[<Emit("setTimeout($0, $1)")>]
let private setTimeout (cb: unit -> unit) (ms: int) : int = jsNative
[<Emit("clearTimeout($0)")>]
let private clearTimeout (id: int) : unit = jsNative

// ── Worker bindings ─────────────────────────────────────────────────────

[<Emit("new Worker(new URL('../../worker/fs/MeshWorker.js', import.meta.url), { type: 'module' })")>]
let private createWorker () : obj = jsNative

[<Emit("$0.postMessage($1)")>]
let private workerPost (worker: obj) (data: obj) : unit = jsNative

[<Emit("$0.onmessage = $1")>]
let private workerSetOnMessage (worker: obj) (h: obj -> unit) : unit = jsNative

[<Emit("$0.onerror = $1")>]
let private workerSetOnError (worker: obj) (h: obj -> unit) : unit = jsNative

// ── Shader ──────────────────────────────────────────────────────────────

// Sketch line shader (2D position + colour, projected via the shared Camera
// uniform and a per-sketch frame uniform). Line-list topology.
let private lineWgsl = """
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
"""

// Sketch point shader — instanced billboarded circles. One static quad of
// 6 corner vertices + one instance per point. Viewport uniform converts
// pixel radii to clip-space offsets.
let private pointWgsl = """
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

struct Viewport {
    size: vec2<f32>,
    _pad: vec2<f32>,
};

@group(0) @binding(0) var<uniform> cam: Camera;
@group(1) @binding(0) var<uniform> frame: SketchFrame;
@group(2) @binding(0) var<uniform> viewport: Viewport;

struct QuadIn {
    @location(0) corner: vec2<f32>,
};

struct InstanceIn {
    @location(1) center_2d: vec2<f32>,
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
fn vs(quad: QuadIn, instance: InstanceIn) -> VsOut {
    let world = frame.pos.xyz
        + instance.center_2d.x * frame.x_axis.xyz
        + instance.center_2d.y * frame.y_axis.xyz;
    let center_clip = project_world(world);
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
"""

// Frame gizmo — three screen-space-sized axis lines per frame (X/Y/Z).
// Each vertex carries the frame origin + axis direction + axis length
// (in pixels) + an endpoint flag (0 = origin, 1 = axis tip) + colour.
// Topology = line-list, so pairs of vertices form one segment.
let private gizmoWgsl = """
struct Camera {
    eye: vec3<f32>, _p0: f32,
    forward: vec3<f32>, _p1: f32,
    right: vec3<f32>, _p2: f32,
    up: vec3<f32>, aspect: f32,
};

struct Viewport {
    size: vec2<f32>,
    _pad: vec2<f32>,
};

@group(0) @binding(0) var<uniform> cam: Camera;
@group(1) @binding(0) var<uniform> viewport: Viewport;

struct VsIn {
    @location(0) origin: vec3<f32>,
    @location(1) axis: vec3<f32>,
    @location(2) axis_px: f32,
    @location(3) endpoint: f32,
    @location(4) color: vec4<f32>,
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
    let depth = max(abs(dot(input.origin - cam.eye, cam.forward)), 1e-3);
    let world_per_px = (2.0 * depth * tan(0.3927)) / max(viewport.size.y, 1.0);
    let world = input.origin + input.axis * (input.axis_px * world_per_px * input.endpoint);
    var out: VsOut;
    out.clip_pos = project_world(world);
    out.color = input.color;
    return out;
}

@fragment
fn fs(input: VsOut) -> @location(0) vec4<f32> {
    return input.color;
}
"""

// World-space point pipeline — instance data carries a 3D world position
// directly, so no per-draw frame uniform write is needed. Used by frame
// origin handles (which don't share the sketch frame).
let private worldPointWgsl = """
struct Camera {
    eye: vec3<f32>, _p0: f32,
    forward: vec3<f32>, _p1: f32,
    right: vec3<f32>, _p2: f32,
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
"""

// World-space point pick variant.
let private worldPointPickWgsl = """
struct Camera {
    eye: vec3<f32>, _p0: f32,
    forward: vec3<f32>, _p1: f32,
    right: vec3<f32>, _p2: f32,
    up: vec3<f32>, aspect: f32,
};

struct Viewport {
    size: vec2<f32>,
    _pad: vec2<f32>,
};

@group(0) @binding(0) var<uniform> cam: Camera;
@group(1) @binding(0) var<uniform> viewport: Viewport;

struct VsOut {
    @builtin(position) clip_pos: vec4<f32>,
    @location(0) pick_id: f32,
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
fn vs(
    @location(0) corner: vec2<f32>,
    @location(1) center_world: vec3<f32>,
    @location(2) radius_px: f32,
    @location(3) pick_id: f32
) -> VsOut {
    let center_clip = project_world(center_world);
    let size = max(viewport.size, vec2<f32>(1.0, 1.0));
    let offset_ndc = vec2<f32>(
        corner.x * radius_px * 2.0 / size.x,
        corner.y * radius_px * 2.0 / size.y
    );
    var out: VsOut;
    out.clip_pos = vec4<f32>(
        center_clip.xy + offset_ndc * center_clip.w,
        center_clip.z,
        center_clip.w);
    out.pick_id = pick_id;
    out.local_pos = corner;
    return out;
}

@fragment
fn fs(in: VsOut) -> @location(0) u32 {
    if (dot(in.local_pos, in.local_pos) > 1.0) { discard; }
    return u32(in.pick_id) + 1u;
}
"""

// MSDF label shader — samples a multi-channel signed-distance font atlas.
// Each vertex carries a sketch-local anchor + a pixel-space offset from
// that anchor + uv + colour. The viewport/frame uniform provides screen
// size for the pixel→NDC conversion. Direct port of
// ui/viewer/pipeline-msdf-label.ts.
let private labelWgsl = """
struct Camera {
    eye: vec3<f32>, _p0: f32,
    forward: vec3<f32>, _p1: f32,
    right: vec3<f32>, _p2: f32,
    up: vec3<f32>, aspect: f32,
};

struct LabelUniforms {
    viewport: vec4<f32>,
    frame_pos: vec4<f32>,
    frame_x: vec4<f32>,
    frame_y: vec4<f32>,
};

const ATLAS_PX_RANGE: f32 = 4.0;
const ATLAS_SIZE: f32 = 256.0;
const HALF_FOV: f32 = 0.3927;

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
    let t = tan(HALF_FOV);
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
    let frame_pos = label.frame_pos.xyz;
    let frame_x = label.frame_x.xyz;
    let frame_y = label.frame_y.xyz;

    let anchor_world = frame_pos + input.anchor_2d.x * frame_x + input.anchor_2d.y * frame_y;
    let cam_to_anchor = anchor_world - cam.eye;
    let view_depth = abs(dot(cam_to_anchor, cam.forward));
    let world_per_px = 2.0 * view_depth * tan(HALF_FOV) / label.viewport.y;

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
    return vec4<f32>(input.color.rgb, input.color.a * alpha);
}
"""

// Sketch loop pick shader — takes triangles (x, y, pickId per vertex) and
// writes pickId+1 to the r32uint pick target. No pixel scaling — click
// anywhere inside the triangulated region.
let private loopPickWgsl = """
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
fn vs(@location(0) pos_2d: vec2<f32>, @location(1) pick_id: f32) -> VsOut {
    let world = frame.pos.xyz + pos_2d.x * frame.x_axis.xyz + pos_2d.y * frame.y_axis.xyz;
    var out: VsOut;
    out.clip_pos = project_world(world);
    out.pick_id = pick_id;
    return out;
}

@fragment
fn fs(in: VsOut) -> @location(0) u32 {
    return u32(in.pick_id) + 1u;
}
"""

// Sketch line pick shader — extrudes each line segment into a thick
// triangle-strip quad (2D offset perpendicular to the segment in the
// sketch plane). 6 static corner vertices + one instance per segment.
let private linePickWgsl = """
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
    let t = tan(0.3927);
    let proj = mat4x4<f32>(
        vec4<f32>(1.0 / (cam.aspect * t), 0.0, 0.0, 0.0),
        vec4<f32>(0.0, 1.0 / t, 0.0, 0.0),
        vec4<f32>(0.0, 0.0, -(far + near) / (far - near), -1.0),
        vec4<f32>(0.0, 0.0, -2.0 * far * near / (far - near), 0.0),
    );
    return proj * view * vec4<f32>(pos, 1.0);
}

const THICKNESS: f32 = 0.15;

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
"""

// Sketch point pick shader — same geometry as the visual point shader,
// but the instance attribute is (cx, cy, radiusPx, pickId) and the
// fragment emits pickId+1 to the r32uint pick target.
let private pointPickWgsl = """
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

struct Viewport {
    size: vec2<f32>,
    _pad: vec2<f32>,
};

@group(0) @binding(0) var<uniform> cam: Camera;
@group(1) @binding(0) var<uniform> frame: SketchFrame;
@group(2) @binding(0) var<uniform> viewport: Viewport;

struct VsOut {
    @builtin(position) clip_pos: vec4<f32>,
    @location(0) pick_id: f32,
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
fn vs(
    @location(0) corner: vec2<f32>,
    @location(1) center_2d: vec2<f32>,
    @location(2) radius_px: f32,
    @location(3) pick_id: f32,
) -> VsOut {
    let world = frame.pos.xyz + center_2d.x * frame.x_axis.xyz + center_2d.y * frame.y_axis.xyz;
    let center_clip = project_world(world);
    let size = max(viewport.size, vec2<f32>(1.0, 1.0));
    let offset_ndc = vec2<f32>(
        corner.x * radius_px * 2.0 / size.x,
        corner.y * radius_px * 2.0 / size.y
    );
    var out: VsOut;
    out.clip_pos = vec4<f32>(
        center_clip.xy + offset_ndc * center_clip.w,
        center_clip.z,
        center_clip.w);
    out.pick_id = pick_id;
    out.local_pos = corner;
    return out;
}

@fragment
fn fs(in: VsOut) -> @location(0) u32 {
    if (dot(in.local_pos, in.local_pos) > 1.0) { discard; }
    return u32(in.pick_id) + 1u;
}
"""

// WGSL for the pick pass: outputs `surfaceId + 1` as u32 into an r32uint
// target. Background pixels are 0 (no hit). Shift by +1 lets us distinguish
// "the root surface with id 0" from "missed the mesh entirely".
let private pickWgsl = """
struct Uniforms {
    view: mat4x4<f32>,
    proj: mat4x4<f32>,
    lightDir: vec3<f32>,
    _pad: f32,
};
@group(0) @binding(0) var<uniform> u: Uniforms;

struct VOut {
    @builtin(position) pos: vec4<f32>,
    @location(0) surfaceId: f32,
};

@vertex
fn vs(
    @location(0) pos: vec3<f32>,
    @location(1) normal: vec3<f32>,
    @location(2) surfaceId: f32,
) -> VOut {
    var o: VOut;
    o.pos = u.proj * u.view * vec4<f32>(pos, 1.0);
    o.surfaceId = surfaceId;
    return o;
}

@fragment
fn fs(in: VOut) -> @location(0) u32 {
    return u32(in.surfaceId) + 1u;
}
"""

let private wgsl = """
struct Uniforms {
    view: mat4x4<f32>,
    proj: mat4x4<f32>,
    lightDir: vec3<f32>,
    _pad: f32,
};
@group(0) @binding(0) var<uniform> u: Uniforms;

struct VOut {
    @builtin(position) pos: vec4<f32>,
    @location(0) worldPos: vec3<f32>,
    @location(1) normal: vec3<f32>,
    @location(2) surfaceId: f32,
};

@vertex
fn vs(
    @location(0) pos: vec3<f32>,
    @location(1) normal: vec3<f32>,
    @location(2) surfaceId: f32,
) -> VOut {
    var o: VOut;
    o.pos = u.proj * u.view * vec4<f32>(pos, 1.0);
    o.worldPos = pos;
    o.normal = normal;
    o.surfaceId = surfaceId;
    return o;
}

@fragment
fn fs(in: VOut) -> @location(0) vec4<f32> {
    let n = normalize(in.normal);
    let l = normalize(u.lightDir);
    let diffuse = max(dot(n, l), 0.0);
    let ambient = 0.25;
    let shade = ambient + diffuse * 0.75;

    // Simple surface-id → hue mapping so distinct surfaces read differently.
    let hue = fract(in.surfaceId * 0.37);
    let col = vec3<f32>(
        0.4 + 0.5 * cos(6.283 * (hue + 0.0)),
        0.4 + 0.5 * cos(6.283 * (hue + 0.33)),
        0.4 + 0.5 * cos(6.283 * (hue + 0.67))
    );
    return vec4<f32>(col * shade, 1.0);
}
"""

// ── Viewer mount ────────────────────────────────────────────────────────

let mount (root: HTMLElement) : JS.Promise<obj> =
    promise {
        console.log "F# viewer: Phase 4 mounting"

        let shadow =
            if isNull root.shadowRoot then
                root?attachShadow({| mode = "open" |})
            else
                root.shadowRoot
        shadow?innerHTML <- ""

        let container = document.createElement "div"
        container?style?width <- "100%"
        container?style?height <- "100%"
        container?style?position <- "relative"
        container?style?background <- ViewerColors.PAGE_BG
        shadow?appendChild container |> ignore

        let canvas : HTMLCanvasElement = unbox (document.createElement "canvas")
        canvas?style?width <- "100%"
        canvas?style?height <- "100%"
        canvas?style?display <- "block"
        canvas?style?cursor <- "default"
        container.appendChild canvas |> ignore

        let badge = document.createElement "div"
        badge?style?position <- "absolute"
        badge?style?top <- "8px"
        badge?style?left <- "8px"
        badge?style?padding <- "4px 8px"
        badge?style?background <- "rgba(20,20,20,0.85)"
        badge?style?color <- "#9fd"
        badge?style?fontFamily <- "ui-monospace, monospace"
        badge?style?fontSize <- "11px"
        badge?style?borderRadius <- "3px"
        badge?style?pointerEvents <- "none"
        badge?style?whiteSpace <- "pre"
        badge.textContent <- "F# viewer (Phase 4)\ninitializing…"
        container.appendChild badge |> ignore

        match WebGPU.gpu () with
        | None ->
            badge.textContent <- "ERROR: navigator.gpu missing"
            return box container
        | Some g ->
            let! adapter = g.requestAdapter()
            if isNull adapter then
                badge.textContent <- "ERROR: requestAdapter returned null"
                return box container
            else
                let! device = adapter.requestDevice()

                // MSDF atlas — fetch + upload to GPU. Small file, blocks
                // first frame for ~50 ms. Fonts at /fonts/dekal.{png,json}.
                let! atlas = MsdfAtlas.loadAtlas device "/fonts/dekal.png"
                let! fontMetrics = MsdfAtlas.loadMetrics "/fonts/dekal.json"
                console.log (
                    sprintf "MSDF atlas: %dx%d · %d chars"
                        atlas.Width atlas.Height fontMetrics.Chars.Count)

                let ctx = WebGPU.getWebgpuContext canvas
                let format = g.getPreferredCanvasFormat()
                ctx.configure
                    { device = box device
                      format = format
                      alphaMode = "opaque" }

                // ── Canvas + depth ────────────────────────────────────
                let dpr = window.devicePixelRatio
                let mutable depthTex : IGPUTexture = Unchecked.defaultof<_>
                let mutable pickTex : IGPUTexture = Unchecked.defaultof<_>
                let remakeDepth () =
                    let w : int = canvas?width
                    let h : int = canvas?height
                    if w > 0 && h > 0 then
                        if not (isNull (box depthTex)) then depthTex.destroy()
                        depthTex <-
                            device.createTexture
                                { size = { width = w; height = h; depthOrArrayLayers = 1 }
                                  format = "depth24plus"
                                  usage = GPUTextureUsage.RenderAttachment }
                        if not (isNull (box pickTex)) then pickTex.destroy()
                        pickTex <-
                            device.createTexture
                                { size = { width = w; height = h; depthOrArrayLayers = 1 }
                                  format = "r32uint"
                                  usage = GPUTextureUsage.RenderAttachment ||| GPUTextureUsage.CopySrc }
                let resize () =
                    let w = int (canvas.clientWidth * dpr)
                    let h = int (canvas.clientHeight * dpr)
                    if w > 0 && h > 0 then
                        canvas?width <- w
                        canvas?height <- h
                        remakeDepth ()
                resize ()

                let observer = makeResizeObserver (fun _ -> resize ())
                observe observer canvas

                // ── Pipeline ──────────────────────────────────────────
                let shader = device.createShaderModule { code = wgsl }

                let pipeline =
                    device.createRenderPipeline
                        (box
                            {| layout = "auto"
                               vertex =
                                {| ``module`` = shader
                                   entryPoint = "vs"
                                   buffers =
                                    [| {| arrayStride = 7 * 4
                                          stepMode = "vertex"
                                          attributes =
                                            [| {| shaderLocation = 0; offset = 0;  format = "float32x3" |}
                                               {| shaderLocation = 1; offset = 12; format = "float32x3" |}
                                               {| shaderLocation = 2; offset = 24; format = "float32" |} |] |} |] |}
                               fragment =
                                {| ``module`` = shader
                                   entryPoint = "fs"
                                   targets = [| {| format = format |} |] |}
                               primitive = {| topology = "triangle-list" |}
                               depthStencil =
                                {| format = "depth24plus"
                                   depthWriteEnabled = true
                                   depthCompare = "less" |} |})

                // Uniform: view(16) + proj(16) + lightDir(3) + pad(1) = 144 bytes.
                let uniformBuffer =
                    device.createBuffer
                        { size = 144
                          usage = GPUBufferUsage.Uniform ||| GPUBufferUsage.CopyDst }

                let bindGroup =
                    device.createBindGroup
                        { layout = pipeline.getBindGroupLayout 0
                          entries =
                            [| { binding = 0
                                 resource = box { buffer = uniformBuffer } } |] }

                // ── Pick pipeline ─────────────────────────────────────
                let pickShader = device.createShaderModule { code = pickWgsl }

                let pickPipeline =
                    device.createRenderPipeline
                        (box
                            {| layout = "auto"
                               vertex =
                                {| ``module`` = pickShader
                                   entryPoint = "vs"
                                   buffers =
                                    [| {| arrayStride = 7 * 4
                                          stepMode = "vertex"
                                          attributes =
                                            [| {| shaderLocation = 0; offset = 0;  format = "float32x3" |}
                                               {| shaderLocation = 1; offset = 12; format = "float32x3" |}
                                               {| shaderLocation = 2; offset = 24; format = "float32" |} |] |} |] |}
                               fragment =
                                {| ``module`` = pickShader
                                   entryPoint = "fs"
                                   targets = [| {| format = "r32uint" |} |] |}
                               primitive = {| topology = "triangle-list" |}
                               depthStencil =
                                {| format = "depth24plus"
                                   depthWriteEnabled = true
                                   depthCompare = "less" |} |})

                let pickBindGroup =
                    device.createBindGroup
                        { layout = pickPipeline.getBindGroupLayout 0
                          entries =
                            [| { binding = 0
                                 resource = box { buffer = uniformBuffer } } |] }

                // 256-byte readback buffer (WebGPU min bytesPerRow alignment).
                let pickReadBuffer =
                    device.createBuffer
                        { size = 256
                          usage = GPUBufferUsage.CopyDst ||| GPUBufferUsage.MapRead }

                // ── Camera uniform for sketch overlay pipelines ─────────
                // Separate from the mesh uniform because sketch shaders use
                // the TS viewer's Camera struct layout (eye/forward/right/up).
                // 64 bytes = 4 × vec4<f32>.
                let cameraBuffer =
                    device.createBuffer
                        { size = 64
                          usage = GPUBufferUsage.Uniform ||| GPUBufferUsage.CopyDst }

                let cameraBindGroupLayout =
                    device.createBindGroupLayout
                        { entries =
                            [| box
                                {| binding = 0
                                   visibility = GPUShaderStage.Vertex ||| GPUShaderStage.Fragment
                                   buffer = {| ``type`` = "uniform" |} |} |] }

                let cameraBindGroup =
                    device.createBindGroup
                        { layout = cameraBindGroupLayout
                          entries =
                            [| { binding = 0
                                 resource = box { buffer = cameraBuffer } } |] }

                // Per-sketch frame uniform (position + two axes, 48 bytes
                // padded to 64). Single buffer re-written before drawing
                // each visible sketch.
                let frameBuffer =
                    device.createBuffer
                        { size = 64
                          usage = GPUBufferUsage.Uniform ||| GPUBufferUsage.CopyDst }

                let frameBindGroupLayout =
                    device.createBindGroupLayout
                        { entries =
                            [| box
                                {| binding = 0
                                   visibility = GPUShaderStage.Vertex
                                   buffer = {| ``type`` = "uniform" |} |} |] }

                let frameBindGroup =
                    device.createBindGroup
                        { layout = frameBindGroupLayout
                          entries =
                            [| { binding = 0
                                 resource = box { buffer = frameBuffer } } |] }

                // ── Sketch line pipeline ────────────────────────────────
                let lineShader = device.createShaderModule { code = lineWgsl }

                let linePipelineLayout =
                    device.createPipelineLayout
                        { bindGroupLayouts = [| cameraBindGroupLayout; frameBindGroupLayout |] }

                let linePipeline =
                    device.createRenderPipeline
                        (box
                            {| layout = linePipelineLayout
                               vertex =
                                {| ``module`` = lineShader
                                   entryPoint = "vs"
                                   buffers =
                                    [| {| arrayStride = 6 * 4
                                          stepMode = "vertex"
                                          attributes =
                                            [| {| shaderLocation = 0; offset = 0; format = "float32x2" |}
                                               {| shaderLocation = 1; offset = 8; format = "float32x4" |} |] |} |] |}
                               fragment =
                                {| ``module`` = lineShader
                                   entryPoint = "fs"
                                   targets =
                                    [| {| format = format
                                          blend =
                                            {| color = {| srcFactor = "src-alpha"; dstFactor = "one-minus-src-alpha"; operation = "add" |}
                                               alpha = {| srcFactor = "one"; dstFactor = "one-minus-src-alpha"; operation = "add" |} |} |} |] |}
                               primitive = {| topology = "line-list" |}
                               depthStencil =
                                {| format = "depth24plus"
                                   depthWriteEnabled = false
                                   depthCompare = "less" |} |})

                // Triangle-list pipeline for loop fills. Same vertex layout
                // as the line pipeline; different topology + always blended.
                let triPipeline =
                    device.createRenderPipeline
                        (box
                            {| layout = linePipelineLayout
                               vertex =
                                {| ``module`` = lineShader
                                   entryPoint = "vs"
                                   buffers =
                                    [| {| arrayStride = 6 * 4
                                          stepMode = "vertex"
                                          attributes =
                                            [| {| shaderLocation = 0; offset = 0; format = "float32x2" |}
                                               {| shaderLocation = 1; offset = 8; format = "float32x4" |} |] |} |] |}
                               fragment =
                                {| ``module`` = lineShader
                                   entryPoint = "fs"
                                   targets =
                                    [| {| format = format
                                          blend =
                                            {| color = {| srcFactor = "src-alpha"; dstFactor = "one-minus-src-alpha"; operation = "add" |}
                                               alpha = {| srcFactor = "one"; dstFactor = "one-minus-src-alpha"; operation = "add" |} |} |} |] |}
                               primitive = {| topology = "triangle-list" |}
                               depthStencil =
                                {| format = "depth24plus"
                                   depthWriteEnabled = false
                                   depthCompare = "less" |} |})

                // ── Sketch point pipeline (instanced billboards) ────────
                let viewportBuffer =
                    device.createBuffer
                        { size = 16
                          usage = GPUBufferUsage.Uniform ||| GPUBufferUsage.CopyDst }

                let viewportBindGroupLayout =
                    device.createBindGroupLayout
                        { entries =
                            [| box
                                {| binding = 0
                                   visibility = GPUShaderStage.Vertex
                                   buffer = {| ``type`` = "uniform" |} |} |] }

                let viewportBindGroup =
                    device.createBindGroup
                        { layout = viewportBindGroupLayout
                          entries =
                            [| { binding = 0
                                 resource = box { buffer = viewportBuffer } } |] }

                // Static quad vertex buffer — 6 corners for the billboard.
                let pointQuadBuffer =
                    device.createBuffer
                        { size = 48
                          usage = GPUBufferUsage.Vertex ||| GPUBufferUsage.CopyDst }
                WebGPU.writeFloat32 device.queue pointQuadBuffer 0
                    [| -1.0f; -1.0f; 1.0f; -1.0f; -1.0f; 1.0f; 1.0f; -1.0f; 1.0f; 1.0f; -1.0f; 1.0f |]

                let pointShader = device.createShaderModule { code = pointWgsl }

                let pointPipelineLayout =
                    device.createPipelineLayout
                        { bindGroupLayouts =
                            [| cameraBindGroupLayout
                               frameBindGroupLayout
                               viewportBindGroupLayout |] }

                // Sketch loop pick pipeline — renders triangulated loop
                // fills to the r32uint pick target.
                let loopPickShader = device.createShaderModule { code = loopPickWgsl }
                let loopPickPipelineLayout =
                    device.createPipelineLayout
                        { bindGroupLayouts = [| cameraBindGroupLayout; frameBindGroupLayout |] }
                let loopPickPipeline =
                    device.createRenderPipeline
                        (box
                            {| layout = loopPickPipelineLayout
                               vertex =
                                {| ``module`` = loopPickShader
                                   entryPoint = "vs"
                                   buffers =
                                    [| {| arrayStride = 3 * 4
                                          stepMode = "vertex"
                                          attributes =
                                            [| {| shaderLocation = 0; offset = 0; format = "float32x2" |}
                                               {| shaderLocation = 1; offset = 8; format = "float32" |} |] |} |] |}
                               fragment =
                                {| ``module`` = loopPickShader
                                   entryPoint = "fs"
                                   targets = [| {| format = "r32uint" |} |] |}
                               primitive = {| topology = "triangle-list" |}
                               depthStencil =
                                {| format = "depth24plus"
                                   depthWriteEnabled = true
                                   depthCompare = "less" |} |})

                // Sketch line pick — thick triangle-strip extruded from each
                // line segment. Uses only camera + frame bind groups (no
                // viewport — thickness is fixed in 2D sketch coords).
                let linePickShader = device.createShaderModule { code = linePickWgsl }

                // Static corner vertices for the pick-line quad.
                // Layout (t, s): two triangles forming a rectangle.
                let linePickCornerBuffer =
                    device.createBuffer
                        { size = 48
                          usage = GPUBufferUsage.Vertex ||| GPUBufferUsage.CopyDst }
                WebGPU.writeFloat32 device.queue linePickCornerBuffer 0
                    [| 0.0f; -1.0f; 1.0f; -1.0f; 0.0f; 1.0f; 1.0f; -1.0f; 1.0f; 1.0f; 0.0f; 1.0f |]

                let linePickPipelineLayout =
                    device.createPipelineLayout
                        { bindGroupLayouts = [| cameraBindGroupLayout; frameBindGroupLayout |] }

                let linePickPipeline =
                    device.createRenderPipeline
                        (box
                            {| layout = linePickPipelineLayout
                               vertex =
                                {| ``module`` = linePickShader
                                   entryPoint = "vs"
                                   buffers =
                                    [| // slot 0: static corner (t, s)
                                       {| arrayStride = 2 * 4
                                          stepMode = "vertex"
                                          attributes =
                                            [| {| shaderLocation = 0; offset = 0; format = "float32x2" |} |] |}
                                       // slot 1: per-segment instance (a, b, pickId)
                                       {| arrayStride = 5 * 4
                                          stepMode = "instance"
                                          attributes =
                                            [| {| shaderLocation = 1; offset = 0;  format = "float32x2" |}
                                               {| shaderLocation = 2; offset = 8;  format = "float32x2" |}
                                               {| shaderLocation = 3; offset = 16; format = "float32" |} |] |} |] |}
                               fragment =
                                {| ``module`` = linePickShader
                                   entryPoint = "fs"
                                   targets = [| {| format = "r32uint" |} |] |}
                               primitive = {| topology = "triangle-list" |}
                               depthStencil =
                                {| format = "depth24plus"
                                   depthWriteEnabled = true
                                   depthCompare = "less" |} |})

                // Sketch point pick pipeline — same geometry layout as
                // pointPipeline but writes u32 pickId to r32uint.
                let pointPickShader = device.createShaderModule { code = pointPickWgsl }

                let pointPickPipeline =
                    device.createRenderPipeline
                        (box
                            {| layout = pointPipelineLayout
                               vertex =
                                {| ``module`` = pointPickShader
                                   entryPoint = "vs"
                                   buffers =
                                    [| {| arrayStride = 2 * 4
                                          stepMode = "vertex"
                                          attributes =
                                            [| {| shaderLocation = 0; offset = 0; format = "float32x2" |} |] |}
                                       {| arrayStride = 4 * 4
                                          stepMode = "instance"
                                          attributes =
                                            [| {| shaderLocation = 1; offset = 0;  format = "float32x2" |}
                                               {| shaderLocation = 2; offset = 8;  format = "float32" |}
                                               {| shaderLocation = 3; offset = 12; format = "float32" |} |] |} |] |}
                               fragment =
                                {| ``module`` = pointPickShader
                                   entryPoint = "fs"
                                   targets = [| {| format = "r32uint" |} |] |}
                               primitive = {| topology = "triangle-list" |}
                               depthStencil =
                                {| format = "depth24plus"
                                   depthWriteEnabled = true
                                   depthCompare = "less" |} |})

                let pointPipeline =
                    device.createRenderPipeline
                        (box
                            {| layout = pointPipelineLayout
                               vertex =
                                {| ``module`` = pointShader
                                   entryPoint = "vs"
                                   buffers =
                                    [| // slot 0: static quad corners
                                       {| arrayStride = 2 * 4
                                          stepMode = "vertex"
                                          attributes =
                                            [| {| shaderLocation = 0; offset = 0; format = "float32x2" |} |] |}
                                       // slot 1: per-point instance data
                                       {| arrayStride = 7 * 4
                                          stepMode = "instance"
                                          attributes =
                                            [| {| shaderLocation = 1; offset = 0;  format = "float32x2" |}
                                               {| shaderLocation = 2; offset = 8;  format = "float32" |}
                                               {| shaderLocation = 3; offset = 12; format = "float32x4" |} |] |} |] |}
                               fragment =
                                {| ``module`` = pointShader
                                   entryPoint = "fs"
                                   targets =
                                    [| {| format = format
                                          blend =
                                            {| color = {| srcFactor = "src-alpha"; dstFactor = "one-minus-src-alpha"; operation = "add" |}
                                               alpha = {| srcFactor = "one"; dstFactor = "one-minus-src-alpha"; operation = "add" |} |} |} |] |}
                               primitive = {| topology = "triangle-list" |}
                               depthStencil =
                                {| format = "depth24plus"
                                   depthWriteEnabled = false
                                   depthCompare = "less" |} |})

                // ── Frame gizmo pipeline ────────────────────────────────
                let gizmoShader = device.createShaderModule { code = gizmoWgsl }
                let gizmoPipelineLayout =
                    device.createPipelineLayout
                        { bindGroupLayouts =
                            [| cameraBindGroupLayout; viewportBindGroupLayout |] }
                let gizmoPipeline =
                    device.createRenderPipeline
                        (box
                            {| layout = gizmoPipelineLayout
                               vertex =
                                {| ``module`` = gizmoShader
                                   entryPoint = "vs"
                                   buffers =
                                    [| {| arrayStride = 12 * 4
                                          stepMode = "vertex"
                                          attributes =
                                            [| {| shaderLocation = 0; offset = 0;  format = "float32x3" |}
                                               {| shaderLocation = 1; offset = 12; format = "float32x3" |}
                                               {| shaderLocation = 2; offset = 24; format = "float32" |}
                                               {| shaderLocation = 3; offset = 28; format = "float32" |}
                                               {| shaderLocation = 4; offset = 32; format = "float32x4" |} |] |} |] |}
                               fragment =
                                {| ``module`` = gizmoShader
                                   entryPoint = "fs"
                                   targets =
                                    [| {| format = format
                                          blend =
                                            {| color = {| srcFactor = "src-alpha"; dstFactor = "one-minus-src-alpha"; operation = "add" |}
                                               alpha = {| srcFactor = "one"; dstFactor = "one-minus-src-alpha"; operation = "add" |} |} |} |] |}
                               primitive = {| topology = "line-list" |}
                               depthStencil =
                                {| format = "depth24plus"
                                   depthWriteEnabled = false
                                   depthCompare = "less" |} |})

                // ── World-space point pipelines ─────────────────────────
                // Frame origins need to render at arbitrary world positions
                // without per-draw uniform writes (those would clobber
                // `frameBuffer` and break the sketch renders that share the
                // same submit). Uses camera + viewport bind groups only.
                let worldPointPipelineLayout =
                    device.createPipelineLayout
                        { bindGroupLayouts =
                            [| cameraBindGroupLayout; viewportBindGroupLayout |] }

                let worldPointShader = device.createShaderModule { code = worldPointWgsl }
                let worldPointPipeline =
                    device.createRenderPipeline
                        (box
                            {| layout = worldPointPipelineLayout
                               vertex =
                                {| ``module`` = worldPointShader
                                   entryPoint = "vs"
                                   buffers =
                                    [| {| arrayStride = 2 * 4
                                          stepMode = "vertex"
                                          attributes =
                                            [| {| shaderLocation = 0; offset = 0; format = "float32x2" |} |] |}
                                       {| arrayStride = 8 * 4
                                          stepMode = "instance"
                                          attributes =
                                            [| {| shaderLocation = 1; offset = 0;  format = "float32x3" |}
                                               {| shaderLocation = 2; offset = 12; format = "float32" |}
                                               {| shaderLocation = 3; offset = 16; format = "float32x4" |} |] |} |] |}
                               fragment =
                                {| ``module`` = worldPointShader
                                   entryPoint = "fs"
                                   targets =
                                    [| {| format = format
                                          blend =
                                            {| color = {| srcFactor = "src-alpha"; dstFactor = "one-minus-src-alpha"; operation = "add" |}
                                               alpha = {| srcFactor = "one"; dstFactor = "one-minus-src-alpha"; operation = "add" |} |} |} |] |}
                               primitive = {| topology = "triangle-list" |}
                               depthStencil =
                                {| format = "depth24plus"
                                   depthWriteEnabled = false
                                   depthCompare = "less" |} |})

                let worldPointPickShader = device.createShaderModule { code = worldPointPickWgsl }
                let worldPointPickPipeline =
                    device.createRenderPipeline
                        (box
                            {| layout = worldPointPipelineLayout
                               vertex =
                                {| ``module`` = worldPointPickShader
                                   entryPoint = "vs"
                                   buffers =
                                    [| {| arrayStride = 2 * 4
                                          stepMode = "vertex"
                                          attributes =
                                            [| {| shaderLocation = 0; offset = 0; format = "float32x2" |} |] |}
                                       {| arrayStride = 5 * 4
                                          stepMode = "instance"
                                          attributes =
                                            [| {| shaderLocation = 1; offset = 0;  format = "float32x3" |}
                                               {| shaderLocation = 2; offset = 12; format = "float32" |}
                                               {| shaderLocation = 3; offset = 16; format = "float32" |} |] |} |] |}
                               fragment =
                                {| ``module`` = worldPointPickShader
                                   entryPoint = "fs"
                                   targets = [| {| format = "r32uint" |} |] |}
                               primitive = {| topology = "triangle-list" |}
                               depthStencil =
                                {| format = "depth24plus"
                                   depthWriteEnabled = true
                                   depthCompare = "less" |} |})

                // ── MSDF label pipeline ─────────────────────────────────
                let labelShader = device.createShaderModule { code = labelWgsl }

                let labelUniformBuffer =
                    device.createBuffer
                        { size = 64
                          usage = GPUBufferUsage.Uniform ||| GPUBufferUsage.CopyDst }

                let labelBindGroupLayout =
                    device.createBindGroupLayout
                        { entries =
                            [| box
                                {| binding = 0
                                   visibility = GPUShaderStage.Vertex
                                   buffer = {| ``type`` = "uniform" |} |}
                               box
                                {| binding = 1
                                   visibility = GPUShaderStage.Fragment
                                   texture = {| sampleType = "float"; viewDimension = "2d" |} |}
                               box
                                {| binding = 2
                                   visibility = GPUShaderStage.Fragment
                                   sampler = {| ``type`` = "filtering" |} |} |] }

                let labelBindGroup =
                    device.createBindGroup
                        { layout = labelBindGroupLayout
                          entries =
                            [| { binding = 0; resource = box { buffer = labelUniformBuffer } }
                               { binding = 1; resource = box (atlas.Texture.createView()) }
                               { binding = 2; resource = box atlas.Sampler } |] }

                let labelPipelineLayout =
                    device.createPipelineLayout
                        { bindGroupLayouts = [| cameraBindGroupLayout; labelBindGroupLayout |] }

                let labelPipeline =
                    device.createRenderPipeline
                        (box
                            {| layout = labelPipelineLayout
                               vertex =
                                {| ``module`` = labelShader
                                   entryPoint = "vs"
                                   buffers =
                                    [| {| arrayStride = 10 * 4
                                          stepMode = "vertex"
                                          attributes =
                                            [| {| shaderLocation = 0; offset = 0;  format = "float32x2" |}
                                               {| shaderLocation = 1; offset = 8;  format = "float32x2" |}
                                               {| shaderLocation = 2; offset = 16; format = "float32x2" |}
                                               {| shaderLocation = 3; offset = 24; format = "float32x4" |} |] |} |] |}
                               fragment =
                                {| ``module`` = labelShader
                                   entryPoint = "fs"
                                   targets =
                                    [| {| format = format
                                          blend =
                                            {| color = {| srcFactor = "src-alpha"; dstFactor = "one-minus-src-alpha"; operation = "add" |}
                                               alpha = {| srcFactor = "one"; dstFactor = "one-minus-src-alpha"; operation = "add" |} |} |} |] |}
                               primitive = {| topology = "triangle-list" |}
                               depthStencil =
                                {| format = "depth24plus"
                                   depthWriteEnabled = false
                                   depthCompare = "less" |} |})

                // ── Reusable vertex buffers (grow-only pool) ──────────
                // Each per-frame draw previously created + leaked a GPU
                // buffer. We now keep one persistent buffer per slot and
                // grow it on demand.
                let mutable submittedFrameCount = 0
                let retiredBuffers = ResizeArray<IGPUBuffer * int>()

                let scheduleBufferDestroy (buffer: IGPUBuffer) =
                    // WebGPU work completes asynchronously relative to the
                    // CPU frame loop. Keep replaced buffers alive for a few
                    // submitted frames so in-flight command buffers never
                    // reference a destroyed resource.
                    retiredBuffers.Add(buffer, submittedFrameCount + 8)

                let flushRetiredBuffers () =
                    let mutable write = 0
                    for i in 0 .. retiredBuffers.Count - 1 do
                        let (buffer, retireAfter) = retiredBuffers.[i]
                        if retireAfter <= submittedFrameCount then
                            buffer.destroy()
                        else
                            retiredBuffers.[write] <- retiredBuffers.[i]
                            write <- write + 1
                    while retiredBuffers.Count > write do
                        retiredBuffers.RemoveAt(retiredBuffers.Count - 1)

                let makeSlot () : IGPUBuffer option ref * int ref =
                    ref None, ref 0

                let upload (bufRef: IGPUBuffer option ref) (capRef: int ref) (data: float32[]) : IGPUBuffer =
                    let bytes = data.Length * 4
                    if !capRef < bytes then
                        !bufRef |> Option.iter scheduleBufferDestroy
                        let newCap = max 1024 (max bytes (!capRef * 2))
                        capRef := newCap
                        bufRef :=
                            Some (device.createBuffer
                                    { size = newCap
                                      usage = GPUBufferUsage.Vertex ||| GPUBufferUsage.CopyDst })
                    match !bufRef with
                    | Some buf ->
                        WebGPU.writeFloat32 device.queue buf 0 data
                        buf
                    | None -> failwith "unreachable"

                let gridSlot = makeSlot ()
                let loopFillSlot = makeSlot ()
                let gizmoSlot = makeSlot ()
                let constraintLineSlot = makeSlot ()
                let sketchLineSlot = makeSlot ()
                let sketchPointSlot = makeSlot ()
                let labelSlot = makeSlot ()
                let loopPickSlot = makeSlot ()
                let linePickSlot = makeSlot ()
                let pointPickSlot = makeSlot ()
                let dimPickSlot = makeSlot ()
                let toolPreviewLineSlot = makeSlot ()
                let toolPreviewPointSlot = makeSlot ()
                let placementPreviewLineSlot = makeSlot ()
                let placementPreviewLabelSlot = makeSlot ()
                let frameOriginPointSlot = makeSlot ()
                let frameOriginPickSlot = makeSlot ()
                let frameGizmoSlot = makeSlot ()

                // Tool cursor in sketch-local coords (one position, shared
                // across the active editing sketch). Updated on mousemove.
                let mutable toolCursor : (ActionId * float * float) option = None

                // ── Mesh state ────────────────────────────────────────
                let mutable meshBuffer : IGPUBuffer option = None
                let mutable meshVertexCount = 0
                let mutable meshTriangleCount = 0
                let mutable lastBuildMs = 0.0
                let mutable lastEvalCount = 0

                let updateBadge () =
                    badge.textContent <-
                        sprintf "F# viewer (Phase 4)\n%d triangles · %d evals · %.0f ms"
                            meshTriangleCount lastEvalCount lastBuildMs

                updateBadge ()

                // ── Worker ────────────────────────────────────────────
                let worker = createWorker ()
                let mutable workerReady = false
                let mutable workerBusy = false
                let mutable pendingRebuild = false
                let mutable lastSentCompiled : obj = null
                let mutable lastSentSlotValues : obj = null
                let mutable liveDragActive = false
                let camera = Camera.create ()
                // surfaceIndex → PickId for dispatching selection messages.
                // Rebuilt on every topology change.
                let mutable surfacePickIds : int[] = [||]
                // PickId → Pickable lookup for resolving GPU picks.
                let mutable pickableById : Map<int, Pickable> = Map.empty

                let requestRebuild () =
                    if not MESH_ENABLED then () else
                    if not workerReady then () else
                    if workerBusy then pendingRebuild <- true
                    else
                        workerBusy <- true
                        let state = AppStore.store.State
                        let halfExtent = camera.Distance * 0.75
                        // Drop depth during an active drag so rebuilds land
                        // at interactive rates; restore to full detail on
                        // release.
                        let depth = if liveDragActive then 5 else 7
                        workerPost worker
                            {| kind = "rebuild"
                               slotValues = state.SlotValues
                               halfExtent = halfExtent
                               maxDepth = depth |}

                let ensureTopology () =
                    let state = AppStore.store.State
                    let compiled = box state.Compiled
                    if compiled <> lastSentCompiled then
                        lastSentCompiled <- compiled
                        let model = ViewerPipeline.viewerModel state

                        // Map ActionId → PickId from PickSurface pickables.
                        let actionIdToPickId =
                            model.Pickables
                            |> List.choose (fun p ->
                                match p with
                                | PickSurface(pid, aid) -> Some (aid, pid)
                                | _ -> None)
                            |> Map.ofList
                        surfacePickIds <-
                            model.Surfaces
                            |> List.map (fun s ->
                                Map.tryFind s.ActionId actionIdToPickId
                                |> Option.defaultValue -1)
                            |> List.toArray

                        // PickId → Pickable for drag/selection dispatch.
                        pickableById <-
                            model.Pickables
                            |> List.map (fun p -> Pickable.pickId p, p)
                            |> Map.ofList

                        workerPost worker
                            {| kind = "topology"
                               surfaces = model.Surfaces
                               pickIds = surfacePickIds |}

                /// Called on every AppStore dispatch. Skips work unless the
                /// inputs that actually affect the mesh (topology or slot
                /// values) changed. Worker coalesces in-flight requests so
                /// rapid-fire calls are safe.
                let maybeRebuild () =
                    let state = AppStore.store.State
                    let compiled = box state.Compiled
                    let values = box state.SlotValues
                    if compiled <> lastSentCompiled || values <> lastSentSlotValues then
                        lastSentSlotValues <- values
                        ensureTopology ()
                        requestRebuild ()

                workerSetOnMessage worker (fun e ->
                    let data = e?data
                    let kind : string = data?kind
                    match kind with
                    | "ready" ->
                        workerReady <- true
                        ensureTopology ()
                        requestRebuild ()
                    | "topology-ack" ->
                        let n : int = unbox (data?surfaceCount)
                        console.log (sprintf "worker topology-ack: %d surfaces" n)
                    | "mesh" ->
                        let vertices : obj = data?vertices
                        let vertexCount : int = unbox (data?vertexCount)
                        let triangleCount : int = unbox (data?triangleCount)
                        let evalCount : int = unbox (data?evalCount)
                        let buildMs : float = unbox (data?buildMs)

                        meshBuffer |> Option.iter scheduleBufferDestroy
                        if vertexCount = 0 then
                            meshBuffer <- None
                            meshVertexCount <- 0
                            meshTriangleCount <- 0
                        else
                            let bytes : int = unbox ((unbox<obj> vertices)?byteLength)
                            let buf =
                                device.createBuffer
                                    { size = bytes
                                      usage = GPUBufferUsage.Vertex ||| GPUBufferUsage.CopyDst }
                            WebGPU.writeBufferRaw device.queue buf 0 vertices
                            meshBuffer <- Some buf
                            meshVertexCount <- vertexCount
                            meshTriangleCount <- triangleCount

                        lastBuildMs <- buildMs
                        lastEvalCount <- evalCount
                        updateBadge ()

                        workerBusy <- false
                        if pendingRebuild then
                            pendingRebuild <- false
                            requestRebuild ()
                    | _ -> ())

                workerSetOnError worker (fun e ->
                    console.error e
                    badge.textContent <- sprintf "worker error: %A" (e?message))

                // Subscribe to AppStore. `maybeRebuild` skips no-op state
                // changes (hover, selection) and posts to the worker on
                // every real change — the worker's in-flight coalescing
                // handles rapid-fire calls during drag.
                Store.subscribe AppStore.store (fun () -> maybeRebuild ())

                // ── Mouse / wheel ─────────────────────────────────────
                let mutable pickInFlight = false
                // Drag state.
                let mutable dragButton : int option = None
                let mutable dragStart : float * float = 0.0, 0.0
                let mutable dragLast : float * float = 0.0, 0.0
                let mutable dragPickable : Pickable option = None
                let mutable dragActive = false
                let DRAG_THRESHOLD_PX = 4.0

                let sketchPlane (sketchId: ActionId) : (Vec3 * Vec3 * Vec3) option =
                    let state = AppStore.store.State
                    let viewState = ViewerPipeline.viewerState state
                    viewState.SketchTransforms
                    |> List.tryFind (fun f -> f.Id = sketchId)
                    |> Option.map (fun f ->
                        let origin = f.Transform.Trans
                        let xAxis = f.Transform.Rot.Rotate({ X = 1.0; Y = 0.0; Z = 0.0 })
                        let yAxis = f.Transform.Rot.Rotate({ X = 0.0; Y = 1.0; Z = 0.0 })
                        origin, xAxis, yAxis)

                /// Mouse → sketch-local 2D coordinates via ray / plane intersection.
                let mouseToSketchLocal (sketchId: ActionId) (mx: float) (my: float)
                    : (float * float) option =
                    match sketchPlane sketchId with
                    | None -> None
                    | Some (origin, xAxis, yAxis) ->
                        let rect = canvas?getBoundingClientRect ()
                        let localX = (mx - rect?left) * dpr
                        let localY = (my - rect?top) * dpr
                        let w = canvas.clientWidth * dpr
                        let h = canvas.clientHeight * dpr
                        let ray = Camera.screenToRay w h camera localX localY
                        Camera.rayPlaneIntersection ray origin xAxis yAxis

                let pickAt (px: int) (py: int) : JS.Promise<uint32> = promise {
                    if pickInFlight then return 0u
                    else
                        pickInFlight <- true
                        let encoder = device.createCommandEncoder()
                        WebGPU.copyTextureToBuffer1x1 encoder pickTex px py pickReadBuffer
                        device.queue.submit [| encoder.finish() |]
                        do! pickReadBuffer.mapAsync GPUMapMode.Read
                        let arr = pickReadBuffer.getMappedRange()
                        let id = WebGPU.readFirstU32 arr
                        pickReadBuffer.unmap()
                        pickInFlight <- false
                        return id
                }

                addEventPassiveFalse canvas "mousedown" (fun e ->
                    let button = eButton e
                    // Middle-button has a default "autoscroll" cursor in
                    // browsers that we want to suppress while panning.
                    if button = 1 then ePreventDefault e
                    let x, y = eClientX e, eClientY e
                    dragButton <- Some button
                    dragStart <- x, y
                    dragLast <- x, y
                    dragPickable <- None
                    dragActive <- false
                    if button = 0 then
                        // If the user is in a sketch tool (line, circle, …),
                        // a click with no entity under the cursor adds a
                        // tool point instead of picking.
                        let state = AppStore.store.State
                        let viewState = ViewerPipeline.viewerState state
                        let toolActive =
                            viewState.SketchUi.EditMode
                            && viewState.SketchUi.Tool <> "none"
                            && viewState.SketchUi.Tool <> ""
                            && viewState.SketchUi.Tool <> "select"
                        let placementActive =
                            viewState.SketchUi.EditMode
                            && viewState.SketchUi.ConstraintPlacementMode.IsSome

                        let rect = canvas?getBoundingClientRect ()
                        let px = int ((x - rect?left) * dpr)
                        let py = int ((y - rect?top) * dpr)
                        pickAt px py
                        |> Promise.iter (fun id ->
                            if placementActive then
                                // Constraint placement flow: clicking on an
                                // entity adds it to the draft; clicking empty
                                // space with a pending placement drops the
                                // label at the cursor.
                                dragPickable <- None
                                let hovered =
                                    if id = 0u then None
                                    else
                                        let pickId = int id - 1
                                        Map.tryFind pickId pickableById
                                let isTargetable =
                                    match hovered with
                                    | Some (PickPoint _) | Some (PickLine _)
                                    | Some (PickCircle _) | Some (PickArc _)
                                    | Some (PickFrameOrigin _) -> true
                                    | _ -> false
                                if isTargetable then
                                    let pickId = int id - 1
                                    Store.dispatch AppStore.store
                                        (ViewerHover [ { PickId = pickId; Score = 0.0f } ])
                                    Store.dispatch AppStore.store ViewerDimensionClickTarget
                                else
                                    let latest = ViewerPipeline.viewerState AppStore.store.State
                                    match latest.SketchUi.PendingConstraintPlacement, toolCursor with
                                    | Some _, Some (_, u, v) ->
                                        Store.dispatch AppStore.store
                                            (ViewerPlaceConstraint(u, v))
                                    | _ -> ()
                            elif toolActive then
                                // While a tool is active, every click is a
                                // tool click. Core snaps to HoveredTarget
                                // if it's a point of the active sketch.
                                dragPickable <- None
                                match toolCursor with
                                | Some (_, u, v) ->
                                    Store.dispatch AppStore.store
                                        (ViewerToolClick(u, v))
                                | None -> ()
                            elif id = 0u then
                                dragPickable <- None
                                Store.dispatch AppStore.store (ViewerPick("replace", []))
                            else
                                let pickId = int id - 1
                                dragPickable <- Map.tryFind pickId pickableById
                                Store.dispatch AppStore.store
                                    (ViewerPick("replace",
                                        [ { PickId = pickId; Score = 0.0f } ]))))

                addEvent canvas "dblclick" (fun e ->
                    // Double-click:
                    //   • on a dimension label  → start editing its value
                    //   • on any other sketch entity → enter edit mode
                    //     for that sketch (so users don't need the keyboard).
                    ePreventDefault e
                    let rect = canvas?getBoundingClientRect ()
                    let px = int ((eClientX e - rect?left) * dpr)
                    let py = int ((eClientY e - rect?top) * dpr)
                    pickAt px py
                    |> Promise.iter (fun id ->
                        if id <> 0u then
                            let pickId = int id - 1
                            match Map.tryFind pickId pickableById with
                            | Some (PickDimension(_, sid, idx, _)) ->
                                let vs = ViewerPipeline.viewerState AppStore.store.State
                                if not vs.SketchUi.EditMode then
                                    Store.dispatch AppStore.store (SelectAction sid)
                                    Store.dispatch AppStore.store ToggleSketchEdit
                                Store.dispatch AppStore.store (StartEditingDimension idx)
                            | Some p ->
                                let sketchIdOpt =
                                    match p with
                                    | PickPoint(_, sid, _, _, _)
                                    | PickLine(_, sid, _, _, _)
                                    | PickCircle(_, sid, _, _, _)
                                    | PickArc(_, sid, _, _, _, _, _)
                                    | PickLoop(_, sid, _, _) -> Some sid
                                    | _ -> None
                                match sketchIdOpt with
                                | Some sid ->
                                    let vs = ViewerPipeline.viewerState AppStore.store.State
                                    Store.dispatch AppStore.store (SelectAction sid)
                                    if not vs.SketchUi.EditMode then
                                        Store.dispatch AppStore.store ToggleSketchEdit
                                | None -> ()
                            | None -> ()))

                let activeEditSketchId (state: EditorState) : ActionId option =
                    let vs = ViewerPipeline.viewerState state
                    if not vs.SketchUi.EditMode then None
                    else
                        state.Doc.SelectedId
                        |> Option.filter (fun id ->
                            vs.SketchTransforms |> List.exists (fun t -> t.Id = id))

                addEvent window "mousemove" (fun e ->
                    // Track the cursor in sketch-local coords of the active
                    // edit sketch, for tool preview rendering.
                    let state = AppStore.store.State
                    match activeEditSketchId state with
                    | Some sid ->
                        let mx, my = eClientX e, eClientY e
                        match mouseToSketchLocal sid mx my with
                        | Some (u, v) ->
                            toolCursor <- Some (sid, u, v)
                            // Live-preview the dimension label position
                            // while placement is pending.
                            let vs = ViewerPipeline.viewerState state
                            if vs.SketchUi.PendingConstraintPlacement.IsSome then
                                Store.dispatch AppStore.store
                                    (SetConstraintPlacementCursor
                                        (Some (sid, { X = u; Y = v })))
                        | None -> ()
                    | None ->
                        toolCursor <- None

                    // Hover dispatch when no drag is in progress.
                    if dragButton.IsNone && not pickInFlight then
                        let rect = canvas?getBoundingClientRect ()
                        let px = int ((eClientX e - rect?left) * dpr)
                        let py = int ((eClientY e - rect?top) * dpr)
                        let w : int = canvas?width
                        let h : int = canvas?height
                        if px >= 0 && py >= 0 && px < w && py < h then
                            pickAt px py
                            |> Promise.iter (fun id ->
                                if id = 0u then
                                    Store.dispatch AppStore.store (ViewerHover [])
                                else
                                    let pickId = int id - 1
                                    Store.dispatch AppStore.store
                                        (ViewerHover [ { PickId = pickId; Score = 0.0f } ]))

                    match dragButton with
                    | None -> ()
                    | Some button ->
                        let x, y = eClientX e, eClientY e
                        let (lx, ly) = dragLast
                        let dx = x - lx
                        let dy = y - ly
                        let (sx, sy) = dragStart
                        let movedPx = sqrt ((x - sx) * (x - sx) + (y - sy) * (y - sy))

                        match button, dragPickable with
                        | 0, Some (PickPoint(_, sid, pid, _, _)) ->
                            // Point drag. Upgrade to active once past threshold;
                            // thereafter dispatch UpdateSketchDragTarget every move.
                            if not dragActive && movedPx > DRAG_THRESHOLD_PX then
                                match mouseToSketchLocal sid x y with
                                | Some (u, v) ->
                                    dragActive <- true
                                    liveDragActive <- true
                                    Store.dispatch AppStore.store
                                        (ViewerMessages.beginPointDrag sid pid u v)
                                | None -> ()
                            elif dragActive then
                                match mouseToSketchLocal sid x y with
                                | Some (u, v) ->
                                    Store.dispatch AppStore.store
                                        (ViewerMessages.updateSketchDrag u v)
                                | None -> ()
                        | 0, Some (PickDimension(_, sid, cix, _)) ->
                            if not dragActive && movedPx > DRAG_THRESHOLD_PX then
                                match mouseToSketchLocal sid x y with
                                | Some (u, v) ->
                                    dragActive <- true
                                    liveDragActive <- true
                                    Store.dispatch AppStore.store
                                        (ViewerMessages.beginConstraintLabelDrag sid cix u v)
                                | None -> ()
                            elif dragActive then
                                match mouseToSketchLocal sid x y with
                                | Some (u, v) ->
                                    Store.dispatch AppStore.store
                                        (ViewerMessages.updateSketchDrag u v)
                                | None -> ()
                        | 0, _ ->
                            // No dragged point → camera orbit.
                            Camera.orbit camera dx dy
                        | 1, _ ->
                            Camera.pan camera dx dy (canvas.clientHeight * dpr)
                        | 2, _ ->
                            Camera.orbit camera dx dy
                        | _ -> ()

                        dragLast <- x, y)

                addEvent window "mouseup" (fun _ ->
                    if dragActive then
                        Store.dispatch AppStore.store ViewerMessages.finishSketchDrag
                    dragButton <- None
                    dragPickable <- None
                    dragActive <- false
                    if liveDragActive then
                        liveDragActive <- false
                        // Kick a final full-depth rebuild now that the drag
                        // has settled. Not gated on slot change — the last
                        // change may have already been emitted.
                        requestRebuild ())

                addEventPassiveFalse canvas "contextmenu" (fun e -> ePreventDefault e)

                addEventPassiveFalse canvas "wheel" (fun e ->
                    ePreventDefault e
                    let dy = eDeltaY e
                    let rect = canvas?getBoundingClientRect ()
                    let localX = (eClientX e - rect?left) * dpr
                    let localY = (eClientY e - rect?top) * dpr
                    Camera.zoomTowardsPointer camera
                        (canvas.clientWidth * dpr)
                        (canvas.clientHeight * dpr)
                        localX localY dy
                    // Zoom changes the view box → rebuild (not gated by
                    // slot/topology change since those haven't moved).
                    requestRebuild ())

                // ── Render loop ───────────────────────────────────────
                let viewMatrix () =
                    let b = Camera.basis camera
                    let eye = (float32 b.Eye.X, float32 b.Eye.Y, float32 b.Eye.Z)
                    let tgt = (float32 camera.Target.X, float32 camera.Target.Y, float32 camera.Target.Z)
                    ViewerMath.lookAt eye tgt (0.0f, 0.0f, 1.0f)

                let projMatrix (w: float) (h: float) =
                    let aspect = float32 (w / max h 1.0)
                    // fovY = 2 * HALF_FOV. HALF_FOV is ~0.3927 rad, so fovY ~ 0.785 (45°).
                    ViewerMath.perspective (float32 (Camera.HALF_FOV * 2.0)) aspect 0.05f 2000.0f

                let mutable labelDebugLogged = false
                let rec frame (_: float) =
                    let w : int = canvas?width
                    let h : int = canvas?height
                    let v = viewMatrix ()
                    let p = projMatrix (float w) (float h)
                    let vCol = ViewerMath.toColumnMajorFloat32 v
                    let pCol = ViewerMath.toColumnMajorFloat32 p

                    // Light: a fixed world-space direction, normalised.
                    let lx, ly, lz =
                        let (rx, ry, rz) = (0.4f, 0.6f, 0.8f)
                        let m = sqrt (rx * rx + ry * ry + rz * rz)
                        rx / m, ry / m, rz / m

                    let uniformData =
                        Array.concat [ vCol; pCol; [| lx; ly; lz; 0.0f |] ]
                    WebGPU.writeFloat32 device.queue uniformBuffer 0 uniformData

                    // Shared camera uniform for sketch / overlay pipelines.
                    let b = Camera.basis camera
                    let aspect = float32 (float w / max (float h) 1.0)
                    let cameraData =
                        [| float32 b.Eye.X;     float32 b.Eye.Y;     float32 b.Eye.Z;     0.0f
                           float32 b.Forward.X; float32 b.Forward.Y; float32 b.Forward.Z; 0.0f
                           float32 b.Right.X;   float32 b.Right.Y;   float32 b.Right.Z;   0.0f
                           float32 b.Up.X;      float32 b.Up.Y;      float32 b.Up.Z;      aspect |]
                    WebGPU.writeFloat32 device.queue cameraBuffer 0 cameraData

                    // Viewport uniform for screen-space sizing (point billboards).
                    WebGPU.writeFloat32 device.queue viewportBuffer 0
                        [| float32 w; float32 h; 0.0f; 0.0f |]

                    let colorView = ctx.getCurrentTexture().createView()
                    let depthView = depthTex.createView()
                    let pickView = pickTex.createView()
                    let encoder = device.createCommandEncoder()

                    // Colour pass. Clear colour matches PAGE_BG (#FEFCF3).
                    let colorPass =
                        WebGPU.beginRenderPassClearColor encoder colorView 0.996 0.988 0.953 depthView
                    colorPass.setPipeline pipeline
                    colorPass.setBindGroup(0, bindGroup)
                    match meshBuffer with
                    | Some buf when meshVertexCount > 0 ->
                        colorPass.setVertexBuffer(0, buf)
                        colorPass.draw meshVertexCount
                    | _ -> ()

                    // Sketch overlay — line-list per visible sketch.
                    let state = AppStore.store.State
                    let model = ViewerPipeline.viewerModel state
                    let viewState = ViewerPipeline.viewerState state
                    let frameById =
                        viewState.SketchTransforms
                        |> List.map (fun f -> f.Id, f.Transform)
                        |> Map.ofList

                    let isVisible (actionId: string) =
                        Map.tryFind actionId viewState.Visible
                        |> Option.defaultValue true

                    for sketch in model.Sketches do
                        match Map.tryFind sketch.Id frameById with
                        | None -> ()
                        | Some _ when not (isVisible sketch.Id) -> ()
                        | Some transform ->
                            // Frame axes in world space.
                            let pos = transform.Trans
                            let xAxis = transform.Rot.Rotate({ X = 1.0; Y = 0.0; Z = 0.0 })
                            let yAxis = transform.Rot.Rotate({ X = 0.0; Y = 1.0; Z = 0.0 })
                            let frameData =
                                [| float32 pos.X;   float32 pos.Y;   float32 pos.Z;   0.0f
                                   float32 xAxis.X; float32 xAxis.Y; float32 xAxis.Z; 0.0f
                                   float32 yAxis.X; float32 yAxis.Y; float32 yAxis.Z; 0.0f
                                   0.0f; 0.0f; 0.0f; 0.0f |]
                            WebGPU.writeFloat32 device.queue frameBuffer 0 frameData

                            // Label uniform: viewport + sketch frame. Written
                            // unconditionally so any label draw (real or
                            // placement preview) picks up the right frame.
                            let canvasWpx = float32 (canvas.clientWidth * dpr)
                            let canvasHpx = float32 (canvas.clientHeight * dpr)
                            let labelUniform =
                                [| canvasWpx; canvasHpx; 0.0f; 0.0f
                                   float32 pos.X;   float32 pos.Y;   float32 pos.Z;   0.0f
                                   float32 xAxis.X; float32 xAxis.Y; float32 xAxis.Z; 0.0f
                                   float32 yAxis.X; float32 yAxis.Y; float32 yAxis.Z; 0.0f |]
                            WebGPU.writeFloat32 device.queue labelUniformBuffer 0 labelUniform

                            // Grid — drawn first so entities overlay it.
                            let gridData =
                                SketchOverlay.buildSketchGridBuffer
                                    sketch.Id sketch.Sketch.Entities
                                    state.Compiled.Slots.Index viewState.Params
                                    1.0 10

                            if gridData.Length > 0 then
                                let (br, cr) = gridSlot
                                let gridBuf = upload br cr gridData
                                colorPass.setPipeline linePipeline
                                colorPass.setBindGroup(0, cameraBindGroup)
                                colorPass.setBindGroup(1, frameBindGroup)
                                colorPass.setVertexBuffer(0, gridBuf)
                                colorPass.draw (gridData.Length / 6)

                            // Loop fills — semi-transparent triangles inside
                            // closed regions. Drawn under the sketch entity
                            // lines but above the grid.
                            let sketchLoops =
                                viewState.SketchLoops
                                |> List.tryFind (fun l -> l.SketchId = sketch.Id)
                                |> Option.map (fun l -> l.Loops)
                                |> Option.defaultValue []

                            let loopFillData =
                                SketchOverlay.buildSketchLoopFillBuffer
                                    sketch.Id sketch.Sketch sketchLoops
                                    state.Compiled.Slots.Index viewState.Params
                                    viewState.HoveredTarget viewState.SelectedTargets

                            if loopFillData.Length > 0 then
                                let (br, cr) = loopFillSlot
                                let fbuf = upload br cr loopFillData
                                colorPass.setPipeline triPipeline
                                colorPass.setBindGroup(0, cameraBindGroup)
                                colorPass.setBindGroup(1, frameBindGroup)
                                colorPass.setVertexBuffer(0, fbuf)
                                colorPass.draw (loopFillData.Length / 6)

                            // Frame gizmo — X and Y axes at the sketch origin.
                            let gizmoData = SketchOverlay.buildSketchGizmoBuffer ()
                            let (br, cr) = gizmoSlot
                            let gizmoBuf = upload br cr gizmoData
                            colorPass.setPipeline linePipeline
                            colorPass.setBindGroup(0, cameraBindGroup)
                            colorPass.setBindGroup(1, frameBindGroup)
                            colorPass.setVertexBuffer(0, gizmoBuf)
                            colorPass.draw (gizmoData.Length / 6)

                            // Lines / circles / arcs.
                            let lineData =
                                SketchOverlay.buildSketchLineBuffer
                                    sketch.Id sketch.Sketch.Entities
                                    state.Compiled.Slots.Index viewState.Params
                                    viewState.HoveredTarget viewState.SelectedTargets

                            if lineData.Length > 0 then
                                let (br, cr) = sketchLineSlot
                                let lineBuf = upload br cr lineData
                                colorPass.setPipeline linePipeline
                                colorPass.setBindGroup(0, cameraBindGroup)
                                colorPass.setBindGroup(1, frameBindGroup)
                                colorPass.setVertexBuffer(0, lineBuf)
                                colorPass.draw (lineData.Length / 6)

                            // Constraint-visualization lines (dimension
                            // apparatus, Fixed crosshairs, H/V ticks).
                            let showDimensions =
                                List.contains sketch.Id viewState.VisibleDimensionSketchIds
                            let constraintLineData =
                                SketchOverlay.buildSketchConstraintLinesBuffer
                                    sketch.Id sketch.Sketch
                                    state.Compiled.Slots.Index viewState.Params
                                    showDimensions
                                    viewState.HoveredTarget viewState.SelectedTargets

                            if constraintLineData.Length > 0 then
                                let (br, cr) = constraintLineSlot
                                let cbuf = upload br cr constraintLineData
                                colorPass.setPipeline linePipeline
                                colorPass.setBindGroup(0, cameraBindGroup)
                                colorPass.setBindGroup(1, frameBindGroup)
                                colorPass.setVertexBuffer(0, cbuf)
                                colorPass.draw (constraintLineData.Length / 6)

                            // Placement preview: live render the pending
                            // dimension (lines + label) at the cursor.
                            match viewState.SketchUi.PendingConstraintPlacement with
                            | Some pending when pending.SketchId = sketch.Id ->
                                let cursorPos =
                                    match toolCursor with
                                    | Some (sid, u, v) when sid = sketch.Id ->
                                        Some { X = u; Y = v }
                                    | _ -> None
                                match cursorPos with
                                | Some cursor ->
                                    let previewLines =
                                        SketchOverlay.buildPendingConstraintLineBuffer
                                            sketch.Id sketch.Sketch.Entities
                                            state.Compiled.Slots.Index viewState.Params
                                            pending.Constraint cursor
                                    if previewLines.Length > 0 then
                                        let (br, cr) = placementPreviewLineSlot
                                        let pbuf = upload br cr previewLines
                                        colorPass.setPipeline linePipeline
                                        colorPass.setBindGroup(0, cameraBindGroup)
                                        colorPass.setBindGroup(1, frameBindGroup)
                                        colorPass.setVertexBuffer(0, pbuf)
                                        colorPass.draw (previewLines.Length / 6)

                                    let previewPoints =
                                        SketchOverlay.resolvePointMap
                                            state.Compiled.Slots.Index viewState.Params
                                            sketch.Id sketch.Sketch.Entities
                                    let previewRadius =
                                        SketchOverlay.circleRadiusLookup
                                            state.Compiled.Slots.Index viewState.Params
                                            sketch.Id sketch.Sketch.Entities
                                    let previewLabelData =
                                        LabelBuilder.buildSketchLabelBuffer
                                            fontMetrics previewPoints previewRadius
                                            sketch.Id
                                            [ SketchOverlay.withLabelPosition cursor pending.Constraint ]
                                            None []
                                    if previewLabelData.Length > 0 then
                                        let (br, cr) = placementPreviewLabelSlot
                                        let lbuf = upload br cr previewLabelData
                                        colorPass.setPipeline labelPipeline
                                        colorPass.setBindGroup(0, cameraBindGroup)
                                        colorPass.setBindGroup(1, labelBindGroup)
                                        colorPass.setVertexBuffer(0, lbuf)
                                        colorPass.draw (previewLabelData.Length / 10)
                                | None -> ()
                            | _ -> ()

                            // Tool preview (line/circle/rectangle/arc being drawn).
                            let isActiveEditSketch =
                                viewState.SketchUi.EditMode
                                && state.Doc.SelectedId = Some sketch.Id
                            if isActiveEditSketch
                               && viewState.SketchUi.Tool <> ""
                               && viewState.SketchUi.Tool <> "none" then
                                let cursorForSketch =
                                    match toolCursor with
                                    | Some (sid, u, v) when sid = sketch.Id -> Some (u, v)
                                    | _ -> None
                                let toolLineData =
                                    SketchOverlay.buildToolPreviewLineBuffer
                                        viewState.SketchUi.Tool
                                        viewState.SketchUi.ToolPoints
                                        cursorForSketch
                                if toolLineData.Length > 0 then
                                    let (br, cr) = toolPreviewLineSlot
                                    let tlbuf = upload br cr toolLineData
                                    colorPass.setPipeline linePipeline
                                    colorPass.setBindGroup(0, cameraBindGroup)
                                    colorPass.setBindGroup(1, frameBindGroup)
                                    colorPass.setVertexBuffer(0, tlbuf)
                                    colorPass.draw (toolLineData.Length / 6)

                                let toolPointData =
                                    SketchOverlay.buildToolPreviewPointBuffer
                                        viewState.SketchUi.Tool
                                        viewState.SketchUi.ToolPoints
                                        cursorForSketch
                                if toolPointData.Length > 0 then
                                    let (br, cr) = toolPreviewPointSlot
                                    let tpbuf = upload br cr toolPointData
                                    colorPass.setPipeline pointPipeline
                                    colorPass.setBindGroup(0, cameraBindGroup)
                                    colorPass.setBindGroup(1, frameBindGroup)
                                    colorPass.setBindGroup(2, viewportBindGroup)
                                    colorPass.setVertexBuffer(0, pointQuadBuffer)
                                    colorPass.setVertexBuffer(1, tpbuf)
                                    let instanceCount = toolPointData.Length / 7
                                    colorPass.drawInstanced(6, instanceCount)

                            // Points (instanced billboards).
                            let pointData =
                                SketchOverlay.buildSketchPointBuffer
                                    sketch.Id sketch.Sketch.Entities
                                    state.Compiled.Slots.Index viewState.Params
                                    viewState.HoveredTarget viewState.SelectedTargets

                            if pointData.Length > 0 then
                                let (br, cr) = sketchPointSlot
                                let pointBuf = upload br cr pointData
                                colorPass.setPipeline pointPipeline
                                colorPass.setBindGroup(0, cameraBindGroup)
                                colorPass.setBindGroup(1, frameBindGroup)
                                colorPass.setBindGroup(2, viewportBindGroup)
                                colorPass.setVertexBuffer(0, pointQuadBuffer)
                                colorPass.setVertexBuffer(1, pointBuf)
                                let instanceCount = pointData.Length / 7
                                colorPass.drawInstanced(6, instanceCount)

                            // Constraint labels — only when the sketch's
                            // dimensions are toggled on.
                            let points =
                                SketchOverlay.resolvePointMap
                                    state.Compiled.Slots.Index viewState.Params
                                    sketch.Id sketch.Sketch.Entities
                            let radiusLookup =
                                SketchOverlay.circleRadiusLookup
                                    state.Compiled.Slots.Index viewState.Params
                                    sketch.Id sketch.Sketch.Entities
                            let labelData =
                                if showDimensions then
                                    LabelBuilder.buildSketchLabelBuffer
                                        fontMetrics points radiusLookup
                                        sketch.Id sketch.Sketch.Constraints
                                        viewState.HoveredTarget viewState.SelectedTargets
                                else [||]
                            if not labelDebugLogged then
                                labelDebugLogged <- true
                                console.log (
                                    sprintf "sketch %s: %d constraints → %d label vertices"
                                        sketch.Id
                                        sketch.Sketch.Constraints.Length
                                        (labelData.Length / 10))
                                sketch.Sketch.Constraints
                                |> List.iteri (fun i c ->
                                    let kind =
                                        match c with
                                        | Fixed _ -> "Fixed"
                                        | Coincident _ -> "Coincident"
                                        | FrameCoincident _ -> "FrameCoincident"
                                        | Concentric _ -> "Concentric"
                                        | Horizontal _ -> "Horizontal"
                                        | Vertical _ -> "Vertical"
                                        | Distance(_, _, d, lp) -> sprintf "Distance d=%.2f lp=%A" d lp
                                        | FrameDistance(_, _, _, d, lp) -> sprintf "FrameDistance d=%.2f lp=%A" d lp
                                        | Equal _ -> "Equal"
                                        | EqualRadius _ -> "EqualRadius"
                                        | Midpoint _ -> "Midpoint"
                                        | Parallel _ -> "Parallel"
                                        | FrameParallel _ -> "FrameParallel"
                                        | Perpendicular _ -> "Perpendicular"
                                        | FramePerpendicular _ -> "FramePerpendicular"
                                        | Tangent _ -> "Tangent"
                                        | CurveTangent _ -> "CurveTangent"
                                        | CircleDiameter(_, _, d, lp) -> sprintf "CircleDiameter d=%.2f lp=%A" d lp
                                        | LineDistance(_, _, _, _, _, _, d, lp) -> sprintf "LineDistance d=%.2f lp=%A" d lp
                                        | FrameLineDistance(_, _, _, _, _, d, lp) -> sprintf "FrameLineDistance d=%.2f lp=%A" d lp
                                        | PointLineDistance(_, _, _, _, d, lp) -> sprintf "PointLineDistance d=%.2f lp=%A" d lp
                                        | PointCircleDistance(_, _, _, d, lp) -> sprintf "PointCircleDistance d=%.2f lp=%A" d lp
                                        | LineCircleDistance(_, _, _, _, _, d, lp) -> sprintf "LineCircleDistance d=%.2f lp=%A" d lp
                                        | CircleCircleDistance(_, _, _, _, d, _, lp) -> sprintf "CircleCircleDistance d=%.2f lp=%A" d lp
                                        | Angle(_, _, _, _, _, _, a, _, _, _, lp) -> sprintf "Angle a=%.2f lp=%A" a lp
                                    console.log (sprintf "  c[%d] = %s" i kind))

                            if labelData.Length > 0 then
                                let (br, cr) = labelSlot
                                let labelBuf = upload br cr labelData
                                colorPass.setPipeline labelPipeline
                                colorPass.setBindGroup(0, cameraBindGroup)
                                colorPass.setBindGroup(1, labelBindGroup)
                                colorPass.setVertexBuffer(0, labelBuf)
                                colorPass.draw (labelData.Length / 10)
                            // TODO: pool/reuse buffers; allocating per-frame
                            // per-sketch will leak + churn GC. Phase 9 polish.

                    // Frame gizmos — three axis lines per frame, scaled to a
                    // fixed pixel length. One draw covers every frame; no
                    // per-frame uniform writes so the sketch state stays
                    // intact for the rest of the submit.
                    let visibleFrames =
                        viewState.Frames |> List.filter (fun f -> isVisible f.Id)
                    let gizmoData =
                        SketchOverlay.buildFramesGizmoBuffer
                            visibleFrames
                            viewState.HoveredTarget viewState.SelectedTargets
                            state.Doc.SelectedId
                    if gizmoData.Length > 0 then
                        let (br, cr) = frameGizmoSlot
                        let gbuf = upload br cr gizmoData
                        colorPass.setPipeline gizmoPipeline
                        colorPass.setBindGroup(0, cameraBindGroup)
                        colorPass.setBindGroup(1, viewportBindGroup)
                        colorPass.setVertexBuffer(0, gbuf)
                        colorPass.draw (gizmoData.Length / 12)

                    // Origin dot — visible pick handle at each frame's origin.
                    let frameOriginData =
                        SketchOverlay.buildFrameOriginsPointBuffer
                            visibleFrames
                            viewState.HoveredTarget viewState.SelectedTargets
                    if frameOriginData.Length > 0 then
                        let (br, cr) = frameOriginPointSlot
                        let fbuf = upload br cr frameOriginData
                        colorPass.setPipeline worldPointPipeline
                        colorPass.setBindGroup(0, cameraBindGroup)
                        colorPass.setBindGroup(1, viewportBindGroup)
                        colorPass.setVertexBuffer(0, pointQuadBuffer)
                        colorPass.setVertexBuffer(1, fbuf)
                        let instanceCount = frameOriginData.Length / 8
                        colorPass.drawInstanced(6, instanceCount)

                    colorPass.endPass()

                    // Pick pass — writes surface-id+1 into the r32uint pick
                    // texture. Background is 0 → no hit.
                    let pickPass =
                        encoder.beginRenderPass
                            (box
                                {| colorAttachments =
                                    [| {| view = pickView
                                          loadOp = "clear"
                                          storeOp = "store"
                                          clearValue = {| r = 0; g = 0; b = 0; a = 0 |} |} |]
                                   depthStencilAttachment =
                                    {| view = depthView
                                       depthLoadOp = "clear"
                                       depthStoreOp = "store"
                                       depthClearValue = 1.0 |} |})
                    pickPass.setPipeline pickPipeline
                    pickPass.setBindGroup(0, pickBindGroup)
                    match meshBuffer with
                    | Some buf when meshVertexCount > 0 ->
                        pickPass.setVertexBuffer(0, buf)
                        pickPass.draw meshVertexCount
                    | _ -> ()

                    // Sketch entity picks — draw lines/circles/arcs first
                    // (thick quads), then points on top (fat billboards).
                    for sketch in model.Sketches do
                        match Map.tryFind sketch.Id frameById with
                        | None -> ()
                        | Some _ when not (isVisible sketch.Id) -> ()
                        | Some transform ->
                            let pos = transform.Trans
                            let xAxis = transform.Rot.Rotate({ X = 1.0; Y = 0.0; Z = 0.0 })
                            let yAxis = transform.Rot.Rotate({ X = 0.0; Y = 1.0; Z = 0.0 })
                            WebGPU.writeFloat32 device.queue frameBuffer 0
                                [| float32 pos.X;   float32 pos.Y;   float32 pos.Z;   0.0f
                                   float32 xAxis.X; float32 xAxis.Y; float32 xAxis.Z; 0.0f
                                   float32 yAxis.X; float32 yAxis.Y; float32 yAxis.Z; 0.0f
                                   0.0f; 0.0f; 0.0f; 0.0f |]

                            // Loop fills — pickable regions inside closed loops.
                            let sketchPickLoops =
                                viewState.SketchLoops
                                |> List.tryFind (fun l -> l.SketchId = sketch.Id)
                                |> Option.map (fun l -> l.Loops)
                                |> Option.defaultValue []

                            let loopPickData =
                                SketchOverlay.buildSketchLoopPickBuffer
                                    sketch.Id sketch.Sketch sketchPickLoops
                                    state.Compiled.Slots.Index viewState.Params
                                    model.Pickables

                            if loopPickData.Length > 0 then
                                let (br, cr) = loopPickSlot
                                let lpbuf = upload br cr loopPickData
                                pickPass.setPipeline loopPickPipeline
                                pickPass.setBindGroup(0, cameraBindGroup)
                                pickPass.setBindGroup(1, frameBindGroup)
                                pickPass.setVertexBuffer(0, lpbuf)
                                pickPass.draw (loopPickData.Length / 3)

                            let linePickData =
                                SketchOverlay.buildSketchPickLineBuffer
                                    sketch.Id sketch.Sketch.Entities
                                    state.Compiled.Slots.Index viewState.Params
                                    model.Pickables

                            if linePickData.Length > 0 then
                                let (br, cr) = linePickSlot
                                let lbuf = upload br cr linePickData
                                pickPass.setPipeline linePickPipeline
                                pickPass.setBindGroup(0, cameraBindGroup)
                                pickPass.setBindGroup(1, frameBindGroup)
                                pickPass.setVertexBuffer(0, linePickCornerBuffer)
                                pickPass.setVertexBuffer(1, lbuf)
                                let segments = linePickData.Length / 5
                                pickPass.drawInstanced(6, segments)

                            let pointPickData =
                                SketchOverlay.buildSketchPointPickBuffer
                                    sketch.Id sketch.Sketch.Entities
                                    state.Compiled.Slots.Index viewState.Params
                                    model.Pickables

                            if pointPickData.Length > 0 then
                                let (br, cr) = pointPickSlot
                                let pbuf = upload br cr pointPickData
                                pickPass.setPipeline pointPickPipeline
                                pickPass.setBindGroup(0, cameraBindGroup)
                                pickPass.setBindGroup(1, frameBindGroup)
                                pickPass.setBindGroup(2, viewportBindGroup)
                                pickPass.setVertexBuffer(0, pointQuadBuffer)
                                pickPass.setVertexBuffer(1, pbuf)
                                let instanceCount = pointPickData.Length / 4
                                pickPass.drawInstanced(6, instanceCount)

                            // Dimension-label picks — same pipeline as
                            // points, fatter radius, anchors come from
                            // constraint label positions. Skip when the
                            // sketch's dimensions are toggled off.
                            let pickShowDims =
                                List.contains sketch.Id viewState.VisibleDimensionSketchIds
                            let dimPickData =
                                if pickShowDims then
                                    SketchOverlay.buildSketchDimensionPickBuffer
                                        sketch.Id sketch.Sketch
                                        state.Compiled.Slots.Index viewState.Params
                                        model.Pickables
                                else [||]

                            if dimPickData.Length > 0 then
                                let (br, cr) = dimPickSlot
                                let dbuf = upload br cr dimPickData
                                pickPass.setPipeline pointPickPipeline
                                pickPass.setBindGroup(0, cameraBindGroup)
                                pickPass.setBindGroup(1, frameBindGroup)
                                pickPass.setBindGroup(2, viewportBindGroup)
                                pickPass.setVertexBuffer(0, pointQuadBuffer)
                                pickPass.setVertexBuffer(1, dbuf)
                                let instanceCount = dimPickData.Length / 4
                                pickPass.drawInstanced(6, instanceCount)

                    // Frame-origin picks — single draw, world-space instances.
                    let frameOriginPickData =
                        SketchOverlay.buildFrameOriginsPickBuffer
                            visibleFrames model.Pickables
                    if frameOriginPickData.Length > 0 then
                        let (br, cr) = frameOriginPickSlot
                        let fpbuf = upload br cr frameOriginPickData
                        pickPass.setPipeline worldPointPickPipeline
                        pickPass.setBindGroup(0, cameraBindGroup)
                        pickPass.setBindGroup(1, viewportBindGroup)
                        pickPass.setVertexBuffer(0, pointQuadBuffer)
                        pickPass.setVertexBuffer(1, fpbuf)
                        let instanceCount = frameOriginPickData.Length / 5
                        pickPass.drawInstanced(6, instanceCount)

                    // Frame-axis picks — sampled world-space pick points
                    // along each visible axis segment.
                    let frameAxisPickData =
                        SketchOverlay.buildFrameAxesPickBuffer
                            visibleFrames
                            model.Pickables
                            b.Eye
                            b.Forward
                            Camera.HALF_FOV
                            (float h)
                    if frameAxisPickData.Length > 0 then
                        let (br, cr) = frameOriginPickSlot
                        let fabuf = upload br cr frameAxisPickData
                        pickPass.setPipeline worldPointPickPipeline
                        pickPass.setBindGroup(0, cameraBindGroup)
                        pickPass.setBindGroup(1, viewportBindGroup)
                        pickPass.setVertexBuffer(0, pointQuadBuffer)
                        pickPass.setVertexBuffer(1, fabuf)
                        let instanceCount = frameAxisPickData.Length / 5
                        pickPass.drawInstanced(6, instanceCount)

                    pickPass.endPass()

                    device.queue.submit [| encoder.finish() |]
                    submittedFrameCount <- submittedFrameCount + 1
                    flushRetiredBuffers ()

                    WebGPU.requestAnimationFrame frame |> ignore

                WebGPU.requestAnimationFrame frame |> ignore

                // ── Dimension editor overlay ──────────────────────────
                // Floating <input> positioned over the label of the
                // constraint currently carried by viewState.SketchUi.EditingDimension.
                let dimensionInput : HTMLInputElement =
                    unbox (document.createElement "input")
                dimensionInput?``type`` <- "number"
                dimensionInput?step <- "any"
                dimensionInput?style?position <- "absolute"
                dimensionInput?style?display <- "none"
                dimensionInput?style?transform <- "translate(-50%, -50%)"
                dimensionInput?style?padding <- "2px 6px"
                dimensionInput?style?border <- "1px solid #b48b2b"
                dimensionInput?style?borderRadius <- "3px"
                dimensionInput?style?background <- "#fff8e4"
                dimensionInput?style?fontFamily <- "ui-monospace, monospace"
                dimensionInput?style?fontSize <- "12px"
                dimensionInput?style?width <- "72px"
                dimensionInput?style?textAlign <- "center"
                dimensionInput?style?outline <- "none"
                dimensionInput?style?zIndex <- "10"
                container.appendChild dimensionInput |> ignore

                let mutable dimensionClosing = false
                let mutable dimensionEditingKey : string = ""

                // Stop clicks inside the input from bubbling to the canvas
                // pick/drag handlers.
                addEvent dimensionInput "mousedown" (fun e -> e?stopPropagation() |> ignore)
                addEvent dimensionInput "dblclick" (fun e -> e?stopPropagation() |> ignore)

                addEvent dimensionInput "keydown" (fun e ->
                    e?stopPropagation() |> ignore
                    let key : string = e?key
                    match key with
                    | "Enter" ->
                        ePreventDefault e
                        dimensionClosing <- true
                        let raw : string = dimensionInput?value
                        let mutable parsed = 0.0
                        if System.Double.TryParse(raw, &parsed) then
                            Store.dispatch AppStore.store (CommitEditingDimension parsed)
                        else
                            Store.dispatch AppStore.store CancelEditingDimension
                    | "Escape" ->
                        ePreventDefault e
                        dimensionClosing <- true
                        Store.dispatch AppStore.store CancelEditingDimension
                    | _ -> ())

                addEvent dimensionInput "blur" (fun _ ->
                    // Don't dispatch during blur — that triggers a render
                    // which may remove the input from the DOM while the
                    // browser is mid-focus-change. Just refocus on the
                    // next frame, like the old TS viewer did.
                    if not dimensionClosing then
                        WebGPU.requestAnimationFrame (fun _ ->
                            let state = AppStore.store.State
                            let vs = ViewerPipeline.viewerState state
                            if vs.SketchUi.EditingDimension.IsSome then
                                dimensionInput?focus() |> ignore
                                dimensionInput?select() |> ignore)
                        |> ignore)

                let dimensionAnchorForSketch
                        (state: EditorState)
                        (sketchId: ActionId)
                        (sketch: ActionSketch)
                        (constraintIndex: int) : LabelPos option =
                    if constraintIndex < 0
                       || constraintIndex >= sketch.Constraints.Length then
                        None
                    else
                        let c = sketch.Constraints.[constraintIndex]
                        match SketchConstraint.labelPos c with
                        | Some lp -> Some lp
                        | None ->
                            let vs = ViewerPipeline.viewerState state
                            let points =
                                SketchOverlay.resolvePointMap
                                    state.Compiled.Slots.Index vs.Params
                                    sketchId sketch.Entities
                            let radiusOf =
                                SketchOverlay.circleRadiusLookup
                                    state.Compiled.Slots.Index vs.Params
                                    sketchId sketch.Entities
                            SketchOverlay.dimensionFallbackAnchor points radiusOf c

                let syncDimensionEditor () =
                    let state = AppStore.store.State
                    let vs = ViewerPipeline.viewerState state
                    match vs.SketchUi.EditingDimension with
                    | None ->
                        dimensionInput?style?display <- "none"
                        dimensionEditingKey <- ""
                        dimensionClosing <- false
                    | Some editing ->
                        let sketchActionOpt =
                            state.Doc.Actions
                            |> List.tryFind (fun a -> a.Id = editing.SketchId)
                        match sketchActionOpt with
                        | Some { Kind = Sketch(_, _, sketch) } ->
                            match dimensionAnchorForSketch state editing.SketchId sketch editing.ConstraintIndex with
                            | Some anchor ->
                                let sketchFrame =
                                    vs.SketchTransforms
                                    |> List.tryFind (fun f -> f.Id = editing.SketchId)
                                match sketchFrame with
                                | Some frameView ->
                                    let pos = frameView.Transform.Trans
                                    let xAxis = frameView.Transform.Rot.Rotate({ X = 1.0; Y = 0.0; Z = 0.0 })
                                    let yAxis = frameView.Transform.Rot.Rotate({ X = 0.0; Y = 1.0; Z = 0.0 })
                                    let world = pos + anchor.X * xAxis + anchor.Y * yAxis
                                    let w = canvas.clientWidth
                                    let h = canvas.clientHeight
                                    match Camera.worldToScreen w h camera world with
                                    | Some (sx, sy) ->
                                        let key =
                                            sprintf "%s:%d" editing.SketchId editing.ConstraintIndex
                                        if dimensionEditingKey <> key then
                                            dimensionEditingKey <- key
                                            dimensionClosing <- false
                                            dimensionInput?value <- string editing.Value
                                            setTimeout
                                                (fun () ->
                                                    dimensionInput?focus() |> ignore
                                                    dimensionInput?select() |> ignore) 0
                                            |> ignore
                                        dimensionInput?style?display <- ""
                                        dimensionInput?style?left <- sprintf "%fpx" sx
                                        dimensionInput?style?top <- sprintf "%fpx" sy
                                    | None ->
                                        dimensionInput?style?display <- "none"
                                | None ->
                                    dimensionInput?style?display <- "none"
                            | None ->
                                dimensionInput?style?display <- "none"
                        | _ ->
                            dimensionInput?style?display <- "none"

                Store.subscribe AppStore.store syncDimensionEditor
                // Also reposition every frame (camera moves, sketch drags).
                let rec positionFrame (_: float) =
                    syncDimensionEditor ()
                    WebGPU.requestAnimationFrame positionFrame |> ignore
                WebGPU.requestAnimationFrame positionFrame |> ignore

                return box container
    }
