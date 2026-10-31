# GitHub Actions 工作流用途与运行手册

> 2026-09-25。Rust 发布按原 C# 项目的独立产品入口组织，共用 Rust 构建逻辑；不运行 dotnet restore/publish。

## 发布入口

| Actions 名称 / 文件 | 系统与架构 | 结果 |
| --- | --- | --- |
| [Build Windows desktop package](../.github/workflows/windows-desktop-package.yml) | x64、arm64、all | 所选版本便携 ZIP、NSIS 安装器归档 |
| [Build Linux desktop package](../.github/workflows/linux-desktop-package.yml) | x64、arm64、all | 所选版本便携 tar.gz、deb/AppImage 归档 |
| [Build macOS desktop package](../.github/workflows/macos-desktop-package.yml) | arm64（Apple Silicon） | 所选版本便携 tar.gz、app/dmg 归档 |
| [Build Web server package](../.github/workflows/rust-native-web-server-release.yml) | 选择 linux/windows/macos/all，再选择 x64/arm64/all | React + Rust HTTP + PostgreSQL 18 客户端的服务包 |
| [Build and publish Docker image](../.github/workflows/rust-native-container-release.yml) | x64（amd64）、arm64、all | 可下载镜像或 GHCR 版本索引 |

Windows 桌面两架构使用各自 MSVC runner；Linux 两架构分别使用 Ubuntu 24.04 x64/ARM64 runner；macOS 使用 Apple Silicon runner。按用户确认继续停用 Intel macOS。Windows 网页服务只开放 x64，因为当前受管 PostgreSQL 客户端仅提供 Windows x64；macOS 网页服务仅 ARM64。选择不存在的组合在构建前报错；all 只包含已定义的目标。

桌面和 Web 共用 [native-package-reusable.yml](../.github/workflows/native-package-reusable.yml)，它仅接受内部调用，不显示另一个手工打包入口。桌面入口默认 edition=All，只生成 Document、Sales 两版，也可单选；内部省略版本时默认 Document。Web/Docker 保持 Full 完整功能，行政人事及团队协作集中于多用户部署。Document 包含 OCR 和单证模板，Sales 不附这些资源；两版均保留 PDFium、字体及许可。桌面归档在平台后追加版本名，Full/Administration 桌面构建参数拒绝，旧发布资产不自动转换。

## 版本号与下载

1. 在 Actions 选择上表工作流，点击 Run workflow，选择要构建的源码分支。
2. 填写 version，例如 `0.1.2`、`v0.1.2` 或 `0.1.2-beta.1`；选择目标系统/架构；桌面同时选择 edition。
3. 仅下载构建产物时保持发布开关关闭。运行成功后从该次 Actions 页面下载 Artifacts。
4. 桌面/Web 选择 `publish_release`，所有所选目标成功后上传至 `v<version>` 的 GitHub Release。首次创建先使用 draft，附件上传完成后公开；预发布版本标记 prerelease。此流程不更新 latest 发布指针或自动更新通道。

