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
  const at = (grid, row, column) => api.getGridCellLocations(grid).find(cell => cell.rowIndex === row && cell.columnIndex === column);
  const geometry = grid => api.getGridCellLocations(grid).map(({ rowIndex, columnIndex, rowSpan, colSpan }) => [rowIndex, columnIndex, rowSpan, colSpan]);
  // Rebuild every default grid through the same resize/merge/width operations as the UI.
  for (const [name, source] of defaultReportDesigns()) {
    const document = api.parseReportDesignerV3Source(JSON.stringify(source), source.reportType).schema;
    for (const element of document.layers.flatMap(layer => layer.elements).filter(element => element.flowKind === 'Grid')) {
      const target = element.block;
      let grid = api.resizeGridStructure(blank(), target.rows.length, target.columns.length);
      const targetGeometry = api.getGridCellLocations(target);
      for (const cell of targetGeometry) {
        for (let row = cell.rowIndex; row < cell.rowIndex + cell.rowSpan; row++) {
          while (at(grid, row, cell.columnIndex).colSpan < cell.colSpan) {
            const next = api.mergeGridCellRight(grid, at(grid, row, cell.columnIndex).cell.id);
            assert.notEqual(next, grid, `${name}/${element.id}: horizontal merge remains reachable`);
            grid = next;
          }
        }
        while (at(grid, cell.rowIndex, cell.columnIndex).rowSpan < cell.rowSpan) {
          const next = api.mergeGridCellDown(grid, at(grid, cell.rowIndex, cell.columnIndex).cell.id);
          assert.notEqual(next, grid, `${name}/${element.id}: vertical merge remains reachable`);
          grid = next;
        }
        const id = at(grid, cell.rowIndex, cell.columnIndex).cell.id;
        grid = api.updateGridCell(grid, id, () => ({ ...cell.cell, id }));
      }
      for (let index = 0; index < target.columns.length - 1; index++) {
        grid = api.resizeAdjacentGridColumnWidths(grid, grid.columns[index].id, target.columns[index].widthPercent - grid.columns[index].widthPercent);
      }
      assert.deepEqual(geometry(grid), geometry(target), `${name}/${element.id}: blank authoring reproduces all merged cells`);
      grid.columns.forEach((column, index) => assert(Math.abs(column.widthPercent - target.columns[index].widthPercent) < 0.01, `${name}/${element.id}: column width is reachable`));
      const state = api.parseReportDesignerV3Source('', document.reportType).schema;
      state.layers.find(layer => layer.role === 'Body').elements = [api.createV3FlowElement(grid)];
      assert.equal(api.validateReportDesignerV3Draft(state, document.reportType).blocked, false, `${name}/${element.id}: reconstructed grid remains printable`);
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
}
