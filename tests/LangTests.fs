module LangTests

open Xunit
open Server.Lang

/// Resolve a spec's typed interface (param names + types + output)
/// from either `BlockSpec` (the surviving intrinsics — translate /
/// mirror-symmetric / from-sketch / revolve / wing-remap-preview)
/// or the default user-script source (sphere / box / cylinder /
/// halfplane / union / intersect / subtract / thicken / shell, plus
/// the `smooth_min` / `capsule` helpers). After the BlockSpec→script
/// migration, tests need to walk both lookup paths.
let private specInterface (name: string)
        : string list * Type.T list * Type.T =
    match BlockSpec.tryFind name with
    | Some s ->
        let i = BlockSpec.typedInterface s
        i.Params |> List.map (fun p -> p.Name),
        i.Params |> List.map (fun p -> p.Type),
        i.Output
    | None ->
        let scriptSrc = (Server.Document.emptyDocument ()).ScriptSourceText
        let r = UserScript.analyze scriptSrc
        match Map.tryFind name r.Specs with
        | Some us ->
            us.Params |> List.map (fun p -> p.Name),
            us.Params |> List.map (fun p -> p.Type),
            us.Output
        | None -> failwithf "no spec '%s' in BlockSpec or default script" name

// ─── Lexer ──────────────────────────────────────────────────────────────────

let private kindsOf (source: string) : Token.Kind list =
    match Lexer.tokenize source with
    | Ok toks ->
        toks
        |> Array.toList
        |> List.map (fun t -> t.Kind)
    | Error e -> failwithf "lex failed: %s" e.Message

[<Fact>]
let ``lex: let-binding produces expected kind sequence`` () =
    let kinds = kindsOf "let x = 1"
    Assert.Equal<Token.Kind list>(
        [ Token.Let; Token.Ident "x"; Token.Equals; Token.Integer 1; Token.Eof ],
        kinds)

[<Fact>]
let ``lex: internal idents strip the at-sign prefix`` () =
    let kinds = kindsOf "@sphere"
    Assert.Equal<Token.Kind list>(
        [ Token.InternalIdent "sphere"; Token.Eof ],
        kinds)

[<Fact>]
let ``lex: string escapes are decoded`` () =
    match Lexer.tokenize "\"a\\nb\"" with
    | Ok toks ->
        Assert.Equal(Token.String "a\nb", toks.[0].Kind)
    | Error e -> failwithf "lex failed: %s" e.Message

[<Fact>]
let ``lex: float vs integer disambiguation`` () =
    let kinds = kindsOf "1.5 1e3 .5 42"
    Assert.Equal<Token.Kind list>(
        [ Token.Float 1.5; Token.Float 1000.0; Token.Float 0.5; Token.Integer 42; Token.Eof ],
        kinds)

[<Fact>]
let ``lex: block comment is consumed in full`` () =
    let kinds = kindsOf "1 /* comment */ 2"
    Assert.Equal<Token.Kind list>(
        [ Token.Integer 1; Token.Integer 2; Token.Eof ],
        kinds)

[<Fact>]
let ``lex: newline run produces single Newline token`` () =
    let kinds = kindsOf "1\n\n\n2"
    Assert.Equal<Token.Kind list>(
        [ Token.Integer 1; Token.Newline; Token.Integer 2; Token.Eof ],
        kinds)

// ─── Parser ─────────────────────────────────────────────────────────────────

let private parseExprOnly (source: string) : Ast.Expr =
    match Parser.parseProgram source with
    | Ok [ Ast.SExpr e ] -> e
    | Ok stmts -> failwithf "expected single expr, got %A" stmts
    | Error e -> failwithf "parse failed: %s" e.Message

[<Fact>]
let ``parse: precedence — multiplication binds tighter than addition`` () =
    let e = parseExprOnly "1 + 2 * 3"
    match e.Node with
    | Ast.EBinary(Ast.Add, _, right) ->
        match right.Node with
        | Ast.EBinary(Ast.Mul, _, _) -> ()
        | other -> failwithf "expected Mul, got %A" other
    | other -> failwithf "expected Add at root, got %A" other

