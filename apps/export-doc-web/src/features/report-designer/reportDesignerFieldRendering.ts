import { isReportDesignerFieldPath } from "./reportDesignerSchemaValidation.ts";

export const shippingMarksFieldPath = "Invoice.ShippingMarks";
export const shippingMarksPreviewText = "唛头（文字 / 图片自动）";

export function isShippingMarksField(fieldPath: string) {
  return fieldPath.trim() === shippingMarksFieldPath;
}

/** Fields and Flow cells share the same fallback and content box contract. */
export function renderReportField(fieldPath: string, fallbackText?: string, imageHeightMm?: number) {
  const normalized = fieldPath.trim();
  const fallback = escapeHtml(fallbackText ?? "");
  if (!isReportDesignerFieldPath(normalized)) return fallback;
  const expression = `{{ ${normalized} }}`;
  const value = fallbackText ? `{{ if ${normalized} }}${expression}{{ else }}${fallback}{{ end }}` : expression;
  if (!isShippingMarksField(normalized)) return value;
  const height = imageHeightMm === undefined ? "" : `;--edm-field-image-height:${Math.max(1, imageHeightMm)}mm`;
  return `<span class="edm-report-field-content" style="display:block;max-width:100%;white-space:pre-wrap;overflow-wrap:anywhere${height}">${value}</span>`;
}

function escapeHtml(value: string) {
  return value.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}
