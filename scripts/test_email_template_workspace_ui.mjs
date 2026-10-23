import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { locateChromeForTesting } from './lib/chromium-executable.mjs';

const repo=path.resolve(import.meta.dirname,'..'), web=path.join(repo,'apps/export-doc-web');
const output=path.join(repo,'artifacts/email-template-workspace-ui',String(Date.now()));
fs.mkdirSync(path.join(output,'Temp'),{recursive:true});
Object.assign(process.env,{TEMP:path.join(output,'Temp'),TMP:path.join(output,'Temp')});
const require=createRequire(path.join(web,'package.json'));
const src=name=>JSON.stringify(path.join(web,'src',name).replaceAll('\\','/'));
const bundle=path.join(output,'app.js');
await require('esbuild').build({stdin:{resolveDir:web,loader:'tsx',contents:`
import React from 'react'; import { createRoot } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { EmailTemplatePage } from ${src('features/email-templates/EmailTemplatePage.tsx')};
import { PermissionAccessProvider } from ${src('app/PermissionAccessContext.tsx')};
import { ConfirmationProvider } from ${src('ui/ConfirmationProvider.tsx')};
import { UnsavedChangesProvider } from ${src('ui/unsavedChangesGuard.tsx')};
const row=id=>({id,name:'Template '+id,category:'通用',subject:'Subject '+id,bodyHtml:'<p>Body</p>',status:'Draft',shareScope:'Private',versionNumber:3,canEdit:true,canRestore:true,canArchive:true});
window.rows=[row(1),row(2)];window.saves=[];window.histories=[];window.restores=[];
window.finishSave=()=>{ const p=window.saves.at(-1);const saved={...window.rows.find(r=>r.id===p.request.id),...p.request.body,id:p.request.id||3,versionNumber:p.request.body.expectedVersion+1,canEdit:true};window.rows=[...window.rows.filter(r=>r.id!==saved.id),saved];p.resolve(saved); };
const client={listEmailTemplates:async()=>{if(window.failRefresh)throw new Error('refresh failed');return structuredClone(window.rows);},listEmailTemplateVariables:async()=>[],
saveEmailTemplateDraft:(request,{signal})=>new Promise(resolve=>window.saves.push({request,signal,resolve})),createEmailTemplate:(request,{signal})=>new Promise(resolve=>window.saves.push({request:{...request,id:0},signal,resolve})),
listEmailTemplateVersions:(request,{signal})=>new Promise(resolve=>window.histories.push({request,signal,resolve})),
restoreEmailTemplateVersion:async request=>{window.restores.push(request);return {...window.rows.find(r=>r.id===request.id),versionNumber:4};}};
const permissions=['view','edit','restore','archive'].map(action=>({resourceKey:'sales.email-templates',action,dataScope:'all'}));
createRoot(document.getElementById('root')).render(<MemoryRouter><PermissionAccessProvider permissions={permissions}><ConfirmationProvider><UnsavedChangesProvider><EmailTemplatePage client={client}/></UnsavedChangesProvider></ConfirmationProvider></PermissionAccessProvider></MemoryRouter>);
`},outfile:bundle,bundle:true,platform:'browser',format:'iife',jsx:'automatic',logLevel:'silent'});
const context=await require('playwright').chromium.launchPersistentContext(path.join(output,'Chrome'),{executablePath:locateChromeForTesting(repo),headless:true});
let passed=0;
async function scenario(check){
  const page=await context.newPage();page.setDefaultTimeout(6000);
  const errors=[];page.on('pageerror',e=>errors.push(e.message));
  try {
    await page.setContent('<!doctype html><html lang="zh-CN"><title>模板并发回归</title><div id="root"></div></html>');
    await page.addScriptTag({path:bundle});
    await page.getByText('Template 1',{exact:true}).waitFor();
    const open=async id=>{await page.getByRole('row').filter({has:page.getByText('Template '+id,{exact:true})}).getByRole('button').click();};
    await check(page,open);assert.deepEqual(errors,[]);passed++;
  }catch(e){await page.screenshot({path:path.join(output,'failure-'+passed+'.png'),fullPage:true});throw e;}
  finally{await page.close();}
}
try{
  await scenario(async(page,open)=>{
    await open(1);await page.getByLabel('邮件主题',{exact:true}).fill('submitted');
    await page.getByRole('button',{name:'保存草稿',exact:true}).click();
    await page.waitForFunction(()=>window.saves.length===1);
    await page.getByLabel('邮件主题',{exact:true}).fill('newer unsaved');
    await page.evaluate(()=>window.finishSave());
    await page.getByText('邮件模板草稿已保存。',{exact:true}).waitFor();
    assert.equal(await page.getByLabel('邮件主题',{exact:true}).inputValue(),'newer unsaved');
    await page.getByRole('button',{name:'保存草稿',exact:true}).click();
    await page.waitForFunction(()=>window.saves.length===2);
    assert.equal(await page.evaluate(()=>window.saves[1].request.body.expectedVersion),4);
  });
  await scenario(async(page,open)=>{
    await open(1);await page.getByLabel('邮件主题',{exact:true}).fill('saved with failed refresh');
    await page.evaluate(()=>window.failRefresh=true);
    await page.getByRole('button',{name:'保存草稿',exact:true}).click();
    await page.waitForFunction(()=>window.saves.length===1);await page.evaluate(()=>window.finishSave());
    await page.getByText(/目录刷新失败/).waitFor();
    await page.getByRole('button',{name:'保存草稿',exact:true}).click();
    await page.waitForFunction(()=>window.saves.length===2);
    assert.equal(await page.evaluate(()=>window.saves[1].request.body.expectedVersion),4);
  });
  await scenario(async(page,open)=>{
    await open(1);await page.getByRole('tab',{name:'版本历史',exact:true}).click();
    await page.waitForFunction(()=>window.histories.length===1);
    await page.evaluate(()=>window.histories[0].resolve([{id:12,emailTemplateId:1,versionNumber:2,subject:'Historical A',bodyHtml:'A',canRestore:true}]));
    await page.getByLabel('历史主题',{exact:true}).waitFor();
    await page.getByRole('tab',{name:'模板目录',exact:true}).click();await open(2);
    await page.getByRole('tab',{name:'版本历史',exact:true}).click();
    await page.waitForFunction(()=>window.histories.length===2);
    assert.equal(await page.getByLabel('历史主题',{exact:true}).count(),0);
    assert.equal(await page.getByRole('button',{name:'恢复此版本',exact:true}).count(),0);
    await page.evaluate(()=>window.histories[1].resolve([{id:22,emailTemplateId:2,versionNumber:2,subject:'Historical B',bodyHtml:'B',canRestore:true}]));
    await page.getByRole('button',{name:'恢复此版本',exact:true}).click();
    await page.getByRole('button',{name:'确认恢复',exact:true}).click();
    await page.waitForFunction(()=>window.restores.length===1);
    assert.equal(await page.evaluate(()=>window.restores[0].id),2);
  });
  await scenario(async(page,open)=>{
    await open(1);await page.getByRole('tab',{name:'版本历史',exact:true}).click();
    await page.waitForFunction(()=>window.histories.length===1);
    await page.getByRole('tab',{name:'模板目录',exact:true}).click();await open(2);
    await page.getByRole('tab',{name:'版本历史',exact:true}).click();await page.waitForFunction(()=>window.histories.length===2);
    await page.evaluate(()=>{window.histories[1].resolve([{id:22,emailTemplateId:2,versionNumber:2,subject:'Historical B',bodyHtml:'B',canRestore:true}]);window.histories[0].resolve([{id:12,emailTemplateId:1,versionNumber:2,subject:'Historical A',bodyHtml:'A',canRestore:true}]);});
    await page.waitForFunction(()=>document.querySelector('input[readonly]')?.value==='Historical B');
    assert.equal(await page.getByLabel('历史主题',{exact:true}).inputValue(),'Historical B');
  });
  console.log('Email template workspace UI: '+passed+' passed; '+output);
}finally{await context.close();}
