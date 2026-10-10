# ExportDocManager Tauri + React + Rust 重构协作与工程规则

本文件适用于 Rust 重构分支及其 worktree。用户于 2026-09-20 明确调整为 Tauri 2 最新稳定版 + React + Rust：桌面使用 Tauri + React + SQLite，网页／Docker 使用同一 React 前端 + Rust HTTP 服务 + PostgreSQL 18。直接复用原 C# 项目的前端（只读对照来源为 `origin/tauri-csharp-net10-backup` 与 Git 历史），后端统一 Rust，停用并删除 Slint／egui 客户端。Tauri 只负责桌面宿主，不在浏览器或容器中运行桌面壳。本文描述修改、验证和交付规则，不表示全部迁移已完成。用户明确指令优先；当前进度以源码、`docs/当前架构事实.md` 和 `docs/Rust原生架构与选型.md` 为准。

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
   - `docs/Rust桌面平台适配与验收.md`：Tauri 稳定版本证据、跨平台适配边界，以及各 OS／架构分别完成的检查。
   - `docs/产品架构与文档总览.md`：产品形态、运行方式和门禁总览。
   - `docs/程序改进重构进度文档.md`：按日期保存的实施证据；旧条目只用于追溯，不能当作当前契约。
   - `docs/运行目录与路径存储审查清单.md`：路径、缓存、临时文件和系统目录审查规则。
   - `docs/多平台与多架构支持矩阵.md`：RID、平台、架构和真机验收边界。
   - `scripts/README.md` 与 `scripts/clean-generated-artifacts.ps1`：脚本入口和空间清理边界。

3. 如果工作树已有修改，必须保留并避开无关文件；不要用 `git reset --hard`、`git checkout --`、广泛 `Remove-Item` 或其它不可逆操作覆盖用户工作。

## 2. 项目形态与目录边界

- 2026-09-30 产品定位：正式桌面交付 Document 单证版与 Sales 业务员版，单证版为主力，Sales 定位个人外贸工作台；网页/Docker 多用户 Full 保留完整功能。按用户本日明确要求，本地脚本另支持显式 `-Edition Full` 生成 **Full + SQLite 单机测试便携包**，用于完整界面布局、公告通知与 OA 登记流程测试，使用独立 Full 目录和测试包身份。默认单版仍为 Document，批量 All/正式发布/安装器仍只生成 Document、Sales；不恢复 Administration 单机包，不删除共享实现或已有数据。Full SQLite 测试不替代 PostgreSQL 多用户权限、并发和部署验收。

