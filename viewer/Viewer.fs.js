import { PromiseBuilder__Delay_62FBFDE1, PromiseBuilder__Run_212F1D4B } from "../ui/fable_modules/Fable.Promise.3.2.0/Promise.fs.js";
import { defaultArg, filter, map as map_1, toArray, some } from "../ui/fable_modules/fable-library-js.4.29.0/Option.js";
import { PAGE_BG } from "./Colors.fs.js";
import { GPUMapMode_Read, GPUBufferUsage_Vertex, GPUPipelineLayoutDescriptor, GPUShaderModuleDescriptor, GPUBindGroupDescriptor, GPUBindGroupEntry, GPUBufferBinding, GPUBindGroupLayoutDescriptor, GPUShaderStage_Fragment, GPUShaderStage_Vertex, GPUBufferUsage_Uniform, GPUBufferDescriptor, GPUBufferUsage_MapRead, GPUBufferUsage_CopyDst, GPUTextureUsage_CopySrc, GPUTextureDescriptor, GPUTextureUsage_RenderAttachment, GPUExtent3D, GPUCanvasConfiguration, gpu } from "./WebGPU.fs.js";
import { promise } from "../ui/fable_modules/Fable.Promise.3.2.0/PromiseImpl.fs.js";
import { loadMetrics, loadAtlas } from "./MsdfAtlas.fs.js";
import { tryFind as tryFind_1, ofList, empty, FSharpMap__get_Count } from "../ui/fable_modules/fable-library-js.4.29.0/Map.js";
import { printf, toText } from "../ui/fable_modules/fable-library-js.4.29.0/String.js";
import { stringHash, disposeSafe, getEnumerator, equals, comparePrimitives, defaultOf } from "../ui/fable_modules/fable-library-js.4.29.0/Util.js";
import { FSharpRef } from "../ui/fable_modules/fable-library-js.4.29.0/Types.js";
import { iterate } from "../ui/fable_modules/fable-library-js.4.29.0/Seq.js";
import { tryParse, max } from "../ui/fable_modules/fable-library-js.4.29.0/Double.js";
import { worldToScreen, HALF_FOV, basis, zoomTowardsPointer, orbit, pan, screenToRay, rayPlaneIntersection, create } from "./Camera.fs.js";
import { store } from "../ui/AppStore.fs.js";
import { ViewerPipeline_viewerState, ViewerPipeline_viewerModel } from "../core/Editor/ViewerPipeline.fs.js";
import { item, filter as filter_1, iterateIndexed, length, contains, exists, empty as empty_1, singleton, tryFind, map } from "../ui/fable_modules/fable-library-js.4.29.0/List.js";
import { PickableModule_pickId } from "../core/Editor/Pickable.fs.js";
import { dispatch, subscribe } from "../ui/Store.fs.js";
import { Quat__Rotate_Z2E054BF3 } from "../core/Math/Quat.fs.js";
import { Vec3_op_Multiply_ZB3DA56A, Vec3_op_Addition_Z3F547E60, Vec3 } from "../core/Math/Vec.fs.js";
import { SketchDrag, SketchDragKind, Message, PickCandidateInput } from "../core/Editor/Editor.fs.js";
import { SketchConstraintModule_labelPos, LabelPos } from "../core/Sketch/Sketch.fs.js";
import { SketchConstraintField, ActionParamField, SketchEntityField } from "../core/Editor/Domain.fs.js";
import { dimensionFallbackAnchor, buildFrameAxesPickBuffer, buildFrameOriginsPickBuffer, buildSketchDimensionPickBuffer, buildSketchPointPickBuffer, buildSketchPickLineBuffer, buildSketchLoopPickBuffer, buildFrameOriginsPointBuffer, buildFramesGizmoBuffer, buildSketchPointBuffer, buildToolPreviewPointBuffer, buildToolPreviewLineBuffer, withLabelPosition, circleRadiusLookup, resolvePointMap, buildPendingConstraintLineBuffer, buildSketchConstraintLinesBuffer, buildSketchLineBuffer, buildSketchGizmoBuffer, buildSketchLoopFillBuffer, buildSketchGridBuffer } from "./SketchOverlayRender.fs.js";
import { buildSketchLabelBuffer } from "./LabelBuilder.fs.js";
import { setItem } from "../ui/fable_modules/fable-library-js.4.29.0/Array.js";

const lineWgsl = "\nstruct Camera {\n    eye: vec3<f32>, _p0: f32,\n    forward: vec3<f32>, _p1: f32,\n    right: vec3<f32>, _p2: f32,\n    up: vec3<f32>, aspect: f32,\n};\n\nstruct SketchFrame {\n    pos: vec4<f32>,\n    x_axis: vec4<f32>,\n    y_axis: vec4<f32>,\n};\n\n@group(0) @binding(0) var<uniform> cam: Camera;\n@group(1) @binding(0) var<uniform> frame: SketchFrame;\n\nstruct VsIn {\n    @location(0) position_2d: vec2<f32>,\n    @location(1) color: vec4<f32>,\n};\n\nstruct VsOut {\n    @builtin(position) clip_pos: vec4<f32>,\n    @location(0) color: vec4<f32>,\n};\n\nfn project_world(pos: vec3<f32>) -> vec4<f32> {\n    let f = cam.forward;\n    let r = cam.right;\n    let u = cam.up;\n    let view = mat4x4<f32>(\n        vec4<f32>(r.x, u.x, -f.x, 0.0),\n        vec4<f32>(r.y, u.y, -f.y, 0.0),\n        vec4<f32>(r.z, u.z, -f.z, 0.0),\n        vec4<f32>(-dot(r, cam.eye), -dot(u, cam.eye), dot(f, cam.eye), 1.0),\n    );\n    let near = 0.001;\n    let far = 1000.0;\n    let t = tan(0.3927);\n    let proj = mat4x4<f32>(\n        vec4<f32>(1.0 / (cam.aspect * t), 0.0, 0.0, 0.0),\n        vec4<f32>(0.0, 1.0 / t, 0.0, 0.0),\n        vec4<f32>(0.0, 0.0, -(far + near) / (far - near), -1.0),\n        vec4<f32>(0.0, 0.0, -2.0 * far * near / (far - near), 0.0),\n    );\n    return proj * view * vec4<f32>(pos, 1.0);\n}\n\n@vertex\nfn vs(input: VsIn) -> VsOut {\n    let world = frame.pos.xyz\n        + input.position_2d.x * frame.x_axis.xyz\n        + input.position_2d.y * frame.y_axis.xyz;\n    var out: VsOut;\n    out.clip_pos = project_world(world);\n    out.color = input.color;\n    return out;\n}\n\n@fragment\nfn fs(input: VsOut) -> @location(0) vec4<f32> {\n    return input.color;\n}\n";

const pointWgsl = "\nstruct Camera {\n    eye: vec3<f32>, _p0: f32,\n    forward: vec3<f32>, _p1: f32,\n    right: vec3<f32>, _p2: f32,\n    up: vec3<f32>, aspect: f32,\n};\n\nstruct SketchFrame {\n    pos: vec4<f32>,\n    x_axis: vec4<f32>,\n    y_axis: vec4<f32>,\n};\n\nstruct Viewport {\n    size: vec2<f32>,\n    _pad: vec2<f32>,\n};\n\n@group(0) @binding(0) var<uniform> cam: Camera;\n@group(1) @binding(0) var<uniform> frame: SketchFrame;\n@group(2) @binding(0) var<uniform> viewport: Viewport;\n\nstruct QuadIn {\n    @location(0) corner: vec2<f32>,\n};\n\nstruct InstanceIn {\n    @location(1) center_2d: vec2<f32>,\n    @location(2) radius_px: f32,\n    @location(3) color: vec4<f32>,\n};\n\nstruct VsOut {\n    @builtin(position) clip_pos: vec4<f32>,\n    @location(0) color: vec4<f32>,\n    @location(1) local_pos: vec2<f32>,\n};\n\nfn project_world(pos: vec3<f32>) -> vec4<f32> {\n    let f = cam.forward;\n    let r = cam.right;\n    let u = cam.up;\n    let view = mat4x4<f32>(\n        vec4<f32>(r.x, u.x, -f.x, 0.0),\n        vec4<f32>(r.y, u.y, -f.y, 0.0),\n        vec4<f32>(r.z, u.z, -f.z, 0.0),\n        vec4<f32>(-dot(r, cam.eye), -dot(u, cam.eye), dot(f, cam.eye), 1.0),\n    );\n    let near = 0.001;\n    let far = 1000.0;\n    let t = tan(0.3927);\n    let proj = mat4x4<f32>(\n        vec4<f32>(1.0 / (cam.aspect * t), 0.0, 0.0, 0.0),\n        vec4<f32>(0.0, 1.0 / t, 0.0, 0.0),\n        vec4<f32>(0.0, 0.0, -(far + near) / (far - near), -1.0),\n        vec4<f32>(0.0, 0.0, -2.0 * far * near / (far - near), 0.0),\n    );\n    return proj * view * vec4<f32>(pos, 1.0);\n}\n\n@vertex\nfn vs(quad: QuadIn, instance: InstanceIn) -> VsOut {\n    let world = frame.pos.xyz\n        + instance.center_2d.x * frame.x_axis.xyz\n        + instance.center_2d.y * frame.y_axis.xyz;\n    let center_clip = project_world(world);\n    let size = max(viewport.size, vec2<f32>(1.0, 1.0));\n    let offset_ndc = vec2<f32>(\n        quad.corner.x * instance.radius_px * 2.0 / size.x,\n        quad.corner.y * instance.radius_px * 2.0 / size.y\n    );\n    var out: VsOut;\n    out.clip_pos = vec4<f32>(\n        center_clip.xy + offset_ndc * center_clip.w,\n        center_clip.z,\n        center_clip.w);\n    out.color = instance.color;\n    out.local_pos = quad.corner;\n    return out;\n}\n\n@fragment\nfn fs(in: VsOut) -> @location(0) vec4<f32> {\n    if (dot(in.local_pos, in.local_pos) > 1.0) { discard; }\n    return in.color;\n}\n";

const gizmoWgsl = "\nstruct Camera {\n    eye: vec3<f32>, _p0: f32,\n    forward: vec3<f32>, _p1: f32,\n    right: vec3<f32>, _p2: f32,\n    up: vec3<f32>, aspect: f32,\n};\n\nstruct Viewport {\n    size: vec2<f32>,\n    _pad: vec2<f32>,\n};\n\n@group(0) @binding(0) var<uniform> cam: Camera;\n@group(1) @binding(0) var<uniform> viewport: Viewport;\n\nstruct VsIn {\n    @location(0) origin: vec3<f32>,\n    @location(1) axis: vec3<f32>,\n    @location(2) axis_px: f32,\n    @location(3) endpoint: f32,\n    @location(4) color: vec4<f32>,\n};\n\nstruct VsOut {\n    @builtin(position) clip_pos: vec4<f32>,\n    @location(0) color: vec4<f32>,\n};\n\nfn project_world(pos: vec3<f32>) -> vec4<f32> {\n    let f = cam.forward;\n    let r = cam.right;\n    let u = cam.up;\n    let view = mat4x4<f32>(\n        vec4<f32>(r.x, u.x, -f.x, 0.0),\n        vec4<f32>(r.y, u.y, -f.y, 0.0),\n        vec4<f32>(r.z, u.z, -f.z, 0.0),\n        vec4<f32>(-dot(r, cam.eye), -dot(u, cam.eye), dot(f, cam.eye), 1.0),\n    );\n    let near = 0.001;\n    let far = 1000.0;\n    let t = tan(0.3927);\n    let proj = mat4x4<f32>(\n        vec4<f32>(1.0 / (cam.aspect * t), 0.0, 0.0, 0.0),\n        vec4<f32>(0.0, 1.0 / t, 0.0, 0.0),\n        vec4<f32>(0.0, 0.0, -(far + near) / (far - near), -1.0),\n        vec4<f32>(0.0, 0.0, -2.0 * far * near / (far - near), 0.0),\n    );\n    return proj * view * vec4<f32>(pos, 1.0);\n}\n\n@vertex\nfn vs(input: VsIn) -> VsOut {\n    let depth = max(abs(dot(input.origin - cam.eye, cam.forward)), 1e-3);\n    let world_per_px = (2.0 * depth * tan(0.3927)) / max(viewport.size.y, 1.0);\n    let world = input.origin + input.axis * (input.axis_px * world_per_px * input.endpoint);\n    var out: VsOut;\n    out.clip_pos = project_world(world);\n    out.color = input.color;\n    return out;\n}\n\n@fragment\nfn fs(input: VsOut) -> @location(0) vec4<f32> {\n    return input.color;\n}\n";

