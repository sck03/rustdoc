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
import {useBackupPlan} from ${src('features/settings/useBackupPlan.ts')};
import {useJobCenterOperations} from ${src('features/jobs/useJobCenterOperations.ts')};
import {usePostgreSqlMaintenance} from ${src('features/settings/usePostgreSqlMaintenance.ts')};
import {ConfirmationProvider} from ${src('ui/ConfirmationProvider.tsx')};
import {UnsavedChangesProvider} from ${src('ui/unsavedChangesGuard.tsx')};
import {queryKeys} from ${src('api/queryKeys.ts')};
window.calls=[]; window.notices=[];
const backupStatus={revision:1,settingsRevision:1,plan:{enabled:false,time:'03:00',retentionCount:7,retentionDays:0,replicaMode:'None',s3:{}},webDav:{enabled:false,url:'',userName:''},encryptionPasswordSet:false};
window.cache=new QueryClient({defaultOptions:{queries:{retry:false,staleTime:Infinity},mutations:{retry:false}}});
window.seed=owner=>{
 window.cache.setQueryData(queryKeys.settings(),{settings:{system:{marker:owner}},secrets:{},storagePolicy:'test'});
 window.cache.setQueryData(queryKeys.backups(),{backups:[],backupRoot:'test'});
}; window.seed(0);
const clients=[0,1].map(owner=>new Proxy({}, {get:(_,method)=>(request,options)=>{
 if(method==='getSettings')return Promise.resolve({settings:{system:{marker:owner}},secrets:{},storagePolicy:'test'});
 if(method==='listDatabaseBackups')return Promise.resolve({backups:[],backupRoot:'test'});
 if(method==='getHealth')return Promise.resolve({databaseProviderKey:'sqlite'});
 if(method==='getBackupPlan')return Promise.resolve(backupStatus);
 if(method==='listPostgreSqlPhysicalBackups')return Promise.resolve({status:{postgreSqlConfigured:true,toolsReady:true,database:'review',username:'review'},backups:[{fileName:'review.dump'}]});
 if(method==='getServerMigrationStatus')return window.delayStatus?new Promise((resolve,reject)=>window.statusRefresh={resolve,reject}):Promise.resolve({supported:true,toolsReady:true,pendingRestore:false});
 return new Promise((resolve,reject)=>window.calls.push({owner,method,request,signal:options?.signal??request?.signal,resolve,reject}));
}}));
function App({owner}){
 const client=clients[owner];
 const settings=useSettingsWorkspace({client,canManageSettings:true,canManageUsers:true,canUseDocumentWorkspace:true,isDesktopRuntime:false});
 const backup=useBackupManagement(client,true);
 const plan=useBackupPlan(client,true); window.plan=plan;
 const jobs=useJobCenterOperations({client,canOperate:true,canManage:true,canCreateInvoiceReportZip:false,desktopAvailable:false,defaultExportDirectory:'',jobsCount:1,reportTemplates:[],configuredReportTemplatePath:'',focusJob:(...args)=>window.notices.push(args),clearFocusedJob:()=>window.notices.push('clear')});
 window.actions={save:settings.save,backup:backup.createBackup,cancel:()=>jobs.handleCancelJob({jobId:'one',title:'测试任务',canCancel:true}),retry:()=>jobs.retryMutation.mutate('one')};
 window.pending=settings.isBusy||backup.isBusy||jobs.isBusy;
 return <output>{settings.message||backup.message||jobs.message||settings.successMessage||backup.successMessage||''}</output>;
}
function PgApp({owner}){
 const maintenance=usePostgreSqlMaintenance(clients[owner],true,'all'); window.pg=maintenance;
 window.pending=maintenance.isBusy;
 return <output>{maintenance.message||maintenance.successMessage||''}</output>;
}
const root=createRoot(document.getElementById('root'));
window.render=(owner=0,shown=true)=>root.render(<React.StrictMode><MemoryRouter><QueryClientProvider client={window.cache}><ConfirmationProvider><UnsavedChangesProvider>{shown&&(window.pgMode?<PgApp owner={owner}/>:<App owner={owner}/>)}</UnsavedChangesProvider></ConfirmationProvider></QueryClientProvider></MemoryRouter></React.StrictMode>);
window.render();
` }, outfile: path.join(output, 'app.js'), bundle: true, platform: 'browser', format: 'iife', jsx: 'automatic', logLevel: 'silent' });
const context = await require('playwright').chromium.launchPersistentContext(path.join(output, 'Chrome'), { executablePath: locateChromeForTesting(repo), headless: true });
let passed = 0;
async function scenario(check, pgMode = false) {
  const page = await context.newPage(); page.setDefaultTimeout(8000);
  const errors = []; page.on('pageerror', error => errors.push(error.message));
  try {
    await page.setContent('<!doctype html><html lang="zh-CN"><title>会话操作回归</title><div id="root"></div></html>');
    await page.evaluate(value => { window.pgMode = value; }, pgMode);
    await page.addScriptTag({ path: path.join(output, 'app.js') });
    await page.waitForFunction(() => (window.pgMode ? window.pg : window.actions) && !window.pending);
    await check(page); assert.deepEqual(errors, []); passed++;
  } catch (error) {
    console.error({ errors, state: await page.evaluate(() => ({ pending: window.pending, pg: Boolean(window.pg), methods: window.calls?.map(call => call.method) })) });
    await page.screenshot({ path: path.join(output, `failure-${passed}.png`) }); throw error;
  } finally { await page.close(); }
}
try {
  for (const departure of ['owner', 'unmount']) await scenario(async page => {
    await page.evaluate(() => {
      window.pg.setMigrationAdminPassword('review-secret');
      window.pg.setMigrationRestorePassword('package-secret');
      window.pg.setMigrationRestoreConfirmation('MIGRATE');
      window.pg.setMigrationRestoreFile(new File(['review'], 'review.edmmigration'));
    });
    await page.waitForFunction(() => window.pg.canRestoreMigration);
    await page.evaluate(() => window.pg.restoreMigrationMutation.mutate());
    await page.waitForFunction(() => window.calls.length === 1);
    await page.evaluate(departure => window.render(1, departure !== 'unmount'), departure);
    await page.waitForFunction(() => window.calls[0].signal.aborted);
    await page.evaluate(() => window.calls[0].resolve({ticket:'obsolete'}));
    await page.evaluate(() => new Promise(resolve => setTimeout(resolve, 60)));
    assert.equal(await page.evaluate(() => window.calls.length), 1, 'late authorization must not start an upload');
    assert.equal(await page.evaluate(() => window.cache.getMutationCache().getAll().length), 0);
    if (departure === 'owner') assert.equal(await page.evaluate(() => window.pg.migrationRestoreFile), null);
  }, true);
  await scenario(async page => {
    await page.evaluate(() => { window.pg.setDatabaseAdminPassword('review-secret'); window.pg.setDatabaseRestoreConfirmation('RESTORE DATABASE'); });
    await page.waitForFunction(() => window.pg.canRestoreDatabase);
    await page.evaluate(() => { window.delayStatus=true; window.pg.databaseRestoreMutation.mutate(); });
    await page.waitForFunction(() => window.calls.length === 1);
    await page.evaluate(() => window.calls[0].resolve({message:'已暂存'}));
    await page.waitForFunction(() => window.statusRefresh && !window.pg.databaseRestoreMutation.isPending);
    assert.equal(await page.evaluate(() => window.pg.databaseAdminPassword), '');
    assert.equal(await page.evaluate(() => window.cache.getMutationCache().getAll().length), 0);
    await page.evaluate(() => window.statusRefresh.reject(new Error('刷新失败')));
    await page.waitForFunction(() => window.pg.message?.includes('操作已完成，刷新失败'));
  }, true);
  await scenario(async page => {
    await page.waitForFunction(() => Boolean(window.plan.draft));
    await page.evaluate(() => { window.plan.setPassword('test-secret-12345'); window.plan.setConfirmation('test-secret-12345'); });
    await page.waitForFunction(() => window.plan.draft.encryptionPassword === 'test-secret-12345');
    await page.evaluate(() => window.plan.save());
    await page.waitForFunction(() => window.calls.some(call => call.method === 'saveBackupPlan'));
    await page.evaluate(() => window.calls.find(call => call.method === 'saveBackupPlan').resolve({revision:2,settingsRevision:2,plan:{enabled:false,time:'03:00',retentionCount:7,retentionDays:0,replicaMode:'None',s3:{}},webDav:{enabled:false,url:'',userName:''},encryptionPasswordSet:true}));
    await page.waitForFunction(() => !window.plan.busy && window.plan.draft.encryptionPassword === '');
    assert.equal(await page.evaluate(() => window.cache.getMutationCache().getAll().length), 0);
  });
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
