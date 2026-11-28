# SeaweedFS 局域网存储安装与连接手册

适用项目：ExportDocManager；文档核对日期：2026-11-28。本文固定使用已验收的 SeaweedFS 4.55 社区版，不使用会自动变化的 latest。

这份手册面向第一次搭建文件服务器的用户。目标是在一台专门的电脑上保存图片、附件和应用恢复点，并让本项目通过局域网使用它。正文选择一种明确的安装方式：**64 位 Debian 13＋SeaweedFS 官方 Linux 二进制＋系统服务**。B 不必安装 Docker、Rust 编译器或 PostgreSQL。

目前推荐 SeaweedFS。RustFS 也通过了本项目的兼容与恢复测试，选型依据和测试边界见[局域网附件存储选型与接入](./局域网附件存储选型与接入.md)。本文只提供 SeaweedFS 的操作步骤，避免混用两个产品的参数。

项目程序／Docker 镜像须包含本轮实现。GitHub main 的源码提交不等于 GHCR 或已经安装的程序自动更新；首次部署和换机前，请保留并使用实际验收过的安装包或镜像版本。

## 1. 先分清三种设备和三种密码

| 名称 | 用途 | 本文示例 |
| --- | --- | --- |
| A：主设备 | 运行本项目和 PostgreSQL，保存单据、人员、权限等业务记录 | 192.168.1.10 |
| B：存储设备 | 运行 SeaweedFS，保存图片、附件及可选的恢复点 | 192.168.1.20 |
| C：独立备份介质 | 移动硬盘、另一台备份设备等，保存另一份恢复资料 | 根据实际情况选择 |

A 可以单独安装和使用。B、C 都是后续按需增加的设备。已有 A 时，从第 2 节开始准备 B 即可。

本文中的 192.168.1.10、192.168.1.20、192.168.1.0/24 都是示例。先记下自己实际使用的地址，再替换命令和表格；不要直接把别人的地址照搬到自己网络。

| 凭据 | 用在哪里 | 如何取得 |
| --- | --- | --- |
| Linux 管理员密码 | 安装和维护 B | 安装 Debian 时设置 |
| S3 Access Key ID／Secret Access Key | 让 A 访问 B 的特定存储桶 | 第 6 节生成两组，附件和备份各用一组 |
| 应用恢复凭证 | 解密应用恢复点 | 在本项目“备份计划”中设置；与上面两个密码无关 |

“存储桶”可以理解为服务器中的一个独立存储区域。本文创建两个桶：edm-files 存在线附件，edm-backups 存应用恢复点。它们位于同一 B 时，B 的整机故障仍会影响两者，所以后面还会说明如何保留独立副本。

## 2. 安装之前准备什么

- 一台可以安装 64 位 Linux 的 B、一块准备作为系统／数据盘的硬盘、网线，以及安装用 U 盘。
- A、B 接入同一个可信局域网。优先使用网线，并在路由器中为两台设备设置“地址保留／静态 DHCP”，避免重启后 IP 改变。
- 安装系统会涉及磁盘分区。先转移 B 硬盘上需要保留的资料；有多块盘时，安装阶段只连接准备安装系统的那块空盘，降低选错风险。
- E5200 的指令集模拟已经通过，但本项目没有验收你的实际内存、硬盘和网卡。先完成本文的试用和换机演练，再放入正式资料。不要把模拟结果理解为老硬盘可靠性保证。

本文默认将 B 的数据放在系统磁盘的 /srv/seaweedfs。该路径必须有足够容量。如果要使用另一块专门的数据盘，先按第 14 节完成挂载，再执行服务安装；不要启动后再临时拔换磁盘。

## 3. 在 B 上安装 Debian 13

如果 B 已经装好 Debian 13 amd64，可直接进入第 4 节。

