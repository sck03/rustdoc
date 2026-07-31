# GitHub Actions 工作流用途与运行手册

> 更新日期：2026-07-31
> 适用仓库：`sck03/rustdoc`
> 工作流目录：[`../.github/workflows`](../.github/workflows)

本文是仓库当前 15 个 GitHub Actions 工作流的入口说明。它回答三个问题：每个工作流验证或发布什么、什么时候会运行、失败后应该从哪里排查。工作流只负责一次性的代码门禁、构建、Artifact、GHCR 镜像和 GitHub Release，不是 PostgreSQL/API 的长期运行服务器，也不能替代目标公司服务器上的备份、权限、并发和真机验收。

## 1. 总体分类与建议顺序

| 类别 | 工作流 | 默认触发 | 主要结果 |
| --- | --- | --- | --- |
| 公开源码与供应链门禁 | [`public-source-guard.yml`](../.github/workflows/public-source-guard.yml)、[`dependency-governance.yml`](../.github/workflows/dependency-governance.yml) | 手工；依赖治理另有每周定时和依赖文件变更触发 | 阻止私有密钥/大文件进入公开仓库，审计 npm/NuGet/Cargo，生成 SBOM |
| 前端、字体与报表 | [`cross-platform-typography.yml`](../.github/workflows/cross-platform-typography.yml) | `main` 相关路径 push、PR、手工 | Windows/macOS/Linux 字体、缩放、浏览器会话和 PDF 分页证据 |
| Firefox/WebKit 多端验收 | [`browser-compatibility.yml`](../.github/workflows/browser-compatibility.yml) | Web/API 相关路径 push、PR、手工 | 真实 Firefox/WebKit 桌面与手机视口、axe 严重问题、横向溢出、页面异常和 HTTP 500 |
| 原生多平台契约 | [`cross-platform-validation.yml`](../.github/workflows/cross-platform-validation.yml) | 手工 | Windows/Linux/macOS 的 x64/ARM64 编译、Tauri/Rust 合同 |
| 真实共享数据库 | [`postgresql-integration-validation.yml`](../.github/workflows/postgresql-integration-validation.yml) | `main` 相关路径 push、PR、手工 | PostgreSQL 18 初始化、索引、容量、并发、岗位权限和 SQL 安全 |
| 容器运行时 | [`container-runtime-validation.yml`](../.github/workflows/container-runtime-validation.yml) | `main` 相关路径 push、PR、手工 | HTTP/HTTPS Compose、镜像、探针、volume 持久化和备份恢复 |
| 镜像发布 | [`container-images.yml`](../.github/workflows/container-images.yml) | 手工 | Linux amd64/arm64 API/Web 镜像并推送 GHCR |
| 桌面打包 | 三个平台入口 + [`desktop-package-reusable.yml`](../.github/workflows/desktop-package-reusable.yml) | 手工 | Windows NSIS、Linux deb/AppImage、macOS dmg Artifact；可选 Release |
| 浏览器服务器打包 | 两个平台入口 + [`browser-server-package-reusable.yml`](../.github/workflows/browser-server-package-reusable.yml) | 手工 | Windows ZIP、Linux x64/ARM64 tar.gz Artifact；可选 Release |

推荐的发布前顺序是：先运行公开源码和依赖门禁，再运行 PostgreSQL、容器和字体/报表门禁，确认结果后先以 `publish_release=false` 打包验收，最后才运行 GHCR 或带 Release 的发布入口。`cross-platform-validation.yml`、`container-images.yml` 和所有打包入口均为重型手工任务，不会因为普通文档提交自动启动。

## 2. 逐个工作流说明

### 2.1 Public source guard

文件：[`public-source-guard.yml`](../.github/workflows/public-source-guard.yml)
显示名称：`Public source guard`

