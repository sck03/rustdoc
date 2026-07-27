# Tauri 正式更新签名与发布配置

> 更新日期：2026-07-27
> 当前状态：更新信任链代码和发布门禁已经完成；正式更新地址、正式公钥和私钥尚未由项目所有者确认或生成，因此当前不能宣称自动更新已经正式启用。

## 1. 当前设计边界

- 桌面客户端只使用构建时内置的 HTTPS 更新清单地址和签名公钥。普通用户不能在更新页面临时输入地址或替换公钥，避免恶意更新源用配套公钥自行背书。
- 测试构建允许不配置 updater，并继续生成无更新签名的测试安装包。
- 勾选发布到 GitHub Release 的正式构建必须同时提供更新地址、公钥、带密码私钥和私钥密码；任一缺失都会在构建前失败。
- 私钥不进入源码、安装包、运行目录或 GitHub Variables。客户端只内置可以公开的公钥。
- 更新只替换程序安装内容；SQLite/PostgreSQL 数据、授权镜像、日志、备份和用户输出仍遵守现有运行数据根策略。

## 2. 更新地址如何确定

更新地址必须指向 Tauri updater 的 `latest.json`，不能填写仓库首页、Actions 页面或 Release 详情页。

如果继续使用公开的 GitHub Releases，建议候选地址为：

```text
https://github.com/sck03/rustdoc/releases/latest/download/latest.json
```

该地址目前只是建议值，尚未写死到源码。确认后应设置为仓库 Actions Variable：

```text
EXPORTDOCMANAGER_UPDATER_ENDPOINT
```

公开 GitHub 仓库的 Release 资产可被客户端匿名下载；如果以后把仓库或 Release 改成私有，桌面客户端不能安全保存 GitHub 管理令牌，应改用公开只读的自有 HTTPS 更新站点或对象存储。

## 3. 公钥、私钥和签名文件

| 对象 | 用途 | 是否保密 | 存放位置 |
|---|---|---:|---|
| updater 私钥 | 发布时给安装包签名 | 必须保密 | 项目所有者的离线备份、GitHub Actions Secret |
| 私钥密码 | 解锁加密私钥 | 必须保密 | 密码管理器、GitHub Actions Secret |
| updater 公钥 | 客户端验证安装包签名 | 不需要保密 | GitHub Actions Variable，构建时内置到安装包 |
| `.sig` | 某一版本安装包的签名 | 不需要保密 | 对应 GitHub Release |
| `latest.json` | 版本、下载地址、发布说明及各平台签名 | 不需要保密 | 对应 GitHub Release |

公钥无法反推出私钥。该密钥对用于应用更新签名，不是 HTTPS 服务器证书，也不是软件代码签名证书。

## 4. 一次性生成正式密钥

正式密钥只能由项目所有者在可信电脑上手工生成一次，不能由每次 CI 发布临时生成。建议把私钥写到仓库以外的明确目录，例如：

```powershell
npm --prefix apps/export-doc-tauri run tauri -- signer generate -w D:\ExportDocManager-Secrets\updater.key
```

命令会提示输入私钥密码。应使用强密码，并保存生成的私钥和公钥；常见输出为：

```text
updater.key
updater.key.pub
```

具体文件名以命令实际输出为准。生成完成后应：

1. 把私钥文件和密码分别做至少两份加密离线备份。
2. 确认私钥路径在 Git 仓库之外；本仓库同时忽略 `*.key` 和容器 `secrets/`。
3. 不在聊天、Issue、Release、日志或支持包中粘贴私钥和密码。
4. 记录生成日期、保管人和恢复演练结果，不记录明文密码。

“程序安装在 C 盘”不是密钥或路径 P0 问题。只有一个 C 盘时也可选择仓库外的受控目录；有独立数据盘时优先把私钥备份放到独立加密介质。

## 5. GitHub Actions 配置

进入仓库 `Settings -> Secrets and variables -> Actions`。

Variables：

```text
EXPORTDOCMANAGER_UPDATER_ENDPOINT
EXPORTDOCMANAGER_UPDATER_PUBLIC_KEY
```

Secrets：

```text
TAURI_SIGNING_PRIVATE_KEY
TAURI_SIGNING_PRIVATE_KEY_PASSWORD
```

- `EXPORTDOCMANAGER_UPDATER_PUBLIC_KEY` 填公钥文件完整内容。
- `TAURI_SIGNING_PRIVATE_KEY` 填私钥文件完整内容。
- `TAURI_SIGNING_PRIVATE_KEY_PASSWORD` 填生成密钥时设置的密码。
- 公钥和地址使用 Variables，私钥和密码必须使用 Secrets。

## 6. 哪些内容会自动生成

密钥对不会自动轮换。完成一次性配置后，每次正式发布自动执行：

```text
构建各平台安装包
  -> 使用固定私钥生成对应 .sig
  -> 上传安装包和签名到 GitHub Release
  -> 串行合并各平台条目并发布 latest.json
  -> 客户端读取 latest.json
  -> 使用安装包内置公钥验证下载内容
```

当前发布脚本使用的平台更新产物为：

- Windows：`*-setup.exe` 与 `*-setup.exe.sig`
- Linux：`*.AppImage` 与 `*.AppImage.sig`
- macOS：`*.app.tar.gz` 与 `*.app.tar.gz.sig`

Windows MSI、Linux DEB、macOS DMG 可以作为人工安装资产同时发布，但自动更新清单引用的是 Tauri 对应的签名更新产物。

## 7. 密钥轮换和丢失风险

- 不得每次发布重新生成密钥。旧客户端只信任安装时内置的旧公钥，直接改用新密钥会导致更新验签失败。
- 正常轮换应先发布一个仍由旧私钥签名、同时具备新信任迁移能力的过渡版本，再切换后续发布密钥，并完成三平台真实升级验证。
- 私钥丢失且没有可用备份时，已有客户端通常无法自动信任新密钥，用户需要手工下载安装新的信任起点。
- 私钥疑似泄露时应立即停止自动发布、撤销更新地址上的可疑资产、发布安全公告并执行经过设计的密钥迁移；不能只在 GitHub 中替换 Secret 后继续发布。

## 8. 正式启用前验收

- [ ] 项目所有者确认 updater HTTPS 地址。
- [ ] 在仓库外一次性生成带密码的密钥对并完成离线恢复演练。
- [ ] GitHub Variables/Secrets 四项配置完成。
- [ ] Windows x64、Linux x64/ARM64、macOS x64/ARM64 发布产物和 `latest.json` 平台键核对完成。
- [ ] 正确签名升级、清单不可达、下载中断、签名篡改、版本相同和离线场景验证完成。
- [ ] 更新前后 SQLite/PostgreSQL 业务数据、授权、运行目录和用户输出保持不变。
- [ ] 安装器重启、失败回滚和真实设备权限提示完成验收。

在以上项目完成前，文档完成度应写为“更新发布信任链代码完成，正式地址/密钥和真机升级待验收”，不能写成自动更新已经生产可用。
