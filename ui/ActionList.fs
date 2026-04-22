module PointerMk18.Ui.ActionList

open Fable.Core
open Fable.Core.JsInterop
open Server
open PointerMk18.Ui
open Browser.Dom
open Browser.Types

// ---------------------------------------------------------------------------
// Left panel: the list of actions in the current document. Ported from
// render.ts:244–371 with the row renderer from render.ts:588–633.
// ---------------------------------------------------------------------------

// ── Action kind → display helpers ──────────────────────────────────────

let kindLabel (kind: ActionKind) : string =
    match kind with
    | Origin -> "origin"
    | Cylinder _ -> "cylinder"
    | Sphere _ -> "sphere"
    | Box _ -> "box"
    | HalfPlane _ -> "halfplane"
    | Translate _ -> "translate"
    | Rotate _ -> "rotate"
    | Move _ -> "move"
    | Union _ -> "union"
    | Subtract _ -> "subtract"
    | Intersect _ -> "intersect"
    | Sketch _ -> "sketch"
    | FromSketch _ -> "fromsketch"
    | Thicken _ -> "thicken"
    | Shell _ -> "shell"
    | Mesh _ -> "mesh"

let private kindSubtitle (kind: ActionKind) : string =
    match kind with
    | Cylinder(r, h) -> sprintf "r%g h%g" r h
    | Sphere r -> sprintf "r%g" r
    | Box(w, h, d) -> sprintf "%g\u00D7%g\u00D7%g" w h d
    | HalfPlane(axis, offset, _) -> sprintf "%s %g" axis offset
    | Translate(_, x, y, z) -> sprintf "%g, %g, %g" x y z
    | Rotate(_, _, _, _, a) -> sprintf "%g" a
    | Thicken(_, amount) -> sprintf "%g" amount
    | Shell(_, t) -> sprintf "%g" t
    | Mesh(_, size, res) -> sprintf "%g \u00D7%d" size res
    | _ -> ""

// ── Template table for the "+ Add" dropdown ────────────────────────────

let private templates : (ActionTemplate * string) list =
    [ SphereTemplate, "Sphere"
      CylinderTemplate, "Cylinder"
      BoxTemplate, "Box"
      HalfPlaneTemplate, "HalfPlane"
      TranslateTemplate, "Translate"
      RotateTemplate, "Rotate"
      MoveTemplate, "Move"
      UnionTemplate, "Union"
      SubtractTemplate, "Subtract"
      IntersectTemplate, "Intersect"
      SketchTemplate, "Sketch"
      FromSketchTemplate, "FromSketch"
      ThickenTemplate, "Thicken"
      ShellTemplate, "Shell"
      MeshTemplate, "Mesh" ]

[<Emit("Math.random().toString(36).slice(2, 8)")>]
let private randomSuffix () : string = jsNative

let private newActionId (label: string) : string =
    label.ToLower() + "_" + randomSuffix ()

// ── Action row ─────────────────────────────────────────────────────────

let private isOrigin (kind: ActionKind) =
    match kind with Origin -> true | _ -> false

let private renderRow
        (dispatch: Message -> unit)
        (doc: DocumentView)
        (action: DocAction)
        : HTMLElement =

    let selected = doc.SelectedId = Some action.Id
    let hasError = doc.Errors |> List.exists (fun e -> e.ActionId = action.Id)

    let row = Dom.el "div" "action-row"
    row.dataset?actionId <- action.Id
    if selected then row.classList.add "is-selected"
    if isOrigin action.Kind then row.classList.add "is-fixed"
    if hasError then row.classList.add "has-error"
    row.addEventListener ("click", fun _ -> dispatch (SelectAction action.Id))

    let main = Dom.el "div" "action-main"

    let icon = Dom.el "span" "action-icon"
    icon.appendChild (Icons.forKind action.Kind :> Node) |> ignore
    main.appendChild (icon :> Node) |> ignore

    let info = Dom.el "div" "action-info"
    let title = action.Name |> Option.defaultValue (kindLabel action.Kind)
    info.appendChild (Dom.elText "span" "action-title" title :> Node) |> ignore
    let sub = kindSubtitle action.Kind
    if sub <> "" then
        let subtitle = Dom.elText "span" "action-subtitle" sub
        subtitle.dataset?actionId <- action.Id
        info.appendChild (subtitle :> Node) |> ignore
    main.appendChild (info :> Node) |> ignore

    row.appendChild (main :> Node) |> ignore

    // Visibility toggle + kbd hint — Origin is always visible and has neither.
    if not (isOrigin action.Kind) then
        if selected then
            row.appendChild (Dom.kbdHintTitled "v" "Press v to toggle" :> Node) |> ignore

        let vis = Dom.el "button" "toggle-btn"
        vis.textContent <- "\u25CF"
        if action.Visible then vis.classList.add "is-active"
        vis.addEventListener (
            "click",
            fun e ->
                e.stopPropagation ()
                dispatch (ToggleActionVisible action.Id)
        )
        row.appendChild (vis :> Node) |> ignore

    row

