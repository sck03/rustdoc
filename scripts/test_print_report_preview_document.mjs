import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const require = createRequire(import.meta.url);
const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const workspaceRoot = path.join(repoRoot, ".codex-runtime", "print-report-preview-document-test");
const entryPath = path.join(workspaceRoot, "entry.ts");
const bundlePath = path.join(workspaceRoot, "bundle.mjs");
const modelPath = path.join(
  repoRoot,
  "apps",
  "export-doc-web",
  "src",
  "features",
  "reports",
  "printReportPreviewDocument.ts",
);
const modelImportSpecifier = `./${path.relative(workspaceRoot, modelPath).replaceAll("\\", "/")}`;

fs.rmSync(workspaceRoot, { recursive: true, force: true });
fs.mkdirSync(workspaceRoot, { recursive: true });
fs.writeFileSync(
  entryPath,
  `export { buildPrintSourceHtml } from ${JSON.stringify(modelImportSpecifier)};`,
  "utf8",
);

const esbuild = require(path.join(repoRoot, "apps", "export-doc-web", "node_modules", "esbuild"));
await esbuild.build({ entryPoints: [entryPath], outfile: bundlePath, bundle: true, platform: "node", format: "esm" });
const { buildPrintSourceHtml } = await import(`${pathToFileURL(bundlePath).href}?v=${Date.now()}`);

const templateHtml = `<!doctype html>
<html lang="zh-CN">
<head>
  <style>
    @page { size: A4 portrait; margin: 1cm; }
    body { margin: 20px; }
  </style>
</head>
<body><main>第一页</main><section style="break-before: page">第二页</section></body>
</html>`;
const builtTemplateHtml = buildPrintSourceHtml(templateHtml);

assert(builtTemplateHtml.includes("@page { size: A4 portrait; margin: 1cm; }"), "应保留模板自己的 @page 页边距");
assert(builtTemplateHtml.includes("body { margin: 20px; }"), "应保留模板自己的 body 边距");
assert(!/@page\s*\{[^}]*margin\s*:\s*0\b/is.test(builtTemplateHtml), "不得清零模板页边距");
assert(!/margin\s*:\s*calc\(/i.test(builtTemplateHtml), "不得把页边距转移到只作用一次的 body margin");
assert(!/\.page-break\s*\+\s*\.(?:print-page|customs-page)/i.test(builtTemplateHtml), "不得依赖某一种模板分页类名");
for (const marginBox of [
  "top-left-corner",
  "top-left",
  "top-center",
  "top-right",
  "top-right-corner",
  "bottom-left-corner",
  "bottom-left",
  "bottom-center",
  "bottom-right",
  "bottom-right-corner",
]) {
  assert(new RegExp(`@${marginBox}\\s*\\{\\s*content\\s*:\\s*""`, "i").test(builtTemplateHtml), `应清空 ${marginBox} 页眉页脚槽位`);
}
assert(
  builtTemplateHtml.indexOf('id="edm-print-preview-overrides"') < builtTemplateHtml.toLowerCase().indexOf("</head>"),
  "打印覆盖样式应注入 head",
);
assert(builtTemplateHtml.includes("print-color-adjust: exact"), "应保留打印颜色校准");

const htmlWithoutHead = buildPrintSourceHtml('<html lang="en"><body>content</body></html>');
assert(htmlWithoutHead.includes('<html lang="en"><head>'), "缺少 head 的完整 HTML 应补入 head");
assert(countOccurrences(htmlWithoutHead, 'id="edm-print-preview-overrides"') === 1, "打印覆盖样式只能注入一次");

const fragmentHtml = buildPrintSourceHtml("<article>fragment</article>");
assert(fragmentHtml.startsWith("<!doctype html><html><head>"), "HTML 片段应包装为完整文档");
assert(fragmentHtml.includes("<body><article>fragment</article></body>"), "HTML 片段内容应保持不变");

const customMarginHtml = buildPrintSourceHtml(`<!doctype html><html><head><style>
  @page { size: A4 portrait; margin: 12mm 10mm 8mm; }
</style></head><body><div class="page-break"></div><div class="print-page">content</div></body></html>`);
assert(customMarginHtml.includes("@page { size: A4 portrait; margin: 12mm 10mm 8mm; }"), "自定义四边页边距应原样保留");

const explicitTopMarginHtml = buildPrintSourceHtml(`<!doctype html><html><head><style>
  @page { size: A4 portrait; margin: 8mm; margin-top: 14mm; }
</style></head><body><div class="page-break"></div><div class="print-page">content</div></body></html>`);
assert(explicitTopMarginHtml.includes("margin-top: 14mm"), "显式 margin-top 应原样保留");

const zeroBodyMarginHtml = buildPrintSourceHtml(`<!doctype html><html><head><style>
  body { margin: 0; padding: 15px; }
</style></head><body>content</body></html>`);
assert(zeroBodyMarginHtml.includes("body { margin: 0; padding: 15px; }"), "无 @page 的模板也应保留自身 body 版式");

console.log("print report preview document tests passed");

function countOccurrences(value, search) {
  return value.split(search).length - 1;
}

function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}
