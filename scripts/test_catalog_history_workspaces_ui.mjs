import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";
import { locateChromeForTesting } from "./lib/chromium-executable.mjs";

const root = path.resolve(import.meta.dirname, ".."), web = path.join(root, "apps/export-doc-web");
const require = createRequire(path.join(web, "package.json"));
const output = path.join(root, "artifacts/catalog-history-workspaces-ui", String(Date.now()));
fs.mkdirSync(path.join(output, "Temp"), { recursive: true });
Object.assign(process.env, { TEMP: path.join(output, "Temp"), TMP: path.join(output, "Temp") });
const source = name => JSON.stringify(path.join(web, "src", name).replaceAll("\\", "/"));
const bundle = path.join(output, "app.js");
await require("esbuild").build({ stdin: { loader: "tsx", resolveDir: web, contents: `
import React,{useState} from "react";import {createRoot} from "react-dom/client";
import {QueryClient,QueryClientProvider} from "@tanstack/react-query";import {MemoryRouter} from "react-router-dom";
import {ApiError} from ${source("api/index.ts")};
import {MasterDataListPage} from ${source("features/master-data/MasterDataListPage.tsx")};
import {masterDataConfigs} from ${source("features/master-data/masterDataConfigs.ts")};
import {HsCodeObsoleteAction} from ${source("features/master-data/HsCodeObsoleteAction.tsx")};
import {HsCodeObsoleteReviewPanel} from ${source("features/master-data/HsCodeObsoleteReviewPanel.tsx")};
import {HsCodeImportPreview} from ${source("features/master-data/HsCodeImportPreview.tsx")};
import {ReportTemplateHistoryRetention} from ${source("features/reports/ReportTemplateHistoryRetention.tsx")};
import {ConfirmationProvider} from ${source("ui/ConfirmationProvider.tsx")};
import {UnsavedChangesProvider,useHasUnsavedChanges} from ${source("ui/unsavedChangesGuard.tsx")};
window.calls=[];window.requests=[];window.changed=0;window.totalHistory=27;
window.catalog=[{id:1,code:"6109100000",name:"甲编码",rowVersion:"native:1",status:"SuspectedObsolete"},{id:2,code:"6109100001",name:"乙编码",rowVersion:"native:1",status:"ReferenceOnly"}];
const queries=new QueryClient({defaultOptions:{queries:{retry:false,staleTime:30000},mutations:{retry:false}}});window.queries=queries;
const deferred=(kind,request,init)=>new Promise((resolve,reject)=>window.requests.push({kind,request,signal:init.signal,resolve,reject}));
const retention=keep=>({totalCount:window.totalHistory,totalBytes:window.totalHistory*1024,eligibleCount:Math.max(0,window.totalHistory-keep),eligibleBytes:Math.max(0,window.totalHistory-keep)*1024,retainLatest:keep,maxCleanupCount:5000,canClean:true,policy:"保留当前模板，历史由预检确认清理。"});
const client={listHsCodes:async r=>structuredClone({items:window.catalog,pageNumber:1,pageSize:r.pageSize,totalCount:2,totalPages:1,hasNextPage:false,hasPreviousPage:false}),
deleteHsCodesBatch:async r=>{window.calls.push({kind:"delete",request:r});if(r.body.expectedVersions.some(v=>window.catalog.find(row=>row.id===v.id)?.rowVersion!==v.rowVersion))throw new ApiError(409,"Conflict",JSON.stringify({message:"编码已变化，请重新读取。"}));return {success:true,message:"已删除"};},
confirmHsCodesObsolete:(r,i)=>deferred("obsolete",r,i),previewHsCodesObsolete:i=>deferred("obsolete-preview",{},i),getUserReportTemplateHistoryRetention:async r=>retention(r.retainLatest||20),
previewUserReportTemplateHistoryCleanup:(r,i)=>deferred("history-preview",r,i),commitUserReportTemplateHistoryCleanup:(r,i)=>deferred("history-commit",r,i)};
window.finish=index=>{const p=window.requests[index];if(p.kind==="obsolete")p.resolve({success:true,message:"已确认作废"});
else if(p.kind==="obsolete-preview")p.resolve({totalCount:5200,maxBatchCount:5000,policy:"核实后批量确认作废。",items:Array.from({length:5000},(_,i)=>({id:10000+i,code:"6109"+String(i).padStart(6,"0"),name:"疑似编码 "+i,status:"SuspectedObsolete",rowVersion:"native:1"}))});
else if(p.kind==="history-preview"){const keep=p.request.body.retainLatest;p.resolve({token:"preview-token",templateVersion:27,retention:retention(keep),items:Array.from({length:Math.max(0,window.totalHistory-keep)},(_,i)=>({id:i+1,recordVersion:1,versionNumber:i+1,byteLength:1024}))});}
else {window.totalHistory=20;p.resolve({success:true,message:"历史已清理，当前模板保留。"});}};
const preview={token:"annual",confidence:100,addCount:5000,updateCount:0,suspectedObsoleteCount:5000,conflictCount:0,invalidCount:0,columns:[],warnings:[],worksheetName:"年度",headerRowNumber:1,sourceName:"官方资料",effectiveYear:2026,
items:Array.from({length:10000},(_,i)=>({changeType:i%2?"SuspectedObsolete":"Add",rowNumber:i+2,item:{code:String(i).padStart(10,"0"),name:"编码 "+i},message:"年度核对",replacementCandidates:[]}))};
function Harness(){const [shown,setShown]=useState(true),[recordVersion,setVersion]=useState(1),[templateId,setTemplateId]=useState(23);window.hide=()=>setShown(false);window.switchRecord=()=>setVersion(2);window.switchTemplate=()=>setTemplateId(24);window.dirty=useHasUnsavedChanges();
return !shown?<div>已退出</div>:window.mode==="list"?<MasterDataListPage client={client} config={masterDataConfigs.find(c=>c.key==="hs-codes")} canManage canOperate canViewMasterData/>:
window.mode==="obsolete"?<HsCodeObsoleteAction client={client} records={[{...window.catalog[0],rowVersion:"native:"+recordVersion}]} disabled={false} onChanged={async()=>{window.changed++;}}/>:
window.mode==="review"?<HsCodeObsoleteReviewPanel client={client} disabled={false} businessYear={2026} onChanged={async()=>{window.changed++;}}/>:
window.mode==="preview"?<HsCodeImportPreview preview={preview} busy={false} onBack={()=>{}} onCommit={()=>{window.changed++;}}/>:
<ReportTemplateHistoryRetention client={client} template={{id:templateId,versionNumber:27,canManageHistory:true}} enabled isBusy={false}/>;}
createRoot(document.getElementById("root")).render(<MemoryRouter><QueryClientProvider client={queries}><ConfirmationProvider><UnsavedChangesProvider><Harness/></UnsavedChangesProvider></ConfirmationProvider></QueryClientProvider></MemoryRouter>);
` }, outfile: bundle, bundle: true, platform: "browser", format: "iife", jsx: "automatic", logLevel: "silent" });
const context = await require("playwright").chromium.launchPersistentContext(path.join(output, "Chrome"), { executablePath: locateChromeForTesting(root), headless: true });
const results = [];
async function scenario(mode, name, check) {
  const page = await context.newPage(), errors = []; page.setDefaultTimeout(8000); page.on("pageerror", error => errors.push(error.message));
  try {
    await page.setContent('<!doctype html><html lang="zh-CN"><title>Catalog and history regression</title><div id="root"></div></html>');
    await page.evaluate(mode => window.mode = mode, mode); await page.addScriptTag({ path: bundle });
    await check(page); assert.deepEqual(errors, []); results.push(name);
  } catch (error) { await page.screenshot({ path: path.join(output, `failure-${results.length}.png`), fullPage: true }); throw error; }
  finally { await page.close(); }
}
try {
  await scenario("list", "selection-retains-observed-versions-after-refresh", async page => {
    await page.getByLabel("选择当前页HS编码").check();
    await page.evaluate(async()=>{window.catalog[1].rowVersion="native:2";await window.queries.refetchQueries();});
    await page.getByRole("button",{name:"删除选中",exact:true}).click();await page.getByRole("button",{name:"删除 2 条",exact:true}).click();
    await page.getByText("编码已变化，请重新读取。",{exact:true}).waitFor();
    assert.deepEqual(await page.evaluate(()=>window.calls[0].request.body.expectedVersions),[{id:1,rowVersion:"native:1"},{id:2,rowVersion:"native:1"}]);
  });
  await scenario("obsolete", "obsolete-requires-reason-version-and-single-submit", async page => {
    await page.getByRole("button",{name:"确认作废",exact:true}).click();
    assert.equal(await page.getByRole("button",{name:"确认 1 条作废",exact:true}).isDisabled(),true);
    await page.getByLabel("确认作废原因",{exact:true}).fill("已核对官方年度公告");await page.waitForFunction(()=>window.dirty);
    await page.getByRole("button",{name:"确认 1 条作废",exact:true}).click();await page.waitForFunction(()=>window.requests.length===1);
    assert.deepEqual(await page.evaluate(()=>window.requests[0].request.body),{items:[{id:1,rowVersion:"native:1"}],reason:"已核对官方年度公告"});
    await page.evaluate(()=>window.finish(0));await page.getByText("已确认作废",{exact:true}).waitFor();
    assert.equal(await page.evaluate(()=>window.changed),1);await page.waitForFunction(()=>!window.dirty);
  });
  await scenario("obsolete", "obsolete-late-result-cancels-after-record-change", async page => {
    await page.getByRole("button",{name:"确认作废",exact:true}).click();await page.getByLabel("确认作废原因",{exact:true}).fill("年度核对");
    await page.getByRole("button",{name:"确认 1 条作废",exact:true}).click();await page.waitForFunction(()=>window.requests.length===1);
    await page.evaluate(()=>window.switchRecord());await page.waitForFunction(()=>window.requests[0].signal.aborted);await page.evaluate(()=>window.finish(0));
    assert.equal(await page.evaluate(()=>window.changed),0);assert.equal(await page.getByText("已确认作废",{exact:true}).count(),0);
  });
  await scenario("review", "five-thousand-suspected-codes-select-across-pages-with-exclusions", async page => {
    await page.getByText("疑似作废批量核对",{exact:true}).click();
    await page.getByRole("button",{name:"读取疑似作废编码",exact:true}).click();await page.waitForFunction(()=>window.requests.length===1);await page.evaluate(()=>window.finish(0));
    await page.getByLabel("选择本批全部编码").check();await page.getByLabel("选择编码 6109000000",{exact:true}).uncheck();
    await page.getByRole("button",{name:"下一页",exact:true}).click();await page.getByLabel("选择编码 6109000050",{exact:true}).uncheck();
    assert.equal(await page.locator("table tbody tr").count(),50);await page.getByText(/已选 4998 条/).waitFor();
    await page.getByRole("button",{name:"批量确认作废",exact:true}).click();await page.getByLabel("确认作废原因").fill("核对官方公告后批量确认");
    await page.getByRole("button",{name:"确认 4998 条作废",exact:true}).click();await page.waitForFunction(()=>window.requests.length===2);
    assert.equal(await page.evaluate(()=>window.requests[1].request.body.items.length),4998);
    assert.equal(await page.evaluate(()=>window.requests[1].request.body.items.some(row=>row.id===10000||row.id===10050)),false);
    await page.evaluate(()=>window.finish(1));await page.getByText(/已确认本批 4998 个编码作废/).waitFor();assert.equal(await page.evaluate(()=>window.changed),1);
  });
  await scenario("preview", "large-annual-preview-pages-and-filters", async page => {
    const rows=page.locator("table tbody tr");await rows.first().waitFor();assert.equal(await rows.count(),50);
    await page.getByLabel("筛选导入变更").selectOption("SuspectedObsolete");assert.equal(await rows.count(),50);
    await page.getByRole("button",{name:"下一页",exact:true}).click();await page.getByText("编码 101",{exact:true}).waitFor();
    await page.getByLabel("筛选导入变更").selectOption("Add");await page.getByText("编码 0",{exact:true}).waitFor();
    assert.equal(await rows.count(),50);
  });
  await scenario("history", "history-preview-and-reason-confirmation-refresh-capacity", async page => {
    await page.getByText(/历史快照：27 版/).waitFor();await page.getByRole("button",{name:"预检旧快照清理",exact:true}).click();
    await page.waitForFunction(()=>window.requests.length===1);await page.evaluate(()=>window.finish(0));
    assert.equal(await page.getByRole("button",{name:"确认清理旧快照",exact:true}).isDisabled(),true);
    await page.getByLabel("历史清理原因").fill("保留最近20版");await page.getByRole("button",{name:"确认清理旧快照",exact:true}).click();
    await page.waitForFunction(()=>window.requests.length===2);await page.evaluate(()=>window.finish(1));
    await page.getByText(/历史快照：20 版/).waitFor();assert.equal(await page.getByRole("button",{name:"预检旧快照清理",exact:true}).isDisabled(),true);
  });
  await scenario("history", "history-late-preview-cancels-after-template-change", async page => {
    await page.getByText(/历史快照：27 版/).waitFor();await page.getByRole("button",{name:"预检旧快照清理",exact:true}).click();
    await page.waitForFunction(()=>window.requests.length===1);await page.evaluate(()=>window.switchTemplate());
    await page.waitForFunction(()=>window.requests[0].signal.aborted);await page.evaluate(()=>window.finish(0));assert.equal(await page.getByRole("dialog").count(),0);
  });
  fs.writeFileSync(path.join(output,"summary.json"),JSON.stringify({passed:results.length,results},null,2));
  process.stdout.write(`Catalog/history workspace UI passed (${results.length} cases).\n`);
} finally { await context.close(); }