// ── Panel ──────────────────────────────────────────────────────────────

let render (dispatch: Message -> unit) (doc: DocumentView) : HTMLElement =
    let left = Dom.el "div" "panel"
    let header = Dom.el "div" "panel-header"
    header.appendChild (Dom.elText "h2" "" "Actions" :> Node) |> ignore

    // Palette hint button (phase 5 will wire it; for now it dispatches
    // PaletteOpen so early testing works).
    let paletteBtn = Dom.el "button" "palette-hint-btn"
    paletteBtn.appendChild (Dom.elText "kbd" "" "\u2318" :> Node) |> ignore
    paletteBtn.appendChild (Dom.elText "span" "palette-hint-plus" "+" :> Node) |> ignore
    paletteBtn.appendChild (Dom.elText "kbd" "" "K" :> Node) |> ignore
    paletteBtn.appendChild (document.createTextNode " " :> Node) |> ignore
    paletteBtn.appendChild (Dom.elText "span" "" "palette" :> Node) |> ignore
    paletteBtn.addEventListener ("click", fun _ -> dispatch PaletteOpen)
    header.appendChild (paletteBtn :> Node) |> ignore

    // "+ Add" dropdown
    let addWrapper = Dom.el "div" "add-wrapper"
    let addBtn = Dom.elText "button" "btn-add" "+"
    let dropdown = Dom.el "div" "dropdown"
    dropdown?style?display <- "none"

    for (template, label) in templates do
        let item = Dom.el "button" "dropdown-item"
        item.appendChild (Icons.forTemplate template :> Node) |> ignore
        item.appendChild (Dom.elText "span" "" label :> Node) |> ignore
        item.addEventListener (
            "click",
            fun _ ->
                dropdown?style?display <- "none"
                dispatch (AddDefaultAction(template, newActionId label))
        )
        dropdown.appendChild (item :> Node) |> ignore

    addBtn.addEventListener (
        "click",
        fun e ->
            e.stopPropagation ()
            dropdown?style?display <-
                if unbox<string> (dropdown?style?display) = "none" then "flex" else "none"
    )
    document.addEventListener (
        "click",
        fun _ -> dropdown?style?display <- "none"
    )

    addWrapper.appendChild (addBtn :> Node) |> ignore
    addWrapper.appendChild (dropdown :> Node) |> ignore
    header.appendChild (addWrapper :> Node) |> ignore
    left.appendChild (header :> Node) |> ignore

    // ── List with drag-reorder ─────────────────────────────────────────
    let list = Dom.el "div" "actions-list"

    let actions = doc.Actions |> List.toArray
    let rows : HTMLElement[] = Array.zeroCreate actions.Length
    let mutable dragIndex : int option = None
    let mutable dropIndex : int option = None
    let mutable dropBefore = false

    let clearDropIndicators () =
        for r in rows do
            if not (isNull r) then
                r.classList.remove "drop-before"
                r.classList.remove "drop-after"

    for i in 0 .. actions.Length - 1 do
        let action = actions.[i]
        let row = renderRow dispatch doc action
        rows.[i] <- row

        if not (isOrigin action.Kind) then
            row?draggable <- true
            row.addEventListener (
                "dragstart",
                fun e ->
                    let de = e :?> DragEvent
                    dragIndex <- Some i
                    de.dataTransfer.effectAllowed <- "move"
                    de.dataTransfer.setData ("text/plain", string i) |> ignore
                    window.requestAnimationFrame (fun _ -> row.classList.add "is-dragging")
                    |> ignore
            )

        row.addEventListener (
            "dragover",
            fun e ->
                match dragIndex with
                | None -> ()
                | Some di when di = i ->
                    dropIndex <- None
                    clearDropIndicators ()
                | Some _ ->
                    e.preventDefault ()
                    let de = e :?> DragEvent
                    de.dataTransfer.dropEffect <- "move"
                    let rect = row.getBoundingClientRect ()
                    let before = de.clientY < rect.top + rect.height / 2.0
                    if isOrigin action.Kind && before then
                        dropIndex <- None
                        clearDropIndicators ()
                    else
                        dropBefore <- before
                        dropIndex <- Some i
                        clearDropIndicators ()
                        row.classList.add (if before then "drop-before" else "drop-after")
        )

        list.appendChild (row :> Node) |> ignore

    // Drop in empty space → append at end
    list.addEventListener (
        "dragover",
        fun e ->
            match dragIndex with
            | None -> ()
            | Some _ ->
                let de = e :?> DragEvent
                let target = de.target :?> HTMLElement
                if Option.isNone (target.closest ".action-row") then
                    e.preventDefault ()
                    de.dataTransfer.dropEffect <- "move"
                    let last = rows.Length - 1
                    if last >= 0 then
                        dropIndex <- Some last
                        dropBefore <- false
                        clearDropIndicators ()
                        rows.[last].classList.add "drop-after"
    )

    list.addEventListener (
        "drop",
        fun e ->
            e.preventDefault ()
            clearDropIndicators ()
            for r in rows do
                if not (isNull r) then r.classList.remove "is-dragging"
            match dragIndex, dropIndex with
            | Some di, Some dri ->
                let ids = actions |> Array.map (fun a -> a.Id) |> ResizeArray
                let moved = ids.[di]
                ids.RemoveAt di
                let mutable target = dri + (if dropBefore then 0 else 1)
                if di < target then target <- target - 1
                ids.Insert (target, moved)
                dragIndex <- None
                dropIndex <- None
                dispatch (ReorderActions(List.ofSeq ids))
            | _ ->
                dragIndex <- None
                dropIndex <- None
    )

    list.addEventListener (
        "dragend",
        fun _ ->
            dragIndex <- None
            dropIndex <- None
            clearDropIndicators ()
            for r in rows do
                if not (isNull r) then r.classList.remove "is-dragging"
    )

    left.appendChild (list :> Node) |> ignore
    left

