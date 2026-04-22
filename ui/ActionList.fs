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
    let titleText = action.Name |> Option.defaultValue (kindLabel action.Kind)
    let titleSpan = Dom.elText "span" "action-title" titleText
    info.appendChild (titleSpan :> Node) |> ignore
    let sub = kindSubtitle action.Kind
    if sub <> "" then
        let subtitle = Dom.elText "span" "action-subtitle" sub
        subtitle.dataset?actionId <- action.Id
        info.appendChild (subtitle :> Node) |> ignore
    main.appendChild (info :> Node) |> ignore

    // Double-click the title to rename. Swaps the <span> for an
    // <input>; Enter commits via UpdateAction, Escape cancels, blur
    // commits (matches the behaviour of similar inline-edit fields
    // across the app).
    let beginRename () =
        let current = action.Name |> Option.defaultValue ""
        let input = document.createElement "input" :?> HTMLInputElement
        input.``type`` <- "text"
        input.className <- "action-title-edit"
        input.value <- current
        titleSpan.parentNode.replaceChild(input, titleSpan) |> ignore
        input.focus ()
        input.select ()
        let mutable finished = false
        let commit () =
            if not finished then
                finished <- true
                let trimmed = input.value.Trim()
                let nextName =
                    if trimmed = "" || trimmed = kindLabel action.Kind then None
                    else Some trimmed
                if nextName <> action.Name then
                    dispatch (UpdateAction(action.Id, { action with Name = nextName }))
        let cancel () =
            if not finished then
                finished <- true
                // Let the store-driven re-render put the original span
                // back. Swap locally as a fallback if there's no redraw.
                if not (isNull input.parentNode) then
                    input.parentNode.replaceChild(titleSpan, input) |> ignore
        input.addEventListener ("blur", fun _ -> commit ())
        input.addEventListener (
            "keydown",
            fun ev ->
                let ke = ev :?> KeyboardEvent
                match ke.key with
                | "Enter" ->
                    ev.preventDefault ()
                    ev.stopPropagation ()
                    commit ()
                    input.blur ()
                | "Escape" ->
                    ev.preventDefault ()
                    ev.stopPropagation ()
                    cancel ()
                | _ -> ev.stopPropagation ())
        input.addEventListener ("click", fun ev -> ev.stopPropagation ())
        input.addEventListener ("mousedown", fun ev -> ev.stopPropagation ())

    titleSpan.addEventListener (
        "dblclick",
        fun ev ->
            ev.preventDefault ()
            ev.stopPropagation ()
            beginRename ())

    row.appendChild (main :> Node) |> ignore

    // Eye slot on the right of the row — purely visual placement for
    // the attached eye's badge. The whole row is the drop target for
    // eye drags (see row-level listeners below); this div just keeps
    // the badge sized consistently at the far right of the row.
    let eye = doc.Eyes |> List.tryFind (fun e -> e.TargetActionId = action.Id)
    let slot = Dom.el "div" "eye-slot"

    match eye with
    | Some e ->
        let badge = Dom.el "button" "eye-badge"
        if e.TailFollowing then badge.classList.add "is-tail"
        if doc.SelectedEyeId = Some e.Id then badge.classList.add "is-selected"
        badge?draggable <- true
        badge.dataset?eyeId <- e.Id
        badge.appendChild (Icons.eye () :> Node) |> ignore
        badge.addEventListener (
            "dragstart",
            fun ev ->
                let de = ev :?> DragEvent
                de.dataTransfer.effectAllowed <- "move"
                de.dataTransfer.setData ("application/x-eye", e.Id) |> ignore
                ev.stopPropagation ())
        badge.addEventListener (
            "click",
            fun ev ->
                ev.stopPropagation ()
                let next =
                    if doc.SelectedEyeId = Some e.Id then None else Some e.Id
                dispatch (SelectEye next))
        slot.appendChild (badge :> Node) |> ignore
    | None ->
        slot.classList.add "is-empty"

    row.appendChild (slot :> Node) |> ignore

    // ── Eye drop target: the entire row ────────────────────────────
    //
    // Classify the drag by the MIME types advertised on the transfer.
    // During dragover `getData` returns "" for security, so the types
    // list is the only reliable signal. Returns "move" for an
    // existing-eye drag, "copy" for a fresh eye from the header.
    let dragKind (de: DragEvent) : string option =
        let types = de.dataTransfer.types
        let mutable kind : string option = None
        for i in 0 .. types.length - 1 do
            let t = types.[i]
            if t = "application/x-eye" then kind <- Some "move"
            elif t = "application/x-new-eye" && kind = None then kind <- Some "copy"
        kind

    row.addEventListener (
        "dragover",
        fun ev ->
            let de = ev :?> DragEvent
            match dragKind de with
            | Some "copy" when Option.isSome eye ->
                // Occupied row rejects new-eye drops (one eye per action).
                ()
            | Some effect ->
                ev.preventDefault ()
                // Short-circuit the row's action-reorder dragover path
                // below — eye drags must not leave reorder indicators.
                ev.stopPropagation ()
                de.dataTransfer.dropEffect <- effect
                row.classList.add "is-eye-drop"
            | None -> ())
    row.addEventListener (
        "dragleave",
        fun _ -> row.classList.remove "is-eye-drop")
    row.addEventListener (
        "drop",
        fun ev ->
            let de = ev :?> DragEvent
            row.classList.remove "is-eye-drop"
            let existingEyeId = de.dataTransfer.getData "application/x-eye"
            let newEye = de.dataTransfer.getData "application/x-new-eye"
            if not (System.String.IsNullOrEmpty existingEyeId) then
                ev.preventDefault ()
                ev.stopPropagation ()
                dispatch (MoveEye(existingEyeId, action.Id))
            elif not (System.String.IsNullOrEmpty newEye) && Option.isNone eye then
                ev.preventDefault ()
                ev.stopPropagation ()
                dispatch (CreateEyeFor action.Id))

    row

