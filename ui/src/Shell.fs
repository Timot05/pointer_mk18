module PointerMk18.Ui.Shell

open Server
open PointerMk18.Ui
open Browser.Types

// ---------------------------------------------------------------------------
// Overall layout: top bar + main layout (left panel only for Phase 2 —
// center viewport and right panel land in later phases).
// ---------------------------------------------------------------------------

let render (dispatch: Message -> unit) (doc: DocumentView) : HTMLElement =
    let root = Dom.el "div" "ui-root"

    root.appendChild (TopBar.render dispatch :> Node) |> ignore

    let layout = Dom.el "div" "layout"
    layout.appendChild (ActionList.render dispatch doc :> Node) |> ignore

    // Center: placeholder for the WebGPU viewport (wired in Phase 7),
    // plus the sketch-authoring overlay when edit mode is active.
    let center = Dom.el "div" "panel panel-center"
    center.appendChild (Dom.elText "div" "viewport-placeholder" "(viewport — Phase 7)" :> Node) |> ignore
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
