namespace Server.Lang

// ---------------------------------------------------------------------------
// AstQueries.fs — pure traversals over a parsed `Stmt list`.
//
// The notebook driver uses these to discover a block's I/O contract
// structurally: imports come from `import x` declarations, outputs from
// `export x = ...`. Inline-input UI surfaces and any future static analysis
// can reuse the same helpers.
//
// Only top-level statements are inspected — `export` / `import` declarations
// nested inside a `{ ... }` block expression are intentionally ignored.
// Raise the visibility you want by lifting the binding to the top level.
// ---------------------------------------------------------------------------

module AstQueries =

    open Ast

    /// Top-level imports, in source order.
    let collectImports (stmts: Stmt list) : Ident list =
        stmts |> List.choose (function SImport id -> Some id | _ -> None)

    /// Top-level export bindings. If the same name is exported more than once,
    /// keep the *last* declaration — eval semantics are last-write-wins for
    /// shadowing, and the driver reads the value out of the post-eval env
    /// after all statements have run.
    let collectExports (stmts: Stmt list) : Ident list =
        let seen = System.Collections.Generic.Dictionary<string, Ident>()
        let order = ResizeArray<string>()
        for s in stmts do
            match s with
            | SExport(name, _) ->
                if not (seen.ContainsKey name.Name) then
                    order.Add name.Name
                seen.[name.Name] <- name
            | _ -> ()
        order |> Seq.map (fun n -> seen.[n]) |> List.ofSeq
