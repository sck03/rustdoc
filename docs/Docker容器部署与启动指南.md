# Docker 容器部署与启动指南

产品界面名称为“业务协同平台”，与独立网页多用户版一致。Docker 表示部署方式；镜像地址、容器服务名和数据目录保持原有规则。分版名称及界面规范见[产品命名规范](./前端视觉与产品命名规范.md)。

核对日期：2026-11-27。适用于包含 `--print-compose`、`--print-uninstall` 和 `--prepare-docker` 的 Rust 镜像。React 网页和 Rust API 在应用容器中，PostgreSQL 18 在独立数据库容器中。无需下载源码、安装 Rust、Node 或 PowerShell。当前只接受全新 schema 19，旧实验数据库必须另存并显式建立新库，不能直接覆盖升级。

**首次部署只需 A 一台主机。** 默认 Compose 已包含应用和 PostgreSQL，附件使用 A 的 `data/Files`；不需要预先准备 B、NAS、S3 或移动硬盘。独立附件位置按第 7 节后续接入，备份按第 5 节按需开启；本机备份与独立设备副本的保护范围分别说明。

2026-11-27 [后续重构方案](./通用申请与存储灾备重构实施方案.md)保留上述默认行为，并规划直写备份目标与独立维护网页。当前仍使用本文的一次性 restore 维护流程，新方案不是现行镜像已支持的功能清单。

## 1. 拉取镜像并启动

安装 Docker Engine 和 Docker Compose v2。在一个准备长期保留的空目录中执行，Linux shell、PowerShell 7 均可使用：

```sh
docker pull ghcr.io/sck03/exportdoc-rust-native:latest
docker run --rm ghcr.io/sck03/exportdoc-rust-native:latest --print-compose > compose.yml
docker compose up -d --wait
```

镜像会生成自身适用的 Compose 配置。首次启动自动生成随机数据库密码和初始化口令，准备目录、创建独立数据库角色并初始化业务库；后续启动复用原配置和数据。长期运行的是应用与 PostgreSQL 两个服务，初始化服务完成后退出。

新版配置的常驻容器名为 `exportdoc-application`、`exportdoc-postgres`。原名称末尾的 `-1` 是 Compose 自动分配的第一个实例编号，不是异常。现在使用 `${COMPOSE_PROJECT_NAME}-服务名` 显式命名；另建隔离部署时指定不同项目名和端口即可，既有部署不可为改名字而换项目名或数据库卷。当前架构只支持单 API 实例，不用 `--scale application` 扩容。

`setup` 准备配置及目录，`initialize` 初始化新数据库或验证现有基线；它们成功退出后不再占用进程、CPU 或运行内存。确认上面的 `up --wait` 成功、两项常驻服务 healthy，且初始化退出码均为 0 后，可清理停止的容器：

```sh
docker compose ps -a
docker compose rm -f setup initialize
```

该命令仅移除停止容器及其容器日志，不删除 `config`、`data` 和 `postgres_data`。初始化失败时先保留容器，并查看 `docker compose logs --no-color --tail=100 setup initialize postgres`；旧卷与新密码不匹配的排查及重建步骤见第 8.2 节。保留 Compose 中的服务定义；下次完整 `up` 会重建并运行它们，升级仍需要维护权限。普通重启已运行的应用可用 `docker compose restart application`，不会额外运行初始化。维护容器无需为隐藏退出状态而常驻，也不向业务程序开放 Docker socket 来自删容器。

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
| `data/Files`、`data/Configuration/file-storage.json` | 默认附件对象和存储配置；可迁移到独立磁盘或 S3 | 通过“文件与存储”维护；不能按缓存删除 |
| `data/Logs`、`data/Cache` | 日志、受管运行缓存 | 通过维护功能检查，勿混入业务资料 |
| Compose 的 `postgres_data` 卷 | PostgreSQL 原始数据库目录 | 由 PostgreSQL 管理，不复制运行中的原始文件代替备份 |

业务单据、账号、模板、历史及文件引用保存在 PostgreSQL 中；附件和图片的字节保存于本地/S3 文件存储。`data` 目录不是数据库卷的替代品，数据库 dump 也不能代替附件对象。恢复材料应包含完整加密包及其密码，部署配置另存；搬运普通快照时须一并搬运同名 `.objects.jsonl` 清单和邻接 `Objects/` 仓库。

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