const worldPointWgsl = "\nstruct Camera {\n    eye: vec3<f32>, _p0: f32,\n    forward: vec3<f32>, _p1: f32,\n    right: vec3<f32>, _p2: f32,\n    up: vec3<f32>, aspect: f32,\n};\n\nstruct Viewport {\n    size: vec2<f32>,\n    _pad: vec2<f32>,\n};\n\n@group(0) @binding(0) var<uniform> cam: Camera;\n@group(1) @binding(0) var<uniform> viewport: Viewport;\n\nstruct QuadIn {\n    @location(0) corner: vec2<f32>,\n};\n\nstruct InstanceIn {\n    @location(1) center_world: vec3<f32>,\n    @location(2) radius_px: f32,\n    @location(3) color: vec4<f32>,\n};\n\nstruct VsOut {\n    @builtin(position) clip_pos: vec4<f32>,\n    @location(0) color: vec4<f32>,\n    @location(1) local_pos: vec2<f32>,\n};\n\nfn project_world(pos: vec3<f32>) -> vec4<f32> {\n    let f = cam.forward;\n    let r = cam.right;\n    let u = cam.up;\n    let view = mat4x4<f32>(\n        vec4<f32>(r.x, u.x, -f.x, 0.0),\n        vec4<f32>(r.y, u.y, -f.y, 0.0),\n        vec4<f32>(r.z, u.z, -f.z, 0.0),\n        vec4<f32>(-dot(r, cam.eye), -dot(u, cam.eye), dot(f, cam.eye), 1.0),\n    );\n    let near = 0.001;\n    let far = 1000.0;\n    let t = tan(0.3927);\n    let proj = mat4x4<f32>(\n        vec4<f32>(1.0 / (cam.aspect * t), 0.0, 0.0, 0.0),\n        vec4<f32>(0.0, 1.0 / t, 0.0, 0.0),\n        vec4<f32>(0.0, 0.0, -(far + near) / (far - near), -1.0),\n        vec4<f32>(0.0, 0.0, -2.0 * far * near / (far - near), 0.0),\n    );\n    return proj * view * vec4<f32>(pos, 1.0);\n}\n\n@vertex\nfn vs(quad: QuadIn, instance: InstanceIn) -> VsOut {\n    let center_clip = project_world(instance.center_world);\n    let size = max(viewport.size, vec2<f32>(1.0, 1.0));\n    let offset_ndc = vec2<f32>(\n        quad.corner.x * instance.radius_px * 2.0 / size.x,\n        quad.corner.y * instance.radius_px * 2.0 / size.y\n    );\n    var out: VsOut;\n    out.clip_pos = vec4<f32>(\n        center_clip.xy + offset_ndc * center_clip.w,\n        center_clip.z,\n        center_clip.w);\n    out.color = instance.color;\n    out.local_pos = quad.corner;\n    return out;\n}\n\n@fragment\nfn fs(in: VsOut) -> @location(0) vec4<f32> {\n    if (dot(in.local_pos, in.local_pos) > 1.0) { discard; }\n    return in.color;\n}\n";

const worldPointPickWgsl = "\nstruct Camera {\n    eye: vec3<f32>, _p0: f32,\n    forward: vec3<f32>, _p1: f32,\n    right: vec3<f32>, _p2: f32,\n    up: vec3<f32>, aspect: f32,\n};\n\nstruct Viewport {\n    size: vec2<f32>,\n    _pad: vec2<f32>,\n};\n\n@group(0) @binding(0) var<uniform> cam: Camera;\n@group(1) @binding(0) var<uniform> viewport: Viewport;\n\nstruct VsOut {\n    @builtin(position) clip_pos: vec4<f32>,\n    @location(0) pick_id: f32,\n    @location(1) local_pos: vec2<f32>,\n};\n\nfn project_world(pos: vec3<f32>) -> vec4<f32> {\n    let f = cam.forward;\n    let r = cam.right;\n    let u = cam.up;\n    let view = mat4x4<f32>(\n        vec4<f32>(r.x, u.x, -f.x, 0.0),\n        vec4<f32>(r.y, u.y, -f.y, 0.0),\n        vec4<f32>(r.z, u.z, -f.z, 0.0),\n        vec4<f32>(-dot(r, cam.eye), -dot(u, cam.eye), dot(f, cam.eye), 1.0),\n    );\n    let near = 0.001;\n    let far = 1000.0;\n    let t = tan(0.3927);\n    let proj = mat4x4<f32>(\n        vec4<f32>(1.0 / (cam.aspect * t), 0.0, 0.0, 0.0),\n        vec4<f32>(0.0, 1.0 / t, 0.0, 0.0),\n        vec4<f32>(0.0, 0.0, -(far + near) / (far - near), -1.0),\n        vec4<f32>(0.0, 0.0, -2.0 * far * near / (far - near), 0.0),\n    );\n    return proj * view * vec4<f32>(pos, 1.0);\n}\n\n@vertex\nfn vs(\n    @location(0) corner: vec2<f32>,\n    @location(1) center_world: vec3<f32>,\n    @location(2) radius_px: f32,\n    @location(3) pick_id: f32\n) -> VsOut {\n    let center_clip = project_world(center_world);\n    let size = max(viewport.size, vec2<f32>(1.0, 1.0));\n    let offset_ndc = vec2<f32>(\n        corner.x * radius_px * 2.0 / size.x,\n        corner.y * radius_px * 2.0 / size.y\n    );\n    var out: VsOut;\n    out.clip_pos = vec4<f32>(\n        center_clip.xy + offset_ndc * center_clip.w,\n        center_clip.z,\n        center_clip.w);\n    out.pick_id = pick_id;\n    out.local_pos = corner;\n    return out;\n}\n\n@fragment\nfn fs(in: VsOut) -> @location(0) u32 {\n    if (dot(in.local_pos, in.local_pos) > 1.0) { discard; }\n    return u32(in.pick_id) + 1u;\n}\n";

const labelWgsl = "\nstruct Camera {\n    eye: vec3<f32>, _p0: f32,\n    forward: vec3<f32>, _p1: f32,\n    right: vec3<f32>, _p2: f32,\n    up: vec3<f32>, aspect: f32,\n};\n\nstruct LabelUniforms {\n    viewport: vec4<f32>,\n    frame_pos: vec4<f32>,\n    frame_x: vec4<f32>,\n    frame_y: vec4<f32>,\n};\n\nconst ATLAS_PX_RANGE: f32 = 4.0;\nconst ATLAS_SIZE: f32 = 256.0;\nconst HALF_FOV: f32 = 0.3927;\n\n@group(0) @binding(0) var<uniform> cam: Camera;\n@group(1) @binding(0) var<uniform> label: LabelUniforms;\n@group(1) @binding(1) var atlas: texture_2d<f32>;\n@group(1) @binding(2) var atlas_sampler: sampler;\n\nstruct VsIn {\n    @location(0) anchor_2d: vec2<f32>,\n    @location(1) offset_px: vec2<f32>,\n    @location(2) uv: vec2<f32>,\n    @location(3) color: vec4<f32>,\n};\n\nstruct VsOut {\n    @builtin(position) clip_pos: vec4<f32>,\n    @location(0) uv: vec2<f32>,\n    @location(1) color: vec4<f32>,\n};\n\nfn project_world(pos: vec3<f32>) -> vec4<f32> {\n    let f = cam.forward;\n    let r = cam.right;\n    let u = cam.up;\n    let view = mat4x4<f32>(\n        vec4<f32>(r.x, u.x, -f.x, 0.0),\n        vec4<f32>(r.y, u.y, -f.y, 0.0),\n        vec4<f32>(r.z, u.z, -f.z, 0.0),\n        vec4<f32>(-dot(r, cam.eye), -dot(u, cam.eye), dot(f, cam.eye), 1.0),\n    );\n    let near = 0.001;\n    let far = 1000.0;\n    let t = tan(HALF_FOV);\n    let proj = mat4x4<f32>(\n        vec4<f32>(1.0 / (cam.aspect * t), 0.0, 0.0, 0.0),\n        vec4<f32>(0.0, 1.0 / t, 0.0, 0.0),\n        vec4<f32>(0.0, 0.0, -(far + near) / (far - near), -1.0),\n        vec4<f32>(0.0, 0.0, -2.0 * far * near / (far - near), 0.0),\n    );\n    return proj * view * vec4<f32>(pos, 1.0);\n}\n\n@vertex\nfn vs(input: VsIn) -> VsOut {\n    let frame_pos = label.frame_pos.xyz;\n    let frame_x = label.frame_x.xyz;\n    let frame_y = label.frame_y.xyz;\n\n    let anchor_world = frame_pos + input.anchor_2d.x * frame_x + input.anchor_2d.y * frame_y;\n    let cam_to_anchor = anchor_world - cam.eye;\n    let view_depth = abs(dot(cam_to_anchor, cam.forward));\n    let world_per_px = 2.0 * view_depth * tan(HALF_FOV) / label.viewport.y;\n\n    let proj_fx = frame_x - dot(frame_x, cam.forward) * cam.forward;\n    let proj_fy = frame_y - dot(frame_y, cam.forward) * cam.forward;\n    let x_sign = select(-1.0, 1.0, dot(proj_fx, cam.right) > 0.0);\n    let y_sign = select(-1.0, 1.0, dot(proj_fy, cam.up) > 0.0);\n    let plane_offset_2d = vec2<f32>(\n        input.offset_px.x * x_sign,\n        -input.offset_px.y * y_sign\n    ) * world_per_px;\n    let plane_offset_world = plane_offset_2d.x * frame_x + plane_offset_2d.y * frame_y;\n\n    var out: VsOut;\n    out.clip_pos = project_world(anchor_world + plane_offset_world);\n    out.uv = input.uv;\n    out.color = input.color;\n    return out;\n}\n\nfn median3(r: f32, g: f32, b: f32) -> f32 {\n    return max(min(r, g), min(max(r, g), b));\n}\n\n@fragment\nfn fs(input: VsOut) -> @location(0) vec4<f32> {\n    let msd = textureSample(atlas, atlas_sampler, input.uv).rgb;\n    let sd = median3(msd.r, msd.g, msd.b);\n    let unit_range = ATLAS_PX_RANGE / ATLAS_SIZE;\n    let uv_deriv = fwidth(input.uv);\n    let screen_px_range = max(0.5 * (unit_range / uv_deriv.x + unit_range / uv_deriv.y), 1.0);\n    let screen_px_distance = screen_px_range * (sd - 0.5);\n    let alpha = clamp(screen_px_distance + 0.5, 0.0, 1.0);\n    return vec4<f32>(input.color.rgb, input.color.a * alpha);\n}\n";

const loopPickWgsl = "\nstruct Camera {\n    eye: vec3<f32>, _p0: f32,\n    forward: vec3<f32>, _p1: f32,\n    right: vec3<f32>, _p2: f32,\n    up: vec3<f32>, aspect: f32,\n};\n\nstruct SketchFrame {\n    pos: vec4<f32>,\n    x_axis: vec4<f32>,\n    y_axis: vec4<f32>,\n};\n\n@group(0) @binding(0) var<uniform> cam: Camera;\n@group(1) @binding(0) var<uniform> frame: SketchFrame;\n\nstruct VsOut {\n    @builtin(position) clip_pos: vec4<f32>,\n    @location(0) pick_id: f32,\n};\n\nfn project_world(pos: vec3<f32>) -> vec4<f32> {\n    let f = cam.forward;\n    let r = cam.right;\n    let u = cam.up;\n    let view = mat4x4<f32>(\n        vec4<f32>(r.x, u.x, -f.x, 0.0),\n        vec4<f32>(r.y, u.y, -f.y, 0.0),\n        vec4<f32>(r.z, u.z, -f.z, 0.0),\n        vec4<f32>(-dot(r, cam.eye), -dot(u, cam.eye), dot(f, cam.eye), 1.0),\n    );\n    let near = 0.001;\n    let far = 1000.0;\n    let t = tan(0.3927);\n    let proj = mat4x4<f32>(\n        vec4<f32>(1.0 / (cam.aspect * t), 0.0, 0.0, 0.0),\n        vec4<f32>(0.0, 1.0 / t, 0.0, 0.0),\n        vec4<f32>(0.0, 0.0, -(far + near) / (far - near), -1.0),\n        vec4<f32>(0.0, 0.0, -2.0 * far * near / (far - near), 0.0),\n    );\n    return proj * view * vec4<f32>(pos, 1.0);\n}\n\n@vertex\nfn vs(@location(0) pos_2d: vec2<f32>, @location(1) pick_id: f32) -> VsOut {\n    let world = frame.pos.xyz + pos_2d.x * frame.x_axis.xyz + pos_2d.y * frame.y_axis.xyz;\n    var out: VsOut;\n    out.clip_pos = project_world(world);\n    out.pick_id = pick_id;\n    return out;\n}\n\n@fragment\nfn fs(in: VsOut) -> @location(0) u32 {\n    return u32(in.pick_id) + 1u;\n}\n";

