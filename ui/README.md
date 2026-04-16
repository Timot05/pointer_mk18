# ui — F# frontend

Incremental port of the TS `user-interface/` to F# via Fable.

## Run

```bash
cd ui
dotnet tool restore      # installs Fable
npm install              # installs Vite
npm start                # dotnet fable watch + vite dev
```

Opens on http://localhost:5176 (see `vite.config.ts`).

## Layout

- `src/Store.fs` — tiny pub/sub store, F# side of what TS had in `app/src/store.ts`
- `src/Program.fs` — entry: mounts F# editor, subscribes, renders

The F# core lives in `../core/` and is referenced via project reference in
`Ui.fsproj`. `dotnet fable src` compiles both into `src-gen/`.
