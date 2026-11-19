import type { ApiInvoiceDetailDto, ApiInvoiceItemDto, HsCodeKnowledgeFeedbackInput } from "../../api/index.ts";
import { decimalText } from "../../ui/decimalSum.ts";
import { calculateInvoiceTotals, createEmptyInvoiceItem } from "./invoiceItemsEditorModel.ts";
import { normalizeInvoiceForSave, type RouteInvoiceImportAction } from "./invoiceModel.ts";
import { normalizeInvoiceItemSpareColumnCount } from "./invoiceItemColumnVisibility.ts";

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

function readInvoiceSystemSetting(settings: object | undefined, key: string) {
  const system = settings && typeof settings === "object"
    ? (settings as { system?: unknown }).system
    : null;
  const systemSettings = system && typeof system === "object" ? (system as Record<string, unknown>) : null;
  return systemSettings?.[key];
}

export function readInvoiceItemBlankRowCount(settings?: object) {
  const value = Number(readInvoiceSystemSetting(settings, "itemEntryBlankRowCount"));
  return Number.isFinite(value) ? Math.max(1, Math.min(500, Math.trunc(value))) : 20;
}

export function readInvoiceItemSpareColumnCount(settings?: object) {
  return normalizeInvoiceItemSpareColumnCount(readInvoiceSystemSetting(settings, "itemEntrySpareColumnCount"));
}

export function areInvoiceItemValuesEqual(left: unknown, right: unknown) {
  return left === right;
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
  return value.trim() ? decimalText(value) : undefined;
}
