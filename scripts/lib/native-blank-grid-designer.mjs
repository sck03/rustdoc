import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { captureScreenshot } from './web-runtime-browser-session.mjs';
import { waitFor } from './web-runtime-smoke-common.mjs';

export async function verifyDesktopBlankGrid({ run, request, token, businessDate, cdp, output }) {
  const created = await request('CreateUserReportTemplate', token, { reportType: 'PaymentVoucher', name: '空白付款网格验收', contentHtml: '' });
  assert.equal(created.status, 201);
  assert.equal(JSON.parse(created.body.contentHtml).layers.flatMap(layer => layer.elements).length, 0);
  const wait = async expression => {
    try { await waitFor(() => run(expression), 30000, 'Blank desktop designer timed out'); }
    catch (error) { throw new Error(`${error.message}: ${expression}\n${await run('document.body.innerText')}`); }
  };
  const control = label => `[...document.querySelectorAll('.report-designer-v3-inspector label')].find(node=>node.querySelector('span')?.textContent.trim()===${JSON.stringify(label)}).querySelector('input,select,textarea')`;
  const key = async (key, modifiers = 0) => {
    await cdp.send('Input.dispatchKeyEvent', { type: 'keyDown', key, modifiers, windowsVirtualKeyCode: key === 'Enter' ? 13 : undefined, text: key === 'Enter' && !modifiers ? '\r' : undefined });
    await cdp.send('Input.dispatchKeyEvent', { type: 'keyUp', key, modifiers });
  };
  const button = async (label, scope = '.report-designer-v3-inspector') => {
    await run(`[...document.querySelectorAll(${JSON.stringify(scope + ' button')})].find(node=>node.textContent.trim()===${JSON.stringify(label)}).click(); true`);
    await run('new Promise(resolve=>requestAnimationFrame(resolve))');
  };
  const select = async (label, value) => {
    await wait(`[...${control(label)}.options].some(option=>option.value===${JSON.stringify(value)})`);
    await run(`(()=>{const node=${control(label)};node.value=${JSON.stringify(value)};node.dispatchEvent(new Event('change',{bubbles:true}));})()`);
    await run('new Promise(resolve=>requestAnimationFrame(resolve))');
  };
  const fill = async (label, value, multiline = false) => {
    await run(`(()=>{const node=${control(label)};node.focus();node.select();})()`);
    await cdp.send('Input.insertText', { text: String(value) });
    await key('Enter', multiline ? 2 : 0);
  };
  const cell = async (row, column) => {
    await button('整张表');
    await run(`document.querySelector('[aria-label="第 ${row} 行，第 ${column} 列"]').click(); true`);
    await button('单元格');
  };
  await run(`location.hash=${JSON.stringify('#/reports/templates?reportType=PaymentVoucher&userTemplateId=' + created.body.id)}; true`);
  await wait("Boolean(document.querySelector('[data-v3-page-canvas]'))");
  await run("[...document.querySelectorAll('[role=tab]')].find(node=>node.textContent.trim()==='可视化设计').click(); true");
  await wait("document.querySelector('[data-v3-page-canvas]').getClientRects().length>0");
  await run("[...document.querySelectorAll('.report-designer-v3-toolbar details')].find(node=>node.querySelector('summary').textContent.includes('高级排版')).open=true; document.querySelector('[aria-label=普通表格]').click(); true");
  await wait("Boolean(document.querySelector('.new-report-grid-properties'))");
  await button('整张表');
  await select('快速版式', 'Blank');
  await fill('行数', 5);
  await fill('列数', 7);
  await button('单元格');
  await fill('文字内容', '用款事项', true);
  await run("[...document.querySelectorAll('.new-report-grid-cell-editor label')].find(node=>node.textContent.includes('竖排文字')).querySelector('input').click(); true");
  for (let index = 0; index < 4; index++) await button('向下合并');
  await cell(1, 2);
  for (let index = 0; index < 5; index++) await button('向右合并');
  await select('内容类型', 'Field');
  await select('业务字段', 'Payment.PayeeName');
  await fill('字段前标签（可选）', '支付单位');
  await cell(2, 2);
  for (let index = 0; index < 5; index++) await button('向右合并');
  await select('内容类型', 'CheckboxGroup');
  await select('判断字段', 'Payment.PaymentMethod');
  await fill('勾选项（每行：名称=值）', '支票=支票\n电汇=电汇\n预付=预付', true);
  await cell(3, 2);
  await button('向右合并');
  await fill('本行高度 (mm)', 14);
  await run("[...document.querySelectorAll('.new-report-grid-cell-editor label')].find(node=>node.textContent.includes('斜线表头')).querySelector('input').click(); true");
  await fill('斜线上方文字', '项目');
  await fill('斜线下方文字', '金额');
  await wait("Boolean([...document.querySelectorAll('button')].find(node=>node.textContent.trim()==='保存'&&!node.disabled))");
  await button('保存', 'body');
  await wait("document.body.innerText.includes('私人模板已保存')");
  const saved = await request('GetUserReportTemplate', token, undefined, { id: created.body.id });
  assert.equal(saved.status, 200);
  const design = JSON.parse(saved.body.contentHtml);
  const grid = design.layers.flatMap(layer => layer.elements).find(element => element.flowKind === 'Grid').block;
  assert.equal(grid.rows.length, 5); assert.equal(grid.columns.length, 7);
  assert.equal(grid.rows[0].cells[0].rowSpan, 5); assert.equal(grid.rows[0].cells[0].verticalText, true);
  assert.deepEqual(grid.rows[1].cells[0].checkboxOptions.map(option => option.value), ['支票', '电汇', '预付']);
  assert.equal(grid.rows[2].cells[0].diagonalHeader.lowerRightText, '金额');
  fs.writeFileSync(path.join(output, 'desktop-blank-grid.json'), saved.body.contentHtml);
  await button('重新载入', '.report-designer-v3-header');
  await captureScreenshot(cdp, path.join(output, 'Full-blank-grid-designer.png'));
  await run("[...document.querySelectorAll('[role=tab]')].find(node=>node.textContent.trim()==='预览').click(); true");
  await wait("Boolean([...document.querySelectorAll('button')].find(node=>node.textContent.trim()==='样例预览'&&!node.disabled))");
  await button('样例预览', 'body');
  await wait("Boolean(document.querySelector('iframe[title=模板预览]')?.srcdoc.includes('用款事项'))");
  await captureScreenshot(cdp, path.join(output, 'Full-blank-grid-preview.png'));
  const payment = await request('CreatePayment', token, { voucherNo: 'BLANK-GRID-001', payeeName: '空白设计收款方', payerName: '空白设计付款单位', paymentDate: businessDate, paymentMethod: '电汇', cnyAmount: 128.5 });
  assert.equal(payment.status, 201);
  const pdf = path.join(output, 'desktop-blank-grid.pdf');
  const job = await request('StartPaymentVoucherPdfSaveToPathJob', token, { templatePath: `user-template:${created.body.id}`, destinationPath: pdf }, { paymentId: payment.body.payment.id });
  assert.equal(job.status, 202);
  await waitFor(() => fs.existsSync(pdf), 30000, 'Blank grid PDF did not complete');
  assert.equal(fs.readFileSync(pdf).subarray(0, 5).toString(), '%PDF-');
}
