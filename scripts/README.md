# 脚本使用说明

> 2026-09-20：当前主线为 Tauri 2 + React + Rust。原 C# 行为和脚本从只读固定提交 `ba9dbdca72590b06c5f32740e4cd58d3027578da` 及 Git 历史对照；这里的正式构建不发布 ASP.NET sidecar，不要求 .NET SDK／Runtime，不使用 NPOI。

## 本地入口

| 入口 | 用途 |
| --- | --- |
| `build-windows-desktop-run.cmd`／`.ps1` | 默认生成 Document、Sales；可用 `-Edition Full` 单独生成 Full + SQLite 测试便携包 |
| `build-windows-installers.cmd`／`.ps1` | 生成 Tauri NSIS 安装器，默认 Document，也可选择 Sales |
| `build-native.cmd`／`.ps1` | 当前平台 Rust + Tauri + React 便携包；默认 Document，目录 `artifacts/native-desktop/ExportDocManager.Tauri.Document` |
| `run-native.cmd`／`.ps1` | 默认启动 Document；`-Edition Sales` 或 `-Edition Full` 启动业务员版或 Full SQLite 测试包，`-AppRoot` 可指定包目录 |
| `package-native-web-server.ps1` | 原版 React 构建、Rust HTTP 服务、报表与可选 OCR 资源；目标数据库 PostgreSQL 18 |
| `run-native-docker.cmd`／`.ps1` | React + Rust HTTP + PostgreSQL 18 容器构建、启动和停止 |

普通用户只运行 `scripts/` 根入口；`lib/`、`prepare-*`、`verify-*`、`assert-*` 为内部组合部件。

### 维护文件分工

| 位置/命名 | 当前用途 |
| --- | --- |
| 根部 build/run/package 入口 | 用户构建、启动与打包；cmd 复用统一 PowerShell 宿主 |
| `lib/` | 构建、受管路径、部署生命周期、浏览器会话和界面场景等共享实现，不直接运行 |
| `github/` | 仓库初始化、公开源码检查及正式发布/镜像提升，仍被工作流引用 |
| `baselines/` | 源码与样式规模门禁，属于版本控制输入，不是临时测试报告 |
| `test_*`、`verify-*` | 模型、真实界面、平台与工程验证；不是安装运行依赖 |
| `prepare-*`、`provision-*` | 从中央清单准备并校验受管资源，供构建/发布复用 |

邮件模板并发回归为 `npm --prefix apps/export-doc-web run test:email-template-workspace-ui`，用真实 React 组件和受控延迟验证保存期间继续编辑、刷新失败后版本、模板历史身份及乱序响应。`node scripts/test_review_workflows_ui.mjs` 使用已构建的 `office_review` 示例和真实 Rust HTTP，检查币种分组、桌面／窄屏布局、邮件保存／历史恢复及诊断指标；其输出为隔离的 `artifacts/review-workflows-ui/<本次编号>`。运行前先完成 React 生产构建及 `cargo build --locked -p export-doc-server --example office_review`；不要与其它使用相同 target 的 Cargo 操作同时构建。

`test:concurrency-feedback` 包含 `test_packing_credit_workspace_ui.mjs`：真实 React 组件验证装柜保存续编/切换/加载取消、刷新失败后的版本，以及信用证导入/审查在切换、修订、卸载后的迟到成功与错误。`test:abortable-operations-ui` 另验证印章选择和二进制读取的资料切换、停用与卸载边界；`test_native_desktop_editions.mjs <包父目录> Full` 在真实 WebView2 核对 ArrayBuffer 字节与格式拒绝，再执行原桌面流程。SQLite 查询中断、回滚/连接复用及备份取消在 Storage 单元测试，专用查询合同由 SQLite 与 `test-native-postgres.ps1 -Scope Workflows` 共用。

同一并发门禁包含 `test_catalog_history_workspaces_ui.mjs`，核对 HS 选择时版本、原因确认、5000 条疑似作废的跨页选择与排除、年度预检分页，以及历史容量清理和切换取消。`test_report_template_draft_ui.mjs` 覆盖画布未确认文字和属性文字／数值缓冲的后台刷新、保存／离开保护及单步撤销。HS／历史用例和存储投影共用 SQLite 与 PostgreSQL 的业务合同；HS 操作说明见[年度维护与清理](../docs/HS编码年度维护与清理.md)。

2026-10-08 移除四个旧 HTML 报表 visual/pdf/pdf-pixels/print-pixels 入口及其专用阈值和解析代码：它们将 .dtpl 当 HTML 读取，不能验证当前 Rust 输出。现用浏览器定位保留在 `lib/chromium-executable.mjs`，业务资料 PDF 查看器会话仍保留。原 HTML 对照夹具不作为运行模板。

`build-native.ps1 -PreflightOnly -NoPause` 只检查 Rust、Node、curl 等构建工具。`-Configuration Debug` 用于联调，默认 Release。`-RustTarget` 明确目标架构；`-Bundles nsis`、`-Bundles deb,appimage` 或 `-Bundles app,dmg` 生成对应平台安装／应用包。安装器需要对应平台工具链。

`-SkipBuild` 只整理已经构建好的相同 profile／target 二进制与资源。Document 正式包默认包含 OCR；`-WithoutOcr` 仅生成明确不提供文字识别的轻量验收包，不能用它代替完整单证版验收。网页/Docker Full 继续包含 OCR。

便携/Web 组包写入 `exportdoc-package-files.json`，登记受控程序路径和 SHA-256。同目录重打包只移除已退出新清单且归属/摘要仍匹配的程序资源，清除旧 OCR 与 Web hash 文件；`App_Data`、DataRoot 用户模板、备份及私有文件不进入此清单。已修改的程序资源或模板、损坏清单、链接、嵌套 Git 和 Windows 路径别名会在复制前报错，先另存冲突内容或选择新包目录。旧无清单包只识别明确的只读运行资源树；首次遇到不同内容的模板不猜测覆盖。

直接 Cargo 命令、本地打包、实库/界面回归与 GitHub 工作流默认共用仓库 `target/`；不再分别写入 `.codex-runtime/cargo-target-native` 和 `artifacts/cargo-target-native`。`CARGO_TARGET_DIR` 可显式覆盖，相对路径按仓库根解析；公共入口同时把下载缓存和临时目录放入 `.codex-runtime`。手工运行 Cargo 时也应设置仓库内的 `CARGO_HOME`，以复用脚本的下载缓存。`-SkipBuild` 会先检查主程序及所需 OCR 程序，缺失时在创建/改动输出包之前报错；所有文件复制前统一验证来源和目标边界。

