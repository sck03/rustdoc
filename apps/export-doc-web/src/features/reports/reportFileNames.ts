export function buildReportPdfDefaultFileName({
  templatePath,
  displayName,
  fallbackTitle,
  documentNumber,
}: {
  templatePath?: string;
  displayName?: string;
  fallbackTitle: string;
  documentNumber?: string;
}) {
  const title =
    sanitizeFileNamePart(displayName ?? "") ||
    sanitizeFileNamePart(readTemplateTitleFromPath(templatePath ?? "")) ||
    sanitizeFileNamePart(fallbackTitle) ||
    "report";
  const number = sanitizeFileNamePart(documentNumber ?? "") || "document";
  return `${title}_${number}.pdf`;
}

function readTemplateTitleFromPath(path: string) {
  const fileName = path.split(/[\\/]/).filter(Boolean).pop() ?? "";
  const withoutExtension = fileName.replace(/\.[^.]+$/, "");
  const withoutTemplateSuffix = withoutExtension.replace(/_template$/i, "");
  return withoutTemplateSuffix.replace(/[_-]+/g, " ").trim();
}

function sanitizeFileNamePart(value: string) {
  return value
    .replace(/[<>:"/\\|?*\x00-\x1f]/g, " ")
    .replace(/\s+/g, " ")
    .trim()
    .replace(/[. ]+$/g, "");
}
