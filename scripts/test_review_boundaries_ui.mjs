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
import {MemoryRouter} from 'react-router-dom'; import {QueryClient,QueryClientProvider} from '@tanstack/react-query';
import {InvoiceProfitAnalysisPanel} from ${src('features/invoices/InvoiceProfitAnalysisPanel.tsx')};
import {InvoiceStatusHistory} from ${src('features/invoices/InvoiceStatusHistory.tsx')};
import {BackupArchiveRestorePanel} from ${src('features/settings/BackupArchiveRestorePanel.tsx')};
import {EmailPage} from ${src('features/tools/EmailPage.tsx')};
import {createEmptyInvoice} from ${src('features/invoices/invoiceModel.ts')};
import {PermissionAccessProvider} from ${src('app/PermissionAccessContext.tsx')};
import {ConfirmationProvider} from ${src('ui/ConfirmationProvider.tsx')};
import {UnsavedChangesProvider} from ${src('ui/unsavedChangesGuard.tsx')};
import {queryKeys} from ${src('api/queryKeys.ts')};
window.deliveryKey=queryKeys.emailDeliveries();
window.calls=[]; window.jobs=[]; window.historyCalls=[];
window.cache=new QueryClient({defaultOptions:{queries:{retry:false},mutations:{retry:false}}});
const deferred=(kind,request,{signal})=>new Promise((resolve,reject)=>window.calls.push({kind,request,signal,resolve,reject}));
const client={analyzeInvoiceProfit:(r,i)=>deferred('profit',r,i),stageRecoveryArchiveRestore:(r,i)=>deferred('restore',r,i),sendEmail:(r,i)=>deferred('send',r,i),
getEmailToolStatus:async()=>({isConfigured:true,fromAddress:'review@example.invalid',fromDisplayName:'测试发件人',smtpHost:'example.invalid',smtpPort:465,enableSsl:true}),
listInvoiceStatusHistory:async request=>{window.historyCalls.push(request);return {items:[{id:request.beforeId?1:2,invoiceId:7,fromStatus:'Draft',toStatus:'Verified',note:request.beforeId?'早期原因':'最近原因',changedByUsername:'历史操作人',changedAt:'2026-11-15T00:00:00Z'}],nextCursor:request.beforeId?null:2};}};
const controller={activeJob:false,status:{archives:[{id:'one',createdAt:'2026-11-15T00:00:00Z',sizeBytes:100}]},trackJob:id=>window.jobs.push(id)};
function Harness({mode}){const [invoice,setInvoice]=useState(()=>createEmptyInvoice('2026-11-15'));
window.edit=()=>setInvoice(value=>({...value,remarks:(value.remarks||'')+'changed'}));
return mode==='profit'?<InvoiceProfitAnalysisPanel client={client} invoice={invoice} invoiceId={7} disabled={false}/>:
mode==='restore'?<BackupArchiveRestorePanel client={client} controller={controller} disabled={false} postgres={true} selectedId="one"/>:
mode==='history'?<InvoiceStatusHistory client={client} invoiceId={7}/>:
mode==='email'?<EmailPage client={client} businessTimeZone="Asia/Shanghai"/>:<div>closed</div>;}
const app=createRoot(document.getElementById('root'));
window.render=(mode,delivery=false)=>app.render(<MemoryRouter><QueryClientProvider client={window.cache}><PermissionAccessProvider permissions={['send',...(delivery?['view-delivery']:[])].map(action=>({resourceKey:'common.email-delivery',action,dataScope:'all'}))}><ConfirmationProvider><UnsavedChangesProvider><Harness key={mode} mode={mode}/></UnsavedChangesProvider></ConfirmationProvider></PermissionAccessProvider></QueryClientProvider></MemoryRouter>);
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
  } finally { await page.close(); }
}
try {
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
