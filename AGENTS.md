# ExportDocManager 项目协作与工程规则

本文件是本仓库中自动化代理、维护者和贡献者的通用工作规范。它描述的是“如何安全地修改、验证和交付”本项目，不替代产品需求或安全政策。用户/维护者的明确指令优先于本文件；本文件与源码不一致时，先以源码和 `docs/当前架构事实.md` 为准，并在提交中修正文档事实。

## 1. 开始工作前

1. 先查看工作树和远端状态：

   ```powershell
   git status --short --branch
   git log -1 --oneline
   git fetch origin main
   ```

2. 先读以下事实源，再决定修改位置：

   - `docs/当前架构事实.md`：当前部署、目录、数据库、API 和模块边界的唯一事实源。
   - `docs/产品架构与文档总览.md`：产品形态、运行方式和门禁总览。
   - `docs/程序改进重构进度文档.md`：按日期保存的实施证据；旧条目只用于追溯，不能当作当前契约。
   - `docs/运行目录与路径存储审查清单.md`：路径、缓存、临时文件和系统目录审查规则。
   - `docs/多平台与多架构支持矩阵.md`：RID、平台、架构和真机验收边界。
   - `scripts/README.md` 与 `scripts/clean-generated-artifacts.ps1`：脚本入口和空间清理边界。

3. 如果工作树已有修改，必须保留并避开无关文件；不要用 `git reset --hard`、`git checkout --`、广泛 `Remove-Item` 或其它不可逆操作覆盖用户工作。

## 2. 项目形态与目录边界

- 产品共用 Domain、Application、API 契约和 React Web 界面，包含 Tauri 桌面端、浏览器服务器版和容器版。
- 后端统一为 `net10.0`；能力按程序集拆分为核心 Infrastructure、Excel、Browser、PDF/OCR 模块。核心层不得重新直接引用这些可裁剪模块的重量级实现。
- API 是组合根：基础设施适配、DI 注册、端点映射和模块发现留在 API/Infrastructure；Domain 不引用 ASP.NET Core、文件系统宿主细节或具体数据库 provider。
- Web 源码在 `apps/export-doc-web`；Tauri 壳和 Rust 命令在 `apps/export-doc-tauri`；OCR 与 Excel analyzer 是独立 Rust 工程；C# 测试在 `tests`。
- `src`、`apps`、`tests`、`tools` 中的 `bin/`、`obj/`、`dist/`、`target/`、`node_modules/` 以及根 `artifacts/`、`TestResults/`、`.codex-runtime/` 都是生成或本地工作区，不得提交到 Git。

## 3. 架构不变量

### 3.1 运行目录和数据

- 所有持久化路径由启动组合根显式注入 `AppRoot`/`DataRoot` 和 `IAppPathProvider` 解析；服务不得自行 `new` 全局路径提供器，也不得在静态字段中缓存宿主路径。
- 数据库、配置、日志、备份、模板、缓存、浏览器 profile、PostgreSQL 客户端、OCR/浏览器资源和随包工具必须落在运行目录或明确职责的容器层；不要默认写入 `C:\Users\...\AppData`、系统 TEMP、ProgramData 或系统级工具缓存。
- 配置中保存相对路径；写入、读取、迁移前后都必须验证仍在受管根目录内，并拒绝符号链接、联接点、路径穿越、磁盘根和不可写目录。
- SQLite 仅用于桌面单机；团队/服务器/容器模式使用 PostgreSQL 18。项目尚未投产，不添加旧 v1—v7 数据兼容分支、猜测式迁移或双读逻辑；需要改变空库基线时直接更新正式 schema 和测试。
- 单 API 实例锁、后台任务、恢复和迁移必须保持 fail-closed；不能把数据库/文件系统故障伪装成空列表或普通业务冲突。

### 3.2 跨平台路径、文件名和时间

