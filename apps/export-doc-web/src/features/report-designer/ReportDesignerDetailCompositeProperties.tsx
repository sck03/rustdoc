import { useState } from "react";
import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import type { ReportDetailTableColumn, ReportDetailTableCellContent } from "./reportDesignerSchema.ts";
import { createDetailTableCellContent } from "./reportDesignerBlockFactories.ts";
import { normalizeDetailCellPartKind } from "./reportDesignerPropertiesModel.ts";
import { CommitTextField, DesignerCheckbox, FieldPathInput } from "./ReportDesignerPropertyControls.tsx";
import { NumberField } from "./ReportDesignerV3InspectorControls.tsx";

const partNames = { Text: "固定文本", Field: "明细字段", LineBreak: "换行", ColumnBreak: "分栏对齐" };

export function ReportDesignerDetailCompositeProperties({ column, fieldGroups, onChange }: {
  column: ReportDetailTableColumn;
  fieldGroups: ReportDesignerFieldGroup[];
  onChange: (column: Partial<ReportDetailTableColumn>) => void;
}) {
  const [selectedId, setSelectedId] = useState("");
  const content = column.content ?? [];
  const part = content.find(part => part.id === selectedId) ?? content[0];
  const fields = new Map(fieldGroups.flatMap(group => group.fields).map(field => [field.value, field.label]));
  function update(patch: Partial<ReportDetailTableCellContent>) {
    onChange({ content: content.map(current => current.id === part?.id ? { ...current, ...patch } : current) });
  }
  function add(kind: ReportDetailTableCellContent["kind"]) {
    const next = createDetailTableCellContent(kind);
    setSelectedId(next.id);
    onChange({ content: [...content, next] });
  }
  return <div className="new-report-detail-style-group">
    <DesignerCheckbox checked={column.omitEmptyLines === true} onChange={omitEmptyLines => onChange({ omitEmptyLines })}>不显示空内容行</DesignerCheckbox>
    <div className="new-report-detail-column-actions">
      {Object.entries(partNames).map(([kind, title]) => <button key={kind} type="button" className="command-button secondary" onClick={() => add(kind as ReportDetailTableCellContent["kind"])}>添加{title}</button>)}
    </div>
    {part && <>
      <label><span>编辑组合内容</span><select value={part.id} onChange={event => setSelectedId(event.target.value)}>
        {content.map((item, index) => <option key={item.id} value={item.id}>{index + 1}. {item.kind === "Field" ? fields.get(item.fieldPath) ?? item.fieldPath : item.kind === "Text" ? item.text || partNames.Text : partNames[item.kind]}</option>)}
      </select></label>
      <DesignerCheckbox checked={part.visible !== false} onChange={visible => update({ visible })}>显示此片段</DesignerCheckbox>
      <label><span>类型</span><select value={part.kind} onChange={event => update({ kind: normalizeDetailCellPartKind(event.target.value), ...(event.target.value === "ColumnBreak" ? { positionPercent: part.positionPercent ?? 50 } : {}) })}>
        {Object.entries(partNames).map(([kind, title]) => <option key={kind} value={kind}>{title}</option>)}
      </select></label>
      {part.kind === "Text" && <label><span>文本</span><CommitTextField value={part.text} onCommit={text => update({ text })} /></label>}
      {part.kind === "Field" && <FieldPathInput selectOnly label="字段" value={part.fieldPath} fieldGroups={fieldGroups} onChange={fieldPath => update({ fieldPath })} />}
      {part.kind === "ColumnBreak" && <NumberField label="距单元格左侧 (%)" value={part.positionPercent ?? 50} min={1} max={99} onCommit={positionPercent => update({ positionPercent })} />}
      <button type="button" className="command-button secondary" onClick={() => onChange({ content: content.filter(current => current.id !== part.id) })}>删除片段</button>
    </>}
  </div>;
}