[<Fact>]
let ``parse: pipe is left-associative and lowest precedence`` () =
    let e = parseExprOnly "x |> f |> g"
    match e.Node with
    | Ast.EBinary(Ast.Pipe, left, right) ->
        match left.Node, right.Node with
        | Ast.EBinary(Ast.Pipe, _, _), Ast.EVar id when id.Name = "g" -> ()
        | _ -> failwithf "unexpected shape: left=%A right=%A" left.Node right.Node
    | other -> failwithf "expected Pipe at root, got %A" other

[<Fact>]
let ``parse: application is greedy postfix and left-associative`` () =
    let e = parseExprOnly "f x y"
    match e.Node with
    | Ast.EApply(inner, _) ->
        match inner.Node with
        | Ast.EApply(fnExpr, _) ->
            match fnExpr.Node with
            | Ast.EVar id when id.Name = "f" -> ()
            | other -> failwithf "expected EVar f, got %A" other
        | other -> failwithf "expected EApply, got %A" other
    | other -> failwithf "expected EApply at root, got %A" other

[<Fact>]
let ``parse: lambda body is the inner expression`` () =
    let e = parseExprOnly "fun x -> x + 1 end"
    match e.Node with
    | Ast.ELambda(param, _, body) ->
        Assert.Equal("x", param.Name)
        match body.Node with
        | Ast.EBinary(Ast.Add, _, _) -> ()
        | other -> failwithf "expected Add body, got %A" other
    | other -> failwithf "expected Lambda, got %A" other

[<Fact>]
let ``parse: builtin juxtaposition produces EApply with EVar Internal callee`` () =
    // `@sphere 1.0` — F#-style call. `@sphere` resolves at eval-time to a
    // VBuiltin of arity 1; saturation triggers dispatch.
    let e = parseExprOnly "@sphere 1.0"
    match e.Node with
    | Ast.EApply(fnExpr, arg) ->
        match fnExpr.Node, arg.Node with
        | Ast.EVar id, Ast.ENumber 1.0 ->
            Assert.Equal("sphere", id.Name)
            Assert.Equal(Ast.Internal, id.IdentKind)
        | _ -> failwithf "unexpected shape: fn=%A arg=%A" fnExpr.Node arg.Node
    | other -> failwithf "expected EApply at root, got %A" other

[<Fact>]
let ``parse: braces produce a Block`` () =
    let e = parseExprOnly "{ let x = 1; x + 2 }"
    match e.Node with
    | Ast.EBlock stmts ->
        Assert.Equal(2, List.length stmts)
        match stmts.[0] with
        | Ast.SLet(names, _) ->
            Assert.Equal(1, List.length names)
            Assert.Equal("x", names.[0].Name)
        | other -> failwithf "expected SLet, got %A" other
    | other -> failwithf "expected EBlock, got %A" other

// ─── Evaluator ──────────────────────────────────────────────────────────────

let private runOk (source: string) : MathIr.MathIR * Value.Value =
    match Eval.run source with
    | Ok r -> r
    | Error e -> failwithf "eval failed: %s (span=%d..%d)" e.Message e.Span.Start e.Span.Stop

[<Fact>]
let ``eval: numeric expression respects precedence`` () =
    let _, v = runOk "1 + 2 * 3"
    Assert.Equal(Value.VNumber 7.0, v)

[<Fact>]
let ``eval: let-binding then reference`` () =
    let _, v = runOk "let x = 5\nx + 1"
    Assert.Equal(Value.VNumber 6.0, v)

[<Fact>]
let ``eval: closure captures and applies`` () =
    let _, v = runOk "let f = fun x -> x + 1 end\nf 5"
    Assert.Equal(Value.VNumber 6.0, v)

[<Fact>]
let ``eval: pipe sugar applies to a closure`` () =
    let _, v = runOk "let inc = fun x -> x + 1 end\n5 |> inc"
    Assert.Equal(Value.VNumber 6.0, v)

[<Fact>]
let ``eval: bare name does not resolve to a builtin`` () =
    // Builtins live behind the `@` prefix only — bare `sphere` is unbound.
    match Eval.run "sphere 1" with
    | Ok _ -> failwith "expected eval failure for bare-name builtin"
    | Error e ->
        Assert.Contains("unbound", e.Message)

