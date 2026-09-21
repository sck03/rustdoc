import type { ReportGridBlock } from "./reportDesignerSchema.ts";
import type { ReportDesignerSchemaIssue } from "./reportDesignerSchemaValues.ts";

export type ReportGridCellPlacement = {
  cell: ReportGridBlock["rows"][number]["cells"][number];
  rowIndex: number;
  columnIndex: number;
  rowSpan: number;
  colSpan: number;
};

/**
 * Keep V3 HTML validation on the same table occupancy rules as the Rust
 * report layout.  A grid row stores only standalone cells; rows covered by a
 * rowSpan are intentionally empty and must be accepted when every column in
 * that row is occupied by an earlier merged cell.
 */
export function reportGridCellPlacements(
  block: ReportGridBlock,
  path: string,
  issues: ReportDesignerSchemaIssue[],
): ReportGridCellPlacement[] {
  const rows = block.rows.length;
  const columns = block.columns.length;
  if (rows === 0 || rows > 1000 || columns === 0 || columns > 100) {
    issues.push({ severity: "error", path, message: "普通表格行列数量无效。" });
    return [];
  }

  const occupied = Array.from({ length: rows }, () => Array<boolean>(columns).fill(false));
  const placements: ReportGridCellPlacement[] = [];
  for (const [rowIndex, row] of block.rows.entries()) {
    const rowPath = `${path}.rows[${rowIndex}]`;
    let columnIndex = 0;
    for (const [cellIndex, cell] of row.cells.entries()) {
      while (columnIndex < columns && occupied[rowIndex][columnIndex]) {
        columnIndex += 1;
      }
      const rowSpan = Math.trunc(cell.rowSpan ?? 1);
      const colSpan = Math.trunc(cell.colSpan ?? 1);
      if (
        rowSpan < 1 ||
        colSpan < 1 ||
        rowSpan > rows - rowIndex ||
        colSpan > columns - columnIndex
      ) {
        issues.push({
          severity: "error",
          path: `${rowPath}.cells[${cellIndex}]`,
          message: `普通表格单元格 ${cell.id} 的合并范围越界。`,
        });
        return placements;
      }

      for (let rowOffset = 0; rowOffset < rowSpan; rowOffset += 1) {
        for (let columnOffset = 0; columnOffset < colSpan; columnOffset += 1) {
          const targetRow = rowIndex + rowOffset;
          const targetColumn = columnIndex + columnOffset;
          if (occupied[targetRow][targetColumn]) {
            issues.push({
              severity: "error",
              path: `${rowPath}.cells[${cellIndex}]`,
              message: `普通表格单元格 ${cell.id} 的合并范围重叠。`,
            });
            return placements;
          }
          occupied[targetRow][targetColumn] = true;
        }
      }

      placements.push({ cell, rowIndex, columnIndex, rowSpan, colSpan });
      columnIndex += colSpan;
    }

    if (row.cells.length === 0 && occupied[rowIndex].some((value) => !value)) {
      issues.push({
        severity: "error",
        path: rowPath,
        message: "普通表格空行必须由跨行合并单元格完整覆盖。",
      });
      return placements;
    }
  }

  return placements;
}
