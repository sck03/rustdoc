import "./styles/index.css";
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
import { applyInterfaceDensity, readInterfaceDensity } from "./app/interfaceDensity.ts";
import { installPortableFonts } from "./app/typographyPolicy.ts";
import { isTauri } from "@tauri-apps/api/core";
import { MigrationStartupScreen } from "./desktop/MigrationStartupScreen.tsx";

installPortableFonts();
const migrationStartup = isTauri() && window.location.hash === "#/startup-migration";
if (!migrationStartup) installFrontendErrorLogger();
applyInterfaceDensity(readInterfaceDensity());

ReactDOM.createRoot(document.getElementById("root") as HTMLElement).render(
  <React.StrictMode>
    {migrationStartup ? <MigrationStartupScreen /> : <QueryClientProvider client={queryClient}>
      <HashRouter>
        <ConfirmationProvider>
          <UnsavedChangesProvider>
            <FrontendErrorBoundary>
              <App />
            </FrontendErrorBoundary>
          </UnsavedChangesProvider>
        </ConfirmationProvider>
      </HashRouter>
    </QueryClientProvider>}
  </React.StrictMode>,
);
