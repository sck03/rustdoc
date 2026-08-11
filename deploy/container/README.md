# ExportDocManager Full 容器版部署与运维

正式容器拓扑为 `Nginx Web + ASP.NET Core API + 隔离 Chromium Browser + PostgreSQL 18`；HTTPS 模式额外运行 Certbot 自动续期容器。SQLite 仅用于单机版。API `5188`、Browser CDP `9222` 和 PostgreSQL `5432` 都不发布到宿主机，用户只访问 Nginx。

本文以 Linux VPS 默认目录 `/opt/export-doc-manager` 为准。下文命令均假定当前为 `root`；普通账号先执行 `sudo -i`，不要直接对 `edm` Shell 函数使用 `sudo`。

## 1. 数据目录

一键安装不需要提前创建目录。

| 内容 | 宿主机路径 |
| --- | --- |
| 部署清单和安装器 | `/opt/export-doc-manager/` |
| 密码、镜像版本和部署参数 | `/opt/export-doc-manager/.env` |
| PostgreSQL 原始数据 | `/opt/export-doc-manager/runtime/postgres/` |
| API 配置、印章、模板、日志、缓存和应用备份 | `/opt/export-doc-manager/runtime/api-data/` |
| 隔离 Chromium profile 与自身缓存（可重建） | `/opt/export-doc-manager/runtime/browser/` |
| 本地主密钥 | `/opt/export-doc-manager/runtime/api-data/Security/local-master-key.bin` |
| Let's Encrypt 证书 | `/opt/export-doc-manager/runtime/letsencrypt/` |
| 本文创建的运维数据库备份 | `/opt/export-doc-manager/backups/postgresql/` |

完整迁移必须同时保留数据库备份、`.env` 和 `runtime/api-data/`。缺少 `Security/local-master-key.bin`，或更换 `.env` 中显式配置的 `EXPORTDOCMANAGER_MASTER_KEY`，可能导致已有加密配置无法解密。`runtime/browser/` 只保存隔离浏览器的可重建 profile/缓存，不属于业务迁移必需数据。

Docker 镜像和构建缓存由 Docker Engine 的全局 `data-root` 管理，不属于应用目录。若需放到独立数据盘，应在首次安装前按 Docker 官方方法配置 Engine。

## 2. 安装和首次登录

### 内网 HTTP

安装器默认只绑定回环地址 `127.0.0.1`，适合先通过 SSH 隧道或本机浏览器验收：

```bash
curl -fsSL https://raw.githubusercontent.com/sck03/rustdoc/main/deploy/container/install-container.sh |
  sudo bash -s -- --mode http --tag 0.1.2 --revision 完整40位提交SHA
```

访问：`http://127.0.0.1:8080`。需要在可信办公网/VPN 提供 HTTP 时，显式设置绑定地址；若要在 HTTP 上进行敏感恢复，还必须显式开启灾备开关：

```bash
sudo /opt/export-doc-manager/install-container.sh \
  --mode http --tag 0.1.2 --revision 完整40位提交SHA \
  --web-bind-address 0.0.0.0 \
  --allow-insecure-disaster-recovery
```

HTTP 模式默认写入 `EXPORTDOCMANAGER_ALLOW_INSECURE_DISASTER_RECOVERY=false`，数据库恢复、完整迁移和其它敏感灾备操作会要求 HTTPS、本机回环通道或显式可信 HTTP。普通登录仍可在 HTTP 模式使用，但凭据和业务数据没有传输加密。HTTPS 模式始终写入 `false`，由 Nginx TLS 通道满足安全要求。不要把未加密 HTTP 端口暴露到公网或访客网络。

### 公网 HTTPS

先把域名解析到 VPS，并开放 TCP `80/443`：

```bash
curl -fsSL https://raw.githubusercontent.com/sck03/rustdoc/main/deploy/container/install-container.sh |
  sudo bash -s -- \
    --mode https \
    --domain docs.example.com \
    --email ops@example.com \
    --tag 0.1.2 \
    --revision 完整40位提交SHA
```

