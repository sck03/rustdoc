import { useMemo } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "react-router-dom";
import type { ApiInvoiceSaveResponse } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";
import { getInvoiceStatusLabel, normalizeInvoiceForSave, normalizeInvoiceStatus } from "./invoiceModel.ts";
import { invoiceViewKeys } from "./invoiceCache.ts";
import type { InvoicePersistenceOptions } from "./useInvoiceSaveMutation.ts";

type Options = Pick<InvoicePersistenceOptions, "client" | "invoice" | "invoiceId" | "isNew" | "setInvoice" | "setMessage" | "setSuccessMessage" | "setPendingHsFeedback" | "setPersistedInvoiceDraft" | "setPersistedInvoiceStatus">;

export function useInvoiceControlOperations(options: Options) {
  const { client, invoice, invoiceId, isNew, setInvoice, setMessage, setSuccessMessage,
    setPendingHsFeedback, setPersistedInvoiceDraft, setPersistedInvoiceStatus } = options;
  const owner = useMemo(() => ({}), [client, invoiceId, isNew]);
  const cache = useQueryClient(), navigate = useNavigate();
  const onError = (error: unknown) => { setMessage(readApiError(error)); setSuccessMessage(null); };
  const onRefreshError = (error: unknown) => setMessage(`操作已完成，但关联列表刷新失败：${readApiError(error)}`);
  const refreshKeys = (response: { id: number }) => invoiceViewKeys(response.id);
  const accept = (response: Pick<ApiInvoiceSaveResponse, "id" | "invoice">, message: string) => {
    setInvoice(response.invoice); setPendingHsFeedback([]);
    setPersistedInvoiceDraft(normalizeInvoiceForSave(response.invoice, response.id));
    setPersistedInvoiceStatus(normalizeInvoiceStatus(response.invoice.status));
    setMessage(null); setSuccessMessage(message);
    cache.setQueryData(queryKeys.invoice(response.id), response.invoice);
  };
  // One command lane prevents clone/status/delete from racing with one another.
  type Command = { kind: "clone"; targetType: string } | { kind: "unverify" } |
    { kind: "transition"; targetStatus: string; note: string } | { kind: "delete" };
  const command = useScopedMutation("invoice-control", owner, {
    mutationFn: async (input: Command, signal) => {
      const rowVersion = invoice?.rowVersion ?? "";
      switch (input.kind) {
        case "clone": return { kind: input.kind, response: await client.cloneInvoiceAsType({ id: invoiceId,
          body: { targetType: input.targetType, options: { copyHeader: true, copyItems: true, resetDates: false, clearAmounts: false } } }, { signal }) } as const;
        case "unverify": return { kind: input.kind, response: await client.unverifyInvoice({ id: invoiceId,
          body: { rowVersion, note: "用户申请反审核，返回草稿后重新核对。" } }, { signal }) } as const;
        case "transition": return { kind: input.kind, response: await client.transitionInvoiceStatus({ id: invoiceId,
          body: { rowVersion, targetStatus: input.targetStatus, note: input.note } }, { signal }) } as const;
        case "delete": return { kind: input.kind, response: await client.deleteInvoice({ id: invoiceId, rowVersion }, { signal }) } as const;
      }
    },
    refreshKeys: result => result.kind === "delete" ? [...invoiceViewKeys(), queryKeys.jobsRoot()] : refreshKeys(result.response),
    onSuccess: result => {
      setMessage(null);
      if (result.kind === "delete") {
        for (const queryKey of [queryKeys.invoice(invoiceId), queryKeys.singleWindowCustomsCooDocument(invoiceId),
          queryKeys.singleWindowCustomsCooExportReview(invoiceId), queryKeys.singleWindowAgentConsignmentDocument(invoiceId),
          queryKeys.singleWindowAgentConsignmentExportReview(invoiceId)]) cache.removeQueries({ queryKey });
        navigate("/invoices", { replace: true, state: { successMessage: result.response.message || "发票已删除。" } });
      } else if (result.kind === "clone") {
        cache.setQueryData(queryKeys.invoice(result.response.id), result.response.invoice);
        navigate(`/invoices/${result.response.id}`, { state: { successMessage: result.response.message || `已生成同一发票号的${result.response.invoice.type}。` } });
      } else {
        accept(result.response, result.kind === "unverify" ? "发票已反审核，当前为草稿状态。" : `发票状态已更新为“${getInvoiceStatusLabel(result.response.invoice.status)}”。`);
      }
    }, onError, onRefreshError,
  });
  return {
    invoiceControlBusy: command.isPending,
    cloneInvoiceTypeMutation: { isPending: command.isPending, mutate: (input: { targetType: string }) => command.mutate({ kind: "clone", ...input }) },
    unverifyInvoiceMutation: { isPending: command.isPending, mutate: () => command.mutate({ kind: "unverify" }) },
    statusTransitionMutation: { isPending: command.isPending, mutate: (input: { targetStatus: string; note: string }) => command.mutate({ kind: "transition", ...input }) },
    deleteInvoiceMutation: { isPending: command.isPending, mutate: () => command.mutate({ kind: "delete" }) },
  };
}
