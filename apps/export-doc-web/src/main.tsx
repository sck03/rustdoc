import React from "react";
import ReactDOM from "react-dom/client";
import { QueryClientProvider } from "@tanstack/react-query";
import { HashRouter } from "react-router-dom";
import App from "./App.tsx";
import { installFrontendErrorLogger } from "./desktop/frontendErrorLogger.ts";
import { queryClient } from "./queryClient.ts";
import { FrontendErrorBoundary } from "./ui/FrontendErrorBoundary.tsx";
import { UnsavedChangesProvider } from "./ui/unsavedChangesGuard.tsx";
import { ConfirmationProvider } from "./ui/ConfirmationProvider.tsx";
import "./styles/foundation.css";
import "./styles/workspaces.css";
import "./styles/responsive.css";

installFrontendErrorLogger();

ReactDOM.createRoot(document.getElementById("root") as HTMLElement).render(
  <React.StrictMode>
    <QueryClientProvider client={queryClient}>
      <HashRouter>
        <ConfirmationProvider>
          <UnsavedChangesProvider>
            <FrontendErrorBoundary>
              <App />
            </FrontendErrorBoundary>
          </UnsavedChangesProvider>
        </ConfirmationProvider>
      </HashRouter>
    </QueryClientProvider>
  </React.StrictMode>,
);
