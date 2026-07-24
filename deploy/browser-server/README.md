# 非 Docker 浏览器服务器版

该发布包把 React 页面、ASP.NET Core API、Chrome Headless Shell 和稳定程序资源合并到同一目录，不需要 Docker。运行时仍需连接原生安装的 PostgreSQL。

1. 安装 PostgreSQL 18，创建数据库和账号。
2. 编辑包内 `appsettings.json`，替换数据库地址、账号和 `CHANGE_ME_BEFORE_START` 密码。
3. 设置至少 24 位、与数据库密码和管理员密码不同的首次部署令牌：Windows PowerShell 使用 `$env:EXPORTDOCMANAGER_BOOTSTRAP_TOKEN="请替换为长随机令牌"`；Linux 使用 `export EXPORTDOCMANAGER_BOOTSTRAP_TOKEN='请替换为长随机令牌'`。
4. Windows 运行 `pwsh -File start-windows.ps1`；Linux 运行 `./start-linux.sh`。
5. 浏览器访问 `http://服务器地址:5188`，展开“服务器连接设置”，首次以 `admin` 建立管理员时填写同一令牌。令牌只在页面内存中保留到登录成功，不写浏览器存储；数据库已有用户后普通登录无需填写。

数据库、日志、缓存、备份、业务文件和默认生成的 `Security/local-master-key.bin` 均写入发布目录的 `App_Data/`。如改用 `EXPORTDOCMANAGER_MASTER_KEY`，该 32 字节 Base64/64 位十六进制密钥必须长期安全备份，不能随意更换。正式公网部署应在程序前配置 Caddy、Nginx 或 IIS HTTPS 反向代理，并限制防火墙访问范围。