- **触发：** 仅 `workflow_dispatch`，建议在公开推送前手工运行。
- **平台：** `ubuntu-latest`；安装 Node.js 24。
- **做什么：** 执行公开源码边界检查，拒绝注册机、签发私钥、授权产物、过大的二进制和不应进入公开仓库的运行文件；检查所有工作流的 Action/Node 24/Artifact v7-v8 版本政策；检查 Tauri updater 的 endpoint、公钥和签名信任契约。
- **输出：** 只写 Job Summary 和日志，不上传 Artifact，不创建 Release，不修改仓库。
- **Secrets/Variables：** 不需要自定义 Secret 或 Variable；只读仓库内容。
- **常见失败：** 新增了私钥/`KEY` 目录/浏览器大二进制、工作流仍引用旧 Action 主版本、运行时 updater 配置绕过构建时信任边界。
- **耗时：** 通常 1—3 分钟。

### 2.2 Cross-platform typography and report validation

文件：[`cross-platform-typography.yml`](../.github/workflows/cross-platform-typography.yml)
显示名称：`Cross-platform typography and report validation`

- **触发：** `workflow_dispatch`；`main` push 和 PR 只在字体、模板、Web、报表服务及相关验证脚本路径变化时触发。
- **平台：** 三个并行矩阵：`windows-latest`、`macos-latest`、`ubuntu-latest`；另有 Ubuntu 汇总 Job。
- **做什么：** 安装受许可约束的 Noto CJK 报表字体和 Chrome Headless Shell，执行浏览器会话、CSS 治理、320/375/390 窄屏与缩放合同；渲染多语言长文本 PDF，检查换行、重叠、分页，再比较三平台指标。Linux 仅在该 CI 步骤显式使用 `EXPORTDOCMANAGER_CHROMIUM_NO_SANDBOX=1`，不改变生产默认值。
- **输出：** 每个平台上传缩放证据和 PDF/metrics/layout JSON；汇总 Job 上传比较报告。通常保留 14 天，汇总报告保留 30 天。
- **Secrets/Variables：** 不需要自定义 Secret；字体和浏览器在 runner 临时目录准备。
- **常见失败：** 字体许可证文件或字体下载失败、Chrome Headless Shell 下载/启动失败、Linux 系统库缺失、单场景 CDP 超时、当前模板导致文本重叠或跨平台分页指标偏差。
- **耗时：** 三平台并行通常 8—20 分钟；首次下载字体/浏览器或 runner 拥堵时更久。单个缩放合同有 5 分钟上限。

### 2.3 Cross-platform validation

文件：[`cross-platform-validation.yml`](../.github/workflows/cross-platform-validation.yml)
显示名称：`Cross-platform validation`

- **触发：** 仅 `workflow_dispatch`。
- **平台：** Windows、Linux、macOS 三个矩阵 Job；每个 Job 同时验证 x64 和 ARM64 的 RID/编译合同。
- **做什么：** 安装原生桌面依赖，执行 .NET API/Infrastructure 的 x64/ARM64 还原与 Release 编译，安装 Web/Tauri 依赖，运行 updater 信任合同、Web 构建、Tauri `cargo check/test`。公开源码不携带大浏览器二进制，因此该工作流只验证浏览器路径/壳合同；正式打包由专用发布工作流供给浏览器。
- **输出：** Job Summary 和构建日志；不推送镜像、不创建 Release，通常不上传业务 Artifact。
- **Secrets/Variables：** 不需要自定义 Secret；允许缺少浏览器的公开源码契约由工作流环境显式设置。
- **常见失败：** Linux 原生 GTK/WebKit 依赖、ARM64 RID 还原资产缺失、Rust target 或 Tauri 依赖编译失败、误把正式签名配置要求带入测试构建。
- **耗时：** 10—25 分钟，取决于三平台并发和 Rust/. NET 缓存命中情况。

### 2.4 PostgreSQL integration validation

文件：[`postgresql-integration-validation.yml`](../.github/workflows/postgresql-integration-validation.yml)
显示名称：`PostgreSQL integration validation`

