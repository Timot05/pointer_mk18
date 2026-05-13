module UserScriptTests

open Xunit
open Server.Lang

[<Fact>]
let ``analyze: empty source yields empty result`` () =
    let r = UserScript.analyze ""
    Assert.True(r.ParseError.IsNone)
    Assert.True(r.Specs.IsEmpty)
    Assert.True(r.AnalysisErrors.IsEmpty)

[<Fact>]
let ``analyze: single annotated lambda surfaces as a UserSpec`` () =
    let r = UserScript.analyze "let donut = fun (r: Scalar) -> r + 1 end"
    Assert.True(r.ParseError.IsNone)
    Assert.True(r.AnalysisErrors.IsEmpty)
    let spec = Map.find "donut" r.Specs
    Assert.Equal("donut", spec.Name)
    Assert.Equal(1, List.length spec.Params)
    Assert.Equal("r", spec.Params.[0].Name)
    Assert.Equal(Type.Scalar, spec.Params.[0].Type)

[<Fact>]
let ``analyze: two specs both register`` () =
    let r =
        UserScript.analyze
            "let one = fun (x: Scalar) -> x + 1 end\n\
             let two = fun (y: Field) -> y * 2.0 end"
    Assert.Equal(2, Map.count r.Specs)
    Assert.Contains("one", r.Specs |> Map.toList |> List.map fst)
    Assert.Contains("two", r.Specs |> Map.toList |> List.map fst)

[<Fact>]
let ``analyze: parse error yields ParseError and empty specs`` () =
    let r = UserScript.analyze "let f = fun (x: Scalar) -> "
    Assert.True(r.ParseError.IsSome)
    Assert.True(r.Specs.IsEmpty)
    Assert.True(r.Stmts.IsEmpty)

[<Fact>]
let ``analyze: missing annotation records AnalysisError and skips spec`` () =
    // Mixing annotated + unannotated params — second param has no `: Type`.
    let r = UserScript.analyze "let f = fun (x: Scalar) y -> x + y end"
    Assert.True(r.ParseError.IsNone)
    Assert.False(r.Specs.ContainsKey "f")
    Assert.Equal(1, List.length r.AnalysisErrors)
    let (name, msg) = r.AnalysisErrors.[0]
    Assert.Equal("f", name)
    Assert.Contains("annotation", msg)

[<Fact>]
let ``analyze: mixed broken+good lambdas keep the good one`` () =
    let r =
        UserScript.analyze
            "let broken = fun x -> x + 1 end\n\
             let good = fun (x: Scalar) -> x + 1 end"
    Assert.True(r.Specs.ContainsKey "good")
    Assert.False(r.Specs.ContainsKey "broken")
    Assert.Equal(1, List.length r.AnalysisErrors)
    Assert.Equal("broken", fst r.AnalysisErrors.[0])

[<Fact>]
let ``analyze: non-lambda let lands in Stmts but not Specs`` () =
    let r = UserScript.analyze "let pi = 3.14159"
    Assert.True(r.ParseError.IsNone)
    Assert.True(r.AnalysisErrors.IsEmpty)
    Assert.True(r.Specs.IsEmpty)
    Assert.Equal(1, List.length r.Stmts)

[<Fact>]
let ``analyze: stmts are preserved in source order`` () =
    let r =
        UserScript.analyze
            "let a = 1\n\
             let f = fun (x: Scalar) -> x + 1 end\n\
             let b = 2"
    Assert.Equal(3, List.length r.Stmts)
    Assert.True(r.Specs.ContainsKey "f")

[<Fact>]
let ``Document.emptyDocument's default script parses and analyses cleanly`` () =
    // Guards against accidentally breaking the demo content that users
    // see when opening a fresh document. If this test ever fails, fix
    // the default source in `Document.emptyDocument` rather than
    // deleting the test.
    let doc = Server.Document.emptyDocument ()
    let r = UserScript.analyze doc.ScriptSourceText
    Assert.True(r.ParseError.IsNone, "default script must parse")
    Assert.True(r.AnalysisErrors.IsEmpty, "default script must have no analysis errors")
    Assert.True(r.Specs.ContainsKey "capsule", "default script must define `capsule`")

[<Fact>]
let ``analyze: curried two-param spec captures both params in order`` () =
    let r =
        UserScript.analyze "let scale = fun (x: Scalar) (y: Field) -> x * y end"
    let spec = Map.find "scale" r.Specs
    Assert.Equal(2, List.length spec.Params)
    Assert.Equal("x", spec.Params.[0].Name)
    Assert.Equal(Type.Scalar, spec.Params.[0].Type)
    Assert.Equal("y", spec.Params.[1].Name)
    Assert.Equal(Type.Field, spec.Params.[1].Type)
