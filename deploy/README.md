# 部署文件说明

`rust-native/` 是当前 React + Rust HTTP + PostgreSQL 18 的 Docker 部署源，仍被本地入口、验证和发布工作流引用，不能整体删除。

| 文件 | 用途 |
| --- | --- |
| [Dockerfile](rust-native/Dockerfile) | 构建 React、Rust 服务及受管字体/PDFium/OCR/数据库客户端资源 |
| [compose.yml](rust-native/compose.yml) | application/postgres 常驻服务、独立初始化与恢复服务、内部网络及持久卷 |
| [postgres-init.sh](rust-native/postgres-init.sh) | 首次数据库角色初始化，区分业务与维护身份 |
| [deployment-assets.sha256](rust-native/deployment-assets.sha256) | 发布部署文件的完整性清单，相关源变更后按现有发布流程同步 |
| `rust-native/runtime/`（Git 忽略） | 本地部署凭据和配置；属于已有部署身份，不是可随意重建的测试缓存 |

用户入口为 [scripts/run-native-docker.ps1](../scripts/run-native-docker.ps1)，启动先构建、停 API、维护数据库，再启动 API。不要用裸 compose up 跳过维护流程，也不要删除数据卷或 runtime 目录重新生成凭据接管旧卷。

桌面包不使用 Docker；旧 deploy/container 已退役。详细启动、升级、备份与恢复见[脚本说明](../scripts/README.md#网页与-docker)，公开发布见[镜像说明](../docs/GitHub开源发布与Docker镜像说明.md)。
