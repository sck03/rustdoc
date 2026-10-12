import { createGridCell, createGridColumns, createGridRow } from "./reportDesignerBlockFactories.ts";
import type { ReportGridBlock, ReportGridCell } from "./reportDesignerSchema.ts";
import { normalizeColumnPercentages } from "./reportDesignerColumnWidths.ts";
import { REPORT_GRID_MAX_COLUMNS, REPORT_GRID_MAX_ROWS, reportGridCellPlacements, type ReportGridCellPlacement } from "./reportDesignerGridPlacement.ts";

export type ReportGridCellLocation = ReportGridCellPlacement;
export type ReportGridAxis = "row" | "column";

export function getGridCellLocations(block: ReportGridBlock): ReportGridCellLocation[] {
  return reportGridCellPlacements(block, "grid", []);
}

export function gridCellAt(locations: ReportGridCellLocation[], row: number, column: number) {
  return locations.find(location => row >= location.rowIndex && row < location.rowIndex + location.rowSpan &&
    column >= location.columnIndex && column < location.columnIndex + location.colSpan) ?? null;
}

export function adjacentGridCell(block: ReportGridBlock, cellId: string, direction: "left" | "right" | "up" | "down", locations = getGridCellLocations(block)) {
  const current = locations.find(location => location.cell.id === cellId);
  if (!current) return null;
  return gridCellAt(locations,
    direction === "up" ? current.rowIndex - 1 : direction === "down" ? current.rowIndex + current.rowSpan : current.rowIndex,
    direction === "left" ? current.columnIndex - 1 : direction === "right" ? current.columnIndex + current.colSpan : current.columnIndex);
}

export function emptyGridCell(): ReportGridCell {
  return { ...createGridCell("Text", "", "", 1, 1, {}), border: undefined };
}

function normalizeWidths(columns: ReportGridBlock["columns"]) {
  const weights = columns.map(column => Math.max(0, column.widthPercent - 1));
  const total = weights.reduce((sum, value) => sum + value, 0);
  return normalizeColumnPercentages(columns.map((column, index) => ({ ...column,
    widthPercent: 1 + (100 - columns.length) * (total ? weights[index] / total : 1 / columns.length) })));
}

/** Rebuild the sparse rows once, filling only uncovered cells and sharing unchanged content. */
function rebuildGrid(block: ReportGridBlock, locations: ReportGridCellLocation[], rows = block.rows, columns = block.columns, fillEmpty = false): ReportGridBlock {
  const rowCount = rows.length, columnCount = columns.length;
  const occupied = fillEmpty ? new Uint8Array(rowCount * columnCount) : null;
  const placed = Array.from({ length: rowCount }, () => [] as ReportGridCellLocation[]);
  for (const location of locations) {
    if (location.rowIndex >= rowCount || location.columnIndex >= columnCount) continue;
    const rowSpan = Math.min(location.rowSpan, rowCount - location.rowIndex);
    const colSpan = Math.min(location.colSpan, columnCount - location.columnIndex);
    for (let row = location.rowIndex; occupied && row < location.rowIndex + rowSpan; row++) {
      const start = row * columnCount + location.columnIndex;
      occupied.fill(1, start, start + colSpan);
    }
    placed[location.rowIndex].push({ ...location, rowSpan, colSpan,
      cell: rowSpan === (location.cell.rowSpan ?? 1) && colSpan === (location.cell.colSpan ?? 1)
        ? location.cell : { ...location.cell, rowSpan, colSpan } });
  }
  for (let rowIndex = 0; occupied && rowIndex < rowCount; rowIndex++) {
    for (let columnIndex = 0; columnIndex < columnCount; columnIndex++) {
      if (!occupied[rowIndex * columnCount + columnIndex]) placed[rowIndex].push({ rowIndex, columnIndex, rowSpan: 1, colSpan: 1, cell: emptyGridCell() });
    }
  }
  return { ...block, columns, rows: rows.map((row, index) => {
    const cells = placed[index].sort((a, b) => a.columnIndex - b.columnIndex).map(location => location.cell);
    return row.cells.length === cells.length && row.cells.every((cell, index) => cell === cells[index]) ? row : { ...row, cells };
  }) };
}

export function resizeGridStructure(block: ReportGridBlock, rowCount: number, columnCount: number): ReportGridBlock {
  if (!Number.isInteger(rowCount) || rowCount < 1 || rowCount > REPORT_GRID_MAX_ROWS ||
      !Number.isInteger(columnCount) || columnCount < 1 || columnCount > REPORT_GRID_MAX_COLUMNS ||
      (rowCount === block.rows.length && columnCount === block.columns.length)) return block;
  const rows = block.rows.slice(0, rowCount);
  while (rows.length < rowCount) rows.push(createGridRow([], block.rows.at(-1)?.heightMm ?? 9));
  let columns = block.columns;
  if (columnCount !== columns.length) {
    columns = columns.slice(0, columnCount);
    if (columnCount > columns.length) columns = [...columns, ...createGridColumns(columnCount - columns.length)
      .map(column => ({ ...column, widthPercent: 100 / block.columns.length }))];
    columns = normalizeWidths(columns);
  }
  return rebuildGrid(block, getGridCellLocations(block), rows, columns, true);
}

