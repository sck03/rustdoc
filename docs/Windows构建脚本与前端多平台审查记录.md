# Windows 构建脚本与前端多平台审查记录

> 审查日期：2026-07-17  
> 范围：`scripts/` Windows 构建入口、构建缓存与临时目录、Tauri bundle 依赖发现、Web 动态视口和现有前端依赖。

## 1. 本轮结论

- `build-windows-desktop-run.ps1`、`build-windows-editions.ps1`、`build-windows-installers.ps1` 默认在成功或失败后等待回车，不再依赖 Explorer 父进程识别。自动化使用 `-NoPause`，或设置 `EXPORTDOCMANAGER_NO_PAUSE=1`/`CI=true` 后自动退出。
- 四个普通用户 `.cmd` 入口都缩减为 4 行，只声明对应 PowerShell 文件并转交 `scripts/lib/run-powershell-entry.cmd`。共享 CMD 统一发现 PowerShell、设置子脚本无暂停环境、透传用户参数、保留真实退出码并在普通双击场景执行 `pause`；即使 PowerShell 在脚本初始化早期失败，窗口也会保留。
- PowerShell 侧的外部命令调用、退出码检查和直接运行暂停集中在 `scripts/lib/build-script-support.ps1`；CMD 与 PowerShell 两层职责不重叠。`GeneratedArtifactCleanupScriptTests` 会阻止以后把 `where pwsh`、`pause` 和宿主逻辑复制回四个根入口。
- 新增 `verify-script-suite.ps1` 作为脚本目录唯一总门禁，递归覆盖 `18` 个 PS1、`5` 个 CMD 和 `48` 个 MJS。它统一执行 PowerShell AST、Node `--check`、公开 CMD 薄入口、共享 CMD 宿主、危险系统状态/系统盘模式和原生命令退出码检查；`run-tests.ps1` 在 .NET 测试前自动调用。
- Windows 便携目录固定视为一次性构建产物。每次准备输出时直接删除目标目录中的 `App_Data/` 与 `logs/`，不检查、不询问、不保留旧运行数据，也不在 editions 最终验收中重复扫描这两个目录。需要保留的数据由使用者在构建前自行备份。
- 构建前可使用 `-PreflightOnly` 只检查 PowerShell、Rust/Cargo、Node/npm、.NET、输出目录和缓存目录，不进入编译、不删除文件，也不枚举旧 `App_Data/logs`。
- NuGet、npm、.NET CLI、HTTP 缓存以及 `TEMP/TMP` 默认进入仓库 `.codex-runtime/`。Tauri bundle 直接执行时会从 `CARGO_HOME` 或 `PATH` 上的 `cargo` 推导 Cargo home，不再静默回退到系统用户目录 `~/.cargo`；Windows 推导到系统盘时明确失败。
- 安装器 `Editions` 与验证脚本 `ExpectedEditions` 支持 PowerShell 数组或命令行逗号分隔值，修复 `pwsh -File ... -ExpectedEditions Document Sales Full` 的数组绑定失败风险。
- Web 主 Shell、侧栏、主要全屏弹窗和内部注册机在保留 `vh` 回退的同时增加 `dvh`，改善移动浏览器地址栏、Tauri 小窗口和不同桌面 WebView 可用高度变化时的裁切问题。

## 2. 构建入口

### 2.1 只做环境预检

```powershell
./scripts/build-windows-desktop-run.cmd -ProductEdition Full -PreflightOnly
./scripts/build-windows-editions.cmd -PreflightOnly
./scripts/build-windows-installers.cmd -PreflightOnly
```

预检只输出环境和路径诊断，并以 `RuntimeDataCleanup = "unconditional"` 明确正式构建策略；它不会枚举或判断目标目录当前是否含运行数据。

### 2.2 正式构建

```powershell
./scripts/build-windows-desktop-run.cmd -ProductEdition Full
./scripts/build-windows-editions.cmd
./scripts/build-windows-installers.cmd
```

