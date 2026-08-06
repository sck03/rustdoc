# 非 Docker 浏览器服务器版

该发布包把 React 页面、ASP.NET Core API、Chrome Headless Shell 和稳定程序资源合并到同一目录，不需要 Docker，也不需要 Nginx。运行时仍需连接目标环境已经安装并初始化的 PostgreSQL 18。它适合公司内网直接提供 HTTP，也可以放在 IIS、Caddy、Apache 或 Nginx 后面由外部组件终止 HTTPS。

## 一键初始化并启动

初始化脚本只负责生成程序配置、运行数据根和本地环境文件，不会偷偷安装 PostgreSQL、修改防火墙或注册系统服务；这样不会把企业现有运维策略覆盖掉。数据库应先由管理员安装、创建账号并允许服务器连接。

### Windows

普通用户可直接双击 `setup-windows.cmd`。脚本会检查 PowerShell 7、隐藏输入 PostgreSQL 密码、自动生成首次部署令牌，并询问当前服务器是否位于可信办公网/VPN；配置完成后立即启动。以后可双击 `start-windows.cmd` 再次启动。

需要自定义数据库地址、监听地址或数据盘时，在发布包根目录打开 PowerShell：

```powershell
pwsh -File .\initialize-windows.ps1 `
  -PostgreSqlHost "127.0.0.1" `
  -PostgreSqlPort 5432 `
  -PostgreSqlDatabase "exportdoc" `
  -PostgreSqlUsername "exportdoc" `
  -DataRoot "D:\ExportDocManagerData" `
  -AllowHttpDisasterRecovery `
  -Start
```

不传 `-PostgreSqlPassword` 时会安全提示输入，不进入 PowerShell 历史或进程参数；不传 `-BootstrapToken` 时使用系统加密随机数生成器自动生成并只显示一次。非交互自动化仍可显式传入这两个参数。不传 `-Start` 时只写配置，之后可运行 `pwsh -File .\start-windows.ps1`。通过 IIS/Caddy/Nginx 反向代理时，可用 `-TrustedProxies "192.168.1.20"` 指定直接代理 IP；HTTPS 部署不要传 `-AllowHttpDisasterRecovery`。配置完成后，数据库连接参数写入数据根的 `Config\appsettings.json`，其中密码字段保持为空；数据库密码、首次部署令牌和运行参数写入 `Security\browser-server.env`。发布包根只保留只读示例 `appsettings.example.json` 和一个用于定位外部数据根的小型 `browser-server.env.path`。脚本拒绝磁盘/共享根、普通文件，以及 DataRoot、`Config`、`Security` 或其任一现存祖先中的符号链接/联接点；配置通过同目录临时文件原子替换，并会尽力关闭目录/文件 ACL 继承，显式授予当前管理员、Administrators 与 LocalSystem。已有有效配置时脚本默认拒绝覆盖，管理员确认后才使用 `-Force`；以后如改用其它 Windows 服务账号，应由管理员显式授予该账号读取权限。

### Linux

在发布包根目录执行：

```sh
./initialize-linux.sh \
  --postgres-host 127.0.0.1 \
  --postgres-port 5432 \
  --postgres-database exportdoc \
  --postgres-user exportdoc \
  --data-root /srv/exportdoc-manager-data \
  --allow-http-disaster-recovery \
  --start
```

未传 `--postgres-password` 时脚本从 `/dev/tty` 隐藏输入；未传 `--bootstrap-token` 时使用 OpenSSL 或 `/dev/urandom` 自动生成并只显示一次。非交互自动化仍可显式传参或注入环境变量。不传 `--start` 时只写配置，之后可运行 `./start-linux.sh`。脚本拒绝把数据根设置为 `/`，也拒绝 DataRoot、`Config`、`Security`、环境文件及任一现存祖先中的符号链接或普通文件；使用 `umask 077`，目录权限固定为 `700`，`appsettings.json`、`browser-server.env` 与定位指针通过临时文件原子替换并设为 `600`。已有有效配置时默认拒绝覆盖，只有管理员确认后才使用 `--force`。Linux ARM64 会按当前运行时能力默认关闭不支持的 Paddle OCR 验证；其它架构仅在程序版本或架构变化后执行一次真实 OCR 验证，版本或架构缺失时每次启动都重验而不写“unknown”缓存。成功标记保存在 DataRoot 的 `Cache/RuntimeVerification/`，清理缓存后可自动重验。

数据库密码解析优先级固定为：`EXPORTDOCMANAGER_POSTGRES_PASSWORD_FILE`、`EXPORTDOCMANAGER_POSTGRES_PASSWORD`、`appsettings.json` 中由程序生成的 AES-GCM 受保护载荷。相对密码文件路径只允许位于当前 DataRoot 的 `Security/` 下；不要在 `appsettings.json` 保存明文密码。初始化脚本默认使用权限受限的 `browser-server.env`，企业 secret 管理器也可以改为注入密码文件。网页恢复会创建临时验证数据库，因此数据库账号除目标业务库权限外还必须具备 `CREATEDB`。

## 访问和 HTTPS 边界

默认监听 `http://0.0.0.0:5188`，公司内网可以直接访问 `http://服务器地址:5188`，但应由防火墙只允许办公网/VPN 网段访问。HTTP 会明文传输登录和业务数据，不应把端口转发到公网或不可信网络。

