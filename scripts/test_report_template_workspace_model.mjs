import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const require = createRequire(import.meta.url);
const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(scriptDir, "..");
const workspaceRoot = path.join(repoRoot, ".codex-runtime", "report-template-workspace-model-test");
const entryPath = path.join(workspaceRoot, "entry.ts");
const bundlePath = path.join(workspaceRoot, "bundle.mjs");
const modelPath = path.join(
  repoRoot,
  "apps",
  "export-doc-web",
  "src",
  "features",
  "reports",
  "reportTemplateDesignerModel.ts",
);
const exportDefaultsModelPath = path.join(
  repoRoot,
  "apps",
  "export-doc-web",
  "src",
  "features",
  "reports",
  "reportExportDefaultsModel.ts",
);
const workspaceStateModelPath = path.join(
  repoRoot,
  "apps",
  "export-doc-web",
  "src",
  "features",
  "reports",
  "reportTemplateWorkspaceState.ts",
);
const stylesRoot = path.join(repoRoot, "apps", "export-doc-web", "src");
const reportWorkspaceCss = readCssGraph(path.join(stylesRoot, "reportWorkspace.css"));
const responsiveOverridesCss = readCssGraph(path.join(stylesRoot, "responsiveOverrides.css"));
const workspaceStateSource = fs.readFileSync(path.join(repoRoot, "apps", "export-doc-web", "src", "features", "reports", "reportTemplateWorkspaceState.ts"), "utf8");
const userPanelSource = fs.readFileSync(path.join(repoRoot, "apps", "export-doc-web", "src", "features", "reports", "ReportTemplateUserPanel.tsx"), "utf8");
const exportDefaultsPanelSource = fs.readFileSync(path.join(repoRoot, "apps", "export-doc-web", "src", "features", "reports", "ReportExportDefaultsPanel.tsx"), "utf8");
const invoicePreviewModelSource = fs.readFileSync(path.join(repoRoot, "apps", "export-doc-web", "src", "features", "invoices", "invoiceReportPreviewModel.ts"), "utf8");
const invoicePackageWorkspaceSource = fs.readFileSync(path.join(repoRoot, "apps", "export-doc-web", "src", "features", "invoices", "useInvoiceDocumentPackageWorkspace.ts"), "utf8");
const modelImportSpecifier = `./${path.relative(workspaceRoot, modelPath).replaceAll("\\", "/")}`;
const exportDefaultsModelImportSpecifier = `./${path.relative(workspaceRoot, exportDefaultsModelPath).replaceAll("\\", "/")}`;
const workspaceStateModelImportSpecifier = `./${path.relative(workspaceRoot, workspaceStateModelPath).replaceAll("\\", "/")}`;

fs.mkdirSync(workspaceRoot, { recursive: true });
fs.writeFileSync(
  entryPath,
  [
    `export { buildUserTemplateClonePayload, buildUserTemplateCreatePayload, readUserTemplateIdFromKey, readUserTemplateIdFromSearch, resolveDefaultTemplatePath, resolvePreviewSourceId, resolveReportTypeOptions } from ${JSON.stringify(modelImportSpecifier)};`,
    `export { resolveBatchExportItems } from ${JSON.stringify(exportDefaultsModelImportSpecifier)};`,
    `export { deriveReportTemplateWorkspaceState } from ${JSON.stringify(workspaceStateModelImportSpecifier)};`,
  ].join("\n"),
  "utf8",
);

const esbuild = require(path.join(repoRoot, "apps", "export-doc-web", "node_modules", "esbuild"));
await esbuild.build({ entryPoints: [entryPath], outfile: bundlePath, bundle: true, platform: "node", format: "esm" });
const { buildUserTemplateClonePayload, buildUserTemplateCreatePayload, deriveReportTemplateWorkspaceState, readUserTemplateIdFromKey, readUserTemplateIdFromSearch, resolveBatchExportItems, resolveDefaultTemplatePath, resolvePreviewSourceId, resolveReportTypeOptions } = await import(`${pathToFileURL(bundlePath).href}?v=${Date.now()}`);

