import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";
import { locateChromeForTesting } from "./lib/chromium-executable.mjs";

const root = path.resolve(import.meta.dirname, ".."), web = path.join(root, "apps/export-doc-web");
const require = createRequire(path.join(web, "package.json"));
const output = path.join(root, "artifacts/editor-recovery-ui");
fs.mkdirSync(path.join(output, "Temp"), { recursive: true });
Object.assign(process.env, { TEMP: path.join(output, "Temp"), TMP: path.join(output, "Temp") });
const source = name => JSON.stringify(path.join(web, "src", name).replaceAll("\\", "/"));
const bundle = await require("esbuild").build({ stdin: { resolveDir: web, loader: "tsx", contents: `
import React from "react"; import {createRoot} from "react-dom/client";
import {QueryClient,QueryClientProvider} from "@tanstack/react-query";
import {MemoryRouter,Routes,Route} from "react-router-dom";
import {useInvoiceEditorWorkspace} from ${source("features/invoices/hooks/useInvoiceEditorWorkspace.ts")};
import {createEmptyInvoice} from ${source("features/invoices/invoiceModel.ts")};
import {createEmptyInvoiceItem} from ${source("features/invoices/invoiceItemsEditorModel.ts")};
import {useBackupPlan} from ${source("features/settings/useBackupPlan.ts")};
import {ApiError} from ${source("api/index.ts")};
import {PermissionAccessProvider} from ${source("app/PermissionAccessContext.tsx")};
import {ConfirmationProvider} from ${source("ui/ConfirmationProvider.tsx")};
import {UnsavedChangesProvider} from ${source("ui/unsavedChangesGuard.tsx")};
window.serverInvoice={...createEmptyInvoice("2026-11-08"),id:7,invoiceNo:"SERVER-1",rowVersion:"native:1",
  items:Array.from({length:window.rows??1},()=>({...createEmptyInvoiceItem(7),styleName:"item",quantity:1,unitPrice:2}))};
window.pending=[];window.jobCalls=0;
const client={getInvoice:async({id},{signal})=>{
  if(window.failRefresh)throw new Error("refresh failure");
  if(window.delay&&id===7)return new Promise(resolve=>window.pending.push({resolve,signal}));
  return {...structuredClone(window.serverInvoice),id};
},listInvoiceStatusHistory:async()=>[],listUnits:async()=>[],
getSettings:async()=>({settings:{system:{documentFieldLabels:{}},report:{}},secrets:{},storagePolicy:""}),
listCustomOptions:async({optionType})=>({optionType,options:[]}),
getBackupPlan:async()=>({revision:1,settingsRevision:1,plan:{enabled:false},webDav:{},runningJobId:"missing"}),
getJob:async()=>{window.jobCalls++;if(!window.jobAvailable)throw new ApiError(404,"missing job");return {jobId:"missing",status:"Succeeded"};}};
function Invoice(){window.ws=useInvoiceEditorWorkspace({businessDate:"2026-11-08",client,mode:"edit"});return <button onClick={()=>void window.ws.handleReloadLatestInvoice()}>reload</button>}
function Backup(){window.backup=useBackupPlan(client,true);return <output>{window.backup.jobError}</output>}
const qc=new QueryClient({defaultOptions:{queries:{retry:false,staleTime:30000,refetchOnWindowFocus:false},mutations:{retry:false}}});
const permissions=["view","operate","manage"].map(action=>({resourceKey:"document.invoices",action,dataScope:"all"}));
createRoot(document.getElementById("root")).render(<React.StrictMode><MemoryRouter initialEntries={["/invoices/7"]}><QueryClientProvider client={qc}>
<PermissionAccessProvider permissions={permissions} subject={{id:1,companyScope:"",departmentId:""}} canManageSettings><ConfirmationProvider><UnsavedChangesProvider>
{window.mode==="backup"?<Backup/>:<Routes><Route path="/invoices/:invoiceId" element={<Invoice/>}/></Routes>}
</UnsavedChangesProvider></ConfirmationProvider></PermissionAccessProvider></QueryClientProvider></MemoryRouter></React.StrictMode>);
` }, write: false, bundle: true, platform: "browser", format: "iife", jsx: "automatic", logLevel: "silent" });
const context = await require("playwright").chromium.launchPersistentContext(path.join(output, "Chrome"), { executablePath: locateChromeForTesting(root), headless: true });
let passed = 0;
async function scenario(mode, check, rows = 1) {
  const page = await context.newPage(), errors = [];
  page.setDefaultTimeout(10000); page.on("pageerror", error => errors.push(error.message));
  try {
    await page.setContent('<!doctype html><html lang="zh-CN"><title>Editor recovery</title><div id="root"></div></html>');
    await page.evaluate(value => Object.assign(window, value), { mode, rows });
    await page.addScriptTag({ content: bundle.outputFiles[0].text });
    await page.waitForFunction(mode => mode === "backup" ? Boolean(window.backup?.jobError) : window.ws?.invoice?.id === 7, mode);
    await check(page); assert.deepEqual(errors, []); passed++;
  } finally { await page.close(); }
}
async function reload(page) {
  await page.getByRole("button", { name: "reload", exact: true }).click();
  await page.getByRole("button", { name: "加载最新版本", exact: true }).click();
}
try {
  for (const fail of [false, true]) await scenario("invoice", async page => {
    await page.evaluate(fail => { window.ws.patchInvoice({ invoiceNo: "LOCAL" }); window.failRefresh = fail; window.serverInvoice = { ...window.serverInvoice, invoiceNo: "SERVER-2", rowVersion: "native:2" }; }, fail);
    await page.waitForFunction(() => window.ws.hasUnsavedInvoiceChanges);
    await reload(page);
    await page.waitForFunction(fail => fail ? window.ws.invoiceQuery.isError : window.ws.invoice.rowVersion === "native:2", fail);
    await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
    assert.deepEqual(await page.evaluate(() => [window.ws.invoice.invoiceNo, window.ws.hasUnsavedInvoiceChanges, window.ws.serverDraftSync.hasPendingServerVersion]), fail ? ["LOCAL", true, false] : ["SERVER-2", false, false]);
    if (fail) assert.equal(await page.evaluate(() => window.ws.successMessage), null);
  });
  await scenario("invoice", async page => {
    await page.evaluate(() => { window.ws.patchInvoice({ invoiceNo: "LOCAL" }); window.delay = true; });
    await reload(page); await page.waitForFunction(() => window.pending.length > 0);
    await page.evaluate(() => window.ws.navigate("/invoices/8"));
    await page.waitForFunction(() => window.ws.invoice?.id === 8);
    await page.evaluate(() => window.pending.forEach(request => request.resolve({ ...window.serverInvoice, invoiceNo: "OBSOLETE" })));
    await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
    assert.deepEqual(await page.evaluate(() => [window.ws.invoice.id, window.ws.invoice.invoiceNo, window.ws.successMessage]), [8, "SERVER-1", null]);
  });
  await scenario("invoice", async page => {
    await page.evaluate(() => { window.originalRows = window.ws.currentInvoiceDraft.items; window.ws.patchInvoice({ contractNo: "HEADER" }); });
    await page.waitForFunction(() => window.ws.currentInvoiceDraft.contractNo === "HEADER");
    assert.equal(await page.evaluate(() => window.originalRows === window.ws.currentInvoiceDraft.items), true);
    assert.equal(await page.evaluate(() => window.ws.currentInvoiceDraft.totalAmount), 10000);
    await page.evaluate(() => window.ws.patchInvoice({ items: window.ws.invoice.items.map((row, index) => index ? row : { ...row, quantity: 3 }) }));
    await page.waitForFunction(() => window.ws.currentInvoiceDraft.totalAmount === 10004);
  }, 5000);
  await scenario("backup", async page => {
    const calls = await page.evaluate(() => window.jobCalls);
    await page.waitForTimeout(4300);
    assert.equal(await page.evaluate(() => window.jobCalls), calls, "404 must stop interval polling");
    await page.evaluate(() => { window.jobAvailable = true; window.backup.retryJob(); });
    await page.waitForFunction(() => window.backup.job?.status === "Succeeded");
    assert.equal(await page.evaluate(() => window.backup.jobError), null);
    await page.evaluate(() => window.backup.dismissJob());
    await page.waitForFunction(() => window.backup.jobId === null);
  });
  console.log(`Editor recovery UI: ${passed} passed`);
} finally { await context.close(); }
