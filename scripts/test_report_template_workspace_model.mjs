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

console.log("report-template-workspace-model tests passed");

function assertEqual(actual, expected, message) {
  if (actual !== expected) {
    throw new Error(`${message}: expected ${JSON.stringify(expected)}, received ${JSON.stringify(actual)}`);
  }
}
