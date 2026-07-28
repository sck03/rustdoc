# ExportDocManager Web

This folder contains the production React/Vite frontend described in `../../docs/产品架构与文档总览.md`.

Current scope:

- Generated TypeScript API types and fetch client live under `src/api/generated/`.
- The generated client is produced from `ExportDocManager.Api` OpenAPI metadata.
- Business routes are loaded on demand and shared by Tauri desktop and browser deployments.
- Product edition, account permissions and server-side authorization remain aligned; the frontend is not the security boundary.

Regenerate the client from the repository root:

```powershell
.\scripts\generate-api-client.ps1
```