// ─── `import` / `export` declarations ──────────────────────────────────────

[<Fact>]
let ``parse: import x produces SImport`` () =
    match Parser.parseProgram "import radius" with
    | Ok [ Ast.SImport id ] ->
        Assert.Equal("radius", id.Name)
        Assert.Equal(Ast.User, id.IdentKind)
    | other -> failwithf "expected single SImport, got %A" other

[<Fact>]
let ``parse: import x = default is rejected`` () =
    match Parser.parseProgram "import radius = 1.0" with
    | Ok stmts -> failwithf "expected parse error, got %A" stmts
    | Error e ->
        Assert.Contains("default value", e.Message)

[<Fact>]
let ``parse: export y = expr produces SExport`` () =
    match Parser.parseProgram "export shape = @sphere 1" with
    | Ok [ Ast.SExport(name, value) ] ->
        Assert.Equal("shape", name.Name)
        match value.Node with
        | Ast.EApply _ -> ()
        | other -> failwithf "expected EApply rhs, got %A" other
    | other -> failwithf "expected single SExport, got %A" other

[<Fact>]
let ``ast: collectImports finds top-level imports in source order`` () =
    match Parser.parseProgram "import a\nimport b\nexport c = a + b" with
    | Ok stmts ->
        let names = AstQueries.collectImports stmts |> List.map (fun i -> i.Name)
        Assert.Equal<string list>([ "a"; "b" ], names)
    | Error e -> failwithf "parse failed: %s" e.Message

[<Fact>]
let ``ast: collectExports deduplicates by name keeping last write`` () =
    match Parser.parseProgram "export x = 1\nexport x = 2" with
    | Ok stmts ->
        let names = AstQueries.collectExports stmts |> List.map (fun i -> i.Name)
        Assert.Equal<string list>([ "x" ], names)
    | Error e -> failwithf "parse failed: %s" e.Message

// ─── Native block specs ────────────────────────────────────────────────────

[<Fact>]
let ``sphere extracts as [radius:Scalar] -> Field`` () =
    let names, types, output = specInterface "sphere"
    Assert.Equal<string list>([ "radius" ], names)
    Assert.Equal<Type.T list>([ Type.Scalar ], types)
    Assert.Equal(Type.Field, output)

[<Fact>]
let ``translate extracts in declaration order with mixed types`` () =
    let names, types, _ = specInterface "translate"
    Assert.Equal<string list>([ "x"; "y"; "z"; "child" ], names)
    Assert.Equal<Type.T list>(
        [ Type.Scalar; Type.Scalar; Type.Scalar; Type.Field ],
        types)

[<Fact>]
let ``union extracts as [target:Field; tool:Field; radius:Scalar] -> Field`` () =
    let names, types, output = specInterface "union"
    Assert.Equal<string list>([ "target"; "tool"; "radius" ], names)
    Assert.Equal<Type.T list>([ Type.Field; Type.Field; Type.Scalar ], types)
    Assert.Equal(Type.Field, output)

// ─── F#-style param annotations ─────────────────────────────────────────────

[<Fact>]
let ``parse: lambda with annotated paren param carries Some paramAnno`` () =
    let e = parseExprOnly "fun (x: Scalar) -> x end"
    match e.Node with
    | Ast.ELambda(param, Some ty, _) ->
        Assert.Equal("x", param.Name)
        Assert.Equal(Type.Scalar, ty)
    | other -> failwithf "expected annotated lambda, got %A" other

[<Fact>]
let ``parse: two annotated params build a curried lambda`` () =
    let e = parseExprOnly "fun (x: Scalar) (y: Field) -> x * y end"
    match e.Node with
    | Ast.ELambda(p1, Some t1, inner) ->
        Assert.Equal("x", p1.Name)
        Assert.Equal(Type.Scalar, t1)
        match inner.Node with
        | Ast.ELambda(p2, Some t2, _) ->
            Assert.Equal("y", p2.Name)
            Assert.Equal(Type.Field, t2)
        | other -> failwithf "expected inner annotated lambda, got %A" other
    | other -> failwithf "expected outer annotated lambda, got %A" other

