# Rust 原生架构与桌面选型

> 2026-09-16，独立分支的实施记录。本文不表示原有全部功能已迁移。

用户要求保留现有功能、布局和操作习惯，特别是发票编辑和商品明细；保留 SQLite / PostgreSQL，桌面与 Web 分别实现界面，保持模块化和解耦。

Windows、Linux、macOS 共同开发与维护，按平台分别验收。Slint 最新稳定版与已用的新特性、平台接口、目标架构和 CI 状态见[《Rust 桌面平台适配与验收》](./Rust桌面平台适配与验收.md)。

## 目标与当前实现

| 层 | 目标 | 当前事实 |
| --- | --- | --- |
| 桌面界面 | Rust + Slint，不依赖 WebView | 已建立原生侧栏、发票五页签、38 列商品明细、付款、组织树、人员档案、客户／供应商关联资料、概览、待办、附件版本、原生 PDF 预览与模板编辑；逐页验收范围见《Rust 原生功能迁移核对表》。egui/eframe 不进入 Slint 包。 |
| 网页、Docker 前端 | React 19，共用同一 Web 工程 | 原工程保持，不需要改成 Rust/WASM。 |
| 业务与服务 | 共用 Rust 业务规则与应用服务，通过独立适配器供桌面和 HTTP API 调用 | 已拆出契约、纯业务规则和服务工程；Rust 迁移尚未覆盖完整 API。 |
| 桌面数据 | SQLite | 独立原生数据目录已实现持久化、单实例锁和乐观并发；当前开发库空库基线为 4，拒绝旧基线。 |
| 团队、服务器、Docker 数据 | PostgreSQL 18 | `export-doc-storage` 已实现 PostgreSQL 18 与 SQLite 适配；Rust HTTP 服务已连接真实 PostgreSQL，并可承载现有 React 构建。Docker 打包／生命周期尚未验收。 |
| 报表 | 独立 Rust 排版与 PDF 模块 | 原生发票版式和分页已验证;PDF 使用 krilla 0.8.2 + krilla-svg 0.8.1 + usvg 0.47.0 + pdf-writer 0.15.0。旧 HTML 模板兼容范围仍须逐项验收。 |
| Excel | 可选 Rust 能力，不需要 Office、COM 或 .NET | 复用现有表头识别规则与原 `.xlsx` 模板；已实现上传／本机文件预览、模板导出、托单转换和发票托单输出。更多客户格式及专项主数据导入仍须继续对照。 |
| 文件任务 | 共用持久任务和受控下载 | 状态及输出在同一业务库事务内提交，重启中断标记、取消竞争、权限隔离、备份回读和幂等清理已有回归。可重放任务的重试、到期清理及占用统计已接入，仍需覆盖全部任务种类。 |

## 工程边界

- `crates/export-doc-contracts`：由现有官方 OpenAPI 生成的 DTO 和契约元数据，不包含端点业务实现。
- `crates/export-doc-domain`：发票金额、数量、重量、报告结构和编辑历史，不引用 GUI、HTTP、数据库、宿主文件系统。
- `crates/export-doc-engine`：应用用例、授权、业务时钟、路径及可选能力编排；系统设置、待办、组织、人事、行政、附件、报表分别分模块。数据库 SQL 已移入存储适配层。
- `crates/export-doc-storage`：存储接口、SQLite / PostgreSQL 18、单实例锁、事务、并发版本、文件内容和备份基础设施。桌面默认依赖图不启用 PostgreSQL feature。
- `crates/export-doc-excel`：受限字节流解析、精确金额导入、保留原模板的 OOXML 输出；不处理数据库、授权、用户路径或 GUI。`tools/excel-analyzer-rs` 同时提供库与原 CLI，同一套字段识别规则直接链接进原生程序，不启动 analyzer sidecar。
- `crates/export-doc-report`：内置商业单据、报关、付款报销的共用排版与 PDF 输出；不依赖浏览器。模板类型的实际支持边界必须逐项验收。
- `export-doc-engine::engine::tasks`：后台执行、取消、事务化发布和任务生命周期；`engine::excel` 只负责编排 Excel 用例，`apps/export-doc-slint/src/controller/files.rs` 管理文件对话框和预览草稿。
- 行政目录、预约／库存交接和处理历史分别位于 `engine::office_queries`、`office_workflows`、`office_events`；Slint 行政草稿、控制器与视图独立。通用记录列表、表单事件和后台结果接收拆为 `controller/records`、`forms`、`responses`，不把新增页面业务塞入列表协调器。SQLite 备份采用 `engine::maintenance` 与原生维护页；PostgreSQL 的维护连接、原生备份及灾备仍须专项实现。
- `apps/export-doc-server`：Axum HTTP 组合根，读取生成的操作／授权元数据，共用 Rust 应用服务；负责 HTTP 请求限制、取消、错误映射与静态 Web 托管。
- `apps/export-doc-slint`：正式桌面方向的声明式视图和界面适配器；业务规则不得在 `.slint` 中重复实现。
- `apps/export-doc-native`：之前的 egui/eframe 比较客户端，复用同一 Rust 核心；不是另一套业务实现。
- `apps/export-doc-web`：继续使用 React；后端替换时根据同一 OpenAPI 契约生成客户端。

