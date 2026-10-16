# Docker 容器部署与启动指南

> 2026-10-16。适用于包含 `--print-compose`／`--prepare-docker` 的新版 Rust 镜像。React 网页和 Rust API 在应用容器中，PostgreSQL 18 在独立数据库容器中。无需下载源码、安装 Rust、Node 或 PowerShell。

## 1. 拉取镜像并启动

安装 Docker Engine 和 Docker Compose v2。在一个准备长期保留的空目录中执行，Linux shell、PowerShell 7 均可使用：

```sh
docker pull ghcr.io/sck03/exportdoc-rust-native:latest
docker run --rm ghcr.io/sck03/exportdoc-rust-native:latest --print-compose > compose.yml
docker compose up -d --wait
```

镜像会生成自身适用的 Compose 配置。首次启动自动生成随机数据库密码和初始化口令，准备目录、创建独立数据库角色并初始化业务库；后续启动复用原配置和数据。长期运行的是应用与 PostgreSQL 两个服务，初始化服务完成后退出。

新版配置的常驻容器名为 `exportdoc-application`、`exportdoc-postgres`。原名称末尾的 `-1` 是 Compose 自动分配的第一个实例编号，不是异常。现在使用 `${COMPOSE_PROJECT_NAME}-服务名` 显式命名；另建隔离部署时指定不同项目名和端口即可，既有部署不可为改名字而换项目名或数据库卷。当前架构只支持单 API 实例，不用 `--scale application` 扩容。

`setup` 准备配置及目录，`initialize` 执行数据库初始化或兼容升级；它们成功退出后不再占用进程、CPU 或运行内存。确认上面的 `up --wait` 成功、两项常驻服务 healthy，且初始化退出码均为 0 后，可清理停止的容器：

```sh
docker compose ps -a
docker compose rm -f setup initialize
```

该命令仅移除停止容器及其容器日志，不删除 `config`、`data` 和 `postgres_data`。初始化失败时先保留容器并用 `docker compose logs setup initialize` 排查。保留 Compose 中的服务定义；下次完整 `up` 会重建并运行它们，升级仍需要维护权限。普通重启已运行的应用可用 `docker compose restart application`，不会额外运行初始化。维护容器无需为隐藏退出状态而常驻，也不向业务程序开放 Docker socket 来自删容器。

打开 **`http://服务器局域网IP:5188`**；服务器本机也可打开 `http://127.0.0.1:5188`。首次登录使用 `admin`，自行设置至少 12 位的管理员密码，在登录页“连接设置”中填写管理员初始化口令。口令保存在：

```text
config/application/bootstrap-token.txt
```

Linux 上可由管理员用 `sudo cat config/application/bootstrap-token.txt` 读取。不要把口令公开到聊天、工单或截图。初始化口令只用于空库建立第一个管理员，后续使用已设置的账号密码。

正式部署建议把第一条命令的镜像换成已发布的固定版本；生成 Compose 时也把该引用作为参数传入，例如：

```sh
docker run --rm ghcr.io/sck03/exportdoc-rust-native:<已发布版本> --print-compose ghcr.io/sck03/exportdoc-rust-native:<已发布版本> > compose.yml
```

`main` 源码更新不会自动覆盖 GHCR 的 `latest`。若镜像提示不认识上述参数，需先通过 [Docker 镜像发布工作流](./GitHub开源发布与Docker镜像说明.md)构建和发布本次版本，再重新拉取。不要把旧镜像与新版初始化配置混用。

## 2. 管理员需要保留的目录

| 主机位置 | 用途 | 管理方式 |
| --- | --- | --- |
| `compose.yml`、可选 `.env` | 部署方式、镜像、端口 | 与部署记录一起保存 |
| `config/application` | 业务连接、首次初始化口令、部署标记 | 只读挂给 API |
| `config/maintenance` | 独立维护连接 | 仅初始化／恢复服务使用 |
| `config/postgres` | 首次建库脚本及数据库凭据 | 仅 PostgreSQL 使用 |
| `data/Backups` | 完整灾备、迁移包、数据库快照 | 管理员可直接搬运加密备份；也可网页下载 |
| `data/Templates`、`data/Security` | 用户文件模板、凭据主密钥 | 必须保留；完整加密包会包含 |
| `data/Logs`、`data/Cache` | 日志、受管运行缓存 | 通过维护功能检查，勿混入业务资料 |
| Compose 的 `postgres_data` 卷 | PostgreSQL 原始数据库目录 | 由 PostgreSQL 管理，不复制运行中的原始文件代替备份 |

业务单据、账号、图片、OA 附件、数据库模板和历史记录保存在 PostgreSQL 中。`data` 目录不是数据库卷的替代品。恢复材料至少应包含完整加密包及其密码，部署配置另存。

