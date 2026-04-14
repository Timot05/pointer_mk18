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

    let recompile () =
        compiled <- Pipeline.compile doc.Actions

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
                            | FramePointLineDistance(_, _, _, _, lp)
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
           Frames = frames
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

        // Pick report — frontend posts the GPU-returned pickId after a click.
        // Server resolves it to an action id and calls Document.select.
        // Stale ids (from a pre-recompile model) are silently ignored.
        app.MapPost("/api/viewer/pick",
            Func<HttpContext, IResult>(fun ctx ->
                let body = ctx.Request.ReadFromJsonAsync<JsonElement>().Result
                let pid = body.GetProperty("pickId").GetInt32()
                let target =
                    compiled.Pickables
                    |> List.tryFind (fun p -> Pickable.pickId p = pid)
                    |> Option.map Pickable.targetAction
                match target with
                | Some actionId -> mutate (Document.select actionId)
                | None -> json ())) |> ignore

        app.MapPut("/api/document/select/{id}",
            Func<string, IResult>(fun id -> mutate (Document.select id))) |> ignore

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
