# ExportDocManager

ExportDocManager 是面向外贸单证、销售协作和团队权限管理的跨平台应用。当前主线由 ASP.NET Core 8 API、React/Vite Web、Tauri 桌面壳、SQLite/PostgreSQL 和 Docker 组成。

## 主要形态

- Windows 桌面便携版/安装版：Document、Sales、Full 三种产品版本。
- 浏览器与局域网版：Full 产品能力，按账号岗位与权限模板授权。
- Docker 版：PostgreSQL + API + Nginx Web，运行数据统一挂载到部署目录。

## 本地验证

```powershell
pwsh -NoProfile -File scripts/run-tests.ps1 -NoPause
npm --prefix apps/export-doc-web run build
```

## Docker

本地源码构建说明见 [deploy/container/README.md](deploy/container/README.md)。在 GitHub Actions 中手工运行 `container-images.yml`、填写发布版本后，会构建多架构 API/Web 镜像并发布到 GitHub Container Registry（GHCR）。GitHub 负责构建和保存镜像，不负责长期免费运行数据库与容器；实际运行仍需要自己的 Docker 主机或第三方容器平台。

## 安全边界

离线注册码签发工具、签发私钥、`KEY/` 内部产物和注册机源码不属于公开仓库。公开代码只保留 ECDSA 公钥验签能力。上传前运行：

```powershell
pwsh -NoProfile -File scripts/github/verify-public-source.ps1
```

GitHub 初始化、提交和推送说明见 [GitHub 开源发布与 Docker 镜像说明](docs/GitHub开源发布与Docker镜像说明.md)。
