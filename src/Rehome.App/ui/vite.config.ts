import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// The app serves these files from memory under a strict CSP: no inline scripts, nothing remote.
export default defineConfig({
  plugins: [react()],
  base: "./",
  build: {
    outDir: "dist",
    emptyOutDir: true,
    modulePreload: { polyfill: false },
    sourcemap: false,
  },
  server: { port: 5199, strictPort: true },
});
