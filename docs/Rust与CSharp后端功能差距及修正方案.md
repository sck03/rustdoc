# Rust 后端差距与持续改进

> 2026-10-08 按当前源码与已记录验收整理。C# 对照来源为 `origin/tauri-csharp-net10-backup`，固定对照提交 `ba9dbdc`；2026-09-20 首次盘点、候选依赖比较和旧实施顺序见 [Git 历史](https://github.com/sck03/rustdoc/blob/b44fbb6/docs/Rust与CSharp后端功能差距及修正方案.md)。当前事实只维护在[架构事实](./当前架构事实.md)，测试数字只进入带日期的[进度](./程序改进重构进度文档.md)。

## 1. 继续维护模块化单体

React 共用界面；Tauri/HTTP 负责宿主与传输，Domain 负责纯规则，Engine 编排授权和用例，Storage 管理 SQL/事务，Excel、报表、HS、邮件等按 crate/feature 隔离。当前部署是单 API、多浏览器与 PostgreSQL 18，桌面使用 SQLite。

连接池不等于多 API 高可用。未经容量与可用性需求确认，不引入 Redis、消息队列、第二业务库或微服务，不直接放开实例锁。新功能优先扩展已有边界，避免复制协议、解析器或查询规则。

## 2. 已有实现与继续验收的区别

| 领域 | 已有实现 | 仍需独立验证 |
| --- | --- | --- |
| 通用查询 Q01 | 资源、模板目录/历史、任务的权限/过滤/排序/计数/分页下推 Storage，schema 7 保留数据迁移 | 待办汇总、部分行政和单一窗口专用列表仍有应用层过滤；按实际数据量优化，不宣称所有查询已下推 |
| 容量与运维 | PostgreSQL 有界池、HTTP 公平等待、请求关联号、脱敏轮转日志与管理员指标；有本机混合负载和断连演练记录 | 生产容量、真实磁盘满、设备故障、备份频率下的 RPO/RTO 与多实例设计 |
| 模板入口 | 六份 .dtpl、统一身份、文件锁/回滚、个人/共享权限、版本、图片引用与默认项 | 全部客户模板、复杂组合/长行/跨页、实体打印与其它平台 |
| PDF | Rust 实际测量与分页、krilla 编码、pdfium-render worker | 绑定/API feature/原生资源各目标的完整实跑；SVG 预览不等于实际 PDF 页图预览 |
| 备份恢复 | HTTPS/chunked WebDAV、流式下载与清理，SQLite 灾备及 PostgreSQL 独立维护恢复 | 真实 WebDAV 服务、断传/证书、生产备份恢复和目标设备演练 |
| 权限与并发 | 生成 endpoint metadata、授权/许可证检查、事务内复核、会话撤销及图片/附件归属 | 各岗位×动作×数据范围的完整业务验收，不能只看菜单或路由存在 |
| 行政/OA | 六类申请、分步/代理审批、办理分工、财务接收、通知、人员交接与 Office 附件 | 金额条件分支、自动催办、全文检索、薪酬/总账/WMS 未实现；现场流程另验 |
| 单一窗口 | 源草稿、手工锁定、XML、schema 4.0 认证交接包、受管本机档案与回执 | 官方卡/客户端、签名、真实回执变体和企业多卡流程；详见[设计](./单一窗口对接代码级设计.md) |

已有定向测试或本机运行记录，不等于所有场景和平台全部通过。旧计划中的“替换手写 PDFium”“六份默认模板尚未转换”“WebDAV 只支持 HTTP”“通用模板分页全部未实现”均不再作为现行待办。

## 3. 明确的后续改进

| 优先级 | 工作 | 完成边界 |
| --- | --- | --- |
| P1 | 单证主流程与报表逐页对照 | Document 的资料→制单→校验→设计/预览→Excel/PDF→备份恢复；同一匿名输入核对金额、字段、页数、印章、长内容及错误路径 |
| P1 | HS 实际操作闭环 | 搜索→详情→候选审核→发票回填→保存；保留当前标准/历史实例区分、推荐预算、排序及源故障错误 |
| P1 | 对专用查询测量后下推 | 保留权限、计数/页面同一快照、精确数值和非分页数组合同；需要新索引时追加迁移 |
| P1 | 浏览器/桌面真实交互 | 中文 IME、键盘/粘贴、表格滚动、草稿、取消、原生对话框及实际打印；各 OS 分开记录 |
| P2 | 内部强类型逐步演进 | Engine/Storage 仍广泛使用 serde_json::Value；按用例引入明确命令/查询类型，在边界转换生成 DTO，不全仓重写 |
| P2 | 实际 PDF 页图预览 | 在明确需求下通过官方契约接入 PDFium，绑定草稿修订、权限及缓存；保留可编辑画布 |
| P2 | 正式平台交付与升级 | MSVC、ARM64、Linux/macOS、Docker、安装与签名更新各自验收；不宣称旧 .NET 包直接升级 Rust |

