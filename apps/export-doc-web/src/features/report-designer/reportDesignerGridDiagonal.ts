import type { ReportGridDiagonalHeader } from "./reportDesignerSchema.ts";

function escapeHtml(value: string) {
  return value.replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll('"', "&quot;");
}

export function renderGridDiagonalHeader(header: ReportGridDiagonalHeader) {
  return `<div class="edm-report-grid-diagonal" aria-hidden="true"><span class="edm-report-grid-diagonal-upper-left">${escapeHtml(header.upperLeftText)}</span><span class="edm-report-grid-diagonal-lower-right">${escapeHtml(header.lowerRightText)}</span></div>`;
}