版本统一进入 version.json、Rust workspace、Tauri、React/npm 锁文件、独立 OCR/Excel 工具和包内标记。归档名称形如 `exportdoc-desktop-0.1.2-windows-x64-Document.zip`、`exportdoc-web-0.1.2-linux-arm64.tar.gz`，每份附 SHA-256。CI 不自动提交源码版本；本地构建入口可直接指定 `-Version`，同步修改当前工作树的版本文件，详见[构建版本参数](../scripts/README.md#构建时指定程序版本)。来源提交另写入编译期构建标识，容器工作流通过 build-arg 传入，不影响发布号与签名更新比较。

同一版本的其它系统包可随后上传，但必须选择同一源码提交。已有标签指向另一提交或同名附件内容不同会明确失败，不覆盖旧包。重复上传相同字节会复用已有附件；重新构建若产生不同字节，应使用新版本。Unix 先 tar.gz 再上传，以保留执行位及 .app 链接结构。

## Docker / GHCR

`publish=false`：构建并验证同一镜像，上传可用 `docker load -i <文件>` 导入的 tar.gz。

`publish=true`：生命周期、真实 PostgreSQL、文件任务与重启持久化通过后，将同一个本地镜像推至 GHCR 临时唯一标签。所有所选架构通过后按 digest 合并 `ghcr.io/<owner>/exportdoc-rust-native:<version>`，并保存 container-release.json。仓库 owner 自动转小写。

`publish_latest=true`：仅允许 publish=true、architecture=all 的稳定版，更新 latest。预发布或单架构运行不会截断完整的 latest；版本索引存在且内容不同时拒绝覆盖。需要从单架构增加到双架构时使用新版本。

发布使用该次工作流的 GITHUB_TOKEN 和 packages:write。需要公开下载时由包所有者在 GitHub Packages 中设置 Public。部署命令及私有配置边界见[镜像说明](./GitHub开源发布与Docker镜像说明.md)。

## 检查与失败定位

| 工作流 | 范围 |
| --- | --- |
| [Release script validation](../.github/workflows/release-script-validation.yml) | 发布相关 push/PR/手工触发；版本、架构、发布冲突回归，脚本语法与 Actionlint |
| [Rust native delivery validation](../.github/workflows/rust-native-validation.yml) | 自动按改动范围执行 Rust/前端；手动 full 完整验证平台、服务包与容器 |
| [Dependency security and SBOM governance](../.github/workflows/dependency-governance.yml) | npm/Cargo 安全、许可、notices 和 SBOM |
| [浏览器兼容](../.github/workflows/browser-compatibility.yml) | 仅手工 Firefox/WebKit，不加入普通提交检查 |

先查看第一个失败步骤及完整日志。依赖安全错误、下载故障、工具链和平台编译问题分别处理；取消、ignored、静态语法通过不计为运行验收。

2026-10-30 调整日常校验范围：纯文档提交不会启动 Rust 构建，由发布脚本工作流校验文档链接；仅 React 改动执行前端构建、无障碍、缩放、视觉与编辑/取消回归。Rust 改动执行后端工作区测试和生成契约；Tauri 宿主变动另跑宿主测试，共用 Rust 变动检查桌面组合根。涉及人员/OA 的 Rust 改动另验真实 HTTP、资源附件、审批单及页面操作。编译缓存按锁文件、工具链和清单固定，缓存前清理已完成的测试 EXE，保留库和构建输入；仍使用 locked 命令及 Cargo 的源码校验，不把缓存恢复当作通过。

从本工作流 Run workflow 选择 `validation_mode=full`，才执行完整工作区、全部特性及 [内部平台验收组合](../.github/workflows/native-full-validation.yml)中的五种桌面、四种服务包和两种容器目标。full 组合只接受 workflow_call，没有第二个用户入口；发布工作流及其实际验收保持。日常前端和 Rust 分开并行，独立脚本/依赖门禁继续保留。

调整依据为 [2026-10-29 成功运行](https://github.com/sck03/rustdoc/actions/runs/40892157181)：核心任务 36 分 41 秒，工作区测试含编译 24 分 54 秒、全部特性检查 5 分 18 秒，缩放/视觉合计约 4 分钟。此前只有 Cargo 下载缓存，没有编译产物缓存。该数字属于旧配置；新配置的实际耗时须按之后的云端回执记录，首次冷缓存或锁图变化仍可能较长。

2026-10-25 复核 [run 40121278583](https://github.com/sck03/rustdoc/actions/runs/40121278583/job/120953136194)：主锁图的 `ctutils 0.4.3` 命中新公告 `RUSTSEC-2026-0385`，涉及非常量时间掩码/比较；OCR/Excel 图未命中。独立升级到 `0.4.4` 并重生成 notices，原 MIT/Apache-2.0 许可及既有 GLib 例外保持。审计步骤保留原 JSON 报告并输出依赖、公告和修复版本摘要，不用忽略公告消除失败。主工作区库存标为 `rust-workspace`，与桌面实际运行图分别检查。

2026-10-30 起，日常 Rust 验证使用 `cargo test --locked --workspace --exclude export-doc-tauri`，宿主按变更单独测试；手动 full 使用完整工作区及全部特性，独立 OCR 工程继续分别执行。原生库清单、字体、模板、工具链和相关准备脚本加入对应 paths 过滤，避免这些构建输入单独变化时遗漏门禁；Firefox/WebKit 仍仅手工执行。

同日 [run 40167194980](https://github.com/sck03/rustdoc/actions/runs/40167194980/job/121090563126) 的新增嵌套 Git 保护回归在 Linux 清理隐藏 `.git` 夹具时失败。清理精确的自建夹具使用 `-Force`，Windows 也将该夹具标为 Hidden 以覆盖同一边界；正式包的 Git 保护规则不放宽。

2026-10-08 复核 [run 37550212861](https://github.com/sck03/rustdoc/actions/runs/37550212861)：发布合同中的清理测试在 Ubuntu 删除测试用 .git 文件时报“隐藏/只读项”。Linux 将点文件标为 Hidden，测试释放自己创建的单个文件时需要 -Force；修正测试清理，并在 Windows 显式设置同一隐藏属性复现和回归。正式清理器的 Git/数据保护保持不变，不能为让测试通过而允许删除真实仓库。

同日复核 [run 37512895890](https://github.com/sck03/rustdoc/actions/runs/37512895890) 的 rustsec-workspace.json：yoke-derive 0.8.3 被撤回，触发 --deny yanked；OCR/Excel 图没有该阻断。独立定向升级为未撤回的 0.8.4（Unicode-3.0）并同步锁文件/notices，三份锁图按原审计参数通过。GLib 既有单项例外及源码前置检查保留，不新增豁免。

2026-09-29 核对 [run 36503387531](https://github.com/sck03/rustdoc/actions/runs/36503387531)：脚本套件输出 `Success: true`，但 GitHub PowerShell 包装器仍返回 1。原因是缺少构建产物的反向测试留下预期的 `$LASTEXITCODE=1`；后续检查通过未清除它。套件改为通过共用进程执行器隔离 PowerShell 回归，检查子进程真实结果，全部通过后显式返回 0。发布合同回归使用 Actions 相同的调用及退出码传播方式，验证成功和真实语法失败；不放宽文件保护、依赖、权限或发布检查。

2026-09-25 核对 GitHub run 36044362655：`Audit Rust lock files` 被 glib 0.18.5 的 [RUSTSEC-2024-0429](https://rustsec.org/advisories/RUSTSEC-2024-0429) 阻断，SBOM 未生成是连带结果。Tauri 2.11.6 的 GTK3 图使用该版本；修复版本 glib >=0.20 与此依赖图不兼容，0.18 分支没有发布修复。受影响 API 为 VariantStrIter/array_iter_str，当前项目及其它锁定依赖源码没有引用它们。`verify-rustsec-glib-exception.mjs` 为单项风险例外准备了保守检查：固定 Tauri/glib 版本，任何其它依赖或项目出现受影响符号即失败。源码检查不等于修复或形式化不可达证明；例外是否启用必须记录本次决定，不能全局关闭 unsound/yanked 门禁。

2026-10-23 升级重新评估：Tauri `2.12.5` 的 Linux GTK3 图仍锁定 glib `0.18.5`；扫描新锁图 21,298 个 Rust 源文件，项目与其它依赖仍未引用受影响 API。继续保留同一项既有例外，没有新增豁免；主图、OCR 与 Excel 的 unsound/yanked 审计按原参数通过。这不是上游修复，也不替代 Linux 实际运行验收。

## 签名与验收边界

2026-09-25 修复 run `36081725495` 的相邻仓库链接依赖：证据链接改用本仓库保留的 C# 对照源码，干净 checkout 即可校验。修复 run `36081725496` 时启用上述单项 GLib 例外：先验证固定版本和所有锁定依赖源码，再仅对主锁图传入 `--ignore RUSTSEC-2024-0429`；OCR/Excel 审计不豁免，`unsound`/`yanked` 及其它公告继续阻断。本机扫描 22,212 个 Rust 文件通过；这不表示上游漏洞已修复，Tauri/GLib 升级时必须移除或重新评估例外。

GitHub Release 附件供人工下载安装，不启用 Tauri updater 通道。系统级 Authenticode、Developer ID 和 Apple 公证不执行；Tauri 的签名/固定公钥校验保留，正式升级须另按[更新合同](./Tauri正式更新签名与发布配置.md)验收。

只有对应 OS/架构成功运行的 GitHub 结果才作为该项证据；本机 GNU 构建不替代 Windows MSVC，编译不替代 Linux/macOS/ARM64 真机、输入法、PDF/打印与更新验收。