把 `0.1.2` 和 revision 换成同一次 `container-images.yml` 发布生成的不可变容器清单值。安装器拒绝可变的 `latest` 标签，并在拉取后核验 API、Browser、Web 三个镜像的 `org.opencontainers.image.revision` 必须完全一致；也不执行联网下载后直接运行的 Docker 安装脚本，请先通过 Linux 发行版或 Docker 官方签名软件源安装并启动 Docker Engine 与 Compose v2。

安装器不会修改 UFW、云安全组、DNS 或 Docker Engine 全局配置。内网 HTTP 会明文传输登录和业务数据，不能直接暴露到公网。

### 首次登录

安装成功时会显示首次启用令牌。也可以重新读取：

```bash
sed -n 's/^EXPORTDOCMANAGER_BOOTSTRAP_TOKEN=//p' \
  /opt/export-doc-manager/.env
```

在登录页展开“高级连接选项”，填写启用令牌，以 `admin` 登录，并输入要设置的应用管理员密码。数据库密码、启用令牌和应用管理员密码不能复用。

首次安装会在 `.env` 中保存三份互不相同的 PostgreSQL 登录密码，并由 `postgres-init-roles.sh` 建立四角色最小权限模型：`POSTGRES_USER` 是数据库容器初始化管理员；`exportdoc_owner` 是不可登录的数据库/schema owner；`exportdoc_app` 是 API 普通业务连接，只具备表 DML、序列和例程权限，明确无 DDL、无 `CREATEDB`；`exportdoc_maintenance` 是独立维护连接，具备 `CREATEDB` 并可切换 owner，只用于首次建表、结构初始化和数据库恢复。应用登录页中的 `admin` 是 ExportDocManager 业务管理员，不是 PostgreSQL 管理员。

Private GHCR Package 使用只有 `read:packages` 权限的 token：

```bash
sudo env \
  GHCR_USER='你的GitHub账号' \
  GHCR_TOKEN='只读Packages令牌' \
  /opt/export-doc-manager/install-container.sh \
    --mode http \
    --tag 0.1.3 \
    --revision 完整40位提交SHA
```

Token 只通过进程环境传入，不写入安装参数或 `.env`。

## 3. 日常管理和升级

每次 SSH 登录后先执行下面这段。它会自动选择 HTTP 或 HTTPS Compose 文件，并定义 `edm` 辅助函数：

```bash
cd /opt/export-doc-manager
umask 077

MODE=$(sed -n 's/^EXPORTDOCMANAGER_DEPLOYMENT_MODE=//p' .env)
EDM_COMPOSE=(-f docker-compose.ghcr.yml)
if [ "$MODE" = "https" ]; then
  EDM_COMPOSE+=(-f docker-compose.acme.yml)
fi

edm() {
  docker compose "${EDM_COMPOSE[@]}" --env-file .env "$@"
}
```

常用命令：

```bash
edm ps -a
edm logs --no-color --tail=200 postgres browser api web
edm restart api web
edm restart postgres browser api web
```

HTTPS 证书续期日志：

```bash
[ "$MODE" = "https" ] && edm logs --no-color --tail=100 certbot
```

升级前先创建数据库备份，然后执行：

```bash
cd /opt/export-doc-manager
MODE=$(sed -n 's/^EXPORTDOCMANAGER_DEPLOYMENT_MODE=//p' .env)

./install-container.sh --mode "$MODE" --tag 0.1.3 --revision 完整40位提交SHA
```

安装器会保留 `.env`、数据库、API DataRoot 和证书。安装根、运行根和关键子目录必须是真实非根目录，不能经过符号链接；部署根会收紧为 root `700`。新部署资产会先完整下载到安装目录内的临时 staging，对两份 Compose、Nginx、`postgres-init-roles.sh` 和安装器自身五项资产逐项核对 `deployment-assets.sha256`，再校验安装器/角色脚本语法及 HTTP/HTTPS Compose 双模式后原子替换；锁文件、环境更新、资产激活和首次令牌标记均拒绝链接或使用 `mktemp`。Compose 校验、镜像拉取、证书申请、容器启动或就绪检查任一步失败时，会恢复旧 `.env`、旧部署文件和本次可能改写的 Let's Encrypt 状态，并用 `--force-recreate` 重新创建原 HTTP/HTTPS 容器，确保恢复后的证书目录和配置重新挂载。`--no-start` 成功时只保留已校验的新文件，不改变容器。