- 正式桌面为 `apps/export-doc-tauri`，复用 `apps/export-doc-web` 的原版 React 界面；Tauri 在进程内托管共用 Rust HTTP 适配器与 SQLite 应用服务，不启动 .NET sidecar。Node 仅用于构建，WebView 是明确采用的桌面显示组件。
- 网页前端继续位于 `apps/export-doc-web`，保留 React 19、原布局和操作；`apps/export-doc-server` 是 Rust HTTP 组合根，团队及 Docker 使用 PostgreSQL 18，不能改用 SQLite 或把数据库账号交给前端。
- `crates/export-doc-contracts` 管理生成的 API 契约；`export-doc-domain` 放纯业务规则；`export-doc-engine` 编排用例；`export-doc-storage` 提供存储边界与 SQLite／PostgreSQL 适配。数据库 SQL 不进入 UI 或用例协调器，Domain 不引用 GUI、HTTP、数据库、进程或宿主文件系统。
- UI 状态／事件、应用用例、业务校验、存储、文件、报表／PDF、Excel、OCR、邮件和系统集成须按职责分模块。能力依赖按 Cargo feature 或独立 crate 裁剪，核心不得为了单一可选功能拉入整套浏览器实现。
- Excel 能力位于 `crates/export-doc-excel`，由组合根显式启用 `excel` feature；现有 `tools/excel-analyzer-rs` 同时提供库和对照 CLI，禁止复制第二套表头／字段识别器。文件任务位于 `engine::tasks`，状态和结果事务化发布，文件预览不得隐式写入正式业务数据。
- Windows、Linux、macOS 桌面共同维护一套 Tauri 2 + React + Rust + SQLite 源码；Windows 在当前宿主优先运行验证，其它目标在对应 runner／设备验收。原生窗口句柄、对话框、剪贴板、打印、进程树和安装包进入平台适配边界，禁止把 Windows 路径、COM／Win32 或 Linux／macOS 命令散入业务层。每个平台分别记录编译、运行和功能证据，预留接口不等于已支持。
- `origin/tauri-csharp-net10-backup` 与 Git 历史只读作为界面、后端和操作流程基线。2026-10-02 起 Rust 仓库使用自身 `.git`，旧 C# 目录已退出工作流程；原数据及私有资料另存本地备份，不再依赖相邻工作树。按用户 2026-09-28 清理要求，本分支删除已退役的 C# 源码、测试、工程和部署文件；只保留仍被 Rust 使用的冻结契约、参考数据和跨实现夹具。Slint／egui 已删除，不维护第二套桌面界面；清理旧源码不代表剩余迁移差距已验收。
- 不以通用 JSON 表单或同名路由代替原有完整业务。逐项对照主导航、页签、表单顺序、表格编辑、键盘／中文 IME、权限、并发、导入导出、报表和维护流程；未完成或未验收的能力须明确记录。
- 按用户 2026-09-20 的要求，优先逐页完成原版界面、后端用例和操作衔接，积累一批后集中联调，最后统一执行完整门禁。开发中只做必要的快速编译和针对实际失败的回归，不在每个模块后重复全量构建／测试；已经通过且未受后续修改影响的检查不重复运行。
- 原生界面统一提供可折叠分区：常用内容默认展开，地址／银行明细、备用字段、信用证、高级设置等低频内容默认收起。展开状态保存在当前界面会话内；收起不丢失草稿、已保存数据或校验，出错时自动展开对应分区。
- `src`、`crates`、`apps`、`tests`、`tools` 中的 `bin/`、`obj/`、`dist/`、`target/`、`node_modules/`，以及根 `target/`、`artifacts/`、`TestResults/`、`.codex-runtime/` 都是生成或本地工作区，不得提交到 Git。
- 原生迁移必须按功能、权限、数据、界面和交付分别验证；创建窗口、生成 API 路由或通过编译不代表功能等价。原版行为从只读备份分支和 Git 历史追溯，仍参与验证的冻结契约、参考数据与有效夹具继续保留。不能把原版测试结果写成 Rust 已通过；清理无调用的旧实现前须核对生产引用和当前验收范围。

## 3. 架构不变量

### 3.1 运行目录和数据

- 所有持久化路径由启动组合根显式注入 `AppRoot`/`DataRoot`，使用 `RuntimePaths`／受管路径接口；服务不得自行构造全局路径提供器，也不得在静态字段中缓存宿主路径。
- 数据库、配置、日志、备份、模板、缓存、浏览器 profile、PostgreSQL 客户端、OCR/浏览器资源和随包工具必须落在运行目录或明确职责的容器层；不要默认写入 `C:\Users\...\AppData`、系统 TEMP、ProgramData 或系统级工具缓存。
- 配置中保存相对路径；写入、读取、迁移前后都必须验证仍在受管根目录内，并拒绝符号链接、联接点、路径穿越、磁盘根和不可写目录。
- SQLite 仅用于桌面单机；团队/服务器/容器模式使用 PostgreSQL 18。按用户 2026-09-28 的要求，Rust 数据库从版本 5 起支持逐版兼容升级，保留已有数据；版本 4 及更早试验库、C# 库不做兼容。只有确认数据库不存在时才能初始化，已有空文件、损坏库、版本不明或比程序更新的库必须报错。`export-doc-storage` 保留版本 5 建库基线和已发布迁移，后续变更追加有序的 SQLite/PostgreSQL 事务迁移及保留数据、失败回滚、重启幂等回归，不修改版本标记伪装升级、不双读或猜测式修复。SQLite 业务库统一在 DataRoot/Database；PostgreSQL 升级使用独立维护账号，普通 API 不获得 DDL 权限。
- 单 API 实例锁、后台任务、恢复和迁移必须保持 fail-closed；不能把数据库/文件系统故障伪装成空列表或普通业务冲突。