1. 从 Debian 官方下载页取得 Debian 13 的 amd64 安装镜像。amd64 同时适用于支持 Intel 64 的 Intel 处理器，不是只给 AMD 使用。
2. 用能够写入系统安装镜像的 U 盘工具制作启动盘，按工具提示选中正确的 U 盘。这个操作会覆盖 U 盘原内容。
3. 把 U 盘插到 B，通过开机启动菜单选择 U 盘。老主板通常使用 BIOS 启动；具体按键看主板开机提示。
4. 进入 Install 或 Graphical install，选择语言、键盘和网络。主机名可以填写 edm-s3。
5. 创建普通管理用户，例如 storageadmin。为简化后面的 sudo 操作，在安装器的 root 密码步骤留空，让第一个普通用户取得 sudo 管理能力；该用户自己的密码必须设置。
6. 分区只选择已经确认可以清空的目标磁盘。初次使用可选择“向导：使用整个磁盘”和“所有文件放在同一分区”。已有资料的磁盘不要使用这个选项。
7. 软件选择时保留 SSH server、standard system utilities；作为专用存储机可以取消 Debian desktop environment，减少不需要的桌面程序。
8. 安装完成后拔掉安装 U 盘并重启，使用刚创建的普通用户登录。

查看 B 的地址：

~~~sh
hostname -I
~~~

记下有线局域网地址。到路由器中为它保留固定地址；本文后续用 192.168.1.20 代表它。

可以在 B 的键盘上继续操作，也可以在另一台电脑的终端／PowerShell 中连接：

~~~text
ssh storageadmin@192.168.1.20
~~~

首次连接确认的是 B 的主机身份。输入 Linux 用户密码时，终端通常不会显示星号，这是正常行为。

官方安装资料见文末。本文不提供第三方修改镜像或绕过系统校验的下载地址。

## 4. 更新系统，准备服务目录

**以下命令都在 B 的 Linux 终端执行。** 每个代码块整体复制；遇到报错先停止，按报错处理后再继续。

~~~sh
sudo apt update
sudo apt install -y ca-certificates curl tar python3 ufw
~~~

创建专门运行存储服务的系统账号和目录。这不是供人登录的账号：

~~~sh
sudo adduser --system --group --home /srv/seaweedfs --no-create-home seaweedfs
sudo install -d -o seaweedfs -g seaweedfs -m 0750 /srv/seaweedfs
sudo install -d -o root -g seaweedfs -m 0750 /etc/seaweedfs
~~~

检查容量：

~~~sh
df -h /srv/seaweedfs
~~~

看到的可用空间应符合自己的计划。系统盘、附件与同机备份共用这块盘时，要为系统、附件增长、备份和整理空间都留出余量；不能只按当前附件大小选盘。

## 5. 下载并校验 SeaweedFS 4.55

在 B 执行：

~~~sh
mkdir -p "$HOME/seaweedfs-install-4.55"
cd "$HOME/seaweedfs-install-4.55"
curl --fail --location --retry 3 --output linux_amd64.tar.gz https://github.com/seaweedfs/seaweedfs/releases/download/4.55/linux_amd64.tar.gz
printf '%s  %s\n' f156c609eeaf9e5fcfeea33fd70411222005f83760aa95893c9d58eff006b45c linux_amd64.tar.gz | sha256sum --check -
~~~

必须看到 linux_amd64.tar.gz: OK 后再继续。摘要不一致时不要解压安装，重新从同一官方版本地址下载并核对。

~~~sh
tar -xzf linux_amd64.tar.gz weed
sudo install -o root -g root -m 0755 weed /usr/local/bin/weed
/usr/local/bin/weed version
~~~

输出应包含 4.55。这里安装的是 Linux amd64 版本，不要选择 arm64、Windows 或 macOS 归档。

## 6. 生成附件和备份两个专用账号

下面脚本只在第一次安装时执行。它自动生成随机密钥，账号分别只能操作自己的桶；没有匿名账号，也不把全局管理员权限交给本项目。配置已存在时脚本会停止，避免覆盖正在使用的凭据。

~~~sh
sudo python3 - <<'PY'
import json
import os
import pathlib
import secrets
import shutil

base = pathlib.Path('/etc/seaweedfs')
config_file = base / 's3.json'
notes_file = base / 'connection-notes.txt'
if config_file.exists() or notes_file.exists():
    raise SystemExit('配置已存在：停止，未覆盖任何凭据。')

