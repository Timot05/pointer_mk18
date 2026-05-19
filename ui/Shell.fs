module PointerMk18.Ui.Shell

open Server
open PointerMk18.Ui
open Browser.Dom
open Browser.Types
open Fable.Core.JsInterop

// ---------------------------------------------------------------------------
// Overall layout: top bar + two-panel layout (block list / viewport).
// Inline scalar / ref editors live under each block row in BlockList; there
// is no separate properties panel.
//
// The `viewerHost` is created once at app startup and re-parented on every
// render. WebGPU canvas state persists across detach/reattach, so the
// viewer keeps its pipelines and textures intact even though the shell
// wipes and rebuilds the rest of the DOM every dispatch.
// ---------------------------------------------------------------------------

// Width of the script editor column (px). Survives re-renders so a user-
// dragged size sticks across dispatches. Initial value mirrors the static
// CSS column width in styles.css.
let mutable private scriptPanelWidth = 480.0
let private minScriptPanelWidth = 200.0

let private clampScriptPanelWidth (w: float) =
    let viewport = window.innerWidth
    // Reserve 260px for the block list and at least 240px for the viewer.
    let maxW = max minScriptPanelWidth (viewport - 260.0 - 240.0)
    max minScriptPanelWidth (min maxW w)

let private applyScriptColumns (layout: HTMLElement) =
    layout?style?gridTemplateColumns <- sprintf "260px %fpx 1fr" scriptPanelWidth

let private attachScriptResize (handle: HTMLElement) (layout: HTMLElement) =
    let mutable dragging = false
    let mutable startX = 0.0
    let mutable startWidth = scriptPanelWidth

    handle.addEventListener (
        "pointerdown",
        fun e ->
            let pe = e :?> PointerEvent
            dragging <- true
            startX <- pe.clientX
            startWidth <- scriptPanelWidth
            handle.setPointerCapture pe.pointerId
            handle.classList.add "is-dragging"
            layout.classList.add "is-resizing"
            pe.preventDefault ()
    )

    handle.addEventListener (
        "pointermove",
        fun e ->
            if dragging then
                let pe = e :?> PointerEvent
                let dx = pe.clientX - startX
                scriptPanelWidth <- clampScriptPanelWidth (startWidth + dx)
                applyScriptColumns layout
    )

    let stop (e: Event) =
        if dragging then
            dragging <- false
            handle.classList.remove "is-dragging"
            layout.classList.remove "is-resizing"
            let pe = e :?> PointerEvent
            try handle.releasePointerCapture pe.pointerId with _ -> ()

    handle.addEventListener ("pointerup", stop)
    handle.addEventListener ("pointercancel", stop)

let render
        (dispatch: Message -> unit)
        (doc: DocumentView)
        (viewerHost: HTMLElement)
        (onSave: unit -> unit)
        (onLoad: unit -> unit)
        (onLoadExample: string -> unit)
        : HTMLElement =
    let root = Dom.el "div" "ui-root"

    root.appendChild (TopBar.render dispatch onSave onLoad onLoadExample doc.ScriptEditorOpen :> Node) |> ignore

    let layout = Dom.el "div" "layout"
    if doc.ScriptEditorOpen then
        layout.classList.add "has-script-editor"
    let leftHost = Dom.el "div" "panel-host panel-host-actions"
    leftHost.appendChild (BlockList.render dispatch doc :> Node) |> ignore
    layout.appendChild (leftHost :> Node) |> ignore

    // Middle column: Monaco script editor. The host element is persistent
    // (same DOM object across renders) so Monaco's internal state survives.
    if doc.ScriptEditorOpen then
        let scriptHost = Dom.el "div" "panel panel-script"
        scriptHost.appendChild (ScriptEditorPanel.render dispatch doc.ScriptSourceText :> Node) |> ignore
        let resizeHandle = Dom.el "div" "script-resize-handle"
        scriptHost.appendChild (resizeHandle :> Node) |> ignore
        attachScriptResize resizeHandle layout
        layout.appendChild (scriptHost :> Node) |> ignore
        scriptPanelWidth <- clampScriptPanelWidth scriptPanelWidth
        applyScriptColumns layout

    let center = Dom.el "div" "panel panel-center"
    center.appendChild (viewerHost :> Node) |> ignore
    match SketchAuthoringPanel.render dispatch doc with
    | Some overlay -> center.appendChild (overlay :> Node) |> ignore
    | None -> ()
    layout.appendChild (center :> Node) |> ignore

    root.appendChild (layout :> Node) |> ignore
    root
