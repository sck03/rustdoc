import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import { captureScreenshot } from './web-runtime-browser-session.mjs';
import { waitFor } from './web-runtime-smoke-common.mjs';
import { verifyEntryTemplateDesigner } from './native-entry-template-designer.mjs';

export async function verifyDesktopEntryRows({ run, request, token, cdp, output }) {
  const repo = path.resolve(import.meta.dirname, '../..');
  const web = path.join(repo, 'apps/export-doc-web');
  const require = createRequire(path.join(web, 'package.json'));
  const bundle = path.join(output, 'entry-print-model.mjs');
  await require('esbuild').build({ stdin: { resolveDir: web, contents: "export {createEmptyInvoice} from './src/features/invoices/invoiceModel.ts'; export {createEmptyInvoiceItem} from './src/features/invoices/invoiceItemsEditorModel.ts';" }, bundle: true, platform: 'node', format: 'esm', outfile: bundle, logLevel: 'silent' });
  const { createEmptyInvoice, createEmptyInvoiceItem } = await import(pathToFileURL(bundle).href);
  const wait = expression => waitFor(() => run(`Boolean(${expression})`), 30000, expression);
  const screenshot = async name => {
    await run("document.getElementById('invoice-editor-navigation').scrollIntoView({block:'start'}); true");
    await captureScreenshot(cdp, path.join(output, name), { captureBeyondViewport: false });
  };
  const click = async label => {
    const expression = `[...document.querySelectorAll('button')].find(e=>e.textContent.trim()===${JSON.stringify(label)}&&!e.closest('[hidden]'))`;
    await wait(expression); await run(`${expression}.click(); true`);
  };
  const edit = async (line, text) => {
    const expression = `document.querySelector('[aria-label="打印第 ${line} 行描述"]')`;
    await wait(expression);
    await run(`${expression}.focus(); ${expression}.select(); true`);
    if (text) await cdp.send('Input.insertText', { text });
    else {
      await cdp.send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Backspace', windowsVirtualKeyCode: 8 });
      await cdp.send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Backspace', windowsVirtualKeyCode: 8 });
    }
    await wait(`${expression}.value===${JSON.stringify(text)}`);
  };
  const draft = { ...createEmptyInvoice('2026-10-19'), invoiceNo: 'ENTRY-DESKTOP-001', exporterNameEN: 'SAMPLE EXPORT COMPANY', customerNameEN: 'SAMPLE CUSTOMER', currency: 'USD' };
  draft.items = [[331, 7944, 73849], [185, 3330, 40293]].map(([cartons, quantity, totalPrice]) => ({ ...createEmptyInvoiceItem(), styleName: 'INTERNAL PRODUCT', cartons, quantity, totalPrice, priceCalculationMode: 'LineAmountDriven', unitEN: 'PCS', ctnUnitEN: 'CTNS' }));
  const created = await request('CreateInvoice', token, draft);
  assert.equal(created.status, 201, JSON.stringify(created.body));
  const invoiceId = created.body.id;
  await run(`location.hash='#/invoices/${invoiceId}?section=items'; true`);
  await wait("document.querySelector('[aria-label=\"商品明细\"]')");
  await run("(()=>{const n=document.querySelector('[aria-label=\"明细打印方式\"]');n.value='entries';n.dispatchEvent(new Event('change',{bubbles:true}));return true})()");
  await edit(1, 'MEN SWEATERS');
  await click('描述移为上方说明');
  await wait("document.querySelector('[aria-label=\"打印第 2 行描述\"]').value===''");
  await click('撤销');
  await wait("document.querySelectorAll('[aria-label=\"按录入行打印明细\"] tbody tr').length===2");
  await click('重做');
  await wait("document.querySelectorAll('[aria-label=\"按录入行打印明细\"] tbody tr').length===3");
  await run("document.querySelector('[aria-label=\"选择打印第 2 行\"]').click(); true");
  await click('下方插入空白行');
  await edit(4, '');
  await click('撤销');
  await wait("document.querySelector('[aria-label=\"打印第 4 行描述\"]').value==='INTERNAL PRODUCT'");
  await click('重做');
  await wait("document.querySelector('[aria-label=\"打印第 4 行描述\"]').value===''");
  await screenshot('Full-entry-print-rows.png');
  await click('保存发票');
  await waitFor(async () => (await request('GetInvoice', token, undefined, { id: invoiceId })).body.useEntryPrintRows === true, 30000, 'Entry layout was not saved');
  const saved = (await request('GetInvoice', token, undefined, { id: invoiceId })).body;
  assert.deepEqual(saved.items.map(item => item.printRows.map(({ kind, text }) => ({ kind, text }))), [
    [{ kind: 'Text', text: 'MEN SWEATERS' }, { kind: 'Item', text: '' }, { kind: 'Blank', text: '' }], [{ kind: 'Item', text: '' }],
  ]);
  assert.equal(saved.totalAmount, 114142);
  assert.equal(saved.totalCartons, 516);
  await run('location.reload(); true');
  await wait("document.querySelector('[aria-label=\"按录入行打印明细\"]')");
  assert.equal(await run("document.querySelectorAll('[aria-label=\"按录入行打印明细\"] tbody tr').length"), 4);
  await run("document.querySelector('[aria-label=\"打印第 4 行描述\"]').focus(); true");
  await cdp.send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Enter', windowsVirtualKeyCode: 13, modifiers: 2 });
  await cdp.send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Enter', windowsVirtualKeyCode: 13, modifiers: 2 });
  await wait("document.activeElement.getAttribute('aria-label')==='打印第 5 行描述'");
  await click('撤销');
  await wait("document.querySelectorAll('[aria-label=\"按录入行打印明细\"] tbody tr').length===4");
  await click('下方插入说明');
  await wait("document.activeElement.getAttribute('aria-label')==='打印第 5 行描述'");
  await click('删除说明 / 空白行');
  await wait("document.querySelectorAll('[aria-label=\"按录入行打印明细\"] tbody tr').length===4");
  const catalog = (await request('ListReportTemplates', token)).body;
  for (const name of ['invoice', 'packing_list']) {
    const templatePath = catalog.find(item => item.templatePath.endsWith(`${name}_template.dtpl`))?.templatePath;
    assert(templatePath);
    const preview = await request('PreviewInvoiceReportHtml', token, { reportType: 'ExportDocument', templatePath }, { invoiceId });
    assert.equal(preview.status, 200, JSON.stringify(preview.body));
    assert.equal(preview.body.html.split('MEN SWEATERS').length - 1, 1);
    assert(!preview.body.html.includes('INTERNAL PRODUCT'));
    assert(preview.body.html.indexOf('7944PCS') < preview.body.html.indexOf('3330PCS'));
    fs.writeFileSync(path.join(output, `entry-${name}.html`), preview.body.html);
    const destinationPath = path.join(output, `entry-${name}.pdf`);
    const job = await request('StartInvoiceReportPdfSaveToPathJob', token, { reportType: 'ExportDocument', templatePath, destinationPath }, { invoiceId });
    assert.equal(job.status, 202, JSON.stringify(job.body));
    await waitFor(() => fs.existsSync(destinationPath), 30000, 'Entry PDF missing');
    assert.equal(fs.readFileSync(destinationPath).subarray(0, 5).toString(), '%PDF-');
  }
  // The text is arbitrary: no style/order presets or interpretation are involved.
  await edit(1, 'PACKING INSTRUCTIONS\n任意说明：KEEP DRY');
  await click('下移行');
  await click('撤销');
  await wait("document.querySelector('[aria-label=\"打印第 1 行描述\"]').value.includes('任意说明')");
  await click('保存发票');
  await waitFor(async () => (await request('GetInvoice', token, undefined, { id: invoiceId })).body.items[0].printRows[0].text.includes('任意说明'), 30000, 'Multiline notes were not saved');
  await screenshot('Full-entry-arbitrary-notes.png');
  await run("document.querySelector('[aria-label=\"选择打印第 3 行\"]').click(); true");
  await click('下移行');
  await wait("document.querySelector('[aria-label=\"打印第 3 行描述\"]')");
  assert.equal(await run("document.querySelector('[aria-label=\"按录入行打印明细\"] tbody tr:last-child td').textContent"), '空白');
  await click('撤销');
  await wait("document.querySelector('[aria-label=\"打印第 4 行描述\"]')");
  await run("[...document.querySelectorAll('summary')].find(e=>e.textContent==='录入显示列').click(); true");
  await run("[...document.querySelectorAll('[aria-label=\"打印排版录入列\"] label')].find(e=>e.textContent.trim()==='总毛重').querySelector('input').click(); true");
  await run("[...document.querySelectorAll('summary')].find(e=>e.textContent==='录入显示列').click(); true");
  await click('商品资料');
  await click('打印排版');
  await wait("document.querySelector('.invoice-items-print [data-invoice-item-field=gwTotal]')");
  await run("document.querySelector('[aria-label=\"选择打印第 1 行\"]').click(); [...document.querySelectorAll('summary')].find(e=>e.textContent==='批量插入说明与空白行').click(); document.querySelector('[aria-label=\"批量说明文字\"]').focus(); true");
  await cdp.send('Input.insertText', { text: '任意文字 A\n\n任意文字 B' });
  await click('插入到所选行下方');
  await wait("document.querySelectorAll('[aria-label=\"按录入行打印明细\"] tbody tr').length===7");
  assert.equal(await run("document.activeElement.getAttribute('aria-label')"), '打印第 2 行描述');
  await click('撤销');
  await wait("document.querySelectorAll('[aria-label=\"按录入行打印明细\"] tbody tr').length===4");
  await click('重做');
  await click('保存发票');
  await waitFor(async () => (await request('GetInvoice', token, undefined, { id: invoiceId })).body.items[0].printRows.length === 6, 30000, 'Batch notes did not roundtrip');
  const final = (await request('GetInvoice', token, undefined, { id: invoiceId })).body;
  assert.equal(final.totalAmount, 114142);
  assert.equal(final.items[0].printRows[2].kind, 'Blank');
  await run("[...document.querySelectorAll('summary')].find(e=>e.textContent==='批量插入说明与空白行').click(); true");
  await screenshot('Full-entry-batch-notes.png');
  await verifyEntryTemplateDesigner({ run, request, token, cdp, output, invoice: final, originalDraft: draft, templatePath: catalog.find(item => item.templatePath.endsWith('invoice_template.dtpl')).templatePath });
  return { invoiceId };
}
