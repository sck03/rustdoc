import assert from 'node:assert/strict';

export function verifyGridTextImport(api) {
  assert.deepEqual(api.parseGridText('标题\t"多行\n文字"\r\n内容\t"他说""可以"""\r\n'), [['标题', '多行\n文字'], ['内容', '他说"可以"']]);
  for (const invalid of ['"未结束', 'a\tb\nc', 'x\t'.repeat(101), 'x'.repeat(api.MAX_GRID_PASTE_CHARACTERS + 1)]) assert.throws(() => api.parseGridText(invalid));
  let grid = api.applyGridPreset(api.createGridBlock(), 'Blank');
  const source = structuredClone(grid), cellId = grid.rows[1].cells[1].id;
  const result = api.importGridText(grid, cellId, '收款人\t项目\t金额\n张三\t差旅费\t100.00');
  assert.equal(result.notice, null);
  assert.equal(result.block.columns.length, 4);
  assert.equal(result.block.rows[1].cells[1].text, '收款人');
  assert.equal(result.block.rows[2].cells[3].text, '100.00');
  assert.deepEqual(grid, source, 'the original undo snapshot remains untouched');
  assert.equal(result.block.rows[1].cells[1].style, grid.rows[1].cells[1].style);
  assert.equal(result.block.rows[1].cells[0], grid.rows[1].cells[0]);
  assert.equal(api.importGridText(result.block, cellId, '收款人\t项目\t金额\n张三\t差旅费\t100.00').block, result.block, 'identical content is not a new edit');

  const merged = api.mergeGridCellRight(grid, grid.rows[0].cells[0].id);
  const conflict = api.importGridText(merged, merged.rows[0].cells[0].id, '甲\t乙');
  assert(conflict.notice); assert.equal(conflict.block, merged, 'merged-cell conflicts refuse the whole import');
  assert.equal(api.importGridText(merged, merged.rows[0].cells[0].id, '标题\t').notice, null);
  grid.rows[0].cells[1] = { ...grid.rows[0].cells[1], contentKind: 'Field', fieldPath: 'Payment.Amount' };
  const protectedField = api.importGridText(grid, grid.rows[0].cells[0].id, '标题\t100');
  assert(protectedField.notice); assert.equal(protectedField.block, grid, 'pasting text cannot silently replace business bindings');
  assert.equal(api.importGridText(grid, 'missing', 'x').block, grid);

  const document = api.parseReportDesignerV3Source('', 'PaymentVoucher').schema;
  document.layers.find(layer => layer.role === 'Body').elements = [api.createV3FlowElement(result.block)];
  const readBack = api.parseReportDesignerV3Source(JSON.stringify(document), 'PaymentVoucher').schema;
  assert.equal(api.validateReportDesignerV3Draft(readBack, 'PaymentVoucher').blocked, false);
  assert.deepEqual(readBack.layers.find(layer => layer.role === 'Body').elements[0].block.rows.map(row => row.cells.map(cell => cell.text)), result.block.rows.map(row => row.cells.map(cell => cell.text)));
}
