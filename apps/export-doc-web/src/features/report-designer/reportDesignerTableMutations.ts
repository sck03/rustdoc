import type {
  ReportDetailTableBlock,
  ReportDetailTableGroupFooterCell,
  ReportDetailTableSummaryCell,
  ReportGridBlock,
  ReportRowColumn,
} from "./reportDesignerSchema.ts";
import {
  createDetailTableColumn,
  createGridColumns,
  createRowColumn,
} from "./reportDesignerBlockFactories.ts";
import { createReportBlockId } from "./reportDesignerMutationUtils.ts";

export function normalizeRowColumnWidths(columns: ReportRowColumn[]) {
  const safeColumns = columns.length > 0 ? columns : [createRowColumn()];
  const total = safeColumns.reduce((sum, column) => sum + Math.max(1, column.widthPercent), 0);
  return safeColumns.map((column) => ({
    ...column,
    widthPercent: Math.round((Math.max(1, column.widthPercent) / total) * 1000) / 10,
  }));
}

export function distributeGridColumnWidths(block: ReportGridBlock): ReportGridBlock {
  const safeColumns = block.columns.length > 0 ? block.columns : createGridColumns(1);
  const widthPercent = Math.round((1000 / safeColumns.length)) / 10;

  return {
    ...block,
    columns: safeColumns.map((column) => ({
      ...column,
      widthPercent,
    })),
  };
}

export function resizeAdjacentGridColumnWidths(
  block: ReportGridBlock,
  leftColumnId: string,
  deltaPercent: number,
): ReportGridBlock {
  return {
    ...block,
    columns: resizeAdjacentWidths(block.columns, leftColumnId, deltaPercent, 1, "widthPercent"),
  };
}

export function resizeAdjacentRowColumnWidths(
  columns: ReportRowColumn[],
  leftColumnId: string,
  deltaPercent: number,
): ReportRowColumn[] {
  return normalizeRowColumnWidths(resizeAdjacentWidths(columns, leftColumnId, deltaPercent, 1, "widthPercent"));
}

export function resizeAdjacentDetailTableColumnWidths(
  block: ReportDetailTableBlock,
  leftColumnId: string,
  deltaMm: number,
): ReportDetailTableBlock {
  return {
    ...block,
    columns: resizeAdjacentWidths(block.columns, leftColumnId, deltaMm, 8, "widthMm"),
  };
}

export function applyGridDefaultCellStyle(block: ReportGridBlock): ReportGridBlock {
  return {
    ...block,
    rows: block.rows.map((row) => ({
      ...row,
      cells: row.cells.map((cell) => ({
        ...cell,
        style: { ...block.defaultCellStyle },
      })),
    })),
  };
}

export function applyGridBorderToCells(block: ReportGridBlock): ReportGridBlock {
  return {
    ...block,
    rows: block.rows.map((row) => ({
      ...row,
      cells: row.cells.map((cell) => ({
        ...cell,
        border: { ...block.border },
      })),
    })),
  };
}

export function distributeDetailTableColumnWidths(block: ReportDetailTableBlock): ReportDetailTableBlock {
  if (block.columns.length === 0) {
    return block;
  }

  const totalWidthMm = block.columns.reduce((sum, column) => sum + Math.max(8, column.widthMm), 0);
  const widthMm = Math.round((totalWidthMm / block.columns.length) * 10) / 10;

  return {
    ...block,
    columns: block.columns.map((column) => ({
      ...column,
      widthMm,
    })),
  };
}

export function applyDetailTableBorderToColumns(block: ReportDetailTableBlock): ReportDetailTableBlock {
  return {
    ...block,
    columns: block.columns.map((column) => ({
      ...column,
      border: { ...block.border },
    })),
  };
}

export function clearDetailTableColumnBorders(block: ReportDetailTableBlock): ReportDetailTableBlock {
  return {
    ...block,
    columns: block.columns.map((column) => {
      const { border: _border, ...nextColumn } = column;
      return nextColumn;
    }),
  };
}

export function moveDetailTableColumn(
  block: ReportDetailTableBlock,
  columnId: string,
  direction: "up" | "down",
): ReportDetailTableBlock {
  const currentIndex = block.columns.findIndex((column) => column.id === columnId);
  const targetIndex = direction === "up" ? currentIndex - 1 : currentIndex + 1;
  if (currentIndex < 0 || targetIndex < 0 || targetIndex >= block.columns.length) {
    return block;
  }

  const columns = [...block.columns];
  const [movedColumn] = columns.splice(currentIndex, 1);
  columns.splice(targetIndex, 0, movedColumn);

  return {
    ...block,
    columns,
  };
}

export function reorderDetailTableColumn(
  block: ReportDetailTableBlock,
  sourceColumnId: string,
  targetColumnId: string,
): ReportDetailTableBlock {
  if (sourceColumnId === targetColumnId) {
    return block;
  }

  const sourceIndex = block.columns.findIndex((column) => column.id === sourceColumnId);
  const targetIndex = block.columns.findIndex((column) => column.id === targetColumnId);
  if (sourceIndex < 0 || targetIndex < 0) {
    return block;
  }

  const columns = [...block.columns];
  const [movedColumn] = columns.splice(sourceIndex, 1);
  columns.splice(targetIndex, 0, movedColumn);

  return {
    ...block,
    columns,
  };
}

