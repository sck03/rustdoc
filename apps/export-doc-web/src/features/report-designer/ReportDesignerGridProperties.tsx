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
} from "./reportDesignerMutations.ts";
import type { ReportBlock, ReportGridBlock, ReportGridCell } from "./reportDesignerSchema.ts";
import { BorderEditor, ColumnWidthStrip, FieldPathInput, TextStyleEditor } from "./ReportDesignerPropertyControls.tsx";
import { normalizeGridCellContentKind, normalizeNumber } from "./reportDesignerPropertiesModel.ts";

export function GridBlockProperties({ block, fieldGroups, onCommit }: {
  block: ReportGridBlock;
  fieldGroups: ReportDesignerFieldGroup[];
  onCommit: (block: ReportBlock) => void;
}) {
  const locations = useMemo(() => getGridCellLocations(block), [block]);
  const [selectedCellId, setSelectedCellId] = useState(locations[0]?.cell.id ?? "");
  const selected = locations.find((location) => location.cell.id === selectedCellId) ?? locations[0];
  useEffect(() => {
    if (selected && selected.cell.id !== selectedCellId) setSelectedCellId(selected.cell.id);
  }, [selected, selectedCellId]);

  function updateCell(update: (cell: ReportGridCell) => ReportGridCell) {
    if (!selected) return;
    onCommit({
      ...block,
      rows: block.rows.map((row) => ({
        ...row,
        cells: row.cells.map((cell) => cell.id === selected.cell.id ? update(cell) : cell),
      })),
    });
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
      <div className="new-report-property-grid">
        <label><span>表格名称（可选）</span><input value={block.title ?? ""} onChange={(event) => onCommit({ ...block, title: event.target.value })} /></label>
        <label><span>快速版式</span><select value="" onChange={(event) => {
          if (!event.target.value) return;
          const next = applyGridPreset(block, event.target.value as "Blank" | "Form" | "Approval");
          setSelectedCellId(next.rows[0].cells[0].id);
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
        <table className="new-report-grid-cell-picker" aria-label="选择要编辑的表格单元格">
          <colgroup>{block.columns.map((column) => <col key={column.id} style={{ width: `${column.widthPercent}%` }} />)}</colgroup>
          <tbody>{block.rows.map((row, rowIndex) => <tr key={row.id}>{row.cells.map((cell) => {
            const location = locations.find((candidate) => candidate.cell.id === cell.id);
            if (!location) return null;
            return <td key={cell.id} colSpan={location.colSpan} rowSpan={location.rowSpan}><button type="button" className={selected?.cell.id === cell.id ? "is-selected" : ""} aria-pressed={selected?.cell.id === cell.id} aria-label={`第 ${rowIndex + 1} 行，第 ${location.columnIndex + 1} 列`} onClick={() => setSelectedCellId(cell.id)}>{cellSummary(cell)}</button></td>;
          })}</tr>)}</tbody>
        </table>
      </section>

      {selected && selectedRow ? <section className="new-report-grid-cell-editor" aria-label="当前单元格">
        <div className="new-report-detail-column-title"><strong>第 {selected.rowIndex + 1} 行，第 {selected.columnIndex + 1} 列</strong><small>{selected.rowSpan} × {selected.colSpan} 格</small></div>
        <div className="new-report-grid-structure-actions">
          <button className="command-button secondary" type="button" disabled={!canMergeGridCellRight(block, selected.cell.id)} onClick={() => onCommit(mergeGridCellRight(block, selected.cell.id))}>向右合并</button>
          <button className="command-button secondary" type="button" disabled={!canMergeGridCellDown(block, selected.cell.id)} onClick={() => onCommit(mergeGridCellDown(block, selected.cell.id))}>向下合并</button>
          <button className="command-button secondary" type="button" disabled={!canSplit} onClick={() => onCommit(splitGridCell(block, selected.cell.id))}>拆分</button>
        </div>
        <div className="new-report-property-grid">
          <label><span>内容类型</span><select value={selected.cell.contentKind} onChange={(event) => updateCell((cell) => ({ ...cell, contentKind: normalizeGridCellContentKind(event.target.value) }))}><option value="Text">固定文本</option><option value="Field">业务字段</option><option value="CheckboxGroup">勾选组</option></select></label>
          <label><span>本行高度 (mm)</span><input type="number" min={2} max={80} step={0.5} value={selectedRow.heightMm ?? 9} onChange={(event) => onCommit({ ...block, rows: block.rows.map((row) => row.id === selectedRow.id ? { ...row, heightMm: normalizeNumber(event.target.value, row.heightMm ?? 9) } : row) })} /></label>
          <label className="new-report-checkbox-label"><span>竖排文字</span><input type="checkbox" checked={Boolean(selected.cell.verticalText)} onChange={(event) => updateCell((cell) => ({ ...cell, verticalText: event.target.checked }))} /></label>
        </div>
        {selected.cell.contentKind === "Text" ? <label className="new-report-property-wide"><span>文本</span><textarea rows={2} value={selected.cell.text} onChange={(event) => updateCell((cell) => ({ ...cell, text: event.target.value }))} /></label> : null}
        {selected.cell.contentKind === "Field" || selected.cell.contentKind === "CheckboxGroup" ? <FieldPathInput className="new-report-property-wide" label={selected.cell.contentKind === "CheckboxGroup" ? "判断字段" : "业务字段"} value={selected.cell.fieldPath} fieldGroups={fieldGroups} onChange={(fieldPath) => updateCell((cell) => ({ ...cell, fieldPath }))} /> : null}
        {selected.cell.contentKind === "Field" ? <label><span>字段前标签（可选）</span><input value={selected.cell.label ?? ""} onChange={(event) => updateCell((cell) => ({ ...cell, label: event.target.value }))} /></label> : null}
        {selected.cell.contentKind === "CheckboxGroup" ? <label className="new-report-property-wide"><span>勾选项（每行：名称=值）</span><textarea rows={4} value={(selected.cell.checkboxOptions ?? []).map((option) => `${option.label}=${option.value}`).join("\n")} onChange={(event) => updateCheckboxOptions(event.target.value)} /></label> : null}
        <details className="new-report-grid-cell-details"><summary>单元格样式与边框</summary><TextStyleEditor style={selected.cell.style} onChange={(style) => updateCell((cell) => ({ ...cell, style }))} /><BorderEditor border={selected.cell.border ?? block.border} onChange={(border) => updateCell((cell) => ({ ...cell, border }))} /></details>
      </section> : null}

      <details className="new-report-detail-style-group"><summary>列宽与整表样式</summary>
        <ColumnWidthStrip columns={block.columns.map((column, index) => ({ id: column.id, title: `列 ${index + 1}`, width: column.widthPercent }))} minWidth={1} unit="%" onResizeBoundary={(leftColumnId, delta) => onCommit(resizeAdjacentGridColumnWidths(block, leftColumnId, delta))} />
        <div className="new-report-grid-structure-actions">
          <button className="command-button secondary" type="button" onClick={() => onCommit(distributeGridColumnWidths(block))}>等宽列</button>
          <button className="command-button secondary" type="button" disabled={!selectedRow} onClick={() => onCommit(setGridRowsToUniformHeight(block, selectedRow?.heightMm ?? 9))}>统一行高</button>
          <button className="command-button secondary" type="button" onClick={() => onCommit(applyGridDefaultCellStyle(block))}>套用样式</button>
          <button className="command-button secondary" type="button" onClick={() => onCommit(applyGridBorderToCells(block))}>套用边框</button>
        </div>
        <TextStyleEditor style={block.defaultCellStyle} onChange={(defaultCellStyle) => onCommit({ ...block, defaultCellStyle })} />
        <BorderEditor border={block.border} onChange={(border) => onCommit({ ...block, border })} />
      </details>
    </div>
  );
}

function cellSummary(cell: ReportGridCell) {
  if (cell.contentKind === "Field") return cell.label || cell.fieldPath || "字段";
  if (cell.contentKind === "CheckboxGroup") return "勾选组";
  return cell.text.trim() || "空白";
}