## 4. 数据库备份

PostgreSQL 容器和 API 镜像都自带 PostgreSQL 18 客户端；API 使用镜像内 `/usr/lib/postgresql/18/bin`，不要求宿主机安装客户端，也不会把工具放入系统 C 盘。命令行备份仍可使用本节方法，网页端操作见下文。

备份可在线执行，但建议选择业务低峰期。必须再复制到另一台服务器、对象存储或离线介质；只放在同一 VPS 上不能防止磁盘或整机损坏。

先初始化第 3 节的 `edm` 函数，再执行：

```bash
BACKUP_ROOT=/opt/export-doc-manager/backups/postgresql
install -d -m 700 "$BACKUP_ROOT"

BACKUP_FILE="$BACKUP_ROOT/exportdoc_$(date +%Y%m%d_%H%M%S).dump"

if ! edm exec -T postgres sh -ec '
  exec pg_dump \
    -U "$POSTGRES_USER" \
    -d "$POSTGRES_DB" \
    --format=custom \
    --blobs \
    --no-owner \
    --no-privileges
' > "$BACKUP_FILE"; then
  rm -f -- "$BACKUP_FILE"
  echo "数据库备份失败"
  exit 1
fi

test -s "$BACKUP_FILE" || {
  rm -f -- "$BACKUP_FILE"
  echo "数据库备份失败：文件为空"
  exit 1
}
chmod 600 "$BACKUP_FILE"

if ! edm exec -T postgres pg_restore --list \
  < "$BACKUP_FILE" >/dev/null; then
  rm -f -- "$BACKUP_FILE"
  echo "数据库备份校验失败"
  exit 1
fi

BACKUP_NAME=$(basename -- "$BACKUP_FILE")
(
  cd "$BACKUP_ROOT"
  sha256sum "$BACKUP_NAME" > "$BACKUP_NAME.sha256"
)
chmod 600 "$BACKUP_FILE.sha256"
ls -lh "$BACKUP_FILE" "$BACKUP_FILE.sha256"
```

复制到其它服务器：

```bash
scp "$BACKUP_FILE" "$BACKUP_FILE.sha256" \
  root@备份服务器:/data/exportdoc-backups/
```

`pg_restore --list` 只证明归档结构可读取。正式环境仍应定期在独立测试数据库或新服务器上完成恢复演练。

查看超过 30 天的手工备份：

```bash
BACKUP_ROOT=/opt/export-doc-manager/backups/postgresql
find "$BACKUP_ROOT" -maxdepth 1 -type f \
  \( -name '*.dump' -o -name '*.dump.sha256' \) \
  -mtime +30 -print
```

确认输出无误后再删除：

```bash
find "$BACKUP_ROOT" -maxdepth 1 -type f \
  \( -name '*.dump' -o -name '*.dump.sha256' \) \
  -mtime +30 -delete
```

不要把备份、`.env`、TLS 私钥或主密钥提交到 Git 仓库。

### 4.1 网页端备份和恢复

管理员登录后进入“系统设置 -> 维护 -> 团队库”。创建 `.dump` 已改为后台任务，不再让 `pg_dump` 占用长时间 HTTP 请求；页面关闭后任务仍会继续，可在任务中心查看或取消。服务器已有备份通过五分钟短期票据和 HTTP Range 流式下载，不会把数 GiB 文件先装入浏览器内存；票据签发和每次消费都会重新检查 HTTPS/显式可信 HTTP、备份目录边界及符号链接状态。也可以上传 `.dump` 后输入 `RESTORE DATABASE` 排队恢复。容器会在恢复响应完成后自动重启；恢复前会把当前数据库保存到 `runtime/api-data/Backups/ServerMigration/Safety/`。