- **触发：** `workflow_dispatch`；`main` push/PR 在 Infrastructure、数据库相关测试或该工作流变化时触发。
- **平台：** `ubuntu-latest`，使用服务容器 `postgres:18-bookworm`。
- **做什么：** 建立真实 PostgreSQL 连接，执行初始化、Unicode locale、HS 前缀/模糊索引、申报实例和发票分页、容量数据、恶意搜索文本、乐观并发、会话和四岗位权限/数据范围测试。手工输入可选择 HS/实例和发票容量 `10000 / 100000 / 1000000`；push/PR 默认各为 10000。
- **输出：** Job Summary、测试日志和失败堆栈；不把测试数据库或 dump 上传为公开 Artifact。
- **Secrets/Variables：** 不需要仓库 Secret。数据库密码是一次性 runner 环境变量，仅用于测试，不可复制到生产。
- **常见失败：** PostgreSQL 服务未通过健康检查、百万级数据超过 45 分钟测试上限、索引/排序计划与预期不符、权限范围或 SQL 安全回归。
- **耗时：** 10k 通常 5—15 分钟；100k/1m 是容量验收，可能需要 15—45 分钟，应按 runner 资源单独观察。

### 2.5 Container runtime lifecycle validation

文件：[`container-runtime-validation.yml`](../.github/workflows/container-runtime-validation.yml)
显示名称：`Container runtime lifecycle validation`

- **触发：** `workflow_dispatch`；`main` push/PR 在 Compose、Dockerfile、API/Web、OCR 或该工作流变化时触发。
- **平台：** `ubuntu-latest`，真实执行 Docker Compose。
- **做什么：** 校验 HTTP 基础 Compose 和 HTTPS overlay；构建 API/Web 镜像并分步启动；检查 `/readyz`、匿名轻量 `/healthz`、CSP/HSTS、API `5188` 不对宿主发布；删除并重建 PostgreSQL 容器验证 bind volume 数据持久化；在 runner 内执行 `pg_dump/pg_restore` 恢复验收，最后清理容器。匿名 `/healthz` 在 API 路由表和鉴权/许可证/数据库服务解析之前由早期探针直接返回，不扫描浏览器、OCR、PostgreSQL 工具或服务器路径；管理员 Bearer/可信桌面连接仍可进入完整诊断。
- **输出：** 只上传 `artifacts/container-runtime/evidence` 下的探针、Compose 状态和日志证据，不递归扫描 PostgreSQL 数据目录，也不上传数据库 dump；通常保留 14 天。
- **Secrets/Variables：** 不需要自定义 Secret。测试密码、自签证书和端口由工作流临时生成，不能用于生产。
- **常见失败：** Docker 构建超过 15 分钟、API 启动/健康检查超过 5 分钟、镜像内 Chromium/OCR 缺库、端口或 Docker 网段冲突、volume 权限不足、备份恢复失败。API 的 `expose: 5188` 只表示 Compose 内部可达，不等于宿主端口发布；边界检查读取容器实际 `NetworkSettings.Ports` 的 host binding，不使用会把 `expose` 也打印出来的 `docker compose port` 作为判据。
- **耗时：** 通常 10—30 分钟；构建和启动均有明确上限，失败时先看上传的 evidence 与 API health JSON。公开 `/healthz` 采用 3 次、每次最多 3 秒的有界重试；仍失败时步骤会额外尝试 API 容器内直连并输出 Web/API/Compose 诊断，避免只看到一个无上下文的 curl 超时。

### 2.6 Dependency security and SBOM governance

文件：[`dependency-governance.yml`](../.github/workflows/dependency-governance.yml)
显示名称：`Dependency security and SBOM governance`

