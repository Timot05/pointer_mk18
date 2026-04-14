export type AppEvent = "document-dirty" | "viewer-state-dirty" | "viewer-model-dirty";

export interface AppEventHub {
  emit(event: AppEvent): void;
  subscribe(event: AppEvent, listener: () => void): () => void;
}

export function createAppEventHub(): AppEventHub {
  const listeners = new Map<AppEvent, Set<() => void>>();

  return {
    emit(event) {
      for (const listener of listeners.get(event) ?? []) listener();
    },
    subscribe(event, listener) {
      let bucket = listeners.get(event);
      if (!bucket) {
        bucket = new Set();
        listeners.set(event, bucket);
      }
      bucket.add(listener);
      return () => {
        bucket?.delete(listener);
      };
    },
  };
}
