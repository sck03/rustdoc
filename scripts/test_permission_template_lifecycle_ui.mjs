import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";
import { locateChromeForTesting } from "./lib/chromium-executable.mjs";

const root = path.resolve(import.meta.dirname, ".."), web = path.join(root, "apps/export-doc-web");
const require = createRequire(path.join(web, "package.json"));
const output = path.join(root, "artifacts/permission-template-lifecycle-ui", String(Date.now()));
fs.mkdirSync(path.join(output, "Temp"), { recursive: true });
Object.assign(process.env, { TEMP: path.join(output, "Temp"), TMP: path.join(output, "Temp") });
const source = name => JSON.stringify(path.join(web, "src", name).replaceAll("\\", "/"));
await require("esbuild").build({ stdin: { resolveDir: web, loader: "tsx", contents: `
import React from "react"; import {createRoot} from "react-dom/client";
import {MemoryRouter} from "react-router-dom";
import {QueryClient,QueryClientProvider} from "@tanstack/react-query";
import {PermissionTemplateManagementPanel} from ${source("features/settings/PermissionTemplateManagementPanel.tsx")};
import {ConfirmationProvider} from ${source("ui/ConfirmationProvider.tsx")};
import {UnsavedChangesProvider} from ${source("ui/unsavedChangesGuard.tsx")};
window.calls=[];window.invalidations=0;
const template=id=>({id,versionNumber:1,code:'custom-'+id,name:'方案'+id,description:'',isSystem:false,isActive:true,grants:[],effectiveGrants:[],disabledModules:[],oaAssignedOnly:false});
window.templates=[template(1),template(2)];
const catalog=()=>({templates:window.templates,resources:[],dataScopes:[],accessLevels:[]});
const cache=new QueryClient({defaultOptions:{queries:{retry:false,staleTime:Infinity},mutations:{retry:false}}});window.cache=cache;
const invalidate=cache.invalidateQueries.bind(cache);cache.invalidateQueries=(...args)=>{window.invalidations++;return invalidate(...args);};
const clients=[0,1].map(owner=>new Proxy({}, {get:(_,method)=>(request,options)=>{
 if(method==='listPermissionTemplates')return window.delayDirectory?new Promise((resolve,reject)=>window.refresh={resolve,reject}):Promise.resolve(catalog());
 if(method==='listPermissionTemplateVersions')return Promise.resolve({items:[],totalCount:0,pageNumber:1,pageSize:20});
 return new Promise((resolve,reject)=>window.calls.push({owner,method,request,signal:options?.signal??request?.signal,resolve,reject}));
}}));
const app=createRoot(document.getElementById('root'));
window.render=(owner=0,shown=true,allowed=true)=>app.render(<React.StrictMode><MemoryRouter><QueryClientProvider client={cache}><ConfirmationProvider><UnsavedChangesProvider>{shown&&<PermissionTemplateManagementPanel client={clients[owner]} canManageUsers={allowed}/>}</UnsavedChangesProvider></ConfirmationProvider></QueryClientProvider></MemoryRouter></React.StrictMode>);
window.finish=()=>{const call=window.calls.at(-1);if(call.method==='deletePermissionTemplate'){window.templates=window.templates.filter(t=>t.id!==call.request.id);call.resolve({success:true});}else{const saved={...template(call.request.id??3),...call.request.body,id:call.request.id??3,versionNumber:2};window.templates=window.templates.map(t=>t.id===saved.id?saved:t);call.resolve(saved);}};
window.render();
` }, outfile: path.join(output, "app.js"), bundle: true, format: "iife", platform: "browser", jsx: "automatic", logLevel: "silent" });
const context = await require("playwright").chromium.launchPersistentContext(path.join(output, "Chrome"), { executablePath: locateChromeForTesting(root), headless: true });
let passed = 0;
async function scenario(check) {
  const page = await context.newPage(), errors = [];
  page.setDefaultTimeout(8000); page.on("pageerror", error => errors.push(error.message));
  try {
    await page.setContent('<html lang="zh-CN"><title>权限方案回归</title><div id="root"></div></html>');
    await page.addScriptTag({ path: path.join(output, "app.js") });
    await page.waitForFunction(() => document.querySelector('.permission-template-meta-grid input')?.value === '方案1');
    await check(page); assert.deepEqual(errors, []); passed++;
  } catch (error) { await page.screenshot({ path: path.join(output, `failure-${passed}.png`), fullPage: true }); throw error; }
  finally { await page.close(); }
}
try {
  for (const change of ["client", "unmount", "permission"]) for (const outcome of ["resolve", "reject"]) await scenario(async page => {
    await page.getByLabel("方案名称", { exact: true }).fill("已编辑");
    await page.getByRole("button", { name: "保存方案", exact: true }).click();
    await page.waitForFunction(() => window.calls.some(c => c.method === 'updatePermissionTemplate'));
    await page.evaluate(change => window.render(change === 'client' ? 1 : 0, change !== 'unmount', change !== 'permission'), change);
    await page.waitForFunction(() => window.calls.find(c => c.method === 'updatePermissionTemplate').signal.aborted);
    await page.evaluate(outcome => outcome === 'resolve' ? window.finish() : window.calls.at(-1).reject(new Error('旧保存失败')), outcome);
    assert.equal(await page.getByText("旧保存失败").count(), 0);
    assert.equal(await page.evaluate(() => window.invalidations), 0);
    assert.equal(await page.evaluate(() => window.cache.getMutationCache().getAll().length), 0);
  });
  await scenario(async page => {
    await page.getByRole("button", { name: "删除方案", exact: true }).click();
    await page.getByRole("button", { name: "确认删除", exact: true }).click();
    await page.waitForFunction(() => window.calls.some(c => c.method === 'deletePermissionTemplate'));
    await page.evaluate(() => { window.delayDirectory = true; window.finish(); });
    await page.getByText("权限方案已删除。", { exact: true }).waitFor();
    assert.equal(await page.getByRole("button", { name: "方案1", exact: true }).count(), 0);
    assert.equal(await page.getByLabel("方案名称", { exact: true }).inputValue(), "新权限方案");
    await page.evaluate(() => window.refresh.resolve({templates:window.templates,resources:[],dataScopes:[],accessLevels:[]}));
    assert.equal(await page.getByLabel("方案名称", { exact: true }).inputValue(), "新权限方案");
  });
  await scenario(async page => {
    await page.getByRole("button", { name: "删除方案", exact: true }).click();
    await page.evaluate(() => window.render(0, false));
    await page.getByRole("button", { name: "确认删除", exact: true }).click();
    assert.equal(await page.evaluate(() => window.calls.filter(c => c.method === 'deletePermissionTemplate').length), 0);
  });
  console.log(`Permission template lifecycle UI: ${passed} passed; ${output}`);
} finally { await context.close(); }
