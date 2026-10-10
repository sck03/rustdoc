import assert from 'node:assert/strict';
import path from 'node:path';
import { captureScreenshot } from './web-runtime-browser-session.mjs';
import { waitFor } from './web-runtime-smoke-common.mjs';

export async function verifyDesktopDetailDesigner({ run, request, token, cdp, output }) {
  const wait = async expression => {
    try { await waitFor(() => run(expression), 30000, 'Desktop detail designer timed out'); }
    catch (error) { throw new Error(`${error.message}: ${expression}\n${await run('document.body.innerText')}`); }
  };
  const catalog = await request('ListReportTemplates', token);
  const packing = catalog.body.find(item => item.templatePath.endsWith('packing_list_template.dtpl'));
  assert(packing, 'Packing template available in Full desktop');
  const clone = await request('CloneUserReportTemplate', token, {
    reportType: 'ExportDocument', name: '桌面明细属性验收', sourceTemplatePath: packing.templatePath,
  });
  assert.equal(clone.status, 201);
  const schema = JSON.parse(clone.body.contentHtml);
  const table = schema.layers.flatMap(layer => layer.elements).find(element => element.block?.type === 'DetailTable');
  const selector = `[data-v3-element-id="${table.id}"]`;
  await run(`location.hash=${JSON.stringify('#/reports/templates?reportType=ExportDocument&userTemplateId=' + clone.body.id)}; true`);
  await wait(`Boolean(document.querySelector(${JSON.stringify(selector)}))`);
  await run(`document.querySelector(${JSON.stringify(selector)}).focus(); true`);
  await cdp.send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Enter', windowsVirtualKeyCode: 13 });
  await cdp.send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Enter', windowsVirtualKeyCode: 13 });
  await wait("Boolean(document.querySelector('.new-report-detail-column-card'))");
  await run("(()=>{const input=[...document.querySelectorAll('.new-report-detail-properties label')].find(node=>node.firstElementChild?.textContent==='标题').querySelector('input');input.focus();input.select()})()");
  await cdp.send('Input.insertText', { text: '桌面货物说明' });
  await cdp.send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Enter', windowsVirtualKeyCode: 13 });
  await cdp.send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Enter', windowsVirtualKeyCode: 13 });
  await run("[...document.querySelectorAll('.new-report-detail-properties [role=tab]')].find(node=>node.textContent==='表格样式').click(); true");
  await wait("Boolean([...document.querySelectorAll('.new-report-detail-properties label')].find(node=>node.firstElementChild?.textContent==='设置文字样式')?.getClientRects().length)");
  await captureScreenshot(cdp, path.join(output, 'Full-detail-properties.png'));
  await wait("Boolean([...document.querySelectorAll('button')].find(node=>node.textContent.trim()==='保存'&&!node.disabled))");
  await run("[...document.querySelectorAll('button')].find(node=>node.textContent.trim()==='保存'&&!node.disabled).click(); true");
  await wait("document.body.innerText.includes('私人模板已保存')");
  const saved = await request('GetUserReportTemplate', token, undefined, { id: clone.body.id });
  assert.equal(saved.status, 200);
  const savedTable = JSON.parse(saved.body.contentHtml).layers.flatMap(layer => layer.elements).find(element => element.id === table.id).block;
  assert.equal(savedTable.columns[0].title, '桌面货物说明');
  assert.deepEqual(savedTable.summaryRow, table.block.summaryRow);
  await run("[...document.querySelectorAll('[role=tab]')].find(node=>node.textContent.trim()==='预览').click(); true");
  await wait("Boolean([...document.querySelectorAll('button')].find(node=>node.textContent.trim()==='样例预览'&&!node.disabled))");
  await run("[...document.querySelectorAll('button')].find(node=>node.textContent.trim()==='样例预览').click(); true");
  await wait("Boolean(document.querySelector('iframe[title=模板预览]')?.srcdoc.includes('桌面货物说明'))");
  await captureScreenshot(cdp, path.join(output, 'Full-detail-native-preview.png'));
  const invoice = catalog.body.find(item => item.templatePath.endsWith('invoice_template.dtpl'));
  assert(invoice, 'Commercial invoice template available');
  const invoiceCopy = await request('CloneUserReportTemplate', token, {
    reportType: 'ExportDocument', name: '桌面发票图层验收', sourceTemplatePath: invoice.templatePath,
  });
  assert.equal(invoiceCopy.status, 201);
  const invoiceDesign = JSON.parse(invoiceCopy.body.contentHtml);
  await run(`location.hash=${JSON.stringify('#/reports/templates?reportType=ExportDocument&userTemplateId=' + invoiceCopy.body.id)}; true`);
  await wait(`document.querySelectorAll('[aria-label="画布图层导航"] button').length===${invoiceDesign.layers.length}`);
  await run(`[...document.querySelectorAll('[role=tab]')].find(node=>node.textContent.trim()==='可视化设计').click(); true`);
  await wait(`document.querySelector('[aria-label="画布图层导航"]').getClientRects().length>0`);
  await run(`document.querySelector('.report-designer-v3-sidebar-tabs button:nth-child(3)').click(); true`);
  const total = invoiceDesign.layers.flatMap(layer => layer.elements).find(element => element.text === 'TOTAL:');
  assert(total, 'Invoice total label remains independently editable');
  await run(`document.querySelector('[data-v3-element-id="${total.id}"]').focus({preventScroll:true}); true`);
  await cdp.send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Enter', windowsVirtualKeyCode: 13 });
  await cdp.send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Enter', windowsVirtualKeyCode: 13 });
  await wait(`document.querySelector('.report-designer-selection-readout').textContent.includes('TOTAL:')`);
  assert(await run(`document.querySelector('.report-designer-v3-page').classList.contains('has-element-bounds')`));
  await wait(`document.querySelector('.report-designer-v3-canvas-column').getBoundingClientRect().width > document.querySelector('.report-designer-v3-workspace').clientWidth / 2`);
  await run(`window.scrollTo(0,0); true`);
  await captureScreenshot(cdp, path.join(output, 'Full-invoice-layers.png'), { captureBeyondViewport: false });
  const totalSelector = `[data-v3-element-id="${total.id}"]`;
  const left = `parseFloat(document.querySelector(${JSON.stringify(totalSelector)}).style.left)`;
  const initialLeft = await run(left);
  await run(`document.querySelector(${JSON.stringify(totalSelector)}).focus({preventScroll:true}); true`);
  for (const [key, modifiers, offset] of [['ArrowRight', 0, 1], ['z', 2, 0], ['y', 2, 1], ['ArrowRight', 0, 2], ['z', 2, 1], ['z', 2, 0]]) {
    await cdp.send('Input.dispatchKeyEvent', { type: 'keyDown', key, modifiers });
    await cdp.send('Input.dispatchKeyEvent', { type: 'keyUp', key, modifiers });
    await wait(`Math.abs(${left} - ${initialLeft + offset}) < 0.001`);
  }
  await run(`document.querySelector('.report-designer-v3-sidebar-tabs button:nth-child(2)').click(); true`);
  assert.equal(await run(`document.querySelectorAll('.report-designer-v3-layer-list[hidden] .report-designer-v3-layer-elements button').length`), 0, 'the packaged designer only mounts visible element lists');
  const point = await run(`(()=>{const node=document.querySelector(${JSON.stringify(totalSelector)});node.scrollIntoView({block:'center'});node.focus({preventScroll:true});const rect=node.getBoundingClientRect();return {x:rect.left+rect.width/2,y:rect.top+rect.height/2}})()`);
  await cdp.send('Input.dispatchMouseEvent', { type: 'mousePressed', ...point, button: 'left', clickCount: 1 });
  await cdp.send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: point.x + 20, y: point.y, button: 'left', buttons: 1 });
  await cdp.send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Escape', windowsVirtualKeyCode: 27 });
  await cdp.send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Escape', windowsVirtualKeyCode: 27 });
  await cdp.send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: point.x + 20, y: point.y, button: 'left', clickCount: 1 });
  await wait(`${left} === ${initialLeft} && document.querySelector(${JSON.stringify(totalSelector)}).style.willChange === ''`);
  await wait(`document.querySelector('.report-designer-v3-header').textContent.includes('与已载入模板一致')`);
  await captureScreenshot(cdp, path.join(output, 'Full-designer-drag-cancelled.png'), { captureBeyondViewport: false });
}
