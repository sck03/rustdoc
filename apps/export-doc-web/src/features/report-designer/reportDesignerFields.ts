import type { ApiReportTemplateFieldCatalogResponse } from "../../api/index.ts";
import type { ReportDesignerReportType } from "./reportDesignerSchema.ts";

export type ReportDesignerFieldGroup = {
  category: string;
  fields: Array<{
    label: string;
    value: string;
    originalValue?: string;
  }>;
};

export type ReportDesignerField = ReportDesignerFieldGroup["fields"][number] & {
  category: string;
};

const exportTemplateSystemFields = [
  { category: "模板系统字段", label: "是否带章 (ShowSeal)", value: "ShowSeal" },
  { category: "模板系统字段", label: "单证章图片 (doc_seal_path)", value: "doc_seal_path" },
  { category: "模板系统字段", label: "报关章图片 (customs_seal_path)", value: "customs_seal_path" },
  { category: "模板系统字段", label: "唛头图片 (shipping_marks_image_data)", value: "shipping_marks_image_data" },
];

const paymentTemplateSystemFields = [
  { category: "模板系统字段", label: "是否带章 (ShowSeal)", value: "ShowSeal" },
  { category: "模板系统字段", label: "单证章图片 (doc_seal_path)", value: "doc_seal_path" },
];

const exportDesignerCategoryOrder = [
  "发票表头",
  "贸易方",
  "运输条款",
  "金额与重量",
  "商品明细",
  "模板系统字段",
  "其它字段",
];

const paymentDesignerCategoryOrder = [
  "付款信息",
  "收款与经办",
  "费用项目",
  "金额换算",
  "业务事项",
  "模板系统字段",
  "其它字段",
];

export function buildReportDesignerFieldGroups(
  fieldCatalog?: ApiReportTemplateFieldCatalogResponse | null,
  fallbackReportType: ReportDesignerReportType = "ExportDocument",
): ReportDesignerFieldGroup[] {
  const reportType = normalizeReportType(fieldCatalog?.reportType, fallbackReportType);
  if (!fieldCatalog || fieldCatalog.fields.length === 0) {
    return groupFields(templateSystemFieldsForReportType(reportType));
  }

  const catalogFields = fieldCatalog.fields
    .map((field) => ({
      category: normalizeDesignerFieldCategory(reportType, field.category, normalizeCatalogFieldValue(field.value)),
      label: field.label,
      value: normalizeCatalogFieldValue(field.value),
      originalValue: field.value,
    }))
    .filter((field) => field.value);
  const fields = appendTemplateSystemFields(dedupeFields(catalogFields), reportType);
  const orderedCategories = reportType === "ExportDocument"
    ? exportDesignerCategoryOrder
    : paymentDesignerCategoryOrder;
  const categories = [
    ...orderedCategories,
    ...Array.from(new Set(fields.map((field) => field.category))).filter((category) => !orderedCategories.includes(category)),
  ];

  return categories
    .map((category) => ({
      category,
      fields: fields
        .filter((field) => field.category === category)
        .map((field) => ({
          label: field.label,
          value: field.value,
          originalValue: field.originalValue,
        })),
    }))
    .filter((group) => group.fields.length > 0);
}

function appendTemplateSystemFields(fields: ReportDesignerField[], reportType: ReportDesignerReportType): ReportDesignerField[] {
  const existingValues = new Set(fields.map((field) => field.value));
  return [
    ...fields,
    ...templateSystemFieldsForReportType(reportType)
      .filter((field) => !existingValues.has(field.value))
      .map((field) => ({ ...field })),
  ];
}

function dedupeFields<T extends { value: string }>(fields: T[]) {
  const seen = new Set<string>();
  return fields.filter((field) => {
    const key = field.value.toLowerCase();
    if (seen.has(key)) {
      return false;
    }

    seen.add(key);
    return true;
  });
}

export function normalizeCatalogFieldValue(value: string) {
  const trimmed = value.trim();
  const scribanMatch = trimmed.match(/^\{\{\s*([^|}]+?)(?:\s*\|[^}]*)?\s*\}\}$/);
  return (scribanMatch?.[1] ?? trimmed).trim();
}

function normalizeDesignerFieldCategory(reportType: ReportDesignerReportType, category: string, fieldPath: string) {
  if (reportType === "PaymentVoucher") {
    if (
      fieldPath === "Payment.Id" ||
      fieldPath === "Payment.InvoiceNo" ||
      fieldPath === "Payment.PaymentDate" ||
      fieldPath === "Payment.Department" ||
      fieldPath === "Payment.Project" ||
      fieldPath === "Payment.PaymentMethod"
    ) {
      return "付款信息";
    }

    if (
      fieldPath === "Payment.PayeeName" ||
      fieldPath === "Payment.BankName" ||
      fieldPath === "Payment.AccountNo" ||
      fieldPath === "Payment.PayerName"
    ) {
      return "收款与经办";
    }

    if (
      fieldPath.includes("Expense") ||
      fieldPath === "Payment.TravelExpense" ||
      fieldPath === "Payment.OtherExpense"
    ) {
      return "费用项目";
    }

    if (
      fieldPath === "Payment.USDAmount" ||
      fieldPath === "Payment.CNYAmount" ||
      fieldPath === "cny_amount_upper"
    ) {
      return "金额换算";
    }

    if (fieldPath.startsWith("Payment.")) {
      return "业务事项";
    }

    return category || "其它字段";
  }

  if (fieldPath.startsWith("item.") || fieldPath.startsWith("Invoice.Items.")) {
    return "商品明细";
  }

  if (fieldPath.startsWith("Customer.") || fieldPath.startsWith("Exporter.")) {
    return "贸易方";
  }

  if (
    fieldPath.includes("Port") ||
    fieldPath.includes("Destination") ||
    fieldPath.includes("Transport") ||
    fieldPath.includes("Shipment") ||
    fieldPath.includes("TradeTerms") ||
    fieldPath.includes("PaymentTerms") ||
    fieldPath.includes("LetterOfCredit") ||
    fieldPath.includes("IssuingBank") ||
    fieldPath.includes("SpecialTerms") ||
    fieldPath.includes("SupervisionMode") ||
    fieldPath.includes("ShippingMarks")
  ) {
    return "运输条款";
  }

  if (
    fieldPath.includes("Amount") ||
    fieldPath.includes("Currency") ||
    fieldPath.includes("Quantity") ||
    fieldPath.includes("Cartons") ||
    fieldPath.includes("Weight") ||
    fieldPath.includes("Volume") ||
    fieldPath.includes("Profit") ||
    fieldPath.includes("TaxRefund") ||
    fieldPath.includes("Purchase")
  ) {
    return "金额与重量";
  }

  if (fieldPath.startsWith("Invoice.")) {
    return "发票表头";
  }

  return category || "其它字段";
}

function templateSystemFieldsForReportType(reportType: ReportDesignerReportType) {
  return reportType === "PaymentVoucher" ? paymentTemplateSystemFields : exportTemplateSystemFields;
}

function normalizeReportType(value: string | undefined, fallback: ReportDesignerReportType): ReportDesignerReportType {
  return value === "PaymentVoucher" || value === "ExportDocument" ? value : fallback;
}

function groupFields(fields: typeof exportTemplateSystemFields): ReportDesignerFieldGroup[] {
  return Array.from(new Set(fields.map((field) => field.category))).map((category) => ({
    category,
    fields: fields
      .filter((field) => field.category === category)
      .map((field) => ({
        label: field.label,
        value: field.value,
      })),
  }));
}
