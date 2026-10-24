import type { ApiInvoiceDetailDto, HsCodeKnowledgeFeedbackInput } from "../../api/index.ts";
import { normalizeInvoiceForSave } from "./invoiceModel.ts";

export function reconcileSavedInvoice(current: ApiInvoiceDetailDto, submitted: ApiInvoiceDetailDto, saved: ApiInvoiceDetailDto) {
  const changed = JSON.stringify(normalizeInvoiceForSave(current, submitted.id)) !== JSON.stringify(normalizeInvoiceForSave(submitted, submitted.id));
  return {
    changed,
    invoice: changed ? { ...saved, ...current, id: saved.id, rowVersion: saved.rowVersion,
      ownerUserId: saved.ownerUserId, companyScope: saved.companyScope, departmentId: saved.departmentId,
      items: current.items.map(item => ({ ...item, invoiceId: saved.id })) } : saved,
  };
}

export function remainingHsFeedback(current: HsCodeKnowledgeFeedbackInput[], submitted: HsCodeKnowledgeFeedbackInput[] = []) {
  const saved = new Set(submitted.map(row => JSON.stringify(row)));
  return current.filter(row => !saved.has(JSON.stringify(row)));
}

export type InvoiceSaveContinuation = { invoice: ApiInvoiceDetailDto; feedback: HsCodeKnowledgeFeedbackInput[] };
export function readInvoiceSaveContinuation(state: unknown, saved: ApiInvoiceDetailDto): InvoiceSaveContinuation | null {
  if (!state || typeof state !== "object" || !("invoiceSaveContinuation" in state)) return null;
  const continuation = state.invoiceSaveContinuation as InvoiceSaveContinuation | undefined;
  return continuation?.invoice?.id === saved.id && continuation.invoice.rowVersion === saved.rowVersion
    && Array.isArray(continuation.invoice.items) && Array.isArray(continuation.feedback) ? continuation : null;
}
