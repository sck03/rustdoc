import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";
import { locateChromeForTesting } from "./lib/chromium-executable.mjs";

const root = path.resolve(import.meta.dirname, ".."), web = path.join(root, "apps/export-doc-web");
const require = createRequire(path.join(web, "package.json"));
const output = path.join(root, "artifacts/packing-credit-workspace-ui", String(Date.now()));
fs.mkdirSync(path.join(output, "Temp"), { recursive: true });
Object.assign(process.env, { TEMP: path.join(output, "Temp"), TMP: path.join(output, "Temp") });
const source = name => JSON.stringify(path.join(web, "src", name).replaceAll("\\", "/"));
const bundle = path.join(output, "app.js");
await require("esbuild").build({ stdin: { loader: "tsx", resolveDir: web, contents: `
import React,{useState} from "react";import {createRoot} from "react-dom/client";
import {QueryClient,QueryClientProvider} from "@tanstack/react-query";
import {MemoryRouter} from "react-router-dom";
import {ContainerPackingPanel} from ${source("features/tools/container-packing/ContainerPackingPanel.tsx")};
import {InvoiceLetterOfCreditPanel} from ${source("features/invoices/InvoiceLetterOfCreditPanel.tsx")};
import {createEmptyInvoice} from ${source("features/invoices/invoiceModel.ts")};
import {ConfirmationProvider} from ${source("ui/ConfirmationProvider.tsx")};
import {UnsavedChangesProvider,useHasUnsavedChanges} from ${source("ui/unsavedChangesGuard.tsx")};
window.requests=[];
const queries=new QueryClient({defaultOptions:{queries:{retry:false,staleTime:30000},mutations:{retry:false}}});window.queries=queries;
const deferred=(kind,request,init)=>new Promise((resolve,reject)=>window.requests.push({kind,request,signal:init.signal,resolve,reject}));
const project=id=>({id,versionNumber:1,name:id===2?"乙方案":"甲方案",containerType:"20GP",container:{length:589,width:235,height:239,volume:33.2,maxWeight:28000},rules:{allowRotation:true},cargoItems:[{name:id===2?"乙货物":"甲货物",length:60,width:40,height:40,weight:10,quantity:id===2?77:50}]});
const client={listContainerPackingProjects:async()=>({projects:[project(1),project(2)]}),listContainerPackingContainerTypes:async()=>({containerTypes:[]}),
getContainerPackingProject:(r,i)=>deferred("load",r,i),saveContainerPackingProject:(r,i)=>deferred("save",r,i),deleteContainerPackingProject:(r,i)=>deferred("delete",r,i),
uploadLetterOfCreditDocument:(r,i)=>deferred("import",r,i),reviewLetterOfCreditCompliance:(r,i)=>deferred("review",r,i)};
window.finish=index=>{const pending=window.requests[index];
if(pending.kind==="load")pending.resolve({project:project(pending.request.id)});
else if(pending.kind==="save")pending.resolve({id:pending.request.body.id||1,project:{...pending.request.body,id:pending.request.body.id||1,versionNumber:(pending.request.body.expectedVersion||0)+1}});
else if(pending.kind==="import")pending.resolve({sourcePath:"甲信用证.txt",extractedText:"甲信用证正文",sourceDescription:"甲信用证"});
else pending.resolve({contextSummary:"甲审核",reportText:"甲审核结果"});};
function Harness(){const [shown,setShown]=useState(true),[invoice,setInvoice]=useState(()=>({...createEmptyInvoice("2026-10-26"),id:1,letterOfCreditContent:"当前正文"}));
window.hide=()=>setShown(false);window.switchInvoice=id=>setInvoice({...createEmptyInvoice("2026-10-26"),id,letterOfCreditContent:"乙正文"});window.dirty=useHasUnsavedChanges();window.invoice=invoice;
return shown?(window.mode==="packing"?<ContainerPackingPanel client={client} canOperate canManage/>:<InvoiceLetterOfCreditPanel client={client} invoice={invoice} onChange={changes=>setInvoice(current=>({...current,...changes}))} onClearPageMessages={()=>{}}/>):<div>unmounted</div>;}
createRoot(document.getElementById("root")).render(<MemoryRouter><QueryClientProvider client={queries}><ConfirmationProvider><UnsavedChangesProvider><Harness/></UnsavedChangesProvider></ConfirmationProvider></QueryClientProvider></MemoryRouter>);
` }, outfile: bundle, bundle: true, platform: "browser", format: "iife", jsx: "automatic", logLevel: "silent" });
const context = await require("playwright").chromium.launchPersistentContext(path.join(output, "Chrome"), { executablePath: locateChromeForTesting(root), headless: true });
let passed = 0;
async function scenario(mode, check) {
  const page = await context.newPage(), errors = []; page.setDefaultTimeout(8000); page.on("pageerror", error => errors.push(error.message));
  try {
    await page.setContent('<!doctype html><html lang="zh-CN"><title>Packing and credit regression</title><div id="root"></div></html>');
    await page.evaluate(mode => window.mode = mode, mode); await page.addScriptTag({ path: bundle });
    await check(page); assert.deepEqual(errors, []); passed++;
  } catch (error) { await page.screenshot({ path: path.join(output, `failure-${passed}.png`), fullPage: true }); throw error; }
  finally { await page.close(); }
}
async function load(page, id) {
  await page.getByLabel(/^已存方案/).selectOption(String(id));
  const index = await page.evaluate(() => window.requests.length);
  await page.getByRole("button", { name: "加载方案", exact: true }).click();
  await page.waitForFunction(() => window.requests.some(request => request.kind === "load"));
  await page.evaluate(index => window.finish(index), index);
  await page.getByText("装柜方案已加载。", { exact: true }).waitFor();
}
async function importCredit(page) {
  await page.getByRole("button", { name: "展开信用证", exact: true }).click();
  await page.locator('input[type="file"]').setInputFiles({ name: "甲信用证.txt", mimeType: "text/plain", buffer: Buffer.from("document") });
  await page.getByRole("button", { name: "导入信用证", exact: true }).click();
  await page.waitForFunction(() => window.requests.length === 1);
}
try {
  await scenario("packing", async page => {
    await page.getByLabel("方案名称", { exact: true }).fill("提交名称");
    await page.getByRole("button", { name: "保存方案", exact: true }).click(); await page.waitForFunction(() => window.requests.length === 1);
    await page.getByLabel("方案名称", { exact: true }).fill("继续编辑");
    await page.evaluate(() => window.finish(0)); await page.getByText("装柜方案已保存。", { exact: true }).waitFor();
    assert.equal(await page.getByLabel("方案名称", { exact: true }).inputValue(), "继续编辑");
    assert.equal(await page.evaluate(() => window.dirty), true);
    await page.getByRole("button", { name: "保存方案", exact: true }).click(); await page.waitForFunction(() => window.requests.length === 2);
    assert.equal(await page.evaluate(() => window.requests[1].request.body.id), 1);
    assert.equal(await page.evaluate(() => window.requests[1].request.body.expectedVersion), 1);
    await page.evaluate(() => window.finish(1)); await page.waitForFunction(() => !window.dirty);
  });
  for (const outcome of ["resolve", "reject"]) await scenario("packing", async page => {
    await load(page, 1); await page.getByRole("button", { name: "保存方案", exact: true }).click();
    await page.waitForFunction(() => window.requests.length === 2);
    await page.getByLabel(/^已存方案/).selectOption("2");
    await page.getByRole("button", { name: "加载方案", exact: true }).click(); await page.waitForFunction(() => window.requests.length === 3);
    await page.evaluate(() => window.finish(2)); await page.getByText("装柜方案已加载。", { exact: true }).waitFor();
    await page.evaluate(outcome => outcome === "resolve" ? window.finish(1) : window.requests[1].reject(new Error("甲迟到错误")), outcome);
    await page.waitForFunction(() => window.requests[1].signal.aborted);
    assert.equal(await page.getByLabel("方案名称", { exact: true }).inputValue(), "乙方案");
    await page.getByRole("button", { name: "保存方案", exact: true }).click(); await page.waitForFunction(() => window.requests.length === 4);
    assert.equal(await page.evaluate(() => window.requests[3].request.body.id), 2);
    assert.equal(await page.evaluate(() => window.requests[3].request.body.cargoItems[0].quantity), 77);
    assert.equal(await page.getByText("甲迟到错误", { exact: false }).count(), 0);
  });
  await scenario("packing", async page => {
    await page.getByLabel("方案名称", { exact: true }).fill("未保存");
    await page.getByLabel(/^已存方案/).selectOption("2"); await page.getByRole("button", { name: "加载方案", exact: true }).click();
    await page.getByRole("button", { name: "取消", exact: true }).click();
    assert.equal(await page.getByLabel("方案名称", { exact: true }).inputValue(), "未保存");
    assert.equal(await page.evaluate(() => window.requests.length), 0);
  });
  await scenario("packing", async page => {
    await page.getByLabel(/^已存方案/).selectOption("2"); await page.getByRole("button", { name: "加载方案", exact: true }).click();
    await page.waitForFunction(() => window.requests.length === 1); await page.getByLabel("方案名称", { exact: true }).fill("加载时新输入");
    await page.evaluate(() => window.finish(0)); await page.waitForFunction(() => window.requests[0].signal.aborted);
    assert.equal(await page.getByLabel("方案名称", { exact: true }).inputValue(), "加载时新输入");
  });
  await scenario("packing", async page => {
    await page.getByRole("button", { name: "保存方案", exact: true }).click(); await page.waitForFunction(() => window.requests.length === 1);
    await page.evaluate(() => { window.queries.invalidateQueries = async () => { throw new Error("目录离线"); }; window.finish(0); });
    await page.getByText(/操作已完成，但方案目录刷新失败/).waitFor();
    await page.getByRole("button", { name: "保存方案", exact: true }).click(); await page.waitForFunction(() => window.requests.length === 2);
    assert.equal(await page.evaluate(() => window.requests[1].request.body.expectedVersion), 1);
  });
  for (const operation of ["import", "review"]) for (const change of ["switch", "edit", "unmount"]) for (const outcome of ["resolve", "reject"]) {
    await scenario("credit", async page => {
      if (operation === "import") await importCredit(page);
      else { await page.getByRole("button", { name: "展开信用证", exact: true }).click(); await page.getByRole("button", { name: "AI 审查", exact: true }).click(); await page.waitForFunction(() => window.requests.length === 1); }
      if (change === "edit") await page.getByLabel("信用证号", { exact: true }).fill("新草稿修订");
      else await page.evaluate(change => change === "switch" ? window.switchInvoice(2) : window.hide(), change);
      if (change === "switch") await page.waitForFunction(() => window.invoice.id === 2);
      if (change === "unmount") await page.getByText("unmounted", { exact: true }).waitFor();
      await page.evaluate(outcome => outcome === "resolve" ? window.finish(0) : window.requests[0].reject(new Error("甲迟到错误")), outcome);
      await page.waitForFunction(() => window.requests[0].signal.aborted);
      if (change !== "unmount") assert.equal(await page.evaluate(() => window.invoice.letterOfCreditContent), change === "switch" ? "乙正文" : "当前正文");
      assert.equal(await page.getByText("甲迟到错误", { exact: false }).count(), 0);
      assert.equal(await page.locator(".letter-of-credit-review-result").count(), 0);
    });
  }
  await scenario("credit", async page => {
    await importCredit(page); await page.evaluate(() => window.finish(0));
    await page.getByText("信用证已导入：甲信用证", { exact: true }).waitFor();
    assert.equal(await page.evaluate(() => window.invoice.letterOfCreditContent), "甲信用证正文");
    await page.getByRole("button", { name: "AI 审查", exact: true }).click(); await page.waitForFunction(() => window.requests.length === 2);
    await page.evaluate(() => window.finish(1)); await page.locator(".letter-of-credit-review-result").waitFor();
    await page.getByLabel("信用证号", { exact: true }).fill("新版本");
    await page.waitForFunction(() => !document.querySelector(".letter-of-credit-review-result"));
  });
  console.log(`Packing and credit workspace UI: ${passed} passed; ${output}`);
} finally { await context.close(); }
