# Tauri 2 + React + Rust 架构与选型

> 2026-09-20：按用户明确指令替代此前 Slint／egui 路线。文件名保留以维持文档链接；“Rust 原生”现在指 Rust 后端与桌面宿主，界面使用原版 React。

## 产品与代码边界

| 产品 | 界面与宿主 | 后端 | 数据库 |
| --- | --- | --- | --- |
| 桌面 | Tauri 2 + 原版 React | 进程内 Rust HTTP 适配器、共用应用服务 | SQLite |
| 网页 | 同一 React 构建 | Rust HTTP 服务 | PostgreSQL 18 |
| Docker | 同一 React 静态资源 | Rust HTTP 服务容器 | 独立 PostgreSQL 18 容器 |

Tauri 是桌面宿主；浏览器与 Docker 不运行 Tauri 窗口。Node 只用于前端构建，运行包没有 Node 或 .NET 业务服务。

正式桌面交付 Document/单证版与 Sales/业务员版，默认单证版；网页/Docker Full 用于完整多用户业务。2026-09-30 用户明确增加本地 Full + SQLite 单机测试便携包，复用同一 Tauri + React + Rust 服务，以 `-Edition Full` 单独生成，便于完整布局与流程测试。它在版本清单的 localTestEditions 中独立声明，不进入 All、安装器和正式发布矩阵；Administration 不打包。SQLite 登记流程不代替 PostgreSQL 团队验收。

## 原版来源与职责

原版只读来源为 `ba9dbdca72590b06c5f32740e4cd58d3027578da`（固定对照提交 `ba9dbdc`）中的 `apps/export-doc-web` 与 `apps/export-doc-tauri`，可通过 `git show` 读取；不再依赖本地 C# 工作树。页面、导航、五页签发票、商品表格、设计器、草稿保护及操作顺序直接复用原版；保留本分支已升级 Vite／TypeScript 所需构建配置，不重写一套页面。

- `apps/export-doc-web`：唯一 React 界面；页面组合、hook 查询与变更、纯 model、平台 bridge 分层。
- `apps/export-doc-tauri/src-tauri`：Tauri 窗口、文件对话框、WebView 预检、退出、更新、运行目录与迁移。`desktop_runtime` 仅管理共用 Rust 后端生命周期。
- `apps/export-doc-server`：HTTP 请求、上传下载、认证适配、容量与超时。桌面关闭默认 `postgres` feature，在随机 `127.0.0.1` 端口启动相同 router；团队二进制启用 PostgreSQL。
- `crates/export-doc-contracts`：Rust OpenAPI 组合器保留原官方导出并扩展新能力，统一生成契约、权限和配置元数据。逐项比较原 schema/端点/认证/错误的回归保护既有接口，生成文件不能手改。
- `export-doc-domain`：精确金额、数量、日期与纯业务规则，不依赖 UI、HTTP、数据库或宿主文件系统。
- `export-doc-engine`：应用用例、权限、任务、报表及各可选能力编排；不放 SQL 或 Tauri command。
- `export-doc-storage`：SQLite／PostgreSQL 18、事务、版本、单实例锁与持久化。
- Excel、报表、邮件、汇率、HS、AI、单一窗口分别使用现有独立 crate；同一功能不复制另一套实现。

