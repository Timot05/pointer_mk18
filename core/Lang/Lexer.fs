namespace Server.Lang

// ---------------------------------------------------------------------------
// Lexer.fs — hand-rolled state machine port of pointer_mk19/lib/lexer.mll.
//
// Reads a UTF-8 source string, returns Token.Token[]. Errors carry a span
// for editor highlighting later. Newlines are emitted as a single Newline
// token regardless of how many `\n`s appear in a row (matches OCaml's
// `'\n'+` regex with a single token output).
//
// Block comments do NOT nest — matching the .mll's literal `*/` terminator.
// ---------------------------------------------------------------------------

module Lexer =

    open Token

    type LexError = { Message: string; Span: Span }

    let private isDigit c = c >= '0' && c <= '9'

    let private isAlpha c =
        (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c = '_'

    let private isAlphaNum c = isAlpha c || isDigit c

    let private keywordOrIdent (text: string) : Kind =
        match text with
        | "let" -> Let
        | "import" -> Import
        | "export" -> Export
        | "open" -> Open
        | "publish" -> Publish
        | "fun" -> Fun
        | "module" -> Module
        | "dup" -> Dup
        | "pop" -> Pop
        | "swap" -> Swap
        | "rotate" -> Rotate
        | "end" -> End
        | "match" -> Match
        | "with" -> With
        | _ -> Ident text

    /// Decode the body of a string literal (without the surrounding quotes).
    /// Supports \n \r \t \" \\ ; any other `\c` keeps `c` verbatim, matching
    /// the .mll's catch-all branch.
    let private unescape (body: string) : string =
        let sb = System.Text.StringBuilder()
        let len = body.Length
        let mutable i = 0
        while i < len do
            if body.[i] = '\\' && i + 1 < len then
                let c =
                    match body.[i + 1] with
                    | 'n' -> '\n'
                    | 'r' -> '\r'
                    | 't' -> '\t'
                    | '"' -> '"'
                    | '\\' -> '\\'
                    | other -> other
                sb.Append(c) |> ignore
                i <- i + 2
            else
                sb.Append(body.[i]) |> ignore
                i <- i + 1
        sb.ToString()

    let private startsWith (s: string) (i: int) (probe: string) : bool =
        let pl = probe.Length
        if i + pl > s.Length then false
        else
            let mutable k = 0
            let mutable ok = true
            while ok && k < pl do
                if s.[i + k] <> probe.[k] then ok <- false
                k <- k + 1
            ok

    let tokenize (source: string) : Result<Token[], LexError> =
        let tokens = ResizeArray<Token>()
        let len = source.Length
        let mutable i = 0
        let mutable err : LexError option = None

        let push (start: int) (kind: Kind) =
            tokens.Add({ Kind = kind; Span = { Start = start; Stop = i } })

        let fail (start: int) (stop: int) (msg: string) =
            err <- Some { Message = msg; Span = { Start = start; Stop = stop } }

        while i < len && err.IsNone do
            let start = i
            let c = source.[i]

            // Whitespace & line-internal noise
            if c = ' ' || c = '\t' || c = '\012' || c = '\r' then
                i <- i + 1

            // Newline run → single Newline token
            elif c = '\n' then
                while i < len && source.[i] = '\n' do
                    i <- i + 1
                push start Newline

            // Line comment "// ... <eol>"
            elif startsWith source i "//" then
                i <- i + 2
                while i < len && source.[i] <> '\n' do
                    i <- i + 1

            // Block comment "/* ... */", non-nesting
            elif startsWith source i "/*" then
                i <- i + 2
                let mutable closed = false
                while not closed && i < len do
                    if i + 1 < len && source.[i] = '*' && source.[i + 1] = '/' then
                        i <- i + 2
                        closed <- true
                    else
                        i <- i + 1
                if not closed then
                    fail start i "unterminated block comment"

            // Two-char operators
            elif startsWith source i "->" then i <- i + 2; push start Arrow
            elif startsWith source i "|>" then i <- i + 2; push start Pipe
            elif startsWith source i ">>" then i <- i + 2; push start Compose

            // Single-char punctuation
            elif c = '+' then i <- i + 1; push start Plus
            elif c = '-' then i <- i + 1; push start Minus
            elif c = '=' then i <- i + 1; push start Equals
            elif c = ';' then i <- i + 1; push start Semicolon
            elif c = ',' then i <- i + 1; push start Comma
            elif c = '?' then i <- i + 1; push start Question
            elif c = '!' then i <- i + 1; push start Bang
            elif c = '*' then i <- i + 1; push start Star
            elif c = '/' then i <- i + 1; push start Slash
            elif c = '|' then i <- i + 1; push start Bar
            elif c = '(' then i <- i + 1; push start LParen
            elif c = ')' then i <- i + 1; push start RParen
            elif c = '{' then i <- i + 1; push start LBrace
            elif c = '}' then i <- i + 1; push start RBrace
            elif c = '[' then i <- i + 1; push start LBracket
            elif c = ']' then i <- i + 1; push start RBracket

            // `.` either starts a `.5`-style float or is the Dot token
            elif c = '.' && i + 1 < len && isDigit source.[i + 1] then
                i <- i + 1
                while i < len && isDigit source.[i] do i <- i + 1
                if i < len && (source.[i] = 'e' || source.[i] = 'E') then
                    i <- i + 1
                    if i < len && (source.[i] = '+' || source.[i] = '-') then
                        i <- i + 1
                    while i < len && isDigit source.[i] do i <- i + 1
                let text = source.Substring(start, i - start)
                push start (Float (System.Double.Parse(text, System.Globalization.CultureInfo.InvariantCulture)))

            elif c = '.' then i <- i + 1; push start Dot

            // String literal
            elif c = '"' then
                i <- i + 1
                let bodyStart = i
                let mutable closed = false
                while not closed && i < len do
                    if source.[i] = '"' then
                        closed <- true
                    elif source.[i] = '\\' && i + 1 < len then
                        i <- i + 2
                    else
                        i <- i + 1
                if not closed then
                    fail start i "unterminated string literal"
                else
                    let body = source.Substring(bodyStart, i - bodyStart)
                    i <- i + 1   // consume closing quote
                    push start (String (unescape body))

            // Internal ident: '@' alpha (alpha|digit)*
            elif c = '@' then
                if i + 1 >= len || not (isAlpha source.[i + 1]) then
                    fail start (i + 1) "expected identifier after '@'"
                else
                    i <- i + 1
                    let nameStart = i
                    while i < len && isAlphaNum source.[i] do
                        i <- i + 1
                    let name = source.Substring(nameStart, i - nameStart)
                    push start (InternalIdent name)

            // Number: digit+ optional `.digit*` optional `[eE][+-]?digit+`
            elif isDigit c then
                while i < len && isDigit source.[i] do i <- i + 1
                let mutable isFloat = false
                if i < len && source.[i] = '.' then
                    isFloat <- true
                    i <- i + 1
                    while i < len && isDigit source.[i] do i <- i + 1
                if i < len && (source.[i] = 'e' || source.[i] = 'E') then
                    isFloat <- true
                    i <- i + 1
                    if i < len && (source.[i] = '+' || source.[i] = '-') then
                        i <- i + 1
                    while i < len && isDigit source.[i] do i <- i + 1
                let text = source.Substring(start, i - start)
                if isFloat then
                    push start (Float (System.Double.Parse(text, System.Globalization.CultureInfo.InvariantCulture)))
                else
                    push start (Integer (System.Int32.Parse(text, System.Globalization.CultureInfo.InvariantCulture)))

            // Identifier or keyword
            elif isAlpha c then
                while i < len && isAlphaNum source.[i] do i <- i + 1
                let text = source.Substring(start, i - start)
                push start (keywordOrIdent text)

            else
                fail start (i + 1) (sprintf "unexpected character '%c'" c)

        match err with
        | Some e -> Error e
        | None ->
            tokens.Add({ Kind = Eof; Span = { Start = i; Stop = i } })
            Ok (tokens.ToArray())
