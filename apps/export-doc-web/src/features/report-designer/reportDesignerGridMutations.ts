import { createGridCell, createGridColumns, createGridRow } from "./reportDesignerBlockFactories.ts";
import type { ReportBorderStyle, ReportGridBlock, ReportGridCell } from "./reportDesignerSchema.ts";
import { REPORT_GRID_MAX_COLUMNS, REPORT_GRID_MAX_ROWS, reportGridCellPlacements, type ReportGridCellPlacement } from "./reportDesignerGridPlacement.ts";
import { normalizeColumnPercentages } from "./reportDesignerColumnWidths.ts";

export type ReportGridPreset = "Blank" | "Form" | "Approval";

export type ReportGridCellLocation = ReportGridCellPlacement;

export function updateGridCell(block: ReportGridBlock, cellId: string, update: (cell: ReportGridCell) => ReportGridCell): ReportGridBlock {
  let changed = false;
  const rows = block.rows.map((row) => {
    const index = row.cells.findIndex((cell) => cell.id === cellId);
    if (index < 0) return row;
    const next = update(row.cells[index]);
    if (next === row.cells[index]) return row;
    changed = true;
    const cells = row.cells.slice();
    cells[index] = next;
    return { ...row, cells };
  });
  return changed ? { ...block, rows } : block;
}

export function adjacentGridCell(block: ReportGridBlock, cellId: string, direction: "left" | "right" | "up" | "down") {
  const locations = getGridCellLocations(block);
  const current = locations.find((location) => location.cell.id === cellId);
  if (!current) return null;
  const row = direction === "up" ? current.rowIndex - 1 : direction === "down" ? current.rowIndex + current.rowSpan : current.rowIndex;
  const column = direction === "left" ? current.columnIndex - 1 : direction === "right" ? current.columnIndex + current.colSpan : current.columnIndex;
  return locations.find((location) => row >= location.rowIndex && row < location.rowIndex + location.rowSpan && column >= location.columnIndex && column < location.columnIndex + location.colSpan) ?? null;
}

export function getGridCellLocations(block: ReportGridBlock): ReportGridCellLocation[] {
  return reportGridCellPlacements(block, "grid", []);
}

export function applyGridPreset(block: ReportGridBlock, preset: ReportGridPreset): ReportGridBlock {
  const structure = preset === "Blank"
    ? blankStructure(3, 3)
    : preset === "Approval"
      ? approvalStructure()
      : formStructure();
  return { ...block, ...structure };
}

export function resizeGridStructure(block: ReportGridBlock, rowCount: number, columnCount: number): ReportGridBlock {
  if (!Number.isInteger(rowCount) || rowCount < 1 || rowCount > REPORT_GRID_MAX_ROWS ||
      !Number.isInteger(columnCount) || columnCount < 1 || columnCount > REPORT_GRID_MAX_COLUMNS ||
      (rowCount === block.rows.length && columnCount === block.columns.length)) return block;
  const occupied = new Uint8Array(rowCount * columnCount);
  const locations = getGridCellLocations(block)
    .filter(location => location.rowIndex < rowCount && location.columnIndex < columnCount)
    .map(location => {
      const rowSpan = Math.min(location.rowSpan, rowCount - location.rowIndex);
      const colSpan = Math.min(location.colSpan, columnCount - location.columnIndex);
      for (let row = location.rowIndex; row < location.rowIndex + rowSpan; row++) {
        const start = row * columnCount + location.columnIndex;
        occupied.fill(1, start, start + colSpan);
      }
      return { ...location, rowSpan, colSpan, cell: rowSpan === location.rowSpan && colSpan === location.colSpan
        ? location.cell : { ...location.cell, rowSpan, colSpan } };
    });
  for (let rowIndex = 0; rowIndex < rowCount; rowIndex++) {
    for (let columnIndex = 0; columnIndex < columnCount; columnIndex++) {
      if (!occupied[rowIndex * columnCount + columnIndex]) locations.push({ rowIndex, columnIndex, rowSpan: 1, colSpan: 1,
        cell: emptyGridCell() });
    }
  }
  let columns = block.columns;
  if (columnCount !== columns.length) {
    columns = columns.slice(0, columnCount);
    if (columnCount > columns.length) columns = [...columns, ...createGridColumns(columnCount - columns.length)
      .map(column => ({ ...column, widthPercent: 100 / block.columns.length }))];
    const weights = columns.map(column => Math.max(0, column.widthPercent - 1));
    const total = weights.reduce((sum, value) => sum + value, 0);
    columns = normalizeColumnPercentages(columns.map((column, index) => ({ ...column, widthPercent: 1 + (100 - columnCount) * (total ? weights[index] / total : 1 / columnCount) })));
  }
  return { ...block, columns, rows: rebuildRows(block, locations, rowCount) };
}

