import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

const apiTarget =
  process.env.EXPORTDOC_DEV_API_PROXY_TARGET ??
  process.env.VITE_EXPORTDOC_API_BASE_URL ??
  "http://127.0.0.1:5188";

export default defineConfig({
  plugins: [react()],
  build: {
    chunkSizeWarningLimit: 600,
    rollupOptions: {
      output: {
        onlyExplicitManualChunks: true,
        manualChunks(id) {
          if (!id.includes("node_modules")) {
            return undefined;
          }

          if (id.includes("three")) {
            return "vendor-three";
          }

          if (id.includes("html2canvas")) {
            return undefined;
          }

          if (
            id.includes("jspdf") ||
            id.includes("canvg") ||
            id.includes("dompurify") ||
            id.includes("fflate") ||
            id.includes("fast-png") ||
            id.includes("rgbcolor") ||
            id.includes("svg-pathdata") ||
            id.includes("stackblur-canvas")
          ) {
            return undefined;
          }

          if (id.includes("lucide-react")) {
            return "vendor-icons";
          }

          if (
            id.includes("react") ||
            id.includes("react-dom") ||
            id.includes("react-router-dom") ||
            id.includes("@remix-run") ||
            id.includes("@tanstack")
          ) {
            return "vendor-react";
          }

          return "vendor";
        },
      },
    },
  },
  server: {
    host: "127.0.0.1",
    port: 5173,
    proxy: {
      "/api": apiTarget,
      "/healthz": apiTarget,
      "/readyz": apiTarget,
      "/openapi": apiTarget,
    },
  },
});
