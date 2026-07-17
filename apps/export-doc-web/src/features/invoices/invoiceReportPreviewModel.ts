import type { ApiInvoiceDocumentPackagePreviewResponse, ApiReportTemplateDto, ApiSettingsResponse } from "../../api/index.ts";

export type PackageTemplateView = { template: ApiReportTemplateDto; displayName: string; withSealDefault: boolean; initiallySelected: boolean };
export type BatchExportItemSetting = { name: string; templatePath: string; isEnabled: boolean; showSeal: boolean; reportType: string };
export type BatchExportConfigDraft = { fileNamePattern: string; folderPattern: string; mergePdf: boolean; zipAfterExport: boolean; items: BatchExportItemSetting[] };
export const DEFAULT_DOCUMENT_EMAIL_SUBJECT_TEMPLATE = "Export Documents for Invoice {InvoiceNo}";
export const DEFAULT_DOCUMENT_EMAIL_BODY_TEMPLATE = "Dear Customer,\r\n\r\nPlease find the attached export documents.\r\n\r\nBest regards,";
export const DEFAULT_BATCH_EXPORT_FILE_NAME_PATTERN = "{InvoiceNo}_{DocType}";
export const DEFAULT_BATCH_EXPORT_FOLDER_PATTERN = "{InvoiceNo}_Docs_{Date}";

export function fileNameFromPath(path: string) {
  return path.split(/[\\/]/).filter(Boolean).pop() || path;
}

export function buildDocumentPackagePrintHtml(packagePreview: ApiInvoiceDocumentPackagePreviewResponse) {
  const headHtml = packagePreview.items
    .map((item) => extractHtmlHead(item.html))
    .filter((head) => head.trim().length > 0)
    .join("\n");
  const bodyHtml = packagePreview.items
    .map((item) => `<section class="document-package-print-item">${extractHtmlBody(item.html)}</section>`)
    .join("\n");

  return `<!doctype html>
<html>
<head>
<meta charset="utf-8">
<title>单据包打印预览</title>
${headHtml}
<style>
.document-package-print-item:not(:last-child) {
  break-after: page;
  page-break-after: always;
}
</style>
</head>
<body>
${bodyHtml}
</body>
</html>`;
}

export function extractHtmlHead(html: string) {
  return html.match(/<head[^>]*>([\s\S]*?)<\/head>/i)?.[1] ?? "";
}

export function extractHtmlBody(html: string) {
  return html.match(/<body[^>]*>([\s\S]*?)<\/body>/i)?.[1] ?? html;
}

export function createEmptyBatchExportConfigDraft(): BatchExportConfigDraft {
  return {
    fileNamePattern: DEFAULT_BATCH_EXPORT_FILE_NAME_PATTERN,
    folderPattern: DEFAULT_BATCH_EXPORT_FOLDER_PATTERN,
    mergePdf: true,
    zipAfterExport: true,
    items: [],
  };
}

export function buildBatchExportConfigDraft(
  settings: Record<string, unknown> | undefined,
  templates: ApiReportTemplateDto[],
): BatchExportConfigDraft {
  const batchExport = readBatchExportRecord(settings);
  const configuredItems = readBatchExportItems(settings);
  const items = configuredItems.length > 0
    ? configuredItems
    : templates.map((template) => ({
        name: template.displayName || fileNameFromPath(template.templatePath),
        templatePath: template.templatePath,
        isEnabled: true,
        showSeal: template.withSealDefault,
        reportType: template.reportType || "ExportDocument",
      }));

  return {
    fileNamePattern: readBatchExportPattern(batchExport, "outputFileNamePattern", "OutputFileNamePattern", DEFAULT_BATCH_EXPORT_FILE_NAME_PATTERN),
    folderPattern: readBatchExportPattern(batchExport, "outputFolderPattern", "OutputFolderPattern", DEFAULT_BATCH_EXPORT_FOLDER_PATTERN),
    mergePdf: readBatchExportBoolean(batchExport, "mergePdf", "MergePdf", true),
    zipAfterExport: readBatchExportBoolean(batchExport, "zipAfterExport", "ZipAfterExport", true),
    items,
  };
}

