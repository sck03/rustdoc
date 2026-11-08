import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";
import { locateChromeForTesting } from "./lib/chromium-executable.mjs";

const root = path.resolve(import.meta.dirname, ".."), web = path.join(root, "apps/export-doc-web");
const require = createRequire(path.join(web, "package.json")), output = path.join(root, "artifacts/party-save-workspace-ui", String(Date.now()));
fs.mkdirSync(path.join(output, "Temp"), { recursive: true });
Object.assign(process.env, { TEMP: path.join(output, "Temp"), TMP: path.join(output, "Temp") });
const bundle = path.join(output, "app.js"), source = name => JSON.stringify(path.join(web, "src", name).replaceAll("\\", "/"));
await require("esbuild").build({ stdin: { loader: "tsx", resolveDir: web, contents: `
import React,{useRef,useState} from "react";import {createRoot} from "react-dom/client";
import {QueryClient,QueryClientProvider,useQuery} from "@tanstack/react-query";import {MemoryRouter,Routes,Route,useLocation,useNavigate} from "react-router-dom";
import {queryKeys} from ${source("api/queryKeys.ts")};
import {MasterDataRoute} from ${source("features/master-data/MasterDataPages.tsx")};
import {useOfficeOperation} from ${source("features/office/useOfficeData.ts")};
import {SupplierDirectoryPage} from ${source("features/suppliers/SupplierDirectoryPage.tsx")};
import {CustomerFollowUpPage} from ${source("features/crm/CustomerFollowUpPage.tsx")};
import {ApprovalDocument} from ${source("features/oa/ApprovalDocument.tsx")};
import {PermissionAccessProvider} from ${source("app/PermissionAccessContext.tsx")};
import {permissionResources,permissionActions} from ${source("app/permissionCatalog.ts")};
import {ConfirmationProvider} from ${source("ui/ConfirmationProvider.tsx")};
import {UnsavedChangesProvider,useHasUnsavedChanges} from ${source("ui/unsavedChangesGuard.tsx")};
import {useBusinessDateSessionRefresh} from ${source("app/useBusinessDateSessionRefresh.ts")};
window.requests=[];window.contactQueries=[];window.meCalls=0;
window.previews=[];window.imports=[];window.documentCalls=0;
const queries=new QueryClient({defaultOptions:{queries:{retry:false},mutations:{retry:false}}});window.queries=queries;
const page=(items,number=1)=>({items,pageNumber:number,pageSize:20,totalCount:21,totalPages:2});
let party={id:1,name:"original",versionNumber:1,status:"考察中"},contact={id:21,name:"original contact",versionNumber:1,supplierCompanyId:1,crmCustomerId:1};
const getParty=async ({id})=>({...party,id,name:id===2?"second record":party.name});
const getContacts=async (request,init)=>{window.contactQueries.push(request);if(window.refreshFailure)throw new Error("directory unavailable");return page(request.pageNumber===2||!window.mode.startsWith("supplier")?[contact]:[{...contact,id:1}],request.pageNumber)};
const save=(request,init)=>new Promise((resolve,reject)=>window.requests.push({request,signal:init.signal,resolve,reject}));
const preview=(request,init)=>new Promise((resolve,reject)=>window.previews.push({request,signal:init.signal,resolve,reject}));
const commit=(request,init)=>new Promise((resolve,reject)=>window.imports.push({request,signal:init.signal,resolve,reject}));
const client={getSupplier:getParty,getCrmCustomer:getParty,querySuppliers:async()=>page([party]),queryCrmCustomers:async()=>page([party]),
listCustomersPage:async()=>page([{id:17,customerNameEN:"customer row",rowVersion:"native:4"}]),
listExportersPage:()=>new Promise(resolve=>window.finishExporters=()=>resolve(page([{id:18,exporterNameEN:"exporter row",rowVersion:"native:5"}]))),
deleteCustomer:save,deleteExporter:save,
admitSupplier:save,deactivateSupplier:save,restoreSupplier:save,deleteSupplier:save,setPrimarySupplierContact:save,deleteSupplierContact:save,
deactivateCrmCustomer:save,restoreCrmCustomer:save,deleteCrmCustomer:save,setPrimaryCrmContact:save,deleteCrmContact:save,
previewSupplierImport:preview,previewCrmCustomerImport:preview,importSuppliers:commit,importCrmCustomers:commit,
querySupplierContacts:getContacts,queryCrmContacts:getContacts,updateSupplier:save,createSupplier:save,updateSupplierContact:save,createSupplierContact:save,
updateCrmCustomer:save,createCrmCustomer:save,updateCrmContact:save,createCrmContact:save,getCurrentUser:async()=>{window.meCalls++;return {id:1,capabilities:{},businessDateValidUntilUtc:"2099-01-01T00:00:00Z"}}};
window.finish=(index=0)=>{const pending=window.requests[index],isContact="supplierCompanyId" in pending.request.body||"crmCustomerId" in pending.request.body;
const saved={...(isContact?contact:party),...pending.request.body,id:pending.request.id|| (isContact?22:7),versionNumber:(pending.request.body.expectedVersion||0)+1};
if(isContact)contact=saved;else party=saved;pending.resolve(saved)};
function SessionHarness(){const user={id:1,capabilities:{},businessDateValidUntilUtc:"2099-01-01T00:00:00Z"};
const [session,store]=useState(window.mode==="session-restored"?{accessToken:"restored",user}:null),sessionRef=useRef(session),validatedUserTokenRef=useRef();
const setSession=value=>{sessionRef.current=value;validatedUserTokenRef.current=value.accessToken;store(value)};
window.login=()=>setSession({accessToken:"fresh",user});window.renew=()=>setSession({accessToken:"renewed",user});
useBusinessDateSessionRefresh({client,desktopContextLoading:false,queryClient:queries,session,sessionRef,setSession,validatedUserTokenRef});return <div>session harness</div>}
const approvalApi={submission:async()=>{window.documentCalls++;if(window.documentCalls===2)throw new Error("document unavailable");return {documentStatus:window.documentCalls<4?"Running":"Ready",submissionNumber:1}}};
function OfficeHarness(){const operation=useOfficeOperation();useQuery({queryKey:["office","refresh-test"],queryFn:async()=>{if(window.refreshFailure)throw new Error("directory unavailable");return []}});
return <><button onClick={()=>operation.run(async()=>{window.refreshFailure=true;window.writes=(window.writes||0)+1;return 1},()=>{window.applied=true})}>保存操作</button><div>{operation.error}</div></>}
function Harness(){const [shown,setShown]=useState(true),navigate=useNavigate(),location=useLocation();window.navigate=navigate;window.hide=()=>setShown(false);window.route=location.pathname+location.search;
useQuery({queryKey:[...queryKeys.crmCustomersRoot(),"refresh-test"],enabled:window.mode==="crm-profile",queryFn:async()=>{if(window.refreshFailure)throw new Error("directory unavailable");return []}});
if(window.mode==="master")return <Routes><Route path="/master-data/:entityKey" element={<MasterDataRoute client={client}/>}/></Routes>;
if(window.mode==="office-refresh")return <OfficeHarness/>;
window.dirty=useHasUnsavedChanges();return !shown?<div>unmounted</div>:window.mode==="approval"?<ApprovalDocument api={approvalApi} user={{id:1}} row={{id:1,status:"Approved",versionNumber:1,submissionId:1}} scope="test"/>:window.mode.startsWith("session")?<SessionHarness/>:window.mode.startsWith("supplier")?<SupplierDirectoryPage businessDate="2026-10-25" client={client}/>:<CustomerFollowUpPage businessTimeZone="Asia/Shanghai" client={client}/>}
const mode=window.mode,isNew=mode.endsWith("new")&&!mode.includes("contacts"),contacts=mode.includes("contacts");
const view=mode.endsWith("import")?"import":contacts?"contacts":"profile";
const entry=mode==="master"?"/master-data/customers":mode.startsWith("supplier")?"/suppliers?view="+view+(isNew?"&new=true":"&supplierId=1"):"/crm?view="+view+(isNew?"":"&customerId=1");
const permissions=Object.values(permissionResources).flatMap(resourceKey=>Object.values(permissionActions).map(action=>({resourceKey,action,dataScope:"all"})));
permissions.push(...["view","operate","manage"].map(action=>({resourceKey:"document.master-data",action,dataScope:"all"})));
createRoot(document.getElementById("root")).render(<MemoryRouter initialEntries={[entry]}><QueryClientProvider client={queries}><PermissionAccessProvider permissions={permissions}>
<ConfirmationProvider><UnsavedChangesProvider><Harness/></UnsavedChangesProvider></ConfirmationProvider></PermissionAccessProvider></QueryClientProvider></MemoryRouter>);
` }, outfile: bundle, bundle: true, platform: "browser", format: "iife", jsx: "automatic", logLevel: "silent" });
const context = await require("playwright").chromium.launchPersistentContext(path.join(output, "Chrome"), { executablePath: locateChromeForTesting(root), headless: true });
let passed = 0;
async function scenario(mode, check) {
  const page = await context.newPage(), errors = []; page.setDefaultTimeout(7000); page.on("pageerror", error => errors.push(error.message));
  try {
    await page.setContent('<!doctype html><html lang="zh-CN"><title>Party save regression</title><div id="root"></div></html>');
    await page.evaluate(value => window.mode = value, mode); await page.addScriptTag({ path: bundle });
    if (mode === "supplier-contacts") {
      await page.getByRole("button", { name: "下一页", exact: true }).last().click();
      await page.getByRole("button", { name: "编辑", exact: true }).click();
    }
    if (mode === "supplier-contacts-new") await page.getByRole("button", { name: "新增联系人", exact: true }).click();
    if (mode === "crm-contacts-new") await page.getByRole("button", { name: "新建", exact: true }).click();
    if (!mode.startsWith("session") && !mode.endsWith("import") && !["approval","master","office-refresh"].includes(mode)) await page.locator(`input[name="${mode.includes("contacts") ? "contactName" : "name"}"]`).waitFor();
    await check(page); assert.deepEqual(errors, []); passed++;
  } catch (error) { await page.screenshot({ path: path.join(output, `failure-${passed}.png`), fullPage: true }); throw error; }
  finally { await page.close(); }
}
const submit = page => page.locator('form:has(button[type="submit"]:text-matches("保存|建立|添加"))').evaluate(form => { form.requestSubmit(); form.requestSubmit(); });
try {
  await scenario("office-refresh", async page => {
    await page.getByRole("button", {name:"保存操作"}).click();
    await page.getByText(/操作已成功，列表刷新失败/).waitFor();
    assert.deepEqual(await page.evaluate(()=>[window.writes,window.applied]), [1,true]);
  });
  await scenario("master", async page => {
    const row = page.getByRole("row").filter({hasText:"customer row"});
    await row.waitFor();
    await row.focus(); await page.keyboard.press("Delete");
    await page.getByRole("dialog").waitFor();
    await page.evaluate(()=>window.navigate("/master-data/exporters"));
    await page.waitForFunction(()=>window.finishExporters);
    assert.equal(await page.getByRole("dialog").count(),0);
    assert.equal(await page.getByRole("row").filter({hasText:"customer row"}).count(),0);
    await page.keyboard.press("Delete"); assert.equal(await page.evaluate(()=>window.requests.length),0);
    await page.evaluate(()=>window.finishExporters());
    await page.getByRole("row").filter({hasText:"exporter row"}).focus(); await page.keyboard.press("Delete");
    await page.getByRole("button",{name:"确认删除",exact:true}).click();
    await page.waitForFunction(()=>window.requests.length===1);
    assert.deepEqual(await page.evaluate(()=>window.requests[0].request),{id:18,rowVersion:"native:5"});
    await page.evaluate(()=>window.navigate("/master-data/customers"));
    await row.waitFor(); await page.evaluate(()=>window.requests[0].resolve({message:"old deletion result"}));
    assert.equal(await page.evaluate(()=>window.requests[0].signal.aborted),true);
    assert.equal(await page.getByText("old deletion result").count(),0);
  });
  for (const mode of ["supplier-import", "crm-import"]) for (const oldFirst of [false, true]) await scenario(mode, async page => {
    const file = name => ({ name: `${name}.csv`, mimeType: "text/csv", buffer: Buffer.from("name\n" + name) });
    await page.locator('input[type="file"]').setInputFiles(file("A"));
    await page.locator('input[type="file"]').setInputFiles(file("B"));
    await page.waitForFunction(() => window.previews.length === 2);
    assert.equal(await page.evaluate(() => window.previews[0].signal.aborted), true);
    const finish = index => page.evaluate(index => window.previews[index].resolve({ previewId: index ? "B" : "A", validRows: 1, totalRows: 1, duplicateRows: 0, rows: [{ rowNumber: 2, name: index ? "B" : "A", error: "", isDuplicate: false }] }), index);
    const button = page.getByRole("button", { name: /确认导入有效行|处理中/ });
    if (oldFirst) { await finish(0); assert.equal(await button.isDisabled(), true); }
    await finish(1); await page.getByRole("cell", { name: "B", exact: true }).waitFor();
    if (!oldFirst) await finish(0);
    await button.click(); await page.waitForFunction(() => window.imports.length === 1);
    assert.equal(await page.evaluate(() => window.imports[0].request.body.previewId), "B");
    assert.equal(await page.locator('input[type="file"]').isDisabled(), true);
    await page.evaluate(() => window.imports[0].resolve({createdCustomers:1,createdSuppliers:1,createdContacts:0,skippedRows:0,skippedDuplicates:0}));
    await page.getByText(/已导入 1 家/).waitFor(); assert.equal(await button.isDisabled(), true);
  });
  for (const mode of ["supplier-profile", "crm-profile", "supplier-contacts", "crm-contacts"]) for (const deletion of [false, true]) await scenario(mode, async page => {
    const contact = mode.includes("contacts"), supplier = mode.startsWith("supplier");
    await page.getByRole("button", { name: deletion ? supplier ? "删除" : contact ? "删除联系人" : "删除客户" : contact ? "设为主要联系人" : supplier ? "停用供应商" : "停用客户", exact: true }).click();
    if (deletion || !contact) await page.getByRole("dialog").getByRole("button", { name: deletion ? "确认删除" : supplier ? "停用供应商" : "确认停用", exact: true }).click();
    await page.waitForFunction(() => window.requests.length === 1);
    await page.evaluate(supplier => window.navigate(supplier ? "/suppliers?view=profile&supplierId=2" : "/crm?view=profile&customerId=2"), supplier);
    await page.waitForFunction(() => document.querySelector('input[name="name"]')?.value === "second record");
    await page.locator('input[name="name"]').fill("second draft");
    await page.evaluate(() => window.requests[0].resolve({id:1,name:"old result",versionNumber:2,status:"停用",isPrimary:true}));
    await page.waitForFunction(() => window.requests[0].signal.aborted);
    assert.equal(await page.locator('input[name="name"]').inputValue(), "second draft");
    assert.equal(await page.getByText(/已删除|已停用|已切换/).count(), 0);
  });
  await scenario("approval", async page => {
    await page.getByText("document unavailable", { exact: false }).waitFor();
    const calls = await page.evaluate(() => window.documentCalls);
    await page.waitForTimeout(3300);
    assert.equal(await page.evaluate(() => window.documentCalls), calls, "cached Running must not restart polling after failure");
    await page.getByRole("button", {name:"重试",exact:true}).click();
    await page.getByRole("button", {name:"下载审批单 PDF",exact:true}).waitFor();
    assert.equal(await page.evaluate(() => window.documentCalls), 4);
  });
  for (const mode of ["supplier-profile", "crm-profile", "supplier-contacts", "crm-contacts", "supplier-new", "crm-new", "supplier-contacts-new", "crm-contacts-new"]) {
    await scenario(mode, async page => {
      const input = page.locator(`input[name="${mode.includes("contacts") ? "contactName" : "name"}"]`);
      await input.fill("submitted"); await submit(page); await page.waitForFunction(() => window.requests.length === 1);
      await input.fill("newer unsaved"); await page.evaluate(() => window.finish());
      await page.waitForFunction(() => !document.querySelector('button[type="submit"]:disabled'));
      assert.equal(await input.inputValue(), "newer unsaved"); assert.equal(await page.evaluate(() => window.dirty), true);
      assert.equal(await page.getByText(/服务器上的.*已更新/).count(), 0);
      await submit(page); await page.waitForFunction(() => window.requests.length === 2);
      assert.equal(await page.evaluate(() => window.requests[1].request.body.expectedVersion), mode.endsWith("new") ? 1 : 2);
      assert.equal(await page.evaluate(() => window.requests[1].request.body.name), "newer unsaved");
      await page.evaluate(() => window.finish(1)); await page.waitForFunction(() => !window.dirty);
      if (mode === "supplier-contacts") assert.equal(await page.evaluate(() => window.contactQueries.at(-1).pageNumber), 2);
    });
  }
  for (const mode of ["supplier-profile", "crm-profile"]) for (const outcome of ["resolve", "reject", "unmount"]) {
    await scenario(mode, async page => {
      await submit(page); await page.waitForFunction(() => window.requests.length === 1);
      await page.evaluate(({ mode, outcome }) => outcome === "unmount" ? window.hide() : window.navigate(mode.startsWith("supplier") ? "/suppliers?view=profile&supplierId=2" : "/crm?view=profile&customerId=2"), { mode, outcome });
      if (outcome !== "unmount") await page.waitForFunction(() => document.querySelector('input[name="name"]')?.value === "second record");
      else await page.getByText("unmounted", { exact: true }).waitFor();
      await page.evaluate(outcome => outcome === "resolve" ? window.finish() : window.requests[0].reject(new Error("late old failure")), outcome);
      await page.waitForFunction(() => window.requests[0].signal.aborted);
      assert.equal(await page.getByText("late old failure", { exact: false }).count(), 0);
      if (outcome !== "unmount") assert.equal(await page.locator('input[name="name"]').inputValue(), "second record");
    });
  }
  for (const mode of ["supplier-contacts", "crm-contacts", "crm-profile"]) await scenario(mode, async page => {
    await submit(page); await page.waitForFunction(() => window.requests.length === 1);
    await page.evaluate(() => { window.refreshFailure = true; window.finish(); });
    await page.getByText(/已保存，但目录刷新失败/).waitFor(); assert.equal(await page.evaluate(() => window.dirty), false);
    await submit(page); await page.waitForFunction(() => window.requests.length === 2);
    assert.equal(await page.evaluate(() => window.requests[1].request.body.expectedVersion), 2);
  });
  await scenario("session-new", async page => {
    await page.getByText("session harness").waitFor(); await page.evaluate(() => window.login());
    await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
    assert.equal(await page.evaluate(() => window.meCalls), 0); await page.evaluate(() => window.renew());
    await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
    assert.equal(await page.evaluate(() => window.meCalls), 0); await page.evaluate(() => window.dispatchEvent(new Event("online")));
    await page.waitForFunction(() => window.meCalls === 1);
  });
  await scenario("session-restored", async page => { await page.waitForFunction(() => window.meCalls === 1); });
  console.log(`Party save workspace UI: ${passed} passed; ${output}`);
} finally { await context.close(); }
