module LangTests

open Xunit
open Server.Lang

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
let ``eval: sphere builtin produces a Field with the expected node-tail shape`` () =
    let ir, v = runOk "@sphere 1.0"
    match v with
    | Value.VField root ->
        // sphere = sqrt(x²+y²+z²) - r → root must be a Sub binary node
        Assert.True(ir.Nodes.Count >= 7, sprintf "expected ≥7 nodes, got %d" ir.Nodes.Count)
        let rootNode = ir.Nodes.[root.Id]
        Assert.Equal(MathIr.NodeKind.BinaryK, rootNode.Kind)
        Assert.Equal(int MathIr.Binary.Sub, rootNode.Op)
    | other -> failwithf "expected VField, got %A" other

[<Fact>]
let ``eval: translate wraps target in a RemapAxes node`` () =
    let ir, v = runOk "@translate 1 0 0 (@sphere 1)"
    match v with
    | Value.VField root ->
        let rootNode = ir.Nodes.[root.Id]
        Assert.Equal(MathIr.NodeKind.RemapAxes, rootNode.Kind)
    | other -> failwithf "expected VField, got %A" other

[<Fact>]
let ``eval: pipe sugar applies to a closure`` () =
    let _, v = runOk "let inc = fun x -> x + 1 end\n5 |> inc"
    Assert.Equal(Value.VNumber 6.0, v)

[<Fact>]
let ``eval: union of two spheres roots a Min binary node`` () =
    let ir, v = runOk "@union (@sphere 1) (@sphere 2)"
    match v with
    | Value.VField root ->
        let rootNode = ir.Nodes.[root.Id]
        Assert.Equal(MathIr.NodeKind.BinaryK, rootNode.Kind)
        Assert.Equal(int MathIr.Binary.Min, rootNode.Op)
    | other -> failwithf "expected VField, got %A" other

[<Fact>]
let ``eval: pipe a sphere through translate`` () =
    // x |> f === f x. `@translate 2 0 0` is partially applied (3 of 4
    // args); piping `@sphere 1` in saturates the 4th.
    let _, v = runOk "@sphere 1 |> @translate 2 0 0"
    match v with
    | Value.VField _ -> ()
    | other -> failwithf "expected VField, got %A" other

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
let ``BlockSpec: sphere extracts as [radius:Scalar] -> Field`` () =
    let spec = BlockSpec.find "sphere"
    let extracted = BlockSpec.typedInterface spec
    Assert.Equal<int>(1, List.length extracted.Params)
    Assert.Equal("radius", extracted.Params.[0].Name)
    Assert.Equal(Type.Scalar, extracted.Params.[0].Type)
    Assert.Equal(Type.Field, extracted.Output)

[<Fact>]
let ``BlockSpec: translate extracts in declaration order with mixed types`` () =
    let spec = BlockSpec.find "translate"
    let extracted = BlockSpec.typedInterface spec
    let names = extracted.Params |> List.map (fun p -> p.Name)
    let types = extracted.Params |> List.map (fun p -> p.Type)
    Assert.Equal<string list>([ "x"; "y"; "z"; "child" ], names)
    Assert.Equal<Type.T list>(
        [ Type.Scalar; Type.Scalar; Type.Scalar; Type.Field ],
        types)

[<Fact>]
let ``BlockSpec: union extracts as [a:Field; b:Field] -> Field`` () =
    let spec = BlockSpec.find "union"
    let extracted = BlockSpec.typedInterface spec
    let types = extracted.Params |> List.map (fun p -> p.Type)
    Assert.Equal<Type.T list>([ Type.Field; Type.Field ], types)

[<Fact>]
let ``eval: applying sphere spec to 1.0 yields a Sub-rooted Field`` () =
    // The block driver will saturate the lambda by `EApply`-ing a slot-
    // backed VField for each param. This test substitutes a constant
    // (number) for `radius`, which exercises the same code path.
    let ctx = Value.createContext ()
    match Eval.evalExpr ctx (BlockSpec.find "sphere").Body with
    | Ok (Value.VClosure _) ->
        // Apply the closure to 1.0 — saturates the single param.
        match Eval.evalExpr ctx { Node = Ast.EApply((BlockSpec.find "sphere").Body, { Node = Ast.ENumber 1.0; Span = Ast.noneSpan }); Span = Ast.noneSpan } with
        | Ok (Value.VField root) ->
            let rootNode = ctx.Ir.Nodes.[root.Id]
            Assert.Equal(MathIr.NodeKind.BinaryK, rootNode.Kind)
            Assert.Equal(int MathIr.Binary.Sub, rootNode.Op)
        | other -> failwithf "expected VField, got %A" other
    | other -> failwithf "expected closure, got %A" other