- 文件名先做 NFC 规范化，并遵守 Windows/Linux/macOS 共同非法字符、尾部点/空格、保留设备名和长度规则。
- Windows 路径比较按不区分大小写；Linux 和 macOS 目标文件系统的大小写语义必须被尊重，不能为了“看起来一致”在大小写敏感卷上折叠不同文件。
- 只使用 `Path`/`PathBuf`、`Path.Combine`、URI API 和平台无关分隔符；不得拼接硬编码 `\`、`/`、盘符或假定当前工作目录。
- 业务自然日使用 `DateOnly`；具有时区意义的时间点使用 `DateTimeOffset`。公开 Domain/Application/API/OpenAPI 不新增 `DateTime` 属性；第三方 Excel/文件系统互操作边界除外。

### 3.3 API、错误和契约

- `/openapi/v1.json` 是唯一 API 契约事实源，由 .NET 官方 OpenAPI 元数据生成；TypeScript 客户端必须从生成结果更新，不手写第二套 endpoint/schema 目录。
- 端点认证、桌面令牌和许可证要求使用 endpoint metadata；不要按 `/api` 前缀、路径白名单或前端路由猜测授权。
- 业务错误按现有分类映射：校验 400、权限 403、明确资源不存在 404、冲突 409、繁忙 429、依赖不可用 503、超时 504；不要把数据库、文件或外部工具故障包装成 404/409。
- 所有异步公共操作都要有明确取消边界、超时和资源清理；后台任务完成、失败、取消和输出清理必须可观察且幂等。

### 3.4 解耦与扩展

- 优先扩展接口、能力模块和职责明确的 partial/服务，不在大型协调器中继续堆 UI、数据库、路径和进程控制逻辑。
- 不为单个客户文件名、历史测试快照或旧数据添加分支补丁；先抽象通用解析/校验规则，再补最小回归测试。
- 不引入 Redis、消息队列、第二数据库、LocalStorage 持久化或新的默认导出/图片目录，除非需求和架构文档明确批准。
- 缺少可选能力模块时返回明确“不支持”，不要复制一份降级实现或静默改变数据语义。

## 4. 依赖与许可证策略（硬约束）

所有 .NET 版本集中在 `Directory.Packages.props`，Web 版本集中在 `apps/export-doc-web/package.json`/`package-lock.json`，Rust 版本由各工程 `Cargo.toml`/`Cargo.lock` 管理。升级后必须同步锁文件、第三方 notices、依赖清单和治理证据。

普通依赖的精确版本以中央清单和锁文件为准，不在本规范复制容易过期的版本表。当前批准的技术代际是 .NET 10、React 19 和 xUnit v3；改变技术代际时必须作为独立专项评审和验证。

### NPOI 强制规则

**NPOI 必须保持 `2.7.6`。严禁升级到 `2.8.0`。** `2.8.0` 的额外维护费用条款不符合本项目“免费、开源、可商用”的依赖策略。任何依赖升级、自动化代理或批量更新都必须检查并保持：

```xml
<PackageVersion Include="NPOI" Version="2.7.6" />
```

不得通过传递依赖、局部项目版本或 lock 文件间接引入 NPOI `2.8.0`；提交前应搜索仓库和生成的依赖清单确认没有该版本。

其它依赖规则：

- 优先免费开源、许可证清晰、维护活跃、能离线/受控打包的库；禁止商业格式锁定、未审查二进制和不明来源下载。
- 依赖升级必须是独立、可审计的变更；不要把 React、lucide、xUnit 等大版本迁移与无关业务修复混在同一未说明的补丁中。
- 运行时浏览器、Cargo、NuGet、npm 缓存应定向到仓库运行目录或 CI workspace，避免写系统 C 盘；清理缓存前必须确认可重新获得且用户接受重新下载。
- 运行 `node scripts/generate-dependency-governance.mjs artifacts/dependency-governance --release --verify-repository`，结果必须 `unresolved=0`、`disallowed=0`。

## 5. 前端、桌面和资源规范

- React 19 使用公开 API；不得读取 `__reactProps$` 等私有字段，不得用兼容层掩盖类型或生命周期问题。
- 页面组件负责展示和组合；查询、变更、轮询、表单模型、导出和平台桥接应放在可测试的 hook/model/service 中。
- Tauri 只负责原生窗口、文件对话框、桌面令牌和 sidecar 生命周期；业务规则仍由 API/Application 提供。桌面保存路径必须来自用户显式选择。
- 浏览器/桌面报表 PDF 统一使用后端受控渲染能力；不恢复前端 DOM 截图、Base64 写盘、`html2canvas`/`jsPDF` 等重复链路。
- Firefox/WebKit 桌面/移动重型验收只在 `.github/workflows/browser-compatibility.yml` 通过 `workflow_dispatch` 手动触发，不加入每次提交的普通 Quality Gate。
- 不做 Windows Authenticode、macOS Developer ID 或 Apple 公证；Tauri updater 的包签名/公钥信任合同仍必须保持。

## 6. 测试与质量门禁

改动范围决定验证深度；涉及依赖、路径、打包、API 或基础设施时不得只跑单元测试。

```powershell
# 依赖还原（锁定模式）
dotnet restore ExportDocManager.sln --locked-mode --configfile NuGet.Config

