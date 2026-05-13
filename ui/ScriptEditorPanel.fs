module PointerMk18.Ui.ScriptEditorPanel

open Browser.Dom
open Browser.Types
open Fable.Core
open Fable.Core.JsInterop
open Server
open PointerMk18.Ui

// ---------------------------------------------------------------------------
// Script editor panel — CodeMirror 6 wrapper.
//
// The host `<div>` outlives Shell re-renders (Shell re-parents it on every
// render rather than rebuilding, matching the `viewerHost` pattern in
// Program.fs). The CodeMirror EditorView is created lazily on first open
// and survives for the lifetime of the page so undo history, scroll, and
// cursor are preserved across toggles.
//
// Text flow:
//   * Keystrokes → CodeMirror updateListener → debounced 250ms →
//     `dispatch (UpdateScriptSource _)`.
//   * External doc changes (file load, undo) → `syncSource` dispatches
//     a CodeMirror transaction with `suppressNextChange` flagged so we
//     don't echo the change back through the listener.
//
// Theme tracks the project's CSS variables (defined in `styles.css`) so
// the editor visually belongs in the cream/tan palette. Same vocabulary
// of variable names as the mk14 CodeMirror panel used.
// ---------------------------------------------------------------------------

// ── CodeMirror named imports ───────────────────────────────────────────────

// Factory functions return CodeMirror Extensions (opaque `obj`s once
// they leave the binding layer). Static objects (EditorView, EditorState,
// keymap, Prec) carry static methods we reach through `?` / `callOf`.
let private EditorView : obj = import "EditorView" "@codemirror/view"
let private keymap : obj = import "keymap" "@codemirror/view"
let private lineNumbers : unit -> obj = import "lineNumbers" "@codemirror/view"
let private highlightActiveLine : unit -> obj = import "highlightActiveLine" "@codemirror/view"
let private highlightActiveLineGutter : unit -> obj = import "highlightActiveLineGutter" "@codemirror/view"

let private EditorState : obj = import "EditorState" "@codemirror/state"
let private Prec : obj = import "Prec" "@codemirror/state"

let private defaultKeymap : obj = import "defaultKeymap" "@codemirror/commands"
let private history : unit -> obj = import "history" "@codemirror/commands"
let private historyKeymap : obj = import "historyKeymap" "@codemirror/commands"
let private toggleComment : obj = import "toggleComment" "@codemirror/commands"
let private indentWithTab : obj = import "indentWithTab" "@codemirror/commands"

let private syntaxHighlighting : obj -> obj = import "syntaxHighlighting" "@codemirror/language"
let private defaultHighlightStyle : obj = import "defaultHighlightStyle" "@codemirror/language"
let private bracketMatching : unit -> obj = import "bracketMatching" "@codemirror/language"
let private indentUnit : obj = import "indentUnit" "@codemirror/language"

// ── Small JS-shape helpers ────────────────────────────────────────────────

[<Emit("new $0($1)")>]
let private newEditorView (ctor: obj) (config: obj) : obj = jsNative

[<Emit("$0.of($1)")>]
let private callOf (target: obj) (arg: obj) : obj = jsNative

[<Emit("[$0, ...$1, ...$2]")>]
let private mergeKeymaps (first: obj) (a: obj) (b: obj) : obj array = jsNative

[<Emit("setTimeout($0, $1)")>]
let private setTimeout (cb: unit -> unit) (ms: float) : float = jsNative

[<Emit("clearTimeout($0)")>]
let private clearTimeout (handle: float) : unit = jsNative

let private DEBOUNCE_MS = 250.0

// ── Persistent state (survives Shell re-renders) ──────────────────────────

let mutable private host : HTMLElement option = None
let mutable private view : obj option = None
let mutable private currentSource : string = ""
let mutable private suppressNextChange : bool = false
let mutable private pendingDispatch : (Message -> unit) option = None
let mutable private debounceHandle : float option = None

let private ensureHost () : HTMLElement =
    match host with
    | Some h -> h
    | None ->
        let h = Dom.el "div" "script-editor-panel"
        host <- Some h
        h

let private fireUpdate (text: string) =
    match debounceHandle with
    | Some h -> clearTimeout h
    | None -> ()
    debounceHandle <-
        Some (setTimeout (fun () ->
            debounceHandle <- None
            match pendingDispatch with
            | Some d -> d (UpdateScriptSource text)
            | None -> ()) DEBOUNCE_MS)

