namespace Server.Lang

// ---------------------------------------------------------------------------
// Parser.fs — port of pointer_mk19/compiler/lib/parser.ml.
//
// Hand-rolled recursive descent. Precedence ladder (lowest → highest):
//   parseExpr → parsePipe → parseAdditive → parseMultiplicative
//             → parseApplication → parseUnary → parseAtom
//
// Result-based error propagation via a `>>=` bind operator. State is a
// `tokens[] + mutable index` record; backtracking is just save/restore of
// `index`.
// ---------------------------------------------------------------------------

module Parser =

    open Token
    open Ast

    type ParseError = { Message: string; Span: Span }

    type State = {
        Tokens: Token[]
        mutable Index: int
    }

    let inline (>>=) (r: Result<'a, ParseError>) (f: 'a -> Result<'b, ParseError>) =
        match r with
        | Ok v -> f v
        | Error e -> Error e

    let inline private mapOk (f: 'a -> 'b) (r: Result<'a, ParseError>) : Result<'b, ParseError> =
        match r with
        | Ok v -> Ok (f v)
        | Error e -> Error e

    let private current (p: State) : Token =
        if p.Index < p.Tokens.Length then p.Tokens.[p.Index]
        else p.Tokens.[p.Tokens.Length - 1]

    let private previous (p: State) : Token =
        if p.Index = 0 then p.Tokens.[0]
        else p.Tokens.[p.Index - 1]

    let private advance (p: State) : Token =
        let tok = current p
        p.Index <- p.Index + 1
        tok

    let private peekAt (p: State) (offset: int) : Kind option =
        let i = p.Index + offset
        if i < p.Tokens.Length then Some p.Tokens.[i].Kind else None

    let private err (tok: Token) (msg: string) : Result<'a, ParseError> =
        Error { Message = msg; Span = tok.Span }

    let rec private skipNewlines (p: State) =
        match (current p).Kind with
        | Newline -> advance p |> ignore; skipNewlines p
        | _ -> ()

    let rec private skipSeparators (p: State) =
        match (current p).Kind with
        | Newline | Semicolon -> advance p |> ignore; skipSeparators p
        | _ -> ()

    let private expect (p: State) (kind: Kind) : Result<Token, ParseError> =
        skipNewlines p
        let tok = current p
        if tok.Kind = kind then Ok (advance p)
        else
            err tok (sprintf "expected %s, found %s" (kindName kind) (kindName tok.Kind))

    let private parseIdent (p: State) : Result<Ident, ParseError> =
        skipNewlines p
        match (current p).Kind with
        | Ident name ->
            let tok = advance p
            Ok { Name = name; IdentKind = User; Span = tok.Span }
        | InternalIdent name ->
            let tok = advance p
            Ok { Name = name; IdentKind = Internal; Span = tok.Span }
        | _ -> err (current p) "expected identifier"

    let private parseBindingIdent (p: State) : Result<Ident, ParseError> =
        skipNewlines p
        match (current p).Kind with
        | Ident name ->
            let tok = advance p
            Ok { Name = name; IdentKind = User; Span = tok.Span }
        | InternalIdent _ -> err (current p) "cannot bind internal identifier"
        | _ -> err (current p) "expected identifier"

    let private tokenSpan (p: State) = (current p).Span
    let private previousSpan (p: State) = (previous p).Span

    /// Items can start application arguments without explicit parens.
    let private canStartApplicationArg (p: State) : bool =
        match (current p).Kind with
        | Float _ | Integer _ | String _ | Ident _ | InternalIdent _
        | Pop | Question | Bang | LParen | LBracket | LBrace -> true
        | Minus ->
            match peekAt p 1 with
            | Some (Float _) | Some (Integer _) | Some (Ident _)
            | Some (InternalIdent _) | Some LParen -> true
            | _ -> false
        | _ -> false

    // -- Expression parsing chain ------------------------------------------------

    let rec parseExpr (p: State) : Result<Expr, ParseError> =
        parsePipe p

    and parsePipe (p: State) : Result<Expr, ParseError> =
        parseAdditive p >>= fun left ->
            let rec loop (acc: Expr) =
                let saved = p.Index
                skipNewlines p
                let isPipeOrCompose =
                    match (current p).Kind with
                    | Token.Pipe | Token.Compose -> true
                    | _ -> false
                if not isPipeOrCompose then
                    p.Index <- saved
                match (current p).Kind with
                | Token.Pipe ->
                    advance p |> ignore
                    skipNewlines p
                    parseAdditive p >>= fun right ->
                        let e = mkExpr (EBinary(BinaryOp.Pipe, acc, right)) (mergeSpan acc.Span right.Span)
                        loop e
                | Token.Compose ->
                    advance p |> ignore
                    skipNewlines p
                    parseAdditive p >>= fun right ->
                        let e = mkExpr (EBinary(BinaryOp.Compose, acc, right)) (mergeSpan acc.Span right.Span)
                        loop e
                | _ -> Ok acc
            loop left

    and parseAdditive (p: State) : Result<Expr, ParseError> =
        parseMultiplicative p >>= fun left ->
            let rec loop (acc: Expr) =
                match (current p).Kind with
                | Plus ->
                    advance p |> ignore
                    parseMultiplicative p >>= fun right ->
                        let e = mkExpr (EBinary(BinaryOp.Add, acc, right)) (mergeSpan acc.Span right.Span)
                        loop e
                | Minus ->
                    advance p |> ignore
                    parseMultiplicative p >>= fun right ->
                        let e = mkExpr (EBinary(BinaryOp.Sub, acc, right)) (mergeSpan acc.Span right.Span)
                        loop e
                | _ -> Ok acc
            loop left

    and parseMultiplicative (p: State) : Result<Expr, ParseError> =
        parseApplication p >>= fun left ->
            let rec loop (acc: Expr) =
                match (current p).Kind with
                | Star ->
                    advance p |> ignore
                    parseApplication p >>= fun right ->
                        let e = mkExpr (EBinary(BinaryOp.Mul, acc, right)) (mergeSpan acc.Span right.Span)
                        loop e
                | Slash ->
                    advance p |> ignore
                    parseApplication p >>= fun right ->
                        let e = mkExpr (EBinary(BinaryOp.Div, acc, right)) (mergeSpan acc.Span right.Span)
                        loop e
                | _ -> Ok acc
            loop left

    and parseApplication (p: State) : Result<Expr, ParseError> =
        parseUnary p >>= fun first ->
            let rec loop (func: Expr) =
                match (current p).Kind with
                | Semicolon | Comma | End | With
                | RParen | RBrace | RBracket
                | Equals | Arrow | Newline
                | Token.Pipe | Bar | Token.Compose
                | Plus | Star | Slash | Eof -> Ok func
                | Minus
                    when p.Index + 1 >= p.Tokens.Length
                         || (current p).Span.Stop <> p.Tokens.[p.Index + 1].Span.Start ->
                    Ok func
                | _ when canStartApplicationArg p ->
                    let saved = p.Index
                    let argStart = tokenSpan p
                    match parseUnary p with
                    | Ok arg ->
                        let arg' = { arg with Span = mergeSpan argStart arg.Span }
                        let applied = mkExpr (EApply(func, arg')) (mergeSpan func.Span arg'.Span)
                        loop applied
                    | Error _ ->
                        p.Index <- saved
                        Ok func
                | _ -> Ok func
            loop first

    and parseUnary (p: State) : Result<Expr, ParseError> =
        skipNewlines p
        match (current p).Kind with
        | Minus ->
            let startTok = advance p
            parseUnary p >>= fun inner ->
                let span = mergeSpan startTok.Span inner.Span
                let zero = mkExpr (ENumber 0.0) startTok.Span
                Ok (mkExpr (EBinary(BinaryOp.Sub, zero, inner)) span)
        | Fun ->
            advance p |> ignore
            parseFun p
        | Match ->
            advance p |> ignore
            parseMatch p
        | Question -> parseSugarCall p "unknown"
        | Bang -> parseSugarCall p "free"
        | _ -> parseAtom p

    and parseSugarCall (p: State) (name: string) : Result<Expr, ParseError> =
        let tok = advance p
        let callee = { Name = name; IdentKind = Internal; Span = tok.Span }
        let unitArg = mkExpr EUnit tok.Span
        let arg =
            if canStartApplicationArg p && tok.Span.Stop = (current p).Span.Start then
                match parseAtom p with
                | Ok a -> a
                | Error _ -> unitArg
            else unitArg
        let calleeExpr = mkExpr (EVar callee) callee.Span
        Ok (mkExpr (EApply(calleeExpr, arg)) (mergeSpan tok.Span arg.Span))

    and parseAtom (p: State) : Result<Expr, ParseError> =
        skipNewlines p
        match (current p).Kind with
        | Float value ->
            let tok = advance p
            Ok (mkExpr (ENumber value) tok.Span)
        | Integer value ->
            let tok = advance p
            Ok (mkExpr (ENumber (float value)) tok.Span)
        | String value ->
            let tok = advance p
            Ok (mkExpr (EString value) tok.Span)
        | Ident "true" ->
            let tok = advance p
            Ok (mkExpr (EBool true) tok.Span)
        | Ident "false" ->
            let tok = advance p
            Ok (mkExpr (EBool false) tok.Span)
        | Ident _ | InternalIdent _ -> parseIdentPathOrCall p
        | Pop ->
            let tok = advance p
            Ok (mkExpr EStackTop tok.Span)
        | LBracket -> parseList p
        | LBrace -> parseBlock p
        | LParen -> parseParen p
        | _ -> err (current p) "expected expression"

    and parseIdentPathOrCall (p: State) : Result<Expr, ParseError> =
        parseIdent p >>= fun first ->
            let rec loopPath (parts: Ident list) =
                match (current p).Kind with
                | Dot ->
                    advance p |> ignore
                    parseIdent p >>= fun next ->
                        let rootKind = (List.head parts).IdentKind
                        let next =
                            match rootKind, next.IdentKind with
                            | Internal, User -> { next with IdentKind = Internal }
                            | _ -> next
                        if next.IdentKind <> rootKind then
                            Error { Message = "cannot mix user and internal identifiers in a path"; Span = next.Span }
                        else loopPath (parts @ [ next ])
                | _ -> Ok parts
            loopPath [ first ] >>= fun parts ->
                let baseExpr =
                    match parts with
                    | [ one ] -> mkExpr (EVar one) one.Span
                    | _ ->
                        let last = List.last parts
                        mkExpr (EPath parts) (mergeSpan first.Span last.Span)
                // The OCaml-flavoured `name(arg1, arg2)` call form was
                // retired in favour of F#-style juxtaposition (`name arg1
                // arg2`) and pipes (`arg |> name`). `parseCall` and `ECall`
                // remain on disk as compile-compat stubs but are no longer
                // produced from source.
                Ok baseExpr

    and parseCall (p: State) (callee: Ident) : Result<Expr, ParseError> =
        let startSpan = callee.Span
        expect p LParen >>= fun _ ->
            let rec loop (acc: Arg list) =
                skipNewlines p
                match (current p).Kind with
                | RParen ->
                    let close = advance p
                    Ok (mkExpr (ECall(callee, List.rev acc)) (mergeSpan startSpan close.Span))
                | _ ->
                    parseArg p >>= fun arg ->
                        match (current p).Kind with
                        | Comma ->
                            advance p |> ignore
                            skipNewlines p
                            loop (arg :: acc)
                        | RParen ->
                            let close = advance p
                            Ok (mkExpr (ECall(callee, List.rev (arg :: acc))) (mergeSpan startSpan close.Span))
                        | _ -> err (current p) "expected ',' or ')'"
            loop []

    and parseArg (p: State) : Result<Arg, ParseError> =
        skipNewlines p
        match (current p).Kind with
        | Ident _ when peekAt p 1 = Some Equals ->
            parseIdent p >>= fun name ->
                expect p Equals >>= fun _ ->
                    parseExpr p |> mapOk (fun value -> Named(name, value))
        | InternalIdent _ when peekAt p 1 = Some Equals ->
            err (current p) "named arguments cannot use internal identifiers"
        | _ ->
            parseExpr p |> mapOk (fun value -> Positional value)

    and parseParen (p: State) : Result<Expr, ParseError> =
        let startTok = advance p
        match (current p).Kind with
        | RParen ->
            let close = advance p
            Ok (mkExpr EUnit (mergeSpan startTok.Span close.Span))
        | Star | Plus | Minus | Slash ->
            let saved = p.Index
            let opTok = advance p
            if (current p).Kind = RParen then
                let close = advance p
                let name =
                    match opTok.Kind with
                    | Star -> "*"
                    | Plus -> "+"
                    | Minus -> "-"
                    | _ -> "/"
                let ident = { Name = name; IdentKind = User; Span = mergeSpan opTok.Span close.Span }
                Ok (mkExpr (EVar ident) (mergeSpan startTok.Span close.Span))
            else
                p.Index <- saved
                parseExpr p >>= fun inner ->
                    expect p RParen >>= fun _ -> Ok inner
        | _ ->
            let saved = p.Index
            let groupedExpr () =
                parseExpr p >>= fun first ->
                    if (current p).Kind = Comma then
                        let rec items (acc: Expr list) =
                            match (current p).Kind with
                            | Comma ->
                                advance p |> ignore
                                parseExpr p >>= fun item -> items (item :: acc)
                            | RParen ->
                                let close = advance p
                                Ok (mkExpr (ETuple(List.rev acc)) (mergeSpan startTok.Span close.Span))
                            | _ -> err (current p) "expected ',' or ')'"
                        items [ first ]
                    else
                        expect p RParen >>= fun _ -> Ok first
            match groupedExpr () with
            | Ok value -> Ok value
            | Error _ ->
                p.Index <- saved
                parseApplication p >>= fun first ->
                    expect p RParen >>= fun _ -> Ok first

    and parseList (p: State) : Result<Expr, ParseError> =
        let startTok = advance p
        let rec loop (acc: Expr list) =
            skipNewlines p
            match (current p).Kind with
            | RBracket ->
                let close = advance p
                Ok (mkExpr (EList(List.rev acc)) (mergeSpan startTok.Span close.Span))
            | _ ->
                parseExpr p >>= fun item ->
                    match (current p).Kind with
                    | Comma | Semicolon | Newline ->
                        advance p |> ignore
                        loop (item :: acc)
                    | RBracket ->
                        let close = advance p
                        Ok (mkExpr (EList(List.rev (item :: acc))) (mergeSpan startTok.Span close.Span))
                    | _ -> err (current p) "expected list separator or ']'"
        loop []

    and parseBlock (p: State) : Result<Expr, ParseError> =
        let startTok = advance p
        let rec loop (acc: Stmt list) =
            skipSeparators p
            match (current p).Kind with
            | RBrace ->
                let close = advance p
                Ok (mkExpr (EBlock(List.rev acc)) (mergeSpan startTok.Span close.Span))
            | Eof -> err (current p) "unterminated block"
            | _ ->
                parseStmt p >>= fun item -> loop (item :: acc)
        loop []

    /// Parse a type name. Maps reserved Idents `Scalar | Field | Sketch | Frame`
    /// to `Type.T`. Anything else is a parse error. Used by parenthesised-
    /// annotated parameter syntax `(x: Scalar)`.
    and parseType (p: State) : Result<Type.T, ParseError> =
        skipNewlines p
        let tok = current p
        match tok.Kind with
        | Ident "Scalar" -> advance p |> ignore; Ok Type.Scalar
        | Ident "Field"  -> advance p |> ignore; Ok Type.Field
        | Ident "Sketch" -> advance p |> ignore; Ok Type.Sketch
        | Ident "Frame"  -> advance p |> ignore; Ok Type.Frame
        | Ident name -> err tok (sprintf "unknown type '%s'" name)
        | _ -> err tok "expected type name (Scalar | Field | Sketch | Frame)"

    /// Parse a single lambda / let parameter. Accepted forms:
    ///   bareIdent              →  (ident, None)
    ///   ( ident )              →  (ident, None)
    ///   ( ident : TypeName )   →  (ident, Some ty)   — F#-style annotation
    and parseParam (p: State) : Result<Ident * Type.T option, ParseError> =
        skipNewlines p
        match (current p).Kind with
        | LParen ->
            advance p |> ignore
            parseBindingIdent p >>= fun id ->
                match (current p).Kind with
                | Colon ->
                    advance p |> ignore
                    parseType p >>= fun ty ->
                        expect p RParen |> mapOk (fun _ -> (id, Some ty))
                | RParen ->
                    advance p |> ignore
                    Ok (id, None)
                | _ -> err (current p) "expected ':' or ')' in parameter"
        | _ ->
            parseBindingIdent p |> mapOk (fun id -> (id, None))

    and parseFun (p: State) : Result<Expr, ParseError> =
        let rec collectParams (acc: (Ident * Type.T option) list) =
            skipNewlines p
            match (current p).Kind with
            | Arrow ->
                advance p |> ignore
                Ok (List.rev acc)
            | LParen ->
                parseParam p >>= fun par -> collectParams (par :: acc)
            | _ ->
                parseBindingIdent p >>= fun id -> collectParams ((id, None) :: acc)
        collectParams [] >>= fun ps ->
            skipNewlines p
            let bodyStart = tokenSpan p
            let bodyResult =
                match (current p).Kind with
                | LBrace -> parseBlock p
                | _ ->
                    let rec bodyItems (acc: Stmt list) =
                        skipSeparators p
                        match (current p).Kind with
                        | End ->
                            advance p |> ignore
                            Ok (List.rev acc)
                        | Eof -> err (current p) "unterminated function"
                        | _ ->
                            parseStmt p >>= fun item -> bodyItems (item :: acc)
                    bodyItems [] |> mapOk blockOrSingleExpr
            bodyResult >>= fun body ->
                let body = { body with Span = mergeSpan bodyStart body.Span }
                let ps =
                    match ps with
                    | [] -> [ ({ Name = "_"; IdentKind = User; Span = body.Span }, None) ]
                    | _ -> ps
                let lambda =
                    List.foldBack
                        (fun (param: Ident, ty: Type.T option) (acc: Expr) ->
                            mkExpr (ELambda(param, ty, acc)) (mergeSpan param.Span acc.Span))
                        ps body
                Ok lambda

    and blockOrSingleExpr (items: Stmt list) : Expr =
        match items with
        | [] -> mkExpr EUnit noneSpan
        | [ SExpr e ] -> e
        | _ ->
            let span =
                match items with
                | [] -> noneSpan
                | first :: _ -> mergeSpan (stmtSpan first) (stmtSpan (List.last items))
            mkExpr (EBlock items) span

    and parseMatch (p: State) : Result<Expr, ParseError> =
        parseExpr p >>= fun subject ->
            skipNewlines p
            expect p With >>= fun _ ->
                skipNewlines p
                if (current p).Kind = Bar then advance p |> ignore
                let rec loop (acc: MatchArm list) =
                    skipNewlines p
                    match (current p).Kind with
                    | End ->
                        advance p |> ignore
                        Ok (List.rev acc)
                    | Eof -> err (current p) "unterminated match"
                    | _ ->
                        parsePattern p >>= fun pat ->
                            expect p Arrow >>= fun _ ->
                                skipNewlines p
                                parseExpr p >>= fun body ->
                                    let arm = { Pattern = pat; Body = body }
                                    skipNewlines p
                                    if (current p).Kind = Bar then
                                        advance p |> ignore
                                        loop (arm :: acc)
                                    elif (current p).Kind = End then
                                        loop (arm :: acc)
                                    else
                                        Ok (List.rev (arm :: acc))
                loop [] >>= fun arms ->
                    Ok (mkExpr (EMatch(subject, arms)) (mergeSpan subject.Span (previousSpan p)))

    and parsePattern (p: State) : Result<Pattern, ParseError> =
        skipNewlines p
        match (current p).Kind with
        | Ident "_" ->
            let tok = advance p
            Ok (PWildcard tok.Span)
        | Ident name ->
            let tok = advance p
            let id = { Name = name; IdentKind = User; Span = tok.Span }
            if name = "Ok" || name = "Err" then
                let rec subs (acc: Pattern list) =
                    match (current p).Kind with
                    | Ident _ | InternalIdent _ | Integer _ | Float _ | String _ ->
                        parsePattern p >>= fun pat -> subs (pat :: acc)
                    | _ -> Ok (List.rev acc)
                subs [] |> mapOk (fun args -> PConstructor(id, args))
            else
                Ok (PBind id)
        | InternalIdent name ->
            let tok = advance p
            let id = { Name = name; IdentKind = Internal; Span = tok.Span }
            Ok (PBind id)
        | Integer value ->
            let tok = advance p
            Ok (PLiteral(LNumber(float value), tok.Span))
        | Float value ->
            let tok = advance p
            Ok (PLiteral(LNumber value, tok.Span))
        | String value ->
            let tok = advance p
            Ok (PLiteral(LString value, tok.Span))
        | LParen ->
            let startTok = advance p
            expect p RParen >>= fun close ->
                Ok (PLiteral(LUnit, mergeSpan startTok.Span close.Span))
        | _ -> err (current p) "expected pattern"

    and parseStmt (p: State) : Result<Stmt, ParseError> =
        skipNewlines p
        match (current p).Kind with
        | Import ->
            // `import <name>` — no default value, no `=`. The wire is
            // resolved by the notebook driver from the block's Inputs map.
            advance p |> ignore
            parseBindingIdent p >>= fun name ->
                if (current p).Kind = Equals then
                    err (current p) "import declarations cannot have a default value"
                else
                    Ok (SImport name)
        | Export ->
            // `export <name> = expr` — declares an output the notebook
            // driver collects after the block runs. The magic name `view`
            // routes to the view slot instead of the outputs list.
            let startTok = advance p
            parseBindingIdent p >>= fun name ->
                expect p Equals >>= fun _ ->
                    parseExpr p >>= fun value ->
                        let value = { value with Span = mergeSpan startTok.Span value.Span }
                        Ok (SExport(name, value))
        | Let ->
            let startTok = advance p
            parseBindingIdent p >>= fun firstName ->
                let rec collectNames (acc: Ident list) =
                    match (current p).Kind with
                    | Comma ->
                        advance p |> ignore
                        parseBindingIdent p >>= fun nm -> collectNames (nm :: acc)
                    | _ -> Ok (List.rev acc)
                collectNames [ firstName ] >>= fun names ->
                    expect p Equals >>= fun _ ->
                        parseExpr p >>= fun value ->
                            let value = { value with Span = mergeSpan startTok.Span value.Span }
                            Ok (SLet(names, value))
        | Dup ->
            let tok = advance p
            Ok (SDup tok.Span)
        | Swap ->
            let tok = advance p
            Ok (SSwap tok.Span)
        | Rotate ->
            let tok = advance p
            Ok (SRotate tok.Span)
        | _ -> parseExpr p |> mapOk SExpr

    let parseTokens (tokens: Token[]) : Result<Stmt list, ParseError> =
        let p = { Tokens = tokens; Index = 0 }
        let rec loop (acc: Stmt list) =
            skipSeparators p
            match (current p).Kind with
            | Eof -> Ok (List.rev acc)
            | _ ->
                parseStmt p >>= fun stmt ->
                    match (current p).Kind with
                    | Semicolon | Newline ->
                        skipSeparators p
                        loop (stmt :: acc)
                    | Eof -> loop (stmt :: acc)
                    | _ -> err (current p) "expected statement separator or end of input"
        loop []

    let parseProgram (source: string) : Result<Stmt list, ParseError> =
        match Lexer.tokenize source with
        | Error e -> Error { Message = e.Message; Span = e.Span }
        | Ok toks -> parseTokens toks
