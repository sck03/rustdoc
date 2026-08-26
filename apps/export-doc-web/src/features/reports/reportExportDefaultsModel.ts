import type { ApiReportTemplateDto, BatchExportItem } from "../../api/index.ts";

export function fileNameFromPath(path: string) {
  return path.split(/[\\/]/).filter(Boolean).pop() || path;
}

export function createBatchExportItem(template: ApiReportTemplateDto): BatchExportItem {
  return {
    name: template.displayName || fileNameFromPath(template.templatePath),
    templatePath: template.templatePath,
    isEnabled: true,
    showSeal: template.withSealDefault ?? true,
    reportType: template.reportType || "ExportDocument",
  };
}

export function resolveBatchExportItems(
  configuredItems: BatchExportItem[],
  templates: ApiReportTemplateDto[],
): BatchExportItem[] {
  if (configuredItems.length > 0) {
    return configuredItems;
  }

  return templates.map(createBatchExportItem);
}
