module PointerMk18.Ui.Shell

open Server
open PointerMk18.Ui
open Browser.Types

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

let render
        (dispatch: Message -> unit)
        (doc: DocumentView)
        (viewerHost: HTMLElement)
        (onSave: unit -> unit)
        (onLoad: unit -> unit)
        (onExportStl: unit -> unit)
        : HTMLElement =
    let root = Dom.el "div" "ui-root"

    root.appendChild (TopBar.render dispatch onSave onLoad onExportStl :> Node) |> ignore

    let layout = Dom.el "div" "layout"
    let leftHost = Dom.el "div" "panel-host panel-host-actions"
    leftHost.appendChild (BlockList.render dispatch doc :> Node) |> ignore
    layout.appendChild (leftHost :> Node) |> ignore

    let center = Dom.el "div" "panel panel-center"
    center.appendChild (viewerHost :> Node) |> ignore
    match SketchAuthoringPanel.render dispatch doc with
    | Some overlay -> center.appendChild (overlay :> Node) |> ignore
    | None -> ()
    layout.appendChild (center :> Node) |> ignore

    root.appendChild (layout :> Node) |> ignore
    root