identities = []
notes = []
for name, access, bucket in [
    ('exportdoc-files', 'EDMFILES', 'edm-files'),
    ('exportdoc-backup', 'EDMBACKUP', 'edm-backups'),
]:
    secret = secrets.token_hex(24)
    identities.append({
        'name': name,
        'credentials': [{'accessKey': access, 'secretKey': secret}],
        'actions': [action + ':' + bucket for action in ['Read', 'Write', 'List', 'Tagging']],
    })
    notes.append('用途: ' + name + '\n存储桶: ' + bucket + '\nAccess Key ID: ' + access + '\nSecret Access Key: ' + secret + '\n')

os.umask(0o077)
with config_file.open('x', encoding='utf-8') as stream:
    json.dump({'identities': identities}, stream, ensure_ascii=False, indent=2)
shutil.chown(config_file, user='root', group='seaweedfs')
config_file.chmod(0o640)
with notes_file.open('x', encoding='utf-8') as stream:
    stream.write('\n'.join(notes))
print('配置已创建。连接信息保存在 /etc/seaweedfs/connection-notes.txt')
PY
~~~

查看连接信息：

~~~sh
sudo cat /etc/seaweedfs/connection-notes.txt
~~~

把两组凭据分别保存到自己控制的密码管理器或加密离线资料中，并标明“附件”“备份”。不要只保存在 A 或 B 上，也不要放入 Git、聊天群或普通共享目录。后面页面上的 Secret Access Key 就填写这里对应的一串随机字符。

若复制时漏字符，先重新查看本文件，不要再次运行生成脚本重新制造另一套凭据。

## 7. 限制局域网访问

这一节配置 B 的防火墙。先把下面三个地址换成自己的真实值：管理电脑所在网段、A 的地址、B 的地址。若正在远程 SSH 操作，必须先确认 SSH 放行规则覆盖自己的管理电脑；不确定时在 B 的本地键盘执行。

下面示例允许局域网管理电脑通过 SSH 管理 B，只允许 A 使用 S3：

~~~sh
sudo ufw default deny incoming
sudo ufw default allow outgoing
sudo ufw allow from 192.168.1.0/24 to any port 22 proto tcp
sudo ufw allow from 192.168.1.10 to any port 8333 proto tcp
sudo ufw enable
sudo ufw status
~~~

本文的 S3 地址使用可信局域网内的 HTTP。不要在路由器上把 8333 或 SeaweedFS 内部端口转发到互联网；跨公网或不可信网络使用时，先配置经校验的 HTTPS／VPN，本文的 HTTP 方式不作为公网部署方案。

B 的其它内部端口不需要给 A 或普通员工开放。本项目由 A 的后端访问 S3，普通用户浏览器不需要持有 S3 密钥。

## 8. 设置开机自动启动

在 B 执行下面完整代码块。它将配置写入标准系统服务文件：

~~~sh
sudo tee /etc/systemd/system/seaweedfs.service >/dev/null <<'EOF'
[Unit]
Description=SeaweedFS for ExportDocManager
Wants=network-online.target
After=network-online.target
RequiresMountsFor=/srv/seaweedfs

[Service]
Type=simple
User=seaweedfs
Group=seaweedfs
WorkingDirectory=/srv/seaweedfs
RuntimeDirectory=seaweedfs
RuntimeDirectoryMode=0750
ExecStart=/usr/local/bin/weed mini -dir=/srv/seaweedfs -ip=127.0.0.1 -ip.bind=0.0.0.0 -grpc.localSocketDir=/run/seaweedfs -s3.port=8333 -s3.config=/etc/seaweedfs/s3.json -bucket=edm-files,edm-backups -admin.ui=false -webdav=false -s3.iam=false -s3.port.iceberg=0 -s3.port.lance=0 -volume.index=leveldb -s3.concurrentFileUploadLimit=4
Restart=on-failure
RestartSec=5
TimeoutStopSec=180
LimitNOFILE=65536
UMask=0027
NoNewPrivileges=true
PrivateTmp=true
ProtectHome=true
ProtectSystem=strict
ReadWritePaths=/srv/seaweedfs /run/seaweedfs

[Install]
WantedBy=multi-user.target
EOF
sudo systemctl daemon-reload
sudo systemd-analyze verify /etc/systemd/system/seaweedfs.service
sudo systemctl enable --now seaweedfs
sudo systemctl status seaweedfs --no-pager
~~~

应看到 active (running)。初次启动需要创建目录和桶，可稍等再查状态。失败时查看：

