import assert from 'node:assert/strict';
import path from 'node:path';
import { withOfficeUi } from './lib/native-office-ui.mjs';

await withOfficeUi('personnel-accounts-ui', async ({ output, url, invoke, openPage }) => {
  const token = (await invoke('Login', { username: 'oa-review', password: 'Review-2026-Test' })).accessToken;
  const person = await invoke('CreatePersonnel', { requestKey: 'identity-ui-person', employeeNumber: 'IDENTITY-UI',
    departmentId: 'GENERAL', jobTitle: '外贸业务主管', employmentType: 'FullTime', hireDate: '2026-09-01',
    onProbation: false, profile: { fullName: '关联员工验收' } }, {}, token);
  const account = (await invoke('CreateUserAccount', { username: 'identity-ui', fullName: '待同步姓名', role: 'OfficeEmployee',
    companyScope: 'DEFAULT', departmentId: 'GENERAL', isActive: true, resetPassword: 'Identity-UI-2026' }, {}, token)).user;
  const admin = await openPage('oa-review', 'Review-2026-Test');
  await admin.goto(`${url}/#/system/access-control?userId=${account.id}`);
  const form = admin.locator('.user-management-section');
  await form.getByLabel('账号', { exact: true }).waitFor();
  await admin.waitForFunction(() => document.querySelector('.user-management-form-grid input')?.value === 'identity-ui');
  await admin.goto(`${url}/#/office/people/${person.employee.id}`);
  await admin.getByRole('button', { name: '关联账号', exact: true }).click();
  await admin.getByRole('textbox', { name: '搜索可关联账号' }).fill('identity-ui');
  await admin.getByRole('dialog').getByRole('button', { name: '搜索', exact: true }).click();
  await admin.getByRole('radio', { name: /identity-ui/u }).check();
  await admin.getByRole('button', { name: '确认关联', exact: true }).click();
  await admin.getByRole('dialog').waitFor({ state: 'hidden' });
  await admin.getByRole('link', { name: '管理关联账号与权限' }).click();
  await form.getByText('实际职位：外贸业务主管', { exact: false }).waitFor();
  assert.equal(await form.getByLabel('账号', { exact: true }).inputValue(), 'identity-ui');
  assert.equal(await form.getByLabel('姓名', { exact: true }).inputValue(), '关联员工验收');
  assert.equal(await form.getByLabel('姓名', { exact: true }).getAttribute('readonly'), '');
  assert(await form.getByLabel(/^所属部门/u).isDisabled());
  assert.equal(await form.getByRole('combobox', { name: /^角色/u }).inputValue(), 'OfficeEmployee');
  await form.getByLabel('启用账号', { exact: true }).uncheck();
  await form.getByRole('button', { name: '保存', exact: true }).click();
  await form.getByRole('status').filter({ hasText: '账号已保存' }).waitFor();
  await form.getByRole('link', { name: '打开员工档案' }).click();
  await admin.getByText('identity-ui（停用）', { exact: true }).waitFor();
  await admin.getByRole('link', { name: '管理关联账号与权限' }).click();
  await form.getByLabel('启用账号', { exact: true }).check();
  await form.getByRole('button', { name: '保存', exact: true }).click();
  await form.getByRole('status').filter({ hasText: '账号已保存' }).waitFor();
  await form.getByRole('link', { name: '打开员工档案' }).click();
  await admin.getByText('identity-ui（启用）', { exact: true }).waitFor();
  await admin.getByRole('button', { name: '编辑档案', exact: true }).click();
  await admin.locator('input[name=fullName]').fill('人事同步新姓名');
  await admin.getByRole('button', { name: '保存档案', exact: true }).click();
  await admin.getByRole('dialog').waitFor({ state: 'hidden' });
  await admin.getByRole('link', { name: '管理关联账号与权限' }).click();
  await admin.waitForFunction(() => document.querySelector('.user-management-form-grid input[readonly]')?.value === '人事同步新姓名');
  await form.getByLabel('查找账号或分组').fill('IDENTITY-UI');
  assert.equal(await form.locator('tbody tr').count(), 1);
  await admin.screenshot({ path: path.join(output, 'linked-account.png'), fullPage: true });
  await admin.setViewportSize({ width: 390, height: 844 });
  assert(await admin.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
  await admin.screenshot({ path: path.join(output, 'linked-account-mobile.png'), fullPage: true });
});
