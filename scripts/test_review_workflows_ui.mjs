import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { withOfficeUi } from './lib/native-office-ui.mjs';
import { verifyBusinessWorkspaceLayouts } from './lib/workspace-layout-review.mjs';

await withOfficeUi('review-workflows-ui',async({repo,output,require,url,invoke,openPage})=>{
  const login=await invoke('Login',{username:'oa-review',password:'Review-2026-Test'});
  const token=login.accessToken;
  const model=path.join(output,'invoice-model.mjs');
  await require('esbuild').build({stdin:{resolveDir:path.join(repo,'apps/export-doc-web'),contents:"export { createEmptyInvoice } from './src/features/invoices/invoiceModel.ts'; export { createEmptyInvoiceItem } from './src/features/invoices/invoiceItemsEditorModel.ts';"},bundle:true,platform:'node',format:'esm',outfile:model,logLevel:'silent'});
  const {createEmptyInvoice,createEmptyInvoiceItem}=await import(pathToFileURL(model).href);
  for(const [field,value] of [['quantity',0.1],['unitPrice','1e3'],['unitPrice','1_000'],
    ['unitPrice','1.00000000000000000000000000001'],['unitPrice','79228162514264337593543950336'],['exchangeRate','']]) {
    const body={...createEmptyInvoice(login.user.businessDate),invoiceNo:'WIRE-REJECT',
      items:[{...createEmptyInvoiceItem(),styleName:'Precision input',quantity:'1',unitPrice:'1'}]};
    (field==='exchangeRate'?body:body.items[0])[field]=value;
    await assert.rejects(invoke('CreateInvoice',body,{},token), /CreateInvoice \(400\)/u);
  }
  assert.equal((await invoke('ListInvoices',undefined,{keyword:'WIRE-REJECT'},token)).totalCount,0,'Invalid decimals must not persist an invoice');
  for(const currency of ['USD','CNY']) {
    const invoice={...createEmptyInvoice(login.user.businessDate),invoiceNo:'CURRENCY-'+currency,currency,exchangeRate:null,
      items:[{...createEmptyInvoiceItem(),styleName:'Sample product',quantity:"10",unitPrice:"10",totalPrice:"100"}]};
    await invoke('CreateInvoice',invoice,{},token);
  }
  const page=await openPage('oa-review','Review-2026-Test');
  await page.goto(url+'/#/dashboard');
  for(const currency of ['USD','CNY']) {
    const metric=page.getByRole('button',{name:`本月出口额（${currency}）：100.00`,exact:true});
    await metric.waitFor();assert.equal(await metric.count(),1);
    await page.locator('.dashboard-recent-section td.amount-cell').filter({hasText:new RegExp('^'+currency+' 100\\.00$','u')}).waitFor();
  }
  await page.screenshot({path:path.join(output,'dashboard-currencies.png'),fullPage:true});
  await page.setViewportSize({width:390,height:844});
  await page.screenshot({path:path.join(output,'dashboard-currencies-mobile.png'),fullPage:true});
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+1),false);
  await page.setViewportSize({width:1440,height:1000});
  await verifyBusinessWorkspaceLayouts({ page, invoke, token, url, output, businessDate: login.user.businessDate, require });
  const statusDraft={...createEmptyInvoice(login.user.businessDate),invoiceNo:'STATUS-REVIEW',exporterNameEN:'Sample exporter',customerNameEN:'Sample customer',currency:'USD',destinationCountry:'NETHERLANDS',portOfLoading:'NINGBO',portOfDestination:'ROTTERDAM',
    items:[{...createEmptyInvoiceItem(),styleNo:'SAMPLE',styleName:'Sample product',hsCode:'6109100010',quantity:"10",unitPrice:"10",totalPrice:"100"}]};
  const created=await invoke('CreateInvoice',statusDraft,{},token), id=created.id;
  let statusInvoice=created.invoice;
  for(const [operation,targetStatus,note] of [['TransitionInvoiceStatus','Verified','核对通过'],['UnverifyInvoice','Draft','修正商品资料']]) {
    statusInvoice=(await invoke(operation,{rowVersion:statusInvoice.rowVersion,...(operation==='TransitionInvoiceStatus'?{targetStatus}:{}),note},{id},token)).invoice;
  }
  const recent=await invoke('ListInvoiceStatusHistory',undefined,{id,limit:1},token);
  assert.equal(recent.items[0].note,'修正商品资料');assert(recent.nextCursor);
  const previous=await invoke('ListInvoiceStatusHistory',undefined,{id,limit:1,beforeId:recent.nextCursor},token);
  assert.equal(previous.items[0].note,'核对通过');assert.equal(previous.nextCursor,null);
  const historyRequests=[];
  const historyRoute=`/api/invoices/${id}/status-history`;
  page.on('request',request=>{if(request.url().includes(historyRoute))historyRequests.push(request.url());});
  await page.goto(`${url}/#/invoices/${id}?section=basic`);
  await page.getByRole('button',{name:'作废',exact:true}).waitFor();
  assert.equal(historyRequests.length,0,'collapsed history does not query');
  await page.getByText('状态记录',{exact:true}).click();
  await page.getByText('修正商品资料',{exact:true}).waitFor();
  await page.getByRole('button',{name:'作废',exact:true}).click();
  await page.getByRole('textbox',{name:'作废原因',exact:true}).fill('客户取消订单，保留原因');
  await page.getByRole('button',{name:'继续作废',exact:true}).click();
  await page.getByRole('button',{name:'确认已作废',exact:true}).click();
  await page.locator('.invoice-status-history-list').getByText('客户取消订单，保留原因',{exact:true}).waitFor();
  const history=await invoke('ListInvoiceStatusHistory',undefined,{id},token);
  assert.equal(history.items.length,3);assert.equal(history.items[0].changedByUsername,'oa-review');
  assert(!Number.isNaN(Date.parse(history.items[0].changedAt)));
  assert(historyRequests.length>=2,'status transition refreshes visible history');
  await page.locator('.invoice-status-history-list').screenshot({path:path.join(output,'invoice-status-history.png')});
  await page.setViewportSize({width:390,height:844});
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+1),false);
  await page.setViewportSize({width:1440,height:1000});
  await page.goto(url+'/#/crm/email-templates');
  await page.getByRole('button',{name:'新建模板',exact:true}).click();
  await page.getByLabel('模板名称',{exact:true}).fill('完整邮件流程验证');
  await page.getByLabel('邮件主题',{exact:true}).fill('Initial subject');
  await page.getByRole('button',{name:'保存草稿',exact:true}).click();
  await page.getByText('邮件模板草稿已建立。',{exact:true}).waitFor();
  await page.getByLabel('邮件主题',{exact:true}).fill('Updated subject');
  await page.getByRole('button',{name:'保存草稿',exact:true}).click();
  await page.getByText('邮件模板草稿已保存。',{exact:true}).waitFor();
  await page.getByRole('tab',{name:'版本历史',exact:true}).click();
  await page.getByRole('row').filter({has:page.getByText('V1',{exact:true})}).getByRole('button',{name:'查看',exact:true}).click();
  await page.getByLabel('历史主题',{exact:true}).and(page.locator('input[value="Initial subject"]')).waitFor();
  await page.getByRole('button',{name:'恢复此版本',exact:true}).click();
  await page.getByRole('button',{name:'确认恢复',exact:true}).click();
  await page.getByText('已从 V1 恢复，并生成 V3。',{exact:true}).waitFor();
  await page.screenshot({path:path.join(output,'template-history-restored.png'),fullPage:true});
  await page.evaluate(require('axe-core').source);
  assert.deepEqual(await page.evaluate(async()=>(await window.axe.run(document.querySelector('[data-ui=workspace]'))).violations.filter(v=>['serious','critical'].includes(v.impact)).map(v=>v.id)),[]);
  const metrics=await invoke('GetRuntimeMetrics',undefined,{},token);
  assert.equal(metrics.http.downloadCapacity,8);assert('downloads' in metrics.http);
  fs.writeFileSync(path.join(output,'result.json'),JSON.stringify({currencyGroups:['CNY','USD'],templateRestoredVersion:3,httpMetrics:metrics.http},null,2));
});
