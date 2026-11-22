# 手册界面图片

来源：2026-11-17 当前源码运行 `scripts/test_oa_custom_ui.mjs` 的匿名培训场景，已完成创建、表单、仿真发布、申请、审批、办理及 PDF 下载回归；运行 API 与当前生成契约一致，1440/1024/390 宽度及无障碍检查通过。

- `form-designer.png`：`custom-designer-1440.png` 的内容局部，保留流程页签、适用部门及前三个字段设置。
- `request-details.png`：`custom-completed-1440.png` 的申请详情内容，保留完成状态、审批单操作、材料及签署/办理记录。

仅裁去导航和空白边缘，没有改写显示的业务结果。场景中的姓名、客户、工号和申请均由测试创建。更新截图后须重新生成并检查相关 PDF。

2026-11-20 新增匿名界面：

- `project-duties.png`：`scripts/test_foundation_options_ui.mjs` 本批通过的分页选择、保存与重新打开场景，来源 `artifacts/foundation-options-ui/1795173450622/project-scoped-responsibility.png`；仅截取编辑弹窗上部，突出职责、项目和有效期，未改写字段内容。
- `packing-workspace.png`：本批 `scripts/test_packing_credit_workspace_ui.mjs` 装柜工作区 1366 宽度截图，来源 `artifacts/packing-credit-workspace-ui/1795172973904/packing-layout-1366.png`。

既有 2026-11-17 的 OA 表单/完成详情截图保留原来源日期，作为未变更界面的操作示例，不冒充本批重新执行的截图。

2026-11-22 新增匿名界面：

- `hs-import-preview.png`：本批真实 React/Rust HTTP 的 301 行 XLSX 导入预检，来源 `artifacts/review-consistency-http-ui/1795341256567/hs-import-changes.png`；裁去导航和重复抬头，保留摘要、字段前后值、分页与确认操作。
- `attachment-category.png`：同次实际 HTTP 验证中的资料分类筛选，来源 `artifacts/review-consistency-http-ui/1795341256567/attachment-category.png`；裁取目录抬头、筛选及记录行。示例发票、资料及编码均为合成测试数据。

本次只裁剪截图，不替换显示内容。OA 表单、详情及项目职责图片继续按原来源日期标注；其中没有已退役的 OA 附件分级控件。
