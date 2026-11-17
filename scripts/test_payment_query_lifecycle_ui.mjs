import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";
import { locateChromeForTesting } from "./lib/chromium-executable.mjs";

const root = path.resolve(import.meta.dirname, ".."), web = path.join(root, "apps/export-doc-web");
const require = createRequire(path.join(web, "package.json"));
const output = path.join(root, "artifacts/payment-query-lifecycle-ui", String(Date.now()));
fs.mkdirSync(path.join(output, "Temp"), { recursive: true });
Object.assign(process.env, { TEMP: path.join(output, "Temp"), TMP: path.join(output, "Temp") });
const source = name => JSON.stringify(path.join(web, "src", name).replaceAll("\\", "/"));
const bundle = path.join(output, "app.js");
await require("esbuild").build({ stdin: { loader: "tsx", resolveDir: web, contents: `
import React,{useState} from "react";import {createRoot} from "react-dom/client";
import {QueryClient,QueryClientProvider} from "@tanstack/react-query";
import {MemoryRouter,Routes,Route,useNavigate,useLocation} from "react-router-dom";
import {PaymentEditorPage} from ${source("features/payments/PaymentEditorPage.tsx")};
import {createEmptyPayment} from ${source("features/payments/paymentModel.ts")};
import {QueryPage} from ${source("features/query/QueryPage.tsx")};
import {PermissionAccessProvider} from ${source("app/PermissionAccessContext.tsx")};
import {ConfirmationProvider} from ${source("ui/ConfirmationProvider.tsx")};
import {UnsavedChangesProvider,useHasUnsavedChanges} from ${source("ui/unsavedChangesGuard.tsx")};
window.requests=[];window.reads=[];window.failPayment=0;
const cache=new QueryClient({defaultOptions:{queries:{retry:false,staleTime:30000},mutations:{retry:false}}});window.cache=cache;
const record=id=>({...createEmptyPayment("2026-11-17"),id,voucherNo:"Voucher-"+id,payeeName:"Payee-"+id,rowVersion:"native:1"});
const deferred=(kind,request,init)=>new Promise((resolve,reject)=>window.requests.push({kind,request,signal:init.signal,resolve,reject}));
const base={
 getSettings:async()=>({settings:{system:{defaultExportDirectory:"",documentFieldLabels:{}},report:{}},secrets:{}}),
 listCustomOptions:async({optionType})=>({optionType,options:[]}),
 saveCustomOption:(r,i)=>window.deferOptions?deferred("option",r,i):Promise.resolve({}),
 getPayment:async({id})=>{window.reads.push(id);if(window.failPayment===id)throw new Error("payment load failed");return record(id)},
 updatePayment:(r,i)=>deferred("save",r,i),createPayment:(r,i)=>deferred("save",r,i),
 deletePayment:(r,i)=>deferred("delete",r,i),
 listQueriedInvoices:async()=>({items:[],pageNumber:1,pageSize:50,totalCount:0,totalPages:1}),
 downloadQueriedInvoices:(r,i)=>deferred("export",r,i),
};
window.finishSave=()=>{const r=window.requests.at(-1);r.resolve({id:r.request.id||7,payment:{...r.request.body,id:r.request.id||7,rowVersion:"native:2"}})};
const user={id:1,companyScope:"",departmentId:""};
function Harness(){
 const [client,setClient]=useState(()=>({...base})),[allowed,setAllowed]=useState(true),[shown,setShown]=useState(true);
 const navigate=useNavigate(),location=useLocation();window.route=location.pathname;window.navigate=navigate;
 window.switchClient=()=>setClient({...base});window.revoke=()=>setAllowed(false);window.hide=()=>setShown(false);window.dirty=useHasUnsavedChanges();
 const permissions=["document.payments","document.query"].flatMap(resourceKey=>(allowed?["view","operate","manage"]:["view"]).map(action=>({resourceKey,action,dataScope:"all"})));
 return <PermissionAccessProvider subject={user} permissions={permissions}>{shown?<Routes>
 <Route path="/payments/new" element={<PaymentEditorPage client={client} businessDate="2026-11-17" mode="new"/>}/>
 <Route path="/payments/:paymentId" element={<PaymentEditorPage client={client} businessDate="2026-11-17" mode="edit"/>}/>
 <Route path="/payments" element={<div>付款列表</div>}/>
 <Route path="/query" element={<QueryPage client={client} businessDate="2026-11-17"/>}/>
 </Routes>:<div>unmounted</div>}</PermissionAccessProvider>;
}
createRoot(document.getElementById("root")).render(<MemoryRouter initialEntries={[window.entry]}><QueryClientProvider client={cache}>
<ConfirmationProvider><UnsavedChangesProvider><Harness/></UnsavedChangesProvider></ConfirmationProvider></QueryClientProvider></MemoryRouter>);
` }, outfile: bundle, bundle: true, platform: "browser", format: "iife", jsx: "automatic", logLevel: "silent" });
const context = await require("playwright").chromium.launchPersistentContext(path.join(output, "Chrome"), { executablePath: locateChromeForTesting(root), headless: true });
const results = [];
async function scenario(name, entry, check) {
  const page = await context.newPage(), errors = [];
  page.setDefaultTimeout(8000); page.on("pageerror", error => errors.push(error.message));
  try {
    await page.setContent('<!doctype html><html lang="zh-CN"><title>Payment and query lifecycle</title><div id="root"></div></html>');
    await page.evaluate(entry => { window.entry = entry; }, entry); await page.addScriptTag({ path: bundle });
    await check(page); assert.deepEqual(errors, []); results.push(name);
  } catch (error) {
    await page.screenshot({ path: path.join(output, "failure.png"), fullPage: true });
    throw new Error(name, { cause: error });
  } finally { await page.close(); }
}
const voucher = page => page.getByLabel("付款单号", { exact: true });
const submit = page => page.locator("form.entity-form").evaluate(form => { form.requestSubmit(); form.requestSubmit(); });
const flush = page => page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
try {
  for (const change of ["route", "client", "permission", "unmount"]) {
    await scenario("delete confirmation: " + change, "/payments/1", async page => {
      await voucher(page).waitFor(); await page.getByRole("button", { name: "删除", exact: true }).click();
      await page.getByRole("dialog").waitFor();
      await page.evaluate(change => {
        if (change === "route") window.navigate("/payments/2");
        if (change === "client") window.switchClient();
        if (change === "permission") window.revoke();
        if (change === "unmount") window.hide();
      }, change);
      await flush(page); await page.getByRole("button", { name: "确认删除", exact: true }).click(); await flush(page);
      assert.equal(await page.evaluate(() => window.requests.length), 0);
      if (change === "route") assert.equal(await voucher(page).inputValue(), "Voucher-2");
    });
  }
  await scenario("failed route load cannot save old draft", "/payments/1", async page => {
    await voucher(page).fill("draft-A");
    await page.evaluate(() => { window.failPayment = 2; window.navigate("/payments/2"); });
    await page.getByText("payment load failed", { exact: false }).waitFor();
    assert.equal(await voucher(page).count(), 0);
    await page.keyboard.press("Control+s"); await flush(page);
    assert.equal(await page.evaluate(() => window.requests.length), 0);
    await page.evaluate(() => { window.failPayment = 0; window.cache.invalidateQueries({ queryKey: ["payments"] }); });
    await page.evaluate(() => window.navigate("/payments/3"));
    await voucher(page).fill("draft-C"); await submit(page); await page.waitForFunction(() => window.requests.length === 1);
    assert.deepEqual(await page.evaluate(() => ({ id:window.requests[0].request.id, voucher:window.requests[0].request.body.voucherNo, version:window.requests[0].request.body.rowVersion })),
      { id:3, voucher:"draft-C", version:"native:1" });
  });
  await scenario("delete uses captured identity and admits once", "/payments/1", async page => {
    await voucher(page).waitFor(); await page.getByRole("button", { name: "删除", exact: true }).click();
    await page.getByRole("button", { name: "确认删除", exact: true }).evaluate(button => { button.click(); button.click(); });
    await page.waitForFunction(() => window.requests.length === 1);
    assert.deepEqual(await page.evaluate(() => window.requests[0].request), { id:1, rowVersion:"native:1" });
    await page.evaluate(() => window.requests[0].resolve({ message:"deleted" }));
    await page.getByText("付款列表", { exact: true }).waitFor();
  });
  for (const change of ["route", "client", "permission", "unmount"]) {
    await scenario("discard confirmation: " + change, "/payments/1", async page => {
      await voucher(page).fill("unsaved");
      await page.getByRole("button", { name: "返回列表", exact: true }).click();
      await page.getByRole("dialog").waitFor();
      await page.evaluate(change => {
        if (change === "route") window.navigate("/payments/2");
        if (change === "client") window.switchClient();
        if (change === "permission") window.revoke();
        if (change === "unmount") window.hide();
      }, change);
      await flush(page); await page.getByRole("dialog").getByRole("button", { name: "返回付款列表", exact: true }).click();
      await flush(page);
      assert.equal(await page.evaluate(() => window.route), change === "route" ? "/payments/2" : "/payments/1");
    });
  }
  await scenario("save unlocks before refresh and keeps returned version", "/payments/1", async page => {
    await voucher(page).fill("saved"); await page.evaluate(() => { window.cache.invalidateQueries = () => new Promise(() => {}); });
    await submit(page); await page.waitForFunction(() => window.requests.length === 1);
    await page.evaluate(() => window.finishSave()); await page.waitForFunction(() => !window.dirty);
    await voucher(page).fill("next"); await submit(page); await page.waitForFunction(() => window.requests.length === 2);
    assert.equal(await page.evaluate(() => window.requests[1].request.body.rowVersion), "native:2");
    assert.equal(await page.evaluate(() => window.cache.getMutationCache().getAll().length), 0);
  });
  await scenario("new payment replaces workspace after creation", "/payments/new", async page => {
    await voucher(page).fill("created"); await submit(page); await page.waitForFunction(() => window.requests.length === 1);
    await page.evaluate(() => window.finishSave()); await page.waitForFunction(() => window.route === "/payments/7");
    assert.equal(await voucher(page).inputValue(), "created"); assert.equal(await page.evaluate(() => window.dirty), false);
  });
  await scenario("custom options can overlap without blocking payment save", "/payments/1", async page => {
    await voucher(page).waitFor(); await page.evaluate(() => { window.deferOptions = true; });
    for (const name of ["payer-A", "payer-B"]) {
      await page.getByLabel("付款方", { exact: true }).fill(name); await voucher(page).click();
    }
    await page.waitForFunction(() => window.requests.filter(r => r.kind === "option").length === 2);
    await submit(page); await page.waitForFunction(() => window.requests.some(r => r.kind === "save"));
    assert.equal(await page.evaluate(() => window.requests.find(r => r.kind === "save").request.body.payerName), "payer-B");
    await page.evaluate(() => window.navigate("/payments/2"));
    await page.waitForFunction(() => window.requests.every(r => r.signal.aborted));
    await page.evaluate(() => window.requests.forEach(r => r.reject(new Error("late option failure")))); await flush(page);
    assert.equal(await page.getByText("late option failure", { exact: false }).count(), 0);
  });
  for (const change of ["filter", "client", "permission", "unmount"]) for (const outcome of ["resolve", "reject"]) {
    await scenario("export: " + change + " / " + outcome, "/query", async page => {
      await page.locator(".query-export-summary").click(); await page.getByRole("button", { name: "导出 Excel", exact: true }).click();
      await page.waitForFunction(() => window.requests.length === 1);
      if (change === "filter") {
        await page.getByLabel("关键字", { exact: true }).fill("new-filter"); await page.getByRole("button", { name: "查询", exact: true }).click();
      } else await page.evaluate(change => { if (change === "client") window.switchClient(); if (change === "permission") window.revoke(); if (change === "unmount") window.hide(); }, change);
      await page.waitForFunction(() => window.requests[0].signal.aborted);
      await page.evaluate(outcome => outcome === "reject" ? window.requests[0].reject(new Error("stale export error"))
        : window.requests[0].resolve({ jobId:"old-job",status:"Failed",errorMessage:"stale job error" }), outcome);
      await flush(page);
      assert.equal(await page.getByText(/stale (export|job) error/).count(), 0);
      if (change === "filter") {
        await page.getByRole("button", { name: "导出 Excel", exact: true }).click(); await page.waitForFunction(() => window.requests.length === 2);
        assert.equal(await page.evaluate(() => window.requests[1].request.body.keyword), "new-filter");
      }
      assert.equal(await page.evaluate(() => window.cache.getMutationCache().getAll().length), 0);
    });
  }
  console.log("Payment/query lifecycle UI: " + results.length + " passed; " + output);
} finally {
  await context.close();
  fs.writeFileSync(path.join(output, "summary.json"), JSON.stringify({ passed: results.length, results }, null, 2));
}
