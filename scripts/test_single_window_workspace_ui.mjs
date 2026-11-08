import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";
import { locateChromeForTesting } from "./lib/chromium-executable.mjs";

const root = path.resolve(import.meta.dirname, ".."), web = path.join(root, "apps/export-doc-web");
const require = createRequire(path.join(web, "package.json"));
const output = path.join(root, "artifacts/single-window-workspace-ui", String(Date.now()));
fs.mkdirSync(path.join(output, "Temp"), { recursive: true });
Object.assign(process.env, { TEMP: path.join(output, "Temp"), TMP: path.join(output, "Temp") });
const schemas = JSON.parse(fs.readFileSync(path.join(root, "crates/export-doc-contracts/src/openapi.json"), "utf8")).components.schemas;
function example(schema) {
  if (schema.$ref) return example(schemas[schema.$ref.split("/").at(-1)]);
  if (schema.default !== undefined) return schema.default;
  const type = Array.isArray(schema.type) ? schema.type.find(type => type !== "null") : schema.type;
  if (type === "object") return Object.fromEntries(Object.entries(schema.properties ?? {}).map(([key, value]) => [key, example(value)]));
  if (type === "array") return [];
  if (type === "integer" || type === "number") return 0;
  if (type === "boolean") return false;
  return "";
}
const source = name => JSON.stringify(path.join(web, "src", name).replaceAll("\\", "/"));
const result = await require("esbuild").build({ write: false, bundle: true, format: "iife", platform: "browser", jsx: "automatic", loader: { ".css": "empty" }, logLevel: "silent",
  plugins: [{ name: "omit-independent-panels", setup(build) {
    build.onLoad({ filter: /SingleWindow(Handoff|ExportReview)Panel\.tsx$/ }, args => ({ contents: `export function ${path.basename(args.path, ".tsx")}(){return null}`, loader: "tsx" }));
  } }], stdin: { loader: "tsx", resolveDir: web, contents: `
import React,{useState} from "react";import {createRoot} from "react-dom/client";
import {QueryClient,QueryClientProvider} from "@tanstack/react-query";
import {MemoryRouter,Routes,Route,useNavigate} from "react-router-dom";
import {AgentConsignmentPage} from ${source("features/single-window/AgentConsignmentPage.tsx")};
import {CustomsCooPage} from ${source("features/single-window/CustomsCooPage.tsx")};
import {useReferenceCatalogExcelWorkspace} from ${source("features/single-window/useReferenceCatalogExcelWorkspace.ts")};
import {catalogPages,getRows} from ${source("features/single-window/referenceCatalogModel.ts")};
import {PermissionAccessProvider} from ${source("app/PermissionAccessContext.tsx")};
import {ConfirmationProvider} from ${source("ui/ConfirmationProvider.tsx")};
import {UnsavedChangesProvider,useHasUnsavedChanges} from ${source("ui/unsavedChangesGuard.tsx")};
const initial={agent:${JSON.stringify(example(schemas.ApiAgentConsignmentDocumentDto))},coo:${JSON.stringify(example(schemas.ApiCustomsCooDocumentDto))}};
const catalog=${JSON.stringify(example(schemas.SingleWindowReferenceCatalogModel))};
const queries=new QueryClient({defaultOptions:{queries:{retry:false,staleTime:Infinity},mutations:{retry:false}}});window.queries=queries;
const records=new Map();window.pending=[];
const get=id=>{if(!records.has(id))records.set(id,{...initial[window.mode],id:0,sourceInvoiceId:id,draftRevision:0,expectedDraftRevision:0,copCusCode:'1234567890',gName:'initial',certNo:'initial'});return records.get(id)};
const defer=(kind,request,init)=>new Promise((resolve,reject)=>window.pending.push({kind,request,signal:init.signal,resolve,reject}));
const save=(r,i)=>defer('save',r,i),defaults=(r,i)=>defer('defaults',r,i);
const client={getAgentConsignmentDocument:async r=>get(r.invoiceId),getCustomsCooDocument:async r=>get(r.invoiceId),
  saveAgentConsignmentDocument:save,saveCustomsCooDocument:save,buildAgentConsignmentDefaults:defaults,buildCustomsCooDefaults:defaults,
  getSingleWindowExportReview:async()=>{if(window.failRefresh)throw Error('refresh unavailable');return {groups:[]}},
  getSingleWindowReferenceCatalog:async()=>({catalog,revision:0}),
  getAgentConsignmentLockedFields:async()=>{throw Error('locked fields unavailable')},getCustomsCooLockedFields:async()=>{throw Error('locked fields unavailable')},
  getCustomsCooEditorOptions:async()=>(${JSON.stringify(example(schemas.ApiCustomsCooEditorOptionsResponse))}),getCustomsCooIssuingAuthorities:async()=>({options:[]}),
  previewSingleWindowReferenceCatalogExcelImport:(r,i)=>defer('preview',r,i),
};
window.finish=(index=0)=>{const p=window.pending[index],saved={...p.request.body,id:101,draftRevision:p.request.body.expectedDraftRevision+1};records.set(p.request.invoiceId,saved);p.resolve({document:saved,message:'saved'});};
function Excel(){const [activeKey,setKey]=useState('countries'),[draft,setDraft]=useState(catalog),[notice,setNotice]=useState('');
 const activePage=catalogPages.find(page=>page.key===activeKey);
 const workspace=useReferenceCatalogExcelWorkspace({client,activeKey,activePage,draft,rows:getRows(draft,activeKey),canManage:true,externalBusy:false,markDraft:setDraft,clearMessages:()=>setNotice(''),showError:setNotice,showSuccess:setNotice});
 window.excel=workspace;window.setKey=setKey;return <><input type="file" onChange={workspace.handleFile}/><output>{workspace.sheetName}</output><span>{notice}</span></>;
}
function Harness(){const navigate=useNavigate(),[shown,setShown]=useState(true),[enabled,setEnabled]=useState(true),[owner,setOwner]=useState(client);
 window.navigate=navigate;window.hide=()=>setShown(false);window.revoke=()=>setEnabled(false);window.changeClient=()=>setOwner({...client});window.dirty=useHasUnsavedChanges();
 const permissions=['view',...(enabled?['operate','manage']:[])].map(action=>({resourceKey:'document.single-window',action,dataScope:'all'}));
 return <PermissionAccessProvider permissions={permissions}>{!shown?<span>closed</span>:window.mode==='excel'?<Excel/>:<Routes><Route path="/:invoiceId" element={window.mode==='agent'?<AgentConsignmentPage client={owner}/>:<CustomsCooPage client={owner}/>}/></Routes>}</PermissionAccessProvider>;
}
createRoot(document.getElementById('root')).render(<QueryClientProvider client={queries}><MemoryRouter initialEntries={['/1']}><ConfirmationProvider><UnsavedChangesProvider><Harness/></UnsavedChangesProvider></ConfirmationProvider></MemoryRouter></QueryClientProvider>);
` } });
const context = await require("playwright").chromium.launchPersistentContext(path.join(output, "Chrome"), { executablePath: locateChromeForTesting(root), headless: true });
let passed = 0;
async function scenario(mode, check) {
  const page = await context.newPage(), errors = [];
  page.setDefaultTimeout(10000); page.on("pageerror", error => errors.push(error.message));
  try {
    await page.setContent('<!doctype html><html lang="zh-CN"><div id="root"></div></html>');
    await page.evaluate(mode => { window.mode = mode; }, mode); await page.addScriptTag({ content: result.outputFiles[0].text });
    await check(page); assert.deepEqual(errors, []); passed++;
  } catch (error) { await page.screenshot({ path: path.join(output, `failure-${passed}.png`), fullPage: true }); throw error; }
  finally { await page.close(); }
}
const submit = page => page.locator("form.entity-form").evaluate(form => { form.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })); form.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })); });
try {
  for (const mode of ["agent", "coo"]) {
    const label = mode === "agent" ? /^主要货物名称/ : "原产地证编号";
    await scenario(mode, async page => {
      const field = page.getByLabel(label); await field.fill("submitted"); await submit(page);
      await page.waitForFunction(() => window.pending.length === 1); await field.fill("typed-after-save");
      await page.evaluate(() => window.finish()); await page.waitForFunction(() => !document.querySelector('button[type="submit"]:disabled'));
      assert.equal(await field.inputValue(), "typed-after-save"); assert.equal(await page.evaluate(() => window.dirty), true);
      assert.equal(await page.getByText(/服务器上的.*已更新/).count(), 0);
      await submit(page); await page.waitForFunction(() => window.pending.length === 2);
      assert.equal(await page.evaluate(() => window.pending[1].request.body.expectedDraftRevision), 1);
      await page.evaluate(() => { window.failRefresh = true; window.finish(1); });
      await page.getByText(/草稿已保存，关联信息刷新失败/).waitFor();
      await page.waitForFunction(() => !window.dirty);
    });
    await scenario(mode, async page => {
      await page.getByLabel(label).fill("before-lock");
      await page.getByRole("button", { name: "高级模式", exact: true }).click();
      await page.getByRole("button", { name: "字段锁定", exact: true }).click();
      await page.getByRole("button", { name: "保存并继续", exact: true }).click();
      await page.waitForFunction(() => window.pending.length === 1);
      await page.evaluate(() => window.finish());
      await page.getByText("locked fields unavailable", { exact: true }).waitFor();
      await page.waitForFunction(() => !window.dirty);
      await page.getByLabel(label).fill("after-lock-failure"); await submit(page);
      await page.waitForFunction(() => window.pending.length === 2);
      assert.equal(await page.evaluate(() => window.pending[1].request.body.expectedDraftRevision), 1);
      await page.evaluate(() => window.finish(1)); await page.waitForFunction(() => !window.dirty);
    });
    for (const action of ["navigate", "hide", "revoke", "changeClient"]) await scenario(mode, async page => {
      await page.getByLabel(label).fill("submitted"); await submit(page);
      await page.waitForFunction(() => window.pending.length === 1);
      await page.evaluate(action => action === "navigate" ? window.navigate('/2') : window[action](), action);
      await page.waitForFunction(() => window.pending[0].signal.aborted);
      await page.evaluate(() => window.finish());
      if (action === "navigate") { await page.waitForFunction(() => document.querySelector('form')); assert.equal(await page.getByLabel(label).inputValue(), "initial"); }
      assert.equal(await page.getByText("saved", { exact: true }).count(), 0);
    });
    await scenario(mode, async page => {
      await page.getByLabel(label).fill("local"); await page.getByRole("button", { name: "回填空白", exact: true }).click();
      await page.waitForFunction(() => window.pending.length === 1);
      await page.evaluate(() => window.navigate('/2')); await page.waitForFunction(() => window.pending[0].signal.aborted);
      await page.evaluate(() => window.pending[0].resolve({})); assert.equal(await page.getByLabel(label).inputValue(), "initial");
    });
  }
  await scenario("excel", async page => {
    await page.locator('input[type="file"]').setInputFiles({ name: "countries.xlsx", mimeType: "application/octet-stream", buffer: Buffer.from("fixture") });
    await page.waitForFunction(() => window.pending.length === 1); await page.evaluate(() => window.setKey('currencies'));
    await page.waitForFunction(() => window.pending[0].signal.aborted);
    await page.evaluate(() => window.pending[0].resolve({ catalogKey:'countries',sheetName:'obsolete',headerRowNumber:1,dataStartRowNumber:2,columnMappings:[],rowCount:1 }));
    assert.equal(await page.locator("output").textContent(), ""); assert.equal(await page.evaluate(() => window.excel.preview), null);
    assert.equal(await page.evaluate(() => window.queries.getMutationCache().getAll().length), 0);
  });
  console.log(`Single-window workspace UI: ${passed} scenarios passed.`);
} finally { await context.close(); }
