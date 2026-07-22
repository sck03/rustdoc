import { FileArchive, FileCheck2, FileSpreadsheet, FolderOpen, RefreshCw, Send, Upload, X } from "lucide-react";
import type { ApiInvoiceListItemDto, SingleWindowExportReview } from "../../api/index.ts";
import { isDesktopBridgeAvailable } from "../../desktop/desktopBridge.ts";
import { formatDate } from "../../ui/formUtils.ts";
import { ViewJobButton } from "../jobs/ViewJobButton.tsx";
import { PageState, PermissionNotice } from "../../ui/PageState.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import type { SingleWindowBusinessType } from "./invoiceListFileNames.ts";
import { flattenSingleWindowReviewIssues, formatReviewSeverity, formatReviewSeverityKey, formatSingleWindowBusinessType, formatSingleWindowNavigationTarget, getAutoRepairGroupKeys } from "./invoiceListModels.ts";

export type SingleWindowActionDraft = { invoice: ApiInvoiceListItemDto };
export function SingleWindowActionsPanel({
  draft,
  isBusy,
  message,
  messageType,
  jobId,
  packagePath,
  review,
  reviewBusinessType,
  reviewInvoiceId,
  isReviewBusy,
  canOperate,
  canExportBookingSheet,
  onCancel,
  onEditCustomsCoo,
  onEditAgentConsignment,
  onReviewCustomsCoo,
  onReviewAgentConsignment,
  onRepairReview,
  onExportCustomsCoo,
  onExportAgentConsignment,
  onImportReceiptPackage,
  onOpenOperationCenter,
  onOpenPackagePath,
  onExportBookingSheet,
}: {
  draft: SingleWindowActionDraft;
  isBusy: boolean;
  message: string | null;
  messageType: "success" | "error";
  jobId: string | null;
  packagePath: string | null;
  review: SingleWindowExportReview | null;
  reviewBusinessType: SingleWindowBusinessType | null;
  reviewInvoiceId: number | null;
  isReviewBusy: boolean;
  canOperate: boolean;
  canExportBookingSheet: boolean;
  onCancel: () => void;
  onEditCustomsCoo: (invoice: ApiInvoiceListItemDto) => void;
  onEditAgentConsignment: (invoice: ApiInvoiceListItemDto) => void;
  onReviewCustomsCoo: (invoice: ApiInvoiceListItemDto) => void;
  onReviewAgentConsignment: (invoice: ApiInvoiceListItemDto) => void;
  onRepairReview: () => void;
  onExportCustomsCoo: (invoice: ApiInvoiceListItemDto) => void;
  onExportAgentConsignment: (invoice: ApiInvoiceListItemDto) => void;
  onImportReceiptPackage: () => void;
  onOpenOperationCenter: () => void;
  onOpenPackagePath: () => void;
  onExportBookingSheet: (invoice: ApiInvoiceListItemDto) => void;
}) {
  const invoice = draft.invoice;
  const currentReview = review && reviewInvoiceId === invoice.id ? review : null;
  const reviewIssues = flattenSingleWindowReviewIssues(currentReview);
  const repairGroupKeys = currentReview ? getAutoRepairGroupKeys(currentReview) : [];

  return (
    <section className="form-section single-window-list-panel" aria-label="单一窗口办理">
      <div className="section-header">
        <div>
          <h2>单一窗口办理</h2>
          <span>{invoice.invoiceNo || `ID ${invoice.id}`}</span>
        </div>
        <button className="icon-button" type="button" title="关闭单一窗口办理" aria-label="关闭单一窗口办理" disabled={isBusy} onClick={onCancel}>
          <X size={17} aria-hidden="true" />
        </button>
      </div>

      {message ? (
        <div className={`${messageType === "error" ? "alert" : "success-alert"} status-action-alert`}>
          <span>{message}</span>
          <div className="toolbar-actions">
            {messageType === "success" ? <ViewJobButton jobId={jobId} disabled={isBusy} /> : null}
            {packagePath && isDesktopBridgeAvailable() ? (
              <button className="text-button compact-text-button" type="button" onClick={onOpenPackagePath}>
                <FolderOpen size={15} aria-hidden="true" />
                <span>打开</span>
              </button>
            ) : null}
          </div>
        </div>
      ) : null}
      {!canOperate ? (
        <PermissionNotice>
          当前单一窗口权限仅允许查看和预检；修复、导出提交包及导入回执包已禁用。
        </PermissionNotice>
      ) : null}

      <div className="detail-grid single-window-list-detail-grid">
        <div className="detail-item">
          <span>发票号</span>
          <strong>{invoice.invoiceNo || "-"}</strong>
        </div>
        <div className="detail-item">
          <span>类型</span>
          <strong>{invoice.type || "-"}</strong>
        </div>
        <div className="detail-item">
          <span>客户</span>
          <strong>{invoice.customerName || "-"}</strong>
        </div>
        <div className="detail-item">
          <span>日期</span>
          <strong>{formatDate(invoice.invoiceDate)}</strong>
        </div>
      </div>

      <div className="toolbar-actions single-window-list-action-buttons">
        <button className="command-button secondary" type="button" disabled={isBusy} onClick={() => onReviewCustomsCoo(invoice)}>
          <FileCheck2 size={17} aria-hidden="true" />
          <span>预检 COO</span>
        </button>
        <button className="command-button secondary" type="button" disabled={!canOperate || isBusy} onClick={() => onEditCustomsCoo(invoice)}>
          <FileCheck2 size={17} aria-hidden="true" />
          <span>编辑海关原产地证</span>
        </button>
        <button className="command-button secondary" type="button" disabled={!canOperate || isBusy} onClick={() => onExportCustomsCoo(invoice)}>
          <Send size={17} aria-hidden="true" />
          <span>导出 COO 包</span>
        </button>
        <button className="command-button secondary" type="button" disabled={isBusy} onClick={() => onReviewAgentConsignment(invoice)}>
          <FileCheck2 size={17} aria-hidden="true" />
          <span>预检 ACD</span>
        </button>
        <button className="command-button secondary" type="button" disabled={!canOperate || isBusy} onClick={() => onEditAgentConsignment(invoice)}>
          <FileArchive size={17} aria-hidden="true" />
          <span>编辑代理委托</span>
        </button>
        <button className="command-button secondary" type="button" disabled={!canOperate || isBusy} onClick={() => onExportAgentConsignment(invoice)}>
          <Send size={17} aria-hidden="true" />
          <span>导出 ACD 包</span>
        </button>
        <button className="command-button secondary" type="button" disabled={!canOperate || isBusy} onClick={onImportReceiptPackage}>
          <Upload size={17} aria-hidden="true" />
          <span>导入回执包</span>
        </button>
        <button className="command-button secondary" type="button" disabled={!canExportBookingSheet || isBusy} onClick={() => onExportBookingSheet(invoice)}>
          <FileSpreadsheet size={17} aria-hidden="true" />
          <span>导出托单</span>
        </button>
        <button className="command-button" type="button" disabled={isBusy} onClick={onOpenOperationCenter}>
          <FolderOpen size={17} aria-hidden="true" />
          <span>操作中心</span>
        </button>
      </div>

      {isReviewBusy && !currentReview ? <PageState tone="loading" title="正在执行导出前预检" description="系统正在核对发票、申报字段和提交包要求。" /> : null}
      {currentReview ? (
        <section className="single-window-list-review" aria-label="发票列表导出前预检">
          <div className="section-header">
            <div>
              <h3>{formatSingleWindowBusinessType(reviewBusinessType)} 导出前预检</h3>
              <span>
                错误 {currentReview.totalErrorCount} / 警告 {currentReview.totalWarningCount} / 可修复 {repairGroupKeys.length}
              </span>
            </div>
            <button
              className="command-button secondary"
              type="button"
              disabled={!canOperate || isBusy || repairGroupKeys.length === 0}
              onClick={onRepairReview}
            >
              <RefreshCw size={17} aria-hidden="true" />
              <span>修复可自动修复项</span>
            </button>
          </div>
          <div className="detail-grid single-window-list-detail-grid">
            <div className="detail-item">
              <span>草稿版本</span>
              <strong>{currentReview.draftRevision}</strong>
            </div>
            <div className="detail-item">
              <span>人工锁定字段</span>
              <strong>{currentReview.manualLockedFieldCount}</strong>
            </div>
            <div className="detail-item">
              <span>来源差异</span>
              <strong>{currentReview.sourceDiffCount}</strong>
            </div>
            <div className="detail-item">
              <span>预检分组</span>
              <strong>{currentReview.groups.length}</strong>
            </div>
          </div>
          {currentReview.sourceDiffSummary ? <div className="info-alert">{currentReview.sourceDiffSummary}</div> : null}
          <ResponsiveTableFrame className="compact-table" label="单一窗口预检问题">
            <table className="single-window-list-review-table">
              <thead>
                <tr>
                  <th>级别</th>
                  <th>分组</th>
                  <th>字段</th>
                  <th>消息</th>
                  <th>可修复</th>
                </tr>
              </thead>
              <tbody>
                {reviewIssues.length === 0 ? (
                  <tr>
                    <td className="empty-cell small-empty" colSpan={5}>
                      未发现导出问题
                    </td>
                  </tr>
                ) : (
                  reviewIssues.map(({ group, issue }, index) => (
                    <tr key={`${group.groupKey}-${issue.navigationTarget.propertyKey}-${index}`}>
                      <td>
                        <span className={`review-severity review-severity-${formatReviewSeverityKey(issue.severity)}`}>
                          {formatReviewSeverity(issue.severity)}
                        </span>
                      </td>
                      <td>{issue.groupDisplayName || group.groupDisplayName || group.groupKey}</td>
                      <td>{formatSingleWindowNavigationTarget(issue.navigationTarget)}</td>
                      <td className="message-cell" title={issue.message}>
                        {issue.message}
                      </td>
                      <td>{issue.canAutoRepair || group.canAutoRepair ? "是" : "否"}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </ResponsiveTableFrame>
        </section>
      ) : null}
    </section>
  );
}
