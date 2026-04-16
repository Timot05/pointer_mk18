module PointerMk18.Ui.Shell

open Server
open PointerMk18.Ui
open Browser.Types

// ---------------------------------------------------------------------------
// Overall layout: top bar + three-panel layout (actions / viewport / params).
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
        : HTMLElement =
    let root = Dom.el "div" "ui-root"

    root.appendChild (TopBar.render dispatch onSave onLoad :> Node) |> ignore

    let layout = Dom.el "div" "layout"
    layout.appendChild (ActionList.render dispatch doc :> Node) |> ignore

    let center = Dom.el "div" "panel panel-center"
    center.appendChild (viewerHost :> Node) |> ignore
    match SketchOverlay.render dispatch doc with
    | Some overlay -> center.appendChild (overlay :> Node) |> ignore
    | None -> ()
    layout.appendChild (center :> Node) |> ignore

    let right = Dom.el "div" "panel"
    let rightHeader = Dom.el "div" "panel-header"
    rightHeader.appendChild (Dom.elText "h2" "" "Properties" :> Node) |> ignore
    right.appendChild (rightHeader :> Node) |> ignore
    right.appendChild (ParamsPanel.render dispatch doc :> Node) |> ignore
    layout.appendChild (right :> Node) |> ignore

    root.appendChild (layout :> Node) |> ignore
    root