同一页面还可以输入强迁移密码创建加密 `.edmmigration` 完整迁移包，或上传迁移包并输入 `MIGRATE` 排队恢复。完整包包含数据库、应用运行配置、印章、唛头图片和其它业务文件、用户模板、单一窗口数据及本地主密钥；部署目录的 `.env` 不在 API 可见范围内，迁移时必须另外保留并按新服务器网络参数复核。完整包不包含日志、缓存、临时导出文件、历史备份、许可证、机器绑定试用数据，也不包含 `runtime/letsencrypt/` 中的 TLS/Certbot 证书。目标服务器若显式设置 `EXPORTDOCMANAGER_MASTER_KEY`，必须与源服务器一致，否则恢复会在覆盖数据库前中止。

网页恢复会创建临时验证数据库，因此由独立 `exportdoc_maintenance` 账号执行建库和恢复；该账号具备 `CREATEDB`，并可切换不可登录的 `exportdoc_owner` 重新归属数据库对象。普通 API 业务账号 `exportdoc_app` 保持 `NOCREATEDB` 且没有 DDL 权限。数据库密码解析优先级为密码文件、环境变量、程序生成的 AES-GCM 受保护配置；Compose 默认把 app 与 maintenance 的独立密码从部署目录权限受限 `.env` 注入 API 环境，初始化管理员密码不会用于普通 API 连接。迁移到新服务器后，让安装器重新探测容器网段并重新签发 TLS 证书；如需保留原证书，应由部署管理员在 API 迁移包之外单独安全处理 `runtime/letsencrypt/`。

## 5. 在当前服务器恢复数据库

警告：恢复会覆盖当前业务数据库。先创建一份新的当前库备份，并安排停机窗口。

先初始化第 3 节的 `edm` 函数，然后指定备份：

```bash
BACKUP_FILE=/opt/export-doc-manager/backups/postgresql/exportdoc_具体时间.dump

test -s "$BACKUP_FILE" || {
  echo "备份文件不存在或为空"
  exit 1
}

(
  cd "$(dirname -- "$BACKUP_FILE")"
  sha256sum -c "$(basename -- "$BACKUP_FILE").sha256"
)
```

停止 Web 和 API，执行恢复：

```bash
edm stop web api

edm exec -T postgres sh -ec '
  exec pg_restore \
    -U "$POSTGRES_USER" \
    -d "$POSTGRES_DB" \
    --clean \
    --if-exists \
    --exit-on-error \
    --no-owner \
    --no-privileges
' < "$BACKUP_FILE"
```

仅在恢复成功后重新启动：

```bash
edm up -d
edm ps -a
edm logs --no-color --tail=100 postgres browser api web
```

人工核对登录、权限、发票、客户、出口商、印章、HS 查询、任务和审计记录。恢复失败时不要先开放 Web，应检查 PostgreSQL 日志和备份文件。

## 6. 停止、卸载和彻底清理

先初始化第 3 节的 `edm` 函数。

临时停止，保留容器和全部数据：

```bash
edm stop
```

重新启动：

```bash
edm up -d
```

删除容器和项目网络，但保留数据库、印章、配置、证书和备份：

```bash
edm down --remove-orphans
```

以后可重新运行安装器恢复：

```bash
/opt/export-doc-manager/install-container.sh --mode "$MODE"
```

### 彻底删除整个容器版

警告：以下操作会永久删除数据库、账号、发票、印章、模板、配置、证书、本地主密钥和本机备份。必须先把数据库备份和迁移归档复制到其它机器。

记录镜像名称并删除容器：

```bash
IMAGE_NAMESPACE=$(sed -n 's/^EXPORTDOCMANAGER_IMAGE_NAMESPACE=//p' .env)
IMAGE_TAG=$(sed -n 's/^EXPORTDOCMANAGER_IMAGE_TAG=//p' .env)

edm down --remove-orphans
```

可选删除当前版本镜像：

```bash
docker image rm \
  "$IMAGE_NAMESPACE/export-doc-manager-api:$IMAGE_TAG" \
  "$IMAGE_NAMESPACE/export-doc-manager-web:$IMAGE_TAG" || true
```

最后校验固定路径并删除安装目录：

