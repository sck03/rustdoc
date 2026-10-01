import type { ApiReportTemplateDto, AppSettings } from "../../api/index.ts";
import { fileNameFromTemplatePath, normalizeTemplatePath } from "../reports/reportTemplateSelectionModel.ts";

function templateIdentity(path: string) {
  return normalizeTemplatePath(path).replace(/^Templates\//, "builtin:");
}

/** Configured order/names apply only to the exact template returned by the authorized catalog. */
export function buildPaymentTemplateViews(templates: ApiReportTemplateDto[], settings?: AppSettings) {
  const available = new Map(templates.map(template => [templateIdentity(template.templatePath), template]));
  const views: { templatePath: string; displayName: string }[] = [];
  for (const item of settings?.paymentTemplates ?? []) {
    if (item.reportType !== "PaymentVoucher") continue;
    const path = templateIdentity(item.templatePath);
    const template = available.get(path);
    if (!template) continue;
    available.delete(path);
    if (item.isEnabled) views.push({ templatePath: template.templatePath,
      displayName: item.name.trim() || template.displayName || fileNameFromTemplatePath(template.templatePath) });
  }
  for (const template of available.values()) views.push({ templatePath: template.templatePath,
    displayName: template.displayName || fileNameFromTemplatePath(template.templatePath) });
  return views;
}
