import assert from 'node:assert/strict';
import { defaultReportDesigns } from './default-report-designs.mjs';

export function verifyGridAuthoringContracts(api) {
  const document = api.parseReportDesignerV3Source('', 'ExportDocument').schema;
  const header = document.layers.find(layer => layer.role === 'Header');
  let copyState = api.insertV3Element(api.createReportDesignerV3DocumentState(document), header.id, api.createV3FlowElement(api.createGridBlock()));
  copyState = api.duplicateSelectedV3Elements(copyState);
  const copied = copyState.schema.layers.find(layer => layer.id === header.id).elements;
  const identifiers = block => [block.id, ...block.columns.map(column => column.id), ...block.rows.flatMap(row => [row.id, ...row.cells.map(cell => cell.id)])];
  const sourceIds = new Set(identifiers(copied[0].block));
  assert(identifiers(copied[1].block).every(id => !sourceIds.has(id)), 'duplicating a grid must create new block, row, column and cell identities accepted by Rust');
  for (const [, source] of defaultReportDesigns()) for (const element of source.layers.flatMap(layer => layer.elements)) {
    if (element.type !== 'Flow') continue;
    const copy = api.copyReportDesignerBlock(element.block);
    assert.notEqual(copy.id, element.block.id);
    if (copy.type === 'DetailTable') {
      const columns = new Map(element.block.columns.map((column, index) => [column.id, copy.columns[index].id]));
      for (const key of ['summaryRow', 'introRow']) if (copy[key]) assert.deepEqual(copy[key].cells.map(cell => cell.columnId), element.block[key].cells.map(cell => columns.get(cell.columnId)));
      assert.deepEqual(copy.columns.map(column => column.fieldPath), element.block.columns.map(column => column.fieldPath));
    }
  }
  for (let count = 1; count <= 100; count++) {
    const widths = api.createGridColumns(count).map(column => column.widthPercent);
    assert(Math.abs(widths.reduce((sum, width) => sum + width, 0) - 100) < 0.001, 'rounded widths always cover the full table');
    assert(Math.max(...widths) - Math.min(...widths) < 0.101, 'equal columns only differ by the editable rounding unit');
  }
  const blank = () => api.applyGridPreset(api.createGridBlock(), 'Blank');
  const roundingCase = blank();
  roundingCase.columns.forEach((column, index) => { column.widthPercent = [9.06, 1, 90.04][index]; });
  const roundTrip = api.parseReportDesignerV3Source('', 'ExportDocument').schema;
  roundTrip.layers.find(layer => layer.role === 'Body').elements = [api.createV3FlowElement(roundingCase)];
  const normalized = api.parseReportDesignerV3Source(JSON.stringify(roundTrip), 'ExportDocument').schema;
  assert(normalized.layers.find(layer => layer.role === 'Body').elements[0].block.columns.every(column => column.widthPercent >= 1), 'rounding must preserve the minimum editable width');
  assert.equal(api.validateReportDesignerV3Draft(normalized, 'ExportDocument').blocked, false);
  const geometry = grid => api.getGridCellLocations(grid).map(({ rowIndex, columnIndex, rowSpan, colSpan }) => [rowIndex, columnIndex, rowSpan, colSpan]);
  // Editing any track must preserve merged content, identities and row metadata.
  // Full blank reconstruction is exercised by report-designer-rebuild, not duplicated here.
  for (const [name, source] of defaultReportDesigns()) {
    const document = api.parseReportDesignerV3Source(JSON.stringify(source), source.reportType).schema;
    for (const element of document.layers.flatMap(layer => layer.elements).filter(element => element.flowKind === 'Grid')) {
      const target = element.block;
      const snapshot = structuredClone(target);
      for (const axis of ['row', 'column']) for (let index = 0; index <= (axis === 'row' ? target.rows.length : target.columns.length); index++) {
        const inserted = api.editGridTrack(target, axis, index, 'insert');
        assert.notEqual(inserted, target);
        const restored = api.editGridTrack(inserted, axis, index, 'delete');
        assert.deepEqual(geometry(restored), geometry(target), `${name}/${axis}/${index}: merged geometry survives insert/delete`);
        assert.deepEqual(restored.rows, target.rows, 'retained cell content, style and row metadata survive');
        restored.columns.forEach((column, index) => assert(Math.abs(column.widthPercent - target.columns[index].widthPercent) < 0.00001));
        const state = api.parseReportDesignerV3Source('', document.reportType).schema;
        state.layers.find(layer => layer.role === 'Body').elements = [api.createV3FlowElement(inserted)];
        assert.equal(api.validateReportDesignerV3Draft(state, document.reportType).blocked, false, `${name}/${axis}/${index}: inserted track remains printable`);
      }
      assert.deepEqual(target, snapshot, 'structural edits do not mutate the undo snapshot');
    }
  }

  let grid = api.resizeGridStructure(blank(), 3, 1);
  const id = grid.rows[0].cells[0].id;
  grid = api.mergeGridCellDown(api.mergeGridCellDown(grid, id), id);
  assert.equal(grid.rows[0].cells[0].rowSpan, 3);
  assert(grid.rows.slice(1).every(row => row.cells.length === 0), 'fully covered rows are valid');
  const split = api.splitGridCell(grid, id);
  assert(split.rows.every(row => row.cells.length === 1), 'a full-height merged cell can be split again');
  const shrunk = api.resizeGridStructure(grid, 2, 1);
  assert.equal(shrunk.rows[0].cells[0].rowSpan, 2, 'shrinking clips spans without losing the retained content');

  const original = blank();
  original.columns.forEach((column, index) => { column.widthPercent = [1, 49, 50][index]; });
  original.rows[0].cells[0].text = '保留的内容';
  original.rows[2].heightMm = 12;
  const snapshot = structuredClone(original);
  const expanded = api.resizeGridStructure(original, 5, 7);
  assert.deepEqual(original, snapshot, 'editing must not mutate the undo snapshot');
  assert.equal(expanded.rows[0].cells[0], original.rows[0].cells[0]);
  assert(expanded.columns.every(column => column.widthPercent >= 1));
  assert(Math.abs(expanded.columns.reduce((sum, column) => sum + column.widthPercent, 0) - 100) < 0.001);
  assert.equal(expanded.rows[4].heightMm, 12);
  assert.deepEqual(expanded.rows[4].cells[0].style, {}, 'new cells inherit the table text style');
  assert.equal(expanded.rows[4].cells[0].border, undefined, 'new cells inherit the table border');
  const restored = api.resizeGridStructure(expanded, 3, 3);
  assert.equal(restored.rows[0].cells[0], original.rows[0].cells[0]);
  assert.equal(api.resizeGridStructure(original, 3, 3), original, 'unchanged dimensions do not create an undo entry');
  for (const [rows, columns] of [[0, 3], [1001, 3], [3, 0], [3, 101], [NaN, 3], [3, Infinity], [3.5, 3]]) {
    assert.equal(api.resizeGridStructure(original, rows, columns), original, 'invalid dimensions preserve the document');
  }
  let merged = blank();
  const mergedId = merged.rows[0].cells[0].id;
  merged = api.mergeGridCellRight(merged, mergedId);
  merged = api.mergeGridCellRight(merged, merged.rows[1].cells[0].id);
  merged = api.mergeGridCellDown(merged, mergedId);
  merged = api.updateGridCell(merged, mergedId, cell => ({ ...cell, text: '合并内容保留' }));
  for (const axis of ['row', 'column']) for (const index of [0, 1]) {
    const removed = api.editGridTrack(merged, axis, index, 'delete');
    const retained = api.getGridCellLocations(removed).find(location => location.cell.id === mergedId);
    assert.equal(retained.cell.text, '合并内容保留', 'deleting the anchor track preserves a spanning cell');
    assert.equal(retained[axis === 'row' ? 'rowSpan' : 'colSpan'], 1);
  }
  const single = api.resizeGridStructure(blank(), 1, 1);
  for (const axis of ['row', 'column']) {
    assert.equal(api.editGridTrack(single, axis, 0, 'delete'), single);
    for (const index of [-1, 2, NaN, 0.5]) assert.equal(api.editGridTrack(single, axis, index, 'insert'), single);
  }
  assert.equal(api.gridCellAt(api.getGridCellLocations(merged), 1, 1).cell.id, mergedId);
  for (let column = 0; column < 100; column++) {
    assert.deepEqual(api.parseGridCellAddress(api.formatGridCellAddress(999, column)), { rowIndex: 999, columnIndex: column });
  }
  for (const address of ['d4', '4,4', ' 4，4 ']) assert.deepEqual(api.parseGridCellAddress(address), { rowIndex: 3, columnIndex: 3 });
  for (const address of ['', 'A0', 'A1001', 'CW1', 'ZZ1', '-1,2', '1,0', '1.5,2']) assert.equal(api.parseGridCellAddress(address), null);
  assert.equal(api.setGridRowsToUniformHeight(single, single.rows[0].heightMm), single);

  for (const reportType of ['ExportDocument', 'PaymentVoucher']) {
    let state = api.createReportDesignerV3DocumentState(api.parseReportDesignerV3Source('', reportType).schema);
    const element = api.createV3FlowElement(blank());
    state = api.insertV3Element(state, state.activeLayerId, element);
    const target = { elementId: element.id, cellId: element.block.rows[0].cells[0].id };
    const field = reportType === 'ExportDocument' ? 'Invoice.InvoiceNo' : 'Payment.PayeeName';
    const result = api.bindV3GridCellField(state, target, field);
    assert.equal(result.notice, null);
    assert.equal(result.state.schema.layers.flatMap(layer => layer.elements).length, 1, 'binding does not add a floating component');
    const bound = api.findV3Element(result.state.schema, element.id).element.block;
    assert.equal(bound.rows[0].cells[0].fieldPath, field);
    assert.equal(bound.rows[1], element.block.rows[1], 'binding shares unchanged rows');
    assert.equal(api.bindV3GridCellField(result.state, target, field).state, result.state, 'rebinding the same field is a no-op');
    for (const invalid of ['item.Quantity', reportType === 'ExportDocument' ? 'Payment.PayeeName' : 'Invoice.InvoiceNo']) {
      assert.equal(api.bindV3GridCellField(state, target, invalid).state, state, 'incompatible fields preserve the draft');
    }
    const locked = api.updateV3Element(state, element.id, { locked: true });
    assert.equal(api.bindV3GridCellField(locked, target, field).state, locked);
    const invalid = api.updateV3Element(state, element.id, { block: api.updateGridCell(element.block, target.cellId, cell => ({ ...cell, contentKind: 'Field', fieldPath: 'item.Quantity' })) });
    assert.equal(api.validateReportDesignerV3Draft(invalid.schema, reportType).blocked, true, 'fixed cells reject a missing item context before saving');
  }
  const repeating = api.parseReportDesignerV3Source('', 'ExportDocument').schema;
  const detail = api.createDetailTableBlock();
  detail.columns[0].fieldPath = 'Invoice.InvoiceNo';
  repeating.layers.find(layer => layer.role === 'Body').elements = [api.createV3FlowElement(detail)];
  assert.equal(api.validateReportDesignerV3Draft(repeating, 'ExportDocument').blocked, true, 'a repeating column rejects document-level primary bindings before native save');
}