- **触发：** 手工、每周一 02:23 UTC 定时、`main` 依赖文件 push，以及依赖文件 PR。
- **平台：** `ubuntu-latest`；.NET 8、Node.js 24 和 stable Rust。
- **做什么：** 锁定还原 Web/Tauri/npm/NuGet/Cargo 图，执行 npm 生产依赖审计、NuGet 直接/传递审计和三个 `Cargo.lock` 的 RustSec 审计，拒绝新增漏洞、unsound 或 yanked crate，并生成 SPDX 2.3、CycloneDX 1.6 和第三方依赖清单。当前已审查的 React Router RSC 公告和 Tauri GTK 传递例外必须保持精确契约，不能用“忽略全部漏洞”通过。
- **输出：** `dependency-governance` Artifact，通常保留 30 天；不修改依赖版本、不创建 Dependabot PR。
- **Secrets/Variables：** 不需要自定义 Secret；需要 runner 能访问 npm/NuGet/RustSec advisory 源，源不可达时应区分网络告警与真实审计结果。
- **常见失败：** lock 文件漂移、审计源暂时不可达、出现新的 advisory/unsound/yanked crate、SBOM 生成脚本失败。
- **耗时：** 通常 8—30 分钟。

### 2.7 Build and publish container images

文件：[`container-images.yml`](../.github/workflows/container-images.yml)
显示名称：`Build and publish container images`

- **触发：** 仅 `workflow_dispatch`；表单要求版本号，可选择是否更新 `latest`。
- **平台：** `ubuntu-latest`，Docker Buildx + QEMU；矩阵构建 `api` 和 `web` 两个组件的 `linux/amd64`、`linux/arm64`。
- **做什么：** 校验版本并同步 runner 工作区版本，构建带 provenance/SBOM 的多架构镜像，写入版本、`latest`（可选）和 SHA 标签。
- **输出：** 推送到 `ghcr.io/<仓库所有者>/export-doc-manager-api` 和 `...-web`；不上传普通 Artifact、不创建 GitHub Release。
- **Secrets/Variables：** 使用 GitHub 自动 `GITHUB_TOKEN`，工作流权限为 `packages: write`；不需要额外私钥。
- **常见失败：** GHCR 权限/包可见性、版本格式错误、QEMU 或 Buildx 构建失败、Dockerfile 公开源码边界检查失败。API 镜像会先回收一次性 Ubuntu runner 上明确无关的 Android、宿主 .NET SDK、GHC/Haskell 和 Swift 工具空间；该 job 的 .NET 发布在 Docker SDK 镜像内完成。NuGet/Cargo 使用 BuildKit cache mount，GHA cache 使用 `mode=min`；若仍出现 `No space left on device`，先确认这些步骤实际执行并查看 `df -h /`，不要通过关闭 provenance/SBOM 或删除随包 OCR/字体绕过交付契约。工作流只在手工确认后推送，不能把测试分支镜像误当生产版本。
- **耗时：** 多架构 API/Web 通常 10—30 分钟，首次无缓存时可能更久。

### 2.8 Reusable desktop package build

文件：[`desktop-package-reusable.yml`](../.github/workflows/desktop-package-reusable.yml)
显示名称：`Reusable desktop package build`

