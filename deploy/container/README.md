# Full 局域网 / 容器版

该部署只发布 `Full` 产品，并通过 PostgreSQL 中的账号岗位、权限模板和数据归属控制实际能力。Web 与 API 使用同一域名反向代理，API 不直接映射到宿主机端口。Compose 为 Nginx 分配固定容器地址，API 只信任该明确地址提供的 `X-Forwarded-For` / `X-Forwarded-Proto`，因此多用户登录限流按真实客户端地址区分，同时不会无条件信任外部可伪造的转发头。

## 初始化

在本目录执行：

```powershell
pwsh -File .\initialize-container-runtime.ps1 `
  -PostgreSqlPassword "请替换为长随机数据库密码" `
  -BootstrapToken "请替换为另一段至少24位的随机首次部署令牌"
docker compose up -d --build
```

初始化脚本同时写入默认 HTTPS 端口和证书挂载路径，但普通 `docker-compose.yml` 不会读取证书，也不会因为证书尚未准备而阻止局域网 HTTP 部署。正式启用内置 TLS overlay 前，把可信证书和私钥放到 `.env` 指定的宿主路径，再执行：

```powershell
docker compose `
  -f .\docker-compose.yml `
  -f .\docker-compose.https.yml `
  --env-file .\.env `
  up -d --build
```

默认同时保留 HTTP `8080` 和 HTTPS `8443` 映射，便于受控内网迁移；公网部署应由防火墙只开放 HTTPS，或在外层负载均衡/CDN 完成证书终止。`nginx.https.conf` 只允许 TLS 1.2/1.3，启用 HSTS、CSP、禁止 iframe 和 MIME sniffing。首次使用 HSTS 前必须确认域名、证书续期和全部子域均已具备 HTTPS，不能用临时自签证书直接面向正式用户。

如果镜像已由 GitHub Actions 发布到 GHCR，则在 `.env` 设置 `EXPORTDOCMANAGER_IMAGE_NAMESPACE=ghcr.io/你的账号`，改用：

```powershell
docker compose -f .\docker-compose.ghcr.yml --env-file .\.env up -d
```

随后由部署管理员访问 `http://服务器地址:8080`，展开登录页“服务器连接设置”，在“首次部署令牌”中填写初始化命令使用的 `BootstrapToken`，再以用户名 `admin` 登录；这次输入的密码会成为首个应用管理员密码，至少需要 8 个字符。令牌只随本次登录请求发送，登录成功后从页面内存清除，不写入浏览器存储。空数据库首次初始化只接受 `admin` 用户名，其他用户名不能先行创建或认领管理员。数据库连接密码来自运行目录 `runtime/config/appsettings.json`，与应用管理员密码及首次部署令牌相互独立。

API 启动时仍要求 `.env` 中保留至少 24 位的 `EXPORTDOCMANAGER_BOOTSTRAP_TOKEN`；数据库已有用户后，普通登录无需再次填写该令牌，可以按企业密钥轮换制度更换 `.env` 中的值并重启 API。不要把令牌复用为数据库密码或管理员密码。

默认容器网段为 `172.30.238.0/24`，Nginx 地址为 `172.30.238.10`。如果该网段与现有 Docker、VPN 或局域网路由冲突，应在 `.env` 同时修改 `EXPORTDOCMANAGER_CONTAINER_SUBNET` 和 `EXPORTDOCMANAGER_REVERSE_PROXY_IP`，并确保代理地址属于所选网段。公网 HTTPS/CDN 代理位于内置 Nginx 前方时，还应把它实际连接 Nginx 的明确来源 IP 写入 `EXPORTDOCMANAGER_ADDITIONAL_TRUSTED_PROXIES`；多个地址用分号分隔。API 会按已配置代理数量限制转发链，并逐跳核对可信地址，不接受任意长度的客户端伪造链。不要填写整个不受控网段。非 Compose 反向代理部署可通过 `EXPORTDOCMANAGER_TRUSTED_PROXIES` 配置一个或多个明确代理 IP（逗号或分号分隔，不接受主机名和 CIDR）。

敏感配置默认由运行目录 `runtime/api-data/Security/local-master-key.bin` 中自动生成的本地主密钥保护，不写系统盘固定目录。也可以在 `.env` 设置 `EXPORTDOCMANAGER_MASTER_KEY`（32 字节 Base64 或 64 位十六进制）；一旦使用环境主密钥，必须长期安全备份并在迁移时一并提供，随意更换会导致既有密文无法解密。

