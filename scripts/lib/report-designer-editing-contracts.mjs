import assert from "node:assert/strict";

export function verifyDesignerEditingMutations(api) {
  verifyDetailColumnMutations(api);
  verifyLayerMutations(api);
  const schema = api.parseReportDesignerV3Source("", "ExportDocument").schema;
  schema.layers.forEach(layer => { layer.elements = []; });
  const overlay = schema.layers.find(layer => layer.role === "Overlay");
  const header = schema.layers.find(layer => layer.role === "Header");
  const footer = schema.layers.find(layer => layer.role === "Footer");
  const empty = api.createReportDesignerV3DocumentState(schema);
  const following = api.updateV3Layer(empty, footer.id, { print: { ...footer.print, pinToPageBottom: false, followBody: true } });
  assert.equal(following.schema.layers.find(layer => layer.id === footer.id).print.followBody, true);
  const renamed = api.updateV3Layer(following, footer.id, { name: "签字区" });
  assert.equal(renamed.schema.layers.find(layer => layer.id === footer.id).print.followBody, true, "renaming must preserve footer flow");
  const firstPage = api.updateV3Layer(renamed, header.id, { print: { ...header.print, firstPageOnly: true } });
  assert.equal(firstPage.schema.layers.find(layer => layer.id === header.id).print.firstPageOnly, true);
  assert.equal(api.updateV3Layer(firstPage, header.id, { visible: false }).schema.layers.find(layer => layer.id === header.id).print.firstPageOnly, true);
  assert.equal(api.updateV3Layer(firstPage, header.id, { print: { ...header.print, firstPageOnly: true } }), firstPage, "unchanged print options must not create undo entries");
  const pinned = api.updateV3Layer(renamed, footer.id, { print: { ...renamed.schema.layers.find(layer => layer.id === footer.id).print, pinToPageBottom: true } });
  assert.equal(pinned.schema.layers.find(layer => layer.id === footer.id).print.followBody, false, "pin and follow are mutually exclusive");
  const table = api.createV3FlowElement(api.createDetailTableBlock());
  const product = api.createV3FieldElement("item.Quantity");
  for (const [first, second] of [[table, product], [product, table]]) {
    const original = api.insertV3Element(empty, header.id, first);
    assert.match(api.getV3InsertionIssue(original, header.id, second), /不能混用/);
    assert.equal(api.insertV3Element(original, header.id, second), original, "incompatible insertion must retain the complete draft");
    assert.equal(api.pasteV3Elements(original, [second], header.id), original, "paste must obey the same product layout rule");
  }
  assert.equal(api.pasteV3Elements(empty, [table, product], header.id), empty, "a mixed clipboard must fail atomically");
  const body = schema.layers.find(layer => layer.role === "Body");
  const fullBody = { ...empty, schema: { ...schema, layers: schema.layers.map(layer => layer.id === body.id ? { ...layer, elements: Array.from({ length: api.REPORT_DESIGNER_V3_MAX_ELEMENTS_PER_LAYER }, (_, i) => ({ ...api.createV3TextElement(), id: `full-${i}` })) } : layer) } };
  assert.match(api.getV3InsertionIssue(fullBody, header.id, product), /该图层最多/, "capacity feedback must check the actual body destination");
  assert.equal(api.insertV3Element(fullBody, header.id, product), fullBody);
  const lockedBody = api.updateV3Layer(empty, body.id, { locked: true });
  assert.match(api.getV3InsertionIssue(lockedBody, header.id, product), /锁定/);
  const text = (id, x, width, extra = {}) => ({ ...api.createV3TextElement(x, 10000), id, widthHundredthMm: width, heightHundredthMm: 1000, ...extra });
  const state = (elements, selectedIds = elements.map(element => element.id)) => ({
    schema: { ...schema, layers: schema.layers.map(layer => layer.id === overlay.id ? { ...layer, elements } : layer) },
    activeLayerId: overlay.id, selectedIds,
  });
  const element = (document, id) => api.findV3Element(document.schema, id).element;
  const source = text("source", 1000, 4000);
  const original = state([source]);
  assert.equal(api.pasteV3Elements(original, [], header.id), original);
  const pasted = api.pasteV3Elements(original, [source], header.id);
  assert.equal(api.findV3Element(pasted.schema, pasted.selectedIds[0]).layer.id, header.id, "paste must use the chosen region");
  assert.notEqual(pasted.selectedIds[0], source.id);
  assert.equal(element(pasted, source.id), source);
  for (const patch of [{ locked: true }, { visible: false }]) {
    const blocked = { ...original, schema: { ...original.schema, layers: original.schema.layers.map(layer => layer.id === header.id ? { ...layer, ...patch } : layer) } };
    assert.equal(api.pasteV3Elements(blocked, [source], header.id), blocked, "an unavailable target must not silently redirect paste");
  }
  const locked = text("locked", 12000, 2000, { locked: true });
  const removed = api.deleteSelectedV3Elements(state([source, locked]));
  assert.deepEqual(removed.selectedIds, [locked.id]);
  assert.equal(api.deleteSelectedV3Elements(removed), removed);
  assert.deepEqual(api.deleteSelectedV3Elements(original).selectedIds, [], "deletion must not select an unrelated element");
  const hidden = api.updateSelectedV3ElementFlags(state([source, locked]), { visible: false });
  assert.equal(element(hidden, locked.id).visible, false, "element locks still allow visibility changes");
  assert.equal(element(hidden, locked.id).locked, true);
  const lockedLayer = { ...original, schema: { ...original.schema, layers: original.schema.layers.map(layer => ({ ...layer, locked: true })) } };
  assert.equal(api.updateSelectedV3ElementFlags(lockedLayer, { visible: false, locked: false }), lockedLayer, "batch flags cannot bypass layer locks");

  for (const direction of ["horizontal", "vertical"]) {
    const items = [text("wide", 1000, 6000), text("middle", 1800, 800), text("last", 4000, 1000)];
    if (direction === "vertical") items.forEach(item => { item.yHundredthMm = item.xHundredthMm; item.heightHundredthMm = item.widthHundredthMm; item.widthHundredthMm = 1000; });
    const before = state([...items, locked]);
    const after = api.distributeSelectedV3Elements(before, direction);
    assert.equal(element(after, items[0].id), items[0], "first anchor must remain unchanged when objects overlap");
    assert.equal(element(after, items[2].id), items[2], "last anchor must remain unchanged when objects overlap");
    assert.equal(element(after, locked.id), locked);
    const bounds = items.map(item => api.reportDesignerV3ElementBounds(element(after, item.id)));
    const gaps = bounds.slice(1).map((box, index) => direction === "horizontal" ? box.left - bounds[index].right : box.top - bounds[index].bottom);
    assert(Math.abs(gaps[0] - gaps[1]) <= 1, "signed edge gaps must be equal");
  }

  const reference = text("reference", 7000, 2000, { heightHundredthMm: 1800 });
  const sizing = state([source, reference, locked], [reference.id, source.id, locked.id]);
  const resized = api.matchSelectedV3ElementSize(sizing, "both");
  assert.equal(element(resized, source.id).widthHundredthMm, reference.widthHundredthMm);
  assert.equal(element(resized, source.id).heightHundredthMm, reference.heightHundredthMm);
  assert.equal(element(resized, locked.id), locked);
  assert.equal(api.matchSelectedV3ElementSize(resized, "both"), resized, "matching equal sizes must not add history");
  const flow = { ...api.createV3FlowElement(api.createGridBlock()), id: "grid" };
  const styling = state([source, reference, locked, flow]);
  const style = { fontSizePt: 18, bold: true, align: "Right", color: "#334455" };
  const styled = api.applySelectedV3ElementStyle(styling, style);
  assert.deepEqual(element(styled, source.id).style, style);
  assert.equal(element(styled, source.id).xHundredthMm, source.xHundredthMm);
  assert.equal(element(styled, source.id).text, source.text);
  assert.equal(element(styled, locked.id), locked);
  assert.equal(element(styled, flow.id), flow, "table styling belongs to its cells");
  assert.notEqual(element(styled, source.id).style, style);
  assert.equal(api.applySelectedV3ElementStyle(styled, style), styled);
}

