import type { ReportGridBlock } from "./reportDesignerSchema.ts";
import { getGridCellLocations, resizeGridStructure } from "./reportDesignerGridStructure.ts";
import { REPORT_GRID_MAX_COLUMNS, REPORT_GRID_MAX_ROWS } from "./reportDesignerGridPlacement.ts";
import { updateGridCells } from "./reportDesignerGridMutations.ts";
import { parseTabularClipboard } from "../../ui/tabularClipboard.ts";

export const MAX_GRID_PASTE_CHARACTERS = 256 * 1024;
const MAX_PASTE_CELLS = 10_000;

/** Excel-style TSV: quoted cells may contain tabs, newlines and doubled quotes. */
export function parseGridText(text: string): string[][] {
  if (!text || text.length > MAX_GRID_PASTE_CHARACTERS) throw new Error("粘贴内容为空或文字过长，请分批填入。");
  return parseTabularClipboard(text, { characters: MAX_GRID_PASTE_CHARACTERS, rows: REPORT_GRID_MAX_ROWS, columns: REPORT_GRID_MAX_COLUMNS, cells: MAX_PASTE_CELLS, rectangular: true });
}

export function importGridText(block: ReportGridBlock, cellId: string, text: string): { block: ReportGridBlock; notice: string | null } {
  try {
    const values = parseGridText(text);
    const originalLocations = getGridCellLocations(block);
    const anchor = originalLocations.find(location => location.cell.id === cellId);
    if (!anchor) throw new Error("请先选择填入文字的起始格。");
    const rows = Math.max(block.rows.length, anchor.rowIndex + values.length);
    const columns = Math.max(block.columns.length, anchor.columnIndex + values[0].length);
    if (rows > REPORT_GRID_MAX_ROWS || columns > REPORT_GRID_MAX_COLUMNS) throw new Error("从当前格填入会超出表格行列上限。");
    const resized = resizeGridStructure(block, rows, columns);
    const locations = resized === block ? originalLocations : getGridCellLocations(resized);
    const bottom = anchor.rowIndex + values.length, right = anchor.columnIndex + values[0].length;
    const indexed = new Map<number, typeof locations[number]>();
    for (const location of locations) {
      for (let row = Math.max(anchor.rowIndex, location.rowIndex); row < Math.min(bottom, location.rowIndex + location.rowSpan); row++) {
        for (let column = Math.max(anchor.columnIndex, location.columnIndex); column < Math.min(right, location.columnIndex + location.colSpan); column++) indexed.set(row * columns + column, location);
      }
    }
    const updates = new Map<string, string>();
    values.forEach((row, rowOffset) => row.forEach((text, columnOffset) => {
      const rowIndex = anchor.rowIndex + rowOffset, columnIndex = anchor.columnIndex + columnOffset;
      const location = indexed.get(rowIndex * columns + columnIndex);
      if (!location) throw new Error("表格结构不完整，请先修正表格。");
      if (location.rowIndex !== rowIndex || location.columnIndex !== columnIndex) {
        if (text) throw new Error("多份文字落在同一个合并格中，请先拆分或保留一份内容。");
        return;
      }
      if (location.cell.contentKind !== "Text" || location.cell.diagonalHeader) throw new Error("目标区域含业务字段、勾选组或斜线表头，请先选择普通文本区域。");
      updates.set(location.cell.id, text);
    }));
    return { block: updateGridCells(resized, new Set(updates.keys()), cell => cell.text === updates.get(cell.id)
      ? cell : { ...cell, text: updates.get(cell.id)! }), notice: null };
  } catch (error) { return { block, notice: error instanceof Error ? error.message : "无法填入表格文字。" }; }
}