### 3.2 跨平台路径、文件名和时间

- 文件名先做 NFC 规范化，并遵守 Windows/Linux/macOS 共同非法字符、尾部点/空格、保留设备名和长度规则。
- Windows 路径比较按不区分大小写；Linux 和 macOS 目标文件系统的大小写语义必须被尊重，不能为了“看起来一致”在大小写敏感卷上折叠不同文件。
- 本地文件路径使用 `Path`/`PathBuf` 和平台路径 API，网络地址使用 URI API；不得以硬编码分隔符、盘符或当前工作目录推导宿主路径。
- 业务自然日使用严格日期类型，API 为 `YYYY-MM-DD`；时间点保留明确偏移并使用 RFC 3339。禁止用本地无时区时间或字符串截断推断业务日期。金额和数量使用精确十进制，不用浮点数代替业务金额。

### 3.3 API、错误和契约

- `/openapi/v1.json` 是唯一 API 契约事实源。当前由 `export-doc-contracts::openapi` 组合冻结的官方 .NET 基线与新增 Rust 能力；基线端点、schema、认证、错误和权限须逐项保留验证。Rust DTO、路由和权限元数据通过 `crates/export-doc-contracts/examples/generate_clients.rs` 生成，React 客户端从相同文档生成；禁止手工修改生成文件或建立第二套 endpoint/schema。修改契约组合器或生成器时须验证 schema、错误、认证及权限元数据，不能静默变更契约。
- 端点认证、桌面令牌和许可证要求使用 endpoint metadata；不要按 `/api` 前缀、路径白名单或前端路由猜测授权。
- 业务错误按现有分类映射：校验 400、权限 403、明确资源不存在 404、冲突 409、繁忙 429、依赖不可用 503、超时 504；不要把数据库、文件或外部工具故障包装成 404/409。
- 所有异步公共操作都要有明确取消边界、超时和资源清理；后台任务完成、失败、取消和输出清理必须可观察且幂等。

### 3.4 解耦与扩展

- 优先扩展接口和职责明确的模块，不在大型协调器中继续堆 UI、数据库、路径和进程控制逻辑。
- 不为单个客户文件名、历史测试快照或旧数据添加分支补丁；先抽象通用解析/校验规则，再补最小回归测试。
- 优先修复通用根因，保持代码精简、清晰、优美和高效；禁止通过重复分支、临时兼容层、特例选择器或复制实现堆叠“补丁式兼容”，新增代码必须减少真实复杂度并有明确职责。
- 不引入 Redis、消息队列、第二数据库、LocalStorage 持久化或新的默认导出/图片目录，除非需求和架构文档明确批准。
- 缺少可选能力模块时返回明确“不支持”，不要复制一份降级实现或静默改变数据语义。

## 4. 依赖与许可证策略（硬约束）

当前交付依赖以 Cargo workspace 的 `Cargo.toml`／`Cargo.lock`、React／Tauri 的 `package.json`／`package-lock.json` 和 `eng/native-runtime-packages.json` 为准。升级后同步 notices、锁文件和治理证据。本分支不再保留 .NET 工程与 SDK 配置，API 契约与两端客户端统一由 Rust 生成；Node 用于前端构建与开发验证。

普通依赖的精确版本以中央清单和锁文件为准，不在本规范复制容易过期的版本表。根 Cargo workspace 集中管理 Rust 基线及共享依赖；前端保持 React 19，不把无关升级混入业务修复。

