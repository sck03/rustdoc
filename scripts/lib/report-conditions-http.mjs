import assert from "node:assert/strict";
import { defaultReportDesigns } from "./default-report-designs.mjs";

export async function verifyReportConditionsHttp({ api, url, headers, invoiceId }) {
  const designs = defaultReportDesigns();
  const invoice = designs[0][1];
  const catalog = await api('/api/reports/templates?reportType=ExportDocument');
  const templatePath = catalog.find(item => item.templatePath.endsWith('invoice_template.dtpl'))?.templatePath;
  assert(templatePath);
  const conditional = invoice.layers.flatMap(layer => layer.elements).find(element => element.id === "special-terms");
  const block = conditional.block;
  block.conditions.push(
    { fieldPath: "Invoice.Currency", operator: "Equals", value: "eur", ignoreCase: true },
    { fieldPath: "Invoice.TotalAmount", operator: "GreaterThan", value: "10000.00", comparisonType: "Number" },
    { fieldPath: "Invoice.InvoiceDate", operator: "LessOrEqual", value: "2026-10-10", comparisonType: "Date" },
  );
  const preview = () => api(`/api/reports/invoices/${invoiceId}/html-preview`, { reportType: "ExportDocument", templatePath, content: JSON.stringify(invoice), withSeal: true });
  assert.match((await preview()).html, /Special Terms/);
  block.conditions[1].value = "USD";
  assert.doesNotMatch((await preview()).html, /Special Terms|BY APPOINTMENT/);
  block.matchMode = "Any";
  assert.match((await preview()).html, /Special Terms/);
  async function rejects(reportType, design) {
    const response = await fetch(`${url}/api/reports/user-templates`, { method: "POST", headers, body: JSON.stringify({ reportType, name: "Invalid condition fixture", contentHtml: JSON.stringify(design) }) });
    assert.equal(response.status, 400, JSON.stringify(await response.json()));
  }
  block.conditions[2].value = "NaN";
  await rejects("ExportDocument", invoice);
  block.conditions[2].value = "10000";
  block.conditions.push({ fieldPath: "Payment.Notes", operator: "HasValue", value: "" });
  await rejects("ExportDocument", invoice);
  block.conditions.pop();
  await rejects("PaymentVoucher", invoice);
  const payment = designs[4][1];
  payment.layers[2].elements.push({ ...structuredClone(conditional), yHundredthMm: 24500, heightHundredthMm: 3000, block: {
    ...structuredClone(block), conditions: [{ fieldPath: "Payment.Notes", operator: "HasValue", value: "" }, ...block.conditions.slice(1)],
    content: { kind: "Field", fieldPath: "Payment.Notes", label: "备注", text: "", fallbackText: "" },
  } });
  await rejects("PaymentVoucher", payment);
  const paymentBlock = payment.layers[2].elements.at(-1).block;
  paymentBlock.conditions = [paymentBlock.conditions[0],{ fieldPath: "Payment.CNYAmount", operator: "GreaterOrEqual", comparisonType: "Number", value: "0" }];
  const saved = await api("/api/reports/user-templates", { reportType: "PaymentVoucher", name: "Typed payment condition", contentHtml: JSON.stringify(payment) });
  assert(saved.id > 0, "valid payment conditions stay in their own data domain");
}
