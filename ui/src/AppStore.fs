module PointerMk18.Ui.AppStore

open Server
open PointerMk18.Ui

// ---------------------------------------------------------------------------
// Singleton store for the app. Lives in its own module so both F# UI code
// and TS viewer-bridge.ts can import the same instance.
//
// ES module semantics guarantee the `store` binding is created exactly
// once (on first import) and every subsequent import sees the same value.
// ---------------------------------------------------------------------------

let store = Store.create Editor.update (Editor.initState ())