Windows 批量入口默认 `-Edition All`，仅生成 Document、Sales，共用一次 React 构建及 Cargo 输出根。默认目录为 `artifacts/native-desktop/ExportDocManager.Tauri.Document` 和 `.Sales`；显式 `-Edition Full` 只生成 `.Full` 测试目录。`-OutputDir` 指定这些子目录的父目录，单版 `build-native.ps1 -OutputRoot <目录>` 直接指定包目录。每版保留自己的 `App_Data`，不能覆盖另一版本；Administration 参数仍拒绝。

### 单独生成 Full + SQLite 测试版

在仓库根执行：

```powershell
.\scripts\build-windows-desktop-run.cmd -Edition Full -NoPause
.\scripts\run-native.cmd -Edition Full -NoPause
```

输出 `artifacts/native-desktop/ExportDocManager.Tauri.Full/ExportDocManager.exe`；可直接双击运行，无需安装 PostgreSQL。数据库位于该目录的 `App_Data/Database/exportdoc-native.db`，重新构建保留已有数据。此版采用独立 `com.exportdocmanager.desktop.fulltest` 身份，显示完整单证、销售、行政人事、公告通知及账号权限页面，用于本地界面和流程测试。SQLite 的六类日常 OA 保留选择人员、登记批准结果的单机语义；人员变动审批在 SQLite 也禁止申请人和被变动人员自批。团队本人申请、权限和并发仍须 PostgreSQL 实测。

默认 Release 并包含 OCR 资源；仅检查布局时可加 `-Configuration Debug -WithoutOcr` 生成不含 OCR 的轻量测试包。Full 只通过显式参数生成，不进入 All、正式安装器、GitHub 桌面发布矩阵或自动更新频道。跨平台本地构建可使用 `build-native.ps1 -Edition Full`，每个平台仍需单独验收。

Document 使用单证概览、Sales 使用销售概览作为固定首页；Full 优先使用单证概览，没有权限时进入已授权的岗位工作页，普通员工默认进入申请与审批。根地址只负责跳转，不作为单证页面检查权限；无任何可用页面时显示权限说明。

| 版本参数 | 功能范围 | 随包资源 |
| --- | --- | --- |
| Document（单机主力） | 单证与申报、报表和相关工具，优先完成制单与交付 | OCR、单证/Excel 模板、PDFium、字体 |
| Sales（单机） | 个人外贸工作台：客户、联系人、跟进、商机、报价、供应商、邮件及相关工具 | PDFium、字体；不附 OCR 和单证模板 |
| Full（仅多用户 Web/Docker） | 完整单证、销售、行政、人事、OA 申请审批及账号/组织/权限管理 | OCR、单证/Excel 模板、PDFium、字体、PostgreSQL 客户端 |
| Full（本地 SQLite 测试） | 完整界面、公告通知及 OA 登记流程验证 | 默认 OCR、单证/Excel 模板、PDFium、字体；不含 PostgreSQL 服务端 |

后端按统一版本资源目录限制权限，管理员也不能越过产品边界；Document/Sales 不开放 OA 或账号/组织/权限，Full 多用户及本地测试版开放完整模块并按岗位授权。共用 Rust 库保留共享实现。正式安装器使用 `build-windows-installers.ps1 -Edition <Document或Sales>`，签名更新独立验收。

`-SkipMainBuild`（单版入口为 `-SkipBuild`）只复用 `target/<profile>/editions/<版本>` 下已构建的该版 EXE，校验版本及 SHA-256 构建记录。不会将最后一次 Cargo 输出复制成另一版；缺少记录或校验失败须正常重建。Document 默认提供 OCR，只有显式 `-WithoutOcr` 才生成轻量检查包。

### 构建时指定程序版本

桌面便携包、Windows 安装器、Web 服务包和本地 Docker 源码构建均支持 `-Version`，例如：

```powershell
.\scripts\build-windows-desktop-run.cmd -Edition Document -Version 1.2.3 -NoPause
.\scripts\build-windows-installers.cmd -Edition Sales -Version 1.2.3 -NoPause
pwsh -NoProfile -File scripts/package-native-web-server.ps1 -Version 1.2.3 -NoPause
.\scripts\run-native-docker.cmd -Version 1.2.3 -NoPause
```

单版/跨平台 `build-native.ps1` 同样支持此参数。接受 `1.2.3`、`v1.2.3`、`1.2.3-beta.1`；未指定时使用 `version.json` 的仓库默认值，不自动按日期或构建次数升级发布号。指定参数会在构建前同步当前工作树中的 Rust/npm/Tauri 清单及本地包锁，版本修改可随源码审阅提交；不会升级第三方依赖。`-PreflightOnly` 只检查工具，不修改版本。Docker 仅在实际源码构建时接受该参数，此操作需要本机 Node；`-SkipBuild`、停止、准备配置和恢复不能给已有镜像改版本。

`node scripts/sync-version.mjs --check` 检查一致性且不写文件。桌面和 Web 的 `-SkipBuild` 还核对已构建二进制的版本及 SHA-256；Web 记录位于相同 profile/target 的 `server-build.json`。版本不符或记录缺失时必须重新构建，不能把旧 EXE 标记成新版本。

“关于与支持”的版本来自正在运行的 Rust 服务，构建标识形如 `1.2.3+git.012345abcdef`。提交号在编译时获取，只表示来源提交；本地未提交修改不会产生新的提交号。源码归档可通过 `EXPORTDOCMANAGER_BUILD_REVISION` 提供提交号，Docker 对应同名 build-arg，正式容器工作流自动传入。没有来源信息时明确显示未提供；运行程序不调用 Git。发布号仍用于 Tauri 更新比较，构建标识用于核对来源，两者分别维护。

## 桌面启动检查与资源

桌面业务库统一位于 `App_Data/Database/exportdoc-native.db`，实例锁位于 `App_Data/Locks`。旧位置 `App_Data/exportdoc-native.db` 中受支持的数据库会通过 SQLite 快照自动搬入 Database，原文件保留在 `Backups/DatabaseLayout`；新旧两处同时有库则停止并提示核对。不要手工只移动正在使用的主文件：它可能还有未合并的 WAL。WebView 内部数据库保留在 `WebView` profile 中。

