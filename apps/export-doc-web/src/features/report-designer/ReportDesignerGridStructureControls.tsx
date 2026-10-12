import type { ReportGridBlock } from "./reportDesignerSchema.ts";
import { adjacentGridCell, editGridTrack, gridCellAt, type ReportGridCellLocation } from "./reportDesignerGridStructure.ts";
import { REPORT_GRID_MAX_COLUMNS, REPORT_GRID_MAX_ROWS } from "./reportDesignerGridPlacement.ts";
import { NumberField } from "./ReportDesignerV3InspectorControls.tsx";

export function GridCellNavigator({ block, locations, selected, onSelect }: {
  block: ReportGridBlock;
  locations: ReportGridCellLocation[];
  selected?: ReportGridCellLocation;
  onSelect: (cellId: string) => void;
}) {
  if (!selected) return null;
  const index = locations.indexOf(selected);
  const selectAt = (row: number, column: number) => {
    const next = gridCellAt(locations, row, column);
    if (next) onSelect(next.cell.id);
  };
  return <section className="new-report-grid-structure" aria-label="定位表格单元格">
    <div className="new-report-property-grid">
      <NumberField label="定位行" value={selected.rowIndex + 1} min={1} max={block.rows.length} step={1} onCommit={row => selectAt(row - 1, selected.columnIndex)} />
      <NumberField label="定位列" value={selected.columnIndex + 1} min={1} max={block.columns.length} step={1} onCommit={column => selectAt(selected.rowIndex, column - 1)} />
    </div>
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
    <small>也可单击画布选格；合并区域按左上角定位，方向键可跨格移动。</small>
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
