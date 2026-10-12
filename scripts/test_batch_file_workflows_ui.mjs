import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { withOfficeUi } from './lib/native-office-ui.mjs';
import { defaultReportDesigns } from './lib/default-report-designs.mjs';
import { rebuildDesignerDocument } from './lib/report-designer-rebuild.mjs';

await withOfficeUi('batch-file-workflows-ui', async ({ repo, output, url, invoke, openPage, require, operations }) => {
  const bundle = path.join(output, 'models.mjs');
  await require('esbuild').build({ stdin: { loader: 'ts', resolveDir: path.join(repo, 'apps/export-doc-web'), contents: `
    export {createEmptyInvoice} from './src/features/invoices/invoiceModel.ts';
    export {createEmptyInvoiceItem} from './src/features/invoices/invoiceItemsEditorModel.ts';
    export * from './src/features/report-designer/reportDesignerV3Mutations.ts';
    export * from './src/features/report-designer/reportDesignerV3Layers.ts';
    export * from './src/features/report-designer/reportDesignerV3TemplateParser.ts';
    export * from './src/features/report-designer/reportDesignerV3Validation.ts';
    export * from './src/features/report-designer/reportDesignerGridMutations.ts';
    export * from './src/features/report-designer/reportDesignerTableMutations.ts';
    export * from './src/features/report-designer/reportDesignerBlockFactories.ts';
    export * from './src/features/report-designer/reportDesignerBlockCopy.ts';
  ` }, bundle: true, platform: 'node', format: 'esm', outfile: bundle, logLevel: 'silent' });
  const model = await import(pathToFileURL(bundle).href);
  const session = await invoke('Login', { username: 'oa-review', password: 'Review-2026-Test' });
  const token = session.accessToken;
  const route = (operation, parameters = {}) => operations.get(operation).route.replace(/\{([^}]+)\}/gu, (_, key) => encodeURIComponent(parameters[key]));
  const request = (operation, body, parameters = {}, authenticated = true) => fetch(url + route(operation, parameters), {
    method: operations.get(operation).method, signal: AbortSignal.timeout(30000),
    headers: { ...(authenticated ? { authorization: `Bearer ${token}` } : {}), ...(body ? { 'content-type': 'application/json' } : {}) },
    ...(body ? { body: JSON.stringify(body) } : {}),
  });
  const draft = { ...model.createEmptyInvoice(session.user.businessDate), invoiceNo: 'BATCH-001', exporterNameEN: 'SAMPLE EXPORTER', customerNameEN: 'SAMPLE CUSTOMER', currency: 'USD' };
  draft.items = [{ ...model.createEmptyInvoiceItem(), styleNo: 'STYLE-01', styleName: 'Sample product', quantity: 20, cartons: 2, unitEN: 'PCS', ctnUnitEN: 'CTNS', unitPrice: 5, totalPrice: 100 }];
  const invoice = await invoke('CreateInvoice', draft, {}, token);
  const templates = await invoke('ListReportTemplates', undefined, {}, token);
  const fixed = model.parseReportDesignerV3Source('', 'ExportDocument').schema;
  const grid = model.applyGridPreset(model.createGridBlock(), 'Blank');
  const cellId = grid.rows[0].cells[0].id;
  const invalidGrid = model.updateGridCell(grid, cellId, cell => ({ ...cell, contentKind: 'Field', fieldPath: 'item.Quantity' }));
  fixed.layers.find(layer => layer.role === 'Body').elements = [model.createV3FlowElement(invalidGrid)];
  for (const operation of ['CreateUserReportTemplate', 'PreviewReportTemplateContent']) {
    const content = JSON.stringify(fixed);
    const response = await request(operation, operation === 'CreateUserReportTemplate'
      ? { reportType: 'ExportDocument', name: '固定区域非法商品字段', contentHtml: content }
      : { reportType: 'ExportDocument', content, sampleProfile: 'apiSample', withSeal: false });
    assert.equal(response.status, 400, `${operation} must reject a missing item context instead of printing blank content`);
    assert((await response.text()).includes('商品循环'));
  }
  fixed.layers.find(layer => layer.role === 'Body').elements = [model.createV3FlowElement(
    model.updateGridCell(grid, cellId, cell => ({ ...cell, contentKind: 'Field', fieldPath: 'Invoice.InvoiceNo' })))];
  const fixedTemplate = await invoke('CreateUserReportTemplate', { reportType: 'ExportDocument', name: '通用固定字段表格', contentHtml: JSON.stringify(fixed) }, {}, token);
  const fixedPreview = await invoke('PreviewReportTemplateContent', { reportType: 'ExportDocument', content: fixedTemplate.contentHtml, sampleProfile: 'apiSample', withSeal: false }, {}, token);
  assert(fixedPreview.html.includes('<svg'), 'a valid document field still previews with the native renderer');
  const rebuilt = [];
  for (const [name, reference] of defaultReportDesigns()) {
    const content = JSON.stringify(rebuildDesignerDocument(model, reference));
    const profiles = reference.reportType === 'ExportDocument' ? ['apiSample', 'exportLongItems'] : ['paymentVoucher'];
    for (const sampleProfile of process.argv.includes('--batch-only') ? [] : profiles) {
      const original = await invoke('PreviewReportTemplateContent', { reportType: reference.reportType, content: JSON.stringify(reference), sampleProfile, withSeal: false }, {}, token);
      const recreated = await invoke('PreviewReportTemplateContent', { reportType: reference.reportType, content, sampleProfile, withSeal: false }, {}, token);
      fs.writeFileSync(path.join(output, `${path.basename(name, '.dtpl')}-${sampleProfile}.html`), recreated.html);
      if (recreated.html !== original.html) {
        fs.writeFileSync(path.join(output, `${path.basename(name, '.dtpl')}-${sampleProfile}-reference.html`), original.html);
        let index = 0;
        while (index < Math.min(recreated.html.length, original.html.length) && recreated.html[index] === original.html[index]) index++;
        throw new Error(`${name}/${sampleProfile}: native layout differs at ${index}; rebuilt ${recreated.html.slice(Math.max(0,index-80),index+100)}; reference ${original.html.slice(Math.max(0,index-80),index+100)}; ${output}`);
      }
    }
    const created = await invoke('CreateUserReportTemplate', { reportType: reference.reportType, name: `空白重建·${path.basename(name, '.dtpl')}`, contentHtml: content }, {}, token);
    rebuilt.push({ name, reportType: reference.reportType, path: `user-template:${created.id}` });
    fs.writeFileSync(path.join(output, path.basename(name, '.dtpl') + '.json'), content);
    console.log(`${process.argv.includes('--batch-only') ? 'Prepared reconstruction' : 'Blank reconstruction and native preview matched'}: ${name} (${profiles.join(', ')})`);
  }
  const selected = ['invoice_template.dtpl', 'packing_list_template.dtpl', 'customs_declaration_template.dtpl', 'contract_template.dtpl'].map(name => {
    const template = templates.find(template => template.templatePath.endsWith(name)); assert(template, name); return template.templatePath;
  });
  selected.push(rebuilt.find(template => template.name.endsWith('/invoice_template.dtpl')).path);
  const capabilities = await invoke('GetReportTemplateV3Contract', undefined, {}, token);
  assert.equal(capabilities.page.size, 'A4');
  assert.equal(capabilities.batchLimits.maxReports, 200);
  assert(capabilities.page.presets.some(preset => preset.size === 'A5'));
  const page = await openPage('oa-review', 'Review-2026-Test');
  await page.waitForURL('**/#/dashboard');
  await page.evaluate(() => { location.hash = '#/invoices'; });
  await page.getByLabel('选择发票 BATCH-001', { exact: true }).check();
  await page.getByRole('button', { name: '生成批量报表', exact: true }).click();
  for (const template of selected) await page.locator(`.report-template-checklist input[value="${template}"]`).check();
  await page.getByText('1 张发票 × 5 种模板 = 5 份 PDF', { exact: true }).waitFor();
  const batchPanel = await page.locator('.invoice-batch-report').elementHandle();
  await page.setViewportSize({ width: 390, height: 844 });
  await page.locator('.invoice-batch-report').waitFor({ state: 'hidden' });
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.getByText('1 张发票 × 5 种模板 = 5 份 PDF', { exact: true }).waitFor();
  assert(await batchPanel.evaluate(node => node.isConnected), 'responsive capability changes must retain the batch selection');
  await page.screenshot({ path: path.join(output, 'invoice-multiple-templates.png') });
  const [download] = await Promise.all([page.waitForEvent('download', { timeout: 120000 }), page.getByRole('button', { name: '生成并下载', exact: true }).click()]);
  const zipPath = path.join(output, 'multiple-report-templates.zip');
  await download.saveAs(zipPath);
  const bytes = fs.readFileSync(zipPath);
  const directoryEnd = bytes.lastIndexOf(Buffer.from([0x50, 0x4b, 0x05, 0x06]));
  assert(directoryEnd >= 0 && directoryEnd + 22 <= bytes.length, 'the downloaded file is a complete ZIP');
  assert.equal(bytes.readUInt16LE(directoryEnd + 10), 5, 'one invoice supports more than three templates, including a custom design');
  const batch = { invoiceIds: [invoice.id], templatePaths: selected, reportType: 'ExportDocument', withSeal: false };
  assert.equal((await request('StartInvoiceReportPdfZipDownloadJob', batch, {}, false)).status, 401);
  assert.equal((await request('StartInvoiceReportPdfZipDownloadJob', { ...batch, templatePaths: [] })).status, 400);
  assert.equal((await request('StartInvoiceReportPdfZipDownloadJob', { ...batch, invoiceIds: Array.from({ length: 101 }, (_, index) => index + 1) })).status, 400);
  await invoke('createUserAccount', { username: 'batch-peer', fullName: '模板隔离回归', role: 'Admin', isActive: true, companyScope: 'DEFAULT', departmentId: 'GENERAL', resetPassword: 'Batch-Peer-2026' }, {}, token);
  const peer = await invoke('Login', { username: 'batch-peer', password: 'Batch-Peer-2026' });
  const privateTemplate = await invoke('CreateUserReportTemplate', { reportType: 'ExportDocument', name: '另一人的私人模板', contentHtml: '' }, {}, peer.accessToken);
  assert.equal((await request('StartInvoiceReportPdfZipDownloadJob', { ...batch, templatePaths: [selected[0], `user-template:${privateTemplate.id}`] })).status, 409, 'an administrator can inspect another owner’s private template, but cannot output it or create a partial batch');

  async function saveJob(job, name) {
    const deadline = Date.now() + 90000;
    while (!['Succeeded', 'Failed', 'Canceled'].includes(job.status) && Date.now() < deadline) {
      await new Promise(resolve => setTimeout(resolve, 250));
      job = await invoke('GetJob', undefined, { jobId: job.jobId }, token);
    }
    assert.equal(job.status, 'Succeeded', job.errorMessage || job.detailText);
    const response = await request('DownloadJobResult', undefined, { jobId: job.jobId }); assert.equal(response.status, 200);
    fs.writeFileSync(path.join(output, name), Buffer.from(await response.arrayBuffer()));
  }
  await saveJob(await invoke('StartInvoiceReportPdfZipDownloadJob', { ...batch, templatePaths: rebuilt.filter(template => template.reportType === 'ExportDocument').map(template => template.path) }, {}, token), 'rebuilt-export-reports.zip');
  if (!process.argv.includes('--batch-only')) {
    const longInvoice = await invoke('CreateInvoice', { ...draft, invoiceNo: 'BATCH-LONG-001',
      items: Array.from({ length: 72 }, (_, index) => ({ ...draft.items[0], poNumber: `PO-${index + 1}`,
        styleNo: `STYLE-${String(index + 1).padStart(3, '0')}`, styleName: 'Sample product / 多行商品说明' })) }, {}, token);
    await saveJob(await invoke('StartInvoiceReportPdfZipDownloadJob', { ...batch, invoiceIds: [longInvoice.id],
      templatePaths: rebuilt.filter(template => template.reportType === 'ExportDocument').map(template => template.path) }, {}, token), 'rebuilt-long-export-reports.zip');
  }
  const payment = await invoke('CreatePayment', { voucherNo: 'REBUILD-PAY-001', payeeName: '示例收款单位', payerName: '示例支付单位', paymentDate: session.user.businessDate, paymentMethod: '电汇', cnyAmount: 128.5, otherExpense: 128.5 }, {}, token);
  for (const template of rebuilt.filter(template => template.reportType === 'PaymentVoucher')) {
    await saveJob(await invoke('StartPaymentVoucherPdfDownloadJob', { templatePath: template.path }, { paymentId: payment.payment.id }, token), path.basename(template.name, '.dtpl') + '.pdf');
  }

  const category = await invoke('CreateBusinessAttachmentCategory', { name: '批量交付资料', companyScope: session.user.companyScope, expectedVersion: 0 }, {}, token);
  const files = ['交付说明.txt', '包装要求.txt'].map((name, index) => {
    const file = path.join(output, name); fs.writeFileSync(file, `匿名附件 ${index + 1}\n精确字节回读`); return file;
  });
  await page.evaluate(id => { location.hash = `#/invoices/${id}/attachments`; }, invoice.id);
  await page.getByRole('button', { name: '上传资料', exact: true }).click();
  const form = page.getByRole('form', { name: '上传业务资料', exact: true });
  await form.locator('input[type=file]').setInputFiles(files);
  await form.getByRole('combobox').selectOption(String(category.id));
  await form.getByText('待保存 2 个', { exact: true }).waitFor();
  await page.screenshot({ path: path.join(output, 'business-attachments-multiple.png'), fullPage: true });
  await form.getByRole('button', { name: '上传并保留版本（2）', exact: true }).click();
  await form.waitFor({ state: 'hidden' });
  const list = await (await fetch(url + route('ListBusinessAttachments') + `?invoiceId=${invoice.id}`, { headers: { authorization: `Bearer ${token}` }, signal: AbortSignal.timeout(30000) })).json();
  assert.equal(list.page.totalCount, 2);
  for (const record of list.page.items) {
    assert.equal(record.latestRevision, 1, 'multiple new files create distinct records');
    const response = await request('DownloadBusinessAttachment', undefined, { id: record.id, revision: 1 });
    assert.equal(response.status, 200);
    const original = files.find(file => path.parse(file).name === record.title); assert(original);
    assert.deepEqual(Buffer.from(await response.arrayBuffer()), fs.readFileSync(original));
  }
  await page.screenshot({ path: path.join(output, 'business-attachments-saved.png'), fullPage: true });
  fs.writeFileSync(path.join(output, 'summary.json'), JSON.stringify({ invoiceId: invoice.id, templatePaths: selected, pdfCount: 5, rebuiltTemplates: rebuilt, attachmentCount: 2, privateTemplateDenied: true }, null, 2));
});