~~~sh
sudo journalctl -u seaweedfs -n 100 --no-pager
~~~

这些配置关闭了本项目不需要的管理网页、WebDAV、IAM 管理 API、Iceberg 和 Lance 服务；仍保留 S3 鉴权。附件与备份桶由启动参数创建。数据卷和默认 Filer 元数据都放在 /srv/seaweedfs，认证配置在 /etc/seaweedfs。不要另外添加一个把 Filer 元数据指向临时目录的 filer.toml。

volume.index=leveldb 将索引放入磁盘索引实现；并行文件上传限制为 4。它们不是整个服务内存的硬上限，也不代表已测得你的 E5200 吞吐量。先使用这个经过配置核验的组合，再根据真实负载调整。

第一次验收可以重启 B：

~~~sh
sudo reboot
~~~

重新连接后再次检查 systemctl status seaweedfs，确认它能自动运行。

## 9. 让本项目把附件存到 B

**这一节在本项目的网页界面操作。** 使用系统管理员账号登录 A。

1. 打开“系统管理 → 系统设置 → 运行与数据库”，找到“文件与存储”。也可以使用左侧功能搜索定位该页面。
2. 展开“迁移存储”。
3. “目标类型”选择“S3 对象存储”。
4. 按下表填写。地址里的 IP 必须是 B 的实际局域网地址。

| 页面字段 | 本文示例／填写方法 |
| --- | --- |
| 服务地址 | http://192.168.1.20:8333；只填服务地址，不在后面加桶名 |
| 存储桶 | edm-files |
| 区域 | us-east-1 |
| 对象前缀 | files |
| Access Key ID | EDMFILES |
| Secret Access Key | 第 6 节生成的附件账号密钥 |
| 会话令牌（可选） | 留空；本文使用长期专用凭据 |
| 寻址方式 | 选择“路径式”，不要选择虚拟主机式 |

**A 在 Docker 中运行时，也要填写 B 的局域网 IP。不要填 localhost 或 127.0.0.1，它们会指向 A 的容器自身。** 不需要把 B 的硬盘共享挂载进 A。

5. 点击“核验并迁移”，等待任务完成。已有附件会经过复制和摘要核验后切换，源文件保留；切换期间短暂暂停业务写入，宜选空闲时间。
6. 点击“检查状态”，确认“存储位置”为 S3 · edm-files、“连接状态”为“正常”。
7. 打开一份已有业务记录，下载原附件；再上传一个小图片和一个 PDF，下载后确认内容正确。
8. 在“检查与回收”中执行“检查文件完整性”。正式资料迁入并验收前，不要急着清理原文件或回收空间。

如果需要同时运行正式环境和测试环境，为它们分别创建桶和账号。不要让两个独立应用实例共用同一个在线文件库身份／前缀。

## 10. 设置业务备份，保存恢复资料

只连接 edm-files 还不够：单据、人员、权限和附件关联在 A 的 PostgreSQL 中。要能更换 A，必须同时有应用恢复点。

### 10.1 先设置恢复凭证

1. 打开“系统管理 → 备份与恢复 → 备份计划”。
2. 首次设置“恢复凭证”并再次输入确认，至少 12 个字符；已有凭证时通过“管理恢复凭证”操作。
3. 先保存计划。恢复凭证另存在 A 之外，不能只记在这个页面里。

### 10.2 把恢复点写入 B 的备份桶

打开同一页面的“备份位置”，填写：

| 页面字段 | 填写内容 |
| --- | --- |
| 备份存储 | S3／兼容对象存储 |
| S3 端点 | http://192.168.1.20:8333 |
| 区域 | us-east-1 |
| 存储桶 | edm-backups |
| 对象前缀 | recovery |
| Access Key ID | EDMBACKUP |
| Secret Access Key | 勾选“修改 Secret Access Key”后，填写第 6 节生成的备份账号密钥 |
| 寻址方式与临时凭证 | 展开后选择“路径式”；Session Token 保持未设置 |

点击页面上方“保存计划”，再点击“核验已保存的备份目标”，等它完成。保存前不要点击验证；输入框处于“已保存”状态时不要勾选修改后留空，否则会清除相应密钥。

