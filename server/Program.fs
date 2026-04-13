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

    let json () = Results.Content(JsonSerializer.Serialize(doc, jsonOpts), "application/json")

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

        app.Run("http://localhost:5222")
        0
