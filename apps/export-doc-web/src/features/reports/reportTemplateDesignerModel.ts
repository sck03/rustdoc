import type { ApiInvoiceListItemDto, ApiPaymentDto } from "../../api/index.ts";
import { formatAmount, formatDate, readNumber } from "../../ui/formUtils.ts";
import {
  getReportDesignerPreviewSampleProfiles,
  type ReportDesignerPreviewSampleProfile,
} from "../report-designer/reportDesignerPreviewSamples.ts";

export type ReportTypeOption = "ExportDocument" | "PaymentVoucher";
export type DesignerMode = "new" | "source";
export type TemplateWorkspaceMode = "design" | "preview";
export type TemplateImportStrategyOption = "Overwrite" | "Merge" | "AddOnly";
export type TemplatePreviewMode = "sample" | "savedSource";

export const reportTypeOptions: Array<{ value: ReportTypeOption; label: string }> = [
  { value: "ExportDocument", label: "出口单据" },
  { value: "PaymentVoucher", label: "付款凭证" },
];

export const importStrategyOptions: Array<{ value: TemplateImportStrategyOption; label: string }> = [
  { value: "Merge", label: "合并" },
  { value: "Overwrite", label: "覆盖" },
  { value: "AddOnly", label: "仅新增" },
];

export const previewSourcePageSize = 50;

export function defaultTemplateFileName(reportType: ReportTypeOption) {
  return reportType === "PaymentVoucher" ? "payment_voucher_template.html" : "invoice_template.html";
}

export function readReportTypeFromSearch(search: string): ReportTypeOption | null {
  const value = new URLSearchParams(search).get("reportType")?.trim();
  if (value === "PaymentVoucher" || value === "Internal") {
    return "PaymentVoucher";
  }

  if (value === "ExportDocument" || value === "Export") {
    return "ExportDocument";
  }

  return null;
}

export function readTemplateFileNameFromSearch(search: string) {
  return new URLSearchParams(search).get("template")?.trim() ?? "";
}

export function readSearchFromHash(hash = window.location.hash || "") {
  const queryStart = hash.indexOf("?");
  return queryStart >= 0 ? hash.slice(queryStart) : "";
}

export function readPreviewSourceIdFromSearch(search: string, reportType: ReportTypeOption | null) {
  const params = new URLSearchParams(search);
  const sourceId =
    reportType === "PaymentVoucher"
      ? params.get("paymentId") ?? params.get("sourceId")
      : params.get("invoiceId") ?? params.get("sourceId");
  return readNumber(sourceId ?? "");
}

export function matchesTemplateFileName(templatePath: string, requestedTemplateFileName: string) {
  const requested = requestedTemplateFileName.toLowerCase();
  const templatePathLower = templatePath.toLowerCase();
  return fileNameFromPath(templatePathLower) === requested || templatePathLower.includes(requested);
}

export function matchesTemplatePath(left: string, right: string) {
  return normalizeTemplatePath(left) === normalizeTemplatePath(right);
}

export function resolveDefaultTemplatePath({
  templates,
  reportType,
  requestedTemplateFileName,
  currentTemplatePath,
  userTemplateSelected,
}: {
  templates: Array<{ templatePath: string }>;
  reportType: ReportTypeOption;
  requestedTemplateFileName: string;
  currentTemplatePath: string;
  userTemplateSelected: boolean;
}) {
  if (userTemplateSelected) {
    return currentTemplatePath;
  }

  if (templates.length === 0) {
    return "";
  }

  const requestedTemplate = requestedTemplateFileName
    ? templates.find((template) => matchesTemplateFileName(template.templatePath, requestedTemplateFileName))
    : null;
  if (requestedTemplate) {
    return requestedTemplate.templatePath;
  }

  if (currentTemplatePath && templates.some((template) => matchesTemplatePath(template.templatePath, currentTemplatePath))) {
    return currentTemplatePath;
  }

  return (
    templates.find((template) => fileNameFromPath(template.templatePath).toLowerCase() === defaultTemplateFileName(reportType)) ??
    templates[0]
  ).templatePath;
}