function verifyLayerMutations(api) {
  const original = api.createReportDesignerV3DocumentState(api.parseReportDesignerV3Source("", "ExportDocument").schema);
  let state = api.addV3Layer(original, "Footer");
  const footer = state.schema.layers.at(-1);
  assert.equal(state.activeLayerId, footer.id);
  assert.deepEqual(state.selectedIds, []);
  assert.notEqual(footer.name, original.schema.layers.find(layer => layer.role === "Footer").name);
  state = api.updateV3Layer(state, footer.id, { name: "末页签章", print: { ...footer.print, repeatOnEveryPage: false, followBody: true } });
  const moved = api.moveV3Layer(state, footer.id, -1);
  assert.equal(moved.schema.layers.at(-2).id, footer.id);
  assert.equal(moved.schema.layers.at(-2).print.followBody, true);
  assert.equal(api.parseReportDesignerV3Source(JSON.stringify(moved.schema), "ExportDocument").issues.some(issue => issue.severity === "error"), false);
  const removed = api.removeV3Layer(moved, footer.id);
  assert.deepEqual(removed.schema.layers, original.schema.layers);
  assert(removed.schema.layers.some(layer => layer.id === removed.activeLayerId));
  for (const layer of original.schema.layers.filter(layer => ["Body", "Overlay"].includes(layer.role))) {
    assert.equal(api.removeV3Layer(original, layer.id), original, "required regions must survive deletion");
  }
  const locked = api.updateV3Layer(state, footer.id, { locked: true });
  assert.equal(api.removeV3Layer(locked, footer.id), locked);
  assert.equal(api.moveV3Layer(locked, footer.id, -1), locked);
  assert.equal(api.moveV3Layer(locked, locked.schema.layers.at(-2).id, 1), locked, "ordering cannot cross a locked layer");
  const populated = api.insertV3Element(state, footer.id, api.createV3TextElement());
  assert.equal(api.removeV3Layer(populated, footer.id), populated, "layer removal must not discard content");
  assert.equal(api.moveV3Layer(original, original.schema.layers[0].id, -1), original);
  while (state.schema.layers.length < api.REPORT_DESIGNER_V3_MAX_LAYER_COUNT) state = api.addV3Layer(state, "Header");
  assert.equal(api.addV3Layer(state, "Header"), state, "the editor must honor the shared layer limit");
  assert.equal(new Set(state.schema.layers.map(layer => layer.id)).size, state.schema.layers.length);
}

