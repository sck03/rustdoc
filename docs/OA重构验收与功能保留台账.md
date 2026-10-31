# OA 重构验收与功能保留台账

核对日期：2026-10-31。对应[执行方案](./OA审批与组织权限整体重构执行方案.md)的 T01–T42、F01–F20、S1–S8 和 A01–A40。软件实现和本机可执行门禁已完成；Windows 系统 IME 候选窗及独立平台限制在下方明确记录。清理和 Git 回执见文末，不把本机结果扩大为所有平台验收。

## 2026-10-31 前端结构与视觉追加验收

本批从 `98f5f7a4` 开始，工作分支为 `codex/oa-interface-completion`。完整页面审查、产品名称及设计参考见[前端视觉与产品命名规范](./前端视觉与产品命名规范.md)。原始日志位于 `.codex-runtime/oa-interface-20261031/`，与下方账号/组织/OA 内核批次分开记账。

| 检查 | 本批结果及证据 |
| --- | --- |
| Rust 完整门禁 | `final-fmt.log`、`cargo-workspace-test.log`、`cargo-workspace-check.log` 均退出 0；含 doc-tests 82 组，481 passed / 0 failed / 5 ignored |
| Web 构建与通用门禁 | `final-web-build.log` 通过；198 个缩放、84 个视觉场景通过；手机卡片修复后 `final-visual-cards.log` 追加 42 个平板/手机场景通过，像素阈值保持 0.5% |
| 业务列表、资料与 OCR | 33 项业务界面、15 项导航/报表选择、9 项 OCR 回归通过；资料卡片 320/390/768/1440 宽度检查；多行字段增加边界断言并目视确认 |
| 真实业务布局 | `final-layout-review-verified.log` 通过：示例发票、长中英文客户名、供应商、跟进/商机、具体记录跳转、申请分类搜索、文件任务展开状态保留；1440/390 画面与无障碍检查 |
| OA、组织与权限布局 | 113 个办公场景、真实 OA completion、账号/审批权限、单位身份、27 项导航重组回归通过；组织延迟挂载/保留输入、首分类异步加载、HS 横排和申请按钮白字单独断言 |
| 六类模板与批量文件 | 初次返回预览 504；Rust 全量结束后 `final-batch-file-isolated.log` 独立完整复验通过，未延长时限或放宽断言 |
| Windows Tauri 三版 | Document / Sales / Full 均从本批工作树 locked 重建并通过真实桌面回归，日志 `final-desktop-*.log`。修复启动前短暂 Full 标题；含真实 OCR、表格/中文文本、原件/版本、权限和维护。Full 的 16 份/16 页实际 PDF 已渲染查看；小纸张沿用尺寸与已有裁切边界 |
| 脚本、依赖与规模 | 脚本套件、56 项 Actions 引用、updater、release contract、公开源码通过；835 components，unresolved=0 / disallowed=0。最终样式和源码规模通过；Web 生产 88,543 行，比原基线减少 286 行 |

本批不改数据库与 API；下方 PostgreSQL 18、容量、备份恢复为上一内核批次证据，不冒充本批重跑。Windows 系统 IME 候选窗、其它 OS/架构、Docker daemon、实体打印继续各自验收。三版程序位于 `artifacts/oa-completion-desktop/ExportDocManager.Tauri.{Document,Sales,Full}`；构建版本中的 Git 标识为打包时父提交，实际版本以包清单为准，包内含本批工作树实现。

本批按明确来源、活动进程、链接/Git 边界及逐文件 SHA-256 清理 11 个已结束界面测试根，确认全部删除 **418,219,133 字节（约 398.85 MiB）**。保留最新同名截图/PDF/结果约 12.35 MiB；删除量是原文件长度，不能算作磁盘净释放。一次 Code Cache “目录不是空的”后，仅重验原清单剩余文件并逐项删除，最终 0 个残留根。551 个实际/私有保留文件 SHA-256 全部一致；最新三版程序、Cargo/npm/浏览器及原生资源缓存保留。精确清单和回执为本批 `cleanup-plan.json`、`cleanup-receipt.json`、`cleanup-result.json`；精选证据在 `retained-evidence/`，当前桌面复现输出另位于 `artifacts/oa-interface-desktop-validation/`。