- 发布包默认包含对应产品已实现的 OCR 资源；`-WithoutOcr` 只用于明确不提供识别的轻量校验包，不能冒充 Full 发布。启动时核查系统 WebView 和已声明的原生资源。
- Tauri 核心、构建工具、前端 API 和插件采用查询时官方最新稳定 2.x，并在 Cargo/npm 清单与锁文件精确固定；核对 MIT／Apache-2.0 等实际许可，生成 notices 与依赖治理证据，不把“2.0”理解为锁回初始 2.0.0。
- 桌面交付依赖树必须包含 Tauri／平台 WebView 和 SQLite，排除 Slint／egui、PostgreSQL 服务端适配器、Node 与 .NET 运行依赖。网页／容器不引入桌面 Tauri 库；可选受控工具单独声明用途、来源、许可和真实功能边界。

### Rust 交付与原 C# 对照边界

业务后端与桌面宿主使用 Rust、三端界面使用同一 React；Excel／报表分别由 `export-doc-excel`／`export-doc-report` 承担。Rust 交付不包含 NPOI、托管程序集或 .NET Runtime，构建及部署不调用 dotnet restore/publish。PDFium／ONNX 可以从已核验的 NuGet 原生归档抽取库文件与许可原文，这不引入 NuGet 客户端或 .NET 运行依赖。

只读 C# 基线的 NPOI `2.7.6`、.NET 10 与 xUnit v3 仅用于历史对照，不在当前仓库维护其 SDK、工程或锁图；NPOI `2.8.0` 的额外维护费用条款不属于批准的依赖路线。Rust notices／SBOM 只采集 npm、Cargo 和受管原生资源，不能把历史对照依赖写成当前交付依赖。

其它依赖规则：

- 优先免费开源、许可证清晰、维护活跃、能离线/受控打包的库；禁止商业格式锁定、未审查二进制和不明来源下载。
- 依赖升级必须是独立、可审计的变更；不要把 React、lucide 等大版本迁移与无关业务修复混在同一未说明的补丁中。
- 依赖校验和治理门禁也必须遵守精简原则：共享解析、版本判定和错误格式化逻辑，避免同一规则在多个脚本中复制；不要为旧版本、旧锁文件或历史生成物增加兼容分支，规则变化时直接更新正式契约、锁文件和最小回归测试。
- 运行时浏览器、Cargo、NuGet、npm 缓存应定向到仓库运行目录或 CI workspace，避免写系统 C 盘；清理缓存前必须确认可重新获得且用户接受重新下载。
- 同一批次的手工 Cargo 检查和打包命令显式使用相同的仓库 `CARGO_HOME`；公开构建脚本会保留已设置的环境值，不能让继承的全局缓存路径与仓库缓存交替使用而触发重复编译。
- 共用同一个 `target/` 的 Cargo build/check/test 串行执行，必须等测试及 doc-test 全部退出后再启动另一项 Cargo 编译；不能依赖构建文件锁隔离测试运行期间的产物改写。已复制到独立目录的桌面程序验收可与编译并行。
- 运行 `node scripts/generate-dependency-governance.mjs artifacts/dependency-governance --release --verify-repository`，结果必须 `unresolved=0`、`disallowed=0`。

## 5. 前端、桌面和资源规范

- React 19 使用公开 API；不得读取 `__reactProps$` 等私有字段，不得用兼容层掩盖类型或生命周期问题。
- React 页面负责展示和组合，hook/model/service 管理草稿、查询、变更、轮询及导出；Tauri command 只适配窗口、对话框、更新和受控平台操作，业务使用共用 Rust 应用服务。HTTP 与 IPC 不重复实现业务；阻塞数据库、报表及进程操作不在 UI 线程执行。保存路径来自用户显式选择。
- 桌面与服务器复用 Rust 报表模型和受控 PDF 输出；优先原生排版／PDF 能力。旧模板逐类对照实际输出，不能默默退成简化表格，也不恢复 DOM 截图、Base64 写盘、`html2canvas`/`jsPDF` 等重复链路。
- 内置 `.dtpl` 模板与空白设计使用相同组件、属性和 Rust 排版规则。新增默认样式时同步提供可视化设置入口，覆盖图层打印、字体、线条、表格和字段配置；不能只在生成脚本中加入用户无法编辑的专用票面。验收包括从空白组合关键结构、撤销、保存回读及真实预览/PDF；编辑画布显示不等于正式输出已验证。
- Firefox/WebKit 桌面/移动重型验收只在 `.github/workflows/browser-compatibility.yml` 通过 `workflow_dispatch` 手动触发，不加入每次提交的普通 Quality Gate。
- 不做 Windows Authenticode、macOS Developer ID 或 Apple 公证；保留 Tauri updater 的签名／公钥信任合同；Rust 包完成独立更新验收前不得宣称旧 .NET 发布资产可直接升级为 Rust 版。

