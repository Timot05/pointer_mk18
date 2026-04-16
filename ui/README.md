# ui — frontend

The whole app. F# (via Fable) owns state, messages, and the sidebar UI.
A TypeScript WebGPU viewer is mounted into the center panel and talks to
the F# store through a single bridge file.

## Run

```bash
cd ui
dotnet tool restore      # installs Fable
npm install              # installs Vite + @carbon/icons
npm start                # dotnet fable watch + vite dev
```

Opens on http://localhost:5176 (see `vite.config.ts`).

## Layout

```
ui/
├── src/                    # F# sources (Fable-compiled)
│   ├── Ui.fsproj           # project reference → ../../core/Core.fsproj
│   ├── Store.fs            # generic pub/sub store
│   ├── AppStore.fs         # singleton store instance
│   ├── ViewerMessages.fs   # typed Message factories exported to TS
│   ├── Dom.fs              # el / elText / kbdHint / setupDraggable
│   ├── Icons.fs            # Carbon icon rendering
│   ├── TopBar.fs           # file menu
│   ├── ActionList.fs       # left panel: action list, drag-reorder
│   ├── ParamsPanel.fs      # right panel: properties, display, field slice
│   ├── SketchOverlay.fs    # sketch authoring toolbar + constraints
│   ├── CommandPalette.fs   # ⌘K palette (mounted to body)
│   ├── Shortcuts.fs        # global keyboard handler
│   ├── Shell.fs            # three-panel layout
│   ├── Program.fs          # entry point
│   └── viewer-bridge.ts    # the only TS↔F# boundary the viewer touches
├── viewer/                 # TypeScript WebGPU viewer
│   ├── viewer.ts           # main render loop + pick + camera + sketch render
│   ├── mount.ts            # attaches the viewer to a host div (shadow DOM)
│   ├── pipeline-*.ts       # WebGPU pipelines (isosurface, field slice, MSDF)
│   ├── api.ts              # plain TS types for normalized viewer payloads
│   └── …
├── src-gen/                # Fable output (gitignored)
├── index.html
├── package.json
└── vite.config.ts
```

## Data flow

`F# reducer ← Store.dispatch ← { F# UI dispatches, TS viewer dispatches via viewer-bridge }`

After every dispatch, the F# shell re-renders its DOM and
`viewer-bridge.ts` diffs `Doc`/`Compiled` on the state to decide whether
to notify viewer-model listeners (expensive GPU rebuild) or
viewer-state listeners (cheap re-render). The viewer's WebGPU canvas
stays alive across shell re-renders because `viewerHost` is created once
in `Program.fs` and re-parented on each render.

Sketch solving now also runs through the same F# store:
- core emits `RunSketchSolve`
- `AppStore.fs` runs the GPU solver and dispatches the solved result back
- `viewer.ts` only renders `ViewerState.params` and local drag previews

## Build

`dotnet fable src --outDir src-gen` compiles everything under `src/` (and the
referenced `../core/` project) to ES modules under `src-gen/`. Vite then
bundles those plus the `viewer/` TypeScript at dev/build time.
