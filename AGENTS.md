# ExportDocManager Rust 原生重构协作与工程规则

本文件适用于 Rust 重构分支及其 worktree。用户已批准桌面改为 Rust + Slint + SQLite，网页／Docker 保留 React 界面和 PostgreSQL 18，业务及 HTTP 服务逐步由共用 Rust 模块实现。本文描述修改、验证和交付规则，不表示全部迁移已完成。用户明确指令优先；当前进度以源码、`docs/当前架构事实.md` 和 `docs/Rust原生架构与选型.md` 为准。

## 1. 开始工作前

1. 先查看工作树和远端状态：

   ```powershell
   git status --short --branch
   git log -1 --oneline
   git fetch origin main
   ```

2. 先读以下事实源，再决定修改位置：

   - `docs/当前架构事实.md`：当前部署、目录、数据库、API 和模块边界的唯一事实源。
   - `docs/Rust原生架构与选型.md`：本分支的 Rust 实现、未完成项目、许可与原生验收边界；区分重构事实和保留的 C# 对照基线。
   - `docs/Rust桌面平台适配与验收.md`：Slint 稳定版本证据、跨平台适配边界，以及各 OS／架构分别完成的检查。
   - `docs/产品架构与文档总览.md`：产品形态、运行方式和门禁总览。
   - `docs/程序改进重构进度文档.md`：按日期保存的实施证据；旧条目只用于追溯，不能当作当前契约。
   - `docs/运行目录与路径存储审查清单.md`：路径、缓存、临时文件和系统目录审查规则。
   - `docs/多平台与多架构支持矩阵.md`：RID、平台、架构和真机验收边界。
   - `scripts/README.md` 与 `scripts/clean-generated-artifacts.ps1`：脚本入口和空间清理边界。

3. 如果工作树已有修改，必须保留并避开无关文件；不要用 `git reset --hard`、`git checkout --`、广泛 `Remove-Item` 或其它不可逆操作覆盖用户工作。

## 2. 项目形态与目录边界

- 正式桌面方向为 `apps/export-doc-slint`，直接调用 Rust 应用服务并使用 SQLite；不依赖 Tauri、WebView、React、Node 或 .NET sidecar 运行桌面业务。
- 网页前端继续位于 `apps/export-doc-web`，保留 React 19、原布局和操作；`apps/export-doc-server` 是 Rust HTTP 组合根，团队及 Docker 使用 PostgreSQL 18，不能改用 SQLite 或把数据库账号交给前端。
- `crates/export-doc-contracts` 管理生成的 API 契约；`export-doc-domain` 放纯业务规则；`export-doc-engine` 编排用例；`export-doc-storage` 提供存储边界与 SQLite／PostgreSQL 适配。数据库 SQL 不进入 UI 或用例协调器，Domain 不引用 GUI、HTTP、数据库、进程或宿主文件系统。
- UI 状态／事件、应用用例、业务校验、存储、文件、报表／PDF、Excel、OCR、邮件和系统集成须按职责分模块。能力依赖按 Cargo feature 或独立 crate 裁剪，核心不得为了单一可选功能拉入整套浏览器实现。
- Excel 能力位于 `crates/export-doc-excel`，由组合根显式启用 `excel` feature；现有 `tools/excel-analyzer-rs` 同时提供库和对照 CLI，禁止复制第二套表头／字段识别器。文件任务位于 `engine::tasks`，状态和结果事务化发布，文件预览不得隐式写入正式业务数据。
- Windows、Linux、macOS 桌面共同维护一套 Rust + Slint + SQLite 源码；Windows 在当前宿主优先运行验证，其它目标在对应 runner／设备验收。原生窗口句柄、对话框、剪贴板、打印、进程树和安装包进入平台适配边界，禁止把 Windows 路径、COM／Win32 或 Linux／macOS 命令散入业务层。每个平台分别记录编译、运行和功能证据，预留接口不等于已支持。
- 原 C# `src/`、Tauri、Web 界面及测试保留作行为对照；原 .NET 10、xUnit v3 门禁只适用于相关源码修改。`apps/export-doc-native` 是早期 egui 比较工程，不进入 Slint 交付包，不再复制业务实现。
- 不以通用 JSON 表单或同名路由代替原有完整业务。逐项对照主导航、页签、表单顺序、表格编辑、键盘／中文 IME、权限、并发、导入导出、报表和维护流程；未完成或未验收的能力须明确记录。
- 按用户 2026-09-16 的要求，优先逐页完成原版界面、后端用例和操作衔接，积累一批后集中联调，最后统一执行完整门禁。开发中只做必要的快速编译和针对实际失败的回归，不在每个模块后重复全量构建／测试；已经通过且未受后续修改影响的检查不重复运行。
- 原生界面统一提供可折叠分区：常用内容默认展开，地址／银行明细、备用字段、信用证、高级设置等低频内容默认收起。展开状态保存在当前界面会话内；收起不丢失草稿、已保存数据或校验，出错时自动展开对应分区。
- `src`、`crates`、`apps`、`tests`、`tools` 中的 `bin/`、`obj/`、`dist/`、`target/`、`node_modules/`，以及根 `target/`、`artifacts/`、`TestResults/`、`.codex-runtime/` 都是生成或本地工作区，不得提交到 Git。

