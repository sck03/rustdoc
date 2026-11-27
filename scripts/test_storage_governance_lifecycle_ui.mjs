import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { locateChromeForTesting } from './lib/chromium-executable.mjs';

const repo = path.resolve(import.meta.dirname, '..'), web = path.join(repo, 'apps/export-doc-web');
const output = path.join(repo, 'artifacts/storage-governance-lifecycle-ui', String(Date.now()));
fs.mkdirSync(path.join(output, 'Temp'), { recursive: true });
Object.assign(process.env, { TEMP: path.join(output, 'Temp'), TMP: path.join(output, 'Temp') });
const require = createRequire(path.join(web, 'package.json'));
const src = name => JSON.stringify(path.join(web, 'src', name).replaceAll('\\', '/'));
await require('esbuild').build({ stdin: { resolveDir: web, loader: 'tsx', contents: `
import React from 'react'; import {createRoot} from 'react-dom/client';
import {MemoryRouter} from 'react-router-dom';
import {QueryClient,QueryClientProvider,useQuery} from '@tanstack/react-query';
import {ApiError} from ${src('api/index.ts')};
import {queryKeys} from ${src('api/queryKeys.ts')};
import {useStorageGovernance} from ${src('features/settings/useStorageGovernance.ts')};
import {useRecoveryRepository} from ${src('features/settings/useRecoveryRepository.ts')};
import {JobTrackingRecovery} from ${src('features/jobs/JobTrackingRecovery.tsx')};
import {RetentionReview} from ${src('features/settings/RetentionReview.tsx')};
import {addPublicationSample} from ${src('features/oa/simulationSamples.ts')};
import {formatErrorResponse} from ${src('ui/formUtils.ts')};
window.addSample=addPublicationSample; window.formatError=formatErrorResponse;
window.calls=[]; window.jobReads=0; window.jobMode='fail'; window.version=1;
window.cache=new QueryClient({defaultOptions:{queries:{retry:false,staleTime:Infinity}}});
const clients=[0,1].map(owner=>new Proxy({}, {get:(_,method)=>(request,options)=>{
 if(method==='getStorageCapacity')return Promise.resolve({areas:[],samples:[],revision:1});
 if(method==='queryFileRetention')return Promise.resolve({items:[],nextCursor:''});
 if(method==='getFileStorage')return Promise.resolve({versionNumber:window.version,registeredRoots:[]});
 if(method==='getRecoveryRepositoryMaintenance')return Promise.resolve({message:''});
 if(method==='getBackupPlan')return Promise.resolve({revision:window.version});
 if(method==='archiveRetainedFiles')return Promise.resolve({jobId:'archive'});
 if(method==='maintainRecoveryRepository'&&!window.defer)return Promise.resolve({jobId:'repository'});
 if(method==='getJob'){
   window.jobReads++;
   return window.jobMode==='fail'?Promise.reject(new ApiError(404,{message:'任务回执暂不可查'})):Promise.resolve({jobId:request.jobId,status:'Succeeded'});
 }
 return new Promise((resolve,reject)=>window.calls.push({owner,method,signal:options?.signal??request?.signal,resolve,reject}));
}}));
function App({owner,token}){
 const c=useStorageGovernance(clients[owner],true),r=useRecoveryRepository(clients[owner]); window.c=c; window.r=r;
 const plan=useQuery({queryKey:queryKeys.backupPlan(),queryFn:()=>clients[owner].getBackupPlan()});
 window.planRevision=plan.data?.revision;
 const t=c.tracking;
 return <>{t.uncertain&&<JobTrackingRecovery jobId={t.jobId} pending={t.job.isFetching} retry={t.retry} dismiss={t.dismiss}/>}
 <RetentionReview key={token} plan={{token,count:1,bytes:15,requestedBy:1,expiresAt:'2026-12-31T00:00:00Z',reason:'保留期已满，依据结案记录',items:[{id:'retained',fileName:'签署凭证.pdf',company:'DEFAULT',archiveClass:'Signed',basisDate:'2020-01-01',expiresOn:'2020-01-02',size:15}]}} disabled={false} approve={()=>window.approved=true}/></>;
}
const root=createRoot(document.getElementById('root'));
window.render=(owner=0,shown=true,token='one')=>root.render(<React.StrictMode><MemoryRouter><QueryClientProvider client={window.cache}>{shown&&<App owner={owner} token={token}/>}</QueryClientProvider></MemoryRouter></React.StrictMode>);
window.render();
` }, outfile: path.join(output, 'app.js'), bundle: true, platform: 'browser', format: 'iife', jsx: 'automatic', logLevel: 'silent' });
const browser = await require('playwright').chromium.launchPersistentContext(path.join(output, 'Chrome'), { executablePath: locateChromeForTesting(repo), headless: true });
try {
  const page = await browser.newPage(); page.setDefaultTimeout(10000);
  const errors = []; page.on('pageerror', error => errors.push(error.message));
  await page.setContent('<!doctype html><html lang="zh-CN"><title>存储任务回归</title><div id="root"></div></html>');
  await page.addScriptTag({ path: path.join(output, 'app.js') });
  await page.waitForFunction(() => window.c?.locations.data && window.planRevision === 1);
  await page.evaluate(() => window.c.run(signal => Promise.resolve({jobId:'archive'})));
  await page.getByRole('button', { name: '重试查询', exact: true }).waitFor();
  const reads = await page.evaluate(() => window.jobReads);
  await page.waitForTimeout(2200);
  assert.equal(await page.evaluate(() => window.jobReads), reads, '404 stops polling');
  await page.evaluate(() => { window.jobMode='done'; window.version=2; });
  await page.getByRole('button', { name: '重试查询', exact: true }).click();
  await page.waitForFunction(() => !window.c.busy && window.c.locations.data.versionNumber === 2);
  await page.evaluate(() => window.r.start({action:'RotatePassword',newPassword:'test-only-password'}));
  await page.waitForFunction(() => !window.r.busy && window.planRevision === 2);
  await page.evaluate(() => { window.jobMode='fail'; window.c.run(()=>Promise.resolve({jobId:'missing'})); });
  await page.getByRole('button', { name: '停止跟踪', exact: true }).click();
  await page.waitForFunction(() => !window.c.busy && !window.c.tracking.jobId);
  assert.match(await page.locator('body').innerText(), /签署凭证.pdf/);
  assert.match(await page.locator('body').innerText(), /依据结案记录/);
  const approve = page.getByRole('button', { name: '独立复核并批准', exact: true });
  assert(await approve.isDisabled());
  await page.getByLabel('已核对处置理由及全部文件明细').check();
  assert(await approve.isEnabled());
  await page.evaluate(() => window.render(0,true,'replaced-plan'));
  await page.waitForFunction(() => !document.querySelector('input[type=checkbox]').checked);
  assert(await approve.isDisabled(), 'replacement token requires a fresh review');
  const samples = await page.evaluate(() => {
    const sample={employeeId:1,appointmentId:'a',amount:'1',currency:'CNY'};
    let list=[]; for(let i=0;i<3;i++)list=window.addSample(list,sample,'员工',5);
    return [list.length,window.addSample(list,sample,'员工',6).length];
  });
  assert.deepEqual(samples,[1,2]);
  const detail=await page.evaluate(()=>window.formatError({message:'校验失败',issues:[{field:'samples[0].serviceId',message:'办理事项已停用',suggestion:'重新选择办理事项'}]}));
  assert.match(detail,/samples\[0\].serviceId：办理事项已停用/); assert.match(detail,/重新选择办理事项/);
  for(const departure of ['owner','unmount']){
    await page.evaluate(()=>{window.defer=true;window.calls=[];window.render(0,true);});
    await page.waitForFunction(()=>window.r&&!window.r.busy);
    await page.evaluate(()=>{ void window.r.start({action:'Verify'}); });
    await page.waitForFunction(()=>window.calls.length===1);
    await page.evaluate(departure=>window.render(1,departure!=='unmount'),departure);
    await page.waitForFunction(()=>window.calls[0].signal.aborted);
    await page.evaluate(()=>window.calls[0].resolve({jobId:'stale'}));
    await page.waitForTimeout(80);
    if(departure==='owner')assert.equal(await page.evaluate(()=>window.r.tracking.jobId),'');
  }
  assert.deepEqual(errors,[]);
  console.log('Storage governance lifecycle: receipt recovery, shared invalidation, scope cancellation, review manifest, sample deduplication and structured errors passed.');
} finally { await browser.close(); }
