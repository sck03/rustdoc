# GitHub Actions 工作流用途与运行手册

> 2026-09-25 按当前六份 workflow 整理。已删除的 C# 打包、Browser sidecar 和旧可复用工作流不再列作入口。

| 工作流 | 触发与用途 | 结果边界 |
| --- | --- | --- |
| [Rust 验证](../.github/workflows/rust-native-validation.yml) | 相关路径 PR 或手工触发；生成契约、测试、桌面/服务端依赖与构建 | 五个桌面 target 分别记录；构建不替代设备验收 |
| [Rust 桌面包](../.github/workflows/rust-native-desktop-release.yml) | 手工；Windows x64/ARM64、Linux x64/ARM64、macOS ARM64 | Full 便携包及 NSIS、deb/AppImage、app/dmg Artifact |
| [Rust 网页服务包](../.github/workflows/rust-native-web-server-release.yml) | 手工；React 与 Rust HTTP 服务 | 需 PostgreSQL 18；不带 Tauri 桌面壳 |
| [Rust 容器](../.github/workflows/rust-native-container-release.yml) | 手工；生命周期验收，显式 publish 才发布 GHCR | 未选 publish 的运行不是生产镜像发布 |
| [依赖治理](../.github/workflows/dependency-governance.yml) | 文件中指定的 push/PR/定时/手工触发 | npm/Cargo/原生资源审计、notices、SBOM |
| [浏览器兼容](../.github/workflows/browser-compatibility.yml) | 仅手工 | Firefox/WebKit；不进入每次提交的普通检查 |

## 运行

桌面和服务包先归档再上传：Windows ZIP，Linux/macOS tar.gz，保留 Unix 执行权限与 .app 链接结构。网页服务包包含 PostgreSQL 18 客户端和对应许可，Full 构建默认带 OCR。容器 amd64/arm64 分别在原生 runner 构建和验证，再合并多架构索引；不依赖未配置的跨架构模拟器。

在 Actions 选择上表实际存在的工作流和分支后运行。桌面和服务端构建下载 Artifact；源码推送不自动等同发布安装包或新版镜像。平台、架构、资源与许可清单仍须在产物中核对。

本地入口在[脚本说明](../scripts/README.md)。当前 Rust 打包不调用 dotnet restore/publish，不需要 .NET Runtime 或 Node 运行时。Full 默认包含已实现的 OCR 资源；WithoutOcr 只用于明确的轻量检查包。

## 失败定位

先检查首个真实退出码及步骤日志。编译问题按目标工具链定位；依赖/字体/原生归档检查失败时验证固定版本、哈希和许可，不能跳过治理。网络下载失败与实际依赖问题分别记录。缓存和临时目录应位于 runner workspace；不要把取消或 ignored 的用例记为通过。

依赖变更后运行 release 治理并核对 unresolved=0、disallowed=0。跨平台门禁应审查真实 Cargo 依赖图：桌面包含 Tauri/WebView、SQLite，排除 PostgreSQL 服务端适配器、Node、.NET 和已退役 GUI。

## 发布与签名

系统级 Authenticode、Developer ID、Apple 公证不执行。Tauri updater 的产物签名与固定公钥校验保留，配置和真实升级验收见[更新合同](./Tauri正式更新签名与发布配置.md)。当前手工桌面工作流提供构建 Artifact，不能沿用旧 workflow 的 publish_release 参数或宣称已发布签名升级通道。

GitHub 上只有成功运行的对应 OS/架构结果才算该项证据；Docker、真实 PostgreSQL、实体打印、官方单一窗口及设备输入法分别验收。
