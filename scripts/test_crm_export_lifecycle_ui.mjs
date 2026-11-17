import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { locateChromeForTesting } from './lib/chromium-executable.mjs';

const root = path.resolve(import.meta.dirname, '..'), web = path.join(root, 'apps/export-doc-web');
const require = createRequire(path.join(web, 'package.json'));
const output = path.join(root, 'artifacts/crm-export-lifecycle-ui', String(Date.now()));
fs.mkdirSync(path.join(output, 'Temp'), { recursive: true });
Object.assign(process.env, { TEMP: path.join(output, 'Temp'), TMP: path.join(output, 'Temp') });
const source = name => JSON.stringify(path.join(web, 'src', name).replaceAll('\\', '/'));
const bundle = path.join(output, 'app.js');
await require('esbuild').build({ stdin: { loader: 'tsx', resolveDir: web, contents: `
import React,{useState} from 'react';import {createRoot} from 'react-dom/client';
import {QueryClient,QueryClientProvider} from '@tanstack/react-query';
import {MemoryRouter,useNavigate,useLocation} from 'react-router-dom';
import {CustomerFollowUpPage} from ${source('features/crm/CustomerFollowUpPage.tsx')};
import {useInvoiceFileExportOperations} from ${source('features/invoices/useInvoiceFileExportOperations.ts')};
import {useInvoiceDocumentPackageWorkspace} from ${source('features/invoices/useInvoiceDocumentPackageWorkspace.ts')};
import {PermissionAccessProvider} from ${source('app/PermissionAccessContext.tsx')};
import {ConfirmationProvider} from ${source('ui/ConfirmationProvider.tsx')};
import {UnsavedChangesProvider,useHasUnsavedChanges} from ${source('ui/unsavedChangesGuard.tsx')};
window.calls=[];window.dialogs=[];window.messages=[];window.contactReads=[];
window.isTauri=true;window.__TAURI_INTERNALS__={invoke:command=>new Promise(resolve=>window.dialogs.push({command,resolve}))};
const cache=new QueryClient({defaultOptions:{queries:{retry:false},mutations:{retry:false}}});window.cache=cache;
const page=(items,input,total=items.length)=>({items,totalCount:total,totalPages:Math.max(1,Math.ceil(total/input.pageSize)),pageNumber:input.pageNumber,pageSize:input.pageSize});
const customer=id=>({id,name:'Customer '+id,versionNumber:1});
const record=id=>({id,crmCustomerId:1,customerName:'Customer 1',crmContactId:121,contactName:'Contact 121',type:'邮件',summary:'Original '+id,nextAction:'Next',followedUpAt:'2026-11-17T00:00:00Z',nextFollowUpAt:null,versionNumber:1});
const pending=(kind,request,init)=>new Promise((resolve,reject)=>window.calls.push({kind,request,signal:init.signal,resolve,reject}));
const base={
 queryCrmCustomers:async r=>page([customer(1),customer(2)],r),getCrmCustomer:async r=>customer(r.id),
 queryCrmContacts:async r=>{window.contactReads.push(r);return page(Array.from({length:Math.max(0,Math.min(r.pageSize,125-(r.pageNumber-1)*r.pageSize))},(_,i)=>({id:(r.pageNumber-1)*r.pageSize+i+1,crmCustomerId:r.customerId,name:'Contact '+((r.pageNumber-1)*r.pageSize+i+1),versionNumber:1})),r,125)},
 queryCrmFollowUps:async r=>page(r.followUpId?[record(r.followUpId)]:[],r),
 createCrmFollowUp:(r,i)=>pending('save',r,i),updateCrmFollowUp:(r,i)=>pending('save',r,i),transferCrmFollowUp:(r,i)=>pending('transfer',r,i),
 startInvoiceReportPdfSaveToPathJob:(r,i)=>pending('pdf',r,i),startInvoiceBookingSheetSaveToPathJob:(r,i)=>pending('booking',r,i),
 startInvoiceDocumentPackageSaveToPathJob:(r,i)=>pending('package',r,i),
};
const templates=[{templatePath:'builtin:invoice_template.dtpl',displayName:'发票',withSealDefault:true}];
const feedback={clear(){},showError:message=>window.messages.push(message),showJob:message=>window.messages.push(message),showStatus(){}};
function Exports({client,revision,allowed}){
 const common={client,invoiceId:revision,sourceRevision:revision,templates,desktopAvailable:true,defaultExportDirectory:'',feedback};
 const files=useInvoiceFileExportOperations({...common,selectedTemplatePath:templates[0].templatePath,withSeal:true,pdfAllowed:allowed,bookingAllowed:allowed});
 const pack=useInvoiceDocumentPackageWorkspace({...common,previewAllowed:allowed,exportAllowed:allowed,emailAllowed:allowed,onPreviewGenerated(){}});
 window.files=files;window.pack=pack;
 return <output>{files.pdfDestinationPath}|{files.bookingSheetDestinationPath}|{pack.destinationPath}</output>;
}
function Harness(){
 const [client,setClient]=useState(()=>({...base})),[allowed,setAllowed]=useState(true),[shown,setShown]=useState(true),[revision,setRevision]=useState(1);
 window.switchClient=()=>setClient({...base});window.revoke=()=>setAllowed(false);window.hide=()=>setShown(false);window.changeSource=()=>setRevision(2);
 const navigate=useNavigate(),location=useLocation();window.navigate=navigate;window.route=location.search;window.dirty=useHasUnsavedChanges();
 const permissions=['sales.customers','sales.contacts','sales.follow-ups'].flatMap(resourceKey=>(allowed?['view','create','edit','complete','restore','assign','delete','set-primary']:['view']).map(action=>({resourceKey,action,dataScope:'all'})));
 return <PermissionAccessProvider subject={{id:1}} permissions={permissions}>{shown?(window.mode==='exports'?<Exports client={client} revision={revision} allowed={allowed}/>:<CustomerFollowUpPage client={client} businessTimeZone='Asia/Shanghai'/>):<div>closed</div>}</PermissionAccessProvider>;
}
createRoot(document.getElementById('root')).render(<MemoryRouter initialEntries={[window.entry]}><QueryClientProvider client={cache}><ConfirmationProvider><UnsavedChangesProvider><Harness/></UnsavedChangesProvider></ConfirmationProvider></QueryClientProvider></MemoryRouter>);
` }, outfile: bundle, bundle: true, platform: 'browser', format: 'iife', jsx: 'automatic', logLevel: 'silent' });
const context = await require('playwright').chromium.launchPersistentContext(path.join(output, 'Chrome'), { executablePath: locateChromeForTesting(root), headless: true });
const results = [];
const flush = p => p.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
async function scenario(name, mode, entry, check) {
  const p = await context.newPage(), errors = [];
  p.setDefaultTimeout(10000); p.on('pageerror', error => errors.push(error.message));
  try {
    await p.setContent('<!doctype html><html lang="zh-CN"><title>CRM and export lifecycle</title><div id="root"></div></html>');
    await p.evaluate(({mode,entry}) => Object.assign(window,{mode,entry}), {mode,entry}); await p.addScriptTag({path:bundle});
    await check(p); assert.deepEqual(errors, []); results.push(name);
  } catch(error) { await p.screenshot({path:path.join(output,'failure.png'),fullPage:true}); throw new Error(name,{cause:error}); }
  finally { await p.close(); }
}
const editor = '/crm/follow-ups?view=followup-editor&customerId=1';
const summary = p => p.locator('input[name=summary]');
const submit = p => p.getByRole('form',{name:'跟进记录编辑'}).evaluate(form=>{form.requestSubmit();form.requestSubmit()});
try {
  for (const existing of [false,true]) await scenario('preserve concurrent draft: '+existing,'crm',editor+(existing?'&editFollowUpId=9':''),async p=>{
    await summary(p).fill('Submitted'); await submit(p); await p.waitForFunction(()=>window.calls.length===1);
    await summary(p).fill('Typed during save');
    await p.evaluate(()=>{const c=window.calls[0];c.resolve({...c.request.body,id:9,versionNumber:2})});
    await p.waitForFunction(()=>window.route.includes('editFollowUpId=9') && !document.querySelector('button[type=submit]').disabled);
    assert.equal(await summary(p).inputValue(),'Typed during save'); assert.equal(await p.evaluate(()=>window.dirty),true);
    await submit(p); await p.waitForFunction(()=>window.calls.length===2);
    assert.equal(await p.evaluate(()=>window.calls[1].request.body.expectedVersion),2);
    assert.equal(await p.evaluate(()=>window.calls[1].request.id),9);
    await p.evaluate(()=>{const c=window.calls[1];c.resolve({...c.request.body,id:9,versionNumber:3})});
    await p.waitForFunction(()=>window.route.includes('view=followups'));
    assert.equal(await p.evaluate(()=>window.cache.getMutationCache().getAll().length),0);
  });
  for (const change of ['route','client','permission','unmount']) await scenario('ignore late CRM save: '+change,'crm',editor+'&editFollowUpId=9',async p=>{
    await summary(p).fill('Submitted');await submit(p);await p.waitForFunction(()=>window.calls.length===1);
    await p.evaluate(change=>{if(change==='route')window.navigate('/crm/follow-ups?view=followup-editor&customerId=1&editFollowUpId=10');if(change==='client')window.switchClient();if(change==='permission')window.revoke();if(change==='unmount')window.hide()},change);
    await p.waitForFunction(()=>window.calls[0].signal.aborted);
    await p.evaluate(()=>window.calls[0].resolve({...window.calls[0].request.body,id:9,versionNumber:2}));await flush(p);
    assert.equal(await p.getByText('客户跟进已保存。',{exact:true}).count(),0);
    if(change==='route')assert.equal(await summary(p).inputValue(),'Original 10');
  });
  await scenario('contact beyond first hundred remains selected and reachable','crm',editor+'&editFollowUpId=9',async p=>{
    await summary(p).waitFor(); const contact=p.locator('select[name=crmContactId]');
    assert.equal(await contact.inputValue(),'121');assert.match(await contact.locator('option:checked').innerText(),/Contact 121/);
    await p.getByRole('button',{name:'末页',exact:true}).click();await p.waitForFunction(()=>window.contactReads.some(r=>r.pageNumber===7));
    await contact.selectOption('125');await p.getByRole('button',{name:'首页',exact:true}).click();await flush(p);
    assert.equal(await contact.inputValue(),'125');assert.match(await contact.locator('option:checked').innerText(),/Contact 125/);
  });
  for (const kind of ['pdf','booking','package']) for(const change of ['source','client','permission','unmount']) {
    await scenario('dialog scope: '+kind+'/'+change,'exports','/',async p=>{
      await p.waitForFunction(()=>window.files&&window.pack.selectedTemplates.length===1);
      await p.evaluate(kind=>{if(kind==='pdf')window.files.exportPdfWithSaveDialog();if(kind==='booking')window.files.exportBookingSheetWithSaveDialog();if(kind==='package')window.pack.pickDestination()},kind);
      await p.waitForFunction(()=>window.dialogs.length===1);
      await p.evaluate(change=>{if(change==='source')window.changeSource();if(change==='client')window.switchClient();if(change==='permission')window.revoke();if(change==='unmount')window.hide()},change);await flush(p);
      await p.evaluate(()=>window.dialogs[0].resolve('E:\\selected-file.pdf'));await flush(p);
      assert.equal(await p.evaluate(()=>window.calls.length),0);assert.deepEqual(await p.evaluate(()=>window.messages),[]);
      assert.doesNotMatch(await p.locator('body').innerText(),/selected-file/);
    });
    await scenario('request scope: '+kind+'/'+change,'exports','/',async p=>{
      await p.waitForFunction(()=>window.files&&window.pack.selectedTemplates.length===1);
      await p.evaluate(kind=>{if(kind==='pdf'){window.files.changePdfDestination('E:\\out.pdf')}if(kind==='booking'){window.files.changeBookingSheetDestination('E:\\out.xlsx')}if(kind==='package'){window.pack.changeDestination('E:\\out.zip')}},kind);await flush(p);
      await p.evaluate(kind=>{if(kind==='pdf')window.files.generatePdf();if(kind==='booking')window.files.generateBookingSheet();if(kind==='package')window.pack.generatePackage()},kind);
      await p.waitForFunction(()=>window.calls.length===1);
      await p.evaluate(change=>{if(change==='source')window.changeSource();if(change==='client')window.switchClient();if(change==='permission')window.revoke();if(change==='unmount')window.hide()},change);
      await p.waitForFunction(()=>window.calls[0].signal.aborted);
      await p.evaluate(()=>window.calls[0].resolve({jobId:'stale-job'}));await flush(p);
      assert.deepEqual(await p.evaluate(()=>window.messages),[]);assert.equal(await p.evaluate(()=>window.cache.getMutationCache().getAll().length),0);
    });
  }
  console.log('CRM/export lifecycle: '+results.length+' passed; '+output);
} finally { await context.close();fs.writeFileSync(path.join(output,'summary.json'),JSON.stringify({passed:results.length,results},null,2)); }