## 3. 架构不变量

### 3.1 运行目录和数据

- 所有持久化路径由启动组合根显式注入 `AppRoot`/`DataRoot`，Rust 使用 `RuntimePaths`／受管路径接口，C# 对照实现使用 `IAppPathProvider`；服务不得自行构造全局路径提供器，也不得在静态字段中缓存宿主路径。
- 数据库、配置、日志、备份、模板、缓存、浏览器 profile、PostgreSQL 客户端、OCR/浏览器资源和随包工具必须落在运行目录或明确职责的容器层；不要默认写入 `C:\Users\...\AppData`、系统 TEMP、ProgramData 或系统级工具缓存。
- 配置中保存相对路径；写入、读取、迁移前后都必须验证仍在受管根目录内，并拒绝符号链接、联接点、路径穿越、磁盘根和不可写目录。
- SQLite 仅用于桌面单机；团队/服务器/容器模式使用 PostgreSQL 18。项目尚未投产，不添加旧 v1—v7 数据兼容分支、猜测式迁移或双读逻辑；需要改变空库基线时直接更新正式 schema 和测试。
- 单 API 实例锁、后台任务、恢复和迁移必须保持 fail-closed；不能把数据库/文件系统故障伪装成空列表或普通业务冲突。

### 3.2 跨平台路径、文件名和时间