```bash
cd /opt

test "$(readlink -f -- /opt/export-doc-manager)" = "/opt/export-doc-manager" || {
  echo "目录校验失败，停止删除"
  exit 1
}

rm -rf --one-file-system -- /opt/export-doc-manager
```

不要使用 `docker system prune -a --volumes` 清理本项目，它可能删除 VPS 上其它项目的镜像、缓存和数据卷。

## 7. 完整迁移到新服务器

使用“PostgreSQL 逻辑备份 + API DataRoot 归档”。不要复制 `runtime/postgres/18/docker` 原始目录；逻辑备份可以在 x64 与 ARM64 之间迁移，也避免容器 UID 和 PostgreSQL 小版本差异。

迁移时旧服务器必须停止业务写入。新服务器验收前不要删除旧服务器，也不要让两套服务同时对外写入。

### 7.1 旧服务器导出

初始化第 3 节的 `edm` 函数，停止 Web 和 API：

```bash
edm stop web api
```

按第 4 节创建新的数据库备份，并记录最后输出的 `.dump` 完整路径。若已更换 SSH Shell，先重新设置 `BACKUP_FILE`，然后停止全部容器：

```bash
BACKUP_FILE=/opt/export-doc-manager/backups/postgresql/exportdoc_具体时间.dump
edm stop
```

归档 `.env`、API DataRoot 和 HTTPS 状态。数据库 dump 位于独立的 `backups/postgresql`，不会重复进入归档：

```bash
MIGRATION_ROOT=/root/exportdoc-migration
install -d -m 700 "$MIGRATION_ROOT"

MIGRATION_FILE="$MIGRATION_ROOT/exportdoc_environment_$(date +%Y%m%d_%H%M%S).tar.gz"

tar --xattrs --acls \
  -C /opt/export-doc-manager \
  -czf "$MIGRATION_FILE" \
  .env \
  runtime/api-data \
  runtime/letsencrypt \
  runtime/acme-webroot

MIGRATION_NAME=$(basename -- "$MIGRATION_FILE")
(
  cd "$MIGRATION_ROOT"
  sha256sum "$MIGRATION_NAME" > "$MIGRATION_NAME.sha256"
)
chmod 600 "$MIGRATION_FILE" "$MIGRATION_FILE.sha256"
ls -lh "$MIGRATION_FILE" "$MIGRATION_FILE.sha256"
```

环境归档包含数据库密码、TLS 私钥和本地主密钥，只能通过 SSH 等加密通道传输：

```bash
scp \
  "$MIGRATION_FILE" \
  "$MIGRATION_FILE.sha256" \
  "$BACKUP_FILE" \
  "$BACKUP_FILE.sha256" \
  root@新服务器IP:/root/
```

### 7.2 新服务器恢复环境

校验文件：

```bash
cd /root
sha256sum -c exportdoc_environment_具体时间.tar.gz.sha256
sha256sum -c exportdoc_具体时间.dump.sha256
```

目标目录必须为空：

```bash
if [ -d /opt/export-doc-manager ] && \
  [ -n "$(find /opt/export-doc-manager -mindepth 1 -maxdepth 1 -print -quit)" ]; then
  echo "/opt/export-doc-manager 已包含文件，停止迁移"
  exit 1
fi

install -d -m 700 /opt/export-doc-manager
tar --xattrs --acls \
  -xzf /root/exportdoc_environment_具体时间.tar.gz \
  -C /opt/export-doc-manager
```

删除旧服务器 Docker 私网值，让安装器重新探测：

```bash
sed -i \
  -e '/^EXPORTDOCMANAGER_CONTAINER_SUBNET=/d' \
  -e '/^EXPORTDOCMANAGER_REVERSE_PROXY_IP=/d' \
  /opt/export-doc-manager/.env
```

先按目标 Linux 发行版或 Docker 官方签名软件源安装 Docker Engine 与 Compose v2，再读取原部署模式、下载部署清单，但暂不启动业务容器：

