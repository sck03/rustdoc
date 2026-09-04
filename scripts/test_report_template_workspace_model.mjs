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

fs.mkdirSync(workspaceRoot, { recursive: true });
fs.writeFileSync(
  entryPath,
  [
    `export { readUserTemplateIdFromKey, readUserTemplateIdFromSearch, resolveDefaultTemplatePath, resolvePreviewSourceId } from ${JSON.stringify(modelImportSpecifier)};`,
    `export { resolveBatchExportItems } from ${JSON.stringify(exportDefaultsModelImportSpecifier)};`,
  ].join("\n"),
  "utf8",
);

const esbuild = require(path.join(repoRoot, "apps", "export-doc-web", "node_modules", "esbuild"));
await esbuild.build({ entryPoints: [entryPath], outfile: bundlePath, bundle: true, platform: "node", format: "esm" });
const { readUserTemplateIdFromKey, readUserTemplateIdFromSearch, resolveBatchExportItems, resolveDefaultTemplatePath, resolvePreviewSourceId } = await import(`${pathToFileURL(bundlePath).href}?v=${Date.now()}`);

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
assertMatch(workspaceStateSource, /hasUnappliedDesignerChanges\s*=\s*[\s\S]*?designerDraftContent\s*!==\s*content/, "新版画布草稿必须独立识别为未应用修改");
assertMatch(workspaceStateSource, /hasUnsavedChanges\s*=\s*hasChanges\s*\|\|\s*hasUnappliedDesignerChanges/, "保存和离开保护必须同时覆盖源码与画布草稿");
assertMatch(workspaceStateSource, /canSave\s*=\s*[\s\S]*?hasUnsavedChanges/, "画布草稿存在时顶部保存必须可用");
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
