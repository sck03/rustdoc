import type { Dispatch, SetStateAction } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "react-router-dom";
import type {
  ApiInvoiceDetailDto,
  ExportDocManagerApiClient,
  HsCodeKnowledgeFeedbackInput,
} from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { isConcurrencyConflict, readApiError } from "../../ui/formUtils.ts";
import type { ExporterSealType } from "../master-data/ExporterSealField.tsx";
import { getInvoiceStatusLabel, normalizeInvoiceForSave, normalizeInvoiceStatus } from "./invoiceModel.ts";

type NullableTextSetter = Dispatch<SetStateAction<string | null>>;

export function useInvoicePersistenceOperations({
  client,
  invoice,
  invoiceId,
  isNew,
  refreshSelectedExporter,
  resetItemEditHistory,
  setConcurrencyMessage,
  setInvoice,
  setMessage,
  setPendingHsFeedback,
  setPersistedInvoiceDraft,
  setPersistedInvoiceStatus,
  setSuccessMessage,
}: {
  client: ExportDocManagerApiClient;
  invoice: ApiInvoiceDetailDto | null;
  invoiceId: number;
  isNew: boolean;
  refreshSelectedExporter: () => Promise<unknown>;
  resetItemEditHistory: () => void;
  setConcurrencyMessage: NullableTextSetter;
  setInvoice: Dispatch<SetStateAction<ApiInvoiceDetailDto | null>>;
  setMessage: NullableTextSetter;
  setPendingHsFeedback: Dispatch<SetStateAction<HsCodeKnowledgeFeedbackInput[]>>;
  setPersistedInvoiceDraft: Dispatch<SetStateAction<ApiInvoiceDetailDto | null>>;
  setPersistedInvoiceStatus: Dispatch<SetStateAction<string>>;
  setSuccessMessage: NullableTextSetter;
}) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const exporterSealMutation = useMutation({
    mutationFn: async ({ sealType, file }: { sealType: ExporterSealType; file: File }) => {
      const exporterId = invoice?.exporterId ?? 0;
      if (exporterId <= 0) throw new Error("请先选择出口商档案，再设置印章。");

      return client.uploadExporterSeal({
        id: exporterId,
        sealType,
        fileName: file.name,
        body: file,
      });
    },
    onSuccess: async (_saved, variables) => {
      setMessage(null);
      setSuccessMessage(variables.sealType === "document" ? "出口商单证章已保存。" : "出口商报关章已保存。");
      await Promise.all([
        refreshSelectedExporter(),
        queryClient.invalidateQueries({ queryKey: queryKeys.masterDataRoot("exporters") }),
      ]);
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const saveInvoiceMutation = useMutation({
    mutationFn: (body: ApiInvoiceDetailDto) =>
      isNew
        ? client.createInvoice({ body })
        : client.updateInvoice({ id: invoiceId, body }),
    onSuccess: async (response) => {
      setInvoice(response.invoice);
      setPendingHsFeedback([]);
      setPersistedInvoiceDraft(normalizeInvoiceForSave(response.invoice, response.id));
      setPersistedInvoiceStatus(normalizeInvoiceStatus(response.invoice.status));
      resetItemEditHistory();
      setMessage(null);
      setSuccessMessage(response.isUpdate ? "发票已保存。" : "发票已创建。");
      queryClient.setQueryData(queryKeys.invoice(response.id), response.invoice);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.invoicesRoot() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.invoiceParties() }),
      ]);
      if (isNew) {
        navigate(`/invoices/${response.id}`, {
          replace: true,
          state: { successMessage: "发票已创建。" },
        });
      }
    },
    onError: (error) => {
      const nextMessage = readApiError(error);
      setMessage(isConcurrencyConflict(error) ? null : nextMessage);
      setConcurrencyMessage(isConcurrencyConflict(error) ? nextMessage : null);
      setSuccessMessage(null);
    },
  });

  const cloneInvoiceTypeMutation = useMutation({
    mutationFn: ({ targetType }: { targetType: string }) =>
      client.cloneInvoiceAsType({
        id: invoiceId,
        body: {
          targetType,
          options: {
            copyHeader: true,
            copyItems: true,
            resetDates: false,
            clearAmounts: false,
          },
        },
      }),
    onSuccess: async (response) => {
      setMessage(null);
      setSuccessMessage(null);
      queryClient.setQueryData(queryKeys.invoice(response.id), response.invoice);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.invoicesRoot() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.queryInvoicesRoot() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.dashboard() }),
      ]);
      navigate(`/invoices/${response.id}`, {
        state: { successMessage: response.message || `已生成同一发票号的${response.invoice.type}。` },
      });
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const unverifyInvoiceMutation = useMutation({
    mutationFn: () => client.unverifyInvoice({
      id: invoiceId,
      body: {
        rowVersion: invoice?.rowVersion ?? "",
        note: "用户申请反审核，返回草稿后重新核对。",
      },
    }),
    onSuccess: async (response) => {
      setInvoice(response.invoice);
      setPendingHsFeedback([]);
      setPersistedInvoiceDraft(normalizeInvoiceForSave(response.invoice, response.id));
      setPersistedInvoiceStatus(normalizeInvoiceStatus(response.invoice.status));
      setMessage(null);
      setSuccessMessage("发票已反审核，当前为草稿状态。");
      queryClient.setQueryData(queryKeys.invoice(response.id), response.invoice);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.invoicesRoot() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.queryInvoicesRoot() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.dashboard() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.invoiceStatusHistory(response.id) }),
      ]);
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const statusTransitionMutation = useMutation({
    mutationFn: ({ targetStatus, note }: { targetStatus: string; note: string }) => client.transitionInvoiceStatus({
      id: invoiceId,
      body: {
        targetStatus,
        rowVersion: invoice?.rowVersion ?? "",
        note,
      },
    }),
    onSuccess: async (response) => {
      setInvoice(response.invoice);
      setPendingHsFeedback([]);
      setPersistedInvoiceDraft(normalizeInvoiceForSave(response.invoice, response.id));
      setPersistedInvoiceStatus(normalizeInvoiceStatus(response.invoice.status));
      setMessage(null);
      setSuccessMessage(`发票状态已更新为“${getInvoiceStatusLabel(response.invoice.status)}”。`);
      queryClient.setQueryData(queryKeys.invoice(response.id), response.invoice);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.invoicesRoot() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.queryInvoicesRoot() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.dashboard() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.invoiceStatusHistory(response.id) }),
      ]);
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const deleteInvoiceMutation = useMutation({
    mutationFn: () => client.deleteInvoice({ id: invoiceId }),
    onSuccess: async (response) => {
      setMessage(null);
      setSuccessMessage(null);
      queryClient.removeQueries({ queryKey: queryKeys.invoice(invoiceId) });
      queryClient.removeQueries({ queryKey: queryKeys.singleWindowCustomsCooDocument(invoiceId) });
      queryClient.removeQueries({ queryKey: queryKeys.singleWindowCustomsCooExportReview(invoiceId) });
      queryClient.removeQueries({ queryKey: queryKeys.singleWindowAgentConsignmentDocument(invoiceId) });
      queryClient.removeQueries({ queryKey: queryKeys.singleWindowAgentConsignmentExportReview(invoiceId) });
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.invoicesRoot() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.queryInvoicesRoot() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.dashboard() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.jobsRoot() }),
      ]);
      navigate("/invoices", {
        replace: true,
        state: { successMessage: response.message || "发票已删除。" },
      });
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const saveCustomOptionMutation = useMutation({
    mutationFn: ({ optionType, value }: { optionType: string; value: string }) =>
      client.saveCustomOption({
        optionType,
        body: { value },
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.customOptionsRoot() });
    },
  });

  const refreshParties = () => Promise.all([
    queryClient.invalidateQueries({ queryKey: queryKeys.masterDataRoot("customers") }),
    queryClient.invalidateQueries({ queryKey: queryKeys.masterDataRoot("exporters") }),
  ]);

  return {
    cloneInvoiceTypeMutation,
    deleteInvoiceMutation,
    exporterSealMutation,
    refreshParties,
    saveCustomOptionMutation,
    saveInvoiceMutation,
    statusTransitionMutation,
    unverifyInvoiceMutation,
  };
}
