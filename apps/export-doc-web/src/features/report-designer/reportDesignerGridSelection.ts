import type { ReportGridCellPlacement } from "./reportDesignerGridPlacement.ts";

export type ReportGridCellSelection = { cellId: string; endCellId?: string };
export type ReportGridRange = { top: number; left: number; bottom: number; right: number };

function bounds(cell: ReportGridCellPlacement): ReportGridRange {
  return { top: cell.rowIndex, left: cell.columnIndex, bottom: cell.rowIndex + cell.rowSpan, right: cell.columnIndex + cell.colSpan };
}

function intersects(cell: ReportGridCellPlacement, range: ReportGridRange) {
  return cell.rowIndex < range.bottom && cell.rowIndex + cell.rowSpan > range.top &&
    cell.columnIndex < range.right && cell.columnIndex + cell.colSpan > range.left;
}

/** A selection always includes whole merged cells, including merges along its edges. */
export function resolveGridSelection(locations: ReportGridCellPlacement[], selection: ReportGridCellSelection) {
  const anchor = locations.find(location => location.cell.id === selection.cellId);
  if (!anchor) return null;
  const end = locations.find(location => location.cell.id === selection.endCellId) ?? anchor;
  const range = bounds(anchor), last = bounds(end);
  range.top = Math.min(range.top, last.top);
  range.left = Math.min(range.left, last.left);
  range.bottom = Math.max(range.bottom, last.bottom);
  range.right = Math.max(range.right, last.right);
  let expanded: boolean;
  do {
    expanded = false;
    for (const location of locations) {
      if (!intersects(location, range)) continue;
      const next = bounds(location);
      if (next.top < range.top || next.left < range.left || next.bottom > range.bottom || next.right > range.right) {
        range.top = Math.min(range.top, next.top);
        range.left = Math.min(range.left, next.left);
        range.bottom = Math.max(range.bottom, next.bottom);
        range.right = Math.max(range.right, next.right);
        expanded = true;
      }
    }
  } while (expanded);
  return { anchor, end, range, cells: locations.filter(location => intersects(location, range)) };
}

export type ResolvedGridSelection = NonNullable<ReturnType<typeof resolveGridSelection>>;
