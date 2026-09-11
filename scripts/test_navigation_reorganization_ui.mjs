import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import http from "node:http";
import { createRequire } from "node:module";
import { CdpClient, closeChrome, delay } from "./lib/chromium-cdp.mjs";
import { locateChromeForTesting } from "./lib/report-regression-common.mjs";
import { startChrome, createPageSession, evaluate, captureScreenshot } from "./lib/web-runtime-browser-session.mjs";
import { navigationReorganizationUiFixture } from "./lib/navigation-reorganization-ui-fixture.mjs";

const repo = path.resolve(import.meta.dirname, ".."), web = path.join(repo, "apps/export-doc-web");
const output = path.join(repo, "artifacts/navigation-reorganization-ui");
const require = createRequire(path.join(web, "package.json"));
fs.mkdirSync(output, { recursive: true });
const source = name => JSON.stringify(path.join(web, "src", name).replaceAll("\\", "/"));
await require("esbuild").build({ stdin: { loader: "tsx", resolveDir: web, contents: navigationReorganizationUiFixture(source) },
  outfile: path.join(output, "app.js"), bundle: true, format: "esm", platform: "browser", jsx: "automatic", logLevel: "silent" });
const server = http.createServer((request, response) => {
  const name = new URL(request.url, "http://localhost").pathname.slice(1);
  if (!name) { response.setHeader("Content-Type", "text/html; charset=utf-8"); response.end('<!doctype html><html lang="zh-CN"><meta name="viewport" content="width=device-width,initial-scale=1"><title>导航整理验收</title><link rel="stylesheet" href="/app.css"><div id="root"></div><script type="module" src="/app.js"></script></html>'); }
  else if (["app.js", "app.css"].includes(name)) { response.setHeader("Content-Type", name.endsWith("js") ? "text/javascript" : "text/css"); response.end(fs.readFileSync(path.join(output, name))); }
  else response.writeHead(404).end();
});
await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
let chrome, cdp, page;
const results = [];
const read = async expression => (await evaluate(page, expression, true)).value;
async function waitFor(expression) {
  const deadline = Date.now() + 20000;
  while (Date.now() < deadline) { if (await read(`Boolean(${expression})`)) return; await delay(70); }
  throw new Error(`Timed out: ${expression}; ${await read("document.body.innerText.slice(0,2400)")}`);
}
async function clickText(text, selector = "button") {
  await read(`(()=>{const node=[...document.querySelectorAll(${JSON.stringify(selector)})].find(n=>n.getClientRects().length&&n.textContent.trim()===${JSON.stringify(text)});if(!node)throw new Error('Missing '+${JSON.stringify(text)});node.click()})()`); await delay(100);
}
async function input(selector, value) {
  await read(`(()=>{const node=document.querySelector(${JSON.stringify(selector)});if(!node)throw new Error('Missing '+${JSON.stringify(selector)});const proto=node instanceof HTMLSelectElement?HTMLSelectElement.prototype:node instanceof HTMLTextAreaElement?HTMLTextAreaElement.prototype:HTMLInputElement.prototype;Object.getOwnPropertyDescriptor(proto,'value').set.call(node,${JSON.stringify(value)});node.dispatchEvent(new Event('input',{bubbles:true}));node.dispatchEvent(new Event('change',{bubbles:true}))})()`); await delay(100);
}
async function open(route, width = 1440, role = "admin") {
  if (page) await cdp.send("Target.closeTarget", { targetId: page.targetId });
  page = await createPageSession(cdp);
  await page.send("Emulation.setDeviceMetricsOverride", { width, height: 960, deviceScaleFactor: 1, mobile: false });
  await page.send("Page.navigate", { url: `http://127.0.0.1:${server.address().port}/?role=${role}#${route}` });
  await waitFor("document.querySelector('.workspace-header h1') && !document.querySelector('.workspace-content [aria-busy=true]')"); await delay(150);
}
async function audit(label) {
  await read("document.fonts.ready");
  await evaluate(page, fs.readFileSync(require.resolve("axe-core/axe.min.js"), "utf8"), false);
  const violations = await read("axe.run(document,{runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21aa']}}).then(r=>r.violations.map(v=>({id:v.id,nodes:v.nodes.map(n=>({target:n.target,summary:n.failureSummary}))})))");
  await captureScreenshot(page, path.join(output, label + ".png"));
  assert.deepEqual(violations, [], label + ": accessibility");
  assert(await read("document.documentElement.scrollWidth<=innerWidth+1"), label + ": overflow");
  assert.deepEqual(await read("window.__errors"), [], label + ": browser errors");
  results.push(label);
}
try {
  chrome = await startChrome({ browserExecutable: locateChromeForTesting(repo, "headless-shell"), userDataDir: path.join(output, "profile"), timeoutMs: 30000 });
  cdp = await CdpClient.connect(chrome.browserWebSocketUrl);
  for (const width of [1440, 900, 390]) {
    await open("/suppliers", width); await waitFor("document.querySelector('tbody tr button')");
    assert.equal(await read("document.querySelectorAll('[aria-label=供应商工作区] [role=tab]').length"), 2);
    await clickText("下一页"); await waitFor("window.__calls.some(c=>c.name==='suppliers'&&c.input.pageNumber===2)");
    await clickText("打开", "tbody button"); await waitFor("document.querySelector('input[name=name]')?.value==='示例单位 021'");
    assert(await read("window.__route.includes('supplierId=21')")); await audit("supplier-detail-" + width);
    await clickText("联系人", "[role=tab]"); await waitFor("document.querySelector('.supplier-contact-workspace')");
    await clickText("返回供应商目录"); await waitFor("document.querySelector('.pagination-bar')?.textContent.includes('第 2 /')"); results.push("supplier-return-" + width);
  }
  await open("/suppliers?view=profile&supplierId=135"); await waitFor("document.querySelector('input[name=name]')?.value==='示例单位 135'"); results.push("supplier-deep-link-beyond-first-100");
  await open("/crm/follow-ups?customerPage=2"); await waitFor("document.querySelector('tbody tr button')"); await clickText("打开", "tbody button");
  await waitFor("document.querySelector('input[name=name]')?.value==='示例单位 021'");
  await input('input[name=name]', "未保存的客户名称"); await clickText("联系人", "[role=tab]"); await waitFor("document.querySelector('.confirmation-dialog')");
  await clickText("取消", ".confirmation-dialog button"); assert.equal(await read("document.querySelector('input[name=name]').value"), "未保存的客户名称");
  await read("history.back()"); await waitFor("document.querySelector('.confirmation-dialog')"); await clickText("取消", ".confirmation-dialog button");
  assert.equal(await read("document.querySelector('input[name=name]').value"), "未保存的客户名称"); assert(await read("window.__route.includes('view=profile')"));
  await audit("customer-back-keeps-draft");
  await clickText("返回客户目录"); await waitFor("document.querySelector('.confirmation-dialog')"); await clickText("切换客户工作区", ".confirmation-dialog button");
  await waitFor("document.querySelector('.pagination-bar')?.textContent.includes('第 2 /')"); results.push("customer-return-page");
  await clickText("打开", "tbody button"); await waitFor("document.querySelector('input[name=name]')?.value==='示例单位 021'");
  await input('input[name=name]', "准备放弃的修改"); await clickText("联系人", "[role=tab]");
  await clickText("切换客户工作区", ".confirmation-dialog button"); await waitFor("document.querySelector('input[name=contactName]')");
  await clickText("客户资料", "[role=tab]"); await waitFor("document.querySelector('input[name=name]')?.value==='示例单位 021'");
  assert.equal(await read("Boolean(document.querySelector('.confirmation-dialog'))"), false); results.push("customer-discard-clears-dirty-state");
  await input('input[name=name]', "重复返回时保留草稿"); await read("history.back()"); await waitFor("document.querySelector('.confirmation-dialog')");
  await read("history.back()"); await delay(150); await clickText("取消", ".confirmation-dialog button");
  assert.equal(await read("location.hash.slice(1)"), await read("window.__route"));
  assert.equal(await read("document.querySelector('input[name=name]').value"), "重复返回时保留草稿"); results.push("repeated-back-keeps-address-and-draft");
  await clickText("供应商管理", "#workspace-primary-navigation a"); await clickText("离开当前编辑页", ".confirmation-dialog button");
  await waitFor("document.querySelector('[aria-label=供应商工作区]')"); await clickText("打开", "tbody button");
  await waitFor("document.querySelector('input[name=name]')"); await input('input[name=name]', "跨模块返回时保留草稿");
  await read("history.back()"); await waitFor("document.querySelector('.confirmation-dialog')"); await clickText("取消", ".confirmation-dialog button");
  assert.equal(await read("document.querySelector('input[name=name]').value"), "跨模块返回时保留草稿"); results.push("confirmed-link-keeps-history-guard");
  await open("/crm/follow-ups", 390, "customer-only"); await waitFor("document.querySelector('tbody tr button')");
  assert.equal(await read("document.querySelectorAll('[aria-label=客户业务工作区] [role=tab]').length"), 1);
  assert.equal(await read("window.__calls.some(c=>c.name==='followups'||c.name==='customerContacts')"), false); await audit("customer-only-permission");
  await open("/crm/follow-ups?view=followup-editor&editFollowUpId=135"); await waitFor("document.querySelector('input[name=summary]')?.value==='确认样品'");
  assert(await read("document.querySelector('select').value==='135'")); await audit("followup-exact-editor");
  await open("/crm/opportunities?view=editor&opportunityId=135&page=3"); await waitFor("document.querySelector('input[name=title]')?.value==='商机 135'");
  await clickText("报价与阶段历史", "[role=tab]"); await waitFor("document.querySelector('[aria-label=商机版本历史]')"); await audit("opportunity-exact-history");
  await clickText("返回商机目录"); await waitFor("document.querySelector('.pagination-bar')?.textContent.includes('第 3 /')"); results.push("opportunity-return-page");
  await open("/tools/email"); await waitFor("document.querySelector('input[type=email]')");
  await input('input[type=email]', "buyer@example.test"); await input('.email-compose-section label:nth-child(2) input', "尚未发送的邮件");
  await clickText("投递记录", ".workspace-section-nav button"); await waitFor("document.querySelector('[aria-label=邮件投递记录] table')");
  await clickText("写邮件", ".workspace-section-nav button"); assert.equal(await read("document.querySelector('input[type=email]').value"), "buyer@example.test"); results.push("mail-tabs-keep-draft");
  await open("/tools/email?view=deliveries&mailPage=2&mailStatus=Sent"); await waitFor("document.querySelector('[aria-label=邮件投递记录] tbody tr')");
  await clickText("写邮件", ".workspace-section-nav button"); await clickText("投递记录", ".workspace-section-nav button");
  await waitFor("document.querySelector('.pagination-bar')?.textContent.includes('第 2 /')");
  assert.equal(await read("document.querySelector('[aria-label=投递状态]').value"), "Sent"); results.push("mail-return-keeps-filters-and-page");
  await open("/tools/email?view=deliveries&mailPage=4", 390, "delivery-only"); await waitFor("document.querySelector('[aria-label=邮件投递记录] tbody tr')");
  assert.equal(await read("document.querySelectorAll('[aria-label=邮件投递记录] tbody tr').length"), 5);
  assert.equal(await read("document.querySelector('.workspace-section-nav').textContent.includes('写邮件')"), false);
  await input('[aria-label=投递状态]', "Sent"); await waitFor("window.__calls.some(c=>c.name==='deliveries'&&c.input.status==='Sent'&&c.input.pageNumber===1)"); await audit("mail-history-paging-permission");
  await open("/reports/templates/manage"); await waitFor("document.querySelector('.template-default-selection select')?.options.length>=3");
  assert.equal(await read("document.querySelectorAll('.template-selection-panel select').length"), 2);
  await input('.template-default-selection select', "user-template:23"); await waitFor("window.__route.includes('userTemplateId=23')");
  await clickText("输出默认值", "[role=tab]"); await waitFor("document.querySelector('.report-export-defaults-panel')?.getClientRects().length"); await audit("report-defaults-unified-directory");
  await clickText("导入导出", "[role=tab]"); await audit("report-transfer-view");
  await open("/tools/excel"); await waitFor("document.querySelector('.job-excel-grid')");
  assert.equal(await read("window.__calls.some(c=>c.name==='invoices')"), false); assert(await read("Boolean(document.querySelector('.job-excel-grid a[href=\"#/invoices\"]'))")); await audit("excel-context-shortcut");
  await open("/invoices"); await waitFor("document.querySelector('[aria-label=\"选择发票 INV-1\"]')");
  await read("document.querySelector('[aria-label=\"选择发票 INV-1\"]').click()"); await clickText("下一页"); await waitFor("document.querySelector('[aria-label=\"选择发票 INV-21\"]')");
  await read("document.querySelector('[aria-label=\"选择发票 INV-21\"]').click()"); await clickText("生成批量报表");
  await waitFor("document.querySelector('[aria-label=\"批量报表 ZIP 任务\"] button[type=submit]')?.disabled===false");
  await read("document.querySelector('[aria-label=\"批量报表 ZIP 任务\"] button[type=submit]').click()"); await waitFor("document.body.innerText.includes('测试依赖不可用')");
  assert.deepEqual(await read("window.__calls.find(c=>c.name==='batchExport').input.body.invoiceIds"), [1, 21]); await audit("invoice-cross-page-batch");
  fs.writeFileSync(path.join(output, "summary.json"), JSON.stringify({ passed: results.length, results }, null, 2));
  process.stdout.write('Navigation reorganization UI passed (' + results.length + ' cases).\n');
} finally {
  cdp?.close(); if (chrome) await closeChrome(chrome.browserWebSocketUrl, chrome.process);
  await new Promise(resolve => server.close(resolve));
}
