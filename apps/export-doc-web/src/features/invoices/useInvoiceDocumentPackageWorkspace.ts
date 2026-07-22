import { useEffect, useMemo, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import type {
  ApiInvoiceDocumentPackagePreviewResponse,
  ApiReportTemplateDto,
  ApiSettingsResponse,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { selectDirectory, selectReportTemplateFile, selectSaveZipPath } from "../../desktop/desktopBridge.ts";
import { readDesktopError } from "../../ui/DesktopPathActions.tsx";
import { downloadJobResultWhenReady } from "../../ui/downloadJobResult.ts";
import { readApiError } from "../../ui/formUtils.ts";
import {
  buildBatchExportConfigDraft,
  buildDocumentEmailBody,
  buildDocumentEmailSubject,
  buildDocumentPackageDefaultFileName,
  buildPackageTemplateViewsFromItems,
  buildSettingsWithBatchExportConfig,
  buildTemplateSelectOptions,
  createEmptyBatchExportConfigDraft,
  fileNameFromPath,
  findFirstUnusedTemplate,
  formatDateForBatchExport,
  readTemplateDisplayName,
} from "./invoiceReportPreviewModel.ts";
import type { BatchExportConfigDraft, BatchExportItemSetting } from "./invoiceReportPreviewModel.ts";

type PackageTemplateState = { selected: boolean; withSeal: boolean };
type Feedback = {
  clear(): void;
  showError(message: string): void;
  showJob(message: string, jobId: string): void;
  showStatus(message: string): void;
};
type Options = {
  client: ExportDocManagerApiClient;
  invoiceId: number;
  invoiceNo?: string;
  customerName?: string;
  defaultToAddress?: string;
  templates: ApiReportTemplateDto[];
  settingsResponse?: ApiSettingsResponse;
  desktopAvailable: boolean;
  defaultExportDirectory: string;
  feedback: Feedback;
  onPreviewGenerated(): void;
};

export function useInvoiceDocumentPackageWorkspace(options: Options) {
  const {
    client,
    invoiceId,
    invoiceNo,
    customerName,
    defaultToAddress,
    templates,
    settingsResponse,
    desktopAvailable,
    defaultExportDirectory,
    feedback,
    onPreviewGenerated,
  } = options;
  const queryClient = useQueryClient();
  const [preview, setPreview] = useState<ApiInvoiceDocumentPackagePreviewResponse | null>(null);
  const [destinationPath, setDestinationPath] = useState("");
  const [createZip, setCreateZip] = useState(true);
  const [createZipTouched, setCreateZipTouched] = useState(false);
  const [includeMergedPdf, setIncludeMergedPdf] = useState(true);
  const [mergePdfTouched, setMergePdfTouched] = useState(false);
  const [templateState, setTemplateState] = useState<Record<string, PackageTemplateState>>({});
  const [configDraft, setConfigDraft] = useState<BatchExportConfigDraft>(() => createEmptyBatchExportConfigDraft());
  const [configDirty, setConfigDirty] = useState(false);
  const [emailToAddress, setEmailToAddress] = useState(defaultToAddress ?? "");
  const [emailSubject, setEmailSubject] = useState("");
  const [emailSubjectTouched, setEmailSubjectTouched] = useState(false);
  const [emailBody, setEmailBody] = useState("");
  const [emailBodyTouched, setEmailBodyTouched] = useState(false);
  const [emailIncludeMergedPdf, setEmailIncludeMergedPdf] = useState(false);

  const packageTemplates = useMemo(
    () => buildPackageTemplateViewsFromItems(templates, configDraft.items),
    [configDraft.items, templates],
  );
  const templateOptions = useMemo(
    () => buildTemplateSelectOptions(templates, configDraft.items),
    [configDraft.items, templates],
  );
  const documentEmailDate = useMemo(() => formatDateForBatchExport(new Date()), [invoiceId]);
  const defaultEmailSubject = useMemo(
    () => buildDocumentEmailSubject(settingsResponse?.settings, invoiceNo, customerName, documentEmailDate),
    [customerName, documentEmailDate, invoiceNo, settingsResponse?.settings],
  );
  const defaultEmailBody = useMemo(
    () => buildDocumentEmailBody(settingsResponse?.settings, invoiceNo, customerName, documentEmailDate),
    [customerName, documentEmailDate, invoiceNo, settingsResponse?.settings],
  );

  useEffect(() => {
    if (!configDirty) setConfigDraft(buildBatchExportConfigDraft(settingsResponse?.settings, templates));
  }, [configDirty, settingsResponse?.settings, templates]);

  useEffect(() => {
    if (!packageTemplates.length) return;
    setTemplateState((current) => {
      const next: Record<string, PackageTemplateState> = {};
      for (const entry of packageTemplates) {
        const existing = current[entry.template.templatePath];
        next[entry.template.templatePath] = {
          selected: configDirty ? existing?.selected ?? entry.initiallySelected : entry.initiallySelected,
          withSeal: configDirty ? existing?.withSeal ?? entry.withSealDefault : entry.withSealDefault,
        };
      }
      return next;
    });
  }, [configDirty, packageTemplates]);

  useEffect(() => {
    const nextAddress = defaultToAddress?.trim() ?? "";
    if (nextAddress) setEmailToAddress((current) => current || nextAddress);
  }, [defaultToAddress]);
  useEffect(() => {
    if (!emailSubjectTouched) setEmailSubject(defaultEmailSubject);
  }, [defaultEmailSubject, emailSubjectTouched]);
  useEffect(() => {
    if (!emailBodyTouched) setEmailBody(defaultEmailBody);
  }, [defaultEmailBody, emailBodyTouched]);
  useEffect(() => {
    if (!mergePdfTouched) setIncludeMergedPdf(configDraft.mergePdf);
  }, [configDraft.mergePdf, mergePdfTouched]);
  useEffect(() => {
    if (!createZipTouched) setCreateZip(configDraft.zipAfterExport);
  }, [configDraft.zipAfterExport, createZipTouched]);

  const selectedTemplates = packageTemplates
    .filter((entry) => templateState[entry.template.templatePath]?.selected)
    .map((entry) => ({
      displayName: entry.displayName,
      templatePath: entry.template.templatePath,
      withSealDefault: entry.withSealDefault,
    }));
  const selectedItems = selectedTemplates.map((template) => ({
    name: template.displayName || fileNameFromPath(template.templatePath),
    reportType: "ExportDocument",
    templatePath: template.templatePath,
    withSeal: templateState[template.templatePath]?.withSeal ?? template.withSealDefault,
  }));
  const defaultFileName = buildDocumentPackageDefaultFileName(configDraft, invoiceNo, customerName, invoiceId);

  const previewMutation = useMutation({
    mutationFn: () => client.previewInvoiceDocumentPackageHtml({ invoiceId, body: { items: selectedItems } }),
    onSuccess: (response) => {
      setPreview(response);
      onPreviewGenerated();
      feedback.clear();
    },
    onError: (error) => feedback.showError(readApiError(error)),
  });
  const packageMutation = useMutation({
    mutationFn: async () => {
      const job = desktopAvailable
        ? await client.startInvoiceDocumentPackageSaveToPathJob({
            invoiceId,
            body: { items: selectedItems, includeMergedPdf, createZip, destinationPath: destinationPath.trim() },
          })
        : await client.startInvoiceDocumentPackageDownloadJob({
            invoiceId,
            body: { items: selectedItems, includeMergedPdf, createZip: true, destinationPath: "" },
          });
      if (!desktopAvailable) {
        const downloadName = defaultFileName.toLowerCase().endsWith(".zip") ? defaultFileName : `${defaultFileName}.zip`;
        await downloadJobResultWhenReady(client, job, downloadName);
      }
      return job;
    },
    onSuccess: async (job) => {
      feedback.showJob(
        desktopAvailable
          ? `已创建${createZip ? "单据包 ZIP" : "单据文件夹导出"}任务：${job.jobId}`
          : "单据包 ZIP 已交给浏览器下载。",
        job.jobId,
      );
      await queryClient.invalidateQueries({ queryKey: queryKeys.jobsRoot() });
    },
    onError: (error) => feedback.showError(readApiError(error)),
  });
  const emailMutation = useMutation({
    mutationFn: () => client.startInvoiceDocumentEmailJob({
      invoiceId,
      body: {
        items: selectedItems,
        includeMergedPdf: emailIncludeMergedPdf,
        toAddress: emailToAddress.trim(),
        subject: emailSubject.trim(),
        body: emailBody,
      },
    }),
    onSuccess: async (job) => {
      feedback.showJob(`已创建单据邮件任务：${job.jobId}`, job.jobId);
      await queryClient.invalidateQueries({ queryKey: queryKeys.jobsRoot() });
    },
    onError: (error) => feedback.showError(readApiError(error)),
  });
  const saveConfigMutation = useMutation({
    mutationFn: (draft: BatchExportConfigDraft) => client.updateSettings({
      body: { settings: buildSettingsWithBatchExportConfig(settingsResponse?.settings ?? {}, draft), updateSecrets: false },
    }),
    onSuccess: async (response) => {
      const nextDraft = buildBatchExportConfigDraft(response.settings, templates);
      setConfigDraft(nextDraft);
      setConfigDirty(false);
      setMergePdfTouched(false);
      setCreateZipTouched(false);
      setIncludeMergedPdf(nextDraft.mergePdf);
      setCreateZip(nextDraft.zipAfterExport);
      feedback.showStatus(response.message || "单据包配置已保存。");
      queryClient.setQueryData<ApiSettingsResponse>(queryKeys.settings(), {
        secrets: response.secrets,
        settings: response.settings,
        storagePolicy: settingsResponse?.storagePolicy ?? "",
      });
      await queryClient.invalidateQueries({ queryKey: queryKeys.settings() });
    },
    onError: (error) => feedback.showError(readApiError(error)),
  });

  function clearGeneratedOutput() {
    setPreview(null);
    feedback.clear();
  }
  function updateSelectionFromConfig(previousTemplatePath: string, item: BatchExportItemSetting) {
    setTemplateState((current) => {
      const next = { ...current };
      if (previousTemplatePath && previousTemplatePath !== item.templatePath) delete next[previousTemplatePath];
      if (item.templatePath) next[item.templatePath] = { selected: item.isEnabled, withSeal: item.showSeal };
      return next;
    });
  }
  function updateConfig(patch: Partial<Omit<BatchExportConfigDraft, "items">>) {
    setConfigDraft((current) => ({ ...current, ...patch }));
    setConfigDirty(true);
    clearGeneratedOutput();
  }
  function updateConfigItem(index: number, patch: Partial<BatchExportItemSetting>) {
    const previousItem = configDraft.items[index];
    if (!previousItem) return;
    const nextItem = { ...previousItem, ...patch };
    if (patch.templatePath !== undefined && patch.name === undefined && !previousItem.name.trim()) {
      nextItem.name = readTemplateDisplayName(patch.templatePath, templates);
    }
    setConfigDraft((current) => ({
      ...current,
      items: current.items.map((item, itemIndex) => itemIndex === index ? nextItem : item),
    }));
    updateSelectionFromConfig(previousItem.templatePath, nextItem);
    setConfigDirty(true);
    clearGeneratedOutput();
  }
  function addConfigItem() {
    const template = findFirstUnusedTemplate(templates, configDraft.items);
    const nextItem: BatchExportItemSetting = {
      name: template?.displayName || fileNameFromPath(template?.templatePath ?? "") || "新单证",
      templatePath: template?.templatePath ?? "",
      isEnabled: true,
      showSeal: template?.withSealDefault ?? true,
      reportType: "ExportDocument",
    };
    setConfigDraft((current) => ({ ...current, items: [...current.items, nextItem] }));
    updateSelectionFromConfig("", nextItem);
    setConfigDirty(true);
    clearGeneratedOutput();
  }
  function removeConfigItem(index: number) {
    const removedItem = configDraft.items[index];
    if (!removedItem) return;
    setConfigDraft((current) => ({ ...current, items: current.items.filter((_, itemIndex) => itemIndex !== index) }));
    setTemplateState((current) => {
      const next = { ...current };
      delete next[removedItem.templatePath];
      return next;
    });
    setConfigDirty(true);
    clearGeneratedOutput();
  }
  function moveConfigItem(index: number, offset: -1 | 1) {
    const targetIndex = index + offset;
    if (targetIndex < 0 || targetIndex >= configDraft.items.length) return;
    setConfigDraft((current) => {
      const items = [...current.items];
      const [item] = items.splice(index, 1);
      items.splice(targetIndex, 0, item);
      return { ...current, items };
    });
    setConfigDirty(true);
    clearGeneratedOutput();
  }
  async function chooseConfigTemplateFile(index: number) {
    try {
      const selected = await selectReportTemplateFile();
      if (selected) updateConfigItem(index, { templatePath: selected });
    } catch (error) {
      feedback.showError(readDesktopError(error));
    }
  }
  async function pickDestination() {
    try {
      const selected = createZip
        ? await selectSaveZipPath(defaultFileName, defaultExportDirectory)
        : await selectDirectory(defaultExportDirectory);
      if (selected) {
        setDestinationPath(selected);
        feedback.clear();
      }
    } catch (error) {
      feedback.showError(readDesktopError(error));
    }
  }

  return {
    preview,
    destinationPath,
    createZip,
    includeMergedPdf,
    templateState,
    configDraft,
    configDirty,
    packageTemplates,
    templateOptions,
    selectedTemplates,
    defaultFileName,
    emailToAddress,
    emailSubject,
    emailBody,
    emailIncludeMergedPdf,
    isPending: previewMutation.isPending || packageMutation.isPending || emailMutation.isPending || saveConfigMutation.isPending,
    clearPreview: () => setPreview(null),
    changeTemplateSelected(templatePath: string, selected: boolean) {
      setTemplateState((current) => ({
        ...current,
        [templatePath]: { selected, withSeal: current[templatePath]?.withSeal ?? true },
      }));
      clearGeneratedOutput();
    },
    changeTemplateSeal(templatePath: string, withSeal: boolean) {
      setTemplateState((current) => ({
        ...current,
        [templatePath]: { selected: current[templatePath]?.selected ?? true, withSeal },
      }));
      clearGeneratedOutput();
    },
    updateConfig,
    updateConfigItem,
    addConfigItem,
    removeConfigItem,
    moveConfigItem,
    chooseConfigTemplateFile,
    saveConfig: () => saveConfigMutation.mutate(configDraft),
    previewPackage: () => previewMutation.mutate(),
    generatePackage: () => packageMutation.mutate(),
    pickDestination,
    changeDestination(value: string) {
      setDestinationPath(value);
      clearGeneratedOutput();
    },
    changeCreateZip(value: boolean) {
      setCreateZip(value);
      setCreateZipTouched(true);
      setDestinationPath("");
      clearGeneratedOutput();
    },
    changeIncludeMergedPdf(value: boolean) {
      setIncludeMergedPdf(value);
      setMergePdfTouched(true);
      clearGeneratedOutput();
    },
    changeEmailToAddress(value: string) {
      setEmailToAddress(value);
      feedback.clear();
    },
    changeEmailSubject(value: string) {
      setEmailSubject(value);
      setEmailSubjectTouched(true);
      feedback.clear();
    },
    changeEmailBody(value: string) {
      setEmailBody(value);
      setEmailBodyTouched(true);
      feedback.clear();
    },
    changeEmailIncludeMergedPdf(value: boolean) {
      setEmailIncludeMergedPdf(value);
      feedback.clear();
    },
    sendEmail: () => emailMutation.mutate(),
  };
}

export type InvoiceDocumentPackageWorkspace = ReturnType<typeof useInvoiceDocumentPackageWorkspace>;
