import { useScopedMutation } from "../../ui/useScopedMutation.ts";
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
  const scope = `${reportType}:${selectedTemplatePath}`;
  const exportPackageMutation = useScopedMutation(scope, client, {
    mutationFn: (packagePath: string, signal) => client.saveReportTemplatePackageToPath({ body: { packagePath } }, { signal }),
    onSuccess: onExported,
    onError,
  });

  const downloadPackageMutation = useScopedMutation(scope, client, {
    mutationFn: (_: void, signal) => client.downloadReportTemplatePackage({ signal }),
    onSuccess: (blob) => {
      const fileName = fileNameFromPath(packageExportPath.trim()) || buildTemplatePackageFileName();
      downloadBlob(blob, fileName.endsWith(".edtpl") ? fileName : `${fileName}.edtpl`);
      onDownloaded();
    },
    onError,
  });

  const importPackageMutation = useScopedMutation(scope, client, {
    mutationFn: (packagePath: string, signal) =>
      client.importReportTemplatePackage({ body: { packagePath, strategy: importStrategy } }, { signal }),
    onSuccess: (response) => {
      onImported(response, "path");
    },
    refreshKeys: () => [queryKeys.reportTemplates(reportType)],
    onError,
  });

  const uploadPackageMutation = useScopedMutation(scope, client, {
    mutationFn: (file: File, signal) =>
      client.uploadReportTemplatePackage({ strategy: importStrategy, fileName: file.name, body: file }, { signal }),
    onSuccess: (response) => {
      onImported(response, "upload");
    },
    refreshKeys: () => [queryKeys.reportTemplates(reportType)],
    onError,
  });

  return { exportPackageMutation, downloadPackageMutation, importPackageMutation, uploadPackageMutation };
}
