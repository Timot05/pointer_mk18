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
    let mutable paletteSession = Palette.empty

    let json () = Results.Content(JsonSerializer.Serialize(doc, jsonOpts), "application/json")

    let paletteJson () =
        let state = Palette.toState paletteSession doc
        Results.Content(JsonSerializer.Serialize(state, jsonOpts), "application/json")

    let paletteAndDoc () =
        let state = Palette.toState paletteSession doc
        let combined = {| Palette = state; Document = doc |}
        Results.Content(JsonSerializer.Serialize(combined, jsonOpts), "application/json")

    let mutate f =
        doc <- f doc
        json ()

    let mutateSilent f =
        doc <- f doc
        Results.NoContent()

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

        app.MapPut("/api/document/select/{id}",
            Func<string, IResult>(fun id -> mutate (Document.select id))) |> ignore

        app.MapPut("/api/document/action/{id}",
            Func<string, HttpContext, IResult>(fun id ctx ->
                mutate (Document.updateAction id (readBody<DocAction> ctx)))) |> ignore

        // Rapid: fire-and-forget during drag (204 No Content)
        app.MapPatch("/api/document/action/{id}/param/rapid",
            Func<string, HttpContext, IResult>(fun id ctx ->
                let body = ctx.Request.ReadFromJsonAsync<JsonElement>().Result
                let key = body.GetProperty("key").GetString()
                let value = body.GetProperty("value")
                mutateSilent (Document.patchParam id key value))) |> ignore

        // Commit: returns full document (used on pointer up)
        app.MapPatch("/api/document/action/{id}/param",
            Func<string, HttpContext, IResult>(fun id ctx ->
                let body = ctx.Request.ReadFromJsonAsync<JsonElement>().Result
                let key = body.GetProperty("key").GetString()
                let value = body.GetProperty("value")
                mutate (Document.patchParam id key value))) |> ignore

        app.MapPatch("/api/document/action/{id}/visible",
            Func<string, IResult>(fun id -> mutate (Document.toggleVisible id))) |> ignore

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
            let state = Palette.toState paletteSession doc
            if state.Mode = "done" then
                match Palette.buildAction paletteSession (Guid.NewGuid().ToString("N").[..5]) with
                | Some action ->
                    doc <- Document.addAction action doc
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
                let state = Palette.toState paletteSession doc
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
