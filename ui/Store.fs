module PointerMk18.Ui.Store

/// Tiny mutable pub/sub store. Holds the current state, a reduce function,
/// an effect runner, and a list of subscriber callbacks. Every dispatch runs
/// the reducer, executes emitted effects, and notifies all subscribers.
type Store<'state, 'message> =
    { mutable State: 'state
      Reduce: 'message -> 'state -> 'state * Server.Effect list
      RunEffect: Store<'state, 'message> -> Server.Effect -> unit
      mutable Subscribers: (unit -> unit) list }

let create
    (reduce: 'message -> 'state -> 'state * Server.Effect list)
    (runEffect: Store<'state, 'message> -> Server.Effect -> unit)
    (init: 'state)
    : Store<'state, 'message> =
    { State = init
      Reduce = reduce
      RunEffect = runEffect
      Subscribers = [] }

let dispatch (store: Store<_, _>) (message: 'message) =
    let nextState, effects = store.Reduce message store.State
    store.State <- nextState
    for effect in effects do
        store.RunEffect store effect
    for sub in store.Subscribers do
        sub ()

let subscribe (store: Store<_, _>) (fn: unit -> unit) =
    store.Subscribers <- fn :: store.Subscribers
