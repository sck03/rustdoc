import { createGridCell, createGridColumns, createGridRow } from "./reportDesignerBlockFactories.ts";
import type { ReportBorderStyle, ReportGridBlock, ReportGridCell } from "./reportDesignerSchema.ts";
import { emptyGridCell, getGridCellLocations, type ReportGridCellLocation } from "./reportDesignerGridStructure.ts";
export { adjacentGridCell, getGridCellLocations, gridCellAt, formatGridCellAddress, parseGridCellAddress, editGridTrack, resizeGridStructure, mergeGridCellRight, mergeGridCellDown,
  canMergeGridCellRight, canMergeGridCellDown, splitGridCell, setGridRowsToUniformHeight, type ReportGridCellLocation } from "./reportDesignerGridStructure.ts";

export type ReportGridPreset = "Blank" | "Form" | "Approval";

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

export function applyGridPreset(block: ReportGridBlock, preset: ReportGridPreset): ReportGridBlock {
  const structure = preset === "Blank"
    ? blankStructure(3, 3)
    : preset === "Approval"
      ? approvalStructure()
      : formStructure();
  return { ...block, ...structure };
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

function blankStructure(rowCount: number, columnCount: number) {
  return {
    columns: createGridColumns(columnCount),
    rows: Array.from({ length: rowCount }, () => createGridRow(Array.from({ length: columnCount }, emptyGridCell))),
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
