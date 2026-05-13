namespace Server.Lang

// ---------------------------------------------------------------------------
// Token.fs — port of pointer_mk19/compiler/lib/token.ml.
//
// Tokens carry a span (byte offsets into the source string) so the parser
// and evaluator can report errors with precise locations. Newlines are kept
// as tokens because they're significant for statement separation.
// ---------------------------------------------------------------------------

module Token =

    type Span = { Start: int; Stop: int }

    let mkSpan s e = { Start = s; Stop = e }

    /// Token kind. Order matches token.ml. Carries the lexeme value for
    /// idents/literals; punctuation kinds are nullary cases.
    type Kind =
        | Newline
        | Let
        | Import
        | Export
        | Open
        | Publish
        | Fun
        | Module
        | Dup
        | Pop
        | Swap
        | Rotate
        | End
        | Match
        | With
        | Arrow
        | Plus
        | Minus
        | Dot
        | Equals
        | Semicolon
        | Comma
        | Question
        | Bang
        | Star
        | Slash
        | Pipe
        | Bar
        | Compose
        | Colon
        | Ident of string
        | InternalIdent of string
        | Float of float
        | Integer of int
        | String of string
        | LParen
        | RParen
        | LBrace
        | RBrace
        | LBracket
        | RBracket
        | Eof

    type Token = { Kind: Kind; Span: Span }

    let kindName (k: Kind) : string =
        match k with
        | Newline -> "newline"
        | Let -> "let"
        | Import -> "import"
        | Export -> "export"
        | Open -> "open"
        | Publish -> "publish"
        | Fun -> "fun"
        | Module -> "module"
        | Dup -> "dup"
        | Pop -> "pop"
        | Swap -> "swap"
        | Rotate -> "rotate"
        | End -> "end"
        | Match -> "match"
        | With -> "with"
        | Arrow -> "->"
        | Plus -> "+"
        | Minus -> "-"
        | Dot -> "."
        | Equals -> "="
        | Semicolon -> ";"
        | Comma -> ","
        | Question -> "?"
        | Bang -> "!"
        | Star -> "*"
        | Slash -> "/"
        | Pipe -> "|>"
        | Bar -> "|"
        | Compose -> ">>"
        | Colon -> ":"
        | Ident s -> "identifier " + s
        | InternalIdent s -> "internal identifier @" + s
        | Float _ -> "float"
        | Integer _ -> "integer"
        | String _ -> "string"
        | LParen -> "("
        | RParen -> ")"
        | LBrace -> "{"
        | RBrace -> "}"
        | LBracket -> "["
        | RBracket -> "]"
        | Eof -> "end of input"
