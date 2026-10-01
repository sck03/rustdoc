import assert from 'node:assert/strict';
import path from 'node:path';
import { withOfficeUi } from './lib/native-office-ui.mjs';

await withOfficeUi('template-private-ui', async ({ output, url, invoke, openPage }) => {
  const login = await invoke('Login', { username: 'oa-review', password: 'Review-2026-Test' });
  const admin = login.accessToken;
  const grants = ['view', 'design', 'clone'].map(action => ({ resourceKey: 'document.report-templates', action, dataScope: 'company' }));
  grants.push({ resourceKey: 'document.invoices', action: 'view', dataScope: 'company' });
  const role = await invoke('CreatePermissionTemplate', { code: 'PRIVATE-DESIGN', name: '私人模板设计', isActive: true, grants }, {}, admin);
  for (const username of ['private-owner', 'private-other']) {
    await invoke('createUserAccount', { username, fullName: username, role: 'User', companyScope: 'DEFAULT', departmentId: 'GENERAL', permissionTemplateId: role.id, isActive: true, resetPassword: 'Private-Test-2026' }, {}, admin);
  }
  const file = await invoke('CreateReportTemplate', { reportType: 'ExportDocument', displayName: '团队公共原稿', templatePath: 'user:Export/team-template.dtpl' }, {}, admin);
  await invoke('SetDefaultReportTemplate', { reportType: 'ExportDocument', templatePath: file.templatePath }, {}, admin);
  const ownerSession = await invoke('Login', { username: 'private-owner', password: 'Private-Test-2026' });
  assert(ownerSession.user.capabilities.permissions.some(grant => grant.resourceKey === 'document.report-templates' && grant.action === 'view'), JSON.stringify(ownerSession.user.capabilities));
  const page = await openPage('private-owner', 'Private-Test-2026');
  await page.waitForURL('**/#/access-denied');
  await page.goto(`${url}/#/reports/templates/manage`);
  await page.locator('.template-select-field select').selectOption(file.templatePath);
  assert(await page.getByRole('button', { name: '当前全局默认', exact: true }).isDisabled());
  assert.equal(await page.getByLabel('新建模板', { exact: true }).count(), 0);
  await page.locator('details[aria-label="我的和共享模板"] > summary').click();
  await page.getByLabel('新模板名称', { exact: true }).fill('私人发票样式');
  const cloned = page.waitForResponse(response => response.url().endsWith('/api/reports/user-templates/clone') && response.request().method() === 'POST');
  await page.getByRole('button', { name: '复制当前模板', exact: true }).click();
  const copy = await (await cloned).json();
  assert.equal(copy.status, 'Draft'); assert.equal(copy.shareScope, 'Private');
  await page.getByRole('heading', { name: '私人发票样式', exact: true }).waitFor();
  assert.equal(await page.getByRole('button', { name: '发布（为共享准备）', exact: true }).count(), 0);
  await page.getByRole('button', { name: '打开设计器', exact: true }).click();
  const design = JSON.parse(copy.contentHtml);
  const first = design.layers.flatMap(layer => layer.elements).find(element => element.type === 'Text');
  assert(first, 'Starter includes editable text');
  const element = page.locator(`[data-v3-element-id="${first.id}"]`);
  await element.waitFor(); await element.click(); await element.press('ArrowRight');
  const savedResponse = page.waitForResponse(response => response.url().includes(`/api/reports/user-templates/${copy.id}`) && response.request().method() === 'PUT');
  await page.getByRole('button', { name: '保存', exact: true }).click();
  const saved = await (await savedResponse).json();
  assert.equal(saved.status, 'Draft'); assert.equal(saved.shareScope, 'Private');
  assert.notEqual(saved.contentHtml, copy.contentHtml, 'Canvas edit saved');
  await page.getByText('私人模板已保存，可直接用于自己的打印和导出，无需发布。', { exact: true }).waitFor();
  await page.screenshot({ path: path.join(output, 'private-design-saved.png'), fullPage: true });
  await page.getByRole('tab', { name: '预览', exact: true }).click();
  await page.getByRole('button', { name: '样例预览', exact: true }).click();
  await page.waitForFunction(() => document.querySelector('iframe[title="模板预览"]')?.srcdoc.includes('<svg'));
  await page.screenshot({ path: path.join(output, 'private-native-preview.png'), fullPage: true });
  for (const [username, expected] of [['private-owner', true], ['private-other', false]]) {
    const session = await invoke('Login', { username, password: 'Private-Test-2026' });
    const catalog = await invoke('ListReportTemplates', undefined, {}, session.accessToken);
    assert.equal(catalog.some(template => template.templatePath === `user-template:${copy.id}`), expected);
  }
  const original = await fetch(`${url}/api/reports/templates/content?reportType=ExportDocument&templatePath=${encodeURIComponent(file.templatePath)}`, { headers: { authorization: `Bearer ${admin}` } }).then(response => response.json());
  assert.equal(original.content, file.content);
  const peer = await openPage('private-other', 'Private-Test-2026');
  await peer.waitForURL('**/#/access-denied');
  await peer.goto(`${url}/#/reports/templates/manage`);
  await peer.locator('.template-select-field select').waitFor();
  assert.equal(await peer.locator(`option[value="user-template:${copy.id}"]`).count(), 0);
  await peer.screenshot({ path: path.join(output, 'other-user-private-isolation.png'), fullPage: true });
});