[<Fact>]
let ``parse: unannotated lambda still produces None on paramAnno`` () =
    let e = parseExprOnly "fun x -> x + 1 end"
    match e.Node with
    | Ast.ELambda(_, None, _) -> ()
    | Ast.ELambda(_, Some t, _) ->
        failwithf "expected unannotated, got Some %A" t
    | other -> failwithf "expected lambda, got %A" other

[<Fact>]
let ``parse: bogus type name yields a parse error`` () =
    match Parser.parseProgram "fun (x: Bogus) -> x end" with
    | Ok _ -> failwith "expected parse error for unknown type"
    | Error e -> Assert.Contains("Bogus", e.Message)

[<Fact>]
let ``parse: function def uses fun ... end with annotated params`` () =
    match Parser.parseProgram "let donut = fun (r: Scalar) -> r + 1 end" with
    | Ok [ Ast.SLet([ name ], value) ] ->
        Assert.Equal("donut", name.Name)
        match value.Node with
        | Ast.ELambda(p, Some t, _) ->
            Assert.Equal("r", p.Name)
            Assert.Equal(Type.Scalar, t)
        | other -> failwithf "expected annotated lambda RHS, got %A" other
    | other -> failwithf "expected single SLet, got %A" other

[<Fact>]
let ``parse: multi-param fun with mixed types`` () =
    match Parser.parseProgram "let f = fun (x: Scalar) (y: Field) -> y * x end" with
    | Ok [ Ast.SLet([ name ], value) ] ->
        Assert.Equal("f", name.Name)
        match value.Node with
        | Ast.ELambda(p1, Some Type.Scalar, inner) ->
            Assert.Equal("x", p1.Name)
            match inner.Node with
            | Ast.ELambda(p2, Some Type.Field, _) ->
                Assert.Equal("y", p2.Name)
            | other -> failwithf "expected inner Field-annotated lambda, got %A" other
        | other -> failwithf "expected outer Scalar-annotated lambda, got %A" other
    | other -> failwithf "expected single SLet, got %A" other

[<Fact>]
let ``parse: F#-style let-sugar with params is NOT supported`` () =
    // `let f x = body` must error — defs always go through `fun ... end`.
    match Parser.parseProgram "let f x = x + 1" with
    | Ok _ -> failwith "expected let-sugar to fail; only `let f = fun x -> body end` should parse"
    | Error _ -> ()

[<Fact>]
let ``parse: F#-style annotated let-sugar is NOT supported`` () =
    match Parser.parseProgram "let f (x: Scalar) = x + 1" with
    | Ok _ -> failwith "expected annotated let-sugar to fail"
    | Error _ -> ()

[<Fact>]
let ``parse: simple let binds a value`` () =
    match Parser.parseProgram "let x = 1" with
    | Ok [ Ast.SLet([ name ], value) ] ->
        Assert.Equal("x", name.Name)
        match value.Node with
        | Ast.ENumber 1.0 -> ()
        | other -> failwithf "expected ENumber 1.0, got %A" other
    | other -> failwithf "expected single SLet, got %A" other

[<Fact>]
let ``parse: multi-name destructuring still works`` () =
    match Parser.parseProgram "let a, b = 1" with
    | Ok [ Ast.SLet(names, _) ] ->
        Assert.Equal<string list>([ "a"; "b" ], names |> List.map (fun n -> n.Name))
    | other -> failwithf "expected destructuring SLet, got %A" other

[<Fact>]
let ``eval: fun ... end def then call`` () =
    let _, v = runOk "let add = fun a b -> a + b end\nadd 2 3"
    Assert.Equal(Value.VNumber 5.0, v)

[<Fact>]
let ``eval: fun with type-annotated params`` () =
    let _, v = runOk "let scale = fun (x: Scalar) (y: Scalar) -> x * y end\nscale 6 7"
    Assert.Equal(Value.VNumber 42.0, v)

// The "applying sphere yields a Sub-rooted Field" coverage now lives
// in NotebookTests as "user script can call sqrt over field
// expressions to define a sphere" — that test composes a notebook
// against the default script and asserts BinaryK Sub / UnaryK Sqrt
// on the resulting MathIR root, which is the same property this
// test used to check via direct closure application on a
// self-contained AST body.
