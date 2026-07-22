import type { ApiInvoiceDetailDto, ApiInvoiceItemDto } from "../../api/index.ts";
import { readNumber } from "../../ui/formUtils.ts";
import { calculateInvoiceTotals, createEmptyInvoiceItem } from "./InvoiceItemsEditor.tsx";
import { normalizeInvoiceForSave, type RouteInvoiceImportAction } from "./invoiceModel.ts";

export function cloneInvoiceItems(items: ApiInvoiceItemDto[]) {
  return items.map((item) => ({ ...item }));
}

export function mergeRouteInvoiceImportDraft(
  existing: ApiInvoiceDetailDto,
  importedDraft: ApiInvoiceDetailDto,
  action: RouteInvoiceImportAction,
  invoiceId: number,
) {
  const importedItems = (importedDraft.items ?? []).map((item) => ({
    ...createEmptyInvoiceItem(invoiceId),
    ...item,
    id: 0,
    invoiceId,
  }));

  if (action === "AppendItems") {
    const items = [...(existing.items ?? []), ...importedItems];
    return { ...existing, items, ...calculateInvoiceTotals(items) };
  }

  return {
    ...existing,
    ...importedDraft,
    id: existing.id,
    ownerUserId: existing.ownerUserId,
    departmentId: existing.departmentId,
    companyScope: existing.companyScope,
    rowVersion: existing.rowVersion,
    items: importedItems,
    ...calculateInvoiceTotals(importedItems),
  };
}

export function buildInvoiceSnapshot(invoice: ApiInvoiceDetailDto, id: number) {
  return JSON.stringify(normalizeInvoiceForSave(invoice, id));
}

export function readInvoiceItemBlankRowCount(settings?: Record<string, unknown>) {
  const system = settings && typeof settings === "object" ? settings.system : null;
  const systemSettings = system && typeof system === "object" ? (system as Record<string, unknown>) : null;
  const value = Number(systemSettings?.itemEntryBlankRowCount);
  return Number.isFinite(value) ? Math.max(1, Math.min(500, Math.trunc(value))) : 20;
}

export function areInvoiceItemValuesEqual(left: unknown, right: unknown) {
  if (typeof left === "number" || typeof right === "number") {
    const leftNumber = typeof left === "number" && Number.isFinite(left) ? left : Number(left);
    const rightNumber = typeof right === "number" && Number.isFinite(right) ? right : Number(right);
    return Number.isFinite(leftNumber) && Number.isFinite(rightNumber)
      ? leftNumber === rightNumber
      : String(left ?? "") === String(right ?? "");
  }
  return String(left ?? "") === String(right ?? "");
}

export function areInvoiceItemsEqual(left: ApiInvoiceItemDto[], right: ApiInvoiceItemDto[]) {
  return left === right || JSON.stringify(left) === JSON.stringify(right);
}

export function readInvoiceItemTableNumber(value: string) {
  return value.trim() ? readNumber(value) : undefined;
}
