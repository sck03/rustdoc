import assert from 'node:assert/strict';

export function verifyDetailTableContracts(api) {
  let table = api.createDetailTableBlock();
  table.columns.forEach((column, index) => { column.widthMm = [8.03, 13.27, 29.42, 80.1][index]; });
  const total = table.columns.reduce((sum, column) => sum + column.widthMm, 0);
  const equal = api.distributeDetailTableColumnWidths(table);
  assert(Math.abs(equal.columns.reduce((sum, column) => sum + column.widthMm, 0) - total) < 1e-10, 'equal columns preserve the exact reference width');
  const first = table.columns[0];
  first.contentKind = 'Composite';
  first.content = [{ id: 'condition-part', kind: 'Field', text: '', fieldPath: first.fieldPath, condition: { fieldPath: 'item.PoNumber', operator: 'HasValue', value: '' } }];
  const copy = api.duplicateDetailTableColumn(table, first.id).columns[1];
  copy.content[0].condition.value = 'changed';
  assert.equal(first.content[0].condition.value, '', 'copied conditions must not mutate the source or undo snapshot');
  while (table.columns.length < api.REPORT_DETAIL_MAX_COLUMNS) table = api.addDetailTableColumn(table);
  assert.equal(api.addDetailTableColumn(table), table);
  assert.equal(api.duplicateDetailTableColumn(table, table.columns[0].id), table);
  const schema = api.parseReportDesignerV3Source('', 'ExportDocument').schema;
  schema.layers.find(layer => layer.role === 'Body').elements = [api.createV3FlowElement({ ...table, columns: [...table.columns, { ...first, id: 'excess-column' }] })];
  assert(api.validateReportDesignerV3Draft(schema, 'ExportDocument').blocked, 'a manually supplied oversized table is rejected before editing');
}
