import { useState } from "react";
import { ArrowDown, ArrowUp, Copy, Trash2 } from "lucide-react";
import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import { addDetailTableColumn, duplicateDetailTableColumn, moveDetailTableColumn, removeDetailTableColumn, setDetailColumnContentKind } from "./reportDesignerTableMutations.ts";
import type { ReportBlock, ReportDetailTableBlock, ReportDetailTableColumn } from "./reportDesignerSchema.ts";
import { normalizeAlign } from "./reportDesignerPropertiesModel.ts";
import { BorderEditor, CommitTextField, FieldPathInput, DesignerCheckbox } from "./ReportDesignerPropertyControls.tsx";
import { NumberField } from "./ReportDesignerV3InspectorControls.tsx";
import { ReportDesignerDetailCompositeProperties } from "./ReportDesignerDetailCompositeProperties.tsx";

export function ReportDesignerDetailTableColumnsProperties({ block, fieldGroups, onCommit }: {
  block: ReportDetailTableBlock;
  fieldGroups: ReportDesignerFieldGroup[];
  onCommit: (block: ReportBlock) => void;
}) {
  const [selectedId, setSelectedId] = useState(block.columns[0]?.id ?? "");
  const column = block.columns.find(column => column.id === selectedId) ?? block.columns[0];
  const index = block.columns.indexOf(column);
  const fields = new Map(fieldGroups.flatMap(group => group.fields).map(field => [field.value, field.label]));
  function updateColumn(update: Partial<ReportDetailTableColumn>) {
    onCommit({ ...block, columns: block.columns.map(current => current.id === column.id ? { ...current, ...update } : current) });
  }
  function insert(copy: boolean) {
    const next = copy ? duplicateDetailTableColumn(block, column.id) : addDetailTableColumn(block);
    setSelectedId(next.columns[copy ? index + 1 : next.columns.length - 1].id);
    onCommit(next);
  }
  if (!column) return null;
  return <>
    <label><span>选择要修改的列</span><select value={column.id} onChange={event => setSelectedId(event.target.value)}>
      {block.columns.map((item, index) => <option key={item.id} value={item.id}>{index + 1}. {item.title || "未命名列"}</option>)}
    </select></label>
    <div className="new-report-detail-column-card">
      <div className="new-report-detail-column-title">
        <strong>{column.title || `列 ${index + 1}`}</strong>
        <div className="new-report-detail-column-actions" aria-label={`列 ${index + 1} 操作`}>
          <button className="icon-button compact-icon-button" type="button" title="上移列" aria-label="上移列" disabled={index === 0} onClick={() => onCommit(moveDetailTableColumn(block, column.id, "up"))}><ArrowUp size={16} aria-hidden="true" /></button>
          <button className="icon-button compact-icon-button" type="button" title="下移列" aria-label="下移列" disabled={index === block.columns.length - 1} onClick={() => onCommit(moveDetailTableColumn(block, column.id, "down"))}><ArrowDown size={16} aria-hidden="true" /></button>
          <button className="icon-button compact-icon-button" type="button" title="复制列" aria-label="复制列" onClick={() => insert(true)}><Copy size={16} aria-hidden="true" /></button>
          <button className="icon-button compact-icon-button danger-icon" type="button" title="删除列" aria-label="删除列" disabled={block.columns.length <= 1} onClick={() => { setSelectedId(block.columns[index + 1]?.id ?? block.columns[index - 1]?.id ?? ""); onCommit(removeDetailTableColumn(block, column.id)); }}><Trash2 size={16} aria-hidden="true" /></button>
        </div>
      </div>
      <div className="new-report-property-grid">
        <label><span>标题</span><CommitTextField value={column.title} onCommit={title => updateColumn({ title })} /></label>
        <NumberField label="宽度(mm)" value={column.widthMm} min={8} max={180} onCommit={widthMm => updateColumn({ widthMm })} />
        {column.contentKind !== "Composite" && <FieldPathInput selectOnly className="new-report-property-wide" label="显示内容" value={column.fieldPath} fieldGroups={fieldGroups} onChange={fieldPath => updateColumn({ fieldPath })} />}
        <label><span>对齐方式</span><select value={column.align} onChange={event => updateColumn({ align: normalizeAlign(event.target.value) })}><option value="Left">靠左</option><option value="Center">居中</option><option value="Right">靠右</option></select></label>
      </div>
      {column.contentKind === "Composite" && <fieldset className="new-report-detail-style-group"><legend>显示哪些商品信息</legend>
        {(column.content ?? []).filter(part => part.kind === "Field").map(part => <DesignerCheckbox key={part.id} checked={part.visible !== false} onChange={visible => updateColumn({ content: column.content?.map(current => current.id === part.id ? { ...current, visible } : current) })}>{fields.get(part.fieldPath) ?? "未识别的商品信息（请在组合排版中修正）"}</DesignerCheckbox>)}
      </fieldset>}
      <details className="report-designer-property-section" onInvalidCapture={event => { event.currentTarget.open = true; }}>
        <summary>组合排版</summary>
        <label><span>内容</span><select value={column.contentKind ?? "Field"} onChange={event => updateColumn(setDetailColumnContentKind(column, event.target.value === "Composite" ? "Composite" : "Field"))}>
          <option value="Field">一项商品信息</option><option value="Composite">多项信息组合排版</option>
        </select></label>
        {column.contentKind === "Composite" && <ReportDesignerDetailCompositeProperties column={column} fieldGroups={fieldGroups} onChange={updateColumn} />}
      </details>
      <details className="report-designer-property-section" onInvalidCapture={event => { event.currentTarget.open = true; }}>
        <summary>跨列表头与边框</summary>
        <label><span>跨列表头</span><CommitTextField value={column.headerGroupTitle ?? ""} onCommit={headerGroupTitle => updateColumn({ headerGroupTitle })} /></label>
        <NumberField label="跨列数" value={column.headerGroupSpan ?? 1} min={1} max={Math.min(20, block.columns.length - index)} onCommit={value => updateColumn({ headerGroupSpan: Math.floor(value) })} />
        <BorderEditor border={column.border ?? block.border} onChange={border => updateColumn({ border })} />
        {column.border && <button type="button" className="command-button secondary" onClick={() => updateColumn({ border: undefined })}>此列沿用表格边框</button>}
      </details>
    </div>
    <button className="command-button secondary" type="button" onClick={() => insert(false)}>新增列</button>
  </>;
}
