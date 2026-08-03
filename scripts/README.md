# 脚本使用说明

> 报表打印像素回归默认只读基准。模板版式有意调整后，先运行 `node scripts/test_report_template_print_pixel_regression.mjs --update` 生成受控基准，再立即运行不带 `--update` 的普通检查。更新只写测试夹具和 `.codex-runtime`，不会写系统临时目录。

## GitHub 公开发布

- `github/verify-public-source.ps1`：上传前检查注册机、私钥、内部 `KEY/` 产物和 GitHub 大文件边界。
- `github/initialize-github-repository.ps1`：初始化 `main` 分支、暂存公开文件，可选配置 origin、创建提交和推送；默认不会提交或联网推送。
- `verify-github-workflow-actions.mjs`：检查官方 Action 主版本、Node 24、`upload-artifact@v7` 和 `download-artifact@v8`，防止工作流运行时回退。
- `audit-npm-production.mjs`、`audit-dotnet-packages.mjs`：执行结构化 npm/NuGet 漏洞审计；Web 只允许已记录且由声明式路由契约约束的 React Router RSC 公告例外。
- `generate-dependency-governance.mjs`：从 npm/Cargo 锁文件和还原后的 NuGet 图生成 SPDX、CycloneDX 和第三方依赖清单到显式 `artifacts/` 目录。
- `check_frontend_style_governance.mjs`：阻止硬编码颜色、阴影、渐变、px 字号和 `!important` 债务继续增长。

RustSec 工作流直接调用安装后的 `cargo-audit` 时必须保留 `audit` 子命令，并拒绝漏洞、新增 unsound 公告和 yanked crate。Tauri 当前 Linux WebKit/GTK3 传递栈只精确豁免 `RUSTSEC-2024-0429`；该例外不能扩展为通配忽略，也不代表其它停止维护告警已经消失。

Tauri 正式 updater 密钥不由仓库脚本或 CI 自动生成，也不需要先确认 endpoint。项目所有者可先按 `docs/Tauri正式更新签名与发布配置.md` 在仓库外手工执行一次 signer 命令并妥善备份私钥；公钥固定进入正式安装包，更新地址由管理员配置，可在 GitHub、自建服务器和可信企业内网之间切换。

普通用户只需要使用 `scripts/` 根目录下的以下批处理入口，不要直接运行 `lib/`、`prepare-*`、`verify-*` 或 `assert-*`：

| 入口 | 用途 |
| --- | --- |
| `build-windows-desktop-run.cmd` | 构建一个 Windows 便携运行目录，默认全功能版 |
| `build-windows-editions.cmd` | 构建单证员版、业务员版、全功能版三个便携目录 |
| `build-windows-installers.cmd` | 构建三个 Windows NSIS 安装包 |
| `run-tests.cmd` | 先核查全部脚本，再运行完整 .NET 测试 |

公开/客户构建默认不生成内部注册机。只有本机保留私有 `apps/license-keygen-tauri/` 源码并显式向 PowerShell 构建脚本传入 `-IncludeLicenseKeygen` 时，才会把内部工具整理到客户目录之外的 `KEY/`。

构建输出按“一次生成、完整替换”处理：单版和三版便携包会在复制前清理旧稳定资源及整个浏览器目标目录；未传 `-IncludeLicenseKeygen` 的三版构建会删除旧 `KEY/`；安装器只清理本次请求版本的旧安装包与版本 manifest，未请求版本继续保留。单版便携和每一版安装器在进入交付目录前都会自动执行 `verify-package-payload.ps1`，禁止夹带未知字体、Playwright 开发 UI、重复 ONNX Runtime 或内部注册机。

桌面资源准备会在 release 依赖治理扫描前自动还原完整 `ExportDocManager.sln`。因此运行空间清理删除所有项目 `bin/obj` 后，可以直接执行上述构建入口，不需要先手工运行测试或 `dotnet restore`；.NET CLI 和 NuGet 缓存仍由构建环境定向到仓库 `.codex-runtime/`，不会新增系统 C 盘默认缓存。

Tauri 构建包装器直接使用当前 Node 启动 `apps/export-doc-tauri/node_modules/@tauri-apps/cli/tauri.js`，不再从 Node 子进程调用 Windows `npm.cmd`。这是 Node 24 在 Windows 上的进程启动兼容要求，不改变 Tauri 版本、构建参数或安装包内容。

公开仓库不提交 Chromium 二进制。`run-tests.ps1` 找不到程序根 Chromium 或 `EXPORTDOCMANAGER_CHROMIUM_EXECUTABLE` 时，会明确跳过两个真实 PDF 浏览器测试；正式发布验收使用 `-RequireBrowserPdfTests`，缺少渲染器即失败。测试默认执行 restore，只有确认依赖已还原时才使用 `-NoRestore`。

当前不使用 `.github/dependabot.yml` 自动创建依赖更新 PR。NuGet、npm、Cargo、Docker 和 Actions 版本由维护者集中审查后人工升级，避免一次更新触发大量分支和云端构建。

双击 `.cmd` 后窗口会一直保留，最后明确显示成功或失败及退出码，按任意键关闭。构建环境有问题时，错误信息不会一闪而过。

正式构建前可先在终端运行只读预检：

```powershell
./scripts/build-windows-desktop-run.cmd -PreflightOnly
./scripts/build-windows-editions.cmd -PreflightOnly
./scripts/build-windows-installers.cmd -PreflightOnly
```

自动化或已有终端不希望暂停时：

```powershell
$env:EXPORTDOCMANAGER_NO_PAUSE = "1"
./scripts/build-windows-editions.cmd -PreflightOnly
```

开发或审查时可单独执行完整脚本门禁：

```powershell
pwsh -NoProfile -File ./scripts/verify-script-suite.ps1
```

该门禁递归检查全部 `.ps1`、`.cmd` 和 `.mjs`：PowerShell AST、Node 语法、CMD 薄入口/共享宿主、危险系统路径模式，以及原生命令退出码是否统一处理。`run-tests.cmd` 会自动先执行该门禁。

## 工作区空间清理

先只读查看计划，不删除任何内容：

```powershell
./scripts/clean-generated-artifacts.ps1 -ListOnly
```

日常整理建议同时清除 `.codex-runtime` 中的一次性测试、截图、诊断和回归工作区，但保留仓库内 .NET SDK、NuGet/npm 缓存和工具：

```powershell
./scripts/clean-generated-artifacts.ps1 -IncludeCodexRuntimeWorkspaces
```

默认清理会保留 `artifacts/windows-desktop-run/`、`artifacts/windows-installers/`、`artifacts/license-keygen/`、浏览器/工具下载缓存、`node_modules` 和完整 `.codex-runtime` 依赖缓存。只有确认可重新下载或重新构建时才组合使用 `-IncludePackageCaches`、`-IncludeNodeModules`、`-IncludeCodexRuntime` 或 `-IncludeLegacyRuntimeAssets`；只有明确不再需要便携包、安装器和内部注册机输出时才使用 `-IncludeReleaseOutputs`。所有目标都必须解析到当前工作区内部，脚本不会扫描或删除 `App_Data`、数据库、Git、模板、OCR 模型、字体资源或仓库外目录。

注意：`artifacts/windows-desktop-run/` 是一次性构建输出。正式便携构建不检测、不询问，也不保留旧运行数据，会直接删除目标版本目录中的 `App_Data/` 和 `logs/` 后覆盖。需要保留的开发数据请在运行构建脚本前自行备份；不要把真实业务数据库放在该构建目录中长期使用。
