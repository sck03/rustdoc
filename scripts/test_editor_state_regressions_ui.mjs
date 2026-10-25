import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";
import { locateChromeForTesting } from "./lib/chromium-executable.mjs";

const root = path.resolve(import.meta.dirname, ".."), web = path.join(root, "apps/export-doc-web");
const require = createRequire(path.join(web, "package.json"));
const output = path.join(root, "artifacts/editor-state-regressions-ui", String(Date.now()));
fs.mkdirSync(path.join(output, "Temp"), { recursive: true });
Object.assign(process.env, { TEMP: path.join(output, "Temp"), TMP: path.join(output, "Temp") });
const source = name => JSON.stringify(path.join(web, "src", name).replaceAll("\\", "/"));
const bundle = path.join(output, "app.js");
await require("esbuild").build({ stdin: { loader: "tsx", resolveDir: web, contents: `
import React,{useState} from "react";import {createRoot} from "react-dom/client";
import {QueryClient,QueryClientProvider} from "@tanstack/react-query";
import {MemoryRouter,Routes,Route,useLocation,useNavigate,useParams} from "react-router-dom";
import {MasterDataEditorPage} from ${source("features/master-data/MasterDataEditorPage.tsx")};
import {masterDataConfigs} from ${source("features/master-data/masterDataConfigs.ts")};
import {PaymentEditorPage} from ${source("features/payments/PaymentEditorPage.tsx")};
import {createEmptyPayment} from ${source("features/payments/paymentModel.ts")};
import {InvoiceItemCellInput} from ${source("features/invoices/InvoiceItemCellInput.tsx")};
import {PersonnelFormDialog} from ${source("features/office/PersonnelFormDialog.tsx")};
import {AnnouncementDialog} from ${source("features/communication/AnnouncementDialog.tsx")};
import {PermissionAccessProvider} from ${source("app/PermissionAccessContext.tsx")};
import {permissionResources,permissionActions} from ${source("app/permissionCatalog.ts")};
import {ConfirmationProvider} from ${source("ui/ConfirmationProvider.tsx")};
import {UnsavedChangesProvider,useHasUnsavedChanges} from ${source("ui/unsavedChangesGuard.tsx")};
import {queryKeys} from ${source("api/queryKeys.ts")};
window.requests=[];window.hsRequests=[];
const queries=new QueryClient({defaultOptions:{queries:{retry:false,staleTime:30000},mutations:{retry:false}}});window.queries=queries;
const page=(items,size=200)=>({items,pageNumber:1,pageSize:size,totalCount:items.length,totalPages:1,hasPreviousPage:false,hasNextPage:false});
const records=new Map();
const get=(entity,id)=>{const key=entity+":"+id;if(!records.has(key)){const config=masterDataConfigs.find(c=>c.key===entity);records.set(key,{...config.emptyRecord(),id:Number(id),name:"payee "+id,nameEN:"product "+id,productCode:"SKU-"+id,hsCode:"",notes:"",rowVersion:"native:1"});}return records.get(key)};
queries.setQueryData(queryKeys.masterDataList("products",1,200,""),page([get("products",1)]));
queries.setQueryData(queryKeys.masterDataList("units",1,200,""),page([{id:1,nameEN:"KG",nameCN:"千克"}]));
const deferred=(kind,request,init)=>new Promise((resolve,reject)=>window.requests.push({kind,request,signal:init.signal,resolve,reject}));
const client={listProducts:async()=>page([get("products",1)]),listUnits:async()=>[{id:1,nameEN:"KG",nameCN:"千克"}],
  getPayee:async({id})=>get("payees",id),getProduct:async({id})=>get("products",id),
  updatePayee:(r,i)=>deferred("payees",r,i),createPayee:(r,i)=>deferred("payees",r,i),
  deletePayee:(r,i)=>deferred("payees-delete",r,i),
  updateProduct:(r,i)=>deferred("products",r,i),createProduct:(r,i)=>deferred("products",r,i),
  listCustomOptions:async({optionType})=>({optionType,options:[]}),
  getSettings:async()=>({settings:{system:{documentFieldLabels:{}},report:{}},secrets:{},storagePolicy:""}),
  listReportTemplates:async()=>[],listPayeesPage:async()=>page([get("payees",1)]),
  getPayment:async({id})=>records.get("payment:"+id)??({...createEmptyPayment("2026-10-25"),id,voucherNo:"payment "+id,rowVersion:"native:1"}),
  updatePayment:(r,i)=>deferred("payment",r,i),createPayment:(r,i)=>deferred("payment",r,i),
  getPersonnelOptions:async()=>({positions:[],departments:[]}),listAnnouncementDepartments:async()=>[],
  listHsCodes:(request,init)=>request.keyword?new Promise((resolve,reject)=>window.hsRequests.push({request,signal:init.signal,resolve,reject})):Promise.resolve(page([])),
};
window.finish=(index=0)=>{const pending=window.requests[index],id=pending.request.id||7;
  const saved={...pending.request.body,id,rowVersion:"native:"+(Number(pending.request.body.rowVersion?.split(":")[1]||0)+1)};
  records.set(pending.kind+":"+id,saved);pending.resolve(pending.kind==="payment"?{id,payment:saved}:saved);};
window.finishHs=(keyword)=>window.hsRequests.filter(p=>p.request.keyword===keyword).forEach(p=>p.resolve(page([{id:1,code:keyword,normalizedCode:keyword,status:"Active",requiresAnnualReview:false,description:"HS "+keyword,unit:"千克"}])));
function Master({mode}){const {entityKey}=useParams();return <MasterDataEditorPage client={client} config={masterDataConfigs.find(c=>c.key===entityKey)} mode={mode} canOperate canManage/>}
const user={id:1,companyScope:"company",departmentId:"D1",businessDate:"2026-10-25",businessTimeZone:"Asia/Shanghai",capabilities:{canManageUsers:true}};
function Numeric(){const [quantity,setQuantity]=useState(0);return <><InvoiceItemCellInput item={{quantity}} index={1} column={{field:"quantity",kind:"number",ariaName:"数量"}} selected options={["100"]} onFocus={()=>{}} onMouseDown={()=>{}} onChange={setQuantity}/><button>next</button><output>{quantity}</output></>}
function Harness(){const navigate=useNavigate(),location=useLocation(),[shown,setShown]=useState(true);
  window.navigate=navigate;window.hide=()=>setShown(false);window.route=location.pathname;window.dirty=useHasUnsavedChanges();
  if(!shown)return <div>unmounted</div>;
  if(window.mode==="numeric")return <Numeric/>;
  if(window.mode==="personnel")return <PersonnelFormDialog client={client} user={user} departments={[]} onClose={()=>setShown(false)} onSaved={()=>{}}/>;
  if(window.mode==="announcement")return <AnnouncementDialog client={client} user={user} record={{id:1,requestKey:"ann-1",versionNumber:1,title:"existing",body:"body",startsAt:"2026-10-25T00:00:00Z",expiresAt:"2026-11-25T00:00:00Z",attachments:Array.from({length:20},()=>({sizeBytes:1}))}} onClose={()=>setShown(false)} onSaved={()=>{}}/>;
  return <Routes><Route path="/master-data/:entityKey/new" element={<Master mode="new"/>}/><Route path="/master-data/:entityKey/:recordKey" element={<Master mode="edit"/>}/>
    <Route path="/payments/new" element={<PaymentEditorPage client={client} businessDate="2026-10-25" mode="new"/>}/><Route path="/payments/:paymentId" element={<PaymentEditorPage client={client} businessDate="2026-10-25" mode="edit"/>}/></Routes>;
}
const permissions=Object.values(permissionResources).flatMap(resourceKey=>Object.values(permissionActions).map(action=>({resourceKey,action,dataScope:"all"})));
permissions.push(...["view","operate","manage"].map(action=>({resourceKey:"document.payments",action,dataScope:"all"})));
createRoot(document.getElementById("root")).render(<MemoryRouter initialEntries={[window.entry]}><QueryClientProvider client={queries}><PermissionAccessProvider permissions={permissions} subject={user}>
  <ConfirmationProvider><UnsavedChangesProvider><Harness/></UnsavedChangesProvider></ConfirmationProvider></PermissionAccessProvider></QueryClientProvider></MemoryRouter>);
` }, outfile: bundle, bundle: true, platform: "browser", format: "iife", jsx: "automatic", logLevel: "silent" });
const context = await require("playwright").chromium.launchPersistentContext(path.join(output, "Chrome"), { executablePath: locateChromeForTesting(root), headless: true });
let passed = 0;
async function scenario(mode, entry, check) {
  const page = await context.newPage(), errors = []; page.setDefaultTimeout(7000); page.on("pageerror", error => errors.push(error.message));
  try {
    await page.setContent('<!doctype html><html lang="zh-CN"><title>Editor state regression</title><div id="root"></div></html>');
    await page.evaluate(values => Object.assign(window, values), { mode, entry }); await page.addScriptTag({ path: bundle });
    await check(page); assert.deepEqual(errors, []); passed++;
  } catch (error) { await page.screenshot({ path: path.join(output, `failure-${passed}.png`), fullPage: true }); throw error; }
  finally { await page.close(); }
}
const submit = page => page.locator("form.entity-form").evaluate(form => { form.requestSubmit(); form.requestSubmit(); });
const field = (page, label) => page.getByLabel(new RegExp(`^${label}(?:必填)?$`));
try {
  for (const [entity, label] of [["payees", "名称"], ["products", "英文品名"]]) for (const fresh of [false, true]) {
    await scenario("master", `/master-data/${entity}/${fresh ? "new" : "1"}`, async page => {
      const input = field(page, label); await input.fill("submitted"); await submit(page);
      await page.waitForFunction(() => window.requests.length === 1); await input.fill("newer unsaved");
      if (entity === "products") await field(page, "默认单价").fill("9.5");
      await page.evaluate(() => window.finish());
      await page.waitForFunction(() => !document.querySelector('button[type="submit"]:disabled'));
      if (fresh) await page.waitForFunction(() => window.route.endsWith("/7"));
      assert.equal(await input.inputValue(), "newer unsaved"); assert.equal(await page.evaluate(() => window.dirty), true);
      assert.equal(await page.getByText(/服务器上的.*已更新/).count(), 0);
      await submit(page); await page.waitForFunction(() => window.requests.length === 2);
      assert.equal(await page.evaluate(() => window.requests[1].request.body.rowVersion), fresh ? "native:1" : "native:2");
      assert.equal(await page.evaluate(entity => window.requests[1].request.body[entity === "payees" ? "name" : "nameEN"], entity), "newer unsaved");
      if (entity === "products") assert.equal(await page.evaluate(() => window.requests[1].request.body.defaultPrice), 9.5);
      await page.evaluate(() => window.finish(1)); await page.waitForFunction(() => !window.dirty);
      assert.equal(await page.evaluate(() => Array.isArray(window.queries.getQueryData(["master-data", "products", "list", { pageNumber: 1, pageSize: 200, keyword: "" }]))), false);
    });
  }
  await scenario("master", "/master-data/products/1", async page => {
    const code = field(page, "HS 编码"), name = field(page, "英文品名");
    await code.fill("12345678"); await name.click(); await page.waitForFunction(() => window.hsRequests.length > 0);
    await name.fill("edited during HS lookup"); await page.evaluate(() => window.finishHs("12345678"));
    await page.getByText("已套用本地 HS 编码", { exact: false }).waitFor(); assert.equal(await name.inputValue(), "edited during HS lookup");
    await page.getByRole("textbox", { name: /^描述/ }).fill("");
    await code.fill("87654321"); await name.click(); await page.waitForFunction(() => window.hsRequests.some(p => p.request.keyword === "87654321"));
    await code.fill("11223344"); await name.click(); await page.waitForFunction(() => window.hsRequests.some(p => p.request.keyword === "11223344"));
    await page.evaluate(() => window.finishHs("87654321")); assert.equal(await code.inputValue(), "11223344");
    await page.evaluate(() => window.finishHs("11223344")); await page.getByText("已套用本地 HS 编码 11223344", { exact: false }).waitFor();
  });
  for (const outcome of ["resolve", "reject", "unmount"]) for (const kind of ["master", "payment"]) {
    const entry = kind === "master" ? "/master-data/payees/1" : "/payments/1", label = kind === "master" ? "名称" : "付款单号";
    await scenario(kind, entry, async page => {
      await field(page, label).fill("submitted"); await submit(page); await page.waitForFunction(() => window.requests.length === 1);
      await page.evaluate(({ kind, outcome }) => outcome === "unmount" ? window.hide() : window.navigate(kind === "master" ? "/master-data/payees/2" : "/payments/2"), { kind, outcome });
      if (outcome !== "unmount") await page.waitForFunction(({ label }) => Array.from(document.querySelectorAll("input")).some(i => i.value === (label === "名称" ? "payee 2" : "payment 2")), { label });
      else await page.getByText("unmounted").waitFor();
      await page.evaluate(outcome => outcome === "resolve" ? window.finish() : window.requests[0].reject(new Error("late old failure")), outcome);
      await page.waitForFunction(() => window.requests[0].signal.aborted);
      if (outcome !== "unmount") assert.equal(await field(page, label).inputValue(), kind === "master" ? "payee 2" : "payment 2");
      assert.equal(await page.getByText("late old failure", { exact: false }).count(), 0);
    });
  }
  await scenario("master", "/master-data/payees/1", async page => {
    await field(page, "名称").fill("saved"); await submit(page); await page.waitForFunction(() => window.requests.length === 1);
    await page.evaluate(() => { window.queries.invalidateQueries = async () => { throw new Error("refresh unavailable"); }; window.finish(); });
    await page.getByText(/已保存，但目录刷新失败/).waitFor(); assert.equal(await page.evaluate(() => window.dirty), false);
    await submit(page); await page.waitForFunction(() => window.requests.length === 2);
    assert.equal(await page.evaluate(() => window.requests[1].request.body.rowVersion), "native:2");
  });
  await scenario("master", "/master-data/payees/1", async page => {
    await field(page, "名称").waitFor(); await page.getByRole("button", { name: "删除", exact: true }).click();
    await page.getByRole("button", { name: "确认删除", exact: true }).click(); await page.waitForFunction(() => window.requests.length === 1);
    await page.evaluate(() => window.requests[0].reject(new Error("still referenced")));
    await page.getByText("still referenced", { exact: false }).waitFor(); assert.equal(await page.getByRole("dialog").count(), 0);
  });
  await scenario("numeric", "/", async page => {
    const input = page.getByRole("spinbutton"); await input.fill("1"); await input.press("Tab");
    assert.equal(await input.inputValue(), "1");
    await input.fill("12"); assert.equal(await input.inputValue(), "12");
    await input.fill("1"); await page.getByRole("option", { name: "100", exact: true }).click(); assert.equal(await input.inputValue(), "100");
    await input.fill("1"); await input.press("Enter"); assert.equal(await input.inputValue(), "100");
  });
  await scenario("personnel", "/", async page => {
    await page.locator('input[name="fullName"]').fill("未保存姓名"); await page.waitForFunction(() => window.dirty);
    assert.equal(await page.evaluate(() => { const event = new Event("beforeunload", { cancelable: true }); window.dispatchEvent(event); return event.defaultPrevented; }), true);
    await page.getByRole("button", { name: "关闭窗口", exact: true }).click(); await page.getByRole("button", { name: "取消", exact: true }).click();
    assert.equal(await page.locator('input[name="fullName"]').inputValue(), "未保存姓名");
  });
  await scenario("announcement", "/", async page => { await page.locator('input[type="file"]').waitFor(); assert.equal(await page.locator('input[type="file"]').isDisabled(), true); });
  console.log(`Editor state regressions UI: ${passed} passed; ${output}`);
} finally { await context.close(); }