- **触发：** `workflow_call`，不能从 Actions 列表单独运行；由 Windows/Linux/macOS 三个平台入口调用。
- **平台：** 由调用方传入 runner、平台、架构、产品版和 bundle 类型。
- **做什么：** 安装 .NET 8/Node 24/Rust，准备开源字体和 Chrome Headless Shell（Linux ARM64 使用明确的 Chromium ARM64 路径），构建 API/Web/Tauri、OCR 资源并验证精简 payload。`publish_release=false` 生成未签名验收包；`true` 才启用 updater 签名、上传安装包并合并 `latest.json`。
- **输出：** `export-doc-manager-<platform>-<arch>-<edition>-<version>` Artifact，通常保留 14 天；发布模式另上传 GitHub Release 资产。
- **Secrets/Variables：** 测试模式不需要签名材料；发布模式必须配置仓库 Variable `EXPORTDOCMANAGER_UPDATER_PUBLIC_KEY`，以及带密码的 Secrets `TAURI_SIGNING_PRIVATE_KEY`、`TAURI_SIGNING_PRIVATE_KEY_PASSWORD`。`EXPORTDOCMANAGER_UPDATER_ENDPOINT` 改为可选：留空时安装包只内置公钥，由管理员安装后在系统设置中配置 GitHub、自建服务器或公司内网地址。若构建时就要内置 HTTP 默认地址，还必须显式配置 `EXPORTDOCMANAGER_ALLOW_INSECURE_UPDATER_ENDPOINT=true`；公网默认地址仍应使用 HTTPS。调用方使用 `secrets: inherit`，不应把私钥写入仓库或日志。
- **常见失败：** 版本格式、浏览器资源缺失/执行权限、OCR 运行时缺库、公钥或私钥缺失、未显式放行却尝试内置 HTTP endpoint、Release tag 已被其它版本占用。依赖清单校验不再因表单版本与仓库当前版本不同而失败；若提示 stale，错误会指出首个真实差异行，应重新生成并审查依赖，而不是删除 `--verify-repository`。Linux AppImage 使用 Tauri 自带的无 FUSE 解压运行模式，不需要额外安装 `libfuse2`；工作流显式安装 `file`、`xdg-utils`，并设置 `NO_STRIP=1`，避免 `linuxdeploy` 内置旧版 `strip` 重复处理 Ubuntu 24.04 新 ELF 段时退出。桌面构建统一带 `--verbose`，后续 `linuxdeploy`、签名或公证失败会保留真实子进程输出。桌面摘要固定使用 PowerShell 字面量 here-string，避免 Markdown 行尾反引号被解释为续行；治理门禁会拒绝重新引入该 ParserError。
- **耗时：** 15—40 分钟，首次下载浏览器和 Rust 依赖时更久。

### 2.9 Reusable browser server package

文件：[`browser-server-package-reusable.yml`](../.github/workflows/browser-server-package-reusable.yml)
显示名称：`Reusable browser server package`

- **触发：** `workflow_call`，由 Windows/Linux 浏览器服务器入口调用。
- **平台：** 由调用方传入 Windows x64、Linux x64 或 Linux ARM64 runner/RID/Rust target。
- **做什么：** 构建 Web 静态资源和自包含 ASP.NET Core 服务器包，打入 Chrome Headless Shell、Rust OCR、Excel analyzer、运行配置、`initialize-windows.ps1`/`initialize-linux.sh` 一键初始化脚本和启动脚本；运行浏览器 PDF、payload 和 OCR 验证。该包由单个 ASP.NET Core 进程同源托管 Web/API，不需要 Nginx，但仍需要目标机 PostgreSQL。
- **输出：** Windows ZIP 或 Linux x64/ARM64 tar.gz Artifact，通常保留 14 天；`publish_release=true` 时使用 `GITHUB_TOKEN` 上传同名 Release 资产。
- **Secrets/Variables：** 不需要 updater 私钥；只使用 GitHub 自动 token 发布 Release。
- **常见失败：** Chrome/Chromium ARM64 资源不可用、Linux 执行权限或 ONNX/libonnxruntime 缺失、服务器包缺少 `wwwroot/index.html`、版本格式错误。表单版本只写入 SBOM 应用元数据，不再改变根 notices/inventory；真实依赖漂移仍由 release 许可证和仓库清单门禁阻止。
- **耗时：** 10—30 分钟。

### 2.10 Build Windows desktop package

文件：[`windows-desktop-package.yml`](../.github/workflows/windows-desktop-package.yml)
显示名称：`Build Windows desktop package`

- **触发：** 仅 `workflow_dispatch`；输入版本、`Document/Sales/Full` 产品版和是否发布 Release。
- **平台/产物：** `windows-latest`、Windows x64、NSIS 安装包；调用桌面可复用工作流并内置 Windows Chrome Headless Shell。
- **发布：** 默认只生成未签名 Artifact；选择 `publish_release=true` 才要求 updater Variables/Secrets 并上传 Release。
- **常见失败：** 版本或产品版输入、Windows 浏览器资源、Tauri bundle、签名配置和 Release 权限。

### 2.11 Build Linux desktop package

文件：[`linux-desktop-package.yml`](../.github/workflows/linux-desktop-package.yml)
显示名称：`Build Linux desktop package`

