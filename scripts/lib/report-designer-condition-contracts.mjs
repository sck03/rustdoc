import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";

export function verifyConditionalContracts(api, repo) {
  const cases = JSON.parse(fs.readFileSync(path.join(repo, "tests/ReportTemplateFixtures/conditional-rules.json"), "utf8"));
  for (const test of cases) {
    const issues = [];
    api.normalizeConditions({ condition: test.rule }, "$", issues);
    assert.equal(issues.some(issue => issue.severity === "error"), test.invalidRule === true, test.name);
  }
  const schema = api.parseReportDesignerV3Source("", "ExportDocument").schema;
  schema.layers.forEach(layer => layer.elements = []);
  const block = { ...api.createConditionalBlock(), matchMode: "Any", additionalConditions: [
    { fieldPath: "Invoice.TotalAmount", operator: "GreaterOrEqual", comparisonType: "Number", value: "1000.50" },
    { fieldPath: "Invoice.InvoiceDate", operator: "LessOrEqual", comparisonType: "Date", value: "2026-10-10" },
  ] };
  schema.layers[1].elements.push(api.createV3FlowElement(block, 1000, 5000));
  const saved = api.parseReportDesignerV3Source(JSON.stringify(schema), "ExportDocument");
  assert(saved.schema);
  const restored = saved.schema.layers[1].elements[0].block;
  assert.equal(restored.matchMode, "Any");
  assert.equal(restored.additionalConditions[0].comparisonType, "Number");
  assert.equal(restored.additionalConditions[1].value, "2026-10-10");
  assert(api.renderReportDesignerBlockPreviewToHtml(restored).includes("Special Terms"), "canvas shows editable conditional content without evaluating business data");
  assert(!api.validateReportDesignerV3Draft(saved.schema, "ExportDocument").blocked);
  for (const patch of [
    { matchMode: "Execute" },
    { additionalConditions: Array.from({ length: 8 }, () => block.condition) },
    { additionalConditions: [{ ...block.condition, fieldPath: "Payment.Notes" }] },
    { additionalConditions: [{ ...block.condition, fieldPath: "item.Quantity" }] },
    { additionalConditions: [{ fieldPath: "Invoice.TotalAmount", comparisonType: "Number", operator: "Equals", value: "NaN" }] },
  ]) {
    const invalid = structuredClone(schema);
    Object.assign(invalid.layers[1].elements[0].block, patch);
    assert(api.validateReportDesignerV3Draft(invalid, "ExportDocument").blocked, JSON.stringify(patch));
  }
}
