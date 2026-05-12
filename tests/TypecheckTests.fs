module TypecheckTests

open Xunit
open Server.Lang

// ─── Helpers ────────────────────────────────────────────────────────────────

let private emptyEnv : Typecheck.TypeEnv = Map.empty

let private inferOk (env: Typecheck.TypeEnv) (expr: Ast.Expr) : Tast.TExpr =
    match Typecheck.elaborate env expr with
    | Ok t -> t
    | Error es ->
        let msg = es |> List.map Typecheck.formatError |> String.concat "; "
        failwithf "expected ok, got errors: %s" msg

let private inferErr (env: Typecheck.TypeEnv) (expr: Ast.Expr) : Typecheck.TypeError list =
    match Typecheck.elaborate env expr with
    | Ok t -> failwithf "expected error, got %A : %s" t.Node (Type.format t.Type)
    | Error es -> es

// ─── Native specs round-trip through the typechecker ───────────────────────

[<Fact>]
let ``infer: sphere spec body has type Scalar -> Field`` () =
    let spec = BlockSpec.find "sphere"
    let t = inferOk emptyEnv spec.Body
    Assert.Equal(Type.Fun(Type.Scalar, Type.Field), t.Type)

[<Fact>]
let ``infer: translate spec body has type Scalar -> Scalar -> Scalar -> Field -> Field`` () =
    let spec = BlockSpec.find "translate"
    let t = inferOk emptyEnv spec.Body
    let expected =
        Type.curried
            [ Type.Scalar; Type.Scalar; Type.Scalar; Type.Field ]
            Type.Field
    Assert.Equal(expected, t.Type)

[<Fact>]
let ``infer: union spec body has type Field -> Field -> Scalar -> Field`` () =
    let spec = BlockSpec.find "union"
    let t = inferOk emptyEnv spec.Body
    Assert.Equal(
        Type.Fun(Type.Field, Type.Fun(Type.Field, Type.Fun(Type.Scalar, Type.Field))),
        t.Type)

// ─── Error cases ───────────────────────────────────────────────────────────

[<Fact>]
let ``error: unannotated lambda in infer position fails MissingTypeAnnotation`` () =
    // `fun x -> x + 1 end` parsed via the surface lexer/parser leaves
    // the param annotation unset, so plain inference should error.
    let src = "fun x -> x + 1 end"
    match Parser.parseProgram src with
    | Ok [ Ast.SExpr e ] ->
        let errors = inferErr emptyEnv e
        match errors with
        | [ Typecheck.MissingTypeAnnotation("x", _) ] -> ()
        | other -> failwithf "unexpected errors: %A" other
    | other -> failwithf "parse: unexpected %A" other

[<Fact>]
let ``error: applying sphere to a Field-typed argument fails TypeMismatch`` () =
    // Apply the sphere spec to an axis (which is Field, not Scalar) —
    // saturating with a wrongly-typed argument should error.
    let spec = BlockSpec.find "sphere"
    let bogusApp =
        Ast.mkExpr (Ast.EApply(spec.Body, Ast.mkExpr (Ast.EAxis Ast.AxisX) Ast.noneSpan))
            Ast.noneSpan
    let errors = inferErr emptyEnv bogusApp
    match errors with
    | [ Typecheck.TypeMismatch(Type.Scalar, Type.Field, _) ] -> ()
    | other -> failwithf "unexpected errors: %A" other

[<Fact>]
let ``error: undefined identifier surfaces`` () =
    let src = "missingThing"
    match Parser.parseProgram src with
    | Ok [ Ast.SExpr e ] ->
        let errors = inferErr emptyEnv e
        match errors with
        | [ Typecheck.UndefinedVar("missingThing", _) ] -> ()
        | other -> failwithf "unexpected errors: %A" other
    | other -> failwithf "parse: unexpected %A" other

[<Fact>]
let ``check: lambda without annotation in check-mode borrows the expected type`` () =
    // `fun x -> x + 1.0 end`, checked against `Scalar -> Scalar`. The
    // missing annotation is fine because the surrounding context tells us
    // x : Scalar.
    let src = "fun x -> x + 1.0 end"
    match Parser.parseProgram src with
    | Ok [ Ast.SExpr e ] ->
        let expected = Type.Fun(Type.Scalar, Type.Scalar)
        match Typecheck.check Map.empty expected e with
        | Ok t -> Assert.Equal(expected, t.Type)
        | Error es ->
            failwithf "expected ok, got: %A" es
    | other -> failwithf "parse: unexpected %A" other

[<Fact>]
let ``check: annotation mismatch surfaces AnnotationConflict`` () =
    // Hand-built lambda annotated with Scalar checked against Field-typed
    // expectation. (Not parseable from surface syntax yet — build by hand.)
    let p = { Ast.Name = "p"; Ast.IdentKind = Ast.User; Ast.Span = Ast.noneSpan }
    let body = Ast.mkExpr (Ast.EVar p) Ast.noneSpan
    let lambda = Ast.mkExpr (Ast.ELambda(p, Some Type.Scalar, body)) Ast.noneSpan
    let expected = Type.Fun(Type.Field, Type.Field)
    match Typecheck.check Map.empty expected lambda with
    | Ok _ -> failwith "expected error"
    | Error errs ->
        match errs with
        | [ Typecheck.AnnotationConflict(Type.Scalar, Type.Field, _) ] -> ()
        | other -> failwithf "unexpected errors: %A" other

// ─── Env-bound primitives, mimicking the future NotebookCompose flow ───────

[<Fact>]
let ``env: an EApply through an env-bound primitive resolves correctly`` () =
    // Seed env with `mySphere : Scalar -> Field` and check that
    // `mySphere 1.5` has type Field.
    let env =
        Map.ofList [ "mySphere", Type.Fun(Type.Scalar, Type.Field) ]
    let mySphere =
        { Ast.Name = "mySphere"
          Ast.IdentKind = Ast.User
          Ast.Span = Ast.noneSpan }
    let call =
        Ast.mkExpr
            (Ast.EApply(
                Ast.mkExpr (Ast.EVar mySphere) Ast.noneSpan,
                Ast.mkExpr (Ast.ENumber 1.5) Ast.noneSpan))
            Ast.noneSpan
    let t = inferOk env call
    Assert.Equal(Type.Field, t.Type)
