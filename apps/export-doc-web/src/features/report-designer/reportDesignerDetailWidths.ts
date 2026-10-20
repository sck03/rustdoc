import type { ReportDetailTableBlock } from "./reportDesignerSchema.ts";

/** Matches Rust: the side band has a physical width; columns share the remaining space. */
export function detailColumnWidths(block: ReportDetailTableBlock, tableWidthMm: number) {
  const available = Math.max(0, tableWidthMm - (block.sideBand?.widthMm ?? 0));
  const total = block.columns.reduce((sum, column) => sum + column.widthMm, 0);
  return block.columns.map(column => total > 0 ? column.widthMm / total * available : 0);
}

/** Transfer space to/from the adjacent column, preserving the table and side band. */
export function setDetailColumnWidth(block: ReportDetailTableBlock, columnId: string, widthMm: number, tableWidthMm: number): ReportDetailTableBlock {
  const index = block.columns.findIndex(column => column.id === columnId);
  if (index < 0 || block.columns.length < 2 || !Number.isFinite(widthMm)) return block;
  const widths = detailColumnWidths(block, tableWidthMm);
  if (widths.some(width => !Number.isFinite(width) || width <= 0)) return block;
  const adjacent = index === widths.length - 1 ? index - 1 : index + 1;
  const pair = widths[index] + widths[adjacent];
  // Widths in the AST are weights with an 8 mm minimum. Preserve legal weights
  // even for large imported tables whose printed columns are narrower than 8 mm.
  const minimum = Math.min(8, ...widths);
  const selected = Math.max(minimum, Math.min(pair - minimum, widthMm));
  if (Math.abs(selected - widths[index]) < 0.001) return block;
  widths[index] = selected;
  widths[adjacent] = pair - selected;
  const scale = Math.max(1, 8 / Math.min(...widths));
  return { ...block, columns: block.columns.map((column, i) => ({ ...column, widthMm: widths[i] * scale })) };
}
