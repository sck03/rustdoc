import { useEffect, useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { SlidersHorizontal } from "lucide-react";
import { useLocation, useNavigate } from "react-router-dom";
import type { ApiInvoiceDetailDto, ApiReportHtmlPreviewResponse, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { useModulePermission, usePermission, usePermissionCapabilities } from "../../app/PermissionAccessContext.tsx";
import { permissionActions, permissionResources } from "../../app/permissionCatalog.ts";
import { useWorkspaceDeviceProfile } from "../../app/workspaceDevice.ts";
import { isDesktopBridgeAvailable } from "../../desktop/desktopBridge.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { PermissionNotice } from "../../ui/PageState.tsx";
import { printReportPreviewHtml } from "../reports/printReportPreview.ts";
import { readDefaultReportTemplatePath, resolveReportTemplatePath } from "../reports/reportTemplateSelectionModel.ts";
import { readDefaultExportDirectory } from "../settings/settingsPaths.ts";
import { createReportTemplateReturnState } from "../reports/reportTemplateReturnNavigation.ts";
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
  const previewPermission = usePermission(permissionResources.invoiceOutput, permissionActions.preview);
  const printPermission = usePermission(permissionResources.invoiceOutput, permissionActions.print);
  const pdfPermission = usePermission(permissionResources.invoiceOutput, permissionActions.exportPdf);
  const zipPermission = usePermission(permissionResources.invoiceOutput, permissionActions.exportZip);
  const emailPermission = usePermission(permissionResources.invoiceOutput, permissionActions.sendEmail);
  const emailSendPermission = usePermission(permissionResources.emailDelivery, permissionActions.send);
  const templateViewPermission = usePermission(permissionResources.reportTemplates, permissionActions.view);
  const excelPermission = useModulePermission("document.excel");
  const { canManageSettings } = usePermissionCapabilities();
  const location = useLocation();
  const navigate = useNavigate();
  const [selectedTemplatePath, setSelectedTemplatePath] = useState("");
  const [withSeal, setWithSeal] = useState(true);
  const [preview, setPreview] = useState<ApiReportHtmlPreviewResponse | null>(null);
  const [statusMessage, setStatusMessage] = useState<string | null>(null);
  const [lastCreatedJobId, setLastCreatedJobId] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [isPrinting, setIsPrinting] = useState(false);
  const [showExportAdvanced, setShowExportAdvanced] = useState(false);
  const workspaceDeviceCapabilities = useWorkspaceDeviceProfile().capabilities;
  const desktopAvailable = isDesktopBridgeAvailable();
  const hasSavedInvoice = invoiceId > 0;
  const canUseSavedInvoiceOutput = hasSavedInvoice
    && !hasUnsavedDraftChanges
    && workspaceDeviceCapabilities.canImportExport;
  const hasPreviewSource = hasSavedInvoice || Boolean(invoiceDraft);

  const templatesQuery = useQuery({
    queryKey: queryKeys.reportTemplates("ExportDocument"),
    queryFn: ({ signal }) => client.listReportTemplates({ reportType: "ExportDocument" }, { signal }),
    enabled: hasPreviewSource && templateViewPermission.allowed,
    staleTime: 5 * 60 * 1000,
  });
  const settingsQuery = useQuery({
    queryKey: queryKeys.settings(),
    queryFn: ({ signal }) => client.getSettings({ signal }),
    enabled: hasPreviewSource && (templateViewPermission.allowed || canManageSettings),
    staleTime: 5 * 60 * 1000,
  });
  const templates = templatesQuery.data ?? [];
  const configuredTemplatePath = readDefaultReportTemplatePath(
    settingsQuery.data?.settings,
    "ExportDocument",
  );
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
    if (!templates.length || settingsQuery.isFetching) return;
    const next = resolveReportTemplatePath({
      templates,
      currentPath: selectedTemplatePath,
      configuredPath: configuredTemplatePath,
      fallbackFileName: "invoice_template.html",
    });
    if (next !== selectedTemplatePath) {
      setSelectedTemplatePath(next);
      setWithSeal(templates.find((item) => item.templatePath === next)?.withSealDefault ?? true);
    }
  }, [configuredTemplatePath, selectedTemplatePath, settingsQuery.isFetching, templates]);

  useEffect(() => {
    setPreview(null);
    documentPackage.clearPreview();
    clearFeedback();
  }, [invoiceDraft]);

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
  const canPreview = previewPermission.allowed
    && hasPreviewSource
    && (templates.length === 0 || Boolean(selectedTemplatePath));
  const selectedTemplateCount = documentPackage.selectedTemplates.length;
  const hasValidPackageSelection = selectedTemplateCount > 0 && selectedTemplateCount <= 20;
  const canPreviewPackage = previewPermission.allowed && canUseSavedInvoiceOutput && hasValidPackageSelection && !isBusy;
  const canPrintPreview = printPermission.allowed && workspaceDeviceCapabilities.canImportExport
    && (Boolean(preview?.html) || Boolean(documentPackage.preview?.items.some((item) => item.html)))
    && !isBusy;
  const canGeneratePdf = pdfPermission.allowed
    && canUseSavedInvoiceOutput
    && Boolean(selectedTemplatePath)
    && (!desktopAvailable || Boolean(fileExports.pdfDestinationPath.trim()))
    && !isBusy;
  const canQuickGeneratePdf = pdfPermission.allowed && canUseSavedInvoiceOutput && Boolean(selectedTemplatePath) && !isBusy;
  const canGenerateBookingSheet = excelPermission.canOperate
    && canUseSavedInvoiceOutput
    && (!desktopAvailable || Boolean(fileExports.bookingSheetDestinationPath.trim()))
    && !isBusy;
  const canQuickGenerateBookingSheet = excelPermission.canOperate && canUseSavedInvoiceOutput && !isBusy;
  const canGeneratePackage = zipPermission.allowed
    && canUseSavedInvoiceOutput
    && hasValidPackageSelection
    && (!desktopAvailable || Boolean(documentPackage.destinationPath.trim()))
    && !isBusy;
  const canSendDocumentEmail = emailPermission.allowed
    && emailSendPermission.allowed
    && canUseSavedInvoiceOutput
    && hasValidPackageSelection
    && Boolean(documentPackage.emailToAddress.trim())
    && !isBusy;
  const canOpenTemplateManagement = (templateViewPermission.allowed || canManageSettings) && !isBusy;
  const hasAdvancedOutput = pdfPermission.allowed || zipPermission.allowed || emailPermission.allowed || excelPermission.canOperate;
  const templateMessage = templatesQuery.isError ? readApiError(templatesQuery.error) : null;

  function handleTemplateChange(value: string) {
    setSelectedTemplatePath(value);
    const template = templates.find((item) => item.templatePath === value);
    if (template) setWithSeal(template.withSealDefault ?? true);
    setPreview(null);
    documentPackage.clearPreview();
    clearFeedback();
  }

  function openTemplateManagement() {
    const params = new URLSearchParams({ reportType: "ExportDocument" });
    if (hasSavedInvoice) params.set("invoiceId", String(invoiceId));
    const templateFileName = fileNameFromPath(selectedTemplatePath);
    if (templateFileName) params.set("template", templateFileName);
    navigate(`/reports/templates/manage?${params.toString()}`, {
      state: createReportTemplateReturnState(location, "返回发票"),
    });
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
        canRefreshTemplates={templateViewPermission.allowed}
        errorMessage={errorMessage}
        hasSavedInvoice={hasSavedInvoice && workspaceDeviceCapabilities.canImportExport}
        hasUnsavedDraftChanges={hasUnsavedDraftChanges}
        isBusy={isBusy}
        jobId={lastCreatedJobId}
        statusMessage={statusMessage}
        templateMessage={templateMessage}
        onPreview={() => previewMutation.mutate()}
        onPrint={() => void printPreview()}
        onRefresh={() => void templatesQuery.refetch()}
      />
      {!previewPermission.allowed ? (
        <PermissionNotice>当前账号未授予发票报表预览权限；打印、PDF、ZIP 和邮件外发仍按各自动作权限独立控制。</PermissionNotice>
      ) : null}
      <InvoiceReportTemplateControls
        canConfigureOutput={templateViewPermission.allowed}
        canQuickGenerateBookingSheet={canQuickGenerateBookingSheet}
        canQuickGeneratePdf={canQuickGeneratePdf}
        desktopAvailable={desktopAvailable}
        hasSavedInvoice={hasSavedInvoice}
        isBusy={isBusy}
        canOpenTemplateManagement={canOpenTemplateManagement}
        selectedTemplatePath={selectedTemplatePath}
        templates={templates}
        withSeal={withSeal}
        onExportBookingSheet={() => void fileExports.exportBookingSheetWithSaveDialog()}
        onExportPdf={() => void fileExports.exportPdfWithSaveDialog()}
        onOpenTemplateManagement={openTemplateManagement}
        onTemplateChange={handleTemplateChange}
        onWithSealChange={(value) => {
          setWithSeal(value);
          setPreview(null);
          documentPackage.clearPreview();
          clearFeedback();
        }}
      />

      {hasSavedInvoice && hasAdvancedOutput && workspaceDeviceCapabilities.canImportExport ? (
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
