import { useMemo, useRef } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "react-router-dom";
import type { ApiInvoiceSaveResponse } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { isAbortError, useAbortableOperation } from "../../ui/useAbortableOperation.ts";
import type { ExporterSealType } from "../master-data/ExporterSealField.tsx";
import { getInvoiceStatusLabel, normalizeInvoiceForSave, normalizeInvoiceStatus } from "./invoiceModel.ts";
import { invoiceSealDataUrl } from "./invoiceSealImage.ts";
import { useInvoiceSaveMutation, type InvoicePersistenceOptions } from "./useInvoiceSaveMutation.ts";
import { invalidateInvoiceViews, invoiceControlMutationKey } from "./invoiceCache.ts";

export function useInvoicePersistenceOperations(options: InvoicePersistenceOptions) {
  const { client, invoice, invoiceId, isNew, setInvoice, setMessage, setPendingHsFeedback,
    setPersistedInvoiceDraft, setPersistedInvoiceStatus, setSuccessMessage } = options;
  const navigate = useNavigate(), queryClient = useQueryClient();
  const scope = useMemo(() => ({}), [client, invoiceId, isNew]);
  const currentScope = useRef(scope);
  currentScope.current = scope;
  const run = useAbortableOperation(scope);
  const check = (signal: AbortSignal) => {
    signal.throwIfAborted();
    if (currentScope.current !== scope) throw new DOMException("单据已切换。", "AbortError");
  };
  const failed = (error: unknown) => { if (!isAbortError(error)) { setMessage(readApiError(error)); setSuccessMessage(null); } };
  const refresh = async (signal: AbortSignal, label: string, id?: number) => {
    try { await invalidateInvoiceViews(queryClient, id); }
    catch (error) { check(signal); setMessage(`${label}，但关联列表刷新失败：${readApiError(error)}`); }
    check(signal);
  };
  const accept = (response: Pick<ApiInvoiceSaveResponse, "id" | "invoice">, message: string) => {
    setInvoice(response.invoice); setPendingHsFeedback([]);
    setPersistedInvoiceDraft(normalizeInvoiceForSave(response.invoice, response.id));
    setPersistedInvoiceStatus(normalizeInvoiceStatus(response.invoice.status));
    setMessage(null); setSuccessMessage(message);
    queryClient.setQueryData(queryKeys.invoice(response.id), response.invoice);
  };
  const saveInvoiceMutation = useInvoiceSaveMutation(options);
  const exporterSealMutation = useMutation({
    mutationFn: ({ file, sealType }: { sealType: ExporterSealType; file: File }) => run(async signal => {
      if (!invoice) throw new Error("请先打开需要编辑的发票。");
      const imageDataUrl = await invoiceSealDataUrl(file);
      check(signal);
      const saved = await client.saveInvoiceSealImage({ body: { imageDataUrl } }, { signal });
      check(signal);
      const field = sealType === "document" ? "docSealPath" : "customsSealPath";
      setInvoice(current => current && current.id === invoice.id ? { ...current, [field]: saved.imagePath } : current);
      setMessage(null); setSuccessMessage(sealType === "document" ? "单证章已上传，请保存发票。" : "报关章已上传，请保存发票。");
      return saved;
    }), onError: failed,
  });
  const cloneInvoiceTypeMutation = useMutation({
    mutationKey: [...invoiceControlMutationKey(invoiceId), "clone"],
    mutationFn: ({ targetType }: { targetType: string }) => run(async signal => {
      const response = await client.cloneInvoiceAsType({ id: invoiceId, body: { targetType,
        options: { copyHeader: true, copyItems: true, resetDates: false, clearAmounts: false } } }, { signal });
      check(signal);
      queryClient.setQueryData(queryKeys.invoice(response.id), response.invoice);
      setMessage(null); setSuccessMessage(null);
      await refresh(signal, "另一类型发票已生成");
      navigate(`/invoices/${response.id}`, { state: { successMessage: response.message || `已生成同一发票号的${response.invoice.type}。` } });
      return response;
    }), onError: failed,
  });
  const unverifyInvoiceMutation = useMutation({
    mutationKey: [...invoiceControlMutationKey(invoiceId), "unverify"],
    mutationFn: () => run(async signal => {
      const response = await client.unverifyInvoice({ id: invoiceId, body: { rowVersion: invoice?.rowVersion ?? "", note: "用户申请反审核，返回草稿后重新核对。" } }, { signal });
      check(signal); accept(response, "发票已反审核，当前为草稿状态。");
      await refresh(signal, "发票已反审核", response.id);
      return response;
    }), onError: failed,
  });
  const statusTransitionMutation = useMutation({
    mutationKey: [...invoiceControlMutationKey(invoiceId), "transition"],
    mutationFn: ({ targetStatus, note }: { targetStatus: string; note: string }) => run(async signal => {
      const response = await client.transitionInvoiceStatus({ id: invoiceId, body: { targetStatus, rowVersion: invoice?.rowVersion ?? "", note } }, { signal });
      check(signal); accept(response, `发票状态已更新为“${getInvoiceStatusLabel(response.invoice.status)}”。`);
      await refresh(signal, "发票状态已更新", response.id);
      return response;
    }), onError: failed,
  });
  const deleteInvoiceMutation = useMutation({
    mutationKey: [...invoiceControlMutationKey(invoiceId), "delete"],
    mutationFn: () => run(async signal => {
      const response = await client.deleteInvoice({ id: invoiceId, rowVersion: invoice?.rowVersion ?? "" }, { signal });
      check(signal); setMessage(null); setSuccessMessage(null);
      for (const queryKey of [queryKeys.invoice(invoiceId), queryKeys.singleWindowCustomsCooDocument(invoiceId),
        queryKeys.singleWindowCustomsCooExportReview(invoiceId), queryKeys.singleWindowAgentConsignmentDocument(invoiceId),
        queryKeys.singleWindowAgentConsignmentExportReview(invoiceId)]) queryClient.removeQueries({ queryKey });
      await refresh(signal, "发票已删除");
      await queryClient.invalidateQueries({ queryKey: queryKeys.jobsRoot() });
      check(signal); navigate("/invoices", { replace: true, state: { successMessage: response.message || "发票已删除。" } });
      return response;
    }), onError: failed,
  });
  const saveCustomOptionMutation = useMutation({
    mutationFn: ({ optionType, value }: { optionType: string; value: string }) => run(async signal => {
      const response = await client.saveCustomOption({ optionType, body: { value } }, { signal });
      check(signal); await queryClient.invalidateQueries({ queryKey: queryKeys.customOptionsRoot() });
      return response;
    }), onError: failed,
  });
  const refreshParties = () => Promise.all([queryKeys.masterDataRoot("customers"), queryKeys.masterDataRoot("exporters")]
    .map(queryKey => queryClient.invalidateQueries({ queryKey })));
  return { cloneInvoiceTypeMutation, deleteInvoiceMutation, exporterSealMutation, refreshParties,
    saveCustomOptionMutation, saveInvoiceMutation, statusTransitionMutation, unverifyInvoiceMutation };
}
