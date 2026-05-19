import { defineConfig } from "vite";
import path from "path";
import { fileURLToPath } from "url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

// Monaco workers are registered via `ui/monaco-setup.ts` using Vite's
// native `?worker` import — see that file. No plugin needed.

export default defineConfig({
  resolve: {
    alias: {
      "@shaders": path.resolve(__dirname, "../viewer/Shaders"),
      "@defaults": path.resolve(__dirname, "./defaults"),
    },
  },
  server: {
    port: 5176,
    fs: {
      // Fable emits to src-gen/ and may reference packages one level up.
      allow: [".."],
    },
  },
});