应用以 UID/GID `10001` 运行，初始化服务只在准备目录时使用必要的权限。Linux 管理员可用 `sudo` 操作主机目录；若迁入已有 `data`，应先核对来源和所有者。程序拒绝接管非空且所有者错误的目录，也不会在已有配置损坏时重新生成密码。

**保持原目录、原项目名和数据库卷。** 不要为重启新建另一套空目录，不要使用 `docker compose down -v` 删除正式数据库卷。

## 3. 局域网 IP 与公网域名

默认发布 `0.0.0.0:5188`，局域网电脑可访问服务器 IP。主机防火墙需要放行网页端口；PostgreSQL 不发布主机端口，无需向局域网或公网开放 5432。

需要改端口或仅供本机反向代理时，在同目录创建 `.env`，填入实际值后重新执行 `docker compose up -d --wait`：

```dotenv
NATIVE_PORT=5188
NATIVE_BIND_ADDRESS=127.0.0.1
```

上例只允许本机代理连接。局域网直接访问时使用 `0.0.0.0` 或服务器指定网卡的 IP。不要把 `0.0.0.0` 当作用户浏览器的访问地址。

公网域名需要域名解析、公司的外网入口以及 HTTPS 反向代理。网页与 API 一起代理到应用端口，保留原路径即可，不必重编译前端或把数据库地址交给浏览器。没有公网入口时可通过 VPN 接入公司网络。内外网可使用同一域名配合内网 DNS，也可以分别使用 IP 和域名；浏览器会话按访问来源分别管理。

浏览器直接读取剪贴板等能力需要 HTTPS 安全上下文；局域网 HTTP 下可使用键盘粘贴。建议需要完整浏览器能力时也使用 HTTPS 域名。

Nginx 已配置有效证书后，可在站点的 `location /` 中使用：

```nginx
location / {
    proxy_pass http://127.0.0.1:5188;
    proxy_http_version 1.1;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-Proto $scheme;
    client_max_body_size 0;
    proxy_request_buffering off;
    proxy_buffering off;
    proxy_read_timeout 86400s;
    proxy_send_timeout 86400s;
}
```

这段配置允许大包流式上传下载。网关、NAS、CDN 和磁盘也有各自的限制；很大的迁移包优先通过服务器目录搬运，见[备份迁移与恢复指南](./服务器备份迁移与恢复指南.md)。

## 4. 启停与升级

```sh
docker compose ps
docker compose logs --tail=100 application
docker compose stop
docker compose up -d --wait
```

升级前在网页创建完整加密备份，保存部署配置。然后在原目录逐条执行，每条成功后再执行下一条：

```sh
docker compose pull
docker compose down
docker compose up -d --wait
```

`down` 不加 `-v`，保留数据库卷和主机目录。先拉取成功再停止旧服务。初始化服务持维护权限执行版本兼容升级，业务 API 只获得业务权限；初始化失败时不会继续启动 API。数据库升级后，不能只换回旧镜像就认定回退完成。

已有 `-1` 名称的部署采用新版命名时，在原目录保存旧配置并创建完整备份，用旧配置执行 `down`（不加 `-v`），再合并新版镜像输出的 `container_name` 配置；保留原项目名、镜像选择、端口、挂载及卷。重新 `up -d --wait` 后核对登录和业务数据，再按第一节清理成功退出的初始化容器。只推送源码不会修改已安装容器或已发布镜像。

## 5. 日常灾备与恢复

网页进入 **系统设置 → 备份与恢复**：

- **日常灾备**：默认关闭；设置包密码后可立即备份，或启用每天／每周计划、设置保留份数和天数。
- **异机副本**：选择 WebDAV／NAS 或 S3 兼容存储，保存后测试实际写入、回读和删除权限。
- **整机迁移**：创建用于换机的完整包。
- **恢复与校验**：选择服务端现有完整包，或上传外部完整包／数据库快照，先校验并暂存。

网页提示恢复已暂存后，在原 Compose 目录逐条执行：

```sh
docker compose stop application
docker compose run --rm --no-deps restore
docker compose up -d --no-deps --wait application
```

第二条失败时保持 API 停止，保留待恢复标记、安全副本和日志，处理原因后重试。普通重启不会代替恢复维护。恢复后用备份中的账号登录，核对数据、附件、模板、PDF 和权限。

更详细的大包导入、跨服务器迁移及失败处理见[恢复指南](./服务器备份迁移与恢复指南.md)。

## 6. 已有源码脚本部署

旧 `scripts/run-native-docker.ps1` 入口仍供已经采用它的部署和开发验证使用，继续使用原 RuntimeRoot、项目名和卷。它不是新版镜像安装的前置条件。不要直接在已有生产库旁启动上述空目录方案；切换部署方式应先创建完整迁移包，在隔离目标恢复并验收。源码构建参数见 [scripts/README](../scripts/README.md)。
