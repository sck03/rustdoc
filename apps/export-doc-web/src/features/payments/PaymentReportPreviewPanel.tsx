import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Eye, FileDown, LayoutTemplate, Printer, RefreshCw, Save, Settings } from "lucide-react";
import { useNavigate } from "react-router-dom";
import { ApiPaymentDto, ApiPaymentReportHtmlPreviewResponse, ApiReportTemplateDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { useModulePermission, usePermissionCapabilities } from "../../app/PermissionAccessContext.tsx";
import { queryKeys } from "../../api/queryKeys.ts";
import { isDesktopBridgeAvailable, selectSavePdfPath } from "../../desktop/desktopBridge.ts";
import { DesktopIconButton, readDesktopError, renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { SelectField } from "../../ui/FormFields.tsx";
import { PathField } from "../../ui/PathField.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { downloadJobResultWhenReady } from "../../ui/downloadJobResult.ts";
import { PermissionNotice } from "../../ui/PageState.tsx";
import { ViewJobButton } from "../jobs/ViewJobButton.tsx";
import { buildReportPdfDefaultFileName } from "../reports/reportFileNames.ts";
import { printReportPreviewHtml } from "../reports/printReportPreview.ts";
import { readDefaultExportDirectory } from "../settings/settingsPaths.ts";

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
  const navigate = useNavigate();
  const reportType = "PaymentVoucher";
  const [selectedTemplatePath, setSelectedTemplatePath] = useState("");
  const [withSeal, setWithSeal] = useState(true);
  const [preview, setPreview] = useState<ApiPaymentReportHtmlPreviewResponse | null>(null);
  const [pdfDestinationPath, setPdfDestinationPath] = useState("");
  const [statusMessage, setStatusMessage] = useState<string | null>(null);
  const [lastCreatedJobId, setLastCreatedJobId] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [isPrinting, setIsPrinting] = useState(false);
  const desktopAvailable = isDesktopBridgeAvailable();
  const paymentDraftPreviewKey = paymentDraft ? JSON.stringify(paymentDraft) : "";
  const hasSavedPayment = paymentId > 0;
  const canUseSavedPaymentOutput = hasSavedPayment && !hasUnsavedDraftChanges;
  const hasPreviewSource = hasSavedPayment || Boolean(paymentDraft);

  const templatesQuery = useQuery({
    queryKey: queryKeys.reportTemplates(reportType),
    queryFn: () => client.listReportTemplates({ reportType }),
    enabled: hasPreviewSource && reportOutputPermission.canView,
    staleTime: 5 * 60 * 1000,
  });

  const settingsQuery = useQuery({
    queryKey: queryKeys.settings(),
    queryFn: () => client.getSettings(),
    enabled: hasPreviewSource,
    staleTime: 5 * 60 * 1000,
  });
  const defaultExportDirectory = readDefaultExportDirectory(settingsQuery.data?.settings);

  const templateViews = useMemo(
    () => buildPaymentTemplateViews(templatesQuery.data ?? [], settingsQuery.data?.settings),
    [settingsQuery.data?.settings, templatesQuery.data],
  );

  useEffect(() => {
    if (!templateViews.length) {
      return;
    }

    const preferredTemplate =
      templateViews.find((template) => fileNameFromPath(template.templatePath).toLowerCase() === "payment_voucher_template.html") ??
      templateViews[0];
    setSelectedTemplatePath((current) => {
      if (current && templateViews.some((template) => template.templatePath === current)) {
        return current;
      }

      return preferredTemplate.templatePath;
    });
  }, [templateViews]);

  useEffect(() => {
    if (!selectedTemplatePath) {
      return;
    }

    const template = templateViews.find((item) => item.templatePath === selectedTemplatePath);
    if (template) {
      setWithSeal(template.withSealDefault);
    }
  }, [selectedTemplatePath, templateViews]);

  useEffect(() => {
    setPreview(null);
    setStatusMessage(null);
    setLastCreatedJobId(null);
    setErrorMessage(null);
  }, [paymentDraftPreviewKey]);

  const previewMutation = useMutation({
    mutationFn: () => {
      const body = {
        reportType,
        templatePath: selectedTemplatePath,
        withSeal,
      };

      return paymentDraft
        ? client.previewPaymentVoucherDraftHtml({
            body: {
              ...body,
              payment: paymentDraft,
            },
          })
        : client.previewPaymentVoucherHtml({
            paymentId,
            body,
          });
    },
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
    mutationFn: async () => {
      const job = desktopAvailable
        ? await client.startPaymentVoucherPdfSaveToPathJob({
        paymentId,
        body: {
          reportType,
          templatePath: selectedTemplatePath,
          withSeal,
          destinationPath: pdfDestinationPath.trim(),
        },
      })
        : await client.startPaymentVoucherPdfDownloadJob({
            paymentId,
            body: { reportType, templatePath: selectedTemplatePath, withSeal, destinationPath: "" },
          });
      if (!desktopAvailable) {
        await downloadJobResultWhenReady(client, job, buildPaymentReportPdfDefaultFileName());
      }
      return job;
    },
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
  const canOpenTemplateDesigner = reportDesignPermission.canView && Boolean(selectedTemplatePath) && !isBusy;
  const previewStoragePolicy = preview?.storagePolicy || "";
  const templateMessage = templatesQuery.isError
    ? readApiError(templatesQuery.error)
    : settingsQuery.isError
      ? readApiError(settingsQuery.error)
      : null;

  function handleTemplateChange(value: string) {
    setSelectedTemplatePath(value);
    const template = templateViews.find((item) => item.templatePath === value);
    if (template) {
      setWithSeal(template.withSealDefault);
    }
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

    const templateFileName = fileNameFromPath(selectedTemplatePath);
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
                  title="管理付款/报销模板"
                  disabled={isBusy}
                  onClick={() => navigate("/settings?section=paymentTemplates")}
                >
                  <Settings size={17} aria-hidden="true" />
                  <span>模板设置</span>
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
        </div>
      </div>

      {templateMessage ? <div className="alert">{templateMessage}</div> : null}
      {!reportOutputPermission.canOperate ? (
        <PermissionNotice>当前模板未授予付款报销单据预览和输出操作权限。</PermissionNotice>
      ) : null}
      {errorMessage ? <div className="alert">{errorMessage}</div> : null}
      {statusMessage ? (
        <div className="success-alert status-action-alert">
          <span>{statusMessage}</span>
          <ViewJobButton jobId={lastCreatedJobId} disabled={isBusy} />
        </div>
      ) : null}
      {hasSavedPayment && hasUnsavedDraftChanges ? (
        <div className="info-alert">当前付款/报销单有未保存修改。HTML 预览使用当前草稿；PDF 请先保存后再生成。</div>
      ) : null}
      {previewStoragePolicy ? <div className="info-alert">{previewStoragePolicy}</div> : null}

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
        <label className="toggle-field">
          <input
            type="checkbox"
            checked={withSeal}
            disabled={isBusy || !reportOutputPermission.canOperate}
            onChange={(event) => {
              setWithSeal(event.target.checked);
              setPreview(null);
              setStatusMessage(null);
            }}
          />
          <span>带章</span>
        </label>
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
            onClick={() => pdfMutation.mutate()}
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

function fileNameFromPath(path: string) {
  return path.split(/[\\/]/).filter(Boolean).pop() || path;
}

type PaymentTemplateSetting = {
  name: string;
  templatePath: string;
  isEnabled: boolean;
  showSeal: boolean;
  reportType: string;
};

type PaymentTemplateView = {
  templatePath: string;
  displayName: string;
  withSealDefault: boolean;
};

function buildPaymentTemplateViews(
  templates: ApiReportTemplateDto[],
  settings: Record<string, unknown> | undefined,
): PaymentTemplateView[] {
  const configuredItems = readPaymentTemplateItems(settings).filter((item) => item.templatePath.length > 0);
  const usedTemplatePaths = new Set<string>();
  const views: PaymentTemplateView[] = [];

  for (const item of configuredItems) {
    const template = findTemplateForPaymentItem(item, templates, usedTemplatePaths);
    const templatePath = template?.templatePath ?? item.templatePath;
    if (!templatePath) {
      continue;
    }

    const normalizedPath = normalizePathForMatch(templatePath);
    if (usedTemplatePaths.has(normalizedPath)) {
      continue;
    }

    usedTemplatePaths.add(normalizedPath);
    if (!item.isEnabled) {
      continue;
    }

    views.push({
      templatePath,
      displayName: item.name || template?.displayName || fileNameFromPath(templatePath),
      withSealDefault: item.showSeal,
    });
  }

  for (const template of templates) {
    const normalizedPath = normalizePathForMatch(template.templatePath);
    if (usedTemplatePaths.has(normalizedPath)) {
      continue;
    }

    views.push({
      templatePath: template.templatePath,
      displayName: template.displayName || fileNameFromPath(template.templatePath),
      withSealDefault: template.withSealDefault,
    });
  }

  return views;
}

function readPaymentTemplateItems(settings?: Record<string, unknown>): PaymentTemplateSetting[] {
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
      showSeal: readBoolean(rawItem, true, "showSeal", "ShowSeal"),
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
      !usedTemplatePaths.has(normalizePathForMatch(template.templatePath)) &&
      pathsReferToSameTemplate(item.templatePath, template.templatePath),
  );
  if (pathMatch) {
    return pathMatch;
  }

  const itemFileName = fileNameFromPath(item.templatePath).toLowerCase();
  if (!itemFileName) {
    return undefined;
  }

  const fileNameMatches = templates.filter(
    (template) =>
      !usedTemplatePaths.has(normalizePathForMatch(template.templatePath)) &&
      fileNameFromPath(template.templatePath).toLowerCase() === itemFileName,
  );

  return fileNameMatches.length === 1 ? fileNameMatches[0] : undefined;
}

function isPaymentTemplateReportType(reportType: string) {
  const normalized = reportType.trim().toLowerCase();
  return (
    normalized.length === 0 ||
    normalized === "paymentvoucher" ||
    normalized === "paymentdocument" ||
    normalized === "internal"
  );
}

function pathsReferToSameTemplate(left: string, right: string) {
  const leftPath = normalizePathForMatch(left);
  const rightPath = normalizePathForMatch(right);
  if (!leftPath || !rightPath) {
    return false;
  }

  return leftPath === rightPath || leftPath.endsWith(`/${rightPath}`) || rightPath.endsWith(`/${leftPath}`);
}

function normalizePathForMatch(path: string) {
  return path
    .trim()
    .replace(/^file:[/\\]*/i, "")
    .replace(/\\/g, "/")
    .replace(/\/+/g, "/")
    .replace(/^\/+/, "")
    .toLowerCase();
}

function readRecordValue(record: Record<string, unknown>, ...names: string[]) {
  for (const name of names) {
    if (Object.prototype.hasOwnProperty.call(record, name)) {
      return record[name];
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
