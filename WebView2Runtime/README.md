# WebView2 Runtime 发布资产

Windows 绿色版在启动窗口前会检查 Microsoft Edge WebView2 Runtime。

把微软官方 Evergreen Standalone Installer 放在本目录，构建脚本会按受 Git 跟踪的 `webview2-runtime.json` 固定清单校验版本、体积、SHA-256、微软数字签名和文件元数据，再将它复制到 Windows x64 便携包。当前发布文件名为：

- `MicrosoftEdgeWebView2RuntimeInstallerX64.exe`
安装器是约 203 MiB 的第三方发布二进制，不提交到 Git；仅小型校验清单和本说明进入仓库。需要升级 Evergreen 安装器时，应从微软官方 WebView2 下载页获取 x64 离线版，审核后一次性更新清单中的固定 HTTPS 地址、版本、大小和 SHA-256：

<https://developer.microsoft.com/microsoft-edge/webview2/>

绿色版先检查 Windows 版本和 Runtime。当前支持基线为 Windows 10 1809（内部版本 17763）或更高版本；发现 Runtime 缺失时，会提示用户确认，然后打开随包的微软安装程序。安装完成后会再次检测；若 Windows 尚未刷新组件状态，程序会提示注销或重启后再运行。

普通客户优先使用 NSIS 安装版。固定版本 WebView2 不随本项目打包；Evergreen Runtime 由 Windows 统一维护和更新。
