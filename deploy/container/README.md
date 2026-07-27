# Full 局域网 / 容器版

该部署只发布 `Full` 产品，并通过 PostgreSQL 中的账号岗位、权限模板和数据归属控制实际能力。当前 Compose 方案使用一个 Web 容器承载 Nginx、一个 API 容器和一个 PostgreSQL 容器；它适合公司局域网，也支持在需要时叠加 TLS。HTTP、HTTPS 和 Nginx 的选择属于部署边界，不改变业务权限、数据库隔离或登录流程。

## 先选择部署模式

### 公司内网 HTTP（正式支持）

如果服务器和浏览器都位于受控的公司内网、VLAN 或 VPN 内，通常不需要为了“形式上的严格”强制启用 HTTPS。基础 `docker-compose.yml` 就是 HTTP-only 模式：不读取证书，不挂载私钥，也不依赖 `docker-compose.https.yml`。

```powershell
docker compose -f .\docker-compose.yml --env-file .\.env up -d --build
```

默认从 `http://服务器地址:8080` 访问。应由网络管理员使用防火墙/交换机 ACL 只允许办公网段访问该端口，并避免把它转发到公网。HTTP 会明文传输登录凭据和业务数据，因此跨越不可信网络、公共 Wi-Fi、互联网或存在合规要求时，必须改用 HTTPS 或由可信 VPN/外部负载均衡终止 TLS；内网 HTTP 是有明确边界的部署模式，不是程序故障降级。

### 公网或合规 HTTPS

在公网、跨网段访问、零信任网络或企业合规要求下，准备可信证书和私钥后叠加 HTTPS overlay：

```powershell
docker compose `
  -f .\docker-compose.yml `
  -f .\docker-compose.https.yml `
  --env-file .\.env `
  up -d --build
```

overlay 额外发布 HTTPS 端口 `8443`，启用 TLS 1.2/1.3、HSTS 和更严格的安全响应头。若要“只允许 HTTPS”，应在防火墙关闭或限制 `8080`，不要仅依赖浏览器重定向。也可以让企业负载均衡、网关或 CDN 在 Nginx 前终止 TLS；此时必须把其实际连接 Nginx 的固定 IP 配置为可信代理。

### 非 Docker 浏览器服务器包

Windows/Linux 浏览器服务器包由单个 ASP.NET Core 进程同时托管 React 静态文件和 API，部署机连接原生 PostgreSQL，不需要 Nginx。该模式适合已有 IIS、Apache、企业网关，或不希望维护 Docker Web 容器的环境；是否增加外部反向代理由现场网络决定。

## Nginx 在当前 Compose 中做什么

“内网不强制 HTTPS”不等于“当前 Nginx 必须删除”。Nginx 在 Compose 里仍承担三个实际职责：

- 提供 React 的静态文件和 SPA fallback，让浏览器可以直接打开 Web 页面；
- 代理 `/api`、`/readyz`、`/healthz` 和 `/openapi`，让 Web/API 同源，并且 API `5188` 只在 Compose 内部可见；
- 统一 CSP、禁止 iframe、MIME sniffing 防护等 HTTP 响应头，并向 API 传递经过配置的可信代理地址。

因此，内网 HTTP 只是不启用 TLS 配置，不是把 Web 服务器和同源代理一并省略。若现场确实要移除 Nginx，必须同时提供等价的静态文件服务、SPA fallback、`/api` 反向代理、健康探针转发和安全响应头，并重新审查 `KnownProxies`/CORS/CSP；不能只删掉 Web 容器后把 API 端口直接暴露给所有客户端。单进程浏览器服务器包已经提供了不使用 Nginx 的正式替代路径。

## 初始化

在本目录执行：

```powershell
pwsh -File .\initialize-container-runtime.ps1 `
  -PostgreSqlPassword "请替换为长随机数据库密码" `
  -BootstrapToken "请替换为另一段至少24位的随机首次部署令牌"
docker compose -f .\docker-compose.yml --env-file .\.env up -d --build
```