OCR 的“粘贴图片”在局域网 HTTP 下会说明浏览器限制并聚焦图片预览区，再按 `Ctrl+V`（macOS 使用 `Cmd+V`）粘贴截图，也可选择图片文件。浏览器在 HTTPS 安全上下文中才允许网页申请直接读取剪贴板；支持该接口且权限允许时，按钮可一键载入。拒绝权限时保留已有图片并给出快捷键粘贴引导。无需为此修改容器 OCR 模型或开放数据库端口。

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

初始化检查同时比较 schema 版本、正式 SQL 的模型指纹和实际数据库结构；即使新旧镜像都显示同一 schema 版本，模型指纹变化也会拒绝继续使用旧实验库。出现“数据库模型指纹或结构不匹配，已保留原库”时，原因是镜像与数据基线不一致，与之前删除成功退出的 setup／initialize 容器无关。保留原数据与备份，使用显式的新数据目录或新 PostgreSQL 数据库重新初始化；不要删除版本／指纹记录或跳过初始化来强行启动。仅更新业务代码且数据库基线一致时，正常重复启动仍复用现有库。

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

`down` 不加 `-v`，保留数据库卷和主机目录。先拉取成功再停止旧服务。初始化服务使用维护权限核对当前 schema 和模型，业务 API 只获得业务权限；旧库或初始化失败时不会继续启动 API。当前没有旧 schema 兼容升级链，不能只换回旧镜像就认定回退完成。

已有 `-1` 名称的部署采用新版命名时，在原目录保存旧配置并创建完整备份，用旧配置执行 `down`（不加 `-v`），再合并新版镜像输出的 `container_name` 配置；保留原项目名、镜像选择、端口、挂载及卷。重新 `up -d --wait` 后核对登录和业务数据，再按第一节清理成功退出的初始化容器。只推送源码不会修改已安装容器或已发布镜像。

## 5. 日常灾备与恢复

网页进入 **系统管理 → 备份与恢复**：

- **备份计划**：默认关闭；设置恢复凭证后可立即备份，或启用每天／每周计划、设置保留份数和天数。
- **备份位置**：选择本机、登记目录/移动盘、WebDAV 或 S3，保存后核验目标能力。恢复点直接写入所选仓库，外部故障不回退本机。
- **恢复点**：查看已提交的完整清单，下载 .edmpoint 或选择用于恢复；保留与仓库清理单独预检。
- **整机迁移**：导出自包含 `.edmpoint` 恢复点文件，或选择已登记的新格式恢复仓库。
- **恢复与校验**：选择新格式恢复点或导入 `.edmpoint`／数据库快照，先预检。业务库不可用时使用独立灾备维护入口。

网页提示恢复已暂存后，在原 Compose 目录逐条执行：

```sh
docker compose stop application
docker compose run --rm --no-deps restore
docker compose up -d --no-deps --wait application
```

第二条失败时保持 API 停止，保留待恢复标记、安全副本和日志，处理原因后重试。普通重启不会代替恢复维护。Docker 管理面板若能单独停应用、一次性运行原项目的 `restore` 服务并查看退出码，可以用面板完成同样的操作；不要为此重部署整个项目。业务库不可用时，在停止 application 后执行 `docker compose --profile maintenance up -d maintenance`，访问部署主机 `http://127.0.0.1:5189/#/maintenance`，凭据取自部署目录的 `maintenance-token.txt`；完成后停止维护容器并启动 application。恢复后用备份中的账号登录，核对数据、附件、模板、PDF 和权限。

更详细的大包导入、跨服务器迁移及失败处理见[恢复指南](./服务器备份迁移与恢复指南.md)。

## 6. 已有源码脚本部署

旧 `scripts/run-native-docker.ps1` 入口仍供已经采用它的部署和开发验证使用，继续使用原 RuntimeRoot、项目名和卷。它不是新版镜像安装的前置条件。不要直接在已有生产库旁启动上述空目录方案；切换部署方式应先创建完整迁移包，在隔离目标恢复并验收。源码构建参数见 [scripts/README](../scripts/README.md)。

## 7. 独立附件磁盘与 S3

