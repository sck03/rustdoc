import assert from 'node:assert/strict';
import path from 'node:path';
import { createReactUiFixture } from './lib/react-ui-fixture.mjs';

const {web,output,require,source,launch}=createReactUiFixture('hs-transfer-ui');
const result=await require('esbuild').build({write:false,bundle:true,format:'iife',platform:'browser',jsx:'automatic',loader:{'.css':'empty'},logLevel:'silent',stdin:{resolveDir:web,loader:'tsx',contents:`
import React from 'react';import {createRoot} from 'react-dom/client';
import {QueryClient,QueryClientProvider} from '@tanstack/react-query';
import {HsKnowledgeTransfer} from ${source('features/master-data/HsKnowledgeTransfer.tsx')};
import {HsCodeToolsPanel} from ${source('features/master-data/HsCodeToolsPanel.tsx')};
import {ConfirmationProvider} from ${source('ui/ConfirmationProvider.tsx')};
import {ApiError} from ${source('api/index.ts')};
window.calls=[];window.downloads=[];window.cache=new QueryClient({defaultOptions:{queries:{retry:false},mutations:{retry:false}}});
document.addEventListener('click',event=>{if(event.target instanceof HTMLAnchorElement){event.preventDefault();window.downloads.push(event.target.download);}});
const clients=[0,1].map(owner=>new Proxy({}, {get:(_,method)=>(request,options)=>new Promise((resolve,reject)=>window.calls.push({owner,method,request,signal:options?.signal,resolve,reject}))}));
const root=createRoot(document.getElementById('root'));
window.ApiError=ApiError;
window.render=(owner=0,enabled=true)=>root.render(<React.StrictMode><QueryClientProvider client={window.cache}><ConfirmationProvider>{window.mode==='transfer'?<HsKnowledgeTransfer client={clients[owner]} canImport={enabled} canExport={enabled}/>:<HsCodeToolsPanel client={clients[owner]} disabled={!enabled} businessYear={2026} keyword="" mode={window.mode==='import'?'import':'remote'} readOnlySearch={window.mode==='read-only'} onLocalDataChanged={async()=>{}}/>}</ConfirmationProvider></QueryClientProvider></React.StrictMode>);
window.hide=()=>root.unmount();window.render();
`}});
const context=await launch();
let passed=0;
async function scenario(mode,check){
  const page=await context.newPage();page.setDefaultTimeout(10000);const errors=[];
  page.on('pageerror',error=>errors.push(error.message));
  try{await page.setContent('<!doctype html><html lang="zh-CN"><title>文件操作回归</title><div id="root"></div></html>');await page.evaluate(mode=>window.mode=mode,mode);await page.addScriptTag({content:result.outputFiles[0].text});await check(page);assert.deepEqual(errors,[]);passed++;}
  catch(error){await page.screenshot({path:path.join(output,'failure-'+passed+'.png')});throw error;}
  finally{await page.close();}
}
async function startReferenceSave(page) {
  await page.getByLabel('联网查询 HS 编码').fill('棉布');
  await page.getByRole('button',{name:'查询',exact:true}).click();
  await page.waitForFunction(()=>window.calls.length===1);
  await page.evaluate(()=>window.calls[0].resolve({items:[{id:0,code:'5208',name:'远程棉布名称',elements:'',notes:'远程备注',rowVersion:'native:1',remoteRecordKind:'StandardCode'}]}));
  await page.getByRole('button',{name:'保存为本地参考资料'}).click();
  await page.waitForFunction(()=>window.calls.length===2);
  assert.equal(await page.evaluate(()=>window.calls[1].method),'getHsCode');
}
async function existingReference(page, status='ReferenceOnly') {
  await page.evaluate(status=>window.calls[1].resolve({id:7,code:'5208',name:'本地名称',elements:'人工申报要素',notes:'本地核验备注',sourceName:'本地来源',effectiveYear:2026,rowVersion:'native:4',status}),status);
}
try{
  await scenario('remote',async page=>{
    await startReferenceSave(page);
    await page.evaluate(()=>window.calls[1].reject(new window.ApiError(404,'不存在','')));
    await page.waitForFunction(()=>window.calls.length===3);
    const created=await page.evaluate(()=>({method:window.calls[2].method,body:window.calls[2].request.body}));
    assert.equal(created.method,'createHsCode');assert.equal(created.body.id,0);assert.equal(created.body.rowVersion,'');
    await page.evaluate(()=>window.calls[2].resolve({...window.calls[2].request.body,id:7,rowVersion:'native:1'}));
    await page.getByText(/已保存为本地参考资料，导入年度税则后/).waitFor();
  });
  for(const status of [403,503]) await scenario('remote',async page=>{
    await startReferenceSave(page);
    await page.evaluate(status=>window.calls[1].reject(new window.ApiError(status,'Failed',JSON.stringify({message:'本地查询失败'}))),status);
    await page.getByText('本地查询失败',{exact:true}).waitFor();
    assert.equal(await page.evaluate(()=>window.calls.length),2,'Only a confirmed 404 allows creation');
  });
  for(const conflict of [false,true]) await scenario('remote',async page=>{
    await startReferenceSave(page);await existingReference(page);
    await page.getByRole('dialog',{name:'更新本地参考资料'}).waitFor();
    assert.equal(await page.evaluate(()=>window.calls.length),2);
    await page.getByRole('button',{name:'确认更新',exact:true}).click();
    await page.waitForFunction(()=>window.calls.length===3);
    const update=await page.evaluate(()=>({method:window.calls[2].method,body:window.calls[2].request.body}));
    assert.equal(update.method,'updateHsCode');
    assert.deepEqual([update.body.id,update.body.rowVersion,update.body.notes,update.body.sourceName,update.body.effectiveYear,update.body.elements,update.body.name],
      [7,'native:4','本地核验备注','本地来源',2026,'人工申报要素','远程棉布名称']);
    await page.evaluate(conflict=>conflict?window.calls[2].reject(new window.ApiError(409,'Conflict',JSON.stringify({message:'记录版本已变化',code:'ConcurrencyConflict'}))):window.calls[2].resolve({...window.calls[2].request.body,rowVersion:'native:5'}),conflict);
    await page.getByText(conflict?'记录版本已变化':/已更新本地参考资料/,{exact:conflict}).waitFor();
    assert.equal(await page.evaluate(()=>window.calls.length),3,'A conflicting write is never replayed');
    assert.equal(await page.evaluate(()=>window.cache.getMutationCache().getAll().length),0);
  });
  for(const departure of ['cancel','owner','revoke']) await scenario('remote',async page=>{
    await startReferenceSave(page);await existingReference(page);
    await page.getByRole('dialog',{name:'更新本地参考资料'}).waitFor();
    if(departure!=='cancel')await page.evaluate(departure=>window.render(departure==='owner'?1:0,departure!=='revoke'),departure);
    await page.getByRole('button',{name:departure==='cancel'?'取消':'确认更新',exact:true}).click();
    await page.getByRole('dialog').waitFor({state:'hidden'});
    assert.equal(await page.evaluate(()=>window.calls.length),2,'An obsolete or rejected confirmation must not write');
  });
  await scenario('remote',async page=>{
    await startReferenceSave(page);await existingReference(page,'Active');
    await page.getByText('此编码已有税则记录，请在本地 HS 编码中查看或编辑。',{exact:true}).waitFor();
    assert.equal(await page.evaluate(()=>window.calls.length),2);
  });
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
