import { useMemo, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";
import { useTransientPreview } from "../../ui/useTransientPreview.ts";

export function useInvoiceDataMaintenance(client: ExportDocManagerApiClient, canManageSettings: boolean) {
  const cache = useQueryClient(), requestConfirmation = useConfirmation();
  const [invoiceId, setInvoiceId] = useState("");
  const [invoiceNoConfirmation, setInvoiceNoConfirmation] = useState("");
  const [reason, setReason] = useState("");
  const [message, setMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const scope = `invoice-maintenance:${invoiceId}:${canManageSettings}`;
  const previewScope = useMemo(() => ({ client, scope }), [client, scope]);
  const onError = (error: unknown) => { setMessage(readApiError(error)); setSuccessMessage(null); };
  const lookup = useTransientPreview(previewScope,
    signal => client.getInvoiceDataMaintenancePreview({ id: Number(invoiceId) }, { signal }), onError,
    () => { setInvoiceNoConfirmation(""); setReason(""); setMessage(null); setSuccessMessage(null); });
  const preview = lookup.preview;
  const canPurge = canManageSettings && Boolean(preview?.canPurge)
    && invoiceNoConfirmation.trim() === preview?.invoiceNo
    && Boolean(reason.trim()) && reason.trim().length <= 500;
  const purgeMutation = useScopedMutation(scope, client, {
    mutationFn: async (_: void, signal, { checkCurrent }) => {
      if (!preview || !canPurge) return null;
      const request = { id: preview.id, body: { invoiceNoConfirmation: invoiceNoConfirmation.trim(), reason: reason.trim() } };
      if (!await requestConfirmation({ title: "永久清理已作废发票",
        description: `将物理删除发票“${preview.invoiceNo}”及其明细和关联工作区记录。`,
        details: ["该操作仅用于确有依据的错误数据或测试数据清理，不能撤销。", "清理原因、操作人、原状态和发票摘要会继续保留在审计日志中。"],
        confirmLabel: "确认永久清理", tone: "danger" })) return null;
      checkCurrent();
      return client.purgeCancelledInvoice(request, { signal });
    },
    onSuccess: response => {
      if (!response) return;
      for (const queryKey of [queryKeys.invoice(response.invoiceId), queryKeys.invoiceStatusHistory(response.invoiceId),
        queryKeys.singleWindowCustomsCooDocument(response.invoiceId), queryKeys.singleWindowCustomsCooExportReview(response.invoiceId),
        queryKeys.singleWindowAgentConsignmentDocument(response.invoiceId), queryKeys.singleWindowAgentConsignmentExportReview(response.invoiceId)])
        cache.removeQueries({ queryKey });
      lookup.clear(); setInvoiceNoConfirmation(""); setReason(""); setMessage(null);
      setSuccessMessage(response.message || "已完成发票数据清理，审计记录已保留。");
    },
    refreshKeys: response => response ? [queryKeys.invoicesRoot(), queryKeys.queryInvoicesRoot(), queryKeys.dashboard(),
      queryKeys.singleWindowOperationCenterRoot(), queryKeys.auditLogsRoot()] : [],
    onError,
  });
  return { invoiceId, invoiceNoConfirmation, setInvoiceNoConfirmation, reason, setReason, preview, message, successMessage,
    purgeMutation, lookupMutation: { isPending: lookup.isPending, mutate: () => { if (canManageSettings) void lookup.generate(); } },
    canLookup: canManageSettings && Number.isInteger(Number(invoiceId)) && Number(invoiceId) > 0 && !lookup.isPending && !purgeMutation.isPending,
    canPurge: canPurge && !purgeMutation.isPending,
    changeInvoiceId: (value: string) => {
      setInvoiceId(value); lookup.clear(); setInvoiceNoConfirmation(""); setReason(""); setMessage(null); setSuccessMessage(null);
    },
    handlePurge: () => { setMessage(null); setSuccessMessage(null); purgeMutation.mutate(); },
  };
}