**系统管理 → 系统设置 → 运行与数据库 → 文件与存储**默认使用 `data/Files`。应用与 NAS 分开部署时，只把业务文件迁到 NAS，PostgreSQL 和应用的运行数据留在应用主机。NAS 接入可按部署方式选择：

| 场景 | 不用终端的做法 |
| --- | --- |
| NAS 已提供 S3 兼容服务 | 在 NAS／对象存储管理页预建存储桶和专用凭据，然后在项目网页选「S3 对象存储」并执行「核验并迁移」。不用改 Compose 或挂载。 |
| Docker 跑在 NAS 上 | 在 NAS 管理页创建专用空目录；若 Docker 管理页能编辑 Compose，把下方挂载配置合并到原项目并重新部署，不用启用 NFS。 |
| Docker 跑在另一台机器，NAS 提供 NFS | 在 NAS 管理页创建共享，然后在 Docker 宿主机持久挂载，再在 Compose 中登记。能否全程用网页，取决于部署端是否有相应的管理界面；项目网页不能代为挂载。 |

使用独立本地目录时，先挂载并创建真实目录，授予容器 UID/GID 10001 访问权限。通过 Docker 管理页操作时，在**原 Compose 项目**合并[挂载示例](../deploy/rust-native/file-storage.override.example.yml)中的 `application`、`restore`、`maintenance` 配置及共用挂载定义，并设置 `FILE_STORAGE_PATH` 为实际目录；保留原项目名、数据卷、镜像和连接配置。如果走命令行，将示例保存到部署目录的 `compose.file-storage.yml`，在同目录 `.env` 设置 `FILE_STORAGE_PATH`，例如 `/srv/exportdoc-attachments`，再执行：

```sh
docker compose -f compose.yml -f compose.file-storage.yml config --quiet
docker compose -f compose.yml -f compose.file-storage.yml up -d --wait
```

示例向 application、restore 与 maintenance 同时登记 `attachments` 位置，且禁止 Docker 自动创建不存在的宿主目录。在“迁移存储”选择该位置或输入 `@attachments`，执行核验并迁移。复制可继续，切换时短暂停止业务写入；源文件保留。后续启动和恢复仍使用同一挂载配置；用命令行时带上同样两个 `-f` 参数。挂载消失或身份标记不符会报错，不接管同名空目录。

首次切换前另存完整包；切换后读一份既有附件并试传新文件。以后若只给 NAS 的同一存储池扩容，且共享路径和权限不变、配额允许，无需在项目网页再次迁移。恢复点的本地重建和重新接回 NAS 的步骤见[恢复指南](./服务器备份迁移与恢复指南.md)。

远程存储选择 S3，填写 endpoint、bucket、region、前缀、显式寻址方式和凭据。应用须能访问对象服务；普通数据库快照恢复若继续使用当前 S3 附件库，恢复容器也需要访问该服务。默认 `restore` 仅在内部数据库网络；外部库恢复使用登记挂载示例所示的受控网络，独立 `maintenance` 已显式接入数据库与 web 网络。S3 密钥使用主密钥加密，不从状态接口返回。管理员可在“更新存储凭据”原地轮换，无须迁移；远端认证失效不会阻断管理入口启动，文件操作仍报错。实际厂商兼容性、网络故障和容量须独立验收。默认单文件 100 MiB、传输总期限 24 小时；浏览器目前没有断点续传，`.edmpoint` 也没有分卷，详见[容量与运维边界](./文件存储与容量管理.md)。

## 8. 卸载与重装

### 8.1 保留数据的普通卸载

Linux 镜像部署在原 `compose.yml` 所在目录，用实际部署的同一镜像版本生成并运行卸载脚本：

```sh
docker run --rm ghcr.io/sck03/exportdoc-rust-native:<已发布版本> --print-uninstall > uninstall.sh
sh ./uninstall.sh
```

脚本移除本部署容器和网络，保留数据库卷、附件、配置、主密钥、备份与镜像。Windows 或不使用 shell 的部署可在原目录执行 `docker compose --file compose.yml --profile maintenance down`；使用挂载扩展时加上同一 `--file compose.file-storage.yml`。重复执行不会把数据清空；不要加 `-v` 或运行全局 prune。

