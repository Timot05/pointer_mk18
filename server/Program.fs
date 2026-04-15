namespace Server

open System
open System.Text.Json
open System.Text.Json.Serialization
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection

module Program =

    let jsonOpts =
        let o = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
        o.Converters.Add(
            JsonFSharpConverter(
                JsonUnionEncoding.InternalTag ||| JsonUnionEncoding.NamedFields ||| JsonUnionEncoding.UnwrapOption,
                unionTagName = "case",
                unionFieldNamingPolicy = JsonNamingPolicy.CamelCase
            )
        )
        o

    let mutable doc = Document.defaultDocument ()
    let mutable compiled = Pipeline.compile doc.Actions
    let mutable paletteSession = Palette.empty
    let mutable hoveredTarget : SelectionTarget option = None
    let mutable selectedTargets : SelectionTarget list = []
    let mutable sketchEditMode = false
    let mutable sketchTool = "none"
    let mutable sketchToolPoints : LabelPos list = []
    let mutable editingDimension : EditingDimension option = None
    let mutable constraintPlacementMode : string option = None
    let mutable constraintPlacementDraft : ConstraintPlacementDraft option = None
    let mutable constraintPlacementCursor : (string * LabelPos) option = None

    let effectivePlacementTargets () =
        match hoveredTarget with
        | Some target when selectedTargets |> List.contains target |> not -> selectedTargets @ [ target ]
        | _ -> selectedTargets

    let activeSketchEditId () =
        match sketchEditMode, doc.SelectedId with
        | true, Some selectedId ->
            match doc.Actions |> List.tryFind (fun a -> a.Id = selectedId) with
            | Some { Kind = Sketch _ } -> Some selectedId
            | _ -> None
        | _ -> None

    let sketchEditFrames () =
        match activeSketchEditId () with
        | None -> []
        | Some sketchId ->
            let sketchIndex = doc.Actions |> List.findIndex (fun a -> a.Id = sketchId)
            doc.Actions
            |> List.take sketchIndex
            |> List.choose (fun a ->
                match Map.tryFind a.Id compiled.TypeMap with
                | Some FieldType.Frame ->
                    Map.tryFind a.Id compiled.Frames
                    |> Option.map (fun t -> {| Id = a.Id; Transform = t |})
                | _ -> None)

    let isAllowedSketchEditFrameTarget =
        let allowedFrameIds () =
            sketchEditFrames () |> List.map (fun f -> f.Id) |> Set.ofList
        function
        | TargetFrameOrigin(frameId) ->
            Set.contains frameId (allowedFrameIds ())
        | _ -> false

    let isValidSelectionTarget target =
        match target with
        | TargetFrameOrigin _ -> isAllowedSketchEditFrameTarget target
        | _ -> compiled.Pickables |> List.exists (Pickable.sameTarget target)

    let trySketchContext (sketchId: string) =
        doc.Actions
        |> List.tryFind (fun action -> action.Id = sketchId)
        |> Option.bind (fun action ->
            match action.Kind with
            | Sketch(origin, sketch) ->
                let sketchOrigin =
                    origin
                    |> Option.bind (fun id -> Map.tryFind id compiled.Frames)
                    |> Option.defaultValue RigidTransform.Identity
                Some(sketch, sketchOrigin)
            | _ -> None)

    let tryPoint2 (sketch: ActionSketch) (pointId: string) =
        sketch.Entities
        |> List.tryPick (function
            | REPoint(id, x, y) when id = pointId -> Some(x, y)
            | _ -> None)

    let tryFrameOrigin2 (sketchOrigin: RigidTransform) (frameId: string) =
        Map.tryFind frameId compiled.Frames
        |> Option.map (fun frameT ->
            let local = sketchOrigin.Inverse.Apply frameT.Trans
            local.X, local.Y)

    let tryLine2 (sketch: ActionSketch) (startId: string) (endId: string) =
        match tryPoint2 sketch startId, tryPoint2 sketch endId with
        | Some(a), Some(b) -> Some(a, b)
        | _ -> None

    let pointLineDistance ((px, py): float * float) ((ax, ay): float * float) ((bx, by): float * float) =
        let dx = bx - ax
        let dy = by - ay
        let len = sqrt (dx * dx + dy * dy)
        if len < 1e-9 then 0.0
        else abs ((dx * (py - ay) - dy * (px - ax)) / len)

    let pointDistance ((ax, ay): float * float) ((bx, by): float * float) =
        let dx = bx - ax
        let dy = by - ay
        sqrt (dx * dx + dy * dy)

    let withResolvedPendingConstraintValue (state: SketchUiState) =
        let resolved =
            state.PendingConstraintPlacement
            |> Option.bind (fun pending ->
                trySketchContext pending.SketchId
                |> Option.map (fun (sketch, sketchOrigin) ->
                    let nextConstraint =
                        match pending.Constraint with
                        | FrameDistance(pointId, frameId, "origin", _distance, lp) ->
                            match tryPoint2 sketch pointId, tryFrameOrigin2 sketchOrigin frameId with
                            | Some p, Some fp -> FrameDistance(pointId, frameId, "origin", pointDistance p fp, lp)
                            | _ -> pending.Constraint
                        | FrameLineDistance(lineId, aStart, aEnd, frameId, "origin", _distance, lp) ->
                            match tryLine2 sketch aStart aEnd, tryFrameOrigin2 sketchOrigin frameId with
                            | Some(a, b), Some fp -> FrameLineDistance(lineId, aStart, aEnd, frameId, "origin", pointLineDistance fp a b, lp)
                            | _ -> pending.Constraint
                        | _ -> pending.Constraint
                    { pending with Constraint = nextConstraint }))
        { state with PendingConstraintPlacement = resolved }

    let sketchUiState () =
        let baseState =
            let placementCursor =
                match constraintPlacementCursor, doc.SelectedId with
                | Some(sketchId, position), Some selectedId when sketchEditMode && selectedId = sketchId -> Some(position.X, position.Y)
                | _ -> None
            let placementTargets =
                match constraintPlacementMode with
                | Some _ -> effectivePlacementTargets ()
                | None -> selectedTargets
            SketchAuthoring.availabilityForSelection doc sketchEditMode sketchTool constraintPlacementMode placementTargets placementCursor constraintPlacementDraft hoveredTarget
        { baseState with
            ToolPoints = if baseState.Tool = "none" then [] else sketchToolPoints
            EditingDimension = editingDimension }
        |> withResolvedPendingConstraintValue

    let normalizeSketchUiState () =
        let next = sketchUiState ()
        sketchEditMode <- next.EditMode
        sketchTool <- next.Tool
        sketchToolPoints <- if next.Tool = "none" then [] else sketchToolPoints
        editingDimension <-
            editingDimension
            |> Option.bind (fun current ->
                match SketchAuthoring.trySelectedSketch doc with
                | Some selected when sketchEditMode && selected.Action.Id = current.SketchId ->
                    SketchAuthoring.tryEditableDimension current.SketchId selected.Sketch current.ConstraintIndex
                | _ -> None)
        constraintPlacementCursor <-
            match constraintPlacementMode, constraintPlacementCursor, doc.SelectedId with
            | Some _, Some(sketchId, pos), Some selectedId when sketchEditMode && sketchTool = "none" && selectedId = sketchId -> Some(sketchId, pos)
            | _ -> None
        constraintPlacementDraft <-
            match constraintPlacementMode, constraintPlacementDraft, doc.SelectedId with
            | Some kind, Some draft, Some selectedId when sketchEditMode && sketchTool = "none" && draft.SketchId = selectedId && draft.Kind = kind -> Some draft
            | _ -> None
        constraintPlacementMode <- next.ConstraintPlacementMode

    let recompile () =
        compiled <- Pipeline.compile doc.Actions
        hoveredTarget <-
            hoveredTarget
            |> Option.filter isValidSelectionTarget
        selectedTargets <-
            selectedTargets
            |> List.filter isValidSelectionTarget
        normalizeSketchUiState ()

    let clearEditorTransientState () =
        hoveredTarget <- None
        selectedTargets <- []
        sketchToolPoints <- []
        editingDimension <- None
        constraintPlacementDraft <- None
        constraintPlacementCursor <- None

    let applyDeleteIntent () =
        if sketchEditMode then
            match SketchAuthoring.trySelectedSketch doc with
            | Some ctx when not selectedTargets.IsEmpty ->
                doc <- SketchAuthoring.withUpdatedSketch doc ctx.Action.Id (SketchAuthoring.deleteTargets selectedTargets ctx.Sketch)
                clearEditorTransientState ()
                recompile ()
                "model"
            | _ ->
                "state"
        else
            match doc.SelectedId with
            | Some id when id <> "origin" ->
                clearEditorTransientState ()
                doc <- Document.removeAction id doc
                recompile ()
                "model"
            | _ ->
                "state"

    let formatErrors (errs: TypeError list) =
        errs |> List.map (fun e ->
            match e with
            | MissingRef(id, key) -> {| ActionId = id; Key = key; Error = "missing" |}
            | RefNotFound(id, key, target) -> {| ActionId = id; Key = key; Error = $"not found: {target}" |}
            | ForwardRef(id, key, target) -> {| ActionId = id; Key = key; Error = $"forward ref: {target}" |}
            | TypeMismatch(id, key, expected, got) ->
                let exp = expected |> List.map string |> String.concat "|"
                {| ActionId = id; Key = key; Error = $"expected {exp}, got {got}" |})

    let json_payload () =
        let tm = compiled.TypeMap
        let errors = formatErrors compiled.Errors

        let refOptions =
            match doc.SelectedId with
            | None -> Map.empty
            | Some selId ->
                match doc.Actions |> List.tryFind (fun a -> a.Id = selId) with
                | None -> Map.empty
                | Some sel ->
                    let selIdx = doc.Actions |> List.findIndex (fun a -> a.Id = selId)
                    let before = doc.Actions |> List.take selIdx
                    let accepted = TypeCheck.acceptedInputs sel.Kind
                    accepted |> Map.map (fun _key types ->
                        before
                        |> List.choose (fun a ->
                            match Map.tryFind a.Id tm with
                            | Some t when List.contains t types -> Some a.Id
                            | _ -> None))

        // Field-type actions always get display settings; others get None
        let actions =
            doc.Actions
            |> List.map (fun a ->
                match Map.tryFind a.Id tm with
                | Some FieldType.Field ->
                    { a with
                        Display = Some (a.Display |> Option.defaultValue DisplaySettings.defaults)
                        FieldSlice = Some (a.FieldSlice |> Option.defaultValue FieldSliceSettings.defaults) }
                | _ ->
                    { a with Display = None; FieldSlice = None })

        {| Name = doc.Name; Actions = actions; SelectedId = doc.SelectedId
           SelectedTargets = selectedTargets
           SketchUi = sketchUiState ()
           RefOptions = refOptions; Errors = errors |}

    let json () =
        Results.Content(JsonSerializer.Serialize(json_payload (), jsonOpts), "application/json")

    let paletteJson () =
        let state = Palette.toState paletteSession compiled.TypeMap doc
        Results.Content(JsonSerializer.Serialize(state, jsonOpts), "application/json")

    let paletteAndDoc () =
        let state = Palette.toState paletteSession compiled.TypeMap doc
        let combined = {| Palette = state; Document = json_payload () |}
        Results.Content(JsonSerializer.Serialize(combined, jsonOpts), "application/json")

    let mutate f =
        doc <- f doc
        recompile ()
        json ()

    let mutateSilent f =
        doc <- f doc
        recompile ()
        Results.NoContent()

    // ── Viewer state (slot values + display settings only; no topology) ──
    let viewerStatePayload () =
        let dragTarget =
            let isActiveSketchTarget =
                function
                | TargetPoint(sketchId, _)
                | TargetDimension(sketchId, _) ->
                    sketchEditMode && doc.SelectedId = Some sketchId
                | _ -> false
            hoveredTarget |> Option.filter isActiveSketchTarget

        let highlightedTargetAllowed =
            let frameHighlightAllowed () =
                match constraintPlacementMode with
                | Some "angle" -> false
                | _ -> true
            function
            | TargetPoint(sketchId, _)
            | TargetLine(sketchId, _)
            | TargetCircle(sketchId, _)
            | TargetArc(sketchId, _)
            | TargetLoop(sketchId, _)
            | TargetDimension(sketchId, _) ->
                sketchEditMode && doc.SelectedId = Some sketchId
            | TargetFrameOrigin _
                as target ->
                frameHighlightAllowed () && (activeSketchEditId ()).IsSome && isAllowedSketchEditFrameTarget target
            | TargetFrameAxis _ ->
                false
            | TargetSurface _ ->
                true

        let highlightedTarget = hoveredTarget |> Option.filter highlightedTargetAllowed
        let highlightedTargets = selectedTargets |> List.filter highlightedTargetAllowed
        let visibleDimensionSketchIds =
            match sketchEditMode, doc.SelectedId with
            | true, Some selectedId ->
                match doc.Actions |> List.tryFind (fun a -> a.Id = selectedId) with
                | Some { Kind = Sketch _ } -> [ selectedId ]
                | _ -> []
            | _ -> []

        // Per-action display & field-slice settings, for actions where they
        // apply. Slots ARE in Slots.Values, but sending the original
        // settings is simpler for the UI right now.
        let displayByAction =
            doc.Actions
            |> List.choose (fun a ->
                match Map.tryFind a.Id compiled.TypeMap with
                | Some FieldType.Field ->
                    let d = a.Display |> Option.defaultValue DisplaySettings.defaults
                    let fs = a.FieldSlice |> Option.defaultValue FieldSliceSettings.defaults
                    Some (a.Id, {| Display = d; FieldSlice = fs |})
                | _ -> None)
            |> Map.ofList

        let frames =
            doc.Actions
            |> List.choose (fun a ->
                match Map.tryFind a.Id compiled.TypeMap with
                | Some FieldType.Frame ->
                    Map.tryFind a.Id compiled.Frames
                    |> Option.map (fun t -> {| Id = a.Id; Transform = t |})
                | _ -> None)
        let sketchEditFrames = sketchEditFrames ()

        let sketchFrames =
            doc.Actions
            |> List.choose (fun a ->
                match a.Kind with
                | Sketch(origin, _sk) ->
                    let sketchOrigin =
                        origin
                        |> Option.bind (fun id -> Map.tryFind id compiled.Frames)
                        |> Option.defaultValue RigidTransform.Identity
                    Some {| Id = a.Id; Transform = sketchOrigin |}
                | _ -> None)

        let visibleByAction =
            doc.Actions
            |> List.map (fun a -> a.Id, a.Visible)
            |> Map.ofList

        let constraintLabelPositions =
            doc.Actions
            |> List.choose (fun a ->
                match a.Kind with
                | Sketch(_, sk) ->
                    sk.Constraints
                    |> List.mapi (fun i c ->
                        let lp =
                            match c with
                            | Distance(_, _, _, lp)
                            | FrameDistance(_, _, _, _, lp)
                            | LineDistance(_, _, _, _, _, _, _, lp)
                            | FrameLineDistance(_, _, _, _, _, _, lp)
                            | PointLineDistance(_, _, _, _, _, lp)
                            | PointCircleDistance(_, _, _, _, lp)
                            | LineCircleDistance(_, _, _, _, _, _, lp)
                            | CircleCircleDistance(_, _, _, _, _, _, lp)
                            | CircleDiameter(_, _, _, lp)
                            | Angle(_, _, _, _, _, _, _, _, _, _, lp) -> lp
                            | _ -> None
                        lp |> Option.map (fun pos -> {| SketchId = a.Id; ConstraintIndex = i; Position = pos |}))
                    |> List.choose id
                    |> Some
                | _ -> None)
            |> List.concat

        {| Params = compiled.Slots.Values
           SelectedId = doc.SelectedId
           HoveredTarget = hoveredTarget
           HighlightedTarget = highlightedTarget
           DragTarget = dragTarget
           SelectedTargets = selectedTargets
           HighlightedTargets = highlightedTargets
           VisibleDimensionSketchIds = visibleDimensionSketchIds
           SketchUi = sketchUiState ()
           Frames = frames
           SketchEditFrames = sketchEditFrames
           SketchFrames = sketchFrames
           Visible = visibleByAction
           ConstraintLabelPositions = constraintLabelPositions
           Display = displayByAction
           Errors = formatErrors compiled.Errors |}

    let viewerStateResult () =
        Results.Content(JsonSerializer.Serialize(viewerStatePayload (), jsonOpts), "application/json")

    let viewerInvalidationForParam (id: string) (key: string) =
        let ref = { ActionId = id; Path = key }
        if Map.containsKey ref compiled.Slots.Index then "state" else "model"

    let withViewerInvalidation (kind: string) (result: IResult) : IResult =
        { new IResult with
            member _.ExecuteAsync(ctx) =
                ctx.Response.Headers.Append("X-Viewer-Invalidation", kind)
                result.ExecuteAsync(ctx) }

    // ── Fast-path: try to update a single slot without recompiling ────────
    // Returns true if the slot update succeeded (no topology change needed).
    let tryFastSlotUpdate (id: string) (key: string) (value: JsonElement) : bool =
        if value.ValueKind <> JsonValueKind.Number then false
        else
            let ref = { ActionId = id; Path = key }
            if SlotTable.update compiled.Slots ref (value.GetDouble()) then
                // Also update doc.Actions so the source of truth stays consistent
                doc <- Document.patchParam id key value doc
                true
            else false

    let readBody<'T> (ctx: HttpContext) =
        let body = ctx.Request.ReadFromJsonAsync<JsonElement>().Result
        JsonSerializer.Deserialize<'T>(body.GetRawText(), jsonOpts)

    [<EntryPoint>]
    let main args =
        let builder = WebApplication.CreateBuilder(args)

        builder.Services.AddCors(fun opts ->
            opts.AddDefaultPolicy(fun p ->
                p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod() |> ignore))
        |> ignore

        let app = builder.Build()
        app.UseCors() |> ignore

        app.MapGet("/api/document", Func<IResult>(fun () -> json ())) |> ignore

        // ── Viewer endpoints ──────────────────────────────────────────
        // /model: topology (rarely changes — structural mutations only).
        //         Includes the SlotRef→Slot index so the client can map
        //         (actionId, key) references to slot numbers.
        // /state: slot values + display settings (changes every drag).
        app.MapGet("/api/viewer/model",
            Func<IResult>(fun () ->
                let indexList =
                    compiled.Slots.Index
                    |> Map.toList
                    |> List.map (fun (r, s) -> {| ActionId = r.ActionId; Path = r.Path; Slot = s |})
                let sketches =
                    doc.Actions
                    |> List.choose (fun a ->
                        match a.Kind with
                        | Sketch(origin, sk) ->
                            let sketchOrigin =
                                origin
                                |> Option.bind (fun id -> Map.tryFind id compiled.Frames)
                                |> Option.defaultValue RigidTransform.Identity
                            let ctx : SketchCompileContext =
                                { SketchOrigin = sketchOrigin; Frames = compiled.Frames }
                            let graph = SketchCompile.compile sk ctx
                            let loops =
                                SketchLoops.detectLoops sk.Entities
                                |> List.map (fun l -> {| Id = l.Id; EntityIds = l.EntityIds |})
                            Some {| Id = a.Id; Origin = origin; Sketch = sk; Graph = graph; Loops = loops |}
                        | _ -> None)
                let payload =
                    {| Surfaces = compiled.Surfaces
                       Sketches = sketches
                       NumSlots = compiled.Slots.Values.Length
                       SlotIndex = indexList
                       Pickables = compiled.Pickables |}
                Results.Content(JsonSerializer.Serialize(payload, jsonOpts), "application/json"))) |> ignore

        app.MapGet("/api/viewer/state",
            Func<IResult>(fun () -> viewerStateResult ())) |> ignore

        let readPickCandidates (body: JsonElement) =
            let mutable candidatesEl = Unchecked.defaultof<JsonElement>
            if body.TryGetProperty("candidates", &candidatesEl) then
                candidatesEl.EnumerateArray()
                |> Seq.choose (fun item ->
                    let mutable pickIdEl = Unchecked.defaultof<JsonElement>
                    let mutable scoreEl = Unchecked.defaultof<JsonElement>
                    if item.TryGetProperty("pickId", &pickIdEl) && item.TryGetProperty("score", &scoreEl) then
                        Some { PickId = pickIdEl.GetInt32(); Score = float32 (scoreEl.GetDouble()) }
                    else None)
                |> Seq.toList
            else []

        let readPickIntent (body: JsonElement) =
            let mutable intentEl = Unchecked.defaultof<JsonElement>
            if body.TryGetProperty("intent", &intentEl) then
                match intentEl.GetString() with
                | "toggle" -> "toggle"
                | _ -> "replace"
            else
                "replace"

        let applySelectionIntent intent target current =
            match intent with
            | "toggle" ->
                if current |> List.exists ((=) target) then
                    current |> List.filter ((<>) target)
                else
                    target :: current
            | _ ->
                [ target ]

        let reduceSelectionCandidates pickCandidates =
            let pickableById = compiled.Pickables |> List.map (fun p -> Pickable.pickId p, p) |> Map.ofList
            pickCandidates
            |> List.choose (fun candidate ->
                Map.tryFind candidate.PickId pickableById
                |> Option.map (fun pickable ->
                    Pickable.selectionTarget pickable, candidate.Score, Some(Pickable.targetAction pickable))
                |> Option.filter (fun (target, _score, _action) -> isValidSelectionTarget target))
            |> List.sortBy (fun (target, score, _action) -> Pickable.selectionPriority target, score)
            |> List.tryHead

        app.MapPost("/api/viewer/hover",
            Func<HttpContext, IResult>(fun ctx ->
                let body = ctx.Request.ReadFromJsonAsync<JsonElement>().Result
                let candidates = readPickCandidates body
                hoveredTarget <-
                    reduceSelectionCandidates candidates
                    |> Option.map (fun (target, _score, _action) -> target)
                viewerStateResult ())) |> ignore

        app.MapPost("/api/viewer/placement-cursor",
            Func<HttpContext, IResult>(fun ctx ->
                let body = ctx.Request.ReadFromJsonAsync<JsonElement>().Result
                let mutable sketchIdEl = Unchecked.defaultof<JsonElement>
                let mutable xEl = Unchecked.defaultof<JsonElement>
                let mutable yEl = Unchecked.defaultof<JsonElement>
                if body.TryGetProperty("sketchId", &sketchIdEl)
                    && body.TryGetProperty("x", &xEl)
                    && body.TryGetProperty("y", &yEl) then
                    constraintPlacementCursor <- Some(sketchIdEl.GetString(), { X = xEl.GetDouble(); Y = yEl.GetDouble() })
                else
                    constraintPlacementCursor <- None
                viewerStateResult ())) |> ignore

        app.MapPost("/api/viewer/pick",
            Func<HttpContext, IResult>(fun ctx ->
                let body = ctx.Request.ReadFromJsonAsync<JsonElement>().Result
                let candidates = readPickCandidates body
                let intent = readPickIntent body
                match reduceSelectionCandidates candidates with
                | Some(target, _score, actionId) ->
                    hoveredTarget <- Some target
                    selectedTargets <- applySelectionIntent intent target selectedTargets
                    match actionId with
                    | Some id -> doc <- Document.select id doc
                    | None -> ()
                    recompile ()
                    viewerStateResult ()
                | None ->
                    hoveredTarget <- None
                    if intent = "replace" then selectedTargets <- []
                    viewerStateResult ())) |> ignore

        app.MapPut("/api/document/select/{id}",
            Func<string, IResult>(fun id ->
                hoveredTarget <- None
                selectedTargets <- []
                sketchToolPoints <- []
                mutate (Document.select id))) |> ignore

        app.MapPost("/api/sketch-ui/edit/toggle",
            Func<IResult>(fun () ->
                sketchEditMode <- not sketchEditMode
                if not sketchEditMode then
                    sketchTool <- "none"
                    sketchToolPoints <- []
                    editingDimension <- None
                    constraintPlacementMode <- None
                    constraintPlacementDraft <- None
                    constraintPlacementCursor <- None
                normalizeSketchUiState ()
                withViewerInvalidation "state" (json ()))) |> ignore

        app.MapPut("/api/sketch-ui/tool",
            Func<HttpContext, IResult>(fun ctx ->
                let body = ctx.Request.ReadFromJsonAsync<JsonElement>().Result
                let tool = body.GetProperty("tool").GetString()
                sketchEditMode <- true
                sketchTool <- if String.IsNullOrWhiteSpace(tool) then "none" else tool
                sketchToolPoints <- []
                editingDimension <- None
                constraintPlacementMode <- None
                constraintPlacementDraft <- None
                constraintPlacementCursor <- None
                normalizeSketchUiState ()
                withViewerInvalidation "state" (json ()))) |> ignore

        app.MapPost("/api/sketch-ui/constraint-placement/toggle",
            Func<HttpContext, IResult>(fun ctx ->
                let body = ctx.Request.ReadFromJsonAsync<JsonElement>().Result
                let kind = body.GetProperty("kind").GetString()
                sketchEditMode <- true
                sketchTool <- "none"
                sketchToolPoints <- []
                editingDimension <- None
                constraintPlacementDraft <- None
                constraintPlacementMode <-
                    match constraintPlacementMode with
                    | Some active when active = kind -> None
                    | _ -> Some kind
                constraintPlacementCursor <- None
                normalizeSketchUiState ()
                withViewerInvalidation "state" (json ()))) |> ignore

        app.MapPost("/api/sketch-ui/add-constraint",
            Func<HttpContext, IResult>(fun ctx ->
                let body = ctx.Request.ReadFromJsonAsync<JsonElement>().Result
                let kind = body.GetProperty("kind").GetString()
                match SketchAuthoring.addConstraintFromSelection doc selectedTargets kind with
                | Some nextDoc ->
                    doc <- nextDoc
                    hoveredTarget <- None
                    selectedTargets <- []
                    sketchToolPoints <- []
                    editingDimension <- None
                    constraintPlacementDraft <- None
                    recompile ()
                    withViewerInvalidation "model" (json ())
                | None ->
                    withViewerInvalidation "state" (json ()))) |> ignore

        app.MapDelete("/api/sketch-ui/constraint/{index}",
            Func<int, IResult>(fun index ->
                match SketchAuthoring.trySelectedSketch doc with
                | Some ctx ->
                    doc <- SketchAuthoring.withUpdatedSketch doc ctx.Action.Id (SketchAuthoring.removeConstraintAt index ctx.Sketch)
                    hoveredTarget <- None
                    selectedTargets <- []
                    sketchToolPoints <- []
                    editingDimension <- None
                    constraintPlacementDraft <- None
                    recompile ()
                    withViewerInvalidation "model" (json ())
                | None ->
                    withViewerInvalidation "state" (json ()))) |> ignore

        app.MapPost("/api/editor/delete",
            Func<IResult>(fun () ->
                let invalidation = applyDeleteIntent ()
                withViewerInvalidation invalidation (json ()))) |> ignore

        app.MapPost("/api/sketch-ui/dimension-edit/start",
            Func<HttpContext, IResult>(fun ctx ->
                let body = ctx.Request.ReadFromJsonAsync<JsonElement>().Result
                let index = body.GetProperty("constraintIndex").GetInt32()
                editingDimension <-
                    match SketchAuthoring.trySelectedSketch doc with
                    | Some selected when sketchEditMode && doc.SelectedId = Some selected.Action.Id ->
                        SketchAuthoring.tryEditableDimension selected.Action.Id selected.Sketch index
                    | _ ->
                        None
                sketchTool <- "none"
                sketchToolPoints <- []
                constraintPlacementDraft <- None
                constraintPlacementMode <- None
                constraintPlacementCursor <- None
                normalizeSketchUiState ()
                withViewerInvalidation "state" (json ()))) |> ignore

        app.MapPost("/api/viewer/dimension-edit/start",
            Func<HttpContext, IResult>(fun ctx ->
                let body = ctx.Request.ReadFromJsonAsync<JsonElement>().Result
                let index = body.GetProperty("constraintIndex").GetInt32()
                editingDimension <-
                    match SketchAuthoring.trySelectedSketch doc with
                    | Some selected when sketchEditMode && doc.SelectedId = Some selected.Action.Id ->
                        SketchAuthoring.tryEditableDimension selected.Action.Id selected.Sketch index
                    | _ ->
                        None
                sketchTool <- "none"
                sketchToolPoints <- []
                constraintPlacementDraft <- None
                constraintPlacementMode <- None
                constraintPlacementCursor <- None
                normalizeSketchUiState ()
                viewerStateResult ())) |> ignore

        app.MapPost("/api/viewer/dimension-edit/cancel",
            Func<IResult>(fun () ->
                editingDimension <- None
                constraintPlacementDraft <- None
                constraintPlacementCursor <- None
                normalizeSketchUiState ()
                viewerStateResult ())) |> ignore

        app.MapPost("/api/viewer/dimension-edit/commit",
            Func<HttpContext, IResult>(fun ctx ->
                let body = ctx.Request.ReadFromJsonAsync<JsonElement>().Result
                let value = body.GetProperty("value")
                match editingDimension with
                | Some current ->
                    let key = $"sketch.constraint.{current.ConstraintIndex}.{current.Key}"
                    editingDimension <- None
                    constraintPlacementDraft <- None
                    constraintPlacementCursor <- None
                    doc <- Document.patchParam current.SketchId key value doc
                    recompile ()
                    viewerStateResult ()
                | None ->
                    viewerStateResult ())) |> ignore

        app.MapPost("/api/sketch-ui/dimension-edit/cancel",
            Func<IResult>(fun () ->
                editingDimension <- None
                constraintPlacementDraft <- None
                constraintPlacementCursor <- None
                normalizeSketchUiState ()
                withViewerInvalidation "state" (json ()))) |> ignore

        app.MapPost("/api/sketch-ui/dimension-edit/commit",
            Func<HttpContext, IResult>(fun ctx ->
                let body = ctx.Request.ReadFromJsonAsync<JsonElement>().Result
                let value = body.GetProperty("value")
                match editingDimension with
                | Some current ->
                    let key = $"sketch.constraint.{current.ConstraintIndex}.{current.Key}"
                    let invalidation = viewerInvalidationForParam current.SketchId key
                    editingDimension <- None
                    constraintPlacementDraft <- None
                    constraintPlacementCursor <- None
                    withViewerInvalidation invalidation (mutate (Document.patchParam current.SketchId key value))
                | None ->
                    withViewerInvalidation "state" (json ()))) |> ignore

        app.MapPost("/api/viewer/dimension-click-target",
            Func<IResult>(fun () ->
                match constraintPlacementMode, SketchAuthoring.trySelectedSketch doc with
                | Some kind, Some selected when sketchEditMode ->
                    constraintPlacementDraft <-
                        SketchAuthoring.updatePlacementDraft selected.Action.Id kind hoveredTarget constraintPlacementDraft
                    viewerStateResult ()
                | _ ->
                    viewerStateResult ())) |> ignore

        app.MapPut("/api/viewer/sketch",
            Func<HttpContext, IResult>(fun ctx ->
                let body = ctx.Request.ReadFromJsonAsync<JsonElement>().Result
                let actionId = body.GetProperty("actionId").GetString()
                let sketch = JsonSerializer.Deserialize<ActionSketch>(body.GetProperty("sketch").GetRawText(), jsonOpts)
                match doc.Actions |> List.tryFind (fun action -> action.Id = actionId) with
                | Some { Kind = Sketch(_, _) } ->
                    doc <- SketchAuthoring.withUpdatedSketch doc actionId sketch
                    hoveredTarget <- None
                    selectedTargets <- []
                    sketchToolPoints <- []
                    recompile ()
                    viewerStateResult ()
                | _ ->
                    viewerStateResult ())) |> ignore

        app.MapPatch("/api/viewer/sketch-params",
            Func<HttpContext, IResult>(fun ctx ->
                let body = ctx.Request.ReadFromJsonAsync<JsonElement>().Result
                let actionId = body.GetProperty("actionId").GetString()
                let updates =
                    body.GetProperty("params").EnumerateArray()
                    |> Seq.map (fun item -> item.GetProperty("key").GetString(), item.GetProperty("value"))
                    |> Seq.toList
                doc <-
                    updates
                    |> List.fold (fun current (key, value) -> Document.patchParam actionId key value current) doc
                recompile ()
                viewerStateResult ())) |> ignore

        app.MapPost("/api/viewer/tool-click",
            Func<HttpContext, IResult>(fun ctx ->
                let body = ctx.Request.ReadFromJsonAsync<JsonElement>().Result
                let x = body.GetProperty("x").GetDouble()
                let y = body.GetProperty("y").GetDouble()
                let nextPoint = { X = x; Y = y }
                match SketchAuthoring.trySelectedSketch doc with
                | Some selected when sketchEditMode && sketchTool <> "none" ->
                    let nextPoints = sketchToolPoints @ [ nextPoint ]
                    if nextPoints.Length >= SketchAuthoring.requiredToolPoints sketchTool then
                        match SketchAuthoring.applyToolClick sketchTool nextPoints selected.Sketch with
                        | Some nextSketch ->
                            doc <- SketchAuthoring.withUpdatedSketch doc selected.Action.Id nextSketch
                            sketchToolPoints <- []
                            hoveredTarget <- None
                            selectedTargets <- []
                            editingDimension <- None
                            constraintPlacementDraft <- None
                            constraintPlacementCursor <- None
                            recompile ()
                            viewerStateResult ()
                        | None ->
                            viewerStateResult ()
                    else
                        sketchToolPoints <- nextPoints
                        viewerStateResult ()
                | _ ->
                    viewerStateResult ())) |> ignore

        app.MapPost("/api/viewer/place-constraint",
            Func<HttpContext, IResult>(fun ctx ->
                let body = ctx.Request.ReadFromJsonAsync<JsonElement>().Result
                let x = body.GetProperty("x").GetDouble()
                let y = body.GetProperty("y").GetDouble()
                match (sketchUiState ()).PendingConstraintPlacement with
                | Some pending ->
                    match SketchAuthoring.placePendingConstraint doc pending { X = x; Y = y } with
                    | Some nextDoc ->
                        doc <- nextDoc
                        hoveredTarget <- None
                        selectedTargets <- []
                        sketchToolPoints <- []
                        editingDimension <- None
                        constraintPlacementDraft <- None
                        constraintPlacementMode <- None
                        constraintPlacementCursor <- None
                        recompile ()
                        viewerStateResult ()
                    | None ->
                        viewerStateResult ()
                | None ->
                    viewerStateResult ())) |> ignore

        app.MapPut("/api/document/action/{id}",
            Func<string, HttpContext, IResult>(fun id ctx ->
                mutate (Document.updateAction id (readBody<DocAction> ctx)))) |> ignore

        // Rapid: fast path for scalar drags. Try to update a single slot
        // in place; fall back to full recompile if the change affects topology.
        // Returns the viewer state payload so the client can refresh its buffer.
        app.MapPatch("/api/document/action/{id}/param/rapid",
            Func<string, HttpContext, IResult>(fun id ctx ->
                let body = ctx.Request.ReadFromJsonAsync<JsonElement>().Result
                let key = body.GetProperty("key").GetString()
                let value = body.GetProperty("value")
                if tryFastSlotUpdate id key value then
                    withViewerInvalidation "state" (viewerStateResult ())
                else
                    // Topology change (ref, bool, etc.) — full recompile
                    doc <- Document.patchParam id key value doc
                    recompile ()
                    withViewerInvalidation "model" (viewerStateResult ()))) |> ignore

        // Commit: returns full document (used on pointer up)
        app.MapPatch("/api/document/action/{id}/param",
            Func<string, HttpContext, IResult>(fun id ctx ->
                let body = ctx.Request.ReadFromJsonAsync<JsonElement>().Result
                let key = body.GetProperty("key").GetString()
                let value = body.GetProperty("value")
                let invalidation = viewerInvalidationForParam id key
                withViewerInvalidation invalidation (mutate (Document.patchParam id key value)))) |> ignore

        app.MapPatch("/api/document/action/{id}/visible",
            Func<string, IResult>(fun id -> mutate (Document.toggleVisible id))) |> ignore

        app.MapPatch("/api/document/action/{id}/display/toggle",
            Func<string, IResult>(fun id -> mutate (Document.toggleDisplay id))) |> ignore

        app.MapPatch("/api/document/action/{id}/display",
            Func<string, HttpContext, IResult>(fun id ctx ->
                let body = ctx.Request.ReadFromJsonAsync<JsonElement>().Result
                let key = body.GetProperty("key").GetString()
                let value = body.GetProperty("value")
                mutate (Document.patchDisplay id key value))) |> ignore

        app.MapPatch("/api/document/action/{id}/field-slice/toggle",
            Func<string, IResult>(fun id -> mutate (Document.toggleFieldSlice id))) |> ignore

        app.MapPatch("/api/document/action/{id}/field-slice",
            Func<string, HttpContext, IResult>(fun id ctx ->
                let body = ctx.Request.ReadFromJsonAsync<JsonElement>().Result
                let key = body.GetProperty("key").GetString()
                let value = body.GetProperty("value")
                mutate (Document.patchFieldSlice id key value))) |> ignore

        app.MapPost("/api/document/action",
            Func<HttpContext, IResult>(fun ctx ->
                mutate (Document.addAction (readBody<DocAction> ctx)))) |> ignore

        app.MapDelete("/api/document/action/{id}",
            Func<string, IResult>(fun id -> mutate (Document.removeAction id))) |> ignore

        app.MapPut("/api/document/reorder",
            Func<HttpContext, IResult>(fun ctx ->
                let ids = ctx.Request.ReadFromJsonAsync<JsonElement>().Result
                let idList = JsonSerializer.Deserialize<string array>(ids.GetRawText()) |> Array.toList
                mutate (Document.reorder idList))) |> ignore

        // ── Palette endpoints ──────────────────────────────────────────

        /// If done, build action + return document; otherwise return palette state.
        let maybeBuild () =
            let state = Palette.toState paletteSession compiled.TypeMap doc
            if state.Mode = "done" then
                match Palette.buildAction paletteSession (Guid.NewGuid().ToString("N").[..5]) with
                | Some action ->
                    doc <- Document.addAction action doc
                    recompile ()
                    paletteSession <- Palette.empty
                    paletteAndDoc ()
                | None ->
                    paletteSession <- Palette.empty
                    paletteJson ()
            else
                paletteJson ()

        app.MapPost("/api/palette/open",
            Func<IResult>(fun () ->
                paletteSession <- Palette.openSession ()
                paletteJson ())) |> ignore

        app.MapPost("/api/palette/query/rapid",
            Func<HttpContext, IResult>(fun ctx ->
                let body = ctx.Request.ReadFromJsonAsync<JsonElement>().Result
                let q = body.GetProperty("query").GetString()
                paletteSession <- Palette.setQuery q paletteSession
                let state = Palette.toState paletteSession compiled.TypeMap doc
                Results.Content(JsonSerializer.Serialize(state.Items, jsonOpts), "application/json"))) |> ignore

        app.MapPost("/api/palette/query",
            Func<HttpContext, IResult>(fun ctx ->
                let body = ctx.Request.ReadFromJsonAsync<JsonElement>().Result
                let q = body.GetProperty("query").GetString()
                paletteSession <- Palette.setQuery q paletteSession
                paletteJson ())) |> ignore

        app.MapPost("/api/palette/pick",
            Func<HttpContext, IResult>(fun ctx ->
                let body = ctx.Request.ReadFromJsonAsync<JsonElement>().Result
                let id = body.GetProperty("id").GetString()
                match paletteSession.PickedKind with
                | None -> paletteSession <- Palette.pickCommand id paletteSession
                | Some _ -> paletteSession <- Palette.pickItem id paletteSession
                maybeBuild ())) |> ignore

        // Fire-and-forget: update a scalar field during drag (204 No Content)
        app.MapPost("/api/palette/scalar/rapid",
            Func<HttpContext, IResult>(fun ctx ->
                let body = ctx.Request.ReadFromJsonAsync<JsonElement>().Result
                let key = body.GetProperty("key").GetString()
                let value = body.GetProperty("value").GetDouble()
                paletteSession <- Palette.setScalarField key value paletteSession
                Results.NoContent())) |> ignore

        // Commit the current scalar group and advance
        app.MapPost("/api/palette/scalars/commit",
            Func<IResult>(fun () ->
                paletteSession <- Palette.commitScalars paletteSession
                maybeBuild ())) |> ignore

        app.MapPost("/api/palette/finish",
            Func<IResult>(fun () ->
                paletteSession <- Palette.skipToEnd paletteSession
                maybeBuild ())) |> ignore

        app.MapPost("/api/palette/back",
            Func<IResult>(fun () ->
                paletteSession <- Palette.back paletteSession
                paletteJson ())) |> ignore

        app.MapPost("/api/palette/close",
            Func<IResult>(fun () ->
                paletteSession <- Palette.empty
                Results.NoContent())) |> ignore

        app.Run("http://localhost:5222")
        0
