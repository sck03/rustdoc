import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { captureScreenshot } from './web-runtime-browser-session.mjs';
import { waitFor } from './web-runtime-smoke-common.mjs';

export async function verifyEntryTemplateDesigner({ run, request, token, cdp, output, invoice, originalDraft, templatePath }) {
  const wait = expression => waitFor(() => run(`Boolean(${expression})`), 30000, expression);
  const click = async (label, selector = 'button') => {
    const expression = `[...document.querySelectorAll(${JSON.stringify(selector)})].find(n=>n.getClientRects().length&&n.textContent.trim()===${JSON.stringify(label)})`;
    await wait(expression); await run(`${expression}.click(); true`);
  };
  const control = label => `[...document.querySelectorAll('.new-report-detail-properties label')].find(n=>n.firstElementChild?.textContent===${JSON.stringify(label)})`;
  const choose = async (label, value) => {
    await run(`(()=>{const n=${control(label)}.querySelector('select');n.value=${JSON.stringify(value)};n.dispatchEvent(new Event('change',{bubbles:true}));return true})()`);
  };
  const input = async (label, value) => {
    const expression = `${control(label)}.querySelector('input,textarea')`;
    await run(`${expression}.focus();${expression}.select();true`);
    await cdp.send('Input.insertText', { text: value });
    await run(`${expression}.blur();true`);
  };
  const clone = await request('CloneUserReportTemplate', token, { reportType: 'ExportDocument', name: '两种明细打印共用列设计', sourceTemplatePath: templatePath });
  assert.equal(clone.status, 201);
  const table = JSON.parse(clone.body.contentHtml).layers.flatMap(l => l.elements).find(e => e.block?.type === 'DetailTable');
  const selector = `[data-v3-element-id="${table.id}"]`;
  await run(`location.hash='#/reports/templates?reportType=ExportDocument&userTemplateId=${clone.body.id}&invoiceId=${invoice.id}';true`);
  await wait(`document.querySelector(${JSON.stringify(selector)})`);
  await click('可视化设计', '[role=tab]');
  await run(`document.querySelector(${JSON.stringify(selector)}).focus();true`);
  for (const type of ['keyDown', 'keyUp']) await cdp.send('Input.dispatchKeyEvent', { type, key: 'Enter', windowsVirtualKeyCode: 13 });
  await wait("document.querySelector('.new-report-detail-column-card')");
  await choose('画布示例', 'entries');
  await wait("document.querySelector('[data-report-entry-kind=blank]')");
  await choose('选择要修改的列', 'price');
  await run("document.querySelector('button[aria-label=删除列]').click();true");
  await wait(`${control('选择要修改的列')}.querySelector('select').options.length===4`);
  await click('撤销', '.report-designer-v3-toolbar button');
  await wait(`${control('选择要修改的列')}.querySelector('select').options.length===5`);
  await click('重做', '.report-designer-v3-toolbar button');
  await wait(`${control('选择要修改的列')}.querySelector('select').options.length===4`);
  await click('新增列', '.new-report-detail-properties button');
  await wait(`${control('选择要修改的列')}.querySelector('select').options.length===5`);
  await input('标题', 'Gross kg');
  await choose('显示内容', 'item.GWTotal');
  await run("document.querySelector('button[aria-label=上移列]').click();true");
  await choose('选择要修改的列', 'description');
  await input('标题', 'Goods / Notes');
  const maximumWidth = Number(await run(`${control('打印宽度(mm)')}.querySelector('input').max`));
  await input('打印宽度(mm)', '75');
  assert(await run("document.querySelector('form.report-template-layout').checkValidity()"),
    JSON.stringify(await run("[...document.querySelectorAll('form.report-template-layout :invalid')].map(n=>({value:n.value,min:n.min,max:n.max,step:n.step,message:n.validationMessage}))")));
  await captureScreenshot(cdp, path.join(output, 'Full-entry-template-columns.png'), { captureBeyondViewport: false });
  await wait("[...document.querySelectorAll('button')].some(n=>n.textContent.trim()==='保存'&&!n.disabled)");
  await click('保存');
  await wait("document.body.innerText.includes('私人模板已保存')");
  const saved = await request('GetUserReportTemplate', token, undefined, { id: clone.body.id });
  assert.equal(saved.status, 200);
  const block = JSON.parse(saved.body.contentHtml).layers.flatMap(l => l.elements).find(e => e.id === table.id).block;
  assert.equal(block.entryDescriptionColumnId, 'description');
  assert.equal(block.columns[0].title, 'Goods / Notes');
  assert(!block.columns.some(c => c.id === 'price'));
  assert.equal(block.columns[3].fieldPath, 'item.GWTotal');
  const printedWidth = block.columns[0].widthMm / block.columns.reduce((sum, c) => sum + c.widthMm, 0) * (table.widthHundredthMm / 100 - block.sideBand.widthMm);
  assert(Math.abs(printedWidth - Math.min(75, maximumWidth)) < 0.01, 'physical width clamps at the adjacent column minimum and remains saveable');
  // Keep the boundary save above, then use readable widths for the PDF comparison.
  await input('打印宽度(mm)', '50');
  await choose('选择要修改的列', 'cartons');
  await input('打印宽度(mm)', '20');
  await choose('选择要修改的列', 'quantity');
  await input('打印宽度(mm)', '20');
  await click('保存');
  await waitFor(async () => {
    const current = await request('GetUserReportTemplate', token, undefined, { id: clone.body.id });
    return current.status === 200 && current.body.contentHtml !== saved.body.contentHtml;
  }, 30000, 'readable column widths were not saved');
  await wait("[...document.querySelectorAll('button')].some(n=>n.textContent.trim()==='保存'&&n.disabled)");
  await run('location.reload();true');
  await wait(`document.querySelector(${JSON.stringify(selector)})?.textContent.includes('Gross kg')`);
  const normal = await request('CreateInvoice', token, { ...originalDraft, invoiceNo: 'ENTRY-NORMAL', useEntryPrintRows: false });
  assert.equal(normal.status, 201);
  for (const [mode, record] of [['entries', invoice], ['products', normal.body]]) {
    const requestBody = { reportType: 'ExportDocument', templatePath: `user-template:${clone.body.id}` };
    const preview = await request('PreviewInvoiceReportHtml', token, requestBody, { invoiceId: record.id });
    assert.equal(preview.status, 200, JSON.stringify(preview.body));
    const html = preview.body.html;
    for (const text of ['Gross kg', 'Goods / Notes', '7944PCS', '3330PCS']) assert(html.includes(text), `${mode}: ${text}`);
    assert(!html.includes('Unit Price'));
    assert.equal(html.includes('INTERNAL PRODUCT'), mode === 'products');
    assert.equal(html.includes('PACKING INSTRUCTIONS'), mode === 'entries');
    fs.writeFileSync(path.join(output, `custom-${mode}.html`), html);
    const destinationPath = path.join(output, `custom-${mode}.pdf`);
    const job = await request('StartInvoiceReportPdfSaveToPathJob', token, { ...requestBody, destinationPath }, { invoiceId: record.id });
    assert.equal(job.status, 202, JSON.stringify(job.body));
    await waitFor(() => fs.existsSync(destinationPath), 30000, `${mode} PDF missing`);
    assert.equal(fs.readFileSync(destinationPath).subarray(0, 5).toString(), '%PDF-');
  }
}
