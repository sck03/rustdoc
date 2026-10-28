import { useEffect, useMemo, useState } from "react";
import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import {
  applyGridBorderToCells,
  applyGridDefaultCellStyle,
  applyGridPreset,
  canMergeGridCellDown,
  canMergeGridCellRight,
  distributeGridColumnWidths,
  getGridCellLocations,
  resizeGridStructure,
  resizeAdjacentGridColumnWidths,
  setGridRowsToUniformHeight,
  splitGridCell,
  updateGridCellBorder,
} from "./reportDesignerMutations.ts";
import type { ReportBlock, ReportGridBlock, ReportGridCell } from "./reportDesignerSchema.ts";
import { DesignerCheckbox, DesignerPropertyTabs, CommitTextField, BorderEditor, ColumnWidthStrip, FieldPathInput, TextStyleEditor } from "./ReportDesignerPropertyControls.tsx";
import { updateGridCell } from "./reportDesignerGridMutations.ts";
import { GridCellNavigator, GridTrackActions } from "./ReportDesignerGridStructureControls.tsx";
import { normalizeGridCellContentKind } from "./reportDesignerPropertiesModel.ts";
import { NumberField, SelectField } from "./ReportDesignerV3InspectorControls.tsx";
import { REPORT_GRID_MAX_COLUMNS, REPORT_GRID_MAX_ROWS } from "./reportDesignerGridPlacement.ts";
import { resolveGridSelection } from "./reportDesignerGridSelection.ts";
import { mergeGridCell } from "./reportDesignerGridStructure.ts";
import { GridRangeProperties } from "./ReportDesignerGridRangeProperties.tsx";
import { GridTextImport } from "./ReportDesignerGridTextImport.tsx";

