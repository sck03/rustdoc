import assert from 'node:assert/strict';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { withOfficeUi } from './lib/native-office-ui.mjs';

await withOfficeUi('permission-navigation-ui', async ({ repo, output, url, invoke, openPage, require, operations }) => {
  const bundle = path.join(output, 'models.mjs');
  const web = path.join(repo, 'apps/export-doc-web');
  await require('esbuild').build({ stdin: { loader: 'ts', resolveDir: web, contents: `
    export { createEmptyInvoice } from './src/features/invoices/invoiceModel.ts';
    export { createEmptyPayment } from './src/features/payments/paymentModel.ts';
    export { filterWorkspaceNavGroups, getWorkspaceRouteItems } from './src/app/workspaceNavigation.ts';
    export { isRouteAccessAllowed } from './src/app/routeAccess.ts';
    export { isRecordInPermissionScope } from './src/app/permissionScope.ts';
  ` }, bundle: true, platform: 'node', format: 'esm', outfile: bundle });
  const model = await import(pathToFileURL(bundle).href);
  const admin = (await invoke('Login', { username: 'oa-review', password: 'Review-2026-Test' })).accessToken;
  const catalog = await invoke('ListUsers', undefined, {}, admin);
  const password = 'Navigation-Review-2026';
  const create = (username, role, permissionGrants) => invoke('createUserAccount', {
    username, fullName: username, role, isActive: true, companyScope: 'DEFAULT', departmentId: 'GENERAL',
    resetPassword: password, permissionTemplateId: null, permissionGrants, disabledModules: [],
  }, {}, admin);
  for (const role of process.argv.includes('--records-only') ? [] : catalog.roles) {
    const username = `nav-${role}`;
    await create(username, role, null);
    const login = await invoke('Login', { username, password });
    const capabilities = { ...login.user.capabilities, isDesktopRuntime: false };
    const groups = model.filterWorkspaceNavGroups(capabilities);
    const page = await openPage(username, password);
    for (const route of model.getWorkspaceRouteItems(groups)) {
      assert(model.isRouteAccessAllowed({ pathname: route.to, user: login.user, canManageSystem: capabilities.canManageSettings, isDesktopRuntime: false }), `${role}: visible route is accessible: ${route.to}`);
    }
    const links = await page.locator('#workspace-primary-navigation a').evaluateAll(items => items.map(item => new URL(item.href).hash.slice(1)));
    const expected = groups.flatMap(group => group.items.map(item => item.to));
    assert.deepEqual([...new Set(links)].sort(), [...new Set(expected)].sort(), `${role}: rendered menu matches server permissions`);
    assert.equal(await page.getByText('当前账号没有此页面权限', { exact: true }).count(), 0, `${role}: useful authorized home`);
    console.log(`${role}: ${links.length} rendered entries and ${model.getWorkspaceRouteItems(groups).length} route permissions verified`);
    await page.context().close();
  }
  const grant = (resourceKey, action, dataScope = 'company') => ({ resourceKey, action, dataScope });
  const permissions = [grant('document.invoices', 'view'), grant('document.invoices', 'manage', 'own'), grant('document.payments', 'view'), grant('document.payments', 'manage', 'own')];
  let account = (await create('nav-scoped', 'User', permissions)).user;
  let login = await invoke('Login', { username: 'nav-scoped', password });
  const ownRecord = { ownerUserId: login.user.id, companyScope: 'DEFAULT', departmentId: 'GENERAL' };
  assert(model.isRecordInPermissionScope('own', login.user, ownRecord));
  assert(!model.isRecordInPermissionScope('own', login.user, { ...ownRecord, companyScope: 'OTHER' }));
  assert(!model.isRecordInPermissionScope('department', login.user, { ...ownRecord, departmentId: 'OTHER' }));
  assert(!model.isRecordInPermissionScope('company', login.user, { ...ownRecord, companyScope: 'OTHER' }));
  const invoice = (await invoke('CreateInvoice', { ...model.createEmptyInvoice(login.user.businessDate), invoiceNo: 'NAV-REVIEW-001', customerNameEN: 'PERMISSION TEST' }, {}, admin)).invoice;
  const payment = (await invoke('CreatePayment', { ...model.createEmptyPayment(login.user.businessDate), voucherNo: 'NAV-PAY-001', payeeName: 'PERMISSION TEST' }, {}, admin)).payment;
  const page = await openPage('nav-scoped', password);
  await page.goto(`${url}/#/invoices/new`);
  await page.getByText('当前页面不可用', { exact: true }).waitFor();
  await page.goto(`${url}/#/invoices/${invoice.id}`);
  await page.locator('[aria-label="客户与出口商"]').waitFor();
  assert(await page.getByRole('button', { name: '保存发票', exact: true }).isDisabled(), 'manage does not grant edit');
  await page.screenshot({ path: path.join(output, 'manage-without-edit.png'), fullPage: true });
  account = (await invoke('updateUserAccount', { ...account, permissionGrants: [...permissions, grant('document.invoices', 'operate', 'own'), grant('document.payments', 'operate', 'own')], expectedVersion: account.versionNumber, resetPassword: '' }, { id: account.id }, admin)).user;
  const current = operations.get('getCurrentUser');
  const revoked = await fetch(url + current.route, { headers: { authorization: `Bearer ${login.accessToken}` } });
  assert.equal(revoked.status, 401, 'old session revoked after permission changes');
  await page.context().close();
  login = await invoke('Login', { username: 'nav-scoped', password });
  const scoped = await openPage('nav-scoped', password);
  await scoped.goto(`${url}/#/invoices/${invoice.id}`);
  await scoped.locator('[aria-label="客户与出口商"]').waitFor();
  assert(await scoped.getByRole('button', { name: '保存发票', exact: true }).isDisabled(), 'own edit cannot edit a colleague invoice');
  const update = operations.get('UpdateInvoice');
  const rejected = await fetch(url + update.route.replace('{id}', String(invoice.id)), { method: update.method, headers: { authorization: `Bearer ${login.accessToken}`, 'content-type': 'application/json' }, body: JSON.stringify(invoice) });
  assert.equal(rejected.status, 403, 'server enforces the same record boundary');
  await scoped.screenshot({ path: path.join(output, 'colleague-invoice-readonly.png'), fullPage: true });
  await scoped.goto(`${url}/#/payments/${payment.id}`);
  await scoped.locator('.entity-form').waitFor();
  assert.equal(await scoped.getByText('参考资料未完整加载', { exact: true }).count(), 0, 'derived lookup permission permits loading payment options');
  const options = await fetch(url + operations.get('ListCustomOptions').route.replace('{optionType}', 'PaymentMethod'), { headers: { authorization: `Bearer ${login.accessToken}` } });
  assert.equal(options.status, 200, 'lookup read permission follows its technical dependency');
  assert(await scoped.locator('.entity-form button[type="submit"]').isDisabled(), 'own edit cannot edit a colleague payment');
  assert.equal(await scoped.getByRole('button', { name: '删除', exact: true }).count(), 0, 'own manage cannot delete a colleague payment');
  await scoped.screenshot({ path: path.join(output, 'colleague-payment-readonly.png'), fullPage: true });
  const ownInvoice = (await invoke('CreateInvoice', { ...model.createEmptyInvoice(login.user.businessDate), invoiceNo: 'NAV-OWN-001' }, {}, login.accessToken)).invoice;
  await scoped.goto(`${url}/#/invoices/${ownInvoice.id}`);
  await scoped.locator('[aria-label="客户与出口商"]').waitFor();
  const save = scoped.getByRole('button', { name: '保存发票', exact: true });
  await scoped.locator('input').filter({ visible: true }).first().waitFor();
  await scoped.getByRole('textbox', { name: /^发票号/u }).fill('NAV-OWN-SAVED');
  const saved = scoped.waitForResponse(response => response.url().endsWith(`/api/invoices/${ownInvoice.id}`) && response.request().method() === 'PUT');
  await save.click();
  assert.equal((await saved).status(), 200, 'own invoice remains editable through React');
  await scoped.getByText('发票已保存。', { exact: true }).waitFor();
  await scoped.screenshot({ path: path.join(output, 'own-invoice-saved.png'), fullPage: true });
  await scoped.goto(`${url}/#/office/approval-settings`);
  await scoped.getByText('当前页面不可用', { exact: true }).waitFor();
});