assertEqual(resolveReportTypeOptions(true, false, true)[0]?.value, "PaymentVoucher", "财务账号应默认进入获准的付款报表域");
assertEqual(resolveReportTypeOptions(true, false, true).length, 1, "财务账号不得看到出口报表域");
assertEqual(resolveReportTypeOptions(false, true, true).length, 0, "没有模板权限时业务权限不得单独开放模板域");
assertEqual(resolveReportTypeOptions(true, false, false).length, 0, "通用模板权限不得越过业务数据域");

const templates = [
  { templatePath: "E:/app/Templates/Export/custom.html", displayName: "自定义发票", reportType: "ExportDocument", withSealDefault: false },
  { templatePath: "E:/app/Templates/Export/invoice_template.html", displayName: "发票", reportType: "ExportDocument", withSealDefault: true },
];

assertEqual(
  resolveDefaultTemplatePath({
    templates,
    reportType: "ExportDocument",
    requestedTemplateFileName: "custom.html",
    currentTemplatePath: templates[1].templatePath,
    userTemplateSelected: false,
  }),
  templates[0].templatePath,
  "路由指定模板应优先",
);
assertEqual(
  resolveDefaultTemplatePath({
    templates,
    reportType: "ExportDocument",
    requestedTemplateFileName: "",
    currentTemplatePath: "E:\\app\\Templates\\Export\\custom.html",
    userTemplateSelected: false,
  }),
  "E:\\app\\Templates\\Export\\custom.html",
  "当前有效选择应跨分隔符保留原值",
);
assertEqual(
  resolveDefaultTemplatePath({
    templates,
    reportType: "ExportDocument",
    requestedTemplateFileName: "",
    currentTemplatePath: "e:\\app\\templates\\export\\CUSTOM.html",
    userTemplateSelected: false,
  }),
  templates[1].templatePath,
  "文件名和目录大小写不一致时不应在大小写敏感平台误匹配",
);
assertEqual(
  resolveDefaultTemplatePath({
    templates,
    reportType: "ExportDocument",
    requestedTemplateFileName: "",
    currentTemplatePath: "missing.html",
    userTemplateSelected: false,
  }),
  templates[1].templatePath,
  "无效选择应回到类型默认模板",
);
assertEqual(
  resolveDefaultTemplatePath({
    templates,
    reportType: "ExportDocument",
    requestedTemplateFileName: "",
    configuredTemplatePath: templates[0].templatePath,
    currentTemplatePath: "",
    userTemplateSelected: false,
  }),
  templates[0].templatePath,
  "未指定深链时应采用已配置的默认模板",
);
assertEqual(
  resolveDefaultTemplatePath({
    templates: [],
    reportType: "ExportDocument",
    requestedTemplateFileName: "",
    currentTemplatePath: "user-template:8",
    userTemplateSelected: true,
  }),
  "user-template:8",
  "用户模板选择不应被默认模板查询覆盖",
);
assertEqual(resolvePreviewSourceId(9, [1, 2]), 9, "已有预览源应保持不变");
assertEqual(resolvePreviewSourceId(0, [0, -1, 6, 7]), 6, "应选择第一个有效预览源");
assertEqual(resolvePreviewSourceId(0, []), 0, "无预览源时应保持未选择");
assertEqual(readUserTemplateIdFromSearch("?userTemplateId=17"), 17, "用户模板深链应解析有效 ID");
assertEqual(readUserTemplateIdFromSearch("?userTemplateId=invalid"), 0, "无效用户模板深链应回退");
assertEqual(readUserTemplateIdFromKey("user-template:17"), 17, "统一用户模板引用应解析有效 ID");
assertEqual(readUserTemplateIdFromKey("user:Export/template.html"), 0, "文件模板引用不应被解析为数据库模板");
const blankCreate = buildUserTemplateCreatePayload({
  reportType: "ExportDocument",
  name: "  新建空白模板  ",
});
assertEqual(blankCreate.contentHtml, "", "新建空白模板不得夹带当前编辑内容");
assertEqual("sourceTemplatePath" in blankCreate, false, "新建命令不得混入复制来源");
assertEqual(blankCreate.name, "新建空白模板", "新模板名称应在提交前规范化");
const builtInClone = buildUserTemplateClonePayload({
  reportType: "ExportDocument",
  selectedTemplatePath: "  builtin:Export/invoice_template.html  ",
  selectedUserTemplateId: 0,
  name: "  内置模板副本  ",
});
assertEqual(builtInClone.sourceTemplatePath, "builtin:Export/invoice_template.html", "内置模板复制只提交受管引用");
assertEqual("contentHtml" in builtInClone, false, "复制命令不得上传浏览器模板正文");
const userClone = buildUserTemplateClonePayload({
  reportType: "ExportDocument",
  selectedTemplatePath: "ignored-stale-reference",
  selectedUserTemplateId: 17,
  name: "副本",
});
assertEqual(userClone.sourceTemplatePath, "user-template:17", "用户模板复制必须提交稳定 ID 引用");
assertEqual("contentHtml" in userClone, false, "用户模板复制不得信任当前浏览器正文");
assertEqual("shareScope" in userClone, false, "复制模板必须先创建为私有草稿，不能混入共享状态");
const implicitItems = resolveBatchExportItems([], templates);
assertEqual(implicitItems.length, templates.length, "空配置应显示全部可用发票模板");
assertEqual(implicitItems.every((item) => item.isEnabled), true, "空配置的全部可用发票模板应默认启用");
assertEqual(implicitItems[0].name, templates[0].displayName, "隐式单据项应使用模板显示名称");
assertEqual(implicitItems[0].showSeal, false, "隐式单据项应沿用模板盖章默认值");
const configuredItems = [{ name: "商业发票", templatePath: templates[1].templatePath, isEnabled: false, showSeal: false, reportType: "ExportDocument" }];
const configuredSnapshot = JSON.stringify(configuredItems);
const effectiveConfiguredItems = resolveBatchExportItems(configuredItems, templates);
assertEqual(JSON.stringify(effectiveConfiguredItems), configuredSnapshot, "显式配置应保留名称、顺序、启用和盖章状态");
assertEqual(JSON.stringify(configuredItems), configuredSnapshot, "解析默认单据项不得修改输入配置");

