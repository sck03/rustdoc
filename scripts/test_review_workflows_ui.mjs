import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { withOfficeUi } from './lib/native-office-ui.mjs';

await withOfficeUi('review-workflows-ui',async({repo,output,require,url,invoke,openPage})=>{
  const login=await invoke('Login',{username:'oa-review',password:'Review-2026-Test'});
  const token=login.accessToken;
  const model=path.join(output,'invoice-model.mjs');
  await require('esbuild').build({stdin:{resolveDir:path.join(repo,'apps/export-doc-web'),contents:"export { createEmptyInvoice } from './src/features/invoices/invoiceModel.ts'; export { createEmptyInvoiceItem } from './src/features/invoices/invoiceItemsEditorModel.ts';"},bundle:true,platform:'node',format:'esm',outfile:model,logLevel:'silent'});
  const {createEmptyInvoice,createEmptyInvoiceItem}=await import(pathToFileURL(model).href);
  for(const currency of ['USD','CNY']) {
    const invoice={...createEmptyInvoice(login.user.businessDate),invoiceNo:'CURRENCY-'+currency,currency,exchangeRate:null,
      items:[{...createEmptyInvoiceItem(),styleName:'Sample product',quantity:10,unitPrice:10,totalPrice:100}]};
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
  assert.equal(await page.getByLabel('历史主题',{exact:true}).inputValue(),'Initial subject');
  await page.getByRole('button',{name:'恢复此版本',exact:true}).click();
  await page.getByRole('button',{name:'确认恢复',exact:true}).click();
  await page.getByText('已从 V1 恢复，并生成 V3。',{exact:true}).waitFor();
  await page.screenshot({path:path.join(output,'template-history-restored.png'),fullPage:true});
  await page.addScriptTag({path:require.resolve('axe-core/axe.min.js')});
  assert.deepEqual(await page.evaluate(async()=>(await window.axe.run(document.querySelector('.work-surface'))).violations.filter(v=>['serious','critical'].includes(v.impact)).map(v=>v.id)),[]);
  const metrics=await invoke('GetRuntimeMetrics',undefined,{},token);
  assert.equal(metrics.http.downloadCapacity,8);assert('downloads' in metrics.http);
  fs.writeFileSync(path.join(output,'result.json'),JSON.stringify({currencyGroups:['CNY','USD'],templateRestoredVersion:3,httpMetrics:metrics.http},null,2));
});
