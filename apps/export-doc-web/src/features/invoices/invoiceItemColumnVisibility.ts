import type { ApiInvoiceItemDto } from "../../api/index.ts";
import { documentSpareKeys } from "../../ui/documentSpareFields.ts";
import { invoiceItemEditableColumns, type EditableInvoiceItemField } from "./invoiceItemTableModel.ts";

export type InvoiceItemColumnOverrides = Partial<Record<EditableInvoiceItemField, boolean>>;

export function normalizeInvoiceItemSpareColumnCount(value: unknown) {
  const count = Number(value);
  return Number.isFinite(count) ? Math.max(0, Math.min(documentSpareKeys.length, Math.trunc(count))) : 0;
}

export function findPopulatedInvoiceSpareColumns(items: ApiInvoiceItemDto[]) {
  return documentSpareKeys.filter((field) => items.some((item) => Boolean(item[field]?.trim())));
}

export function resolveInvoiceItemHiddenColumns(defaultSpareColumnCount: number, populatedFields: readonly string[], overrides: InvoiceItemColumnOverrides = {}) {
  const hidden = new Set<EditableInvoiceItemField>(documentSpareKeys.slice(normalizeInvoiceItemSpareColumnCount(defaultSpareColumnCount)));
  for (const column of invoiceItemEditableColumns) {
    const visible = overrides[column.field];
    if (visible === true || (visible === undefined && populatedFields.includes(column.field))) hidden.delete(column.field);
    if (visible === false) hidden.add(column.field);
  }
  if (hidden.size === invoiceItemEditableColumns.length) hidden.delete(invoiceItemEditableColumns[0].field);
  return hidden;
}