便携包重复构建会保留已有 `App_Data`。Rust 数据库从版本 5 起逐版升级，当前为 8；只有数据库不存在才新建，已有库失败不覆盖。版本 4 及以前不做兼容：确认无需使用后，先退出程序并将原 `App_Data` 整体另存，再启动初始化。不要直接修改版本标记。初始化错误及启动期 panic 会显示错误和日志位置，日志为 `App_Data/Logs/tauri-bootstrap-error.log` 或 `tauri-errors.log`；运行目录不可写时提示备用日志位置或日志写入失败。

Windows 在创建 Tauri 窗口前检查系统最低版本和 WebView2。x64 便携包携带原版固定清单验证的微软离线安装器；缺少 WebView2 时显示安装／退出选择，保留取消、繁忙、超时和需重启处理。构建时核对微软签名、版本、大小和 SHA-256。Windows GNU 构建同时携带 `WebView2Loader.dll`。

便携版在系统检查、随包资源检查、后端启动和主 WebView 窗口创建成功后，后台自动移除本包 `WebView2Runtime` 内的离线安装器、校验清单及说明，最后删除空目录；当前可回收约 203 MiB。安装取消、失败、要求重启或启动检查失败时保留安装器。清理失败写入受管日志，下次成功启动重试，不阻断使用；未知文件、子目录和链接不自动删除。系统 WebView2、`WebView2Loader.dll` 和 `App_Data/WebView` 保留。开发仓库的安装器来源与原始发布压缩包保持完整；将已清理的目录拷到另一台缺少 WebView2 的电脑时，需重新解压原始完整包或安装微软 WebView2。

程序随后读取本包资源清单，核对中文字体、PDFium 及已声明的 OCR 工具、ONNX 和模型。Windows x64/ARM64 OCR 分别携带四个和三个对应架构的 app-local CRT DLL，不安装全局 .NET 或整个 VC 运行库。Linux 使用 WebKitGTK 4.1，macOS 使用系统 WKWebView；其它平台必须在对应 runner／设备验收。OCR 选型、网页上传和各目标边界见 [OCR 评估](../docs/OCR跨平台与RapidOCR评估.md)。

`eng/native-runtime-packages.json` 固定已校验来源及签名的原生资源归档版本和 SHA-512。NuGet 在此只是原生 DLL／so／dylib 的下载载体；不复制托管程序集，也不执行 dotnet restore。不要把 NuGet lock 的“内容哈希”直接当成签名后整个归档的哈希。PowerShell 打包器和容器资源准备都读取这份清单。

共享字体、Excel 模板、原版报表模板、PDFium、notices 和 OCR 资源通过 `lib/native-package-resources.ps1` 组装，桌面与网页服务不维护重复清单。临时文件和下载缓存写仓库 `.codex-runtime/`。AppRoot 与 DataRoot 显式传入；桌面 WebView profile 写 DataRoot/WebView。

字体准备复用 `lib/verified-download.mjs`：流式下载并核对 SHA-256，单文件最多 32 MiB、总时限 120 秒，支持取消和并行独立临时文件；坏摘要、超限、超时或取消均保留原字体并清理本次临时文件。`node scripts/test_verified_download.mjs` 验证这些边界，随脚本套件执行。

六份默认报表统一为 `.dtpl`。开发维护源为 `lib/default-report-designs.mjs`，通过 `node scripts/generate_default_dtpl_templates.mjs` 重建容器，再运行 `node scripts/test_report_designer_v3_contract.mjs` 校验设计器读写与尺寸保留。生成源和六份资产同时提交；旧 HTML 仅可作只读版式参考，不进入目录或发布包。

## 网页与 Docker

普通用户从[Docker 部署指南](../docs/Docker容器部署与启动指南.md)或[独立网页版指南](../docs/网页版部署与启动指南.md)开始；备份、换机和故障恢复按[服务器恢复指南](../docs/服务器备份迁移与恢复指南.md)执行。这里集中维护脚本参数及开发验证，不重复完整操作步骤。

新版已发布镜像可直接执行 `docker run --rm <image> --print-compose > compose.yml`，再 `docker compose up -d --wait`。该入口自动准备 `config`／`data` 并完成初始化，默认发布局域网端口；不需要仓库源码或宿主脚本。网页发布包新增 `--setup-web`，后续自动读取 RuntimeConfig 和相邻 Web 目录。下面的 PowerShell Docker 参数属于已有源码部署及开发流程，不能混用新旧目录接管旧数据。

`run-native-docker.ps1 -PrepareOnly -NoPause` 只生成私有配置；普通运行构建并启动，`-Stop -NoPause` 停止并保留数据库。默认绑定 `127.0.0.1:5188`；局域网地址须显式设置。凭据保存在忽略的 `deploy/rust-native/runtime/`，不进入 Git 或镜像。始终使用同一 RuntimeRoot 和 Compose 项目维护已有部署，不能换一套随机凭据接管旧数据卷。

常驻服务为两个容器：`application` 同源提供 React 与 Rust API，`postgres` 独立运行 PostgreSQL 18。浏览器 → HTTP/HTTPS → API → 内网 `postgres:5432`；数据库不映射宿主端口。API 使用 `database` 与 `web` 网络，PostgreSQL 只使用 `internal: true` 的 `database` 网络。TLS 代理可由服务器部署环境提供，不是必须增加的应用容器。非容器网页包使用相同 Rust 后端连接独立 PostgreSQL 服务，数据库不必容器化。

### 更新程序与数据库

源码脚本的启动／更新经过：准备镜像 → 停 API → 等数据库就绪 → 独立维护容器初始化/逐版升级 → 启 API 并检查就绪。构建及缺失镜像取得在停 API 前完成，失败不打断原应用；已有/离线镜像按 missing 策略复用。原 `compose.yml` 的维护服务仍由脚本显式运行，`-SkipBuild` 复用已有镜像。新版 `compose.image.yml` 则声明初始化完成依赖，支持直接 Compose 启动；升级时先 pull，再 down（不删卷）及 up。两种入口的恢复失败均保留 API 停止状态。

