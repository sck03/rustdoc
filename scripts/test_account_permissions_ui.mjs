import assert from 'node:assert/strict';
import path from 'node:path';
import { withOfficeUi } from './lib/native-office-ui.mjs';

await withOfficeUi('account-permissions-ui', async ({ output, url, invoke, openPage, require, operations }) => {
  const token = (await invoke('Login', { username: 'oa-review', password: 'Review-2026-Test' })).accessToken;
  const admin = await openPage('oa-review', 'Review-2026-Test');
  await admin.goto(`${url}/#/system/access-control`);
  const form = admin.locator('.user-management-section');
  await form.getByRole('button', { name: '新建用户', exact: true }).click();
  assert.equal(await form.getByRole('combobox', { name: /^角色/u }).inputValue(), 'OfficeEmployee');
  await form.getByLabel('账号', { exact: true }).fill('employee-ui');
  await form.getByLabel('姓名', { exact: true }).fill('普通员工验收');
  await form.getByLabel(/^所属公司/u).selectOption('DEFAULT');
  await form.getByLabel(/^所属部门/u).selectOption('GENERAL');
  await form.getByLabel(/^初始\/重置密码/u).fill('Employee-UI-2026');
  await form.getByRole('button', { name: '保存', exact: true }).click();
  await form.getByRole('status').filter({ hasText: '账号已保存' }).waitFor();
  const employee = await openPage('employee-ui', 'Employee-UI-2026');
  await employee.waitForURL('**/#/office/approvals');
  assert(!(await employee.locator('body').innerText()).includes('单证人员'), 'Employee badge uses the correct role');
  assert.equal(await employee.getByRole('link', { name: '单证概览', exact: true }).count(), 0);
  assert.equal(await employee.getByRole('link', { name: '账号与权限', exact: true }).count(), 0);
  assert.equal(await employee.getByRole('link', { name: '人员档案', exact: true }).count(), 0);
  await employee.screenshot({ path: path.join(output, 'employee-home.png'), fullPage: true });
  // Create a shared group, then assign it through the real account editor.
  const catalog = await invoke('ListPermissionTemplates', undefined, {}, token);
  const source = catalog.templates.find(item => item.code === 'OfficeEmployee');
  const resources = new Map(catalog.resources.map(item => [item.key, item]));
  const group = await invoke('CreatePermissionTemplate', { id: 0, code: 'employee-ui-group', name: '行政自助分组', description: '共享分组验收', isActive: true,
    grants: source.grants.filter(grant => !resources.get(grant.resourceKey).isTechnical), disabledModules: ['office.purchase'] }, {}, token);
  await form.getByRole('button', { name: '刷新用户', exact: true }).click();
  await form.getByRole('combobox', { name: /^权限方案/u }).selectOption(String(group.id));
  await form.getByRole('button', { name: '保存', exact: true }).click();
  await form.getByRole('status').filter({ hasText: '账号已保存' }).waitFor();
  let login = await invoke('Login', { username: 'employee-ui', password: 'Employee-UI-2026' });
  assert(!login.user.capabilities.enabledModules.includes('office.purchase'));
  const request = async (id, bearer) => {
    const op = operations.get(id);
    return fetch(url + op.route, { method: op.method, headers: { authorization: `Bearer ${bearer}` } });
  };
  assert.equal((await request('ListPurchaseRequest', login.accessToken)).status, 403);
  // Independent account settings retain the copied restrictions and can narrow actions/scopes.
  await form.getByText(/账号单独配置与模块权限/u).click();
  await form.getByLabel('账号权限来源').selectOption('custom');
  await form.getByLabel('模块分类').selectOption('人事管理');
  const people = form.getByRole('region', { name: '人员档案与通讯录', exact: true });
  await people.getByLabel('人员档案与通讯录模块开放', { exact: true }).uncheck();
  await form.getByLabel('账号权限来源').selectOption('inherit');
  await form.getByText(/账号额外关闭的模块/u).waitFor();
  await form.getByLabel('账号权限来源').selectOption('custom');
  assert.equal(await people.getByLabel('人员档案与通讯录模块开放', { exact: true }).isChecked(), false, 'Switching sources preserves account restrictions');
  await form.getByRole('button', { name: '保存', exact: true }).click();
  await form.getByRole('status').filter({ hasText: '账号已保存' }).waitFor();
  login = await invoke('Login', { username: 'employee-ui', password: 'Employee-UI-2026' });
  assert(!login.user.capabilities.enabledModules.includes('office.people'));
  assert(!login.user.capabilities.enabledModules.includes('office.purchase'));
  assert.equal((await request('ListPersonnel', login.accessToken)).status, 403);
  // A stale account save must preserve the visible draft and version conflict.
  await form.getByLabel('姓名', { exact: true }).fill('保留冲突草稿');
  const users = await invoke('ListUsers', undefined, {}, token);
  const account = users.users.find(item => item.username === 'employee-ui');
  await invoke('updateUserAccount', { ...account, resetPassword: '', expectedVersion: account.versionNumber }, { id: account.id }, token);
  const conflict = admin.waitForResponse(response => response.url().includes(`/api/users/${account.id}`) && response.request().method() === 'PUT');
  await form.getByRole('button', { name: '保存', exact: true }).click();
  assert.equal((await conflict).status(), 409);
  assert.equal(await form.getByLabel('姓名', { exact: true }).inputValue(), '保留冲突草稿');
  await admin.screenshot({ path: path.join(output, 'account-permissions.png'), fullPage: true });
  const axe = require('axe-core');
  await admin.addScriptTag({ content: axe.source });
  const scan = await admin.evaluate(async () => window.axe.run(document.querySelector('.user-management-section'), { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] } }));
  assert.deepEqual(scan.violations, [], 'Account permission editor accessibility');
  await admin.setViewportSize({ width: 390, height: 844 });
  await admin.screenshot({ path: path.join(output, 'account-mobile.png'), fullPage: true });
  assert(await admin.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), 'Mobile page does not overflow');
});
