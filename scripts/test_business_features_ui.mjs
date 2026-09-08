import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import http from "node:http";
import { createRequire } from "node:module";
import { CdpClient, closeChrome, delay } from "./lib/chromium-cdp.mjs";
import { locateChromeForTesting } from "./lib/report-regression-common.mjs";
import { startChrome, createPageSession, evaluate, captureScreenshot } from "./lib/web-runtime-browser-session.mjs";
import { attachPdfViewerSession } from "./lib/pdf-viewer-page-capture.mjs";

const repo = path.resolve(import.meta.dirname, "..");
const web = path.join(repo, "apps/export-doc-web");
const output = path.join(repo, "artifacts/business-features-ui");
const require = createRequire(path.join(web, "package.json"));
fs.mkdirSync(output, { recursive: true });
const source = name => JSON.stringify(path.join(web, "src", name).replaceAll("\\", "/"));
const pdfObjects = ["<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
  "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
  "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"];
const pdfText = "BT /F1 24 Tf 72 720 Td (ARCHIVED ORIGINAL PDF) Tj ET";
pdfObjects.push(`<< /Length ${pdfText.length} >>\nstream\n${pdfText}\nendstream`);
let pdf = "%PDF-1.4\n";
const offsets = [0];
pdfObjects.forEach((object, index) => { offsets.push(pdf.length); pdf += `${index + 1} 0 obj\n${object}\nendobj\n`; });
const xref = pdf.length;
pdf += `xref\n0 ${offsets.length}\n0000000000 65535 f \n${offsets.slice(1).map(offset => String(offset).padStart(10, "0") + " 00000 n \n").join("")}trailer\n<< /Size ${offsets.length} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
const pdfBase64 = Buffer.from(pdf).toString("base64");
await require("esbuild").build({ stdin: { loader: "tsx", resolveDir: web, contents: `
  import React from 'react';
  import { createRoot } from 'react-dom/client';
  import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
  import { MemoryRouter, Routes, Route, useLocation } from 'react-router-dom';
  import { ConfirmationProvider } from ${source("ui/ConfirmationProvider.tsx")};
  import { UnsavedChangesProvider } from ${source("ui/unsavedChangesGuard.tsx")};
  import { BusinessAttachmentsPage } from ${source("features/attachments/BusinessAttachmentsPage.tsx")};
  import { WorklistPage } from ${source("features/worklist/WorklistPage.tsx")};
  import { InvoiceReviewPanel } from ${source("features/invoices/InvoiceReviewPanel.tsx")};
  import { createEmptyInvoice } from ${source("features/invoices/invoiceModel.ts")};
  import ${source("styles/cascade.css")}; import ${source("styles/foundation.css")};
  import ${source("styles/workspaces.css")}; import ${source("styles/responsive.css")};
  const params=new URLSearchParams(location.search), mode=params.get('mode')||'attachments';
  const readonly=params.get('role')==='reader';
  window.__calls=[]; window.__errors=[]; window.__createdUrls=[]; window.__revokedUrls=[];
  addEventListener('error',event=>window.__errors.push(event.message));
  addEventListener('unhandledrejection',event=>window.__errors.push(String(event.reason)));
  const create=URL.createObjectURL.bind(URL), revoke=URL.revokeObjectURL.bind(URL);
  URL.createObjectURL=blob=>{const url=create(blob);window.__createdUrls.push(url);return url;};
  URL.revokeObjectURL=url=>{window.__revokedUrls.push(url);revoke(url);};
  const user={id:7,username:'user',fullName:'示例用户',companyScope:'C1',businessTimeZone:'Asia/Shanghai',businessDate:'2026-09-08',capabilities:{}};
  let attachment={id:11,invoiceId:1,invoiceNo:'INV-2026-091',invoiceType:'实际数据',customerName:'SAMPLE CUSTOMER',title:'客户确认图纸',category:'Confirmation',poNumber:'PO-2026',styleNo:'NEW-STYLE',latestRevision:2,currentRevision:1,isArchived:false,versionNumber:2,updatedAt:'2026-09-08T04:00:00Z',canEdit:!readonly};
  let versions=[2,1].map(revision=>({revision,fileName:'客户图纸-v'+revision+'.png',contentType:'image/png',length:2048,sha256:'0'.repeat(64),uploadedBy:'示例用户',note:revision===2?'修改领口与包装要求':'客户原始确认',createdAt:'2026-09-08T04:00:00Z'}));
  if(mode==='pdf') versions[0]={...versions[0],fileName:'正式文件.pdf',contentType:'application/pdf'};
  let events=[];
  const page=(items,request={})=>({items,totalCount:items.length,pageNumber:request.pageNumber||1,pageSize:request.pageSize||20,totalPages:items.length?1:0,hasPreviousPage:false,hasNextPage:false});
  const record=(name,input,result)=>{window.__calls.push({name,input});return Promise.resolve(result);};
  const client={
    getInvoice:async()=>({...createEmptyInvoice('2026-09-08'),id:1,invoiceNo:attachment.invoiceNo}),
    listBusinessAttachments:input=>record('listAttachments',input,{page:page(!attachment.isArchived||input.includeArchived?[{...attachment}]:[],input),canUpload:!readonly,usedBytes:4096,fileBytesLimit:16777216,invoiceBytesLimit:268435456}),
    getBusinessAttachment:input=>record('getAttachment',input,{attachment:{...attachment},revisions:[...versions],events:[...events],eventCount:events.length}),
    uploadBusinessAttachment:async input=>{
      const fields=Object.fromEntries(input.body.entries()); window.__calls.push({name:'upload',input:{invoiceId:input.invoiceId,fields:{...fields,file:fields.file.name}}});
      attachment={...attachment,latestRevision:attachment.latestRevision+1,versionNumber:attachment.versionNumber+1};
      versions=[{...versions[0],revision:attachment.latestRevision,fileName:fields.file.name,note:fields.note},...versions];return {...attachment};
    },
    updateBusinessAttachment:input=>{attachment={...attachment,...input.body,versionNumber:attachment.versionNumber+1};events.unshift({action:input.body.isArchived?'Archive':'Confirm',revision:input.body.currentRevision,actorName:'示例用户',note:input.body.note,createdAt:'2026-09-08T05:00:00Z'});return record('updateAttachment',input,{...attachment});},
    downloadBusinessAttachment:async input=>{window.__calls.push({name:'download',input});if(mode==='pdf')return new Blob([Uint8Array.from(atob(${JSON.stringify(pdfBase64)}),letter=>letter.charCodeAt(0))],{type:'application/pdf'});const canvas=document.createElement('canvas');canvas.width=600;canvas.height=240;const ctx=canvas.getContext('2d');ctx.fillStyle='#eeeeee';ctx.fillRect(0,0,600,240);ctx.fillStyle='#334455';ctx.font='30px sans-serif';ctx.fillText('CUSTOMER DRAWING v'+input.revision,30,120);return new Promise(resolve=>canvas.toBlob(resolve,'image/png'));},
    reviewInvoice:input=>record('review',input,{ready:false,issues:[{field:'quantity',rowNumber:3,message:'数量必须大于 0。'}]}),
    getWorklist:input=>{
      if(mode==='failure') return Promise.reject(new Error('业务库暂时不可用'));
      const items=[{source:'invoice-review',recordId:1,title:'INV-2026-091',description:'实际数据 · SAMPLE CUSTOMER',dueDate:null,dueAt:null,isOverdue:false},
        {source:'customer-follow-up',recordId:79,title:'客户确认交期',description:'确认新款样衣与包装要求',dueDate:null,dueAt:'2026-09-07T04:00:00Z',isOverdue:true},
        {source:'contract-end',recordId:5,title:'李明',description:'EMP-005 · 劳动合同',dueDate:'2026-09-08',dueAt:null,isOverdue:false}];
      const selected=items.filter(item=>(!input.source||item.source===input.source)&&(input.due!=='Overdue'||item.isOverdue));
      return record('worklist',input,{page:page(selected,input),sources:[{key:'invoice-review',name:'单据待核对',count:1},{key:'customer-follow-up',name:'客户跟进',count:1},{key:'contract-end',name:'劳动合同到期',count:1}],businessDate:'2026-09-08',asOf:'2026-09-08T04:00:00Z'});
    }
  };
  function Destination(){const current=useLocation();window.__destination=current.pathname+current.search;return <p>原业务处理页面</p>;}
  const entry=mode==='worklist'||mode==='failure'?'/worklist':mode==='review'?'/review':'/invoices/1/attachments';
  createRoot(document.getElementById('root')).render(<MemoryRouter initialEntries={[entry]}><QueryClientProvider client={new QueryClient({defaultOptions:{queries:{retry:false},mutations:{retry:false}}})}>
    <ConfirmationProvider><UnsavedChangesProvider><main className='workspace-content' style={{padding:16}}><h1>业务功能验收</h1><Routes>
      <Route path='/invoices/:invoiceId/attachments' element={<BusinessAttachmentsPage client={client} user={user}/>}/>
      <Route path='/worklist' element={<WorklistPage client={client} user={user}/>}/>
      <Route path='/review' element={<InvoiceReviewPanel client={client} invoice={createEmptyInvoice('2026-09-08')} disabled={false} hasUnsavedChanges={true}/>}/>
      <Route path='*' element={<Destination/>}/>
    </Routes></main></UnsavedChangesProvider></ConfirmationProvider>
  </QueryClientProvider></MemoryRouter>);