应用 schema 升级与 PostgreSQL 软件主版本升级是两件事：当前 Rust schema 为 8，从 5 开始按顺序事务升级；模板权限调整不改变 schema 或备份格式。PostgreSQL 18 的补丁镜像升级保留现有卷，但不能将镜像改成 19 后直接复用旧卷；主版本升级须另行安排 PostgreSQL 原生迁移、兼容验证与停机恢复演练。升级前在界面导出备份/完整迁移包并保存在本机故障之外的位置。

首次浏览器管理员用 `admin`、自定 8—128 字符密码及该目录的 `bootstrap-token.txt` 初始化。日常服务只持有 PostgreSQL 18 业务连接，维护连接只供初始化／升级／恢复使用。桌面 SQLite 空库仍为 admin 空密码；当前数据库版本 8，支持从 Rust 版本 5 升级，不兼容 C# v19 或版本 4 及更早试验库。

## 远端入口

### 连接池、运行指标与容量验证

PostgreSQL 默认 4 个业务连接，另有一个独立实例锁连接；读取并行，事务写入保持统一协调、同连接事务和当前权限复核。SQLite 使用单连接。`EXPORTDOCMANAGER_POSTGRES_POOL_SIZE` 接受 1–16，`EXPORTDOCMANAGER_POSTGRES_POOL_WAIT_MS` 接受 1–30000 毫秒，默认 5000；Docker 分别通过 `NATIVE_POSTGRES_POOL_SIZE` 和 `NATIVE_POSTGRES_POOL_WAIT_MS` 传入。参数无效时拒绝启动。连接等待超时返回繁忙，取消释放等待；观察到连接/实例锁故障后停止继续办理，修复后重启，不自动重放写入。

HTTP 保持 16 个执行名额，另有 16 个公平等待名额，最多等待 5 秒；队列满或等待超时返回 429。管理员使用正常登录取得的 Bearer 令牌访问 `/api/diagnostics/metrics`，可读取连接池、写入协调、HTTP 排队和处理耗时、失败/拒绝及任务计数。服务端分位数是固定直方图的上界，负载脚本另外统计客户端实测 P95/P99；进程计数在重启后重置，任务计数来自当前保留记录。

已声明的 API 响应带 `X-Request-Id`。`DataRoot/Logs/requests.jsonl` 记录对应操作名、时间、状态与耗时，约 8 MiB 轮转到 `requests.previous.jsonl`，保留两个文件；日志使用 256 条有界队列和批量写入，不记录令牌、密码、SQL 或请求正文。丢弃和写入失败可在指标中观察；这是运维日志，数据库中的事务审计继续独立保存。

`test-native-postgres.ps1 -PostgresBin <PostgreSQL-18-bin>` 默认执行存储、团队业务及恢复实库契约，可用 `-Scope Storage` 或 `-Scope Engine` 定向验证。`-Scope Capacity` 是独立的 20 会话负载：使用新建的隔离数据库比较 1/4 连接，包含查询、发票保存、人员关联后的报销提交/审批及 PDF 导出。运行前需准备前端 npm 依赖和随包字体；脚本构建实际 Debug HTTP 服务，原数据库和便携数据不参与测试。输出吞吐、P95/P99、HTTP 状态、内存及指标快照到 `.codex-runtime/native-capacity`，不把 Debug 场景当成生产 SLA。

存储故障演练分别终止隔离库的实例锁连接和业务连接，验证已提交数据保留、未提交写入回滚及重建连接池耗时；测试维护账号的 `pg_signal_backend` 仅在临时测试集群授予，生产部署不授予。升级失败和损坏备份保持失败与回滚边界。连接重建的恢复点与从备份恢复的时间点分别记录，生产备份频率和设备故障演练由部署环境制定。

### 文件任务结果容量

`test-native-postgres.ps1` 的 All／Engine 另外创建独立发布测试库，验证报表来源快照、最终账号／权限复核，以及文件发布后的 SQL 失败补偿和重启恢复。普通 React 回归 `npm --prefix apps/export-doc-web run test:invoice-editing` 包含 200→1000→200 行的滚动／粘贴／撤销和 5000×38 大选区复制、填充、清空。

有契约正文的认证请求先核验真实会话再读取；备份上传单次读取或写入空闲超过 60 秒结束本次上传。HTTP 与桌面宿主共用取消和关闭登记，HTTP 排空最多等待 35 秒；数据库、调度器和任务清理仍返回真实结果。

任务记录与生成文件分别保留。普通数据库文件结果默认全局 512 MiB、每用户 128 MiB；先保留较新的结果，超限回收旧 BLOB，保留成功状态和历史并提示重新生成。既有 30 天／全局 2000 条／每用户 200 条的任务历史策略继续执行。受管完整备份、服务器迁移包及业务附件不由此策略删除。

桌面任务保存实际所选文件／目录位置，副本回收及任务删除保留外部用户文件。待完成的文件发布清单位于注入缓存根的 TaskPublications，标准为 DataRoot/Cache/Native/TaskPublications；默认清理器保留该恢复目录，提交不明或未知变更须先由原程序重启核对。

重试工作簿在成功终态事务内删除，启动时也清理旧成功任务遗留的输入。失败/取消任务输入独立使用相同的全局与用户容量上限，活动输入不回收；回收后保留历史但不再提供重试。单据组合与单据邮件的重试复用原用例，邮件继续按原幂等键及投递状态拒绝自动重复发送。

`EXPORTDOCMANAGER_JOB_OUTPUT_MIB` 和 `EXPORTDOCMANAGER_JOB_USER_OUTPUT_MIB` 分别设置两项容量（64–1048576 MiB，个人不能大于全局），分别约束输出与待重试输入，无效配置拒绝启动；Docker 对应 `NATIVE_JOB_OUTPUT_MIB`／`NATIVE_JOB_USER_OUTPUT_MIB`。数据库任务下载在加载内容前共用 128 MiB 内存预算，繁忙返回 429，传输结束或失败归还。管理员指标的 `jobs.retainedInputBytes`、`jobs.retainedOutputBytes` 和 `jobs.downloadMemory` 用于观察实际容量及预留。

### 备份、迁移与恢复

