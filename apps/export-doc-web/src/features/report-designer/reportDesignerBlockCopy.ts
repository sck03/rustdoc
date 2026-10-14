import type { ReportBlock } from "./reportDesignerSchema.ts";
import { createReportBlockId } from "./reportDesignerMutationUtils.ts";

/** Copy identities and their references; immutable content and styles stay shared. */
export function copyReportDesignerBlock<T extends ReportBlock>(source: T): T {
  const copyId = <U extends { id: string }>(value: U): U => ({ ...value, id: createReportBlockId("copy") });
  const block = copyId(source);
  if (block.type === "Row") return { ...block, columns: block.columns.map(copyId) };
  if (block.type === "Grid") return {
    ...block,
    columns: block.columns.map(copyId),
    rows: block.rows.map(row => ({ ...copyId(row), cells: row.cells.map(cell => ({
      ...copyId(cell), checkboxOptions: cell.checkboxOptions?.map(copyId),
    })) })),
  };
  if (block.type === "DetailTable") {
    const columns = block.columns.map(column => ({ ...copyId(column), content: column.content?.map(copyId) }));
    const ids = new Map(block.columns.map((column, index) => [column.id, columns[index].id]));
    const references = <U extends { cells: { columnId: string }[] }>(row: U | undefined): U | undefined => row && ({
      ...row, cells: row.cells.map(cell => ({ ...cell, columnId: ids.get(cell.columnId) ?? cell.columnId })),
    });
    return {
      ...block, columns, summaryRow: references(block.summaryRow), introRow: references(block.introRow),
      adjustments: block.adjustments && { descriptionColumnId: ids.get(block.adjustments.descriptionColumnId)!, amountColumnId: ids.get(block.adjustments.amountColumnId)! },
      grouping: block.grouping && { ...block.grouping, footer: references(block.grouping.footer) },
    };
  }
  return block;
}