桌面单机直接调用 Rust 应用服务并使用 SQLite；本轮不扩展团队桌面客户端。React 通过受认证的 Rust HTTP API 使用 PostgreSQL。服务器独占业务连接与实例锁，不将数据库账号交给前端。

## 本轮已执行的验证与剩余范围

- 2026-09-16：隔离 PostgreSQL 18 实库验证通过，覆盖独立维护／业务角色、业务账号禁止 DDL、实例锁、存储事务、首次管理员部署令牌、人员账号关联、团队预约审批及会话续期。入口为 `scripts/test-native-postgres.ps1 -PostgresBin <PostgreSQL-18-bin>`，每次创建独立集群并在结束时停止，不访问业务库。
- Slint 首轮真实窗口验证通过，输出位于 `artifacts/native-slint-validation`：中文文本输入事件、Enter 换行、撤销重做、保存回读、实际 PDF、模板保存／发布、客户录入、导航、AboutSlint 署名及 5000 行虚拟表格。系统 IME 组合输入没有完成验收，不能由文本事件测试替代。
- React 已连接真实 Rust HTTP／PostgreSQL 服务打开工作概览。随后页面检查发现待办分页层级、任务列表、组织目录等响应与原契约不符；已建立直接使用生成 schema 的结构回归，并逐项修复。该记录不表示全部 React 页面已通过。
- 系统设置的默认值与中文字段名称从原配置类型通过官方 OpenAPI 元数据导出，避免重新手写一份默认值；Slint 采用与原网页一致的八个设置分类，切换分类保留同一草稿。业务时区使用 IANA 名称，日期有效期为下一个当地零点，包含夏令时边界。
- 接续回归：原模板托单往返、重排表头、行金额计价、超出模板明细区后的合计／页尾移动、公式文本与 ZIP 路径拒绝，以及“导出—预览—备份—恢复”闭环已通过。浏览器票据绑定 HttpOnly Cookie 和登录会话，退出后失效；服务器拒绝本机文件读取／保存入口。
- 新增 Docker 源码和公开 `scripts/run-native-docker` 入口：React 静态构建由 Rust 服务托管，PostgreSQL 18 独立维护／业务角色，建表为一次性容器，正式服务没有维护连接；SIGTERM 正常取消、收尾。此宿主未安装 Docker CLI，仅准备脚本已验证，容器构建与生命周期必须由对应 CI／设备补验。
- 2026-09-17 客户与供应链批次已实现专用商机／报价／历史页、销售概览、跟进工作台、客户与供应商文件预检／导入／导出、供应商评价分析、分类改名／删除，以及商品与发票双向映射。`related_records` 统一联系人／供货／评价的父对象权限；预检内容和元数据进入同一业务数据库，30 分钟过期且只能确认一次。SQLite 与 PostgreSQL 共用 `support/business_contract` 的操作闭环；结果以本批实际运行日志为准。
- 尚未完成全量迁移：客户格式的完整 Excel 导入对照、其它专项主数据导入导出、OCR、邮件实际投递、单一窗口完整流程、全套原报表及图片／印章、全部任务种类、团队备份灾备、许可证／签名更新、四产品版与跨平台发布等需要继续实现与验收。路由覆盖报告只表示分发入口，不能作为功能等价比例。

## 桌面选型建议

> 2026-09-20 交付入口更正:上一节“尚未完成全量迁移”中的“OCR、邮件实际投递、单一窗口完整流程、团队备份灾备、四产品版与跨平台发布”不再按旧实现整体描述。当前 Rust 主支已经接入 OCR 资源检查与识别链、真实 SMTP 投递、单一窗口提交包/回执包和持卡机流程、SQLite 备份与灾备包确认,继续保留的缺口改为真实客户端交换箱/官方回执样本、全套旧报表资源、真实 Docker/PostgreSQL 现场、系统 IME/原生文件对话框和各目标平台真机验收。正式交付入口如下。

Rust 主支的交付入口已经从旧 Tauri/ASP.NET 链拆开:

