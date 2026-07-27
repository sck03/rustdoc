# Tauri 正式更新签名与发布配置

> 更新日期：2026-07-28
>
> 当前状态：更新地址已改为管理员受控配置，签名公钥仍固定在安装包内；正式公钥、私钥和真实三平台升级尚待项目所有者生成与验收。

## 1. 当前信任模型

- `system.updaterEndpoint` 是管理员可保存的“更新清单位置”，支持 GitHub、自建 HTTPS 服务器和受控公司内网 HTTP 服务器。
- 更新中心只显示当前生效地址，并引导管理员到“设置 -> 运行与数据库 -> 软件更新”修改；普通业务账号看不到该地址，也不能保存系统设置。
- updater 公钥不是运行配置。它继续由正式构建写入安装包，React、API 请求、`appsettings.json`、数据库和 Tauri IPC 都不能传入或替换公钥。
- updater 私钥和私钥密码只存在于发布侧，绝不能进入客户端、服务器运行目录、数据库、容器镜像或源码仓库。
- 更新地址被误改最多改变查询位置并造成不可用；攻击者仍必须持有正式私钥才能生成客户端认可的安装包签名。
- 更新只替换程序安装内容；SQLite/PostgreSQL 数据、授权、日志、备份和用户输出继续遵守运行数据根策略。

## 2. 更新地址如何配置

管理员可填写指向 Tauri updater `latest.json` 的绝对地址，例如：

```text
https://github.com/sck03/rustdoc/releases/latest/download/latest.json
https://updates.example.com/export-doc/latest.json
http://updates.internal:8080/export-doc/latest.json
```

地址规则：

- 留空：使用安装包构建时内置的默认地址；安装包也未内置时，更新中心会明确提示管理员配置。
- 只允许 `http://` 或 `https://` 绝对地址。
- 禁止地址中携带用户名、密码、反斜杠、控制字符和 `#` 片段，长度上限为 2048 个字符。
- `https://` 适用于公网、跨不可信网络和一般正式部署。
- `http://` 仅适用于受控公司内网、专用 VLAN 或可信 VPN。HTTP 不会绕过安装包签名校验，但会暴露请求与清单内容，也可能被旁路阻断或替换，因此不能当作公网安全方案。
- 更新地址变化不需要重启 API sidecar；下一次“检查更新”立即使用新地址。检查后若地址发生变化，页面要求重新检查，避免检查和安装使用不同来源。

浏览器服务器版和容器版自身不执行 Tauri 桌面更新。它们可以承载或反向代理 `latest.json`，但不要把 updater 私钥放在这些运行服务器上。

## 3. 公钥、私钥与地址互不绑定

| 对象 | 用途 | 是否保密 | 存放位置 |
|---|---|---:|---|
| updater 私钥 | 发布时给更新产物签名 | 必须保密 | 项目所有者离线备份、GitHub Actions Secret |
| 私钥密码 | 解锁加密私钥 | 必须保密 | 密码管理器、GitHub Actions Secret |
| updater 公钥 | 客户端验证更新产物 | 不保密但不可被运行时替换 | GitHub Actions Variable，构建时固化到安装包 |
| 管理员更新地址 | 指定 `latest.json` 所在位置 | 通常不保密 | 程序根 `appsettings.json` 的 `system.updaterEndpoint` |
| `.sig` | 某一更新产物的签名 | 不保密 | 对应 Release 或更新服务器 |
| `latest.json` | 版本、下载地址、说明和各平台签名 | 不保密 | GitHub Release、自建服务器或内网更新服务器 |

更新地址从 GitHub 切换到自建服务器，或从 HTTPS 切换到内网 HTTP，不要求重新生成密钥。只要新服务器继续发布由同一正式私钥签名的产物，旧客户端仍可验证。

## 4. 一次性生成正式密钥

正式密钥应由项目所有者在可信电脑上手工生成一次，不能由每次 CI 临时生成：

```powershell
npm --prefix apps/export-doc-tauri run tauri -- signer generate -w D:\ExportDocManager-Secrets\updater.key
```

命令会提示设置私钥密码，常见输出为：

```text
updater.key
updater.key.pub
```

