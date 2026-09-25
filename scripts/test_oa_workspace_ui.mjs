import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { CdpClient, closeChrome, delay } from "./lib/chromium-cdp.mjs";
import { locateChromeForTesting } from "./lib/report-regression-common.mjs";
import { startChrome, createPageSession, evaluate, captureScreenshot } from "./lib/web-runtime-browser-session.mjs";
import { spawnProcessTree, stopProcessTree } from "./lib/child-process-tree.mjs";

const repo = path.resolve(import.meta.dirname, "..");
const output = path.join(repo, "artifacts/oa-native-ui", Date.now().toString());
fs.mkdirSync(output, { recursive: true });
const executable = path.join(repo, "target/debug/examples", process.platform === "win32" ? "office_review.exe" : "office_review");
const server = spawnProcessTree(executable, [path.join(output, "Data")], { cwd: repo, windowsHide: true, stdio: ["ignore", "pipe", "pipe"] });
let chrome, cdp, page;
const run = async expression => (await evaluate(page, expression, true)).value;
async function wait(expression, message) {
  const deadline = Date.now() + 30000;
  while (Date.now() < deadline) { if (await run(`Boolean(${expression})`)) return; await delay(150); }
  throw new Error(`${message}\n${await run("document.body.innerText")}`);
}
async function click(text, scope = "document") {
  await run(`(() => {const button=[...${scope}.querySelectorAll('button,a')].find(e=>e.textContent.trim()===${JSON.stringify(text)} && e.getClientRects().length); if(!button) throw new Error('Missing control: '+${JSON.stringify(text)}); button.click();})()`);
}
async function fill(label, value) {
  await run(`(() => {const label=[...document.querySelectorAll('label')].find(e=>e.querySelector('span')?.textContent===${JSON.stringify(label)}); const input=label?.querySelector('input,textarea,select'); if(!input) throw new Error('Missing field: '+${JSON.stringify(label)}); Object.getOwnPropertyDescriptor(Object.getPrototypeOf(input),'value').set.call(input,${JSON.stringify(value)}); input.dispatchEvent(new Event('input',{bubbles:true})); input.dispatchEvent(new Event('change',{bubbles:true}));})()`);
}
async function navigate(url) { await page.send("Page.navigate", { url }); await wait("document.readyState === 'complete'", "Page load timed out"); }
async function act(label, note) {
  await click(label);
  await wait("document.querySelector('[role=dialog] textarea')", "Action dialog missing");
  await fill("处理说明", note);
  await run("document.querySelector('[role=dialog] button[type=submit]').click()");
  await wait("!document.querySelector('[role=dialog]')", `${label} did not complete`);
}
try {
  const url = await new Promise((resolve, reject) => {
    let log = "";
    const timer = setTimeout(() => reject(new Error(`Review server startup timed out: ${log}`)), 60000);
    server.on("error", error => { clearTimeout(timer); reject(error); });
    server.on("exit", code => { clearTimeout(timer); reject(new Error(`Review server exited ${code}: ${log}`)); });
    server.stderr.on("data", chunk => { log += chunk; });
    server.stdout.on("data", chunk => { log += chunk; const match = log.match(/\{"url":"([^"]+)"\}/u); if (match) { clearTimeout(timer); resolve(match[1]); } });
  });
  const browser = locateChromeForTesting(repo);
  chrome = await startChrome({ browserExecutable: browser, userDataDir: path.join(output, "Chrome"), timeoutMs: 30000 });
  cdp = await CdpClient.connect(chrome.browserWebSocketUrl); page = await createPageSession(cdp);
  await page.send("Page.addScriptToEvaluateOnNewDocument", { source: "window.__oaErrors=[];addEventListener('error',e=>window.__oaErrors.push(e.message));addEventListener('unhandledrejection',e=>window.__oaErrors.push(String(e.reason)));" });
  await navigate(url);
  await wait("document.querySelector('input[autocomplete=username]')", "Login not displayed");
  await run(`(() => {for(const [selector,value] of [['input[autocomplete=username]','oa-review'],['input[autocomplete=current-password]','Review-2026-Test']]){const input=document.querySelector(selector);Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(input,value);input.dispatchEvent(new Event('input',{bubbles:true}));}})()`);
  await click("登录");
  await wait("!document.querySelector('.login-submit-button')", "Login failed");
  await navigate(`${url}/#/office/approvals`);
  await wait("document.querySelector('[aria-label=\"申请与审批中心\"]')", "Approval hub missing");
  assert(await run("document.body.innerText.includes('人事管理') && document.body.innerText.includes('行政办公')"), "Separated navigation groups are visible");
  await captureScreenshot(page, path.join(output, "approval-center.png"));
  const yesterday = new Date(Date.now() - 86400000).toISOString().slice(0, 10);
  for (const [kind, name] of [["leave", "员工请假"], ["overtime", "加班申请"], ["expense", "费用报销"], ["travel", "出差申请"], ["purchase", "采购申请"], ["general", "通用申请"]]) {
    await navigate(`${url}/#/office/requests/${kind}`);
    await wait(`document.querySelector('.oa-workspace')?.getAttribute('aria-label')===${JSON.stringify(name)}`, `${name} page missing`);
    await click(`新建${name}`);
    await wait("document.querySelector('[role=dialog] .remote-select-field select option[value]:not([value=\"\"])')", "Employee options missing");
    await run("(() => {const select=document.querySelector('[role=dialog] .remote-select-field select');select.value=[...select.options].find(o=>o.value).value;select.dispatchEvent(new Event('change',{bubbles:true}));})()");
    await fill("申请标题", `${name}真实界面验收`); await fill("申请说明", "验证中文输入、保存、附件和审批历史。");
    if (kind === "leave") { await fill("开始日期", yesterday); await fill("结束日期", yesterday); }
    if (kind === "overtime") { await fill("开始时间", `${yesterday}T18:00`); await fill("结束时间", `${yesterday}T20:00`); await fill("加班地点", "总部办公室"); }
    if (kind === "travel") { await fill("出差地点", "上海"); await fill("出发日期", yesterday); await fill("返程日期", yesterday); }
    if (kind === "expense") { await fill("费用说明", "市内交通凭证"); await fill("金额", "12.35"); }
    if (kind === "purchase") { await fill("物品名称", "办公纸"); await fill("预算单价", "18.25"); }
    await click("保存草稿");
    await wait("!document.querySelector('[role=dialog]') && document.querySelector('.oa-detail')", `${name} save failed`);
    if (kind === "expense") {
      const fixture = path.join(output, "receipt.pdf"); fs.writeFileSync(fixture, "%PDF-1.7\n1 0 obj<</Type/Catalog>>endobj\n%%EOF\n");
      const { root } = await page.send("DOM.getDocument");
      const { nodeId } = await page.send("DOM.querySelector", { nodeId: root.nodeId, selector: "input[type=file]" });
      await page.send("DOM.setFileInputFiles", { nodeId, files: [fixture] });
      await wait("document.querySelector('.oa-attachments')?.innerText.includes('receipt.pdf')", "Receipt upload failed");
      const requestId = await run("new URLSearchParams(location.hash.split('?')[1]).get('requestId')");
      const login = await fetch(`${url}/api/auth/login`, { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ username: "oa-review", password: "Review-2026-Test" }) }).then(response => response.json());
      const headers = { authorization: `Bearer ${login.accessToken}` };
      const record = await fetch(`${url}/api/office/expense-requests/${requestId}`, { headers }).then(response => response.json());
      const attachmentUrl = `${url}/api/office/expense-requests/${requestId}/attachments/${record.attachments[0].id}`;
      assert.equal((await fetch(attachmentUrl)).status, 401, "Receipt download requires a live session");
      const downloaded = await fetch(attachmentUrl, { headers });
      assert.equal(downloaded.status, 200);
      assert(downloaded.headers.get("content-disposition").startsWith("attachment;"));
      assert.deepEqual(Buffer.from(await downloaded.arrayBuffer()), fs.readFileSync(fixture), "HTTP receipt download preserves bytes");
      const wrongParent = await fetch(`${url}/api/office/expense-requests/999999/attachments/${record.attachments[0].id}`, { headers });
      assert.equal(wrongParent.status, 404, "Attachment identifiers do not bypass the parent request");
    }
    await act("提交审批", "申请提交");
    await wait("document.querySelector('.oa-detail .office-badge')?.textContent==='待审批'", "Submission state missing");
    await act("登记批准结果", "同意办理");
    await wait("document.querySelector('.oa-detail .office-badge')?.textContent==='已批准'", "Approval state missing");
    const label = { leave: "销假归档", overtime: "确认加班完成", expense: "移交财务", travel: "返程归档", purchase: "登记验收", general: "办结登记" }[kind];
    await act(label, kind === "expense" ? "已移交独立财务软件，接收人王会计" : "已核对并完成办理");
    await run("document.querySelector('.oa-detail details').open=true");
    await wait("document.querySelector('.oa-history')?.innerText.includes('完成登记')", "Completion history missing");
    assert.deepEqual(await run("window.__oaErrors"), []);
    await captureScreenshot(page, path.join(output, `${kind}-desktop.png`));
    await page.send("Emulation.setDeviceMetricsOverride", { width: 390, height: 844, deviceScaleFactor: 1, mobile: true });
    assert(await run("document.documentElement.scrollWidth<=window.innerWidth+1"), `${kind}: no page overflow on phone`);
    await captureScreenshot(page, path.join(output, `${kind}-mobile.png`));
    await page.send("Emulation.clearDeviceMetricsOverride");
    console.log(`${name}: real React/Rust HTTP save, approval, completion, history and mobile layout passed.`);
  }
  console.log(`Office UI evidence: ${output}`);
} catch (error) {
  if (page) { await captureScreenshot(page, path.join(output, "failure.png")); fs.writeFileSync(path.join(output, "failure.txt"), await run("document.body.innerText")); }
  throw error;
} finally {
  cdp?.close();
  if (chrome) await closeChrome(chrome.browserWebSocketUrl, chrome.process);
  await stopProcessTree(server, 5000);
}
