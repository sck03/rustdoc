import { approvalStage, publishDefinitions } from './lib/approval-fixtures.mjs';
import assert from 'node:assert/strict';
import path from 'node:path';
import { withOfficeUi } from './lib/native-office-ui.mjs';

async function verifyOfficeNavigation(page, personnel, administration) {
  for (const [name, links] of [['人事管理', personnel], ['行政办公', administration]]) {
    const group = page.locator('nav').getByRole('button', { name, exact: true });
    await group.waitFor();
    if (await group.getAttribute('aria-expanded') === 'false') await group.click();
    for (const label of links) await page.locator('nav').getByRole('link', { name: label, exact: true }).waitFor({ state: 'visible' });
  }
}

await withOfficeUi('approval-permissions-ui', async ({ output, url, invoke, openPage, operations, require }) => {
  const adminToken = (await invoke('Login', { username: 'oa-review', password: 'Review-2026-Test' })).accessToken;
  const admin = await openPage('oa-review', 'Review-2026-Test');
  for (const [code, name, parentCode] of [['EXEC', '总经办', null], ['TRADE', '外贸部', 'EXEC'], ['FINANCE', '财务部', 'EXEC']]) {
    await invoke('CreateOrganizationDepartment', { code, name, parentCode, companyCode: 'DEFAULT', isActive: true }, {}, adminToken);
  }
  await admin.goto(`${url}/#/office/people`);
  await admin.getByRole('button', { name: '入职登记', exact: true }).click();
  const dialog = admin.getByRole('dialog', { name: '入职登记', exact: true });
  const title = dialog.getByRole('combobox', { name: '职位', exact: true });
  assert(await title.evaluate(input => [...input.list.options].some(option => option.value === '普通员工')));
  assert(await title.evaluate(input => [...input.list.options].some(option => option.value === '总经理')));
  await dialog.locator('[name=employeeNumber]').fill('APPROVAL-GM');
  await dialog.locator('[name=fullName]').fill('总经理审批验收');
  await dialog.locator('[name=departmentId]').selectOption('EXEC');
  await title.fill('总经理兼业务协调负责人');
  await dialog.locator('[name=hireDate]').fill('2026-09-01');
  const personResponse = admin.waitForResponse(response => response.url().endsWith('/api/office/people') && response.request().method() === 'POST');
  await dialog.getByRole('button', { name: '登记入职', exact: true }).click();
  const personHttp = await personResponse; assert(personHttp.ok()); const manager = await personHttp.json();
  await dialog.waitFor({ state: 'hidden' });
  assert.equal(manager.employee.jobTitle, '总经理兼业务协调负责人');
  const account = (await invoke('createUserAccount', { username: 'approval-gm', fullName: manager.employee.fullName, role: 'OfficeEmployee',
    companyScope: 'DEFAULT', departmentId: 'EXEC', isActive: true, resetPassword: 'Approval-Manager-2026' }, {}, adminToken)).user;
  await invoke('LinkPersonnelAccount', { expectedVersion: manager.versionNumber, userId: account.id, expectedAccountVersion: account.versionNumber }, { id: manager.employee.id }, adminToken);
  const before = await invoke('Login', { username: 'approval-gm', password: 'Approval-Manager-2026' });
  assert(!before.user.capabilities.permissions.some(grant => grant.action === 'approve'), 'A manager job title does not grant approval');

  await admin.goto(`${url}/#/system/access-control?view=templates`);
  const schemes = admin.locator('.permission-template-section');
  await schemes.getByLabel('新建方案起点').selectOption('company');
  await schemes.getByRole('button', { name: '新建方案', exact: true }).click();
  assert.equal(await schemes.getByLabel('方案名称', { exact: true }).inputValue(), '公司审批负责人');
  await schemes.getByLabel('模块分类').selectOption('人事管理');
  assert.equal(await schemes.getByLabel('员工请假查看数据范围', { exact: true }).inputValue(), 'company');
  assert.equal(await schemes.getByLabel('员工请假审批与作废数据范围', { exact: true }).inputValue(), 'company');
  assert.equal(await schemes.getByLabel('人员变动审批快捷设置').inputValue(), 'approve');
  const savedScheme = admin.waitForResponse(response => response.url().endsWith('/api/permission-templates') && response.request().method() === 'POST');
  await schemes.getByRole('button', { name: '保存方案', exact: true }).click();
  const schemeHttp = await savedScheme; assert(schemeHttp.ok()); const scheme = await schemeHttp.json();
  await schemes.getByRole('status').filter({ hasText: '权限方案已保存' }).waitFor();
  await admin.screenshot({ path: path.join(output, 'company-approval-scheme.png'), fullPage: true });
  await admin.goto(`${url}/#/system/access-control?userId=${account.id}`);
  const accounts = admin.locator('.user-management-section');
  await accounts.getByText(/总经理兼业务协调负责人/u).waitFor();
  await accounts.getByRole('combobox', { name: /^权限方案/u }).selectOption(String(scheme.id));
  await accounts.getByRole('button', { name: '保存', exact: true }).click();
  await accounts.getByRole('status').filter({ hasText: '账号已保存' }).waitFor();
  const login = await invoke('Login', { username: 'approval-gm', password: 'Approval-Manager-2026' });
  await assert.rejects(() => invoke('GetPersonnel', undefined, { id: manager.employee.id }, login.accessToken), /403/u);
  await assert.rejects(() => invoke('ListUsers', undefined, {}, login.accessToken), /403/u);
  const gm = await openPage('approval-gm', 'Approval-Manager-2026');
  await verifyOfficeNavigation(gm, ['人员变动审批', '员工请假'], ['费用报销', '通用申请']);
  assert.equal(await gm.locator('nav').getByRole('link', { name: '人员档案', exact: true, hidden: true }).count(), 0);
  const employee = await invoke('CreatePersonnel', { requestKey: 'approval-worker', employeeNumber: 'APPROVAL-WORKER', departmentId: 'TRADE',
    jobTitle: '普通员工', employmentType: 'FullTime', hireDate: '2026-09-01', onProbation: true, profile: { fullName: '跨部门审批员工' } }, {}, adminToken);
  const policy = await invoke('GetOaApprovalSettings', undefined, {}, adminToken);
  await publishDefinitions(invoke, adminToken, { ...policy, personnelApprovalActions: ['Confirm'],
    rules: policy.rules.map(rule => ['general', 'personnel:Confirm'].includes(rule.key) ? { ...rule, stages: [approvalStage('review', [account.id])] } : rule) }, ['general', 'personnel:Confirm']);
  for (const [kind, op, extra, button] of [
    ['general', 'General', { category: 'Other' }, '批准'],
    ['personnel', 'PersonnelChange', { personnelChange: { action: 'Confirm', expectedPersonnelVersion: employee.versionNumber, effectiveDate: login.user.businessDate } }, '批准人员变动'],
  ]) {
    const draft = await invoke(`Create${op}Request`, { requestKey: `approval-${kind}`, employeeId: employee.employee.id,
      title: `总经理跨部门${kind}审批`, reason: '职责方案与实际审批分离验证', ...extra }, {}, adminToken);
    const row = await invoke(`Submit${op}Request`, { expectedVersion: draft.versionNumber, note: '提交总经理审批' }, { id: draft.id }, adminToken);
    await gm.goto(`${url}/#/office/requests/${kind}?requestId=${row.id}`);
    await gm.getByRole('button', { name: button, exact: true }).click();
    await gm.getByRole('textbox', { name: '处理说明', exact: true }).fill('审批范围与当前步骤核对通过');
    await gm.getByRole('dialog').getByRole('button', { name: button, exact: true }).click();
    await gm.getByRole('dialog').waitFor({ state: 'hidden' });
    const approved = await invoke(`Get${op}Request`, undefined, { id: row.id }, login.accessToken);
    assert.equal(approved.status, kind === 'personnel' ? 'Completed' : 'Approved');
    assert.equal(approved.approvalPlan.steps[0].actedByUserId, account.id);
    if (kind === 'general') await assert.rejects(() => invoke('CompleteGeneralRequest', { expectedVersion: approved.versionNumber, note: '审批不授予办结' }, { id: row.id }, login.accessToken), /403/u);
  }
  assert.equal((await invoke('GetPersonnel', undefined, { id: employee.employee.id }, adminToken)).employee.status, 'Active');
  await gm.screenshot({ path: path.join(output, 'manager-personnel-approved.png'), fullPage: true });
  await gm.setViewportSize({ width: 390, height: 844 });
  assert(await gm.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
  await gm.screenshot({ path: path.join(output, 'manager-approval-mobile.png'), fullPage: true });
  const axe = require('axe-core'); await gm.addScriptTag({ content: axe.source });
  assert.deepEqual((await gm.evaluate(async () => window.axe.run(document.querySelector('main'), { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] } }))).violations, []);
  // Account restrictions survive changing the fallback role, and close both UI/API access.
  await accounts.locator('summary').filter({ hasText: '账号单独配置与模块权限' }).click();
  await accounts.getByLabel('员工请假模块开放', { exact: true }).uncheck();
  await accounts.getByRole('combobox', { name: /^角色/u }).selectOption('Sales');
  assert.equal(await accounts.getByLabel('员工请假模块开放', { exact: true }).isChecked(), false);
  await accounts.getByRole('combobox', { name: /^角色/u }).selectOption('OfficeEmployee');
  await accounts.getByRole('combobox', { name: /^权限方案/u }).selectOption(String(scheme.id));
  await accounts.getByRole('button', { name: '保存', exact: true }).click();
  await accounts.getByRole('status').filter({ hasText: '账号已保存' }).waitFor();
  const op = operations.get('getCurrentUser');
  assert.equal((await fetch(url + op.route, { headers: { authorization: `Bearer ${login.accessToken}` } })).status, 401);
  const restricted = await invoke('Login', { username: 'approval-gm', password: 'Approval-Manager-2026' });
  await assert.rejects(() => invoke('ListLeaveRequest', undefined, {}, restricted.accessToken), /403/u);

  await admin.goto(`${url}/#/system/access-control?view=templates`);
  await schemes.getByLabel('新建方案起点').selectOption('combined');
  const sources = schemes.getByRole('group', { name: '选择需要兼任的职责' });
  assert.equal(await sources.getByRole('checkbox', { name: '系统管理员', exact: true }).count(), 0);
  for (const name of ['人事管理员', '行政管理员', '财务人员']) await sources.getByRole('checkbox', { name, exact: true }).check();
  await schemes.getByRole('button', { name: '新建方案', exact: true }).click();
  await schemes.getByLabel('方案名称', { exact: true }).fill('人事行政财务综合岗');
  const combinedResponse = admin.waitForResponse(response => response.url().endsWith('/api/permission-templates') && response.request().method() === 'POST');
  await schemes.getByRole('button', { name: '保存方案', exact: true }).click();
  const combinedHttp = await combinedResponse; assert(combinedHttp.ok()); const combined = await combinedHttp.json();
  await schemes.getByRole('status').filter({ hasText: '权限方案已保存' }).waitFor();
  await admin.setViewportSize({ width: 390, height: 844 });
  assert(await admin.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
  await admin.screenshot({ path: path.join(output, 'combined-duties-mobile.png'), fullPage: true });
  await invoke('createUserAccount', { username: 'combined-worker', fullName: '人事行政财务兼任', role: 'OfficeEmployee',
    companyScope: 'DEFAULT', departmentId: 'GENERAL', isActive: true, resetPassword: 'Combined-Worker-2026', permissionTemplateId: combined.id }, {}, adminToken);
  const combinedLogin = await invoke('Login', { username: 'combined-worker', password: 'Combined-Worker-2026' });
  for (const [resourceKey, action] of [['office.people', 'view-details'], ['office.general', 'approve'], ['office.expenses', 'complete']]) {
    assert(combinedLogin.user.capabilities.permissions.some(grant => grant.resourceKey === resourceKey && grant.action === action && grant.dataScope === 'company'));
  }
  assert.equal(combinedLogin.user.capabilities.canManageUsers, false);
  assert.equal((await invoke('GetPersonnel', undefined, { id: employee.employee.id }, combinedLogin.accessToken)).employee.status, 'Active');
  await assert.rejects(() => invoke('ListUsers', undefined, {}, combinedLogin.accessToken), /403/u);
  const worker = await openPage('combined-worker', 'Combined-Worker-2026');
  await verifyOfficeNavigation(worker, ['人员档案'], ['通用申请', '费用报销']);
  await worker.screenshot({ path: path.join(output, 'combined-duties-navigation.png'), fullPage: true });
});