- **触发：** 仅 `workflow_dispatch`；输入版本、产品版、x64/ARM64 和是否发布 Release。
- **平台/产物：** x64 使用 `ubuntu-latest`，ARM64 使用 `ubuntu-24.04-arm`；输出 deb/AppImage。Linux ARM64 当前是应用编译合同，浏览器资源使用明确的 Chromium ARM64 供给路径，必须以工作流实际结果为准。
- **发布：** 规则与 Windows 桌面入口相同，默认不签名、不发布。
- **常见失败：** GTK/WebKit、`file`/`xdg-utils`、AppImage 打包、执行权限、ARM64 runner 可用性和签名配置。若日志停在 `failed to run linuxdeploy`，应查看同一步骤前面的 verbose stderr；磁盘不足会有明确的 `No space left on device`，不需要把清理大量 runner 预装软件作为本工作流的固定前置步骤。

### 2.12 Build macOS desktop package

文件：[`macos-desktop-package.yml`](../.github/workflows/macos-desktop-package.yml)
显示名称：`Build macOS desktop package`

- **触发：** 仅 `workflow_dispatch`；输入版本、产品版、ARM64/x64 和是否发布 Release。
- **平台/产物：** `macos-15` 或 `macos-15-intel`，输出 dmg；按架构内置对应官方 Chrome Headless Shell。
- **发布：** 默认只生成 Artifact；发布模式要求同一套 updater Variables/Secrets。签名、公证和真机启动仍需单独验收，工作流成功不等于 Apple 发布合规完成。
- **常见失败：** macOS runner 架构选择、浏览器执行权限、Tauri bundle、签名/公证材料或 Release 权限。

### 2.13 Build Windows browser server package

文件：[`windows-browser-server-package.yml`](../.github/workflows/windows-browser-server-package.yml)
显示名称：`Build Windows browser server package`

- **触发：** 仅 `workflow_dispatch`；输入版本和是否上传 GitHub Release。
- **平台/产物：** `windows-latest`、`win-x64`，生成 Windows 浏览器服务器 ZIP；调用 `browser-server-package-reusable.yml`。
- **发布：** 默认 Artifact-only；选择发布后使用 GitHub 自动 token 上传 Release，不需要 Tauri updater 私钥。
- **常见失败：** Web/API 自包含发布、Chrome Headless Shell、OCR/Excel analyzer 资源或压缩包内容校验。

### 2.14 Build Linux browser server package

文件：[`linux-browser-server-package.yml`](../.github/workflows/linux-browser-server-package.yml)
显示名称：`Build Linux browser server package`

- **触发：** 仅 `workflow_dispatch`；输入版本、x64/ARM64 和是否上传 Release。
- **平台/产物：** x64 使用 `ubuntu-latest`/`linux-x64`，ARM64 使用 `ubuntu-24.04-arm`/`linux-arm64`；生成对应 tar.gz。
- **发布：** 默认不发布；发布模式使用自动 token 上传 Release。该服务器包不需要 Nginx，但目标机仍需 PostgreSQL 和适当的进程/防火墙管理。
- **常见失败：** ARM64 runner、Chromium ARM64、Linux 执行权限、ONNX/OCR 运行库、服务器包 payload 检查。

### 2.15 Browser compatibility acceptance

文件：[`browser-compatibility.yml`](../.github/workflows/browser-compatibility.yml)
显示名称：`Browser compatibility acceptance`

- **触发：** Web/API、跨浏览器 smoke 或本工作流变化时的 `main` push、Pull Request，也可手工运行。
- **平台与做法：** `ubuntu-latest`；安装 Firefox 与 WebKit Playwright runtime，构建 Web/API，然后使用仓库 `artifacts/playwright-browsers` 运行真实浏览器。
- **验收内容：** 桌面 `1440×1000` 与手机 `390×844`；登录、工作区标题、desktop/phone 模式、移动端导航、横向溢出、页面异常、HTTP 500 和 `critical/serious` axe 问题均为硬门禁。该工作流只验证 Web/API，不冒充 Tauri 原生 WebView 或官方单一窗口客户端验收。
- **运行数据边界：** Playwright 浏览器、临时 API DataRoot 和日志只写 runner 的 `artifacts/`，不上传业务数据库、密钥或浏览器缓存。

