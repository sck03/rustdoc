import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import { captureScreenshot } from './web-runtime-browser-session.mjs';
import { waitFor } from './web-runtime-smoke-common.mjs';

export async function verifyDesktopConditionalReports({ run, request, token, cdp, output }) {
  const repo = path.resolve(import.meta.dirname, '../..');
  const web = path.join(repo, 'apps/export-doc-web');
  const bundle = path.join(output, 'conditional-invoice-model.mjs');
  const require = createRequire(path.join(web, 'package.json'));
  await require('esbuild').build({ stdin: { resolveDir: web, contents: "export {createEmptyInvoice} from './src/features/invoices/invoiceModel.ts'; export {createEmptyInvoiceItem} from './src/features/invoices/invoiceItemsEditorModel.ts';" }, bundle: true, platform: 'node', format: 'esm', outfile: bundle, logLevel: 'silent' });
  const { createEmptyInvoice, createEmptyInvoiceItem } = await import(pathToFileURL(bundle).href);
  const wait = expression => waitFor(() => run(`Boolean(${expression})`), 30000, 'Desktop conditional report timed out: ' + expression);
  const settle = () => run('new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve(true))))');
  const catalog = (await request('ListReportTemplates', token)).body;
  const fileTemplate = name => { const selected = catalog.find(item => item.templatePath.endsWith(name)); assert(selected, name); return selected.templatePath; };
  const seal = await request('SaveInvoiceSealImage', token, { imageDataUrl: 'data:image/png;base64,' + fs.readFileSync(path.join(repo, 'tests/ReportTemplateFixtures/sample-seal.png')).toString('base64') });
  assert.equal(seal.status, 200);
  const draft = { ...createEmptyInvoice('2026-10-10'), invoiceNo: 'CONDITION-DESKTOP-001', exporterNameEN: 'SAMPLE EXPORT COMPANY', customerNameEN: 'SAMPLE CUSTOMER', currency: 'USD', docSealPath: seal.body.imagePath, customsSealPath: seal.body.imagePath };
  draft.items = [1, 2, 3].map(n => ({ ...createEmptyInvoiceItem(), styleNo: `STYLE-${n}`, styleName: `Sample product ${n}`, quantity: 100 * n, cartons: 10 * n, unitEN: 'PCS', ctnUnitEN: 'CTNS', unitPrice: 12, totalPrice: 1200 * n }));
  const created = await request('CreateInvoice', token, draft);
  assert.equal(created.status, 201);
  const invoiceId = created.body.id;
  await run(`location.hash='#/invoices/${invoiceId}'; true`);
  await wait("document.querySelector('[aria-label=\"客户与出口商\"]')");
  await run("[...document.querySelectorAll('button')].find(e=>e.textContent.trim().startsWith('商品明细')).click(); true");
  await wait("document.querySelector('.invoice-special-terms-field textarea')");
  await run("document.querySelector('.invoice-items-support-details').open=true; document.querySelector('.invoice-special-terms-field textarea').focus(); true");
  await cdp.send('Input.insertText', { text: 'DELIVERY BY APPOINTMENT\nKEEP DRY\n备注：仅作模板验收' });
  await run("[...document.querySelectorAll('button')].find(e=>e.textContent.trim()==='保存发票').click(); true");
  await wait("document.body.innerText.includes('发票已保存')");
  assert.equal((await request('GetInvoice', token, undefined, { id: invoiceId })).body.specialTerms, 'DELIVERY BY APPOINTMENT\nKEEP DRY\n备注：仅作模板验收');
  await captureScreenshot(cdp, path.join(output, 'Full-special-terms-editor.png'));
  const cloned = await request('CloneUserReportTemplate', token, { reportType: 'ExportDocument', name: '桌面条件发票', sourceTemplatePath: fileTemplate('invoice_template.dtpl') });
  assert.equal(cloned.status, 201);
  await run(`location.hash='#/reports/templates?reportType=ExportDocument&userTemplateId=${cloned.body.id}&invoiceId=${invoiceId}'; true`);
  await wait("document.querySelector('.report-designer-v3-header h2')?.textContent==='桌面条件发票'");
  await wait("document.querySelector('[data-v3-element-id=special-terms]')");
  await settle();
  await run("document.querySelector('[data-v3-element-id=special-terms]').focus(); true");
  await cdp.send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Enter', windowsVirtualKeyCode: 13 });
  await cdp.send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Enter', windowsVirtualKeyCode: 13 });
  await wait("document.querySelector('.new-report-conditional-properties')");
  await run("[...document.querySelectorAll('.new-report-conditional-properties button')].find(e=>e.textContent.trim()==='添加条件').click(); true");
  await wait("document.querySelector('[aria-label=\"条件 2\"]')");
  const rule = `document.querySelector('[aria-label="条件 2"]')`;
  for (const [label, value] of [['条件字段', 'Invoice.Currency'], ['判断', 'Equals']]) {
    await run(`(()=>{const e=[...${rule}.querySelectorAll('label')].find(e=>e.firstElementChild?.textContent===${JSON.stringify(label)}).querySelector('select');e.value=${JSON.stringify(value)};e.dispatchEvent(new Event('change',{bubbles:true}));return true})()`);
    await settle();
  }
  await run(`(()=>{const e=[...${rule}.querySelectorAll('label')].find(e=>e.firstElementChild?.textContent==='比较值').querySelector('input');e.focus();e.select();return true})()`);
  await cdp.send('Input.insertText', { text: 'USD' });
  await settle();
  await cdp.send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Enter', windowsVirtualKeyCode: 13 });
  await cdp.send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Enter', windowsVirtualKeyCode: 13 });
  await settle();
  await wait("[...document.querySelectorAll('button')].some(e=>e.textContent.trim()==='保存'&&!e.disabled)");
  await run("[...document.querySelectorAll('button')].find(e=>e.textContent.trim()==='保存'&&!e.disabled).click(); true");
  await wait("document.body.innerText.includes('私人模板已保存')");
  const saved = await request('GetUserReportTemplate', token, undefined, { id: cloned.body.id });
  const condition = JSON.parse(saved.body.contentHtml).layers.flatMap(layer => layer.elements).find(element => element.id === 'special-terms').block;
  assert.equal(condition.additionalConditions[0].fieldPath, 'Invoice.Currency');
  assert.equal(condition.additionalConditions[0].value, 'USD', JSON.stringify(saved.body));
  await captureScreenshot(cdp, path.join(output, 'Full-conditional-designer.png'));
  const preview = await request('PreviewInvoiceReportHtml', token, { reportType: 'ExportDocument', templatePath: `user-template:${cloned.body.id}`, withSeal: true }, { invoiceId });
  assert.equal(preview.status, 200);
  assert.match(preview.body.html, /Special Terms: DELIVERY BY APPOINTMENT/);
  assert.match(preview.body.html, /备注：仅作模板验收/);
  fs.writeFileSync(path.join(output, 'desktop-conditional-preview.html'), preview.body.html);
  for (const [name, templatePath] of [
    ['invoice', `user-template:${cloned.body.id}`], ['packing', fileTemplate('packing_list_template.dtpl')],
    ['contract', fileTemplate('contract_template.dtpl')], ['customs', fileTemplate('customs_declaration_template.dtpl')],
  ]) {
    const destinationPath = path.join(output, `desktop-${name}.pdf`);
    const job = await request('StartInvoiceReportPdfSaveToPathJob', token, { reportType: 'ExportDocument', templatePath, withSeal: true, destinationPath }, { invoiceId });
    assert.equal(job.status, 202, JSON.stringify(job.body));
    await waitFor(() => fs.existsSync(destinationPath), 30000, `Desktop ${name} PDF missing`);
    assert.equal(fs.readFileSync(destinationPath).subarray(0, 5).toString(), '%PDF-');
  }
  const blank = await request('CreateInvoice', token, { ...draft, invoiceNo: 'COND-EMPTY-001', specialTerms: ' \n　' });
  assert.equal(blank.status, 201);
  const emptyPreview = await request('PreviewInvoiceReportHtml', token, { reportType: 'ExportDocument', templatePath: fileTemplate('invoice_template.dtpl') }, { invoiceId: blank.body.id });
  assert.equal(emptyPreview.status, 200, JSON.stringify(emptyPreview.body));
  assert.doesNotMatch(emptyPreview.body.html, /Special Terms/);
  await run("location.hash='#/tools/email'; true");
  await wait("document.querySelector('.workspace-header h1')?.textContent==='邮件中心'");
  assert.equal(await run("document.querySelector('[data-nav-group=workspace]').getAttribute('aria-expanded')"), 'true');
  await captureScreenshot(cdp, path.join(output, 'Full-email-workspace.png'));
}
