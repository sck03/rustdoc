import assert from 'node:assert/strict';
import path from 'node:path';

export async function verifyPaymentWorkflow({ page, url, invoke, operations, token, payment, output }) {
  for (let index = 0; index < 21; index++) {
    await invoke('CreatePayment', { ...payment, id: 0, rowVersion: '', voucherNo: `PAGE-PAY-${index}`, invoiceNo: `REF-${index}` }, {}, token);
  }
  await page.goto(`${url}/#/payments`);
  await page.locator('.page-size-control select').selectOption('20');
  await page.getByRole('button', { name: '下一页', exact: true }).click();
  await page.locator('.pagination-bar').getByText('第 2 / 2 页', { exact: false }).waitFor();
  assert.equal(await page.locator('.payment-table tbody tr').count(), 2, 'placeholder rows cannot reset the requested page');
  await page.getByRole('textbox', { name: '搜索付款报销', exact: true }).fill(payment.voucherNo);
  await page.getByRole('textbox', { name: '搜索付款报销', exact: true }).press('Enter');
  await page.locator('.pagination-bar').getByText('第 1 / 1 页 · 1 条', { exact: true }).waitFor();
  const row = page.locator('.payment-table tbody tr').filter({ hasText: payment.voucherNo });
  await row.getByRole('link', { name: '打印/PDF', exact: true }).press('Enter');
  await page.getByRole('tab', { name: '打印与 PDF', exact: true }).waitFor();
  assert.equal(await page.getByRole('tab', { name: '打印与 PDF', exact: true }).getAttribute('aria-selected'), 'true');
  assert(page.url().endsWith(`payments/${payment.id}?section=report`), 'keyboard output shortcut must not bubble to row navigation');
  const panel = page.locator('[aria-label="付款/报销单预览"]');
  const preview = panel.getByRole('button', { name: '预览', exact: true });
  await preview.click();
  await panel.locator('iframe').waitFor();
  await panel.getByRole('button', { name: '报表模板管理', exact: true }).click();
  await page.getByRole('button', { name: '返回付款/报销单', exact: true }).click();
  assert.equal(await page.getByRole('tab', { name: '打印与 PDF', exact: true }).getAttribute('aria-selected'), 'true', 'template return preserves output section');

  // Deliver a real old preview only after the user has changed the draft.
  const previewPath = operations.get('PreviewPaymentVoucherDraftHtml').route;
  let deliver;
  let received;
  const receivedPromise = new Promise(resolve => { received = resolve; });
  const deliverPromise = new Promise(resolve => { deliver = resolve; });
  const intercept = async route => {
    const response = await route.fetch();
    received();
    await deliverPromise;
    await route.fulfill({ response }).catch(() => {}); // Aborted by the changed source.
  };
  await page.route(`**${previewPath}`, intercept);
  await preview.click();
  await receivedPromise;
  await page.getByRole('tab', { name: '基本信息', exact: true }).click();
  await page.getByRole('textbox', { name: '付款单号', exact: true }).fill('CHANGED-WHILE-PREVIEWING');
  deliver();
  await page.unroute(`**${previewPath}`, intercept);
  await page.getByRole('tab', { name: '打印与 PDF', exact: true }).click();
  assert.equal(await panel.locator('iframe').count(), 0, 'late preview cannot revive stale printable content');
  assert(await panel.getByRole('button', { name: '打印', exact: true }).isDisabled());
  assert(await panel.getByRole('button', { name: '导出 PDF', exact: true }).isDisabled(), 'unsaved draft cannot export a stale saved record');
  const currentPreview = page.waitForResponse(response => response.url().endsWith(previewPath) && response.request().method() === 'POST');
  await preview.click();
  const response = await currentPreview;
  assert.equal(response.request().postDataJSON().payment.voucherNo, 'CHANGED-WHILE-PREVIEWING');
  await panel.locator('iframe').waitFor();
  assert.equal(await panel.locator('iframe').getAttribute('srcdoc'), (await response.json()).html, 'the displayed preview belongs to the current request');
  await page.getByRole('button', { name: '保存付款报销', exact: true }).click();
  await page.getByText('付款报销已保存。', { exact: true }).waitFor();
  assert.equal(await page.getByRole('tab', { name: '打印与 PDF', exact: true }).getAttribute('aria-selected'), 'true');
  await page.screenshot({ path: path.join(output, 'payment-workflow.png'), fullPage: true });
  assert(await panel.getByRole('button', { name: '导出 PDF', exact: true }).isEnabled());
  assert.equal(await panel.getByRole('button', { name: '下载 PDF', exact: true }).count(), 0, 'one PDF action per workflow');
  const catalogUrl = `**${operations.get('ListReportTemplates').route}?*`;
  await page.route(catalogUrl, route => route.fulfill({ json: [] }));
  await panel.getByRole('button', { name: '刷新模板', exact: true }).click();
  await page.waitForFunction(() => document.querySelector('[aria-label="付款/报销单预览"]')?.getAttribute('data-selected-template-path') === '');
  assert(await panel.getByRole('button', { name: '导出 PDF', exact: true }).isDisabled(), 'empty catalog clears the previously selected template');
  assert.equal(await panel.locator('iframe').count(), 0);
  await page.unroute(catalogUrl);
}