# C# 格式和严格构建
dotnet format ExportDocManager.sln --verify-no-changes --no-restore
dotnet build ExportDocManager.sln -c Release --no-restore -warnaserror -m:1 -p:BuildInParallel=false

# 完整 .NET 测试；有 Chromium 时启用真实 PDF 测试
./scripts/run-tests.ps1 -Configuration Release -NoRestore -RequireBrowserPdfTests -NoPause

# Web
npm --prefix apps/export-doc-web ci
npm --prefix apps/export-doc-web run build
npm --prefix apps/export-doc-web run test:accessibility-contracts
npm --prefix apps/export-doc-web run test:scale-contracts
npm --prefix apps/export-doc-web run test:visual-baselines

# 脚本、依赖、公开源码和工作流门禁
pwsh -NoProfile -File scripts/verify-script-suite.ps1
node scripts/verify-github-workflow-actions.mjs
node scripts/test_tauri_updater_release_contract.mjs
pwsh -NoProfile -File scripts/github/verify-public-source.ps1
git diff --check
```

Rust 修改必须至少执行对应工程的 `cargo fmt --check` 和 `cargo test --locked`；发布/RID 修改还要执行相应 locked restore/build。Firefox/WebKit、真实 Docker/PostgreSQL、ARM64/macOS 真机属于独立手动/CI 验收，不能在本机结果中虚报为已完成。

## 7. 脚本、文件和文档规范

- 公共脚本必须传播真实退出码、处理超时/取消、清理进程树和临时文件；PowerShell 外部参数使用安全参数列表，不拼接未经验证的命令行字符串。
- 普通用户入口只使用 `scripts/` 根部的 `.cmd`/公开脚本；`lib/`、`prepare-*`、`verify-*` 是内部组合部件，不要为方便新增第二套入口。
- 文件名保持稳定大小写和 NFC；导入路径大小写必须与实际文件名完全一致，TypeScript 继续启用 `forceConsistentCasingInFileNames`。
- 文档中的“当前”数字、版本、路径和测试结果必须来自最近一次真实门禁；历史数字放在带日期的归档条目中，不覆盖当前事实源。
- 不在源码中提交密码、令牌、私钥、证书、真实数据库、客户文件或内部 `KEY/` 产物；`.env.example` 只能包含公开占位示例。

## 8. Git 交付规范

- 默认从 `codex/` 前缀分支工作；不要未经明确要求直接改写远端历史或强制推送。
- 提交前检查 `git status`、`git diff --stat`、`git diff --check`、暂存区内容和生成物；只提交与任务相关的文件。
- 依赖升级提交应说明版本、许可证/商业策略和已执行的治理门禁；NPOI `2.7.6` 必须在提交说明或验证结果中明确保留。
- 推送前确认 `origin/main` 没有未审查漂移；用户明确要求发布时才推送 `main`。

## 9. 工作区空间清理规则

空间清理必须先盘点、再列计划、后删除。推荐流程：

```powershell
pwsh -NoProfile -File scripts/clean-generated-artifacts.ps1 -ListOnly
pwsh -NoProfile -File scripts/clean-generated-artifacts.ps1 -IncludeCodexRuntimeWorkspaces
```

清理脚本默认只删除可重建的 `artifacts/`、`bin/`、`obj/`、`dist/`、`target/`、`TestResults/` 和一次性测试工作区，并保留交付输出及可复用依赖/浏览器缓存。只有用户明确确认后，才使用：

- `-IncludeNodeModules`：删除 npm 安装树；
- `-IncludePackageCaches`：删除 NuGet/npm/Cargo 审计缓存；
- `-IncludeCodexRuntime`：删除整个本地代理运行缓存；
- `-IncludeReleaseOutputs`：删除便携包、安装器或注册机输出；
- `-IncludeLegacyRuntimeAssets`：删除本地浏览器资源副本。

绝不通过清理脚本或手工命令删除 `.git`、`App_Data`、`Templates`、`OcrModels`、`Resources`、业务数据库、用户备份、已确认仍需的浏览器资源或系统外目录。已推送且干净的临时 Git worktree 应使用精确的 `git worktree remove --force <path>`，完成后运行 `git worktree prune`；不要直接递归删除包含未提交工作的 worktree。

清理后应重新检查：

```powershell
git status --short --branch
git worktree list
git diff --check
```

清理生成物不会改变源码或锁文件；下次构建会按现有脚本重新生成。若清理会导致大规模重新下载，先告知用户预估释放空间和需要保留的缓存。
