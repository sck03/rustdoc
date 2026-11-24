import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
import { CdpClient, delay } from './lib/chromium-cdp.mjs';
import { captureScreenshot, evaluate, getFreePort } from './lib/web-runtime-browser-session.mjs';
import { spawnProcessTree, stopProcessTree } from './lib/child-process-tree.mjs';

// Targeted WebView2 regression; business workflow assertions live in the HTTP/React companion.
const root = path.resolve(import.meta.dirname, '..');
const app = path.resolve(process.argv[2]);
const out = path.join(root, 'artifacts/organization-desktop-ui', String(Date.now()));
fs.mkdirSync(out, { recursive: true });
const port = await getFreePort();
const child = spawnProcessTree(path.join(app, 'ExportDocManager.exe'), ['--app-root', app, '--data-root', path.join(out, 'Data')], {
  cwd: app, windowsHide: true, stdio: 'ignore', env: { ...process.env,
    EXPORTDOCMANAGER_DESKTOP_SMOKE: '1', EXPORTDOCMANAGER_DESKTOP_TOKEN: 'isolated-organization-test',
    WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: `--remote-debugging-address=127.0.0.1 --remote-debugging-port=${port}` },
});
let cdp;
const run = async expression => (await evaluate(cdp, expression, true)).value;
async function wait(expression) {
  const deadline = Date.now()+30000;
  while(Date.now()<deadline) { if(await run(expression)) return; await delay(100); }
  throw new Error('Desktop condition timed out: '+expression);
}
try {
  const deadline = Date.now()+60000;
  while(Date.now()<deadline) {
    assert.equal(child.exitCode,null,'Desktop host must remain alive');
    try { const pages=await (await fetch(`http://127.0.0.1:${port}/json/list`,{signal:AbortSignal.timeout(1000)})).json();
      const page=pages.find(page=>page.type==='page'&&/tauri\.localhost/u.test(page.url));
      if(page){cdp=await CdpClient.connect(page.webSocketDebuggerUrl);break;}
    } catch { /* Await the bounded WebView startup. */ }
    await delay(200);
  }
  assert(cdp,'Packaged WebView2 opened');
  await wait("!!document.querySelector('input[autocomplete=username]')");
  const context=await run("window.__TAURI_INTERNALS__.invoke('get_desktop_runtime_context')");
  const spec=JSON.parse(fs.readFileSync(path.join(root,'crates/export-doc-contracts/src/openapi.json'),'utf8'));
  const operations=new Map(Object.entries(spec.paths).flatMap(([route,methods])=>Object.entries(methods).map(([method,op])=>[op.operationId,{route,method}])));
  let token='';
  async function invoke(id,body,parameters={}) {
    const op=operations.get(id);assert(op,id);
    const route=op.route.replace(/\{([^}]+)\}/gu,(_,key)=>encodeURIComponent(parameters[key]));
    const response=await fetch(context.apiBaseUrl+route,{method:op.method.toUpperCase(),signal:AbortSignal.timeout(15000),
      headers:{'content-type':'application/json','X-ExportDocManager-Desktop-Token':context.desktopAccessToken,...(token?{authorization:`Bearer ${token}`}:{})},...(body?{body:JSON.stringify(body)}:{})});
    assert(response.ok,`${id}: ${response.status}`);
    return response.status===204?undefined:response.json();
  }
  token=(await invoke('Login',{username:'admin',password:''})).accessToken;
  await invoke('CreateOrganizationDepartment',{code:'UIA',name:'甲部门',companyCode:'DEFAULT',isActive:true});
  await invoke('CreateOrganizationDepartment',{code:'UIB',name:'乙部门',companyCode:'DEFAULT',isActive:true});
  await run(`(()=>{const input=document.querySelector('input[autocomplete=username]');Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(input,'admin');input.dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('.login-submit-button').click();})()`);
  await wait("!document.querySelector('.login-submit-button')&&!!document.querySelector('main')");
  await run("location.hash='/system/access-control'");
  await wait("!!document.querySelector('.user-management-section > .section-header .toolbar-actions')");
  assert(await run("(()=>{const rects=[...document.querySelector('.user-management-section > .section-header .toolbar-actions').children].map(e=>e.getBoundingClientRect());return Math.max(...rects.map(r=>r.top))-Math.min(...rects.map(r=>r.top))<5;})()"));
  await captureScreenshot(cdp,path.join(out,'account-toolbar.png'));
  await run("location.hash='/system/organization'");
  await wait("document.querySelectorAll('.organization-tree-table tbody tr').length===3");
  for(const width of [1440,1920,600]) {
    await cdp.send('Emulation.setDeviceMetricsOverride',{width,height:900,deviceScaleFactor:1,mobile:false});
    await delay(200);
    const bounds=await run("(()=>{const table=document.querySelector('.organization-tree-table');return {table:table.getBoundingClientRect().width,frame:table.parentElement.clientWidth,aligned:getComputedStyle(table.querySelector('.organization-actions-heading')).textAlign==='center',actions:[...document.querySelectorAll('.organization-row-actions')].every(e=>e.scrollWidth<=e.clientWidth+1)};})()");
    assert(bounds.aligned&&bounds.actions,JSON.stringify(bounds));
    if(width>680)assert(Math.abs(bounds.table-bounds.frame)<2,JSON.stringify(bounds));
    await captureScreenshot(cdp,path.join(out,`organization-${width}.png`));
  }
  const before=await run("[...document.querySelectorAll('.organization-tree-table tbody tr')].map(row=>row.dataset.departmentCode)");
  const moving=before.indexOf('UIB')>0?'UIB':'UIA';
  const index=before.indexOf(moving);assert(index>0);
  const expected=[...before];[expected[index-1],expected[index]]=[expected[index],expected[index-1]];
  await run(`document.querySelector('[data-department-code=${moving}] .organization-row-actions button').click()`);
  await wait(`JSON.stringify([...document.querySelectorAll('.organization-tree-table tbody tr')].map(row=>row.dataset.departmentCode))===${JSON.stringify(JSON.stringify(expected))}`);
  assert.equal(await run("document.querySelectorAll('[role=dialog]').length"),0);
  await run("location.hash='/office/approval-settings'");
  await wait("!!document.querySelector('tr[data-definition-key=general]')");
  await run("document.querySelector('tr[data-definition-key=general] button').click()");
  await wait("!!document.querySelector('section[data-definition-key=general]')");
  await run("[...document.querySelectorAll('section[data-definition-key=general] [role=tab]')].find(e=>e.textContent==='节点图').click()");
  await wait("document.querySelector('section[data-definition-key=general]').textContent.includes('负责人本人跳过，转公司领导审批')");
  await run("[...document.querySelectorAll('section[data-definition-key=general] button')].find(e=>e.textContent==='流程图').click()");
  await wait("document.querySelector('svg[aria-label=流程节点与依赖关系]')?.textContent.includes('负责人本人跳过 → 公司领导')");
  await captureScreenshot(cdp,path.join(out,'self-routing.png'));
  await run("location.hash='/invoices/new'");
  await wait("[...document.querySelectorAll('button')].some(e=>e.textContent.trim().startsWith('商品明细'))");
  await run("[...document.querySelectorAll('button')].find(e=>e.textContent.trim().startsWith('商品明细')).click()");
  await wait("!!document.querySelector('.item-actions-heading')");
  assert.equal(await run("getComputedStyle(document.querySelector('.item-actions-heading')).textAlign"),'center');
  fs.writeFileSync(path.join(out,'result.json'),JSON.stringify({edition:context.productEdition,layout:[1440,1920,600],toolbar:true,directOrdering:true,selfRouting:true},null,2));
  console.log('Targeted Tauri/WebView2 verification passed: '+out);
} finally {
  if(cdp)cdp.close();
  await stopProcessTree(child);
}