返回“备份计划”，点击“立即备份一次”。完成后到“恢复点”查看结果，确认不是失败或部分完成。记录“备份计划”里的**恢复仓库编号**。

日常定时备份需要勾选“启用日常灾备”，选择每天／每周及业务时区中的时间，并保存。程序必须在计划时间运行；先确认自动备份成功过一次，再依赖它。

### 10.3 再保留一份独立副本

B 的 edm-backups 可以帮助应对 A 损坏，但不能应对 B 同时损坏。建议定期在“恢复点”点击“下载”，将导出的 .edmpoint 便携恢复文件保存到独立移动硬盘或另一设备，下载完成后确认文件存在且大小正常，再在隔离环境演练恢复。

该便携文件包含完整恢复所需内容并加密；恢复时仍需要导出时使用的恢复凭证。以后轮换仓库恢复凭证，不会自动改写已经导出的旧便携文件。

也可以把主备份目标直接配置到独立 C。当前页面选择的是一个主仓库目标，不要以为填写了 B 就自动在 C 生成第二份。

### 10.4 离线保管清单

把下面这些内容保存在 A 之外，且至少有一份不依赖 B：

- 应用安装包／镜像的准确版本，以及部署目录中的配置资料。
- 备份端点、备份桶 edm-backups、前缀 recovery、恢复仓库编号。
- 应用恢复凭证，以及备份 S3 账号和密钥。
- 在线附件端点、桶 edm-files、前缀 files，以及附件 S3 账号和密钥。
- 最近一次实际恢复演练的日期和结果；如有 .edmpoint，注明它对应的恢复凭证。

这里的“保存资料”不等于把明文密码放进公共文档。本文中的表格可作为记录格式，真实密码请单独私密保存。

## 11. A 坏了，换新设备接回原 B

**可以这样恢复。本项目已经对 SeaweedFS、RustFS 分别实测通过新数据库／新数据目录接回原库，恢复后原附件和新上传均正常。** 可恢复的是成功恢复点对应的业务状态；之后尚未进入备份的记录不保证恢复。

1. 让旧 A 断电或断网，确保它不能自行重连继续写入。保留 B 原数据，不要重新初始化桶、改前缀或删除文件库标记。
2. 在新 A 安装已验收的同版本项目程序和 PostgreSQL 18，使用新的数据目录／数据库。安装方法见[Docker 部署指南](./Docker容器部署与启动指南.md)。不要从旧 PostgreSQL 数据目录挑几个文件手工拼回。
3. 新 A 的 IP 改变时，在 B 添加新 A 的访问规则，例如：

~~~sh
sudo ufw allow from 192.168.1.11 to any port 8333 proto tcp
~~~

4. 在新 A 的**镜像部署目录**中进入维护模式。该目录应含本次部署生成的 compose 配置和 .env；不要在任意目录执行：

~~~sh
docker compose stop application
docker compose --profile maintenance up -d maintenance
~~~

5. 在新 A 本机浏览器访问 http://127.0.0.1:5189/#/maintenance。如果新 A 没有桌面，可从管理电脑建立 SSH 隧道，然后在管理电脑浏览器访问同一地址：

~~~text
ssh -N -L 5189:127.0.0.1:5189 storageadmin@192.168.1.11
~~~

此处 SSH 用户必须换成**新 A 的管理用户**。该终端保持打开。维护端口默认只绑定回环地址，不需要公开给整个网络。

6. 在新 A 的部署目录读取一次性维护凭据。镜像部署入口默认生成 config/maintenance/maintenance-token.txt；把内容填写到页面“部署端一次性凭证”，点击“进入维护”。源码脚本部署的文件位于其原 NATIVE_RUNTIME_ROOT，下述相对路径不适用时按[服务器恢复指南](./服务器备份迁移与恢复指南.md)查找。

~~~sh
cat config/maintenance/maintenance-token.txt
~~~

