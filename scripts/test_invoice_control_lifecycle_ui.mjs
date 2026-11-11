import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";
import { locateChromeForTesting } from "./lib/chromium-executable.mjs";

const root = path.resolve(import.meta.dirname, ".."), web = path.join(root, "apps/export-doc-web");
const require = createRequire(path.join(web, "package.json"));
const output = path.join(root, "artifacts/invoice-control-lifecycle-ui", String(Date.now()));
fs.mkdirSync(path.join(output, "Temp"), { recursive: true });
Object.assign(process.env, { TEMP: path.join(output, "Temp"), TMP: path.join(output, "Temp") });
const source = name => JSON.stringify(path.join(web, "src", name).replaceAll("\\", "/"));
const bundle = path.join(output, "app.js");
await require("esbuild").build({ stdin: { loader: "tsx", resolveDir: web, contents: `
import React,{StrictMode,useMemo,useState} from "react";import {createRoot} from "react-dom/client";
import {QueryClient,QueryClientProvider} from "@tanstack/react-query";import {MemoryRouter} from "react-router-dom";
import {useInvoiceControlOperations} from ${source("features/invoices/useInvoiceControlOperations.ts")};
import {useInvoiceSaveMutation} from ${source("features/invoices/useInvoiceSaveMutation.ts")};
import {createEmptyInvoice} from ${source("features/invoices/invoiceModel.ts")};
import {ConfirmationProvider,useConfirmation} from ${source("ui/ConfirmationProvider.tsx")};
import {useScopedConfirmation} from ${source("ui/useScopedConfirmation.ts")};
const cache=new QueryClient({defaultOptions:{queries:{retry:false}}});window.cache=cache;window.requests=[];
const deferred=(kind,request,init)=>new Promise((resolve,reject)=>window.requests.push({kind,request,signal:init.signal,resolve,reject}));
const client={deleteInvoice:(r,i)=>deferred("delete",r,i),unverifyInvoice:(r,i)=>deferred("unverify",r,i),
transitionInvoiceStatus:(r,i)=>deferred("transition",r,i),cloneInvoiceAsType:(r,i)=>deferred("clone",r,i),updateInvoice:(r,i)=>deferred("save",r,i)};
const alternate={...client};
function Harness(){const [shown,setShown]=useState(true),[id,setId]=useState(7),[owner,setOwner]=useState(false);
window.hide=()=>setShown(false);window.switchId=setId;window.switchClient=()=>setOwner(true);
return shown?<Editor id={id} client={owner?alternate:client}/>:<div>unmounted</div>;}
function Editor({id,client}){
const [invoice,setInvoice]=useState(()=>({...createEmptyInvoice("2026-11-11"),id:7,rowVersion:"native:1"}));
const [message,setMessage]=useState(null),[success,setSuccessMessage]=useState(null);
const options={client,invoice,invoiceId:id,isNew:false,setInvoice,setMessage,setSuccessMessage,setPendingHsFeedback:()=>{},setPersistedInvoiceDraft:()=>{},setPersistedInvoiceStatus:()=>{},setConcurrencyMessage:setMessage,resetItemEditHistory:()=>{}};
const commands=useInvoiceControlOperations(options);const save=useInvoiceSaveMutation(options);
const scope=useMemo(()=>({}),[client,id]);const confirm=useScopedConfirmation(scope,useConfirmation());
window.commands=commands;window.save=()=>save.mutate({...invoice});window.busy=save.isPending||commands.invoiceControlBusy;window.id=id;window.owner=client===alternate;
window.confirmDelete=async()=>{if(await confirm({title:"删除发票",description:"确认删除",confirmLabel:"确认删除"})) commands.deleteInvoiceMutation.mutate();};
window.resolve=index=>{const p=window.requests[index];p.resolve(p.kind==="delete"?{success:true,message:"deleted"}:{id,invoice:{...invoice,rowVersion:"native:2"},isUpdate:true});};
return <><output data-message>{message}</output><output data-success>{success}</output></>;}
createRoot(document.getElementById("root")).render(<StrictMode><MemoryRouter><QueryClientProvider client={cache}><ConfirmationProvider><Harness/></ConfirmationProvider></QueryClientProvider></MemoryRouter></StrictMode>);
` }, outfile: bundle, bundle: true, platform: "browser", format: "iife", jsx: "automatic", logLevel: "silent" });
const context = await require("playwright").chromium.launchPersistentContext(path.join(output, "Chrome"), { executablePath: locateChromeForTesting(root), headless: true });
let passed = 0;
async function scenario(check) {
  const page = await context.newPage(), errors = [];
  page.setDefaultTimeout(7000); page.on("pageerror", e => errors.push(e.message));
  try {
    await page.setContent('<!doctype html><html lang="zh-CN"><title>Invoice lifecycle</title><div id="root"></div></html>');
    await page.addScriptTag({ path: bundle }); await page.waitForFunction(() => Boolean(window.commands));
    await check(page); assert.deepEqual(errors, []); passed++;
  } finally { await page.close(); }
}
try {
  for (const switcher of ["hide", "switchId", "switchClient"]) {
    await scenario(async page => {
      await page.evaluate(() => { window.oldDelete = window.commands.deleteInvoiceMutation.mutate; void window.confirmDelete(); });
      await page.getByRole("button", { name: "确认删除", exact: true }).waitFor();
      await page.evaluate(s => window[s](8), switcher);
      if (switcher === "hide") await page.getByText("unmounted").waitFor();
      else await page.waitForFunction(s => s === "switchId" ? window.id === 8 : window.owner, switcher);
      await page.getByRole("button", { name: "确认删除", exact: true }).click();
      await page.evaluate(async () => { window.oldDelete(); await new Promise(r => setTimeout(r, 20)); });
      assert.equal(await page.evaluate(() => window.requests.length), 0);
    });
  }
  await scenario(async page => {
    await page.evaluate(() => { window.commands.unverifyInvoiceMutation.mutate(); window.commands.deleteInvoiceMutation.mutate(); });
    await page.waitForFunction(() => window.requests.length === 1);
    assert.equal(await page.evaluate(() => window.requests[0].kind), "unverify");
    await page.evaluate(() => window.resolve(0)); await page.waitForFunction(() => !window.busy);
    assert.equal(await page.evaluate(() => window.cache.getMutationCache().getAll().length), 0);
  });
  await scenario(async page => {
    await page.evaluate(() => {
      window.cache.invalidateQueries = () => new Promise((resolve,reject) => { window.refreshResolve=resolve;window.refreshReject=reject; });
      window.save();
    });
    await page.waitForFunction(() => window.requests.length === 1);
    await page.evaluate(() => window.resolve(0)); await page.waitForFunction(() => !window.busy && Boolean(window.refreshReject));
    assert.match(await page.locator("[data-success]").innerText(), /已保存/);
    await page.evaluate(() => window.refreshReject(new Error("refresh failed")));
    await page.waitForFunction(() => document.querySelector("[data-message]").textContent.includes("刷新失败"));
    assert.match(await page.locator("[data-success]").innerText(), /已保存/);
  });
  await scenario(async page => {
    await page.evaluate(() => window.commands.deleteInvoiceMutation.mutate()); await page.waitForFunction(() => window.requests.length === 1);
    await page.evaluate(() => window.hide()); await page.getByText("unmounted").waitFor();
    await page.evaluate(() => window.requests[0].reject(new Error("late failure")));
    assert.equal(await page.evaluate(() => window.requests[0].signal.aborted), true);
  });
  console.log(
    `Invoice control lifecycle UI: ${passed} passed; ${output}`,
  );
  fs.writeFileSync(path.join(output, "summary.json"), JSON.stringify({ passed }, null, 2));
} finally { await context.close(); }
