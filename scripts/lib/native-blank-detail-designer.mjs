import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { captureScreenshot } from './web-runtime-browser-session.mjs';
import { waitFor } from './web-runtime-smoke-common.mjs';

export async function verifyDesktopBlankDetails({ run, request, token, invoice, cdp, output }) {
  const wait = expression => waitFor(() => run(`Boolean(${expression})`), 30000, 'Blank detail designer: ' + expression);
  const key = async name => {
    await cdp.send('Input.dispatchKeyEvent', { type: 'keyDown', key: name, windowsVirtualKeyCode: name === 'Enter' ? 13 : undefined });
    await cdp.send('Input.dispatchKeyEvent', { type: 'keyUp', key: name, windowsVirtualKeyCode: name === 'Enter' ? 13 : undefined });
  };
  const choose = async (label, selector = 'button') => {
    await run(`[...document.querySelectorAll(${JSON.stringify(selector)})].find(e=>e.getClientRects().length&&e.textContent.trim()===${JSON.stringify(label)}).click(); true`);
    await run('new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(()=>resolve(true))))');
  };
  const create = async name => {
    const created = await request('CreateUserReportTemplate', token, { reportType: 'ExportDocument', name, contentHtml: '' });
    assert.equal(created.status, 201);
    const initial = JSON.parse(created.body.contentHtml).layers.flatMap(layer => layer.elements);
    assert.equal(initial.length, 1);
    await run(`location.hash='#/reports/templates?reportType=ExportDocument&userTemplateId=${created.body.id}&invoiceId=${invoice.id}'; true`);
    await wait(`document.querySelector('[data-v3-element-id="${initial[0].id}"]')`);
    await choose('可视化设计', '[role=tab]');
    await run(`document.querySelector('[data-v3-element-id="${initial[0].id}"]').focus(); true`);
    await key('Enter');
    await run("document.querySelector('[aria-label=删除]').click(); true");
    await wait("document.querySelectorAll('[data-v3-element-id]').length===0");
    await run("[...document.querySelectorAll('[aria-label=画布图层导航] button')].find(e=>e.textContent.includes('主体')).click(); true");
    return created.body;
  };
  const save = async id => {
    await wait("[...document.querySelectorAll('button')].some(e=>e.textContent.trim()==='保存'&&!e.disabled)");
    await choose('保存');
    await wait("document.body.innerText.includes('私人模板已保存')");
    const saved = await request('GetUserReportTemplate', token, undefined, { id });
    assert.equal(saved.status, 200);
    return JSON.parse(saved.body.contentHtml);
  };
  const previewPdf = async (id, name) => {
    const preview = await request('PreviewInvoiceReportHtml', token, { reportType: 'ExportDocument', templatePath: `user-template:${id}` }, { invoiceId: invoice.id });
    assert.equal(preview.status, 200, JSON.stringify(preview.body));
    fs.writeFileSync(path.join(output, name + '.html'), preview.body.html);
    const destinationPath = path.join(output, name + '.pdf');
    const job = await request('StartInvoiceReportPdfSaveToPathJob', token, { reportType: 'ExportDocument', templatePath: `user-template:${id}`, withSeal: false, destinationPath }, { invoiceId: invoice.id });
    assert.equal(job.status, 202);
    await waitFor(() => fs.existsSync(destinationPath), 30000, 'Blank detail PDF missing');
    return preview.body.html;
  };
  const tableTemplate = await create('空白商品明细与调整');
  await run("[...document.querySelectorAll('.report-designer-v3-toolbar details')].find(e=>e.querySelector('summary').textContent.includes('高级排版')).open=true; document.querySelector('button[aria-label=明细表]').click(); true");
  await wait("document.querySelector('.new-report-detail-column-card')");
  await choose('分页与高级', '.new-report-detail-properties [role=tab]');
  await choose('定金与费用调整', '.new-report-detail-properties summary');
  await run("[...document.querySelectorAll('label')].find(e=>e.textContent.trim()==='显示发票金额调整').querySelector('input').click(); true");
  await choose('撤销', '.report-designer-v3-toolbar button');
  await choose('重做', '.report-designer-v3-toolbar button');
  const tableDesign = await save(tableTemplate.id);
  const table = tableDesign.layers.flatMap(layer => layer.elements).find(element => element.block?.type === 'DetailTable').block;
  assert(table.adjustments);
  assert(table.summaryRow.cells.some(cell => cell.fieldPath === 'Invoice.AdjustedTotalAmount'));
  const html = await previewPdf(tableTemplate.id, 'blank-detail-adjustments');
  for (const text of ['RECEIVED DEPOSIT', '-USD10963.50', 'SAMPLE CHARGE', 'USD61161.80']) assert(html.includes(text), text);
  await captureScreenshot(cdp, path.join(output, 'Full-blank-adjustment-designer.png'));

  const freeTemplate = await create('空白独立商品字段与占位');
  await run("document.querySelector('button[aria-label=商品字段]').click(); for(const e of document.querySelectorAll('.report-designer-v3-sidebar details'))e.open=true; true");
  await wait("document.querySelector('button[aria-label=\"插入字段 款号\"]')");
  await run("document.querySelector('button[aria-label=\"插入字段 款号\"]').click(); true");
  await choose('显隐与空行', '.report-designer-v3-inspector summary');
  for (const label of ['空内容时隐藏字段', '按条件显示', '隐藏时收起空内容行']) {
    await run(`[...document.querySelectorAll('.report-designer-v3-inspector label')].find(e=>e.textContent.trim()===${JSON.stringify(label)}).querySelector('input').click(); true`);
  }
  await choose('在商品末尾添加占位行');
  await choose('撤销', '.report-designer-v3-toolbar button');
  await choose('重做', '.report-designer-v3-toolbar button');
  const freeDesign = await save(freeTemplate.id);
  const elements = freeDesign.layers.flatMap(layer => layer.elements);
  assert(elements.some(element => element.type === 'ProductSpacer'));
  const field = elements.find(element => element.fieldPath === 'item.StyleNo');
  assert(field.product.hideWhenEmpty && field.product.collapseEmpty && field.product.condition);
  assert((await previewPdf(freeTemplate.id, 'blank-independent-product-spacer')).includes('STYLE-1'));
  await captureScreenshot(cdp, path.join(output, 'Full-blank-independent-spacer.png'));
}
