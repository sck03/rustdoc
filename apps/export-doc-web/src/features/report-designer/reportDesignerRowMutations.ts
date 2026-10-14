import type { ReportBlock, ReportRowColumn } from "./reportDesignerSchema.ts";
import { createRowColumn } from "./reportDesignerBlockFactories.ts";
import { normalizeRowColumnWidths, resizeAdjacentRowColumnWidths } from "./reportDesignerTableMutations.ts";

type Row = Extract<ReportBlock, { type: "Row" }>;
// Each column has a minimum width of 1% in the shared report schema.
export const MAX_ROW_COLUMNS = 100;

export function resizeRowColumns(block: Row, count: number): Row {
  if (!Number.isInteger(count) || count < 1 || count > MAX_ROW_COLUMNS || count === block.columns.length) return block;
  const columns = block.columns.slice(0, count);
  while (columns.length < count) columns.push(createRowColumn());
  return { ...block, columns: columns.map(column => ({ ...column, widthPercent: 100 / count })) };
}

export function updateRowColumn(block: Row, id: string, patch: Partial<Omit<ReportRowColumn, "id" | "widthPercent">>): Row {
  const column = block.columns.find(column => column.id === id);
  if (!column || Object.entries(patch).every(([key, value]) => column[key as keyof ReportRowColumn] === value)) return block;
  return { ...block, columns: block.columns.map(current => current.id === id ? { ...current, ...patch } : current) };
}

export function insertRowColumn(block: Row, copyId?: string): Row {
  if (block.columns.length >= MAX_ROW_COLUMNS) return block;
  const index = copyId ? block.columns.findIndex(column => column.id === copyId) : block.columns.length - 1;
  if (index < 0) return block;
  const created = createRowColumn();
  const column = copyId ? { ...block.columns[index], id: created.id } : created;
  return { ...block, columns: normalizeRowColumnWidths([...block.columns.slice(0, index + 1), column, ...block.columns.slice(index + 1)]) };
}

export function moveRowColumn(block: Row, id: string, direction: -1 | 1): Row {
  const index = block.columns.findIndex(column => column.id === id), target = index + direction;
  if (index < 0 || target < 0 || target >= block.columns.length) return block;
  const columns = [...block.columns];
  [columns[index], columns[target]] = [columns[target], columns[index]];
  return { ...block, columns };
}

export function removeRowColumn(block: Row, id: string): Row {
  if (block.columns.length <= 1 || !block.columns.some(column => column.id === id)) return block;
  return { ...block, columns: normalizeRowColumnWidths(block.columns.filter(column => column.id !== id)) };
}

export function setRowColumnWidth(block: Row, id: string, width: number): Row {
  const index = block.columns.findIndex(column => column.id === id);
  if (index < 0 || block.columns.length < 2 || !Number.isFinite(width)) return block;
  const last = index === block.columns.length - 1;
  const left = block.columns[last ? index - 1 : index];
  const desired = last ? left.widthPercent + block.columns[index].widthPercent - width : width;
  const columns = resizeAdjacentRowColumnWidths(block.columns, left.id, desired - left.widthPercent);
  return columns === block.columns ? block : { ...block, columns };
}