function verifyDetailColumnMutations(api) {
  const block = api.createDetailTableBlock();
  const original = block.columns[0];
  original.omitEmptyLines = true;
  original.border = { color: "#123456", widthPx: 1, top: true };
  const composite = api.setDetailColumnContentKind(original, "Composite");
  assert.equal(composite.content[0].fieldPath, original.fieldPath, "switching mode must not replace the selected field with a default");
  composite.content[0].visible = false;
  block.columns[0] = composite;
  assert.deepEqual(api.setDetailColumnContentKind(api.setDetailColumnContentKind(composite, "Field"), "Composite"), composite, "switching away and back retains the composition");
  const cell = { columnId: original.id, contentKind: "Text", text: "保留说明", fieldPath: "", suffix: "KGS" };
  block.introRow = { label: "说明", labelColumnSpan: 2, cells: [cell], style: {} };
  block.summaryRow = { ...block.introRow, label: "TOTAL" };
  block.grouping = { ...api.createDetailTableGrouping(), footer: { ...block.introRow, cells: [{ ...cell, contentKind: "Count" }] } };
  const before = structuredClone(block);
  const copied = api.duplicateDetailTableColumn(block, original.id);
  const copy = copied.columns[1];
  assert.notEqual(copy.id, original.id);
  assert.equal(copy.omitEmptyLines, true);
  assert.notEqual(copy.content[0].id, composite.content[0].id);
  assert.equal(copy.content[0].visible, false);
  for (const [source, target] of [[block.introRow, copied.introRow], [block.summaryRow, copied.summaryRow], [block.grouping.footer, copied.grouping.footer]]) {
    assert.equal(target.labelColumnSpan, source.labelColumnSpan + 1, "copying within a merged label must keep its existing columns inside the label");
    assert.deepEqual(target.cells.find(cell => cell.columnId === copy.id), { ...source.cells[0], columnId: copy.id });
  }
  copy.border.color = "#ffffff";
  assert.deepEqual(block, before, "copying and editing must not mutate the source or undo history");
  const moved = api.moveDetailTableColumn(copied, copy.id, "down");
  assert.equal(moved.columns[2].id, copy.id);
  const removed = api.removeDetailTableColumn(moved, copy.id);
  assert.deepEqual(removed, before, "removal must also remove copied intro, total and subtotal cells");
  assert.equal(api.removeDetailTableColumn(block, "missing"), block);
  assert.equal(api.duplicateDetailTableColumn(block, "missing"), block);
  const added = api.addDetailTableColumn(block);
  assert.equal(added.columns.length, block.columns.length + 1);
  assert.deepEqual(added.introRow, block.introRow);
  const removedOutsideLabel = api.removeDetailTableColumn(block, block.columns.at(-1).id);
  assert.equal(removedOutsideLabel.summaryRow.labelColumnSpan, 2);
  const sparse = { ...block, introRow: { ...block.introRow, cells: [] } };
  assert.equal(api.duplicateDetailTableColumn(sparse, original.id).introRow.labelColumnSpan, 3, "empty merged labels also follow inserted columns");
  const doc = api.parseReportDesignerV3Source("", "ExportDocument").schema;
  doc.layers.forEach(layer => { layer.elements = layer.role === "Body" ? [api.createV3FlowElement(removed)] : []; });
  assert.equal(api.validateReportDesignerV3Draft(doc, "ExportDocument").blocked, false, "edited table must remain saveable");
}
