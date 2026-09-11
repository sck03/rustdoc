import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import http from "node:http";
import { createRequire } from "node:module";
import { CdpClient, closeChrome, delay } from "./lib/chromium-cdp.mjs";
import { locateChromeForTesting } from "./lib/report-regression-common.mjs";
import { startChrome, createPageSession, evaluate, captureScreenshot } from "./lib/web-runtime-browser-session.mjs";
import { documentEditorUiFixture } from "./lib/document-editor-ui-fixture.mjs";

const repo = path.resolve(import.meta.dirname, ".."), web = path.join(repo, "apps/export-doc-web");
const output = path.join(repo, "artifacts/document-editor-ui");
const require = createRequire(path.join(web, "package.json"));
fs.mkdirSync(output, { recursive: true });
const source = name => JSON.stringify(path.join(web, "src", name).replaceAll("\\", "/"));
await require("esbuild").build({ stdin: { loader: "tsx", resolveDir: web, contents: documentEditorUiFixture(source) },
  outfile: path.join(output, "app.js"), bundle: true, format: "esm", platform: "browser", jsx: "automatic", logLevel: "silent" });
const server = http.createServer((request, response) => {
  const name = new URL(request.url, "http://localhost").pathname.slice(1);
  if (!name) {
    response.setHeader("Content-Type", "text/html; charset=utf-8");
    response.end('<!doctype html><html lang="zh-CN"><meta name="viewport" content="width=device-width,initial-scale=1"><title>单据界面验收</title><link rel="stylesheet" href="/app.css"><div id="root"></div><script type="module" src="/app.js"></script></html>');
  } else if (["app.js", "app.css"].includes(name)) {
    response.setHeader("Content-Type", name.endsWith("js") ? "text/javascript" : "text/css");
    response.end(fs.readFileSync(path.join(output, name)));
  } else response.writeHead(404).end();
});
await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
const results = [];
const read = async (page, expression) => (await evaluate(page, expression, true)).value;
async function waitFor(page, expression) {
  const deadline = Date.now() + 20000;
  while (Date.now() < deadline) { if (await read(page, `Boolean(${expression})`)) return; await delay(75); }
  throw new Error(`Timed out: ${expression}; ${await read(page, "document.body.innerText.slice(0,1800)")}`);
}
async function click(page, selector) {
  await read(page, `(()=>{const node=document.querySelector(${JSON.stringify(selector)});if(!node||!node.getClientRects().length)throw new Error('Hidden or missing '+${JSON.stringify(selector)});node.click()})()`);
  await delay(100);
}
async function clickText(page, text, selector = "button") {
  await read(page, `(()=>{const node=[...document.querySelectorAll(${JSON.stringify(selector)})].find(n=>n.getClientRects().length&&n.textContent.trim()===${JSON.stringify(text)});if(!node)throw new Error('Missing '+${JSON.stringify(text)});node.click()})()`);
  await delay(100);
}
async function input(page, selector, value) {
  await read(page, `(()=>{const node=document.querySelector(${JSON.stringify(selector)});const prototype=node instanceof HTMLSelectElement?HTMLSelectElement.prototype:node instanceof HTMLTextAreaElement?HTMLTextAreaElement.prototype:HTMLInputElement.prototype;Object.getOwnPropertyDescriptor(prototype,'value').set.call(node,${JSON.stringify(value)});node.dispatchEvent(new Event('input',{bubbles:true}));node.dispatchEvent(new Event('change',{bubbles:true}))})()`);
  await delay(80);
}
async function field(page, label, value) {
  const selector = await read(page, `(()=>{const labels=[...document.querySelectorAll('label')];const node=labels.find(n=>n.querySelector('.form-field-label')?.textContent.replace('必填','').trim()===${JSON.stringify(label)})?.querySelector('input,select,textarea');if(!node)throw new Error('Missing field '+${JSON.stringify(label)});node.dataset.testField='current';return '[data-test-field="current"]';})()`);
  await input(page, selector, value);
  await read(page, "document.querySelector('[data-test-field=current]').removeAttribute('data-test-field')");
}
async function audit(page, label) {
  await read(page, "document.fonts.ready.then(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))))");
  await evaluate(page, fs.readFileSync(require.resolve("axe-core/axe.min.js"), "utf8"), false);
  const violations = await read(page, "axe.run(document,{runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21aa']}}).then(r=>r.violations.map(v=>({id:v.id,nodes:v.nodes.map(n=>({target:n.target,summary:n.failureSummary}))})))");
  if (violations.length) await captureScreenshot(page, path.join(output, label + "-failed.png"));
  assert.deepEqual(violations, [], label + ": accessibility");
  if (!await read(page, "document.documentElement.scrollWidth<=innerWidth+1")) {
    await captureScreenshot(page, path.join(output, label + "-overflow.png"));
    const overflow = await read(page, "[...document.querySelectorAll('body *')].filter(n=>n.getClientRects().length&&!n.closest('.table-frame')&&n.getBoundingClientRect().right>innerWidth+1).map(n=>({tag:n.tagName,class:n.className,width:n.getBoundingClientRect().width})).slice(0,12)");
    assert.fail(label + ": page overflow " + JSON.stringify(overflow));
  }
  assert.deepEqual(await read(page, "window.__errors"), [], label + ": browser errors");
  results.push(label);
}
let chrome, cdp;
try {
  chrome = await startChrome({ browserExecutable: locateChromeForTesting(repo), userDataDir: path.join(output, `profile-${Date.now()}`), timeoutMs: 30000 });
  cdp = await CdpClient.connect(chrome.browserWebSocketUrl);
  const page = await createPageSession(cdp);
  const open = async (mode, width = 1440, extra = "") => {
    await page.send("Emulation.setDeviceMetricsOverride", { width, height: 960, deviceScaleFactor: 1, mobile: false });
    await page.send("Page.navigate", { url: `http://127.0.0.1:${server.address().port}/?mode=${mode}${extra}` });
    await waitFor(page, "document.querySelector('form') && window.__calls.some(call=>call.name==='getSettings') || document.querySelector('[aria-label=发票录入默认值]')");
    await delay(200);
  };
  for (const width of [1440, 1024, 390, 320]) {
    await open("new", width);
    assert.equal(await read(page, "document.querySelectorAll('[role=tabpanel]:not([hidden])').length"), 1);
    assert.equal(await read(page, "window.__calls.some(call=>call.name==='listReportTemplates')"), false, "output loads only when opened");
    await audit(page, `invoice-header-${width}`);
    await captureScreenshot(page, path.join(output, `invoice-header-${width}.png`));
    await clickText(page, "银行与印章", "summary");
    await audit(page, `invoice-bank-details-${width}`);
    await click(page, "#invoice-tab-items");
    await audit(page, `invoice-items-${width}`);
    await captureScreenshot(page, path.join(output, `invoice-items-${width}.png`));
    await click(page, '[aria-label="显示/隐藏明细列"]');
    await read(page, "(()=>{const list=document.querySelector('.item-column-menu-list');list.scrollTop=list.scrollHeight})()");
    assert(await read(page, "(()=>{const menu=document.querySelector('.item-column-menu').getBoundingClientRect(),last=document.querySelector('.item-column-option:last-child').getBoundingClientRect();return last.bottom<=menu.bottom+1&&last.top>=menu.top})()"), "all ten spare choices must be reachable without clipping");
    await audit(page, `invoice-column-menu-${width}`);
  }
  await open("new");
  await field(page, "合同号", "DRAFT-CONTRACT");
  await click(page, "#invoice-tab-items");
  assert.equal(await read(page, "document.querySelectorAll('th[data-field^=spare],input[aria-label*=备用]').length"), 0);
  await click(page, "#invoice-tab-shipping");
  await field(page, "目的国", "CANADA");
  await clickText(page, "保存发票");
  await waitFor(page, "document.querySelector('#invoice-tab-header').getAttribute('aria-selected')==='true'");
  assert.equal(await read(page, "window.__calls.some(call=>call.name==='createInvoice')"), false);
  await waitFor(page, "document.activeElement.closest('label')?.textContent.includes('发票号')");
  await field(page, "发票号", "NEW-2026-091");
  await click(page, "#invoice-tab-shipping");
  await clickText(page, "保存发票");
  await waitFor(page, "window.__route==='/invoices/7'");
  const saved = await read(page, "window.__calls.find(call=>call.name==='createInvoice').input.body");
  assert.equal(saved.invoiceNo, "NEW-2026-091"); assert.equal(saved.contractNo, "DRAFT-CONTRACT"); assert.equal(saved.destinationCountry, "CANADA");
  results.push("cross-tab-draft-and-hidden-required-field");
  await open("edit", 1440, "&spares=3&populated=1");
  await click(page, "#invoice-tab-items");
  const spareHeaders = () => read(page, "[...document.querySelectorAll('.item-editor-table th')].map(n=>n.textContent.trim()).filter(text=>/^备用/.test(text))");
  assert.deepEqual(await spareHeaders(), ["备用 1", "备用 2", "备用 3", "备用 10"]);
  await read(page, "window.__updateDefaultSpares(0)");
  await waitFor(page, "[...document.querySelectorAll('.item-editor-table th')].filter(n=>/^备用/.test(n.textContent.trim())).length===1");
  await click(page, '[aria-label="显示/隐藏明细列"]');
  await clickText(page, "全部显示"); assert.equal((await spareHeaders()).length, 10);
  await clickText(page, "恢复默认"); assert.deepEqual(await spareHeaders(), ["备用 10"]);
  await click(page, '[aria-label="显示/隐藏明细列"]');
  await click(page, "#invoice-tab-header"); await click(page, "#invoice-tab-items");
  assert.deepEqual(await spareHeaders(), ["备用 10"]);
  await input(page, 'input[data-invoice-item-row="0"][data-invoice-item-field="spare10"]', "");
  assert.deepEqual(await spareHeaders(), ["备用 10"], "clearing the last value must keep the editing column visible");
  await click(page, '[aria-label="撤销明细编辑"]');
  assert.equal(await read(page, 'document.querySelector("input[data-invoice-item-field=spare10]").value'), "保留原始备注");
  await click(page, '[aria-label="显示/隐藏明细列"]');
  await clickText(page, "备用 10", ".item-column-option");
  assert.deepEqual(await spareHeaders(), []);
  await click(page, '[aria-label="显示/隐藏明细列"]');
  await clickText(page, "保存发票"); await waitFor(page, "window.__calls.some(call=>call.name==='updateInvoice')");
  assert.equal(await read(page, "window.__calls.find(call=>call.name==='updateInvoice').input.body.items[0].spare10"), "保留原始备注");
  await clickText(page, "明细工作台"); await waitFor(page, "document.querySelector('[aria-label=商品明细工作台]')");
  await clickText(page, "返回发票"); await waitFor(page, "document.querySelector('#invoice-tab-items')?.getAttribute('aria-selected')==='true'");
  results.push("column-default-populated-data-and-workbench-return");
  await open("edit");
  await click(page, "#invoice-tab-shipping");
  await read(page, "document.querySelector('#invoice-tab-shipping').focus()");
  await page.send("Input.dispatchKeyEvent", { type: "keyDown", key: "End", code: "End", windowsVirtualKeyCode: 35 });
  await page.send("Input.dispatchKeyEvent", { type: "keyUp", key: "End", code: "End", windowsVirtualKeyCode: 35 });
  await waitFor(page, "document.querySelector('#invoice-tab-report').getAttribute('aria-selected')==='true'");
  await waitFor(page, "window.__calls.some(call=>call.name==='listReportTemplates')");
  await click(page, "#invoice-tab-header"); await field(page, "合同号", "KEYBOARD-SAVE"); await click(page, "#invoice-tab-items");
  await page.send("Input.dispatchKeyEvent", { type: "keyDown", key: "s", code: "KeyS", windowsVirtualKeyCode: 83, modifiers: 2 });
  await page.send("Input.dispatchKeyEvent", { type: "keyUp", key: "s", code: "KeyS", windowsVirtualKeyCode: 83, modifiers: 2 });
  await waitFor(page, "window.__calls.some(call=>call.name==='updateInvoice')");
  assert.equal(await read(page, "window.__calls.find(call=>call.name==='updateInvoice').input.body.contractNo"), "KEYBOARD-SAVE");
  results.push("keyboard-tabs-and-save-from-items");
  await open("payment");
  assert(await read(page, "document.body.innerText.includes('收汇日期')&&!document.body.innerText.includes('收票日期')"));
  await clickText(page, "新增选项"); await field(page, "新增付款方式", "银行承兑汇票");
  await clickText(page, "添加并选择"); await waitFor(page, "document.body.innerText.includes('下次可直接选择')");
  assert.equal(await read(page, "document.querySelector('.custom-option-select select').value"), "银行承兑汇票");
  await read(page, "window.__reloadOptions()");
  assert.equal(await read(page, "document.querySelector('.custom-option-select select').options.length"), 5);
  await clickText(page, "新增选项"); await field(page, "新增付款方式", "银行承兑汇票"); await clickText(page, "添加并选择");
  assert.equal(await read(page, "window.__calls.filter(call=>call.name==='saveCustomOption').length"), 1);
  await clickText(page, "新增选项"); await field(page, "新增付款方式", "现金"); await read(page, "window.__failCustomOption=true"); await clickText(page, "添加并选择");
  await waitFor(page, "document.body.innerText.includes('选项未添加')");
  assert.equal(await read(page, "document.querySelector('.custom-option-create input').value"), "现金");
  await clickText(page, "取消", ".custom-option-create button");
  await clickText(page, "保存"); await waitFor(page, "window.__calls.some(call=>call.name==='updatePayment')");
  const savedPayment = await read(page, "window.__calls.find(call=>call.name==='updatePayment').input.body");
  assert.equal(savedPayment.paymentMethod, "银行承兑汇票"); assert.equal(savedPayment.receiptDate, "2026-09-11");
  results.push("payment-choice-persistence-deduplication-and-failure");
  for (const width of [1440, 390, 320]) { await open("payment", width); await audit(page, `payment-${width}`); await captureScreenshot(page, path.join(output, `payment-${width}.png`)); }
  await open("settings", 1024); await field(page, "默认显示备用列数", "4");
  assert.deepEqual(await read(page, "window.__settingPatch"), { path: ["system", "itemEntrySpareColumnCount"], value: 4 });
  results.push("settings-default-spare-column-control");
  await open("payment", 390, "&role=reader");
  assert(await read(page, "[...document.querySelectorAll('button')].find(n=>n.textContent.trim()==='新增选项').matches(':disabled')"));
  await open("edit", 390, "&role=reader"); await click(page, "#invoice-tab-items");
  assert(await read(page, "[...document.querySelectorAll('.item-editor-table input')].every(n=>n.disabled||n.readOnly)"));
  await audit(page, "invoice-reader-tabs");
  fs.writeFileSync(path.join(output, "summary.json"), JSON.stringify({ passed: results.length, results }, null, 2));
  process.stdout.write(`Document editor UI passed (${results.length} cases).\n`);
} finally {
  cdp?.close(); if (chrome) await closeChrome(chrome.browserWebSocketUrl, chrome.process);
  await new Promise(resolve => server.close(resolve));
}