```bash
MODE=$(sed -n 's/^EXPORTDOCMANAGER_DEPLOYMENT_MODE=//p' \
  /opt/export-doc-manager/.env)

curl -fsSL https://raw.githubusercontent.com/sck03/rustdoc/main/deploy/container/install-container.sh |
  bash -s -- \
    --mode "$MODE" \
    --no-start
```

若 GHCR Package 是 Private，新服务器不会继承旧服务器的 Docker 登录状态；正式启动前按第 2 节设置 `GHCR_USER` 和只读 `GHCR_TOKEN`。

重新初始化第 3 节的 `edm` 函数，只启动空 PostgreSQL：

```bash
cd /opt/export-doc-manager
edm pull postgres
edm up -d postgres

until edm exec -T postgres \
  sh -ec 'pg_isready -U "$POSTGRES_USER" -d "$POSTGRES_DB"' \
  >/dev/null; do
  sleep 2
done
```

### 7.3 新服务器恢复数据库并启动

```bash
BACKUP_FILE=/root/exportdoc_具体时间.dump

edm exec -T postgres sh -ec '
  exec pg_restore \
    -U "$POSTGRES_USER" \
    -d "$POSTGRES_DB" \
    --clean \
    --if-exists \
    --exit-on-error \
    --no-owner \
    --no-privileges
' < "$BACKUP_FILE"
```

HTTP 模式或继续使用原 HTTPS 域名：

```bash
/opt/export-doc-manager/install-container.sh --mode "$MODE"
```

HTTPS 改用新域名时，先完成 DNS 和防火墙设置：

```bash
/opt/export-doc-manager/install-container.sh \
  --mode https \
  --domain new-docs.example.com \
  --email ops@example.com
```

检查服务：

```bash
edm ps -a
edm logs --no-color --tail=100 postgres browser api web
```

原 HTTPS 域名尚未切换 DNS 时，可在新服务器本机验证：

```bash
DOMAIN=$(sed -n 's/^EXPORTDOCMANAGER_PUBLIC_DOMAIN=//p' .env)
curl --resolve "$DOMAIN:443:127.0.0.1" "https://$DOMAIN/readyz"
```

确认登录、权限、发票、客户、出口商、印章、HS 查询、PDF、任务和审计记录正常后再切换流量。旧服务器建议保持停止状态数天；确认无需回退后再彻底删除。迁移验收后应删除 `/root` 中的环境归档和数据库备份中转副本。

## 8. HTTPS、Nginx 和 PostgreSQL 说明

当前方案不需要 Caddy。Nginx 在 HTTP 和 HTTPS 模式都负责静态文件、SPA fallback、同源 `/api` 代理、安全响应头和隐藏 API 内部端口，因此不能删除。HTTPS 首次证书申请由安装器调用 Certbot standalone；以后 Certbot 容器每 12 小时检查续期，Nginx 每小时 reload 新证书。Certbot 不代理业务请求。

企业网关或 CDN 在 Nginx 前终止 TLS 时，必须把其实际连接 Nginx 的固定 IP 加入 `EXPORTDOCMANAGER_ADDITIONAL_TRUSTED_PROXIES`，不要填写不受控网段，也不要直接公开 API `5188`。

API 与 Browser 最终运行层都固定为 `debian:trixie-slim`；Web 只承载静态资源和反向代理，继续使用更轻量的 `nginx:1.30.4-alpine3.24`。Debian 原生 Chromium 和配套 `chromium-sandbox` 只安装在独立 Browser 镜像的 `/usr/bin/chromium`，API 镜像不再携带浏览器。API 与 Browser 都以固定非 root UID/GID `10001` 运行；API 丢弃全部 Linux capabilities 并启用 `no-new-privileges`，只有 Browser 获得 Chromium 命名空间沙箱所需的 `SYS_ADMIN`，仍不使用 `--no-sandbox`。Browser 不接入 PostgreSQL `backend` 网络，也不接收数据库、维护账号、主密钥或首次启用令牌；API 仅通过不发布到宿主的专用 `browser` 网络访问 `http://browser:9222`。PostgreSQL 18 客户端来自 `trixie-pgdg`，位于 API 镜像 `/usr/lib/postgresql/18/bin`；.NET/ASP.NET Core Runtime 通过 Microsoft 官方 Debian 13 仓库配置包安装构建时可用的最新稳定 `10.0.x`，由官方配置包同步仓库签名密钥轮换，镜像构建会验证两个共享框架使用同一补丁版本，不会硬编码尚未发布到该仓库的包修订号。Microsoft Container Registry 当前没有稳定版 .NET 10 Trixie SDK/ASP.NET 标签，因此构建层使用官方 `10.0.302-noble`，不会再引用不存在的 `bookworm-slim` 标签。