export function mergeGridCellRight(block: ReportGridBlock, cellId: string): ReportGridBlock {
  return mergeGridCell(block, cellId, "right");
}

export function mergeGridCellDown(block: ReportGridBlock, cellId: string): ReportGridBlock {
  return mergeGridCell(block, cellId, "down");
}

function gridMergePair(locations: ReportGridCellLocation[], cellId: string, direction: "right" | "down") {
  const current = locations.find(location => location.cell.id === cellId);
  const adjacent = current && locations.find(location => direction === "right"
    ? location.rowIndex === current.rowIndex && location.columnIndex === current.columnIndex + current.colSpan && location.rowSpan === current.rowSpan
    : location.rowIndex === current.rowIndex + current.rowSpan && location.columnIndex === current.columnIndex && location.colSpan === current.colSpan);
  return current && adjacent ? { current, adjacent } : null;
}

function mergeGridCell(block: ReportGridBlock, cellId: string, direction: "right" | "down") {
  const pair = gridMergePair(getGridCellLocations(block), cellId, direction);
  if (!pair) return block;
  const span = direction === "right" ? "colSpan" : "rowSpan";
  return replaceGridCells(block, cellId, pair.adjacent.cell.id, {
    ...pair.current.cell, [span]: pair.current[span] + pair.adjacent[span],
  });
}

export function splitGridCell(block: ReportGridBlock, cellId: string): ReportGridBlock {
  const locations = getGridCellLocations(block);
  const current = locations.find((location) => location.cell.id === cellId);
  if (!current || (current.colSpan === 1 && current.rowSpan === 1)) return block;
  const replacements: ReportGridCellLocation[] = [{
    ...current,
    rowSpan: 1,
    colSpan: 1,
    cell: { ...current.cell, rowSpan: 1, colSpan: 1 },
  }];
  for (let rowOffset = 0; rowOffset < current.rowSpan; rowOffset += 1) {
    for (let columnOffset = 0; columnOffset < current.colSpan; columnOffset += 1) {
      if (rowOffset === 0 && columnOffset === 0) continue;
      replacements.push({
        cell: { ...emptyGridCell(), style: current.cell.style, border: current.cell.border },
        rowIndex: current.rowIndex + rowOffset,
        columnIndex: current.columnIndex + columnOffset,
        rowSpan: 1,
        colSpan: 1,
      });
    }
  }
  return {
    ...block,
    rows: rebuildRows(block, [...locations.filter((location) => location.cell.id !== cellId), ...replacements], block.rows.length),
  };
}

export function canMergeGridCellRight(block: ReportGridBlock, cellId: string, locations = getGridCellLocations(block)) {
  return gridMergePair(locations, cellId, "right") !== null;
}

export function canMergeGridCellDown(block: ReportGridBlock, cellId: string, locations = getGridCellLocations(block)) {
  return gridMergePair(locations, cellId, "down") !== null;
}

export function setGridRowsToUniformHeight(block: ReportGridBlock, heightMm: number): ReportGridBlock {
  return { ...block, rows: block.rows.map((row) => ({ ...row, heightMm })) };
}

/**
 * A collapsed table border is shared by both adjacent cells.  Keep the
 * opposite side in sync so turning one side off is immediately visible on the
 * canvas instead of being silently supplied by its neighbour.
 */
export function updateGridCellBorder(
  block: ReportGridBlock,
  cellId: string,
  border: ReportBorderStyle,
): ReportGridBlock {
  const locations = getGridCellLocations(block);
  const selected = locations.find((location) => location.cell.id === cellId);
  if (!selected) return block;

  const cellAt = Array.from({ length: block.rows.length }, () =>
    Array<ReportGridCellLocation | null>(block.columns.length).fill(null));
  for (const location of locations) {
    for (let rowOffset = 0; rowOffset < location.rowSpan; rowOffset += 1) {
      for (let columnOffset = 0; columnOffset < location.colSpan; columnOffset += 1) {
        cellAt[location.rowIndex + rowOffset][location.columnIndex + columnOffset] = location;
      }
    }
  }

  const neighborPatches = new Map<string, Partial<ReportBorderStyle>>();
  syncOppositeBorderSide(neighborPatches, adjacentGridCells(cellAt, selected, "top"), "bottom", "top", border);
  syncOppositeBorderSide(neighborPatches, adjacentGridCells(cellAt, selected, "right"), "left", "right", border);
  syncOppositeBorderSide(neighborPatches, adjacentGridCells(cellAt, selected, "bottom"), "top", "bottom", border);
  syncOppositeBorderSide(neighborPatches, adjacentGridCells(cellAt, selected, "left"), "right", "left", border);

  return {
    ...block,
    rows: block.rows.map((row) => ({
      ...row,
      cells: row.cells.map((cell) => {
        if (cell.id === cellId) return { ...cell, border };
        const patch = neighborPatches.get(cell.id);
        return patch ? { ...cell, border: { ...(cell.border ?? block.border), ...patch } } : cell;
      }),
    })),
  };
}

