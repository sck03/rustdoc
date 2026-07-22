import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowDown, ArrowUp, Eye, FileArchive, FileDown, FileSpreadsheet, FolderOpen, LayoutTemplate, Mail, Plus, Printer, RefreshCw, Save, Settings, SlidersHorizontal, Trash2 } from "lucide-react";
import { useNavigate } from "react-router-dom";
import { ApiInvoiceDetailDto, ApiInvoiceDocumentPackagePreviewResponse, ApiReportHtmlPreviewResponse, ApiSettingsResponse, ApiReportTemplateDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { useModulePermission, usePermissionCapabilities } from "../../app/PermissionAccessContext.tsx";
import { queryKeys } from "../../api/queryKeys.ts";
import { isDesktopBridgeAvailable, selectDirectory, selectReportTemplateFile, selectSaveExcelPath, selectSavePdfPath, selectSaveZipPath } from "../../desktop/desktopBridge.ts";
import { DesktopIconButton, readDesktopError, renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { PathField } from "../../ui/PathField.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { downloadJobResultWhenReady } from "../../ui/downloadJobResult.ts";
import { PermissionNotice } from "../../ui/PageState.tsx";
import { buildReportPdfDefaultFileName } from "../reports/reportFileNames.ts";
import { printReportPreviewHtml } from "../reports/printReportPreview.ts";
import { readDefaultExportDirectory } from "../settings/settingsPaths.ts";
import { fileNameFromPath, buildDocumentPackagePrintHtml, extractHtmlHead, extractHtmlBody, createEmptyBatchExportConfigDraft, buildBatchExportConfigDraft, buildSettingsWithBatchExportConfig, toBatchExportItemRecord, normalizeBatchExportPattern, readBatchExportPattern, readBatchExportBoolean, cloneSettings, buildDocumentEmailSubject, buildDocumentEmailBody, readEmailTemplate, applyDocumentEmailTemplate, buildPackageTemplateViewsFromItems, createConfiguredTemplate, buildTemplateSelectOptions, findFirstUnusedTemplate, readTemplateDisplayName, readBatchExportItems, findTemplateForBatchExportItem, isExportBatchReportType, pathsReferToSameTemplate, normalizePathForMatch, buildDocumentPackageDefaultFileName, buildInvoiceBookingSheetDefaultFileName, applyBatchExportPattern, replaceBatchExportPlaceholder, formatDateForBatchExport, sanitizeFileNamePart, readBatchExportRecord, readRecordValue, readString, readBoolean, isRecord } from "./invoiceReportPreviewModel.ts";
import type { BatchExportConfigDraft, BatchExportItemSetting } from "./invoiceReportPreviewModel.ts";
import { InvoiceDocumentPackageConfig } from "./InvoiceDocumentPackageConfig.tsx";
import { InvoiceReportPreviewCanvas } from "./InvoiceReportPreviewCanvas.tsx";
import { InvoiceDocumentEmailPanel } from "./InvoiceDocumentEmailPanel.tsx";
import { InvoiceReportTemplateControls } from "./InvoiceReportTemplateControls.tsx";
import { InvoiceReportPreviewHeader } from "./InvoiceReportPreviewHeader.tsx";
type PackageTemplateState = {
  selected: boolean;
  withSeal: boolean;
};

export function InvoiceReportPreviewPanel({
  client,
  invoiceId,
  invoiceDraft,
  invoiceNo,
  customerName,
  defaultToAddress,
  hasUnsavedDraftChanges = false,
}: {
  client: ExportDocManagerApiClient;
  invoiceId: number;
  invoiceDraft?: ApiInvoiceDetailDto;
  invoiceNo?: string;
  customerName?: string;
  defaultToAddress?: string;
  hasUnsavedDraftChanges?: boolean;
}) {
  const reportOutputPermission = useModulePermission("document.invoice-reports");
  const reportDesignPermission = useModulePermission("document.reports");
  const excelPermission = useModulePermission("document.excel");
  const { canManageSettings } = usePermissionCapabilities();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const reportType = "ExportDocument";
  const [selectedTemplatePath, setSelectedTemplatePath] = useState("");
  const [withSeal, setWithSeal] = useState(true);
  const [preview, setPreview] = useState<ApiReportHtmlPreviewResponse | null>(null);
  const [packagePreview, setPackagePreview] = useState<ApiInvoiceDocumentPackagePreviewResponse | null>(null);
  const [pdfDestinationPath, setPdfDestinationPath] = useState("");
  const [packageDestinationPath, setPackageDestinationPath] = useState("");
  const [packageCreateZip, setPackageCreateZip] = useState(true);
  const [packageCreateZipTouched, setPackageCreateZipTouched] = useState(false);
  const [packageIncludeMergedPdf, setPackageIncludeMergedPdf] = useState(true);
  const [packageMergePdfTouched, setPackageMergePdfTouched] = useState(false);
  const [packageTemplateState, setPackageTemplateState] = useState<Record<string, PackageTemplateState>>({});
  const [packageConfigDraft, setPackageConfigDraft] = useState<BatchExportConfigDraft>(() => createEmptyBatchExportConfigDraft());
  const [packageConfigDirty, setPackageConfigDirty] = useState(false);
  const [bookingSheetDestinationPath, setBookingSheetDestinationPath] = useState("");
  const [emailToAddress, setEmailToAddress] = useState(defaultToAddress ?? "");
  const [emailSubject, setEmailSubject] = useState("");
  const [emailSubjectTouched, setEmailSubjectTouched] = useState(false);
  const [emailBody, setEmailBody] = useState("");
  const [emailBodyTouched, setEmailBodyTouched] = useState(false);
  const [emailIncludeMergedPdf, setEmailIncludeMergedPdf] = useState(false);
  const [statusMessage, setStatusMessage] = useState<string | null>(null);
  const [lastCreatedJobId, setLastCreatedJobId] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [isPrinting, setIsPrinting] = useState(false);
  const [showExportAdvanced, setShowExportAdvanced] = useState(false);
  const desktopAvailable = isDesktopBridgeAvailable();
  const invoiceDraftPreviewKey = invoiceDraft ? JSON.stringify(invoiceDraft) : "";
  const hasSavedInvoice = invoiceId > 0;
  const canUseSavedInvoiceOutput = hasSavedInvoice && !hasUnsavedDraftChanges;
  const hasPreviewSource = hasSavedInvoice || Boolean(invoiceDraft);

  const templatesQuery = useQuery({
    queryKey: queryKeys.reportTemplates(reportType),
    queryFn: () => client.listReportTemplates({ reportType }),
    enabled: hasPreviewSource && reportOutputPermission.canView,
    staleTime: 5 * 60 * 1000,
  });

  const settingsQuery = useQuery({
    queryKey: queryKeys.settings(),
    queryFn: () => client.getSettings(),
    enabled: hasPreviewSource && (reportOutputPermission.canOperate || canManageSettings),
    staleTime: 5 * 60 * 1000,
  });
  const defaultExportDirectory = readDefaultExportDirectory(settingsQuery.data?.settings);

  useEffect(() => {
    if (!templatesQuery.data?.length) {
      return;
    }

    const preferredTemplate =
      templatesQuery.data.find((template) => fileNameFromPath(template.templatePath).toLowerCase() === "invoice_template.html") ??
      templatesQuery.data[0];
    setSelectedTemplatePath((current) => current || preferredTemplate.templatePath);
    setWithSeal((current) => (selectedTemplatePath ? current : preferredTemplate.withSealDefault));
  }, [selectedTemplatePath, templatesQuery.data]);

  const templates = templatesQuery.data ?? [];
  const packageTemplates = useMemo(
    () => buildPackageTemplateViewsFromItems(templates, packageConfigDraft.items),
    [packageConfigDraft.items, templates],
  );
  const packageConfigTemplateOptions = useMemo(
    () => buildTemplateSelectOptions(templates, packageConfigDraft.items),
    [packageConfigDraft.items, templates],
  );
  const documentEmailDate = useMemo(() => formatDateForBatchExport(new Date()), [invoiceId]);
  const defaultEmailSubject = useMemo(
    () => buildDocumentEmailSubject(settingsQuery.data?.settings, invoiceNo, customerName, documentEmailDate),
    [customerName, documentEmailDate, invoiceNo, settingsQuery.data?.settings],
  );
  const defaultEmailBody = useMemo(
    () => buildDocumentEmailBody(settingsQuery.data?.settings, invoiceNo, customerName, documentEmailDate),
    [customerName, documentEmailDate, invoiceNo, settingsQuery.data?.settings],
  );

  useEffect(() => {
    if (packageConfigDirty) {
      return;
    }

    setPackageConfigDraft(buildBatchExportConfigDraft(settingsQuery.data?.settings, templates));
  }, [packageConfigDirty, settingsQuery.data?.settings, templates]);

  useEffect(() => {
    if (!packageTemplates.length) {
      return;
    }

    setPackageTemplateState((current) => {
      const next: Record<string, PackageTemplateState> = {};
      for (const entry of packageTemplates) {
        const template = entry.template;
        const existing = current[template.templatePath];
        next[template.templatePath] = {
          selected: packageConfigDirty ? existing?.selected ?? entry.initiallySelected : entry.initiallySelected,
          withSeal: packageConfigDirty ? existing?.withSeal ?? entry.withSealDefault : entry.withSealDefault,
        };
      }

      return next;
    });
  }, [packageConfigDirty, packageTemplates]);

  useEffect(() => {
    const nextAddress = defaultToAddress?.trim() ?? "";
    if (!nextAddress) {
      return;
    }

    setEmailToAddress((current) => current || nextAddress);
  }, [defaultToAddress]);

  useEffect(() => {
    if (emailSubjectTouched) {
      return;
    }

    setEmailSubject(defaultEmailSubject);
  }, [defaultEmailSubject, emailSubjectTouched]);

  useEffect(() => {
    if (emailBodyTouched) {
      return;
    }

    setEmailBody(defaultEmailBody);
  }, [defaultEmailBody, emailBodyTouched]);

  useEffect(() => {
    if (packageMergePdfTouched) {
      return;
    }

    setPackageIncludeMergedPdf(packageConfigDraft.mergePdf);
  }, [packageConfigDraft.mergePdf, packageMergePdfTouched]);

  useEffect(() => {
    if (packageCreateZipTouched) {
      return;
    }

    setPackageCreateZip(packageConfigDraft.zipAfterExport);
  }, [packageConfigDraft.zipAfterExport, packageCreateZipTouched]);

  useEffect(() => {
    setPreview(null);
    setPackagePreview(null);
    setStatusMessage(null);
    setLastCreatedJobId(null);
    setErrorMessage(null);
  }, [invoiceDraftPreviewKey]);

  const selectedPackageTemplates = packageTemplates
    .filter((entry) => packageTemplateState[entry.template.templatePath]?.selected)
    .map((entry) => ({
      displayName: entry.displayName,
      templatePath: entry.template.templatePath,
      withSealDefault: entry.withSealDefault,
    }));
  const selectedPackageItems = selectedPackageTemplates.map((template) => ({
    name: template.displayName || fileNameFromPath(template.templatePath),
    reportType,
    templatePath: template.templatePath,
    withSeal: packageTemplateState[template.templatePath]?.withSeal ?? template.withSealDefault,
  }));

  const previewMutation = useMutation({
    mutationFn: () => {
      const body = {
        reportType,
        templatePath: selectedTemplatePath,
        withSeal,
      };

      return invoiceDraft
        ? client.previewInvoiceReportDraftHtml({
            body: {
              ...body,
              invoice: invoiceDraft,
            },
          })
        : client.previewInvoiceReportHtml({
            invoiceId,
            body,
          });
    },
    onSuccess: (response) => {
      setPreview(response);
      setPackagePreview(null);
      setStatusMessage(null);
      setLastCreatedJobId(null);
      setErrorMessage(null);
    },
    onError: (error) => {
      setLastCreatedJobId(null);
      setErrorMessage(readApiError(error));
    },
  });

  const packagePreviewMutation = useMutation({
    mutationFn: () =>
      client.previewInvoiceDocumentPackageHtml({
        invoiceId,
        body: {
          items: selectedPackageItems,
        },
      }),
    onSuccess: (response) => {
      setPackagePreview(response);
      setPreview(null);
      setStatusMessage(null);
      setLastCreatedJobId(null);
      setErrorMessage(null);
    },
    onError: (error) => {
      setStatusMessage(null);
      setLastCreatedJobId(null);
      setErrorMessage(readApiError(error));
    },
  });

  const pdfMutation = useMutation({
    mutationFn: async (destinationPath?: string) => {
      const job = desktopAvailable
        ? await client.startInvoiceReportPdfSaveToPathJob({
        invoiceId,
        body: {
          reportType,
          templatePath: selectedTemplatePath,
          withSeal,
          destinationPath: (destinationPath ?? pdfDestinationPath).trim(),
        },
      })
        : await client.startInvoiceReportPdfDownloadJob({
            invoiceId,
            body: { reportType, templatePath: selectedTemplatePath, withSeal, destinationPath: "" },
          });
      if (!desktopAvailable) {
        await downloadJobResultWhenReady(client, job, buildInvoiceReportPdfDefaultFileName());
      }
      return job;
    },
    onSuccess: async (job) => {
      setStatusMessage(desktopAvailable ? `已创建报表 PDF 任务：${job.jobId}` : "PDF 已交给浏览器下载。");
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

  const bookingSheetMutation = useMutation({
    mutationFn: async (destinationPath?: string) => {
      const job = desktopAvailable
        ? await client.startInvoiceBookingSheetSaveToPathJob({
        body: {
          invoiceId,
          destinationPath: (destinationPath ?? bookingSheetDestinationPath).trim(),
        },
      })
        : await client.startInvoiceBookingSheetDownloadJob({ invoiceId });
      if (!desktopAvailable) {
        await downloadJobResultWhenReady(client, job, bookingSheetDefaultFileName);
      }
      return job;
    },
    onSuccess: async (job) => {
      setStatusMessage(`已创建发票托单任务：${job.jobId}`);
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

  const packageMutation = useMutation({
    mutationFn: async () => {
      const job = desktopAvailable
        ? await client.startInvoiceDocumentPackageSaveToPathJob({
        invoiceId,
        body: {
          items: selectedPackageItems,
          includeMergedPdf: packageIncludeMergedPdf,
          createZip: packageCreateZip,
          destinationPath: packageDestinationPath.trim(),
        },
      })
        : await client.startInvoiceDocumentPackageDownloadJob({
            invoiceId,
            body: {
              items: selectedPackageItems,
              includeMergedPdf: packageIncludeMergedPdf,
              createZip: true,
              destinationPath: "",
            },
          });
      if (!desktopAvailable) {
        const downloadName = documentPackageDefaultFileName.toLowerCase().endsWith(".zip")
          ? documentPackageDefaultFileName
          : `${documentPackageDefaultFileName}.zip`;
        await downloadJobResultWhenReady(client, job, downloadName);
      }
      return job;
    },
    onSuccess: async (job) => {
      setStatusMessage(desktopAvailable
        ? `已创建${packageCreateZip ? "单据包 ZIP" : "单据文件夹导出"}任务：${job.jobId}`
        : "单据包 ZIP 已交给浏览器下载。");
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

  const emailMutation = useMutation({
    mutationFn: () =>
      client.startInvoiceDocumentEmailJob({
        invoiceId,
        body: {
          items: selectedPackageItems,
          includeMergedPdf: emailIncludeMergedPdf,
          toAddress: emailToAddress.trim(),
          subject: emailSubject.trim(),
          body: emailBody,
        },
      }),
    onSuccess: async (job) => {
      setStatusMessage(`已创建单据邮件任务：${job.jobId}`);
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

  const savePackageConfigMutation = useMutation({
    mutationFn: (draft: BatchExportConfigDraft) =>
      client.updateSettings({
        body: {
          settings: buildSettingsWithBatchExportConfig(settingsQuery.data?.settings ?? {}, draft),
          updateSecrets: false,
        },
      }),
    onSuccess: async (response) => {
      const nextDraft = buildBatchExportConfigDraft(response.settings, templates);
      setPackageConfigDraft(nextDraft);
      setPackageConfigDirty(false);
      setPackageMergePdfTouched(false);
      setPackageCreateZipTouched(false);
      setPackageIncludeMergedPdf(nextDraft.mergePdf);
      setPackageCreateZip(nextDraft.zipAfterExport);
      setStatusMessage(response.message || "单据包配置已保存。");
      setLastCreatedJobId(null);
      setErrorMessage(null);
      queryClient.setQueryData<ApiSettingsResponse>(queryKeys.settings(), {
        secrets: response.secrets,
        settings: response.settings,
        storagePolicy: settingsQuery.data?.storagePolicy ?? "",
      });
      await queryClient.invalidateQueries({ queryKey: queryKeys.settings() });
    },
    onError: (error) => {
      setStatusMessage(null);
      setLastCreatedJobId(null);
      setErrorMessage(readApiError(error));
    },
  });

  const isBusy =
    templatesQuery.isFetching ||
    settingsQuery.isFetching ||
    previewMutation.isPending ||
    packagePreviewMutation.isPending ||
    pdfMutation.isPending ||
    bookingSheetMutation.isPending ||
    packageMutation.isPending ||
    emailMutation.isPending ||
    savePackageConfigMutation.isPending ||
    isPrinting;
  const canPreview = reportOutputPermission.canOperate && hasPreviewSource && (templates.length === 0 || Boolean(selectedTemplatePath));
  const canPreviewPackage = reportOutputPermission.canOperate && canUseSavedInvoiceOutput && selectedPackageTemplates.length > 0 && selectedPackageTemplates.length <= 20 && !isBusy;
  const canPrintPreview = (Boolean(preview?.html) || Boolean(packagePreview?.items.some((item) => item.html))) && !isBusy;
  const canGeneratePdf = reportOutputPermission.canOperate && canUseSavedInvoiceOutput && canPreview && (!desktopAvailable || Boolean(pdfDestinationPath.trim())) && !isBusy;
  const canQuickGeneratePdf = reportOutputPermission.canOperate && canUseSavedInvoiceOutput && canPreview && !isBusy;
  const canGenerateBookingSheet = excelPermission.canOperate && canUseSavedInvoiceOutput && (!desktopAvailable || Boolean(bookingSheetDestinationPath.trim())) && !isBusy;
  const canQuickGenerateBookingSheet = excelPermission.canOperate && canUseSavedInvoiceOutput && !isBusy;
  const canGeneratePackage =
    reportOutputPermission.canOperate &&
    canUseSavedInvoiceOutput &&
    selectedPackageTemplates.length > 0 &&
    selectedPackageTemplates.length <= 20 &&
    (!desktopAvailable || Boolean(packageDestinationPath.trim())) &&
    !isBusy;
  const canSendDocumentEmail =
    reportOutputPermission.canOperate &&
    canUseSavedInvoiceOutput &&
    selectedPackageTemplates.length > 0 &&
    selectedPackageTemplates.length <= 20 &&
    Boolean(emailToAddress.trim()) &&
    !isBusy;
  const canEditPackageConfig = canManageSettings && Boolean(settingsQuery.data?.settings) && !isBusy;
  const canSavePackageConfig = canEditPackageConfig && packageConfigDirty && packageConfigDraft.items.length > 0;
  const canOpenTemplateDesigner = reportDesignPermission.canView && Boolean(selectedTemplatePath) && !isBusy;
  const templateMessage = templatesQuery.isError ? readApiError(templatesQuery.error) : null;
  const previewStoragePolicy = preview?.storagePolicy || packagePreview?.storagePolicy || "";
  const documentPackageDefaultFileName = buildDocumentPackageDefaultFileName(
    packageConfigDraft,
    invoiceNo,
    customerName,
    invoiceId,
  );
  const bookingSheetDefaultFileName = buildInvoiceBookingSheetDefaultFileName(invoiceNo, invoiceId);

  function handleTemplateChange(value: string) {
    setSelectedTemplatePath(value);
    const template = templates.find((item) => item.templatePath === value);
    if (template) {
      setWithSeal(template.withSealDefault);
    }
    setPreview(null);
    setPackagePreview(null);
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
    if (hasSavedInvoice) {
      params.set("invoiceId", String(invoiceId));
    }
    const templateFileName = fileNameFromPath(selectedTemplatePath);
    if (templateFileName) {
      params.set("template", templateFileName);
    }

    navigate(`/reports/templates?${params.toString()}`);
  }

  function changePackageTemplateSelected(templatePath: string, selected: boolean) {
    setPackageTemplateState((current) => ({
      ...current,
      [templatePath]: {
        selected,
        withSeal: current[templatePath]?.withSeal ?? true,
      },
    }));
    setPackagePreview(null);
    setStatusMessage(null);
    setLastCreatedJobId(null);
    setErrorMessage(null);
  }

  function changePackageTemplateSeal(templatePath: string, withSealValue: boolean) {
    setPackageTemplateState((current) => ({
      ...current,
      [templatePath]: {
        selected: current[templatePath]?.selected ?? true,
        withSeal: withSealValue,
      },
    }));
    setPackagePreview(null);
    setStatusMessage(null);
    setLastCreatedJobId(null);
    setErrorMessage(null);
  }

  function updatePackageSelectionFromConfig(previousTemplatePath: string, item: BatchExportItemSetting) {
    setPackageTemplateState((current) => {
      const next = { ...current };
      if (previousTemplatePath && previousTemplatePath !== item.templatePath) {
        delete next[previousTemplatePath];
      }

      if (item.templatePath) {
        next[item.templatePath] = {
          selected: item.isEnabled,
          withSeal: item.showSeal,
        };
      }

      return next;
    });
  }

  function updatePackageConfig(patch: Partial<Omit<BatchExportConfigDraft, "items">>) {
    setPackageConfigDraft((current) => ({ ...current, ...patch }));
    setPackageConfigDirty(true);
    setStatusMessage(null);
    setLastCreatedJobId(null);
    setErrorMessage(null);
  }

  function updatePackageConfigItem(index: number, patch: Partial<BatchExportItemSetting>) {
    const previousItem = packageConfigDraft.items[index];
    if (!previousItem) {
      return;
    }

    const nextItem = { ...previousItem, ...patch };
    if (patch.templatePath !== undefined && patch.name === undefined && !previousItem.name.trim()) {
      nextItem.name = readTemplateDisplayName(patch.templatePath, templates);
    }

    setPackageConfigDraft((current) => {
      if (index < 0 || index >= current.items.length) {
        return current;
      }

      const nextItems = current.items.map((item, itemIndex) => {
        return itemIndex === index ? nextItem : item;
      });
      return { ...current, items: nextItems };
    });
    updatePackageSelectionFromConfig(previousItem.templatePath, nextItem);
    setPackageConfigDirty(true);
    setPackagePreview(null);
    setStatusMessage(null);
    setLastCreatedJobId(null);
    setErrorMessage(null);
  }

  function addPackageConfigItem() {
    const template = findFirstUnusedTemplate(templates, packageConfigDraft.items);
    const nextItem: BatchExportItemSetting = {
      name: template?.displayName || fileNameFromPath(template?.templatePath ?? "") || "新单证",
      templatePath: template?.templatePath ?? "",
      isEnabled: true,
      showSeal: template?.withSealDefault ?? true,
      reportType,
    };
    setPackageConfigDraft((current) => ({ ...current, items: [...current.items, nextItem] }));
    updatePackageSelectionFromConfig("", nextItem);
    setPackageConfigDirty(true);
    setPackagePreview(null);
    setStatusMessage(null);
    setLastCreatedJobId(null);
    setErrorMessage(null);
  }

  function removePackageConfigItem(index: number) {
    const removedItem = packageConfigDraft.items[index];
    if (!removedItem) {
      return;
    }

    setPackageConfigDraft((current) => {
      return { ...current, items: current.items.filter((_, itemIndex) => itemIndex !== index) };
    });
    setPackageTemplateState((selection) => {
      const next = { ...selection };
      delete next[removedItem.templatePath];
      return next;
    });
    setPackageConfigDirty(true);
    setPackagePreview(null);
    setStatusMessage(null);
    setLastCreatedJobId(null);
    setErrorMessage(null);
  }

  function movePackageConfigItem(index: number, offset: number) {
    const targetIndex = index + offset;
    if (targetIndex < 0 || targetIndex >= packageConfigDraft.items.length) {
      return;
    }

    setPackageConfigDraft((current) => {
      const nextItems = [...current.items];
      const [item] = nextItems.splice(index, 1);
      nextItems.splice(targetIndex, 0, item);
      return { ...current, items: nextItems };
    });
    setPackageConfigDirty(true);
    setPackagePreview(null);
    setStatusMessage(null);
    setErrorMessage(null);
  }

  async function choosePackageConfigTemplateFile(index: number) {
    try {
      const selected = await selectReportTemplateFile();
      if (selected) {
        updatePackageConfigItem(index, { templatePath: selected });
      }
    } catch (error) {
      setErrorMessage(readDesktopError(error));
    }
  }

  function savePackageConfig() {
    if (!canSavePackageConfig) {
      return;
    }

    savePackageConfigMutation.mutate(packageConfigDraft);
  }

  async function pickPdfDestination() {
    try {
      const selected = await selectSavePdfPath(buildInvoiceReportPdfDefaultFileName(), defaultExportDirectory);
      if (selected) {
        setPdfDestinationPath(selected);
      }
    } catch (error) {
      setErrorMessage(readDesktopError(error));
    }
  }

  async function exportPdfWithSaveDialog() {
    if (!desktopAvailable) {
      setStatusMessage(null);
      setLastCreatedJobId(null);
      setErrorMessage(null);
      pdfMutation.mutate(undefined);
      return;
    }

    try {
      const selected = await selectSavePdfPath(buildInvoiceReportPdfDefaultFileName(), defaultExportDirectory);
      if (selected) {
        setPdfDestinationPath(selected);
        setStatusMessage(null);
        setLastCreatedJobId(null);
        setErrorMessage(null);
        pdfMutation.mutate(selected);
      }
    } catch (error) {
      setErrorMessage(readDesktopError(error));
    }
  }

  function buildInvoiceReportPdfDefaultFileName() {
    const template = templates.find((item) => item.templatePath === selectedTemplatePath);
    return buildReportPdfDefaultFileName({
      templatePath: selectedTemplatePath,
      displayName: template?.displayName,
      fallbackTitle: "ExportDocument",
      documentNumber: invoiceNo?.trim() || `invoice-${invoiceId}`,
    });
  }

  async function pickPackageDestination() {
    try {
      const selected = packageCreateZip
        ? await selectSaveZipPath(documentPackageDefaultFileName, defaultExportDirectory)
        : await selectDirectory(defaultExportDirectory);
      if (selected) {
        setPackageDestinationPath(selected);
        setStatusMessage(null);
        setLastCreatedJobId(null);
      }
    } catch (error) {
      setErrorMessage(readDesktopError(error));
    }
  }

  async function pickBookingSheetDestination() {
    try {
      const selected = await selectSaveExcelPath(bookingSheetDefaultFileName, defaultExportDirectory);
      if (selected) {
        setBookingSheetDestinationPath(selected);
        setStatusMessage(null);
        setLastCreatedJobId(null);
      }
    } catch (error) {
      setErrorMessage(readDesktopError(error));
    }
  }

  async function exportBookingSheetWithSaveDialog() {
    if (!desktopAvailable) {
      setStatusMessage(null);
      setLastCreatedJobId(null);
      setErrorMessage(null);
      bookingSheetMutation.mutate(undefined);
      return;
    }

    try {
      const selected = await selectSaveExcelPath(bookingSheetDefaultFileName, defaultExportDirectory);
      if (selected) {
        setBookingSheetDestinationPath(selected);
        setStatusMessage(null);
        setLastCreatedJobId(null);
        setErrorMessage(null);
        bookingSheetMutation.mutate(selected);
      }
    } catch (error) {
      setErrorMessage(readDesktopError(error));
    }
  }

  async function printPreview() {
    const html = packagePreview?.items.length
      ? buildDocumentPackagePrintHtml(packagePreview)
      : preview?.html ?? "";

    if (!html.trim()) {
      setErrorMessage("请先生成预览后再打印。");
      setStatusMessage(null);
      return;
    }

    try {
      setIsPrinting(true);
      setErrorMessage(null);
      await printReportPreviewHtml(html, packagePreview?.items.length ? "单据包打印预览" : "报表打印预览");
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
    <section className="form-section report-preview-section" aria-label="报表预览">
      <InvoiceReportPreviewHeader
        canPreview={canPreview} canPrint={canPrintPreview} canRefreshTemplates={reportOutputPermission.canView}
        errorMessage={errorMessage} hasSavedInvoice={hasSavedInvoice}
        hasUnsavedDraftChanges={hasUnsavedDraftChanges} isBusy={isBusy} jobId={lastCreatedJobId}
        previewStoragePolicy={previewStoragePolicy} statusMessage={statusMessage} templateMessage={templateMessage}
        onPreview={() => previewMutation.mutate()} onPrint={() => void printPreview()} onRefresh={() => void templatesQuery.refetch()}
      />
      {!reportOutputPermission.canOperate ? (
        <PermissionNotice>
          当前模板仅允许查看发票，未授予报表预览和单据输出操作权限。
        </PermissionNotice>
      ) : null}
      <InvoiceReportTemplateControls
        canConfigureOutput={reportOutputPermission.canOperate}
        canOpenTemplateDesigner={canOpenTemplateDesigner} canQuickGenerateBookingSheet={canQuickGenerateBookingSheet}
        canQuickGeneratePdf={canQuickGeneratePdf} desktopAvailable={desktopAvailable} hasSavedInvoice={hasSavedInvoice}
        isBusy={isBusy} showTemplateDesigner={reportDesignPermission.canView} showTemplateSettings={canManageSettings}
        selectedTemplatePath={selectedTemplatePath} templates={templates} withSeal={withSeal}
        onExportBookingSheet={() => void exportBookingSheetWithSaveDialog()} onExportPdf={() => void exportPdfWithSaveDialog()}
        onManageTemplates={() => navigate("/settings?section=documentTemplates")} onOpenTemplateDesigner={openTemplateDesigner}
        onTemplateChange={handleTemplateChange} onWithSealChange={(value) => { setWithSeal(value); setPreview(null); setPackagePreview(null); setStatusMessage(null); }}
      />

      {hasSavedInvoice && reportOutputPermission.canOperate ? (
        <>
          <details
            className="report-export-advanced"
            open={showExportAdvanced}
            onToggle={(event) => setShowExportAdvanced(event.currentTarget.open)}
          >
            <summary>
              <span>
                <SlidersHorizontal size={16} aria-hidden="true" />
                高级导出
              </span>
              <small>手动路径、单据包、邮件附件</small>
            </summary>
            <div className="report-export-advanced-body">
          {desktopAvailable ? <div className="report-pdf-controls">
            <PathField
              label="输出 PDF"
              value={pdfDestinationPath}
              disabled={isBusy}
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
            />
            <button
              className="command-button secondary"
              type="button"
              disabled={!canGeneratePdf}
              onClick={() => pdfMutation.mutate(undefined)}
            >
              <FileDown size={17} aria-hidden="true" />
              <span>生成 PDF</span>
            </button>
          </div> : null}

          {desktopAvailable ? <div className="report-pdf-controls">
            <PathField
              label="托单 Excel"
              value={bookingSheetDestinationPath}
              disabled={isBusy}
              onChange={(value) => {
                setBookingSheetDestinationPath(value);
                setStatusMessage(null);
              }}
              actions={
                <>
                  {desktopAvailable ? (
                    <DesktopIconButton title="选择发票托单保存位置" disabled={isBusy} onClick={pickBookingSheetDestination}>
                      <Save size={15} aria-hidden="true" />
                    </DesktopIconButton>
                  ) : null}
                  {renderOpenPathAction(bookingSheetDestinationPath, "打开发票托单输出位置", setErrorMessage)}
                </>
              }
            />
            <button
              className="command-button secondary"
              type="button"
              disabled={!canGenerateBookingSheet}
              onClick={() => bookingSheetMutation.mutate(undefined)}
            >
              <FileSpreadsheet size={17} aria-hidden="true" />
              <span>导出托单</span>
            </button>
          </div> : null}

          <div className="document-package-panel">
        <div className="document-package-heading">
          <h3>单据包</h3>
          <div className="toolbar-actions">
            <span>{selectedPackageTemplates.length} 个模板</span>
            <button
              className="command-button secondary"
              type="button"
              disabled={!canPreviewPackage}
              onClick={() => packagePreviewMutation.mutate()}
            >
              <Eye size={17} aria-hidden="true" />
              <span>预览单据包</span>
            </button>
            <button
              className="command-button secondary"
              type="button"
              title="保存当前单据包配置"
              disabled={!canSavePackageConfig}
              onClick={savePackageConfig}
            >
              <Save size={17} aria-hidden="true" />
              <span>保存配置</span>
            </button>
          </div>
        </div>

        <InvoiceDocumentPackageConfig
          canEdit={canEditPackageConfig} desktopAvailable={desktopAvailable} draft={packageConfigDraft}
          templateOptions={packageConfigTemplateOptions} onAdd={addPackageConfigItem} onChooseTemplate={choosePackageConfigTemplateFile}
          onMove={movePackageConfigItem} onRemove={removePackageConfigItem} onUpdate={updatePackageConfig}
          onUpdateItem={updatePackageConfigItem}
        />
        <div className="document-package-template-list" aria-label="单据包模板">
          {packageTemplates.length === 0 ? (
            <div className="document-package-empty">{templatesQuery.isFetching ? "加载中" : "暂无模板"}</div>
          ) : (
            packageTemplates.map((entry) => {
              const template = entry.template;
              const state = packageTemplateState[template.templatePath] ?? {
                selected: entry.initiallySelected,
                withSeal: entry.withSealDefault,
              };
              return (
                <div className="document-package-template-row" key={template.templatePath}>
                  <label className="document-package-template-check">
                    <input
                      type="checkbox"
                      checked={state.selected}
                      disabled={isBusy}
                      onChange={(event) => changePackageTemplateSelected(template.templatePath, event.target.checked)}
                    />
                    <span title={template.templatePath}>{entry.displayName || fileNameFromPath(template.templatePath)}</span>
                  </label>
                  <label className="document-package-seal-check">
                    <input
                      type="checkbox"
                      checked={state.withSeal}
                      disabled={isBusy || !state.selected}
                      onChange={(event) => changePackageTemplateSeal(template.templatePath, event.target.checked)}
                    />
                    <span>带章</span>
                  </label>
                </div>
              );
            })
          )}
        </div>

        <div className="report-pdf-controls document-package-output" data-default-file-name={documentPackageDefaultFileName}>
          {desktopAvailable ? <label className="toggle-field document-package-zip-check">
            <input
              type="checkbox"
              checked={packageCreateZip}
              disabled={isBusy}
              onChange={(event) => {
              setPackageCreateZip(event.target.checked);
              setPackageCreateZipTouched(true);
              setPackageDestinationPath("");
              setPackagePreview(null);
              setStatusMessage(null);
            }}
            />
            <span>生成 ZIP</span>
          </label> : <div className="field-help">浏览器将生成 ZIP，并保存到默认下载目录。</div>}
          {desktopAvailable ? <PathField
            label={packageCreateZip ? "输出 ZIP" : "输出文件夹"}
            value={packageDestinationPath}
            disabled={isBusy}
            onChange={(value) => {
              setPackageDestinationPath(value);
              setPackagePreview(null);
              setStatusMessage(null);
            }}
            actions={
              <>
                {desktopAvailable ? (
                  <DesktopIconButton title="选择保存位置" disabled={isBusy} onClick={pickPackageDestination}>
                    <Save size={15} aria-hidden="true" />
                  </DesktopIconButton>
                ) : null}
                {renderOpenPathAction(packageDestinationPath, "打开输出位置", setErrorMessage)}
              </>
            }
          /> : null}
          <label className="toggle-field document-package-merge-check">
            <input
              type="checkbox"
              checked={packageIncludeMergedPdf}
              disabled={isBusy || selectedPackageTemplates.length < 2}
              onChange={(event) => {
                setPackageIncludeMergedPdf(event.target.checked);
                setPackageMergePdfTouched(true);
                setPackagePreview(null);
                setStatusMessage(null);
              }}
            />
            <span>合并 PDF</span>
          </label>
          <button
            className="command-button secondary"
            type="button"
            disabled={!canGeneratePackage}
            onClick={() => packageMutation.mutate()}
          >
            <FileArchive size={17} aria-hidden="true" />
            <span>{desktopAvailable ? (packageCreateZip ? "生成 ZIP" : "导出文件夹") : "下载 ZIP"}</span>
          </button>
        </div>

        <InvoiceDocumentEmailPanel
          body={emailBody} canSend={canSendDocumentEmail} includeMergedPdf={emailIncludeMergedPdf} isBusy={isBusy}
          selectedTemplateCount={selectedPackageTemplates.length} subject={emailSubject} toAddress={emailToAddress}
          onBodyChange={(value) => { setEmailBody(value); setEmailBodyTouched(true); setStatusMessage(null); }}
          onIncludeMergedPdfChange={(value) => { setEmailIncludeMergedPdf(value); setStatusMessage(null); }}
          onOpenSettings={() => navigate("/settings?section=email")} onSend={() => emailMutation.mutate()}
          onSubjectChange={(value) => { setEmailSubject(value); setEmailSubjectTouched(true); setStatusMessage(null); }}
          onToAddressChange={(value) => { setEmailToAddress(value); setStatusMessage(null); }}
        />

          </div>
            </div>
          </details>
        </>
      ) : null}

      <InvoiceReportPreviewCanvas isBusy={isBusy} packagePreview={packagePreview} preview={preview} />
    </section>
  );
}
