# ExportDocManager Web

This folder is the staging area for the React/Vite frontend described in `docs/多平台Tauri重构可执行方案.md`.

Current scope:

- Generated TypeScript API types and fetch client live under `src/api/generated/`.
- The generated client is produced from `ExportDocManager.Api` OpenAPI metadata.
- No React page runtime is added yet; this keeps the first frontend step focused on the API contract.

Regenerate the client from the repository root:

```powershell
.\scripts\generate-api-client.ps1
```
