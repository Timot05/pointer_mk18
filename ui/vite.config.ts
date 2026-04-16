import { defineConfig } from "vite";

export default defineConfig({
  server: {
    port: 5176,
    fs: {
      // Fable emits to src-gen/ and may reference packages one level up.
      allow: [".."],
    },
  },
});
