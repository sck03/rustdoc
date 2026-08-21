# 脚本使用说明

> 报表打印像素回归默认只读基准。模板版式有意调整后，先运行 `node scripts/test_report_template_print_pixel_regression.mjs --update` 生成受控基准，再立即运行不带 `--update` 的普通检查。更新只写测试夹具和 `.codex-runtime`，不会写系统临时目录。

## GitHub 公开发布

- `github/verify-public-source.ps1`：上传前检查注册机、私钥、内部 `KEY/` 产物和 GitHub 大文件边界。
- `github/validate-container-installer.sh`：在 Linux CI 中验证容器一键安装器的幂等、回滚、符号链接拒绝、目录权限和 PostgreSQL 数据卷合同。
- `github/initialize-github-repository.ps1`：初始化 `main` 分支、暂存公开文件，可选配置 origin、创建提交和推送；默认不会提交或联网推送。
- `verify-github-workflow-actions.mjs`：检查官方 Action 主版本、Node 24、`upload-artifact@v7` 和 `download-artifact@v8`，防止工作流运行时回退。
- `audit-npm-production.mjs`、`audit-dotnet-packages.mjs`：执行结构化 npm/NuGet 漏洞审计；NuGet 优先读取官方源的 `dotnet --vulnerable` 结果，若宿主 TLS 无法访问漏洞元数据，则从已还原依赖图提取精确版本并使用 OSV NuGet 生态复核，依赖图本身不可读取时仍立即失败。
- `verify-dependency-policy.mjs`：校验中央 NuGet 清单、所有锁文件和生成的依赖证据；NPOI 强制保持 `2.7.6`，发现 `2.8.0` 或锁图缺失时立即失败。普通生产依赖继续使用精确 lockfile，不用“大于某版本即可”替代可复现构建。依赖治理工作流生成 SBOM 后使用 `--generated-only` 只复核证据，避免重复扫描源码锁图。
- `global.json` 以精确稳定版本声明最低 SDK 基线，并用 `rollForward: latestFeature`、`allowPrerelease: false` 允许同一 .NET `major.minor` 内使用更新的稳定 feature band，例如 `10.0.302 -> 10.0.303/10.0.400/10.0.401`；低于基线、preview、`10.1.x` 和 `11.x` 均拒绝。GitHub Actions 与容器分别使用稳定通道 `10.0.x`、`10.0-noble`，门禁从 `global.json` 推导通道，避免重复硬编码精确 SDK。NuGet servicing 与发布依赖仍使用中央精确版本和 lockfile，当前为 `10.0.11`，不得改成 `10.0.*` 或“大于某版本”。
- `generate-dependency-governance.mjs`：从 npm/Cargo 锁文件和还原后的 NuGet 图生成 SPDX、CycloneDX 和第三方依赖清单到显式 `artifacts/` 目录。
- `check_frontend_style_governance.mjs`：阻止硬编码颜色、阴影、渐变、px 字号和 `!important` 债务继续增长。
- `check_source_size_governance.mjs`：对 .NET、Web、Rust、测试、自动化和 GitHub 工作流实行全仓库单文件上限、聚合上限与相对基线增量门禁；统一忽略 `bin/obj/dist/target/node_modules/artifacts/TestResults/.codex-runtime/.git` 等生成目录，避免构建产物污染统计。

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

构建输出按“一次生成、完整替换”处理：单版和三版便携包会在复制前清理旧稳定资源及整个浏览器目标目录；未传 `-IncludeLicenseKeygen` 的三版构建会删除旧 `KEY/`；安装器只清理本次请求版本的旧安装包与版本 manifest，未请求版本继续保留。Windows 便携目录和 GitHub 便携 ZIP 在进入交付阶段前都会执行最终 `ExportDocManager.exe` 的零参数启动、动态 API 健康、空密码 `admin` 登录及基础分页冒烟检查，随后清理测试生成的 `App_Data`；载荷门禁继续禁止未知字体、Playwright 开发 UI、重复 ONNX Runtime 或内部注册机。本机 GNU 构建默认单并发，以控制普通 16 GiB 电脑上的 LLVM 峰值内存；GitHub Windows 发布仍使用 MSVC，并由工作流显式设置自己的并发度。

桌面资源准备会在 release 依赖治理扫描前自动还原完整 `ExportDocManager.sln`。本地 Tauri 构建入口也会在锁定依赖缺失时分别对 Web 与桌面项目执行一次 `npm ci`。因此运行空间清理删除项目 `bin/obj` 或显式删除 `node_modules` 后，可以直接执行上述构建入口，不需要先手工运行测试、`dotnet restore` 或 `npm ci`；.NET CLI、NuGet 和 npm 缓存仍由构建环境定向到仓库 `.codex-runtime/`，不会新增系统 C 盘默认缓存。

同一工作区一次只允许一个本地 Tauri 构建占用共享 Cargo 与资源暂存目录。重复双击或同时从终端启动第二次构建时，后启动的入口会立即给出明确提示并停止，避免两个构建互相覆盖 `artifacts/tauri-bundle` 后出现 `EBUSY`、文件锁或不完整便携包。

Tauri CLI 由当前 Node 直接启动 `apps/export-doc-tauri/node_modules/@tauri-apps/cli/tauri.js`，不再通过 npm 命令垫片间接启动。依赖还原和普通 npm script 继续经过统一外部进程入口；该入口会把裸命令解析为当前平台的实际可执行文件（Windows 包括 `.cmd`），同时保留超时、心跳和退出码检查。

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

依赖升级后可按仓库全部 `packages.lock.json` 精确删除不再引用的 NuGet 旧版本，同时保留当前锁定图和 NPOI `2.7.6`：

```powershell
./scripts/clean-generated-artifacts.ps1 -PruneUnusedNuGetVersions
```

默认清理会保留 `artifacts/windows-desktop-run/`、`artifacts/windows-installers/`、`artifacts/license-keygen/`、浏览器/工具下载缓存、`node_modules` 和完整 `.codex-runtime` 依赖缓存。`-PruneUnusedNuGetVersions` 只修剪锁文件未引用的普通 NuGet 精确版本，不清空当前依赖，并保留由当前 .NET SDK 管理的 Runtime/Host packs；Cargo 与 npm 内容寻址缓存不做猜测式版本删除。只有确认可重新下载或重新构建时才组合使用 `-IncludePackageCaches`、`-IncludeNodeModules`、`-IncludeCodexRuntime` 或 `-IncludeLegacyRuntimeAssets`；只有明确不再需要便携包、安装器和内部注册机输出时才使用 `-IncludeReleaseOutputs`。所有目标都必须解析到当前工作区内部，脚本不会扫描或删除 `App_Data`、数据库、Git、模板、OCR 模型、字体资源或仓库外目录。

注意：`artifacts/windows-desktop-run/` 是一次性构建输出。正式便携构建不检测、不询问，也不保留旧运行数据，会直接删除目标版本目录中的 `App_Data/` 和 `logs/` 后覆盖。需要保留的开发数据请在运行构建脚本前自行备份；不要把真实业务数据库放在该构建目录中长期使用。
