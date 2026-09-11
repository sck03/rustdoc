import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";
import { pathToFileURL } from "node:url";

const repo = path.resolve(import.meta.dirname, "..");
const web = path.join(repo, "apps/export-doc-web");
const require = createRequire(path.join(web, "package.json"));
const output = path.join(repo, ".codex-runtime/document-spare-field-tests");
fs.mkdirSync(output, { recursive: true });
const source = (name) => JSON.stringify(path.join(web, "src", name).replaceAll("\\", "/"));
const bundle = path.join(output, "model.mjs");
await require("esbuild").build({ stdin: { loader: "ts", resolveDir: web, contents: `
  export * as invoice from ${source("features/invoices/invoiceModel.ts")};
  export * as items from ${source("features/invoices/invoiceItemsEditorModel.ts")};
  export * from ${source("features/invoices/invoiceItemColumnVisibility.ts")};
  export * as payment from ${source("features/payments/paymentModel.ts")};
  export * from ${source("ui/documentSpareFields.ts")};
  export * from ${source("features/report-designer/reportDesignerFields.ts")};
  export * from ${source("features/report-designer/reportDesignerPropertiesModel.ts")};
  export * from ${source("features/report-designer/reportDesignerPreviewSamples.ts")};
  export * from ${source("features/report-designer/reportDesignerBlockFactories.ts")};
  export * from ${source("features/report-designer/reportDesignerV3ElementFactories.ts")};
  export * from ${source("features/report-designer/reportDesignerV3TemplateParser.ts")};
  export * from ${source("features/report-designer/reportDesignerV3HtmlExporter.ts")};
` }, bundle: true, platform: "node", format: "esm", outfile: bundle, logLevel: "silent" });
const api = await import(pathToFileURL(bundle).href);
const { documentSpareKeys: keys } = api;
let invoice = api.invoice.createEmptyInvoice("2026-09-11");
let payment = api.payment.createEmptyPayment("2026-09-11");
invoice.items = [api.items.createEmptyInvoiceItem()];
for (const key of keys) { invoice[key] = ` header-${key} `; invoice.items[0][key] = ` item-${key} `; payment[key] = ` pay-${key} `; }
invoice = api.invoice.normalizeInvoiceForSave(invoice, 0);
payment = api.payment.normalizePaymentForSave(payment, 0);
for (const key of keys) {
  assert.equal(invoice[key], `header-${key}`);
  assert.equal(invoice.items[0][key], `item-${key}`);
  assert.equal(payment[key], `pay-${key}`);
  assert.equal(api.invoice.uppercaseInvoiceEnglishText(invoice).items[0][key], `ITEM-${key.toUpperCase()}`);
}
assert.equal(api.items.isMeaningfulInvoiceItem({ ...api.items.createEmptyInvoiceItem(), spare10: "only spare" }), true);
assert.deepEqual([...api.resolveInvoiceItemHiddenColumns(0, [])], keys);
assert.deepEqual([...api.resolveInvoiceItemHiddenColumns(3, [])], keys.slice(3));
assert.equal(api.resolveInvoiceItemHiddenColumns(10, []).size, 0);
const populated = api.findPopulatedInvoiceSpareColumns([{ ...api.items.createEmptyInvoiceItem(), spare10: "original data", spare1: "  " }]);
assert.deepEqual(populated, ["spare10"]);
assert(!api.resolveInvoiceItemHiddenColumns(0, populated).has("spare10"), "existing content must be discoverable by default");
assert(api.resolveInvoiceItemHiddenColumns(0, populated, { spare10: false }).has("spare10"), "explicit temporary column choices take precedence");
assert.equal(api.normalizeInvoiceItemSpareColumnCount(-1), 0);
assert.equal(api.normalizeInvoiceItemSpareColumnCount(11), 10);
assert.equal(api.normalizeInvoiceItemSpareColumnCount(undefined), 0);
assert.match(api.payment.validatePaymentDraft({ ...payment, spare10: "a".repeat(501) }), /备用字段10/);

const paths = (root) => keys.map((key) => `${root}.${key[0].toUpperCase()}${key.slice(1)}`);
const catalog = { reportType: "ExportDocument", categoryOrder: ["明细备用列", "单据备用字段"], fields: [
  ...paths("Invoice").map((value) => ({ category: "单据备用字段", label: value, value: `{{ ${value} }}` })),
  ...paths("item").map((value) => ({ category: "明细备用列", label: value, value: `{{ ${value} }}` })),
] };
const groups = api.buildReportDesignerFieldGroups(catalog);
const priceGroups = api.buildReportDesignerFieldGroups({ reportType: "ExportDocument", categoryOrder: ["商品明细"],
  fields: [{ category: "商品明细", label: "单价", value: "{{ item.UnitPrice | format_unit_price }}" }] });
assert.equal(priceGroups[0].fields[0].value, "item.UnitPrice", "formatting expressions must remain selectable as business fields");
assert.equal(groups[0].category, "明细备用列", "the API owns category ordering");
assert.deepEqual(api.filterDetailItemFieldGroups(groups).flatMap((group) => group.fields.map((field) => field.value)), paths("item"));
const schema = api.parseReportDesignerV3FromHtml("", "ExportDocument").schema;
const body = schema.layers.find((layer) => layer.role === "Body");
body.elements = paths("Invoice").map((field, index) => api.createV3FieldElement(field, 1000, 1000 + index * 600));
const detail = api.createDetailTableBlock();
assert.equal(detail.columns[0].fieldPath, "Invoice.Items.StyleName", "the default product column must bind the real model");
detail.columns = paths("item").map((field) => api.createDetailTableColumn(field, field, 12));
body.elements.push(api.createV3FlowElement(detail, 1000, 8000));
const html = api.exportReportDesignerV3SchemaToHtml(schema, "ExportDocument");
assert(html, "all spare field bindings must produce valid V3 HTML");
for (const field of [...paths("Invoice"), ...paths("item")]) assert(html.includes(field));
const preview = api.renderReportDesignerLocalPreviewSample(html, "exportStandard");
for (let index = 1; index <= 10; index++) assert(preview.includes(`备用 ${index} 示例`));
assert.match(api.renderReportDesignerLocalPreviewSample("{{ Payment.CNYAmount }} {{ Payment.AccountNo }} {{ Payment.Spare10 }}", "paymentVoucher"), /12345\.67.*6222.*备用 10 示例/);
assert.match(api.renderReportDesignerLocalPreviewSample("{{ for item in Invoice.Items }}{{ item.StyleName }}{{ end }}", "exportStandard"), /Sample product 01/);
process.stdout.write("Document spare fields passed: all 30 fields, normalization, independent drafts, designer selectors, HTML and sample rendering.\n");
