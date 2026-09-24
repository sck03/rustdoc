# GitHub 开源发布与 Docker 镜像说明

> 2026-09-23。当前交付为 React + Rust，桌面使用 Tauri；旧 ASP.NET/Chromium sidecar 发布入口已经退役。

## 公开边界

仓库公开源码、契约、测试、资源清单和许可说明。客户文件、业务数据库、真实配置、私钥、注册机、运行日志、缓存与大型原生资源不提交。原生归档从中央清单的核验来源准备，不引入 NuGet 客户端或 .NET 运行依赖。

提交前运行 `scripts/github/verify-public-source.ps1` 并检查暂存区。源码推送与发布包、镜像发布是不同动作，发布入口见[工作流手册](./GitHub%20Actions工作流用途与运行手册.md)。

## 产品入口

| 形态 | 本地脚本 | 运行要求 |
| --- | --- | --- |
| 桌面 Full | build-native.ps1 / run-native.ps1 | Tauri、平台 WebView、Rust、SQLite；正式包默认 OCR |
| 网页服务器 | package-native-web-server.ps1 | React + Rust HTTP、PostgreSQL 18 |
| Docker | run-native-docker.ps1 | 同一 React、Rust HTTP、PostgreSQL 18 容器 |

普通用户使用 scripts 根目录入口，详细参数见[脚本说明](../scripts/README.md)。当前仅开放 Full 打包；其它产品的裁剪、更新与权限须单独完成 Rust 验收。

## 容器与团队模式

Docker 使用 `deploy/rust-native`，本地私密运行配置位于其忽略的 runtime 目录。默认只绑定回环地址；局域网和公网绑定由部署者明确配置。首次管理员初始化需要私有 bootstrap token，日常 API 使用受限 PostgreSQL 业务账号；数据库维护角色与运行账号分离。

桌面 SQLite 不能代替团队 PostgreSQL。当前支持单 API、多浏览器用户，不支持把多个 API 指向同一业务库当作高可用部署。数据库、受管文件、备份与配置一起按现有恢复流程验证。

`rust-native-container-release.yml` 先做生命周期验收，只有显式 publish 才发布 GHCR。镜像名称、tag、平台与产物以当次工作流和清单为准，不沿用已删除的 container-images.yml 或旧三镜像版本提升流程。

## 验收

在对应平台完成启动、认证/授权、资源检查、上传下载、实际 PDF、任务取消、备份恢复、停止重启和卷持久化。跨平台编译、端点存在和历史 C# 测试不能替代这些结果。

系统级 Windows/macOS 签名与 Apple 公证不执行；Tauri updater 的独立签名信任合同保留。推送 main 不自动生成或发布新的安装包。
