import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";
import { cargoExampleExecutable } from './lib/cargo-paths.mjs';
import { CdpClient, closeChrome, delay } from "./lib/chromium-cdp.mjs";
import { locateChromeForTesting } from "./lib/report-regression-common.mjs";
import { startChrome, createPageSession, evaluate, captureScreenshot } from "./lib/web-runtime-browser-session.mjs";
import { spawnProcessTree, stopProcessTree } from "./lib/child-process-tree.mjs";
import { exerciseOfficeResources } from "./lib/office-resource-ui-scenarios.mjs";

const repo = path.resolve(import.meta.dirname, "..");
const output = path.join(repo, "artifacts/oa-native-ui", Date.now().toString());
fs.mkdirSync(output, { recursive: true });
const executable = cargoExampleExecutable(repo, 'office_review');
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
  await run(`(() => {const label=[...document.querySelectorAll('label')].find(e=>(e.querySelector('span')?.textContent ?? e.textContent)===${JSON.stringify(label)}); const input=label?.control ?? label?.querySelector('input,textarea,select'); if(!input) throw new Error('Missing field: '+${JSON.stringify(label)}); Object.getOwnPropertyDescriptor(Object.getPrototypeOf(input),'value').set.call(input,${JSON.stringify(value)}); input.dispatchEvent(new Event('input',{bubbles:true})); input.dispatchEvent(new Event('change',{bubbles:true}));})()`);
}
async function navigate(url) { await page.send("Page.navigate", { url }); await wait("document.readyState === 'complete'", "Page load timed out"); }
async function chooseFiles(selector, files) {
  const { root } = await page.send("DOM.getDocument");
  const { nodeId } = await page.send("DOM.querySelector", { nodeId: root.nodeId, selector });
  assert(nodeId, `Missing file picker ${selector}`);
  await page.send("DOM.setFileInputFiles", { nodeId, files });
}
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
  await page.send("Page.addScriptToEvaluateOnNewDocument", { source: `window.__oaErrors=[];addEventListener('error',e=>window.__oaErrors.push(e.message));addEventListener('unhandledrejection',e=>window.__oaErrors.push(String(e.reason)));
    const originalFetch=window.fetch.bind(window);window.fetch=async(...args)=>{const counts=window.__saveCounts;if(!counts)return originalFetch(...args);const url=String(args[0] instanceof Request ? args[0].url : args[0]);const method=args[1]?.method;if(method==='POST' && /expense-requests$/.test(url))counts.create++;const upload=method==='POST' && /expense-requests\\/[^/]+\\/attachments$/.test(url);if(upload)counts.upload++;const response=await originalFetch(...args);if(upload && counts.upload===2)throw new TypeError('Simulated lost upload response');return response;};` });
  await navigate(url);
  await wait("document.querySelector('input[autocomplete=username]')", "Login not displayed");
  await run(`(() => {for(const [selector,value] of [['input[autocomplete=username]','oa-review'],['input[autocomplete=current-password]','Review-2026-Test']]){const input=document.querySelector(selector);Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(input,value);input.dispatchEvent(new Event('input',{bubbles:true}));}})()`);
  await click("登录");
  await wait("!document.querySelector('.login-submit-button')", "Login failed");
  await exerciseOfficeResources({ url, page, output, run, wait, click, fill, navigate, captureScreenshot });
  await navigate(`${url}/#/office/approvals`);
  await wait("document.querySelector('[aria-label=\"申请与审批中心\"]')", "Approval hub missing");
  assert(await run("document.body.innerText.includes('人事管理') && document.body.innerText.includes('行政办公')"), "Separated navigation groups are visible");
  await captureScreenshot(page, path.join(output, "approval-center.png"));
  const receipt = path.join(output, "receipt.pdf");
  const supporting = path.join(output, "supporting.pdf");
  fs.writeFileSync(receipt, "%PDF-1.7\n1 0 obj<</Type/Catalog>>endobj\n%%EOF\n");
  fs.writeFileSync(supporting, "%PDF-1.7\n2 0 obj<</Type/Catalog>>endobj\n%%EOF\n");
  await navigate(`${url}/#/office/people`);
  await wait("document.querySelector('.personnel-directory')", "Personnel page missing");
  await click("入职登记");
  await fill("工号（系统内唯一）", "UI-ATTACH-001"); await fill("姓名", "一次保存人员"); await fill("岗位", "业务专员");
  await chooseFiles('[aria-label="选择档案图片或文档"] input', [receipt]);
  await run(`new Promise(resolve => {const canvas=document.createElement('canvas'); canvas.width=40;canvas.height=40;const c=canvas.getContext('2d');c.fillStyle='#147d92';c.fillRect(0,0,40,40);canvas.toBlob(blob=>{const transfer=new DataTransfer();transfer.items.add(new File([blob],'avatar.png',{type:'image/png'}));const input=document.querySelector('[aria-label="选择人员头像"] input');input.files=transfer.files;input.dispatchEvent(new Event('change',{bubbles:true}));resolve(true);},'image/png');})`);
  await wait("document.querySelector('[role=dialog] img')?.naturalWidth>0", "Draft image preview missing");
  await run("document.querySelector('[aria-label=\"选择档案图片或文档\"]').scrollIntoView({block:'end'})");
  await captureScreenshot(page, path.join(output, "personnel-before-save.png"));
  await click("登记入职");
  await wait("!document.querySelector('[role=dialog]') && document.querySelector('.personnel-facts')", "One-click personnel save failed");
  await captureScreenshot(page, path.join(output, "personnel-profile.png"));
  assert.equal(await run("getComputedStyle(document.querySelector('.personnel-fact-section')).backgroundColor"), "rgb(255, 255, 255)");
  await click("照片与证件");
  await wait("document.querySelector('[aria-label=档案文档]')?.innerText.includes('receipt.pdf') && document.querySelector('.personnel-image-preview img')?.naturalWidth>0", "Personnel image or document was not saved");
  await captureScreenshot(page, path.join(output, "personnel-saved-files.png"));
  const yesterday = new Date(Date.now() - 86400000).toISOString().slice(0, 10);
  const travelDay = new Date(Date.now() - 2 * 86400000).toISOString().slice(0, 10);
  for (const [kind, name] of [["leave", "员工请假"], ["overtime", "加班申请"], ["expense", "费用报销"], ["travel", "出差申请"], ["purchase", "采购申请"], ["general", "通用申请"]]) {
    await navigate(`${url}/#/office/requests/${kind}`);
    await wait(`document.querySelector('.oa-workspace')?.getAttribute('aria-label')===${JSON.stringify(name)}`, `${name} page missing`);
    assert(await run(`(() => {const items=[...document.querySelectorAll('.oa-toolbar > label, .oa-toolbar > button')];const centers=items.map(e=>{const r=e.getBoundingClientRect();return r.top+r.height/2;});return centers.length>=4 && Math.max(...centers)-Math.min(...centers)<2;})()`), `${name}: toolbar controls share one baseline`);
    await captureScreenshot(page, path.join(output, `${kind}-toolbar.png`));
    await click(`新建${name}`);
    await fill("登记人员", "UI-ATTACH-001");
    await wait("document.querySelector('[role=option]')?.textContent.includes('一次保存人员') && document.querySelectorAll('[role=option]').length===1", "Employee number search failed");
    if (kind === 'leave') {
      const require = createRequire(path.join(repo, 'apps/export-doc-web/package.json'));
      await page.send('Runtime.evaluate', { expression: fs.readFileSync(require.resolve('axe-core/axe.min.js'), 'utf8') });
      assert.deepEqual(await run("window.axe.run(document.querySelector('[role=dialog]')).then(r=>r.violations.filter(v=>['critical','serious'].includes(v.impact)).map(v=>({id:v.id,nodes:v.nodes.map(n=>n.target)})))"), [], 'Employee combobox accessibility');
    }
    await captureScreenshot(page, path.join(output, `${kind}-employee-search.png`));
    await run("document.querySelector('[role=option]').click()");
    await fill("登记人员", "不存在的员工");
    await wait("document.querySelector('.employee-picker-status')?.textContent.includes('没有匹配')", "Empty search feedback missing");
    assert.equal(await run("document.querySelectorAll('[role=option]').length"), 0, "Selected employee must not leak into unmatched results");
    await fill("登记人员", "一次保存");
    await wait("document.querySelector('[role=option]')?.textContent.includes('UI-ATTACH-001')", "Employee name search failed");
    await run("document.querySelector('.employee-picker input').focus()");
    for (const key of ['ArrowUp', 'Enter']) {
      await page.send('Input.dispatchKeyEvent', { type: 'keyDown', key, code: key, windowsVirtualKeyCode: key === 'Enter' ? 13 : 38 });
      await page.send('Input.dispatchKeyEvent', { type: 'keyUp', key, code: key, windowsVirtualKeyCode: key === 'Enter' ? 13 : 38 });
    }
    await wait("!document.querySelector('[role=listbox]') && document.querySelector('.employee-picker input').value.includes('UI-ATTACH-001')", "Keyboard employee selection failed");
    await fill("申请标题", `${name}真实界面验收`); await fill("申请说明", "验证中文输入、保存、附件和审批历史。");
    if (kind === "leave") { await fill("开始日期", yesterday); await fill("结束日期", yesterday); }
    if (kind === "overtime") { await fill("开始时间", `${yesterday}T18:00`); await fill("结束时间", `${yesterday}T20:00`); await fill("加班地点", "总部办公室"); }
    if (kind === "travel") { await fill("出差地点", "上海"); await fill("出发日期", travelDay); await fill("返程日期", travelDay); }
    if (kind === "expense") { await fill("费用说明", "市内交通凭证"); await fill("金额", "12.35"); }
    if (kind === "purchase") { await fill("物品名称", "办公纸"); await fill("预算单价", "18.25"); }
    await chooseFiles('[role=dialog] input[type=file]', kind === "expense" ? [receipt, supporting] : [receipt]);
    if (kind === "expense") await run("window.__saveCounts={create:0,upload:0}");
    await run("document.querySelector('[role=dialog] input[type=file]').scrollIntoView({block:'center'})");
    await captureScreenshot(page, path.join(output, `${kind}-before-save.png`));
    await click("保存草稿");
    if (kind === "expense") {
      await wait("document.querySelector('[role=dialog] [role=status]') && document.querySelector('[role=dialog] button[type=submit]')?.disabled===false", "Partial save did not retain draft");
      assert.equal(await run("document.querySelectorAll('[role=dialog] li').length"), 1, "Only unacknowledged file remains queued");
      await click("保存草稿");
    }
    await wait("!document.querySelector('[role=dialog]') && document.querySelector('.oa-detail')", `${name} save failed`);
    if (kind === "expense") assert.deepEqual(await run("window.__saveCounts"), { create: 1, upload: 3 }, "Retry does not recreate the request or resend successful files");
    assert.equal(await run("document.querySelectorAll('.oa-attachments li').length"), kind === "expense" ? 2 : 1, "Attachments are included in the first save");
    const savedId = await run("new URLSearchParams(location.hash.split('?')[1]).get('requestId')");
    await captureScreenshot(page, path.join(output, `${kind}-draft.png`));
    await navigate(`${url}/#/office/approvals`);
    await wait("document.querySelector('[aria-label=\"申请与审批中心\"]')", "Navigation away failed");
    await navigate(`${url}/#/office/requests/${kind}`);
    await wait(`document.querySelector('.office-resource-grid')?.innerText.includes(${JSON.stringify(`${name}真实界面验收`)})`, `${name}: saved draft missing after navigation`);
    assert.equal(await run("document.querySelector('.oa-toolbar select').value"), "", "Default list includes drafts for approvers");
    await click(`${name}真实界面验收`);
    await wait("document.querySelector('.oa-detail .office-badge')?.textContent==='草稿'", "Draft did not reopen");
    assert.equal(await run("new URLSearchParams(location.hash.split('?')[1]).get('requestId')"), savedId);
    assert.equal(await run("getComputedStyle(document.querySelector('.oa-detail-content')).backgroundColor"), "rgb(255, 255, 255)");
    await click("编辑草稿");
    await wait("document.querySelector('[role=dialog] textarea')", "Draft editor missing");
    await fill("申请说明", "切换导航后继续编辑，保存内容仍然存在。");
    await click("保存草稿");
    await wait("!document.querySelector('[role=dialog]') && document.querySelector('.oa-reason')?.innerText.includes('切换导航后继续编辑')", "Draft edit was not persisted");
    if (kind === "expense") {
      await wait("document.querySelector('.oa-attachments')?.innerText.includes('receipt.pdf')", "Receipt upload failed");
      const requestId = await run("new URLSearchParams(location.hash.split('?')[1]).get('requestId')");
      const login = await fetch(`${url}/api/auth/login`, { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ username: "oa-review", password: "Review-2026-Test" }) }).then(response => response.json());
      const headers = { authorization: `Bearer ${login.accessToken}` };
      const record = await fetch(`${url}/api/office/expense-requests/${requestId}`, { headers }).then(response => response.json());
      const attachmentUrl = `${url}/api/office/expense-requests/${requestId}/attachments/${record.attachments.find(file => file.fileName === 'receipt.pdf').id}`;
      assert.equal((await fetch(attachmentUrl)).status, 401, "Receipt download requires a live session");
      const downloaded = await fetch(attachmentUrl, { headers });
      assert.equal(downloaded.status, 200);
      assert(downloaded.headers.get("content-disposition").startsWith("attachment;"));
      assert.deepEqual(Buffer.from(await downloaded.arrayBuffer()), fs.readFileSync(receipt), "HTTP receipt download preserves bytes");
      const wrongParent = await fetch(`${url}/api/office/expense-requests/999999/attachments/${record.attachments[0].id}`, { headers });
      assert.equal(wrongParent.status, 404, "Attachment identifiers do not bypass the parent request");
    }
    await act("提交审批", "申请提交");
    await wait("document.querySelector('.oa-detail .office-badge')?.textContent==='待审批'", "Submission state missing");
    await act("登记批准结果", "同意办理");
    await wait("document.querySelector('.oa-detail .office-badge')?.textContent==='已批准'", "Approval state missing");
    const label = { leave: "销假归档", overtime: "确认加班完成", expense: "财务接收", travel: "返程归档", purchase: "登记验收", general: "办结登记" }[kind];
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
