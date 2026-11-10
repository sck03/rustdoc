import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { locateChromeForTesting } from './lib/chromium-executable.mjs';

const repo = path.resolve(import.meta.dirname, '..'), web = path.join(repo, 'apps/export-doc-web');
const output = path.join(repo, 'artifacts/settings-job-lifecycle-ui', String(Date.now()));
fs.mkdirSync(path.join(output, 'Temp'), { recursive: true });
Object.assign(process.env, { TEMP: path.join(output, 'Temp'), TMP: path.join(output, 'Temp') });
const require = createRequire(path.join(web, 'package.json'));
const src = name => JSON.stringify(path.join(web, 'src', name).replaceAll('\\', '/'));
await require('esbuild').build({ stdin: { resolveDir: web, loader: 'tsx', contents: `
import React from 'react'; import {createRoot} from 'react-dom/client';
import {MemoryRouter} from 'react-router-dom';
import {QueryClient,QueryClientProvider} from '@tanstack/react-query';
import {useSettingsWorkspace} from ${src('features/settings/useSettingsWorkspace.ts')};
import {useBackupManagement} from ${src('features/settings/useBackupManagement.ts')};
import {useJobCenterOperations} from ${src('features/jobs/useJobCenterOperations.ts')};
import {ConfirmationProvider} from ${src('ui/ConfirmationProvider.tsx')};
import {UnsavedChangesProvider} from ${src('ui/unsavedChangesGuard.tsx')};
import {queryKeys} from ${src('api/queryKeys.ts')};
window.calls=[]; window.notices=[];
window.cache=new QueryClient({defaultOptions:{queries:{retry:false,staleTime:Infinity},mutations:{retry:false}}});
window.seed=owner=>{
 window.cache.setQueryData(queryKeys.settings(),{settings:{system:{marker:owner}},secrets:{},storagePolicy:'test'});
 window.cache.setQueryData(queryKeys.backups(),{backups:[],backupRoot:'test'});
}; window.seed(0);
const clients=[0,1].map(owner=>new Proxy({}, {get:(_,method)=>(request,options)=>{
 if(method==='getSettings')return Promise.resolve({settings:{system:{marker:owner}},secrets:{},storagePolicy:'test'});
 if(method==='listDatabaseBackups')return Promise.resolve({backups:[],backupRoot:'test'});
 if(method==='getHealth')return Promise.resolve({databaseProviderKey:'sqlite'});
 return new Promise((resolve,reject)=>window.calls.push({owner,method,request,signal:options?.signal??request?.signal,resolve,reject}));
}}));
function App({owner}){
 const client=clients[owner];
 const settings=useSettingsWorkspace({client,canManageSettings:true,canManageUsers:true,canUseDocumentWorkspace:true,isDesktopRuntime:false});
 const backup=useBackupManagement(client,true);
 const jobs=useJobCenterOperations({client,canOperate:true,canManage:true,canCreateInvoiceReportZip:false,desktopAvailable:false,defaultExportDirectory:'',jobsCount:1,reportTemplates:[],configuredReportTemplatePath:'',focusJob:(...args)=>window.notices.push(args),clearFocusedJob:()=>window.notices.push('clear')});
 window.actions={save:settings.save,backup:backup.createBackup,cancel:()=>jobs.handleCancelJob({jobId:'one',title:'测试任务',canCancel:true}),retry:()=>jobs.retryMutation.mutate('one')};
 window.pending=settings.isBusy||backup.isBusy||jobs.isBusy;
 return <output>{settings.message||backup.message||jobs.message||settings.successMessage||backup.successMessage||''}</output>;
}
const root=createRoot(document.getElementById('root'));
window.render=(owner=0,shown=true)=>root.render(<React.StrictMode><MemoryRouter><QueryClientProvider client={window.cache}><ConfirmationProvider><UnsavedChangesProvider>{shown&&<App owner={owner}/>}</UnsavedChangesProvider></ConfirmationProvider></QueryClientProvider></MemoryRouter></React.StrictMode>);
window.render();
` }, outfile: path.join(output, 'app.js'), bundle: true, platform: 'browser', format: 'iife', jsx: 'automatic', logLevel: 'silent' });
const context = await require('playwright').chromium.launchPersistentContext(path.join(output, 'Chrome'), { executablePath: locateChromeForTesting(repo), headless: true });
let passed = 0;
async function scenario(check) {
  const page = await context.newPage(); page.setDefaultTimeout(8000);
  const errors = []; page.on('pageerror', error => errors.push(error.message));
  try {
    await page.setContent('<!doctype html><html lang="zh-CN"><title>会话操作回归</title><div id="root"></div></html>');
    await page.addScriptTag({ path: path.join(output, 'app.js') });
    await page.waitForFunction(() => window.actions && !window.pending);
    await check(page); assert.deepEqual(errors, []); passed++;
  } catch (error) {
    await page.screenshot({ path: path.join(output, `failure-${passed}.png`) }); throw error;
  } finally { await page.close(); }
}
try {
  for (const command of ['save', 'backup', 'retry']) for (const departure of ['owner', 'unmount']) for (const outcome of ['success', 'failure']) await scenario(async page => {
    await page.evaluate(command => window.actions[command](), command);
    await page.waitForFunction(() => window.calls.length === 1);
    await page.evaluate(departure => { window.cache.clear(); window.seed(1); window.render(1, departure !== 'unmount'); }, departure);
    await page.waitForFunction(() => window.calls[0].signal.aborted);
    await page.evaluate(({ command, outcome }) => {
      const call = window.calls[0];
      if (outcome === 'failure') call.reject(new Error('旧会话错误'));
      else call.resolve(command === 'save' ? { settings: { system: { marker: 'stale' } }, secrets: {}, message: '旧设置' } : { jobId: 'old', status: 'Succeeded' });
    }, { command, outcome });
    await page.evaluate(() => new Promise(resolve => setTimeout(resolve, 60)));
    assert.equal(await page.evaluate(() => window.cache.getQueryData(['settings']).settings.system.marker), 1);
    assert.deepEqual(await page.evaluate(() => window.notices), []);
    assert.doesNotMatch(await page.locator('body').innerText(), /旧会话错误|旧设置/);
    assert.equal(await page.evaluate(() => window.cache.getMutationCache().getAll().length), 0);
  });
  await scenario(async page => {
    await page.evaluate(() => window.actions.backup());
    await page.waitForFunction(() => window.calls.length === 1);
    await page.evaluate(() => window.calls[0].resolve({ jobId: 'waiting', status: 'Running' }));
    await page.waitForFunction(() => window.calls.some(call => call.method === 'getJob'));
    await page.evaluate(() => window.render(0, false));
    await page.waitForFunction(() => window.calls.every(call => call.signal.aborted));
    await page.evaluate(() => window.calls.find(call => call.method === 'getJob').resolve({ jobId: 'waiting', status: 'Succeeded' }));
    assert.equal(await page.evaluate(() => window.cache.getMutationCache().getAll().length), 0);
  });
  await scenario(async page => {
    await page.evaluate(() => { window.actions.cancel(); window.actions.cancel(); });
    await page.getByRole('button', { name: '确认取消', exact: true }).waitFor();
    await page.evaluate(() => window.render(0, false));
    await page.getByRole('button', { name: '确认取消', exact: true }).click();
    await page.evaluate(() => new Promise(resolve => setTimeout(resolve, 60)));
    assert.equal(await page.evaluate(() => window.calls.length), 0);
  });
  console.log(`Settings/job lifecycle UI: ${passed} passed; ${output}`);
} finally { await context.close(); }