界面实现 `63569ce5` 已普通快进推送 main，工作分支 `codex/oa-interface-completion` 已删除。[发布脚本检查](https://github.com/sck03/rustdoc/actions/runs/41203948490)通过；[首轮主验证](https://github.com/sck03/rustdoc/actions/runs/41203948557)的前端布局通过，Chromium 编辑回归发现设置页表单说明在灰背景上的对比度为 4.39，低于 4.5。共用表单说明颜色改用 `--text-secondary`，保留对比度标准。随后本地完整执行 CI 的七组前端脚本全部通过：发票编辑、60 项单据编辑、V3 设计器、38 项取消操作、邮件模板、并发反馈、浏览器会话/超时/上传；`ci-test-*.log` 保存结果。`contrast-visual.log` 的 84 场景通过，不更新参考图或放宽阈值；生产构建、样式/源码规模及三版包同步通过。后续远端状态以实际 workflow 回执为准。

补充修复 `eb6a6357` 已普通快进推送 main，工作分支 `codex/frontend-description-contrast` 已删除。该代码提交的 [Release script validation](https://github.com/sck03/rustdoc/actions/runs/41204986669) 与 [Rust native delivery validation](https://github.com/sck03/rustdoc/actions/runs/41204986684) 均为 success；后者实际通过前端构建/布局及 Chromium 编辑/异步回归，Rust 核心与 full-validation 因本次仅改 CSS/文档而 skipped，不计为重新通过。Rust 仍以上方本机完整门禁为证。本回执是后续文档修改，不改变已验证代码；原始远端结果保存在 `github-contrast-runs.json` 与 `github-contrast-native-jobs.json`。

## 账号、组织与 OA 内核批次：实现与证据规则

源码基线为 schema 11；字段、DDL、指纹及关系见[数据字典](./账号组织OA新基线数据字典.md)，操作见[人员组织权限与 OA 手册](./人员组织权限与OA操作手册.md)。起点为 `e9c1aa8a`，工作分支为 `codex/oa-integration-completion`。起点未提交内容已备份，保留原业务数据和用户私有备份。

下表中的“通过”仅指写明的检查；模型或模拟界面检查不等于真实 HTTP、数据库或桌面验证。“待复验”包含已实现但本轮证据不足的内容。历史 schema 10、旧 .NET 和其他平台的结果不纳入本轮数字。

本轮日志根为 `.codex-runtime/oa-completion-20261031/`。清理后保留日志、精确清单及 `retained-evidence/` 中的容量/恢复结果和精选 PDF/截图；原 `artifacts/*-ui/` 与隔离测试库目录已退役。上述本地证据不提交 Git；测试入口、具体断言和本台账保留在源码。

## 账号、组织与 OA 内核批次：集中门禁

| 检查 | 当前结果及证据 |
| --- | --- |
| Rust 格式 | `fmt-check-33.log` 通过 |
| Rust 全 workspace | `workspace-20.log` 含 doc-tests 的 82 组已结束：480 passed、1 failed、5 ignored；唯一失败是通信夹具遗漏有效命令/任务键，修复后 `communication-28.log` 两项通过。有效覆盖为 481 项通过，不能把首次命令退出 101 改写成退出 0 |
| 全 features / API 生成一致性 | `allfeatures-33.log` 和 `generator-check-28.log` 均退出 0；727 operations / 659 schemas，后续 SQL/夹具修正不改变生成契约 |
| Web 构建 | `web-build-24.log` 通过，含单位岗位入口、分类默认值、独立办理候选及操作后刷新期间禁止重复审批 |
| 可访问性 / 规模 / 视觉基线 / 权限模型 | `web-test-*-25.log` 通过：规模 198 场景、视觉 84 场景、办公界面 113 场景及办公模型；权限与原业务模型沿用未受后续改动影响的本轮结果 |
| 真实单位身份 / 审批权限 / 调岗工作流 | `test_unit_identity_ui-13.log`、`test_approval_permissions_ui-13.log`、`test_organization_workflow_ui-13.log` 通过 |
| 办公页面布局及操作 | `office-workspace-15.log` 通过，113 个模拟界面场景，含 1440/1024/768/390/320 宽度和无障碍检查 |
| 公告通知 / 财务接收 | `communication-15.log` 真实 React → Rust HTTP 通过；`artifacts/communication-ui/1793429318047` |
| 资源审批单与权限卡片 | `test_oa_integration_completion_ui-24.log` 通过，覆盖五种宽度、单位岗位、并行加签/减签/随即审批、单一 hub、附件及任务交接 |
| V3 审批单编辑与预览 | `approval-documents-28.log`、`material-documents-28.log` 与 `report-approval-28.log` 通过；受限 PDF 1 页、完整 PDF 3 页，四页均渲染查看。私密标记仅见于完整 PDF，ZIP 原件逐字节一致；续页不再分离标题与签署 |
| SQLite OA / 人员 / 资源 | `workspace-20.log` 中 OA 11、office 2、personnel 1、report_workflow 14、sales 3 等均通过；六类申请真实网页 `workspace-ui-26.log`、办理 `test_oa_handling_ui-22.log`、账号/私人模板 `*-21.log` 通过 |
| 原专业前端 | `business-*-final.log` 的 16 项通过；`test_batch_file_workflows_ui-16.log` 独立复验通过六类空白设计、原生预览和批量文件 |
| PostgreSQL 18 | `postgres-all-30.log` 全部通过：共享存储 1 项、发布恢复/物理恢复 2 项、完整团队业务合同 1 项，均为实库且 0 ignored。团队合同耗时 1,314.67 秒；隔离集群正常关闭 |
| 20 会话容量 | `postgres-capacity-32.log` 通过：连接池 1/4 分别完成 1,007 次操作，0 次 429 重试；最终写入一致、审批/输出完成、活动任务归零，具体延迟见下表 |
| PostgreSQL 网页维护 | `postgres-maintenance-33.log` 通过：真实 React 创建/下载/上传备份、计划备份、在用实例拒绝、离线恢复及幂等、跨数据库/DataRoot 迁移、原管理员/密钥/模板恢复；隔离集群正常关闭 |
| Windows Tauri | `desktop-Full-29.log`、`desktop-Document-29.log`、`desktop-Sales-29.log` 和 `desktop-shutdown-29.log` 均通过；Full 的 16 份/16 页实际 PDF 已渲染查看，运行依赖 434 项通过 |
| 菜单与直接地址授权 | `permission-navigation-30.log` 通过：8 种角色的真实 React 菜单、直接地址与 Rust 能力一致，并验证受限发票/付款操作 |
| 脚本 / 工作流 / 公开源码 | `script-suite-31.log`、Actions/updater/验证计划 `*-32.log`、`public-source-32.log` 通过；清除编辑残留后的公开源码 1,884 文件、69.29 MiB |
| 依赖治理 | `governance-verified.log`：835 个组件，unresolved=0、disallowed=0；依赖锁图/声明未变更时有效 |

## F01–F20 原功能保留

所有入口继续按各业务自身状态工作；没有销售转单、共享客户/供货商主档、OA 报销转付款打印或商业审批前置。

| 编号 | 页面与专业操作 | 本轮验证入口及状态 |
| --- | --- | --- |
| F01 | 登录、产品裁剪、许可、更新、退出 | `native_service` 13 项、三个最新桌面包、退出专项和 updater 合同通过 |
| F02 | 工作概览、我的申请/审批/办理、文件任务 | `dashboard_currency` 2 项、引擎任务/发布单元合同及真实 OA hub 通过；PostgreSQL 实库恢复另列 |
| F03 | 客户、联系人、跟进、导入导出 | `business_service` 3 项和 business-features UI 通过，包含显式权限版本下的导入/导出 |
| F04 | 商机与报价及各自状态 | business-feature-models / business-features UI 与 `sales_service` 3 项通过 |
| F05 | 供货商、联系人、评价及概览 | `business_service` 关系和评价通过；supplier-assessment-analytics 模型通过 |
| F06 | 制单基础资料、商品、港口、银行、字段候选 | reference-catalog 模型、concurrency-feedback UI 通过，保持制单资料独立 |
| F07 | 发票五页签、明细编辑、保存续编和草稿 | invoice-editing、concurrency-feedback UI 通过；精确金额领域测试通过 |
| F08 | 实际/报关、同号、复制、核对、作废 | `invoice_cloning`、`native_service` 和原业务状态界面合同通过 |
| F09 | 查询、统计、原币与利润、明细导出 | `dashboard_currency`、授权/存储查询合同及原前端统计回归通过 |
| F10 | 付款四页签、自由文本/备用字段、私人模板、PDF | payment-printing UI、`payment_printing_contract` 双库和 Full 桌面实际付款/报销 PDF 通过 |
| F11 | 六类票面、空白设计、V3、素材与共享 | invoice-report / report-template-draft / batch-file UI、`report_workflow` 14 项与最新 Full 桌面 16 页实际 PDF 通过；小纸张只验尺寸与既有裁切 |
| F12 | Excel/托单、预检、映射、导入导出及单据包 | `excel_workflow`、`document_package_workflow` 通过；batch-file UI 独立复验通过 |
| F13 | OCR、信用证、回填与取消 | smart-ocr / abortable-operations / concurrency-feedback UI 及最新 Full 包真实 OCR 通过 |
| F14 | HS 编码、年度资料、历史及清理保护 | `hs_lifecycle` 3 项、reference-catalog 模型通过 |
| F15 | 单一窗口、映射、批次及回执 | `single_window_service` 2 项、单一窗口 crate 和原专业 UI 合同通过；外部海关实际提交不包含在本机测试中 |
| F16 | 装柜、方案保存/加载、版本保护 | container-packing 模型、concurrency-feedback UI 通过 |
| F17 | 邮件、模板、附件、结果不明与汇率 | `mail_service` 5 项、email-template-workspace UI 通过；外部投递与撤权由引擎原任务合同验证 |
| F18 | 业务原件、版本、下载与归档 | `document_files_service` 2 项、`document_service` 1 项及 OA 原件下载/ZIP 字节核验通过 |
| F19 | 组织/人员、13 类申请、资源、通知、交接 | 单位、调岗、资源和通信真实 UI，高级流程操作、材料、任职生效与交接双库合同通过 |
| F20 | 账号权限、设置、审计、备份恢复与维护 | 审计、维护、灾备单元场景、新权限版本双库合同及真实 PostgreSQL 网页全库备份/恢复/迁移通过 |

## S1–S8 贯穿场景

| 编号 | 具体入口 | 本轮状态 |
| --- | --- | --- |
| S1 | `organization_workflow_contract`、`unit_contract`、`handling_contract`、`workflow_operations_contract`；真实 organization-workflow UI | 真实调岗、完整双库合同与流程操作边界组合通过 |
| S2 | `oa_archive_contract`、`material_contract`、`approval_voting`；communication / oa-approval-documents UI | 材料投影、拒绝受限 ZIP、财务接收通过；受限 PDF 1 页/完整 PDF 3 页全部渲染查看，原件 ZIP 逐字节一致 |
| S3 | `appointment_contract`、`identity_periods`、`oa/runtime_tests`、`unit_contract` | 期间、未来生效/重启/冲突/取消单元场景及稳定任职 ID 双库合同通过 |
| S4 | `excel_workflow`、`invoice_cloning`、`document_package_workflow`、invoice-editing / invoice-report / batch-file UI | Excel、手工、编辑、单据包、批量 UI 独立复验及 Full 桌面/OCR 全部通过 |
| S5 | `account_scope`、`permission_identity_contract`、`business_service`、`sales_service` | SQLite 及 PostgreSQL 适用合同通过，8 角色真实菜单/直接地址与受限发票、付款操作通过 |
| S6 | `payment_printing_contract`、payment-printing / communication UI | 两套独立界面、双库输出与授权通过 |
| S7 | `personnel_contract`、`office_contract`、`account_scope`、`tasks/tests/publication` | 人员交接、原归属保护和文件发布撤权双库合同通过 |
| S8 | `team_backup/tests`、`excel_workflow`、`test_postgres_maintenance_ui.mjs`、`test_native_desktop_editions.mjs` | SQLite 恢复、PostgreSQL 实库物理恢复及真实网页备份/跨库迁移、三个桌面包分别通过 |

## A01–A40 合同索引

入口用于定位具体断言，不以文件存在或测试名称代替通过证据；最终结果与上方门禁、F/S 表交叉核对。

| 编号 | 断言入口和当前限制 |
| --- | --- |
| A01 | `identity_periods`、`appointment_contract`：不重叠期间、同人多岗、重新任职新 ID 双库合同通过 |
| A02 | `personnel_contract`、`unit_contract`、`units/account_security`：交接、到期、安全停用与历史保护 |
| A03 | `permission_identity_contract`、`permission_policy`、`account_permissions`：发布绑定、范围限制、转授权及预览 |
| A04 | unit-identity UI、concurrency-feedback、abortable-operations UI：切换、缓存与迟到响应 |
| A05 | `material_contract`：详情/列表/通知/历史/附件/归档投影；受限实际 PDF 无私密标记，完整 PDF 包含标记，均已渲染查看 |
| A06 | `approval_voting`、`approval_contract`：真实计票、同人去重、否决/或签/比例 |
| A07 | `approval_graph`、`organization_workflow_contract`：条件、缺人、并行汇合 |
| A08 | `approval_operations`、`workflow_operations_contract`：加减签、指定退回、代次、旧签署引用 |
| A09 | `workflow_operations_contract`、子流程领域校验：固定版本、拒绝循环、累计预算与传播 |
| A10 | `oa_contract`、`office_contract`：审批与取消/资源争用的事务冲突 |
| A11 | `workflow_operations_contract`、`command_receipts`：同键重试、不同行为冲突与回执 |
| A12 | `business_calendar`、`oa/runtime_tests`：时区、节假日、暂停、提醒去重、无自动批准 |
| A13 | `oa/runtime_tests`、`appointment_contract`：未来变更重新核验、Blocked、取消保留签署 |
| A14 | `office_contract`、`resource_contract`、`handling_contract`：资源预留/发放/分次归还、交接及补偿 |
| A15 | `tasks/tests`、`team_backup/tests`、storage contract：取消、崩溃恢复、原子发布通过；PostgreSQL 发布恢复与物理恢复两项实库通过 |
| A16 | `approval_contract`、`workflow_operations_contract`、权限发布合同：草稿与在途冻结 |
| A17 | organization-workflow、oa-approval、office-workspace UI：列表/图形与窄屏；真实系统中文 IME 待验，文本注入单列 |
| A18 | oa-approval-documents / integration-completion UI、`oa_archive_contract`：V3、原件、续页及受限实际 PDF 均通过 |
| A19 | storage contract、`team_backup/tests`：两库新建、版本/指纹拒绝、恢复不改原库通过；网页维护流程另列 |
| A20 | contracts 基线保留测试、生成器测试通过：退役目标接口，其他 schema/认证/错误保留；727 operations / 659 schemas 生成一致 |
| A21 | `native_service`、team_service、desktop-editions：产品裁剪、最新双库与三个桌面包通过 |
| A22 | storage `organization_query`、`generic_query`、`workflow_query` 双库及 native-capacity 通过：SQL 过滤/游标/计数和 20 会话样本；容量规模和角色限制见下表 |
| A23 | 本文 F01–F20 已逐项登记实际合同/操作/输出；外部海关投递、系统 IME 与独立平台边界继续保留 |
| A24 | business_service、account_scope、制单输入测试：客户/供货商与票面资料独立 |
| A25 | sales_service、business-feature-models、supplier-assessment-analytics：原状态与评价 |
| A26 | invoice-editing、concurrency-feedback、领域精确计算 |
| A27 | invoice_cloning、native_service：原单据关系、核对/作废和单位隔离 |
| A28 | account_permissions、permission_identity_contract、permission-navigation UI：权限/模块/菜单分层通过，8 角色的真实入口和直接地址分别验证 |
| A29 | report_workflow、document_package_workflow、invoice-report UI、桌面 V3 商业输出通过；最新 Full 的 16 页 PDF 已查看 |
| A30 | excel_workflow、batch-file / smart-ocr / abortable-operations UI：原输入与回填 |
| A31 | hs_lifecycle、exchange 单元测试、packing-credit UI：历史资料、适用期、金额及草稿 |
| A32 | container-packing、single-window crate、桌面单据交接检查 |
| A33 | mail_service、tasks publication、email-template UI：模板、原件及结果不明 |
| A34 | payment_printing_contract、payment-printing UI、communication 财务接收 |
| A35 | dashboard_currency、invoice query/summary 存储合同：授权先于聚合 |
| A36 | team_backup 恢复、excel_workflow、PostgreSQL maintenance UI 通过：全库内容、密钥/模板/账号恢复、外部动作结果不明边界；实际 SMTP/海关投递不在此脚本内 |
| A37 | navigation / invoice-editing / concurrency UI：直达入口、返回查询和多草稿 |
| A38 | material_contract、permission_identity_contract：任务材料资格不会授予整个业务域 |
| A39 | desktop-editions、native_service、Excel/制单 UI 通过：Document/Sales 个人版本无 OA 开通前置，最新三个桌面包分别通过 |
| A40 | personnel_contract、account_scope、office_contract：原归属和创建/签署历史不改写 |

## 源码规模审查

2026-10-31 按完整重构重新登记总量基线：Web 生产源码 88,829 行；Rust 核心生产源码 91,008 行；Rust 测试/示例 26,816 行；普通工程自动化 39,265 行。总量增加对应身份/雇佣/任职与职责、权限不可变版本和显式限制、流程命令/运行事实/材料投影、资源审批单与工作日历、双库约束及回归；生成客户端仍排除在生产规模统计之外。Storage 适配边界与 Engine 已认证分派拆成独立模块，旧人员直接变更弹窗和重复 OA hub hook 已删除。办公资源组件 1,033 行、组织目录组件 723 行；增加岗位入口、版本绑定和申请/办理交接。保持各模块原有单文件上限，检查未发现单文件超限；以后增量仍受原增长限额约束。

## 容量与平台登记

本机 Windows x64 GNU/Debug 采用隔离 PostgreSQL 18、连接池 1/4、20 个 Admin 业务读取/制单会话、220 张发票、5 份报销、5 个关联实际人员的审批账号和 2 项并发 PDF。签署资格与管理员维护身份分开。测量前固定边界为单请求 35 秒、单输出等待 120 秒、429 最多重试 12 次；要求最终写入值一致、五份申请批准、输出成功、活动任务归零。宿主为 Intel Core i5-9400F（6 核）及约 16 GiB 内存。以下为 `native-capacity/ec9af368-eef0-4d9e-8f00-ca02044a6b0c/result.json` 实测，耗时不含建库/样本准备；它不代表普通员工授权压力、大历史库、库存竞争、完整 OCR/Excel 或所有专业页面的性能，不作为产品 SLA。

| 连接池 | 操作数 | 测量耗时 | 操作/秒 | P50 / P95 / P99（毫秒） | 峰值工作集 | 429 重试 |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 1,007 | 95.25 秒 | 10.57 | 1,678.39 / 3,265.44 / 6,585.44 | 138.58 MiB | 0 |
| 4 | 1,007 | 36.60 秒 | 27.51 | 616.76 / 1,013.97 / 5,195.83 | 143.10 MiB | 0 |

两组均核对了 20 张最终保存值、五份批准结果和输出成功，结束时 `storage.failed=false`、`jobs.active=0`；没有为了通过测试放宽请求时限或重试上限。连接池 4 保留更好的并发吞吐，无需引入额外缓存或数据库。

Firefox/WebKit、Linux/macOS/ARM64 真机、Docker daemon 生命周期、MSVC 发布、实体打印分别由对应环境验收；本机结果不得替代。系统签名/公证不在本轮范围。

## 清理与交付

已核实的 11 个旧测试目录共 1,528,759,394 字节移至 `E:/rustdoc/待手动删除-20261031/旧OA测试输出`，清单为 `E:/rustdoc/待手动删除-20261031/移动清单.json`。当时仅移出项目。本轮再次盘点时原待删目录已经不存在；不推断由谁删除，也不把这笔历史移动计入接下来定向清理的实际释放量。

全部 Cargo、数据库和界面测试结束后完成定向清理：360 个 Cargo fingerprints 确认的测试 EXE/元数据删除 20,101,534,372 字节，复盘候选为零；41 个测试根中的已确认删除量为 1,652,542,812 字节；104 个格式化/编辑残留删除 1,668,437 字节。合计 **21,755,745,621 字节（约 20.262 GiB）**。这是已删除文件长度合计的保守值，不是磁盘净变化，也没有把移动计为释放。

16 个本轮测试根的未删残留集中到 `E:/rustdoc/待手动删除-20261031/网页测试` 或 `隔离测试库`，已知移动长度 467,080,951 字节。另有 3 个部分删除后遇到 ACL 限制的根，原长度合计 304,052,566 字节；其删除量和剩余移动量均不猜测、不计入上述数字。`maintenance-tests` 的少量剩余目录另存 `maintenance-tests-残留`。历史 8 个受限测试根已整体移入同一待删目录的 `历史受限测试输出`，25 个后代访问限制仍保留。目录内附说明和两份清单，用户可手动删除。

业务库、实际程序、用户/私有备份、起点补丁和可复用 Cargo/npm/浏览器/原生资源缓存继续保留。受保护的 643 个文件共 1,277,356,237 字节，清理前后 SHA-256 全部一致；其中包含三个最新桌面包。41 个原测试根均已不存在，工作树和 Git worktree 边界再次检查。完整回执为本轮 `cleanup-final-result.json`。

重构实现提交 `4e9036ec4c6ed769938bb9331ddd2abbc895f68e` 已普通快进推送 [GitHub main](https://github.com/sck03/rustdoc/tree/main)。推送前再次获取远端并确认无未审查漂移；`codex/oa-integration-completion` 已合并删除。本地仅 `main`，远端仅 `origin/main`（`origin/HEAD` 为指向它的符号引用），仅保留主工作树。此回执随后随 main 文档提交保存；本机证据不代替本次推送触发的 GitHub Actions 结果。
