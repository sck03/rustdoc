import assert from 'node:assert/strict';
import path from 'node:path';
import { withOfficeUi } from './lib/native-office-ui.mjs';

await withOfficeUi('personnel-accounts-ui', async ({ output, url, invoke, openPage }) => {
  const token = (await invoke('Login', { username: 'oa-review', password: 'Review-2026-Test' })).accessToken;
  const person = await invoke('CreatePersonnel', { requestKey: 'identity-ui-person', employeeNumber: 'IDENTITY-UI',
    departmentId: 'GENERAL', jobTitle: '外贸业务主管', employmentType: 'FullTime', hireDate: '2026-09-01',
    onProbation: false, profile: { fullName: '关联员工验收' } }, {}, token);
  const account = (await invoke('createUserAccount', { username: 'identity-ui', fullName: '待同步姓名', role: 'OfficeEmployee',
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
  const namesakes = [];
  for (const number of ['NAMESAKE-A', 'NAMESAKE-B']) {
    namesakes.push(await invoke('CreatePersonnel', { requestKey: number, employeeNumber: number,
      departmentId: 'GENERAL', jobTitle: '业务主管', employmentType: 'FullTime', hireDate: '2026-09-01',
      onProbation: false, profile: { fullName: '同名负责人' } }, {}, token));
  }
  await invoke('CreateOrganizationDepartment', { code: 'NAMESAKE', companyCode: 'DEFAULT', name: '重名负责人测试', isActive: true }, {}, token);
  await admin.setViewportSize({ width: 1440, height: 1000 });
  await admin.goto(`${url}/#/system/organization`);
  const department = admin.locator('tr[data-department-code=NAMESAKE]');
  await department.getByRole('button', { name: '编辑重名负责人测试', exact: true }).click();
  await admin.getByRole('button', { name: '选择负责人', exact: true }).click();
  const picker = admin.getByRole('dialog', { name: '选择部门负责人', exact: true });
  await picker.getByRole('textbox', { name: '按姓名或工号搜索负责人' }).fill('同名负责人');
  await picker.getByRole('button', { name: '搜索', exact: true }).click();
  await picker.getByRole('button', { name: /NAMESAKE-B/u }).waitFor();
  await admin.waitForFunction(() => document.querySelectorAll('.organization-managers li').length === 2);
  assert.equal(await picker.locator('.organization-managers li').count(), 2);
  await picker.getByRole('button', { name: /NAMESAKE-B/u }).click();
  await admin.getByRole('dialog', { name: '编辑部门', exact: true }).getByText('同名负责人 · NAMESAKE-B', { exact: true }).waitFor();
  await admin.getByRole('button', { name: '保存部门', exact: true }).click();
  await admin.getByRole('dialog').waitFor({ state: 'hidden' });
  await department.getByText('NAMESAKE-B', { exact: true }).waitFor();
  const savedDepartment = (await invoke('GetOrganizationDirectory', undefined, {}, token)).departments.find(row => row.code === 'NAMESAKE');
  assert.equal(savedDepartment.managerEmployeeId, namesakes[1].employee.id);
  assert.equal(savedDepartment.managerEmployeeNumber, 'NAMESAKE-B');
  await admin.getByRole('searchbox', { name: '查找部门或负责人' }).fill('NAMESAKE-B');
  assert.equal(await admin.locator('.organization-tree-table tbody tr').count(), 1);
  await department.locator('.organization-node-name').click();
  await admin.screenshot({ path: path.join(output, 'namesake-manager.png'), fullPage: true });
  await admin.setViewportSize({ width: 390, height: 844 });
  assert(await admin.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
  await admin.locator('.organization-selection-path').getByText(/NAMESAKE-B/u).waitFor();
  await admin.screenshot({ path: path.join(output, 'namesake-manager-mobile.png'), fullPage: true });
  const application = await invoke('CreateGeneralRequest', { requestKey: 'namesake-application', employeeId: namesakes[1].employee.id,
    title: '同名申请核对', reason: '确认按工号选择到第二人', category: 'Other', currency: 'CNY' }, {}, token);
  assert.equal(application.employeeNumber, 'NAMESAKE-B');
  await admin.setViewportSize({ width: 1440, height: 1000 });
  await admin.goto(`${url}/#/office/requests/general`);
  await admin.getByRole('button', { name: '同名申请核对', exact: true }).click();
  await admin.locator('.oa-detail-summary').getByText('同名负责人 · 工号 NAMESAKE-B', { exact: true }).waitFor();
  await admin.screenshot({ path: path.join(output, 'namesake-application.png'), fullPage: true });
});
