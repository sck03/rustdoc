import { useMemo, useRef, type Dispatch, type SetStateAction } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "react-router-dom";
import type { ApiInvoiceDetailDto, ExportDocManagerApiClient, HsCodeKnowledgeFeedbackInput } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { isConcurrencyConflict, readApiError } from "../../ui/formUtils.ts";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";
import { normalizeInvoiceForSave, normalizeInvoiceStatus } from "./invoiceModel.ts";
import { reconcileSavedInvoice, remainingHsFeedback } from "./invoiceSaveModel.ts";
import { invalidateInvoiceViews } from "./invoiceCache.ts";

type TextSetter = Dispatch<SetStateAction<string | null>>;
export type InvoicePersistenceOptions = {
  client: ExportDocManagerApiClient; invoice: ApiInvoiceDetailDto | null; invoiceId: number; isNew: boolean;
  pendingHsFeedback?: HsCodeKnowledgeFeedbackInput[];
  resetItemEditHistory: () => void; setConcurrencyMessage: TextSetter; setInvoice: Dispatch<SetStateAction<ApiInvoiceDetailDto | null>>;
  setMessage: TextSetter; setPendingHsFeedback: Dispatch<SetStateAction<HsCodeKnowledgeFeedbackInput[]>>;
  setPersistedInvoiceDraft: Dispatch<SetStateAction<ApiInvoiceDetailDto | null>>;
  setPersistedInvoiceStatus: Dispatch<SetStateAction<string>>; setSuccessMessage: TextSetter;
};

export function useInvoiceSaveMutation(options: InvoicePersistenceOptions) {
  const { client, invoice, invoiceId, isNew, setInvoice, setPendingHsFeedback, setPersistedInvoiceDraft,
    setPersistedInvoiceStatus, resetItemEditHistory, setMessage, setConcurrencyMessage, setSuccessMessage } = options;
  const queryClient = useQueryClient(), navigate = useNavigate();
  const scope = useMemo(() => ({}), [client, invoiceId, isNew]);
  const latest = useRef({ scope, invoice, feedback: options.pendingHsFeedback ?? [] });
  latest.current = { scope, invoice, feedback: options.pendingHsFeedback ?? [] };
  return useScopedMutation("invoice-save", scope, {
    mutationFn: async (body: ApiInvoiceDetailDto, signal) => {
        const response = await (isNew ? client.createInvoice({ body }, { signal }) : client.updateInvoice({ id: invoiceId, body }, { signal }));
        signal.throwIfAborted();
        if (latest.current.scope !== scope || !latest.current.invoice) throw new DOMException("单据已切换。", "AbortError");
        const reconciled = reconcileSavedInvoice(latest.current.invoice, body, response.invoice);
        const feedback = remainingHsFeedback(latest.current.feedback, body.pendingHsFeedback ?? []);
        setInvoice(reconciled.invoice);
        setPendingHsFeedback(current => remainingHsFeedback(current, body.pendingHsFeedback ?? []));
        setPersistedInvoiceDraft(normalizeInvoiceForSave(response.invoice, response.id));
        setPersistedInvoiceStatus(normalizeInvoiceStatus(response.invoice.status));
        if (!reconciled.changed) resetItemEditHistory();
        setMessage(null); setConcurrencyMessage(null);
        const success = reconciled.changed ? "发票已保存；保存期间的后续修改仍在草稿中。" : response.isUpdate ? "发票已保存。" : "发票已创建。";
        setSuccessMessage(success);
        queryClient.setQueryData(queryKeys.invoice(response.id), response.invoice);
        if (isNew) navigate(`/invoices/${response.id}`, { replace: true, state: { successMessage: success,
          ...(reconciled.changed || feedback.length ? { invoiceSaveContinuation: { invoice: reconciled.invoice, feedback } } : {}) } });
        try {
          await invalidateInvoiceViews(queryClient, response.id);
        } catch (error) {
          if (!signal.aborted && latest.current.scope === scope) setMessage(`发票已保存，但关联列表刷新失败：${readApiError(error)}`);
        }
    },
    onError: error => {
      const message = readApiError(error);
      setMessage(isConcurrencyConflict(error) ? null : message);
      setConcurrencyMessage(isConcurrencyConflict(error) ? message : null);
      setSuccessMessage(null);
    },
  });
}