- “日常灾备”默认关闭，可手工执行或按业务时区每天／每周生成完整加密包。“异机副本”选择 WebDAV／S3 并测试写入、回读和删除。新包及副本成功后执行受管保留策略，副本失败保留本地包和旧备份。内建为完整快照，增量和 WAL/PITR 由数据库运维另行配置。
- 桌面 `.edmrecovery` 灾备包包含 SQLite 快照、Security 主密钥和 DataRoot/Templates；从本机明确选择的文件校验并暂存后，退出重开即可恢复。启动先取得实例锁，校验暂存内容，保存原文件并记录替换日志；失败停止启动，保留恢复目录。
- PostgreSQL API 沿用“物理备份”名称，实际为 `pg_dump` custom-format `.dump`。完整包另含主密钥和用户模板。新包使用 ZIP64／v2 认证分块，上传下载及摘要使用固定缓冲和受管磁盘，不设置固定总容量额度；旧 v1 包仍按旧格式边界读取。磁盘余量、存储协议及 24 小时任务时限仍适用。
- 服务器在界面完成密码/确认及暂存后，停止正常 API，使用同一个 AppRoot/DataRoot 执行 `ExportDocManager.Server --app-root <AppRoot> --data-root <DataRoot> --restore-pending`。维护进程需提供 `EXPORTDOCMANAGER_POSTGRES_CONNECTION`、独立 `EXPORTDOCMANAGER_POSTGRES_MAINTENANCE_CONNECTION` 和 NOLOGIN 所有者 `EXPORTDOCMANAGER_POSTGRES_OWNER`；两种连接须指向同一数据库，均可使用对应 `_FILE`。命令完成即退出，再以业务连接启动 API。
- 新镜像部署依次执行 `docker compose stop application`、`docker compose run --rm --no-deps restore`、`docker compose up -d --no-deps --wait application`；每条成功后再执行下一条。旧源码入口继续使用 `scripts/run-native-docker.ps1 -RestorePending -NoPause` 及原 RuntimeRoot。普通 API 不持维护密钥，失败保留停止状态、待恢复标记及安全副本。
- 服务端现有完整包可在网页按编号暂存恢复；外部大包可用 `--import-migration <绝对包路径> --package-password-file <私有密码文件>`，在 API 停止后通过独立维护锁导入并恢复。命令保持原 AppRoot／DataRoot 和维护连接。
- 恢复先生成安全 dump，再以 `pg_restore --single-transaction --no-owner --no-privileges --role <NOLOGIN-owner>` 执行，存储层验证 schema 并重新授予业务账号必要表/序列权限。失败保留标记和安全备份；不得删除标记冒充恢复完成。
- 主密钥、暂存目录与恢复前副本使用共同的 Windows 私有 ACL／Unix 0700 目录边界。环境变量提供主密钥的部署明确拒绝独立密钥包操作，须由管理员安排密钥迁移。
- 网页包随附 `Tools/PostgreSQL` 客户端和许可；版本、来源及 Windows SHA-256 在 `eng/native-runtime-packages.json`。Linux 客户端采用官方 bookworm 资源以避免在 Ubuntu 24.04 上引入更高 glibc 要求；容器使用 trixie 资源。macOS 构建机先安装 PostgreSQL 18 Homebrew formula，版本须与中央清单一致。

`pg_dump`/`pg_restore` 从应用/维护容器通过数据库连接工作，不需要把数据库目录挂给 API，也不需要浏览器执行 `docker exec`。`postgres_data` 是数据库原始数据卷，`app_data` 含 Security、用户文件模板、任务输出和备份；换机不能只迁移数据库卷而漏掉应用数据和私有配置。正常停止不删除卷，禁止用 `down -v` 作为更新或恢复步骤。完整迁移包覆盖业务库、主密钥和用户模板，任务临时输出不属于完整业务迁移承诺；不能直接复制正在运行的 PostgreSQL 原始数据目录作为可靠备份。

### GitHub 构建产物

| 工作流 | 用途 |
| --- | --- |
| `windows-desktop-package.yml` | Windows 桌面；选择 x64／ARM64／all，版本号及 GitHub Release 开关 |
| `linux-desktop-package.yml` | Linux 桌面；选择 x64／ARM64／all，版本号及 GitHub Release 开关 |
| `macos-desktop-package.yml` | macOS ARM64 桌面；版本号及 GitHub Release 开关 |
| `rust-native-web-server-release.yml` | React + Rust PostgreSQL 服务包；选择系统、架构、版本号及 GitHub Release 开关 |
| `rust-native-container-release.yml` | Docker 版本号与 x64／ARM64／all；验证同一镜像后按 publish 选择发布 GHCR，publish_latest 单独控制 |
| `release-script-validation.yml` | 自动检查发布参数、版本同步、发布冲突、脚本语法及 workflow 语法 |
| `rust-native-validation.yml` | Rust 测试、生成契约、依赖边界与跨平台构建／容器验收 |
| `dependency-governance.yml` | npm／Cargo／原生资源的许可与 SBOM；不调用 .NET |
| `browser-compatibility.yml` | 仅手工 Firefox／WebKit 验收 |

Tauri updater 默认没有端点或公钥，签名发布须显式配置受信公钥和私钥，私钥不写仓库。便携包不执行安装器更新。不执行 Windows Authenticode、Developer ID 或 Apple 公证。未实跑的 CI／系统／架构不写成已通过。

桌面和 Web 共用 `native-package-reusable.yml`，该内部工作流不提供手工运行入口。`version` 接受 `0.1.2`、`v0.1.2`、`0.1.2-beta.1`，同时进入 Rust/npm/Tauri、包内标记和归档名称。只改 CI checkout，不自动提交版本文件。已有不同源码的 GitHub Release 标签或不同内容的同名附件拒绝覆盖。完整参数及下载步骤见[工作流手册](../docs/GitHub%20Actions工作流用途与运行手册.md)。

本地优先在构建入口传入 `-Version`；也可提前运行 `node scripts/sync-version.mjs 1.2.3`，随后使用现有入口。同步范围包含 Rust 主工作区、独立 OCR/Excel 工具及各自锁文件；版本文件只保存产品版本，不再维护已退役的 .NET 程序集版本字段。

运行已发布镜像：`./scripts/run-native-docker.ps1 -Image ghcr.io/<owner>/exportdoc-rust-native:0.1.2 -SkipBuild -NoPause`。后续启动、停止和恢复均使用同一 `-Image` 参数。`-SkipBuild` 使用已有/拉取的镜像；默认本地命令仍从源码构建。

## 验证和证据

`npm --prefix apps/export-doc-web run test:invoice-editing` 读取与 Rust 共用的金额夹具，验证精确运算、TSV 和保存基线，再运行真实 React hook 的延迟、切换、卸载、刷新失败、重复提交与精确商品查询。`test:document-editor-ui` 另检查实际发票页面的新建跳转和二次保存。普通 Rust/Chromium CI 已加入这些回归及 Clippy；Firefox/WebKit 继续保留手动入口。

