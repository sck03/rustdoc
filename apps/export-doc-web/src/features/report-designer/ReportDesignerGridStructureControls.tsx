import { useState } from "react";
import type { ReportGridBlock } from "./reportDesignerSchema.ts";
import { adjacentGridCell, editGridTrack, formatGridCellAddress, gridCellAt, parseGridCellAddress, type ReportGridCellLocation } from "./reportDesignerGridStructure.ts";
import { REPORT_GRID_MAX_COLUMNS, REPORT_GRID_MAX_ROWS } from "./reportDesignerGridPlacement.ts";
import { CommitTextField } from "./ReportDesignerPropertyControls.tsx";
import type { ResolvedGridSelection } from "./reportDesignerGridSelection.ts";

export function GridCellNavigator({ block, locations, selected, selection, onSelect }: {
  block: ReportGridBlock;
  locations: ReportGridCellLocation[];
  selected?: ReportGridCellLocation;
  selection?: ResolvedGridSelection | null;
  onSelect: (cellId: string, endCellId?: string) => void;
}) {
  const [error, setError] = useState("");
  if (!selected) return null;
  const index = locations.indexOf(selected);
  const range = selection && selection.cells.length > 1 ? selection.range : null;
  const value = range ? `${formatGridCellAddress(range.top, range.left)}:${formatGridCellAddress(range.bottom - 1, range.right - 1)}`
    : formatGridCellAddress(selected.rowIndex, selected.columnIndex);
  const selectAt = (value: string) => {
    const addresses = value.split(/[:：]/).map(parseGridCellAddress);
    const cells = addresses.map(address => address && gridCellAt(locations, address.rowIndex, address.columnIndex));
    const valid = cells.length <= 2 && cells.every(Boolean);
    setError(valid ? "" : `请输入 A1 至 ${formatGridCellAddress(block.rows.length - 1, block.columns.length - 1)} 内的位置或区域，例如 A1:C3。`);
    if (valid) onSelect(cells[0]!.cell.id, cells[1]?.cell.id);
  };
  return <section className="new-report-grid-structure" aria-label="定位表格单元格" onFocusCapture={() => setError("")}>
    <label><span>单元格位置</span><CommitTextField value={value} placeholder="D4、4,4 或 A1:C3" onCommit={selectAt} /></label>
    {error && <small role="alert">{error}</small>}
    <div className="new-report-grid-structure-actions" onKeyDown={event => {
      const direction = event.key === "ArrowLeft" ? "left" : event.key === "ArrowRight" ? "right" : event.key === "ArrowUp" ? "up" : event.key === "ArrowDown" ? "down" : null;
      if (!direction) return;
      event.preventDefault();
      const next = adjacentGridCell(block, event.shiftKey && selection ? selection.end.cell.id : selected.cell.id, direction, locations);
      if (next) onSelect(event.shiftKey ? selected.cell.id : next.cell.id, event.shiftKey ? next.cell.id : undefined);
    }}>
      <button type="button" className="command-button secondary" disabled={index <= 0} onClick={() => onSelect(locations[index - 1].cell.id)}>上一格</button>
      <button type="button" className="command-button secondary" disabled={index >= locations.length - 1} onClick={() => onSelect(locations[index + 1].cell.id)}>下一格</button>
    </div>
    <small>输入位置或 A1:C3 后按 Enter；Shift+单击画布格或 Shift+方向键扩展选区，完整包含接触的合并格。</small>
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
