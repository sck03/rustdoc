import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { withOfficeUi } from './lib/native-office-ui.mjs';

await withOfficeUi('oa-handling-ui', async ({ output, require, url, invoke, openPage }) => {
  const password = 'Handling-UI-2026';
  const token = (await invoke('Login', { username: 'oa-review', password: 'Review-2026-Test' })).accessToken;
  const users = [];
  for (const [name, role] of [['leader', 'OfficeManager'], ['director', 'OfficeManager'], ['seal', 'OfficeEmployee'], ['special', 'OfficeEmployee']]) {
    const permissionGrants = ['seal', 'special'].includes(name) ? [
      ...['view', 'complete'].map(action => ({ resourceKey: 'office.general', action, dataScope: 'own' })),
      { resourceKey: 'office.supplies', action: 'view', dataScope: 'own' },
      ...['issue', 'return', 'restock'].map(action => ({ resourceKey: 'office.supplies', action, dataScope: 'own' })),
      { resourceKey: 'office.notifications', action: 'view', dataScope: 'own' },
    ] : undefined;
    users.push((await invoke('createUserAccount', { username: `handling-${name}`, fullName: `办理${name}`, role, companyScope: 'DEFAULT', departmentId: 'GENERAL', isActive: true, resetPassword: password, permissionGrants }, {}, token)).user);
  }
  const admin = await openPage('oa-review', 'Review-2026-Test');
  await admin.goto(`${url}/#/office/approval-settings`);
  const section = admin.getByRole('region', { name: '通用申请审批规则', exact: true });
  await section.getByLabel('审批方式').selectOption('Named');
  for (const [i,user] of users.slice(0,2).entries()) {
    await section.getByRole('button', { name: '添加审批步骤', exact: true }).click();
    await section.getByLabel(`第 ${i+1} 步审批人`).selectOption(String(user.id));
  }
  for (const [i,[category,name,handler]] of [['Seal','公章',2],['Seal','合同章',3],['Certificate','在职证明',3],['Supply','日常用品',2],['Supply','服装辅料',3]].entries()) {
    await admin.getByRole('button', { name: '添加办理分工', exact: true }).click();
    const group = admin.getByRole('group', { name: `办理分工 ${i+1}`, exact: true });
    await group.getByLabel('分工类别').selectOption(category);
    await group.getByLabel('事项或物品组名称').fill(name);
    await group.getByRole('combobox', { name: /^办理人员 1/u }).selectOption(String(users[handler].id));
  }
  const save = admin.waitForResponse(r => r.url().endsWith('/api/office/approval-settings') && r.request().method() === 'PUT');
  await admin.getByRole('button', { name: '保存审批设置', exact: true }).click();
  assert.equal((await save).status(),200);
  await admin.screenshot({ path: path.join(output,'handling-settings.png'),fullPage:true });
  const policy = await invoke('GetOaApprovalSettings',undefined,{},token);
  const keys = Object.fromEntries(policy.handlingServices.map(row => [row.name,row.key]));
  const leader = await openPage('handling-leader',password);
  const director = await openPage('handling-director',password);
  const seal = await openPage('handling-seal',password);
  const special = await openPage('handling-special',password);
  const act = async (page,label,note=label) => {
    await page.getByRole('button',{name:label,exact:true}).click();
    const dialog=page.getByRole('dialog'); await dialog.getByLabel('处理说明').fill(note);
    await dialog.getByRole('button',{name:label,exact:true}).click(); await dialog.waitFor({state:'hidden'});
  };
  const proof=path.join(output,'seal-proof.pdf'); fs.writeFileSync(proof,'%PDF-1.7\n%%EOF');
  for (const [category,name,handler,other] of [['Seal','公章',seal,special],['Seal','合同章',special,seal],['Certificate','在职证明',special,seal]]) {
    await admin.goto(`${url}/#/office/requests/general`);
    await admin.getByRole('button',{name:'新建通用申请',exact:true}).click();
    const form=admin.getByRole('dialog');
    await form.getByLabel('登记人员',{exact:true}).fill('OA-001'); await form.getByRole('option',{name:/OA-001/u}).click();
    await form.getByLabel('申请标题').fill(`${name}闭环验收`); await form.getByLabel('申请说明').fill('先经部门领导及总经理批准，再交专岗办理。');
    await form.getByLabel('申请类别').selectOption(category);
    await form.getByLabel('具体印章／办理事项').selectOption(keys[name]);
    await form.locator('input[type=file]').setInputFiles(proof);
    await form.getByRole('button',{name:'保存草稿',exact:true}).click(); await form.waitFor({state:'hidden'});
    const requestUrl=admin.url();
    await act(admin,'提交审批');
    await handler.goto(`${url}/#/office/requests/general?view=handling`);
    await handler.getByText('当前筛选下没有申请',{exact:true}).waitFor();
    await leader.goto(requestUrl); await act(leader,'登记批准结果');
    await handler.reload(); await handler.getByText('当前筛选下没有申请',{exact:true}).waitFor();
    await director.goto(requestUrl); await act(director,'登记批准结果');
    await handler.goto(`${url}/#/office/notifications`);
    await handler.getByRole('heading',{name:`审批通过 · ${name}闭环验收`,exact:true}).waitFor();
    await handler.getByRole('link',{name:'查看关联申请',exact:true}).first().click();
    await handler.getByText(`办理事项：${name}`,{exact:false}).waitFor();
    const download=handler.waitForEvent('download'); await handler.getByRole('button',{name:'下载',exact:true}).click();
    assert.equal((await download).suggestedFilename(),'seal-proof.pdf');
    await other.goto(requestUrl); await other.getByText('申请读取失败',{exact:true}).waitFor();
    await handler.screenshot({path:path.join(output,`${category}-${name}.png`),fullPage:true});
    await act(handler,'办结登记',`${name}已当面交付，接收人确认无误`);
    await handler.locator('.oa-detail-summary .office-badge').getByText('已完成',{exact:true}).waitFor();
  }
  for (const [group,name,handler,other] of [['日常用品','卫生卷纸',seal,special],['服装辅料','服装吊牌',special,seal]]) {
    await admin.goto(`${url}/#/office/supplies`); await admin.getByRole('button',{name:'添加物品',exact:true}).click();
    const form=admin.getByRole('dialog'); await form.getByLabel('物品名称',{exact:true}).fill(name);
    await form.getByLabel('物品组与保管分工').selectOption(keys[group]);
    await form.getByRole('button',{name:'保存',exact:true}).click(); await form.waitFor({state:'hidden'});
    const card=admin.locator('.office-resource-card').filter({has:admin.getByRole('heading',{name,exact:true})});
    await card.getByRole('button',{name:'补充库存',exact:true}).click();
    await admin.getByRole('dialog').getByLabel(/^本次补充数量/u).fill('10');
    await admin.getByRole('dialog').getByLabel('入库说明／采购单号').fill('采购验收后入库');
    await admin.getByRole('dialog').getByRole('button',{name:'确认入库',exact:true}).click(); await form.waitFor({state:'hidden'});
    await card.getByRole('button',{name:'登记领用',exact:true}).click();
    await form.getByLabel('登记人员',{exact:true}).fill('OA-001'); await form.getByRole('option',{name:/OA-001/u}).click();
    await form.getByLabel('领用用途').fill('分工发放验收'); await form.getByRole('button',{name:'登记领用',exact:true}).click(); await form.waitFor({state:'hidden'});
    await handler.goto(`${url}/#/office/supplies?view=requests&handlingOnly=true&status=Approved`);
    const request=handler.locator('.office-request-card').filter({has:handler.getByRole('heading',{name,exact:true})});
    await request.waitFor(); await request.getByRole('button',{name:'确认发放',exact:true}).click();
    await handler.getByRole('dialog').getByRole('button',{name:'确认发放',exact:true}).click(); await handler.getByRole('dialog').waitFor({state:'hidden'});
    await other.goto(`${url}/#/office/supplies?view=requests&handlingOnly=true&status=Approved`);
    await other.getByText('当前条件下没有登记记录',{exact:true}).waitFor();
    await handler.screenshot({path:path.join(output,`${group}-issued.png`),fullPage:true});
  }
  await special.setViewportSize({width:390,height:844}); await special.goto(`${url}/#/office/approvals`);
  await special.getByRole('link',{name:'通用申请 · 待办理',exact:true}).waitFor();
  await special.addScriptTag({path:require.resolve('axe-core/axe.min.js')});
  assert.deepEqual(await special.evaluate(async()=> (await window.axe.run(document.querySelector('.oa-workspace'))).violations.filter(v=>['critical','serious'].includes(v.impact)).map(v=>v.id)),[]);
  await special.screenshot({path:path.join(output,'mobile-handling-hub.png'),fullPage:true});
});