HS 当前采用受控静态 HTTP，没有旧浏览器取语义 DOM 的后备能力。源站依赖 JavaScript、登录或验证码时明确报不可读取/不支持；只有真实需求确认后才设计独立可选适配器，不复用桌面会话或绕过验证码。

高级 HTML/CSS/Scriban 报表解释器已退出范围；不把它列为迁移缺口，不恢复 Chromium 报表生成。旧 HTML 夹具仅作版式对照，现用 .dtpl 和 Rust 输出才是运行链路。

## 4. 修改与验证顺序

先用真实输入定位差异，修通用规则，再补必要回归；页面只组合展示，草稿/查询在 hook/model/service，SQL 在 Storage，平台文件/进程在适配边界。数据库从版本 5 起有序事务升级，不能删除旧数据冒充修复。

一批实现后集中执行 Rust fmt/tests/all-features、前端构建与相关交互、脚本/文档/依赖治理；数据库变更另用真实 PostgreSQL 18 与 SQLite 验证。测试失败、ignored、未运行平台分别报告。历史 C# 测试不计作 Rust 通过，源码和锁文件推送不等于正式安装包已经发布。

## 5. 按原版逐页补齐的实施台账

| 页面/流程 | 共用 Rust 落点 | 验收重点 |
| --- | --- | --- |
| 启动、登录、授权、退出 | accounts/auth/licensing/lifecycle、Server/Tauri | 初始化、撤权、过期、回环令牌、任务与进程退出 |
| 工作概览、待办、任务 | dashboard/crm_dashboard/worklist/tasks | 公司范围、日期、汇总、分页、恢复、取消/重试 |
| 发票、明细、信用证、统计 | records/workflows/invoice_query/letter_of_credit | 五页签、精确金额、草稿、核对、HS/AI、跨页导出 |
| 付款报销打印 | records/workflows/reports | 本人/财务权限、收款快照、费用、草稿预览、实际 PDF |
| 客户、供应商、商机、跟进 | crm/sales/related_records/party_files | 关联、候选并发、状态流转、报价、导入导出 |
| 邮件与模板 | email/email_templates、export-doc-mail | 收件人/附件权限、SMTP、模板变量、投递/取消/失败 |
| 业务资料 | attachments/attachment_categories | 分类、版本、并发、预览取消、原文件、容量及删除审计 |
| 基础资料、HS | records/custom_options/hs*、export-doc-hs | 引用保护、远程选择、年度/知识数据、联网语义与审核 |
| 报表模板、设计器、单据包 | report_templates/report_template_files/report_assets/reports | 身份/默认值、私人/共享、保存冲突、完整票面及多模板输出 |
| 单一窗口 | single_window、Domain 与独立协议 crate | COO/ACD 独立草稿、词典、导出审查、认证包和真实客户端 |
| Excel、OCR、装柜、汇率 | excel/ocr/packing/exchange 与独立能力 crate | 文件/格式/公式、精度、受控资源、实际输出与失败清理 |
| 人员、组织、会议/物品、六类 OA | personnel/organization/office/oa/handling | 私密资料、账号联动、库存、审批、办理、交接与历史 |
| 公告、通知、账号权限 | communication/accounts/permission_templates | 受众、版本回执、关联业务授权、权限来源与会话撤销 |
| 设置、审计、维护和交付 | settings/audit/maintenance/team_backup、Tauri | 敏感配置、备份恢复、许可证、资源/依赖、正式升级 |

逐页操作范围仍与[迁移核对表](./Rust原生功能迁移核对表.md)、[导航说明](./导航与页面简化说明.md)及各专题相互对应；本表是验收入口，不是按文件存在自动打勾的完成清单。
