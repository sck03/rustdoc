import assert from 'node:assert/strict';
import { createReactUiFixture } from './lib/react-ui-fixture.mjs';

const { web, require, bundle, source: src, launch } = createReactUiFixture('storage-receipts-ui');
await require('esbuild').build({ stdin: { resolveDir: web, loader: 'tsx', contents: `
import React from 'react'; import {createRoot} from 'react-dom/client';
import {QueryClient,QueryClientProvider} from '@tanstack/react-query';
import {MemoryRouter} from 'react-router-dom';
import {useFileStorage} from ${src('features/settings/useFileStorage.ts')};
import {FileStorageCredentials} from ${src('features/settings/FileStorageCredentials.tsx')};
import {ConfirmationProvider} from ${src('ui/ConfirmationProvider.tsx')};
import {UnsavedChangesProvider} from ${src('ui/unsavedChangesGuard.tsx')};
window.writes=[];window.failed=false;
const status={kind:'s3',versionNumber:1,credentialVersion:1,s3:{accessKeyId:'first'},policy:{fileBytes:100,requestBytes:200,requestFiles:10,quotaBytes:0}};
const client={getFileStorage:async()=>{if(window.failed)throw Error('状态暂不可用');return status;},getFileStoragePolicy:async()=>status.policy,
 updateFileStoragePolicy:async({body})=>{window.writes.push(body);window.failed=true;return {versionNumber:body.expectedVersion+1,policy:body.policy};},
 updateFileStorageCredentials:async({body})=>{window.writes.push(body);window.failed=true;return {credentialVersion:body.expectedVersion+1,accessKeyId:body.accessKeyId};}};
window.cache=new QueryClient({defaultOptions:{queries:{retry:false,staleTime:Infinity}}});
function App(){const c=useFileStorage(client,true);window.c=c;
 return <><p>{c.success}</p><p>{c.message}</p>{c.status&&<FileStorageCredentials client={client} status={c.status} disabled={false}/>}</>;}
createRoot(document.getElementById('root')).render(<React.StrictMode><MemoryRouter><QueryClientProvider client={window.cache}><ConfirmationProvider><UnsavedChangesProvider><App/></UnsavedChangesProvider></ConfirmationProvider></QueryClientProvider></MemoryRouter></React.StrictMode>);
` }, outfile: bundle, bundle: true, platform: 'browser', format: 'iife', jsx: 'automatic', logLevel: 'silent' });
const context = await launch();
try {
  const page = await context.newPage(); page.setDefaultTimeout(10000);
  const errors = []; page.on('pageerror', error => errors.push(error.message));
  await page.setContent('<!doctype html><html lang="zh-CN"><title>存储提交回执回归</title><div id="root"></div></html>');
  await page.addScriptTag({path:bundle});
  await page.waitForFunction(()=>window.c?.status).catch(error=>{throw new Error(errors.join('; ') || error.message);});
  await page.evaluate(()=>window.c.patchPolicy('fileBytes',80));
  await page.waitForFunction(()=>window.c.dirty);
  await page.evaluate(()=>window.c.save());
  await page.getByText('容量策略已保存。',{exact:true}).waitFor();
  await page.getByText('容量策略已保存，状态刷新失败：状态暂不可用',{exact:true}).waitFor();
  assert.equal(await page.evaluate(()=>window.c.status.versionNumber),2);
  await page.evaluate(()=>window.c.patchPolicy('fileBytes',60));
  await page.waitForFunction(()=>window.c.policy.fileBytes===60);
  await page.evaluate(()=>window.c.save());
  await page.waitForFunction(()=>window.c.status.versionNumber===3);
  assert.equal(await page.evaluate(()=>window.writes[1].expectedVersion),2);
  await page.locator('summary').filter({hasText:'更新存储凭据'}).click();
  await page.getByLabel('Access Key ID',{exact:true}).fill('second');
  await page.getByLabel('Secret Access Key',{exact:true}).fill('test-secret');
  await page.getByRole('button',{name:'校验并更新凭据',exact:true}).click();
  await page.getByText('凭据已更新。',{exact:true}).waitFor();
  await page.getByText('凭据已更新，状态刷新失败：状态暂不可用',{exact:true}).waitFor();
  assert.equal(await page.getByLabel('Secret Access Key',{exact:true}).inputValue(),'');
  assert.equal(await page.evaluate(()=>window.c.status.credentialVersion),2);
  assert.equal(await page.evaluate(()=>window.c.status.s3.accessKeyId),'second');
  assert.deepEqual(errors,[]);
  console.log('Storage receipts UI passed: durable versions, independent failed refresh and cleared secrets.');
} finally { await context.close(); }
