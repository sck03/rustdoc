import { useEffect, useMemo, useState } from "react";
import { Minus, Plus } from "lucide-react";
import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import {
  appendGridColumn,
  appendGridRow,
  applyGridBorderToCells,
  applyGridDefaultCellStyle,
  applyGridPreset,
  canMergeGridCellDown,
  canMergeGridCellRight,
  distributeGridColumnWidths,
  getGridCellLocations,
  mergeGridCellDown,
  mergeGridCellRight,
  removeLastGridColumn,
  removeLastGridRow,
  resizeAdjacentGridColumnWidths,
  setGridRowsToUniformHeight,
  splitGridCell,
  updateGridCellBorder,
} from "./reportDesignerMutations.ts";
import type { ReportBlock, ReportGridBlock, ReportGridCell } from "./reportDesignerSchema.ts";
import { DesignerCheckbox, DesignerPropertyTabs, CommitTextField, BorderEditor, ColumnWidthStrip, FieldPathInput, TextStyleEditor } from "./ReportDesignerPropertyControls.tsx";
import { adjacentGridCell, updateGridCell } from "./reportDesignerGridMutations.ts";
import { normalizeGridCellContentKind, normalizeNumber } from "./reportDesignerPropertiesModel.ts";

export function GridBlockProperties({ block, fieldGroups, selectedCellId, onSelectCell, onCommit }: {
  block: ReportGridBlock;
  fieldGroups: ReportDesignerFieldGroup[];
  selectedCellId?: string;
  onSelectCell: (cellId: string) => void;
  onCommit: (block: ReportBlock) => void;
}) {
  const [tab, setTab] = useState<"cell" | "table">("cell");
  const locations = useMemo(() => getGridCellLocations(block), [block]);
  const selected = locations.find((location) => location.cell.id === selectedCellId) ?? locations[0];
  useEffect(() => {
    if (selected && selected.cell.id !== selectedCellId) onSelectCell(selected.cell.id);
  }, [onSelectCell, selected, selectedCellId]);

  function updateCell(update: (cell: ReportGridCell) => ReportGridCell) {
    if (!selected) return;
    onCommit(updateGridCell(block, selected.cell.id, update));
  }

  function updateCheckboxOptions(value: string) {
    const checkboxOptions = value.split(/\r?\n/).map((line) => line.trim()).filter(Boolean).map((line, index) => {
      const [label, optionValue] = line.split("=");
      return { id: `grid-option-${index + 1}`, label: (label ?? "").trim(), value: (optionValue ?? label ?? "").trim() };
    });
    updateCell((cell) => ({ ...cell, checkboxOptions }));
  }

  const selectedRow = selected ? block.rows[selected.rowIndex] : undefined;
  const canSplit = Boolean(selected && (selected.colSpan > 1 || selected.rowSpan > 1));
  return (
    <div className="new-report-grid-properties">
      <DesignerPropertyTabs value={tab} onChange={setTab} options={[{ value: "cell", label: "单元格" }, { value: "table", label: "整张表" }]}>
      {tab === "table" ? <>
      <div className="new-report-property-grid">
        <label><span>表格名称（可选）</span><CommitTextField value={block.title ?? ""} onCommit={(title) => onCommit({ ...block, title })} /></label>
        <label><span>快速版式</span><select value="" onChange={(event) => {
          if (!event.target.value) return;
          const next = applyGridPreset(block, event.target.value as "Blank" | "Form" | "Approval");
          onSelectCell(next.rows[0].cells[0].id);
          onCommit(next);
        }}><option value="">选择...</option><option value="Blank">空白 3 × 3</option><option value="Form">标签/内容表单</option><option value="Approval">审批签字栏</option></select></label>
        <label><span>上距 (mm)</span><input type="number" min={0} max={30} step={0.5} value={block.marginTopMm ?? 0} onChange={(event) => onCommit({ ...block, marginTopMm: normalizeNumber(event.target.value, block.marginTopMm ?? 0) })} /></label>
        <label><span>下距 (mm)</span><input type="number" min={0} max={30} step={0.5} value={block.marginBottomMm ?? 0} onChange={(event) => onCommit({ ...block, marginBottomMm: normalizeNumber(event.target.value, block.marginBottomMm ?? 0) })} /></label>
      </div>

      <section className="new-report-grid-structure" aria-label="表格结构">
        <div className="new-report-detail-column-title">
          <strong>表格结构</strong><small>{block.rows.length} 行 × {block.columns.length} 列</small>
        </div>
        <div className="new-report-grid-structure-actions">
          <button className="command-button secondary" type="button" onClick={() => onCommit(appendGridRow(block))}><Plus size={14} aria-hidden="true" /> 行</button>
          <button className="command-button secondary" type="button" disabled={block.rows.length <= 1} onClick={() => onCommit(removeLastGridRow(block))}><Minus size={14} aria-hidden="true" /> 行</button>
          <button className="command-button secondary" type="button" onClick={() => onCommit(appendGridColumn(block))}><Plus size={14} aria-hidden="true" /> 列</button>
          <button className="command-button secondary" type="button" disabled={block.columns.length <= 1} onClick={() => onCommit(removeLastGridColumn(block))}><Minus size={14} aria-hidden="true" /> 列</button>
        </div>
        <div className="new-report-grid-cell-picker" role="group" aria-label="选择要编辑的表格单元格" style={{ gridTemplateColumns: block.columns.map((column) => `minmax(0, ${Math.max(0.01, column.widthPercent)}fr)`).join(" ") }}>
          {locations.map(({ cell, rowIndex, columnIndex, rowSpan, colSpan }) => <button key={cell.id} type="button" data-grid-picker-cell={cell.id} tabIndex={selected?.cell.id === cell.id ? 0 : -1} className={selected?.cell.id === cell.id ? "is-selected" : ""} aria-pressed={selected?.cell.id === cell.id} aria-label={`第 ${rowIndex + 1} 行，第 ${columnIndex + 1} 列`} title={cellSummary(cell)} style={{ gridRow: `${rowIndex + 1} / span ${rowSpan}`, gridColumn: `${columnIndex + 1} / span ${colSpan}` }} onClick={() => onSelectCell(cell.id)} onKeyDown={(event) => {
            const direction = event.key === "ArrowLeft" ? "left" : event.key === "ArrowRight" ? "right" : event.key === "ArrowUp" ? "up" : event.key === "ArrowDown" ? "down" : null;
            if (!direction) return;
            event.preventDefault();
            const next = adjacentGridCell(block, cell.id, direction);
            if (next) { onSelectCell(next.cell.id); event.currentTarget.parentElement?.querySelector<HTMLElement>(`[data-grid-picker-cell="${CSS.escape(next.cell.id)}"]`)?.focus(); }
          }}>{cellSummary(cell)}</button>)}
        </div>
      </section>
      </> : null}

      {tab === "cell" && selected && selectedRow ? <section className="new-report-grid-cell-editor" aria-label="当前单元格">
        <div className="new-report-detail-column-title"><strong>第 {selected.rowIndex + 1} 行，第 {selected.columnIndex + 1} 列</strong><small>{selected.rowSpan} × {selected.colSpan} 格</small></div>
        <div className="new-report-grid-structure-actions">
          <button className="command-button secondary" type="button" disabled={!canMergeGridCellRight(block, selected.cell.id)} onClick={() => onCommit(mergeGridCellRight(block, selected.cell.id))}>向右合并</button>
          <button className="command-button secondary" type="button" disabled={!canMergeGridCellDown(block, selected.cell.id)} onClick={() => onCommit(mergeGridCellDown(block, selected.cell.id))}>向下合并</button>
          <button className="command-button secondary" type="button" disabled={!canSplit} onClick={() => onCommit(splitGridCell(block, selected.cell.id))}>拆分</button>
        </div>
        <div className="new-report-property-grid">
          <label><span>内容类型</span><select value={selected.cell.contentKind} onChange={(event) => updateCell((cell) => ({ ...cell, contentKind: normalizeGridCellContentKind(event.target.value) }))}><option value="Text">固定文本</option><option value="Field">业务字段</option><option value="CheckboxGroup">勾选组</option></select></label>
          <label><span>本行高度 (mm)</span><input type="number" min={2} max={80} step={0.5} value={selectedRow.heightMm ?? 9} onChange={(event) => onCommit({ ...block, rows: block.rows.map((row) => row.id === selectedRow.id ? { ...row, heightMm: normalizeNumber(event.target.value, row.heightMm ?? 9) } : row) })} /></label>
          <DesignerCheckbox checked={Boolean(selected.cell.verticalText)} onChange={(checked) => updateCell((cell) => ({ ...cell, verticalText: checked }))}>竖排文字</DesignerCheckbox>
        </div>
        {selected.cell.contentKind === "Text" ? <label className="new-report-property-wide"><span>文字内容</span><CommitTextField multiline rows={3} value={selected.cell.text} onCommit={(text) => updateCell((cell) => ({ ...cell, text }))} /></label> : null}
        {selected.cell.contentKind === "Field" || selected.cell.contentKind === "CheckboxGroup" ? <FieldPathInput className="new-report-property-wide" label={selected.cell.contentKind === "CheckboxGroup" ? "判断字段" : "业务字段"} value={selected.cell.fieldPath} fieldGroups={fieldGroups} onChange={(fieldPath) => updateCell((cell) => ({ ...cell, fieldPath }))} /> : null}
        {selected.cell.contentKind === "Field" ? <label><span>字段前标签（可选）</span><input value={selected.cell.label ?? ""} onChange={(event) => updateCell((cell) => ({ ...cell, label: event.target.value }))} /></label> : null}
        {selected.cell.contentKind === "CheckboxGroup" ? <label className="new-report-property-wide"><span>勾选项（每行：名称=值）</span><textarea rows={4} value={(selected.cell.checkboxOptions ?? []).map((option) => `${option.label}=${option.value}`).join("\n")} onChange={(event) => updateCheckboxOptions(event.target.value)} /></label> : null}
        <TextStyleEditor style={selected.cell.style} onChange={(style) => updateCell((cell) => ({ ...cell, style }))} />
        <BorderEditor border={selected.cell.border ?? block.border} onChange={(border) => onCommit(updateGridCellBorder(block, selected.cell.id, border))} />
      </section> : null}

      {tab === "table" ? <section className="new-report-detail-style-group" aria-label="整表样式"><strong>列宽、行高与整表样式</strong>
        <ColumnWidthStrip columns={block.columns.map((column, index) => ({ id: column.id, title: `列 ${index + 1}`, width: column.widthPercent }))} minWidth={1} unit="%" onResizeBoundary={(leftColumnId, delta) => onCommit(resizeAdjacentGridColumnWidths(block, leftColumnId, delta))} />
        <div className="new-report-grid-structure-actions">
          <button className="command-button secondary" type="button" onClick={() => onCommit(distributeGridColumnWidths(block))}>等宽列</button>
          <button className="command-button secondary" type="button" disabled={!selectedRow} onClick={() => onCommit(setGridRowsToUniformHeight(block, selectedRow?.heightMm ?? 9))}>统一行高</button>
        </div>
        <div className="new-report-designer-muted">修改整表样式会立即应用到全部单元格；之后仍可单独覆盖当前单元格。</div>
        <TextStyleEditor style={block.defaultCellStyle} onChange={(defaultCellStyle) => onCommit(applyGridDefaultCellStyle({ ...block, defaultCellStyle }))} />
        <BorderEditor border={block.border} onChange={(border) => onCommit(applyGridBorderToCells({ ...block, border }))} />
      </section> : null}
      </DesignerPropertyTabs>
      <small className="report-designer-v3-muted">单击画布选格，双击固定文本直接编辑。</small>
    </div>
  );
}

function cellSummary(cell: ReportGridCell) {
  if (cell.contentKind === "Field") return cell.label || cell.fieldPath || "字段";
  if (cell.contentKind === "CheckboxGroup") return "勾选组";
  return cell.text.trim() || "空白";
}
