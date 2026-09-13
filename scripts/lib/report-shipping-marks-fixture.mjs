/** One scenario shared by the exporter and real browser checks. */
export function createShippingMarksScenario(api) {
  const fieldPath = "Invoice.ShippingMarks";
  const schema = api.parseReportDesignerV3FromHtml("", "ExportDocument").schema;
  schema.layers.forEach(layer => { layer.elements = []; });
  const row = api.createRowBlock();
  Object.assign(row.columns[0], { contentKind: "Field", fieldPath, label: "", fallbackText: "N/M" });
  const grid = api.createGridBlock();
  grid.rows = [grid.rows[0]];
  grid.rows[0].heightMm = 28;
  Object.assign(grid.rows[0].cells[0], { contentKind: "Field", fieldPath, label: "", fallbackText: "N/M" });
  const conditional = api.createConditionalBlock();
  conditional.condition = { fieldPath: "Invoice.InvoiceNo", operator: "HasValue", value: "" };
  conditional.content = { kind: "Field", fieldPath, text: "", fallbackText: "N/M" };
  const detail = api.createDetailTableBlock();
  detail.sideBand = api.createDetailTableSideBand();
  detail.sideBand.style.marginTopMm = 0;
  const body = schema.layers.find(layer => layer.role === "Body");
  body.elements = [row, grid, conditional, detail].map((block, index) => ({
    ...api.createV3FlowElement(block, 1000, 6500 + index * 4000),
    id: `marks-flow-${block.type}`, heightHundredthMm: 3500,
  }));
  schema.layers.find(layer => layer.role === "Overlay").elements.push({
    ...api.createV3FieldElement(fieldPath, 1000, 1500), id: "marks-field",
    label: "唛头 / Shipping marks\n文字或图片",
    widthHundredthMm: 6000, heightHundredthMm: 3000,
    style: { fontSizePt: 12, align: "Center", paddingHundredthMm: 200, borderWidthPx: 1, borderStyle: "Solid" },
  });
  return schema;
}
