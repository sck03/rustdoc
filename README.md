# ExportDocManager

ExportDocManager 是面向外贸单证、销售协作和单一窗口业务的跨平台管理软件。桌面端、浏览器服务器版和容器版共用 ASP.NET Core API、React Web 界面以及同一套领域与数据访问规则。

当前项目使用 .NET 8 LTS，桌面壳采用 Tauri 2，前端采用 React 18，单机数据使用 SQLite，团队部署使用 PostgreSQL 18。运行数据库、配置、日志、缓存、备份、用户模板和业务文件统一放在显式选择的 `DataRoot`，不会默认散落到系统盘用户目录。

## 产品形态

| 形态 | 适用场景 | 数据库 |
| --- | --- | --- |
| Windows / Linux / macOS 桌面版 | 单机或本地操作员 | SQLite |
| Windows / Linux 浏览器服务器版 | 办公室局域网、无需 Docker 的服务器 | PostgreSQL 18 |
| Docker 容器版 | Linux 服务器、内网或 HTTPS 域名部署 | PostgreSQL 18 |

主要能力包括发票与单据管理、客户与供应商资料、付款报销、报表模板、PDF/Excel 工具、PP-OCRv6、本地 HS 编码知识库、单一窗口交接、数据库备份恢复、完整服务器迁移、审计日志和多用户权限。

## 快速开始

### 本地开发

环境要求：.NET 8 SDK、Node.js、Rust stable 和 PowerShell 7。

```powershell
dotnet restore ExportDocManager.sln
npm --prefix apps/export-doc-web ci
dotnet build ExportDocManager.sln -c Release
npm --prefix apps/export-doc-web run build
```

启动 Web 开发服务器：

```powershell
npm --prefix apps/export-doc-web run dev
```

桌面开发和打包命令见 [Tauri 应用说明](apps/export-doc-tauri/README.md) 与 [GitHub Actions 工作流手册](docs/GitHub%20Actions工作流用途与运行手册.md)。

### Docker 一键部署

Linux 服务器可使用：

```bash
sudo bash deploy/container/install-container.sh
```

安装器支持内网 HTTP 和公网 HTTPS 模式，部署资源会先在 staging 中校验，失败时恢复原配置。数据库、备份和运行数据位置由安装参数或 `EXPORTDOCMANAGER_RUNTIME_ROOT` 明确指定。

已有部署文件也可以按 [容器部署说明](deploy/container/README.md) 手工启动。

### 浏览器服务器包

无需 Docker 时，可在 GitHub Actions 中运行：

- `Build Windows browser server package`
- `Build Linux browser server package`

生成包包含 Web、ASP.NET Core API、Chrome Headless Shell、Rust OCR、Excel analyzer、PostgreSQL 客户端和初始化脚本。目标服务器仍需可访问的 PostgreSQL 18 实例。

## 数据与迁移安全

- DataRoot 在创建任何运行文件前检查绝对路径、文件系统根、符号链接和 Windows 重解析点。
- 数据目录迁移通过逐文件 SHA-256 树清单验证，验证完成后才原子切换并清理旧目录。
- 浏览器版提供 SQLite/PostgreSQL 备份、恢复、WebDAV 备份取回和加密完整迁移包。
- 服务器迁移不复制许可证、设备绑定、日志缓存或 TLS 私钥；部署证书应在目标服务器重新签发。
- 系统级 Windows/macOS 代码签名和 Apple 公证当前暂不执行；这不影响未签名安装包的构建与内部验收。

## 质量检查

常用检查命令：

```powershell
dotnet test ExportDocManager.sln -c Release
dotnet build ExportDocManager.sln -c Release -warnaserror
npm --prefix apps/export-doc-web run build
npm --prefix apps/export-doc-web run test:source-size-governance
npm --prefix apps/export-doc-web run test:style-governance
cargo check --manifest-path apps/export-doc-tauri/src-tauri/Cargo.toml --locked
pwsh -NoProfile -File scripts/verify-script-suite.ps1
```

GitHub Actions 还覆盖容器生命周期、PostgreSQL 恢复、浏览器兼容、跨平台字体、桌面包、浏览器服务器包、依赖安全和 SBOM。

## 架构与文档

- [当前架构事实](docs/当前架构事实.md)：当前部署、路径、数据库和错误契约的事实源。
- [产品架构与文档总览](docs/产品架构与文档总览.md)：架构背景和完整文档索引。
- [程序改进重构进度](docs/程序改进重构进度文档.md)：实现记录和验证证据。
- [运行目录与路径存储审查清单](docs/运行目录与路径存储审查清单.md)：系统盘、DataRoot 和外部依赖边界。
- [多平台与多架构支持矩阵](docs/多平台与多架构支持矩阵.md)：已验证、契约验证和待真机验证的平台范围。

## 第三方组件

项目优先使用免费、可审计的开源依赖。依赖清单、许可证和随包分发要求见 [THIRD_PARTY_DEPENDENCIES.md](THIRD_PARTY_DEPENDENCIES.md) 与 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。公开仓库边界和私钥排除规则见 [GitHub 开源发布与 Docker 镜像说明](docs/GitHub开源发布与Docker镜像说明.md)。