7. 恢复源选择“S3 仓库”；填写离线保存的恢复仓库编号、应用恢复凭证，以及 edm-backups 的端点、桶、前缀和备份账号密钥；区域为 us-east-1，寻址为路径式。
8. 确认“旧业务实例已停止并隔离”，点击“解锁仓库”，选择需要的恢复点。
9. “附件恢复方式”选择“接回完好的原文件库”。B 地址和凭据仍有效时不需要重新搬运全部附件；程序恢复原配置并核验原库身份和对象。
10. B 的访问地址或凭据变化时，展开“重签 S3 访问凭据（可选）”，填写仍指向**同一个真实文件库**的新连接信息。一个新建空桶不能冒充原 B。
11. 点击“完整预检”。只在预检成功后点击“执行恢复与切换”。执行过程中不要关闭服务或强行断电；显示完成后继续下一步。
12. 在新 A 的部署目录执行：

~~~sh
docker compose stop maintenance
docker compose up -d --no-deps --wait application
~~~

13. 使用恢复后的账号登录。抽查单据、人员权限、旧图片、旧 PDF，再新增附件并下载。旧登录会话失效后重新登录属于预期行为。
14. 重新确认自动备份设置和离线副本。核验成功后再处理旧 A，不要让旧 A 与新 A 同时作为业务写入实例运行。

如果 B 也坏了，在第 9 步选择重建到本地或新 S3，使用独立备份恢复附件。不要选择“接回完好的原文件库”。只有附件桶、没有数据库及应用恢复点，不能完整恢复业务。

### A、B 都不可用，使用移动硬盘里的 .edmpoint

先按上面的步骤准备新 A 并停止业务服务。以镜像生成的默认部署目录为例，将自己导出的便携文件放入 data/Recovery/Inbox；以下文件名需要改成真实文件名，两个 install 命令在新 A 的部署目录执行：

~~~sh
sudo install -d -o 10001 -g 10001 -m 0750 data/Recovery/Inbox
sudo install -o 10001 -g 10001 -m 0640 /mnt/edm-backup/你的备份.edmpoint data/Recovery/Inbox/recovery.edmpoint
~~~

进入独立维护页面后：

1. “恢复源”选择“登记目录或移动盘”，展开“从便携文件读取”。
2. “维护收件目录中的文件名”填写 recovery.edmpoint，不填写任意绝对路径。
3. 填写该文件导出时的恢复凭证；恢复仓库编号可留空，由文件读取。确认旧实例已经隔离后解锁。
4. 选择恢复点；附件恢复方式选择重建到本地，或填写一个全新的 S3 目标。空间不足时先准备足够的目标空间。
5. 完整预检、执行恢复；完成后停止 maintenance，再启动 application，并核对旧数据和新上传。

使用自行修改过挂载位置的部署时，先确认实际 DataRoot 对应的宿主目录，不照搬 data 路径。该便携文件应由本项目下载／导出，普通数据库 dump 或手工改扩展名的 ZIP 不能替代它。

## 12. B 的整机备份与更换

日常优先使用第 10 节的应用恢复点和独立便携副本。下面是额外的 B 整机数据备份，适合计划停机后把 SeaweedFS 搬到另一台机器。

### 12.1 停写后备份 B

先在 A 的部署目录停止 application，并确认 maintenance 也没有运行；停止其它写入 B 的工具。然后在 B 执行：

~~~sh
sudo systemctl stop seaweedfs
sudo systemctl is-active seaweedfs
~~~

结果应为 inactive。准备并挂载独立备份盘到 /mnt/edm-backup，使用下列检查确认它确实是挂载点：

~~~sh
mountpoint /mnt/edm-backup
df -h /srv/seaweedfs /mnt/edm-backup
~~~

不是挂载点、空间不足、或备份盘与原数据实际在同一块故障磁盘上时，不继续。

~~~sh
B_BACKUP_FILE="/mnt/edm-backup/seaweedfs-$(date +%Y%m%d-%H%M%S).tar"
sudo tar -C / -cpf "$B_BACKUP_FILE" srv/seaweedfs etc/seaweedfs etc/systemd/system/seaweedfs.service usr/local/bin/weed
sudo sha256sum "$B_BACKUP_FILE" | sudo tee "$B_BACKUP_FILE.sha256"
sudo tar -tf "$B_BACKUP_FILE" | head -n 20
~~~

打包无报错、清单正常后，启动 B，再启动 A：

~~~sh
sudo systemctl start seaweedfs
sudo systemctl status seaweedfs --no-pager
~~~

