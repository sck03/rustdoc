# 脚本使用说明

> 2026-09-20：当前主线为 Tauri 2 + React + Rust。相邻 ExportDocManager_CS 是只读行为和脚本对照；这里的正式构建不发布 ASP.NET sidecar，不要求 .NET SDK／Runtime，不使用 NPOI。

## 本地入口

| 入口 | 用途 |
| --- | --- |
| `build-windows-desktop-run.cmd`／`.ps1` | 沿用原版名称，调用统一 Rust 打包器，生成 Windows Full 便携目录 |
| `build-windows-installers.cmd`／`.ps1` | 生成 Tauri NSIS 安装器，使用同一 Rust 业务与资源 |
| `build-native.cmd`／`.ps1` | 当前平台 Rust + Tauri + React 便携包；默认目录 `artifacts/native-desktop/ExportDocManager.Tauri` |
| `run-native.cmd`／`.ps1` | 启动已构建便携包；`-AppRoot` 可指定包目录 |
| `package-native-web-server.ps1` | 原版 React 构建、Rust HTTP 服务、报表与可选 OCR 资源；目标数据库 PostgreSQL 18 |
| `run-native-docker.cmd`／`.ps1` | React + Rust HTTP + PostgreSQL 18 容器构建、启动和停止 |

普通用户只运行 `scripts/` 根入口；`lib/`、`prepare-*`、`verify-*`、`assert-*` 为内部组合部件。

`build-native.ps1 -PreflightOnly -NoPause` 只检查 Rust、Node、curl 等构建工具。`-Configuration Debug` 用于联调，默认 Release。`-RustTarget` 明确目标架构；`-Bundles nsis`、`-Bundles deb,appimage` 或 `-Bundles app,dmg` 生成对应平台安装／应用包。安装器需要对应平台工具链。

`-SkipBuild` 只整理已经构建好的相同 profile／target 二进制与资源。正式包默认包含 OCR；`-WithoutOcr` 仅生成明确不提供文字识别的轻量验收包，不能用它代替 Full 功能验收。

当前开放 Full 打包。原版 Document／Sales／Administration 的 Rust 资源裁剪、权限隔离和更新通道仍需分别验收；不通过更换标题冒充四版已完成。

## 桌面启动检查与资源

Windows 在创建 Tauri 窗口前检查系统最低版本和 WebView2。x64 便携包携带原版固定清单验证的微软离线安装器；缺少 WebView2 时显示安装／退出选择，保留取消、繁忙、超时和需重启处理。构建时核对微软签名、版本、大小和 SHA-256。Windows GNU 构建同时携带 `WebView2Loader.dll`。

程序随后读取本包资源清单，核对中文字体、PDFium 及已声明的 OCR 工具、ONNX 和模型。Windows OCR 只携带四个 app-local CRT DLL，不安装全局 .NET 或整个 VC 运行库。Linux 使用 WebKitGTK 4.1，macOS 使用系统 WKWebView；其它平台必须在对应 runner／设备验收。

`eng/native-runtime-packages.json` 固定已校验来源及签名的原生资源归档版本和 SHA-512。NuGet 在此只是原生 DLL／so／dylib 的下载载体；不复制托管程序集，也不执行 dotnet restore。不要把 NuGet lock 的“内容哈希”直接当成签名后整个归档的哈希。PowerShell 打包器和容器资源准备都读取这份清单。

共享字体、Excel 模板、原版报表模板、PDFium、notices 和 OCR 资源通过 `lib/native-package-resources.ps1` 组装，桌面与网页服务不维护重复清单。临时文件和下载缓存写仓库 `.codex-runtime/`。AppRoot 与 DataRoot 显式传入；桌面 WebView profile 写 DataRoot/WebView。

六份默认报表统一为 `.dtpl`。开发维护源为 `lib/default-report-designs.mjs`，通过 `node scripts/generate_default_dtpl_templates.mjs` 重建容器，再运行 `node scripts/test_report_designer_v3_contract.mjs` 校验设计器读写与尺寸保留。生成源和六份资产同时提交；旧 HTML 仅可作只读版式参考，不进入目录或发布包。

## 网页与 Docker

`run-native-docker.ps1 -PrepareOnly -NoPause` 只生成私有配置；普通运行构建并启动，`-Stop -NoPause` 停止并保留数据库。默认绑定 `127.0.0.1:5188`；局域网地址须显式设置。凭据保存在忽略的 `deploy/rust-native/runtime/`，不进入 Git 或镜像。

首次浏览器管理员用 `admin`、自定 8—128 字符密码及该目录的 `bootstrap-token.txt` 初始化。日常服务只持有 PostgreSQL 18 业务连接，维护连接只供初始化／维护使用。桌面 SQLite 空库仍为 admin 空密码，数据库使用独立 Rust 基线 4，不能打开 C# v19 或旧 Rust 试验库。

## 远端入口

### 备份、迁移与恢复

