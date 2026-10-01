import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { withOfficeUi } from './lib/native-office-ui.mjs';

await withOfficeUi('payment-printing-ui', async ({ repo, output, url, invoke, openPage, require, operations }) => {
  const bundle = path.join(output, 'payment-model.mjs');
  await require('esbuild').build({ stdin: { loader: 'ts', resolveDir: path.join(repo, 'apps/export-doc-web'), contents: `
    export { createEmptyPayment } from './src/features/payments/paymentModel.ts';
    export { buildPaymentTemplateViews } from './src/features/payments/paymentReportTemplates.ts';
  ` }, bundle: true, platform: 'node', format: 'esm', outfile: bundle });
  const model = await import(pathToFileURL(bundle).href);
  const admin = (await invoke('Login', { username: 'oa-review', password: 'Review-2026-Test' })).accessToken;
  const password = 'Payment-Review-2026';
  for (const [username, role] of [['pay-owner', 'OfficeEmployee'], ['pay-peer', 'OfficeEmployee'], ['pay-finance', 'Finance']]) {
    await invoke('createUserAccount', { username, fullName: username, role, isActive: true,
      companyScope: 'DEFAULT', departmentId: 'GENERAL', resetPassword: password, permissionGrants: null }, {}, admin);
  }
  const owner = await invoke('Login', { username: 'pay-owner', password });
  const peer = await invoke('Login', { username: 'pay-peer', password });
  const finance = await invoke('Login', { username: 'pay-finance', password });
  const request = (id, token, parameters = {}, query = '', body) => {
    const op = operations.get(id);
    return fetch(url + op.route.replace(/\{([^}]+)\}/gu, (_, key) => encodeURIComponent(parameters[key])) + query,
      { method: op.method, headers: { authorization: `Bearer ${token}`, 'content-type': 'application/json' }, ...(body ? { body: JSON.stringify(body) } : {}) });
  };
  for (const session of [owner, finance]) {
    for (const id of ['ListInvoices', 'GetReportTemplateContent', 'GetReportTemplateFieldCatalog', 'ListUserReportTemplates']) {
      assert.equal((await request(id, session.accessToken, {}, '?reportType=ExportDocument')).status, 403, `${id}: export domain denied`);
    }
  }
  const page = await openPage('pay-owner', password);
  await page.waitForURL('**/#/office/approvals');
  await page.getByRole('link', { name: '报表模板管理', exact: true }).waitFor();
  const navigation = await page.locator('#workspace-primary-navigation').innerText();
  assert(navigation.includes('付款报销') && navigation.includes('报表模板管理'));
  assert(!navigation.includes('单证与申报') && !navigation.includes('发票管理'));
  await page.goto(`${url}/#/reports/templates/manage`);
  await page.locator('.template-select-field select').waitFor();
  assert.equal(await page.locator('option[value="ExportDocument"]').count(), 0);
  assert.equal(await page.getByLabel('新建模板', { exact: true }).count(), 0, 'Public files stay protected');
  await page.locator('details[aria-label="我的和共享模板"] > summary').click();
  await page.getByLabel('新模板名称', { exact: true }).fill('我的报销票据');
  const cloning = page.waitForResponse(response => response.url().endsWith('/api/reports/user-templates/clone') && response.request().method() === 'POST');
  await page.getByRole('button', { name: '复制当前模板', exact: true }).click();
  const copy = await (await cloning).json();
  assert.equal(copy.reportType, 'PaymentVoucher');
  assert.equal(copy.shareScope, 'Private');
  await page.getByRole('button', { name: '打开设计器', exact: true }).click();
  const first = JSON.parse(copy.contentHtml).layers.flatMap(layer => layer.elements).find(element => element.type === 'Text');
  const element = page.locator(`[data-v3-element-id="${first.id}"]`);
  await element.waitFor(); await element.click(); await element.press('ArrowRight');
  const saving = page.waitForResponse(response => response.url().includes(`/api/reports/user-templates/${copy.id}`) && response.request().method() === 'PUT');
  await page.getByRole('button', { name: '保存', exact: true }).click();
  const saved = await (await saving).json();
  assert.equal(saved.status, 'Draft'); assert.notEqual(saved.contentHtml, copy.contentHtml);
  await page.getByText('私人模板已保存，可直接用于自己的打印和导出，无需发布。', { exact: true }).waitFor();
  await page.screenshot({ path: path.join(output, 'employee-payment-designer.png'), fullPage: true });
  assert.equal((await request('GetUserReportTemplate', peer.accessToken, { id: copy.id })).status, 403);
  assert.equal((await request('PublishUserReportTemplate', owner.accessToken, { id: copy.id }, '', { expectedVersion: saved.versionNumber })).status, 403);
  const payment = (await invoke('CreatePayment', { ...model.createEmptyPayment(owner.user.businessDate),
    voucherNo: 'STAFF-PAY-001', payeeName: '员工报销验收', cnyAmount: 123.45, otherExpense: 123.45, notes: '本人付款报销打印' }, {}, owner.accessToken)).payment;
  assert.equal((await request('GetPayment', peer.accessToken, { id: payment.id })).status, 403);
  assert.equal((await request('GetPayment', finance.accessToken, { id: payment.id })).status, 200);
  assert.equal((await request('UpdatePayment', finance.accessToken, { id: payment.id }, '', payment)).status, 403);
  await page.goto(`${url}/#/payments/${payment.id}`);
  await page.getByRole('tab', { name: '预览与导出', exact: true }).click();
  const preview = page.locator('[aria-label="付款/报销单预览"]');
  await preview.getByRole('combobox', { name: '模板', exact: true }).selectOption(`user-template:${copy.id}`);
  await preview.getByRole('button', { name: '预览', exact: true }).click();
  await page.waitForFunction(() => document.querySelector('iframe[title="付款/报销单 HTML 预览"]')?.srcdoc.includes('<svg'));
  assert(await preview.getByRole('button', { name: '打印', exact: true }).isEnabled());
  const download = page.waitForEvent('download');
  await preview.getByRole('button', { name: '导出 PDF', exact: true }).click();
  const file = await download;
  assert(file.suggestedFilename().includes('STAFF-PAY-001'), file.suggestedFilename());
  await file.saveAs(path.join(output, 'employee-payment.pdf'));
  assert(fs.readFileSync(path.join(output, 'employee-payment.pdf')).subarray(0, 5).equals(Buffer.from('%PDF-')));
  await page.screenshot({ path: path.join(output, 'employee-payment-preview.png'), fullPage: true });
  await preview.getByRole('combobox', { name: '模板', exact: true }).selectOption('Templates/Internal/expense_reimbursement_template.dtpl');
  const expenseDownload = page.waitForEvent('download');
  await preview.getByRole('button', { name: '导出 PDF', exact: true }).click();
  await (await expenseDownload).saveAs(path.join(output, 'employee-expense.pdf'));
  // Output-only custom users do not inherit design permissions or colleagues' jobs.
  const direct = [ ['document.payments', 'view'], ['document.payment-output', 'export-pdf'] ]
    .map(([resourceKey, action]) => ({ resourceKey, action, dataScope: 'company' }));
  await invoke('createUserAccount', { username: 'pay-output', fullName: '仅输出', role: 'OfficeEmployee', isActive: true,
    companyScope: 'DEFAULT', departmentId: 'GENERAL', resetPassword: password, permissionGrants: direct }, {}, admin);
  const outputUser = await invoke('Login', { username: 'pay-output', password });
  assert.equal((await request('GetReportTemplateContent', outputUser.accessToken, {}, '?reportType=PaymentVoucher')).status, 403);
  const catalog = await (await request('ListReportTemplates', outputUser.accessToken, {}, '?reportType=PaymentVoucher')).json();
  assert(catalog.every(template => template.reportType === 'PaymentVoucher'));
  const ownJobs = await (await request('ListJobs', outputUser.accessToken)).json();
  assert.equal(ownJobs.totalCount, 0);
  assert.equal((await request('SaveCustomOption', outputUser.accessToken, { optionType: 'PaymentMethod' }, '', { value: '不应写入' })).status, 403);
  const views = model.buildPaymentTemplateViews([{ templatePath: 'user:Internal/same.dtpl', displayName: '有效模板' }],
    { paymentTemplates: [{ templatePath: 'builtin:Internal/same.dtpl', name: '失效配置', isEnabled: false, reportType: 'PaymentVoucher' }] });
  assert.equal(views[0].displayName, '有效模板', 'A missing template must not rename or disable another file with the same name');
  assert.equal(model.buildPaymentTemplateViews([{ templatePath: 'Templates/Internal/payment.dtpl', displayName: '内置' }],
    { paymentTemplates: [{ templatePath: 'builtin:Internal/payment.dtpl', name: '已配置', isEnabled: true, reportType: 'PaymentVoucher' }] })[0].displayName, '已配置');
});