` }, outfile: path.join(output, "app.js"), bundle: true, format: "esm", platform: "browser", jsx: "automatic", logLevel: "silent" });
const server = http.createServer((request, response) => {
  const name = new URL(request.url, "http://localhost").pathname.slice(1);
  if (!name) {
    response.setHeader("Content-Type", "text/html; charset=utf-8");
    response.end('<!doctype html><html lang="zh-CN"><meta name="viewport" content="width=device-width, initial-scale=1"><title>业务功能验收</title><link rel="stylesheet" href="/app.css"><div id="root"></div><script type="module" src="/app.js"></script></html>');
  } else if (["app.js", "app.css"].includes(name)) {
    response.setHeader("Content-Type", name.endsWith("js") ? "text/javascript" : "text/css");
    response.end(fs.readFileSync(path.join(output, name)));
  } else response.writeHead(404).end();
});
await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
let chrome, cdp;
const results = [];
const verifyPdfViewer = process.argv.includes("--pdf-viewer");
const read = async (page, expression) => (await evaluate(page, expression, true)).value;
async function waitFor(page, expression) {
  const until = Date.now() + 20000;
  while (Date.now() < until) { if (await read(page, `Boolean(${expression})`)) return; await delay(60); }
  throw new Error(`Timed out: ${expression}; ${await read(page, "document.body.innerText.slice(0,2000)")}`);
}
async function click(page, text, selector = "button") {
  await read(page, `(()=>{const node=[...document.querySelectorAll(${JSON.stringify(selector)})].find(n=>n.getClientRects().length&&n.textContent.trim()===${JSON.stringify(text)});if(!node)throw new Error('Missing '+${JSON.stringify(text)});node.click()})()`);
  await delay(80);
}
async function input(page, selector, value) {
  await read(page, `(()=>{const node=document.querySelector(${JSON.stringify(selector)});Object.getOwnPropertyDescriptor(node instanceof HTMLSelectElement?HTMLSelectElement.prototype:HTMLInputElement.prototype,'value').set.call(node,${JSON.stringify(value)});node.dispatchEvent(new Event('input',{bubbles:true}));node.dispatchEvent(new Event('change',{bubbles:true}))})()`);
  await delay(80);
}
async function audit(page, name) {
  await read(page, "document.fonts.ready.then(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))))");
  await evaluate(page, fs.readFileSync(require.resolve("axe-core/axe.min.js"), "utf8"), false);
  const issues = await read(page, "axe.run(document,{runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21aa']}}).then(r=>r.violations.map(v=>({id:v.id,nodes:v.nodes.map(n=>n.target)})))");
  await captureScreenshot(page, path.join(output, `${name}.png`));
  assert.deepEqual(issues, [], `${name}: accessibility`);
  assert(await read(page, "document.documentElement.scrollWidth<=innerWidth+1"), `${name}: horizontal overflow`);
  assert.deepEqual(await read(page, "window.__errors"), []);
  results.push(name);
}
try {
  chrome = await startChrome({ browserExecutable: locateChromeForTesting(repo, verifyPdfViewer ? "full-chrome" : "headless-shell"), userDataDir: path.join(output, `profile-${Date.now()}`), timeoutMs: 30000 });
  cdp = await CdpClient.connect(chrome.browserWebSocketUrl);
  const page = await createPageSession(cdp);
  async function open(mode, width = 1440, role = "editor") {
    await page.send("Emulation.setDeviceMetricsOverride", { width, height: 1000, deviceScaleFactor: 1, mobile: false });
    await page.send("Page.navigate", { url: `http://127.0.0.1:${server.address().port}/?mode=${mode}&role=${role}` });
    await waitFor(page, "document.querySelector('.business-records, details[aria-label=\"单据核对检查\"]')");
    await waitFor(page, "window.__calls.length>0 || new URLSearchParams(location.search).get('mode')==='review' || document.body.innerText.includes('业务库暂时不可用')");
  }
  for (const width of [1440, 1024, 390, 320]) {
    await open("worklist", width); await waitFor(page, "document.querySelectorAll('.business-records-card').length===3");
    await audit(page, `worklist-${width}`);
    await open("attachments", width); await click(page, "查看版本"); await waitFor(page, "document.querySelectorAll('.attachment-version').length===2");
    await audit(page, `versions-${width}`);
  }
  await open("worklist");
  await input(page, ".business-records-toolbar label:nth-child(2) select", "Overdue");
  await waitFor(page, "document.querySelectorAll('.business-records-card').length===1");
  assert(await read(page, "window.__calls.some(c=>c.name==='worklist'&&c.input.due==='Overdue')"));
  await click(page, "查看并处理", "a"); await waitFor(page, "window.__destination");
  assert.equal(await read(page, "window.__destination"), "/crm/follow-ups?followUpId=79"); results.push("worklist-filter-and-source-link");
  await open("attachments"); await click(page, "查看版本"); await waitFor(page, "document.querySelector('.attachment-detail')");
  await click(page, "设为有效版本"); await waitFor(page, "document.body.innerText.includes('请先填写本次确认')");
  await input(page, ".attachment-detail input", "客户确认采用第二版"); await click(page, "设为有效版本");
  await waitFor(page, "document.querySelector('[role=dialog]')"); await click(page, "确认", "[role=dialog] button");
  await waitFor(page, "window.__calls.some(c=>c.name==='updateAttachment')");
  assert.equal(await read(page, "window.__calls.find(c=>c.name==='updateAttachment').input.body.currentRevision"), 2);
  await click(page, "预览"); await waitFor(page, "document.querySelector('.attachment-preview img')?.naturalWidth===600");
  await audit(page, "confirmed-version-preview"); await click(page, "关闭预览");
  await waitFor(page, "window.__revokedUrls.length===window.__createdUrls.length"); results.push("confirmation-and-preview-cleanup");
  await click(page, "上传新版本"); await waitFor(page, "document.querySelector('input[type=file]')");
  await read(page, "(()=>{const input=document.querySelector('input[type=file]'),transfer=new DataTransfer();transfer.items.add(new File(['客户新要求'],'新要求.txt',{type:'text/plain'}));input.files=transfer.files;input.dispatchEvent(new Event('change',{bubbles:true}))})()");
  await click(page, "上传并保留版本"); await waitFor(page, "window.__calls.some(c=>c.name==='upload')");
  assert.equal(await read(page, "window.__calls.find(c=>c.name==='upload').input.fields.file"), "新要求.txt");
  await waitFor(page, "document.querySelectorAll('.attachment-detail ol .attachment-version').length===3"); results.push("replacement-upload-retains-versions");
  await open("attachments", 390, "reader"); await click(page, "查看版本"); await waitFor(page, "document.querySelector('.attachment-detail')");
  assert.equal(await read(page, "[...document.querySelectorAll('button')].some(b=>['上传资料','上传新版本','设为有效版本','停用资料'].includes(b.textContent.trim()))"), false);
  await audit(page, "readonly-attachments");
  await open("failure"); await waitFor(page, "document.body.innerText.includes('业务库暂时不可用')");
  assert.equal(await read(page, "document.querySelectorAll('.business-records-card').length"), 0); await audit(page, "worklist-failure");
  await open("review"); await click(page, "核对检查", "summary"); await click(page, "检查当前单据");
  await waitFor(page, "document.body.innerText.includes('第 3 行：数量必须大于 0')"); await audit(page, "invoice-review-row-errors");
  if (verifyPdfViewer) {
    await open("pdf"); await click(page, "查看版本"); await waitFor(page, "document.querySelector('.attachment-detail')");
    await click(page, "预览"); await waitFor(page, "document.querySelector('.attachment-preview iframe')");
    const session = await attachPdfViewerSession(cdp, page.targetId, { slug: "archive-preview", expectedPages: 1 });
    await read(page, "document.querySelector('.attachment-preview').scrollIntoView({block:'start'})");
    await delay(300);
    await captureScreenshot(page, path.join(output, "pdf-preview.png"), { captureBeyondViewport: false });
    await cdp.send("Target.detachFromTarget", { sessionId: session });
    results.push("native-pdf-preview-one-page");
  }
  fs.writeFileSync(path.join(output, "summary.json"), JSON.stringify({ passed: results.length, results }, null, 2));
  process.stdout.write(`Business feature UI contracts passed (${results.length} cases).\n`);
} finally {
  cdp?.close();
  if (chrome) await closeChrome(chrome.browserWebSocketUrl, chrome.process);
  await new Promise(resolve => server.close(resolve));
}
