import assert from 'node:assert/strict';

export async function verifyGridTextUi({ page, url, read, waitFor, click, results }) {
  await page.send('Page.navigate', { url: `${url}?blank=1&payment=1` });
  await waitFor(page, 'document.querySelector("[data-v3-page-canvas]") && window.__designerDraftState');
  await click(page, '[aria-label="普通表格"]');
  const block = `window.__designerSchema.layers.flatMap(layer=>layer.elements).find(element=>element.flowKind==='Grid').block`;
  await waitFor(page, `${block}.rows.length===3`);
  await read(page, `[...document.querySelectorAll('summary')].find(node=>node.textContent==='从表格粘贴文字').parentElement.open=true`);
  const textarea = `document.querySelector('.report-designer-property-section textarea[placeholder^="从 Excel"]')`;
  const before = await read(page, 'window.__designerHtml');
  const updates = await read(page, 'window.__designerUpdates');
  await read(page, `${textarea}.focus()`);
  await page.send('Input.insertText', { text: '项目\t说明\t币种\t金额\n差旅费\t"交通\n住宿"\tCNY\t123.45' });
  assert.equal(await read(page, 'window.__designerUpdates'), updates, 'typing TSV does not serialize or mutate the template');
  await read(page, `[...document.querySelectorAll('button')].find(node=>node.textContent==='填入表格').click()`);
  await waitFor(page, `${block}.columns.length===4 && ${block}.rows[1].cells[3].text==='123.45'`);
  assert.equal(await read(page, `${block}.rows[1].cells[1].text`), '交通\n住宿');
  assert.equal(await read(page, 'window.__designerUpdates'), updates + 1);
  assert(await read(page, 'window.__designerDraftState.isValid'));
  await click(page, '[aria-label="撤销"]');
  await waitFor(page, `window.__designerHtml===${JSON.stringify(before)}`);
  await click(page, '[aria-label="重做"]');
  await waitFor(page, `${block}.columns.length===4 && ${block}.rows[1].cells[3].text==='123.45'`);
  results.push({ test: 'generic pasted table preserves multiline cells, expands columns and supports one-step undo/redo', passed: true });
}