- 桌面 `.edmrecovery` 灾备包包含 SQLite 快照、Security 主密钥和 DataRoot/Templates；从本机明确选择的文件校验并暂存后，退出重开即可恢复。启动先取得实例锁，校验暂存内容，保存原文件并记录替换日志；失败停止启动，保留恢复目录。
- PostgreSQL API 沿用“物理备份”名称，实际为 `pg_dump` custom-format `.dump`，只恢复数据库。换服务器使用另含主密钥和用户模板的 `.edmmigration` 完整包。加密包明文及下载上限为 256 MiB；超大数据库由部署管理员使用 PostgreSQL 原生离线备份工具。
- 服务器在界面完成密码/确认及暂存后，停止正常 API，使用同一个 AppRoot/DataRoot 执行 `ExportDocManager.Server --app-root <AppRoot> --data-root <DataRoot> --restore-pending`。维护进程需提供 `EXPORTDOCMANAGER_POSTGRES_CONNECTION`、独立 `EXPORTDOCMANAGER_POSTGRES_MAINTENANCE_CONNECTION` 和 NOLOGIN 所有者 `EXPORTDOCMANAGER_POSTGRES_OWNER`；两种连接须指向同一数据库，均可使用对应 `_FILE`。命令完成即退出，再以业务连接启动 API。
- Docker 使用 `scripts/run-native-docker.ps1 -RestorePending -NoPause`：停 API、运行独立 restore 容器、成功后再启动。普通 application 容器没有维护密钥；数据库实例锁阻止 API 与恢复同时操作。
- 恢复先生成安全 dump，再以 `pg_restore --single-transaction --no-owner --no-privileges --role <NOLOGIN-owner>` 执行，存储层验证 schema 并重新授予业务账号必要表/序列权限。失败保留标记和安全备份；不得删除标记冒充恢复完成。
- 主密钥、暂存目录与恢复前副本使用共同的 Windows 私有 ACL／Unix 0700 目录边界。环境变量提供主密钥的部署明确拒绝独立密钥包操作，须由管理员安排密钥迁移。
- 网页包随附 `Tools/PostgreSQL` 客户端和许可；版本、来源及 Windows SHA-256 在 `eng/native-runtime-packages.json`。Linux 客户端采用官方 bookworm 资源以避免在 Ubuntu 24.04 上引入更高 glibc 要求；容器使用 trixie 资源。macOS 构建机先安装 PostgreSQL 18 Homebrew formula，版本须与中央清单一致。

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

本地需要变更程序版本时先运行 `node scripts/sync-version.mjs 0.1.2`，再使用现有构建入口。该脚本同步 Rust 主工作区、独立 OCR/Excel 工具及各自锁文件，不修改保留 C# 对照的构建属性，也不升级第三方依赖。

运行已发布镜像：`./scripts/run-native-docker.ps1 -Image ghcr.io/<owner>/exportdoc-rust-native:0.1.2 -SkipBuild -NoPause`。后续启动、停止和恢复均使用同一 `-Image` 参数。`-SkipBuild` 使用已有/拉取的镜像；默认本地命令仍从源码构建。

## 验证和证据

文档整理后运行 `node scripts/verify-documentation-links.mjs`，检查 docs、根 README 和本页的本地文件链接；当前入口不应引用已退役的文档或工作流。

按用户要求先集中完成一批页面、后端和操作，再统一联调与最终门禁。开发中只做必要编译和针对失败的回归。

- Rust：`cargo fmt --all --check`、`cargo test --locked --workspace`、`cargo check --locked --workspace --all-features`。
- 实库：`test-native-postgres.ps1 -PostgresBin <PostgreSQL-18-bin>` 创建并停止隔离集群；忽略的实库测试不计通过。
- React：项目 `build`、API／登录／权限／草稿／无障碍及相应页面回归；真正的 Tauri 窗口和输出仍需实跑。
- 依赖：`generate-dependency-governance.mjs artifacts/dependency-governance --release --verify-repository`，要求 `unresolved=0 / disallowed=0`。
- 平台：`verify-native-desktop.mjs` 验证 Tauri／SQLite，排除 Slint／egui／PostgreSQL 桌面依赖；`assert-tauri-command-permissions.ps1` 校验 command 与能力白名单。
- 脚本：`verify-script-suite.ps1`、`verify-github-workflow-actions.mjs`、`test_tauri_updater_release_contract.mjs`、`github/verify-public-source.ps1`、`git diff --check`。

Rust notices 不列保留 C# 的运行图。`verify-dependency-policy.mjs` 仍单独约束未删除的 C# 对照锁文件，NPOI 2.7.6 只属于该对照规则，与 Rust 业务运行无关。

报表原版 React 设计器可复用，但 Rust 渲染仍有明确未完成项；当前事实见 `docs/Rust原生功能迁移核对表.md`。旧 Slint 的 `--validation --ui-smoke` 入口已退役。

## 工作区清理

只需释放 Rust 构建空间时，可在确认编译/测试进程已结束、可运行文件及验证记录已另存后，定向执行 `cargo clean --target-dir target`。旧的仓库内独立 Cargo 输出也可用 `--target-dir` 指定其已盘点路径。不要把 Cargo 下载缓存或业务目录作为目标；清理后须重新编译，但保留的 Cargo/npm 下载缓存可继续复用。

先 `clean-generated-artifacts.ps1 -ListOnly` 盘点，再按根 AGENTS 中已授权的范围清理。保留业务数据、模板、模型、已需资源、交付输出和可复用依赖缓存；依赖缓存、node_modules、整个运行缓存及发布输出只有用户明确同意后才能删除。
