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
    | Ast.ELambda(param, body) ->
        Assert.Equal("x", param.Name)
        match body.Node with
        | Ast.EBinary(Ast.Add, _, _) -> ()
        | other -> failwithf "expected Add body, got %A" other
    | other -> failwithf "expected Lambda, got %A" other

[<Fact>]
let ``parse: call with named argument`` () =
    let e = parseExprOnly "@sphere(radius = 1.0)"
    match e.Node with
    | Ast.ECall(callee, [ Ast.Named(name, value) ]) ->
        Assert.Equal("sphere", callee.Name)
        Assert.Equal(Ast.Internal, callee.IdentKind)
        Assert.Equal("radius", name.Name)
        match value.Node with
        | Ast.ENumber 1.0 -> ()
        | other -> failwithf "expected number, got %A" other
    | other -> failwithf "expected named-arg ECall, got %A" other

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
    let ir, v = runOk "@sphere(1.0)"
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
    let ir, v = runOk "@translate(1, 0, 0, @sphere(1))"
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
    let ir, v = runOk "@union(@sphere(1), @sphere(2))"
    match v with
    | Value.VField root ->
        let rootNode = ir.Nodes.[root.Id]
        Assert.Equal(MathIr.NodeKind.BinaryK, rootNode.Kind)
        Assert.Equal(int MathIr.Binary.Min, rootNode.Op)
    | other -> failwithf "expected VField, got %A" other
