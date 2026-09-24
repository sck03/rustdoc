import type { ReportDetailTableBlock, ReportDetailTableSummaryRow, ReportBorderStyle, ReportTextStyle } from "./reportDesignerSchema.ts";

type Renderers = {
  style: (style: ReportTextStyle, border: ReportBorderStyle, align: "Left" | "Center" | "Right") => string;
  field: (path: string) => string;
  text: (value: string) => string;
};

/** Introductory notes and totals share fixed cells outside the item loop. */
export function renderDetailFixedRow(block: ReportDetailTableBlock, row: ReportDetailTableSummaryRow | undefined, render: Renderers) {
  if (!row) return "";
  const span = Math.min(block.columns.length, Math.max(1, Math.floor(row.labelColumnSpan)));
  const cells = new Map(row.cells.map(cell => [cell.columnId, cell]));
  const cellContent = (id: string) => {
    const cell = cells.get(id);
    return cell?.contentKind === "Field" ? render.field(cell.fieldPath) : cell?.contentKind === "Text" ? render.text(cell.text) : "";
  };
  const values = block.columns.slice(span).map(column => {
    const content = cellContent(column.id);
    return `<td style="${render.style(row.style, column.border ?? block.border, column.align)}">${content}</td>`;
  }).join("");
  const label = [render.text(row.label), ...block.columns.slice(0, span).map(column => cellContent(column.id))].filter(Boolean).join("<br>");
  return `<tr class="edm-detail-summary-row"><td colspan="${span}" style="${render.style(row.style, block.border, "Right")}">${label}</td>${values}</tr>`;
}
