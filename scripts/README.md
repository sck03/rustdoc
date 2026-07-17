# 脚本使用说明

## GitHub 公开发布

- `github/verify-public-source.ps1`：上传前检查注册机、私钥、内部 `KEY/` 产物和 GitHub 大文件边界。
- `github/initialize-github-repository.ps1`：初始化 `main` 分支、暂存公开文件，可选配置 origin、创建提交和推送；默认不会提交或联网推送。

普通用户只需要使用 `scripts/` 根目录下的以下批处理入口，不要直接运行 `lib/`、`prepare-*`、`verify-*` 或 `assert-*`：

| 入口 | 用途 |
| --- | --- |
| `build-windows-desktop-run.cmd` | 构建一个 Windows 便携运行目录，默认全功能版 |
| `build-windows-editions.cmd` | 构建单证员版、业务员版、全功能版三个便携目录 |
| `build-windows-installers.cmd` | 构建三个 Windows NSIS 安装包 |
| `run-tests.cmd` | 先核查全部脚本，再运行完整 .NET 测试 |

公开/客户构建默认不生成内部注册机。只有本机保留私有 `apps/license-keygen-tauri/` 源码并显式向 PowerShell 构建脚本传入 `-IncludeLicenseKeygen` 时，才会把内部工具整理到客户目录之外的 `KEY/`。

公开仓库不提交 Chromium 二进制。`run-tests.ps1` 找不到程序根 Chromium 或 `EXPORTDOCMANAGER_CHROMIUM_EXECUTABLE` 时，会明确跳过两个真实 PDF 浏览器测试；正式发布验收使用 `-RequireBrowserPdfTests`，缺少渲染器即失败。测试默认执行 restore，只有确认依赖已还原时才使用 `-NoRestore`。

当前不使用 `.github/dependabot.yml` 自动创建依赖更新 PR。NuGet、npm、Cargo、Docker 和 Actions 版本由维护者集中审查后人工升级，避免一次更新触发大量分支和云端构建。

双击 `.cmd` 后窗口会一直保留，最后明确显示成功或失败及退出码，按任意键关闭。构建环境有问题时，错误信息不会一闪而过。

正式构建前可先在终端运行只读预检：

```powershell
./scripts/build-windows-desktop-run.cmd -PreflightOnly
./scripts/build-windows-editions.cmd -PreflightOnly
./scripts/build-windows-installers.cmd -PreflightOnly
```

自动化或已有终端不希望暂停时：

```powershell
$env:EXPORTDOCMANAGER_NO_PAUSE = "1"
./scripts/build-windows-editions.cmd -PreflightOnly
```

开发或审查时可单独执行完整脚本门禁：

```powershell
pwsh -NoProfile -File ./scripts/verify-script-suite.ps1
```

该门禁递归检查全部 `.ps1`、`.cmd` 和 `.mjs`：PowerShell AST、Node 语法、CMD 薄入口/共享宿主、危险系统路径模式，以及原生命令退出码是否统一处理。`run-tests.cmd` 会自动先执行该门禁。

注意：`artifacts/windows-desktop-run/` 是一次性构建输出。正式便携构建不检测、不询问，也不保留旧运行数据，会直接删除目标版本目录中的 `App_Data/` 和 `logs/` 后覆盖。需要保留的开发数据请在运行构建脚本前自行备份；不要把真实业务数据库放在该构建目录中长期使用。
