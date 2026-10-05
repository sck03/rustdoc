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
}
