# GitHub 开源发布与 Docker 镜像说明

> 更新日期：2026-07-17

## 1. 公开仓库边界

公开仓库只包含主程序、API、Web、Tauri 客户端、公钥验签代码、测试、部署文件和文档。以下内容必须留在本机或独立私有仓库：

- `apps/license-keygen-tauri/` 注册机源码；
- 签发私钥、证书私钥、`.pem/.key/.p8/.p12/.pfx/.snk`；
- `KEY/ExportDocLicenseKeyGen.exe`、注册机 WebView2 依赖和任何注册机发布包；
- `.env`、容器真实数据库配置、运行数据库、日志、缓存和客户数据。

主程序只公开 ECDSA 公钥验签，不公开签发方法。Windows 客户程序默认构建不再生成注册机；只有本机恢复私有源码后显式使用 `-IncludeLicenseKeygen` 才构建内部工具。

## 2. 初始化与推送

先在 GitHub 网页创建一个空仓库，不要勾选自动生成 README、License 或 `.gitignore`。然后在项目根运行：

```powershell
pwsh -NoProfile -File scripts/github/initialize-github-repository.ps1 `
  -RemoteUrl https://github.com/你的账号/你的仓库.git `
  -CreateCommit
```

确认 `git status` 和暂存文件后，再推送：

```powershell
pwsh -NoProfile -File scripts/github/initialize-github-repository.ps1 `
  -RemoteUrl https://github.com/你的账号/你的仓库.git `
  -CreateCommit `
  -Push
```

如果使用 SSH，把地址改为 `git@github.com:你的账号/你的仓库.git`。脚本在暂存前后都会执行公开源码守卫；发现注册机路径、私钥标记、注册生成 API 或超过 95 MiB 的文件时停止。

## 3. GitHub Actions 与 GHCR

- `public-source-guard.yml`：每次 push/PR 检查公开边界。
- `cross-platform-validation.yml`：Windows、Linux、macOS 的 .NET/Web/Tauri 路径与构建契约。
- `container-images.yml`：构建 `linux/amd64`、`linux/arm64` 的 API/Web 镜像，并在非 PR 事件发布到 GHCR。

镜像名称为：

```text
ghcr.io/<github-owner>/export-doc-manager-api:<tag>
ghcr.io/<github-owner>/export-doc-manager-web:<tag>
```

仓库第一次发布后，在 GitHub 的 Packages 页面把需要公开拉取的镜像可见性设置为 Public。GitHub Actions 和 GHCR 可以免费构建/保存公开项目镜像，但 GitHub Pages 只能托管静态文件，不能运行 ASP.NET Core API 和 PostgreSQL，因此不能替代完整 Docker 服务器。

## 4. 使用 GHCR 镜像部署

复制 `.env.example` 为 `.env`，再增加：

```dotenv
EXPORTDOCMANAGER_IMAGE_NAMESPACE=ghcr.io/你的github账号
EXPORTDOCMANAGER_IMAGE_TAG=latest
```

初始化运行目录并启动：

```powershell
pwsh -NoProfile -File deploy/container/initialize-container-runtime.ps1
docker compose -f deploy/container/docker-compose.ghcr.yml --env-file deploy/container/.env up -d
```

数据库、配置、日志、缓存和导出任务全部位于 `EXPORTDOCMANAGER_RUNTIME_ROOT` 指定目录，默认是 `deploy/container/runtime/`，不会写入源码仓库，也不依赖系统 C 盘用户目录。

## 5. 是否可从 GitHub 网页直接上传

可以在仓库网页使用 “Add file → Upload files”，但不建议用于本项目首次上传：网页上传适合少量文件，单文件受网页上传大小限制，目录多时也很难可靠审查忽略规则。项目包含大量源码、模型和跨平台文件，优先使用本页脚本、Git 命令行、GitHub Desktop 或 VS Code Source Control；这样 `.gitignore` 和敏感文件守卫才会完整生效。
