# ExportDocManager Tauri + React + Rust

桌面复用 `../export-doc-web` 的原版 React 页面。Tauri 只负责窗口、文件对话框、受管路径和更新；`desktop_runtime` 在进程内运行 `export-doc-server` 的 SQLite HTTP 适配器，业务由共用 Rust crates 实现，没有 .NET API sidecar。

正式构建入口为仓库 `scripts/build-native.ps1`，启动为 `scripts/run-native.ps1`。前端开发可在本目录执行 `npm ci` 后 `npm run dev`；通过 `EXPORTDOCMANAGER_APP_ROOT`、`EXPORTDOCMANAGER_DATA_ROOT` 显式指定独立开发资源和数据目录。首次安装模式选择数据目录，便携模式使用包旁 App_Data。

桌面访问令牌通过主窗口 IPC 提供，仅保存在内存中。WebView 缓存在 DataRoot/WebView；运行配置在 AppRoot/RuntimeConfig 或 `EXPORTDOCMANAGER_RUNTIME_CONFIG_ROOT` 指定位置。不得使用原 C# 业务目录做试验。

Tauri 2.x 的精确版本、许可和验证边界见根 Cargo.lock、package-lock.json 与 docs/Rust桌面平台适配与验收.md。默认更新端点与公钥为空，Rust 发布资产须单独验证后配置；便携版不执行安装器更新。
