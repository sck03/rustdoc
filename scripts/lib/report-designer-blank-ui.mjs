import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { captureScreenshot } from './web-runtime-browser-session.mjs';

export async function verifyBlankDesigner({ page, url, read, waitFor, click, key, results, output }) {
  const inspector = '.report-designer-v3-inspector';
  const control = (label, scope = inspector) => `[...document.querySelectorAll(${JSON.stringify(scope + ' label')})].find(node=>node.querySelector('span')?.textContent.trim()===${JSON.stringify(label)}).querySelector('input,select,textarea')`;
  const select = async (label, value, scope) => {
    await read(page, `(()=>{const node=${control(label, scope)};node.value=${JSON.stringify(value)};node.dispatchEvent(new Event('change',{bubbles:true}));})()`);
    await waitFor(page, `${control(label, scope)}.value===${JSON.stringify(value)}`);
  };
  const fill = async (label, value, scope) => {
    await read(page, `(()=>{const node=${control(label, scope)};node.focus();node.select();})()`);
    await page.send('Input.insertText', { text: String(value) });
    await key(page, 'Enter');
  };
  const open = async (label, scope) => read(page, `(()=>{const node=[...document.querySelectorAll(${JSON.stringify(scope + ' summary')})].find(node=>node.textContent.trim().startsWith(${JSON.stringify(label)}));if(!node.parentElement.open)node.click();})()`);
  const button = async (label, scope) => read(page, `[...document.querySelectorAll(${JSON.stringify(scope + ' button')})].find(node=>node.textContent.trim()===${JSON.stringify(label)}).click()`);
  const check = async (label, scope) => read(page, `[...document.querySelectorAll(${JSON.stringify(scope + ' label')})].find(node=>node.textContent.trim().startsWith(${JSON.stringify(label)})).querySelector('input').click()`);
  const side = '.report-designer-v3-layer-list';
  const active = side + ' .report-designer-v3-layer-row.is-active';
  const elements = 'window.__designerSchema.layers.flatMap(layer=>layer.elements)';
  await page.send('Page.navigate', { url: `${url}?blank=1` });
  await waitFor(page, 'document.querySelector("[data-v3-page-canvas]") && window.__designerDraftState');
  assert.equal(await read(page, `${elements}.length`), 0, 'start with the actual blank document');
  await select('页面字体', 'Noto Serif CJK SC');
  await fill('默认字号 pt', 9.5);
  await waitFor(page, 'window.__designerSchema.page.fontSizePt===9.5');

  await click(page, '.report-designer-v3-sidebar-tabs button:nth-child(3)');
  await open('新增图层', side);
  await button('添加图层', side);
  await waitFor(page, 'window.__designerSchema.layers.length===5');
  await open('图层设置', active);
  await fill('图层名称', '末页合计与签章', active);
  await fill('图层名称', '   ', active);
  await waitFor(page, `${control('图层名称', active)}.value==='末页合计与签章'`);
  await open('打印行为', active);
  await check('每页重复', active);
  await check('跟随正文', active);
  await waitFor(page, 'window.__designerSchema.layers.at(-1).print.followBody && !window.__designerSchema.layers.at(-1).print.repeatOnEveryPage');
  await select('新增图层类型', 'Header', side);
  await button('添加图层', side);
  await waitFor(page, 'window.__designerSchema.layers.length===6');
  await open('图层设置', active);
  await fill('图层名称', '首页唛头', active);
  await fill('设计区高度 (mm)', 60, active);
  await open('打印行为', active);
  await check('仅首页输出', active);
  await waitFor(page, 'window.__designerSchema.layers.at(-1).print.firstPageOnly');
  const authoredLayers = await read(page, 'window.__designerHtml');
  await button('上移图层', active);
  await waitFor(page, 'window.__designerSchema.layers.at(-2).name==="首页唛头"');
  await click(page, '[aria-label="撤销"]');
  await waitFor(page, `window.__designerHtml===${JSON.stringify(authoredLayers)}`);
  await button('删除空图层', active);
  await waitFor(page, 'window.__designerSchema.layers.length===5');
  await click(page, '[aria-label="撤销"]');
  await waitFor(page, `window.__designerHtml===${JSON.stringify(authoredLayers)}`);
  results.push({ test: 'blank template authors independent first-page and final-page layers with undo', passed: true });

  await click(page, '[aria-label="选择字段"]');
  await waitFor(page, "document.querySelector('[aria-label=\"插入字段 发票号\"]')");
  await click(page, '[aria-label="插入字段 发票号"]');
  await select('垂直对齐', 'Bottom');
  await select('字体', 'serif');
  await fill('字号 pt', 7);
  await waitFor(page, `${elements}.find(element=>element.type==='Field')?.style.verticalAlign==='Bottom'`);
  assert.equal(await read(page, 'document.querySelector(".report-designer-v3-element-field").style.alignItems'), 'flex-end');
  await click(page, '[aria-label="线"]');
  await fill('线宽 px', 0.6);
  await select('线型', 'Dashed');
  await waitFor(page, `${elements}.find(element=>element.type==='Line')?.style.borderWidthPx===0.6`);
  await fill('线宽 px', 0.8);
  await select('线型', 'Solid');
  await waitFor(page, `${elements}.find(element=>element.type==='Line')?.style.borderWidthPx===0.8`);
  assert.equal(await read(page, 'document.querySelector(".report-designer-v3-preview-line").style.height'), '0.8px');
  results.push({ test: 'blank fields support bottom alignment and lines preserve fractional widths', passed: true });

  await click(page, '.report-designer-v3-sidebar-tabs button:nth-child(3)');
  await click(page, `${side} [data-layer-role="Body"] .report-designer-v3-layer-name`);
  await open('高级排版', '.report-designer-v3-toolbar');
  await click(page, '[aria-label="普通表格"]');
  await open('字体', inspector);
  await select('表格字体', 'Noto Serif CJK SC');
  assert.equal(await read(page, 'document.querySelector(".report-designer-v3-element-flow").style.fontFamily'), '"Noto Serif CJK SC"');
  await read(page, "(()=>{const node=document.querySelector('[aria-label=\"表格文字颜色高级色值\"]');node.focus();node.select();})()");
  await page.send('Input.insertText', { text: '#000000' });
  await key(page, 'Enter');
  await waitFor(page, `${elements}.find(element=>element.flowKind==='Grid').style.color==='#000000'`);
  assert.equal(await read(page, 'document.querySelector(".report-designer-v3-element-flow").style.color'), 'rgb(0, 0, 0)');
  await button('整张表', inspector);
  await open('文字间距', inspector);
  await fill('左距(mm)', 1);
  await fill('右距(mm)', 1);
  await waitFor(page, `${elements}.find(element=>element.flowKind==='Grid').block.defaultCellStyle.marginRightMm===1`);
  assert.equal(await read(page, `${elements}.find(element=>element.flowKind==='Grid').block.defaultCellStyle.marginLeftMm`), 1);
  await open('精确列宽', inspector);
  await fill('列宽 (%)', 12.5);
  await waitFor(page, `${elements}.find(element=>element.flowKind==='Grid').block.columns[0].widthPercent===12.5`);
  const widths = await read(page, `${elements}.find(element=>element.flowKind==='Grid').block.columns`);
  await select('调整列', widths.at(-1).id);
  const lastWidth = Math.round((widths.at(-1).widthPercent + 1) * 100) / 100;
  await fill('列宽 (%)', lastWidth);
  await waitFor(page, `${elements}.find(element=>element.flowKind==='Grid').block.columns.at(-1).widthPercent===${lastWidth}`);
  const total = await read(page, `${elements}.find(element=>element.flowKind==='Grid').block.columns.reduce((sum,column)=>sum+column.widthPercent,0)`);
  assert(Math.abs(total - 100) < 0.01, 'precise column editing preserves the total width');
  results.push({ test: 'blank grid supports fonts, text color, horizontal padding and precise column widths', passed: true });

  const content = await read(page, 'window.__designerHtml');
  const saved = await read(page, 'window.__designerSchema');
  fs.writeFileSync(path.join(output, 'blank-authored-document.json'), content);
  const restore = await page.send('Page.addScriptToEvaluateOnNewDocument', { source: `window.__restoredCommercial=${JSON.stringify(content)}` });
  try {
    await page.send('Page.navigate', { url: `${url}?blank=1` });
    await waitFor(page, 'window.__designerDraftState && document.querySelector("[data-v3-page-canvas]")');
    assert.deepEqual(await read(page, 'window.__designerSchema'), saved, 'reopening must preserve all authored layers, fonts, widths and alignments');
    assert.deepEqual(await read(page, 'window.__designerErrors'), []);
    await captureScreenshot(page, path.join(output, 'blank-authored-reopened.png'));
  } finally {
    await page.send('Page.removeScriptToEvaluateOnNewDocument', { identifier: restore.identifier });
  }
  results.push({ test: 'blank authored document reopens with identical printable properties', passed: true });
}
