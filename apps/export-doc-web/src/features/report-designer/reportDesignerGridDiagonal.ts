import type { ReportGridDiagonalHeader } from "./reportDesignerSchema.ts";

function escapeHtml(value: string) {
  return value.replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll('"', "&quot;");
}

export function renderGridDiagonalHeader(header: ReportGridDiagonalHeader) {
  const down = header.direction === "Down";
  return `<div class="edm-report-grid-diagonal" aria-hidden="true"><svg viewBox="0 0 100 100" preserveAspectRatio="none" style="position:absolute;inset:0;width:100%;height:100%"><line x1="0" y1="${down ? 0 : 100}" x2="100" y2="${down ? 100 : 0}" stroke="currentColor" stroke-width="0.7" vector-effect="non-scaling-stroke"/></svg><span class="edm-report-grid-diagonal-upper-left" style="${down ? 'left:auto;right:1mm' : ''}">${escapeHtml(header.upperLeftText)}</span><span class="edm-report-grid-diagonal-lower-right" style="${down ? 'right:auto;left:1mm' : ''}">${escapeHtml(header.lowerRightText)}</span></div>`;
}
