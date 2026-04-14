import { defineConfig } from "vite";
import { resolve } from "node:path";

export default defineConfig({
  server: {
    port: 5175,
    proxy: {
      "/api": "http://localhost:5222",
    },
    fs: {
      allow: [
        resolve(__dirname, ".."),
      ],
    },
  },
});