实际 PDF 页图回归可设置 `EXPORTDOCMANAGER_REVIEW_APP_ROOT` 为本批组装的应用包根，直接复用包内受管 PDFium、字体和模板；`office_review` 的第二个可选参数对应此 AppRoot，第一个参数仍是独立测试 DataRoot，React 静态文件继续来自仓库构建。缺少原生库时专项明确失败。

`test:concurrency-feedback` 另运行客户/供应商与联系人保存的真实 React 回归，覆盖保存期间继续输入、新建编号与并发版本、重复提交、切换/卸载、迟到响应和刷新失败；共用保存 hook 不通过清空草稿规避冲突。导入预检在同一业务库中保留 30 分钟，单次边界保持；待提交内容另限每账号 200 份/128 MiB、全局 2000 份/256 MiB，满额返回 429。提交成功即事务化释放正文，重复提交仍明确拒绝。

开发时通过 `generate-api-client.ps1` 调用 `cargo run --locked -p export-doc-contracts --example generate_clients`，由 Rust 从同一 OpenAPI 文档生成 Rust 与 React 客户端；`-Check` 只校验生成文件。`crates/export-doc-contracts/src/reference_openapi.json` 是保留的官方 .NET 基线，新增能力只修改 Rust 组合器，不能覆盖生成文件或另建前端 URL/schema。`-OpenApiPath` 可指定已审阅的输入，并同时更新两端；不需要 .NET SDK。Rust 生成器回归随 workspace tests 执行，校验完整生成结果、端点策略、上传下载、请求头、默认值和类型投影；请求运行行为由现有 React/API 测试校验。

OA 真实界面回归先执行 `cargo build --locked -p export-doc-server --example office_review`，再执行 `npm --prefix apps/export-doc-web run test:oa-ui`。回环测试宿主使用独立 DataRoot，覆盖六类申请、附件、办理记录、窄屏，以及管理员规则设置、多级审批、有效代理、手动催办和财务接收；不代替 PostgreSQL 18 或 Tauri 验收。公告通知与审批界面共用测试宿主/浏览器生命周期，正式业务仍使用同一 Rust 服务。

同一宿主构建后，`node scripts/test_personnel_accounts_ui.mjs` 验证人员与账号双向入口、启停和改名同步、同名工号选择、窄屏、继承权限时模块开闭，以及人事直接办理、管理员启用离职审批、人事提交和批准后停用账号。Rust 人员契约在 SQLite 与 `test-native-postgres.ps1 -Scope All` 的真实 PostgreSQL 18 中共用审批策略、版本回滚、交接、身份隔离和权限测试。

发票印章与模板预览回归复用该隔离宿主：先构建 React 和 `office_review`，再运行 `node scripts/test_invoice_report_ui.mjs`，验证无出口商关联的上传按钮、图片保存回读及样例/真实单据的原生排版。

实际 PDF 页图专项使用 `node scripts/test_invoice_report_ui.mjs --with-native-pdf`，验证多页翻页、修订变化、越界及未登录拒绝和真实 React 翻页。优先将 `EXPORTDOCMANAGER_REVIEW_APP_ROOT` 指向 `build-native.ps1` 组装的当前平台/架构测试包，直接读取包内 `Resources/Pdf`（Windows 为 `pdfium.dll`，Linux 为 `libpdfium.so`，macOS 为 `libpdfium.dylib`）。未设置时使用仓库 AppRoot，其资源也必须来自本项目锁定摘要的原生包；不要求复制或清理包内原生库。缺库时该专项明确失败；普通 SVG/krilla 回归不要求 PDFium。

付款模板和真实 PDF 使用 `npm --prefix apps/export-doc-web run test:payment-printing-ui`，发票使用 `test:invoice-report-ui`，多模板 ZIP 和多附件使用 `test:batch-file-ui`；均要求先完成 React build 和 `cargo build --locked -p export-doc-server --example office_review`。批量回归包含六份模板的空白组件重建、原生样例/长明细输出对照、同一发票选择超过三种模板、逐模板权限与附件字节回读。付款回归覆盖个人模板、权限、预览失效、保存/取消和下载，生成的 PDF 仍须检查真实票面。

文档整理后运行 `node scripts/verify-documentation-links.mjs`，检查 docs 及各说明入口的本地文件链接；当前入口不应引用已退役的文档或工作流。

按用户要求先集中完成一批页面、后端和操作，再统一联调与最终门禁。开发中只做必要编译和针对失败的回归。