let syncSubtitles (root: HTMLElement) (doc: DocumentView) : unit =
    for action in doc.Actions do
        match root.querySelector($".action-row[data-action-id=\"{action.Id}\"]") with
        | null -> ()
        | row ->
            let subtitleText = kindSubtitle action.Kind
            let existing = row.querySelector ".action-subtitle"

            if subtitleText = "" then
                if not (isNull existing) then
                    existing.remove ()
            else
                match existing with
                | null ->
                    let info = row.querySelector ".action-info"
                    if not (isNull info) then
                        let subtitle = Dom.elText "span" "action-subtitle" subtitleText
                        subtitle.dataset?actionId <- action.Id
                        info.appendChild (subtitle :> Node) |> ignore
                | existingSubtitle ->
                    existingSubtitle.textContent <- subtitleText

let syncPanel (root: HTMLElement) (dispatch: Message -> unit) (doc: DocumentView) : unit =
    match root.querySelector ".panel-host-actions" with
    | :? HTMLElement as host ->
        let prevScroll =
            match host.querySelector ".actions-list" with
            | :? HTMLElement as list -> list.scrollTop
            | _ -> 0.0
        host.innerHTML <- ""
        host.appendChild (render dispatch doc :> Node) |> ignore
        match host.querySelector ".actions-list" with
        | :? HTMLElement as list -> list.scrollTop <- prevScroll
        | _ -> ()
    | _ ->
        ()