## 3. Node、Action 和 Artifact 版本政策

截至 2026-07-30，仓库的 15 个工作流已通过静态版本门禁：

- 显式构建工具统一使用 `actions/setup-node@v5` 并指定 Node.js `24`；客户运行的 Web、Tauri、API 和容器不需要安装 Node.js。
- `actions/checkout@v5`、`actions/setup-dotnet@v5`、`actions/setup-python@v6` 使用当前仓库允许的 Node 24 runtime 主版本。
- Artifact 统一为 `actions/upload-artifact@v7` 和 `actions/download-artifact@v8`。新工作流不得重新引入 v3—v6。
- Docker 工作流使用 `docker/setup-qemu-action@v4`、`docker/setup-buildx-action@v4`、`docker/login-action@v4`、`docker/metadata-action@v6`、`docker/build-push-action@v7`；这些引用已纳入 Action runtime 门禁。
- `dtolnay/rust-toolchain@stable` 是 composite Action，不应误判成 Node 20/22/24 的 JavaScript Action；Rust 版本由工具链和 lock 文件门禁控制。
- `scripts/verify-github-workflow-actions.mjs` 会阻止旧 Node runtime、旧 Artifact 主版本和未登记的 Action 引用。工作流改动后应先运行公开源码守卫或该脚本，再提交。

Action runtime 的升级只影响 GitHub 托管 runner；它不会把 Node、Python、Rust、Docker CLI 或 SBOM 工具安装到客户的运行目录，也不会改变 SQLite/PostgreSQL 数据路径。

## 4. 手工运行与失败排查

### 手工运行流程

1. 打开 GitHub 仓库的 **Actions**，选择目标工作流，点击 **Run workflow**，确认分支为待验证的提交。
2. 重型容量/发布任务先用默认或较小输入跑一次；PostgreSQL 的 `1000000` 档、容器多架构镜像和正式 Release 不要与普通代码提交同时盲目重试。
3. 打包入口先使用 `publish_release=false`，从 Artifact 下载并在目标平台人工检查启动、字体、PDF、路径和权限；确认后再用同一版本运行 `publish_release=true`。
4. 失败时先查看失败步骤的标准输出，再下载对应 Artifact。字体/PDF 看 `cross-platform-report-*`；容器看 `container-runtime-lifecycle`；依赖看 `dependency-governance`。不要把 PostgreSQL 数据卷或私钥作为排障附件上传。
5. 修复后优先 **Re-run failed jobs**；如果输入、版本或工作流已变更，重新运行整个 workflow，并记录运行 ID、提交 SHA、输入档位和结论。

### 失败现象与处理方向

| 现象 | 首先检查 |
| --- | --- |
| Action 被提示 Node 旧版或 Artifact 弃用 | `scripts/verify-github-workflow-actions.mjs`、所有 `uses:` 主版本和 `setup-node` 的 `node-version` |
| Linux 字体/PDF 失败或长时间无输出 | Linux 系统库、Chrome 启动日志、具体 scale profile；确认单步 5 分钟上限是否触发 |
| `/readyz` unhealthy 或容器启动很慢 | API health JSON、`compose.log`、镜像构建与启动是否分开超时；匿名 `/healthz` 应保持轻量，不应扫描浏览器/OCR/PostgreSQL 工具或 PostgreSQL 数据目录 |
| API 端口边界检查误报 | 先看 `compose-ps.txt` 的 `PORTS` 列；只有出现 `0.0.0.0:5188->5188` 或实际 host binding 才算发布，单独的 `5188/tcp` 是 Compose `expose`，不是宿主映射 |
| PostgreSQL 测试很慢 | 输入容量、索引计划、runner 资源和测试 45 分钟上限；不要把未连接真实 PG 的本机测试当成容量结论 |
| GHCR push denied | 仓库 Actions 权限、`packages: write`、镜像命名空间和组织包策略 |
| 多架构 API 镜像出现 `No space left on device` | 查看 API job 的 runner 空间回收和 `df -h /`；确认 Dockerfile 使用按架构 NuGet/Cargo cache mount、只复制 `/out` 最终二进制，且 API `cache-to` 为 `mode=min` |
| Linux/macOS 打包提示 `THIRD_PARTY_NOTICES.md is stale` | 查看错误中的首个差异行；版本行不再参与清单比较，若仍失败就是依赖、许可证、作用域或随包 notice 的真实变化，应运行 `node scripts/generate-dependency-governance.mjs artifacts/dependency-governance --release --write-repository` 并审查差异 |
| 正式桌面包签名失败 | endpoint/public key Variables、带密码私钥两个 Secrets、版本 tag 和签名文件；不要把私钥贴到日志 |
| 浏览器服务器包缺文件 | `verify-package-payload.ps1`、Chrome/Chromium 架构、Linux 执行权限、OCR 模型和 `wwwroot/index.html` |
| 工作流没有自动启动 | 检查该工作流的 `paths` 过滤器；手工入口用 `workflow_dispatch`，可复用工作流必须由平台入口调用 |

