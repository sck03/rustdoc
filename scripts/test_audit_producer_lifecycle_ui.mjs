import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { locateChromeForTesting } from './lib/chromium-executable.mjs';

const root = path.resolve(import.meta.dirname, '..'), web = path.join(root, 'apps/export-doc-web');
const require = createRequire(path.join(web, 'package.json'));
const output = path.join(root, 'artifacts/audit-producer-lifecycle-ui', String(Date.now()));
fs.mkdirSync(path.join(output, 'Temp'), { recursive: true });
Object.assign(process.env, { TEMP: path.join(output, 'Temp'), TMP: path.join(output, 'Temp') });
const src = name => JSON.stringify(path.join(web, 'src', name).replaceAll('\\', '/'));
const bundle = path.join(output, 'app.js');
const nativeEntities = Object.fromEntries([...fs.readFileSync(path.join(root, 'crates/export-doc-engine/src/engine/audit.rs'), 'utf8')
  .match(/pub const ENTITIES[\s\S]*?\n];/)[0].matchAll(/\(\s*"[^"]+",\s*"([^"]+)",\s*"([^"]+)"\s*,?\s*\)/g)].map(match => [match[1], match[2]]));
await require('esbuild').build({ stdin: { resolveDir: web, loader: 'tsx', contents: `
import React from 'react'; import {createRoot} from 'react-dom/client';
import {MemoryRouter} from 'react-router-dom'; import {QueryClient,QueryClientProvider} from '@tanstack/react-query';
import {useAuditLogWorkspace} from ${src('features/audit-logs/hooks/useAuditLogWorkspace.ts')};
import {entityLabels} from ${src('features/audit-logs/auditLogViewState.ts')};
import {CustomsCooProducerProfileDialog} from ${src('features/single-window/CustomsCooProducerProfileDialog.tsx')};
import {ConfirmationProvider} from ${src('ui/ConfirmationProvider.tsx')};
import {UnsavedChangesProvider} from ${src('ui/unsavedChangesGuard.tsx')};
window.calls=[]; window.downloads=0; window.applied=[];
window.entityLabels=entityLabels;
window.cache=new QueryClient({defaultOptions:{queries:{retry:false},mutations:{retry:false}}});
const original=URL.createObjectURL; URL.createObjectURL=blob=>{window.downloads++;return original(blob)};
const deferred=(kind,request,{signal})=>new Promise((resolve,reject)=>window.calls.push({kind,request,signal,resolve,reject}));
const profiles=[{id:1,versionNumber:1,ciqRegNo:'A01',prdcEtpsName:'企业甲'},{id:2,versionNumber:1,ciqRegNo:'B02',prdcEtpsName:'企业乙'}];
const client={listAuditLogs:async()=>({items:[],totalCount:3,totalPages:1,pageNumber:1,pageSize:50}),
getSettings:async()=>({settings:{system:{defaultExportDirectory:''}}}),
downloadAuditLogs:(r,i)=>deferred('download',r,i),cleanupAuditLogs:(r,i)=>deferred('cleanup',r,i),deleteAuditLogsByCriteria:(r,i)=>deferred('delete',r,i),
listCustomsCooProducerProfiles:async()=>({items:profiles,totalCount:2,totalPages:1,pageNumber:1,pageSize:50}),
updateCustomsCooProducerProfile:(r,i)=>deferred('save',r,i),deleteCustomsCooProducerProfile:(r,i)=>deferred('delete',r,i)};
const replacement={...client};
function Audit({owner}){const model=useAuditLogWorkspace({client:owner,canManageAuditLogs:true});window.audit=model;
return <><button disabled={!model.canDownload} onClick={model.handleDownload}>下载</button><output>{model.actionMessage?.text}</output></>}
function Harness({mode,changed}){return mode==='audit'?<Audit owner={changed?replacement:client}/>:
mode==='producer'?<CustomsCooProducerProfileDialog client={client} currentProfile={{}} rowLabel="货项一" onApply={p=>window.applied.push(p)} onClose={()=>window.render('closed')}/>:<div>closed</div>}
const app=createRoot(document.getElementById('root'));
window.render=(mode,changed=false)=>app.render(<MemoryRouter><QueryClientProvider client={window.cache}><ConfirmationProvider><UnsavedChangesProvider><Harness mode={mode} changed={changed}/></UnsavedChangesProvider></ConfirmationProvider></QueryClientProvider></MemoryRouter>);
` }, outfile: bundle, bundle: true, platform: 'browser', format: 'iife', jsx: 'automatic', logLevel: 'silent' });
const context = await require('playwright').chromium.launchPersistentContext(path.join(output, 'Chrome'), { executablePath: locateChromeForTesting(root), headless: true });
let passed = 0;
async function scenario(mode, check) {
  const page = await context.newPage(), errors = [];
  page.setDefaultTimeout(10000); page.on('pageerror', error => errors.push(error.message));
  try {
    await page.setContent('<!doctype html><html lang="zh-CN"><title>审计与生产企业操作回归</title><div id="root"></div></html>');
    await page.addScriptTag({ path: bundle }); await page.evaluate(mode => window.render(mode), mode);
    await check(page); assert.deepEqual(errors, []); passed++;
  } finally { await page.close(); }
}
try {
  await scenario('audit', async page => {
    assert.deepEqual(await page.evaluate(() => window.entityLabels), nativeEntities, 'audit filters cover the current entity catalog');
    for (let count = 1; count <= 3; count++) {
      await page.getByRole('button', { name: '下载', exact: true }).click();
      await page.waitForFunction(count => window.calls.length === count, count);
      await page.evaluate(() => window.calls.at(-1).resolve(new Blob([new Uint8Array(8 * 1024 * 1024)])));
      await page.waitForFunction(count => window.downloads === count, count);
      await page.waitForFunction(() => !window.audit.isActionBusy);
    }
    assert.equal(await page.evaluate(() => window.cache.getMutationCache().getAll().length), 0);
    await page.getByRole('button', { name: '下载', exact: true }).click();
    await page.waitForFunction(() => window.calls.length === 4);
    await page.evaluate(() => window.render('closed'));
    await page.waitForFunction(() => window.calls[3].signal.aborted);
    await page.evaluate(async () => { window.calls[3].resolve(new Blob(['late'])); await new Promise(resolve => setTimeout(resolve, 30)); });
    assert.equal(await page.evaluate(() => window.downloads), 3);
  });
  await scenario('audit', async page => {
    await page.getByRole('button', { name: '下载', exact: true }).click();
    await page.waitForFunction(() => window.calls.length === 1);
    await page.evaluate(() => window.render('audit', true));
    await page.waitForFunction(() => window.calls[0].signal.aborted);
    await page.evaluate(async () => { window.calls[0].resolve(new Blob(['stale-owner'])); await new Promise(resolve => setTimeout(resolve, 30)); });
    assert.equal(await page.evaluate(() => window.downloads), 0);
  });
  await scenario('audit', async page => {
    await page.waitForFunction(() => window.audit.canCleanup);
    await page.evaluate(() => { window.audit.handleCleanup(); window.audit.handleCleanup(); });
    await page.waitForFunction(() => window.calls.length === 1);
    await page.evaluate(() => window.render('closed'));
    await page.waitForFunction(() => window.calls[0].signal.aborted);
    await page.evaluate(async () => { window.calls[0].resolve({message:'LATE-CLEANUP'}); await new Promise(resolve => setTimeout(resolve, 30)); });
    assert.doesNotMatch(await page.locator('body').innerText(), /LATE-CLEANUP/);
  });
  await scenario('producer', async page => {
    await page.getByRole('cell', { name: 'A01', exact: true }).click();
    await page.getByLabel('生产企业名称', { exact: true }).fill('企业甲已修改');
    await page.getByRole('button', { name: '保存资料', exact: true }).click();
    await page.waitForFunction(() => window.calls.length === 1);
    await page.getByRole('cell', { name: 'B02', exact: true }).dispatchEvent('click');
    assert.equal(await page.getByLabel('生产企业代码', { exact: true }).inputValue(), 'A01');
    await page.evaluate(() => window.calls[0].resolve({id:1,profile:{id:1,versionNumber:2,...window.calls[0].request.body.profile},message:'资料保存完成'}));
    await page.getByText('资料保存完成', { exact: true }).waitFor();
    await page.getByRole('cell', { name: 'B02', exact: true }).click();
    await page.waitForFunction(() => document.querySelector('.producer-profile-editor input').value === 'B02');
    assert.equal(await page.evaluate(() => window.cache.getMutationCache().getAll().length), 0);
  });
  await scenario('producer', async page => {
    await page.getByRole('cell', { name: 'A01', exact: true }).click();
    await page.getByRole('button', { name: '保存资料', exact: true }).click();
    await page.waitForFunction(() => window.calls.length === 1);
    await page.evaluate(() => window.render('closed'));
    await page.waitForFunction(() => window.calls[0].signal.aborted);
    await page.evaluate(async () => { window.calls[0].resolve({id:1,profile:{id:1,versionNumber:2},message:'LATE-SAVE'}); await new Promise(resolve => setTimeout(resolve, 30)); });
    assert.doesNotMatch(await page.locator('body').innerText(), /LATE-SAVE/);
  });
  await scenario('producer', async page => {
    await page.getByRole('cell', { name: 'A01', exact: true }).click();
    await page.getByRole('button', { name: '删除', exact: true }).click();
    await page.getByRole('button', { name: '确认删除', exact: true }).waitFor();
    await page.evaluate(() => window.render('closed'));
    await page.getByRole('button', { name: '确认删除', exact: true }).click();
    await page.evaluate(() => new Promise(resolve => setTimeout(resolve, 30)));
    assert.equal(await page.evaluate(() => window.calls.length), 0);
  });
  console.log(`Audit and producer lifecycle UI: ${passed} passed; ${output}`);
} finally { await context.close(); }