Browser 服务分配 `shm_size: 512mb`，让 Chromium 使用内存文件系统而不是把共享内存工作负载转移到磁盘缓存。API 生成的 `runtime/api-data/Cache/ReportPdf` 以只读方式挂载到 Browser 的同等运行路径；Browser 只能读取当前临时报表和随镜像提供的开源字体，PDF 字节通过 CDP 返回 API 后再由 API 原子写入目标文件。非容器部署仍保留兼容小 `/dev/shm` 环境的默认启动参数。

Compose 默认资源档位为 API `2 CPU / 2 GiB / 512 PID`、Browser `1.5 CPU / 1536 MiB / 512 PID`、PostgreSQL `2 CPU / 2 GiB / 256 PID`、Web `0.5 CPU / 256 MiB / 128 PID`，均可在 `.env` 中按主机容量调整。API、Browser、PostgreSQL 和 Web 分别有 60/15/45/15 秒停止宽限，避免长任务无限占用主机或升级时粗暴截断数据库。

Compose 固定使用 Debian 13 基线的 `postgres:18.4-trixie`，初始化参数为：

```text
--encoding=UTF8 --locale-provider=builtin --builtin-locale=PG_UNICODE_FAST
```

PostgreSQL 18 容器数据位于 `/var/lib/postgresql/18/docker`，因此宿主 `runtime/postgres/` 必须挂载到 `/var/lib/postgresql`，不能改成旧版常见的 `/var/lib/postgresql/data`。跨 PostgreSQL 大版本升级必须使用验证过的 dump/restore 或 `pg_upgrade`，不能直接复用旧版原始数据目录。

外层 `runtime/` 使用 `root:root 700`。API 与 Browser 镜像都以固定 `10001:10001` 非 root 身份运行；`runtime/api-data/`、`runtime/api-data/Cache/ReportPdf/` 和 `runtime/browser/` 由安装器设置为该身份可访问的 `750`，其中 ReportPdf 对 Browser 只读。PostgreSQL 目录由固定 `999:999` 身份持有并保持 `700`，应用配置文件为 `600`。这些 bind mount 不再依赖 `777/1777` 世界可写权限，也不要脱离不可遍历的父目录单独暴露。

## 9. 开发者附录

- `docker-compose.ghcr.yml`：拉取已发布镜像，适合正式 VPS；
- `docker-compose.yml`：从源码构建，只用于开发和 CI；
- `docker-compose.acme.yml`：一键 HTTPS 和自动续期；
- `docker-compose.https.yml`：手工证书 overlay；
- `deployment-assets.sha256`：安装器下载资产的 SHA-256 完整性清单；
- `postgres-init-roles.sh`：全新 PostgreSQL volume 的四角色最小权限初始化脚本；
- `initialize-container-runtime.ps1`：克隆仓库后的 PowerShell 初始化工具；Windows 可直接运行，Linux/macOS 需使用 `sudo pwsh` 以设置固定容器 UID/GID；可信 HTTP 开发环境需显式设置 `-WebBindAddress 0.0.0.0`，并按需增加 `-AllowHttpDisasterRecovery`；
- `install-container.sh`：Linux VPS 正式安装、升级和恢复入口。

GitHub 工作流 `Container runtime lifecycle validation` 会验证四服务启动、Browser 沙箱与 CDP、API/Browser 凭据和网络隔离、真实 Chromium PDF、安全响应头、PostgreSQL bind mount 持久化和 `pg_dump/pg_restore`。CI 成功不能替代生产环境的异机备份和恢复演练。