const baseWorkspaceStateInput = {
  reportType: "ExportDocument",
  designerDraftContent: "",
  content: "<html>{{ Invoice.InvoiceNo }}</html>",
  loadedContent: "<html>{{ Invoice.InvoiceNo }}</html>",
  contentTemplatePath: "builtin:Export/invoice_template.html",
  selectedTemplatePath: "builtin:Export/invoice_template.html",
  selectedContentTemplatePath: "builtin:Export/invoice_template.html",
  currentUserTemplate: null,
  templatePreviewMode: "savedSource",
  templatePreviewSampleProfile: "apiSample",
  previewHtml: "",
  previewInvoices: [],
  previewPayments: [],
  previewInvoiceId: 7,
  previewPaymentId: 0,
  busyFlags: [],
  canManageTemplates: false,
  canDesignTemplates: false,
  currentTemplateDisplayName: "Template",
  persistedDisplayName: "Template",
  defaultTemplatePath: "",
  canUseAdvancedTools: true,
  canCloneTemplates: false,
  canArchiveTemplates: false,
  canImportTemplates: false,
  canExportTemplates: false,
  canPreviewSavedSource: false,
  newTemplateFileName: "",
  newUserTemplateName: "",
  renameTemplateFileName: "",
  desktopAvailable: false,
  packageExportPath: "",
  packageImportPath: "",
  fileExportPath: "",
  fileImportPath: "",
};
const exportOnlyWorkspaceState = deriveReportTemplateWorkspaceState({
  ...baseWorkspaceStateInput,
  canExportTemplates: true,
});
assertEqual(exportOnlyWorkspaceState.canDownloadPackage, true, "仅导出权限应允许下载模板包");
assertEqual(exportOnlyWorkspaceState.canDownloadTemplateFile, true, "仅导出权限应允许下载所选文件模板");
assertEqual(exportOnlyWorkspaceState.canUploadPackage, false, "仅导出权限不得串权到模板包导入");
assertEqual(exportOnlyWorkspaceState.canDeleteTemplate, false, "仅导出权限不得串权到模板归档");

