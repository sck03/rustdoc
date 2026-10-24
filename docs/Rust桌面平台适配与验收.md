# Rust 桌面平台适配与验收

> 2026-09-20：桌面已改为 Tauri 2 + React + Rust。旧 Slint 编译、截图或测试仅是历史，不作为当前交付证据。

## 稳定版本

查询时间为 2026-10-23。官方 [Tauri crates.io](https://crates.io/crates/tauri) 稳定版本 `2.12.5`，构建库 `2.7.4`；[npm CLI](https://www.npmjs.com/package/@tauri-apps/cli) 与 [npm API](https://www.npmjs.com/package/@tauri-apps/api) 均为 `2.12.4`。single-instance `2.5.4`、updater `2.14.0`。Rust `1.99.1`，构建用 Node `26.12.1`；Node 不进入运行包。精确清单与锁文件为版本事实源，旧版本跨平台结果不替代升级后的验收。

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

Windows 桌面入口在 Debug 和 Release 均使用 GUI 子系统，Full 本地测试包也不创建常驻控制台；启动错误继续使用弹窗及受管日志。`scripts/test_native_desktop_shutdown.mjs` 在启动前检查实际 EXE 的 PE 子系统，避免自动化的隐藏窗口选项掩盖控制台构建。

## PDFium 动态库与 ABI 边界

OCR 同样按目标 OS/架构组装 Rust worker、ONNX 和模型。2026-10-15 补齐 Windows ARM64 app-local CRT，与 x64 共用显式初始化和受控管道；各目标打包后新增实际模型回归。签名/摘要和源码检查不代替各设备运行，详见 [OCR 跨平台评估](./OCR跨平台与RapidOCR评估.md)。

2026-10-15 在[原生交付工作流](https://github.com/sck03/rustdoc/actions/runs/38694473894)完成上表全部五种 target 的独立桌面构建、运行依赖检查、Document 包组装及七份真实 OCR 模型夹具。Windows ARM64 实际加载 ARM64 ONNX 和三个 ARM64 CRT；x64 MSVC 与本机 GNU 分别记录。Linux/macOS runner 未在本批执行完整窗口、系统输入法及打印操作。当批 Full SQLite / 含 OCR 测试包另通过 Tauri 页面、空白设计、保存回读、十份 PDF、员工权限及退出清理，构建与路径见本日进度。

2026-10-23 升级及审查优化后的 Windows x64 GNU / Debug / Full SQLite / 含 OCR 测试包位于 `artifacts/review-hardening-desktop/ExportDocManager.Tauri.Full`，完整 Tauri 回归覆盖人员组织编辑、确认框真实鼠标交互、中文字段、表格编辑、报表、OCR 与正常退出。16 份 PDF 共 16 页经 Poppler 渲染检查；自定义小纸张样例只验证尺寸保存和输出，内容裁切不计为重新排版通过。独立 React／Rust HTTP 回归另核对 CNY／USD 出口额、窄屏及邮件模板保存／历史恢复。ONNX Runtime `1.31.0` 的七组真实样例及异常边界为本日升级批次证据；审查批次未升级原生库。旧 `personnel-stability-desktop` 程序及资源副本已清理，App_Data 保留。实际构建标识和证据目录见[当日进度](./程序改进重构进度文档.md)。

报表维持 krilla 生成 PDF、PDFium worker 处理已有 PDF。当前已采用 `pdfium-render 0.9.4`（`pdfium_7881 + image_025`），原生包为 `158.0.8111`；2026-10-23 包内 Windows x64 GNU worker 的真实加载、文字、预览、OCR 页图、合并重开及异常边界通过。其它目标和 MSVC 发布须分别验收，不能将迁移前手写绑定的静态符号检查当成新封装运行通过。

2026-10-24 模板与查询完善后的 Windows x64 GNU / Debug / Full SQLite / 含 OCR 包位于 `artifacts/project-hardening-desktop/ExportDocManager.Tauri.Full`，最终完整 Tauri 回归为 `artifacts/project-hardening-desktop-evidence/desktop-1792849624279`。16 份 PDF 共 16 页已渲染检查，包内 PDFium 的加载、页图/文字/OCR 页图、合并及错误边界通过；同一 React/Rust HTTP 的 7 页实际 PDF 翻页另有独立证据。服务端原生预览保留取消、30 秒子进程时限和 504 分类。该结果仍不替代 MSVC、其它 OS/架构 GUI、系统 IME 或实体打印，详细范围与清理记录见当日进度。

2026-09-20 本地 Windows x64 DLL 与 Linux x64 ELF 静态导出检查均包含当前 19/19 个所需符号；本轮没有执行 Linux/macOS/ARM64 加载或功能调用，不据此宣称跨平台运行通过。详细平台矩阵、符号/加载/回调/资源限制检查见[《PDFium 跨平台绑定与验收方案》](./PDFium跨平台绑定与验收方案.md)。

本批实际验证统一记入进度文档。平台完整验收包括原版导航和页签、表格滚动／编辑、中文 IME、撤销与粘贴、缩放、保存取消、路径与链接拒绝、登录授权、后台任务、真实 PDF／打印、备份恢复和退出清理。未经实跑不声明通过，不执行 Windows Authenticode、Developer ID 或 Apple 公证。

2026-09-30 本地 `-Edition Full` 支持 SQLite 单机测试便携包。Windows GNU Debug `-WithoutOcr` 实際包已启动 Tauri/WebView2，核对完整导航、默认首页、人员/OA/账号/公告/通知及单证/销售 API、公告页面截图和正常退出/端口关闭；实际记录见进度文档。Full 测试包不加入正式 All、安装器和更新发布矩阵；其它平台和 OCR/打印等能力不由这次布局验证代替。

## 2026-09-28 Windows 退出清理

`desktop_runtime` 区分运行、正在清理和清理完成；并发退出只启动一个清理线程，其余请求继续等待。后端关闭在工作线程执行，保持现有 45 秒等待上限和错误日志；退出前显式销毁 WebView 窗口，让 wry 关闭平台控制器，不依赖 `App::run` 直接退出进程后的 Rust 析构。

`scripts/test_native_desktop_shutdown.mjs` 在隔离 DataRoot 启动实际 GNU Release EXE，通过 WebView2 CDP 检查 React 登录页和退出事件，记录对应进程及 HTTP 端口。两次正常退出、一次连续 12 次退出 IPC、一次只终止宿主的异常退出均观察到本实例 6 个 WebView2 进程归零、HTTP 监听关闭；期间独立测试窗口持续响应，最后也正常清空。该结果证明本机测试场景，不能推导为所有崩溃条件或 Linux/macOS/ARM64 已验收。
