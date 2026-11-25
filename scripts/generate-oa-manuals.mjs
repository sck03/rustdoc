import fs from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";
import { createRequire } from "node:module";
import { createHash } from "node:crypto";
import { locateChromeForTesting } from "./lib/chromium-executable.mjs";

// Editable Markdown is the source; Chromium embeds subsets of the reviewed Noto fonts.
const root = path.resolve(import.meta.dirname, "..");
const sourceRoot = path.join(root, "docs/manuals");
const output = path.join(root, "artifacts/oa-manuals");
const require = createRequire(path.join(root, "apps/export-doc-web/package.json"));
fs.mkdirSync(path.join(output, "Temp"), { recursive: true });
Object.assign(process.env, { TEMP: path.join(output, "Temp"), TMP: path.join(output, "Temp") });
const fontUrl = name => pathToFileURL(path.join(root, "Resources/Fonts/OpenSource", name)).href;
for (const name of ["NotoSansCJKsc-Regular.otf", "NotoSansCJKsc-Bold.otf"]) {
  if (!fs.existsSync(path.join(root, "Resources/Fonts/OpenSource", name))) throw new Error("Run scripts/provision-report-fonts.mjs first.");
}
const escape = text => text.replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll('"', "&quot;");
function inline(text) {
  return escape(text).replace(/\*\*(.+?)\*\*/gu, "<strong>$1</strong>").replace(/`([^`]+)`/gu, "<code>$1</code>");
}
function markdown(source) {
  const lines = source.split(/\r?\n/u), blocks = [];
  for (let index = 0; index < lines.length; index++) {
    const line = lines[index].trim();
    if (!line) continue;
    if (line.startsWith("```")) {
      const code = [];
      while (++index < lines.length && !lines[index].trim().startsWith("```")) code.push(lines[index]);
      if (index === lines.length) throw new Error("Manual has an unterminated code block");
      blocks.push(`<pre><code>${escape(code.join("\n"))}</code></pre>`);
      continue;
    }
    const heading = /^(#{1,3})\s+(.+)$/u.exec(line);
    if (heading) { blocks.push(`<h${heading[1].length}>${inline(heading[2])}</h${heading[1].length}>`); continue; }
    if (line.startsWith("|")) {
      const rows = [];
      while (index < lines.length && lines[index].trim().startsWith("|")) {
        const cells = lines[index++].trim().slice(1, -1).split("|").map(cell => cell.trim());
        if (!cells.every(cell => /^:?-+:?$/u.test(cell))) rows.push(cells);
      }
      index--;
      blocks.push(`<table><thead><tr>${rows.shift().map(cell => `<th>${inline(cell)}</th>`).join("")}</tr></thead><tbody>${rows.map(row => `<tr>${row.map(cell => `<td>${inline(cell)}</td>`).join("")}</tr>`).join("")}</tbody></table>`);
      continue;
    }
    if (/^\d+\.\s/u.test(line) || line.startsWith("- ")) {
      const ordered = /^\d+\.\s/u.test(line), items = [], pattern = ordered ? /^\d+\.\s/u : /^-\s/u;
      while (index < lines.length && pattern.test(lines[index].trim())) items.push(`<li>${inline(lines[index++].trim().replace(pattern, ""))}</li>`);
      index--; blocks.push(`<${ordered ? "ol" : "ul"}>${items.join("")}</${ordered ? "ol" : "ul"}>`); continue;
    }
    blocks.push(`<p>${inline(line)}</p>`);
  }
  return blocks.join("\n");
}
const style = `
@font-face{font-family:Manual;src:url("${fontUrl("NotoSansCJKsc-Regular.otf")}");font-weight:400}
@font-face{font-family:Manual;src:url("${fontUrl("NotoSansCJKsc-Bold.otf")}");font-weight:700}
@page{size:A4;margin:19mm 18mm 19mm}
*{box-sizing:border-box}body{font-family:Manual,sans-serif;font-size:10.5pt;line-height:1.7;color:#233747;margin:0}
section{break-before:page}section:first-child{break-before:auto}h1{font-size:31pt;line-height:1.4;margin:32mm 0 15mm;color:#163d50}
h2{font-size:20pt;line-height:1.4;margin:0 0 8mm;padding-bottom:4mm;border-bottom:2px solid #4b8193;color:#163d50}
h3{font-size:13pt}p{margin:0 0 4mm}ol,ul{padding-left:6mm;margin:0 0 5mm}li{padding-left:1mm;margin-bottom:2mm}
table{border-collapse:collapse;width:100%;font-size:9.5pt;line-height:1.6;margin:5mm 0}th,td{text-align:left;vertical-align:top;border:1px solid #c7d4dc;padding:2.3mm 3mm}th{background:#edf3f6;color:#163d50}tr{break-inside:avoid}thead{display:table-header-group}
strong{font-weight:700}.cover p:first-of-type{font-size:18pt;color:#427285}.cover{border-top:6px solid #427285;padding-top:8mm}.cover p{max-width:160mm;margin-bottom:7mm}
pre{font-family:Manual,sans-serif;font-size:8.5pt;line-height:1.6;white-space:pre-wrap;overflow-wrap:anywhere;background:#f3f6f8;border:1px solid #c7d4dc;padding:3mm;break-inside:avoid}code{font-family:inherit}
figure{margin:5mm 0}img{display:block;width:100%;max-height:207mm;object-fit:contain;object-position:top}figcaption{margin-top:4mm;color:#526877;font-size:9pt}
`;
const browser = await require("playwright").chromium.launch({ executablePath: locateChromeForTesting(root), headless: true });
const manifest = [];
const imageTitles = {
  "form-designer.png": { title: "自定义申请表单", date: "2026-11-17" },
  "project-duties.png": { title: "项目限定职责", date: "2026-11-20" },
  "request-details.png": { title: "申请详情与办理结果", date: "2026-11-17" },
  "packing-workspace.png": { title: "装柜工作区", date: "2026-11-20" },
  "hs-import-preview.png": { title: "HS 导入差异核对", date: "2026-11-22" },
  "attachment-category.png": { title: "业务资料分类筛选", date: "2026-11-22" },
};
try {
  const manuals = [
    ["OA管理员手册", "ADMIN", ["form-designer.png", "project-duties.png"]],
    ["OA用户手册", "USER", ["request-details.png"]],
    ["项目程序操作说明", "OPERATIONS", ["hs-import-preview.png", "attachment-category.png", "packing-workspace.png", "project-duties.png"]],
  ];
  const selected = process.argv[2];
  if (selected && !manuals.some(([name]) => name === selected)) throw new Error(`Unknown manual: ${selected}`);
  for (const [name, label, screenshots] of manuals.filter(([name]) => !selected || selected === name)) {
    const source = fs.readFileSync(path.join(sourceRoot, `${name}.md`), "utf8");
    const date = source.match(/版本日期：(\d{4}-\d{2}-\d{2})/u)?.[1];
    if (!date) throw new Error(`${name}: source revision date is required`);
    const sections = source.split(/^---\s*$/mu).map((section, index) => `<section class="${index ? "chapter" : "cover"}">${markdown(section)}</section>`);
    for (const screenshot of screenshots) {
      const screenshotPath = path.join(sourceRoot, "images", screenshot);
      if (!fs.existsSync(screenshotPath)) throw new Error(`Anonymous UI evidence is required: ${screenshotPath}`);
      const reference = imageTitles[screenshot];
      if (!reference) throw new Error(`Screenshot caption is required: ${screenshot}`);
      sections.push(`<section><h2>${escape(reference.title)}</h2><figure><img src="${pathToFileURL(screenshotPath).href}" alt="${escape(reference.title)}匿名界面"><figcaption>${reference.date} 匿名验收界面示例。显示的字段、操作和流程以当前产品版、身份权限及公司发布规则为准。</figcaption></figure></section>`);
    }
    const html = path.join(output, `${name}.html`);
    fs.writeFileSync(html, `<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><title>${name}</title><style>${style}</style></head><body>${sections.join("\n")}</body></html>`);
    const page = await browser.newPage();
    try {
      await page.goto(pathToFileURL(html).href);
      await page.evaluate(async () => { await document.fonts.ready; await Promise.all([...document.images].map(image => image.decode())); });
      const pdf = path.join(sourceRoot, `${name}.pdf`);
      const stagedPdf = path.join(output, `${name}.rendered.pdf`);
      await page.pdf({ path: stagedPdf, preferCSSPageSize: true, printBackground: true, tagged: true, outline: true, displayHeaderFooter: true,
        headerTemplate: '<div></div>', footerTemplate: `<div style="font-size:8px;width:100%;margin:0 18mm;color:#607080;display:flex;justify-content:space-between"><span>ExportDocManager / ${label} / ${date}</span><span><span class="pageNumber"></span> / <span class="totalPages"></span></span></div>` });
      fs.renameSync(stagedPdf, pdf);
      manifest.push({ file: path.relative(root, pdf), bytes: fs.statSync(pdf).size, sourceSha256: createHash("sha256").update(source).digest("hex"), expectedPages: sections.length });
    } finally { await page.close(); }
  }
} finally { await browser.close(); }
fs.writeFileSync(path.join(output, "manifest.json"), JSON.stringify(manifest, null, 2));
console.log(JSON.stringify(manifest, null, 2));
