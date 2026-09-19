# Rust 桌面平台适配与验收

> 2026-09-16：Windows、Linux、macOS 共同开发，共用业务及界面源码，分别验收。当前本机运行证据来自 Windows x64 GNU 工具链；不能写成 Windows MSVC／ARM64、Linux 或 macOS 已通过。

## 版本与本次使用的 Slint 特性

2026-09-19 查询官方 [GitHub 最新稳定发布](https://github.com/slint-ui/slint/releases/tag/v1.18.0)与 [crates.io](https://crates.io/crates/slint)一致:当前最新稳定版为 `1.18.0`,发布于 2026-09-16。本项目运行库与 `slint-build` 均精确锁定该版本。2026-09-17 本机 `rustc -vV` 为 `1.98.1`、`x86_64-pc-windows-gnu`;根 `Cargo.toml` 与 `rust-toolchain.toml` 同样锁定 `1.98.1`,满足 Slint 1.18 的 MSRV 1.92。CI 配置读取仓库工具链;配置存在不代表对应 runner 已运行通过。

除声明式视图、Rust 模型、虚拟表格和原生输入外，本次接入 1.17 的 `InputType.search`、`Tooltip`、无障碍 landmark／live-region 和布局 `cross-axis-alignment`。保留软件渲染，适用于业务表格和表单；需要 GPU 的独立功能再通过可选能力接入。`compat-1-2` 是官方要求的 Cargo 特性标记，不代表退回旧版框架。

## 模块和平台边界

| 能力 | 共用部分 | 平台适配 |
| --- | --- | --- |
| 窗口、布局、缩放 | Slint + winit，共用 `.slint` 和 Rust controller/model | Windows 原生窗口、Linux X11／Wayland、macOS AppKit；通过 Slint 公共句柄 API 对接 |
| 业务和数据 | 契约、Domain、应用服务和 SQLite 存储 | 不依赖 GUI、WebView 或 OS 路径；团队数据库留在 Rust HTTP 服务器 |
| 文件选择 | controller 请求选择，worker 只接收用户选定路径并写入 | `platform.rs` 调用 rfd；主线程打开有所属窗口的 Windows／macOS 对话框或 Linux portal |
| 剪贴板 | 商品网格提供 TSV，平台层负责系统交互 | Windows／macOS 原生 provider；Linux 按实际显示句柄选择 X11／Wayland；provider 持续存活，Wayland 显示句柄在其后释放 |
| PDF | 共用原生排版及受控预览 | PDFium 随目标平台提供，系统打印继续按平台实现和验收 |
| 路径、字体和进程 | 显式 AppRoot／DataRoot、NFC 文件名、取消与清理契约 | OS 句柄、进程树和安装资源在边界处理；不要在业务模块增加 `cfg(windows)` 分支 |

## 构建和验收矩阵

`.github/workflows/rust-native-validation.yml` 使用目标系统 runner，分别执行 locked 测试、Slint 构建和服务组合根检查。它不运行 Tauri／WebKit，不以其它框架的验收代替 Slint。工作流已配置，尚未推送或触发，因此目前没有对应的远端执行结果。

| 目标 | Rust target | 当前验收状态 |
| --- | --- | --- |
| Windows x64 | `x86_64-pc-windows-msvc` | 已配置 CI；本机已运行的 Slint 验收来自 GNU 工具链 |
| Windows ARM64 | `aarch64-pc-windows-msvc` | 已配置 CI；目标设备运行待验收 |
| Linux x64 | `x86_64-unknown-linux-gnu` | 已配置 CI；X11／Wayland 会话、中文输入和桌面 portal 待验收 |
| Linux ARM64 | `aarch64-unknown-linux-gnu` | 已配置 CI；目标设备运行待验收 |
| macOS Apple Silicon | `aarch64-apple-darwin` | 已配置 CI；AppKit 对话框、Command 键、中文输入、Retina、打印及 `.app` 打包待验收 |

`scripts/verify-native-desktop.mjs` 检查目标平台的真实 Cargo 运行依赖图：桌面只使用 SQLite，不带 WebView、Tauri、egui 或 PostgreSQL 服务器依赖；Domain 不引用 GUI、存储 provider 或 HTTP 客户端。构建桌面时单独选择 `-p export-doc-slint`，避免和服务器一起构建造成 feature 合并。

完成支持声明前，每个平台还需验证：中文组合输入、表格复制粘贴／撤销、5000 行滚动、缩放、原生打开／保存、取消、路径大小写及链接拒绝、备份恢复、实际 PDF 与打印、退出后的进程和文件清理。构建成功只说明该平台可编译。
