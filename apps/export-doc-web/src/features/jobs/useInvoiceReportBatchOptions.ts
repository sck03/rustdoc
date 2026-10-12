import { useState } from "react";
import type { ApiReportTemplateDto } from "../../api/index.ts";
import { ApiInvoiceReportBatchLimitsContractDefaults } from "../../api/generated/exportDocManagerApi.ts";
import { resolveReportTemplatePath } from "../reports/reportTemplateSelectionModel.ts";

export const invoiceReportBatchLimits = ApiInvoiceReportBatchLimitsContractDefaults;

export function invoiceReportBatchIssue(invoiceCount: number, paths: string[], templates: ApiReportTemplateDto[]) {
  const limits = invoiceReportBatchLimits;
  if (!invoiceCount || invoiceCount > limits.maxInvoices) return `请选择 1–${limits.maxInvoices} 张发票。`;
  if (!paths.length || paths.length > limits.maxTemplates) return `请选择 1–${limits.maxTemplates} 个报表模板。`;
  if (new Set(paths).size !== paths.length || paths.some(path => !templates.some(template => template.templatePath === path))) return "部分已选模板已不可用，请重新选择。";
  if (invoiceCount * paths.length > limits.maxReports) return `预计生成 ${invoiceCount * paths.length} 份 PDF，单次最多 ${limits.maxReports} 份，请减少发票或模板数量。`;
  return null;
}

export function useInvoiceReportBatchOptions(templates: ApiReportTemplateDto[], configuredPath: string) {
  const [selected, setTemplatePaths] = useState<string[] | null>(null);
  const [sealOverride, setWithSeal] = useState<boolean | null>(null);
  const fallback = templates.length ? resolveReportTemplatePath({ templates, currentPath: "", configuredPath, fallbackFileName: "invoice_template.dtpl" }) : "";
  // Explicitly clearing the selection stays empty; unavailable choices remain reviewable.
  const templatePaths = selected ?? (fallback ? [fallback] : []);
  const withSeal = sealOverride ?? templates.find(template => template.templatePath === templatePaths[0])?.withSealDefault ?? true;
  return { templatePaths, withSeal, setTemplatePaths, setWithSeal };
}
