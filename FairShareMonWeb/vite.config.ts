/// <reference types="vitest/config" />
import { fileURLToPath, URL } from "node:url";
import { defineConfig, loadEnv } from "vite";
import react, { reactCompilerPreset } from "@vitejs/plugin-react";
import babel from "@rolldown/plugin-babel";

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  // VITE_API_BASE_URL is the dev proxy TARGET only (client code calls /api).
  const env = loadEnv(mode, process.cwd(), "");
  const proxyTarget = env.VITE_API_BASE_URL || "http://localhost:5200";

  return {
    plugins: [react(), babel({ presets: [reactCompilerPreset()] })],
    resolve: {
      alias: {
        "@": fileURLToPath(new URL("./src", import.meta.url)),
      },
    },
    server: {
      // Front-end calls same-origin /api/*; Vite proxies to the backend so there
      // is zero CORS/preflight friction in dev (OQ9a). Prod uses VITE_API_BASE_URL.
      proxy: {
        "/api": {
          target: proxyTarget,
          changeOrigin: true,
        },
      },
    },
    test: {
      environment: "jsdom",
      globals: true,
      setupFiles: ["./src/test/setup.ts"],
      css: true,
    },
  };
});
