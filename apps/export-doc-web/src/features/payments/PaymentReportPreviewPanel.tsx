import { Eye, FileDown, LayoutTemplate, Printer, RefreshCw } from "lucide-react";
import { useLocation, useNavigate } from "react-router-dom";
import type { ApiPaymentDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { usePermission } from "../../app/PermissionAccessContext.tsx";
import { permissionActions, permissionResources } from "../../app/permissionCatalog.ts";
import { SelectField } from "../../ui/FormFields.tsx";
import { InlineNotice, PermissionNotice } from "../../ui/PageState.tsx";
import { ViewJobButton } from "../jobs/ViewJobButton.tsx";
import { buildReportPdfDefaultFileName } from "../reports/reportFileNames.ts";
import { fileNameFromTemplatePath } from "../reports/reportTemplateSelectionModel.ts";
import { createReportTemplateReturnState } from "../reports/reportTemplateReturnNavigation.ts";
import { usePaymentReportTemplates } from "./usePaymentReportTemplates.ts";
import { usePaymentReportPreview } from "./usePaymentReportPreview.ts";
import { usePaymentReportOutput } from "./usePaymentReportOutput.ts";

export function PaymentReportPreviewPanel({ client, paymentId, paymentDraft, hasUnsavedDraftChanges = false }: {
  client: ExportDocManagerApiClient; paymentId: number; paymentDraft?: ApiPaymentDto; hasUnsavedDraftChanges?: boolean;
}) {
  const record = paymentId > 0 ? paymentDraft ?? null : undefined;
  const previewPermission = usePermission(permissionResources.paymentOutput, permissionActions.preview, record);
  const printPermission = usePermission(permissionResources.paymentOutput, permissionActions.print, record);
  const pdfPermission = usePermission(permissionResources.paymentOutput, permissionActions.exportPdf, record);
  const templatePermission = usePermission(permissionResources.reportTemplates, permissionActions.view);
  const catalogPermission = usePermission(permissionResources.reportCatalog, permissionActions.view);
  const location = useLocation();
  const navigate = useNavigate();
  const hasSource = paymentId > 0 || Boolean(paymentDraft);
  const templates = usePaymentReportTemplates(client, hasSource && catalogPermission.allowed);
  const canPreview = previewPermission.allowed && templates.ready;
  const preview = usePaymentReportPreview(client, { paymentId, paymentDraft, templatePath: templates.selectedPath }, templates.revision, canPreview);
  const output = usePaymentReportOutput(client, {
    paymentId, templatePath: templates.selectedPath, defaultExportDirectory: templates.defaultExportDirectory,
    defaultFileName: buildReportPdfDefaultFileName({
      templatePath: templates.selectedPath,
      displayName: templates.views.find(item => item.templatePath === templates.selectedPath)?.displayName,
      fallbackTitle: "Payment Voucher",
      documentNumber: paymentDraft?.voucherNo?.trim() || (paymentId > 0 ? `payment-${paymentId}` : "payment-draft"),
    }),
  });
  const busy = templates.busy || preview.busy || output.busy;
  const saved = paymentId > 0 && !hasUnsavedDraftChanges;
  function openTemplates() {
    const params = new URLSearchParams({ reportType: "PaymentVoucher" });
    if (paymentId > 0) params.set("paymentId", String(paymentId));
    if (templates.selectedPath) params.set("template", fileNameFromTemplatePath(templates.selectedPath));
    navigate(`/reports/templates/manage?${params}`, { state: createReportTemplateReturnState(location, "返回付款/报销单") });
  }
  return <section className="form-section report-preview-section" aria-label="付款/报销单预览"
    data-selected-template-path={templates.selectedPath} data-preview-template-path={preview.preview?.templatePath ?? ""}>
    <div className="section-header">
      <h2>付款/报销单预览</h2>
      <div className="toolbar-actions">
        <button className="icon-button" type="button" title="刷新模板" aria-label="刷新模板"
          disabled={busy || !catalogPermission.allowed} onClick={() => void templates.refresh()}><RefreshCw size={17} aria-hidden="true" /></button>
        {templatePermission.allowed && <button className="command-button secondary" type="button" disabled={busy} onClick={openTemplates}>
          <LayoutTemplate size={17} aria-hidden="true" /><span>报表模板管理</span>
        </button>}
        <button className="command-button secondary" type="button" disabled={busy || !canPreview} onClick={preview.generate}>
          <Eye size={17} aria-hidden="true" /><span>预览</span>
        </button>
        <button className="command-button secondary" type="button" title="打印当前预览"
          disabled={busy || !printPermission.allowed || !preview.preview?.html} onClick={() => { if (preview.preview?.html) void output.print(preview.preview.html); }}>
          <Printer size={17} aria-hidden="true" /><span>打印</span>
        </button>
        <button className="command-button secondary" type="button"
          title={saved ? output.desktop ? "选择保存位置并生成 PDF" : "下载付款/报销 PDF" : "请先保存付款/报销单"}
          disabled={busy || !saved || !pdfPermission.allowed || !templates.ready} onClick={output.exportPdf}>
          <FileDown size={17} aria-hidden="true" /><span>导出 PDF</span>
        </button>
      </div>
    </div>
    {templates.error && <InlineNotice tone="warning" title="报表模板提示">{templates.error}</InlineNotice>}
    {!previewPermission.allowed && <PermissionNotice>当前账号未授予付款报销单据预览权限；PDF 导出按各自动作权限控制。</PermissionNotice>}
    {(preview.error || output.error) && <InlineNotice tone="error" title="付款报表生成失败">{preview.error || output.error}</InlineNotice>}
    {output.message && <InlineNotice tone="success" action={<ViewJobButton jobId={output.jobId} disabled={busy} />}>{output.message}</InlineNotice>}
    {!saved && <InlineNotice tone="info">预览和打印使用当前草稿；导出 PDF 前请先保存付款/报销单。</InlineNotice>}
    <div className="report-preview-controls">
      <SelectField label="模板" value={templates.selectedPath} disabled={busy || !templates.ready}
        options={templates.views.map(template => ({ value: template.templatePath, label: template.displayName }))} onChange={templates.selectTemplate} />
    </div>
    <div className="report-preview-frame-wrap">
      {preview.preview ? <iframe className="report-preview-frame" title="付款/报销单预览" sandbox="" srcDoc={preview.preview.html} data-template-path={preview.preview.templatePath} />
        : <div className="report-preview-empty">{busy ? "加载中" : templates.views.length ? "选择模板后点击预览，核对票面再打印。" : "暂无可用模板，请刷新或联系管理员。"}</div>}
    </div>
  </section>;
}
