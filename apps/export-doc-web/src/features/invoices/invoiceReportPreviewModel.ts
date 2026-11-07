import type { ApiInvoiceDocumentPackagePreviewResponse, ApiReportTemplateDto, AppSettings, BatchExportItem } from "../../api/index.ts";
import { fileNameFromPath, resolveBatchExportItems } from "../reports/reportExportDefaultsModel.ts";
import { templatePathsMatch } from "../reports/reportTemplateSelectionModel.ts";

export { fileNameFromPath };

export type PackageTemplateView = { template: ApiReportTemplateDto; displayName: string; withSealDefault: boolean; initiallySelected: boolean };
export type BatchExportItemSetting = BatchExportItem;
export type BatchExportConfigDraft = { fileNamePattern: string; folderPattern: string; mergePdf: boolean; zipAfterExport: boolean; items: BatchExportItemSetting[] };
export const DEFAULT_DOCUMENT_EMAIL_SUBJECT_TEMPLATE = "Export Documents for Invoice {InvoiceNo}";
export const DEFAULT_DOCUMENT_EMAIL_BODY_TEMPLATE = "Dear Customer,\r\n\r\nPlease find the attached export documents.\r\n\r\nBest regards,";
export const DEFAULT_BATCH_EXPORT_FILE_NAME_PATTERN = "{InvoiceNo}_{DocType}";
export const DEFAULT_BATCH_EXPORT_FOLDER_PATTERN = "{InvoiceNo}_Docs_{Date}";

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

export function buildBatchExportConfigDraft(
  settings: AppSettings | undefined,
  templates: ApiReportTemplateDto[],
): BatchExportConfigDraft {
  const batchExport = settings?.batchExport;
  const items = resolveBatchExportItems(batchExport?.items.filter(item => item.reportType === "ExportDocument") ?? [], templates);

  return {
    fileNamePattern: batchExport?.outputFileNamePattern.trim() ? batchExport.outputFileNamePattern : DEFAULT_BATCH_EXPORT_FILE_NAME_PATTERN,
    folderPattern: batchExport?.outputFolderPattern.trim() ? batchExport.outputFolderPattern : DEFAULT_BATCH_EXPORT_FOLDER_PATTERN,
    mergePdf: batchExport?.mergePdf ?? true,
    zipAfterExport: batchExport?.zipAfterExport ?? true,
    items,
  };
}

export function buildDocumentEmailSubject(
  settings: AppSettings | undefined,
  invoiceNo: string | undefined,
  customerName: string | undefined,
  dateText: string,
) {
  return applyDocumentEmailTemplate(
    settings?.email.documentEmailSubjectTemplate.trim() ? settings.email.documentEmailSubjectTemplate : DEFAULT_DOCUMENT_EMAIL_SUBJECT_TEMPLATE,
    invoiceNo,
    customerName,
    dateText,
  );
}

export function buildDocumentEmailBody(
  settings: AppSettings | undefined,
  invoiceNo: string | undefined,
  customerName: string | undefined,
  dateText: string,
) {
  return applyDocumentEmailTemplate(
    settings?.email.documentEmailBodyTemplate.trim() ? settings.email.documentEmailBodyTemplate : DEFAULT_DOCUMENT_EMAIL_BODY_TEMPLATE,
    invoiceNo,
    customerName,
    dateText,
  );
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
  const usedTemplatePaths = new Set<string>();
  const views: PackageTemplateView[] = [];

  for (const item of effectiveConfiguredItems) {
    const template = findTemplateForBatchExportItem(item, templates, usedTemplatePaths);
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

  const hasConfiguredItems = views.length > 0;
  for (const template of templates) {
    if (usedTemplatePaths.has(template.templatePath)) {
      continue;
    }

    views.push({
      template,
      displayName: template.displayName || fileNameFromPath(template.templatePath),
      withSealDefault: template.withSealDefault ?? true,
      initiallySelected: !hasConfiguredItems,
    });
  }

  return views;
}

export function findTemplateForBatchExportItem(
  item: BatchExportItemSetting,
  templates: ApiReportTemplateDto[],
  usedTemplatePaths: Set<string>,
) {
  const pathMatch = templates.find(
    (template) =>
      !usedTemplatePaths.has(template.templatePath) &&
      templatePathsMatch(item.templatePath, template.templatePath),
  );
  if (pathMatch) {
    return pathMatch;
  }

  const itemFileName = fileNameFromPath(item.templatePath);
  if (!itemFileName) {
    return undefined;
  }

  const fileNameMatches = templates.filter(
    (template) =>
      !usedTemplatePaths.has(template.templatePath) &&
      fileNameFromPath(template.templatePath) === itemFileName,
  );

  return fileNameMatches.length === 1 ? fileNameMatches[0] : undefined;
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