/** Insert/delete a physical row or column. Merges crossing that track grow/shrink; their content survives. */
export function editGridTrack(block: ReportGridBlock, axis: ReportGridAxis, index: number, action: "insert" | "delete"): ReportGridBlock {
  const count = axis === "row" ? block.rows.length : block.columns.length;
  const maximum = axis === "row" ? REPORT_GRID_MAX_ROWS : REPORT_GRID_MAX_COLUMNS;
  const insert = action === "insert";
  if (!Number.isInteger(index) || index < 0 || index > count - (insert ? 0 : 1) || (insert ? count >= maximum : count <= 1)) return block;
  const position = axis === "row" ? "rowIndex" : "columnIndex";
  const span = axis === "row" ? "rowSpan" : "colSpan";
  const locations = getGridCellLocations(block).flatMap(location => {
    const start = location[position], end = start + location[span];
    if (insert) {
      if (start >= index) return [{ ...location, [position]: start + 1 }];
      if (end > index) return [{ ...location, [span]: location[span] + 1 }];
    } else {
      if (start > index) return [{ ...location, [position]: start - 1 }];
      if (end > index) return location[span] === 1 ? [] : [{ ...location, [span]: location[span] - 1 }];
    }
    return [location];
  });
  let rows = block.rows, columns = block.columns;
  if (axis === "row") {
    rows = rows.slice();
    rows.splice(index, insert ? 0 : 1, ...(insert ? [createGridRow([], block.rows[Math.min(index, count - 1)].heightMm ?? 9)] : []));
  } else {
    columns = columns.slice();
    columns.splice(index, insert ? 0 : 1, ...(insert ? createGridColumns(1).map(column => ({ ...column, widthPercent: 100 / count })) : []));
    columns = normalizeWidths(columns);
  }
  return rebuildGrid(block, locations, rows, columns, true);
}

function gridMergePair(locations: ReportGridCellLocation[], cellId: string, direction: "right" | "down") {
  const current = locations.find(location => location.cell.id === cellId);
  const adjacent = current && locations.find(location => direction === "right"
    ? location.rowIndex === current.rowIndex && location.columnIndex === current.columnIndex + current.colSpan && location.rowSpan === current.rowSpan
    : location.rowIndex === current.rowIndex + current.rowSpan && location.columnIndex === current.columnIndex && location.colSpan === current.colSpan);
  return current && adjacent ? { current, adjacent } : null;
}

function mergeGridCell(block: ReportGridBlock, cellId: string, direction: "right" | "down") {
  const locations = getGridCellLocations(block);
  const pair = gridMergePair(locations, cellId, direction);
  if (!pair) return block;
  const span = direction === "right" ? "colSpan" : "rowSpan";
  return rebuildGrid(block, locations.filter(location => location !== pair.adjacent)
    .map(location => location === pair.current ? { ...location, [span]: pair.current[span] + pair.adjacent[span] } : location));
}

export function mergeGridCellRight(block: ReportGridBlock, cellId: string) { return mergeGridCell(block, cellId, "right"); }
export function mergeGridCellDown(block: ReportGridBlock, cellId: string) { return mergeGridCell(block, cellId, "down"); }
export function canMergeGridCellRight(block: ReportGridBlock, cellId: string, locations = getGridCellLocations(block)) { return gridMergePair(locations, cellId, "right") !== null; }
export function canMergeGridCellDown(block: ReportGridBlock, cellId: string, locations = getGridCellLocations(block)) { return gridMergePair(locations, cellId, "down") !== null; }

export function splitGridCell(block: ReportGridBlock, cellId: string): ReportGridBlock {
  const locations = getGridCellLocations(block);
  const current = locations.find(location => location.cell.id === cellId);
  if (!current || (current.colSpan === 1 && current.rowSpan === 1)) return block;
  const replacements: ReportGridCellLocation[] = [];
  for (let row = current.rowIndex; row < current.rowIndex + current.rowSpan; row++) {
    for (let column = current.columnIndex; column < current.columnIndex + current.colSpan; column++) {
      replacements.push({ rowIndex: row, columnIndex: column, rowSpan: 1, colSpan: 1,
        cell: row === current.rowIndex && column === current.columnIndex ? { ...current.cell, rowSpan: 1, colSpan: 1 }
          : { ...emptyGridCell(), style: current.cell.style, border: current.cell.border } });
    }
  }
  return rebuildGrid(block, [...locations.filter(location => location !== current), ...replacements]);
}

export function setGridRowsToUniformHeight(block: ReportGridBlock, heightMm: number): ReportGridBlock {
  return block.rows.every(row => row.heightMm === heightMm) ? block : { ...block,
    rows: block.rows.map(row => row.heightMm === heightMm ? row : { ...row, heightMm }) };
}