// ── Panel ──────────────────────────────────────────────────────────────

let render (dispatch: Message -> unit) (doc: DocumentView) : HTMLElement =
    let left = Dom.el "div" "panel"
    let header = Dom.el "div" "panel-header"
    header.appendChild (Dom.elText "h2" "" "Actions" :> Node) |> ignore

    // Right side of the header: eye source + palette button, kept
    // grouped so the eye doesn't float into the middle.
    let rightGroup = Dom.el "div" "header-right"

    // Eye source — draggable widget. Drop onto any action row to create
    // a new eye attached to that action.
    let eyeSource = Dom.el "div" "eye-source"
    eyeSource.title <- "Drag onto an action to attach an eye"
    eyeSource?draggable <- true
    eyeSource.appendChild (Icons.eye () :> Node) |> ignore
    eyeSource.addEventListener (
        "dragstart",
        fun ev ->
            let de = ev :?> DragEvent
            de.dataTransfer.effectAllowed <- "copy"
            de.dataTransfer.setData ("application/x-new-eye", "new") |> ignore)
    rightGroup.appendChild (eyeSource :> Node) |> ignore

    // Palette hint button.
    let paletteBtn = Dom.el "button" "palette-hint-btn"
    paletteBtn.appendChild (Dom.elText "kbd" "" "\u2318" :> Node) |> ignore
    paletteBtn.appendChild (Dom.elText "span" "palette-hint-plus" "+" :> Node) |> ignore
    paletteBtn.appendChild (Dom.elText "kbd" "" "K" :> Node) |> ignore
    paletteBtn.appendChild (document.createTextNode " " :> Node) |> ignore
    paletteBtn.appendChild (Dom.elText "span" "" "palette" :> Node) |> ignore
    paletteBtn.addEventListener ("click", fun _ -> dispatch PaletteOpen)
    rightGroup.appendChild (paletteBtn :> Node) |> ignore

    header.appendChild (rightGroup :> Node) |> ignore

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
