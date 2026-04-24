module Shaders

// WGSL shader sources — loaded from `viewer/Shaders/*.wgsl` via Vite's
// `?raw` import loader. The `@shaders` alias is defined in `ui/vite.config.ts`.

open Fable.Core.JsInterop

let line : string = importDefault "@shaders/Line.wgsl?raw"
let point : string = importDefault "@shaders/Point.wgsl?raw"
let gizmo : string = importDefault "@shaders/Gizmo.wgsl?raw"
let translateGizmoThick : string = importDefault "@shaders/TranslateGizmoThick.wgsl?raw"
let worldPoint : string = importDefault "@shaders/WorldPoint.wgsl?raw"
let label : string = importDefault "@shaders/Label.wgsl?raw"
let background : string = importDefault "@shaders/Background.wgsl?raw"
let pickCompute : string = importDefault "@shaders/PickCompute.wgsl?raw"
let framePickCompute : string = importDefault "@shaders/FramePickCompute.wgsl?raw"
let gizmoPickCompute : string = importDefault "@shaders/GizmoPickCompute.wgsl?raw"