仓库源码部署使用 `.\scripts\uninstall-native-docker.cmd -RuntimeRoot <原目录> -NoPause`，或 `run-native-docker.ps1 -Uninstall`；它针对源码脚本的固定项目，不能用于另一个镜像安装项目。重装复用原目录、配置、项目名和同一受支持 schema 的卷；彻底删除数据属于单独运维操作，本入口不提供该开关。

### 8.2 初始化失败：旧数据库卷与新密码不匹配

当 `postgres` 为 healthy，但 `initialize` 退出码为 1、`application` 停留在 Created 时，先检查初始化和数据库日志，不能仅凭退出码判断为模型指纹问题。以下 Linux 命令以部署目录 `/opt/rustdoc`、默认项目名 `exportdoc` 为例；自定义目录、容器名或 Compose 扩展文件时，使用实际部署的名称和同一组 `-f` 参数。

```sh
cd /opt/rustdoc &&
docker compose logs --no-color --tail=100 initialize postgres &&
docker inspect exportdoc-postgres --format '{{range .Mounts}}{{printf "%s | %s | %s -> %s\n" .Type .Name .Source .Destination}}{{end}}'
```

本次故障的关键日志为：

```text
Rust API 启动或运行失败：PostgreSQL 数据库操作失败。 SQLSTATE=28P01
PostgreSQL Database directory appears to contain a database; Skipping initialization
FATAL: password authentication failed for user "exportdoc_maintenance"
```

`28P01` 表示密码认证失败，尚未进入数据库模型检查；`Skipping initialization` 单独出现只是复用已有数据库的正常提示。`pg_isready` 健康检查确认数据库可接受连接，不验证维护账号的密码。若日志明确提示“数据库模型指纹或结构不匹配”，按第 4 节处理，不要删除指纹记录或绕过初始化。

数据库保存在 Docker 的独立命名卷中。默认卷名为 `exportdoc_postgres_data`，常见挂载源为 `/var/lib/docker/volumes/exportdoc_postgres_data/_data`，实际名称和位置以 `docker inspect` 输出为准。删除容器、执行普通卸载或清空 `/opt/rustdoc` 都不会删除这个卷。手动删除 `config` 后，`setup` 会为新的空配置目录生成新密码；旧卷中的账号仍使用原密码，PostgreSQL 不会对已有数据库重新执行初始化脚本，因此新维护连接认证失败。

**需要保留旧数据时，不执行下方删卷命令。** 优先恢复与旧数据库配套的原 `config`，保留原卷、附件、密钥和备份；原配置缺失时先完成独立的凭据恢复。密码修复后仍须通过当前模型检查，不代表旧实验数据库自动获得兼容性。排查时不要公开配置中的密码、连接串或初始化口令。

**仅在确认旧测试数据库可以永久删除、准备重新建库时执行以下步骤。** `docker volume rm` 会删除指定卷内的旧账号、OA 申请、单据等数据库记录。先核实挂载检查中的卷名确为 `exportdoc_postgres_data`，且它属于本部署；其他卷名须精确替换，不使用全局卷清理或手动删除 Docker 数据目录。

```sh
cd /opt/rustdoc &&
docker compose --profile maintenance down &&
docker volume rm exportdoc_postgres_data &&
docker compose up -d --wait &&
docker compose ps -a
```

`&&` 确保任何一步失败就停止。若卷仍被容器占用，先核对 `docker ps -a --filter volume=exportdoc_postgres_data` 的结果，不强制删除其他容器。保留本次 `setup` 已核对成功的 `config` 和 `data`；新卷会按当前配置重新建立角色和业务库。该操作只重建数据库，不删除现有附件、密钥或备份，也不会恢复之前删除的文件或旧数据库记录。

成功时，`docker compose ps -a` 应显示：

| 服务 | 预期状态 |
| --- | --- |
| `setup` | `Exited (0)` |
| `initialize` | `Exited (0)` |
| `postgres` | `Up ... (healthy)` |
| `application` | `Up ... (healthy)`，默认发布端口 5188 |

2026-11-26 Debian 13 现场回执确认：删除已核实的旧测试卷后，Compose 创建新卷，两项初始化服务均退出 0，PostgreSQL 与应用均 healthy。该状态证明初始化和服务健康检查通过；随后按第 1 节完成首次管理员登录并核对需要的业务功能。若仍失败，保留现场并重新采集本节开头的日志，不重复盲目删卷。
