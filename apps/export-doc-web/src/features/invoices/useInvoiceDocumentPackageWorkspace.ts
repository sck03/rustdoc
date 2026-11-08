import { useEffect, useMemo, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import type {
  ApiReportTemplateDto,
  ApiSettingsResponse,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { selectDirectory, selectSaveZipPath } from "../../desktop/desktopBridge.ts";
import { readDesktopError } from "../../ui/DesktopPathActions.tsx";
import { downloadJobResultWhenReady } from "../../ui/downloadJobResult.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { useAbortableOperation } from "../../ui/useAbortableOperation.ts";
import { useTransientPreview } from "../../ui/useTransientPreview.ts";
import {
  buildBatchExportConfigDraft,
  buildDocumentEmailBody,
  buildDocumentEmailSubject,
  buildDocumentPackageDefaultFileName,
  buildPackageTemplateViewsFromItems,
  fileNameFromPath,
  formatDateForBatchExport,
} from "./invoiceReportPreviewModel.ts";

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
  sourceRevision?: unknown;
  catalogRevision?: number;
  previewAllowed: boolean;
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
  const runAbortableOperation = useAbortableOperation();
  const [destinationPath, setDestinationPath] = useState("");
  const [createZip, setCreateZip] = useState(true);
  const [createZipTouched, setCreateZipTouched] = useState(false);
  const [includeMergedPdf, setIncludeMergedPdf] = useState(true);
  const [mergePdfTouched, setMergePdfTouched] = useState(false);
  const [templateState, setTemplateState] = useState<Record<string, PackageTemplateState>>({});
  const [emailToAddress, setEmailToAddress] = useState(defaultToAddress ?? "");
  const [emailSubject, setEmailSubject] = useState("");
  const [emailSubjectTouched, setEmailSubjectTouched] = useState(false);
  const [emailBody, setEmailBody] = useState("");
  const [emailBodyTouched, setEmailBodyTouched] = useState(false);
  const [emailIncludeMergedPdf, setEmailIncludeMergedPdf] = useState(false);

  const configDefaults = useMemo(
    () => buildBatchExportConfigDraft(settingsResponse?.settings, templates),
    [settingsResponse?.settings, templates],
  );
  const packageTemplates = useMemo(
    () => buildPackageTemplateViewsFromItems(templates, configDefaults.items),
    [configDefaults.items, templates],
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
    setTemplateState((current) => {
      const next: Record<string, PackageTemplateState> = {};
      for (const entry of packageTemplates) {
        next[entry.template.templatePath] = current[entry.template.templatePath] ?? {
          selected: entry.initiallySelected,
          withSeal: entry.withSealDefault,
        };
      }
      return next;
    });
  }, [packageTemplates]);

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
    if (!mergePdfTouched) setIncludeMergedPdf(configDefaults.mergePdf);
  }, [configDefaults.mergePdf, mergePdfTouched]);
  useEffect(() => {
    if (!createZipTouched) setCreateZip(configDefaults.zipAfterExport);
  }, [configDefaults.zipAfterExport, createZipTouched]);

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
  const defaultFileName = buildDocumentPackageDefaultFileName(configDefaults, invoiceNo, customerName, invoiceId);

  const selectionKey = JSON.stringify(selectedItems);
  const previewScope = useMemo(() => ({}), [client, invoiceId, options.sourceRevision,
    options.catalogRevision, options.previewAllowed, selectionKey, templates]);
  const packagePreview = useTransientPreview(previewScope, (signal) =>
    client.previewInvoiceDocumentPackageHtml({ invoiceId, body: { items: selectedItems } }, { signal }),
    (error) => feedback.showError(readApiError(error)), () => {
      onPreviewGenerated();
      feedback.clear();
    });
  const packageMutation = useMutation({
    mutationFn: async () => runAbortableOperation(async (signal) => {
      const job = desktopAvailable
        ? await client.startInvoiceDocumentPackageSaveToPathJob({
            invoiceId,
            body: { items: selectedItems, includeMergedPdf, createZip, destinationPath: destinationPath.trim() },
          }, { signal })
        : await client.startInvoiceDocumentPackageDownloadJob({
            invoiceId,
            body: { items: selectedItems, includeMergedPdf, createZip: true, destinationPath: "" },
          }, { signal });
      if (!desktopAvailable) {
        const downloadName = defaultFileName.toLowerCase().endsWith(".zip") ? defaultFileName : `${defaultFileName}.zip`;
        await downloadJobResultWhenReady(client, job, downloadName, { signal });
      }
      return job;
    }),
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
    mutationFn: () => runAbortableOperation((signal) => client.startInvoiceDocumentEmailJob({
      invoiceId,
      body: {
        items: selectedItems,
        includeMergedPdf: emailIncludeMergedPdf,
        toAddress: emailToAddress.trim(),
        subject: emailSubject.trim(),
        body: emailBody,
      },
    }, { signal })),
    onSuccess: async (job) => {
      feedback.showJob(`已创建单据邮件任务：${job.jobId}`, job.jobId);
      await queryClient.invalidateQueries({ queryKey: queryKeys.jobsRoot() });
    },
    onError: (error) => feedback.showError(readApiError(error)),
  });
  function clearGeneratedOutput() {
    packagePreview.clear();
    feedback.clear();
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
    preview: packagePreview.preview,
    destinationPath,
    createZip,
    includeMergedPdf,
    templateState,
    packageTemplates,
    selectedTemplates,
    defaultFileName,
    emailToAddress,
    emailSubject,
    emailBody,
    emailIncludeMergedPdf,
    isPending: packagePreview.isPending || packageMutation.isPending || emailMutation.isPending,
    clearPreview: packagePreview.clear,
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
    previewPackage: () => { if (options.previewAllowed) void packagePreview.generate(); },
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