export function buildSettingsWithBatchExportConfig(
  settings: Record<string, unknown>,
  draft: BatchExportConfigDraft,
) {
  const nextSettings = cloneSettings(settings);
  const existingBatchExport = readRecordValue(nextSettings, "batchExport", "BatchExport");
  const nextBatchExport = isRecord(existingBatchExport) ? { ...existingBatchExport } : {};
  nextBatchExport.items = draft.items.map(toBatchExportItemRecord);
  nextBatchExport.outputFileNamePattern = normalizeBatchExportPattern(draft.fileNamePattern, DEFAULT_BATCH_EXPORT_FILE_NAME_PATTERN);
  nextBatchExport.outputFolderPattern = normalizeBatchExportPattern(draft.folderPattern, DEFAULT_BATCH_EXPORT_FOLDER_PATTERN);
  nextBatchExport.mergePdf = draft.mergePdf;
  nextBatchExport.zipAfterExport = draft.zipAfterExport;

  nextSettings.batchExport = nextBatchExport;
  if (Object.prototype.hasOwnProperty.call(nextSettings, "BatchExport")) {
    nextSettings.BatchExport = nextBatchExport;
  }

  return nextSettings;
}

export function toBatchExportItemRecord(item: BatchExportItemSetting) {
  return {
    name: item.name,
    templatePath: item.templatePath,
    isEnabled: item.isEnabled,
    showSeal: item.showSeal,
    reportType: item.reportType || "ExportDocument",
  };
}

export function normalizeBatchExportPattern(value: string, fallback: string) {
  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : fallback;
}

export function readBatchExportPattern(
  batchExport: Record<string, unknown> | null,
  camelName: string,
  pascalName: string,
  fallback: string,
) {
  const value = batchExport ? readRecordValue(batchExport, camelName, pascalName) : undefined;
  return typeof value === "string" && value.trim() ? value : fallback;
}

export function readBatchExportBoolean(
  batchExport: Record<string, unknown> | null,
  camelName: string,
  pascalName: string,
  fallback: boolean,
) {
  const value = batchExport ? readRecordValue(batchExport, camelName, pascalName) : undefined;
  return typeof value === "boolean" ? value : fallback;
}

export function cloneSettings(settings: Record<string, unknown>) {
  return JSON.parse(JSON.stringify(settings)) as Record<string, unknown>;
}

export function buildDocumentEmailSubject(
  settings: Record<string, unknown> | undefined,
  invoiceNo: string | undefined,
  customerName: string | undefined,
  dateText: string,
) {
  return applyDocumentEmailTemplate(
    readEmailTemplate(settings, "documentEmailSubjectTemplate", "DocumentEmailSubjectTemplate") || DEFAULT_DOCUMENT_EMAIL_SUBJECT_TEMPLATE,
    invoiceNo,
    customerName,
    dateText,
  );
}

export function buildDocumentEmailBody(
  settings: Record<string, unknown> | undefined,
  invoiceNo: string | undefined,
  customerName: string | undefined,
  dateText: string,
) {
  return applyDocumentEmailTemplate(
    readEmailTemplate(settings, "documentEmailBodyTemplate", "DocumentEmailBodyTemplate") || DEFAULT_DOCUMENT_EMAIL_BODY_TEMPLATE,
    invoiceNo,
    customerName,
    dateText,
  );
}

export function readEmailTemplate(settings: Record<string, unknown> | undefined, ...names: string[]) {
  const email = settings ? readRecordValue(settings, "email", "Email") : undefined;
  if (!isRecord(email)) {
    return "";
  }

  const value = readRecordValue(email, ...names);
  return typeof value === "string" && value.trim() ? value : "";
}

export function applyDocumentEmailTemplate(
  template: string,
  invoiceNo: string | undefined,
  customerName: string | undefined,
  dateText: string,
) {
  return replaceBatchExportPlaceholder(
    replaceBatchExportPlaceholder(
      replaceBatchExportPlaceholder(
        template,
        "InvoiceNo",
        sanitizeFileNamePart(invoiceNo ?? ""),
      ),
      "Customer",
      sanitizeFileNamePart(customerName ?? ""),
    ),
    "Date",
    dateText,
  );
}

