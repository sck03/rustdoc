import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { locateChromeForTesting } from './lib/chromium-executable.mjs';

const repo=path.resolve(import.meta.dirname,'..'),web=path.join(repo,'apps/export-doc-web');
const output=path.join(repo,'artifacts/report-template-commands-ui',String(Date.now()));
fs.mkdirSync(path.join(output,'Temp'),{recursive:true});
Object.assign(process.env,{TEMP:path.join(output,'Temp'),TMP:path.join(output,'Temp')});
const require=createRequire(path.join(web,'package.json'));
const src=name=>JSON.stringify(path.join(web,'src',name).replaceAll('\\','/'));
await require('esbuild').build({stdin:{resolveDir:web,loader:'tsx',contents:`
import React from 'react';import {createRoot} from 'react-dom/client';
import {QueryClient,QueryClientProvider,useQuery} from '@tanstack/react-query';
import {useReportTemplateSaveMutations} from ${src('features/reports/useReportTemplateSaveMutations.ts')};
import {useReportTemplateFileMutations} from ${src('features/reports/useReportTemplateFileMutations.ts')};
import {queryKeys} from ${src('api/queryKeys.ts')};
window.calls=[];window.success=[];window.errors=[];window.refreshes=[];window.downloads=[];
document.addEventListener('click',event=>{if(event.target instanceof HTMLAnchorElement){event.preventDefault();window.downloads.push(event.target.download);}});
window.cache=new QueryClient({defaultOptions:{queries:{retry:false},mutations:{retry:false}}});
const clients=[0,1].map(owner=>new Proxy({}, {get:(_,method)=>(request,options)=>new Promise((resolve,reject)=>window.calls.push({owner,method,request,signal:options?.signal,resolve,reject}))}));
function App({scope,owner}) {
 const client=clients[owner];
 useQuery({queryKey:queryKeys.reportTemplates('ExportDocument'),queryFn:()=>new Promise((resolve,reject)=>window.refreshes.push({resolve,reject})),initialData:[],staleTime:Infinity});
 const common={client,reportType:'ExportDocument',selectedTemplatePath:scope,expectedRevision:'rev',onError:error=>window.errors.push(error.message)};
 const save=useReportTemplateSaveMutations({...common,expectedUserVersion:1,currentUserTemplate:null,content:'body',userTemplateName:'name',onDefaultTemplateSaved:saved=>window.success.push(saved),onUserTemplateSaved:saved=>window.success.push(saved)});
 const file=useReportTemplateFileMutations({...common,fileExportPath:'test.dtpl',onExported:()=>window.success.push('export'),onDownloaded:()=>window.success.push('download'),onImported:()=>window.success.push('import')});
 window.save=()=>save.saveDefaultTemplateMutation.mutate(undefined);
 window.download=()=>file.downloadFileMutation.mutate();
 window.upload=()=>file.uploadFileMutation.mutate(new File(['x'.repeat(1024*1024)],'test.dtpl'));
 window.pending=save.saveDefaultTemplateMutation.isPending||file.downloadFileMutation.isPending||file.uploadFileMutation.isPending;
 return <output>{window.pending?'pending':'ready'}</output>;
}
const root=createRoot(document.getElementById('root'));
window.render=(scope='a.dtpl',owner=0)=>root.render(<React.StrictMode><QueryClientProvider client={window.cache}><App scope={scope} owner={owner}/></QueryClientProvider></React.StrictMode>);
window.unmount=()=>root.unmount();window.render();
`},outfile:path.join(output,'app.js'),bundle:true,platform:'browser',format:'iife',jsx:'automatic',logLevel:'silent'});
const context=await require('playwright').chromium.launchPersistentContext(path.join(output,'Chrome'),{executablePath:locateChromeForTesting(repo),headless:true});
let passed=0;
async function scenario(check){
 const page=await context.newPage();page.setDefaultTimeout(8000);const errors=[];
 page.on('pageerror',e=>errors.push(e.message));
 try{await page.setContent('<!doctype html><html lang="zh-CN"><title>模板操作回归</title><div id="root"></div></html>');await page.addScriptTag({path:path.join(output,'app.js')});await page.waitForFunction(()=>typeof window.save==='function');await check(page);assert.deepEqual(errors,[]);passed++;}
 catch(e){await page.screenshot({path:path.join(output,'failure-'+passed+'.png')});throw e;}
 finally{await page.close();}
}
try{
 for(const departure of ['scope','owner','unmount']) for(const outcome of ['success','failure']) await scenario(async page=>{
  await page.evaluate(()=>window.save());await page.waitForFunction(()=>window.calls.length===1);
  await page.evaluate(departure=>{if(departure==='unmount')window.unmount();else window.render(departure==='scope'?'b.dtpl':'a.dtpl',departure==='owner'?1:0);},departure);
  await page.waitForFunction(()=>window.calls[0].signal.aborted);
  await page.evaluate(outcome=>{window.cache.clear();const call=window.calls[0];if(outcome==='success')call.resolve({templatePath:'a.dtpl',content:'stale',revision:'r2'});else call.reject(new Error('stale failure'));},outcome);
  await page.evaluate(()=>new Promise(resolve=>setTimeout(resolve,50)));
  assert.deepEqual(await page.evaluate(()=>({success:window.success.length,errors:window.errors.length,cache:window.cache.getQueryCache().getAll().length})),{success:0,errors:0,cache:0});
 });
 await scenario(async page=>{
  await page.evaluate(()=>{window.save();window.save();});await page.waitForFunction(()=>window.calls.length===1);
  await page.evaluate(()=>window.calls[0].resolve({templatePath:'a.dtpl',content:'saved',revision:'r2'}));
  await page.waitForFunction(()=>window.success.length===1&&!window.pending&&window.refreshes.length===1);
  assert.equal(await page.evaluate(()=>window.cache.getMutationCache().getAll().length),0);
  await page.evaluate(()=>window.refreshes[0].reject(new Error('refresh failed')));
  await page.waitForFunction(()=>window.errors.length===1);
  assert.match(await page.evaluate(()=>window.errors[0]),/操作已完成，刷新失败/);
 });
 await scenario(async page=>{
  await page.evaluate(()=>window.save());await page.waitForFunction(()=>window.calls.length===1);
  await page.evaluate(()=>window.calls[0].resolve({templatePath:'a.dtpl',content:'first',revision:'r2'}));
  await page.waitForFunction(()=>window.success.length===1&&!window.pending&&window.refreshes.length===1);
  await page.evaluate(()=>window.save());await page.waitForFunction(()=>window.calls.length===2);
  await page.evaluate(()=>window.refreshes[0].reject(new Error('older refresh failed')));
  await page.evaluate(()=>new Promise(resolve=>setTimeout(resolve,50)));
  assert.equal(await page.evaluate(()=>window.errors.length),0);
  await page.evaluate(()=>window.calls[1].resolve({templatePath:'a.dtpl',content:'second',revision:'r3'}));
  await page.waitForFunction(()=>window.success.length===2&&!window.pending);
 });
 await scenario(async page=>{
  await page.evaluate(()=>window.download());await page.waitForFunction(()=>window.calls.length===1);
  await page.evaluate(()=>window.render('b.dtpl'));await page.waitForFunction(()=>window.calls[0].signal.aborted);
  await page.evaluate(()=>window.calls[0].resolve(new Blob(['old file'])));
  await page.evaluate(()=>new Promise(resolve=>setTimeout(resolve,50)));
  assert.equal(await page.evaluate(()=>window.downloads.length),0);
 });
 await scenario(async page=>{
  for(let n=0;n<10;n++){
   await page.evaluate(()=>window.upload());await page.waitForFunction(n=>window.calls.length===n+1,n);
   await page.evaluate(n=>window.calls[n].resolve({templatePath:'a.dtpl',content:'x'.repeat(1024*1024),revision:'r'+n}),n);
   await page.waitForFunction(n=>window.success.length===n+1&&!window.pending,n);
  }
  assert.equal(await page.evaluate(()=>window.cache.getMutationCache().getAll().length),0);
 });
 console.log('Report template command UI: '+passed+' passed; '+output);
}finally{await context.close();}