// ── Theme — uses the project's CSS variables ──────────────────────────────

let private themeSpec : obj =
    createObj [
        "&" ==> createObj [
            "height" ==> "100%"
            "fontSize" ==> "13px"
            "backgroundColor" ==> "var(--bg-page)"
            "color" ==> "var(--text-primary)"
        ]
        ".cm-scroller" ==> createObj [
            "fontFamily" ==>
                "ui-monospace, \"SF Mono\", Menlo, Monaco, \"Cascadia Mono\", \"Roboto Mono\", Consolas, monospace"
            "lineHeight" ==> "1.5"
        ]
        ".cm-content" ==> createObj [
            "padding" ==> "8px 0"
            "caretColor" ==> "var(--text-primary)"
        ]
        ".cm-gutters" ==> createObj [
            "background" ==> "var(--bg-surface)"
            "borderRight" ==> "1px solid var(--border)"
            "color" ==> "var(--text-faint)"
        ]
        ".cm-activeLineGutter" ==> createObj [
            "background" ==> "var(--bg-surface-active)"
            "color" ==> "var(--text-secondary)"
        ]
        ".cm-activeLine" ==> createObj [
            "background" ==> "transparent"
        ]
        "&.cm-focused .cm-cursor" ==> createObj [
            "borderLeftColor" ==> "var(--text-primary)"
        ]
        "&.cm-focused .cm-selectionBackground, .cm-selectionBackground, ::selection" ==>
            createObj [ "background" ==> "var(--selection)" ]
        "&.cm-focused" ==> createObj [ "outline" ==> "none" ]
    ]

// ── Mount + sync ──────────────────────────────────────────────────────────

let private mountIfNeeded (container: HTMLElement) (initialSource: string) =
    if view.IsNone then
        let theme = EditorView?theme themeSpec

        let updateListener =
            callOf EditorView?updateListener (fun update ->
                if unbox<bool> update?docChanged then
                    if suppressNextChange then
                        suppressNextChange <- false
                    else
                        let text : string = update?state?doc?toString ()
                        currentSource <- text
                        fireUpdate text)

        let modKeymap =
            callOf keymap [|
                createObj [ "key" ==> "Mod-/"; "run" ==> toggleComment ]
            |]

        // Reuses the language-data hook the way mk14 did — gives the
        // commentTokens-aware Mod-/ binding a `// ...` line comment to
        // toggle even without a real language extension.
        let languageData =
            callOf EditorState?languageData (fun () ->
                [| createObj [ "commentTokens" ==> createObj [ "line" ==> "//" ] ] |])

        let extensions : obj array =
            [|
                lineNumbers ()
                highlightActiveLine ()
                highlightActiveLineGutter ()
                history ()
                bracketMatching ()
                syntaxHighlighting defaultHighlightStyle
                languageData
                callOf indentUnit "    "
                callOf EditorState?tabSize 4
                callOf keymap (mergeKeymaps indentWithTab defaultKeymap historyKeymap)
                Prec?high modKeymap
                theme
                updateListener
            |]

        let state =
            EditorState?create (createObj [
                "doc" ==> initialSource
                "extensions" ==> extensions
            ])

        currentSource <- initialSource
        let v =
            newEditorView
                EditorView
                (createObj [ "state" ==> state; "parent" ==> container ])
        view <- Some v

let private syncSource (sourceText: string) =
    if sourceText <> currentSource then
        currentSource <- sourceText
        match view with
        | Some v ->
            suppressNextChange <- true
            let docLen : int = unbox v?state?doc?length
            v?dispatch (createObj [
                "changes" ==> createObj [
                    "from" ==> 0
                    "to" ==> docLen
                    "insert" ==> sourceText
                ]
            ]) |> ignore
        | None -> ()

/// Build/refresh the panel. Returns the persistent host element. The
/// caller (Shell) handles the open/closed check — when
/// `ScriptEditorOpen` is false, Shell skips this call entirely.
let render (dispatch: Message -> unit) (sourceText: string) : HTMLElement =
    pendingDispatch <- Some dispatch
    let container = ensureHost ()
    mountIfNeeded container sourceText
    syncSource sourceText
    container
