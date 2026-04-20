import { defineConfig } from "vite";
import path from "path";
import { fileURLToPath } from "url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

export default defineConfig({
  resolve: {
    alias: {
      "@shaders": path.resolve(__dirname, "../viewer/Shaders"),
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