2026-09-20 用户确认报表继续使用 **krilla + PDFium** 联动：`export-doc-report` 负责统一 V3 模型、排版和 krilla PDF 编码，隔离的 PDFium worker 负责已有 PDF 的预览、文字提取、OCR 页图及合并。HTML/Scriban 不作为报表模板运行时。当前原生包版本及逐项差距见[《Rust 与 C# 后端功能差距及修正方案》](./Rust与CSharp后端功能差距及修正方案.md)；封装/原生包升级单独验证和治理。

当前已使用 `pdfium-render 0.9.4`，启用 `pdfium_7881 + image_025`，原生包为 `158.0.8111`。手写 C ABI 比较保留为历史，不再描述为当前实现；封装加载、原生包组合与各平台运行仍需独立验收，见[《PDFium 跨平台绑定与验收方案》](./PDFium跨平台绑定与验收方案.md)。

2026-09-21 选择轻量原生路线，2026-09-23 收口为唯一 `.dtpl` 模板格式。六份默认模板按原版字段/布局生成可编辑结构化文档，预览与 PDF 共用 Rust 排版；不建设任意 HTML/CSS/Scriban 原生解释器。文件模板 API 返回可编辑 JSON，磁盘保持带校验的二进制容器，React 可视化设计器可直接打开。内部 schema 版本仍为 3，产品界面不附版本号。`export-doc-report` 的 Builtin 读取同一套 `.dtpl` 资产，没有另一套硬编码票面；默认引用也指向这些文件。六份匿名 PDF 已生成并检查，客户数据逐页签字与跨平台打印仍须独立验收，见[《模板与原版 PDF 验收方案》](./Rust统一V3模板与原版PDF验收方案.md)。

模板字体固定使用已随包的 Noto Sans CJK SC Regular/Bold 与 Noto Serif CJK SC Regular，清单及哈希由 `Resources/Fonts/OpenSource/font-manifest.json` 管理。三文件均为 SIL OFL 1.1，允许商业使用、随包分发与 PDF 嵌入；字体替换后校准原版布局，不将参考 PDF 中的微软雅黑/其它字体作为新依赖。需要加粗时使用已有 Sans Bold。

Slint 和 egui 客户端已退役。2026-09-28 按用户清理要求移除本分支重复的 C# 源码、测试、工程、生成器与旧部署；只读备份分支和 Git 历史提供对照，不再依赖相邻 C# 工作树。冻结 OpenAPI、单一窗口参考数据和有效共用夹具继续用于 Rust 契约验收。

2026-09-30 清除退役客户端残留的 `reference-backend` 回环传输 feature 和未使用的 Runtime 包装；`api::ApiClient` 仅持有进程内 Rust 服务，继续供集成工作流验证。Windows 子进程 Job 对象归入 `controlled_process` 私有平台模块，实际进程树清理机制保留。

2026-10-23 审查后移除仅有自身测试和导出的 Rust `invoice_grid`／`history` 旧客户端模型；现行编辑与撤销继续由 React 管理，Domain 发票草稿、计算及打印行仍保留。Storage 专用业务查询复用受控 SQL、字段投影、数据范围和同快照分页；HTTP 下载资源生命周期独立于用例线程，前端邮件 workspace 独立于页面展示。未增加数据库、运行依赖或 OCR 常驻服务，Release 优化等级保持现有配置，后续调整须以实际负载测量为依据。

2026-10-24 进一步把行政列表、跨来源待办及图片引用查询放入 Storage 的专用模块；Engine 只组合授权、用例和分页结果，待办查询独立于展示组装。V3 结构校验复用生成契约的数量边界，排版复用 Domain 的纸张规则。实际 PDF 页图复用既有预览端点、krilla 输出和 PDFium 隔离进程，有界缓存归服务实例管理，不增加第二套报表运行时。React 预览请求按模板/草稿/来源取消，设计器按需加载。退役 Administration 身份从活动目录移除，冻结契约与已发布数据库迁移继续保留；数据库仍为 schema 7，无新增依赖。

## 通信与运行目录

2026-10-26 将装柜保存/加载移入独立项目 hook，信用证及印章选择沿用共享取消边界；用例层通过现有当前授权快照与事务复核保持权限语义。审计专用查询、通用时间排序、分类聚合和存在判断归 Storage，SQLite 的调用方取消控制不依赖 HTTP 或宿主路径。只供测试使用的任务等待/报表辅助代码迁入 `tests/support/jobs.rs`，生产 PDF 容量常量留在 PDF 模块。桌面原始字节 IPC 复用业务图片校验；仅启用现有 rusqlite 的 hooks，并将 Tauri 测试 Base64 声明移至 dev-dependencies，没有依赖版本升级或新数据库。

2026-10-25 后续完整完善复用草稿合并、作用域取消、当前授权快照和存储投影，补齐基础资料/数字录入/删除历史/任务重试边界。文件任务成功后释放重试输入，失败/取消输入另有累计保留预算。迁移标记由 Storage 提供统一普通打开防护，Tauri 负责持锁的目录迁移及恢复，不把宿主路径控制放入业务层。calamine 原版本供应源码增加分配前边界，Excel ZIP 校验按块处理；无运行依赖升级及数据库迁移。

领域模型的硬编码发票样例构造已迁入共享匿名 JSON 测试夹具，领域、报表和工作流回归读取同一份文档；正式内置票面仍只使用 `.dtpl` 资产。记录引用检查从大型协调器分离，读取、完整性校验及可选能力继续按现有职责组织；真实验证及交付证据见本日进度。

2026-10-25 完成发票精确计算、保存作用域、TSV 与精确商品查询的收口。十进制和表格解析复用共用纯模块，保存基线、剩余草稿及缓存失效各有独立职责。Storage 新增有界 HS 投影及排序、文件元数据与结果容量查询，Engine 负责语义匹配、授权和保留用例；普通任务下载在 BLOB 分配前取得实例字节预算。数据库按 7→8 追加 HS 索引，既有迁移和业务记录保持；V3 大变体使用堆间接存储，序列化格式不变。本批没有新增运行依赖、业务数据库或默认文件目录。

同日后续审查修复将客户/供应商草稿及保存生命周期收口到共用 hook，CRM 查询进入 Storage，预检内容增加事务化消费和累计容量边界；PDFium 文档句柄仅在一次隔离 worker 操作内复用。移除无生产调用的 `RuntimePaths::open` 和旧 Rust 编辑粘贴模型，实际运行根仍由组合根注入，React 继续承担编辑与 TSV。共享查询投影和时间排序属于 Storage；HTTP、Tauri 和用例层不复制 SQL。修复 GitHub 安全门禁仅独立升级 `ctutils 0.4.3` 至 `0.4.4`，其 Apache-2.0/MIT 许可不变；无新增运行依赖或 schema 迁移。

桌面令牌由 Rust 宿主生成，仅由主窗口受限 IPC 获取，不写 URL 或日志。随机回环端口、精确 Host／Origin 校验、官方 endpoint policy、登录会话与对象权限共同控制桌面访问。网络服务不安装本机文件／进程能力。

AppRoot／DataRoot 由组合根明确注入；便携版数据在包旁 App_Data，业务 SQLite 在 DataRoot/Database，WebView profile 在 DataRoot/WebView。安装模式运行配置放在 AppRoot/RuntimeConfig 或显式指定的受管配置根，不默认落入系统 AppData。Rust 数据库保留版本 5 建库基线，从版本 5 起按存储层有序事务迁移升级；不打开原 C# v19 或版本 4 及更早试验库，不做猜测迁移。

## 版本与许可

2026-10-23 查询 crates.io 与 npm 官方注册表：Tauri `2.12.5`、tauri-build `2.7.4`、single-instance `2.5.4`、updater `2.14.0`；CLI 与前端 API 均为 `2.12.4`。Rust `1.99.1`、React `19.3.1`、Vite `8.3.5`，构建用 Node `26.12.1`。版本精确写入 Cargo/npm 清单、锁文件及构建镜像；不将历史平台验证冒充新版本验收。

依赖裁剪关闭未用的 Tauri 动态 ACL、tower-http limit feature，移除无引用的 serde 与 Engine 测试 tokio/lettre 声明。保留实际所需的平台显示、OCR、Excel、报表和网络能力。calamine 升至 `0.37.0`，删除旧 ZIP 适配，只保留上游尚未修复的两处 BIFF8 末行索引扩宽；供应源码仅保留库、清单、说明及许可。`usvg 0.47.0` 与最新版 krilla-svg 的公开类型对齐，不为追逐独立版本制造第二套 SVG 依赖。开发与测试统一使用行号调试信息，避免每份产物重复包含完整类型信息；Release 优化不变。

Tauri 使用 MIT／Apache-2.0 双许可路径，具体传递组件以生成 notices 和依赖治理为准。WebView2／WebKit 是明确采用的平台显示依赖；Slint 许可不再属于交付图。NPOI 2.7.6 仅约束保留的 C# 对照程序。OCR 沿用已有 `ort 2.0.0-rc.12`，官方尚无稳定 2.x；本轮未将它升级到另一个 RC。PDFium／ONNX 原生资源只从通过受管 SHA-512 校验的官方归档抽取本地库，Rust 构建不执行 dotnet restore。

## 开发与验收

按原版逐页核对界面、后端、权限及操作闭环，积累一批后集中联调，最后统一运行完整门禁。开发中只做必要编译与针对真实失败的回归，不逐模块重复全量构建或测试。功能核对表见《Rust原生功能迁移核对表》，平台验收见《Rust桌面平台适配与验收》。路由存在、编译通过和旧 Slint 截图均不表示当前 Tauri 页面已验收。

桌面构建／运行入口为 `scripts/build-native.ps1`／`run-native.ps1`。网页发布包可通过 `--setup-web` 准备配置并自动识别相邻 Web 目录；新版 Docker 镜像通过 `--print-compose` 输出独立部署配置，无需克隆源码。`package-native-web-server.ps1` 与 `run-native-docker.ps1` 继续用于构建、已有部署及开发验证。实际结果按平台记录在当前批次进度中。
