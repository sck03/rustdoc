import { useMutation, useQueryClient } from "@tanstack/react-query";
import {
  ApiReportTemplatePackageExportResponse,
  ApiReportTemplatePackageImportResponse,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { downloadBlob } from "../../ui/downloadBlob.ts";
import {
  buildTemplatePackageFileName,
  fileNameFromPath,
  type ReportTypeOption,
  type TemplateImportStrategyOption,
} from "./reportTemplateDesignerModel.ts";

export function useReportTemplatePackageMutations({
  client,
  reportType,
  selectedTemplatePath,
  packageExportPath,
  importStrategy,
  onExported,
  onDownloaded,
  onImported,
  onError,
}: {
  client: ExportDocManagerApiClient;
  reportType: ReportTypeOption;
  selectedTemplatePath: string;
  packageExportPath: string;
  importStrategy: TemplateImportStrategyOption;
  onExported: (response: ApiReportTemplatePackageExportResponse) => void;
  onDownloaded: () => void;
  onImported: (response: ApiReportTemplatePackageImportResponse, source: "path" | "upload") => void;
  onError: (error: unknown) => void;
}) {
  const queryClient = useQueryClient();

  const exportPackageMutation = useMutation({
    mutationFn: (packagePath: string) => client.saveReportTemplatePackageToPath({ body: { packagePath } }),
    onSuccess: onExported,
    onError,
  });

  const downloadPackageMutation = useMutation({
    mutationFn: () => client.downloadReportTemplatePackage(),
    onSuccess: (blob) => {
      const fileName = fileNameFromPath(packageExportPath.trim()) || buildTemplatePackageFileName();
      downloadBlob(blob, fileName.endsWith(".edtpl") ? fileName : `${fileName}.edtpl`);
      onDownloaded();
    },
    onError,
  });

  async function invalidateTemplateQueries() {
    await queryClient.invalidateQueries({ queryKey: queryKeys.reportTemplates(reportType) });
    if (selectedTemplatePath) {
      await queryClient.invalidateQueries({
        queryKey: queryKeys.reportTemplateContent(reportType, selectedTemplatePath),
      });
    }
  }

  const importPackageMutation = useMutation({
    mutationFn: (packagePath: string) =>
      client.importReportTemplatePackage({ body: { packagePath, strategy: importStrategy } }),
    onSuccess: async (response) => {
      onImported(response, "path");
      await invalidateTemplateQueries();
    },
    onError,
  });

  const uploadPackageMutation = useMutation({
    mutationFn: (file: File) =>
      client.uploadReportTemplatePackage({ strategy: importStrategy, fileName: file.name, body: file }),
    onSuccess: async (response) => {
      onImported(response, "upload");
      await invalidateTemplateQueries();
    },
    onError,
  });

  return { exportPackageMutation, downloadPackageMutation, importPackageMutation, uploadPackageMutation };
}
