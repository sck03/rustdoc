import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { FileWarning, RefreshCw } from "lucide-react";
import type { ApiInvoiceDataMaintenancePreviewResponse } from "../../api/index.ts";
import { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { formatRuntimeDate } from "./settingsFormatters.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";

export function DataOwnershipUnavailablePanel() {
  return (
    <section className="form-section shared-ownership-section" aria-label="数据归属说明">
      <div className="section-header">
        <div>
          <h2>数据归属</h2>
          <p className="section-description">用于多人团队发生岗位交接时，把发票和付款报销改派给接手人员。</p>
        </div>
      </div>
      <InlineNotice tone="info">
        当前版本按单机单用户方式使用，不需要进行数据归属改派。启用全功能团队版并由管理员维护账号后，此处会提供来源人员、接手人员和业务范围选择。
      </InlineNotice>
    </section>
  );
}
export function InvoiceDataMaintenancePanel({
  client,
  canManageSettings,
}: {
  client: ExportDocManagerApiClient;
  canManageSettings: boolean;
}) {
  const requestConfirmation = useConfirmation();
  const queryClient = useQueryClient();
  const [invoiceId, setInvoiceId] = useState("");
  const [invoiceNoConfirmation, setInvoiceNoConfirmation] = useState("");
  const [reason, setReason] = useState("");
  const [preview, setPreview] = useState<ApiInvoiceDataMaintenancePreviewResponse | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

  const lookupMutation = useMutation({
    mutationFn: () => client.getInvoiceDataMaintenancePreview({ id: Number(invoiceId) }),
    onSuccess: (response) => {
      setPreview(response);
      setInvoiceNoConfirmation("");
      setReason("");
      setMessage(null);
      setSuccessMessage(null);
    },
    onError: (error) => {
      setPreview(null);
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const purgeMutation = useMutation({
    mutationFn: () => client.purgeCancelledInvoice({
      id: preview?.id ?? 0,
      body: {
        invoiceNoConfirmation: invoiceNoConfirmation.trim(),
        reason: reason.trim(),
      },
    }),
    onSuccess: async (response) => {
      queryClient.removeQueries({ queryKey: queryKeys.invoice(response.invoiceId) });
      queryClient.removeQueries({ queryKey: queryKeys.invoiceStatusHistory(response.invoiceId) });
      queryClient.removeQueries({ queryKey: queryKeys.singleWindowCustomsCooDocument(response.invoiceId) });
      queryClient.removeQueries({ queryKey: queryKeys.singleWindowCustomsCooExportReview(response.invoiceId) });
      queryClient.removeQueries({ queryKey: queryKeys.singleWindowAgentConsignmentDocument(response.invoiceId) });
      queryClient.removeQueries({ queryKey: queryKeys.singleWindowAgentConsignmentExportReview(response.invoiceId) });
      setPreview(null);
      setInvoiceId("");
      setInvoiceNoConfirmation("");
      setReason("");
      setMessage(null);
      setSuccessMessage(response.message || "已完成发票数据清理，审计记录已保留。");
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.invoicesRoot() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.queryInvoicesRoot() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.dashboard() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterRoot() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.auditLogsRoot() }),
      ]);
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const numericInvoiceId = Number(invoiceId);
  const canLookup = canManageSettings
    && Number.isInteger(numericInvoiceId)
    && numericInvoiceId > 0
    && !lookupMutation.isPending
    && !purgeMutation.isPending;
  const canPurge = canManageSettings
    && Boolean(preview?.canPurge)
    && invoiceNoConfirmation.trim() === preview?.invoiceNo
    && Boolean(reason.trim())
    && reason.trim().length <= 500
    && !purgeMutation.isPending;

  async function handlePurge() {
    if (!preview || !canPurge) return;
    if (!await requestConfirmation({
      title: "永久清理已作废发票",
      description: `将物理删除发票“${preview.invoiceNo}”及其明细和关联工作区记录。`,
      details: [
        "该操作仅用于确有依据的错误数据或测试数据清理，不能撤销。",
        "清理原因、操作人、原状态和发票摘要会继续保留在审计日志中。",
      ],
      confirmLabel: "确认永久清理",
      tone: "danger",
    })) return;

    setMessage(null);
    setSuccessMessage(null);
    purgeMutation.mutate();
  }

  return (
    <section className="form-section invoice-data-maintenance-section" aria-label="发票数据清理">
      <div className="section-header">
        <div>
          <h2>发票数据清理</h2>
          <p className="section-description">独立的管理员危险操作，只处理已经作废且确有清理依据的发票。</p>
        </div>
      </div>
      <InlineNotice tone="warning" title="商用留存规则">
        草稿在发票编辑页直接删除；已核对、已出运、已结汇只能作废；已作废默认长期保留，不在日常业务界面提供删除入口。
      </InlineNotice>
      {message ? <InlineNotice tone="error" title="发票数据清理失败">{message}</InlineNotice> : null}
      {successMessage ? <InlineNotice tone="success">{successMessage}</InlineNotice> : null}
      <div className="backup-action-grid invoice-maintenance-query-grid">
        <label>
          <span>发票 ID</span>
          <input
            type="number"
            min={1}
            step={1}
            value={invoiceId}
            disabled={!canManageSettings || purgeMutation.isPending}
            placeholder="从发票详情地址或列表读取"
            onChange={(event) => {
              setInvoiceId(event.target.value);
              setPreview(null);
              setInvoiceNoConfirmation("");
              setReason("");
              setMessage(null);
              setSuccessMessage(null);
            }}
          />
        </label>
        <button
          className="command-button"
          type="button"
          disabled={!canLookup}
          onClick={() => lookupMutation.mutate()}
        >
          <RefreshCw size={17} aria-hidden="true" />
          <span>{lookupMutation.isPending ? "查询中" : "查询发票"}</span>
        </button>
      </div>

      {preview ? (
        <>
          <div className="detail-grid runtime-detail-grid invoice-maintenance-preview">
            <div className="detail-item">
              <span>发票号</span>
              <strong>{preview.invoiceNo || "-"}</strong>
            </div>
            <div className="detail-item">
              <span>当前状态</span>
              <strong>{preview.statusDisplayName || preview.status}</strong>
            </div>
            <div className="detail-item">
              <span>数据类型</span>
              <strong>{preview.type || "-"}</strong>
            </div>
            <div className="detail-item">
              <span>发票日期</span>
              <strong>{formatRuntimeDate(preview.invoiceDate)}</strong>
            </div>
            <div className="detail-item detail-item-wide">
              <span>客户</span>
              <strong>{preview.customerName || "-"}</strong>
            </div>
          </div>
          <InlineNotice tone={preview.canPurge ? "warning" : "info"}>
            {preview.guidance}
          </InlineNotice>
          {preview.canPurge ? (
            <div className="invoice-maintenance-danger-zone">
              <label>
                <span>再次输入完整发票号</span>
                <input
                  value={invoiceNoConfirmation}
                  disabled={!canManageSettings || purgeMutation.isPending}
                  placeholder={preview.invoiceNo}
                  autoComplete="off"
                  onChange={(event) => setInvoiceNoConfirmation(event.target.value)}
                />
                <small>必须与“{preview.invoiceNo}”完全一致。</small>
              </label>
              <label className="textarea-field settings-textarea-field">
                <span>清理原因</span>
                <textarea
                  value={reason}
                  maxLength={500}
                  disabled={!canManageSettings || purgeMutation.isPending}
                  placeholder="说明数据来源、错误原因和清理依据"
                  onChange={(event) => setReason(event.target.value)}
                />
                <small>{reason.trim().length}/500；该内容会进入审计日志。</small>
              </label>
              <button
                className="command-button danger-command"
                type="button"
                disabled={!canPurge}
                onClick={() => void handlePurge()}
              >
                <FileWarning size={17} aria-hidden="true" />
                <span>{purgeMutation.isPending ? "正在清理" : "永久清理已作废发票"}</span>
              </button>
            </div>
          ) : null}
          <p className="settings-helper-text">{preview.storagePolicy}</p>
        </>
      ) : null}
    </section>
  );
}
