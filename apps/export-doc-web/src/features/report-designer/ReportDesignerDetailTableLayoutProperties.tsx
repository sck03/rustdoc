import { useState } from "react";
import { applyDetailTableBorderToColumns, clearDetailTableColumnBorders, distributeDetailTableColumnWidths, resizeAdjacentDetailTableColumnWidths } from "./reportDesignerTableMutations.ts";
import type { ReportBlock, ReportDetailTableBlock } from "./reportDesignerSchema.ts";
import { BorderEditor, DesignerCheckbox, ColumnWidthStrip, TextStyleEditor } from "./ReportDesignerPropertyControls.tsx";

export function ReportDesignerDetailTableLayoutProperties({ block, onCommit }: {
  block: ReportDetailTableBlock;
  onCommit: (block: ReportBlock) => void;
}) {
  const [textTarget, setTextTarget] = useState<"headerStyle" | "bodyStyle">("bodyStyle");
  const width = Math.round(block.columns.reduce((sum, column) => sum + column.widthMm, 0) * 10) / 10;
  return <>
    <label><span>设置文字样式</span><select value={textTarget} onChange={event => setTextTarget(event.target.value === "headerStyle" ? "headerStyle" : "bodyStyle")}><option value="bodyStyle">商品内容</option><option value="headerStyle">表头标题</option></select></label>
    <TextStyleEditor style={block[textTarget]} onChange={style => onCommit({ ...block, [textTarget]: style })} />
    <details className="new-report-detail-style-group">
      <summary>表格边框</summary>
      <BorderEditor border={block.border} onChange={border => onCommit({ ...block, border })} />
      <DesignerCheckbox checked={block.rowSeparators !== false} onChange={rowSeparators => onCommit({ ...block, rowSeparators })}>商品之间显示横线</DesignerCheckbox>
      <DesignerCheckbox checked={block.print.fillHeight === true} onChange={fillHeight => onCommit({ ...block, print: { ...block.print, fillHeight } })}>边框延伸到表格底部</DesignerCheckbox>
      <div className="new-report-detail-column-actions">
        <button className="command-button secondary" type="button" onClick={() => onCommit(applyDetailTableBorderToColumns(block))}>统一边框</button>
        <button className="command-button secondary" type="button" onClick={() => onCommit(clearDetailTableColumnBorders(block))}>所有列沿用表格边框</button>
      </div>
    </details>
    <details className="new-report-detail-style-group">
      <summary>调整列宽 · {block.columns.length} 列</summary>
      <div className="new-report-property-readout"><span>参考总宽</span><strong>{width} mm</strong></div>
      <ColumnWidthStrip columns={block.columns.map((column, index) => ({ id: column.id, title: column.title || `列 ${index + 1}`, width: column.widthMm }))} minWidth={8} unit="mm" onResizeBoundary={(leftColumnId, delta) => onCommit(resizeAdjacentDetailTableColumnWidths(block, leftColumnId, delta))} />
      <button className="command-button secondary" type="button" onClick={() => onCommit(distributeDetailTableColumnWidths(block))}>等分列宽</button>
      <small>列宽按比例适配表格；拖动分隔线或用方向键微调。</small>
    </details>
  </>;
}
