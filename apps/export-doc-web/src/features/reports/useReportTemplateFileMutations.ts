import { useMutation, useQueryClient } from "@tanstack/react-query";
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
  fileExportPath,
  onExported,
  onDownloaded,
  onImported,
  onError,
}: {
  client: ExportDocManagerApiClient;
  reportType: ReportTypeOption;
  selectedTemplatePath: string;
  fileExportPath: string;
  onExported: (response: ApiReportTemplateFileExportResponse) => void;
  onDownloaded: () => void;
  onImported: (response: ApiReportTemplateContentDto, source: "path" | "upload") => void;
  onError: (error: unknown) => void;
}) {
  const queryClient = useQueryClient();

  const exportFileMutation = useMutation({
    mutationFn: (filePath: string) => client.saveReportTemplateFileToPath({
      body: { reportType, templatePath: selectedTemplatePath, filePath },
    }),
    onSuccess: onExported,
    onError,
  });

  const downloadFileMutation = useMutation({
    mutationFn: () => client.downloadReportTemplateFile({
      body: { reportType, templatePath: selectedTemplatePath },
    }),
    onSuccess: (blob) => {
      const fileName = fileNameFromPath(fileExportPath.trim()) || buildTemplateFileName(reportType);
      downloadBlob(blob, fileName.endsWith(".html") ? fileName : `${fileName}.html`);
      onDownloaded();
    },
    onError,
  });

  async function invalidateTemplateQueries(templatePath: string) {
    await queryClient.invalidateQueries({ queryKey: queryKeys.reportTemplates(reportType) });
    if (templatePath) {
      await queryClient.invalidateQueries({ queryKey: queryKeys.reportTemplateContent(reportType, templatePath) });
    }
    if (selectedTemplatePath && selectedTemplatePath !== templatePath) {
      await queryClient.invalidateQueries({ queryKey: queryKeys.reportTemplateContent(reportType, selectedTemplatePath) });
    }
  }

  const importFileMutation = useMutation({
    mutationFn: (filePath: string) => client.importReportTemplateFile({
      body: { reportType, templatePath: selectedTemplatePath, filePath },
    }),
    onSuccess: async (response) => {
      onImported(response, "path");
      await invalidateTemplateQueries(response.templatePath);
    },
    onError,
  });

  const uploadFileMutation = useMutation({
    mutationFn: async (file: File) => client.uploadReportTemplateFile({
      reportType,
      templatePath: selectedTemplatePath,
      fileName: file.name,
      // The raw HTML endpoint must not be JSON encoded by the generated client.
      body: new Blob([await file.arrayBuffer()], { type: "text/html" }),
    }),
    onSuccess: async (response) => {
      onImported(response, "upload");
      await invalidateTemplateQueries(response.templatePath);
    },
    onError,
  });

  return { exportFileMutation, downloadFileMutation, importFileMutation, uploadFileMutation };
}
