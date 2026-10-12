import { createGridCell, createGridColumns, createGridRow } from "./reportDesignerBlockFactories.ts";
import type { ReportBorderStyle, ReportGridBlock, ReportGridCell, ReportTextStyle } from "./reportDesignerSchema.ts";
import { emptyGridCell, getGridCellLocations, type ReportGridCellLocation } from "./reportDesignerGridStructure.ts";
import type { ResolvedGridSelection } from "./reportDesignerGridSelection.ts";
export { adjacentGridCell, getGridCellLocations, gridCellAt, formatGridCellAddress, parseGridCellAddress, editGridTrack, resizeGridStructure, mergeGridCellRight, mergeGridCellDown,
  canMergeGridCellRight, canMergeGridCellDown, mergeGridCell, mergeGridSelection, splitGridSelection, splitGridCell, setGridRowsToUniformHeight, type ReportGridCellLocation } from "./reportDesignerGridStructure.ts";

export type ReportGridPreset = "Blank" | "Form" | "Approval";

export function updateGridCell(block: ReportGridBlock, cellId: string, update: (cell: ReportGridCell) => ReportGridCell): ReportGridBlock {
  return updateGridCells(block, new Set([cellId]), update);
}

function updateGridCells(block: ReportGridBlock, ids: ReadonlySet<string>, update: (cell: ReportGridCell) => ReportGridCell): ReportGridBlock {
  let changed = false;
  const rows = block.rows.map((row) => {
    let cells: ReportGridCell[] | undefined;
    row.cells.forEach((cell, index) => {
      if (!ids.has(cell.id)) return;
      const next = update(cell);
      if (next === cell) return;
      cells ??= row.cells.slice();
      cells[index] = next;
    });
    if (!cells) return row;
    changed = true;
    return { ...row, cells };
  });
  return changed ? { ...block, rows } : block;
}

export function patchGridCellStyles(block: ReportGridBlock, ids: ReadonlySet<string>, patch: Partial<ReportTextStyle>) {
  return updateGridCells(block, ids, cell => sameProperties(cell.style, { ...cell.style, ...patch })
    ? cell : { ...cell, style: { ...cell.style, ...patch } });
}

export function setGridSelectionBorders(block: ReportGridBlock, selected: ResolvedGridSelection, preset: "all" | "outline" | "none") {
  const reference = selected.anchor.cell.border ?? block.border;
  const border = { ...reference, widthPx: reference.widthPx > 0 ? reference.widthPx : 1,
    style: reference.style === "None" ? "Solid" as const : reference.style };
  const { top, left, bottom, right } = selected.range;
  return applyGridCellBorders(block, new Map(selected.cells.map(location => [location.cell.id, {
    ...border,
    top: preset === "all" || preset === "outline" && location.rowIndex === top,
    left: preset === "all" || preset === "outline" && location.columnIndex === left,
    bottom: preset === "all" || preset === "outline" && location.rowIndex + location.rowSpan === bottom,
    right: preset === "all" || preset === "outline" && location.columnIndex + location.colSpan === right,
  }])));
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
  return applyGridCellBorders(block, new Map([[cellId, border]]));
}

function applyGridCellBorders(block: ReportGridBlock, borders: Map<string, ReportBorderStyle>): ReportGridBlock {
  const locations = getGridCellLocations(block);
  const selected = locations.filter(location => borders.has(location.cell.id));
  if (!selected.length) return block;

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
  for (const location of selected) {
    const border = borders.get(location.cell.id)!;
    syncOppositeBorderSide(neighborPatches, adjacentGridCells(cellAt, location, "top"), "bottom", "top", border);
    syncOppositeBorderSide(neighborPatches, adjacentGridCells(cellAt, location, "right"), "left", "right", border);
    syncOppositeBorderSide(neighborPatches, adjacentGridCells(cellAt, location, "bottom"), "top", "bottom", border);
    syncOppositeBorderSide(neighborPatches, adjacentGridCells(cellAt, location, "left"), "right", "left", border);
  }
  return updateGridCells(block, new Set([...borders.keys(), ...neighborPatches.keys()]), cell => {
    const current = cell.border ?? block.border;
    const next = borders.get(cell.id) ?? { ...current, ...neighborPatches.get(cell.id) };
    return sameProperties(current, next) ? cell : { ...cell, border: next };
  });
}

function sameProperties<T extends object>(left: T, right: T) {
  return Object.keys(left).length === Object.keys(right).length &&
    (Object.keys(right) as Array<keyof T>).every(key => Object.is(left[key], right[key]));
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
