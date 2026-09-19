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
    rolldownOptions: {
      output: {
        codeSplitting: {
          groups: [
            { name: "vendor-three", test: /node_modules[\\/]three[\\/]/, priority: 30 },
            { name: "vendor-react", test: /node_modules[\\/](?:react|react-dom|react-router|react-router-dom|scheduler|@tanstack|@remix-run)[\\/]/, priority: 20 },
            { name: "vendor-icons", test: /node_modules[\\/]lucide-react[\\/]/, priority: 10 },
            { name: "vendor", test: /node_modules[\\/]/ },
          ],
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
