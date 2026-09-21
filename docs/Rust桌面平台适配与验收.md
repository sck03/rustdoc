# Rust 桌面平台适配与验收

> 2026-09-20：桌面已改为 Tauri 2 + React + Rust。旧 Slint 编译、截图或测试仅是历史，不作为当前交付证据。

## 稳定版本

查询时间为 2026-09-20。官方 [Tauri crates.io](https://crates.io/crates/tauri) 稳定版本 `2.11.6`，构建库 `2.6.3`；[npm CLI](https://www.npmjs.com/package/@tauri-apps/cli) `2.11.5`、[npm API](https://www.npmjs.com/package/@tauri-apps/api) `2.11.1`。single-instance `2.4.5`、updater `2.12.0`。精确清单与锁文件为版本事实源。

## 平台边界

| 平台 | Rust target | 显示组件 | 验收边界 |
| --- | --- | --- | --- |
| Windows x64 | x86_64-pc-windows-msvc | WebView2 | 当前宿主另有 GNU 工具链；其结果不能替代 MSVC 发布 |
| Windows ARM64 | aarch64-pc-windows-msvc | WebView2 | runner 编译与设备运行分别验收 |
| Linux x64 | x86_64-unknown-linux-gnu | WebKitGTK 4.1 | 需 GTK、WebKitGTK、AppIndicator、X11／Wayland 环境 |
| Linux ARM64 | aarch64-unknown-linux-gnu | WebKitGTK 4.1 | 需对应 runner／设备验收 |
| macOS ARM64 | aarch64-apple-darwin | 系统 WKWebView | 需 AppKit、Retina、输入法、文件对话框与 .app 验收 |

窗口、路径、文件对话框、系统打开、安装器和更新进入 Tauri 平台模块；业务和 SQL 不进入界面。WebView profile 必须在受管 DataRoot 中。桌面直接连接进程内 Rust API，随机回环端口由操作系统绑定，令牌通过主窗口 IPC 读取。

`scripts/verify-native-desktop.mjs` 检查真实 Cargo 运行依赖图：包含 Tauri 和 SQLite，排除 Slint／egui、PostgreSQL 服务端适配器；Domain 保持纯业务。桌面与服务端分别构建，避免 workspace feature 合并被误认为桌面交付图。

## PDFium 动态库与 ABI 边界

报表维持 krilla 生成 PDF、PDFium worker 处理已有 PDF。当前已采用 `pdfium-render 0.9.4`（`pdfium_7881 + image_025`），原生包为 `152.0.7961`；尚需验证完整封装/API feature/原生库组合。Windows x64/ARM64、Linux x64/ARM64、macOS ARM64 分别验收，不能将迁移前手写绑定的静态符号检查当成新封装运行通过。

2026-09-20 本地 Windows x64 DLL 与 Linux x64 ELF 静态导出检查均包含当前 19/19 个所需符号；本轮没有执行 Linux/macOS/ARM64 加载或功能调用，不据此宣称跨平台运行通过。详细平台矩阵、符号/加载/回调/资源限制检查见[《PDFium 跨平台绑定与验收方案》](./PDFium跨平台绑定与验收方案.md)。

本批实际验证统一记入进度文档。平台完整验收包括原版导航和页签、表格滚动／编辑、中文 IME、撤销与粘贴、缩放、保存取消、路径与链接拒绝、登录授权、后台任务、真实 PDF／打印、备份恢复和退出清理。未经实跑不声明通过，不执行 Windows Authenticode、Developer ID 或 Apple 公证。
