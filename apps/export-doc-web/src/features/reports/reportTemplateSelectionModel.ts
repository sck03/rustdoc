import type { AppSettings } from "../../api/index.ts";

export type ReportTemplatePath = { templatePath: string };

export function fileNameFromTemplatePath(path: string) {
  return path.split(/[\\/]/).filter(Boolean).pop() || path;
}

export function normalizeTemplatePath(path: string) {
  return path
    .trim()
    .replace(/^file:[/\\]*/i, "")
    .replace(/\\/g, "/")
    .replace(/\/+/g, "/")
    .replace(/^\/+/, "");
}

export function templatePathsMatch(left: string, right: string) {
  const leftPath = normalizeTemplatePath(left);
  const rightPath = normalizeTemplatePath(right);
  return Boolean(leftPath && rightPath) && (
    leftPath === rightPath ||
    leftPath.endsWith(`/${rightPath}`) ||
    rightPath.endsWith(`/${leftPath}`)
  );
}

export function resolveReportTemplatePath({
  templates,
  currentPath,
  configuredPath,
  fallbackFileName,
}: {
  templates: ReportTemplatePath[];
  currentPath: string;
  configuredPath: string;
  fallbackFileName: string;
}) {
  const current = templates.find((template) => templatePathsMatch(template.templatePath, currentPath));
  if (current) return current.templatePath;

  const configured = templates.find((template) => templatePathsMatch(template.templatePath, configuredPath));
  if (configured) return configured.templatePath;

  return (
    templates.find((template) => fileNameFromTemplatePath(template.templatePath) === fallbackFileName) ??
    templates[0]
  )?.templatePath ?? "";
}

export function readDefaultReportTemplatePath(
  settings: AppSettings | undefined,
  reportType: "ExportDocument" | "PaymentVoucher",
) {
  const defaults = settings?.reportTemplateDefaults;
  return (reportType === "ExportDocument" ? defaults?.exportDocumentTemplatePath : defaults?.paymentVoucherTemplatePath)?.trim() ?? "";
}