这份 tar 包包含 S3 密钥，**tar 本身不加密**，请使用私密／加密介质保管。此操作保护整个数据目录和元数据；不要仅复制 .dat 文件，也不要把服务运行中直接复制文件夹当作一致性备份。

### 12.2 在全新的 B 上还原

此流程只用于新的、尚未投入使用的目标机器。旧 B 保持停止；新 B 安装相同 Linux 环境、建立 seaweedfs 用户，但先不要启动服务。

1. 接入备份盘，核对 tar 的 SHA-256 与保存的摘要一致。摘要文件可能包含旧机器的绝对路径，挂载路径变化时手工对比两边的 64 位摘要即可。
2. 核对归档清单只有前文保存的四组路径。将本例文件名替换成实际备份文件名：

~~~sh
sudo tar -xpf /mnt/edm-backup/seaweedfs-实际日期.tar --no-same-owner -C / srv/seaweedfs etc/seaweedfs etc/systemd/system/seaweedfs.service usr/local/bin/weed
sudo chown -R seaweedfs:seaweedfs /srv/seaweedfs
sudo chown root:seaweedfs /etc/seaweedfs /etc/seaweedfs/s3.json
sudo chmod 0750 /etc/seaweedfs
sudo chmod 0640 /etc/seaweedfs/s3.json
sudo chmod 0600 /etc/seaweedfs/connection-notes.txt
sudo systemctl daemon-reload
sudo systemctl enable --now seaweedfs
~~~

3. 设置与第 7 节相同的防火墙和地址保留；原 B 已隔离时，可以让新 B 接替原 B 的 IP。
4. 启动 A，检查连接状态、执行完整性检查，下载旧附件并上传新附件。
5. 不要删除原备份，直到上述检查及另一份独立备份都完成。

该 B 整机停机打包流程尚未在实际 E5200 上执行；已完成的项目恢复验收是应用恢复点、接回原 S3、重建到本地／新 S3。第一次正式使用前，建议在空测试设备做一次本节演练。

若改用 RustFS，不能把 SeaweedFS 的原始磁盘文件直接交给 RustFS。使用本项目“迁移存储”或从应用恢复点“重建到新 S3”；两种厂商间的对象迁移已经实测。

## 13. 常见问题怎么处理

| 现象 | 先检查什么 | 怎么处理 |
| --- | --- | --- |
| 连接超时／连接被拒绝 | B 是否开机、IP 是否变化、seaweedfs 是否 active、防火墙是否放行 A 的真实来源 IP | 先修正服务和网络，不要重复初始化文件库 |
| 在 Docker 中填了 127.0.0.1 后失败 | 该地址指向 A 的应用容器 | 改为 B 的局域网 IP |
| 浏览器直接打开 S3 地址显示 AccessDenied | 没有携带 S3 签名 | 对受保护的桶属预期现象；从项目页面用正确凭据核验 |
| 403／签名不匹配 | 附件和备份密钥是否混用；是否复制完整；系统时间和区域是否正确 | 核对对应账号、us-east-1、路径式；检查 timedatectl status，不要关闭鉴权 |
| 提示存储桶不存在 | 桶名是否拼错；启动日志是否完成建桶 | 使用 edm-files／edm-backups；不要把桶名拼进服务地址 |
| 提示库身份不符 | 是否改了 bucket／prefix、连接到另一台 B 或空目录 | 找回原库；更换库通过正式迁移或重建流程 |
| B 离线后不能上传 | 当前正式文件库在 B | 恢复 B 和网络；程序不会自动写回 A 形成两套分散数据 |
| 备份显示失败／部分完成 | 任务详情里的具体原因、空间、凭据、存储连接 | 按任务提示检查；先查看已有恢复点和回执，不盲目重复提交 |
| 恢复点存在但解锁失败 | 恢复仓库编号、应用恢复凭证及 S3 访问凭据 | 三者分别核对；S3 密钥不能替代应用恢复凭证 |
| 主设备坏了但没有备份 | 是否还有完整数据库、应用恢复点或 .edmpoint | 仅靠附件桶不能恢复单据、权限和关联；保留原盘再处理 |
| 磁盘空间不下降 | 旧副本、应用保留策略、SeaweedFS 内部整理 | 先通过应用检查和受控回收，不在桶或卷目录手删文件 |
| 服务启动失败 | journalctl 中的端口占用、目录权限、配置错误 | 按具体错误处理；不要给全部目录 chmod 777 |