初始化脚本会检查当前宿主机接口、路由表和已存在的 Docker 网络，从候选私有地址中自动选择不重叠的紧凑 `/28`，并把 Nginx 的可信代理地址一起写入 `.env`。仓库不再提供 `172.30.238.0/24` 这类固定默认值；新部署会按现场网络自动生成，已存在的 `.env` 默认保持稳定。需要重新探测时增加 `-RegenerateNetwork`。企业 VPN 使用大范围路由、或现场有特殊网络规划时，可以显式指定 `/24` 至 `/28` 网段和代理地址：

```powershell
pwsh -File .\initialize-container-runtime.ps1 `
  -PostgreSqlPassword "请替换为长随机数据库密码" `
  -BootstrapToken "请替换为另一段至少24位的随机首次部署令牌" `
  -ContainerSubnet "10.238.42.0/28" `
  -ReverseProxyIp "10.238.42.10"
```

显式网段与已发现的本机/Docker 网络重叠时，脚本默认拒绝继续；只有网络管理员确认隔离且明确传入 `-AllowNetworkOverlap` 才会放行。Compose 不再在缺少这两个变量时静默回退到某个固定网段。`/28` 足够容纳当前 Web、API、PostgreSQL 以及后续少量副本；如果现场要扩展更多容器，可以显式改用 `/27` 或 `/24`（脚本会拒绝宽于 `/24` 的网络）。

初始化脚本会把 HTTPS 端口和证书挂载路径写入 `.env`，方便以后启用 overlay；这不会把内网 HTTP 部署变成必需 TLS。首次使用 HSTS 前必须确认域名、证书续期和全部子域均已具备 HTTPS，不能用临时自签证书直接面向正式用户。

如果镜像已由 GitHub Actions 发布到 GHCR，则在 `.env` 设置 `EXPORTDOCMANAGER_IMAGE_NAMESPACE=ghcr.io/你的账号`，改用：

```powershell
docker compose -f .\docker-compose.ghcr.yml --env-file .\.env up -d
```

随后由部署管理员访问 `http://服务器地址:8080`，展开登录页“服务器连接设置”，在“首次部署令牌”中填写初始化命令使用的 `BootstrapToken`，再以用户名 `admin` 登录；这次输入的密码会成为首个应用管理员密码，至少需要 8 个字符。令牌只随本次登录请求发送，登录成功后从页面内存清除，不写入浏览器存储。空数据库首次初始化只接受 `admin` 用户名，其他用户名不能先行创建或认领管理员。数据库连接密码来自运行目录 `runtime/config/appsettings.json`，与应用管理员密码及首次部署令牌相互独立。

API 启动时仍要求 `.env` 中保留至少 24 位的 `EXPORTDOCMANAGER_BOOTSTRAP_TOKEN`；数据库已有用户后，普通登录无需再次填写该令牌，可以按企业密钥轮换制度更换 `.env` 中的值并重启 API。不要把令牌复用为数据库密码或管理员密码。

容器网段和 Nginx 地址由初始化脚本写入 `.env`，必须是专用、彼此匹配的 IPv4 `/24` 至 `/28` 与地址；它们只存在于 Docker 内部，不是公司局域网对外的服务器地址。公网 HTTPS/CDN 代理位于内置 Nginx 前方时，还应把它实际连接 Nginx 的明确来源 IP 写入 `EXPORTDOCMANAGER_ADDITIONAL_TRUSTED_PROXIES`；多个地址用分号分隔。API 会按已配置代理数量限制转发链，并逐跳核对可信地址，不接受任意长度的客户端伪造链。不要填写整个不受控网段。非 Compose 反向代理部署可通过 `EXPORTDOCMANAGER_TRUSTED_PROXIES` 配置一个或多个明确代理 IP（逗号或分号分隔，不接受主机名和 CIDR）。

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