也可双击同名 `.cmd`，这是 Windows Explorer 下最可靠的入口。正式构建会覆盖 `artifacts/windows-desktop-run/` 或 `artifacts/windows-installers/` 下的目标产物。便携目录中的 `App_Data/`、`logs/` 会在每次构建前删除。

终端或自动化不需要等待时使用：

```powershell
./scripts/build-windows-editions.cmd -NoPause
```

### 2.3 测试入口

`scripts/run-tests.cmd` 复用同一 CMD 宿主，实际测试编排位于 `scripts/run-tests.ps1`，先执行完整脚本门禁，再使用仓库内缓存和 `TestResults/` 运行 .NET：

```powershell
./scripts/run-tests.cmd
./scripts/run-tests.cmd -Configuration Release -Restore
```

只核查脚本而不运行 .NET：

```powershell
pwsh -NoProfile -File ./scripts/verify-script-suite.ps1
```

## 3. 路径与依赖边界

| 内容 | 当前默认位置 | 说明 |
| --- | --- | --- |
| .NET CLI home | `.codex-runtime/dotnet-cli/` | 构建期缓存，不写用户配置目录 |
| NuGet 包 | `.codex-runtime/nuget-packages/` | `NuGet.Config` 与脚本双重约束 |
| NuGet HTTP 缓存 | `.codex-runtime/nuget-http-cache/` | 构建期可再生 |
| npm 缓存 | `.codex-runtime/npm-cache/` | 不使用系统用户 npm cache |
| 构建临时目录 | `.codex-runtime/temp/` | 同时设置 `TEMP` 与 `TMP` |
| Cargo target | `artifacts/cargo-target-*` | 主程序、授权工具和 Excel 分析器分开 |
| NSIS 下载缓存 | `artifacts/tool-downloads/` | 下载后校验固定 SHA-1 |
| 便携交付目录 | `artifacts/windows-desktop-run/` | 一次性构建产物，构建时清理运行数据 |
| 安装器目录 | `artifacts/windows-installers/` | 交付输出，包含 manifest 和 SHA-256 |

本机 `dotnet.exe` 或 Windows 自带 `curl.exe` 可以位于系统盘，它们属于开发机已安装的工具入口；项目包、NuGet/npm 缓存、Cargo target、下载缓存、临时文件和交付产物仍受仓库路径约束。客户运行版 API 为 self-contained sidecar，不依赖客户系统盘安装 .NET SDK。

## 4. 前端多平台与商业化审查

- 当前生产依赖仍为 React、React Router、TanStack Query、Lucide、Three.js、html2canvas 和 jsPDF，均为免费开源库；本轮未新增依赖、云字体、在线 UI 服务或仅用于装饰的组件库。
- 字体继续使用 Windows、macOS、Linux 平台原生字体回退，不下载远程字体。
- 主要宽表使用内部滚动容器保留专业录入密度；普通业务表单在既有 `1180 / 860 / 560px` 断点下换列，销售工作区、设置页和维护页不要求桌面固定分辨率。
- 本轮对 `app-shell`、侧栏、单一窗口锁定弹窗、生产企业档案、发票商品专注模式、报表预览、唛头编辑和商品库弹窗补充动态视口高度，降低移动浏览器和小型 WebView 中底部按钮被浏览器 UI 遮挡的风险。
- 现有 Web 生产构建已按路由和大型能力拆分 chunk；Three.js、jsPDF 和 html2canvas 没有进入所有页面的主业务 chunk。
- 本轮属于代码与自动化层审查，不替代 Windows ARM64、Linux、macOS、触屏、高 DPI 和真实移动浏览器的人工实机验收。

## 5. 验证记录