- 本地绿色桌面版由 `scripts/build-native.ps1` 直接生成 Slint + SQLite 包,不经过 WebView、Tauri、Node 或 .NET sidecar。
- 远端桌面端由 `.github/workflows/rust-native-desktop-release.yml` 手工构建 Windows x64/ARM64、Linux x64/ARM64、macOS ARM64 绿色包。
- 远端网页端由 `.github/workflows/rust-native-web-server-release.yml` 手工构建 React 静态资源与 Rust HTTP 服务包,目标数据源为 PostgreSQL 18;本地可先运行 `scripts/package-native-web-server.ps1` 整理同名目录验收。
- 远端 Docker 容器版由 `.github/workflows/rust-native-container-release.yml` 手工验证 Compose 生命周期;选择 publish 时才推送 Rust 容器镜像。
- `.github/workflows/rust-native-validation.yml`、`dependency-governance.yml` 和 `browser-compatibility.yml` 只承担门禁与验收,不伪装成正式发布入口。

旧 `apps/export-doc-tauri` 和 WebView2/旧桌面发布脚本已从 Rust 主支删除;保留的旧 Web 文档和历史测试只用于行为对照,不进入 Rust 原生交付依赖图。

Slint 更贴近本项目长期业务软件的侧栏、表单、页签、弹窗及统一样式维护需求，视图声明和 Rust 业务模型的边界更直观。egui/eframe 的优势是 Rust 编写工具界面和自定义画布直接，已有表格基础也便于验证。

两个框架都不提供本项目完整的 Excel 式发票编辑器。列可见性、行重排、多格选择、复制粘贴、精确计价、撤销重做和中文输入必须使用共享编辑模型实现并验收，不能用一个基础 TableView 就宣称等效替换。

## 免费商用许可

已核对 Slint `1.18.0` 的官方原文：

- [Slint Royalty-free 2.0](https://github.com/slint-ui/slint/blob/v1.18.0/LICENSES/LicenseRef-Slint-Royalty-free-2.0.md) 第 1 条允许桌面、移动和 Web 应用免版税使用和分发；其前言明确适用于不使用 copyleft 路径的应用。
- 本项目是通用电脑上的桌面应用，拟使用 Royalty-free 2.0 路径，而不是要求应用按 GPLv3 分发的路径。
- 第 2 条的署名方式选择：在顶层可访问的“关于”页面显示官方 `AboutSlint` 控件。另一个可选方式是公开下载网页展示官方徽章。
- 不使用这份许可覆盖嵌入式设备、独立分发 Slint 本身或对外暴露 Slint API 的产品。
- 不购买付费支持或 GUI 测试附加产品；使用 Rust 测试及公开接口验证应用。
- 升级 Slint 时重新核对许可；不能将此项许可判断泛化为任何未来版本或任何产品类型。

egui/eframe 当前使用 MIT / Apache-2.0 许可路径，保留相应许可证和版权声明即可，没有 Slint 的界面/网页署名条件。如果产品要求完全避免这类署名条件，egui 是更简单的许可选择。

## 依赖边界（2026-09-18 修正）

Rust 原生程序的后端与桌面最终全部使用 Rust：版本由根 `Cargo.toml` / `Cargo.lock` 精确锁定，crate 选型取查询时最新稳定版（Slint `1.18.0`、rustc `1.98.1`，见《Rust 桌面平台适配与验收》）；Excel 由 `export-doc-excel` 以纯 Rust 受限 OOXML 读写，PDF 由 `export-doc-report` 使用 krilla 0.8.2 + krilla-svg 0.8.1 + usvg 0.47.0 + pdf-writer 0.15.0 原生生成。Slint 桌面交付依赖树不含 NPOI、NuGet 或 .NET 运行依赖，也不引入 Office、COM 或 sidecar。

`NPOI 必须保持 2.7.6`、`Directory.Packages.props`、`global.json` 与 NuGet lockfile 规则只适用于本工作树中保留对照的原 C# / Tauri 实现及其依赖治理；只改 Rust 源码不触发这些 .NET 约束，也不得把 NPOI / NuGet 版本表套用到 Cargo 依赖。原 .NET 依赖治理结果（含 NPOI `2.7.6` 的保留证据）继续由 `scripts/generate-dependency-governance.mjs` 收集，Rust 桌面依赖图以 `native-desktop` 作用域进入同一治理，二者分开验收。

## 发票表格验收边界

以现有 `invoiceItemTableModel.ts`、`invoiceItemsEditorInteraction.ts` 和截图为依据：38 个可编辑字段，原顺序和中英文字段名；42 px 行/表头、横向滚动、长列表虚拟化；空白备用行；显示列；商品库及联想；唛头与特殊条款；复制、移动、删除和新增行；独立明细工作台。

键盘沿用原规则：Enter / Tab 同列下一行，Shift 反向；方向键换格；Ctrl+D 向下填充；Ctrl+Shift+D 复制行；Alt+上下移动行；Shift+方向键扩选；Ctrl+C / Delete 复制或清空选区；Ctrl+Z/Y 撤销重做；IME 组合输入期间不抢占快捷键。

必须分别记录 Slint 的中文 IME、滚动、5000 行交互、缩放、实际 PDF 和业务回读结果。egui 或原 React 的验收结果不得记作 Slint 已通过。