const linePickWgsl = "\nstruct Camera {\n    eye: vec3<f32>, _p0: f32,\n    forward: vec3<f32>, _p1: f32,\n    right: vec3<f32>, _p2: f32,\n    up: vec3<f32>, aspect: f32,\n};\n\nstruct SketchFrame {\n    pos: vec4<f32>,\n    x_axis: vec4<f32>,\n    y_axis: vec4<f32>,\n};\n\n@group(0) @binding(0) var<uniform> cam: Camera;\n@group(1) @binding(0) var<uniform> frame: SketchFrame;\n\nstruct VsOut {\n    @builtin(position) clip_pos: vec4<f32>,\n    @location(0) pick_id: f32,\n};\n\nfn project_world(pos: vec3<f32>) -> vec4<f32> {\n    let f = cam.forward;\n    let r = cam.right;\n    let u = cam.up;\n    let view = mat4x4<f32>(\n        vec4<f32>(r.x, u.x, -f.x, 0.0),\n        vec4<f32>(r.y, u.y, -f.y, 0.0),\n        vec4<f32>(r.z, u.z, -f.z, 0.0),\n        vec4<f32>(-dot(r, cam.eye), -dot(u, cam.eye), dot(f, cam.eye), 1.0),\n    );\n    let near = 0.001;\n    let far = 1000.0;\n    let t = tan(0.3927);\n    let proj = mat4x4<f32>(\n        vec4<f32>(1.0 / (cam.aspect * t), 0.0, 0.0, 0.0),\n        vec4<f32>(0.0, 1.0 / t, 0.0, 0.0),\n        vec4<f32>(0.0, 0.0, -(far + near) / (far - near), -1.0),\n        vec4<f32>(0.0, 0.0, -2.0 * far * near / (far - near), 0.0),\n    );\n    return proj * view * vec4<f32>(pos, 1.0);\n}\n\nconst THICKNESS: f32 = 0.15;\n\n@vertex\nfn vs(\n    @location(0) corner: vec2<f32>,   // (t, s) where t ∈ {0,1}, s ∈ {-1,+1}\n    @location(1) a_2d: vec2<f32>,\n    @location(2) b_2d: vec2<f32>,\n    @location(3) pick_id: f32,\n) -> VsOut {\n    let t = corner.x;\n    let s = corner.y;\n    let p = mix(a_2d, b_2d, t);\n    var dir = b_2d - a_2d;\n    let len = length(dir);\n    if (len > 1e-9) {\n        dir = dir / len;\n    } else {\n        dir = vec2<f32>(1.0, 0.0);\n    }\n    let perp = vec2<f32>(-dir.y, dir.x);\n    let offset = perp * s * THICKNESS;\n    let p2 = p + offset;\n    let world = frame.pos.xyz + p2.x * frame.x_axis.xyz + p2.y * frame.y_axis.xyz;\n    var out: VsOut;\n    out.clip_pos = project_world(world);\n    out.pick_id = pick_id;\n    return out;\n}\n\n@fragment\nfn fs(in: VsOut) -> @location(0) u32 {\n    return u32(in.pick_id) + 1u;\n}\n";

const pointPickWgsl = "\nstruct Camera {\n    eye: vec3<f32>, _p0: f32,\n    forward: vec3<f32>, _p1: f32,\n    right: vec3<f32>, _p2: f32,\n    up: vec3<f32>, aspect: f32,\n};\n\nstruct SketchFrame {\n    pos: vec4<f32>,\n    x_axis: vec4<f32>,\n    y_axis: vec4<f32>,\n};\n\nstruct Viewport {\n    size: vec2<f32>,\n    _pad: vec2<f32>,\n};\n\n@group(0) @binding(0) var<uniform> cam: Camera;\n@group(1) @binding(0) var<uniform> frame: SketchFrame;\n@group(2) @binding(0) var<uniform> viewport: Viewport;\n\nstruct VsOut {\n    @builtin(position) clip_pos: vec4<f32>,\n    @location(0) pick_id: f32,\n    @location(1) local_pos: vec2<f32>,\n};\n\nfn project_world(pos: vec3<f32>) -> vec4<f32> {\n    let f = cam.forward;\n    let r = cam.right;\n    let u = cam.up;\n    let view = mat4x4<f32>(\n        vec4<f32>(r.x, u.x, -f.x, 0.0),\n        vec4<f32>(r.y, u.y, -f.y, 0.0),\n        vec4<f32>(r.z, u.z, -f.z, 0.0),\n        vec4<f32>(-dot(r, cam.eye), -dot(u, cam.eye), dot(f, cam.eye), 1.0),\n    );\n    let near = 0.001;\n    let far = 1000.0;\n    let t = tan(0.3927);\n    let proj = mat4x4<f32>(\n        vec4<f32>(1.0 / (cam.aspect * t), 0.0, 0.0, 0.0),\n        vec4<f32>(0.0, 1.0 / t, 0.0, 0.0),\n        vec4<f32>(0.0, 0.0, -(far + near) / (far - near), -1.0),\n        vec4<f32>(0.0, 0.0, -2.0 * far * near / (far - near), 0.0),\n    );\n    return proj * view * vec4<f32>(pos, 1.0);\n}\n\n@vertex\nfn vs(\n    @location(0) corner: vec2<f32>,\n    @location(1) center_2d: vec2<f32>,\n    @location(2) radius_px: f32,\n    @location(3) pick_id: f32,\n) -> VsOut {\n    let world = frame.pos.xyz + center_2d.x * frame.x_axis.xyz + center_2d.y * frame.y_axis.xyz;\n    let center_clip = project_world(world);\n    let size = max(viewport.size, vec2<f32>(1.0, 1.0));\n    let offset_ndc = vec2<f32>(\n        corner.x * radius_px * 2.0 / size.x,\n        corner.y * radius_px * 2.0 / size.y\n    );\n    var out: VsOut;\n    out.clip_pos = vec4<f32>(\n        center_clip.xy + offset_ndc * center_clip.w,\n        center_clip.z,\n        center_clip.w);\n    out.pick_id = pick_id;\n    out.local_pos = corner;\n    return out;\n}\n\n@fragment\nfn fs(in: VsOut) -> @location(0) u32 {\n    if (dot(in.local_pos, in.local_pos) > 1.0) { discard; }\n    return u32(in.pick_id) + 1u;\n}\n";

