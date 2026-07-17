# ExportDocManager Tauri Shell

This package hosts the Tauri desktop shell for the React/Vite workspace in `apps/export-doc-web`.

The shell is intentionally thin:

- starts the ASP.NET Core API sidecar on `127.0.0.1` with a random local port;
- creates the portable runtime directories beside the desktop executable;
- passes `--app-root` and `--data-root` to the sidecar;
- opens the shared Web frontend with `?apiBaseUrl=...`.

Runtime layout follows the project storage policy:

- stable resources stay beside the program root: `Templates/`, `OcrModels/`, `Browsers/`, `Resources/`;
- writable business data and operational logs default to `App_Data/`, with logs under `App_Data/Logs/`;
- `appsettings.json` stays in the program root;
- sidecar stdout/stderr are appended to `App_Data/Logs/api-sidecar.stdout.log` and `App_Data/Logs/api-sidecar.stderr.log`, or the equivalent explicit data root;
- the Windows authorization module keeps its existing dual-write behavior.

Useful environment overrides for development:

- `EXPORTDOCMANAGER_API_SIDECAR`: absolute path to `ExportDocManager.Api.exe`;
- `EXPORTDOCMANAGER_APP_ROOT`: app root passed to `--app-root`;
- `EXPORTDOCMANAGER_DATA_ROOT`: data root passed to `--data-root`.

Rust/Cargo is required to run `npm run dev` or `npm run build`.

Local Windows build notes:

- this workspace is currently verified with the GNU Rust target `stable-x86_64-pc-windows-gnu`;
- `scripts/run-tauri-local.ps1` adds `D:\Rust\.cargo\bin` and `D:\msys64\ucrt64\bin` for the current process only;
- Cargo output defaults to the workspace `artifacts/cargo-target-exportdoc/` directory; callers may still pass `-CargoTargetDir` or `CARGO_TARGET_DIR` explicitly when a different non-system-drive cache is required;
- `npm run tauri:check:local` runs `cargo check`;
- `npm run tauri:compile:local` runs `tauri build --debug --no-bundle`;
- `npm run tauri:build:local` runs `tauri build --no-bundle` for a release desktop executable and copied resource root;
- `npm run tauri:bundle:nsis:local` runs `tauri build --bundles nsis --no-sign` and has been verified with the GNU/UCRT64 toolchain;
- the current GNU/UCRT64 + NSIS-only route does not require Visual Studio Build Tools / Windows SDK as a blocking prerequisite;
- `npm run tauri:bundle:local` runs the full configured Tauri bundling flow. The NSIS target is verified; switching to an MSVC Rust target, MSI/WiX, or all Windows bundle targets may still require Visual Studio Build Tools / Windows SDK depending on the selected bundle target.
