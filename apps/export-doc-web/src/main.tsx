import React from "react";
import ReactDOM from "react-dom/client";
import { QueryClientProvider } from "@tanstack/react-query";
import { HashRouter } from "react-router-dom";
import App from "./App.tsx";
import { installFrontendErrorLogger } from "./desktop/frontendErrorLogger.ts";
import { queryClient } from "./queryClient.ts";
import { FrontendErrorBoundary } from "./ui/FrontendErrorBoundary.tsx";
import { UnsavedChangesProvider } from "./ui/unsavedChangesGuard.tsx";
import "./styles.css";
import "./theme.css";

installFrontendErrorLogger();

ReactDOM.createRoot(document.getElementById("root") as HTMLElement).render(
  <React.StrictMode>
    <QueryClientProvider client={queryClient}>
      <HashRouter>
        <UnsavedChangesProvider>
          <FrontendErrorBoundary>
            <App />
          </FrontendErrorBoundary>
        </UnsavedChangesProvider>
      </HashRouter>
    </QueryClientProvider>
  </React.StrictMode>,
);
