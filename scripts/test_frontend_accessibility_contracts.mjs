import fs from "node:fs";
import path from "node:path";
import ts from "../apps/export-doc-web/node_modules/typescript/lib/typescript.js";

const root = path.resolve(import.meta.dirname, "../apps/export-doc-web/src");
const failures = [];

for (const file of walk(root)) {
  if (!file.endsWith(".tsx") && !file.endsWith(".ts")) continue;
  const sourceText = fs.readFileSync(file, "utf8");
  const sourceRelativePath = path.relative(root, file).replaceAll("\\", "/");
  const source = ts.createSourceFile(file, sourceText, ts.ScriptTarget.Latest, true, file.endsWith(".tsx") ? ts.ScriptKind.TSX : ts.ScriptKind.TS);
  if (/\bwindow\.confirm\s*\(|(^|[^.\w])confirm\s*\(/m.test(sourceText)) {
    failures.push(`${path.relative(path.resolve(import.meta.dirname, ".."), file)}: 不允许使用原生 confirm，请使用应用确认组件`);
  }
  if (/\b(?:success-alert|error-alert|info-alert)\b/.test(sourceText)
    || /className\s*=\s*["']alert["']/.test(sourceText)
    || /className\s*=\s*\{[^}]*["']alert["']/s.test(sourceText)) {
    failures.push(`${sourceRelativePath}: 业务提示必须使用 InlineNotice 提供统一反馈语义`);
  }
  visit(source, source);

  if (sourceRelativePath === "features/invoices/InvoiceReportPreviewPanel.tsx") {
    const advancedPanel = sourceText.indexOf("<InvoiceReportAdvancedExportPanel");
    const lazyGuard = sourceText.lastIndexOf("showExportAdvanced ?", advancedPanel);
    if (advancedPanel < 0 || lazyGuard < 0) {
      failures.push(`${sourceRelativePath}: 高级导出组件必须按展开状态延迟挂载，避免隐藏模板和邮件表单占用渲染资源`);
    }
    for (const extractedCoordinator of ["useInvoiceFileExportOperations", "useInvoiceDocumentPackageWorkspace"]) {
      if (!sourceText.includes(extractedCoordinator)) {
        failures.push(`${sourceRelativePath}: 报表输出协调器必须保持职责拆分：${extractedCoordinator}`);
      }
    }
    for (const leakedOperation of ["selectSavePdfPath", "selectSaveExcelPath", "startInvoiceDocumentPackageSaveToPathJob", "startInvoiceDocumentEmailJob"]) {
      if (sourceText.includes(leakedOperation)) {
        failures.push(`${sourceRelativePath}: 文件与单据包操作不应重新回流主协调器：${leakedOperation}`);
      }
    }
  }

  if (sourceRelativePath === "features/invoices/InvoiceReportAdvancedExportPanel.tsx"
    && !sourceText.includes('className="report-export-advanced-body"')) {
    failures.push(`${sourceRelativePath}: 高级导出工作区缺少稳定的布局容器`);
  }

  if (sourceRelativePath === "features/query/QueryPage.tsx"
    && (!sourceText.includes('<details className="form-section query-export-panel"')
      || sourceText.includes('<details className="form-section query-export-panel" open'))) {
    failures.push(`${sourceRelativePath}: 低频查询导出区必须使用默认折叠的原生 details，避免长期占用结果页空间`);
  }

  if (sourceRelativePath === "features/master-data/masterDataConfigs.ts"
    && /name:\s*["']category["'][^}\n]*required:\s*true[^}\n]*PayeeCategory/.test(sourceText)) {
    failures.push(`${sourceRelativePath}: 收款对象分类是可选辅助信息，不得作为保存必填项`);
  }

  if (sourceRelativePath === "features/invoices/InvoiceEditorPage.tsx") {
    if (!sourceText.includes("useInvoiceItemsWorkspace")) {
      failures.push(`${sourceRelativePath}: 商品库与明细表操作必须保持独立工作区 Hook`);
    }
    for (const leakedItemOperation of ["recalculateInvoiceItem", "createProductDraftFromInvoiceItem", "maxInvoiceItemHistoryDepth"]) {
      if (sourceText.includes(leakedItemOperation)) {
        failures.push(`${sourceRelativePath}: 明细编辑实现不应重新回流发票页面协调器：${leakedItemOperation}`);
      }
    }
  }

  if (new Set([
    "features/invoices/InvoiceEditorPage.tsx",
    "features/payments/PaymentEditorPage.tsx",
    "features/master-data/MasterDataEditorPage.tsx",
    "features/single-window/AgentConsignmentPage.tsx",
    "features/single-window/CustomsCooPage.tsx",
    "features/single-window/SingleWindowReferenceCatalogPage.tsx",
  ]).has(sourceRelativePath) && !sourceText.includes("useServerDraftSync")) {
    failures.push(`${sourceRelativePath}: 长表单必须使用 useServerDraftSync 防止服务器刷新静默覆盖本地草稿`);
  }

  if (new Set([
    "features/settings/UserManagementPanel.tsx",
    "features/settings/PermissionTemplateManagementPanel.tsx",
  ]).has(sourceRelativePath) && !sourceText.includes("useUnsavedChangesGuard")) {
    failures.push(`${sourceRelativePath}: 管理编辑页必须保护切换、刷新和浏览器导航时的未保存修改`);
  }

  if (sourceRelativePath === "ui/unsavedChangesGuard.tsx") {
    for (const historyGuardContract of [
      "popstate",
      "history.state",
      ".idx",
      "history.go(",
      "confirmEntryDiscardChanges",
    ]) {
      if (!sourceText.includes(historyGuardContract)) {
        failures.push(`${sourceRelativePath}: HashRouter 后退/前进保护缺少 ${historyGuardContract}`);
      }
    }
  }

  if (["ui/ConfirmationDialog.tsx", "features/invoices/InvoiceStatusReasonDialog.tsx"].includes(sourceRelativePath)) {
    if (!sourceText.includes("useModalDialog")) {
      failures.push(`${sourceRelativePath}: 模态框必须复用公共焦点循环、Escape 关闭和焦点恢复 Hook`);
    }
    for (const duplicatedModalImplementation of ["window.addEventListener(\"keydown\"", "previouslyFocusedElement", "querySelectorAll<HTMLElement>"]) {
      if (sourceText.includes(duplicatedModalImplementation)) {
        failures.push(`${sourceRelativePath}: 不得重新复制公共模态框键盘与焦点实现：${duplicatedModalImplementation}`);
      }
    }
  }

  if (sourceRelativePath === "features/invoices/useInvoiceItemsWorkspace.ts") {
    for (const workspaceContract of ["maxInvoiceItemHistoryDepth = 30", "currentItems.length > 500 ? 10", "currentItems.length > 150 ? 20", "readInvoiceItemHistoryDepth", "latestInvoiceItemsRef", "setInvoice((current)", "recalculateInvoiceItem", "masterDataRoot(\"products\")", "productLibraryEnabled", "productLibraryPageSize", "placeholderData: keepPreviousData"]) {
      if (!sourceText.includes(workspaceContract)) {
        failures.push(`${sourceRelativePath}: 发票明细工作区缺少历史、重算或商品库闭环：${workspaceContract}`);
      }
    }
  }

  if (sourceRelativePath === "features/invoices/InvoiceProductLibraryPickerDialog.tsx") {
    for (const productLibraryContract of ["ListPaginationControls", "totalCount", "onPageSizeChange"]) {
      if (!sourceText.includes(productLibraryContract)) {
        failures.push(`${sourceRelativePath}: 商品库选择器必须使用服务端分页并展示准确总数：${productLibraryContract}`);
      }
    }
  }

  if (sourceRelativePath === "features/master-data/HsCodeKnowledgePage.tsx") {
    for (const hsKnowledgeContract of ["useDebouncedValue", "placeholderData: keepPreviousData", "ListPaginationControls", "confirmSelected", "queryFn: ({ signal })"]) {
      if (!sourceText.includes(hsKnowledgeContract)) {
        failures.push(`${sourceRelativePath}: HS 实例、候选和历史列表缺少防抖/分页/准确反馈闭环：${hsKnowledgeContract}`);
      }
    }
    if (!sourceText.includes("candidates.data?.notice")) {
      failures.push(`${sourceRelativePath}: 历史资料候选必须向用户说明有界扫描窗口，避免把分批结果误认为完整数据集`);
    }
  }

  if (sourceRelativePath === "features/invoices/InvoiceHsKnowledgePanel.tsx") {
    if (/<form\b/.test(sourceText)) {
      failures.push(`${sourceRelativePath}: HS 查询面板不得嵌套发票外层 form，查询按钮会被浏览器误判为保存提交`);
    }
    for (const hsSearchContract of ['role="search"', 'type="button"', "onKeyDown", "maxLength={500}"]) {
      if (!sourceText.includes(hsSearchContract)) {
        failures.push(`${sourceRelativePath}: HS 查询面板缺少独立按钮、快捷键或输入边界：${hsSearchContract}`);
      }
    }
  }

  if (["features/suppliers/SupplierDirectoryPage.tsx", "features/opportunities/SalesOpportunityPage.tsx", "features/crm/CrmCustomerDirectoryPanel.tsx"].includes(sourceRelativePath)) {
    for (const listContract of ["usePagedDirectoryQuery", "(signal)", "ListPaginationControls"]) {
      if (!sourceText.includes(listContract)) {
        failures.push(`${sourceRelativePath}: 业务目录分页必须统一使用可取消查询和公共分页组件：${listContract}`);
      }
    }
  }

  if (sourceRelativePath === "ui/usePagedDirectoryQuery.ts") {
    for (const pagedQueryContract of ["keepPreviousData", "queryFn: ({ signal })", "query(signal)"]) {
      if (!sourceText.includes(pagedQueryContract)) failures.push(`${sourceRelativePath}: 公共分页查询必须保留旧页并取消过期请求：${pagedQueryContract}`);
    }
  }

  if (sourceRelativePath === "features/reports/ReportTemplateWorkspacePage.tsx") {
    for (const reportDraftContract of ["useUnsavedChangesGuard", "designerDraftContent", "hasUnsavedChanges", "handleRefreshTemplates"]) {
      if (!sourceText.includes(reportDraftContract)) {
        failures.push(`${sourceRelativePath}: 报表设计器必须识别画布草稿并保护未保存切换：${reportDraftContract}`);
      }
    }
  }

  if (sourceRelativePath === "features/jobs/JobCenterPage.tsx") {
    for (const taskCenterContract of ["messageTone", "useJobCenterOperations"]) {
      if (!sourceText.includes(taskCenterContract)) {
        failures.push(`${sourceRelativePath}: 任务中心缺少危险操作确认或稳定反馈语义：${taskCenterContract}`);
      }
    }
    for (const taskCenterFilterContract of [
      "function applyFilters(nextKeyword = keyword, nextStatus = status)",
      "function handleKeywordChange(value: string)",
      "if (!value.trim()) applyFilters(value)",
      "function handleRefresh()",
      "hasPendingJobCenterFilters(keyword, committedKeyword, pageNumber)",
      "applyFilters(keyword, value)",
      'onClick={() => applyFilters("")}',
      'title="刷新" aria-label="刷新" disabled={jobsQuery.isFetching || operations.isBusy} onClick={handleRefresh}',
    ]) {
      if (!sourceText.includes(taskCenterFilterContract)) {
        failures.push(`${sourceRelativePath}: 任务筛选必须在清空、切换状态和刷新时应用当前输入：${taskCenterFilterContract}`);
      }
    }
    if (/\<tr[\s\S]{0,240}tabIndex=\{0\}/.test(sourceText)) {
      failures.push(`${sourceRelativePath}: 任务表格行没有直接动作，不应伪装成可键盘操作控件`);
    }
    if (!sourceText.includes("useJobCenterOperations")) {
      failures.push(`${sourceRelativePath}: 任务查询、导出、删除和反馈协调必须保持在独立工作区 Hook`);
    }
  }

  if (sourceRelativePath === "features/jobs/useJobCenterOperations.ts") {
    for (const taskCenterWorkspaceContract of ["requestConfirmation", "handleCancelJob", "handleDeleteJob", "handleClearFinishedJobs", "messageTone"]) {
      if (!sourceText.includes(taskCenterWorkspaceContract)) {
        failures.push(`${sourceRelativePath}: 任务中心工作区缺少危险操作确认或稳定反馈语义：${taskCenterWorkspaceContract}`);
      }
    }
  }

  if (sourceRelativePath === "features/tools/container-packing/ContainerPackingWorkspace.tsx"
    && !sourceText.includes("useContainerPackingPdfExport")) {
    failures.push(`${sourceRelativePath}: PDF 导出状态与异步路径选择不得重新回流装柜展示组件`);
  }

  if (sourceRelativePath === "features/tools/container-packing/useContainerPackingPdfExport.ts"
    && (!sourceText.includes("downloadContainerPackingPdf") || !sourceText.includes("saveContainerPackingPdfToPath"))) {
    failures.push(`${sourceRelativePath}: 装柜 PDF 必须由后端受控模板渲染并保持浏览器下载/桌面保存双路径`);
  }

  const coordinatorContracts = {
    "features/single-window/CustomsCooPage.tsx": ["useSingleWindowLockedFields", "useCustomsCooProducerProfiles", "useCustomsCooAuthoritySelection"],
    "features/single-window/AgentConsignmentPage.tsx": ["useSingleWindowLockedFields"],
    "features/single-window/SingleWindowReferenceCatalogPage.tsx": ["useReferenceCatalogExcelWorkspace"],
    "features/invoices/InvoiceListPage.tsx": ["useInvoiceListSingleWindowOperations"],
    "features/reports/ReportTemplateWorkspacePage.tsx": ["useReportTemplatePackageWorkspace"],
    "features/invoices/InvoiceItemsEditor.tsx": ["useInvoiceItemsEditorInteraction", "InvoiceItemsEditorDialogs", "InvoiceItemsEditorProps"],
    "features/settings/SettingsPage.tsx": ["useSettingsMaintenanceActions", "useSettingsDraftSync"],
  };
  for (const requiredCoordinator of coordinatorContracts[sourceRelativePath] ?? []) {
    if (!sourceText.includes(requiredCoordinator)) {
      failures.push(`${sourceRelativePath}: 大型页面协调职责不得回流，缺少 ${requiredCoordinator}`);
    }
  }

  if (sourceRelativePath === "features/settings/useSettingsDraftSync.ts") {
    for (const settingsDraftContract of ["hasUnsavedChanges", "if (!response || hasUnsavedChanges)", "setSettings(response.settings as unknown as SettingsRecord)"]) {
      if (!sourceText.includes(settingsDraftContract)) {
        failures.push(`${sourceRelativePath}: 设置草稿同步缺少未保存保护契约：${settingsDraftContract}`);
      }
    }
  }

  if (sourceRelativePath === "ui/ResponsiveTable.tsx") {
    for (const responsiveTableContract of ["isScrollableRegion", 'role={isScrollableRegion ? "region" : undefined}', "tabIndex={isScrollableRegion ? 0 : undefined}"]) {
      if (!sourceText.includes(responsiveTableContract)) {
        failures.push(`${sourceRelativePath}: 响应式表格缺少滚动区域可访问性契约：${responsiveTableContract}`);
      }
    }
  }

  if (sourceRelativePath === "ui/tableRowInteractions.ts") {
    for (const tableRowInteractionContract of ["event.target === event.currentTarget", "must not also activate the row's default action"]) {
      if (!sourceText.includes(tableRowInteractionContract)) {
        failures.push(`${sourceRelativePath}: 表格行键盘事件缺少行内控件隔离契约：${tableRowInteractionContract}`);
      }
    }
  }

  if (sourceRelativePath === "features/invoices/InvoiceTable.tsx" || sourceRelativePath === "features/single-window/SingleWindowOperationCenterList.tsx") {
    if (!sourceText.includes("isDirectTableRowKeyboardEvent")) {
      failures.push(`${sourceRelativePath}: 含行内操作按钮的可点击行必须隔离按钮键盘事件`);
    }
  }

  if (sourceRelativePath === "features/invoices/InvoiceLetterOfCreditPanel.tsx") {
    for (const creditPanelContract of ["isExpanded", "aria-expanded={isExpanded}", "低频信息，按需展开"]) {
      if (!sourceText.includes(creditPanelContract)) {
        failures.push(`${sourceRelativePath}: 低频信用证面板缺少默认折叠或可解释展开契约：${creditPanelContract}`);
      }
    }
  }

  if (sourceRelativePath === "ui/FrontendErrorBoundary.tsx") {
    for (const requiredRecoveryText of ["重试当前界面", "重新加载程序界面", "incidentId", "reportFrontendError"]) {
      if (!sourceText.includes(requiredRecoveryText)) {
        failures.push(`${sourceRelativePath}: 全局异常页缺少恢复操作或可追踪异常编号：${requiredRecoveryText}`);
      }
    }
  }

  if (sourceRelativePath === "App.tsx") {
    for (const requiredWorkspaceNoticeContract of ["setWorkspaceNotice", "notice={workspaceNotice}", "onDismissNotice"]) {
      if (!sourceText.includes(requiredWorkspaceNoticeContract)) {
        failures.push(`${sourceRelativePath}: 权限或授权跳转必须在已登录工作区显示可关闭通知：${requiredWorkspaceNoticeContract}`);
      }
    }
  }

  if (sourceRelativePath === "main.tsx") {
    for (const cssLayer of ["./styles/foundation.css", "./styles/workspaces.css", "./styles/responsive.css"]) {
      if (!sourceText.includes(cssLayer)) failures.push(`${sourceRelativePath}: CSS 入口缺少分层加载：${cssLayer}`);
    }
    if (sourceText.includes('import "./responsiveOverrides.css"')) failures.push(`${sourceRelativePath}: 响应式覆盖不应绕过最终 responsive 层`);
  }

  if (sourceRelativePath === "app/WorkspaceShell.tsx") {
    for (const routeAccessibilityContract of [
      'className="skip-link"',
      'href="#workspace-main-content"',
      'id="workspace-main-content"',
      "workspaceTitleRef.current?.focus",
      "document.title = `${context.title}",
    ]) {
      if (!sourceText.includes(routeAccessibilityContract)) {
        failures.push(`${sourceRelativePath}: 路由切换缺少跳转主内容、标题更新或焦点落点：${routeAccessibilityContract}`);
      }
    }
    for (const requiredWorkspaceNoticeView of ["workspace-global-notice", "InlineNotice", "关闭提示"]) {
      if (!sourceText.includes(requiredWorkspaceNoticeView)) {
        failures.push(`${sourceRelativePath}: 工作区通知缺少统一反馈、可见容器或关闭操作：${requiredWorkspaceNoticeView}`);
      }
    }
    for (const mobileNavigationContract of [
      'aria-controls="workspace-primary-navigation"',
      "workspace-nav-backdrop",
      'event.key === "Escape"',
      'event.key !== "Tab"',
      'documentElement.style.overflow = "hidden"',
    ]) {
      if (!sourceText.includes(mobileNavigationContract)) {
        failures.push(`${sourceRelativePath}: 手机导航缺少抽屉关闭、焦点循环或背景滚动保护：${mobileNavigationContract}`);
      }
    }
  }
}

const globalFoundationManifest = fs.readFileSync(path.join(root, "styles.css"), "utf8");
const globalWorkspaceManifest = fs.readFileSync(path.join(root, "styles", "workspaces.css"), "utf8");
for (const routeOnlyStyle of [
  "single-window-core.css",
  "single-window-runtime.css",
  "single-window-documents.css",
  "coo-review.css",
  "container-packing.css",
]) {
  if (globalFoundationManifest.includes(routeOnlyStyle)) {
    failures.push(`styles.css: 重型路由样式不得重新进入首屏清单：${routeOnlyStyle}`);
  }
}
if (globalWorkspaceManifest.includes("reportWorkspace.css")) {
  failures.push("styles/workspaces.css: 报表设计样式必须随 lazy route 加载");
}
for (const [sourceRelativePath, routeStyle] of [
  ["features/reports/ReportTemplateWorkspacePage.tsx", "../../styles/routes/reports.css"],
  ["features/single-window/SingleWindowPages.tsx", "../../styles/routes/single-window.css"],
  ["features/single-window/SingleWindowReferenceCatalogPage.tsx", "../../styles/routes/single-window.css"],
  ["features/single-window/CustomsCooPage.tsx", "../../styles/routes/single-window.css"],
  ["features/single-window/AgentConsignmentPage.tsx", "../../styles/routes/single-window.css"],
  ["features/tools/container-packing/ContainerPackingPage.tsx", "../../../styles/routes/container-packing.css"],
]) {
  const sourceText = fs.readFileSync(path.join(root, sourceRelativePath), "utf8");
  if (!sourceText.includes(routeStyle)) {
    failures.push(`${sourceRelativePath}: 缺少按路由加载的样式入口 ${routeStyle}`);
  }
}

const responsiveCss = readCssImportGraph(path.join(root, "responsiveOverrides.css"));
if (!/@media\s*\(min-width:\s*861px\)\s*and\s*\(max-width:\s*1180px\)[\s\S]*?\.field-grid\s*\{[\s\S]*?grid-template-columns:\s*repeat\(2,\s*minmax\(0,\s*1fr\)\)/u.test(responsiveCss)) {
  failures.push("responsiveOverrides.css: 公共业务表单必须在 861—1180px 中等桌面宽度切换为双列");
}
if (!/@media\s*\(min-width:\s*861px\)\s*and\s*\(max-width:\s*1180px\)[\s\S]*?\.invoice-party-group-exporter\s+\.field-grid\s*\{[\s\S]*?grid-template-columns:\s*repeat\(4,\s*minmax\(0,\s*1fr\)\)/u.test(responsiveCss)) {
  failures.push("responsiveOverrides.css: 出口商资料在 861—1180px 仍应使用可容纳的专用四列布局，避免通用双列规则拉长页面");
}

for (const scriptName of ["test_frontend_visual_baselines.mjs", "test_frontend_scale_contracts.mjs"]) {
  const scriptSource = fs.readFileSync(path.resolve(import.meta.dirname, scriptName), "utf8");
  if (!scriptSource.includes("spawnProcessTree(process.execPath") || !scriptSource.includes("viteCli,")) {
    failures.push(`${scriptName}: Windows Vite 必须由当前 Node 直接运行 vite.js，以便可靠回收进程树`);
  }
  if (/spawnProcessTree\([^)]*(?:npm|cmd(?:\.exe)?)/su.test(scriptSource)) {
    failures.push(`${scriptName}: 不得通过 cmd/npm 外层进程启动 Vite`);
  }
}
for (const motionContract of [
  "@keyframes login-ambient-sweep",
  "@keyframes login-grid-drift",
  "@keyframes login-brand-enter",
  "@keyframes login-card-enter",
  ".login-submit-button:hover:not(:disabled) svg",
]) {
  if (!responsiveCss.includes(motionContract)) {
    failures.push(`responsiveOverrides.css: 登录页轻量 CSS 动效契约缺少 ${motionContract}`);
  }
}

const themeCss = readCssImportGraph(path.join(root, "theme.css"));
const queryPageSource = fs.readFileSync(path.join(root, "features", "query", "QueryPage.tsx"), "utf8");
for (const queryLayoutContract of [
  "query-filter-stack",
  "query-common-filter-grid",
  "query-advanced-filters",
  "query-advanced-filter-summary",
  "query-advanced-filter-grid",
  "query-date-range",
  "query-filter-field",
]) {
  if (!queryPageSource.includes(queryLayoutContract)) {
    failures.push(`features/query/QueryPage.tsx: 单据查询筛选区缺少响应式布局契约 ${queryLayoutContract}`);
  }
}
for (const obsoleteQueryLayout of ['className="filter-bar query-filter-bar"', "inline-filter query-party-filter"]) {
  if (queryPageSource.includes(obsoleteQueryLayout)) {
    failures.push(`features/query/QueryPage.tsx: 单据查询筛选区不得回退到会压缩标签的旧行内布局 ${obsoleteQueryLayout}`);
  }
}
const remoteSelectCss = fs.readFileSync(path.join(root, "styles", "remote-select.css"), "utf8");
if (remoteSelectCss.includes(".query-party-filter")) {
  failures.push("styles/remote-select.css: 公共远程选择器样式不得重新硬编码单据查询页面宽度");
}
const auditQueryCss = fs.readFileSync(path.join(root, "styles", "audit-query.css"), "utf8");
for (const queryBreakpoint of [
  "@media (min-width: 1720px)",
  "@media (max-width: 1180px)",
  "@media (max-width: 860px)",
  "@media (max-width: 620px)",
]) {
  if (!auditQueryCss.includes(queryBreakpoint)) {
    failures.push(`styles/audit-query.css: 单据查询筛选区缺少响应式断点 ${queryBreakpoint}`);
  }
}
for (const queryGridContract of [
  ".query-filter-grid {",
  ".query-date-range {",
  ".query-filter-field {",
  "grid-template-columns: repeat(4, minmax(0, 1fr))",
]) {
  if (!auditQueryCss.includes(queryGridContract)) {
    failures.push(`styles/audit-query.css: 单据查询筛选区缺少网格布局契约 ${queryGridContract}`);
  }
}
const queryTabletBreakpointStart = auditQueryCss.indexOf("@media (max-width: 1180px)");
const queryNarrowBreakpointStart = auditQueryCss.indexOf("@media (max-width: 860px)");
const queryTabletCss = queryTabletBreakpointStart >= 0 && queryNarrowBreakpointStart > queryTabletBreakpointStart
  ? auditQueryCss.slice(queryTabletBreakpointStart, queryNarrowBreakpointStart)
  : "";
for (const tabletFilterContract of [
  ".query-advanced-filters",
  ".query-advanced-filter-summary",
  "display: flex",
]) {
  if (!queryTabletCss.includes(tabletFilterContract)) {
    failures.push(`styles/audit-query.css: 681—1180px 平板筛选入口缺少 ${tabletFilterContract}`);
  }
}
for (const sharedWorkspacePrimitive of [
  ".visually-hidden {",
  ".danger-icon {",
  ".section-header {",
  ".section-header > div:first-child {",
  ".section-header > div:first-child span {",
  ".field-grid {",
  "grid-template-columns: repeat(4, minmax(160px, 1fr))",
  ".field-grid-span-all {",
  ".field-grid-span-2 {",
  ".textarea-field {",
  ".filter-bar {",
  ".inline-filter {",
  ".inline-check {",
  ".row-actions-cell {",
  ".detail-grid {",
  ".detail-item-wide {",
  ".detail-value-row {",
  ".detail-item-actions {",
  ".workspace-modal-backdrop {",
  ".workspace-modal-dialog {",
  ".workspace-modal-header {",
  ".workspace-modal-footer {",
]) {
  if (!themeCss.includes(sharedWorkspacePrimitive)) {
    failures.push(`theme.css: 公共工作区原语不能依赖报表或单一窗口 lazy route 样式：${sharedWorkspacePrimitive}`);
  }
}
const settingsPageSource = fs.readFileSync(path.join(root, "features", "settings", "SettingsPage.tsx"), "utf8");
if (!settingsPageSource.includes('import "../../styles/runtime-diagnostics.css"')) {
  failures.push("features/settings/SettingsPage.tsx: 运行诊断样式必须随设置路由加载，不能依赖 Single Window 或进入首屏 CSS");
}
const runtimeDiagnosticsCss = fs.readFileSync(path.join(root, "styles", "runtime-diagnostics.css"), "utf8");
for (const settingsRuntimeStyle of [
  ".runtime-detail-grid {",
  ".runtime-diagnostics-section {",
  ".runtime-dependency-grid {",
  ".runtime-path-row {",
  ".runtime-template-storage-result {",
]) {
  if (!runtimeDiagnosticsCss.includes(settingsRuntimeStyle)) {
    failures.push(`runtime-diagnostics.css: 设置页运行诊断缺少独立样式 ${settingsRuntimeStyle}`);
  }
}
const globalFoundationCss = readCssImportGraph(path.join(root, "styles.css"));
if (!globalFoundationCss.includes(".job-title-cell {")) {
  failures.push("styles.css: 任务中心标题布局不能依赖装柜 lazy route");
}
const globalBusinessCss = readCssImportGraph(path.join(root, "businessFeatures.css"));
for (const globalBusinessStyle of [
  ".invoice-single-window-action-buttons {",
  ".review-severity {",
  ".review-severity-error {",
  ".review-severity-warning {",
  ".review-severity-info {",
]) {
  if (!globalBusinessCss.includes(globalBusinessStyle)) {
    failures.push(`businessFeatures.css: 发票与审核公共样式不能依赖单一窗口 lazy route：${globalBusinessStyle}`);
  }
}
const invoicePartiesCss = readCssImportGraph(path.join(root, "styles", "business", "invoice-parties.css"));
if (!/@media\s*\(max-width:\s*860px\)[\s\S]*?\.invoice-party-group:not\(\.invoice-party-group-exporter\)\s+\.field-grid\s*,[\s\S]*?grid-template-columns:\s*1fr/u.test(invoicePartiesCss)) {
  failures.push("invoice-parties.css: 发票客户与通知人专用双列布局必须在窄窗口退化为单列");
}
for (const [routeCssPath, forbiddenSharedDefinitions] of [
  ["styles/single-window-core.css", [".visually-hidden {", ".danger-icon {", ".filter-bar {", ".inline-filter {", ".inline-check {"]],
  ["styles/single-window-runtime.css", [".row-actions-cell {", ".detail-grid {", ".detail-item {", ".runtime-diagnostics-section {"]],
  ["styles/single-window-documents.css", [".workspace-modal-backdrop {", ".workspace-modal-dialog {", ".workspace-modal-header {", ".workspace-modal-footer {"]],
  ["styles/coo-review.css", [".review-severity {"]],
  ["styles/container-packing.css", [".job-title-cell {"]],
  ["styles/report/designer-canvas.css", [".section-header {", ".section-header > div:first-child {", ".section-header > div:first-child span {", ".field-grid {", ".field-grid-span-all {", ".field-grid-span-2 {", ".textarea-field {"]],
]) {
  const routeCss = fs.readFileSync(path.join(root, routeCssPath), "utf8");
  for (const forbiddenSharedDefinition of forbiddenSharedDefinitions) {
    if (routeCss.includes(forbiddenSharedDefinition)) {
      failures.push(`${routeCssPath}: 公共样式不得重新由 lazy route 独占 ${forbiddenSharedDefinition}`);
    }
  }
}
for (const sharedDangerActionContract of [
  ".command-button.danger-command",
  ".command-button.danger-command:hover:not(:disabled)",
  ".command-button.danger-command:disabled",
]) {
  if (!themeCss.includes(sharedDangerActionContract)) {
    failures.push(`theme.css: 维护与恢复页面的危险操作样式不能依赖报表 lazy route：${sharedDangerActionContract}`);
  }
}
for (const saveActionContract of [
  ".invoice-editor-sticky-actions > div > span",
  ".invoice-editor-sticky-actions .command-button:disabled",
]) {
  if (!themeCss.includes(saveActionContract)) {
    failures.push(`theme.css: 发票顶部保存操作缺少清晰的状态文字或禁用按钮对比度契约 ${saveActionContract}`);
  }
}
if (themeCss.includes(".invoice-editor-sticky-actions span {")) {
  failures.push("theme.css: 发票保存区状态文字选择器不得覆盖按钮组件内部文字");
}
const hsKnowledgeCss = fs.readFileSync(path.join(root, "features", "master-data", "hsKnowledge.css"), "utf8");
if (!hsKnowledgeCss.includes("background: var(--input-background, var(--edm-white))")) {
  failures.push("hsKnowledge.css: 智能 HS 查询输入框必须有稳定的非透明背景回退");
}
for (const reducedMotionContract of [
  "@media (prefers-reduced-motion: reduce)",
  "animation-duration: 0.01ms !important",
  "animation-iteration-count: 1 !important",
]) {
  if (!themeCss.includes(reducedMotionContract)) {
    failures.push(`theme.css: 减少动态模式缺少 ${reducedMotionContract}`);
  }
}

if (failures.length) {
  process.stderr.write(`${failures.join("\n")}\n`);
  process.exit(1);
}

process.stdout.write("frontend accessibility contracts passed\n");

function visit(node, source) {
  if (ts.isJsxElement(node)) {
    checkElement(node.openingElement, node.children, source);
  } else if (ts.isJsxSelfClosingElement(node)) {
    checkElement(node, [], source);
  }
  ts.forEachChild(node, (child) => visit(child, source));
}

function checkElement(opening, children, source) {
  const tag = opening.tagName.getText(source);
  const attributes = new Map();
  for (const property of opening.attributes.properties) {
    if (ts.isJsxAttribute(property)) attributes.set(property.name.getText(source), property.initializer?.getText(source) ?? "");
  }

  const className = attributes.get("className") ?? "";
  if (className.includes("clickable-row") && (!attributes.has("tabIndex") || !attributes.has("onKeyDown"))) {
    fail(opening, source, "可点击表格行必须提供键盘焦点和 Enter/空格操作，避免只能用鼠标打开");
  }
  if (tag === "button" && !attributes.has("type")) {
    fail(opening, source, "原生按钮必须显式声明 type，防止表单内误提交");
  }

  if (tag === "button" && className.includes("icon-button")) {
    const hasAccessibleName = attributes.has("aria-label") || hasVisibleText(children, source);
    if (!hasAccessibleName) fail(opening, source, "纯图标按钮必须提供 aria-label");
  }

  if (tag === "img" && !attributes.has("alt")) {
    fail(opening, source, "图片必须提供 alt");
  }

  if (attributes.get("role")?.includes("dialog") && !attributes.has("aria-label") && !attributes.has("aria-labelledby")) {
    fail(opening, source, "对话框必须提供 aria-label 或 aria-labelledby");
  }
}

function hasVisibleText(children, source) {
  return children.some((child) => {
    if (ts.isJsxText(child)) return child.getText(source).trim().length > 0;
    if (ts.isJsxElement(child)) return child.children.some((nested) => ts.isJsxText(nested) && nested.getText(source).trim().length > 0);
    return false;
  });
}

function fail(node, source, message) {
  const position = source.getLineAndCharacterOfPosition(node.getStart(source));
  failures.push(`${path.relative(path.resolve(import.meta.dirname, ".."), source.fileName)}:${position.line + 1}: ${message}`);
}

function readCssImportGraph(entryPath, visited = new Set()) {
  const resolvedPath = path.resolve(entryPath);
  if (visited.has(resolvedPath)) return "";
  visited.add(resolvedPath);

  const css = fs.readFileSync(resolvedPath, "utf8");
  const importedCss = [...css.matchAll(/@import\s+(?:url\(\s*)?["']([^"']+)["']\s*\)?[^;]*;/gu)]
    .map((match) => match[1])
    .filter((specifier) => specifier.startsWith("."))
    .map((specifier) => readCssImportGraph(path.resolve(path.dirname(resolvedPath), specifier), visited))
    .join("\n");

  return `${importedCss}\n${css}`;
}

function* walk(directory) {
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory()) yield* walk(fullPath);
    else yield fullPath;
  }
}