- 文件名先做 NFC 规范化，并遵守 Windows/Linux/macOS 共同非法字符、尾部点/空格、保留设备名和长度规则。
- Windows 路径比较按不区分大小写；Linux 和 macOS 目标文件系统的大小写语义必须被尊重，不能为了“看起来一致”在大小写敏感卷上折叠不同文件。
- 只使用 `Path`/`PathBuf`、`Path.Combine`、URI API 和平台无关分隔符；不得拼接硬编码 `\`、`/`、盘符或假定当前工作目录。
- 业务自然日使用严格日期类型，API 为 `YYYY-MM-DD`；时间点保留明确偏移并使用 RFC 3339。Rust 使用日期／带时区类型，C# 对照实现继续使用 `DateOnly`／`DateTimeOffset`；禁止用本地无时区时间或字符串截断推断业务日期。金额和数量使用精确十进制，不用浮点数代替业务金额。

### 3.3 API、错误和契约

- `/openapi/v1.json` 是唯一 API 契约事实源。迁移期间从原 .NET 官方 OpenAPI 导出契约，Rust DTO、路由和权限元数据通过 `scripts/generate-native-api-client.mjs` 生成，React 客户端仍从相同契约生成；禁止手工修改生成文件或建立第二套 endpoint/schema。切换到 Rust 契约生成器须独立验证全部 schema、错误、认证及权限元数据，不能静默变更契约。
- 端点认证、桌面令牌和许可证要求使用 endpoint metadata；不要按 `/api` 前缀、路径白名单或前端路由猜测授权。
- 业务错误按现有分类映射：校验 400、权限 403、明确资源不存在 404、冲突 409、繁忙 429、依赖不可用 503、超时 504；不要把数据库、文件或外部工具故障包装成 404/409。
- 所有异步公共操作都要有明确取消边界、超时和资源清理；后台任务完成、失败、取消和输出清理必须可观察且幂等。

### 3.4 解耦与扩展

- 优先扩展接口、能力模块和职责明确的 partial/服务，不在大型协调器中继续堆 UI、数据库、路径和进程控制逻辑。
- 不为单个客户文件名、历史测试快照或旧数据添加分支补丁；先抽象通用解析/校验规则，再补最小回归测试。
- 优先修复通用根因，保持代码精简、清晰、优美和高效；禁止通过重复分支、临时兼容层、特例选择器或复制实现堆叠“补丁式兼容”，新增代码必须减少真实复杂度并有明确职责。
- 不引入 Redis、消息队列、第二数据库、LocalStorage 持久化或新的默认导出/图片目录，除非需求和架构文档明确批准。
- 缺少可选能力模块时返回明确“不支持”，不要复制一份降级实现或静默改变数据语义。

## 4. 依赖与许可证策略（硬约束）

所有 .NET NuGet 包版本集中在 `Directory.Packages.props`，SDK 最低基线和滚动策略集中在 `global.json`，Web 版本集中在 `apps/export-doc-web/package.json`/`package-lock.json`，Rust 版本由各工程 `Cargo.toml`/`Cargo.lock` 管理。升级后必须同步锁文件、第三方 notices、依赖清单和治理证据。

普通依赖的精确版本以中央清单和锁文件为准，不在本规范复制容易过期的版本表。本分支已批准 Rust + Slint 原生迁移，根 Cargo workspace 集中管理 Rust 基线及共享依赖；React 19、原 .NET 10 和 xUnit v3 保持现有代际，不把无关升级混入迁移。

- Slint 使用已审查的 Royalty-free 2.0 桌面应用许可路径；顶层可访问的“关于”页面保留官方 `AboutSlint`，随包包含许可原文和 notices。升级时重新审查，不能删掉署名或泛化许可适用范围。
- Slint 交付依赖树须确认没有 WebView／Tauri／egui、Node 或 .NET 运行依赖；可选受控工具单独声明用途、来源、许可和真实功能边界。

### NPOI 强制规则（仅适用于保留对照的原 .NET 实现）

Rust 原生程序的后端与桌面最终全部使用 Rust：版本由根 `Cargo.toml`/`Cargo.lock` 精确锁定，crate 选型取查询时最新稳定版，Excel 与 PDF 由 `export-doc-excel`/`export-doc-report` 纯 Rust 实现，交付依赖树不含 NPOI、NuGet 或 .NET 运行依赖。下列 .NET/NuGet 规则只约束本工作树中保留对照的原 C#/Tauri 源码及其治理结果，只改 Rust 源码不触发这些约束，也不得把 NPOI/NuGet 版本表套用到 Cargo 依赖；Rust 桌面依赖图以 `native-desktop` 作用域进入同一治理脚本分开验收。

**NPOI 必须保持 `2.7.6`。严禁升级到 `2.8.0`。** `2.8.0` 的额外维护费用条款不符合本项目“免费、开源、可商用”的依赖策略。任何依赖升级、自动化代理或批量更新都必须检查并保持：

```xml
<PackageVersion Include="NPOI" Version="2.7.6" />
```

不得通过传递依赖、局部项目版本或 lock 文件间接引入 NPOI `2.8.0`；提交前应搜索仓库和生成的依赖清单确认没有该版本。

其它依赖规则：

- 优先免费开源、许可证清晰、维护活跃、能离线/受控打包的库；禁止商业格式锁定、未审查二进制和不明来源下载。
- 依赖升级必须是独立、可审计的变更；不要把 React、lucide、xUnit 等大版本迁移与无关业务修复混在同一未说明的补丁中。
- .NET SDK 使用精确稳定最低基线、`rollForward: latestFeature` 和 `allowPrerelease: false`，只允许同一 `major.minor` 内滚动到更新的稳定 feature band；CI/容器使用由该基线推导的稳定 `10.0.x` 通道，拒绝 preview、较低版本、跨 minor 和跨 major。Runtime/NuGet servicing 包继续精确锁定并由 lockfile 保证可复现，不使用通配版本或开放范围。
- 依赖校验和治理门禁也必须遵守精简原则：共享解析、版本判定和错误格式化逻辑，避免同一规则在多个脚本中复制；不要为旧版本、旧锁文件或历史生成物增加兼容分支，规则变化时直接更新正式契约、锁文件和最小回归测试。
- 运行时浏览器、Cargo、NuGet、npm 缓存应定向到仓库运行目录或 CI workspace，避免写系统 C 盘；清理缓存前必须确认可重新获得且用户接受重新下载。
- 运行 `node scripts/generate-dependency-governance.mjs artifacts/dependency-governance --release --verify-repository`，结果必须 `unresolved=0`、`disallowed=0`。

## 5. 前端、桌面和资源规范

- React 19 使用公开 API；不得读取 `__reactProps$` 等私有字段，不得用兼容层掩盖类型或生命周期问题。
- 页面组件负责展示和组合；查询、变更、轮询、表单模型、导出和平台桥接应放在可测试的 hook/model/service 中。
- Slint 视图只负责展示、布局和输入，Rust controller/model 管理草稿、选择、焦点和事件，应用服务负责业务；阻塞数据库、报表及外部进程操作不在 UI 线程执行。优先原生文件对话框、剪贴板、打印和窗口接口；保存路径来自用户显式选择。
- 桌面与服务器复用 Rust 报表模型和受控 PDF 输出；优先原生排版／PDF 能力。旧模板逐类对照实际输出，不能默默退成简化表格，也不恢复 DOM 截图、Base64 写盘、`html2canvas`/`jsPDF` 等重复链路。
- Firefox/WebKit 桌面/移动重型验收只在 `.github/workflows/browser-compatibility.yml` 通过 `workflow_dispatch` 手动触发，不加入每次提交的普通 Quality Gate。
- 不做 Windows Authenticode、macOS Developer ID 或 Apple 公证；原 Tauri updater 信任合同保留用于对照版，新原生更新机制完成签名／公钥验收前不得宣称可替换正式更新渠道。

## 6. 测试与质量门禁

改动范围决定验证深度；涉及依赖、路径、打包、API 或基础设施时不得只跑单元测试。

Rust 主工作区至少执行 `cargo fmt --all --check`、`cargo test --locked --workspace` 和 `cargo check --locked --workspace --all-features`。数据库变更须用隔离的真实 PostgreSQL 18 与 SQLite 验证同一业务契约；忽略的实库测试不算通过。HTTP 变更须验证真实 React 请求、认证、授权、错误、上传下载和会话；原生界面须启动 Slint，检查截图、表格滚动／编辑、中文输入及实际 PDF。发布时执行对应平台 locked build 和包内依赖审查。

下列 .NET 和原 Web 门禁按被修改的对照源码适用；只改 Rust 不要求把完整 C# 构建当作 Rust 验收，更不能借旧测试结果宣称新实现等价：

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
- 依赖升级提交应说明版本、许可证/商业策略和已执行的治理门禁；涉及原 .NET 实现的提交必须在说明或验证结果中明确保留 NPOI `2.7.6`，纯 Rust 依赖升级只说明 Cargo crate 版本、许可证和治理结果。
- 推送前确认 `origin/main` 没有未审查漂移；用户明确要求发布时才推送 `main`。

## 9. 工作区空间清理规则

空间清理必须先盘点、再列计划、后删除。推荐流程：

```powershell
pwsh -NoProfile -File scripts/clean-generated-artifacts.ps1 -ListOnly
pwsh -NoProfile -File scripts/clean-generated-artifacts.ps1 -IncludeCodexRuntimeWorkspaces
```

清理脚本默认只删除可重建的 `artifacts/`、`bin/`、`obj/`、`dist/`、`target/`、`TestResults/` 和一次性测试工作区，并保留交付输出及可复用依赖/浏览器缓存。只有用户明确确认后，才使用：

- `-PruneUnusedNuGetVersions`：按全部 `packages.lock.json` 删除未引用的普通 NuGet 精确版本，保留当前锁图、SDK Runtime/Host packs 和 NPOI `2.7.6`；
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
