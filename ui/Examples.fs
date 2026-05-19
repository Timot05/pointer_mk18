module PointerMk18.Ui.Examples

// Bundle of example scenes shipped in `ui/defaults/examples/`.
// Vite intercepts `import.meta.glob` at bundle time, walks the
// matched files, and inlines each as a raw string (via the `?raw`
// query). Eager mode resolves synchronously so `bundle` is a plain
// `Record<string, string>` keyed by file path. We walk it into a
// sorted (label, content) list for the top-bar dropdown.
//
// The glob must be a literal string and the option object must be
// an object literal at the call site — Vite static-analyses both at
// build time. `emitJsExpr` emits them verbatim into the generated
// JS so the literal-ness is preserved.

open Fable.Core
open Fable.Core.JsInterop

#if FABLE_COMPILER
let private bundle : obj =
    emitJsExpr () """import.meta.glob('@defaults/examples/*.json', { eager: true, query: '?raw', import: 'default' })"""

[<Emit("Object.entries($0)")>]
let private entries (o: obj) : (string * obj)[] = jsNative

/// Strip the leading directory and the `.json` extension so a file
/// `./examples/aircraft.json` shows up as `aircraft` in the menu.
let private labelOf (path: string) : string =
    let baseName =
        let lastSlash = path.LastIndexOf '/'
        if lastSlash >= 0 then path.Substring(lastSlash + 1) else path
    if baseName.EndsWith ".json"
    then baseName.Substring(0, baseName.Length - 5)
    else baseName

/// `(label, raw-json-string)` for every example file. Sorted by
/// label so the dropdown order is stable.
let all () : (string * string) list =
    entries bundle
    |> Array.toList
    |> List.map (fun (path, content) -> labelOf path, unbox<string> content)
    |> List.sortBy fst
#else
let all () : (string * string) list = []
#endif