系统默认关闭公开自注册。管理员登录后通过侧栏“系统维护 → 账号与权限”创建、停用、重置或删除账号；已有发票、付款等业务数据归属的账号只能停用，不能直接删除。

## PostgreSQL 版本

Compose 固定使用 `postgres:18-bookworm`：锁定 PostgreSQL 18 大版本，允许拉取 18 系列内的安全和缺陷修复。该 Debian/glibc 变体同时提供 Linux amd64 与 arm64/v8 镜像，更适合作为长期运行的商业数据库默认值；Windows、macOS 和 Linux 客户端均通过 PostgreSQL 网络协议访问，不依赖容器内 libc。

官方 `postgres:18-alpine` 同样支持 amd64/arm64，且 PostgreSQL 已启用 ICU，但官方文档明确提示 musl 可能影响依赖 libc 假设的软件。Alpine amd64 镜像相对 Bookworm 只节省约 35 MB，对数据库运行目录和业务数据体量意义有限，却会增加未来原生扩展、区域设置和排障差异，因此不作为本项目默认值。Docker 官方没有通用 `postgres:18-slim` 标签；如仅做资源受限测试，可手工改为 `18-alpine`，正式数据必须重新完成完整回归。

首次初始化显式使用 PostgreSQL 18 内置 Unicode provider：`--locale-provider=builtin --builtin-locale=PG_UNICODE_FAST --encoding=UTF8`。文本排序、大小写映射和字符分类不依赖 glibc/musl locale，避免不同 Linux 基础镜像造成数据库默认排序差异；项目如未来需要中文拼音排序，应另建明确的 ICU `zh-CN` collation，而不是依赖操作系统默认 locale。

PostgreSQL 18 官方镜像把默认 `PGDATA` 改为版本化目录 `/var/lib/postgresql/18/docker`，因此 Compose 必须把宿主运行根 `postgres/` 挂载到容器 `/var/lib/postgresql`，不能沿用 17 及以下的 `/var/lib/postgresql/data`。当前项目尚未投产，开发期旧 16 数据目录应备份需要的样例后删除并重新初始化；若未来已有生产数据，跨大版本必须使用 `pg_upgrade` 或 dump/restore，不能直接复用旧数据目录。

## 存储边界

- PostgreSQL 数据：`runtime/postgres/`
- API 数据、日志、授权镜像、缓存和备份：`runtime/api-data/`
- 可编辑程序配置：`runtime/config/appsettings.json`
- 容器内报表 Chromium：Debian 官方 `chromium` 包，固定通过 `/usr/bin/chromium` 使用；不从宿主 C 盘或程序运行数据根复制浏览器二进制
- 镜像层与 Docker 自身缓存由 Docker Engine 管理；Windows 上如要求系统 C 盘零占用，还必须把 Docker Desktop/Engine 的 data-root 或磁盘镜像迁到非系统盘。

不要把 `runtime/`、`.env`、TLS 私钥或数据库密码提交到版本库。公网部署必须使用可信 HTTPS 和防火墙，可启用本目录 TLS overlay，也可在 Web 容器前使用成熟反向代理/CDN；不要直接公开 API 容器端口。

## 生命周期、备份与恢复验收

仓库工作流 `Container runtime lifecycle validation` 会在 GitHub Ubuntu runner 上真实执行：

- HTTP/HTTPS Compose 解析和完整 API/Web 镜像构建；
- `/readyz`、`/healthz`、CSP/HSTS 等响应头；
- API `5188` 不对宿主机发布；
- PostgreSQL 容器删除重建后的 bind volume 数据持久化；
- `pg_dump -Fc` 备份、删除测试表后使用 `pg_restore --clean --if-exists --exit-on-error` 恢复；
- 失败日志、探针响应和恢复包 Artifact 上传及最终容器清理。

该工作流使用固定的临时测试密码和自签证书，只服务一次性 runner，不应复制到生产环境。生产恢复演练必须使用独立的备份账号/介质、实际数据规模、加密备份和经过批准的停机窗口；恢复前停止 API 写入，恢复后检查应用登录、权限、发票、HS 查询、任务和审计记录。单纯看到备份文件存在不等于恢复可用。

GitHub 只负责构建、生命周期验证和保存 GHCR/Artifact，不提供 PostgreSQL/API 的长期运行主机。真实部署仍需 Docker Engine；当前开发机没有 Docker CLI，因此本地只能完成 Dockerfile、Compose 和工作流静态验证。只有 GitHub 生命周期工作流或目标服务器演练实际成功后，才能把对应平台记录为容器验收通过。