export function buildPackageTemplateViewsFromItems(
  templates: ApiReportTemplateDto[],
  configuredItems: BatchExportItemSetting[],
): PackageTemplateView[] {
  const effectiveConfiguredItems = configuredItems.filter((item) => item.templatePath.length > 0);
  const hasConfiguredItems = effectiveConfiguredItems.length > 0;
  const usedTemplatePaths = new Set<string>();
  const views: PackageTemplateView[] = [];

  for (const item of effectiveConfiguredItems) {
    const template = findTemplateForBatchExportItem(item, templates, usedTemplatePaths) ?? createConfiguredTemplate(item);
    if (!template) {
      continue;
    }

    usedTemplatePaths.add(template.templatePath);
    views.push({
      template,
      displayName: item.name || template.displayName || fileNameFromPath(template.templatePath),
      withSealDefault: item.showSeal,
      initiallySelected: item.isEnabled,
    });
  }

  for (const template of templates) {
    if (usedTemplatePaths.has(template.templatePath)) {
      continue;
    }

    views.push({
      template,
      displayName: template.displayName || fileNameFromPath(template.templatePath),
      withSealDefault: template.withSealDefault,
      initiallySelected: !hasConfiguredItems,
    });
  }

  return views;
}

export function createConfiguredTemplate(item: BatchExportItemSetting): ApiReportTemplateDto | null {
  const templatePath = item.templatePath.trim();
  if (!templatePath) {
    return null;
  }

  return {
    reportType: item.reportType || "ExportDocument",
    displayName: item.name || fileNameFromPath(templatePath),
    templatePath,
    withSealDefault: item.showSeal,
  };
}

export function buildTemplateSelectOptions(templates: ApiReportTemplateDto[], items: BatchExportItemSetting[]) {
  const options: Array<{ value: string; label: string }> = [];
  const usedPaths = new Set<string>();

  function addOption(path: string, label: string) {
    const normalized = normalizePathForMatch(path);
    if (!path || usedPaths.has(normalized)) {
      return;
    }

    usedPaths.add(normalized);
    options.push({
      value: path,
      label: label || fileNameFromPath(path),
    });
  }

  for (const template of templates) {
    addOption(template.templatePath, template.displayName || fileNameFromPath(template.templatePath));
  }

  for (const item of items) {
    addOption(item.templatePath, item.name || fileNameFromPath(item.templatePath));
  }

  if (options.length === 0) {
    options.push({ value: "", label: "手工填写模板路径" });
  }

  return options;
}

export function findFirstUnusedTemplate(templates: ApiReportTemplateDto[], items: BatchExportItemSetting[]) {
  return templates.find(
    (template) => !items.some((item) => pathsReferToSameTemplate(item.templatePath, template.templatePath)),
  ) ?? templates[0];
}

export function readTemplateDisplayName(templatePath: string, templates: ApiReportTemplateDto[]) {
  const template = templates.find((item) => pathsReferToSameTemplate(item.templatePath, templatePath));
  return template?.displayName || fileNameFromPath(templatePath) || "新单证";
}

