import assert from 'node:assert/strict';

// Rebuild through the editor's insertion, layer and grid operations. Reference
// values represent user-entered properties; no existing document is loaded as the draft.
export function rebuildDesignerDocument(api, reference) {
  const expected = api.parseReportDesignerV3Source(JSON.stringify(reference), reference.reportType).schema;
  let state = api.createReportDesignerV3DocumentState(api.parseReportDesignerV3Source('', expected.reportType).schema);
  state = api.updateV3Page(state, expected.page);
  state = api.updateV3Grid(state, expected.grid);
  if (expected.detailRowHeightHundredthMm !== undefined) state = { ...state, schema: { ...state.schema, detailRowHeightHundredthMm: expected.detailRowHeightHundredthMm } };
  const used = new Set(), layerIds = [];
  for (const layer of expected.layers) {
    let target = state.schema.layers.find(candidate => candidate.role === layer.role && !used.has(candidate.id));
    if (!target) { state = api.addV3Layer(state, layer.role); target = state.schema.layers.at(-1); }
    used.add(target.id); layerIds.push(target.id);
    state = api.updateV3Layer(state, target.id, { name: layer.name, print: layer.print, designHeightHundredthMm: layer.designHeightHundredthMm });
  }
  for (const layer of state.schema.layers.filter(layer => !used.has(layer.id))) state = api.removeV3Layer(state, layer.id);
  layerIds.forEach((id, index) => {
    while (state.schema.layers.findIndex(layer => layer.id === id) > index) state = api.moveV3Layer(state, id, -1);
  });
  const factories = { Text: api.createV3TextElement, Field: element => api.createV3FieldElement(element.fieldPath),
    Image: api.createV3ImageElement, Line: api.createV3LineElement, Rectangle: api.createV3RectangleElement, ProductSpacer: api.createV3ProductSpacerElement, PageNumber: api.createV3PageNumberElement,
    Flow: element => api.createV3FlowElement(rebuildBlock(api, element.block, expected.reportType)) };
  expected.layers.forEach((layer, index) => {
    for (const element of layer.elements) {
      const base = element.type === 'Field' || element.type === 'Flow' ? factories[element.type](element) : factories[element.type]();
      const draft = { ...base, ...element, id: base.id, ...(base.type === 'Flow' ? { block: base.block } : {}) };
      const next = api.insertV3Element(state, layerIds[index], draft);
      assert.notEqual(next.schema, state.schema, `${element.id}: insertion must be supported`);
      state = api.updateV3Element(next, base.id, draft);
    }
    state = api.updateV3Layer(state, layerIds[index], { visible: layer.visible, locked: layer.locked });
  });
  const actual = api.parseReportDesignerV3Source(JSON.stringify(state.schema), expected.reportType).schema;
  assert.equal(api.validateReportDesignerV3Draft(actual).blocked, false);
  assert.deepEqual(printableStructure(actual), printableStructure(expected), 'blank reconstruction must preserve all printable properties and stacking order');
  return actual;
}

function rebuildBlock(api, target, reportType) {
  if (target.type === 'Grid') return rebuildDesignerGrid(api, target);
  if (target.type === 'DetailTable') return rebuildDetailTable(api, target);
  const factory = { Conditional: () => api.createConditionalBlock(reportType), Row: api.createRowBlock, PageBreak: api.createPageBreakBlock }[target.type];
  assert(factory, `${target.type}: the blank editor needs a supported insertion operation`);
  const block = factory();
  const { id, ...properties } = target;
  return api.copyReportDesignerBlock({ ...block, ...properties });
}

function rebuildDetailTable(api, target) {
  let block = api.createDetailTableBlock();
  while (block.columns.length < target.columns.length) block = api.addDetailTableColumn(block);
  while (block.columns.length > target.columns.length) block = api.removeDetailTableColumn(block, block.columns.at(-1).id);
  const ids = new Map(target.columns.map((column, index) => [column.id, block.columns[index].id]));
  const columns = target.columns.map((column, index) => {
    const fresh = api.setDetailColumnContentKind(block.columns[index], column.contentKind ?? 'Field');
    return { ...fresh, ...column, id: fresh.id,
      content: column.content?.map(part => ({ ...part, id: api.createDetailTableCellContent(part.kind).id })) };
  });
  const row = targetRow => targetRow && { ...api.createDetailTableSummaryRow(columns), ...targetRow,
    cells: targetRow.cells.map(cell => ({ ...cell, columnId: ids.get(cell.columnId) })) };
  const { id, columns: unusedColumns, ...properties } = target;
  return { id: block.id, ...properties, columns,
    summaryRow: row(target.summaryRow), introRow: row(target.introRow),
    grouping: target.grouping && { ...target.grouping, footer: row(target.grouping.footer) },
    adjustments: target.adjustments && { descriptionColumnId: ids.get(target.adjustments.descriptionColumnId), amountColumnId: ids.get(target.adjustments.amountColumnId) } };
}

export function rebuildDesignerGrid(api, target) {
  let grid = api.resizeGridStructure(api.applyGridPreset(api.createGridBlock(), 'Blank'), target.rows.length, target.columns.length);
  const at = (row, column) => api.getGridCellLocations(grid).find(cell => cell.rowIndex === row && cell.columnIndex === column);
  for (const cell of api.getGridCellLocations(target)) {
    if (cell.rowSpan > 1 || cell.colSpan > 1) {
      const result = api.mergeGridSelection(grid, { cellId: at(cell.rowIndex, cell.columnIndex).cell.id,
        endCellId: at(cell.rowIndex + cell.rowSpan - 1, cell.columnIndex + cell.colSpan - 1).cell.id });
      assert.equal(result.notice, null);
      assert.notEqual(result.block, grid, 'each merged rectangle must be reachable in one edit');
      grid = result.block;
    }
    const id = at(cell.rowIndex, cell.columnIndex).cell.id;
    grid = api.updateGridCell(grid, id, () => ({ ...cell.cell, id }));
  }
  for (let index = 0; index < target.columns.length - 1; index++) grid = api.resizeAdjacentGridColumnWidths(grid, grid.columns[index].id, target.columns[index].widthPercent - grid.columns[index].widthPercent);
  const { id, columns, rows, ...properties } = target;
  return api.copyReportDesignerBlock({ ...grid, ...properties, rows: grid.rows.map((row, index) => ({ ...row, heightMm: rows[index].heightMm })) });
}

function printableStructure(document) {
  const value = structuredClone(document);
  value.layers.forEach(layer => layer.elements.sort((a, b) => a.zIndex - b.zIndex));
  const ids = new Map();
  function collect(value) {
    if (Array.isArray(value)) return value.forEach(collect);
    if (!value || typeof value !== 'object') return;
    if (value.id) ids.set(value.id, String(ids.size));
    Object.keys(value).sort().forEach(key => collect(value[key]));
  }
  collect(value);
  return JSON.parse(JSON.stringify(value, (key, value) => key === 'zIndex' ? undefined : key === 'widthPercent' ? Math.fround(value) : ['id', 'columnId', 'descriptionColumnId', 'amountColumnId'].includes(key) ? ids.get(value) ?? value : value));
}
