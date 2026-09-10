# WebView2 Runtime 发布资产

Windows 绿色版在启动窗口前会检查 Microsoft Edge WebView2 Runtime。

把微软官方 Evergreen Standalone Installer 放在本目录，构建脚本会按受 Git 跟踪的 `webview2-runtime.json` 固定清单校验版本、体积、SHA-256、微软数字签名和文件元数据，再将它复制到 Windows x64 便携包。当前发布文件名为：

- `MicrosoftEdgeWebView2RuntimeInstallerX64.exe`
安装器是约 203 MiB 的第三方发布二进制，不提交到 Git；仅小型校验清单和本说明进入仓库。需要升级 Evergreen 安装器时，应从微软官方 WebView2 下载页获取 x64 离线版，审核后一次性更新清单中的固定 HTTPS 地址、版本、大小和 SHA-256：

<https://developer.microsoft.com/microsoft-edge/webview2/>

绿色版先检查 Windows 版本和 Runtime。当前支持基线为 Windows 10 1809（内部版本 17763）或更高版本；发现 Runtime 缺失时，在不依赖 WebView 的 Windows 原生窗口中点击“安装并启动”，即可使用随包微软安装器离线安装并显示进度，成功后自动进入程序。重复双击不会同时启动多个安装器；取消、安装繁忙、失败和需要重启分别处理。安装等待超过 15 分钟或进程状态读取失败时清理本次进程树，失败弹窗提供诊断日志位置。

下载暂存文件使用 `.download` 后缀，验证依靠固定 SHA-256、体积、微软签名和 PE 元数据，校验通过后才改成正式文件名。临时文件名不改变内容可信要求。没有随包安装器时，程序会提示重新运行产品安装包或完整解压便携包，并给出微软官方下载地址。

普通联网客户优先使用 NSIS 安装版，由 Evergreen bootstrapper 自动检测并补齐 WebView2；离线交付可使用完整便携包。固定版本 WebView2 不随本项目打包；Evergreen Runtime 由 Windows 统一维护和更新，可能按 Windows 权限策略要求安装确认。