生成后应：

1. 将私钥文件和密码分别做至少两份加密离线备份。
2. 保证私钥目录位于 Git 仓库之外。
3. 不在聊天、Issue、Release、日志或支持包中粘贴私钥和密码。
4. 记录生成日期、保管人和恢复演练结果，不记录明文密码。

私钥丢失且没有备份时，旧客户端通常无法自动信任新密钥，只能由用户手工安装新的信任起点。因此“备份和恢复演练”比频繁轮换更重要。

## 5. GitHub Actions 配置

进入仓库 `Settings -> Secrets and variables -> Actions`。

必须配置的 Variable：

```text
EXPORTDOCMANAGER_UPDATER_PUBLIC_KEY
```

可选 Variables：

```text
EXPORTDOCMANAGER_UPDATER_ENDPOINT
EXPORTDOCMANAGER_ALLOW_INSECURE_UPDATER_ENDPOINT
```

必须配置的 Secrets：

```text
TAURI_SIGNING_PRIVATE_KEY
TAURI_SIGNING_PRIVATE_KEY_PASSWORD
```

- `EXPORTDOCMANAGER_UPDATER_PUBLIC_KEY` 填公钥文件完整内容。
- `EXPORTDOCMANAGER_UPDATER_ENDPOINT` 可留空；留空时正式安装包只内置公钥，由管理员安装后配置地址。
- 如确需把 HTTP 地址直接内置为安装包默认地址，必须同时把 `EXPORTDOCMANAGER_ALLOW_INSECURE_UPDATER_ENDPOINT` 明确设为 `true`。运行时由管理员填写内网 HTTP 地址不需要修改公钥。
- 正式发布缺少公钥、私钥或私钥密码会在构建前失败；默认地址不再是正式签名构建的必填项。

## 6. 自动发布流程

```text
使用固定私钥构建各平台更新产物
  -> 生成对应 .sig
  -> 上传安装包和签名
  -> 合并并发布 latest.json
  -> 客户端读取管理员地址或安装包默认地址
  -> 使用安装包内置公钥验证下载内容
  -> 验签通过后交给平台安装器安装并重启
```

当前自动更新产物：

- Windows：`*-setup.exe` 与 `*-setup.exe.sig`
- Linux：`*.AppImage` 与 `*.AppImage.sig`
- macOS：`*.app.tar.gz` 与 `*.app.tar.gz.sig`

Windows MSI、Linux DEB、macOS DMG 可以作为人工安装资产同时发布，但自动更新清单引用 Tauri 对应的签名更新产物。

## 7. 密钥轮换和泄露处置

- 不得每次发布重新生成密钥；旧客户端只信任安装时内置的公钥。
- 正常轮换应先发布一个仍由旧私钥签名、同时具备新信任迁移能力的过渡版本，再切换后续密钥。
- 私钥疑似泄露时应立即停止发布、撤下可疑资产、发布安全公告并执行设计好的密钥迁移，不能只替换 GitHub Secret 后继续发布。
- 更新地址变化与密钥轮换是两件事：改地址通常不需要换密钥；换密钥必须考虑旧客户端迁移。

## 8. 正式启用前验收

- [ ] 项目所有者一次性生成带密码密钥对，并完成至少一次离线恢复演练。
- [ ] GitHub 公钥 Variable、私钥 Secret 和私钥密码 Secret 已配置。
- [ ] 管理员分别验证 GitHub HTTPS、自建 HTTPS、可信内网 HTTP 和空地址回退行为。
- [ ] Windows x64、Linux x64/ARM64、macOS x64/ARM64 的平台键与更新产物核对完成。
- [ ] 正确签名升级、清单不可达、下载中断、签名篡改、版本相同和离线场景验证完成。
- [ ] HTTP 场景确认只位于受控内网/VPN，防火墙不把更新端口暴露到公网。
- [ ] 更新前后业务数据库、授权、运行目录和用户输出保持不变。
- [ ] 安装器重启、失败回滚和真实设备权限提示完成验收。

在这些项目完成前，只能记录为“更新地址管理和签名信任链代码完成，正式密钥与真机升级待验收”，不能宣称自动更新已经生产可用。
