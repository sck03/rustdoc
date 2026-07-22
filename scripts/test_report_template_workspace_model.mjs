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
const reportWorkspaceCss = fs.readFileSync(path.join(repoRoot, "apps", "export-doc-web", "src", "reportWorkspace.css"), "utf8");
const responsiveOverridesCss = fs.readFileSync(path.join(repoRoot, "apps", "export-doc-web", "src", "responsiveOverrides.css"), "utf8");
const themeCss = fs.readFileSync(path.join(repoRoot, "apps", "export-doc-web", "src", "theme.css"), "utf8");
const workspaceStateSource = fs.readFileSync(path.join(repoRoot, "apps", "export-doc-web", "src", "features", "reports", "reportTemplateWorkspaceState.ts"), "utf8");
const modelImportSpecifier = `./${path.relative(workspaceRoot, modelPath).replaceAll("\\", "/")}`;

fs.mkdirSync(workspaceRoot, { recursive: true });
fs.writeFileSync(
  entryPath,
  `export { resolveDefaultTemplatePath, resolvePreviewSourceId } from ${JSON.stringify(modelImportSpecifier)};`,
  "utf8",
);

const esbuild = require(path.join(repoRoot, "apps", "export-doc-web", "node_modules", "esbuild"));
await esbuild.build({ entryPoints: [entryPath], outfile: bundlePath, bundle: true, platform: "node", format: "esm" });
const { resolveDefaultTemplatePath, resolvePreviewSourceId } = await import(`${pathToFileURL(bundlePath).href}?v=${Date.now()}`);

const templates = [
  { templatePath: "E:/app/Templates/Export/custom.html" },
  { templatePath: "E:/app/Templates/Export/invoice_template.html" },
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
    currentTemplatePath: "e:\\app\\templates\\export\\CUSTOM.html",
    userTemplateSelected: false,
  }),
  "e:\\app\\templates\\export\\CUSTOM.html",
  "当前有效选择应跨平台保留原值",
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
assertMatch(workspaceStateSource, /hasUnappliedDesignerChanges\s*=\s*[\s\S]*?designerDraftContent\s*!==\s*content/, "新版画布草稿必须独立识别为未应用修改");
assertMatch(workspaceStateSource, /hasUnsavedChanges\s*=\s*hasChanges\s*\|\|\s*hasUnappliedDesignerChanges/, "保存和离开保护必须同时覆盖源码与画布草稿");
assertMatch(workspaceStateSource, /canSave\s*=\s*[\s\S]*?hasUnsavedChanges/, "画布草稿存在时顶部保存必须可用");

assertMatch(
  reportWorkspaceCss,
  /\.report-template-sidebar\s*\{[\s\S]*?grid-template-columns:\s*minmax\(0,\s*1\.8fr\)\s*repeat\(3,\s*minmax\(0,\s*1fr\)\)/,
  "宽屏模板选择、我的模板、模板操作和模板包应保持四栏",
);
assertMatch(reportWorkspaceCss, /\.template-selection-panel\s*\{\s*grid-column:\s*1;\s*grid-row:\s*1;/, "选择区应固定在宽屏第一列");
assertMatch(reportWorkspaceCss, /\.template-package-panel\s*\{\s*grid-column:\s*4;\s*grid-row:\s*1;/, "模板包应固定在宽屏第四列而不是换行");
assertMatch(
  responsiveOverridesCss,
  /@media\s*\(min-width:\s*861px\)\s*and\s*\(max-width:\s*1180px\)[\s\S]*?\.report-template-sidebar\s*\{\s*grid-template-columns:\s*repeat\(3,\s*minmax\(0,\s*1fr\)\)[\s\S]*?\.template-selection-panel\s*\{\s*grid-column:\s*span 3/,
  "中等宽度应让选择区独占首行，其余三个模板面板同排",
);
assertMatch(
  themeCss,
  /@container\s+report-workspace\s*\(max-width:\s*1160px\)[\s\S]*?\.template-selection-panel\s*\{[\s\S]*?grid-column:\s*1\s*\/\s*-1;[\s\S]*?grid-row:\s*auto;/,
  "工作区实际变窄时选择区必须独占首行并清除宽屏行定位",
);
assertMatch(
  themeCss,
  /@container\s+report-workspace\s*\(max-width:\s*1160px\)[\s\S]*?\.template-user-panel,\s*\.template-admin-panel,\s*\.template-package-panel\s*\{[\s\S]*?grid-column:\s*auto;[\s\S]*?grid-row:\s*auto;/,
  "工作区实际变窄时三个管理面板必须清除宽屏固定列，避免覆盖默认模板",
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