## 6. 测试与质量门禁

改动范围决定验证深度；涉及依赖、路径、打包、API 或基础设施时不得只跑单元测试。

批次实现和集中联调完成后统一执行最终门禁，开发中只做必要编译与针对失败的回归，不逐模块重复全量验证。最终 Rust 主工作区至少执行 `cargo fmt --all --check`、`cargo test --locked --workspace` 和 `cargo check --locked --workspace --all-features`。数据库变更须用隔离的真实 PostgreSQL 18 与 SQLite 验证同一业务契约；忽略的实库测试不算通过。HTTP 变更须验证真实 React 请求、认证、授权、错误、上传下载和会话；桌面界面须启动 Tauri + React，检查截图、表格滚动／编辑、中文输入及实际 PDF。发布时执行对应平台 locked build 和包内依赖审查。

前端与工程门禁如下；C# 对照通过只读备份分支和 Git 历史追溯，旧测试结果不作为 Rust 验收：

```powershell
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
- 依赖升级提交应说明实际变更的 Cargo/npm/原生资源版本、许可证/商业策略和已执行的治理门禁；历史 .NET 依赖不混入 Rust 交付说明。
- 推送前重新获取并确认 `origin/main` 没有未审查漂移；用户明确要求推送 `main` 或发布时，完成验证后以普通快进方式交付，不强制覆盖远端历史。

## 9. 工作区空间清理规则

空间清理必须先盘点、再列计划、后删除。推荐流程：

```powershell
pwsh -NoProfile -File scripts/clean-generated-artifacts.ps1 -ListOnly
pwsh -NoProfile -File scripts/clean-generated-artifacts.ps1 -IncludeCodexRuntimeWorkspaces
```

用户要求保留可复用缓存时，先从盘点中排除仍需使用的下载、浏览器及编译缓存，定向清理已结束的验证输出。默认清理器会包含 `target/`，不能未经筛选就把全部候选项当作已过期内容。

清理脚本默认只删除可重建的 `artifacts/`、`bin/`、`obj/`、`dist/`、`target/`、`TestResults/` 和一次性测试工作区，并保留交付输出及可复用依赖/浏览器缓存。只有用户明确确认后，才使用：

- `-IncludeNodeModules`：删除 npm 安装树；
- `-IncludePackageCaches`：删除 NuGet/npm/Cargo 审计缓存；
- `-IncludeCodexRuntime`：删除整个本地代理运行缓存；
- `-IncludeReleaseOutputs`：删除便携包、安装器或注册机输出；

绝不通过清理脚本或手工命令删除 `.git`、`App_Data`、`Templates`、`OcrModels`、`Resources`、业务数据库、用户备份、已确认仍需的浏览器资源或系统外目录。已推送且干净的临时 Git worktree 应使用精确的 `git worktree remove --force <path>`，完成后运行 `git worktree prune`；不要直接递归删除包含未提交工作的 worktree。

2026-10-10 用户补充授权：可复用缓存保留；确认已过期的测试程序包和已经无用的隔离测试数据可以清理，包括包内的资源副本与测试数据库。此例外只适用于已核实来源和用途的测试输出，不能仅凭目录名或修改时间判断。须先列出精确绝对路径、检查进程占用和链接/Git 边界，并校验保留的实际程序、业务数据、用户备份与私有补丁；默认清理脚本的保护规则不变，不能用此例外扩大到真实业务目录。

清理后应重新检查：

```powershell
git status --short --branch
git worktree list
git diff --check
```

清理生成物不会改变源码或锁文件；下次构建会按现有脚本重新生成。若清理会导致大规模重新下载，先告知用户预估释放空间和需要保留的缓存。
