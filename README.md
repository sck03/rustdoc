# ExportDocManager

ExportDocManager 是面向外贸单证、客户与供应商协作、报表、OCR、Excel 和单一窗口资料管理的跨平台应用。项目同时提供桌面端、非 Docker 浏览器服务器版和容器版，运行数据与第三方工具优先保存在应用运行目录或管理员指定的数据盘，不依赖系统盘用户目录。

## 交付形态

| 形态 | 适用场景 | 数据库 | 安装入口 |
| --- | --- | --- | --- |
| Windows / macOS / Linux 桌面端 | 单机办公、离线资料处理 | SQLite | GitHub Release 桌面安装包 |
| 浏览器服务器版 | 已有 PostgreSQL、无需 Docker 的办公网服务器 | PostgreSQL 18 | Windows 双击 `setup-windows.cmd`；Linux 运行 `initialize-linux.sh` |
| 容器版 | 团队部署、VPS、自动 HTTPS | PostgreSQL 18 | `deploy/container/install-container.sh` |

浏览器服务器版说明见 [deploy/browser-server/README.md](deploy/browser-server/README.md)，容器部署与运维见 [deploy/container/README.md](deploy/container/README.md)。完整架构、重构进度和路径审查分别见：

- [docs/产品架构与文档总览.md](docs/产品架构与文档总览.md)
- [docs/程序改进重构进度文档.md](docs/程序改进重构进度文档.md)
- [docs/运行目录与路径存储审查清单.md](docs/运行目录与路径存储审查清单.md)

## 一键容器安装

可信办公网/VPN 的 HTTP 部署：

```bash
curl -fsSL https://raw.githubusercontent.com/sck03/rustdoc/main/deploy/container/install-container.sh |
  sudo bash -s -- --mode http --tag 精确版本号
```

公网 HTTPS 部署：

```bash
curl -fsSL https://raw.githubusercontent.com/sck03/rustdoc/main/deploy/container/install-container.sh |
  sudo bash -s -- \
    --mode https \
    --domain docs.example.com \
    --email ops@example.com \
    --tag 精确版本号
```

安装器拒绝 `latest`，保留数据库、配置、证书和 DataRoot，并拒绝把安装根或运行根放在符号链接/文件系统根上。升级资产先进入 staging 并完成 HTTP/HTTPS Compose 校验；拉取、证书、启动或就绪失败会自动恢复旧 `.env`、旧部署文件和本次可能改写的 Let's Encrypt 状态，再强制重建原容器以重新挂载恢复后的证书和配置。

## 网页备份、恢复和完整迁移

管理员可在“系统设置 → 维护 → 团队库”完成：

- 后台创建并校验 PostgreSQL custom-format `.dump` 备份；
- 使用短期票据和 HTTP Range 流式下载大型备份，不占用整包浏览器内存；票据消费时仍会重新检查安全通道、受管目录边界和符号链接；
- 上传或选择服务器备份，重新认证后安排数据库恢复；
- 创建加密 `.edmmigration` 完整迁移包，或在新服务器上传恢复；
- 创建脱敏技术支持包，后台生成后流式下载。

纯 HTTP 灾难恢复只允许在明确配置的可信办公网/VPN 使用；公网和不可信网络必须使用 HTTPS。完整迁移不包含许可证、机器绑定状态、日志缓存和 TLS/Certbot 证书，这些部署层内容需单独处理。

## 运行目录原则

- 程序资源、内置浏览器、OCR 模型和 PostgreSQL 客户端随安装包或镜像交付。
- SQLite、PostgreSQL 业务文件、配置、日志、缓存、备份、模板和本地主密钥写入 DataRoot。
- 浏览器服务器初始化脚本拒绝磁盘根、文件系统根和符号链接数据根。
- 容器默认使用安装目录下的 `runtime/` bind mount；可在首次部署时改到独立数据盘。
- 不把数据库密码、主密钥、Release 私钥或运行备份提交到仓库。

## 开发与验证

主要技术栈为 .NET、React/TypeScript、Tauri/Rust、PostgreSQL、SQLite、Nginx 和 Playwright。常用入口：

```powershell
dotnet build ExportDocManager.sln
dotnet test ExportDocManager.sln --no-build

cd apps/export-doc-web
npm ci
npm run build
```

API 客户端由项目 OpenAPI 文档生成：

```powershell
pwsh -File .\scripts\generate-api-client.ps1
```

仓库还包含视觉、缩放、无障碍、跨浏览器、Tauri、OCR、Excel、依赖许可、软件物料清单和发布包内容门禁。生产交付前仍需在目标 Windows/macOS/Linux、真实 Docker/PostgreSQL/Nginx 和实际网络环境完成恢复演练与人工验收。

## 签名状态

当前阶段不启用 Windows 系统级代码签名、macOS Developer ID 签名或公证。相关发布边界会继续保留，但正式对外商用分发前应由发行主体补齐证书、签名、公证和信誉积累流程。
