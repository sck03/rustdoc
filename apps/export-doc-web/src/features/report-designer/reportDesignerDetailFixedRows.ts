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
  const cellBorder = (border: ReportBorderStyle) => row.border ? { ...border, top: false, bottom: false } : border;
  const cells = new Map(row.cells.map(cell => [cell.columnId, cell]));
  const cellContent = (id: string) => {
    const cell = cells.get(id);
    return cell?.contentKind === "Field" ? `${cell.fieldFormat === "Currency" ? render.field("Invoice.Currency") : ""}${render.field(cell.fieldPath)}${render.text(cell.suffix ?? "")}` : cell?.contentKind === "Text" ? render.text(cell.text) : "";
  };
  const values = block.columns.slice(span).map(column => {
    const content = cellContent(column.id);
    return `<td style="${render.style(row.style, cellBorder(column.border ?? block.border), row.style.align ?? column.align)}">${content}</td>`;
  }).join("");
  const label = [render.text(row.label), ...block.columns.slice(0, span).map(column => cellContent(column.id))].filter(Boolean).join("<br>");
  return `<tr class="edm-detail-summary-row"${row.border ? ` style="${render.style({}, row.border, "Left")}"` : ""}><td colspan="${span}" style="${render.style(row.style, cellBorder(detailSpanBorder(block, 0, span)), row.style.align ?? "Right")}">${label}</td>${values}</tr>`;
}

export function detailSpanBorder(block: ReportDetailTableBlock, start: number, span: number): ReportBorderStyle {
  const first = block.columns[start]?.border ?? block.border;
  const last = block.columns[start + span - 1]?.border ?? block.border;
  return { ...first, right: last.right };
}
