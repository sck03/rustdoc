import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { createReactUiFixture } from "./lib/react-ui-fixture.mjs";
import { runtimeDiagnosticsHealth } from "./lib/runtime-diagnostics-fixture.mjs";

const { root, web, output, require, bundle, source, launch } = createReactUiFixture("sales-opportunity-lifecycle-ui");
await require("esbuild").build({ stdin: { loader: "tsx", resolveDir: web, contents: `
import React,{useState} from "react";import {createRoot} from "react-dom/client";
import {QueryClient,QueryClientProvider} from "@tanstack/react-query";
import {MemoryRouter,useNavigate,useLocation} from "react-router-dom";
import {SalesOpportunityPage} from ${source("features/opportunities/SalesOpportunityPage.tsx")};
import {RuntimeDiagnosticsSection} from ${source("features/settings/RuntimeDiagnosticsSection.tsx")};
import {PermissionAccessProvider} from ${source("app/PermissionAccessContext.tsx")};
import {ConfirmationProvider} from ${source("ui/ConfirmationProvider.tsx")};
import {UnsavedChangesProvider,useHasUnsavedChanges} from ${source("ui/unsavedChangesGuard.tsx")};
import ${source("styles/index.css")};import ${source("styles/workspaces.css")};import ${source("styles/responsive.css")};
import ${source("styles/runtime-diagnostics.css")};
window.requests=[];window.reads=[];window.historyReads=[];window.optionReads=[];window.metricReads=0;window.failRecord=0;
const health=${JSON.stringify(runtimeDiagnosticsHealth())};
const cache=new QueryClient({defaultOptions:{queries:{retry:false,staleTime:30000},mutations:{retry:false}}});window.cache=cache;
const record=id=>({id,crmCustomerId:1,customerName:"示例客户",productId:7,productCode:"P-7",productName:"棉制T恤",
 title:"商机-"+id,stage:"线索",quotationNo:"Q-"+id,estimatedAmount:"10000000000000.0001",currency:"USD",probabilityPercent:20,
 expectedCloseDate:"2026-12-15",nextAction:"联系客户",notes:"",versionNumber:1,isArchived:id===1&&window.config.archived,
 allowedNextStages:["需求确认"]});
window.records={1:record(1),2:record(2),3:record(3)};
const paged=(items,r={})=>({items,totalCount:items.length,pageNumber:r.pageNumber||1,pageSize:r.pageSize||20,totalPages:1,hasNextPage:false,hasPreviousPage:false});
const deferred=(kind,request,init)=>new Promise((resolve,reject)=>window.requests.push({kind,request,signal:init.signal,resolve,reject}));
const base={querySalesOpportunities:async r=>paged(Object.values(window.records).filter(x=>r.includeArchived||!x.isArchived),r),
 getRuntimeMetrics:async()=>{window.metricReads++;return {storage:{capacity:4,leased:1,failed:false,writeCoordinatorFailed:false,writeWait:{p95UpperBoundMicroseconds:1000}},
 jobs:{active:0,retainedInputBytes:1024,retainedOutputBytes:2048,downloadMemory:{reservedBytes:0,capacityBytes:134217728}},
 http:{requestCapacity:16,availableRequestSlots:14,queuedRequests:3,queueCapacity:16,responseMemory:{reservedBytes:33554432,capacityBytes:134217728,rejections:1},downloadTimeouts:2}};},
 getSalesOpportunity:async({id})=>{window.reads.push(id);if(window.failRecord===id)throw new Error("record load failed");return {...window.records[id]};},
 queryCrmCustomers:async()=>{window.optionReads.push('customers');return paged([{id:1,name:"示例客户"}]);},listProducts:async()=>{window.optionReads.push('products');return paged([{id:7,productCode:"P-7",nameCN:"棉制T恤",nameEN:"Cotton"}]);},
 createSalesOpportunity:(r,i)=>deferred("create",r,i),updateSalesOpportunity:(r,i)=>deferred("save",r,i),
 archiveSalesOpportunity:(r,i)=>deferred("archive",r,i),transitionSalesOpportunity:(r,i)=>deferred("transition",r,i),
 listSalesOpportunityHistory:async r=>{window.historyReads.push(r);return {items:(r.beforeId?[1]:[3,2]).map(versionNumber=>({id:versionNumber,versionNumber,salesOpportunityId:r.id,
 changeType:"资料更新",stage:"线索",quotationNo:"Q-1",estimatedAmount:"125.75",currency:"USD",probabilityPercent:20,changeNote:"第"+versionNumber+"次",changedBy:"测试人员",createdAt:"2026-11-18T01:00:00Z"})),nextCursor:r.beforeId?null:2};},
};
window.finish=(fail=false)=>{const pending=window.requests.at(-1);if(fail){pending.reject(new Error("late write failed"));return;}
 const id=pending.request.id||3,previous=window.records[id],saved={...previous,...pending.request.body,id,versionNumber:pending.request.id?previous.versionNumber+1:1};
 window.records[id]=saved;pending.resolve(saved);};
function Harness(){const [client,setClient]=useState(()=>({...base})),[allowed,setAllowed]=useState(window.config.readOnly!==true),[shown,setShown]=useState(true);
 const navigate=useNavigate(),location=useLocation();window.route=location.search;window.navigate=navigate;window.dirty=useHasUnsavedChanges();
 window.switchClient=()=>setClient({...base});window.revoke=()=>setAllowed(false);window.hide=()=>setShown(false);
 const permissions=[...(allowed?["view","create","edit","transition","archive"]:["view"]).map(action=>({resourceKey:"sales.opportunities",action,dataScope:"all"})),
 ...(window.config.quote===false?[]:["create","edit"].map(action=>({resourceKey:"sales.quotes",action,dataScope:"all"})))];
 return <PermissionAccessProvider subject={{id:1,companyScope:"DEFAULT",departmentId:"GENERAL"}} permissions={permissions}>
 {shown?(window.config.metrics?<RuntimeDiagnosticsSection client={client} canManageSettings health={health} isBusy={false} onRefresh={()=>{}} onPathError={()=>{}} errorMessage={null}/>:<SalesOpportunityPage client={client} businessTimeZone="Asia/Shanghai"/>):<p>unmounted</p>}</PermissionAccessProvider>;
}
createRoot(document.getElementById("root")).render(<MemoryRouter initialEntries={[window.config.entry||"/crm/opportunities?opportunityId=1&view=editor"]}>
 <QueryClientProvider client={cache}><ConfirmationProvider><UnsavedChangesProvider><Harness/></UnsavedChangesProvider></ConfirmationProvider></QueryClientProvider></MemoryRouter>);
` }, outfile: bundle, bundle: true, platform: "browser", format: "iife", jsx: "automatic", logLevel: "silent" });
const context = await launch();
const results = [];
async function scenario(name, config, check) {
  if (process.argv[2] && !name.includes(process.argv[2])) return;
  const page = await context.newPage(), errors = [];
  page.setDefaultTimeout(10000); page.on("pageerror", error => errors.push(error.message));
  try {
    await page.setContent('<!doctype html><html lang="zh-CN"><title>商机回归</title><main><h1>商机回归验收</h1><div id="root"></div></main></html>');
    await page.evaluate(config => { window.config = config; }, config);
    await page.addStyleTag({ path: path.join(output, "app.css") }); await page.addScriptTag({ path: bundle });
    await check(page); assert.deepEqual(errors, []); results.push(name);
  } catch (error) {
    process.stderr.write(JSON.stringify({ errors, state:await page.evaluate(() => ({ route:window.route, dirty:window.dirty, text:document.body.innerText.slice(0,2500) })) })+"\n");
    await page.screenshot({ path: path.join(output, "failure.png"), fullPage: true }); throw new Error(name, { cause: error });
  }
  finally { await page.close(); }
}
const title = page => page.locator('input[name="title"]');
const submit = page => page.locator('form[aria-label="编辑商机"],form[aria-label="新建商机"]').evaluate(form => { form.requestSubmit(); form.requestSubmit(); });
const flush = page => page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
const changeScope = (page, change) => page.evaluate(change => {
  if (change === "route") window.navigate("/crm/opportunities?opportunityId=2&view=editor");
  if (change === "client") window.switchClient(); if (change === "permission") window.revoke(); if (change === "unmount") window.hide();
}, change);
try {
  await scenario("restricted quotation fields survive save", { quote: false }, async page => {
    await title(page).fill("修改基础资料"); await submit(page); await page.waitForFunction(() => window.requests.length === 1);
    assert.deepEqual(await page.evaluate(() => { const b=window.requests[0].request.body;return [b.productId,b.expectedCloseDate,b.estimatedAmount,b.quotationNo]; }), [7,"2026-12-15","10000000000000.0001","Q-1"]);
    assert.equal(await page.locator('[name="estimatedAmount"]').isDisabled(), true);
    assert.deepEqual(await page.evaluate(() => window.optionReads), ['customers']);
  });
  await scenario("read-only detail uses saved names without candidate queries", { readOnly: true }, async page => {
    await title(page).waitFor(); await flush(page);
    assert.equal(await title(page).isDisabled(), true);
    assert.equal(await page.locator('[name="crmCustomerId"] option:checked').textContent(), '示例客户');
    assert.deepEqual(await page.evaluate(() => window.optionReads), []);
  });
  for (const isNew of [false, true]) await scenario("save merges continued typing: " + isNew, { entry: isNew ? "/crm/opportunities?view=editor" : undefined }, async page => {
    if (isNew) await page.locator('[name="crmCustomerId"]').selectOption("1");
    await title(page).fill("待保存内容"); await submit(page); await page.waitForFunction(() => window.requests.length === 1);
    await title(page).fill("保存期间的中文续编"); await page.evaluate(() => { window.cache.invalidateQueries=()=>new Promise(()=>{});window.finish(); });
    await page.getByText("商机已保存，后续修改仍在草稿中。", { exact: true }).waitFor();
    assert.equal(await title(page).inputValue(), "保存期间的中文续编");
    await submit(page); await page.waitForFunction(() => window.requests.length === 2);
    assert.deepEqual(await page.evaluate(() => [window.requests[1].kind,window.requests[1].request.id,window.requests[1].request.body.expectedVersion]), ["save",isNew?3:1,isNew?1:2]);
    assert.equal(await page.evaluate(() => window.cache.getMutationCache().getAll().length), 0);
  });
  for (const change of ["route", "client", "permission", "unmount"]) {
    for (const fail of [false, true]) await scenario(`late save ${change} ${fail}`, {}, async page => {
      await title(page).fill("old write"); await submit(page); await page.waitForFunction(() => window.requests.length === 1);
      await changeScope(page, change); await flush(page); assert.equal(await page.evaluate(() => window.requests[0].signal.aborted), true);
      await page.evaluate(fail => window.finish(fail), fail); await flush(page);
      assert.equal(await page.getByText("late write failed", { exact: false }).count(), 0);
      if (change === "route") assert.equal(await title(page).inputValue(), "商机-2");
    });
    await scenario("late archive confirmation " + change, {}, async page => {
      await title(page).waitFor(); await page.getByRole("button", { name: "归档商机", exact: true }).click(); await page.getByRole("dialog").waitFor();
      await changeScope(page, change); await flush(page); await page.getByRole("dialog").getByRole("button", { name: "确认", exact: true }).click(); await flush(page);
      assert.equal(await page.evaluate(() => window.requests.length), 0);
    });
  }
  await scenario("failed detail load cannot expose old draft", {}, async page => {
    await title(page).fill("old draft"); await page.evaluate(() => { window.failRecord=2;window.navigate("/crm/opportunities?opportunityId=2&view=editor"); });
    await page.getByText("record load failed", { exact: false }).waitFor(); assert.equal(await title(page).count(), 0);
    assert.equal(await page.evaluate(() => window.requests.length), 0);
  });
  await scenario("archived history loads on demand and pages", { archived: true, entry: "/crm/opportunities" }, async page => {
    await page.getByRole("checkbox", { name: "含归档" }).click(); await page.waitForFunction(() => window.route.includes("archived=true")); await page.getByRole("button", { name: "查看历史", exact: true }).waitFor();
    assert.equal(await page.evaluate(() => window.historyReads.length), 0);
    await page.getByRole("button", { name: "查看历史", exact: true }).click(); await page.getByText("V3", { exact: true }).waitFor();
    await page.getByRole("button", { name: "下一页", exact: true }).click(); await page.getByText("V1", { exact: true }).waitFor();
    assert.equal(await page.evaluate(() => window.historyReads.at(-1).beforeId), 2);
    await page.getByRole("tab", { name: "商机资料", exact: true }).click(); await title(page).waitFor(); assert.equal(await title(page).isDisabled(), true);
    assert.equal(await page.getByRole("button", { name: "保存商机", exact: true }).count(), 0);
  });
  await scenario("accessible responsive editor", {}, async page => {
    await title(page).waitFor(); await page.addScriptTag({ path: require.resolve("axe-core/axe.min.js") });
    for (const width of [1440, 390]) {
      await page.setViewportSize({ width, height: 900 });
      const issues = await page.evaluate(() => axe.run(document,{runOnly:{type:"tag",values:["wcag2a","wcag2aa","wcag21aa"]}}).then(r=>r.violations.map(v=>({id:v.id,nodes:v.nodes.map(n=>n.target)}))));
      assert.deepEqual(issues, []);
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth<=innerWidth+1), true);
      await page.screenshot({ path: path.join(output, `editor-${width}.png`), fullPage: true });
    }
  });
  await scenario("runtime metrics load only while expanded", { metrics: true }, async page => {
    await page.locator('summary').filter({ hasText: "资源使用" }).waitFor();
    assert.equal(await page.getByText("4/4 核心路径正常", { exact: true }).count(), 1);
    assert.equal(await page.locator('.runtime-path-group[aria-label="程序文件"] .runtime-path-row').count(), 1);
    assert.equal(await page.locator('.runtime-path-group[aria-label="运行数据"] .runtime-path-row').count(), 3);
    assert.equal(await page.locator('[aria-label="SQLite"] .runtime-path-requirement').textContent(), "核心必需");
    assert.equal(await page.getByText("1 项可选依赖未就绪", { exact: true }).count(), 1);
    assert.equal(await page.evaluate(() => window.metricReads), 0);
    await page.locator('summary').filter({ hasText: "资源使用" }).click();
    await page.getByText("写入等待 P95", { exact: true }).waitFor();
    assert.equal(await page.getByText("3 / 16", { exact: true }).count(), 1);
    assert.equal(await page.getByText("2 / 16", { exact: true }).count(), 1);
    assert.equal(await page.getByText("JSON 缓冲预算", { exact: true }).count(), 1);
    await page.addScriptTag({ path: require.resolve("axe-core/axe.min.js") });
    for (const width of [1440, 390]) {
      await page.setViewportSize({ width, height: 900 });
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), true);
      assert.deepEqual(await page.evaluate(() => axe.run(document, { runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21aa"] } }).then(r => r.violations.map(v => v.id))), []);
      await page.screenshot({ path: path.join(output, `diagnostics-${width}.png`), fullPage: true });
    }
    await page.locator('summary').filter({ hasText: "资源使用" }).click();
    await page.waitForFunction(() => !window.cache.getQueryCache().find({ queryKey:["runtime-metrics"] }));
  });
  fs.writeFileSync(path.join(output, "summary.json"), JSON.stringify({ passed: results.length, results }, null, 2));
  process.stdout.write(`Sales opportunity lifecycle passed (${results.length} cases).\n`);
} finally { await context.close(); }