const importOnlyWorkspaceState = deriveReportTemplateWorkspaceState({
  ...baseWorkspaceStateInput,
  canImportTemplates: true,
});
assertEqual(importOnlyWorkspaceState.canUploadPackage, true, "仅导入权限应允许上传模板包");
assertEqual(importOnlyWorkspaceState.canUploadTemplateFile, true, "仅导入权限应允许上传所选文件模板");
assertEqual(importOnlyWorkspaceState.canDownloadPackage, false, "仅导入权限不得串权到模板包导出");
assertEqual(importOnlyWorkspaceState.canDeleteTemplate, false, "仅导入权限不得串权到模板归档");

const archiveOnlyWorkspaceState = deriveReportTemplateWorkspaceState({
  ...baseWorkspaceStateInput,
  canArchiveTemplates: true,
});
assertEqual(archiveOnlyWorkspaceState.canDeleteTemplate, true, "仅归档权限应允许归档所选文件模板");
assertEqual(archiveOnlyWorkspaceState.canUploadPackage, false, "仅归档权限不得串权到模板包导入");
assertEqual(archiveOnlyWorkspaceState.canDownloadPackage, false, "仅归档权限不得串权到模板包导出");

const deniedSavedSourcePreviewState = deriveReportTemplateWorkspaceState(baseWorkspaceStateInput);
assertEqual(deniedSavedSourcePreviewState.canRenderTemplatePreview, false, "缺少对应单据预览权限时不得预览已保存业务数据");
const deniedApiSamplePreviewState = deriveReportTemplateWorkspaceState({
  ...baseWorkspaceStateInput,
  templatePreviewMode: "sample",
});
assertEqual(deniedApiSamplePreviewState.canRenderTemplatePreview, false, "缺少设计权限时不得调用后端样例渲染");
const localSamplePreviewState = deriveReportTemplateWorkspaceState({
  ...baseWorkspaceStateInput,
  templatePreviewMode: "sample",
  templatePreviewSampleProfile: "exportStandard",
});
assertEqual(localSamplePreviewState.canRenderTemplatePreview, true, "只读用户仍可使用不读取业务数据的本地 V3 样例");
assertMatch(workspaceStateSource, /hasUnappliedDesignerChanges\s*=\s*[\s\S]*?designerDraftContent\s*!==\s*content/, "新版画布草稿必须独立识别为未应用修改");
assertMatch(workspaceStateSource, /hasUnsavedChanges\s*=\s*hasChanges\s*\|\|\s*hasUnappliedDesignerChanges/, "保存和离开保护必须同时覆盖源码与画布草稿");
const dirtyDesignerInput = { ...baseWorkspaceStateInput, designerDraftContent: "<html>Changed</html>", canManageTemplates: true };
const dirtyDesignerState = deriveReportTemplateWorkspaceState(dirtyDesignerInput);
assertEqual(dirtyDesignerState.canSave, true, "获准编辑且存在画布草稿时顶部保存必须可用");
assertEqual(deriveReportTemplateWorkspaceState({ ...dirtyDesignerInput, busyFlags: [true] }).canSave, false, "请求进行中不得重复保存");
assertEqual(deriveReportTemplateWorkspaceState({ ...dirtyDesignerInput, canManageTemplates: false }).canSave, false, "撤销编辑权限后不得保存草稿");
if (/<details[^>]*template-user-panel[^>]*\bopen\b/u.test(userPanelSource)) {
  throw new Error("我的 / 共享模板默认应保持折叠");
}
assertMatch(exportDefaultsPanelSource, /resolveBatchExportItems\(settings\.batchExport\.items,\s*templates\)/, "管理页应显示与高级导出一致的有效单据项");
assertMatch(invoicePreviewModelSource, /resolveBatchExportItems\(readBatchExportItems\(settings\),\s*templates\)/, "发票高级导出应复用有效单据项模型");
if (/updateSettings\s*\(/u.test(invoicePackageWorkspaceSource)) {
  throw new Error("发票高级导出不得重新保存全局单据包设置");
}
assertMatch(exportDefaultsPanelSource, /<details[^>]*className="report-export-default-items"/, "发票单据项应使用可折叠区域");
if (/<details[^>]*className="report-export-default-items"[^>]*\bopen\b/u.test(exportDefaultsPanelSource)) {
  throw new Error("发票单据项默认应保持折叠");
}
assertMatch(exportDefaultsPanelSource, /enabledCount[\s\S]*?mergePdf[\s\S]*?zipAfterExport/, "折叠摘要应显示启用数量及 PDF/ZIP 默认值");

assertMatch(
  reportWorkspaceCss,
  /\.report-template-management-workspace\s*\{[\s\S]*?grid-template-columns:\s*repeat\(2,\s*minmax\(0,\s*1fr\)\)/,
  "模板管理工作区应使用清晰的双栏布局",
);
assertMatch(reportWorkspaceCss, /\.template-selection-panel\s*\{\s*grid-column:\s*1\s*\/\s*-1;/, "选择区应独占管理页首行");
assertMatch(
  reportWorkspaceCss,
  /\.template-package-panel\s*,\s*\.template-file-panel\s*\{\s*grid-column:\s*auto;/,
  "模板包和单个模板文件应在管理网格中并排显示",
);
assertMatch(
  reportWorkspaceCss,
  /\.report-export-default-item\s*\{[\s\S]*?grid-template-columns:\s*auto\s+minmax\(130px,\s*0\.8fr\)\s+minmax\(220px,\s*1\.5fr\)/,
  "发票单据项应使用稳定网格，避免模板选择挤出容器",
);
assertMatch(
  responsiveOverridesCss,
  /@media\s*\(min-width:\s*861px\)\s*and\s*\(max-width:\s*1180px\)[\s\S]*?\.template-selection-panel\s*\{\s*grid-column:\s*1\s*\/\s*-1/,
  "中等宽度应继续让选择区独占首行",
);
assertMatch(
  responsiveOverridesCss,
  /@container\s+report-workspace\s*\(max-width:\s*1160px\)[\s\S]*?\.template-selection-panel\s*\{[\s\S]*?grid-column:\s*1\s*\/\s*-1;[\s\S]*?grid-row:\s*auto;/,
  "工作区实际变窄时选择区必须独占首行并清除宽屏行定位",
);
assertMatch(
  responsiveOverridesCss,
  /@container\s+report-workspace\s*\(max-width:\s*1160px\)[\s\S]*?\.template-user-panel,\s*\.template-admin-panel\s*\{[\s\S]*?grid-column:\s*auto;[\s\S]*?grid-row:\s*auto;/,
  "工作区实际变窄时用户与管理面板必须清除宽屏固定列",
);
assertMatch(
  responsiveOverridesCss,
  /@container\s+report-workspace\s*\(max-width:\s*820px\)[\s\S]*?\.template-package-panel\s*,\s*\.template-file-panel\s*\{[\s\S]*?grid-column:\s*1;/,
  "工作区实际变窄时模板包和单个模板文件应堆叠",
);
assertMatch(
  responsiveOverridesCss,
  /@container\s+report-workspace\s*\(max-width:\s*820px\)[\s\S]*?\.report-export-default-item\s*\{[\s\S]*?grid-template-columns:\s*auto\s+minmax\(0,\s*1fr\)\s+repeat\(3,\s*30px\)/,
  "窄工作区的发票单据项应改为可换行布局",
);

console.log("report-template-workspace-model tests passed");

function assertEqual(actual, expected, message) {
  if (actual !== expected) {
    throw new Error(`${message}: expected ${JSON.stringify(expected)}, received ${JSON.stringify(actual)}`);
  }
}

function assertMatch(actual, expected, message) {
  if (!expected.test(actual)) {
    throw new Error(`${message}: pattern ${expected} not found`);
  }
}

function readCssGraph(entryPath, visited = new Set()) {
  const absolutePath = path.resolve(entryPath);
  if (visited.has(absolutePath)) {
    return "";
  }

  visited.add(absolutePath);
  const source = fs.readFileSync(absolutePath, "utf8");
  const imports = [];
  const importPattern = /@import\s+(?:url\()?\s*["']([^"']+)["']\s*\)?\s*;?/g;
  let match;
  while ((match = importPattern.exec(source)) !== null) {
    const importedPath = match[1];
    if (importedPath.startsWith(".") && importedPath.endsWith(".css")) {
      imports.push(readCssGraph(path.resolve(path.dirname(absolutePath), importedPath), visited));
    }
  }

  return `${source}\n${imports.join("\n")}`;
}
