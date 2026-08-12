import type { ApiInvoiceDetailDto, ApiInvoiceItemDto, HsCodeKnowledgeFeedbackInput } from "../../api/index.ts";
import { readNumber } from "../../ui/formUtils.ts";
import { calculateInvoiceTotals, createEmptyInvoiceItem } from "./InvoiceItemsEditor.tsx";
import { normalizeInvoiceForSave, type RouteInvoiceImportAction } from "./invoiceModel.ts";

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
    return {
      ...existing,
      items,
      ...calculateInvoiceTotals(items, existing.exchangeRate, existing.currency),
    };
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
    ...calculateInvoiceTotals(
      importedItems,
      importedDraft.exchangeRate,
      importedDraft.currency,
    ),
  };
}

export function buildInvoiceSnapshot(invoice: ApiInvoiceDetailDto, id: number, pendingHsFeedback: HsCodeKnowledgeFeedbackInput[] = []) {
  return JSON.stringify(normalizeInvoiceForSave(invoice, id, pendingHsFeedback));
}

export function readInvoiceItemBlankRowCount(settings?: object) {
  const system = settings && typeof settings === "object"
    ? (settings as { system?: unknown }).system
    : null;
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
  if (left === right) {
    return true;
  }
  if (left.length !== right.length) {
    return false;
  }

  for (let index = 0; index < left.length; index += 1) {
    const leftItem = left[index];
    const rightItem = right[index];
    if (leftItem === rightItem) {
      continue;
    }

    const keys = Object.keys(leftItem) as Array<keyof ApiInvoiceItemDto>;
    if (keys.length !== Object.keys(rightItem).length || keys.some((key) =>
      !Object.prototype.hasOwnProperty.call(rightItem, key) || leftItem[key] !== rightItem[key])) {
      return false;
    }
  }

  return true;
}

export function readInvoiceItemTableNumber(value: string) {
  return value.trim() ? readNumber(value) : undefined;
}
