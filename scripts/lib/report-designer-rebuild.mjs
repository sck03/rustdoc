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
    Image: api.createV3ImageElement, Line: api.createV3LineElement, Rectangle: api.createV3RectangleElement, PageNumber: api.createV3PageNumberElement,
    Flow: element => api.createV3FlowElement(element.flowKind === 'Grid' ? rebuildDesignerGrid(api, element.block) : api.copyReportDesignerBlock(element.block)) };
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
  return JSON.parse(JSON.stringify(value, (key, value) => key === 'zIndex' ? undefined : key === 'widthPercent' ? Math.fround(value) : ['id', 'columnId'].includes(key) ? ids.get(value) ?? value : value));
}
