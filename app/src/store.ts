export type Listener = () => void;

export interface EditorStore<TState, TMessage> {
  getState(): TState;
  dispatch(message: TMessage): void;
  subscribe(listener: Listener): () => void;
}

export function createStore<TState, TMessage>(
  initialState: TState,
  update: (message: TMessage, state: TState) => TState,
): EditorStore<TState, TMessage> {
  let state = initialState;
  const listeners = new Set<Listener>();

  return {
    getState() {
      return state;
    },
    dispatch(message) {
      const next = update(message, state);
      if (Object.is(next, state)) return;
      state = next;
      for (const listener of listeners) listener();
    },
    subscribe(listener) {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
  };
}
