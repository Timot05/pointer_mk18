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
//
// `infer: sphere spec body has type ...` and the `union` analog used to
// elaborate `BlockSpec.find "sphere/union".Body` against `emptyEnv`.
// After the BlockSpec→script migration, those bodies reference math
// primitives (`sqrt`, `min`, `max`) and axes (`x`, `y`, `z`) that need
// to be in scope, so a bare `emptyEnv` elaboration doesn't work. The
// same "param list / output" contract is verified end-to-end in
// LangTests' `sphere extracts as [radius:Scalar] -> Field` (which reads
// the already-resolved `UserScript.UserSpec.Params` / `.Output`).
// Translate stayed intrinsic, so its dedicated test still exercises
// the BlockSpec path.

[<Fact>]
let ``infer: translate spec body has type Scalar -> Scalar -> Scalar -> Field -> Field`` () =
    let spec = BlockSpec.find "translate"
    let t = inferOk emptyEnv spec.Body
    let expected =
        Type.curried
            [ Type.Scalar; Type.Scalar; Type.Scalar; Type.Field ]
            Type.Field
    Assert.Equal(expected, t.Type)

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
let ``error: applying a Scalar -> Field function to a Field argument fails TypeMismatch`` () =
    // Bind a synthetic `f : Scalar -> Field` and a Field-typed `x` in
    // the env; applying `f x` should error because Field is not <: Scalar
    // (only the reverse direction lifts, per `Type.isSubtypeOf`).
    let env =
        Map.empty
        |> Map.add "f" (Type.Fun(Type.Scalar, Type.Field))
        |> Map.add "x" Type.Field
    let src = "f x"
    match Parser.parseProgram src with
    | Ok [ Ast.SExpr e ] ->
        let errors = inferErr env e
        match errors with
        | [ Typecheck.TypeMismatch(Type.Scalar, Type.Field, _) ] -> ()
        | other -> failwithf "unexpected errors: %A" other
    | other -> failwithf "parse: unexpected %A" other

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