- Rust：`cargo fmt --all --check`、`cargo test --locked --workspace`、`cargo check --locked --workspace --all-features`。
- OCR 独立工程：`cargo fmt --manifest-path apps/exportdoc-ocr-rs/Cargo.toml --check`、`cargo test --locked --manifest-path apps/exportdoc-ocr-rs/Cargo.toml`；组装资源后运行 `node scripts/test_native_ocr.mjs <AppRoot>`，验证真实模型、倾斜/旋转/手机方向和输入边界。根 workspace 不包含此工程；网页切图、上传、取消及内存回收使用 `test:smart-ocr-ui`。
- PDFium 升级：`node scripts/test_native_pdf.mjs <包内EXE> <包内PDFium库> <匿名生成PDF>`，真实调用包内 worker 验证文字、预览、OCR 页图、合并后重新打开，以及损坏输入、缺库、页码与流边界。输出仅写 `.codex-runtime/native-pdf-validation`；使用当批报表回归生成的匿名 PDF，不使用业务文件。
- 实库：`test-native-postgres.ps1 -PostgresBin <PostgreSQL-18-bin>` 创建并停止隔离集群；忽略的实库测试不计通过。
- 团队备份界面：先完成 React build，再运行同一实库脚本的 `-Scope Maintenance`。使用真实 Rust CLI、文件连接配置、浏览器首次登录、dump 下载/上传、完整迁移、错误/既有密码、停机恢复、主密钥/模板保留和重试幂等；不接受生产端点。该专项不加入普通浏览器重型门禁。
- Docker：`test_native_docker_lifecycle.ps1` 在无 daemon 环境验证停机顺序与失败中止；真实两架构 CI 另核对内部网络、端口、凭据/卷隔离、运行中重复部署、custom-format dump 恢复及重启持久化。脚本和 Compose 配置通过不等于容器实跑通过。
- React：项目 `build`、API／登录／权限／草稿／无障碍及相应页面回归；真正的 Tauri 窗口和输出仍需实跑。
- 异步生命周期：`npm --prefix apps/export-doc-web run test:abortable-operations-ui` 验证资料切换、卸载后的迟到成功/失败、保存后刷新期间离开页面、商机/供货产品连续查询，以及附件容量刷新和部分失败重试的版本选择；`test:business-features-ui` 另验证真实资料页面不会显示旧预览及旧错误。`test:oa-ui` 的人员详情实跑满额上传、删除后释放容量及继续上传，保留真实 HTTP 版本校验。
- 供应商评价与下载：异步回归另覆盖连续提交、切换供应商/离开页面及保存成功后刷新失败的记录保留；`test:background-job-polling` 验证取消后的迟到下载凭据不会触发下载，失败的临时链接会清理。
- 模板／压缩包导入：domain 的 `template_container` 与 engine 的 `archive`、`report_template_files` 回归覆盖伪造展开长度、容量、CRC、逐块取消、冲突路径及正常包回读；`test:api-client-contracts` 核对原始 File 的二进制上传。Office 附件不缓存无需解析的图片条目，模板包上传不额外落盘再读回。
- 装柜三维：`test:container-packing-model` 检查同色材质复用、层线几何与资源只释放一次；`node scripts/test_container_packing_scene_ui.mjs` 使用真实 WebGL 检查俯视/柜门方向、切换框线和父级重绘时保留视角、右键、暂停及退出清理。该浏览器回归不替代各 OS 的 GPU 驱动验收。
- 依赖：`generate-dependency-governance.mjs artifacts/dependency-governance --release --verify-repository`，要求 `unresolved=0 / disallowed=0`。
- 平台：`verify-native-desktop.mjs` 验证 Tauri／SQLite，排除 Slint／egui／PostgreSQL 桌面依赖；`assert-tauri-command-permissions.ps1` 校验 command 与能力白名单。
- Windows 桌面：`node scripts/test_native_desktop_editions.mjs [包父目录] Full --without-ocr` 验证独立 Full 测试包的 About、设计器、托单与业务资料操作。可设置 `EXPORTDOCMANAGER_DESKTOP_VALIDATION_OUTPUT_ROOT` 指定隔离验收输出父目录；默认是仓库 `artifacts/edition-validation`，不会使用程序包的 App_Data。
- Windows 退出：`node scripts/test_native_desktop_shutdown.mjs [便携包目录] [EXE路径]` 使用隔离 DataRoot 验证真实 React/WebView2、重复退出、宿主异常终止、独立窗口不受影响及 HTTP 端口释放。调试端口仅注入测试子进程，不修改正式配置；其它 OS 需独立验收。
- 脚本：`verify-script-suite.ps1`、`verify-github-workflow-actions.mjs`、`test_tauri_updater_release_contract.mjs`、`github/verify-public-source.ps1`、`git diff --check`。
- 人员与审批职责：`node scripts/test_approval_permissions_ui.mjs` 在隔离 Rust `office_review` 宿主上验证常用/自定义职位、从普通员工建立公司审批方案、跨部门行政和人事审批，以及私密档案、财务办理、模块限制和会话撤销边界。
- 任职与流程闭环：`node scripts/test_organization_workflow_ui.mjs` 复用上述隔离宿主，验证真实兼职/矩阵负责人选择、公司 CEO/职位目录、会签和金额条件设置、未完成审批移交、仅分派可见及窄屏无障碍；先生成客户端、构建 React 和 `cargo build --locked -p export-doc-server --example office_review`。
- PostgreSQL 权限/组织定向回归：`pwsh -NoProfile -File scripts/test-native-postgres.ps1 -PostgresBin <PostgreSQL18/bin> -Scope Workflows` 复用隔离集群与独立账号，串行运行存储合同和完整团队业务合同；`All` 仍额外运行恢复/大容量备份测试。

本分支已删除旧 C# 源码/测试/工程与 `deploy/container`；`verify-dependency-policy.mjs` 拒绝重新引入托管工程或将其依赖列入 Rust notices。原生 NuGet 归档仍按中央资源清单校验。手动跨浏览器验收使用锁定的 npm Playwright 和隔离 Rust `office_review` 宿主，不读取旧 .NET DLL；Firefox/WebKit 仍只在手动工作流执行。

浏览器仅用于开发验收，正式 Rust PDF 和 Tauri 系统 WebView 不依赖根 `Browsers` 目录。旧副本清理后，前端回归统一从既有 `artifacts/playwright-browsers` 缓存定位 Chromium，不再搜索旧 .NET 输出或调用已删除的浏览器准备脚本。需要重建测试缓存时，在仓库根设置 `$env:PLAYWRIGHT_BROWSERS_PATH = Join-Path $PWD 'artifacts/playwright-browsers'`，再执行 `node apps/export-doc-web/node_modules/playwright/cli.js install chromium`；普通界面回归可加 `--only-shell` 只下载无界面浏览器，PDF 查看器回归需要完整 Chromium。这不影响用户正常运行程序，默认清理继续保留该可复用缓存。

报表原版 React 设计器可复用，但 Rust 渲染仍有明确未完成项；当前事实见 `docs/Rust原生功能迁移核对表.md`。旧 Slint 的 `--validation --ui-smoke` 入口已退役。

模板预览新增“实际 PDF 页图”，复用已有预览 API 的可选 `previewFormat=PdfPage`、零起始 `pageIndex` 与翻页修订号；未提供参数仍使用矢量预览。`test_invoice_report_ui.mjs` 同时验证多页 PDFium 真正渲染、翻页、修订变化 409、非法页码 400、匿名拒绝与真实 React 页图。运行前准备受管 PDFium 及本批 `office_review`，完整桌面包另行验收。

## 工作区清理

### 目录职责

