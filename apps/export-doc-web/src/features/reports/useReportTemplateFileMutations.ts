import { useQueryClient } from "@tanstack/react-query";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";
import {
  ApiReportTemplateContentDto,
  ApiReportTemplateFileExportResponse,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { downloadBlob } from "../../ui/downloadBlob.ts";
import { buildTemplateFileName, fileNameFromPath, type ReportTypeOption } from "./reportTemplateDesignerModel.ts";

export function useReportTemplateFileMutations({
  client,
  reportType,
  selectedTemplatePath,
  expectedRevision,
  fileExportPath,
  onExported,
  onDownloaded,
  onImported,
  onError,
}: {
  client: ExportDocManagerApiClient;
  reportType: ReportTypeOption;
  selectedTemplatePath: string;
  expectedRevision: string;
  fileExportPath: string;
  onExported: (response: ApiReportTemplateFileExportResponse) => void;
  onDownloaded: () => void;
  onImported: (response: ApiReportTemplateContentDto, source: "path" | "upload") => void;
  onError: (error: unknown) => void;
}) {
  const queryClient = useQueryClient();
  const scope = `${reportType}:${selectedTemplatePath}`;

  const exportFileMutation = useScopedMutation(scope, client, {
    mutationFn: (filePath: string, signal) => client.saveReportTemplateFileToPath({
      body: { reportType, templatePath: selectedTemplatePath, filePath },
    }, { signal }),
    onSuccess: onExported,
    onError,
  });

  const downloadFileMutation = useScopedMutation(scope, client, {
    mutationFn: (_: void, signal) => client.downloadReportTemplateFile({
      body: { reportType, templatePath: selectedTemplatePath },
    }, { signal }),
    onSuccess: (blob) => {
      const fileName = fileNameFromPath(fileExportPath.trim()) || buildTemplateFileName(reportType);
      downloadBlob(blob, fileName.endsWith(".dtpl") ? fileName : `${fileName}.dtpl`);
      onDownloaded();
    },
    onError,
  });

  function cacheTemplate(response: ApiReportTemplateContentDto) {
    queryClient.setQueryData(queryKeys.reportTemplateContent(reportType, response.templatePath), response);
  }

  const importFileMutation = useScopedMutation(scope, client, {
    mutationFn: (filePath: string, signal) => client.importReportTemplateFile({
      body: { reportType, templatePath: selectedTemplatePath, filePath, expectedRevision },
    }, { signal }),
    onSuccess: (response) => {
      cacheTemplate(response);
      onImported(response, "path");
    },
    refreshKeys: () => [queryKeys.reportTemplates(reportType)],
    onError,
  });

  const uploadFileMutation = useScopedMutation(scope, client, {
    mutationFn: (file: File, signal) => client.uploadReportTemplateFile({
      reportType,
      templatePath: selectedTemplatePath,
      expectedRevision,
      fileName: file.name,
      body: file,
    }, { signal }),
    onSuccess: (response) => {
      cacheTemplate(response);
      onImported(response, "upload");
    },
    refreshKeys: () => [queryKeys.reportTemplates(reportType)],
    onError,
  });

  return { exportFileMutation, downloadFileMutation, importFileMutation, uploadFileMutation };
}
