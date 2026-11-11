import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";
import { createServer } from "node:http";
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
    build.onLoad({ filter: /SingleWindowExportReviewPanel\.tsx$/ }, args => ({ contents: `export function ${path.basename(args.path, ".tsx")}(){return null}`, loader: "tsx" }));
  } }], stdin: { loader: "tsx", resolveDir: web, contents: `
import React,{useState} from "react";import {createRoot} from "react-dom/client";
import {QueryClient,QueryClientProvider} from "@tanstack/react-query";
import {HashRouter,Routes,Route,useNavigate} from "react-router-dom";
import {AgentConsignmentPage} from ${source("features/single-window/AgentConsignmentPage.tsx")};
import {CustomsCooPage} from ${source("features/single-window/CustomsCooPage.tsx")};
import {SingleWindowSourcesPage} from ${source("features/single-window/SingleWindowSourcesPage.tsx")};
import {SingleWindowClientDirectories,emptyClientDirectories} from ${source("features/single-window/SingleWindowClientDirectories.tsx")};
import {OperationCenterListActionsPanel} from ${source("features/single-window/SingleWindowOperationCenterList.tsx")};
import {SingleWindowStationProfilePanel} from ${source("features/single-window/SingleWindowStationProfilePanel.tsx")};
import {useReferenceCatalogExcelWorkspace} from ${source("features/single-window/useReferenceCatalogExcelWorkspace.ts")};
import {catalogPages,getRows} from ${source("features/single-window/referenceCatalogModel.ts")};
import {PermissionAccessProvider} from ${source("app/PermissionAccessContext.tsx")};
import {ConfirmationProvider} from ${source("ui/ConfirmationProvider.tsx")};
import {UnsavedChangesProvider,useHasUnsavedChanges} from ${source("ui/unsavedChangesGuard.tsx")};
const initial={agent:${JSON.stringify(example(schemas.ApiAgentConsignmentDocumentDto))},coo:${JSON.stringify(example(schemas.ApiCustomsCooDocumentDto))}};
const catalog=${JSON.stringify(example(schemas.SingleWindowReferenceCatalogModel))};
const queries=new QueryClient({defaultOptions:{queries:{retry:false,staleTime:Infinity},mutations:{retry:false}}});window.queries=queries;
const records=new Map();window.pending=[];window.downloads=[];
document.addEventListener('click',event=>{if(event.target instanceof HTMLAnchorElement && event.target.download){event.preventDefault();window.downloads.push(event.target.download);}});
window.profileRows=[1,2].map(id=>({...${JSON.stringify(example(schemas.ApiSingleWindowClientProfileDto))},id,versionNumber:1,profileKey:'profile-'+id,profileName:'档案'+id,companyScope:'DEFAULT',cardIdentifier:'CARD-'+id,isEnabled:true,isActive:id===1,canSubmitCustomsCoo:true,canSubmitAgentConsignment:true}));
if(window.mode==='directories'||window.desktop){window.isTauri=true;window.__TAURI_INTERNALS__={invoke:(command,args)=>new Promise((resolve,reject)=>window.pending.push({command,args,resolve,reject}))};}
const get=id=>{if(!records.has(id))records.set(id,{...initial[window.mode],id:0,sourceId:id,draftRevision:0,expectedDraftRevision:0,copCusCode:'1234567890',gName:'initial',certNo:'initial'});return records.get(id)};
const defer=(kind,request,init)=>new Promise((resolve,reject)=>window.pending.push({kind,request,signal:init.signal,resolve,reject}));
const save=(r,i)=>defer('save',r,i),defaults=(r,i)=>defer('defaults',r,i);
const client={getAgentConsignmentDocument:async r=>get(r.sourceId),getCustomsCooDocument:async r=>get(r.sourceId),
  getSettings:async()=>({settings:{excelImportSchemes:[{schemeName:'客户专用'}]}}),
  getSingleWindowClientProfiles:async()=>({profiles:window.profileRows,activeProfileKey:'profile-1'}),
  saveSingleWindowClientProfile:(r,i)=>defer('profile',r,i),
  activateSingleWindowClientProfile:(r,i)=>defer('activate',r,i),
  dispatchSingleWindowBatchToClient:(r,i)=>defer('dispatch',r,i),
  collectSingleWindowClientReceipts:(r,i)=>defer('collect',r,i),
  saveSingleWindowReceiptPackageToPath:(r,i)=>defer('receipt-save',r,i),
  listSingleWindowSources:async()=>({items:[],totalCount:0}),
  previewSingleWindowExcelImport:(r,i)=>defer('source-preview',r,i),
  createSingleWindowSource:(r,i)=>defer('source-create',r,i),
  startSingleWindowExcelTemplateDownloadJob:i=>defer('source-template',{},i),
  startSingleWindowExcelTemplateSaveToPathJob:(r,i)=>defer('source-template-save',r,i),
  createJobDownloadTicket:async()=>({downloadUrl:'/template.xlsx'}),resolveUrl:url=>url,
  saveAgentConsignmentDocument:save,saveCustomsCooDocument:save,buildAgentConsignmentDefaults:defaults,buildCustomsCooDefaults:defaults,
  getSingleWindowExportReview:async()=>{if(window.failRefresh)throw Error('refresh unavailable');return {groups:[]}},
  getSingleWindowReferenceCatalog:async()=>({catalog,revision:0}),
  getAgentConsignmentLockedFields:async()=>{throw Error('locked fields unavailable')},getCustomsCooLockedFields:async()=>{throw Error('locked fields unavailable')},
  getCustomsCooEditorOptions:async()=>(${JSON.stringify(example(schemas.ApiCustomsCooEditorOptionsResponse))}),getCustomsCooIssuingAuthorities:async()=>({options:[]}),
  previewSingleWindowReferenceCatalogExcelImport:(r,i)=>defer('preview',r,i),
  downloadCustomsCooSubmitPackage:(r,i)=>defer('export',r,i),downloadAgentConsignmentSubmitPackage:(r,i)=>defer('export',r,i),
  saveCustomsCooSubmitPackageToPath:(r,i)=>defer('export',r,i),saveAgentConsignmentSubmitPackageToPath:(r,i)=>defer('export',r,i),
};
window.finish=(index=0)=>{const p=window.pending[index],saved={...p.request.body,id:101,draftRevision:p.request.body.expectedDraftRevision+1};records.set(p.request.sourceId,saved);p.resolve({document:saved,message:'saved'});};
function Excel(){const [activeKey,setKey]=useState('countries'),[draft,setDraft]=useState(catalog),[notice,setNotice]=useState('');
 const activePage=catalogPages.find(page=>page.key===activeKey);
 const workspace=useReferenceCatalogExcelWorkspace({client,activeKey,activePage,draft,rows:getRows(draft,activeKey),canManage:true,externalBusy:false,markDraft:setDraft,clearMessages:()=>setNotice(''),showError:setNotice,showSuccess:setNotice});
 window.excel=workspace;window.setKey=setKey;return <><input type="file" onChange={workspace.handleFile}/><output>{workspace.sheetName}</output><span>{notice}</span></>;
}
function Directories({enabled}){const [value,setValue]=useState({customsCooDirectories:emptyClientDirectories(),agentConsignmentDirectories:emptyClientDirectories()});window.directoryValue=value;
 return <SingleWindowClientDirectories value={value} onChange={setValue} autoDetect disabled={!enabled} cooEnabled acdEnabled/>;}
function Batch({client,enabled}){const [id,setId]=useState(1);window.setBatch=setId;return <OperationCenterListActionsPanel client={client} canOperate={enabled} isDesktopStation onOpenDetail={()=>{}}
 row={{batchId:id,batchReference:'batch-'+id,companyScope:'DEFAULT',assignedCardIdentifier:'CARD-1',status:window.receiptMode?'QueuedToClient':'SubmitPackageImported',businessType:'AgentConsignment',invoiceNo:'INV-'+id}}/>;}
function Harness(){const navigate=useNavigate(),[shown,setShown]=useState(true),[enabled,setEnabled]=useState(true),[jobsEnabled,setJobsEnabled]=useState(true),[owner,setOwner]=useState(client);
  window.navigate=navigate;window.hide=()=>setShown(false);window.revoke=()=>setEnabled(false);window.revokeJobs=()=>setJobsEnabled(false);window.changeClient=()=>setOwner({...client});window.dirty=useHasUnsavedChanges();
 const permissions=['view',...(enabled?['operate','manage']:[])].map(action=>({resourceKey:'document.single-window',action,dataScope:'all'}));
  if(jobsEnabled) permissions.push(...['view','operate'].map(action=>({resourceKey:'document.jobs',action,dataScope:'own'})));
 return <PermissionAccessProvider permissions={permissions}>{!shown?<span>closed</span>:window.mode==='batch'?<Batch client={owner} enabled={enabled}/>:window.mode==='profiles'?<SingleWindowStationProfilePanel client={owner} canOperate={enabled}/>:window.mode==='directories'?<Directories enabled={enabled}/>:window.mode==='sources'?<SingleWindowSourcesPage client={owner}/>:window.mode==='excel'?<Excel/>:<Routes><Route path="/:sourceId" element={window.mode==='agent'?<AgentConsignmentPage client={owner}/>:<CustomsCooPage client={owner}/>}/></Routes>}</PermissionAccessProvider>;
}
createRoot(document.getElementById('root')).render(<QueryClientProvider client={queries}><HashRouter><ConfirmationProvider><UnsavedChangesProvider><Harness/></UnsavedChangesProvider></ConfirmationProvider></HashRouter></QueryClientProvider>);
` } });
const server = createServer((_request, response) => { response.setHeader("Content-Type", "text/html; charset=utf-8"); response.end('<!doctype html><html lang="zh-CN"><div id="root"></div></html>'); });
await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
const context = await require("playwright").chromium.launchPersistentContext(path.join(output, "Chrome"), { executablePath: locateChromeForTesting(root), headless: true });
let passed = 0;
async function scenario(mode, check, desktop=false) {
  const page = await context.newPage(), errors = [];
  page.setDefaultTimeout(10000); page.on("pageerror", error => errors.push(error.message));
  try {
    await page.goto(`http://127.0.0.1:${server.address().port}/`);
    await page.evaluate(({mode,desktop}) => { window.mode = mode; window.desktop=desktop; window.location.hash='/1'; }, {mode,desktop}); await page.addScriptTag({ content: result.outputFiles[0].text });
    await check(page); assert.deepEqual(errors, []); passed++;
  } catch (error) { console.error(errors); await page.screenshot({ path: path.join(output, `failure-${passed}.png`), fullPage: true }); throw error; }
  finally { await page.close(); }
}
const submit = page => page.locator("form.entity-form").evaluate(form => { form.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })); form.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })); });
try {
  for(const mode of ['agent','coo']) {
    await scenario(mode,async page=>{
      const button=page.getByRole('button',{name:'下载提交包',exact:true});
      await page.getByLabel('目标持卡机授权码').fill('assignment');
      assert(await button.isDisabled(),'unsaved initial draft cannot export');
      await submit(page);await page.waitForFunction(()=>window.pending.length===1);
      await page.evaluate(()=>window.finish());await button.waitFor();
      await page.waitForFunction(()=>!Array.from(document.querySelectorAll('button')).find(b=>b.textContent.includes('下载提交包')).disabled);
      await button.click();await page.waitForFunction(()=>window.pending.length===2);
      assert.equal(await page.evaluate(()=>window.pending[1].request.body.expectedDraftRevision),1);
      await submit(page);assert.equal(await page.evaluate(()=>window.pending.length),2,'save and export share the lock');
      await page.evaluate(()=>window.hide());await page.waitForFunction(()=>window.pending[1].signal.aborted);
      await page.evaluate(()=>window.pending[1].resolve(new Blob(['late'])));
      await page.evaluate(()=>new Promise(resolve=>setTimeout(resolve,30)));
      assert.equal(await page.evaluate(()=>window.downloads.length),0);
      assert.equal(await page.evaluate(()=>window.queries.getMutationCache().getAll().length),0);
    });
    await scenario(mode,async page=>{
      await page.getByLabel('目标持卡机授权码').fill('assignment');
      await submit(page);await page.waitForFunction(()=>window.pending.length===1);await page.evaluate(()=>window.finish());
      const button=page.getByRole('button',{name:'导出提交包',exact:true});
      await page.waitForFunction(()=>!Array.from(document.querySelectorAll('button')).find(b=>b.textContent.includes('导出提交包')).disabled);
      await button.click();await page.waitForFunction(()=>window.pending.length===2);
      await page.evaluate(()=>window.hide());
      await page.evaluate(()=>window.pending[1].resolve('E:/late.swpkg'));
      await page.evaluate(()=>new Promise(resolve=>setTimeout(resolve,30)));
      assert.equal(await page.evaluate(()=>window.pending.length),2,'late dialog cannot start export');
    },true);
  }
  await scenario('profiles',async page=>{
    await page.waitForFunction(()=>document.querySelector('input[aria-label="档案名称"]')?.value==='档案1'&&!window.dirty);
    await page.getByLabel('选择操作档案').selectOption('profile-2');
    await page.waitForFunction(()=>document.querySelector('input[aria-label="档案名称"]')?.value==='档案2'&&!window.dirty);
    await page.getByLabel('ACD 发送目录 OutBox',{exact:true}).fill('F:/第二张卡');
    await page.getByRole('button',{name:'保存档案',exact:true}).click();
    await page.waitForFunction(()=>window.pending.length===1);
    await page.evaluate(()=>{
      const p=window.pending[0];window.profileRows[1]={...window.profileRows[1],...p.request.body,versionNumber:2};
      p.resolve({profiles:window.profileRows,activeProfileKey:'profile-1',savedProfileKey:p.request.body.profileKey});
    });
    await page.getByText('操作档案已保存。',{exact:true}).waitFor();
    assert.equal(await page.getByLabel('选择操作档案').inputValue(),'profile-2');
    assert.equal(await page.getByLabel('ACD 发送目录 OutBox',{exact:true}).inputValue(),'F:/第二张卡');
    await page.getByRole('button',{name:'新增档案',exact:true}).click();
    await page.getByLabel('档案名称',{exact:true}).fill('本次新建');
    await page.getByLabel('公司抬头',{exact:true}).fill('DEFAULT');
    await page.getByLabel('操作卡标识',{exact:true}).fill('CARD-NEW');
    await page.getByRole('button',{name:'保存档案',exact:true}).click();
    await page.waitForFunction(()=>window.pending.length===2);
    await page.evaluate(()=>{
      const p=window.pending[1], other={...window.profileRows[0],id:3,profileKey:'other-new',profileName:'另一窗口新建'};
      const own={...window.profileRows[0],...p.request.body,id:4,profileKey:'own-new',versionNumber:1};
      p.resolve({profiles:[...window.profileRows,other,own],activeProfileKey:'profile-1',savedProfileKey:'own-new'});
    });
    await page.waitForFunction(()=>document.querySelector('input[aria-label="档案名称"]')?.value==='本次新建'&&!window.dirty);
    assert.equal(await page.getByLabel('选择操作档案').inputValue(),'own-new');
  });
  for (const mode of ['agent','coo']) await scenario(mode, async page => {
    const navigation = page.locator('.single-window-section-nav-item');
    await navigation.first().waitFor();
    const hash = await page.evaluate(() => location.hash);
    for (const button of await navigation.all()) {
      const target = await button.getAttribute('aria-controls');
      assert(target && await page.locator('[id="'+target+'"]').count(), 'section exists');
      await button.click();
      assert.equal(await page.evaluate(() => location.hash), hash, 'section navigation preserves HashRouter route');
      assert(await page.locator('[id="'+target+'"]').isVisible(), 'target section is expanded');
    }
  });
  for (const action of ['hide','changeClient','revoke']) await scenario('profiles', async page => {
    await page.getByLabel('档案名称',{exact:true}).fill('pending profile');
    await page.getByRole('button',{name:'保存档案',exact:true}).click();
    await page.waitForFunction(()=>window.pending.length===1);
    assert.equal(await page.evaluate(()=>window.pending[0].request.body.expectedVersion),1);
    await page.evaluate(action=>{window[action]();window.queries.clear();},action);
    await page.waitForFunction(()=>window.pending[0].signal.aborted);
    await page.evaluate(()=>window.pending[0].resolve({profiles:[{...window.profileRows[0],profileName:'OLD SESSION',versionNumber:2}],activeProfileKey:'profile-1'}));
    await page.evaluate(()=>new Promise(resolve=>setTimeout(resolve,30)));
    assert(!await page.evaluate(()=>JSON.stringify(window.queries.getQueryCache().getAll().map(q=>q.state.data)).includes('OLD SESSION')));
    assert.equal(await page.getByText('操作档案已保存。',{exact:true}).count(),0);
  });
  await scenario('batch',async page=>{
    await page.getByRole('button',{name:'送入官方客户端待办目录'}).click();
    await page.waitForFunction(()=>window.pending.length===1);
    await page.evaluate(()=>window.setBatch(2));
    await page.waitForFunction(()=>window.pending[0].signal.aborted);
    await page.evaluate(()=>window.pending[0].resolve({payloadFileCount:37}));
    assert.equal(await page.getByText('37',{exact:true}).count(),0);
    assert.equal(await page.evaluate(()=>window.queries.getMutationCache().getAll().length),0);
  });
  await scenario('batch',async page=>{
    await page.evaluate(()=>{window.receiptMode=true;window.setBatch(2);});
    await page.getByRole('button',{name:'收集并导出回执'}).click();
    await page.waitForFunction(()=>window.pending.length===1);
    await page.evaluate(()=>window.pending[0].resolve({receiptFiles:['E:/receipt.xml']}));
    await page.waitForFunction(()=>window.pending.length===2);
    await page.evaluate(()=>window.setBatch(3));
    await page.waitForFunction(()=>window.pending[0].signal.aborted);
    await page.evaluate(()=>window.pending[1].resolve('E:/late.swpkg'));
    await page.evaluate(()=>new Promise(resolve=>setTimeout(resolve,30)));
    assert.equal(await page.evaluate(()=>window.pending.length),2,'late native save dialog cannot export the previous batch');
  },true);
  const detected = { customsCooDirectories: {outBox:'F:/发送',sentBox:'E:/归档',inBox:'D:/回执',failBox:'F:/失败'}, agentConsignmentDirectories:{outBox:'F:/ACD发送',sentBox:'E:/ACD归档',inBox:'D:/ACD回执',failBox:'F:/ACD失败'} };
  await scenario('directories', async page => {
    await page.waitForFunction(() => window.pending.length===1);
    await page.evaluate(found => window.pending[0].resolve(found), detected);
    await page.getByText('已读取客户端任务目录，请核对后保存。').waitFor();
    assert.deepEqual(await page.evaluate(() => window.directoryValue),detected);
    await page.getByLabel('COO 回执目录 InBox',{exact:true}).fill('D:/手动回执');
    await page.getByRole('button',{name:'选择 COO 回执目录 InBox',exact:true}).click();
    await page.waitForFunction(() => window.pending.length===2);
    await page.evaluate(() => window.pending[1].resolve(null));
    assert.equal(await page.getByLabel('COO 回执目录 InBox',{exact:true}).inputValue(),'D:/手动回执');
  });
  await scenario('directories', async page => {
    await page.waitForFunction(() => window.pending.length===1);
    await page.getByLabel('ACD 发送目录 OutBox',{exact:true}).fill('F:/手动发送');
    await page.evaluate(found => window.pending[0].resolve(found), detected);
    await page.getByText(/目录已手动修改/).waitFor();
    assert.equal(await page.getByLabel('ACD 发送目录 OutBox',{exact:true}).inputValue(),'F:/手动发送');
    await page.getByRole('button',{name:'重新检测目录'}).click();
    await page.waitForFunction(() => window.pending.length===2);
    await page.evaluate(found => window.pending[1].resolve(found), detected);
    await page.getByText('已读取客户端任务目录，请核对后保存。').waitFor();
    assert.deepEqual(await page.evaluate(() => window.directoryValue),detected);
  });
  await scenario('directories', async page => {
    await page.waitForFunction(() => window.pending.length===1);
    await page.evaluate(() => window.pending[0].reject(Error('未找到客户端配置')));
    await page.getByText('未找到客户端配置',{exact:true}).waitFor();
    await page.getByRole('button',{name:'选择 COO 发送目录 OutBox',exact:true}).click();
    await page.waitForFunction(() => window.pending.length===2);
    await page.evaluate(() => window.pending[1].resolve('F:/人工选择'));
    await page.waitForFunction(() => window.directoryValue.customsCooDirectories.outBox==='F:/人工选择');
  });
  for(const action of ['hide','revoke']) await scenario('directories', async page => {
    await page.waitForFunction(() => window.pending.length===1);
    await page.evaluate(action => window[action](), action);
    await page.evaluate(found => window.pending[0].resolve(found), detected);
    assert.equal(await page.getByText('已读取客户端任务目录，请核对后保存。').count(),0);
    assert.equal(await page.evaluate(() => window.directoryValue.customsCooDirectories.outBox),'');
  });
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
  await scenario("sources", async page => {
    await page.getByLabel('Excel 识别方式').selectOption('客户专用');
    await page.locator('input[type="file"]').setInputFiles({ name:'manual.xlsx',mimeType:'application/octet-stream',buffer:Buffer.from('manual') });
    await page.waitForFunction(() => window.pending.length === 1);
    assert.equal(await page.evaluate(() => window.pending[0].request.schemeName), '客户专用');
    await page.evaluate(() => window.pending[0].resolve({success:true,errors:[],invoice:{items:[]}}));
    await page.getByText('已识别 0 行商品，请核对后创建。').waitFor();
    await page.getByLabel('Excel 识别方式').selectOption('');
    assert.equal(await page.getByText(/已识别/).count(), 0);
    assert.equal(await page.getByText('manual.xlsx', {exact:true}).count(), 0);
  });
  await scenario("sources", async page => {
    await page.getByLabel("资料编号 / 客户发票号").fill("CUSTOMER-MANUAL");
    await page.getByRole("button", { name: "创建申报资料" }).click();
    await page.waitForFunction(() => window.pending.length === 1);
    assert.deepEqual(await page.evaluate(() => window.pending[0].request.body), { referenceNo: "CUSTOMER-MANUAL" });
    await page.evaluate(() => window.pending[0].resolve({ id: 1 }));
    await page.getByText("申报资料已创建，可分别填写代理委托和原产地证。").waitFor();
    assert.equal(await page.evaluate(() => window.dirty), false);
  });
  await scenario("sources", async page => {
    await page.getByLabel("资料编号 / 客户发票号").fill("CUSTOMER-EXCEL");
    await page.locator('input[type="file"]').setInputFiles({ name: "custom.xlsx", mimeType: "application/octet-stream", buffer: Buffer.from("invalid") });
    await page.waitForFunction(() => window.pending.length === 1);
    await page.evaluate(() => window.pending[0].reject(Error('invalid workbook')));
    await page.getByText('invalid workbook', { exact: true }).waitFor();
    assert.equal(await page.getByRole('button', { name: '创建申报资料' }).isDisabled(), true);
    await page.getByRole('button', { name: '清除文件，手工录入' }).click();
    assert.equal(await page.getByRole('button', { name: '创建申报资料' }).isEnabled(), true);
  });
  await scenario("sources", async page => {
    await page.getByLabel("资料编号 / 客户发票号").fill("STALE-CUSTOMER");
    await page.locator('input[type="file"]').setInputFiles({ name: "custom.xlsx", mimeType: "application/octet-stream", buffer: Buffer.from("fixture") });
    await page.waitForFunction(() => window.pending.length === 1);
    await page.evaluate(() => window.changeClient());
    await page.waitForFunction(() => window.pending[0].signal.aborted);
    await page.evaluate(() => window.pending[0].resolve({ success:true, errors:[], invoice:{items:[]} }));
    assert.equal(await page.getByLabel("资料编号 / 客户发票号").inputValue(), "");
    assert.equal(await page.getByText(/已识别/).count(), 0);
  });
  await scenario("sources", async page => {
    await page.getByRole('button', { name: '下载 Excel 模板' }).click();
    await page.waitForFunction(() => window.pending.length === 1);
    assert.equal(await page.evaluate(() => window.pending[0].kind), 'source-template');
    await page.evaluate(() => window.pending[0].resolve({jobId:'template',status:'Succeeded'}));
    await page.getByText('导入模板已下载。', { exact:true }).waitFor();
    assert.deepEqual(await page.evaluate(() => window.downloads), ['单一窗口申报资料模板.xlsx']);
    assert.equal(await page.evaluate(() => window.queries.getMutationCache().getAll().length), 0);
  });
  for (const action of ['hide', 'changeClient', 'revoke', 'revokeJobs']) await scenario("sources", async page => {
    await page.getByLabel('资料编号 / 客户发票号').fill('KEEP-DRAFT');
    await page.getByRole('button', { name: '下载 Excel 模板' }).click();
    await page.waitForFunction(() => window.pending.length === 1);
    await page.evaluate(action => window[action](), action);
    await page.waitForFunction(() => window.pending[0].signal.aborted);
    await page.evaluate(() => window.pending[0].resolve({jobId:'template',status:'Succeeded'}));
    assert.equal(await page.evaluate(() => window.downloads.length), 0);
    if(action==='revokeJobs') assert.equal(await page.getByLabel('资料编号 / 客户发票号').inputValue(), 'KEEP-DRAFT');
  });
  await scenario("sources", async page => {
    await page.getByRole('button', { name: '下载 Excel 模板' }).click();
    await page.waitForFunction(() => window.pending.length === 1);
    await page.waitForFunction(() => [...document.querySelectorAll('button')].find(button => button.textContent.trim() === '下载 Excel 模板')?.disabled);
    await page.evaluate(() => window.changeClient());
    // Visibility is unchanged by a client switch; wait for the old command to release its busy state.
    await page.waitForFunction(() => [...document.querySelectorAll('button')].some(button => button.textContent.trim() === '下载 Excel 模板' && !button.disabled));
    await page.evaluate(() => window.pending[0].resolve('E:/late.xlsx'));
    assert.equal(await page.evaluate(() => window.pending.length), 1, 'late save dialog must not start a job');
  }, true);
  console.log(`Single-window workspace UI: ${passed} scenarios passed.`);
} finally { await context.close(); await new Promise(resolve => server.close(resolve)); }