### 2026-07-27 三类失败的已修复口径

- PostgreSQL 日志出现 `Cannot write DateTime with Kind=Unspecified to PostgreSQL type 'timestamp with time zone'` 时，先检查查询边界和 `AppDbContext` 保存规范化是否仍统一转为 UTC；不要通过 Npgsql 旧兼容开关掩盖 DateTime 契约。当前仪表盘月份边界、单一窗口心跳和 PostgreSQL 保存路径已按该口径收口。
- Linux/API 测试出现“取消请求返回 false，但任务已经 Canceled”时，检查取消实现是否先原子更新为 `Canceling`、再触发 `CancellationTokenSource.Cancel()`，并在 CAS 失败时重新读取最新状态；不能先发取消信号再用旧快照覆盖状态。
- 容器在镜像构建成功后长期卡在健康检查时，先分别看 `/readyz` 与匿名 `/healthz`。两者都在完整路由表之前走早期探针；`/readyz` 只表示进程就绪，匿名 `/healthz` 返回版本、状态和数据库模式，只有带管理员 Bearer 或可信桌面 token 的请求才执行完整运行路径/依赖扫描。该边界同时避免可选浏览器、OCR、PostgreSQL 工具发现和大规模 Minimal API 路由冷启动拖慢容器存活检查。
- 上述修复应只运行相关 API/领域集成测试、Web 类型/构建与工作流静态门禁；除非改动扩展到公共基础设施，不要求为了同一故障重复执行全仓库测试。推送后的 GitHub runner 结果仍是跨平台/真实 PostgreSQL/Docker 的最终证据。

## 5. 运行边界与审查记录

- GitHub Actions 是一次性构建/验证环境，不是公司内网服务器、数据库托管或在线用户访问入口。生产部署仍需由管理员准备 Docker Engine 或浏览器服务器包、PostgreSQL、运行目录、备份和防火墙。
- 当前正式多人拓扑仍是“一套 PostgreSQL + 一个 API 实例 + 多个浏览器用户”；Redis 尚未作为强依赖引入。工作流验证的是这条边界，不会因为 Artifact 或 GHCR 存在就自动获得多 API 副本能力。
- 容器基础 Compose 支持内网 HTTP；HTTPS 是可选 overlay。Compose 中的 Nginx 负责静态文件和同源代理，非 Docker 浏览器服务器包则由 ASP.NET Core 单进程托管 Web/API，不需要 Nginx。详细部署选择见 [`deploy/container/README.md`](../deploy/container/README.md)。
- 工作流成功只代表对应的代码/runner 契约通过。公网证书、公司内网 ACL、真实浏览器、触屏/读屏、备份介质、签名密钥、公证和长期运行仍需目标环境验收，并应写入 [`docs/程序改进重构进度文档.md`](./程序改进重构进度文档.md)。