网页备份恢复和完整迁移在纯 HTTP 下默认关闭。只有确认服务器位于可信办公网/VPN 时，才在初始化阶段使用 Windows 的 `-AllowHttpDisasterRecovery` 或 Linux 的 `--allow-http-disaster-recovery`；脚本会把显式选择保存为 `EXPORTDOCMANAGER_ALLOW_INSECURE_DISASTER_RECOVERY=true`。公网、访客网络和跨互联网访问必须使用 HTTPS，并保持该值为 `false`。

公网、跨网段或有合规要求时，在该程序前配置可信 HTTPS 反向代理，并把代理直接连接本程序的固定 IP 写入 `EXPORTDOCMANAGER_TRUSTED_PROXIES`（初始化脚本的 `-TrustedProxies/--trusted-proxies`）。反向代理负责证书、HSTS、外部访问控制和可选静态缓存；API 仍负责登录、权限、数据归属和审计。非 Docker 包不使用 Compose 容器网段，也不需要 `EXPORTDOCMANAGER_CONTAINER_SUBNET` 或 Nginx 固定地址。

## 网页端备份与完整迁移

管理员登录后进入“系统设置 -> 维护 -> 团队库”。发布包已携带 PostgreSQL 18 的 `pg_dump`、`pg_restore` 和 `psql`，工具位于程序目录 `Tools/PostgreSQL/`，不要求把客户端安装到系统盘或使用全局 PATH。Linux 客户端从 `postgres:18.4-bookworm` 提取，正式兼容边界是 Debian 12/Bookworm 或兼容的 glibc 环境；其它发行版应在目标机完成工具启动与恢复演练。

网页端支持：

- 后台创建并校验 PostgreSQL custom-format `.dump` 备份，页面关闭后任务仍可在任务中心查看或取消；
- 从服务器已有备份下载，或上传 `.dump` 后输入 `RESTORE DATABASE` 排队恢复；
- 输入强迁移密码创建加密 `.edmmigration` 完整迁移包，或上传该包并输入 `MIGRATE` 排队恢复。

完整迁移包包含 PostgreSQL 业务库、运行配置、印章、唛头图片和其它业务文件、用户模板、单一窗口数据及本地主密钥。它不包含日志、缓存、临时导出文件、历史备份、许可证、机器绑定试用数据，也不包含 TLS/Certbot 证书。恢复前会把当前数据库和受管文件保存到数据根 `Backups/ServerMigration/Safety/`，再在下一次启动、建立数据库连接前执行数据库恢复和文件原子替换；失败时自动回滚。目标服务器若设置了 `EXPORTDOCMANAGER_MASTER_KEY`，必须与迁移包中的主密钥一致，否则恢复会在覆盖数据库前中止。

大型 `.dump` 下载使用五分钟短期票据和 HTTP Range 流式传输，不会先把整个文件读入浏览器内存；票据签发后仍会在每次请求时重新确认当前连接为 HTTPS/显式可信 HTTP、文件位于受管备份目录、路径没有符号链接或重解析点且文件尚未被清理。

非 Docker 部署不会自行重启进程。恢复操作返回后，请通过 systemd、Windows 服务、IIS 或其它进程管理器重启 API；若没有进程管理器，手动停止后重新运行 `start-linux.sh` 或 `start-windows.ps1`。TLS 证书属于部署层状态，迁移到新主机后应由反向代理重新签发或由管理员单独安全部署。

桌面端 updater 的 `latest.json` 地址由桌面管理员在系统设置中单独维护，可指向 GitHub、自建 HTTPS 或受控内网 HTTP。浏览器服务器包只托管 Web/API，不执行 Tauri 更新，也不应保存 updater 私钥；公钥由桌面正式安装包固定携带。

数据库、日志、缓存、备份、业务文件和默认生成的 `Security/local-master-key.bin` 均写入配置的数据根，不写系统用户目录。若设置 `EXPORTDOCMANAGER_MASTER_KEY`，该 32 字节 Base64/64 位十六进制密钥必须长期安全备份，不能随意更换。正式运行建议再由企业服务管理器（Windows 服务、systemd 或容器平台）负责进程拉起和日志轮换，但不要把服务注册动作隐藏在初始化脚本中。
