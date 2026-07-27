# 非 Docker 浏览器服务器版

该发布包把 React 页面、ASP.NET Core API、Chrome Headless Shell 和稳定程序资源合并到同一目录，不需要 Docker，也不需要 Nginx。运行时仍需连接目标环境已经安装并初始化的 PostgreSQL 18。它适合公司内网直接提供 HTTP，也可以放在 IIS、Caddy、Apache 或 Nginx 后面由外部组件终止 HTTPS。

## 一键初始化并启动

初始化脚本只负责生成程序配置、运行数据根和本地环境文件，不会偷偷安装 PostgreSQL、修改防火墙或注册系统服务；这样不会把企业现有运维策略覆盖掉。数据库应先由管理员安装、创建账号并允许服务器连接。

### Windows

在发布包根目录打开 PowerShell：

```powershell
pwsh -File .\initialize-windows.ps1 `
  -PostgreSqlHost "127.0.0.1" `
  -PostgreSqlPort 5432 `
  -PostgreSqlDatabase "exportdoc" `
  -PostgreSqlUsername "exportdoc" `
  -PostgreSqlPassword "请替换为长随机数据库密码" `
  -BootstrapToken "请替换为另一段至少24位的随机首次部署令牌" `
  -Start
```

不传 `-Start` 时只写配置，之后可运行 `pwsh -File .\start-windows.ps1`。需要把数据放在其它磁盘时增加 `-DataRoot "D:\ExportDocManagerData"`；通过 IIS/Caddy/Nginx 反向代理时，可用 `-TrustedProxies "192.168.1.20"` 指定直接代理 IP。配置完成后，数据库密码写入 `appsettings.json`，首次部署令牌和运行参数写入指定数据根的 `Security\browser-server.env`，包根的 `browser-server.env.path` 只保存该文件位置；脚本会尽力把数据库配置和环境文件 ACL 收紧为当前管理员、Administrators 与 LocalSystem 可访问。已有有效配置时脚本默认拒绝覆盖，管理员确认后才使用 `-Force`；以后如改用其它 Windows 服务账号，应由管理员显式授予该账号读取权限。

### Linux

在发布包根目录执行：

```sh
./initialize-linux.sh \
  --postgres-host 127.0.0.1 \
  --postgres-port 5432 \
  --postgres-database exportdoc \
  --postgres-user exportdoc \
  --postgres-password '请替换为长随机数据库密码' \
  --bootstrap-token '请替换为另一段至少24位的随机首次部署令牌' \
  --start
```

不传 `--start` 时只写配置，之后可运行 `./start-linux.sh`。数据目录可通过 `--data-root /srv/exportdoc-manager-data` 指定；脚本使用 `umask 077`，并把包含数据库密码的 `appsettings.json` 与含令牌的环境文件权限设为 `600`，包根的 `browser-server.env.path` 只保存环境文件位置。已有有效配置时默认拒绝覆盖，只有管理员确认后才使用 `--force`。Linux ARM64 会按当前运行时能力默认关闭不支持的 Paddle OCR 验证，但仍保留 HS、发票和报表等其它服务能力；如现场有兼容 OCR 运行库，可显式设置 `EXPORTDOCMANAGER_OCR_RUNTIME` 后再启动。

## 访问和 HTTPS 边界

默认监听 `http://0.0.0.0:5188`，公司内网可以直接访问 `http://服务器地址:5188`，但应由防火墙只允许办公网/VPN 网段访问。HTTP 会明文传输登录和业务数据，不应把端口转发到公网或不可信网络。

公网、跨网段或有合规要求时，在该程序前配置可信 HTTPS 反向代理，并把代理直接连接本程序的固定 IP 写入 `EXPORTDOCMANAGER_TRUSTED_PROXIES`（初始化脚本的 `-TrustedProxies/--trusted-proxies`）。反向代理负责证书、HSTS、外部访问控制和可选静态缓存；API 仍负责登录、权限、数据归属和审计。非 Docker 包不使用 Compose 容器网段，也不需要 `EXPORTDOCMANAGER_CONTAINER_SUBNET` 或 Nginx 固定地址。

数据库、日志、缓存、备份、业务文件和默认生成的 `Security/local-master-key.bin` 均写入配置的数据根，不写系统用户目录。若设置 `EXPORTDOCMANAGER_MASTER_KEY`，该 32 字节 Base64/64 位十六进制密钥必须长期安全备份，不能随意更换。正式运行建议再由企业服务管理器（Windows 服务、systemd 或容器平台）负责进程拉起和日志轮换，但不要把服务注册动作隐藏在初始化脚本中。
