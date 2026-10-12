import { useState } from "react";
import type { ReportGridBlock } from "./reportDesignerSchema.ts";
import { adjacentGridCell, editGridTrack, formatGridCellAddress, gridCellAt, parseGridCellAddress, type ReportGridCellLocation } from "./reportDesignerGridStructure.ts";
import { REPORT_GRID_MAX_COLUMNS, REPORT_GRID_MAX_ROWS } from "./reportDesignerGridPlacement.ts";
import { CommitTextField } from "./ReportDesignerPropertyControls.tsx";

export function GridCellNavigator({ block, locations, selected, onSelect }: {
  block: ReportGridBlock;
  locations: ReportGridCellLocation[];
  selected?: ReportGridCellLocation;
  onSelect: (cellId: string) => void;
}) {
  const [error, setError] = useState("");
  if (!selected) return null;
  const index = locations.indexOf(selected);
  const selectAt = (value: string) => {
    const address = parseGridCellAddress(value);
    const next = address && gridCellAt(locations, address.rowIndex, address.columnIndex);
    setError(next ? "" : `请输入 A1 至 ${formatGridCellAddress(block.rows.length - 1, block.columns.length - 1)} 范围内的位置。`);
    if (next) onSelect(next.cell.id);
  };
  return <section className="new-report-grid-structure" aria-label="定位表格单元格" onFocusCapture={() => setError("")}>
    <label><span>单元格位置</span><CommitTextField value={formatGridCellAddress(selected.rowIndex, selected.columnIndex)} placeholder="D4 或 4,4" onCommit={selectAt} /></label>
    {error && <small role="alert">{error}</small>}
    <div className="new-report-grid-structure-actions" onKeyDown={event => {
      const direction = event.key === "ArrowLeft" ? "left" : event.key === "ArrowRight" ? "right" : event.key === "ArrowUp" ? "up" : event.key === "ArrowDown" ? "down" : null;
      if (!direction) return;
      event.preventDefault();
      const next = adjacentGridCell(block, selected.cell.id, direction, locations);
      if (next) onSelect(next.cell.id);
    }}>
      <button type="button" className="command-button secondary" disabled={index <= 0} onClick={() => onSelect(locations[index - 1].cell.id)}>上一格</button>
      <button type="button" className="command-button secondary" disabled={index >= locations.length - 1} onClick={() => onSelect(locations[index + 1].cell.id)}>下一格</button>
    </div>
    <small>输入 D4 或 4,4（行,列）后按 Enter；合并格显示左上角，按钮方向键可跨格移动。</small>
  </section>;
}

export function GridTrackActions({ block, selected, onCommit }: {
  block: ReportGridBlock;
  selected?: ReportGridCellLocation;
  onCommit: (block: ReportGridBlock) => void;
}) {
  if (!selected) return null;
  return <div className="new-report-grid-structure-actions" role="group" aria-label="插入或删除当前行列">
    {(["row", "column"] as const).map(axis => {
      const row = axis === "row";
      const index = row ? selected.rowIndex : selected.columnIndex;
      const span = row ? selected.rowSpan : selected.colSpan;
      const count = row ? block.rows.length : block.columns.length;
      const maximum = row ? REPORT_GRID_MAX_ROWS : REPORT_GRID_MAX_COLUMNS;
      return <span key={axis} className="new-report-grid-structure-actions">
        <button type="button" className="command-button secondary" disabled={count >= maximum} onClick={() => onCommit(editGridTrack(block, axis, index, "insert"))}>{row ? "上方插入行" : "左侧插入列"}</button>
        <button type="button" className="command-button secondary" disabled={count >= maximum} onClick={() => onCommit(editGridTrack(block, axis, index + span, "insert"))}>{row ? "下方插入行" : "右侧插入列"}</button>
        <button type="button" className="command-button secondary" disabled={count <= 1} title={`删除第 ${index + 1} ${row ? "行" : "列"}，跨越它的合并区域自动缩减；可撤销`} onClick={() => onCommit(editGridTrack(block, axis, index, "delete"))}>{row ? "删除当前行" : "删除当前列"}</button>
      </span>;
    })}
  </div>;
}
