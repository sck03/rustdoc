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
import {QueryClient,QueryClientProvider} from "@tanstack/react-query";import {MemoryRouter,useLocation,useNavigate} from "react-router-dom";
import {SupplierDirectoryPage} from ${source("features/suppliers/SupplierDirectoryPage.tsx")};
import {CustomerFollowUpPage} from ${source("features/crm/CustomerFollowUpPage.tsx")};
import {PermissionAccessProvider} from ${source("app/PermissionAccessContext.tsx")};
import {permissionResources,permissionActions} from ${source("app/permissionCatalog.ts")};
import {ConfirmationProvider} from ${source("ui/ConfirmationProvider.tsx")};
import {UnsavedChangesProvider,useHasUnsavedChanges} from ${source("ui/unsavedChangesGuard.tsx")};
import {useBusinessDateSessionRefresh} from ${source("app/useBusinessDateSessionRefresh.ts")};
window.requests=[];window.contactQueries=[];window.meCalls=0;
const queries=new QueryClient({defaultOptions:{queries:{retry:false},mutations:{retry:false}}});window.queries=queries;
const page=(items,number=1)=>({items,pageNumber:number,pageSize:20,totalCount:21,totalPages:2});
let party={id:1,name:"original",versionNumber:1,status:"考察中"},contact={id:21,name:"original contact",versionNumber:1,supplierCompanyId:1,crmCustomerId:1};
const getParty=async ({id})=>({...party,id,name:id===2?"second record":party.name});
const getContacts=async (request,init)=>{window.contactQueries.push(request);if(window.refreshFailure)throw new Error("directory unavailable");return page(request.pageNumber===2||!window.mode.startsWith("supplier")?[contact]:[{...contact,id:1}],request.pageNumber)};
const save=(request,init)=>new Promise((resolve,reject)=>window.requests.push({request,signal:init.signal,resolve,reject}));
const client={getSupplier:getParty,getCrmCustomer:getParty,querySuppliers:async()=>page([party]),queryCrmCustomers:async()=>page([party]),
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
function Harness(){const [shown,setShown]=useState(true),navigate=useNavigate(),location=useLocation();window.navigate=navigate;window.hide=()=>setShown(false);window.route=location.search;
window.dirty=useHasUnsavedChanges();return !shown?<div>unmounted</div>:window.mode.startsWith("session")?<SessionHarness/>:window.mode.startsWith("supplier")?<SupplierDirectoryPage businessDate="2026-10-25" client={client}/>:<CustomerFollowUpPage businessTimeZone="Asia/Shanghai" client={client}/>}
const mode=window.mode,isNew=mode.endsWith("new")&&!mode.includes("contacts"),contacts=mode.includes("contacts");
const entry=mode.startsWith("supplier")?"/suppliers?view="+(contacts?"contacts":"profile")+(isNew?"&new=true":"&supplierId=1"):"/crm?view="+(contacts?"contacts":"profile")+(isNew?"":"&customerId=1");
const permissions=Object.values(permissionResources).flatMap(resourceKey=>Object.values(permissionActions).map(action=>({resourceKey,action,dataScope:"all"})));
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
    if (!mode.startsWith("session")) await page.locator(`input[name="${mode.includes("contacts") ? "contactName" : "name"}"]`).waitFor();
    await check(page); assert.deepEqual(errors, []); passed++;
  } catch (error) { await page.screenshot({ path: path.join(output, `failure-${passed}.png`), fullPage: true }); throw error; }
  finally { await page.close(); }
}
const submit = page => page.locator('form:has(button[type="submit"]:text-matches("保存|建立|添加"))').evaluate(form => { form.requestSubmit(); form.requestSubmit(); });
try {
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
    await page.evaluate(() => { window.refreshFailure = true; window.queries.invalidateQueries = async () => { throw new Error("directory unavailable"); }; window.finish(); });
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
