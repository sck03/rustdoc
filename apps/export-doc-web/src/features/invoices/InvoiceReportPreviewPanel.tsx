import { useEffect, useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { SlidersHorizontal } from "lucide-react";
import { useNavigate } from "react-router-dom";
import type { ApiInvoiceDetailDto, ApiReportHtmlPreviewResponse, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { useModulePermission, usePermissionCapabilities } from "../../app/PermissionAccessContext.tsx";
import { getWorkspaceDeviceCapabilities, useWorkspaceDeviceMode } from "../../app/workspaceDevice.ts";
import { isDesktopBridgeAvailable } from "../../desktop/desktopBridge.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { PermissionNotice } from "../../ui/PageState.tsx";
import { printReportPreviewHtml } from "../reports/printReportPreview.ts";
import { readDefaultExportDirectory } from "../settings/settingsPaths.ts";
import { InvoiceReportAdvancedExportPanel } from "./InvoiceReportAdvancedExportPanel.tsx";
import { InvoiceReportPreviewCanvas } from "./InvoiceReportPreviewCanvas.tsx";
import { InvoiceReportPreviewHeader } from "./InvoiceReportPreviewHeader.tsx";
import { InvoiceReportTemplateControls } from "./InvoiceReportTemplateControls.tsx";
import { buildDocumentPackagePrintHtml, fileNameFromPath } from "./invoiceReportPreviewModel.ts";
import { useInvoiceDocumentPackageWorkspace } from "./useInvoiceDocumentPackageWorkspace.ts";
import { useInvoiceFileExportOperations } from "./useInvoiceFileExportOperations.ts";

type Props = {
  client: ExportDocManagerApiClient;
  invoiceId: number;
  invoiceDraft?: ApiInvoiceDetailDto;
  invoiceNo?: string;
  customerName?: string;
  defaultToAddress?: string;
  hasUnsavedDraftChanges?: boolean;
};

export function InvoiceReportPreviewPanel({
  client,
  invoiceId,
  invoiceDraft,
  invoiceNo,
  customerName,
  defaultToAddress,
  hasUnsavedDraftChanges = false,
}: Props) {
  const reportOutputPermission = useModulePermission("document.invoice-reports");
  const reportDesignPermission = useModulePermission("document.reports");
  const excelPermission = useModulePermission("document.excel");
  const { canManageSettings } = usePermissionCapabilities();
  const navigate = useNavigate();
  const [selectedTemplatePath, setSelectedTemplatePath] = useState("");
  const [withSeal, setWithSeal] = useState(true);
  const [preview, setPreview] = useState<ApiReportHtmlPreviewResponse | null>(null);
  const [statusMessage, setStatusMessage] = useState<string | null>(null);
  const [lastCreatedJobId, setLastCreatedJobId] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [isPrinting, setIsPrinting] = useState(false);
  const [showExportAdvanced, setShowExportAdvanced] = useState(false);
  const workspaceDeviceMode = useWorkspaceDeviceMode();
  const workspaceDeviceCapabilities = getWorkspaceDeviceCapabilities(workspaceDeviceMode);
  const desktopAvailable = isDesktopBridgeAvailable();
  const hasSavedInvoice = invoiceId > 0;
  const canUseSavedInvoiceOutput = hasSavedInvoice
    && !hasUnsavedDraftChanges
    && workspaceDeviceCapabilities.canImportExport;
  const hasPreviewSource = hasSavedInvoice || Boolean(invoiceDraft);
  const invoiceDraftPreviewKey = invoiceDraft ? JSON.stringify(invoiceDraft) : "";

  const templatesQuery = useQuery({
    queryKey: queryKeys.reportTemplates("ExportDocument"),
    queryFn: () => client.listReportTemplates({ reportType: "ExportDocument" }),
    enabled: hasPreviewSource && reportOutputPermission.canView,
    staleTime: 5 * 60 * 1000,
  });
  const settingsQuery = useQuery({
    queryKey: queryKeys.settings(),
    queryFn: () => client.getSettings(),
    enabled: hasPreviewSource && (reportOutputPermission.canOperate || canManageSettings),
    staleTime: 5 * 60 * 1000,
  });
  const templates = templatesQuery.data ?? [];
  const defaultExportDirectory = readDefaultExportDirectory(settingsQuery.data?.settings);

  function clearFeedback() {
    setStatusMessage(null);
    setLastCreatedJobId(null);
    setErrorMessage(null);
  }
  function showError(message: string) {
    setStatusMessage(null);
    setLastCreatedJobId(null);
    setErrorMessage(message);
  }
  function showJob(message: string, jobId: string) {
    setStatusMessage(message);
    setLastCreatedJobId(jobId);
    setErrorMessage(null);
  }
  function showStatus(message: string) {
    setStatusMessage(message);
    setLastCreatedJobId(null);
    setErrorMessage(null);
  }

  const feedback = { clear: clearFeedback, showError, showJob, showStatus };
  const fileExports = useInvoiceFileExportOperations({
    client,
    invoiceId,
    invoiceNo,
    templates,
    selectedTemplatePath,
    withSeal,
    desktopAvailable,
    defaultExportDirectory,
    feedback,
  });
  const documentPackage = useInvoiceDocumentPackageWorkspace({
    client,
    invoiceId,
    invoiceNo,
    customerName,
    defaultToAddress,
    templates,
    settingsResponse: settingsQuery.data,
    desktopAvailable,
    defaultExportDirectory,
    feedback,
    onPreviewGenerated: () => setPreview(null),
  });

  useEffect(() => {
    if (!templates.length) return;
    const preferredTemplate = templates.find(
      (template) => fileNameFromPath(template.templatePath).toLowerCase() === "invoice_template.html",
    ) ?? templates[0];
    setSelectedTemplatePath((current) => current || preferredTemplate.templatePath);
    setWithSeal((current) => selectedTemplatePath ? current : preferredTemplate.withSealDefault);
  }, [selectedTemplatePath, templates]);

  useEffect(() => {
    setPreview(null);
    documentPackage.clearPreview();
    clearFeedback();
  }, [invoiceDraftPreviewKey]);

  const previewMutation = useMutation({
    mutationFn: () => {
      const body = { reportType: "ExportDocument", templatePath: selectedTemplatePath, withSeal };
      return invoiceDraft
        ? client.previewInvoiceReportDraftHtml({ body: { ...body, invoice: invoiceDraft } })
        : client.previewInvoiceReportHtml({ invoiceId, body });
    },
    onSuccess: (response) => {
      setPreview(response);
      documentPackage.clearPreview();
      clearFeedback();
    },
    onError: (error) => showError(readApiError(error)),
  });

  const isBusy =
    templatesQuery.isFetching ||
    settingsQuery.isFetching ||
    previewMutation.isPending ||
    fileExports.isPending ||
    documentPackage.isPending ||
    isPrinting;
  const canPreview = reportOutputPermission.canOperate
    && hasPreviewSource
    && (templates.length === 0 || Boolean(selectedTemplatePath));
  const selectedTemplateCount = documentPackage.selectedTemplates.length;
  const hasValidPackageSelection = selectedTemplateCount > 0 && selectedTemplateCount <= 20;
  const canPreviewPackage = reportOutputPermission.canOperate && canUseSavedInvoiceOutput && hasValidPackageSelection && !isBusy;
  const canPrintPreview = workspaceDeviceCapabilities.canImportExport
    && (Boolean(preview?.html) || Boolean(documentPackage.preview?.items.some((item) => item.html)))
    && !isBusy;
  const canGeneratePdf = reportOutputPermission.canOperate
    && canUseSavedInvoiceOutput
    && canPreview
    && (!desktopAvailable || Boolean(fileExports.pdfDestinationPath.trim()))
    && !isBusy;
  const canQuickGeneratePdf = reportOutputPermission.canOperate && canUseSavedInvoiceOutput && canPreview && !isBusy;
  const canGenerateBookingSheet = excelPermission.canOperate
    && canUseSavedInvoiceOutput
    && (!desktopAvailable || Boolean(fileExports.bookingSheetDestinationPath.trim()))
    && !isBusy;
  const canQuickGenerateBookingSheet = excelPermission.canOperate && canUseSavedInvoiceOutput && !isBusy;
  const canGeneratePackage = reportOutputPermission.canOperate
    && canUseSavedInvoiceOutput
    && hasValidPackageSelection
    && (!desktopAvailable || Boolean(documentPackage.destinationPath.trim()))
    && !isBusy;
  const canSendDocumentEmail = reportOutputPermission.canOperate
    && canUseSavedInvoiceOutput
    && hasValidPackageSelection
    && Boolean(documentPackage.emailToAddress.trim())
    && !isBusy;
  const canEditPackageConfig = canManageSettings && Boolean(settingsQuery.data?.settings) && !isBusy;
  const canSavePackageConfig = canEditPackageConfig
    && documentPackage.configDirty
    && documentPackage.configDraft.items.length > 0;
  const canOpenTemplateDesigner = workspaceDeviceCapabilities.canUseDenseWorkbench
    && reportDesignPermission.canView
    && Boolean(selectedTemplatePath)
    && !isBusy;
  const templateMessage = templatesQuery.isError ? readApiError(templatesQuery.error) : null;
  const previewStoragePolicy = preview?.storagePolicy || documentPackage.preview?.storagePolicy || "";

  function handleTemplateChange(value: string) {
    setSelectedTemplatePath(value);
    const template = templates.find((item) => item.templatePath === value);
    if (template) setWithSeal(template.withSealDefault);
    setPreview(null);
    documentPackage.clearPreview();
    clearFeedback();
  }

  function openTemplateDesigner() {
    if (!selectedTemplatePath) return;
    const params = new URLSearchParams({ reportType: "ExportDocument" });
    if (hasSavedInvoice) params.set("invoiceId", String(invoiceId));
    const templateFileName = fileNameFromPath(selectedTemplatePath);
    if (templateFileName) params.set("template", templateFileName);
    navigate(`/reports/templates?${params.toString()}`);
  }

  async function printPreview() {
    const html = documentPackage.preview?.items.length
      ? buildDocumentPackagePrintHtml(documentPackage.preview)
      : preview?.html ?? "";
    if (!html.trim()) {
      showError("请先生成预览后再打印。");
      return;
    }
    try {
      setIsPrinting(true);
      setErrorMessage(null);
      await printReportPreviewHtml(html, documentPackage.preview?.items.length ? "单据包打印预览" : "报表打印预览");
      showStatus("已打开打印对话框。");
    } catch (error) {
      showError(error instanceof Error ? error.message : "打印失败。");
    } finally {
      setIsPrinting(false);
    }
  }

  return (
    <section className="form-section report-preview-section" aria-label="报表预览">
      <InvoiceReportPreviewHeader
        canPreview={canPreview}
        canPrint={canPrintPreview}
        canRefreshTemplates={reportOutputPermission.canView}
        errorMessage={errorMessage}
        hasSavedInvoice={hasSavedInvoice && workspaceDeviceCapabilities.canImportExport}
        hasUnsavedDraftChanges={hasUnsavedDraftChanges}
        isBusy={isBusy}
        jobId={lastCreatedJobId}
        previewStoragePolicy={previewStoragePolicy}
        statusMessage={statusMessage}
        templateMessage={templateMessage}
        onPreview={() => previewMutation.mutate()}
        onPrint={() => void printPreview()}
        onRefresh={() => void templatesQuery.refetch()}
      />
      {!reportOutputPermission.canOperate ? (
        <PermissionNotice>当前模板仅允许查看发票，未授予报表预览和单据输出操作权限。</PermissionNotice>
      ) : null}
      <InvoiceReportTemplateControls
        canConfigureOutput={reportOutputPermission.canOperate}
        canOpenTemplateDesigner={canOpenTemplateDesigner}
        canQuickGenerateBookingSheet={canQuickGenerateBookingSheet}
        canQuickGeneratePdf={canQuickGeneratePdf}
        desktopAvailable={desktopAvailable}
        hasSavedInvoice={hasSavedInvoice}
        isBusy={isBusy}
        showTemplateDesigner={workspaceDeviceCapabilities.canUseDenseWorkbench && reportDesignPermission.canView}
        showTemplateSettings={workspaceDeviceCapabilities.canUseAdvancedTools && canManageSettings}
        selectedTemplatePath={selectedTemplatePath}
        templates={templates}
        withSeal={withSeal}
        onExportBookingSheet={() => void fileExports.exportBookingSheetWithSaveDialog()}
        onExportPdf={() => void fileExports.exportPdfWithSaveDialog()}
        onManageTemplates={() => navigate("/settings?section=documentTemplates")}
        onOpenTemplateDesigner={openTemplateDesigner}
        onTemplateChange={handleTemplateChange}
        onWithSealChange={(value) => {
          setWithSeal(value);
          setPreview(null);
          documentPackage.clearPreview();
          clearFeedback();
        }}
      />

      {hasSavedInvoice && reportOutputPermission.canOperate && workspaceDeviceCapabilities.canImportExport ? (
        <details
          className="report-export-advanced"
          open={showExportAdvanced}
          onToggle={(event) => setShowExportAdvanced(event.currentTarget.open)}
        >
          <summary>
            <span><SlidersHorizontal size={16} aria-hidden="true" />高级导出</span>
            <small>手动路径、单据包、邮件附件</small>
          </summary>
          {showExportAdvanced ? (
            <InvoiceReportAdvancedExportPanel
              desktopAvailable={desktopAvailable}
              isBusy={isBusy}
              templatesLoading={templatesQuery.isFetching}
              canGeneratePdf={canGeneratePdf}
              canGenerateBookingSheet={canGenerateBookingSheet}
              canPreviewPackage={canPreviewPackage}
              canSavePackageConfig={canSavePackageConfig}
              canEditPackageConfig={canEditPackageConfig}
              canGeneratePackage={canGeneratePackage}
              canSendDocumentEmail={canSendDocumentEmail}
              fileExports={fileExports}
              documentPackage={documentPackage}
              onOpenEmailSettings={() => navigate("/settings?section=email")}
              onError={showError}
            />
          ) : null}
        </details>
      ) : null}

      <InvoiceReportPreviewCanvas isBusy={isBusy} packagePreview={documentPackage.preview} preview={preview} />
    </section>
  );
}
