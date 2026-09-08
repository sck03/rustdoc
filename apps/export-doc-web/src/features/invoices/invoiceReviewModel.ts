import type { ApiInvoiceDetailDto, InvoiceReviewIssue } from "../../api/index.ts";
import { normalizeInvoiceForSave } from "./invoiceModel.ts";
import { isMeaningfulInvoiceItem, normalizeInvoiceItemForSave } from "./invoiceItemsEditorModel.ts";

export function prepareInvoiceReview(invoice: ApiInvoiceDetailDto) {
  // Saving omits blank rows; review messages still point to the visible editor row.
  const sourceRows = invoice.items.flatMap((item, index) =>
    isMeaningfulInvoiceItem(normalizeInvoiceItemForSave(item)) ? [index + 1] : []);
  return { body: normalizeInvoiceForSave(invoice, invoice.id), sourceRows };
}

export function invoiceReviewIssueLabel(issue: InvoiceReviewIssue, sourceRows: readonly number[]) {
  if (!issue.rowNumber) return issue.message;
  return `第 ${sourceRows[issue.rowNumber - 1] ?? issue.rowNumber} 行：${issue.message}`;
}
