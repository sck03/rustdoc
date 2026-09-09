import assert from "node:assert/strict";

export function verifyDesignerEditingMutations(api) {
  const schema = api.parseReportDesignerV3FromHtml("", "ExportDocument").schema;
  schema.layers.forEach(layer => { layer.elements = []; });
  const overlay = schema.layers.find(layer => layer.role === "Overlay");
  const header = schema.layers.find(layer => layer.role === "Header");
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