| 目录 | 内容 | 清理规则 |
| --- | --- | --- |
| `target/` | Cargo 编译缓存、依赖构建、测试程序、Debug/Release 二进制及按版本缓存的 EXE；直接 Cargo、本地脚本和 CI 共用 | 可重建；清理后须重新编译，`-SkipBuild` 需先正常构建 |
| `artifacts/` | 已组装的桌面/网页程序包、发布归档、验证报告和截图 | 普通验证产物可重建；程序包及包内 `App_Data` 不能整体当作缓存删除 |
| `.codex-runtime/` | 仓库内 Cargo/npm/原生资源下载缓存、受控工具、临时工作区及本地日志 | 默认保留；`-IncludeCodexRuntimeWorkspaces` 清理不含受保护内容的一次性目录及根部开发/测试 `.log` 文件 |
| `apps/export-doc-web/dist/` | Vite 生成的前端静态资源，供桌面和网页打包 | 可重建，源码仍在 React 项目中 |

`target` 中的 EXE 是编译结果，`artifacts/native-desktop` 中的 EXE 是附带字体、OCR、运行配置等资源的可运行包，两者存在必要的复制，并非两套源码或两套默认编译缓存。Tauri 原始安装器可能先生成在 `target/<target>/<profile>/bundle`，清理器默认保留含此目录的构建树。不要为整理目录移动已有程序包及其数据根。

新增 Rust 构建继续使用 `target/`，不再新增 `artifacts/cargo-target-*` 或 `.codex-runtime/cargo-target-*`；不同 profile、目标架构和产品版本由同一构建根的子目录区分。现有 `artifacts/playwright-browsers`、`artifacts/tool-downloads` 是验证工具缓存的明确例外，保留路径以复用下载，不属于待删除报告。上述生成目录均不提交 Git；业务源码、冻结契约、模板和测试夹具仍在各自受版本控制的目录。

### 执行清理

保留依赖及增量缓存、只删除已结束的 Rust 测试 EXE 与对应 `.pdb`/`.d` 时，先运行 `pwsh -NoProfile -File scripts/clean-generated-artifacts.ps1 -RustTestOutputsOnly -ListOnly` 取得精确清单，再使用同一参数去掉 `-ListOnly`。入口按 Cargo `test-*.json` 指纹识别，要求有效 Cargo 目录标签，拒绝 Cargo/测试进程仍占用的目标；不依据文件名前缀猜测，不删除实际应用、示例程序和可复用库。`-RustTestOutputsOnly` 不与全量清理选项混用。测试 profile 使用 `line-tables-only` 调试信息，保留回溯行号并降低测试 EXE 体积。

桌面专项 `node scripts/test_native_desktop_editions.mjs <测试包父目录> Full --entry-rows-only` 使用隔离数据验证自由文字/空白行、空品名数量行、撤销/重做、保存回读、真实发票/装箱单 PDF，以及管理员删除误录档案与关联账号保护提示；同一场景也纳入 Full 完整验收。必须先用本批源码构建独立 Full 测试包。

只需释放 Rust 构建空间时，可在确认编译/测试进程已结束、可运行文件及验证记录已另存后，定向执行 `cargo clean --target-dir target`。旧的仓库内独立 Cargo 输出也可用 `--target-dir` 指定其已盘点路径。不要把 Cargo 下载缓存或业务目录作为目标；清理后须重新编译，但保留的 Cargo/npm 下载缓存可继续复用。

需要保留当前编译缓存时，先按 Cargo `test-*` 指纹识别已完成的测试可执行文件，核对旧可执行文件的当前替代品，再为旧增量目录生成精确路径清单。测试程序不一定以 `export_doc` 开头；依赖库、当前可执行文件及仍需的增量版本应保留。2026-10-15 的定向清理、保留校验和空间实测见[实施进度](../docs/程序改进重构进度文档.md)，没有改变默认清理器的保护范围。

先 `clean-generated-artifacts.ps1 -ListOnly` 盘点，再按根 AGENTS 中已授权的范围清理。保留业务数据、模板、模型、已需资源、交付输出和可复用依赖缓存；依赖缓存、node_modules、整个运行缓存及发布输出只有用户明确同意后才能删除。

```powershell
pwsh -NoProfile -File scripts/clean-generated-artifacts.ps1 -ListOnly -IncludeCodexRuntimeWorkspaces
pwsh -NoProfile -File scripts/clean-generated-artifacts.ps1 -IncludeCodexRuntimeWorkspaces
```

扫描源码树时直接跳过依赖、受保护数据/资源、私有 `KEY`、链接、本地运行目录和嵌套 Git 仓库，生成目录只作为整体候选，不继续扫描其内部。`.git` 目录与 worktree/submodule 的 `.git` 文件都受保护，其内部构建目录不能作为独立目标绕过保护。任何候选含业务库、备份、私有材料或链接都保留；即使显式清理发布输出，也不放行业务数据、Git 工作树或私有目录。

清理前结束构建、测试及写日志的进程，并把需要保留的验收结论写入进度文档。根部开发/测试日志与一次性目录使用同一盘点、路径保护和 `-WhatIf` 流程；不会顺带清除包内 `App_Data/Logs`、缓存目录内的日志或根部其它文件。Git worktree 仍须检查提交状态后通过 Git 命令单独移除。

混有数据库或程序包的历史测试目录会整体保留，这不表示目录内每张截图、诊断日志和独立 Chrome 临时配置都仍有用途。用户要求进一步清理时，应单独列出这些可重建文件，核对进程占用、完整路径及链接后定向删除，并校验保留数据；不能删除父目录来绕过保护。需要记录清理过程时，将执行输出写入 `.txt`，避免根部 `.log` 清理规则选中正在写入的执行日志。

用户明确授权清理过期测试程序包及隔离测试数据时，可在确认来源、用途、无运行进程和无需保留后，按已盘点的绝对路径单独回收。此授权不扩大默认清理器的保护边界；正在使用的程序及业务库、用户备份/原件、私有补丁和可复用缓存继续保留。测试目录名或较早修改时间本身不能证明其中全部内容都已无用。

带有效 `CACHEDIR.TAG` 的 Cargo 输出中，`debug/release/build/*/out` 下的生成资源可随构建目录清理；这不会放行业务数据库、备份、链接或其它位置的 `Resources/Templates`。PostgreSQL 的 `PG_VERSION`、SQLite 伴随文件同样受到保护。`-IncludeCodexRuntimeWorkspaces` 清理旧构建/一次性工作区时，仍保留原生归档、PostgreSQL 客户端、审计工具和包下载缓存；`artifacts/releases`、其它目录的 `exportdoc-desktop/web/container` 发布归档及 Cargo 安装器 `bundle` 默认按发布输出保护。清理后的下一次构建需要重新编译，但无需重复下载保留的依赖。
