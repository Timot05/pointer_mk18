module Shaders

// WGSL shader sources — loaded from `viewer/Shaders/*.wgsl` via Vite's
// `?raw` import loader. The `@shaders` alias is defined in `ui/vite.config.ts`.

open Fable.Core.JsInterop

let line : string = importDefault "@shaders/Line.wgsl?raw"
let point : string = importDefault "@shaders/Point.wgsl?raw"
let gizmo : string = importDefault "@shaders/Gizmo.wgsl?raw"
let worldPoint : string = importDefault "@shaders/WorldPoint.wgsl?raw"
let worldPointPick : string = importDefault "@shaders/WorldPointPick.wgsl?raw"
let label : string = importDefault "@shaders/Label.wgsl?raw"
let loopPick : string = importDefault "@shaders/LoopPick.wgsl?raw"
let linePick : string = importDefault "@shaders/LinePick.wgsl?raw"
let pointPick : string = importDefault "@shaders/PointPick.wgsl?raw"
let background : string = importDefault "@shaders/Background.wgsl?raw"
