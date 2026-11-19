import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { locateChromeForTesting } from './lib/chromium-executable.mjs';

const repo=path.resolve(import.meta.dirname,'..'),web=path.join(repo,'apps/export-doc-web');
const output=path.join(repo,'artifacts/hs-transfer-ui',String(Date.now()));
fs.mkdirSync(path.join(output,'Temp'),{recursive:true});
Object.assign(process.env,{TEMP:path.join(output,'Temp'),TMP:path.join(output,'Temp')});
const require=createRequire(path.join(web,'package.json'));
const source=name=>JSON.stringify(path.join(web,'src',name).replaceAll('\\','/'));
const result=await require('esbuild').build({write:false,bundle:true,format:'iife',platform:'browser',jsx:'automatic',loader:{'.css':'empty'},logLevel:'silent',stdin:{resolveDir:web,loader:'tsx',contents:`
import React from 'react';import {createRoot} from 'react-dom/client';
import {QueryClient,QueryClientProvider} from '@tanstack/react-query';
import {HsKnowledgeTransfer} from ${source('features/master-data/HsKnowledgeTransfer.tsx')};
import {HsCodeToolsPanel} from ${source('features/master-data/HsCodeToolsPanel.tsx')};
window.calls=[];window.downloads=[];window.cache=new QueryClient({defaultOptions:{queries:{retry:false},mutations:{retry:false}}});
document.addEventListener('click',event=>{if(event.target instanceof HTMLAnchorElement){event.preventDefault();window.downloads.push(event.target.download);}});
const clients=[0,1].map(owner=>new Proxy({}, {get:(_,method)=>(request,options)=>new Promise((resolve,reject)=>window.calls.push({owner,method,request,signal:options?.signal,resolve,reject}))}));
const root=createRoot(document.getElementById('root'));
window.render=(owner=0,enabled=true)=>root.render(<React.StrictMode><QueryClientProvider client={window.cache}>{window.mode==='transfer'?<HsKnowledgeTransfer client={clients[owner]} canImport={enabled} canExport={enabled}/>:<HsCodeToolsPanel client={clients[owner]} disabled={!enabled} businessYear={2026} keyword="" mode={window.mode==='import'?'import':'remote'} readOnlySearch={window.mode==='read-only'} onLocalDataChanged={async()=>{}}/>}</QueryClientProvider></React.StrictMode>);
window.hide=()=>root.unmount();window.render();
`}});
const context=await require('playwright').chromium.launchPersistentContext(path.join(output,'Chrome'),{executablePath:locateChromeForTesting(repo),headless:true});
let passed=0;
async function scenario(mode,check){
  const page=await context.newPage();page.setDefaultTimeout(10000);const errors=[];
  page.on('pageerror',error=>errors.push(error.message));
  try{await page.setContent('<!doctype html><html lang="zh-CN"><title>文件操作回归</title><div id="root"></div></html>');await page.evaluate(mode=>window.mode=mode,mode);await page.addScriptTag({content:result.outputFiles[0].text});await check(page);assert.deepEqual(errors,[]);passed++;}
  catch(error){await page.screenshot({path:path.join(output,'failure-'+passed+'.png')});throw error;}
  finally{await page.close();}
}
try{
  for(const [mode,method] of [['read-only','searchRemoteHsCodes'],['capture','captureRemoteHsCodes']]) await scenario(mode,async page=>{
    await page.getByLabel('联网查询 HS 编码').fill('棉布');
    await page.getByRole('button',{name:'查询',exact:true}).click();
    await page.waitForFunction(()=>window.calls.length===1);
    assert.equal(await page.evaluate(()=>window.calls[0].method),method);
    await page.evaluate(()=>window.calls[0].resolve({items:[{code:'5208',name:'棉布',detailUrl:'https://example.test/hs/5208'}]}));
    const save=page.getByRole('button',{name:'保存为本地参考资料'});
    await save.waitFor();assert.equal(await save.isDisabled(),mode==='read-only');
    assert.equal(await page.evaluate(()=>window.calls.length),1);
    const detail=page.getByRole('button',{name:'补全详情',exact:true});
    assert.equal(await detail.isDisabled(),false);await detail.click();
    await page.waitForFunction(()=>window.calls.length===2);
    assert.equal(await page.evaluate(()=>window.calls[1].method),mode==='read-only'?'fetchRemoteHsCodeDetail':'resolveRemoteHsCodeDetail');
    assert.equal(await page.evaluate(()=>window.calls[1].request.body.code),'5208');
    await page.evaluate(mode=>{const item={...window.calls[1].request.body,elements:'成分含量、织造方法'};
      window.calls[1].resolve(mode==='read-only'?item:{items:[item],removedItems:[],updatedCount:1,removedCount:0,message:'远程详情已读取。'});},mode);
    await page.getByText('远程详情已读取。',{exact:true}).waitFor();
    await page.getByRole('button',{name:'查看详情',exact:true}).click();
    await page.getByText('成分含量、织造方法',{exact:true}).waitFor();
    assert.equal(await save.isDisabled(),mode==='read-only');
    assert.equal(await page.evaluate(()=>window.calls.length),2,'Reading detail must not implicitly save or capture it');
  });
  for(const departure of ['owner','revoke','unmount']) for(const outcome of ['success','failure']) await scenario('transfer',async page=>{
    await page.getByRole('button',{name:'导出 .edmhs',exact:true}).click();await page.waitForFunction(()=>window.calls.length===1);
    await page.evaluate(departure=>departure==='unmount'?window.hide():window.render(departure==='owner'?1:0,departure!=='revoke'),departure);
    await page.waitForFunction(()=>window.calls[0].signal.aborted);
    await page.evaluate(outcome=>outcome==='success'?window.calls[0].resolve(new Blob(['late'])):window.calls[0].reject(Error('late error')),outcome);
    await page.evaluate(()=>new Promise(resolve=>setTimeout(resolve,30)));
    assert.equal(await page.evaluate(()=>window.downloads.length),0);
    assert.equal(await page.getByText('late error',{exact:true}).count(),0);
  });
  await scenario('transfer',async page=>{
    for(let n=0;n<6;n++){
      await page.locator('input[type=file]').setInputFiles({name:'library.edmhs',mimeType:'application/octet-stream',buffer:Buffer.alloc(1024*1024)});
      await page.waitForFunction(n=>window.calls.length===n+1,n);
      await page.evaluate(n=>window.calls[n].resolve({result:{message:'imported '+n}}),n);
      await page.getByText('imported '+n,{exact:true}).waitFor();
      assert.equal(await page.locator('input[type=file]').inputValue(),'');
    }
    assert.equal(await page.evaluate(()=>window.cache.getMutationCache().getAll().length),0);
  });
  await scenario('import',async page=>{
    await page.locator('input[type=file]').setInputFiles({name:'codes.xlsx',mimeType:'application/octet-stream',buffer:Buffer.alloc(1024*1024)});
    await page.waitForFunction(()=>window.calls.length===1);
    await page.evaluate(()=>window.hide());await page.waitForFunction(()=>window.calls[0].signal.aborted);
    await page.evaluate(()=>window.calls[0].resolve({token:'old',items:[]}));
    await page.evaluate(()=>new Promise(resolve=>setTimeout(resolve,30)));
    assert.equal(await page.evaluate(()=>window.cache.getMutationCache().getAll().length),0);
  });
  console.log('HS file operations: '+passed+' scenarios passed; '+output);
}finally{await context.close();}
