import type { ReportDetailTableBlock } from "./reportDesignerSchema.ts";
import { DesignerCheckbox } from "./ReportDesignerPropertyControls.tsx";
import { createDetailTableSummaryRow } from "./reportDesignerBlockFactories.ts";

export function ReportDesignerDetailAdjustmentsProperties({ block, onCommit }: { block: ReportDetailTableBlock; onCommit: (block: ReportDetailTableBlock) => void }) {
  function update(adjustments: ReportDetailTableBlock["adjustments"]) {
    let summaryRow = block.summaryRow;
    if (adjustments) {
      const existing = summaryRow ?? createDetailTableSummaryRow(block.columns);
      const cells = existing.cells.filter(cell => cell.columnId !== adjustments.amountColumnId);
      if (block.adjustments?.amountColumnId !== adjustments.amountColumnId) {
        const old = cells.find(cell => cell.columnId === block.adjustments?.amountColumnId && cell.fieldPath === "Invoice.AdjustedTotalAmount");
        if (old) cells.splice(cells.indexOf(old), 1);
      }
      summaryRow = { ...existing, cells: [...cells, { columnId: adjustments.amountColumnId, contentKind: "Field", fieldPath: "Invoice.AdjustedTotalAmount", fieldFormat: "Currency", text: "" }] };
    }
    onCommit({ ...block, adjustments, summaryRow });
  }
  return <>
    <DesignerCheckbox checked={Boolean(block.adjustments)} disabled={block.columns.length < 2} onChange={enabled => update(enabled ? { descriptionColumnId: block.columns[0].id, amountColumnId: block.columns.at(-1)!.id } : undefined)}>显示发票金额调整</DesignerCheckbox>
    {block.adjustments && ([['descriptionColumnId', '说明列'], ['amountColumnId', '金额列']] as const).map(([key, label]) => <label key={key}><span>{label}</span><select value={block.adjustments![key]} onChange={event => update({ ...block.adjustments!, [key]: event.target.value })}>
      {block.columns.map(column => <option key={column.id} value={column.id} disabled={column.id === block.adjustments![key === 'descriptionColumnId' ? 'amountColumnId' : 'descriptionColumnId']}>{column.title || '未命名列'}</option>)}
    </select></label>)}
  </>;
}
