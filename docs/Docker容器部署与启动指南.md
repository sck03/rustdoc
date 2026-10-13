# Docker 容器部署与启动指南

> 按 2026-10-13 的公开脚本和 Rust 服务核对。适用于 Full 多用户版。首次部署按本文顺序操作；已经在用的系统从“日常启动、停止与更新”开始，保留原配置和数据。

## 1. 先认识要安装的东西

一台服务器运行两个常驻容器：`application` 提供网页和业务服务，`postgres` 保存 PostgreSQL 18 数据。其他人只需浏览器，无需安装 Rust、Node、PostgreSQL 或桌面版。

```text
同事的浏览器 → 服务器的网页端口 → application → postgres
                                      ↓           ↓
                                  app_data    postgres_data
```

| 名称 | 可以怎样理解 | 要保留什么 |
| --- | --- | --- |
| 镜像 | 程序安装包 | 使用的版本号、镜像地址或离线包 |
| 容器 | 正在运行的程序 | 可以重建，业务数据不应只放在容器内 |
| `postgres_data` 卷 | 数据库的数据盘 | 所有业务数据库内容 |
| `app_data` 卷 | 应用的数据盘 | 主密钥、用户文件模板、任务文件和备份 |
| `RuntimeRoot` | 宿主机上的私有配置目录 | 数据库连接、密码、管理员初始化口令及配置标记 |

**备份办法见[服务器备份、迁移与恢复指南](./服务器备份迁移与恢复指南.md)。仅保存镜像或复制源码不能备份业务数据。**

## 2. 准备服务器

前端入口统一为“系统管理 → 系统设置 → 备份与恢复 → 团队库与完整迁移”：

| 操作 | 网页里可以完成 | Linux 部署管理员负责 |
| --- | --- | --- |
| 日常备份 | 创建数据库备份，直接在文件行点击“下载” | 保管异机副本，定期演练 |
| 换机准备 | 展开“完整迁移与换机”，创建并下载完整包 | 新机部署相容版本、数据库和私有配置 |
| 恢复/迁入 | 选择文件、输入密码和确认文本，安排恢复 | 按页面自动展开的维护步骤执行 `-RestorePending` |
| 程序升级 | 创建完整迁移包，查看“服务器恢复与升级步骤” | 取得匹配的新镜像和部署文件，用原参数正常部署 |
| 启动/停止 | 查看可用状态，正常登录/退出 | 在原部署目录执行公开启动/停止命令 |

恢复和镜像升级的最后一步保留在服务器维护端。网页不持有 Docker 宿主控制权或数据库维护凭据；继续只运行应用和 PostgreSQL 两个常驻容器。