常用检查命令，均在 B 执行：

~~~sh
sudo systemctl status seaweedfs --no-pager
sudo journalctl -u seaweedfs -n 100 --no-pager
df -h /srv/seaweedfs
timedatectl status
sudo ufw status
~~~

账号轮换时，先在 B 的配置中为原身份增加新凭据并重启服务，再在应用对应页面更新和核验；保留旧凭据到新凭据确认可用后再删除。附件凭据入口为“更新存储凭据 → 校验并更新凭据”，备份凭据在“备份位置”修改后保存并核验。不要重新生成整个配置覆盖旧身份。

## 14. 使用独立数据硬盘时的附加要求

本节仅适用于把 /srv/seaweedfs 放到第二块盘的用户。没有第二块盘可以跳过。

- 先备份盘上资料，再由熟悉磁盘分区的人将其格式化／挂载；本手册不提供一条会误格式化整盘的命令。
- 使用固定 UUID 在 /etc/fstab 中挂载到 /srv/seaweedfs，不能依赖每次可能变化的 /dev/sdb 名称。
- 在开始存数据前执行 findmnt /srv/seaweedfs 和 df -h /srv/seaweedfs，确认实际设备、文件系统及容量正确。
- 在 seaweedfs.service 的 [Unit] 中额外加入 ConditionPathIsMountPoint=/srv/seaweedfs，执行 sudo systemctl daemon-reload。这样未挂载时不会将附件写到系统盘上同名的空目录。
- 保留 RequiresMountsFor=/srv/seaweedfs；拔盘、挂载失败或只读时先恢复磁盘，不跳过检查强行启动。
- 不要把有线存储设备当成可随手热拔的 U 盘。移动硬盘可作为离线备份介质；在线文件库断开会影响当前业务。

## 15. 第一次投入使用前的核对表

- [ ] B 重启后 SeaweedFS 自动运行。
- [ ] 附件账号只能使用 edm-files，备份账号只能使用 edm-backups；没有匿名公开读写。
- [ ] A 的“文件与存储”显示正常，旧附件和新上传都能下载。
- [ ] 已成功生成应用恢复点，并在 A 之外保存恢复仓库编号、恢复凭证及访问凭据。
- [ ] 至少一份完整恢复资料位于 B 之外。
- [ ] 已用隔离的新 A／新库做一次恢复演练，核对业务记录、权限、旧附件和新上传。
- [ ] 知道当前恢复点的时间；可以接受从该时间到故障时刻之间尚未备份的数据损失范围。

本文按固定版本的官方参数与项目当前界面编写。额外实测已确认本文的静态双账号配置能分别完成读、写、删除，跨桶访问和匿名访问均被拒绝；应用侧四条 PostgreSQL＋S3 恢复路径也已通过。Linux 系统安装、systemd 开机启动、防火墙及 E5200 硬盘故障仍须在自己的设备上按表验收，不把 Windows 配置核验当作整台 Linux 设备已经通过。

## 官方资料与项目资料

- [SeaweedFS 4.55 官方发行与下载](https://github.com/seaweedfs/seaweedfs/releases/tag/4.55)
- [该版本 mini 命令及持久化参数](https://github.com/seaweedfs/seaweedfs/blob/4.55/weed/command/mini.go)
- [SeaweedFS S3 凭据与桶权限说明](https://github.com/seaweedfs/seaweedfs/wiki/S3-Credentials)
- [SeaweedFS 数据备份说明](https://github.com/seaweedfs/seaweedfs/wiki/Data-Backup)
- [Debian 官方下载](https://www.debian.org/distrib/)
- [Debian 13 amd64 安装手册](https://www.debian.org/releases/trixie/amd64/)
- [Debian 13 安装器账号、分区及软件选择](https://www.debian.org/releases/trixie/amd64/ch06s03.en.html)
- [本项目存储选型与实测证据](./局域网附件存储选型与接入.md)
- [本项目服务器备份迁移与恢复指南](./服务器备份迁移与恢复指南.md)
- [本项目 Docker 部署与启动指南](./Docker容器部署与启动指南.md)
