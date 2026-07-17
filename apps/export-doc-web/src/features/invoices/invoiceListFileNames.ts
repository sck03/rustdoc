import type { ApiInvoiceListItemDto } from "../../api/index.ts";

export type SingleWindowBusinessType = "CustomsCoo" | "AgentConsignment";

export function sanitizePackageFileName(value: string) {
  const sanitized = value.trim().replace(/[<>:"/\\|?*\u0000-\u001f]/g, "_").replace(/\.+$/g, "").trim();
  return sanitized || "invoice";
}

export function buildBookingSheetDefaultFileName(invoice: ApiInvoiceListItemDto) {
  return `${sanitizePackageFileName(invoice.invoiceNo || `invoice-${invoice.id}`)}_订舱托单.xlsx`;
}

export function buildSingleWindowPackageDefaultFileName(invoice: ApiInvoiceListItemDto, businessType: SingleWindowBusinessType) {
  const suffix = businessType === "CustomsCoo" ? "COO" : "ACD";
  return `${sanitizePackageFileName(invoice.invoiceNo || `invoice-${invoice.id}`)}_${suffix}.swpkg`;
}
