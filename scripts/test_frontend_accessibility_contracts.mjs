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

  if (sourceRelativePath === "features/invoices/useInvoiceItemsWorkspace.ts") {
    for (const workspaceContract of ["maxInvoiceItemHistoryDepth = 50", "recalculateInvoiceItem", "masterDataRoot(\"products\")"]) {
      if (!sourceText.includes(workspaceContract)) {
        failures.push(`${sourceRelativePath}: 发票明细工作区缺少历史、重算或商品库闭环：${workspaceContract}`);
      }
    }
  }

  if (sourceRelativePath === "features/jobs/JobCenterPage.tsx") {
    for (const taskCenterContract of ["handleCancelJob", "handleDeleteJob", "handleClearFinishedJobs", "messageTone", "requestConfirmation"]) {
      if (!sourceText.includes(taskCenterContract)) {
        failures.push(`${sourceRelativePath}: 任务中心缺少危险操作确认或稳定反馈语义：${taskCenterContract}`);
      }
    }
    if (/\<tr[\s\S]{0,240}tabIndex=\{0\}/.test(sourceText)) {
      failures.push(`${sourceRelativePath}: 任务表格行没有直接动作，不应伪装成可键盘操作控件`);
    }
  }

  const coordinatorContracts = {
    "features/single-window/CustomsCooPage.tsx": ["useSingleWindowLockedFields", "useCustomsCooProducerProfiles", "useCustomsCooAuthoritySelection"],
    "features/single-window/AgentConsignmentPage.tsx": ["useSingleWindowLockedFields"],
    "features/single-window/SingleWindowReferenceCatalogPage.tsx": ["useReferenceCatalogExcelWorkspace"],
    "features/invoices/InvoiceListPage.tsx": ["useInvoiceListSingleWindowOperations"],
    "features/reports/ReportTemplateDesignerPage.tsx": ["useReportTemplatePackageWorkspace"],
    "features/invoices/InvoiceItemsEditor.tsx": ["useInvoiceItemsEditorInteraction", "InvoiceItemsEditorDialogs", "InvoiceItemsEditorProps"],
    "features/settings/SettingsPage.tsx": ["useSettingsMaintenanceActions"],
  };
  for (const requiredCoordinator of coordinatorContracts[sourceRelativePath] ?? []) {
    if (!sourceText.includes(requiredCoordinator)) {
      failures.push(`${sourceRelativePath}: 大型页面协调职责不得回流，缺少 ${requiredCoordinator}`);
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

  if (sourceRelativePath === "app/WorkspaceShell.tsx") {
    for (const requiredWorkspaceNoticeView of ["workspace-global-notice", "InlineNotice", "关闭提示"]) {
      if (!sourceText.includes(requiredWorkspaceNoticeView)) {
        failures.push(`${sourceRelativePath}: 工作区通知缺少统一反馈、可见容器或关闭操作：${requiredWorkspaceNoticeView}`);
      }
    }
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

function* walk(directory) {
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory()) yield* walk(fullPath);
    else yield fullPath;
  }
}
