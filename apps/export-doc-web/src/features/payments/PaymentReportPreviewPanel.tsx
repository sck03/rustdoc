import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Eye, FileDown, LayoutTemplate, Printer, RefreshCw, Save, Settings } from "lucide-react";
import { useLocation, useNavigate } from "react-router-dom";
import { ApiPaymentDto, ApiPaymentReportHtmlPreviewResponse, ApiReportTemplateDto, AppSettings, ExportDocManagerApiClient } from "../../api/index.ts";

type SettingsLike = AppSettings | Record<string, unknown>;
import { useModulePermission, usePermissionCapabilities } from "../../app/PermissionAccessContext.tsx";
import { queryKeys } from "../../api/queryKeys.ts";
import { isDesktopBridgeAvailable, selectSavePdfPath } from "../../desktop/desktopBridge.ts";
import { DesktopIconButton, readDesktopError, renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { SelectField } from "../../ui/FormFields.tsx";
import { PathField } from "../../ui/PathField.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { downloadJobResultWhenReady } from "../../ui/downloadJobResult.ts";
import { InlineNotice, PermissionNotice } from "../../ui/PageState.tsx";
import { ViewJobButton } from "../jobs/ViewJobButton.tsx";
import { buildReportPdfDefaultFileName } from "../reports/reportFileNames.ts";
import { printReportPreviewHtml } from "../reports/printReportPreview.ts";
import {
  fileNameFromTemplatePath,
  normalizeTemplatePath,
  readDefaultReportTemplatePath,
  resolveReportTemplatePath,
  templatePathsMatch,
} from "../reports/reportTemplateSelectionModel.ts";
import { readDefaultExportDirectory } from "../settings/settingsPaths.ts";
import { createSettingsReturnState } from "../settings/settingsReturnNavigation.ts";
import { useAbortableOperation } from "../../ui/useAbortableOperation.ts";

export function PaymentReportPreviewPanel({
  client,
  paymentId,
  paymentDraft,
  hasUnsavedDraftChanges = false,
}: {
  client: ExportDocManagerApiClient;
  paymentId: number;
  paymentDraft?: ApiPaymentDto;
  hasUnsavedDraftChanges?: boolean;
}) {
  const reportOutputPermission = useModulePermission("document.payment-reports");
  const reportDesignPermission = useModulePermission("document.reports");
  const { canManageSettings } = usePermissionCapabilities();
  const queryClient = useQueryClient();
  const runAbortableOperation = useAbortableOperation();
  const location = useLocation();
  const navigate = useNavigate();
  const reportType = "PaymentVoucher";
  const [selectedTemplatePath, setSelectedTemplatePath] = useState("");
  const [preview, setPreview] = useState<ApiPaymentReportHtmlPreviewResponse | null>(null);
  const [pdfDestinationPath, setPdfDestinationPath] = useState("");
  const [statusMessage, setStatusMessage] = useState<string | null>(null);
  const [lastCreatedJobId, setLastCreatedJobId] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [isPrinting, setIsPrinting] = useState(false);
  const desktopAvailable = isDesktopBridgeAvailable();
  const hasSavedPayment = paymentId > 0;
  const canUseSavedPaymentOutput = hasSavedPayment && !hasUnsavedDraftChanges;
  const hasPreviewSource = hasSavedPayment || Boolean(paymentDraft);

  const templatesQuery = useQuery({
    queryKey: queryKeys.reportTemplates(reportType),
    queryFn: ({ signal }) => client.listReportTemplates({ reportType }, { signal }),
    enabled: hasPreviewSource && reportOutputPermission.canView,
    staleTime: 5 * 60 * 1000,
  });

  const settingsQuery = useQuery({
    queryKey: queryKeys.settings(),
    queryFn: ({ signal }) => client.getSettings({ signal }),
    enabled: hasPreviewSource,
    staleTime: 5 * 60 * 1000,
  });
  const defaultExportDirectory = readDefaultExportDirectory(settingsQuery.data?.settings);

  const templateViews = useMemo(
    () => buildPaymentTemplateViews(templatesQuery.data ?? [], settingsQuery.data?.settings),
    [settingsQuery.data?.settings, templatesQuery.data],
  );
  const configuredTemplatePath = readDefaultReportTemplatePath(
    settingsQuery.data?.settings,
    "PaymentVoucher",
  );

  useEffect(() => {
    if (!templateViews.length || settingsQuery.isFetching) return;
    const next = resolveReportTemplatePath({
      templates: templateViews,
      currentPath: selectedTemplatePath,
      configuredPath: configuredTemplatePath,
      fallbackFileName: "payment_voucher_template.html",
    });
    if (next !== selectedTemplatePath) setSelectedTemplatePath(next);
  }, [configuredTemplatePath, selectedTemplatePath, settingsQuery.isFetching, templateViews]);

  useEffect(() => {
    setPreview(null);
    setStatusMessage(null);
    setLastCreatedJobId(null);
    setErrorMessage(null);
  }, [paymentDraft]);

  const previewMutation = useMutation({
    mutationFn: () => runAbortableOperation((signal) => {
      const body = {
        templatePath: selectedTemplatePath,
      };

      return paymentDraft
        ? client.previewPaymentVoucherDraftHtml({
            body: {
              ...body,
              payment: paymentDraft,
            },
          }, { signal })
        : client.previewPaymentVoucherHtml({
            paymentId,
            body,
          }, { signal });
    }),
    onSuccess: (response) => {
      setPreview(response);
      setStatusMessage(null);
      setLastCreatedJobId(null);
      setErrorMessage(null);
    },
    onError: (error) => {
      setLastCreatedJobId(null);
      setErrorMessage(readApiError(error));
    },
  });

  const pdfMutation = useMutation({
    mutationFn: (destinationPath?: string) => runAbortableOperation(async (signal) => {
      const job = desktopAvailable
        ? await client.startPaymentVoucherPdfSaveToPathJob({
            paymentId,
            body: {
              templatePath: selectedTemplatePath,
              destinationPath: (destinationPath ?? pdfDestinationPath).trim(),
            },
          }, { signal })
        : await client.startPaymentVoucherPdfDownloadJob({
            paymentId,
            body: { templatePath: selectedTemplatePath, destinationPath: "" },
          }, { signal });
      if (!desktopAvailable) {
        await downloadJobResultWhenReady(client, job, buildPaymentReportPdfDefaultFileName(), { signal });
      }
      return job;
    }),
    onSuccess: async (job) => {
      setStatusMessage(desktopAvailable ? `已创建付款/报销 PDF 任务：${job.jobId}` : "PDF 已交给浏览器下载。");
      setLastCreatedJobId(job.jobId);
      setErrorMessage(null);
      await queryClient.invalidateQueries({ queryKey: queryKeys.jobsRoot() });
    },
    onError: (error) => {
      setStatusMessage(null);
      setLastCreatedJobId(null);
      setErrorMessage(readApiError(error));
    },
  });

  const isBusy = templatesQuery.isFetching || settingsQuery.isFetching || previewMutation.isPending || pdfMutation.isPending || isPrinting;
  const canPreview = reportOutputPermission.canOperate && hasPreviewSource && (templateViews.length === 0 || Boolean(selectedTemplatePath));
  const canPrintPreview = Boolean(preview?.html) && !isBusy;
  const canGeneratePdf = reportOutputPermission.canOperate && canUseSavedPaymentOutput && canPreview && (!desktopAvailable || Boolean(pdfDestinationPath.trim())) && !isBusy;
  const canQuickGeneratePdf = reportOutputPermission.canOperate && canUseSavedPaymentOutput && canPreview && !isBusy;
  const canOpenTemplateDesigner = reportDesignPermission.canView && Boolean(selectedTemplatePath) && !isBusy;
  const templateMessage = templatesQuery.isError
    ? readApiError(templatesQuery.error)
    : settingsQuery.isError
      ? readApiError(settingsQuery.error)
      : null;

  function handleTemplateChange(value: string) {
    setSelectedTemplatePath(value);
    setPreview(null);
    setStatusMessage(null);
    setLastCreatedJobId(null);
    setErrorMessage(null);
  }

  function openTemplateDesigner() {
    if (!selectedTemplatePath) {
      return;
    }

    const params = new URLSearchParams({
      reportType,
    });
    if (paymentId > 0) {
      params.set("paymentId", String(paymentId));
    }

    const templateFileName = fileNameFromTemplatePath(selectedTemplatePath);
    if (templateFileName) {
      params.set("template", templateFileName);
    }

    navigate(`/reports/templates?${params.toString()}`);
  }

  async function pickPdfDestination() {
    try {
      const selected = await selectSavePdfPath(buildPaymentReportPdfDefaultFileName(), defaultExportDirectory);
      if (selected) {
        setPdfDestinationPath(selected);
        setStatusMessage(null);
        setLastCreatedJobId(null);
      }
    } catch (error) {
      setErrorMessage(readDesktopError(error));
    }
  }

  async function exportPdfWithSaveDialog() {
    if (!canQuickGeneratePdf) {
      return;
    }

    if (!desktopAvailable) {
      pdfMutation.mutate(undefined);
      return;
    }

    try {
      const selected = await selectSavePdfPath(buildPaymentReportPdfDefaultFileName(), defaultExportDirectory);
      if (selected) {
        setPdfDestinationPath(selected);
        pdfMutation.mutate(selected);
      }
    } catch (error) {
      setErrorMessage(readDesktopError(error));
    }
  }

  function buildPaymentReportPdfDefaultFileName() {
    const template = templateViews.find((item) => item.templatePath === selectedTemplatePath);
    const paymentReference = paymentDraft?.invoiceNo?.trim() || (paymentId > 0 ? `payment-${paymentId}` : "payment-draft");
    return buildReportPdfDefaultFileName({
      templatePath: selectedTemplatePath,
      displayName: template?.displayName,
      fallbackTitle: "Payment Voucher",
      documentNumber: paymentReference,
    });
  }

  async function printPreview() {
    if (!preview?.html) {
      setErrorMessage("请先生成预览后再打印。");
      setStatusMessage(null);
      return;
    }

    try {
      setIsPrinting(true);
      setErrorMessage(null);
      await printReportPreviewHtml(preview.html, "付款/报销单打印预览");
      setStatusMessage("已打开打印对话框。");
      setLastCreatedJobId(null);
    } catch (error) {
      setStatusMessage(null);
      setLastCreatedJobId(null);
      setErrorMessage(error instanceof Error ? error.message : "打印失败。");
    } finally {
      setIsPrinting(false);
    }
  }

  return (
    <section
      className="form-section report-preview-section"
      aria-label="付款/报销单预览"
      data-selected-template-path={selectedTemplatePath}
      data-preview-template-path={preview?.templatePath ?? ""}
    >
      <div className="section-header">
        <h2>付款/报销单预览</h2>
        <div className="toolbar-actions">
          <button
            className="icon-button"
            type="button"
            title="刷新模板" aria-label="刷新模板"
            disabled={isBusy}
            onClick={() => void templatesQuery.refetch()}
          >
            <RefreshCw size={17} aria-hidden="true" />
          </button>
          {reportDesignPermission.canView ? (
            <>
              {canManageSettings ? (
                <button
                  className="command-button secondary"
                  type="button"
                  title="管理导出默认设置"
                  disabled={isBusy}
                  onClick={() => navigate("/settings?section=paymentReports", {
                    state: createSettingsReturnState(location, "返回付款/报销单"),
                  })}
                >
                  <Settings size={17} aria-hidden="true" />
                  <span>报表设置</span>
                </button>
              ) : null}
              <button
                className="command-button secondary"
                type="button"
                title="设计当前模板"
                disabled={!canOpenTemplateDesigner}
                onClick={openTemplateDesigner}
              >
                <LayoutTemplate size={17} aria-hidden="true" />
                <span>设计模板</span>
              </button>
            </>
          ) : null}
          <button
            className="command-button secondary"
            type="button"
            disabled={isBusy || !canPreview}
            onClick={() => previewMutation.mutate()}
          >
            <Eye size={17} aria-hidden="true" />
            <span>预览</span>
          </button>
          <button
            className="command-button secondary"
            type="button"
            title="打印当前预览"
            disabled={!canPrintPreview}
            onClick={() => void printPreview()}
          >
            <Printer size={17} aria-hidden="true" />
            <span>打印</span>
          </button>
          <button
            className="command-button secondary"
            type="button"
            title={canUseSavedPaymentOutput ? (desktopAvailable ? "选择保存位置并生成 PDF" : "下载付款/报销 PDF") : "请先保存付款/报销单"}
            disabled={!hasSavedPayment || !canQuickGeneratePdf}
            onClick={() => void exportPdfWithSaveDialog()}
          >
            <FileDown size={17} aria-hidden="true" />
            <span>导出 PDF</span>
          </button>
        </div>
      </div>

      {templateMessage ? <InlineNotice tone="warning" title="报表模板提示">{templateMessage}</InlineNotice> : null}
      {!reportOutputPermission.canOperate ? (
        <PermissionNotice>当前模板未授予付款报销单据预览和输出操作权限。</PermissionNotice>
      ) : null}
      {errorMessage ? <InlineNotice tone="error" title="付款报表生成失败">{errorMessage}</InlineNotice> : null}
      {statusMessage ? (
        <InlineNotice tone="success" action={<ViewJobButton jobId={lastCreatedJobId} disabled={isBusy} />}>
          {statusMessage}
        </InlineNotice>
      ) : null}
      {hasSavedPayment && hasUnsavedDraftChanges ? (
        <InlineNotice tone="info">当前付款/报销单有未保存修改。HTML 预览使用当前草稿；PDF 请先保存后再生成。</InlineNotice>
      ) : null}
      <div className="report-preview-controls">
        <SelectField
          label="模板"
          value={selectedTemplatePath}
          disabled={isBusy || !reportOutputPermission.canOperate || templateViews.length === 0}
          options={templateViews.map((template) => ({
            value: template.templatePath,
            label: template.displayName,
          }))}
          onChange={handleTemplateChange}
        />
      </div>

      {hasSavedPayment ? (
        <div className="report-pdf-controls">
          {desktopAvailable ? <PathField
            label="输出 PDF"
            value={pdfDestinationPath}
            disabled={isBusy || !reportOutputPermission.canOperate}
            onChange={(value) => {
              setPdfDestinationPath(value);
              setStatusMessage(null);
            }}
            actions={
              <>
                {desktopAvailable ? (
                  <DesktopIconButton title="选择保存位置" disabled={isBusy} onClick={pickPdfDestination}>
                    <Save size={15} aria-hidden="true" />
                  </DesktopIconButton>
                ) : null}
                {renderOpenPathAction(pdfDestinationPath, "打开输出位置", setErrorMessage)}
              </>
            }
          /> : <div className="field-help">PDF 将保存到浏览器默认下载目录。</div>}
          <button
            className="command-button secondary"
            type="button"
            disabled={!canGeneratePdf}
            onClick={() => pdfMutation.mutate(undefined)}
          >
            <FileDown size={17} aria-hidden="true" />
            <span>{desktopAvailable ? "生成 PDF" : "下载 PDF"}</span>
          </button>
        </div>
      ) : null}

      <div className="report-preview-frame-wrap">
        {preview ? (
          <iframe
            className="report-preview-frame"
            title="付款/报销单 HTML 预览"
            sandbox=""
            srcDoc={preview.html}
            data-template-path={preview.templatePath}
          />
        ) : (
          <div className="report-preview-empty">{isBusy ? "加载中" : "暂无预览"}</div>
        )}
      </div>
    </section>
  );
}

type PaymentTemplateSetting = {
  name: string;
  templatePath: string;
  isEnabled: boolean;
  reportType: string;
};

type PaymentTemplateView = {
  templatePath: string;
  displayName: string;
};

function buildPaymentTemplateViews(
  templates: ApiReportTemplateDto[],
  settings: SettingsLike | undefined,
): PaymentTemplateView[] {
  const configuredItems = readPaymentTemplateItems(settings).filter((item) => item.templatePath.length > 0);
  const usedTemplatePaths = new Set<string>();
  const views: PaymentTemplateView[] = [];

  for (const item of configuredItems) {
    const template = findTemplateForPaymentItem(item, templates, usedTemplatePaths);
    if (!template) {
      continue;
    }

    const normalizedPath = normalizeTemplatePath(template.templatePath);
    if (usedTemplatePaths.has(normalizedPath)) {
      continue;
    }

    usedTemplatePaths.add(normalizedPath);
    if (!item.isEnabled) {
      continue;
    }

    views.push({
      templatePath: template.templatePath,
      displayName: item.name || template.displayName || fileNameFromTemplatePath(template.templatePath),
    });
  }

  for (const template of templates) {
    const normalizedPath = normalizeTemplatePath(template.templatePath);
    if (usedTemplatePaths.has(normalizedPath)) {
      continue;
    }

    views.push({
      templatePath: template.templatePath,
      displayName: template.displayName || fileNameFromTemplatePath(template.templatePath),
    });
  }

  return views;
}

function readPaymentTemplateItems(settings?: SettingsLike): PaymentTemplateSetting[] {
  const rawItems = settings ? readRecordValue(settings, "paymentTemplates", "PaymentTemplates") : undefined;
  if (!Array.isArray(rawItems)) {
    return [];
  }

  const items: PaymentTemplateSetting[] = [];
  for (const rawItem of rawItems) {
    if (!isRecord(rawItem)) {
      continue;
    }

    const reportType = readString(rawItem, "reportType", "ReportType") || "PaymentVoucher";
    if (!isPaymentTemplateReportType(reportType)) {
      continue;
    }

    items.push({
      name: readString(rawItem, "name", "Name"),
      templatePath: readString(rawItem, "templatePath", "TemplatePath"),
      isEnabled: readBoolean(rawItem, true, "isEnabled", "IsEnabled"),
      reportType,
    });
  }

  return items;
}

function findTemplateForPaymentItem(
  item: PaymentTemplateSetting,
  templates: ApiReportTemplateDto[],
  usedTemplatePaths: Set<string>,
) {
  const pathMatch = templates.find(
    (template) =>
      !usedTemplatePaths.has(normalizeTemplatePath(template.templatePath)) &&
      templatePathsMatch(item.templatePath, template.templatePath),
  );
  if (pathMatch) {
    return pathMatch;
  }

  const itemFileName = fileNameFromTemplatePath(item.templatePath);
  if (!itemFileName) {
    return undefined;
  }

  const fileNameMatches = templates.filter(
    (template) =>
      !usedTemplatePaths.has(normalizeTemplatePath(template.templatePath)) &&
      fileNameFromTemplatePath(template.templatePath) === itemFileName,
  );

  return fileNameMatches.length === 1 ? fileNameMatches[0] : undefined;
}

function isPaymentTemplateReportType(reportType: string) {
  return reportType.trim().toLowerCase() === "paymentvoucher";
}


function readRecordValue(record: SettingsLike, ...names: string[]) {
  const source = record as unknown as Record<string, unknown>;
  for (const name of names) {
    if (Object.prototype.hasOwnProperty.call(source, name)) {
      return source[name];
    }
  }

  return undefined;
}

function readString(record: Record<string, unknown>, ...names: string[]) {
  const value = readRecordValue(record, ...names);
  return typeof value === "string" ? value.trim() : "";
}

function readBoolean(record: Record<string, unknown>, fallback: boolean, ...names: string[]) {
  const value = readRecordValue(record, ...names);
  return typeof value === "boolean" ? value : fallback;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}
