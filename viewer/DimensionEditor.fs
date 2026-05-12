module DimensionEditor

// Floating <input type="number"> overlay that appears over a dimension
// label while the user is editing it. Position is synced every frame from
// `viewState.SketchUi.EditingDimension` → world anchor → screen coords.

open Fable.Core
open Fable.Core.JsInterop
open Browser.Dom
open Browser.Types
open Server
open PointerMk18.Ui

[<Emit("$0.addEventListener($1, $2)")>]
let private addEvent (target: obj) (name: string) (h: obj -> unit) : unit = jsNative

[<Emit("$0.preventDefault()")>]
let private ePreventDefault (e: obj) : unit = jsNative

[<Emit("setTimeout($0, $1)")>]
let private setTimeout (cb: unit -> unit) (ms: int) : int = jsNative

let private dimensionAnchorForSketch
        (state: EditorState)
        (sketchId: ActionId)
        (sketch: ActionSketch)
        (constraintIndex: int) : LabelPos option =
    if constraintIndex < 0 || constraintIndex >= sketch.Constraints.Length then
        None
    else
        let c = sketch.Constraints.[constraintIndex]
        match SketchConstraint.labelPos c with
        | Some lp -> Some lp
        | None ->
            let vs = ViewerPipeline.viewerState state
            let points =
                SketchOverlayRender.resolvePointMap
                    state.Compiled.Slots.Index vs.Params sketchId sketch.Entities
            let radiusOf =
                SketchOverlayRender.circleRadiusLookup
                    state.Compiled.Slots.Index vs.Params sketchId sketch.Entities
            SketchOverlayRender.dimensionFallbackAnchor points radiusOf c

/// Install the dimension editor into the viewer container. Self-wires to
/// the app store: every state change updates the input's visibility, value,
/// and position.
let install
        (container: HTMLElement)
        (canvas: HTMLCanvasElement)
        (camera: Camera.CameraState) : unit =
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
    // Track the current field's unit so commit/load convert consistently.
    // Angle constraints are stored in radians but rendered/edited in degrees
    // (matches `LabelBuilder.formatAngle`'s `radians * 180 / π` display).
    let mutable dimensionIsAngle = false

    let radToDeg (r: float) = r * 180.0 / System.Math.PI
    let degToRad (d: float) = d * System.Math.PI / 180.0

    // Stop clicks inside the input from bubbling to the canvas pick/drag.
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
                let stored = if dimensionIsAngle then degToRad parsed else parsed
                Store.dispatch AppStore.store (CommitEditingDimension stored)
            else
                Store.dispatch AppStore.store CancelEditingDimension
        | "Escape" ->
            ePreventDefault e
            dimensionClosing <- true
            Store.dispatch AppStore.store CancelEditingDimension
        | _ -> ())

    addEvent dimensionInput "blur" (fun _ ->
        // Don't dispatch during blur — that triggers a render which may
        // remove the input from the DOM while the browser is mid-focus-
        // change. Refocus on the next frame instead.
        if not dimensionClosing then
            WebGPU.requestAnimationFrame (fun _ ->
                let state = AppStore.store.State
                let vs = ViewerPipeline.viewerState state
                if vs.SketchUi.EditingDimension.IsSome then
                    dimensionInput?focus() |> ignore
                    dimensionInput?select() |> ignore)
            |> ignore)

    let hide () = dimensionInput?style?display <- "none"

    let sync () =
        let state = AppStore.store.State
        let vs = ViewerPipeline.viewerState state
        match vs.SketchUi.EditingDimension with
        | None ->
            hide ()
            dimensionEditingKey <- ""
            dimensionClosing <- false
        | Some editing ->
            let sketchOpt =
                match SketchAuthoring.trySelectedSketch state.Doc with
                | Some ctx when ctx.Id = editing.SketchId -> Some ctx.Sketch
                | _ -> None
            match sketchOpt with
            | Some sketch ->
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
                        match Camera.worldToScreen canvas.clientWidth canvas.clientHeight camera world with
                        | Some (sx, sy) ->
                            let key = sprintf "%s:%d" editing.SketchId editing.ConstraintIndex
                            if dimensionEditingKey <> key then
                                dimensionEditingKey <- key
                                dimensionClosing <- false
                                dimensionIsAngle <- (editing.Key = "angle")
                                let displayValue =
                                    if dimensionIsAngle then radToDeg editing.Value
                                    else editing.Value
                                dimensionInput?value <- sprintf "%g" displayValue
                                setTimeout
                                    (fun () ->
                                        dimensionInput?focus() |> ignore
                                        dimensionInput?select() |> ignore) 0
                                |> ignore
                            dimensionInput?style?display <- ""
                            dimensionInput?style?left <- sprintf "%fpx" sx
                            dimensionInput?style?top <- sprintf "%fpx" sy
                        | None -> hide ()
                    | None -> hide ()
                | None -> hide ()
            | _ -> hide ()

    Store.subscribe AppStore.store sync
    // Also reposition every frame (camera moves, sketch drags).
    let rec positionFrame (_: float) =
        sync ()
        WebGPU.requestAnimationFrame positionFrame |> ignore
    WebGPU.requestAnimationFrame positionFrame |> ignore
