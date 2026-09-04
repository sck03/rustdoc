import { createGridCell, createGridColumns, createGridRow } from "./reportDesignerBlockFactories.ts";
import { distributeGridColumnWidths } from "./reportDesignerTableMutations.ts";
import type { ReportBorderStyle, ReportGridBlock, ReportGridCell } from "./reportDesignerSchema.ts";

export type ReportGridPreset = "Blank" | "Form" | "Approval";

export type ReportGridCellLocation = {
  cell: ReportGridCell;
  rowIndex: number;
  columnIndex: number;
  rowSpan: number;
  colSpan: number;
};

export function getGridCellLocations(block: ReportGridBlock): ReportGridCellLocation[] {
  const rowCount = block.rows.length;
  const columnCount = block.columns.length;
  const occupied = Array.from({ length: rowCount }, () => Array<boolean>(columnCount).fill(false));
  const result: ReportGridCellLocation[] = [];

  block.rows.forEach((row, rowIndex) => {
    let columnIndex = 0;
    row.cells.forEach((cell) => {
      while (columnIndex < columnCount && occupied[rowIndex][columnIndex]) columnIndex += 1;
      if (columnIndex >= columnCount) return;
      const colSpan = Math.min(columnCount - columnIndex, Math.max(1, Math.floor(cell.colSpan ?? 1)));
      const rowSpan = Math.min(rowCount - rowIndex, Math.max(1, Math.floor(cell.rowSpan ?? 1)));
      result.push({ cell, rowIndex, columnIndex, rowSpan, colSpan });
      for (let rowOffset = 0; rowOffset < rowSpan; rowOffset += 1) {
        for (let columnOffset = 0; columnOffset < colSpan; columnOffset += 1) {
          occupied[rowIndex + rowOffset][columnIndex + columnOffset] = true;
        }
      }
      columnIndex += colSpan;
    });
  });
  return result;
}

export function applyGridPreset(block: ReportGridBlock, preset: ReportGridPreset): ReportGridBlock {
  const structure = preset === "Blank"
    ? blankStructure(3, 3)
    : preset === "Approval"
      ? approvalStructure()
      : formStructure();
  return { ...block, ...structure };
}

export function appendGridRow(block: ReportGridBlock): ReportGridBlock {
  return {
    ...block,
    rows: [...block.rows, createGridRow(Array.from({ length: block.columns.length }, () => createGridCell("Text", "")))],
  };
}

export function removeLastGridRow(block: ReportGridBlock): ReportGridBlock {
  if (block.rows.length <= 1) return block;
  const rowCount = block.rows.length - 1;
  const locations = getGridCellLocations(block)
    .filter((location) => location.rowIndex < rowCount)
    .map((location) => ({
      ...location,
      rowSpan: Math.min(location.rowSpan, rowCount - location.rowIndex),
      cell: { ...location.cell, rowSpan: Math.min(location.rowSpan, rowCount - location.rowIndex) },
    }));
  return { ...block, rows: rebuildRows(block, locations, rowCount) };
}

export function appendGridColumn(block: ReportGridBlock): ReportGridBlock {
  return distributeGridColumnWidths({
    ...block,
    columns: [...block.columns, createGridColumns(1)[0]],
    rows: block.rows.map((row) => ({ ...row, cells: [...row.cells, createGridCell("Text", "")] })),
  });
}

export function removeLastGridColumn(block: ReportGridBlock): ReportGridBlock {
  if (block.columns.length <= 1) return block;
  const columnCount = block.columns.length - 1;
  const locations = getGridCellLocations(block)
    .filter((location) => location.columnIndex < columnCount)
    .map((location) => ({
      ...location,
      colSpan: Math.min(location.colSpan, columnCount - location.columnIndex),
      cell: { ...location.cell, colSpan: Math.min(location.colSpan, columnCount - location.columnIndex) },
    }));
  const rows = rebuildRows(block, locations, block.rows.length);
  if (rows.some((row) => row.cells.length === 0)) return block;
  return distributeGridColumnWidths({ ...block, columns: block.columns.slice(0, columnCount), rows });
}

export function mergeGridCellRight(block: ReportGridBlock, cellId: string): ReportGridBlock {
  const locations = getGridCellLocations(block);
  const current = locations.find((location) => location.cell.id === cellId);
  const adjacent = current && locations.find((location) =>
    location.rowIndex === current.rowIndex &&
    location.columnIndex === current.columnIndex + current.colSpan &&
    location.rowSpan === current.rowSpan);
  if (!current || !adjacent) return block;
  return replaceGridCells(block, current.cell.id, adjacent.cell.id, {
    ...current.cell,
    colSpan: current.colSpan + adjacent.colSpan,
  });
}

export function mergeGridCellDown(block: ReportGridBlock, cellId: string): ReportGridBlock {
  const locations = getGridCellLocations(block);
  const current = locations.find((location) => location.cell.id === cellId);
  const adjacent = current && locations.find((location) =>
    location.rowIndex === current.rowIndex + current.rowSpan &&
    location.columnIndex === current.columnIndex &&
    location.colSpan === current.colSpan);
  if (!current || !adjacent || block.rows[adjacent.rowIndex].cells.length <= 1) return block;
  return replaceGridCells(block, current.cell.id, adjacent.cell.id, {
    ...current.cell,
    rowSpan: current.rowSpan + adjacent.rowSpan,
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
        cell: createGridCell("Text", ""),
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

export function canMergeGridCellRight(block: ReportGridBlock, cellId: string) {
  return mergeGridCellRight(block, cellId) !== block;
}

export function canMergeGridCellDown(block: ReportGridBlock, cellId: string) {
  return mergeGridCellDown(block, cellId) !== block;
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
  return Array.from({ length: rowCount }, (_, rowIndex) => ({
    ...(block.rows[rowIndex] ?? createGridRow()),
    cells: locations
      .filter((location) => location.rowIndex === rowIndex)
      .sort((left, right) => left.columnIndex - right.columnIndex)
      .map((location) => location.cell),
  }));
}

function blankStructure(rowCount: number, columnCount: number) {
  return {
    columns: createGridColumns(columnCount),
    rows: Array.from({ length: rowCount }, () => createGridRow(Array.from({ length: columnCount }, () => createGridCell("Text", "")))),
  };
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