export function mount(root) {
    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => {
        console.log(some("F# viewer: Phase 4 mounting"));
        const shadow = (root.shadowRoot == null) ? (root.attachShadow({
            mode: "open",
        })) : root.shadowRoot;
        shadow.innerHTML = "";
        const container = document.createElement("div");
        container.style.width = "100%";
        container.style.height = "100%";
        container.style.position = "relative";
        container.style.background = PAGE_BG;
        shadow.appendChild(container);
        const canvas = document.createElement("canvas");
        canvas.style.width = "100%";
        canvas.style.height = "100%";
        canvas.style.display = "block";
        canvas.style.cursor = "default";
        container.appendChild(canvas);
        const badge = document.createElement("div");
        badge.style.position = "absolute";
        badge.style.top = "8px";
        badge.style.left = "8px";
        badge.style.padding = "4px 8px";
        badge.style.background = "rgba(20,20,20,0.85)";
        badge.style.color = "#9fd";
        badge.style.fontFamily = "ui-monospace, monospace";
        badge.style.fontSize = "11px";
        badge.style.borderRadius = "3px";
        badge.style.pointerEvents = "none";
        badge.style.whiteSpace = "pre";
        badge.textContent = "F# viewer (Phase 4)\ninitializing…";
        container.appendChild(badge);
        const matchValue = gpu();
        if (matchValue != null) {
            const g = matchValue;
            return g.requestAdapter().then((_arg) => {
                const adapter = _arg;
                if (adapter == null) {
                    badge.textContent = "ERROR: requestAdapter returned null";
                    return Promise.resolve(container);
                }
                else {
                    return adapter.requestDevice().then((_arg_1) => {
                        const device = _arg_1;
                        return loadAtlas(device, "/fonts/dekal.png").then((_arg_2) => {
                            const atlas = _arg_2;
                            return loadMetrics("/fonts/dekal.json").then((_arg_3) => {
                                let arg_2;
                                const fontMetrics = _arg_3;
                                console.log(some((arg_2 = (FSharpMap__get_Count(fontMetrics.Chars) | 0), toText(printf("MSDF atlas: %dx%d · %d chars"))(atlas.Width)(atlas.Height)(arg_2))));
                                const ctx = canvas.getContext('webgpu');
                                const format = g.getPreferredCanvasFormat();
                                ctx.configure(new GPUCanvasConfiguration(device, format, "opaque"));
                                const dpr = window.devicePixelRatio;
                                let depthTex = defaultOf();
                                let pickTex = defaultOf();
                                const resize = () => {
                                    const w_1 = ~~(canvas.clientWidth * dpr) | 0;
                                    const h_1 = ~~(canvas.clientHeight * dpr) | 0;
                                    if ((w_1 > 0) && (h_1 > 0)) {
                                        canvas.width = w_1;
                                        canvas.height = h_1;
                                        const w = canvas.width | 0;
                                        const h = canvas.height | 0;
                                        if ((w > 0) && (h > 0)) {
                                            if (!(depthTex == null)) {
                                                depthTex.destroy();
                                            }
                                            depthTex = device.createTexture(new GPUTextureDescriptor(new GPUExtent3D(w, h, 1), "depth24plus", GPUTextureUsage_RenderAttachment));
                                            if (!(pickTex == null)) {
                                                pickTex.destroy();
                                            }
                                            pickTex = device.createTexture(new GPUTextureDescriptor(new GPUExtent3D(w, h, 1), "r32uint", GPUTextureUsage_RenderAttachment | GPUTextureUsage_CopySrc));
                                        }
                                    }
                                };
                                resize();
                                const observer = new ResizeObserver((_arg_4) => {
                                    resize();
                                });
                                observer.observe(canvas);
                                const pickReadBuffer = device.createBuffer(new GPUBufferDescriptor(256, GPUBufferUsage_CopyDst | GPUBufferUsage_MapRead));
                                const cameraBuffer = device.createBuffer(new GPUBufferDescriptor(64, GPUBufferUsage_Uniform | GPUBufferUsage_CopyDst));
                                const cameraBindGroupLayout = device.createBindGroupLayout(new GPUBindGroupLayoutDescriptor([{
                                    binding: 0,
                                    buffer: {
                                        type: "uniform",
                                    },
                                    visibility: GPUShaderStage_Vertex | GPUShaderStage_Fragment,
                                }]));
                                const cameraBindGroup = device.createBindGroup(new GPUBindGroupDescriptor(cameraBindGroupLayout, [new GPUBindGroupEntry(0, new GPUBufferBinding(cameraBuffer))]));
                                const frameBuffer = device.createBuffer(new GPUBufferDescriptor(64, GPUBufferUsage_Uniform | GPUBufferUsage_CopyDst));
                                const frameBindGroupLayout = device.createBindGroupLayout(new GPUBindGroupLayoutDescriptor([{
                                    binding: 0,
                                    buffer: {
                                        type: "uniform",
                                    },
                                    visibility: GPUShaderStage_Vertex,
                                }]));
                                const frameBindGroup = device.createBindGroup(new GPUBindGroupDescriptor(frameBindGroupLayout, [new GPUBindGroupEntry(0, new GPUBufferBinding(frameBuffer))]));
                                const lineShader = device.createShaderModule(new GPUShaderModuleDescriptor(lineWgsl));
                                const linePipelineLayout = device.createPipelineLayout(new GPUPipelineLayoutDescriptor([cameraBindGroupLayout, frameBindGroupLayout]));
                                const linePipeline = device.createRenderPipeline({
                                    depthStencil: {
                                        depthCompare: "less",
                                        depthWriteEnabled: false,
                                        format: "depth24plus",
                                    },
                                    fragment: {
                                        entryPoint: "fs",
                                        module: lineShader,
                                        targets: [{
                                            blend: {
                                                alpha: {
                                                    dstFactor: "one-minus-src-alpha",
                                                    operation: "add",
                                                    srcFactor: "one",
                                                },
                                                color: {
                                                    dstFactor: "one-minus-src-alpha",
                                                    operation: "add",
                                                    srcFactor: "src-alpha",
                                                },
                                            },
                                            format: format,
                                        }],
                                    },
                                    layout: linePipelineLayout,
                                    primitive: {
                                        topology: "line-list",
                                    },
                                    vertex: {
                                        buffers: [{
                                            arrayStride: 6 * 4,
                                            attributes: [{
                                                format: "float32x2",
                                                offset: 0,
                                                shaderLocation: 0,
                                            }, {
                                                format: "float32x4",
                                                offset: 8,
                                                shaderLocation: 1,
                                            }],
                                            stepMode: "vertex",
                                        }],
                                        entryPoint: "vs",
                                        module: lineShader,
                                    },
                                });
                                const triPipeline = device.createRenderPipeline({
                                    depthStencil: {
                                        depthCompare: "less",
                                        depthWriteEnabled: false,
                                        format: "depth24plus",
                                    },
                                    fragment: {
                                        entryPoint: "fs",
                                        module: lineShader,
                                        targets: [{
                                            blend: {
                                                alpha: {
                                                    dstFactor: "one-minus-src-alpha",
                                                    operation: "add",
                                                    srcFactor: "one",
                                                },
                                                color: {
                                                    dstFactor: "one-minus-src-alpha",
                                                    operation: "add",
                                                    srcFactor: "src-alpha",
                                                },
                                            },
                                            format: format,
                                        }],
                                    },
                                    layout: linePipelineLayout,
                                    primitive: {
                                        topology: "triangle-list",
                                    },
                                    vertex: {
                                        buffers: [{
                                            arrayStride: 6 * 4,
                                            attributes: [{
                                                format: "float32x2",
                                                offset: 0,
                                                shaderLocation: 0,
                                            }, {
                                                format: "float32x4",
                                                offset: 8,
                                                shaderLocation: 1,
                                            }],
                                            stepMode: "vertex",
                                        }],
                                        entryPoint: "vs",
                                        module: lineShader,
                                    },
                                });
                                const viewportBuffer = device.createBuffer(new GPUBufferDescriptor(16, GPUBufferUsage_Uniform | GPUBufferUsage_CopyDst));
                                const viewportBindGroupLayout = device.createBindGroupLayout(new GPUBindGroupLayoutDescriptor([{
                                    binding: 0,
                                    buffer: {
                                        type: "uniform",
                                    },
                                    visibility: GPUShaderStage_Vertex,
                                }]));
                                const viewportBindGroup = device.createBindGroup(new GPUBindGroupDescriptor(viewportBindGroupLayout, [new GPUBindGroupEntry(0, new GPUBufferBinding(viewportBuffer))]));
                                const pointQuadBuffer = device.createBuffer(new GPUBufferDescriptor(48, GPUBufferUsage_Vertex | GPUBufferUsage_CopyDst));
                                device.queue.writeBuffer(pointQuadBuffer, 0, new Float32Array(new Float32Array([-1, -1, 1, -1, -1, 1, 1, -1, 1, 1, -1, 1])));
                                const pointShader = device.createShaderModule(new GPUShaderModuleDescriptor(pointWgsl));
                                const pointPipelineLayout = device.createPipelineLayout(new GPUPipelineLayoutDescriptor([cameraBindGroupLayout, frameBindGroupLayout, viewportBindGroupLayout]));
                                const loopPickShader = device.createShaderModule(new GPUShaderModuleDescriptor(loopPickWgsl));
                                const loopPickPipelineLayout = device.createPipelineLayout(new GPUPipelineLayoutDescriptor([cameraBindGroupLayout, frameBindGroupLayout]));
                                const loopPickPipeline = device.createRenderPipeline({
                                    depthStencil: {
                                        depthCompare: "less",
                                        depthWriteEnabled: true,
                                        format: "depth24plus",
                                    },
                                    fragment: {
                                        entryPoint: "fs",
                                        module: loopPickShader,
                                        targets: [{
                                            format: "r32uint",
                                        }],
                                    },
                                    layout: loopPickPipelineLayout,
                                    primitive: {
                                        topology: "triangle-list",
                                    },
                                    vertex: {
                                        buffers: [{
                                            arrayStride: 3 * 4,
                                            attributes: [{
                                                format: "float32x2",
                                                offset: 0,
                                                shaderLocation: 0,
                                            }, {
                                                format: "float32",
                                                offset: 8,
                                                shaderLocation: 1,
                                            }],
                                            stepMode: "vertex",
                                        }],
                                        entryPoint: "vs",
                                        module: loopPickShader,
                                    },
                                });
                                const linePickShader = device.createShaderModule(new GPUShaderModuleDescriptor(linePickWgsl));
                                const linePickCornerBuffer = device.createBuffer(new GPUBufferDescriptor(48, GPUBufferUsage_Vertex | GPUBufferUsage_CopyDst));
                                device.queue.writeBuffer(linePickCornerBuffer, 0, new Float32Array(new Float32Array([0, -1, 1, -1, 0, 1, 1, -1, 1, 1, 0, 1])));
                                const linePickPipelineLayout = device.createPipelineLayout(new GPUPipelineLayoutDescriptor([cameraBindGroupLayout, frameBindGroupLayout]));
                                const linePickPipeline = device.createRenderPipeline({
                                    depthStencil: {
                                        depthCompare: "less",
                                        depthWriteEnabled: true,
                                        format: "depth24plus",
                                    },
                                    fragment: {
                                        entryPoint: "fs",
                                        module: linePickShader,
                                        targets: [{
                                            format: "r32uint",
                                        }],
                                    },
                                    layout: linePickPipelineLayout,
                                    primitive: {
                                        topology: "triangle-list",
                                    },
                                    vertex: {
                                        buffers: [{
                                            arrayStride: 2 * 4,
                                            attributes: [{
                                                format: "float32x2",
                                                offset: 0,
                                                shaderLocation: 0,
                                            }],
                                            stepMode: "vertex",
                                        }, {
                                            arrayStride: 5 * 4,
                                            attributes: [{
                                                format: "float32x2",
                                                offset: 0,
                                                shaderLocation: 1,
                                            }, {
                                                format: "float32x2",
                                                offset: 8,
                                                shaderLocation: 2,
                                            }, {
                                                format: "float32",
                                                offset: 16,
                                                shaderLocation: 3,
                                            }],
                                            stepMode: "instance",
                                        }],
                                        entryPoint: "vs",
                                        module: linePickShader,
                                    },
                                });
                                const pointPickShader = device.createShaderModule(new GPUShaderModuleDescriptor(pointPickWgsl));
                                const pointPickPipeline = device.createRenderPipeline({
                                    depthStencil: {
                                        depthCompare: "less",
                                        depthWriteEnabled: true,
                                        format: "depth24plus",
                                    },
                                    fragment: {
                                        entryPoint: "fs",
                                        module: pointPickShader,
                                        targets: [{
                                            format: "r32uint",
                                        }],
                                    },
                                    layout: pointPipelineLayout,
                                    primitive: {
                                        topology: "triangle-list",
                                    },
                                    vertex: {
                                        buffers: [{
                                            arrayStride: 2 * 4,
                                            attributes: [{
                                                format: "float32x2",
                                                offset: 0,
                                                shaderLocation: 0,
                                            }],
                                            stepMode: "vertex",
                                        }, {
                                            arrayStride: 4 * 4,
                                            attributes: [{
                                                format: "float32x2",
                                                offset: 0,
                                                shaderLocation: 1,
                                            }, {
                                                format: "float32",
                                                offset: 8,
                                                shaderLocation: 2,
                                            }, {
                                                format: "float32",
                                                offset: 12,
                                                shaderLocation: 3,
                                            }],
                                            stepMode: "instance",
                                        }],
                                        entryPoint: "vs",
                                        module: pointPickShader,
                                    },
                                });
                                const pointPipeline = device.createRenderPipeline({
                                    depthStencil: {
                                        depthCompare: "less",
                                        depthWriteEnabled: false,
                                        format: "depth24plus",
                                    },
                                    fragment: {
                                        entryPoint: "fs",
                                        module: pointShader,
                                        targets: [{
                                            blend: {
                                                alpha: {
                                                    dstFactor: "one-minus-src-alpha",
                                                    operation: "add",
                                                    srcFactor: "one",
                                                },
                                                color: {
                                                    dstFactor: "one-minus-src-alpha",
                                                    operation: "add",
                                                    srcFactor: "src-alpha",
                                                },
                                            },
                                            format: format,
                                        }],
                                    },
                                    layout: pointPipelineLayout,
                                    primitive: {
                                        topology: "triangle-list",
                                    },
                                    vertex: {
                                        buffers: [{
                                            arrayStride: 2 * 4,
                                            attributes: [{
                                                format: "float32x2",
                                                offset: 0,
                                                shaderLocation: 0,
                                            }],
                                            stepMode: "vertex",
                                        }, {
                                            arrayStride: 7 * 4,
                                            attributes: [{
                                                format: "float32x2",
                                                offset: 0,
                                                shaderLocation: 1,
                                            }, {
                                                format: "float32",
                                                offset: 8,
                                                shaderLocation: 2,
                                            }, {
                                                format: "float32x4",
                                                offset: 12,
                                                shaderLocation: 3,
                                            }],
                                            stepMode: "instance",
                                        }],
                                        entryPoint: "vs",
                                        module: pointShader,
                                    },
                                });
                                const gizmoShader = device.createShaderModule(new GPUShaderModuleDescriptor(gizmoWgsl));
                                const gizmoPipelineLayout = device.createPipelineLayout(new GPUPipelineLayoutDescriptor([cameraBindGroupLayout, viewportBindGroupLayout]));
                                const gizmoPipeline = device.createRenderPipeline({
                                    depthStencil: {
                                        depthCompare: "less",
                                        depthWriteEnabled: false,
                                        format: "depth24plus",
                                    },
                                    fragment: {
                                        entryPoint: "fs",
                                        module: gizmoShader,
                                        targets: [{
                                            blend: {
                                                alpha: {
                                                    dstFactor: "one-minus-src-alpha",
                                                    operation: "add",
                                                    srcFactor: "one",
                                                },
                                                color: {
                                                    dstFactor: "one-minus-src-alpha",
                                                    operation: "add",
                                                    srcFactor: "src-alpha",
                                                },
                                            },
                                            format: format,
                                        }],
                                    },
                                    layout: gizmoPipelineLayout,
                                    primitive: {
                                        topology: "line-list",
                                    },
                                    vertex: {
                                        buffers: [{
                                            arrayStride: 12 * 4,
                                            attributes: [{
                                                format: "float32x3",
                                                offset: 0,
                                                shaderLocation: 0,
                                            }, {
                                                format: "float32x3",
                                                offset: 12,
                                                shaderLocation: 1,
                                            }, {
                                                format: "float32",
                                                offset: 24,
                                                shaderLocation: 2,
                                            }, {
                                                format: "float32",
                                                offset: 28,
                                                shaderLocation: 3,
                                            }, {
                                                format: "float32x4",
                                                offset: 32,
                                                shaderLocation: 4,
                                            }],
                                            stepMode: "vertex",
                                        }],
                                        entryPoint: "vs",
                                        module: gizmoShader,
                                    },
                                });
                                const worldPointPipelineLayout = device.createPipelineLayout(new GPUPipelineLayoutDescriptor([cameraBindGroupLayout, viewportBindGroupLayout]));
                                const worldPointShader = device.createShaderModule(new GPUShaderModuleDescriptor(worldPointWgsl));
                                const worldPointPipeline = device.createRenderPipeline({
                                    depthStencil: {
                                        depthCompare: "less",
                                        depthWriteEnabled: false,
                                        format: "depth24plus",
                                    },
                                    fragment: {
                                        entryPoint: "fs",
                                        module: worldPointShader,
                                        targets: [{
                                            blend: {
                                                alpha: {
                                                    dstFactor: "one-minus-src-alpha",
                                                    operation: "add",
                                                    srcFactor: "one",
                                                },
                                                color: {
                                                    dstFactor: "one-minus-src-alpha",
                                                    operation: "add",
                                                    srcFactor: "src-alpha",
                                                },
                                            },
                                            format: format,
                                        }],
                                    },
                                    layout: worldPointPipelineLayout,
                                    primitive: {
                                        topology: "triangle-list",
                                    },
                                    vertex: {
                                        buffers: [{
                                            arrayStride: 2 * 4,
                                            attributes: [{
                                                format: "float32x2",
                                                offset: 0,
                                                shaderLocation: 0,
                                            }],
                                            stepMode: "vertex",
                                        }, {
                                            arrayStride: 8 * 4,
                                            attributes: [{
                                                format: "float32x3",
                                                offset: 0,
                                                shaderLocation: 1,
                                            }, {
                                                format: "float32",
                                                offset: 12,
                                                shaderLocation: 2,
                                            }, {
                                                format: "float32x4",
                                                offset: 16,
                                                shaderLocation: 3,
                                            }],
                                            stepMode: "instance",
                                        }],
                                        entryPoint: "vs",
                                        module: worldPointShader,
                                    },
                                });
                                const worldPointPickShader = device.createShaderModule(new GPUShaderModuleDescriptor(worldPointPickWgsl));
                                const worldPointPickPipeline = device.createRenderPipeline({
                                    depthStencil: {
                                        depthCompare: "less",
                                        depthWriteEnabled: true,
                                        format: "depth24plus",
                                    },
                                    fragment: {
                                        entryPoint: "fs",
                                        module: worldPointPickShader,
                                        targets: [{
                                            format: "r32uint",
                                        }],
                                    },
                                    layout: worldPointPipelineLayout,
                                    primitive: {
                                        topology: "triangle-list",
                                    },
                                    vertex: {
                                        buffers: [{
                                            arrayStride: 2 * 4,
                                            attributes: [{
                                                format: "float32x2",
                                                offset: 0,
                                                shaderLocation: 0,
                                            }],
                                            stepMode: "vertex",
                                        }, {
                                            arrayStride: 5 * 4,
                                            attributes: [{
                                                format: "float32x3",
                                                offset: 0,
                                                shaderLocation: 1,
                                            }, {
                                                format: "float32",
                                                offset: 12,
                                                shaderLocation: 2,
                                            }, {
                                                format: "float32",
                                                offset: 16,
                                                shaderLocation: 3,
                                            }],
                                            stepMode: "instance",
                                        }],
                                        entryPoint: "vs",
                                        module: worldPointPickShader,
                                    },
                                });
                                const labelShader = device.createShaderModule(new GPUShaderModuleDescriptor(labelWgsl));
                                const labelUniformBuffer = device.createBuffer(new GPUBufferDescriptor(64, GPUBufferUsage_Uniform | GPUBufferUsage_CopyDst));
                                const labelBindGroupLayout = device.createBindGroupLayout(new GPUBindGroupLayoutDescriptor([{
                                    binding: 0,
                                    buffer: {
                                        type: "uniform",
                                    },
                                    visibility: GPUShaderStage_Vertex,
                                }, {
                                    binding: 1,
                                    texture: {
                                        sampleType: "float",
                                        viewDimension: "2d",
                                    },
                                    visibility: GPUShaderStage_Fragment,
                                }, {
                                    binding: 2,
                                    sampler: {
                                        type: "filtering",
                                    },
                                    visibility: GPUShaderStage_Fragment,
                                }]));
                                const labelBindGroup = device.createBindGroup(new GPUBindGroupDescriptor(labelBindGroupLayout, [new GPUBindGroupEntry(0, new GPUBufferBinding(labelUniformBuffer)), new GPUBindGroupEntry(1, atlas.Texture.createView()), new GPUBindGroupEntry(2, atlas.Sampler)]));
                                const labelPipelineLayout = device.createPipelineLayout(new GPUPipelineLayoutDescriptor([cameraBindGroupLayout, labelBindGroupLayout]));
                                const labelPipeline = device.createRenderPipeline({
                                    depthStencil: {
                                        depthCompare: "less",
                                        depthWriteEnabled: false,
                                        format: "depth24plus",
                                    },
                                    fragment: {
                                        entryPoint: "fs",
                                        module: labelShader,
                                        targets: [{
                                            blend: {
                                                alpha: {
                                                    dstFactor: "one-minus-src-alpha",
                                                    operation: "add",
                                                    srcFactor: "one",
                                                },
                                                color: {
                                                    dstFactor: "one-minus-src-alpha",
                                                    operation: "add",
                                                    srcFactor: "src-alpha",
                                                },
                                            },
                                            format: format,
                                        }],
                                    },
                                    layout: labelPipelineLayout,
                                    primitive: {
                                        topology: "triangle-list",
                                    },
                                    vertex: {
                                        buffers: [{
                                            arrayStride: 10 * 4,
                                            attributes: [{
                                                format: "float32x2",
                                                offset: 0,
                                                shaderLocation: 0,
                                            }, {
                                                format: "float32x2",
                                                offset: 8,
                                                shaderLocation: 1,
                                            }, {
                                                format: "float32x2",
                                                offset: 16,
                                                shaderLocation: 2,
                                            }, {
                                                format: "float32x4",
                                                offset: 24,
                                                shaderLocation: 3,
                                            }],
                                            stepMode: "vertex",
                                        }],
                                        entryPoint: "vs",
                                        module: labelShader,
                                    },
                                });
                                let submittedFrameCount = 0;
                                const retiredBuffers = [];
                                const makeSlot = () => [new FSharpRef(undefined), new FSharpRef(0)];
                                const upload = (bufRef, capRef, data) => {
                                    const bytes = (data.length * 4) | 0;
                                    if (capRef.contents < bytes) {
                                        iterate((buffer) => {
                                            void (retiredBuffers.push([buffer, submittedFrameCount + 8]));
                                        }, toArray(bufRef.contents));
                                        const newCap = max(1024, max(bytes, capRef.contents * 2)) | 0;
                                        capRef.contents = (newCap | 0);
                                        bufRef.contents = device.createBuffer(new GPUBufferDescriptor(newCap, GPUBufferUsage_Vertex | GPUBufferUsage_CopyDst));
                                    }
                                    const matchValue_1 = bufRef.contents;
                                    if (matchValue_1 == null) {
                                        throw new Error("unreachable");
                                    }
                                    else {
                                        const buf = matchValue_1;
                                        device.queue.writeBuffer(buf, 0, new Float32Array(data));
                                        return buf;
                                    }
                                };
                                const gridSlot = makeSlot();
                                const loopFillSlot = makeSlot();
                                const gizmoSlot = makeSlot();
                                const constraintLineSlot = makeSlot();
                                const sketchLineSlot = makeSlot();
                                const sketchPointSlot = makeSlot();
                                const labelSlot = makeSlot();
                                const loopPickSlot = makeSlot();
                                const linePickSlot = makeSlot();
                                const pointPickSlot = makeSlot();
                                const dimPickSlot = makeSlot();
                                const toolPreviewLineSlot = makeSlot();
                                const toolPreviewPointSlot = makeSlot();
                                const placementPreviewLineSlot = makeSlot();
                                const placementPreviewLabelSlot = makeSlot();
                                const frameOriginPointSlot = makeSlot();
                                const frameOriginPickSlot = makeSlot();
                                const frameAxisPickSlot = makeSlot();
                                const frameGizmoSlot = makeSlot();
                                let toolCursor = undefined;
                                badge.textContent = "F# viewer";
                                const camera = create();
                                let pickableById = empty({
                                    Compare: comparePrimitives,
                                });
                                let lastSentCompiled = defaultOf();
                                const refreshPickables = () => {
                                    const state = store.State;
                                    const compiled = state.Compiled;
                                    if (!equals(compiled, lastSentCompiled)) {
                                        lastSentCompiled = compiled;
                                        const model = ViewerPipeline_viewerModel(state);
                                        pickableById = ofList(map((p) => [PickableModule_pickId(p), p], model.Pickables), {
                                            Compare: comparePrimitives,
                                        });
                                    }
                                };
                                subscribe(store, refreshPickables);
                                refreshPickables();
                                let pickInFlight = false;
                                let dragButton = undefined;
                                let dragStart = [0, 0];
                                let dragLast = [0, 0];
                                let dragPickable = undefined;
                                let dragActive = false;
                                const mouseToSketchLocal = (sketchId_1, mx, my) => {
                                    const matchValue_2 = map_1((f_1) => [f_1.Transform.Trans, Quat__Rotate_Z2E054BF3(f_1.Transform.Rot, new Vec3(1, 0, 0)), Quat__Rotate_Z2E054BF3(f_1.Transform.Rot, new Vec3(0, 1, 0))], tryFind((f) => (f.Id === sketchId_1), ViewerPipeline_viewerState(store.State).SketchTransforms));
                                    if (matchValue_2 != null) {
                                        const yAxis_1 = matchValue_2[2];
                                        const xAxis_1 = matchValue_2[1];
                                        const origin_1 = matchValue_2[0];
                                        const rect = canvas.getBoundingClientRect();
                                        const localX = (mx - rect.left) * dpr;
                                        const localY = (my - rect.top) * dpr;
                                        return rayPlaneIntersection(screenToRay(canvas.clientWidth * dpr, canvas.clientHeight * dpr, camera, localX, localY), origin_1, xAxis_1, yAxis_1);
                                    }
                                    else {
                                        return undefined;
                                    }
                                };
                                const pickAt = (px, py) => PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => {
                                    if (pickInFlight) {
                                        return Promise.resolve(0);
                                    }
                                    else {
                                        pickInFlight = true;
                                        const encoder = device.createCommandEncoder();
                                        encoder.copyTextureToBuffer(
    { texture: pickTex, origin: { x: px, y: py, z: 0 }, mipLevel: 0 },
    { buffer: pickReadBuffer, bytesPerRow: 256 },
    { width: 1, height: 1, depthOrArrayLayers: 1 }
);
                                        device.queue.submit([encoder.finish()]);
                                        return pickReadBuffer.mapAsync(GPUMapMode_Read).then(() => {
                                            const arr = pickReadBuffer.getMappedRange();
                                            const id = new Uint32Array(arr)[0] >>> 0;
                                            pickReadBuffer.unmap();
                                            pickInFlight = false;
                                            return Promise.resolve(id);
                                        });
                                    }
                                }));
                                canvas.addEventListener("mousedown", ((e) => {
                                    const button = (e.button) | 0;
                                    if (button === 1) {
                                        e.preventDefault();
                                    }
                                    const matchValue_3 = e.clientX;
                                    const y_2 = e.clientY;
                                    const x_2 = matchValue_3;
                                    dragButton = button;
                                    dragStart = [x_2, y_2];
                                    dragLast = [x_2, y_2];
                                    dragPickable = undefined;
                                    dragActive = false;
                                    if (button === 0) {
                                        const viewState_1 = ViewerPipeline_viewerState(store.State);
                                        const toolActive = ((viewState_1.SketchUi.EditMode && (viewState_1.SketchUi.Tool !== "none")) && (viewState_1.SketchUi.Tool !== "")) && (viewState_1.SketchUi.Tool !== "select");
                                        const placementActive = viewState_1.SketchUi.EditMode && (viewState_1.SketchUi.ConstraintPlacementMode != null);
                                        const rect_1 = canvas.getBoundingClientRect();
                                        const pr = pickAt(~~((x_2 - rect_1.left) * dpr), ~~((y_2 - rect_1.top) * dpr));
                                        void (pr.then((id_1) => {
                                            if (placementActive) {
                                                dragPickable = undefined;
                                                const hovered = (id_1 === 0) ? undefined : tryFind_1(~~id_1 - 1, pickableById);
                                                if ((hovered != null) && ((hovered.tag === 0) ? true : ((hovered.tag === 1) ? true : ((hovered.tag === 2) ? true : ((hovered.tag === 3) ? true : (hovered.tag === 6)))))) {
                                                    dispatch(store, new Message(16, [singleton(new PickCandidateInput(~~id_1 - 1, 0))]));
                                                    dispatch(store, new Message(21, []));
                                                }
                                                else {
                                                    const latest = ViewerPipeline_viewerState(store.State);
                                                    const toolCursor_1 = toolCursor;
                                                    let matchResult, u, v;
                                                    if (latest.SketchUi.PendingConstraintPlacement != null) {
                                                        if (toolCursor_1 != null) {
                                                            matchResult = 0;
                                                            u = toolCursor_1[1];
                                                            v = toolCursor_1[2];
                                                        }
                                                        else {
                                                            matchResult = 1;
                                                        }
                                                    }
                                                    else {
                                                        matchResult = 1;
                                                    }
                                                    switch (matchResult) {
                                                        case 0: {
                                                            dispatch(store, new Message(30, [u, v]));
                                                            break;
                                                        }
                                                        case 1: {
                                                            break;
                                                        }
                                                    }
                                                }
                                            }
                                            else if (toolActive) {
                                                dragPickable = undefined;
                                                if (toolCursor == null) {
                                                }
                                                else {
                                                    const v_1 = toolCursor[2];
                                                    dispatch(store, new Message(29, [toolCursor[1], v_1]));
                                                }
                                            }
                                            else if (id_1 === 0) {
                                                dragPickable = undefined;
                                                dispatch(store, new Message(17, ["replace", empty_1()]));
                                            }
                                            else {
                                                const pickId_2 = (~~id_1 - 1) | 0;
                                                dragPickable = tryFind_1(pickId_2, pickableById);
                                                dispatch(store, new Message(17, ["replace", singleton(new PickCandidateInput(pickId_2, 0))]));
                                            }
                                        }));
                                    }
                                }), { passive: false });
                                canvas.addEventListener("dblclick", ((e_1) => {
                                    e_1.preventDefault();
                                    const rect_2 = canvas.getBoundingClientRect();
                                    const pr_1 = pickAt(~~(((e_1.clientX) - rect_2.left) * dpr), ~~(((e_1.clientY) - rect_2.top) * dpr));
                                    void (pr_1.then((id_2) => {
                                        if (id_2 !== 0) {
                                            const matchValue_7 = tryFind_1(~~id_2 - 1, pickableById);
                                            if (matchValue_7 == null) {
                                            }
                                            else if (matchValue_7.tag === 5) {
                                                const idx = matchValue_7.fields[2] | 0;
                                                const sid = matchValue_7.fields[1];
                                                if (!ViewerPipeline_viewerState(store.State).SketchUi.EditMode) {
                                                    dispatch(store, new Message(0, [sid]));
                                                    dispatch(store, new Message(31, []));
                                                }
                                                dispatch(store, new Message(18, [idx]));
                                            }
                                            else {
                                                const p_1 = matchValue_7;
                                                const sketchIdOpt = (p_1.tag === 0) ? p_1.fields[1] : ((p_1.tag === 1) ? p_1.fields[1] : ((p_1.tag === 2) ? p_1.fields[1] : ((p_1.tag === 3) ? p_1.fields[1] : ((p_1.tag === 4) ? p_1.fields[1] : undefined))));
                                                if (sketchIdOpt == null) {
                                                }
                                                else {
                                                    const sid_2 = sketchIdOpt;
                                                    const vs_1 = ViewerPipeline_viewerState(store.State);
                                                    dispatch(store, new Message(0, [sid_2]));
                                                    if (!vs_1.SketchUi.EditMode) {
                                                        dispatch(store, new Message(31, []));
                                                    }
                                                }
                                            }
                                        }
                                    }));
                                }));
                                window.addEventListener("mousemove", ((e_2) => {
                                    let pid, cix;
                                    const state_4 = store.State;
                                    let matchValue_8;
                                    const state_3 = state_4;
                                    const vs_2 = ViewerPipeline_viewerState(state_3);
                                    matchValue_8 = (!vs_2.SketchUi.EditMode ? undefined : filter((id_3) => exists((t) => (t.Id === id_3), vs_2.SketchTransforms), state_3.Doc.SelectedId));
                                    if (matchValue_8 == null) {
                                        toolCursor = undefined;
                                    }
                                    else {
                                        const sid_3 = matchValue_8;
                                        const matchValue_11 = mouseToSketchLocal(sid_3, e_2.clientX, e_2.clientY);
                                        if (matchValue_11 == null) {
                                        }
                                        else {
                                            const v_2 = matchValue_11[1];
                                            const u_2 = matchValue_11[0];
                                            toolCursor = [sid_3, u_2, v_2];
                                            if (ViewerPipeline_viewerState(state_4).SketchUi.PendingConstraintPlacement != null) {
                                                dispatch(store, new Message(36, [[sid_3, new LabelPos(u_2, v_2)]]));
                                            }
                                        }
                                    }
                                    if ((dragButton == null) && !pickInFlight) {
                                        const rect_3 = canvas.getBoundingClientRect();
                                        const px_3 = ~~(((e_2.clientX) - rect_3.left) * dpr) | 0;
                                        const py_3 = ~~(((e_2.clientY) - rect_3.top) * dpr) | 0;
                                        if ((((px_3 >= 0) && (py_3 >= 0)) && (px_3 < canvas.width)) && (py_3 < canvas.height)) {
                                            const pr_2 = pickAt(px_3, py_3);
                                            void (pr_2.then((id_4) => {
                                                if (id_4 === 0) {
                                                    dispatch(store, new Message(16, [empty_1()]));
                                                }
                                                else {
                                                    dispatch(store, new Message(16, [singleton(new PickCandidateInput(~~id_4 - 1, 0))]));
                                                }
                                            }));
                                        }
                                    }
                                    if (dragButton != null) {
                                        const button_1 = dragButton | 0;
                                        const matchValue_12 = e_2.clientX;
                                        const y_3 = e_2.clientY;
                                        const x_3 = matchValue_12;
                                        const ly = dragLast[1];
                                        const dx = x_3 - dragLast[0];
                                        const dy = y_3 - ly;
                                        const sy = dragStart[1];
                                        const sx = dragStart[0];
                                        const movedPx = Math.sqrt(((x_3 - sx) * (x_3 - sx)) + ((y_3 - sy) * (y_3 - sy)));
                                        const updateDragTo = (u_5, v_5) => (new Message(24, [new LabelPos(u_5, v_5)]));
                                        const dragPickable_1 = dragPickable;
                                        let matchResult_1, pid_1, sid_6, cix_1, sid_7;
                                        switch (button_1) {
                                            case 0: {
                                                if (dragPickable_1 != null) {
                                                    switch (dragPickable_1.tag) {
                                                        case 0: {
                                                            matchResult_1 = 0;
                                                            pid_1 = dragPickable_1.fields[2];
                                                            sid_6 = dragPickable_1.fields[1];
                                                            break;
                                                        }
                                                        case 5: {
                                                            matchResult_1 = 1;
                                                            cix_1 = dragPickable_1.fields[2];
                                                            sid_7 = dragPickable_1.fields[1];
                                                            break;
                                                        }
                                                        default:
                                                            matchResult_1 = 4;
                                                    }
                                                }
                                                else {
                                                    matchResult_1 = 4;
                                                }
                                                break;
                                            }
                                            case 1: {
                                                matchResult_1 = 2;
                                                break;
                                            }
                                            case 2: {
                                                matchResult_1 = 3;
                                                break;
                                            }
                                            default:
                                                matchResult_1 = 4;
                                        }
                                        switch (matchResult_1) {
                                            case 0: {
                                                if (!dragActive && (movedPx > 4)) {
                                                    const matchValue_15 = mouseToSketchLocal(sid_6, x_3, y_3);
                                                    if (matchValue_15 == null) {
                                                    }
                                                    else {
                                                        const v_6 = matchValue_15[1];
                                                        const u_6 = matchValue_15[0];
                                                        dragActive = true;
                                                        dispatch(store, (pid = pid_1, new Message(23, [new SketchDrag(sid_6, new SketchDragKind(0, [pid]), new ActionParamField(31, [pid, new SketchEntityField(0, [])]), new ActionParamField(31, [pid, new SketchEntityField(1, [])]), new LabelPos(u_6, v_6))])));
                                                    }
                                                }
                                                else if (dragActive) {
                                                    const matchValue_16 = mouseToSketchLocal(sid_6, x_3, y_3);
                                                    if (matchValue_16 == null) {
                                                    }
                                                    else {
                                                        dispatch(store, updateDragTo(matchValue_16[0], matchValue_16[1]));
                                                    }
                                                }
                                                break;
                                            }
                                            case 1: {
                                                if (!dragActive && (movedPx > 4)) {
                                                    const matchValue_17 = mouseToSketchLocal(sid_7, x_3, y_3);
                                                    if (matchValue_17 == null) {
                                                    }
                                                    else {
                                                        const v_8 = matchValue_17[1];
                                                        const u_8 = matchValue_17[0];
                                                        dragActive = true;
                                                        dispatch(store, (cix = (cix_1 | 0), new Message(23, [new SketchDrag(sid_7, new SketchDragKind(1, [cix]), new ActionParamField(32, [cix, new SketchConstraintField(0, [])]), new ActionParamField(32, [cix, new SketchConstraintField(1, [])]), new LabelPos(u_8, v_8))])));
                                                    }
                                                }
                                                else if (dragActive) {
                                                    const matchValue_18 = mouseToSketchLocal(sid_7, x_3, y_3);
                                                    if (matchValue_18 == null) {
                                                    }
                                                    else {
                                                        dispatch(store, updateDragTo(matchValue_18[0], matchValue_18[1]));
                                                    }
                                                }
                                                break;
                                            }
                                            case 2: {
                                                pan(camera, dx, dy, canvas.clientHeight * dpr);
                                                break;
                                            }
                                            case 3: {
                                                orbit(camera, dx, dy);
                                                break;
                                            }
                                        }
                                        dragLast = [x_3, y_3];
                                    }
                                }));
                                window.addEventListener("mouseup", ((_arg_6) => {
                                    if (dragActive) {
                                        dispatch(store, new Message(27, []));
                                    }
                                    dragButton = undefined;
                                    dragPickable = undefined;
                                    dragActive = false;
                                }));
                                canvas.addEventListener("contextmenu", ((e_3) => {
                                    e_3.preventDefault();
                                }), { passive: false });
                                canvas.addEventListener("wheel", ((e_4) => {
                                    e_4.preventDefault();
                                    const dy_1 = e_4.deltaY;
                                    const rect_4 = canvas.getBoundingClientRect();
                                    const localX_1 = ((e_4.clientX) - rect_4.left) * dpr;
                                    const localY_1 = ((e_4.clientY) - rect_4.top) * dpr;
                                    zoomTowardsPointer(camera, canvas.clientWidth * dpr, canvas.clientHeight * dpr, localX_1, localY_1, dy_1);
                                }), { passive: false });
                                let labelDebugLogged = false;
                                const frame = (_arg_7) => {
                                    let v_10, u_10, sid_9, u_11, v_11, v_12, u_12, sid_11, u_13, v_13, arg_4, arg_5;
                                    const w_4 = canvas.width | 0;
                                    const h_4 = canvas.height | 0;
                                    const b = basis(camera);
                                    const cameraData = new Float32Array([b.Eye.X, b.Eye.Y, b.Eye.Z, 0, b.Forward.X, b.Forward.Y, b.Forward.Z, 0, b.Right.X, b.Right.Y, b.Right.Z, 0, b.Up.X, b.Up.Y, b.Up.Z, w_4 / max(h_4, 1)]);
                                    device.queue.writeBuffer(cameraBuffer, 0, new Float32Array(cameraData));
                                    device.queue.writeBuffer(viewportBuffer, 0, new Float32Array(new Float32Array([w_4, h_4, 0, 0])));
                                    const colorView = ctx.getCurrentTexture().createView();
                                    const depthView = depthTex.createView();
                                    const pickView = pickTex.createView();
                                    const encoder_1 = device.createCommandEncoder();
                                    const colorPass = encoder_1.beginRenderPass({
    colorAttachments: [{
        view: colorView,
        loadOp: 'clear',
        storeOp: 'store',
        clearValue: { r: 0.996, g: 0.988, b: 0.953, a: 1.0 }
    }],
    depthStencilAttachment: {
        view: depthView,
        depthLoadOp: 'clear',
        depthStoreOp: 'store',
        depthClearValue: 1.0
    }
});
                                    const state_5 = store.State;
                                    const model_1 = ViewerPipeline_viewerModel(state_5);
                                    const viewState_2 = ViewerPipeline_viewerState(state_5);
                                    const frameById = ofList(map((f_2) => [f_2.Id, f_2.Transform], viewState_2.SketchTransforms), {
                                        Compare: comparePrimitives,
                                    });
                                    const isVisible = (actionId) => defaultArg(tryFind_1(actionId, viewState_2.Visible), true);
                                    const enumerator = getEnumerator(model_1.Sketches);
                                    try {
                                        while (enumerator["System.Collections.IEnumerator.MoveNext"]()) {
                                            const sketch = enumerator["System.Collections.Generic.IEnumerator`1.get_Current"]();
                                            const matchValue_19 = tryFind_1(sketch.Id, frameById);
                                            if (matchValue_19 != null) {
                                                if (!isVisible(sketch.Id)) {
                                                }
                                                else {
                                                    const transform = matchValue_19;
                                                    const pos = transform.Trans;
                                                    const xAxis_2 = Quat__Rotate_Z2E054BF3(transform.Rot, new Vec3(1, 0, 0));
                                                    const yAxis_2 = Quat__Rotate_Z2E054BF3(transform.Rot, new Vec3(0, 1, 0));
                                                    const frameData = new Float32Array([pos.X, pos.Y, pos.Z, 0, xAxis_2.X, xAxis_2.Y, xAxis_2.Z, 0, yAxis_2.X, yAxis_2.Y, yAxis_2.Z, 0, 0, 0, 0, 0]);
                                                    device.queue.writeBuffer(frameBuffer, 0, new Float32Array(frameData));
                                                    const labelUniform = new Float32Array([canvas.clientWidth * dpr, canvas.clientHeight * dpr, 0, 0, pos.X, pos.Y, pos.Z, 0, xAxis_2.X, xAxis_2.Y, xAxis_2.Z, 0, yAxis_2.X, yAxis_2.Y, yAxis_2.Z, 0]);
                                                    device.queue.writeBuffer(labelUniformBuffer, 0, new Float32Array(labelUniform));
                                                    const gridData = buildSketchGridBuffer(sketch.Id, sketch.Sketch.Entities, state_5.Compiled.Slots.Index, viewState_2.Params, 1, 10);
                                                    if (gridData.length > 0) {
                                                        const gridBuf = upload(gridSlot[0], gridSlot[1], gridData);
                                                        colorPass.setPipeline(linePipeline);
                                                        colorPass.setBindGroup(0, cameraBindGroup);
                                                        colorPass.setBindGroup(1, frameBindGroup);
                                                        colorPass.setVertexBuffer(0, gridBuf);
                                                        colorPass.draw(~~(gridData.length / 6));
                                                    }
                                                    const loopFillData = buildSketchLoopFillBuffer(sketch.Id, sketch.Sketch, defaultArg(map_1((l_1) => l_1.Loops, tryFind((l) => (l.SketchId === sketch.Id), viewState_2.SketchLoops)), empty_1()), state_5.Compiled.Slots.Index, viewState_2.Params, viewState_2.HoveredTarget, viewState_2.SelectedTargets);
                                                    if (loopFillData.length > 0) {
                                                        const fbuf = upload(loopFillSlot[0], loopFillSlot[1], loopFillData);
                                                        colorPass.setPipeline(triPipeline);
                                                        colorPass.setBindGroup(0, cameraBindGroup);
                                                        colorPass.setBindGroup(1, frameBindGroup);
                                                        colorPass.setVertexBuffer(0, fbuf);
                                                        colorPass.draw(~~(loopFillData.length / 6));
                                                    }
                                                    const gizmoData = buildSketchGizmoBuffer();
                                                    const gizmoBuf = upload(gizmoSlot[0], gizmoSlot[1], gizmoData);
                                                    colorPass.setPipeline(linePipeline);
                                                    colorPass.setBindGroup(0, cameraBindGroup);
                                                    colorPass.setBindGroup(1, frameBindGroup);
                                                    colorPass.setVertexBuffer(0, gizmoBuf);
                                                    colorPass.draw(~~(gizmoData.length / 6));
                                                    const lineData = buildSketchLineBuffer(sketch.Id, sketch.Sketch.Entities, state_5.Compiled.Slots.Index, viewState_2.Params, viewState_2.HoveredTarget, viewState_2.SelectedTargets);
                                                    if (lineData.length > 0) {
                                                        const lineBuf = upload(sketchLineSlot[0], sketchLineSlot[1], lineData);
                                                        colorPass.setPipeline(linePipeline);
                                                        colorPass.setBindGroup(0, cameraBindGroup);
                                                        colorPass.setBindGroup(1, frameBindGroup);
                                                        colorPass.setVertexBuffer(0, lineBuf);
                                                        colorPass.draw(~~(lineData.length / 6));
                                                    }
                                                    const showDimensions = contains(sketch.Id, viewState_2.VisibleDimensionSketchIds, {
                                                        Equals: (x_5, y_5) => (x_5 === y_5),
                                                        GetHashCode: stringHash,
                                                    });
                                                    const constraintLineData = buildSketchConstraintLinesBuffer(sketch.Id, sketch.Sketch, state_5.Compiled.Slots.Index, viewState_2.Params, showDimensions, viewState_2.HoveredTarget, viewState_2.SelectedTargets);
                                                    if (constraintLineData.length > 0) {
                                                        const cbuf = upload(constraintLineSlot[0], constraintLineSlot[1], constraintLineData);
                                                        colorPass.setPipeline(linePipeline);
                                                        colorPass.setBindGroup(0, cameraBindGroup);
                                                        colorPass.setBindGroup(1, frameBindGroup);
                                                        colorPass.setVertexBuffer(0, cbuf);
                                                        colorPass.draw(~~(constraintLineData.length / 6));
                                                    }
                                                    const matchValue_20 = viewState_2.SketchUi.PendingConstraintPlacement;
                                                    let matchResult_2, pending_1;
                                                    if (matchValue_20 != null) {
                                                        if (matchValue_20.SketchId === sketch.Id) {
                                                            matchResult_2 = 0;
                                                            pending_1 = matchValue_20;
                                                        }
                                                        else {
                                                            matchResult_2 = 1;
                                                        }
                                                    }
                                                    else {
                                                        matchResult_2 = 1;
                                                    }
                                                    switch (matchResult_2) {
                                                        case 0: {
                                                            const cursorPos = (toolCursor != null) ? (((v_10 = toolCursor[2], (u_10 = toolCursor[1], toolCursor[0] === sketch.Id))) ? ((sid_9 = toolCursor[0], (u_11 = toolCursor[1], (v_11 = toolCursor[2], new LabelPos(u_11, v_11))))) : undefined) : undefined;
                                                            if (cursorPos == null) {
                                                            }
                                                            else {
                                                                const cursor = cursorPos;
                                                                const previewLines = buildPendingConstraintLineBuffer(sketch.Id, sketch.Sketch.Entities, state_5.Compiled.Slots.Index, viewState_2.Params, pending_1.Constraint, cursor);
                                                                if (previewLines.length > 0) {
                                                                    const pbuf = upload(placementPreviewLineSlot[0], placementPreviewLineSlot[1], previewLines);
                                                                    colorPass.setPipeline(linePipeline);
                                                                    colorPass.setBindGroup(0, cameraBindGroup);
                                                                    colorPass.setBindGroup(1, frameBindGroup);
                                                                    colorPass.setVertexBuffer(0, pbuf);
                                                                    colorPass.draw(~~(previewLines.length / 6));
                                                                }
                                                                const previewLabelData = buildSketchLabelBuffer(fontMetrics, resolvePointMap(state_5.Compiled.Slots.Index, viewState_2.Params, sketch.Id, sketch.Sketch.Entities), circleRadiusLookup(state_5.Compiled.Slots.Index, viewState_2.Params, sketch.Id, sketch.Sketch.Entities), sketch.Id, singleton(withLabelPosition(cursor, pending_1.Constraint)), undefined, empty_1());
                                                                if (previewLabelData.length > 0) {
                                                                    const lbuf = upload(placementPreviewLabelSlot[0], placementPreviewLabelSlot[1], previewLabelData);
                                                                    colorPass.setPipeline(labelPipeline);
                                                                    colorPass.setBindGroup(0, cameraBindGroup);
                                                                    colorPass.setBindGroup(1, labelBindGroup);
                                                                    colorPass.setVertexBuffer(0, lbuf);
                                                                    colorPass.draw(~~(previewLabelData.length / 10));
                                                                }
                                                            }
                                                            break;
                                                        }
                                                    }
                                                    if (((viewState_2.SketchUi.EditMode && equals(state_5.Doc.SelectedId, sketch.Id)) && (viewState_2.SketchUi.Tool !== "")) && (viewState_2.SketchUi.Tool !== "none")) {
                                                        const cursorForSketch = (toolCursor != null) ? (((v_12 = toolCursor[2], (u_12 = toolCursor[1], toolCursor[0] === sketch.Id))) ? ((sid_11 = toolCursor[0], (u_13 = toolCursor[1], (v_13 = toolCursor[2], [u_13, v_13])))) : undefined) : undefined;
                                                        const toolLineData = buildToolPreviewLineBuffer(viewState_2.SketchUi.Tool, viewState_2.SketchUi.ToolPoints, cursorForSketch);
                                                        if (toolLineData.length > 0) {
                                                            const tlbuf = upload(toolPreviewLineSlot[0], toolPreviewLineSlot[1], toolLineData);
                                                            colorPass.setPipeline(linePipeline);
                                                            colorPass.setBindGroup(0, cameraBindGroup);
                                                            colorPass.setBindGroup(1, frameBindGroup);
                                                            colorPass.setVertexBuffer(0, tlbuf);
                                                            colorPass.draw(~~(toolLineData.length / 6));
                                                        }
                                                        const toolPointData = buildToolPreviewPointBuffer(viewState_2.SketchUi.Tool, viewState_2.SketchUi.ToolPoints, cursorForSketch);
                                                        if (toolPointData.length > 0) {
                                                            const tpbuf = upload(toolPreviewPointSlot[0], toolPreviewPointSlot[1], toolPointData);
                                                            colorPass.setPipeline(pointPipeline);
                                                            colorPass.setBindGroup(0, cameraBindGroup);
                                                            colorPass.setBindGroup(1, frameBindGroup);
                                                            colorPass.setBindGroup(2, viewportBindGroup);
                                                            colorPass.setVertexBuffer(0, pointQuadBuffer);
                                                            colorPass.setVertexBuffer(1, tpbuf);
                                                            const instanceCount = ~~(toolPointData.length / 7) | 0;
                                                            colorPass.draw(6, instanceCount);
                                                        }
                                                    }
                                                    const pointData = buildSketchPointBuffer(sketch.Id, sketch.Sketch.Entities, state_5.Compiled.Slots.Index, viewState_2.Params, viewState_2.HoveredTarget, viewState_2.SelectedTargets);
                                                    if (pointData.length > 0) {
                                                        const pointBuf = upload(sketchPointSlot[0], sketchPointSlot[1], pointData);
                                                        colorPass.setPipeline(pointPipeline);
                                                        colorPass.setBindGroup(0, cameraBindGroup);
                                                        colorPass.setBindGroup(1, frameBindGroup);
                                                        colorPass.setBindGroup(2, viewportBindGroup);
                                                        colorPass.setVertexBuffer(0, pointQuadBuffer);
                                                        colorPass.setVertexBuffer(1, pointBuf);
                                                        const instanceCount_1 = ~~(pointData.length / 7) | 0;
                                                        colorPass.draw(6, instanceCount_1);
                                                    }
                                                    const labelData = showDimensions ? buildSketchLabelBuffer(fontMetrics, resolvePointMap(state_5.Compiled.Slots.Index, viewState_2.Params, sketch.Id, sketch.Sketch.Entities), circleRadiusLookup(state_5.Compiled.Slots.Index, viewState_2.Params, sketch.Id, sketch.Sketch.Entities), sketch.Id, sketch.Sketch.Constraints, viewState_2.HoveredTarget, viewState_2.SelectedTargets) : (new Float32Array([]));
                                                    if (!labelDebugLogged) {
                                                        labelDebugLogged = true;
                                                        console.log(some((arg_4 = (length(sketch.Sketch.Constraints) | 0), (arg_5 = (~~(labelData.length / 10) | 0), toText(printf("sketch %s: %d constraints → %d label vertices"))(sketch.Id)(arg_4)(arg_5)))));
                                                        iterateIndexed((i_1, c) => {
                                                            const kind = (c.tag === 1) ? "Coincident" : ((c.tag === 2) ? "FrameCoincident" : ((c.tag === 3) ? "Concentric" : ((c.tag === 4) ? "Horizontal" : ((c.tag === 5) ? "Vertical" : ((c.tag === 6) ? toText(printf("Distance d=%.2f lp=%A"))(c.fields[2])(c.fields[3]) : ((c.tag === 7) ? toText(printf("FrameDistance d=%.2f lp=%A"))(c.fields[3])(c.fields[4]) : ((c.tag === 8) ? "Equal" : ((c.tag === 9) ? "EqualRadius" : ((c.tag === 10) ? "Midpoint" : ((c.tag === 11) ? "Parallel" : ((c.tag === 12) ? "FrameParallel" : ((c.tag === 13) ? "Perpendicular" : ((c.tag === 14) ? "FramePerpendicular" : ((c.tag === 15) ? "Tangent" : ((c.tag === 16) ? "CurveTangent" : ((c.tag === 17) ? toText(printf("CircleDiameter d=%.2f lp=%A"))(c.fields[2])(c.fields[3]) : ((c.tag === 18) ? toText(printf("LineDistance d=%.2f lp=%A"))(c.fields[6])(c.fields[7]) : ((c.tag === 19) ? toText(printf("FrameLineDistance d=%.2f lp=%A"))(c.fields[5])(c.fields[6]) : ((c.tag === 20) ? toText(printf("PointLineDistance d=%.2f lp=%A"))(c.fields[4])(c.fields[5]) : ((c.tag === 21) ? toText(printf("PointCircleDistance d=%.2f lp=%A"))(c.fields[3])(c.fields[4]) : ((c.tag === 22) ? toText(printf("LineCircleDistance d=%.2f lp=%A"))(c.fields[5])(c.fields[6]) : ((c.tag === 23) ? toText(printf("CircleCircleDistance d=%.2f lp=%A"))(c.fields[4])(c.fields[6]) : ((c.tag === 24) ? toText(printf("Angle a=%.2f lp=%A"))(c.fields[6])(c.fields[10]) : "Fixed")))))))))))))))))))))));
                                                            console.log(some(toText(printf("  c[%d] = %s"))(i_1)(kind)));
                                                        }, sketch.Sketch.Constraints);
                                                    }
                                                    if (labelData.length > 0) {
                                                        const labelBuf = upload(labelSlot[0], labelSlot[1], labelData);
                                                        colorPass.setPipeline(labelPipeline);
                                                        colorPass.setBindGroup(0, cameraBindGroup);
                                                        colorPass.setBindGroup(1, labelBindGroup);
                                                        colorPass.setVertexBuffer(0, labelBuf);
                                                        colorPass.draw(~~(labelData.length / 10));
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    finally {
                                        disposeSafe(enumerator);
                                    }
                                    const visibleFrames = filter_1((f_3) => isVisible(f_3.Id), viewState_2.Frames);
                                    const gizmoData_1 = buildFramesGizmoBuffer(visibleFrames, viewState_2.HoveredTarget, viewState_2.SelectedTargets, state_5.Doc.SelectedId);
                                    if (gizmoData_1.length > 0) {
                                        const gbuf = upload(frameGizmoSlot[0], frameGizmoSlot[1], gizmoData_1);
                                        colorPass.setPipeline(gizmoPipeline);
                                        colorPass.setBindGroup(0, cameraBindGroup);
                                        colorPass.setBindGroup(1, viewportBindGroup);
                                        colorPass.setVertexBuffer(0, gbuf);
                                        colorPass.draw(~~(gizmoData_1.length / 12));
                                    }
                                    const frameOriginData = buildFrameOriginsPointBuffer(visibleFrames, viewState_2.HoveredTarget, viewState_2.SelectedTargets);
                                    if (frameOriginData.length > 0) {
                                        const fbuf_1 = upload(frameOriginPointSlot[0], frameOriginPointSlot[1], frameOriginData);
                                        colorPass.setPipeline(worldPointPipeline);
                                        colorPass.setBindGroup(0, cameraBindGroup);
                                        colorPass.setBindGroup(1, viewportBindGroup);
                                        colorPass.setVertexBuffer(0, pointQuadBuffer);
                                        colorPass.setVertexBuffer(1, fbuf_1);
                                        const instanceCount_2 = ~~(frameOriginData.length / 8) | 0;
                                        colorPass.draw(6, instanceCount_2);
                                    }
                                    colorPass.end();
                                    const pickPass = encoder_1.beginRenderPass({
                                        colorAttachments: [{
                                            clearValue: {
                                                a: 0,
                                                b: 0,
                                                g: 0,
                                                r: 0,
                                            },
                                            loadOp: "clear",
                                            storeOp: "store",
                                            view: pickView,
                                        }],
                                        depthStencilAttachment: {
                                            depthClearValue: 1,
                                            depthLoadOp: "clear",
                                            depthStoreOp: "store",
                                            view: depthView,
                                        },
                                    });
                                    const enumerator_1 = getEnumerator(model_1.Sketches);
                                    try {
                                        while (enumerator_1["System.Collections.IEnumerator.MoveNext"]()) {
                                            const sketch_1 = enumerator_1["System.Collections.Generic.IEnumerator`1.get_Current"]();
                                            const matchValue_21 = tryFind_1(sketch_1.Id, frameById);
                                            if (matchValue_21 != null) {
                                                if (!isVisible(sketch_1.Id)) {
                                                }
                                                else {
                                                    const transform_1 = matchValue_21;
                                                    const pos_1 = transform_1.Trans;
                                                    const xAxis_3 = Quat__Rotate_Z2E054BF3(transform_1.Rot, new Vec3(1, 0, 0));
                                                    const yAxis_3 = Quat__Rotate_Z2E054BF3(transform_1.Rot, new Vec3(0, 1, 0));
                                                    device.queue.writeBuffer(frameBuffer, 0, new Float32Array(new Float32Array([pos_1.X, pos_1.Y, pos_1.Z, 0, xAxis_3.X, xAxis_3.Y, xAxis_3.Z, 0, yAxis_3.X, yAxis_3.Y, yAxis_3.Z, 0, 0, 0, 0, 0])));
                                                    const loopPickData = buildSketchLoopPickBuffer(sketch_1.Id, sketch_1.Sketch, defaultArg(map_1((l_3) => l_3.Loops, tryFind((l_2) => (l_2.SketchId === sketch_1.Id), viewState_2.SketchLoops)), empty_1()), state_5.Compiled.Slots.Index, viewState_2.Params, model_1.Pickables);
                                                    if (loopPickData.length > 0) {
                                                        const lpbuf = upload(loopPickSlot[0], loopPickSlot[1], loopPickData);
                                                        pickPass.setPipeline(loopPickPipeline);
                                                        pickPass.setBindGroup(0, cameraBindGroup);
                                                        pickPass.setBindGroup(1, frameBindGroup);
                                                        pickPass.setVertexBuffer(0, lpbuf);
                                                        pickPass.draw(~~(loopPickData.length / 3));
                                                    }
                                                    const linePickData = buildSketchPickLineBuffer(sketch_1.Id, sketch_1.Sketch.Entities, state_5.Compiled.Slots.Index, viewState_2.Params, model_1.Pickables);
                                                    if (linePickData.length > 0) {
                                                        const lbuf_1 = upload(linePickSlot[0], linePickSlot[1], linePickData);
                                                        pickPass.setPipeline(linePickPipeline);
                                                        pickPass.setBindGroup(0, cameraBindGroup);
                                                        pickPass.setBindGroup(1, frameBindGroup);
                                                        pickPass.setVertexBuffer(0, linePickCornerBuffer);
                                                        pickPass.setVertexBuffer(1, lbuf_1);
                                                        const segments = ~~(linePickData.length / 5) | 0;
                                                        pickPass.draw(6, segments);
                                                    }
                                                    const pointPickData = buildSketchPointPickBuffer(sketch_1.Id, sketch_1.Sketch.Entities, state_5.Compiled.Slots.Index, viewState_2.Params, model_1.Pickables);
                                                    if (pointPickData.length > 0) {
                                                        const pbuf_1 = upload(pointPickSlot[0], pointPickSlot[1], pointPickData);
                                                        pickPass.setPipeline(pointPickPipeline);
                                                        pickPass.setBindGroup(0, cameraBindGroup);
                                                        pickPass.setBindGroup(1, frameBindGroup);
                                                        pickPass.setBindGroup(2, viewportBindGroup);
                                                        pickPass.setVertexBuffer(0, pointQuadBuffer);
                                                        pickPass.setVertexBuffer(1, pbuf_1);
                                                        const instanceCount_3 = ~~(pointPickData.length / 4) | 0;
                                                        pickPass.draw(6, instanceCount_3);
                                                    }
                                                    const dimPickData = contains(sketch_1.Id, viewState_2.VisibleDimensionSketchIds, {
                                                        Equals: (x_6, y_6) => (x_6 === y_6),
                                                        GetHashCode: stringHash,
                                                    }) ? buildSketchDimensionPickBuffer(sketch_1.Id, sketch_1.Sketch, state_5.Compiled.Slots.Index, viewState_2.Params, model_1.Pickables) : (new Float32Array([]));
                                                    if (dimPickData.length > 0) {
                                                        const dbuf = upload(dimPickSlot[0], dimPickSlot[1], dimPickData);
                                                        pickPass.setPipeline(pointPickPipeline);
                                                        pickPass.setBindGroup(0, cameraBindGroup);
                                                        pickPass.setBindGroup(1, frameBindGroup);
                                                        pickPass.setBindGroup(2, viewportBindGroup);
                                                        pickPass.setVertexBuffer(0, pointQuadBuffer);
                                                        pickPass.setVertexBuffer(1, dbuf);
                                                        const instanceCount_4 = ~~(dimPickData.length / 4) | 0;
                                                        pickPass.draw(6, instanceCount_4);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    finally {
                                        disposeSafe(enumerator_1);
                                    }
                                    const frameOriginPickData = buildFrameOriginsPickBuffer(visibleFrames, model_1.Pickables);
                                    if (frameOriginPickData.length > 0) {
                                        const fpbuf = upload(frameOriginPickSlot[0], frameOriginPickSlot[1], frameOriginPickData);
                                        pickPass.setPipeline(worldPointPickPipeline);
                                        pickPass.setBindGroup(0, cameraBindGroup);
                                        pickPass.setBindGroup(1, viewportBindGroup);
                                        pickPass.setVertexBuffer(0, pointQuadBuffer);
                                        pickPass.setVertexBuffer(1, fpbuf);
                                        const instanceCount_5 = ~~(frameOriginPickData.length / 5) | 0;
                                        pickPass.draw(6, instanceCount_5);
                                    }
                                    const frameAxisPickData = buildFrameAxesPickBuffer(visibleFrames, model_1.Pickables, b.Eye, b.Forward, Math.tan(HALF_FOV), h_4);
                                    if (frameAxisPickData.length > 0) {
                                        const fabuf = upload(frameAxisPickSlot[0], frameAxisPickSlot[1], frameAxisPickData);
                                        pickPass.setPipeline(worldPointPickPipeline);
                                        pickPass.setBindGroup(0, cameraBindGroup);
                                        pickPass.setBindGroup(1, viewportBindGroup);
                                        pickPass.setVertexBuffer(0, pointQuadBuffer);
                                        pickPass.setVertexBuffer(1, fabuf);
                                        const instanceCount_6 = ~~(frameAxisPickData.length / 5) | 0;
                                        pickPass.draw(6, instanceCount_6);
                                    }
                                    pickPass.end();
                                    device.queue.submit([encoder_1.finish()]);
                                    submittedFrameCount = ((submittedFrameCount + 1) | 0);
                                    let write = 0;
                                    for (let i = 0; i <= (retiredBuffers.length - 1); i++) {
                                        const patternInput = retiredBuffers[i];
                                        if (patternInput[1] <= submittedFrameCount) {
                                            patternInput[0].destroy();
                                        }
                                        else {
                                            setItem(retiredBuffers, write, retiredBuffers[i]);
                                            write = ((write + 1) | 0);
                                        }
                                    }
                                    while (retiredBuffers.length > write) {
                                        retiredBuffers.splice(retiredBuffers.length - 1, 1);
                                    }
                                    window.requestAnimationFrame(frame);
                                };
                                window.requestAnimationFrame(frame);
                                const dimensionInput = document.createElement("input");
                                dimensionInput.type = "number";
                                dimensionInput.step = "any";
                                dimensionInput.style.position = "absolute";
                                dimensionInput.style.display = "none";
                                dimensionInput.style.transform = "translate(-50%, -50%)";
                                dimensionInput.style.padding = "2px 6px";
                                dimensionInput.style.border = "1px solid #b48b2b";
                                dimensionInput.style.borderRadius = "3px";
                                dimensionInput.style.background = "#fff8e4";
                                dimensionInput.style.fontFamily = "ui-monospace, monospace";
                                dimensionInput.style.fontSize = "12px";
                                dimensionInput.style.width = "72px";
                                dimensionInput.style.textAlign = "center";
                                dimensionInput.style.outline = "none";
                                dimensionInput.style.zIndex = "10";
                                container.appendChild(dimensionInput);
                                let dimensionClosing = false;
                                let dimensionEditingKey = "";
                                dimensionInput.addEventListener("mousedown", ((e_5) => {
                                    e_5.stopPropagation();
                                }));
                                dimensionInput.addEventListener("dblclick", ((e_6) => {
                                    e_6.stopPropagation();
                                }));
                                dimensionInput.addEventListener("keydown", ((e_7) => {
                                    e_7.stopPropagation();
                                    const key = e_7.key;
                                    switch (key) {
                                        case "Enter": {
                                            e_7.preventDefault();
                                            dimensionClosing = true;
                                            let parsed = 0;
                                            if (tryParse(dimensionInput.value, new FSharpRef(() => parsed, (v_14) => {
                                                parsed = v_14;
                                            }))) {
                                                dispatch(store, new Message(20, [parsed]));
                                            }
                                            else {
                                                dispatch(store, new Message(19, []));
                                            }
                                            break;
                                        }
                                        case "Escape": {
                                            e_7.preventDefault();
                                            dimensionClosing = true;
                                            dispatch(store, new Message(19, []));
                                            break;
                                        }
                                        default:
                                            undefined;
                                    }
                                }));
                                dimensionInput.addEventListener("blur", ((_arg_8) => {
                                    if (!dimensionClosing) {
                                        window.requestAnimationFrame((_arg_9) => {
                                            if (ViewerPipeline_viewerState(store.State).SketchUi.EditingDimension != null) {
                                                dimensionInput.focus();
                                                dimensionInput.select();
                                            }
                                        });
                                    }
                                }));
                                const syncDimensionEditor = () => {
                                    const state_8 = store.State;
                                    const vs_6 = ViewerPipeline_viewerState(state_8);
                                    const matchValue_23 = vs_6.SketchUi.EditingDimension;
                                    if (matchValue_23 != null) {
                                        const editing = matchValue_23;
                                        const sketchActionOpt = tryFind((a_4) => (a_4.Id === editing.SketchId), state_8.Doc.Actions);
                                        let matchResult_3, sketch_3;
                                        if (sketchActionOpt != null) {
                                            if (sketchActionOpt.Kind.tag === 11) {
                                                matchResult_3 = 0;
                                                sketch_3 = sketchActionOpt.Kind.fields[2];
                                            }
                                            else {
                                                matchResult_3 = 1;
                                            }
                                        }
                                        else {
                                            matchResult_3 = 1;
                                        }
                                        switch (matchResult_3) {
                                            case 0: {
                                                let matchValue_24;
                                                const state_7 = state_8;
                                                const sketchId_2 = editing.SketchId;
                                                const sketch_2 = sketch_3;
                                                const constraintIndex = editing.ConstraintIndex | 0;
                                                if ((constraintIndex < 0) ? true : (constraintIndex >= length(sketch_2.Constraints))) {
                                                    matchValue_24 = undefined;
                                                }
                                                else {
                                                    const c_1 = item(constraintIndex, sketch_2.Constraints);
                                                    const matchValue_22 = SketchConstraintModule_labelPos(c_1);
                                                    if (matchValue_22 == null) {
                                                        const vs_5 = ViewerPipeline_viewerState(state_7);
                                                        matchValue_24 = dimensionFallbackAnchor(resolvePointMap(state_7.Compiled.Slots.Index, vs_5.Params, sketchId_2, sketch_2.Entities), circleRadiusLookup(state_7.Compiled.Slots.Index, vs_5.Params, sketchId_2, sketch_2.Entities), c_1);
                                                    }
                                                    else {
                                                        matchValue_24 = matchValue_22;
                                                    }
                                                }
                                                if (matchValue_24 == null) {
                                                    dimensionInput.style.display = "none";
                                                }
                                                else {
                                                    const anchor = matchValue_24;
                                                    const sketchFrame = tryFind((f_4) => (f_4.Id === editing.SketchId), vs_6.SketchTransforms);
                                                    if (sketchFrame == null) {
                                                        dimensionInput.style.display = "none";
                                                    }
                                                    else {
                                                        const frameView = sketchFrame;
                                                        const xAxis_4 = Quat__Rotate_Z2E054BF3(frameView.Transform.Rot, new Vec3(1, 0, 0));
                                                        const yAxis_4 = Quat__Rotate_Z2E054BF3(frameView.Transform.Rot, new Vec3(0, 1, 0));
                                                        const world = Vec3_op_Addition_Z3F547E60(Vec3_op_Addition_Z3F547E60(frameView.Transform.Trans, Vec3_op_Multiply_ZB3DA56A(anchor.X, xAxis_4)), Vec3_op_Multiply_ZB3DA56A(anchor.Y, yAxis_4));
                                                        const matchValue_25 = worldToScreen(canvas.clientWidth, canvas.clientHeight, camera, world);
                                                        if (matchValue_25 == null) {
                                                            dimensionInput.style.display = "none";
                                                        }
                                                        else {
                                                            const sy_1 = matchValue_25[1];
                                                            const sx_1 = matchValue_25[0];
                                                            const key_1 = toText(printf("%s:%d"))(editing.SketchId)(editing.ConstraintIndex);
                                                            if (dimensionEditingKey !== key_1) {
                                                                dimensionEditingKey = key_1;
                                                                dimensionClosing = false;
                                                                dimensionInput.value = editing.Value.toString();
                                                                setTimeout((() => {
                                                                    dimensionInput.focus();
                                                                    dimensionInput.select();
                                                                }), 0);
                                                            }
                                                            dimensionInput.style.display = "";
                                                            dimensionInput.style.left = toText(printf("%fpx"))(sx_1);
                                                            dimensionInput.style.top = toText(printf("%fpx"))(sy_1);
                                                        }
                                                    }
                                                }
                                                break;
                                            }
                                            case 1: {
                                                dimensionInput.style.display = "none";
                                                break;
                                            }
                                        }
                                    }
                                    else {
                                        dimensionInput.style.display = "none";
                                        dimensionEditingKey = "";
                                        dimensionClosing = false;
                                    }
                                };
                                subscribe(store, syncDimensionEditor);
                                const positionFrame = (_arg_10) => {
                                    syncDimensionEditor();
                                    window.requestAnimationFrame(positionFrame);
                                };
                                window.requestAnimationFrame(positionFrame);
                                return Promise.resolve(container);
                            });
                        });
                    });
                }
            });
        }
        else {
            badge.textContent = "ERROR: navigator.gpu missing";
            return Promise.resolve(container);
        }
    }));
}

