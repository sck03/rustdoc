# Tauri 2 + React + Rust 架构与选型

> 2026-09-20：按用户明确指令替代此前 Slint／egui 路线。文件名保留以维持文档链接；“Rust 原生”现在指 Rust 后端与桌面宿主，界面使用原版 React。

## 产品与代码边界

| 产品 | 界面与宿主 | 后端 | 数据库 |
| --- | --- | --- | --- |
| 桌面 | Tauri 2 + 原版 React | 进程内 Rust HTTP 适配器、共用应用服务 | SQLite |
| 网页 | 同一 React 构建 | Rust HTTP 服务 | PostgreSQL 18 |
| Docker | 同一 React 静态资源 | Rust HTTP 服务容器 | 独立 PostgreSQL 18 容器 |

Tauri 是桌面宿主；浏览器与 Docker 不运行 Tauri 窗口。Node 只用于前端构建，运行包没有 Node 或 .NET 业务服务。

## 原版来源与职责

原版只读来源为相邻 `ExportDocManager_CS/apps/export-doc-web` 与 `apps/export-doc-tauri`。页面、导航、五页签发票、商品表格、设计器、草稿保护及操作顺序直接复用原版；保留本分支已升级 Vite／TypeScript 所需构建配置，不重写一套页面。

- `apps/export-doc-web`：唯一 React 界面；页面组合、hook 查询与变更、纯 model、平台 bridge 分层。
- `apps/export-doc-tauri/src-tauri`：Tauri 窗口、文件对话框、WebView 预检、退出、更新、运行目录与迁移。`desktop_runtime` 仅管理共用 Rust 后端生命周期。
- `apps/export-doc-server`：HTTP 请求、上传下载、认证适配、容量与超时。桌面关闭默认 `postgres` feature，在随机 `127.0.0.1` 端口启动相同 router；团队二进制启用 PostgreSQL。
- `crates/export-doc-contracts`：由原官方 `/openapi/v1.json` 生成的契约、权限与配置元数据。生成文件不能手改。
- `export-doc-domain`：精确金额、数量、日期与纯业务规则，不依赖 UI、HTTP、数据库或宿主文件系统。
- `export-doc-engine`：应用用例、权限、任务、报表及各可选能力编排；不放 SQL 或 Tauri command。
- `export-doc-storage`：SQLite／PostgreSQL 18、事务、版本、单实例锁与持久化。
- Excel、报表、邮件、汇率、HS、AI、单一窗口分别使用现有独立 crate；同一功能不复制另一套实现。

2026-09-20 用户确认报表继续使用 **krilla + PDFium** 联动：`export-doc-report` 负责模型、排版和 krilla PDF 编码，隔离的 PDFium worker 负责已有 PDF 的预览、文字提取、OCR 页图及合并。完整 V3 和高级 HTML 的模板语义/原生布局在上游补齐，不通过更换 PDF 库代替。当前原生包版本及逐项差距见[《Rust 与 C# 后端功能差距及修正方案》](./Rust与CSharp后端功能差距及修正方案.md)；封装/原生包升级单独验证和治理。

当前已使用 `pdfium-render 0.9.4`，启用 `pdfium_7881 + image_025`，原生包仍为 `152.0.7961`。手写 C ABI 比较保留为历史，不再描述为当前实现；封装加载、原生包组合与各平台运行仍需独立验收，见[《PDFium 跨平台绑定与验收方案》](./PDFium跨平台绑定与验收方案.md)。

2026-09-21 用户选择统一 V3 轻量原生模板路线。六份默认 HTML 按原版字段/布局逐份转成可编辑 V3，预览与 PDF 共用 Rust 排版；不建设任意 HTML/CSS/Scriban 原生解释器。当前六份仍使用专用 Builtin 布局，尚未完成转换。五份用户 PDF 的版式检查与通用能力缺口见[《Rust统一V3模板与原版PDF验收方案》](./Rust统一V3模板与原版PDF验收方案.md)。

模板字体固定使用已随包的 Noto Sans CJK SC Regular/Bold 与 Noto Serif CJK SC Regular，清单及哈希由 `Resources/Fonts/OpenSource/font-manifest.json` 管理。三文件均为 SIL OFL 1.1，允许商业使用、随包分发与 PDF 嵌入；字体替换后校准原版布局，不将参考 PDF 中的微软雅黑/其它字体作为新依赖。需要加粗时使用已有 Sans Bold。

Slint 和 egui 客户端、专用构建与许可引用已退役。原 C# 源码和历史测试仍作行为对照，不进入 Rust 包。

## 通信与运行目录

桌面令牌由 Rust 宿主生成，仅由主窗口受限 IPC 获取，不写 URL 或日志。随机回环端口、精确 Host／Origin 校验、官方 endpoint policy、登录会话与对象权限共同控制桌面访问。网络服务不安装本机文件／进程能力。

AppRoot／DataRoot 由组合根明确注入；便携版数据在包旁 App_Data，WebView profile 在 DataRoot/WebView。安装模式运行配置放在 AppRoot/RuntimeConfig 或显式指定的受管配置根，不默认落入系统 AppData。Rust 开发库使用独立空库基线 4，不打开原 C# v19 数据库，不做猜测迁移。

## 版本与许可

2026-09-20 查询 crates.io 与 npm 官方注册表：Tauri `2.11.6`、tauri-build `2.6.3`、single-instance `2.4.5`、updater `2.12.0`；CLI `2.11.5`、前端 API `2.11.1`。这里的 Tauri 2 指最新稳定 2.x，并非退回 2.0.0。版本精确写入 Cargo/npm 清单与锁文件。

Tauri 使用 MIT／Apache-2.0 双许可路径，具体传递组件以生成 notices 和依赖治理为准。WebView2／WebKit 是明确采用的平台显示依赖；Slint 许可不再属于交付图。NPOI 2.7.6 仅约束保留的 C# 对照程序。OCR 沿用已有 `ort 2.0.0-rc.12`，官方尚无稳定 2.x；本轮未将它升级到另一个 RC。PDFium／ONNX 原生资源只从签名校验后的归档抽取本地库，Rust 构建不执行 dotnet restore。

## 开发与验收

按原版逐页核对界面、后端、权限及操作闭环，积累一批后集中联调，最后统一运行完整门禁。开发中只做必要编译与针对真实失败的回归，不逐模块重复全量构建或测试。功能核对表见《Rust原生功能迁移核对表》，平台验收见《Rust桌面平台适配与验收》。路由存在、编译通过和旧 Slint 截图均不表示当前 Tauri 页面已验收。

公开入口沿用 `scripts/build-native.ps1`／`run-native.ps1`，含义已切换为 Tauri + React + Rust。网页和 Docker 保持 `package-native-web-server.ps1`／`run-native-docker.ps1`。构建、运行和测试的实际结果只记录在当前批次进度中。
