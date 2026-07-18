# 非 Docker 浏览器服务器版

该发布包把 React 页面、ASP.NET Core API、Chrome Headless Shell 和稳定程序资源合并到同一目录，不需要 Docker。运行时仍需连接原生安装的 PostgreSQL。

1. 安装 PostgreSQL 18，创建数据库和账号。
2. 编辑包内 `appsettings.json`，替换数据库地址、账号和 `CHANGE_ME_BEFORE_START` 密码。
3. Windows 运行 `pwsh -File start-windows.ps1`；Linux 运行 `./start-linux.sh`。
4. 浏览器访问 `http://服务器地址:5188`。

数据库、日志、缓存、备份和业务文件默认写入发布目录的 `App_Data/`。正式公网部署应在程序前配置 Caddy、Nginx 或 IIS HTTPS 反向代理，并限制防火墙访问范围。
