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
    await wait("[...document.querySelectorAll('button')].some(e=>e.textContent.trim()==='保存'&&e.disabled)");
    const saved = await request('GetUserReportTemplate', token, undefined, { id });
    assert.equal(saved.status, 200);
    return JSON.parse(saved.body.contentHtml);
  };
  const previewPdf = async (id, name, invoiceId = invoice.id) => {
    const preview = await request('PreviewInvoiceReportHtml', token, { reportType: 'ExportDocument', templatePath: `user-template:${id}` }, { invoiceId });
    assert.equal(preview.status, 200, JSON.stringify(preview.body));
    fs.writeFileSync(path.join(output, name + '.html'), preview.body.html);
    const destinationPath = path.join(output, name + '.pdf');
    const job = await request('StartInvoiceReportPdfSaveToPathJob', token, { reportType: 'ExportDocument', templatePath: `user-template:${id}`, withSeal: false, destinationPath }, { invoiceId });
    assert.equal(job.status, 202);
    await waitFor(() => fs.existsSync(destinationPath), 30000, 'Blank detail PDF missing');
    return preview.body.html;
  };
  const tableTemplate = await create('空白商品明细与调整');
  await run("[...document.querySelectorAll('.report-designer-v3-toolbar details')].find(e=>e.querySelector('summary').textContent.includes('高级排版')).open=true; document.querySelector('button[aria-label=明细表]').click(); true");
  await wait("document.querySelector('.new-report-detail-column-card')");
  await run("(()=>{const n=[...document.querySelectorAll('.new-report-detail-properties label')].find(e=>e.firstElementChild?.textContent==='按录入行打印的说明列').querySelector('select');n.value=n.options[1].value;n.dispatchEvent(new Event('change',{bubbles:true}));return true})()");
  await choose('说明与合计', '.new-report-detail-properties [role=tab]');
  await choose('金额调整', '.new-report-detail-properties summary');
  await run("[...document.querySelectorAll('label')].find(e=>e.textContent.trim()==='显示发票金额调整').querySelector('input').click(); true");
  await choose('撤销', '.report-designer-v3-toolbar button');
  await choose('重做', '.report-designer-v3-toolbar button');
  const intro = "[...document.querySelectorAll('.new-report-detail-properties details')].find(e=>e.querySelector('summary')?.textContent==='首页说明行')";
  await choose('首页说明行', '.new-report-detail-properties summary');
  await wait(`${intro}.querySelector('button')`);
  await run(`[...${intro}.querySelectorAll('button')].find(e=>e.textContent.trim()==='添加').click(); true`);
  await wait(`${intro}.querySelector('input')`);
  await run(`[...${intro}.querySelectorAll('label')].find(e=>e.firstElementChild?.textContent==='标签').querySelector('input').focus(); [...${intro}.querySelectorAll('label')].find(e=>e.firstElementChild?.textContent==='标签').querySelector('input').select(); true`);
  await cdp.send('Input.dispatchKeyEvent', {type:'keyDown',key:'Backspace',windowsVirtualKeyCode:8});
  await cdp.send('Input.dispatchKeyEvent', {type:'keyUp',key:'Backspace',windowsVirtualKeyCode:8});
  await run(`(()=>{const e=[...${intro}.querySelectorAll('label')].find(e=>e.firstElementChild?.textContent==='设置哪一列').querySelector('select');e.value=e.options[e.options.length-1].value;e.dispatchEvent(new Event('change',{bubbles:true}));return true})()`);
  await wait(`${intro}.querySelector('.new-report-summary-cell-editor strong')?.textContent==='Amount'`);
  await run(`(()=>{const e=[...${intro}.querySelectorAll('label')].find(e=>e.firstElementChild?.textContent==='字段').querySelector('select');e.value='Invoice.TradeTerms';e.dispatchEvent(new Event('change',{bubbles:true}));return true})()`);
  await wait("[...document.querySelectorAll('[data-v3-element-id]')].some(element=>element.textContent.includes('FOB'))");
  const tableDesign = await save(tableTemplate.id);
  const table = tableDesign.layers.flatMap(layer => layer.elements).find(element => element.block?.type === 'DetailTable').block;
  assert(table.adjustments);
  assert.equal(table.entryDescriptionColumnId, table.columns[0].id);
  assert(table.summaryRow.cells.some(cell => cell.fieldPath === 'Invoice.AdjustedTotalAmount'));
  assert.equal(table.introRow.label, '');
  assert(table.introRow.cells.some(cell => cell.columnId === table.columns.at(-1).id && cell.fieldPath === 'Invoice.TradeTerms'));
  const html = await previewPdf(tableTemplate.id, 'blank-detail-adjustments');
  for (const text of ['FOB SHANGHAI', 'RECEIVED DEPOSIT', '-USD10963.50', 'SAMPLE CHARGE', 'USD61161.80']) assert(html.includes(text), text);
  await captureScreenshot(cdp, path.join(output, 'Full-blank-adjustment-designer.png'));
  const sourceInvoice = await request('GetInvoice', token, undefined, { id: invoice.id });
  assert.equal(sourceInvoice.status, 200);
  const entryInvoice = await request('CreateInvoice', token, { ...sourceInvoice.body, id: 0, rowVersion: '', invoiceNo: 'BLANK-ENTRY-DESIGN', useEntryPrintRows: true,
    items: sourceInvoice.body.items.map((item, index) => ({ ...item, id: 0, invoiceId: 0, printRows: [{ kind: 'Text', text: `Free note ${index + 1}` }, { kind: 'Item', text: '' }, { kind: 'Blank', text: '' }] })) });
  assert.equal(entryInvoice.status, 201, JSON.stringify(entryInvoice.body));
  assert.notEqual(entryInvoice.body.id, invoice.id, 'The entry fixture must be a separate invoice');
  const entryHtml = await previewPdf(tableTemplate.id, 'blank-entry-rows', entryInvoice.body.id);
  for (let index = 0; index < sourceInvoice.body.items.length; index++) assert.equal(entryHtml.split(`Free note ${index + 1}`).length - 1, 1);
  const entrySaved = await request('GetInvoice', token, undefined, { id: entryInvoice.body.id });
  assert.equal(entrySaved.status, 200);
  assert.equal(entrySaved.body.totalAmount, sourceInvoice.body.totalAmount);
  await run("(()=>{const n=[...document.querySelectorAll('.new-report-detail-properties label')].find(e=>e.firstElementChild?.textContent==='按录入行打印的说明列').querySelector('select');n.value='';n.dispatchEvent(new Event('change',{bubbles:true}));return true})()");
  const productsOnlyDesign = await save(tableTemplate.id);
  assert(!productsOnlyDesign.layers.flatMap(layer => layer.elements).find(element => element.block?.type === 'DetailTable').block.entryDescriptionColumnId);
  const productsOnlyHtml = await previewPdf(tableTemplate.id, 'blank-products-only', entryInvoice.body.id);
  assert.equal(productsOnlyHtml, html, 'A products-only template must ignore entry mode on the same invoice and preserve automatic output');

  const freeTemplate = await create('空白独立商品字段与占位');
  const catalog = await request('GetReportTemplateFieldCatalog', token);
  assert.equal(catalog.status, 200);
  const styleNo = catalog.body.fields.find(field => field.value.replaceAll('{', '').replaceAll('}', '').trim() === 'item.StyleNo');
  assert(styleNo, 'The authorized field catalog includes item.StyleNo');
  const fieldSelector = `button[aria-label=${JSON.stringify('插入字段 ' + styleNo.label)}]`;
  await run("document.querySelector('button[aria-label=商品字段]').click(); true");
  await wait("document.querySelector('.report-designer-field-scope')");
  await run("for(const e of document.querySelectorAll('.report-designer-v3-sidebar details'))e.open=true; true");
  await wait(`document.querySelector(${JSON.stringify(fieldSelector)})?.getClientRects().length`);
  await run(`document.querySelector(${JSON.stringify(fieldSelector)}).click(); true`);
  await choose('显隐与空行', '.report-designer-v3-inspector summary');
  for (const label of ['空内容时隐藏字段', '按条件显示', '隐藏时收起空内容行']) {
    await run(`[...document.querySelectorAll('.report-designer-v3-inspector label')].find(e=>e.textContent.trim()===${JSON.stringify(label)}).querySelector('input').click(); true`);
  }
  await choose('在商品末尾添加占位行');
  await choose('撤销', '.report-designer-v3-toolbar button');
  await choose('重做', '.report-designer-v3-toolbar button');
  await wait("document.querySelector('.report-designer-v3-inspector [role=tab][aria-selected=true]')?.textContent==='布局'");
  const freeDesign = await save(freeTemplate.id);
  const elements = freeDesign.layers.flatMap(layer => layer.elements);
  assert(elements.some(element => element.type === 'ProductSpacer'));
  const field = elements.find(element => element.fieldPath === 'item.StyleNo');
  assert(field.product.hideWhenEmpty && field.product.collapseEmpty && field.product.condition);
  assert((await previewPdf(freeTemplate.id, 'blank-independent-product-spacer')).includes('STYLE-1'));
  await captureScreenshot(cdp, path.join(output, 'Full-blank-independent-spacer.png'));
}
