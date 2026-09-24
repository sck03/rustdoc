# GitHub Actions 工作流用途与运行手册

> 2026-09-25。Rust 发布按原 C# 项目的独立产品入口组织，共用 Rust 构建逻辑；不运行 dotnet restore/publish。

## 发布入口

| Actions 名称 / 文件 | 系统与架构 | 结果 |
| --- | --- | --- |
| [Build Windows desktop package](../.github/workflows/windows-desktop-package.yml) | x64、arm64、all | Full 便携 ZIP、NSIS 安装器归档 |
| [Build Linux desktop package](../.github/workflows/linux-desktop-package.yml) | x64、arm64、all | Full 便携 tar.gz、deb/AppImage 归档 |
| [Build macOS desktop package](../.github/workflows/macos-desktop-package.yml) | arm64（Apple Silicon） | Full 便携 tar.gz、app/dmg 归档 |
| [Build Web server package](../.github/workflows/rust-native-web-server-release.yml) | 选择 linux/windows/macos/all，再选择 x64/arm64/all | React + Rust HTTP + PostgreSQL 18 客户端的服务包 |
| [Build and publish Docker image](../.github/workflows/rust-native-container-release.yml) | x64（amd64）、arm64、all | 可下载镜像或 GHCR 版本索引 |

Windows 桌面两架构使用各自 MSVC runner；Linux 两架构分别使用 Ubuntu 24.04 x64/ARM64 runner；macOS 使用 Apple Silicon runner。按用户确认继续停用 Intel macOS。Windows 网页服务只开放 x64，因为当前受管 PostgreSQL 客户端仅提供 Windows x64；macOS 网页服务仅 ARM64。选择不存在的组合在构建前报错；all 只包含已定义的目标。

桌面和 Web 共用 [native-package-reusable.yml](../.github/workflows/native-package-reusable.yml)，它仅接受内部调用，不显示另一个手工打包入口。所有包默认 Full 并包含 OCR、PDFium、字体、模板及许可。

## 版本号与下载

1. 在 Actions 选择上表工作流，点击 Run workflow，选择要构建的源码分支。
2. 填写 version，例如 `0.1.2`、`v0.1.2` 或 `0.1.2-beta.1`；选择目标系统/架构。
3. 仅下载构建产物时保持发布开关关闭。运行成功后从该次 Actions 页面下载 Artifacts。
4. 桌面/Web 选择 `publish_release`，所有所选目标成功后上传至 `v<version>` 的 GitHub Release。首次创建先使用 draft，附件上传完成后公开；预发布版本标记 prerelease。此流程不更新 latest 发布指针或自动更新通道。

版本统一进入 version.json、Rust workspace、Tauri、React/npm 锁文件、独立 OCR/Excel 工具和包内标记。归档名称形如 `exportdoc-desktop-0.1.2-windows-x64.zip`、`exportdoc-web-0.1.2-linux-arm64.tar.gz`，每份附 SHA-256。CI 不自动提交源码版本；本地用 `node scripts/sync-version.mjs 0.1.2` 同步。

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
| [Rust native delivery validation](../.github/workflows/rust-native-validation.yml) | Rust 契约、测试、依赖边界、对应平台构建与 Docker 生命周期 |
| [Dependency security and SBOM governance](../.github/workflows/dependency-governance.yml) | npm/Cargo 安全、许可、notices 和 SBOM |
| [浏览器兼容](../.github/workflows/browser-compatibility.yml) | 仅手工 Firefox/WebKit，不加入普通提交检查 |

先查看第一个失败步骤及完整日志。依赖安全错误、下载故障、工具链和平台编译问题分别处理；取消、ignored、静态语法通过不计为运行验收。

2026-09-25 核对 GitHub run 36044362655：`Audit Rust lock files` 被 glib 0.18.5 的 [RUSTSEC-2024-0429](https://rustsec.org/advisories/RUSTSEC-2024-0429) 阻断，SBOM 未生成是连带结果。Tauri 2.11.6 的 GTK3 图使用该版本；修复版本 glib >=0.20 与此依赖图不兼容，0.18 分支没有发布修复。受影响 API 为 VariantStrIter/array_iter_str，当前项目及其它锁定依赖源码没有引用它们。`verify-rustsec-glib-exception.mjs` 为单项风险例外准备了保守检查：固定 Tauri/glib 版本，任何其它依赖或项目出现受影响符号即失败。源码检查不等于修复或形式化不可达证明；例外是否启用必须记录本次决定，不能全局关闭 unsound/yanked 门禁。

## 签名与验收边界

GitHub Release 附件供人工下载安装，不启用 Tauri updater 通道。系统级 Authenticode、Developer ID 和 Apple 公证不执行；Tauri 的签名/固定公钥校验保留，正式升级须另按[更新合同](./Tauri正式更新签名与发布配置.md)验收。

只有对应 OS/架构成功运行的 GitHub 结果才作为该项证据；本机 GNU 构建不替代 Windows MSVC，编译不替代 Linux/macOS/ARM64 真机、输入法、PDF/打印与更新验收。
