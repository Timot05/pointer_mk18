module DocumentJsonRoundTripTests

// Verify that Thoth.Json's auto-derived encoders/decoders correctly
// round-trip the full `Document` type. Both the "Save → Load" and the
// "drop JSON into ui/defaults/" workflows hinge on this — if Thoth
// can't reconstruct the F# DUs / records / maps / lists faithfully,
// reload produces a `Document` whose discriminated unions look fine
// to the JSON parser but break pattern matching downstream.
//
// .NET tests use `Thoth.Json.Net`; the running app uses `Thoth.Json`
// (Fable-only). They share the same wire format, so a passing test
// here is a good (but not perfect) signal that the Fable path works
// too — the two implementations have parallel reflection backends.

open Xunit
open Server
open Server.Lang
open Thoth.Json.Net

[<Fact>]
let ``examples/basic.json decodes and compiles cleanly`` () =
    // One-off check: the hand-crafted basic example for the Examples
    // dropdown must (1) decode via Thoth and (2) compose into a
    // non-empty IR without block errors. Catches structural mistakes
    // in the JSON (loop shape, EVar/EPath wiring, etc.) before the
    // user hits them in the browser.
    let path =
        System.IO.Path.Combine(
            __SOURCE_DIRECTORY__,
            "..", "ui", "defaults", "examples", "basic.json")
    let text = System.IO.File.ReadAllText path
    match Decode.Auto.fromString<Document>(text) with
    | Error msg -> failwithf "decode failed: %s" msg
    | Ok doc ->
        let nb : Notebook.Notebook =
            { NextId = doc.NextBlockId; Blocks = doc.Blocks }
        let userScript = UserScript.analyze doc.ScriptSourceText
        let result = NotebookCompose.compileWith nb userScript
        Assert.True(
            Map.isEmpty result.BlockErrors,
            sprintf "expected no block errors, got %A" (Map.toList result.BlockErrors))
        Assert.True(result.Ir.IsSome, "expected non-empty IR")

[<Fact>]
let ``Thoth: empty document round-trips via Encode.Auto / Decode.Auto`` () =
    let original = Document.emptyDocument ()
    let json = Encode.Auto.toString(2, original)
    match Decode.Auto.fromString<Document>(json) with
    | Ok decoded ->
        Assert.Equal<Document>(original, decoded)
    | Error msg ->
        failwithf "Thoth decode failed: %s\n\nEncoded JSON (first 500 chars):\n%s"
            msg
            (if json.Length > 500 then json.Substring(0, 500) + "..." else json)

[<Fact>]
let ``Thoth: minimal document with one sketch + one native block round-trips`` () =
    // A targeted minimum case: if the full default-doc test fails this
    // narrows the failure to the basic Block / BlockBody shape.
    let block : Lang.Notebook.Block =
        { Id = 1
          Name = "sphere1"
          Body =
              Lang.Notebook.NativeBody(
                  "sphere",
                  Map.ofList
                      [ "radius", Lang.AstBuilder.numE 1.5 ])
          Visibility = Lang.Notebook.VIsosurface
          ColorIndex = 0
          SlicePlane = Lang.Notebook.defaultSlicePlane }
    let doc : Document =
        { Name = "rt-test"
          Blocks = [ block ]
          NextBlockId = 2
          SelectedBlockId = Some 1
          ScriptSourceText = "" }
    let json = Encode.Auto.toString(2, doc)
    match Decode.Auto.fromString<Document>(json) with
    | Ok decoded ->
        Assert.Equal<Document>(doc, decoded)
    | Error msg ->
        failwithf "Thoth decode failed on minimal doc: %s\n\nJSON:\n%s" msg json