export function duplicateDetailTableColumn(
  block: ReportDetailTableBlock,
  columnId: string,
): ReportDetailTableBlock {
  const currentIndex = block.columns.findIndex((column) => column.id === columnId);
  const sourceColumn = block.columns[currentIndex];
  if (!sourceColumn) {
    return block;
  }

  const duplicate = {
    ...createDetailTableColumn(
      `${sourceColumn.title} Copy`,
      sourceColumn.fieldPath,
      sourceColumn.widthMm,
      sourceColumn.align,
    ),
    headerGroupTitle: sourceColumn.headerGroupTitle,
    headerGroupSpan: sourceColumn.headerGroupSpan,
    contentKind: sourceColumn.contentKind,
    content: sourceColumn.content?.map((part) => ({ ...part, id: createReportBlockId("detail-cell-part") })),
    format: sourceColumn.format,
    border: sourceColumn.border,
  };
  const columns = [
    ...block.columns.slice(0, currentIndex + 1),
    duplicate,
    ...block.columns.slice(currentIndex + 1),
  ];

  return {
    ...block,
    columns,
    summaryRow: block.summaryRow
      ? {
          ...block.summaryRow,
          cells: [
            ...block.summaryRow.cells,
            createEmptySummaryCell(duplicate.id),
          ],
        }
      : undefined,
    grouping: block.grouping
      ? {
          ...block.grouping,
          footer: block.grouping.footer
            ? {
                ...block.grouping.footer,
                cells: [
                  ...block.grouping.footer.cells,
                  createEmptyGroupFooterCell(duplicate.id),
                ],
              }
            : undefined,
        }
      : undefined,
  };
}

export function removeDetailTableColumn(
  block: ReportDetailTableBlock,
  columnId: string,
): ReportDetailTableBlock {
  if (block.columns.length <= 1) {
    return block;
  }

  const columns = block.columns.filter((column) => column.id !== columnId);

  return {
    ...block,
    columns,
    summaryRow: block.summaryRow
      ? {
          ...block.summaryRow,
          labelColumnSpan: Math.min(block.summaryRow.labelColumnSpan, columns.length),
          cells: block.summaryRow.cells.filter((cell) => columns.some((column) => column.id === cell.columnId)),
        }
      : undefined,
    grouping: block.grouping
      ? {
          ...block.grouping,
          footer: block.grouping.footer
            ? {
                ...block.grouping.footer,
                labelColumnSpan: Math.min(block.grouping.footer.labelColumnSpan, columns.length),
                cells: block.grouping.footer.cells.filter((cell) => columns.some((column) => column.id === cell.columnId)),
              }
            : undefined,
        }
      : undefined,
  };
}

function createEmptySummaryCell(columnId: string): ReportDetailTableSummaryCell {
  return {
    columnId,
    contentKind: "Empty",
    text: "",
    fieldPath: "",
  };
}

function createEmptyGroupFooterCell(columnId: string): ReportDetailTableGroupFooterCell {
  return {
    columnId,
    contentKind: "Empty",
    text: "",
    fieldPath: "",
  };
}

function resizeAdjacentWidths<T extends { id: string } & Record<TKey, number>, TKey extends keyof T & string>(
  columns: T[],
  leftColumnId: string,
  delta: number,
  minWidth: number,
  widthKey: TKey,
): T[] {
  if (!Number.isFinite(delta) || columns.length < 2) {
    return columns;
  }

  const leftIndex = columns.findIndex((column) => column.id === leftColumnId);
  const rightIndex = leftIndex + 1;
  const leftColumn = columns[leftIndex];
  const rightColumn = columns[rightIndex];
  if (!leftColumn || !rightColumn) {
    return columns;
  }

  const leftWidth = Math.max(minWidth, Number(leftColumn[widthKey]));
  const rightWidth = Math.max(minWidth, Number(rightColumn[widthKey]));
  const pairTotal = leftWidth + rightWidth;
  if (pairTotal < minWidth * 2) {
    return columns;
  }

  const nextLeftWidth = roundDesignerWidth(clamp(leftWidth + delta, minWidth, pairTotal - minWidth));
  const nextRightWidth = roundDesignerWidth(pairTotal - nextLeftWidth);

  return columns.map((column, index) => {
    if (index === leftIndex) {
      return {
        ...column,
        [widthKey]: nextLeftWidth,
      };
    }

    if (index === rightIndex) {
      return {
        ...column,
        [widthKey]: nextRightWidth,
      };
    }

    return column;
  });
}

function clamp(value: number, min: number, max: number) {
  return Math.min(max, Math.max(min, value));
}

function roundDesignerWidth(value: number) {
  return Math.round(value * 10) / 10;
}
