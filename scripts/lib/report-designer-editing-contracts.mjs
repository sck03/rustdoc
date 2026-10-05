import assert from "node:assert/strict";

export function verifyDesignerEditingMutations(api) {
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
