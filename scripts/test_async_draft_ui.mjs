import assert from 'node:assert/strict';
import { createReactUiFixture } from './lib/react-ui-fixture.mjs';

const { web, require, bundle, source: src, launch } = createReactUiFixture('async-draft-ui');
await require('esbuild').build({ stdin: { resolveDir: web, loader: 'tsx', contents: `
import React from 'react'; import {createRoot} from 'react-dom/client';
import {MemoryRouter,useLocation} from 'react-router-dom';
import {QueryClient,QueryClientProvider} from '@tanstack/react-query';
import {EmailTemplatePage} from ${src('features/email-templates/EmailTemplatePage.tsx')};
import {StorageGovernancePanel} from ${src('features/settings/StorageGovernancePanel.tsx')};
import {PermissionAccessProvider} from ${src('app/PermissionAccessContext.tsx')};
import {ConfirmationProvider} from ${src('ui/ConfirmationProvider.tsx')};
import {UnsavedChangesProvider} from ${src('ui/unsavedChangesGuard.tsx')};
window.calls=[];window.deferPreview=false;
window.capacity={revision:1,policy:{warningPercent:85,criticalPercent:95,minimumFreeBytes:0,growthDays:14},areas:[],samples:[],updatedAt:'2026-11-28T00:00:00Z',alerts:[{area:'Files',at:'2026-11-28T00:00:00Z',state:'Warning',message:'测试文件库空间不足，请扩容。'}]};
const pending=(method,request,options)=>new Promise((resolve,reject)=>window.calls.push({method,request,signal:options?.signal??request?.signal,resolve,reject}));
const clients=[0,1].map(owner=>({
 listEmailTemplates:async()=>({items:[],pageNumber:1,totalPages:1,totalCount:0}),
 listEmailTemplateVariables:async()=>[{key:'customer',label:'客户名称',sampleValue:'样例',description:'',token:'{{customer}}'},{key:'note',label:'手工备注',sampleValue:'',description:'',token:'{{note}}'}],
 queryCrmCustomers:(r,o)=>window.deferSearch?pending('search',r,o):Promise.resolve({items:[{id:1,name:'客户 A'},{id:2,name:'客户 B'}]}),
 getCrmEmailVariableDraft:(r,o)=>pending('customer',r,o),
 previewEmailTemplate:(r,o)=>window.deferPreview?pending('preview',r,o):Promise.resolve({subject:r.body.subject,bodyHtml:r.body.bodyHtml,unresolvedTokens:[]}),
 getStorageCapacity:async()=>structuredClone(window.capacity),
 queryFileRetention:async()=>({items:[],nextCursor:''}),getFileStorage:async()=>({registeredRoots:[]}),
 saveStorageCapacityPolicy:(r,o)=>pending('capacity',r,o)
}));
const permissions=[{resourceKey:'sales.customers',action:'view',dataScope:'all'},{resourceKey:'common.email-delivery',action:'send',dataScope:'all'},...['view','edit'].map(action=>({resourceKey:'sales.email-templates',action,dataScope:'all'}))];
const root=createRoot(document.getElementById('root'));
const cache=new QueryClient({defaultOptions:{queries:{retry:false,staleTime:Infinity}}});
function Location(){window.locationState=useLocation();return null;}
window.render=(kind='email',owner=0,shown=true)=>root.render(<React.StrictMode><MemoryRouter><QueryClientProvider client={cache}><PermissionAccessProvider permissions={permissions}><ConfirmationProvider><UnsavedChangesProvider><Location/>{shown&&(kind==='email'?<EmailTemplatePage client={clients[owner]}/>:<StorageGovernancePanel client={clients[owner]} enabled/>)}</UnsavedChangesProvider></ConfirmationProvider></PermissionAccessProvider></QueryClientProvider></MemoryRouter></React.StrictMode>);
window.render();
` }, outfile: bundle, bundle: true, platform: 'browser', format: 'iife', jsx: 'automatic', logLevel: 'silent' });
const context = await launch();
const page = await context.newPage(); page.setDefaultTimeout(10000);
const errors = []; page.on('pageerror', error => errors.push(error.message));
async function settle(method, index, value) {
  await page.evaluate(({method,index,value})=>window.calls.filter(call=>call.method===method)[index].resolve(value),{method,index,value});
}
try {
  await page.setContent('<!doctype html><html lang="zh-CN"><title>异步草稿回归</title><div id="root"></div></html>');
  await page.addScriptTag({path:bundle});
  await page.getByRole('tab',{name:'变量设置',exact:true}).click();
  await page.getByLabel(/^手工备注/).fill('保留手工草稿');
  await page.getByRole('button',{name:'生成预览',exact:true}).click();
  await page.getByLabel('CRM 客户',{exact:true}).selectOption('1');
  await page.getByRole('button',{name:'载入客户变量',exact:true}).click();
  await page.getByLabel('CRM 客户',{exact:true}).selectOption('2');
  await page.getByRole('button',{name:'载入客户变量',exact:true}).click();
  await page.waitForFunction(()=>window.calls.filter(call=>call.method==='customer').length===2);
  await settle('customer',1,{variables:{customer:'B'},toAddress:'b@example.invalid'});
  await page.getByText('建议收件人：b@example.invalid',{exact:true}).waitFor();
  await settle('customer',0,{variables:{customer:'A'},toAddress:'a@example.invalid'});
  assert.equal(await page.getByLabel('CRM 客户',{exact:true}).inputValue(),'2');
  assert.equal(await page.evaluate(()=>window.calls.find(call=>call.method==='customer').signal.aborted),true);
  await page.getByRole('button',{name:'调整预览变量',exact:true}).click();
  assert.equal(await page.getByLabel(/^客户名称/).inputValue(),'B');
  assert.equal(await page.getByLabel(/^手工备注/).inputValue(),'保留手工草稿');
  await page.evaluate(()=>window.deferPreview=true);
  await page.getByRole('button',{name:'生成预览',exact:true}).click();
  await page.waitForFunction(()=>window.calls.some(call=>call.method==='preview'));
  await page.getByLabel(/^手工备注/).fill('预览期间继续编辑');
  await settle('preview',0,{subject:'过期结果',bodyHtml:'过期正文',unresolvedTokens:[]});
  await page.waitForFunction(()=>window.calls.find(call=>call.method==='preview').signal.aborted);
  assert.equal(await page.getByRole('tab',{name:'变量设置',exact:true}).getAttribute('aria-selected'),'true');
  await page.evaluate(()=>window.deferPreview=false);
  await page.getByRole('button',{name:'生成预览',exact:true}).click();
  await page.evaluate(()=>window.deferSearch=true);
  await page.getByRole('button',{name:'查找客户',exact:true}).click();
  await page.waitForFunction(()=>window.calls.some(call=>call.method==='search'));
  await page.getByLabel('CRM 客户',{exact:true}).selectOption('1');
  await settle('search',0,{items:[{id:2,name:'过期客户结果'}]});
  assert.equal(await page.getByLabel('CRM 客户',{exact:true}).inputValue(),'1');
  assert.equal(await page.getByText(/建议收件人/).count(),0);
  await page.evaluate(()=>window.deferPreview=true);
  await page.getByRole('button',{name:'套用到单封邮件',exact:true}).click();
  await page.waitForFunction(()=>window.calls.filter(call=>call.method==='preview').length===2);
  await page.getByRole('button',{name:'调整预览变量',exact:true}).click();
  await settle('preview',1,{subject:'迟到邮件',bodyHtml:'不能跳转',unresolvedTokens:[]});
  assert.equal(await page.evaluate(()=>window.locationState.pathname),'/');

  await page.evaluate(()=>window.render('capacity'));
  await page.locator('summary').filter({hasText:/^日汇总与阈值$/}).click();
  const warning = page.getByLabel('预警占用（%）',{exact:true});
  await warning.fill('80'); await page.getByRole('button',{name:'保存告警策略',exact:true}).click();
  await page.waitForFunction(()=>window.calls.some(call=>call.method==='capacity'));
  await warning.fill('75');
  await page.evaluate(()=>{window.capacity={...window.capacity,revision:2,policy:{...window.capacity.policy,warningPercent:80}};window.calls.find(call=>call.method==='capacity').resolve({revision:window.capacity.revision,policy:structuredClone(window.capacity.policy)});});
  await page.getByRole('button',{name:'保存告警策略',exact:true}).waitFor();
  await page.waitForFunction(()=>!document.querySelector('button[disabled]') || [...document.querySelectorAll('button')].some(b=>b.textContent==='保存告警策略'&&!b.disabled));
  assert.equal(await warning.inputValue(),'75');
  await page.getByRole('button',{name:'保存告警策略',exact:true}).click();
  await page.waitForFunction(()=>window.calls.filter(call=>call.method==='capacity').length===2);
  assert.deepEqual(await page.evaluate(()=>{const body=window.calls.filter(call=>call.method==='capacity')[1].request.body;return [body.expectedVersion,body.policy.warningPercent];}),[2,75]);
  await page.evaluate(()=>{window.capacity={...window.capacity,revision:3,policy:{...window.capacity.policy,warningPercent:75}};window.calls.filter(call=>call.method==='capacity')[1].resolve({revision:window.capacity.revision,policy:structuredClone(window.capacity.policy)});});
  await page.locator('summary').filter({hasText:/^历史告警（1）$/}).click();
  await page.getByRole('table',{name:'容量历史告警'}).getByText('测试文件库空间不足，请扩容。',{exact:true}).waitFor();
  assert.deepEqual(errors,[]);
  console.log('Async drafts: customer response order, manual edits, stale search/preview/navigation, save reconciliation and capacity alert history passed.');
} finally { await context.close(); }