export function GridBlockProperties({ block, fieldGroups, selectedCellId, selectedEndCellId, onSelectCell, onCommit, widthMm }: {
  block: ReportGridBlock;
  fieldGroups: ReportDesignerFieldGroup[];
  selectedCellId?: string;
  selectedEndCellId?: string;
  onSelectCell: (cellId: string, endCellId?: string) => void;
  onCommit: (block: ReportBlock) => void;
  widthMm: number;
}) {
  const [tab, setTab] = useState<"cell" | "table">("cell");
  const [widthColumnId, setWidthColumnId] = useState(block.columns[0]?.id ?? "");
  const [mergeNotice, setMergeNotice] = useState<string | null>(null);
  const locations = useMemo(() => getGridCellLocations(block), [block]);
  const selected = locations.find((location) => location.cell.id === selectedCellId) ?? locations[0];
  const selection = useMemo(() => selected ? resolveGridSelection(locations, { cellId: selected.cell.id, endCellId: selectedEndCellId }) : null,
    [locations, selected, selectedEndCellId]);
  const multiple = Boolean(selection && selection.cells.length > 1);
  useEffect(() => setMergeNotice(null), [block, selectedCellId, selectedEndCellId]);
  useEffect(() => {
    if (selected && selected.cell.id !== selectedCellId) onSelectCell(selected.cell.id);
  }, [onSelectCell, selected, selectedCellId]);

  function updateCell(update: (cell: ReportGridCell) => ReportGridCell) {
    if (!selected) return;
    onCommit(updateGridCell(block, selected.cell.id, update));
  }

  function merge(direction: "right" | "down") {
    if (!selected) return;
    const result = mergeGridCell(block, selected.cell.id, direction);
    setMergeNotice(result.notice);
    onCommit(result.block);
  }

  function updateCheckboxOptions(value: string) {
    const checkboxOptions = value.split(/\r?\n/).map((line) => line.trim()).filter(Boolean).map((line, index) => {
      const separator = line.indexOf("=");
      return { id: selected?.cell.checkboxOptions?.[index]?.id ?? `grid-option-${index + 1}`,
        label: (separator < 0 ? line : line.slice(0, separator)).trim(), value: (separator < 0 ? line : line.slice(separator + 1)).trim() };
    });
    updateCell((cell) => ({ ...cell, checkboxOptions }));
  }

  const selectedRow = selected ? block.rows[selected.rowIndex] : undefined;
  const canSplit = Boolean(selected && (selected.colSpan > 1 || selected.rowSpan > 1));
  const widthIndex = Math.max(0, block.columns.findIndex(column => column.id === widthColumnId));
  const widthColumn = block.columns[widthIndex];
  const nextWidthColumn = block.columns[widthIndex + 1];
  const adjacentWidthColumn = nextWidthColumn ?? block.columns[widthIndex - 1];
  const resizeColumn = (value: number) => onCommit(resizeAdjacentGridColumnWidths(block, nextWidthColumn ? widthColumn.id : adjacentWidthColumn.id, (value - widthColumn.widthPercent) * (nextWidthColumn ? 1 : -1)));
  const maximumPercent = widthColumn.widthPercent + (adjacentWidthColumn?.widthPercent ?? 0) - 1;
  return (
    <div className="new-report-grid-properties">
      <GridCellNavigator block={block} locations={locations} selected={selected} selection={selection} onSelect={onSelectCell} />
      <GridTextImport block={block} selected={selected} onCommit={onCommit} />
      {mergeNotice && <small role="alert">{mergeNotice}</small>}
      <DesignerPropertyTabs value={tab} onChange={setTab} options={[{ value: "cell", label: "单元格" }, { value: "table", label: "整张表" }]}>
      {tab === "table" ? <>
      <div className="new-report-property-grid">
        <label><span>表格名称（可选）</span><CommitTextField value={block.title ?? ""} onCommit={(title) => onCommit({ ...block, title })} /></label>
        <label><input type="checkbox" checked={block.growToContent ?? false} onChange={event => onCommit({ ...block, growToContent: event.target.checked })} />按内容自动增高并续页（横排文字或字段，无跨行合并）</label>
        <label><span>快速版式</span><select value="" onChange={(event) => {
          if (!event.target.value) return;
          const next = applyGridPreset(block, event.target.value as "Blank" | "Form" | "Approval");
          onSelectCell(next.rows[0].cells[0].id);
          onCommit(next);
        }}><option value="">选择...</option><option value="Blank">空白 3 × 3</option><option value="Form">标签/内容表单</option><option value="Approval">审批签字栏</option></select></label>
        <NumberField label="上距 (mm)" min={0} max={30} value={block.marginTopMm ?? 0} onCommit={marginTopMm => onCommit({ ...block, marginTopMm })} />
        <NumberField label="下距 (mm)" min={0} max={30} value={block.marginBottomMm ?? 0} onCommit={marginBottomMm => onCommit({ ...block, marginBottomMm })} />
      </div>

      <section className="new-report-grid-structure" aria-label="表格结构">
        <div className="new-report-detail-column-title">
          <strong>表格结构</strong><small>{block.rows.length} 行 × {block.columns.length} 列</small>
        </div>
        <div className="new-report-property-grid">
          <NumberField label="行数" min={1} max={REPORT_GRID_MAX_ROWS} step={1} value={block.rows.length} onCommit={rows => onCommit(resizeGridStructure(block, rows, block.columns.length))} />
          <NumberField label="列数" min={1} max={REPORT_GRID_MAX_COLUMNS} step={1} value={block.columns.length} onCommit={columns => onCommit(resizeGridStructure(block, block.rows.length, columns))} />
        </div>
        <GridTrackActions block={block} selected={selected} onCommit={onCommit} />
      </section>
      </> : null}

      {tab === "cell" && multiple && selection ? <GridRangeProperties block={block} selected={selection} onCommit={onCommit} onNotice={setMergeNotice} /> : null}
      {tab === "cell" && !multiple && selected && selectedRow ? <section key={selected.cell.id} className="new-report-grid-cell-editor" aria-label="当前单元格">
        <div className="new-report-detail-column-title"><strong>第 {selected.rowIndex + 1} 行，第 {selected.columnIndex + 1} 列</strong><small>{selected.rowSpan} × {selected.colSpan} 格</small></div>
        <div className="new-report-grid-structure-actions">
          <button className="command-button secondary" type="button" disabled={!canMergeGridCellRight(block, selected.cell.id, locations)} onClick={() => merge("right")}>向右合并</button>
          <button className="command-button secondary" type="button" disabled={!canMergeGridCellDown(block, selected.cell.id, locations)} onClick={() => merge("down")}>向下合并</button>
          <button className="command-button secondary" type="button" disabled={!canSplit} onClick={() => onCommit(splitGridCell(block, selected.cell.id))}>拆分</button>
        </div>
        <div className="new-report-property-grid">
          <label><span>内容类型</span><select value={selected.cell.contentKind} onChange={(event) => updateCell((cell) => ({ ...cell, contentKind: normalizeGridCellContentKind(event.target.value) }))}><option value="Text">固定文本</option><option value="Field">业务字段</option><option value="CheckboxGroup">勾选组</option></select></label>
          <NumberField label="本行高度 (mm)" min={2} max={80} value={selectedRow.heightMm ?? 9} onCommit={heightMm => onCommit({ ...block, rows: block.rows.map(row => row.id === selectedRow.id ? { ...row, heightMm } : row) })} />
          <DesignerCheckbox checked={Boolean(selected.cell.verticalText)} onChange={(checked) => updateCell((cell) => ({ ...cell, verticalText: checked }))}>竖排文字</DesignerCheckbox>
          <DesignerCheckbox checked={Boolean(selected.cell.diagonalHeader)} onChange={(checked) => updateCell((cell) => ({
            ...cell,
            diagonalHeader: checked ? cell.diagonalHeader ?? { upperLeftText: "", lowerRightText: "" } : undefined,
          }))}>斜线表头</DesignerCheckbox>
        </div>
        {selected.cell.diagonalHeader ? <div className="new-report-property-grid">
          <label><span>斜线方向</span><select value={selected.cell.diagonalHeader.direction ?? "Up"} onChange={(event) => updateCell(cell => ({...cell,diagonalHeader:{...cell.diagonalHeader!,direction:event.target.value === "Down" ? "Down" : "Up"}}))}><option value="Up">左下至右上</option><option value="Down">左上至右下</option></select></label>
          <label><span>斜线上方文字</span><CommitTextField value={selected.cell.diagonalHeader.upperLeftText} onCommit={upperLeftText => updateCell(cell => ({ ...cell, diagonalHeader: { ...cell.diagonalHeader!, upperLeftText } }))} /></label>
          <label><span>斜线下方文字</span><CommitTextField value={selected.cell.diagonalHeader.lowerRightText} onCommit={lowerRightText => updateCell(cell => ({ ...cell, diagonalHeader: { ...cell.diagonalHeader!, lowerRightText } }))} /></label>
        </div> : null}
        {selected.cell.contentKind === "Text" ? <label className="new-report-property-wide"><span>文字内容</span><CommitTextField multiline rows={3} value={selected.cell.text} onCommit={(text) => updateCell((cell) => ({ ...cell, text }))} /></label> : null}
        {selected.cell.contentKind === "Field" || selected.cell.contentKind === "CheckboxGroup" ? <FieldPathInput selectOnly className="new-report-property-wide" label={selected.cell.contentKind === "CheckboxGroup" ? "判断字段" : "业务字段"} value={selected.cell.fieldPath} fieldGroups={fieldGroups} onChange={(fieldPath) => updateCell((cell) => ({ ...cell, fieldPath }))} /> : null}
        {selected.cell.contentKind === "Field" ? <label><span>字段前标签（可选）</span><CommitTextField value={selected.cell.label ?? ""} onCommit={label => updateCell(cell => ({ ...cell, label }))} /></label> : null}
        {selected.cell.contentKind === "Field" ? <label><span>标签位置</span><select value={selected.cell.labelPosition ?? "Inline"} onChange={event => updateCell(cell => ({...cell,labelPosition:event.target.value === "Above" || event.target.value === "Prefix" ? event.target.value : "Inline"}))}><option value="Inline">与内容同一行</option><option value="Above">在内容上方</option><option value="Prefix">与内容直接连写</option></select></label> : null}
        {selected.cell.contentKind === "CheckboxGroup" ? <label className="new-report-property-wide"><span>勾选项（每行：名称=值）</span><CommitTextField multiline rows={4} value={(selected.cell.checkboxOptions ?? []).map(option => `${option.label}=${option.value}`).join("\n")} onCommit={updateCheckboxOptions} /></label> : null}
        <TextStyleEditor style={selected.cell.style} onChange={(style) => updateCell((cell) => ({ ...cell, style }))} />
        <BorderEditor border={selected.cell.border ?? block.border} onChange={(border) => onCommit(updateGridCellBorder(block, selected.cell.id, border))} />
      </section> : null}

      {tab === "table" ? <section className="new-report-detail-style-group" aria-label="整表样式"><strong>列宽、行高与整表样式</strong>
        <ColumnWidthStrip columns={block.columns.map((column, index) => ({ id: column.id, title: `列 ${index + 1}`, width: column.widthPercent }))} minWidth={1} unit="%" onResizeBoundary={(leftColumnId, delta) => onCommit(resizeAdjacentGridColumnWidths(block, leftColumnId, delta))} />
        {widthColumn && <details className="report-designer-property-section"><summary>精确列宽</summary>
          <SelectField label="调整列" value={widthColumn.id} options={block.columns.map((column, index) => ({ value: column.id, label: `第 ${index + 1} 列` }))} onChange={setWidthColumnId} />
          <NumberField label="列宽 (mm)" value={widthColumn.widthPercent * widthMm / 100} min={widthMm / 100} max={maximumPercent * widthMm / 100} disabled={!adjacentWidthColumn} onCommit={value => resizeColumn(value / widthMm * 100)} />
          <NumberField label="列宽 (%)" value={widthColumn.widthPercent} min={1} max={maximumPercent} step={0.000001} precision={6} disabled={!adjacentWidthColumn} onCommit={resizeColumn} />
        </details>}
        <div className="new-report-grid-structure-actions">
          <button className="command-button secondary" type="button" onClick={() => onCommit(distributeGridColumnWidths(block))}>等宽列</button>
          <button className="command-button secondary" type="button" disabled={!selectedRow} onClick={() => onCommit(setGridRowsToUniformHeight(block, selectedRow?.heightMm ?? 9))}>统一行高</button>
        </div>
        <TextStyleEditor style={block.defaultCellStyle} onChange={(defaultCellStyle) => onCommit(applyGridDefaultCellStyle({ ...block, defaultCellStyle }))} />
        <BorderEditor border={block.border} onChange={(border) => onCommit(applyGridBorderToCells({ ...block, border }))} />
      </section> : null}
      </DesignerPropertyTabs>
    </div>
  );
}
