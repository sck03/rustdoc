import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import http from "node:http";
import { createRequire } from "node:module";
import { CdpClient, closeChrome, delay } from "./lib/chromium-cdp.mjs";
import { locateChromeForTesting } from "./lib/report-regression-common.mjs";
import { startChrome, createPageSession, evaluate, captureScreenshot } from "./lib/web-runtime-browser-session.mjs";

const repo = path.resolve(import.meta.dirname, "..");
const web = path.join(repo, "apps/export-doc-web");
const output = path.join(repo, "artifacts/workspace-navigation-ui");
const require = createRequire(path.join(web, "package.json"));
fs.mkdirSync(output, { recursive: true });
const axe = fs.readFileSync(require.resolve("axe-core/axe.min.js"), "utf8");
await require("esbuild").build({ stdin: { resolveDir: web, loader: "tsx", contents: `
  import React from 'react';
  import {createRoot} from 'react-dom/client';
  import {MemoryRouter,useLocation} from 'react-router-dom';
  import {QueryClient,QueryClientProvider} from '@tanstack/react-query';
  import {WorkspaceShell} from './src/app/WorkspaceShell.tsx';
  import {workspaceNavGroups} from './src/app/workspaceNavigation.ts';
  import {getDefaultWorkspaceRoute} from './src/app/productEdition.ts';
  import {isRouteAccessAllowed} from './src/app/routeAccess.ts';
  import {PermissionAccessProvider} from './src/app/PermissionAccessContext.tsx';
  import {ConfirmationProvider} from './src/ui/ConfirmationProvider.tsx';
  import {JobCenterPage} from './src/features/jobs/JobCenterPage.tsx';
  import './src/styles.css'; import './src/theme.css'; import './src/responsiveOverrides.css';
  const params=new URLSearchParams(location.search), edition=params.get('edition')||'Full', desktop=params.get('runtime')!=='browser';
  const workspaces=edition==='Full'?['document','sales','office']:edition==='Administration'?['office']:edition==='Sales'?['sales']:['document'];
  if(!desktop&&!workspaces.includes('office'))workspaces.push('office');
  const items=workspaceNavGroups.flatMap(group=>group.items).filter(item=>(!item.workspace||workspaces.includes(item.workspace))&&!(edition==='Administration'&&item.moduleKey?.startsWith('common.')));
  const modules=[...new Set(items.flatMap(item=>item.moduleKey?[item.moduleKey]:[]))];
  const permissions=items.flatMap(item=>item.requiredPermissions||[]).map(item=>({...item,dataScope:'all'}));
  permissions.push({resourceKey:'document.invoice-output',action:'export-zip',dataScope:'all'});
  const capabilities={productEdition:edition,canManageSettings:true,canManageUsers:edition==='Full'||edition==='Administration',
    canUseDocumentWorkspace:workspaces.includes('document'),canUseSalesWorkspace:workspaces.includes('sales'),
    usesOfficeRegister:desktop&&workspaces.includes('office'),isDesktopRuntime:desktop,
    enabledModules:modules,moduleAccess:modules.map(moduleKey=>({moduleKey,accessLevel:'manage'})),permissions,availableFeatures:['worklist',...(workspaces.includes('document')?['business-attachments']:[])]};
  if(params.has('denied')){capabilities.enabledModules=[];capabilities.moduleAccess=[];capabilities.permissions=[];capabilities.canManageSettings=false;}
  const user={id:1,username:'demo',fullName:'示例操作员',role:'Admin',isActive:true,companyScope:'DEMO',departmentId:'OFFICE',businessDate:'2026-09-09',capabilities};
  const invoices=Array.from({length:41},(_,index)=>({id:index+1,invoiceNo:'INV'+String(index+1).padStart(3,'0'),customerName:'示例客户 '+(index+1)}));
  window.__requests=[];window.__errors=[];window.addEventListener('error',event=>window.__errors.push(event.message));
  const paged=items=>({items,pageNumber:1,pageSize:20,totalCount:items.length,totalPages:1});
  const client={listJobs:async()=>paged([]),getSettings:async()=>({settings:{}}),
    listReportTemplates:async()=>[{templatePath:'invoice_template.html',displayName:'出口发票',withSealDefault:true}],
    listInvoices:async input=>{window.__requests.push({kind:'search',keyword:input.keyword});return paged(invoices.filter(invoice=>(invoice.invoiceNo+' '+invoice.customerName).includes(input.keyword||'')).slice(0,20))},
    startInvoiceReportPdfZipDownloadJob:async input=>{window.__requests.push({kind:'zip',body:input.body});throw new Error('模拟依赖不可用，请稍后重试。')},
  };
  const queries=new QueryClient({defaultOptions:{queries:{retry:false},mutations:{retry:false}}});
  function Workspace(){const {pathname}=useLocation();return <WorkspaceShell pathname={pathname} apiBaseUrl='/fixture' isDesktopRuntime={desktop} user={user}
    onLogout={()=>{}} connectivityOverride='online' serviceAvailabilityOverride='available'>
    {!isRouteAccessAllowed({pathname,user,canManageSystem:capabilities.canManageSettings,isDesktopRuntime:desktop})?<p role='alert'>当前页面无权限</p>:
      pathname==='/jobs'?<JobCenterPage client={client}/>:<section className='work-surface'><p>从左侧选择业务功能。</p></section>}
    </WorkspaceShell>}
  createRoot(document.getElementById('root')).render(<MemoryRouter initialEntries={[params.get('path')||getDefaultWorkspaceRoute(capabilities)]}>
    <QueryClientProvider client={queries}><PermissionAccessProvider grants={capabilities.moduleAccess} permissions={capabilities.permissions} canManageSettings={capabilities.canManageSettings}>
      <ConfirmationProvider><Workspace/></ConfirmationProvider></PermissionAccessProvider></QueryClientProvider></MemoryRouter>);
` }, outfile: path.join(output, "app.js"), bundle: true, format: "esm", platform: "browser", jsx: "automatic", logLevel: "silent" });
const server = http.createServer((request, response) => {
  const name = new URL(request.url, "http://localhost").pathname.slice(1);
  if (!name) { response.setHeader("Content-Type", "text/html; charset=utf-8"); response.end('<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>导航交互验证</title><link rel="stylesheet" href="/app.css"><div id="root"></div><script type="module" src="/app.js"></script></html>'); return; }
  if (!["app.js", "app.css"].includes(name)) { response.writeHead(404).end(); return; }
  response.setHeader("Content-Type", name.endsWith("js") ? "text/javascript" : "text/css"); response.end(fs.readFileSync(path.join(output, name)));
});
await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
let chrome, cdp, page;
const results = [];
const read = async expression => (await evaluate(page, expression, true)).value;
async function waitFor(expression) {
  const deadline = Date.now() + 20000;
  while (Date.now() < deadline) { if (await read(`Boolean(${expression})`)) return; await delay(60); }
  throw new Error(`Timed out: ${expression}; ${await read("document.body.innerText.slice(0,1800)")}`);
}
async function click(selector) { await read(`document.querySelector(${JSON.stringify(selector)}).click()`); await delay(80); }
async function input(selector, value) {
  await read(`(()=>{const node=document.querySelector(${JSON.stringify(selector)});node.focus();Object.getOwnPropertyDescriptor(node instanceof HTMLSelectElement?HTMLSelectElement.prototype:HTMLInputElement.prototype,'value').set.call(node,${JSON.stringify(value)});node.dispatchEvent(new Event('input',{bubbles:true}));node.dispatchEvent(new Event('change',{bubbles:true}))})()`);
  await delay(80);
}
async function key(key, modifiers = 0) {
  const codes = { Enter: 13, Escape: 27, Tab: 9 };
  await page.send("Input.dispatchKeyEvent", { type: "keyDown", key, code: key, windowsVirtualKeyCode: codes[key], modifiers, ...(key === "Enter" ? { text: "\r" } : {}) });
  await page.send("Input.dispatchKeyEvent", { type: "keyUp", key, code: key, windowsVirtualKeyCode: codes[key], modifiers });
  await delay(80);
}
async function open(width, search = "") {
  await page.send("Emulation.setDeviceMetricsOverride", { width, height: 900, deviceScaleFactor: 1, mobile: false });
  await page.send("Page.navigate", { url: `http://127.0.0.1:${server.address().port}/?${search}` });
  await waitFor("document.querySelector('.workspace-header h1')"); await delay(150);
}
async function audit(label) {
  await evaluate(page, axe, false);
  const violations = await read("axe.run(document,{runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21aa']}}).then(r=>r.violations.map(v=>({id:v.id,nodes:v.nodes.map(n=>n.target)})))");
  assert.deepEqual(violations, [], `${label}: accessibility`);
  assert(await read("document.documentElement.scrollWidth <= innerWidth+1"), `${label}: horizontal overflow`);
  assert.deepEqual(await read("window.__errors"), [], `${label}: browser errors`);
  await captureScreenshot(page, path.join(output, `${label}.png`)); results.push(label);
}
try {
  chrome = await startChrome({ browserExecutable: locateChromeForTesting(repo, "headless-shell"), userDataDir: path.join(repo, ".codex-runtime/workspace-navigation-ui-chrome"), timeoutMs: 30000 });
  cdp = await CdpClient.connect(chrome.browserWebSocketUrl); page = await createPageSession(cdp);
  await open(1366);
  assert.equal(await read("document.querySelector('.workspace-header h1').textContent"), "单证概览");
  assert.equal(await read("document.querySelectorAll('.nav-group-button[aria-expanded=true]').length"), 1);
  assert.equal(await read("document.querySelectorAll('.nav-group-button').length"), 6); await audit("full-desktop");
  await click('[data-nav-group="office"]'); await click('.nav-item[href="/office/people"]');
  await waitFor("document.activeElement===document.querySelector('.workspace-header h1')");
  assert.equal(await read("document.querySelector('.workspace-header h1').textContent"), "人员档案"); await audit("full-administration");
  await input('.nav-search input', "任务中心"); assert.equal(await read("document.querySelectorAll('.nav-item').length"), 1);
  await key("Enter"); assert(await read("document.activeElement.classList.contains('nav-item')"), await read("document.activeElement.outerHTML"));
  await key("Enter"); await waitFor("document.querySelector('.job-table')");
  assert.equal(await read("document.querySelector('.nav-search input').value"), ""); results.push("search-keyboard-navigation");
  await open(1024); assert.equal(await read("document.querySelectorAll('.nav-rail-item').length"), 7); await audit("compact-groups");
  await click('.nav-rail-item[aria-label="展开公司行政"]');
  assert(await read("document.activeElement.dataset.navGroup==='office'"));
  assert.equal(await read("document.querySelectorAll('.nav-group-button[aria-expanded=true]').length"), 1); results.push("compact-expand-focus");
  for (const width of [390, 320]) {
    await open(width); await click('.mobile-nav-toggle'); assert(await read("document.querySelector('main').inert"));
    await input('.nav-search input', "行政"); await audit(`mobile-search-${width}`);
    await key("Escape"); assert(await read("document.querySelector('.workspace-nav-mobile-open') && document.querySelector('.nav-search input').value===''") );
    await read("document.querySelector('.mobile-nav-toggle').focus()"); await key("Tab", 8);
    assert(await read("document.activeElement.closest('nav')!==null"), "reverse tab wraps to visible navigation");
    await key("Tab"); assert(await read("document.activeElement.classList.contains('mobile-nav-toggle')"));
    await key("Escape"); assert(await read("!document.querySelector('main').inert && document.activeElement.classList.contains('mobile-nav-toggle')"));
    await click('.mobile-nav-toggle'); await click('[data-nav-group="office"]'); await click('.nav-item[href="/office/supplies"]');
    assert(await read("!document.querySelector('.workspace-nav-mobile-open') && document.querySelector('.workspace-header h1').textContent==='物品领用'"));
    await click('.mobile-nav-toggle'); await click('[data-nav-group="office"]'); await key("Escape"); await click('.mobile-nav-toggle');
    assert(await read("document.activeElement===document.querySelector('.nav-search input')"), "reopening a collapsed current group focuses the visible search field");
    results.push(`mobile-focus-and-close-${width}`);
  }
  await open(1366, "edition=Sales"); await input('.nav-search input', "行政");
  assert.equal(await read("document.querySelectorAll('.nav-item').length"), 0); results.push("specialist-search-boundary");
  await open(1366, "edition=Sales&path=/office/people"); assert(await read("document.body.innerText.includes('当前页面无权限')"));
  await open(1366, "edition=Administration"); assert.equal(await read("document.querySelector('.workspace-header h1').textContent"), "人员档案");
  await open(1366, "path=/office/people&denied=1"); assert(await read("document.body.innerText.includes('当前页面无权限')")); results.push("direct-route-boundaries");
  await open(1366, "runtime=browser&path=/jobs"); await waitFor("document.querySelector('.job-table')");
  assert.equal(await read("window.__requests.filter(request=>request.kind==='search').length"), 0, "viewing progress does not fetch invoices until the creation panel is opened");
  await click('.job-create-panel summary'); await waitFor("document.querySelector('.job-invoice-selection select')?.options.length>1");
  const select = '.job-invoice-selection select';
  await input(select, "7"); await input(select, "7"); assert.equal(await read("document.querySelectorAll('.job-invoice-selection-list li').length"), 1);
  await input('.job-invoice-selection input', "INV041"); await waitFor("document.querySelector('.job-invoice-selection option[value=\"41\"]')");
  await input(select, "41"); assert.equal(await read("document.querySelectorAll('.job-invoice-selection-list li').length"), 2);
  await click('[aria-label="移除发票 INV007"]'); await audit("batch-report-selection");
  await click('[aria-label="批量报表 ZIP 任务"] button[type=submit]'); await waitFor("window.__requests.some(request=>request.kind==='zip')");
  assert.deepEqual(await read("window.__requests.find(request=>request.kind==='zip').body.invoiceIds"), [41]);
  await waitFor("document.body.innerText.includes('模拟依赖不可用')"); results.push("batch-selection-submit-and-error");
  fs.writeFileSync(path.join(output, "summary.json"), JSON.stringify({ passed: results.length, results }, null, 2));
  process.stdout.write(`Workspace navigation and report selection UI passed (${results.length} cases).\n`);
} finally { cdp?.close(); if (chrome) await closeChrome(chrome.browserWebSocketUrl, chrome.process); await new Promise(resolve => server.close(resolve)); }