- 脚本总门禁：`18` 个 PS1 AST、`48` 个 MJS `node --check`、`5` 个 CMD 结构全部通过；四个公开 CMD 均为薄转发，共享宿主保留 PowerShell 发现、参数透传、真实退出码和暂停。经审查允许的直接原生命令共 `8` 处，仅位于 smoke 的 npm/node 和固定哈希下载的 curl，均显式检查 `$LASTEXITCODE`。
- 构建预检：单版本、三版本和安装器 `.ps1/.cmd` 入口均通过；单版本和三版本输出统一返回 `RuntimeDataCleanup = "unconditional"`，不再枚举 `App_Data/logs`。独立隐藏进程运行默认 `.ps1 -PreflightOnly` 8 秒后仍保持等待输入；CMD 成功、失败、`-NoPause`、`CI=true`、缺脚本、缺 PowerShell、版本非法值和带空格输出路径均通过，成功路径实际显示 `Press any key to close this window`。
- 2026-07-17 截图故障复核：删除 `verify-windows-editions.ps1` 对 `App_Data/logs` 的额外门禁；整理脚本删除或覆盖失败时不再保留旧文件继续构建。现有三版本运行目录已直接清理，验收成功，三版均为 `700` 个文件且主程序 SHA256 各不相同。
- 完整覆盖构建：在 Document、Sales、Full 三个一次性目录中预先创建空 `App_Data/logs` 后，实际执行 `cmd /d /c scripts\build-windows-editions.cmd -NoPause`，约 `716.2` 秒完成。三版运行目录均被直接清理并覆盖，没有检测、确认或运行目录验收拦截；最终每版 `700` 个文件，主程序 SHA256 分别为 `7F45D1BF...`、`D1987A41...`、`7E83B59D...`。
- 安装器 editions 参数：`Document,Sales` 解析通过；非法值明确失败；验证脚本的逗号分隔输入不再发生位置参数绑定错误。
- .NET：`498/498`，Domain `29`、Application `147`、Infrastructure `100`、API `222`；脚本结构专项 `12/12`。
- Tauri Rust：Document、Sales、Full 三版本 `cargo check` 全部通过；既有 Rust 单元测试基线 `34/34` 保持。
- Web：API 客户端重新生成成功；TypeScript 与 Vite 生产构建通过，`2214 modules transformed`；全部 `16` 个 `test_*.mjs` 实际通过，包括新版设计器导出/打印、`9` 模板视觉、`11` 模板 PDF/打印和 `18` 页 PDF 像素回归。
- 依赖准备：Chrome Headless Shell 复用 E 盘现有目录；Tauri NSIS 固定哈希下载/整理通过，工具位于 `artifacts/cargo-target-exportdoc/.tauri/NSIS`。安装器验证按预期因当前没有 `windows-installers/installers-manifest.json` 返回失败，说明尚未生成安装包，不属于脚本回归。
- 路径环境：`DOTNET_CLI_HOME`、`NUGET_PACKAGES`、`NUGET_HTTP_CACHE_PATH`、`npm_config_cache`、`TEMP`、`TMP` 均解析到 `E:\rustdoc\ExportDocManager_CS\.codex-runtime\` 下。

## 6. 完成度与后续门槛

| 范围 | 本轮完成度 | 尚未完成 |
| --- | ---: | --- |
| Windows 构建入口与脚本总门禁 | 100% | 仍需用户机器双击体验复核 |
| 构建缓存与临时目录非系统盘治理 | 100% | 开发机全局 SDK 可继续作为明确例外 |
| Windows 三版本便携构建编排 | 100% | 已完成带预置 `App_Data/logs` 的三版本完整覆盖构建；仍待用户机器运行与高 DPI 人工验收 |
| Windows NSIS 安装器编排 | 95% | 当前没有现成 installer manifest，本轮完成预检与参数修复，仍待重新生成三安装包并执行签名/安装/卸载验收 |
| 前端代码级多平台适配 | 98% | 仍待 Windows ARM64、Linux、macOS、移动浏览器和高 DPI 实机验收 |