export function resolvePreviewSourceId(currentId: number, sourceIds: number[]) {
  if (currentId > 0) {
    return currentId;
  }

  return sourceIds.find((id) => id > 0) ?? 0;
}

export function buildNewTemplateFileName(reportType: ReportTypeOption) {
  const stamp = buildTimestamp(new Date());
  const prefix = reportType === "PaymentVoucher" ? "internal_template" : "export_template";
  return `${prefix}_${stamp}.html`;
}

export function buildTemplatePackageFileName() {
  return `templates_${buildTimestamp(new Date())}.edtpl`;
}

export function buildUserTemplateKey(id: number) {
  return `user-template:${id}`;
}

export function normalizeImportStrategy(value: string): TemplateImportStrategyOption {
  if (value === "Overwrite" || value === "AddOnly") {
    return value;
  }

  return "Merge";
}

export function readPreferredPreviewSampleProfile(reportType: ReportTypeOption): ReportDesignerPreviewSampleProfile {
  return reportType === "PaymentVoucher" ? "paymentVoucher" : "exportStandard";
}

export function normalizePreviewSampleProfile(
  value: string,
  reportType: ReportTypeOption,
): ReportDesignerPreviewSampleProfile {
  const matched = getReportDesignerPreviewSampleProfiles(reportType).find((profile) => profile.value === value);
  return matched?.value ?? readPreferredPreviewSampleProfile(reportType);
}

export function fileNameFromPath(path: string) {
  return path.split(/[\\/]/).filter(Boolean).pop() || path;
}

export function buildInvoicePreviewOptions(invoices: ApiInvoiceListItemDto[], selectedInvoiceId: number) {
  const options = invoices
    .filter((invoice) => invoice.id > 0)
    .map((invoice) => ({
      value: String(invoice.id),
      label: formatInvoicePreviewOption(invoice),
    }));

  return ensureSelectedPreviewOption(options, selectedInvoiceId, "当前打开的发票");
}

export function buildPaymentPreviewOptions(payments: ApiPaymentDto[], selectedPaymentId: number) {
  const options = payments
    .filter((payment) => payment.id > 0)
    .map((payment) => ({
      value: String(payment.id),
      label: formatPaymentPreviewOption(payment),
    }));

  return ensureSelectedPreviewOption(options, selectedPaymentId, "当前打开的付款/报销单");
}

export function buildRawPreviewHtml(content: string) {
  return content.trim() ? content : "<!doctype html><html><body></body></html>";
}

function normalizeTemplatePath(path: string) {
  return path.trim().replace(/[\\/]+/g, "/").toLowerCase();
}

function buildTimestamp(now: Date) {
  return [
    now.getFullYear(),
    pad2(now.getMonth() + 1),
    pad2(now.getDate()),
    pad2(now.getHours()),
    pad2(now.getMinutes()),
    pad2(now.getSeconds()),
  ].join("");
}

function pad2(value: number) {
  return String(value).padStart(2, "0");
}

function ensureSelectedPreviewOption(
  options: Array<{ value: string; label: string }>,
  selectedId: number,
  fallbackLabel: string,
) {
  if (selectedId <= 0 || options.some((option) => option.value === String(selectedId))) {
    return options;
  }

  return [{ value: String(selectedId), label: fallbackLabel }, ...options];
}

function formatInvoicePreviewOption(invoice: ApiInvoiceListItemDto) {
  return compactPreviewOptionParts([
    invoice.invoiceNo || "未编号发票",
    invoice.customerName || invoice.exporterName || "未填写客户",
    formatDate(invoice.invoiceDate),
    formatAmount(invoice.totalAmount, invoice.currency),
  ]);
}

function formatPaymentPreviewOption(payment: ApiPaymentDto) {
  return compactPreviewOptionParts([
    payment.invoiceNo || payment.project || "未编号付款/报销单",
    payment.payeeName || payment.payerName || "未填写往来方",
    formatDate(payment.paymentDate),
    formatAmount(payment.cnyAmount, "CNY"),
  ]);
}

function compactPreviewOptionParts(parts: string[]) {
  const compacted = parts.map((part) => part.trim()).filter((part) => part && part !== "-");
  return compacted.join(" · ") || "未命名单据";
}
