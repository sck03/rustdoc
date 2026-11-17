import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { locateChromeForTesting } from './lib/chromium-executable.mjs';

const root = path.resolve(import.meta.dirname, '..'), web = path.join(root, 'apps/export-doc-web');
const require = createRequire(path.join(web, 'package.json'));
const output = path.join(root, 'artifacts/review-boundaries-ui', String(Date.now()));
fs.mkdirSync(path.join(output, 'Temp'), { recursive: true });
Object.assign(process.env, { TEMP: path.join(output, 'Temp'), TMP: path.join(output, 'Temp') });
const src = name => JSON.stringify(path.join(web, 'src', name).replaceAll('\\', '/'));
const bundle = path.join(output, 'app.js');
await require('esbuild').build({ stdin: { resolveDir: web, loader: 'tsx', contents: `
import React,{useState} from 'react'; import {createRoot} from 'react-dom/client';
import {MemoryRouter,useNavigate} from 'react-router-dom'; import {QueryClient,QueryClientProvider} from '@tanstack/react-query';
import {InvoiceProfitAnalysisPanel} from ${src('features/invoices/InvoiceProfitAnalysisPanel.tsx')};
import {InvoiceStatusHistory} from ${src('features/invoices/InvoiceStatusHistory.tsx')};
import {BackupArchiveRestorePanel} from ${src('features/settings/BackupArchiveRestorePanel.tsx')};
import {EmailPage} from ${src('features/tools/EmailPage.tsx')};
import {createEmptyInvoice} from ${src('features/invoices/invoiceModel.ts')};
import {PermissionAccessProvider} from ${src('app/PermissionAccessContext.tsx')};
import {ConfirmationProvider} from ${src('ui/ConfirmationProvider.tsx')};
import {UnsavedChangesProvider} from ${src('ui/unsavedChangesGuard.tsx')};
import {queryKeys} from ${src('api/queryKeys.ts')};
import {useUserManagement} from ${src('features/settings/useUserManagement.ts')};
import {useInvoiceDocumentPackageWorkspace} from ${src('features/invoices/useInvoiceDocumentPackageWorkspace.ts')};
import {InvoiceDataMaintenancePanel} from ${src('features/settings/MaintenanceInvoiceDataPanel.tsx')};
import {SharedDatabaseOwnershipPanel} from ${src('features/settings/MaintenanceOwnershipPanel.tsx')};
window.deliveryKey=queryKeys.emailDeliveries();
window.calls=[]; window.jobs=[]; window.historyCalls=[];
window.cache=new QueryClient({defaultOptions:{queries:{retry:false},mutations:{retry:false}}});
const deferred=(kind,request,{signal})=>new Promise((resolve,reject)=>window.calls.push({kind,request,signal,resolve,reject}));
window.userKey=queryKeys.users();
window.userData={users:[1,2].map(id=>({id,username:'admin'+id,fullName:'账号'+id,role:'Admin',versionNumber:1,isActive:true,disabledModules:[],companyScope:'DEFAULT',departmentId:'GENERAL'})),roles:['Admin'],permissionTemplates:[],companies:[],departments:[]};
window.cache.setQueryDefaults(window.userKey,{staleTime:Infinity});
window.cache.setQueryData(window.userKey,window.userData);
const accountClient={listUsers:i=>deferred('users',{},i),updateUserAccount:(r,i)=>deferred('save-user',r,i),createUserAccount:(r,i)=>deferred('create-user',r,i),deleteUserAccount:(r,i)=>deferred('delete-user',r,i)};
const nextClient={...accountClient};
const maintenanceClient={getInvoiceDataMaintenancePreview:(r,i)=>deferred('lookup',r,i),purgeCancelledInvoice:(r,i)=>deferred('purge',r,i),
getSharedDatabaseOwnershipSummary:async()=>({owners:[{userId:1,username:'管理员',isActive:true},{userId:2,username:'接手人',isActive:true}]}),
transferSharedDatabaseOwnership:(r,i)=>deferred('transfer',r,i)};
const otherMaintenanceClient={...maintenanceClient};
const documentClient={...accountClient,startInvoiceDocumentEmailJob:(r,i)=>deferred('document-email',r,i)};
const emailTemplates=[{templatePath:'builtin:invoice_template.dtpl',displayName:'发票',withSealDefault:true}];
function Accounts({session}){window.navigate=useNavigate();window.user=useUserManagement(session?nextClient:accountClient,true);return <div>{window.user.message}<input aria-label="Account" value={window.user.draft.username} onChange={e=>window.user.patchDraft('username',e.target.value)}/></div>;}
function DocumentEmail(){window.package=useInvoiceDocumentPackageWorkspace({client:documentClient,invoiceId:7,previewAllowed:true,exportAllowed:true,emailAllowed:true,templates:emailTemplates,desktopAvailable:false,defaultExportDirectory:'',feedback:{clear(){},showError(){},showJob(){},showStatus(){}},onPreviewGenerated(){}});return <div>document email</div>;}
const client={analyzeInvoiceProfit:(r,i)=>deferred('profit',r,i),stageRecoveryArchiveRestore:(r,i)=>deferred('restore',r,i),sendEmail:(r,i)=>deferred('send',r,i),
getEmailToolStatus:async()=>({isConfigured:true,fromAddress:'review@example.invalid',fromDisplayName:'测试发件人',smtpHost:'example.invalid',smtpPort:465,enableSsl:true}),
listInvoiceStatusHistory:async request=>{window.historyCalls.push(request);return {items:[{id:request.beforeId?1:2,invoiceId:7,fromStatus:'Draft',toStatus:'Verified',note:request.beforeId?'早期原因':'最近原因',changedByUsername:'历史操作人',changedAt:'2026-11-15T00:00:00Z'}],nextCursor:request.beforeId?null:2};}};
const controller={activeJob:false,status:{archives:[{id:'one',createdAt:'2026-11-15T00:00:00Z',sizeBytes:100}]},trackJob:id=>window.jobs.push(id)};
function Harness({mode,session}){const [invoice,setInvoice]=useState(()=>createEmptyInvoice('2026-11-15'));
window.edit=()=>setInvoice(value=>({...value,remarks:(value.remarks||'')+'changed'}));
return mode==='purge'?<InvoiceDataMaintenancePanel client={session?otherMaintenanceClient:maintenanceClient} canManageSettings={true}/>:
mode==='ownership'?<SharedDatabaseOwnershipPanel client={session?otherMaintenanceClient:maintenanceClient} canManageUsers={true}/>:
mode==='users'?<Accounts session={session}/>:mode==='document-email'?<DocumentEmail/>:mode==='profit'?<InvoiceProfitAnalysisPanel client={client} invoice={invoice} invoiceId={7} disabled={false}/>:
mode==='restore'?<BackupArchiveRestorePanel client={client} controller={controller} disabled={false} postgres={true} selectedId="one"/>:
mode==='history'?<InvoiceStatusHistory client={client} invoiceId={7}/>:
mode==='email'?<EmailPage client={client} businessTimeZone="Asia/Shanghai"/>:<div>closed</div>;}
const app=createRoot(document.getElementById('root'));
window.render=(mode,delivery=false,session=0)=>app.render(<MemoryRouter><QueryClientProvider client={window.cache}><PermissionAccessProvider permissions={['send',...(delivery?['view-delivery']:[])].map(action=>({resourceKey:'common.email-delivery',action,dataScope:'all'}))}><ConfirmationProvider><UnsavedChangesProvider><Harness key={mode} mode={mode} session={session}/></UnsavedChangesProvider></ConfirmationProvider></PermissionAccessProvider></QueryClientProvider></MemoryRouter>);
` }, outfile: bundle, bundle: true, platform: 'browser', format: 'iife', jsx: 'automatic', logLevel: 'silent' });
const context = await require('playwright').chromium.launchPersistentContext(path.join(output, 'Chrome'), { executablePath: locateChromeForTesting(root), headless: true });
let passed = 0;
async function scenario(mode, check) {
  const page = await context.newPage(), errors = [];
  page.setDefaultTimeout(10000); page.on('pageerror', error => errors.push(error.message));
  try {
    await page.setContent('<!doctype html><html lang="zh-CN"><title>操作边界回归</title><div id="root"></div></html>');
    await page.addScriptTag({ path: bundle }); await page.evaluate(mode => window.render(mode), mode);
    await check(page); assert.deepEqual(errors, []); passed++;
  } catch(error) { await page.screenshot({path:path.join(output,mode+'-failure.png'),fullPage:true}); throw new Error(mode+': '+error.message+'; page errors: '+errors.join('; '),{cause:error});
  } finally { await page.close(); }
}
try {
  await scenario('purge', async page => {
    await page.getByLabel('发票 ID',{exact:true}).fill('1');
    await page.getByRole('button',{name:'查询发票',exact:true}).click();
    await page.waitForFunction(()=>window.calls.length===1);
    await page.getByLabel('发票 ID',{exact:true}).fill('2');
    await page.waitForFunction(()=>window.calls[0].signal.aborted);
    await page.evaluate(()=>window.calls[0].resolve({id:1,invoiceNo:'STALE-INVOICE',canPurge:true}));
    assert.doesNotMatch(await page.locator('body').innerText(),/STALE-INVOICE/);
    await page.getByRole('button',{name:'查询发票',exact:true}).click();
    await page.waitForFunction(()=>window.calls.length===2);
    await page.evaluate(()=>window.calls[1].resolve({id:2,invoiceNo:'CURRENT-INVOICE',canPurge:true}));
    await page.getByText('CURRENT-INVOICE',{exact:true}).waitFor();
    assert.equal(await page.evaluate(()=>window.cache.getMutationCache().getAll().length),0);
  });
  for (const mode of ['purge','ownership']) for (const change of ['unmount','session']) for (const submitted of [false,true]) {
    await scenario(mode, async page => {
      if (mode==='purge') {
        await page.getByLabel('发票 ID',{exact:true}).fill('1');
        await page.getByRole('button',{name:'查询发票',exact:true}).click();
        await page.waitForFunction(()=>window.calls.length===1);
        await page.evaluate(()=>window.calls.shift().resolve({id:1,invoiceNo:'INV-ONE',canPurge:true}));
        await page.getByLabel('再次输入完整发票号',{exact:false}).fill('INV-ONE');
        await page.getByLabel('清理原因',{exact:false}).fill('isolated test');
      }
      await page.getByRole('button',{name:mode==='purge'?'永久清理已作废发票':'执行改派',exact:true}).click();
      const confirm=page.getByRole('button',{name:mode==='purge'?'确认永久清理':'确认改派',exact:true});
      await confirm.waitFor();
      if (submitted) { await confirm.click(); await page.waitForFunction(()=>window.calls.length===1); }
      await page.evaluate(({mode,change})=>window.render(change==='unmount'?'closed':mode,false,1),{mode,change});
      if (change==='unmount') await page.getByText('closed',{exact:true}).waitFor();
      else await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
      if (submitted) {
        await page.waitForFunction(()=>window.calls[0].signal.aborted);
        await page.evaluate(()=>window.calls[0].resolve({invoiceId:1,message:'LATE-MAINTENANCE'}));
        assert.doesNotMatch(await page.locator('body').innerText(),/LATE-MAINTENANCE/);
      } else { await confirm.click(); assert.equal(await page.evaluate(()=>window.calls.length),0); }
      assert.equal(await page.evaluate(()=>window.cache.getMutationCache().getAll().length),0);
    });
  }
  await scenario('users', async page => {
    await page.waitForFunction(() => window.user.selectedUserId === 1);
    await page.evaluate(() => window.navigate('/?userId=2'));
    await page.waitForFunction(() => window.user.selectedUserId === 2);
    await page.evaluate(() => window.user.beginNew());
    await page.waitForFunction(() => window.user.draft.id === 0);
    await page.evaluate(() => window.navigate('/?userId=1'));
    await page.waitForFunction(() => window.user.selectedUserId === 1);
    await page.evaluate(() => window.user.patchDraft('resetPassword','ACCOUNT-SECRET-2026'));
    await page.waitForFunction(() => window.user.draft.resetPassword !== '');
    await page.evaluate(() => { window.user.saveUser(); window.user.saveUser(); });
    await page.waitForFunction(() => window.calls.length === 1);
    assert.equal(await page.evaluate(() => window.cache.getMutationCache().getAll().length), 0);
    await page.evaluate(() => window.calls[0].resolve({user:{...window.userData.users[0],versionNumber:2},message:'saved'}));
    await page.waitForFunction(() => window.user.draft.resetPassword === '');
    assert.equal(await page.evaluate(() => window.user.selectedUserId), 1);
  });
  await scenario('document-email', async page => {
    await page.waitForFunction(() => Boolean(window.package));
    await page.evaluate(() => window.package.sendEmail());
    await page.waitForFunction(() => window.calls.length === 1);
    const key = await page.evaluate(() => window.calls[0].request['Idempotency-Key']);
    await page.evaluate(() => window.calls[0].reject(new TypeError('network reset')));
    await page.waitForFunction(() => !window.package.isPending);
    await page.evaluate(() => window.package.sendEmail());
    await page.waitForFunction(() => window.calls.length === 2);
    assert.equal(await page.evaluate(() => window.calls[1].request['Idempotency-Key']),key);
    await page.evaluate(() => window.calls[1].resolve({jobId:'accepted'}));
    await page.waitForFunction(() => !window.package.isPending);
    await page.evaluate(() => window.package.sendEmail());
    await page.waitForFunction(() => window.calls.length === 3);
    assert.notEqual(await page.evaluate(() => window.calls[2].request['Idempotency-Key']),key);
    await page.evaluate(() => window.package.changeEmailBody('new draft'));
    await page.waitForFunction(() => window.calls[2].signal.aborted);
    assert.equal(await page.evaluate(() => window.cache.getMutationCache().getAll().length),0);
  });
  for (const action of ['save','refresh']) await scenario('users', async page => {
    await page.waitForFunction(() => window.user.selectedUserId === 1);
    await page.evaluate(action => action==='save'?window.user.saveUser():window.user.refreshUsers(),action);
    await page.waitForFunction(() => window.calls.length === 1);
    await page.evaluate(() => { window.render('closed'); window.cache.clear(); });
    await page.waitForFunction(() => window.calls[0].signal.aborted);
    await page.evaluate(action => window.calls[0].resolve(action==='save'?{user:window.userData.users[0]}:window.userData), action);
    assert.equal(await page.evaluate(() => window.cache.getQueryData(window.userKey)), undefined);
  });
  for (const submitted of [false,true]) await scenario('users', async page => {
    await page.waitForFunction(() => window.user.selectedUserId === 1);
    await page.evaluate(() => window.user.deleteSelectedUser());
    const confirm = page.getByRole('button',{name:'删除账号',exact:true});
    await confirm.waitFor();
    if (submitted) { await confirm.click(); await page.waitForFunction(() => window.calls.length === 1); }
    await page.evaluate(() => { window.render('closed'); window.cache.clear(); });
    await page.getByText('closed',{exact:true}).waitFor();
    if (submitted) {
      await page.waitForFunction(() => window.calls[0].signal.aborted);
      await page.evaluate(() => window.calls[0].resolve({message:'late delete'}));
    } else { await confirm.click(); assert.equal(await page.evaluate(() => window.calls.length),0); }
    assert.equal(await page.evaluate(() => window.cache.getQueryData(window.userKey)),undefined);
  });
  await scenario('users', async page => {
    await page.waitForFunction(() => window.user.selectedUserId === 1);
    await page.evaluate(() => window.user.saveUser());
    await page.waitForFunction(() => window.calls.length === 1);
    await page.evaluate(() => window.render('users',false,1));
    await page.waitForFunction(() => window.calls[0].signal.aborted);
    await page.evaluate(() => window.calls[0].resolve({user:{...window.userData.users[0],username:'STALE-SESSION',versionNumber:2}}));
    assert.equal(await page.evaluate(() => window.cache.getQueryData(window.userKey).users[0].username), 'admin1');
  });
  await scenario('profit', async page => {
    await page.getByRole('button', { name: '展开利润分析' }).click();
    await page.getByRole('button', { name: '计算', exact: true }).click();
    await page.waitForFunction(() => window.calls.length === 1);
    await page.evaluate(() => window.edit());
    await page.waitForFunction(() => window.calls[0].signal.aborted);
    await page.evaluate(() => window.calls[0].resolve({grossProfitText:'OLD-RESULT'}));
    assert.doesNotMatch(await page.locator('body').innerText(), /OLD-RESULT/);
    await page.getByRole('button', { name: '计算', exact: true }).click();
    await page.waitForFunction(() => window.calls.length === 2);
    await page.evaluate(() => window.calls[1].resolve({grossProfitText:'CURRENT-RESULT'}));
    await page.getByText('CURRENT-RESULT').waitFor();
    await page.evaluate(() => window.edit());
    await page.getByText('CURRENT-RESULT').waitFor({state:'detached'});
    assert.equal(await page.evaluate(() => window.cache.getMutationCache().getAll().length), 0);
  });
  await scenario('restore', async page => {
    await page.getByLabel('所选备份的密码').fill('SYNTHETIC-BACKUP');
    await page.getByLabel('管理员当前密码').fill('SYNTHETIC-ADMIN');
    await page.getByLabel('恢复确认文本').fill('MIGRATE');
    await page.getByRole('button', { name: '校验并安排完整恢复' }).click();
    await page.waitForFunction(() => window.calls.length === 1);
    await page.evaluate(() => window.calls[0].resolve({jobId:'review-job'}));
    await page.waitForFunction(() => window.jobs.length === 1);
    await page.waitForFunction(() => [...document.querySelectorAll('input[type=password]')].every(input => input.value === ''));
    assert.equal(await page.getByLabel('所选备份的密码').inputValue(), '');
    assert.equal(await page.getByLabel('管理员当前密码').inputValue(), '');
    assert.equal(await page.evaluate(() => window.cache.getMutationCache().getAll().length), 0);
  });
  await scenario('email', async page => {
    await page.getByLabel('收件人', { exact: false }).fill('review@example.invalid');
    const send = page.getByRole('button', { name: '发送邮件', exact: true });
    await send.click();
    await page.waitForFunction(() => window.calls.length === 1);
    await page.evaluate(() => window.render('closed'));
    await page.waitForFunction(() => window.calls[0].signal.aborted);
    await page.evaluate(() => window.calls[0].resolve({success:true,message:'LATE-SEND'}));
    assert.equal(await page.evaluate(() => window.cache.getMutationCache().getAll().length), 0);
    assert.doesNotMatch(await page.locator('body').innerText(), /LATE-SEND/);
  });
  await scenario('history', async page => {
    assert.equal(await page.evaluate(() => window.historyCalls.length), 0);
    await page.getByText('状态记录', { exact: true }).click();
    await page.getByText('最近原因').waitFor();
    await page.getByRole('button', { name: '下一页' }).click();
    await page.getByText('早期原因').waitFor();
    assert.doesNotMatch(await page.locator('body').innerText(), /最近原因/);
    assert.equal(await page.evaluate(() => window.historyCalls.at(-1).beforeId), 2);
    await page.getByRole('button', { name: '上一页' }).click();
    await page.getByText('最近原因').waitFor();
  });
  await scenario('email', async page => {
    await page.evaluate(() => { window.render('email',true); window.cache.setQueryData(window.deliveryKey,[]); });
    await page.getByLabel('收件人', { exact: false }).fill('review@example.invalid');
    await page.getByRole('button', { name: '发送邮件', exact: true }).click();
    await page.waitForFunction(() => window.calls.length === 1);
    await page.evaluate(() => window.calls[0].reject(new Error('发送结果未知')));
    await page.getByText('发送结果未知', { exact: true }).waitFor();
    assert.equal(await page.evaluate(() => window.cache.getQueryState(window.deliveryKey).isInvalidated), true);
  });
  console.log('Review boundaries UI: '+passed+' passed; '+output);
} finally { await context.close(); }
