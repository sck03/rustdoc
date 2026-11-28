import assert from 'node:assert/strict';
import { createReactUiFixture } from './lib/react-ui-fixture.mjs';

const {web, require, bundle, source:src, launch} = createReactUiFixture('backup-selection-download-ui');
await require('esbuild').build({stdin:{resolveDir:web,loader:'tsx',contents:`
import React from 'react'; import {createRoot} from 'react-dom/client';
import {MemoryRouter} from 'react-router-dom';
import {QueryClient,QueryClientProvider} from '@tanstack/react-query';
import {ConfirmationProvider} from ${src('ui/ConfirmationProvider.tsx')};
import {UnsavedChangesProvider} from ${src('ui/unsavedChangesGuard.tsx')};
import {ApiError} from ${src('api/index.ts')};
import {ApiTransport} from ${src('api/generated/transport.ts')};
import {useBackupPlan} from ${src('features/settings/useBackupPlan.ts')};
import {BackupArchiveRestorePanel} from ${src('features/settings/BackupArchiveRestorePanel.tsx')};
import {StorageGovernancePanel} from ${src('features/settings/StorageGovernancePanel.tsx')};
window.listReads=0; window.submitted=[]; window.downloads=[];
window.point=id=>({id,available:true,fileName:id+'.edmpoint',createdAt:'2026-11-28T00:00:00Z',sizeBytes:10});
const transport=new ApiTransport({baseUrl:'http://127.0.0.1:52123'});
window.client={
 resolveUrl:path=>transport.resolveUrl(path),
 getBackupPlan:async()=>({revision:1,settingsRevision:1,plan:{enabled:false},webDav:{},locations:[]}),
 listRecoveryArchives:request=>{window.listReads++; return new Promise(resolve=>window.resolveList=()=>resolve({items:[window.point(request.page===1?'a'.repeat(32):'b'.repeat(32))],page:request.page,pageSize:20,totalCount:21}));},
 stageRecoveryArchiveRestore:async request=>{window.submitted.push(request);return {jobId:'restore-job'};},
 getJob:async()=>{throw new ApiError(404,{message:'回执暂不可用'},'回执暂不可用');},
 getStorageCapacity:async()=>({areas:[],samples:[],alerts:[],policy:{},revision:1}),
 queryFileRetention:async()=>({items:[{id:'retained',fileName:'signed.pdf',archiveClass:'Signed',activeReferences:0}],nextCursor:''}),
 getFileStorage:async()=>({versionNumber:1,registeredRoots:[]}),
 createRetainedFileDownloadTicket:async()=>({downloadUrl:'/downloads/retained?ticket=test'}),
};
HTMLAnchorElement.prototype.click=function(){window.downloads.push(this.href);};
function Backup({mode}){const c=useBackupPlan(window.client,true,undefined,mode==='recovery');window.c=c;return mode==='recovery'?<BackupArchiveRestorePanel client={window.client} controller={c} selectedId={'b'.repeat(32)} disabled={false} postgres={false}/>:<span>计划</span>;}
const root=createRoot(document.getElementById('root'));
const cache=new QueryClient({defaultOptions:{queries:{retry:false,staleTime:Infinity}}});
window.render=(mode='plan')=>root.render(<MemoryRouter><QueryClientProvider client={cache}><ConfirmationProvider><UnsavedChangesProvider>{mode==='storage'?<StorageGovernancePanel client={window.client} enabled/>:<Backup mode={mode}/>}</UnsavedChangesProvider></ConfirmationProvider></QueryClientProvider></MemoryRouter>);
window.render();
`},outfile:bundle,bundle:true,platform:'browser',format:'iife',jsx:'automatic',logLevel:'silent'});
const browser=await launch();
const errors=[];
try {
  const page=await browser.newPage(); page.setDefaultTimeout(10000);
  page.on('pageerror',error=>errors.push(error.message));
  await page.route('http://tauri.localhost/**',route=>route.fulfill({contentType:'text/html',body:'<!doctype html><html lang="zh-CN"><div id="root"></div></html>'}));
  await page.goto('http://tauri.localhost/');await page.addScriptTag({path:bundle});
  await page.waitForFunction(()=>window.c?.status);
  assert.equal(await page.evaluate(()=>window.listReads),0,'plan tab does not scan repository');
  await page.evaluate(()=>window.render('recovery'));
  const selected=page.getByRole('combobox',{name:/^选择恢复点/}), second='b'.repeat(32);
  await page.waitForFunction(()=>window.resolveList);
  assert.equal(await selected.inputValue(),second,'deep link survives pending list');
  await page.getByLabel('所选备份的密码',{exact:true}).fill('recovery-password');
  await page.getByLabel('恢复确认文本',{exact:true}).fill('RECOVER');
  await page.evaluate(()=>window.resolveList());
  await page.waitForFunction(()=>window.c.archiveQuery.isSuccess);
  assert.equal(await selected.inputValue(),second,'selection outside current page survives');
  await page.getByRole('button',{name:'下一页',exact:true}).click();
  await page.waitForFunction(()=>window.listReads===2);await page.evaluate(()=>window.resolveList());
  await page.waitForFunction(()=>window.c.archives[0]?.id==='b'.repeat(32));
  assert.equal(await selected.inputValue(),second);
  assert.equal(await page.getByLabel('所选备份的密码',{exact:true}).inputValue(),'recovery-password');
  await page.getByRole('button',{name:'校验并安排恢复',exact:true}).click();
  await page.waitForFunction(()=>window.c.jobError);
  assert.equal(await page.evaluate(()=>window.submitted[0].id),second);
  assert.equal(await page.evaluate(()=>window.c.activeJob),true,'lost receipt retains busy state');
  await page.evaluate(()=>window.c.dismissJob());await page.waitForFunction(()=>!window.c.activeJob);
  await page.evaluate(()=>window.render('storage'));
  await page.locator('summary').filter({hasText:'正式档案保留'}).click();
  await page.getByRole('button',{name:'取回',exact:true}).click();
  await page.waitForFunction(()=>window.downloads.length===1);
  assert.equal(await page.evaluate(()=>window.downloads[0]),'http://127.0.0.1:52123/downloads/retained?ticket=test');
  assert.deepEqual(errors,[]);
  console.log('Backup UI: lazy listing, delayed/paged selection, draft preservation, uncertain receipt and API-origin retained download passed.');
} catch (cause) { console.error(errors, await browser.pages().at(-1)?.locator('body').innerText()); throw cause; }
finally { await browser.close(); }