export function readBatchExportItems(settings?: Record<string, unknown>): BatchExportItemSetting[] {
  const batchExport = readBatchExportRecord(settings);
  if (!batchExport) {
    return [];
  }

  const rawItems = readRecordValue(batchExport, "items", "Items");
  if (!Array.isArray(rawItems)) {
    return [];
  }

  const items: BatchExportItemSetting[] = [];
  for (const rawItem of rawItems) {
    if (!isRecord(rawItem)) {
      continue;
    }

    const reportType = readString(rawItem, "reportType", "ReportType");
    if (!isExportBatchReportType(reportType)) {
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

export function findTemplateForBatchExportItem(
  item: BatchExportItemSetting,
  templates: ApiReportTemplateDto[],
  usedTemplatePaths: Set<string>,
) {
  const pathMatch = templates.find(
    (template) =>
      !usedTemplatePaths.has(template.templatePath) &&
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
      !usedTemplatePaths.has(template.templatePath) &&
      fileNameFromPath(template.templatePath).toLowerCase() === itemFileName,
  );

  return fileNameMatches.length === 1 ? fileNameMatches[0] : undefined;
}

export function isExportBatchReportType(reportType: string) {
  const normalized = reportType.trim().toLowerCase();
  return (
    normalized.length === 0 ||
    normalized === "exportdocument" ||
    normalized === "commercialinvoice" ||
    normalized === "packinglist" ||
    normalized === "generic"
  );
}

export function pathsReferToSameTemplate(left: string, right: string) {
  const leftPath = normalizePathForMatch(left);
  const rightPath = normalizePathForMatch(right);
  if (!leftPath || !rightPath) {
    return false;
  }

  return leftPath === rightPath || leftPath.endsWith(`/${rightPath}`) || rightPath.endsWith(`/${leftPath}`);
}

export function normalizePathForMatch(path: string) {
  return path
    .trim()
    .replace(/^file:[/\\]*/i, "")
    .replace(/\\/g, "/")
    .replace(/\/+/g, "/")
    .replace(/^\/+/, "")
    .toLowerCase();
}

export function buildDocumentPackageDefaultFileName(
  draft: BatchExportConfigDraft,
  invoiceNo: string | undefined,
  customerName: string | undefined,
  invoiceId: number,
) {
  const fallback = `invoice-${invoiceId}-documents`;
  const dateText = formatDateForBatchExport(new Date());
  const fileName = applyBatchExportPattern(draft.folderPattern, invoiceNo, customerName, "Docs", dateText) || fallback;
  return `${fileName}.zip`;
}

export function buildInvoiceBookingSheetDefaultFileName(invoiceNo: string | undefined, invoiceId: number) {
  const invoiceName = sanitizeFileNamePart(invoiceNo?.trim() || "");
  return `${invoiceName || `invoice-${invoiceId}`}_订舱托单.xlsx`;
}

export function applyBatchExportPattern(
  pattern: string,
  invoiceNo: string | undefined,
  customerName: string | undefined,
  documentName: string,
  dateText: string,
) {
  const normalizedPattern = pattern.trim() || DEFAULT_BATCH_EXPORT_FOLDER_PATTERN;
  return replaceBatchExportPlaceholder(
    replaceBatchExportPlaceholder(
      replaceBatchExportPlaceholder(
        replaceBatchExportPlaceholder(
          sanitizeFileNamePart(normalizedPattern),
          "InvoiceNo",
          sanitizeFileNamePart(invoiceNo ?? ""),
        ),
        "Customer",
        sanitizeFileNamePart(customerName ?? ""),
      ),
      "DocType",
      sanitizeFileNamePart(documentName),
    ),
    "Date",
    dateText,
  )
    .replace(/\s+/g, " ")
    .trim();
}

export function replaceBatchExportPlaceholder(value: string, placeholder: string, replacement: string) {
  return value.replace(new RegExp(`\\{${placeholder}\\}`, "gi"), replacement);
}

export function formatDateForBatchExport(value: Date) {
  const year = value.getFullYear();
  const month = `${value.getMonth() + 1}`.padStart(2, "0");
  const day = `${value.getDate()}`.padStart(2, "0");
  return `${year}${month}${day}`;
}

export function sanitizeFileNamePart(value: string) {
  return value.replace(/[<>:"/\\|?*\u0000-\u001F]/g, "_").replace(/^_+|_+$/g, "");
}

export function readBatchExportRecord(settings?: Record<string, unknown>) {
  const batchExport = settings ? readRecordValue(settings, "batchExport", "BatchExport") : undefined;
  return isRecord(batchExport) ? batchExport : null;
}

export function readRecordValue(record: Record<string, unknown>, ...names: string[]) {
  for (const name of names) {
    if (Object.prototype.hasOwnProperty.call(record, name)) {
      return record[name];
    }
  }

  return undefined;
}

export function readString(record: Record<string, unknown>, ...names: string[]) {
  const value = readRecordValue(record, ...names);
  return typeof value === "string" ? value.trim() : "";
}

export function readBoolean(record: Record<string, unknown>, fallback: boolean, ...names: string[]) {
  const value = readRecordValue(record, ...names);
  return typeof value === "boolean" ? value : fallback;
}

export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

