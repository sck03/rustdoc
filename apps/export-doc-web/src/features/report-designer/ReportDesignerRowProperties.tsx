import { useState } from "react";
import { ArrowDown, ArrowUp, Copy, Trash2 } from "lucide-react";
import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import type { ReportBlock, ReportRowColumn } from "./reportDesignerSchema.ts";
import { BorderEditor, ColumnWidthStrip, CommitTextField, FieldPathInput, TextStyleEditor } from "./ReportDesignerPropertyControls.tsx";
import { NumberField } from "./ReportDesignerV3InspectorControls.tsx";
import { resizeAdjacentRowColumnWidths } from "./reportDesignerTableMutations.ts";
import { MAX_ROW_COLUMNS, insertRowColumn, moveRowColumn, removeRowColumn, resizeRowColumns, setRowColumnWidth, updateRowColumn } from "./reportDesignerRowMutations.ts";

export function RowBlockProperties({ block, fieldGroups, onCommit }: {
  block: Extract<ReportBlock, { type: "Row" }>;
  fieldGroups: ReportDesignerFieldGroup[];
  onCommit: (block: ReportBlock) => void;
}) {
  const [selectedId, setSelectedId] = useState(block.columns[0]?.id ?? "");
  const column = block.columns.find(column => column.id === selectedId) ?? block.columns[0];
  if (!column) return null;
  const index = block.columns.indexOf(column);
  const update = (patch: Partial<Omit<ReportRowColumn, "id" | "widthPercent">>) => onCommit(updateRowColumn(block, column.id, patch));
  function insert(copy: boolean) {
    const next = insertRowColumn(block, copy ? column.id : undefined);
    if (next === block) return;
    setSelectedId(next.columns[copy ? index + 1 : next.columns.length - 1].id);
    onCommit(next);
  }
  return <div className="new-report-row-properties">
    <div className="new-report-property-grid">
      <NumberField label="列数" value={block.columns.length} min={1} max={MAX_ROW_COLUMNS} step={1} precision={0} onCommit={count => onCommit(resizeRowColumns(block, count))} />
      <NumberField label="上距(mm)" value={block.marginTopMm ?? 0} max={80} onCommit={marginTopMm => onCommit({ ...block, marginTopMm })} />
      <NumberField label="下距(mm)" value={block.marginBottomMm ?? 0} max={80} onCommit={marginBottomMm => onCommit({ ...block, marginBottomMm })} />
    </div>
    <ColumnWidthStrip columns={block.columns.map((column, index) => ({ id: column.id, title: `列 ${index + 1}`, width: column.widthPercent }))}
      minWidth={1} unit="%" onResizeBoundary={(id, delta) => {
        const columns = resizeAdjacentRowColumnWidths(block.columns, id, delta);
        if (columns !== block.columns) onCommit({ ...block, columns });
      }} />
    <label><span>选择要修改的列</span><select value={column.id} onChange={event => setSelectedId(event.target.value)}>
      {block.columns.map((column, index) => <option key={column.id} value={column.id}>列 {index + 1}</option>)}
    </select></label>
    <div className="new-report-detail-column-card" key={column.id}>
      <div className="new-report-detail-column-title">
        <strong>列 {index + 1}</strong>
        <div className="new-report-detail-column-actions" aria-label={`行列 ${index + 1} 操作`}>
          <button className="icon-button compact-icon-button" type="button" title="左移列" aria-label="左移列" disabled={index === 0} onClick={() => onCommit(moveRowColumn(block, column.id, -1))}><ArrowUp size={16} aria-hidden="true" /></button>
          <button className="icon-button compact-icon-button" type="button" title="右移列" aria-label="右移列" disabled={index === block.columns.length - 1} onClick={() => onCommit(moveRowColumn(block, column.id, 1))}><ArrowDown size={16} aria-hidden="true" /></button>
          <button className="icon-button compact-icon-button" type="button" title="复制列" aria-label="复制列" disabled={block.columns.length >= MAX_ROW_COLUMNS} onClick={() => insert(true)}><Copy size={16} aria-hidden="true" /></button>
          <button className="icon-button compact-icon-button danger-icon" type="button" title="删除列" aria-label="删除列" disabled={block.columns.length <= 1} onClick={() => {
            setSelectedId(block.columns[index + 1]?.id ?? block.columns[index - 1]?.id ?? "");
            onCommit(removeRowColumn(block, column.id));
          }}><Trash2 size={16} aria-hidden="true" /></button>
        </div>
      </div>
      <div className="new-report-property-grid">
        <label><span>内容</span><select value={column.contentKind} onChange={event => update({ contentKind: event.target.value === "Field" ? "Field" : "Text" })}><option value="Text">固定文本</option><option value="Field">字段</option></select></label>
        <NumberField label="宽度(%)" value={column.widthPercent} min={1} max={100} step={0.000001} precision={6} disabled={block.columns.length === 1} onCommit={value => onCommit(setRowColumnWidth(block, column.id, value))} />
        {column.contentKind === "Field" ? <>
          <label><span>标签</span><CommitTextField value={column.label ?? ""} onCommit={label => update({ label })} /></label>
          <FieldPathInput selectOnly className="new-report-property-wide" label="字段" value={column.fieldPath} fieldGroups={fieldGroups} onChange={fieldPath => update({ fieldPath })} />
          <label><span>占位文本</span><CommitTextField value={column.fallbackText ?? ""} onCommit={fallbackText => update({ fallbackText })} /></label>
        </> : <label className="new-report-property-wide"><span>固定内容</span><CommitTextField multiline rows={4} value={column.text} onCommit={text => update({ text })} /></label>}
      </div>
      <details><summary>文字与边框</summary>
        <TextStyleEditor style={column.style} onChange={style => update({ style })} />
        <BorderEditor border={column.border} onChange={border => update({ border })} />
      </details>
    </div>
    <button className="command-button secondary" type="button" disabled={block.columns.length >= MAX_ROW_COLUMNS} onClick={() => insert(false)}>新增列</button>
  </div>;
}
