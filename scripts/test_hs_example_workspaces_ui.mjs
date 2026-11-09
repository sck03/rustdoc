import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";
import { locateChromeForTesting } from "./lib/chromium-executable.mjs";

const root=path.resolve(import.meta.dirname,".."), web=path.join(root,"apps/export-doc-web");
const require=createRequire(path.join(web,"package.json"));
const output=path.join(root,"artifacts/hs-example-workspaces-ui",String(Date.now()));
fs.mkdirSync(path.join(output,"Temp"),{recursive:true});
Object.assign(process.env,{TEMP:path.join(output,"Temp"),TMP:path.join(output,"Temp")});
const source=name=>JSON.stringify(path.join(web,"src",name).replaceAll("\\","/"));
const bundle=path.join(output,"app.js");
await require("esbuild").build({stdin:{loader:"tsx",resolveDir:web,contents:`
import React,{useState} from "react";import {createRoot} from "react-dom/client";
import {QueryClient,QueryClientProvider} from "@tanstack/react-query";import {MemoryRouter} from "react-router-dom";
import {ExampleLibrary} from ${source("features/master-data/HsExampleLibrary.tsx")};
import {RemoteCandidateReview} from ${source("features/master-data/HsRemoteCandidateReview.tsx")};
import {ConfirmationProvider} from ${source("ui/ConfirmationProvider.tsx")};
import {UnsavedChangesProvider,useHasUnsavedChanges} from ${source("ui/unsavedChangesGuard.tsx")};
window.requests=[];window.lookups=0;window.rows=[{id:1,versionNumber:1,rawReportedHsCode:"6109100000",resolvedCurrentHsCode:"",productName:"案例甲",specification:"棉",source:"Manual",resolutionStatus:"Unresolved"}];
const queries=new QueryClient({defaultOptions:{queries:{retry:false},mutations:{retry:false}}});window.queries=queries;
const deferred=(kind,r,i)=>new Promise((resolve,reject)=>window.requests.push({kind,request:r,signal:i.signal,resolve,reject}));
const client={listHsCodeKnowledgeExamples:async r=>structuredClone({items:window.rows,totalCount:window.rows.length,pageNumber:1,pageSize:r.pageSize}),
saveHsCodeKnowledgeExample:(r,i)=>deferred("save",r,i),deleteHsCodeKnowledgeExamplesBatch:(r,i)=>deferred("delete",r,i),deleteHsCodeKnowledgeExample:(r,i)=>deferred("delete-one",r,i),
listHsCodeRemoteCandidates:async r=>({items:Array.from({length:30},(_,i)=>({id:i+1,productName:"候选"+i,rawReportedHsCode:"6109100000",suggestedCurrentHsCode:"6109100000",source:"Test",queryText:"棉",seenCount:1,resolutionStatus:"Unresolved",reviewStatus:r.status})),totalCount:30,pageNumber:1,pageSize:30}),
listHsCodes:async()=>{window.lookups++;return {items:[{code:"6109100000",normalizedCode:"6109100000",status:"Active",effectiveYear:2026,lastVerifiedAt:"2026-11-09T00:00:00Z",sourceName:"Test"}]};}};
window.finish=index=>{const p=window.requests[index];if(p.kind==="save"){const saved={...p.request.body,versionNumber:(p.request.body.expectedVersion||0)+1,id:p.request.body.id||2};window.rows=[saved];p.resolve(saved);}else p.resolve({message:"已删除",success:true});};
function Harness(){const [shown,setShown]=useState(true);window.hide=()=>setShown(false);window.dirty=useHasUnsavedChanges();
return !shown?<div>已退出</div>:window.mode==="remote"?<RemoteCandidateReview client={client} canOperate canBatchOperate/>:<ExampleLibrary client={client} businessYear={2026} canOperate canManage canBatchManage/>;}
createRoot(document.getElementById("root")).render(<MemoryRouter><QueryClientProvider client={queries}><ConfirmationProvider><UnsavedChangesProvider><Harness/></UnsavedChangesProvider></ConfirmationProvider></QueryClientProvider></MemoryRouter>);
`},outfile:bundle,bundle:true,platform:"browser",format:"iife",jsx:"automatic",logLevel:"silent"});
const context=await require("playwright").chromium.launchPersistentContext(path.join(output,"Chrome"),{executablePath:locateChromeForTesting(root),headless:true});
const results=[];
async function scenario(mode,name,check){
  const page=await context.newPage(),errors=[];page.setDefaultTimeout(8000);page.on("pageerror",e=>errors.push(e.message));
  try{
    await page.setContent('<!doctype html><html lang="zh-CN"><title>HS workspace regression</title><div id="root"></div></html>');
    await page.evaluate(mode=>window.mode=mode,mode);await page.addScriptTag({path:bundle});await check(page);
    assert.deepEqual(errors,[]);results.push(name);
  }catch(error){await page.screenshot({path:path.join(output,"failure-"+results.length+".png"),fullPage:true});throw error;}
  finally{await page.close();}
}
try{
  await scenario("examples","save-preserves-continued-Chinese-edit-and-advances-version",async page=>{
    await page.getByRole("button",{name:"编辑",exact:true}).click();
    await page.getByLabel("商品名称",{exact:true}).fill("提交的品名");
    await page.getByRole("button",{name:"保存实例",exact:true}).click();await page.waitForFunction(()=>window.requests.length===1);
    assert.equal(await page.getByRole("button",{name:"新增实例",exact:true}).isDisabled(),true);
    await page.getByLabel("商品名称",{exact:true}).fill("保存期间继续输入");
    await page.evaluate(()=>window.finish(0));await page.getByText("申报实例已保存。",{exact:true}).waitFor();
    assert.equal(await page.getByLabel("商品名称",{exact:true}).inputValue(),"保存期间继续输入");
    await page.waitForFunction(()=>window.dirty);
    await page.getByRole("button",{name:"保存实例",exact:true}).click();await page.waitForFunction(()=>window.requests.length===2);
    assert.equal(await page.evaluate(()=>window.requests[1].request.body.expectedVersion),2);
    await page.evaluate(()=>window.finish(1));await page.waitForFunction(()=>!window.dirty);
  });
  await scenario("examples","late-save-cancels-on-unmount",async page=>{
    await page.getByRole("button",{name:"编辑",exact:true}).click();await page.getByRole("button",{name:"保存实例",exact:true}).click();
    await page.waitForFunction(()=>window.requests.length===1);await page.evaluate(()=>window.hide());
    await page.waitForFunction(()=>window.requests[0].signal.aborted);await page.evaluate(()=>window.finish(0));
    assert.equal(await page.getByText("申报实例已保存。",{exact:true}).count(),0);
  });
  await scenario("examples","delete-uses-selection-version-after-server-refresh",async page=>{
    await page.getByRole("button",{name:"编辑",exact:true}).click();
    await page.getByLabel("选择 案例甲",{exact:true}).check();
    await page.evaluate(async()=>{window.rows[0].versionNumber=2;await window.queries.refetchQueries();});
    await page.getByRole("button",{name:"批量删除（1）",exact:true}).click();
    await page.getByRole("button",{name:"确认删除",exact:true}).click();await page.waitForFunction(()=>window.requests.length===1);
    assert.equal(await page.evaluate(()=>window.requests[0].request.expectedVersion),1);
    assert.equal(await page.getByLabel("商品名称",{exact:true}).isDisabled(),true);
  });
  await scenario("remote","candidate-lookups-are-lazy-and-share-the-code-cache",async page=>{
    await page.getByLabel("选择当前有效HS编码").first().waitFor();
    assert.equal(await page.evaluate(()=>window.lookups),0);
    await page.getByLabel("选择当前有效HS编码").nth(0).focus();await page.waitForFunction(()=>window.lookups===1);
    await page.getByLabel("选择当前有效HS编码").nth(1).focus();
    await page.getByRole("button",{name:"确认加入",exact:true}).nth(1).waitFor();
    assert.equal(await page.evaluate(()=>window.lookups),1);
  });
  fs.writeFileSync(path.join(output,"summary.json"),JSON.stringify({passed:results.length,results},null,2));
  console.log("HS workspace UI passed ("+results.length+" cases).");
}finally{await context.close();}