type GridBorderSide = "top" | "right" | "bottom" | "left";

function adjacentGridCells(
  cellAt: Array<Array<ReportGridCellLocation | null>>,
  selected: ReportGridCellLocation,
  side: GridBorderSide,
) {
  const candidates: Array<ReportGridCellLocation | null> = [];
  if (side === "top" || side === "bottom") {
    const rowIndex = side === "top" ? selected.rowIndex - 1 : selected.rowIndex + selected.rowSpan;
    if (rowIndex < 0 || rowIndex >= cellAt.length) return [];
    for (let columnIndex = selected.columnIndex; columnIndex < selected.columnIndex + selected.colSpan; columnIndex += 1) {
      candidates.push(cellAt[rowIndex][columnIndex]);
    }
  } else {
    const columnIndex = side === "left" ? selected.columnIndex - 1 : selected.columnIndex + selected.colSpan;
    if (columnIndex < 0 || columnIndex >= (cellAt[0]?.length ?? 0)) return [];
    for (let rowIndex = selected.rowIndex; rowIndex < selected.rowIndex + selected.rowSpan; rowIndex += 1) {
      candidates.push(cellAt[rowIndex][columnIndex]);
    }
  }
  return [...new Map(candidates.filter((candidate): candidate is ReportGridCellLocation =>
    Boolean(candidate) && candidate?.cell.id !== selected.cell.id).map((candidate) => [candidate.cell.id, candidate])).values()];
}

function syncOppositeBorderSide(
  patches: Map<string, Partial<ReportBorderStyle>>,
  neighbors: ReportGridCellLocation[],
  neighborSide: GridBorderSide,
  selectedSide: GridBorderSide,
  border: ReportBorderStyle,
) {
  const visible = border.widthPx > 0 && border.style !== "None" && Boolean(border[selectedSide]);
  for (const neighbor of neighbors) {
    patches.set(neighbor.cell.id, {
      ...patches.get(neighbor.cell.id),
      ...(visible ? { color: border.color, widthPx: border.widthPx, style: border.style } : {}),
      [neighborSide]: visible,
    });
  }
}

function replaceGridCells(block: ReportGridBlock, keepId: string, removeId: string, replacement: ReportGridCell) {
  return {
    ...block,
    rows: block.rows.map((row) => ({
      ...row,
      cells: row.cells.filter((cell) => cell.id !== removeId).map((cell) => cell.id === keepId ? replacement : cell),
    })),
  };
}

function rebuildRows(block: ReportGridBlock, locations: ReportGridCellLocation[], rowCount: number) {
  const rows = Array.from({ length: rowCount }, () => [] as ReportGridCellLocation[]);
  for (const location of locations) rows[location.rowIndex].push(location);
  return rows.map((locations, rowIndex) => {
    const cells = locations.sort((left, right) => left.columnIndex - right.columnIndex).map(location => location.cell);
    const row = block.rows[rowIndex];
    if (!row) return createGridRow(cells, block.rows.at(-1)?.heightMm ?? 9);
    return row.cells.length === cells.length && row.cells.every((cell, index) => cell === cells[index]) ? row : { ...row, cells };
  });
}

function blankStructure(rowCount: number, columnCount: number) {
  return {
    columns: createGridColumns(columnCount),
    rows: Array.from({ length: rowCount }, () => createGridRow(Array.from({ length: columnCount }, emptyGridCell))),
  };
}

function emptyGridCell(): ReportGridCell {
  return { ...createGridCell("Text", "", "", 1, 1, {}), border: undefined };
}

function formStructure() {
  const labelStyle = { fontSizePt: 10, bold: true, align: "Center" as const };
  return {
    columns: createGridColumns(4),
    rows: [
      createGridRow([createGridCell("Text", "标签", "", 1, 1, labelStyle), createGridCell("Text", ""), createGridCell("Text", "标签", "", 1, 1, labelStyle), createGridCell("Text", "")]),
      createGridRow([createGridCell("Text", "标签", "", 1, 1, labelStyle), createGridCell("Text", ""), createGridCell("Text", "标签", "", 1, 1, labelStyle), createGridCell("Text", "")]),
      createGridRow([createGridCell("Text", "备注", "", 1, 1, labelStyle), createGridCell("Text", "", "", 3)]),
    ],
  };
}

function approvalStructure() {
  const labelStyle = { fontSizePt: 10, bold: true, align: "Center" as const };
  return {
    columns: createGridColumns(3),
    rows: [
      createGridRow(["经办", "审核", "批准"].map((text) => createGridCell("Text", text, "", 1, 1, labelStyle)), 8),
      createGridRow(Array.from({ length: 3 }, () => createGridCell("Text", "")), 16),
    ],
  };
}