1. 准备 Linux x64/ARM64 服务器，或安装 Docker Desktop 并切换到 **Linux containers** 的 Windows 电脑。正式长期运行优先选择固定开机的服务器。
2. 按 [Docker 官方安装说明](https://docs.docker.com/engine/install/)安装 Docker Engine 和 Compose 插件；Windows 可按 [Docker Desktop 安装说明](https://docs.docker.com/desktop/setup/install/windows-install/)办理。
3. 安装 [PowerShell 7](https://learn.microsoft.com/powershell/scripting/install/installing-powershell)。Linux 也使用 `pwsh` 执行仓库脚本；Windows 自带的 PowerShell 5.1 不适用。
4. 规划数据盘和空间。少量用户可先按 2 核、4 GiB 内存、30 GiB 可用磁盘做试部署，这只是起步建议；附件、OCR、数据库和备份会继续占用空间。本机从源码构建通常需要更多内存和磁盘。
5. 将 Docker 的镜像/数据卷存储位置放在计划的数据盘。Windows 在 Docker Desktop 设置中核对磁盘映像位置；Linux 由管理员核对 Docker 数据目录。`RuntimeRoot` 只存配置，不决定 Docker 卷实际位于哪块磁盘。

在终端分别运行：

```powershell
docker version
docker compose version
pwsh --version
```

`docker version` 必须同时有 Client 和 Server 信息。只有 Client、提示无法连接 daemon 或 Docker Desktop 尚未启动时，先解决 Docker 本身。Compose 必须支持 `up --wait` 和 `pull --policy missing`，建议使用受支持的新版 Compose v2 或更高版本。

## 3. 取得与程序版本匹配的部署文件

浏览 [GitHub 仓库](https://github.com/sck03/rustdoc)及 [Releases](https://github.com/sck03/rustdoc/releases)。源码压缩包解压到独立目录，或在装有 Git 时执行。Linux 的 Bash 示例：

```bash
sudo install -d -o "$(id -un)" -g "$(id -gn)" /srv/exportdoc
git clone https://github.com/sck03/rustdoc.git /srv/exportdoc/source
cd /srv/exportdoc/source
pwsh -NoProfile
```

这里仅创建专用部署父目录，不改动已有 PostgreSQL 数据目录。若 source 已存在，先核对原部署，不能重复克隆或覆盖。Windows PowerShell 示例：

```powershell
# Windows 示例。这个目标目录应尚不存在。
git clone https://github.com/sck03/rustdoc.git E:\ExportDoc\Source
Set-Location E:\ExportDoc\Source
```

本文后续命令均在源码根目录的 PowerShell 7 中执行，目录里应能看到 `scripts`、`deploy` 和 `Cargo.toml`。Linux 的 Bash 与 PowerShell 变量语法不同，看到 `$run = @{...}` 时必须已经进入 `pwsh`。

使用发布镜像时，部署文件也应来自该镜像对应的版本/源码提交。不要把另一版本的 Compose、初始化文件与镜像任意混用。当前部署文件清单见 [deploy 说明](../deploy/README.md)。

## 4. 一次设好目录、地址与端口

先运行 `pwsh -NoProfile` 进入 **PowerShell 7**，再复制以下内容（上一步已经进入则不用再开一层）。以下以 Linux 为主；Windows 把第一行改成 `$runtime = 'E:\ExportDoc\DockerRuntime'`。从网页下载源码 ZIP 时，Windows 可先在 ZIP“属性”中解除锁定，再解压可信的版本文件。

```powershell
$runtime = '/srv/exportdoc/runtime'
$run = @{
    RuntimeRoot = $runtime
    Image = 'exportdoc-rust-native:local'
    BindAddress = '127.0.0.1'
    Port = 5188
    NoPause = $true
}
```

- `127.0.0.1` 表示暂时只允许这台服务器自己访问，适合首次检查或放在 HTTPS 代理后。
- `5188` 是网页端口，被占用时可换成另一未占用端口。以后始终使用同一个值。
- `RuntimeRoot` 使用独立、可写、非链接目录；不要指向源码根、磁盘根或已有业务文件夹。
- `$run` 只在当前终端会话有效。关闭终端后重新运行这段配置；把实际参数记在自己保管的部署记录中。

首次生成配置：

```powershell
./scripts/run-native-docker.ps1 @run -PrepareOnly
```

看到 `Native Docker configuration prepared` 表示成功。脚本会在指定目录生成随机凭据和 `native-runtime.json`，不会把密码写进源码或镜像。这个目录以后属于本次部署，须备份并限制访问。已有配置缺文件或内容损坏时，脚本会拒绝继续，不能靠重新生成密码接管旧数据库。

**当前公开入口固定使用 Compose 项目 `exportdoc-rust-native`。同一 Docker 环境只维护一套此部署；更换 RuntimeRoot 不会创建另一套隔离数据库。恢复演练请使用另一台测试机或独立 Docker 环境。**

## 5. 首次启动：下面两种方法选一种

### 方法 A：从当前源码构建

没有现成发布镜像时使用此方法。宿主机不必另装 Rust/Node，构建工具在 Docker 构建阶段运行。首次会下载构建依赖和资源，请耐心等待：

```powershell
./scripts/run-native-docker.ps1 @run
```

保留 Docker 构建缓存可以减少以后构建时间。不要加 `-WithoutOcr`；Docker Full 的正式资源由 Dockerfile 统一准备。

### 方法 B：使用已经发布的镜像

先在 GitHub Packages/发布记录中确认一个实际存在的版本。下面的 `0.1.2` 只是版本写法示例，不能由本文推断该标签已经发布：

```powershell
$run.Image = 'ghcr.io/sck03/exportdoc-rust-native:0.1.2'
./scripts/run-native-docker.ps1 @run -SkipBuild
```

私有包需要先按 GitHub 的方式登录 GHCR；公开包无需登录。固定版本或摘要便于核对和恢复，不建议生产环境依赖不断变化的 `latest`。

两种方法都依次执行：准备镜像 → 停应用 → 启动并等待数据库 → 一次性初始化/升级 → 启应用并检查就绪。镜像构建/取得失败时保留原应用运行；维护失败时不重新启动应用。

成功时末尾显示网页地址。在服务器浏览器打开 [http://127.0.0.1:5188](http://127.0.0.1:5188)。修改过端口时使用实际端口。

Linux 服务器通常没有图形界面。可在自己电脑的新终端建立 SSH 转发，保持该终端运行，再用自己电脑的浏览器打开上述地址：

```bash
ssh -N -L 5188:127.0.0.1:5188 deployuser@192.168.1.20
```

把 `deployuser` 和 `192.168.1.20` 换成实际 SSH 账号及服务器地址；两个 `5188` 分别是自己电脑的本地端口和服务器网页端口。本地端口已占用时，把第一个改为 `15188`，浏览器相应访问 `http://127.0.0.1:15188`。首次配置完成后按第 7 节提供公司访问地址；关闭转发终端只断开自己的临时访问，不停止服务器。

## 6. 首次建立管理员

1. 在服务器上打开 RuntimeRoot 中的 `bootstrap-token.txt`，复制其中整行口令。不要把它发给普通使用者。
2. 打开登录页，账号填 `admin`，密码填写自己设定的 **8–128 个字符**密码；这不是数据库密码。
3. 展开“高级连接选项”，保持服务器地址为当前网页服务地址，将刚才口令填入“管理员初始化口令”。
4. 点击“登录”。首次成功会创建管理员；以后登录用刚才设置的密码，不再填初始化口令。
5. 检查“系统管理 → 关于与支持”的版本与授权状态，再按[行政与账号说明](./公司行政协作设计与验收.md)维护公司、部门、人员、账号和权限。

已有管理员时，初始化口令不能重置或覆盖账号。恢复旧系统后应使用备份中的账号密码。试用及许可证规则仍由服务端执行；重新部署或恢复不会自动变成永久授权。

## 7. 让局域网同事访问

确认本机登录正常后，把绑定地址改为服务器实际的局域网 IPv4 地址，例如：

```powershell
$run.BindAddress = '192.168.1.20'
./scripts/run-native-docker.ps1 @run -SkipBuild
```

同事浏览器输入 `http://192.168.1.20:5188`，不能输入 `127.0.0.1`，后者指同事自己的电脑。给服务器配置固定地址，或使用公司内网 DNS 名称。

在服务器防火墙中仅允许需要访问的内网范围进入该网页端口。PostgreSQL 不发布宿主机端口，无需为浏览器开放 5432。涉及公网或不可信网络时，先按[网页版指南的 HTTPS 部署](./网页版部署与启动指南.md#7-域名与-https)配置域名和反向代理，再提供访问地址。

## 8. 日常启动、停止与更新

重新打开终端后，先进入同一源码目录并重建第 4 节的 `$run`，保持实际镜像、目录、地址、端口。

| 要做的事 | 命令 |
| --- | --- |
| 启动或重新部署已有镜像 | `./scripts/run-native-docker.ps1 @run -SkipBuild` |
| 停止并保留数据卷 | `./scripts/run-native-docker.ps1 @run -Stop` |
| 执行界面已暂存的恢复 | `./scripts/run-native-docker.ps1 @run -RestorePending` |
| 从新源码重新构建 | `./scripts/run-native-docker.ps1 @run`，仅适用于本地镜像 |

关闭终端不会停止已启动的容器。机器重启后，Docker 服务启动且容器未被显式停止/删除时，`unless-stopped` 会尝试恢复运行。公开 `-Stop` 会移除容器并保留卷，之后需要再次执行启动命令。

更新按以下顺序：

1. 通知同事停止录入，等待后台任务结束。
2. 创建并下载数据库备份和完整迁移包，另存原 RuntimeRoot、程序版本及代理配置。
3. 取得匹配的新部署文件；发布镜像把 `$run.Image` 改为新版本，从源码构建则仍使用本地镜像名。
4. 执行对应启动命令，等待维护成功和两个常驻服务就绪。
5. 检查登录、最近单据、附件、模板和一份 PDF，再恢复使用。

Rust 当前 schema 为 7，可从版本 5 起有序升级。PostgreSQL **18 的软件版本**与业务库 **schema 版本**不同。不能把 PostgreSQL 镜像换成 19 后直接挂旧卷，也不能在数据库已经升级后随意换回旧程序。

**不要执行 `docker compose down -v`、`docker volume prune` 或删除 RuntimeRoot 来更新/恢复。**这些动作可能删除数据或丢失部署凭据。

## 9. 查看状态和故障

以下环境变量只用于让直接诊断命令读取与脚本相同的配置：

```powershell
$env:NATIVE_RUNTIME_ROOT = $run.RuntimeRoot.Replace('\', '/')
$env:NATIVE_IMAGE = $run.Image
$env:NATIVE_BIND_ADDRESS = $run.BindAddress
$env:NATIVE_PORT = [string]$run.Port
docker compose --project-name exportdoc-rust-native --file ./deploy/rust-native/compose.yml ps
docker compose --project-name exportdoc-rust-native --file ./deploy/rust-native/compose.yml logs --tail 100 application postgres
```

正常应看到 `application` 和 `postgres` 均为 healthy。`initialize`/`restore` 完成即退出并移除，不需要一直运行。不要用裸 `docker compose up` 绕过公开脚本中的维护步骤。

| 现象 | 先检查 |
| --- | --- |
| 无法连接 Docker | Docker Engine/Desktop 是否启动、当前账号是否可访问它 |
| 镜像不存在/无权限 | 镜像地址、真实标签、GHCR 可见性或登录状态；不要先删现有容器 |
| 网页打不开 | 容器状态、绑定 IP、端口、防火墙；服务器本机与同事电脑分别检查 |
| PostgreSQL 登录失败 | 是否仍使用原 RuntimeRoot；已有卷不会因改密码文件自动更新数据库角色密码 |
| 提示已有实例 | 同一数据库只能运行一个 API；核对重复进程/容器 |
| 提示恢复已暂存 | 执行第 8 节 `-RestorePending`，普通重启不会完成恢复 |
| 上传返回 413 | 文件/请求超过当前限制；同时核对反向代理大小限制 |
| 缺字体/OCR/客户端 | 核对完整镜像及其资源，不临时往容器中安装不明组件 |

`/healthz` 用于健康信息，`/readyz` 用于就绪检查。日志和备份在应用数据卷内；获取支持时保留错误时间及 `X-Request-Id`，不要公开口令文件或完整迁移包。

## 10. 没有网络时部署

1. 在联网的同架构环境取得应用镜像和当前 Compose 指定的 PostgreSQL 镜像，保存 `docker save` 归档或下载工作流提供的离线归档。
2. 复制匹配的部署源码/脚本、镜像归档及校验文件到目标机；恢复已有业务时还需完整迁移包和单独保管的密码。
3. 使用 `docker load --input 归档文件` 导入两种镜像。以导入输出为准记录实际镜像名称，填入 `$run.Image`。
4. 新部署按第 4–6 节准备配置并用 `-SkipBuild` 启动。两种镜像均已在本地时无需重新拉取。
5. 旧业务按[迁移恢复指南](./服务器备份迁移与恢复指南.md)导入，不复制正在运行的 PostgreSQL 原始数据目录。

Docker 配置和模拟生命周期检查不能代替目标机器的实际验收。各平台的真实验证记录见[实施进度](./程序改进重构进度文档.md)。
