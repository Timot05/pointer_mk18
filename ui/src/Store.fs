module PointerMk18.Ui.Store

/// Tiny mutable pub/sub store. Holds the current state, a reduce function,
/// and a list of subscriber callbacks. Every dispatch runs the reducer and
/// notifies all subscribers.
///
/// This mirrors app/src/store.ts but lives in F# so the UI layer can
/// consume F# values directly — no Fable-union normalization step.
type Store<'state, 'message> =
    { mutable State: 'state
      Reduce: 'message -> 'state -> 'state
      mutable Subscribers: (unit -> unit) list }

let create (reduce: 'message -> 'state -> 'state) (init: 'state) : Store<'state, 'message> =
    { State = init
      Reduce = reduce
      Subscribers = [] }

let dispatch (store: Store<_, _>) (message: 'message) =
    store.State <- store.Reduce message store.State
    for sub in store.Subscribers do
        sub ()

let subscribe (store: Store<_, _>) (fn: unit -> unit) =
    store.Subscribers <- fn :: store.Subscribers
