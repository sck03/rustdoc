import { createRequire } from "node:module";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { spawn } from "node:child_process";
import { locateChromeForTesting } from "./lib/report-regression-common.mjs";
import { buildChromiumSandboxArguments } from "./lib/chromium-sandbox-policy.mjs";

const require = createRequire(import.meta.url);
const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const runtimeRoot = path.join(repoRoot, ".codex-runtime", "report-designer-v3-print-regression");
const entryPath = path.join(runtimeRoot, "entry.ts");
const bundlePath = path.join(runtimeRoot, "bundle.mjs");
const htmlPath = path.join(runtimeRoot, "v3-print.html");
const pdfPath = path.join(runtimeRoot, "v3-print.pdf");

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

async function runProcess(command, args, options = {}) {
  return await new Promise((resolve, reject) => {
    const child = spawn(command, args, { ...options, windowsHide: true });
    let stdout = "";
    let stderr = "";
    child.stdout?.on("data", (chunk) => { stdout += chunk; });
    child.stderr?.on("data", (chunk) => { stderr += chunk; });
    child.once("error", reject);
    child.once("close", (code) => resolve({ code, stdout, stderr }));
  });
}

async function main() {
  fs.mkdirSync(runtimeRoot, { recursive: true });
  const esbuild = require(path.join(repoRoot, "apps", "export-doc-web", "node_modules", "esbuild"));
  const exporterPath = path.join(repoRoot, "apps/export-doc-web/src/features/report-designer/reportDesignerV3HtmlExporter.ts");
  const relativeExporter = path.relative(runtimeRoot, exporterPath).replaceAll("\\", "/");
  fs.writeFileSync(entryPath, `export { exportReportDesignerV3SchemaToHtml } from ${JSON.stringify(relativeExporter.startsWith(".") ? relativeExporter : `./${relativeExporter}`)};\n`, "utf8");
  await esbuild.build({ entryPoints: [entryPath], outfile: bundlePath, bundle: true, format: "esm", platform: "node", target: "node20", logLevel: "silent" });
  const { exportReportDesignerV3SchemaToHtml } = await import(pathToFileURL(bundlePath).href);

  const detailTable = {
    id: "v3-regression-detail",
    type: "DetailTable",
    title: "Invoice details",
    sourcePath: "Invoice.Items",
    repeatMode: "ScribanFor",
    print: { repeatHeaderOnPageBreak: true, keepRowsTogether: true },
    columns: [
      { id: "description", title: "Description", contentKind: "Field", fieldPath: "Invoice.Items.ProductNameEN", content: [], widthMm: 118, align: "Left" },
      { id: "quantity", title: "Qty", contentKind: "Field", fieldPath: "Invoice.Items.Quantity", content: [], widthMm: 22, align: "Right" },
      { id: "amount", title: "Amount", contentKind: "Field", fieldPath: "Invoice.Items.TotalPrice", content: [], widthMm: 30, align: "Right" },
    ],
    summaryRow: { label: "TOTAL", labelColumnSpan: 2, cells: [{ columnId: "amount", contentKind: "Field", text: "", fieldPath: "Invoice.TotalAmount" }], style: { fontSizePt: 9, bold: true, align: "Right" } },
    headerStyle: { fontSizePt: 9, bold: true, align: "Center" },
    bodyStyle: { fontSizePt: 8, align: "Left" },
    border: { color: "#333333", widthPx: 1, style: "Solid", top: true, right: true, bottom: true, left: true },
  };
  const schema = {
    version: 3,
    reportType: "ExportDocument",
    page: { size: "A4", orientation: "Portrait", widthHundredthMm: 21000, heightHundredthMm: 29700, marginTopHundredthMm: 1000, marginRightHundredthMm: 1000, marginBottomHundredthMm: 1000, marginLeftHundredthMm: 1000, fontFamily: "Arial, sans-serif", fontSizePt: 9 },
    grid: { enabled: true, sizeHundredthMm: 500, snap: true },
    layers: [
      { id: "header", name: "页眉", role: "Header", print: { repeatOnEveryPage: true, keepTogether: true, pinToPageBottom: false, minHeightHundredthMm: 1200 }, visible: true, locked: false, elements: [{ id: "header-title", type: "Text", xHundredthMm: 1000, yHundredthMm: 700, widthHundredthMm: 19000, heightHundredthMm: 700, rotationDeg: 0, zIndex: 0, visible: true, locked: false, style: { fontSizePt: 14, bold: true, align: "Center" }, outputEnabled: true, text: "V3 INVOICE HEADER" }] },
      { id: "body", name: "主体", role: "Body", print: { repeatOnEveryPage: false, keepTogether: false, pinToPageBottom: false, minHeightHundredthMm: 0 }, visible: true, locked: false, elements: [{ id: "detail", type: "Flow", flowKind: "DetailTable", xHundredthMm: 1000, yHundredthMm: 4200, widthHundredthMm: 19000, heightHundredthMm: 5200, rotationDeg: 0, zIndex: 0, visible: true, locked: false, style: {}, outputEnabled: true, block: detailTable }] },
      { id: "footer", name: "页脚", role: "Footer", print: { repeatOnEveryPage: true, keepTogether: true, pinToPageBottom: true, minHeightHundredthMm: 900 }, visible: true, locked: false, elements: [{ id: "footer-text", type: "Text", xHundredthMm: 1000, yHundredthMm: 28600, widthHundredthMm: 19000, heightHundredthMm: 600, rotationDeg: 0, zIndex: 0, visible: true, locked: false, style: { fontSizePt: 8, align: "Center" }, outputEnabled: true, text: "V3 INVOICE FOOTER" }] },
      { id: "overlay", name: "覆盖层", role: "Overlay", print: { repeatOnEveryPage: false, keepTogether: false, pinToPageBottom: false, minHeightHundredthMm: 0 }, visible: true, locked: false, elements: [] },
    ],
  };
  const rows = Array.from({ length: 120 }, (_, index) => ({ ProductNameEN: `Product ${index + 1} with a long description for controlled wrapping`, Quantity: String(index + 1), TotalPrice: (index * 2.5 + 1).toFixed(2) }));
  const html = exportReportDesignerV3SchemaToHtml(schema)
    .replace(/\{\{\s*for\s+item\s+in\s+Invoice\.Items\s*\}\}([\s\S]*?)\{\{\s*end\s*\}\}/g, (_, template) => rows.map((row) => template.replace(/\{\{\s*item\.([A-Za-z_][A-Za-z0-9_]*)\s*\}\}/g, (_match, key) => row[key] ?? "")).join(""))
    .replace(/\{\{\s*Invoice\.TotalAmount\s*\}\}/g, "999.00");
  fs.writeFileSync(htmlPath, html, "utf8");

  const chromePath = locateChromeForTesting(repoRoot);
  assert(chromePath, "未找到 Chromium 测试运行时。");
  const profilePath = path.join(runtimeRoot, "ChromeProfile");
  fs.rmSync(profilePath, { recursive: true, force: true });
  const result = await runProcess(chromePath, [
    ...buildChromiumSandboxArguments(),
    "--headless",
    "--disable-gpu",
    "--disable-extensions",
    "--no-first-run",
    "--hide-scrollbars",
    "--no-pdf-header-footer",
    `--user-data-dir=${profilePath}`,
    `--print-to-pdf=${pdfPath}`,
    pathToFileURL(htmlPath).href,
  ], { cwd: runtimeRoot, stdio: ["ignore", "pipe", "pipe"] });
  assert(result.code === 0 && fs.existsSync(pdfPath), `Chromium PDF 输出失败：${result.stderr}`);

  const pdfBytes = fs.readFileSync(pdfPath);
  const pageCount = (pdfBytes.toString("latin1").match(/\/Type\s*\/Page\b/g) ?? []).length;
  assert(pageCount >= 3, `V3 明细流应输出至少 3 页，实际 ${pageCount} 页。`);
  const python = process.env.EDM_PYTHON ?? "python";
  const textResult = await runProcess(python, ["-c", "from pypdf import PdfReader; import sys; print('\\n'.join((p.extract_text() or '') for p in PdfReader(sys.argv[1]).pages))", pdfPath], { cwd: runtimeRoot, stdio: ["ignore", "pipe", "pipe"] });
  assert(textResult.code === 0, `无法读取 V3 PDF 文本：${textResult.stderr}`);
  const extracted = textResult.stdout;
  assert((extracted.match(/V3 INVOICE HEADER/g) ?? []).length >= 3, "V3 固定页眉没有在多页中重复。");
  assert((extracted.match(/V3 INVOICE FOOTER/g) ?? []).length >= 3, "V3 固定页脚没有在多页中重复。");
  assert(extracted.includes("Product 120"), "V3 明细流没有完整输出末行。");
  console.log(`report-designer-v3-print-regression test passed (${pageCount} pages)`);
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : error);
  process.exitCode = 1;
});
