import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { withOfficeUi } from './lib/native-office-ui.mjs';
await withOfficeUi('company-scope-ui',async({repo,output,url,invoke,openPage,require})=>{
 const admin=(await invoke('Login',{username:'oa-review',password:'Review-2026-Test'})).accessToken;
 const extra=await invoke('CreateOrganizationCompany',{code:'SCOPE-B',name:'Scope B',isActive:true},{},admin);
 await invoke('CreateOrganizationDepartment',{code:'GENERAL',name:'General',companyCode:'SCOPE-B',isActive:true},{},admin);
 const main=(await invoke('GetOrganizationDirectory',undefined,{},admin)).companies.find(row=>row.code==='DEFAULT');
 assert(main);
 const bundle=path.join(output,'models.mjs');
 await require('esbuild').build({stdin:{resolveDir:path.join(repo,'apps/export-doc-web'),contents:"export {createEmptyInvoice} from './src/features/invoices/invoiceModel.ts'; export {createEmptyPayment} from './src/features/payments/paymentModel.ts'; export {isRecordWithinRestrictions} from './src/app/permissionScope.ts';"},bundle:true,platform:'node',format:'esm',outfile:bundle,logLevel:'silent'});
 const model=await import(pathToFileURL(bundle).href);
 const subject={id:7,companyScope:'A',departmentId:'D'};
 const row={ownerUserId:7,companyScope:'A',departmentId:'D',projectId:'12'};
 const rule={resourceKey:'r',action:'operate',mode:'Units',unitCodes:['A','B'],departments:[],projectIds:[]};
 const allowed=rules=>model.isRecordWithinRestrictions(rules,'r','operate',subject,row);
 assert(allowed([rule]));
 assert(!allowed([rule,{...rule,unitCodes:['B']}]),'Multiple restrictions intersect');
 assert(allowed([{...rule,mode:'Departments',departments:[{companyScope:'A',departmentId:'D'}]}]));
 assert(!allowed([{...rule,mode:'Departments',departments:[{companyScope:'B',departmentId:'D'}]}]),'Department codes are unit-qualified');
 assert(allowed([{...rule,mode:'Projects',projectIds:['12']}]));
 assert(!allowed([{...rule,mode:'Projects',projectIds:['13']}]));
 assert(allowed([{...rule,mode:'Participation'}]));
 assert(!allowed([{...rule,mode:'Deny'}]));
 const resources=['document.invoices','document.payments'];
 const grants=scope=>resources.flatMap(resourceKey=>['view','operate'].map(action=>({resourceKey,action,dataScope:scope})));
 const account=async(username,companyScope,scope)=> (await invoke('createUserAccount',{username,fullName:username,role:'User',isActive:true,companyScope,departmentId:'GENERAL',resetPassword:'Scope-Accept-2026',permissionGrants:grants(scope),permissionTemplateId:null,disabledModules:[]},{},admin)).user;
 const login=async username=>invoke('Login',{username,password:'Scope-Accept-2026'});
 await account('scope-writer-a','DEFAULT','company'); await account('scope-writer-b','SCOPE-B','company');
 const reader=await account('scope-finance','DEFAULT','all');
 const records=[];
 for(const [username,suffix] of [['scope-writer-a','A'],['scope-writer-b','B']]){
  const auth=await login(username), token=auth.accessToken;
  const invoice=(await invoke('CreateInvoice',{...model.createEmptyInvoice(auth.user.businessDate),invoiceNo:'SCOPE-INVOICE-'+suffix},{},token)).invoice;
  const payment=(await invoke('CreatePayment',{...model.createEmptyPayment(auth.user.businessDate),voucherNo:'SCOPE-PAYMENT-'+suffix,payeeName:'Scope '+suffix},{},token)).payment;
  records.push({invoice,payment});
 }
 let token=(await login('scope-finance')).accessToken;
 for(const resourceKey of resources){
  const isInvoice=resourceKey.endsWith('invoices'), get=isInvoice?'GetInvoice':'GetPayment', list=isInvoice?'ListInvoices':'ListPayments', key=isInvoice?'invoice':'payment';
  for(const row of records) assert.equal((await invoke(get,undefined,{id:row[key].id},token)).companyScope,row[key].companyScope);
  assert.equal((await invoke(list,undefined,{keyword:isInvoice?'SCOPE-INVOICE':'SCOPE-PAYMENT'},token)).totalCount,2);
 }
 // Configure separate view/edit scopes through the actual account UI.
 const adminPage=await openPage('oa-review','Review-2026-Test');
 await adminPage.goto(`${url}/#/system/access-control`);
 const accountForm=adminPage.locator('.user-management-section');
 await accountForm.getByRole('cell',{name:'scope-finance',exact:true}).first().click();
 const panel=accountForm.locator('details').filter({has:adminPage.locator('summary',{hasText:'明确的数据范围与限制'})});
 await panel.locator('summary').click();
 for(const resourceKey of resources){
  await panel.getByLabel('权限资源',{exact:true}).selectOption(resourceKey);
  for(const [action,units] of [['view',[main.name,extra.name]],['operate',[main.name]]]){
   await panel.getByLabel('权限动作',{exact:true}).selectOption(action);
   await panel.getByLabel('附加范围或限制',{exact:true}).selectOption('Units');
   for(const name of units) await panel.getByRole('checkbox',{name,exact:true}).check();
   await panel.getByLabel('限制或调整原因',{exact:true}).fill('Scope acceptance '+action);
   const response=adminPage.waitForResponse(r=>r.url().endsWith('/api/organization/access-restrictions')&&r.request().method()==='POST');
   await panel.getByRole('button',{name:'保存明确限制',exact:true}).click();
   assert.equal((await response).status(),200);
   // Switch away and back: stored members and mode must hydrate, not reset to Deny.
   await panel.getByLabel('权限动作',{exact:true}).selectOption(action==='view'?'operate':'view');
   await panel.getByLabel('权限动作',{exact:true}).selectOption(action);
   await panel.getByLabel('附加范围或限制',{exact:true}).waitFor();
   assert.equal(await panel.getByLabel('附加范围或限制',{exact:true}).inputValue(),'Units');
   for(const name of [main.name,extra.name]) assert.equal(await panel.getByRole('checkbox',{name,exact:true}).isChecked(),units.includes(name));
  }
 }
 await adminPage.screenshot({path:path.join(output,'scope-editor.png'),fullPage:true});
 await adminPage.evaluate(require('axe-core').source);
 const scan=await adminPage.evaluate(async()=>window.axe.run(document.querySelector('.user-management-section'),{runOnly:{type:'tag',values:['wcag2a','wcag2aa']}}));
 assert.deepEqual(scan.violations,[],'Scope editor accessibility');
 await adminPage.setViewportSize({width:390,height:844});
 assert(await adminPage.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'Scope editor respects mobile width');
 await adminPage.screenshot({path:path.join(output,'scope-editor-mobile.png'),fullPage:true});

 await assert.rejects(invoke('getCurrentUser',undefined,{},token),/\(401\)/);
 let auth=await login('scope-finance'); token=auth.accessToken;
 assert.equal(auth.user.capabilities.scopeRestrictions.length,4);
 const page=await openPage('scope-finance','Scope-Accept-2026');
 for(const resourceKey of resources){
  const isInvoice=resourceKey.endsWith('invoices'), get=isInvoice?'GetInvoice':'GetPayment', list=isInvoice?'ListInvoices':'ListPayments', update=isInvoice?'UpdateInvoice':'UpdatePayment', key=isInvoice?'invoice':'payment';
  assert.equal((await invoke(list,undefined,{keyword:isInvoice?'SCOPE-INVOICE':'SCOPE-PAYMENT'},token)).totalCount,2);
  if(isInvoice){
   await page.goto(`${url}/#/invoices`);
   const copy=page.getByRole('button',{name:'复制发票 SCOPE-INVOICE-B',exact:true});
   await copy.waitFor(); assert(await copy.isDisabled(),'List actions use company restrictions too');
   assert(await page.getByRole('button',{name:'复制发票 SCOPE-INVOICE-A',exact:true}).isEnabled());
  }
  for(const [index,row] of records.entries()){
   const id=row[key].id;
   await invoke(get,undefined,{id},token);
   await page.goto(`${url}/#/${isInvoice?'invoices':'payments'}/${id}`);
   await page.locator(isInvoice?'[aria-label="客户与出口商"]':'.entity-form').waitFor();
   const save=isInvoice?page.getByRole('button',{name:'保存发票',exact:true}):page.locator('.entity-form button[type="submit"]');
   if(index===1) {
    assert(await save.isDisabled(),'Visible company B must be read-only');
    await assert.rejects(invoke(update,row[key],{id},token),/\(403\)/);
    await page.screenshot({path:path.join(output,`${key}-company-b-readonly.png`),fullPage:true});
   } else {
    if(isInvoice) await page.getByRole('textbox',{name:/^发票号/u}).fill('SCOPE-INVOICE-A-SAVED');
    else await page.getByRole('textbox',{name:'付款单号',exact:true}).fill('SCOPE-PAYMENT-A-SAVED');
    assert(await save.isEnabled(),'Company A remains editable');
    const response=page.waitForResponse(r=>r.url().endsWith(`/api/${isInvoice?'invoices':'payments'}/${id}`)&&r.request().method()==='PUT');
    await save.click(); assert.equal((await response).status(),200);
    const saved=await invoke(get,undefined,{id},token);
    assert.equal(saved.companyScope,'DEFAULT'); assert.equal(saved.ownerUserId,row[key].ownerUserId);
   }
  }
 }
 await page.context().close();
 // Narrow viewing too. The list, direct detail URL and writes all enforce A.
 for(const resourceKey of resources){
  const row=(await invoke('GetAccessRestrictions',undefined,{principalKind:'Account',principalId:String(reader.id)},admin)).items.find(row=>row.resourceKey===resourceKey&&row.action==='view');
  await invoke('SaveAccessRestriction',{principalKind:'Account',principalId:String(reader.id),resourceKey,action:'view',mode:'Units',members:[String(main.id)],reason:'Only company A',expectedVersion:row.versionNumber},{},admin);
 }
 await assert.rejects(invoke('getCurrentUser',undefined,{},token),/\(401\)/);
 token=(await login('scope-finance')).accessToken;
 const narrowed=await openPage('scope-finance','Scope-Accept-2026');
 for(const resourceKey of resources){
  const isInvoice=resourceKey.endsWith('invoices'), get=isInvoice?'GetInvoice':'GetPayment', list=isInvoice?'ListInvoices':'ListPayments', update=isInvoice?'UpdateInvoice':'UpdatePayment', key=isInvoice?'invoice':'payment';
  assert.equal((await invoke(list,undefined,{keyword:isInvoice?'SCOPE-INVOICE':'SCOPE-PAYMENT'},token)).totalCount,1);
  await invoke(get,undefined,{id:records[0][key].id},token);
  await assert.rejects(invoke(get,undefined,{id:records[1][key].id},token),/\(403\)/);
  await assert.rejects(invoke(update,records[1][key],{id:records[1][key].id},token),/\(403\)/);
  await narrowed.goto(`${url}/#/${isInvoice?'invoices':'payments'}`);
  await narrowed.getByRole('cell',{name:isInvoice?'SCOPE-INVOICE-A-SAVED':'SCOPE-PAYMENT-A-SAVED',exact:true}).waitFor();
  assert.equal(await narrowed.getByRole('cell',{name:isInvoice?'SCOPE-INVOICE-B':'SCOPE-PAYMENT-B',exact:true}).count(),0);
  const denied=narrowed.waitForResponse(r=>r.url().endsWith(`/api/${isInvoice?'invoices':'payments'}/${records[1][key].id}`));
  await narrowed.goto(`${url}/#/${isInvoice?'invoices':'payments'}/${records[1][key].id}`);
  assert.equal((await denied).status(),403,'Direct React detail URL is denied');
 }
 fs.writeFileSync(path.join(output,'result.json'),JSON.stringify({success:true,resources,checks:['all reads both companies','React editor saves and reloads Units','view A+B edit A disables B save','React allowed edit preserves company and owner','Units narrows React list/detail/edit','permission change revokes session']},null,2));
});
