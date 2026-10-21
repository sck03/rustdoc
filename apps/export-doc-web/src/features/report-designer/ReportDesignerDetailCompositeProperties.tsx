import { useMemo, useState } from "react";
import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import type { ReportDetailTableColumn, ReportDetailTableCellContent } from "./reportDesignerSchema.ts";
import { createDetailTableCellContent } from "./reportDesignerBlockFactories.ts";
import { normalizeDetailCellPartKind } from "./reportDesignerPropertiesModel.ts";
import { CommitTextField, DesignerCheckbox, FieldPathInput } from "./ReportDesignerPropertyControls.tsx";
import { NumberField } from "./ReportDesignerV3InspectorControls.tsx";
import { ConditionRuleProperties } from "./ReportDesignerConditionalProperties.tsx";
import { MAX_DETAIL_PARTS, moveDetailCellPart } from "./reportDesignerDetailComposite.ts";

const partNames = { Text: "固定文本", Field: "明细字段", LineBreak: "换行", ColumnBreak: "分栏对齐", Spacer: "占位行" };

export function ReportDesignerDetailCompositeProperties({ column, fieldGroups, onChange }: {
  column: ReportDetailTableColumn;
  fieldGroups: ReportDesignerFieldGroup[];
  onChange: (column: Partial<ReportDetailTableColumn>) => void;
}) {
  const [selectedId, setSelectedId] = useState("");
  const content = column.content ?? [];
  const part = content.find(part => part.id === selectedId) ?? content[0];
  const fields = useMemo(() => new Map(fieldGroups.flatMap(group => group.fields).map(field => [field.value, field.label])), [fieldGroups]);
  const conditionFields = useMemo(() => fieldGroups.map(group => ({ ...group, fields: group.fields.filter(field => !["doc_seal_path", "customs_seal_path"].includes(field.value)) })).filter(group => group.fields.length), [fieldGroups]);
  const index = content.indexOf(part);
  function update(patch: Partial<ReportDetailTableCellContent>) {
    onChange({ content: content.map(current => current.id === part?.id ? { ...current, ...patch } : current) });
  }
  function add(kind: ReportDetailTableCellContent["kind"], copy = false) {
    if (content.length >= MAX_DETAIL_PARTS) return;
    const created = createDetailTableCellContent(kind);
    const next = copy && part ? { ...structuredClone(part), id: created.id } : created;
    setSelectedId(next.id);
    const updated = [...content];
    updated.splice(part ? index + 1 : updated.length, 0, next);
    onChange({ content: updated });
  }
  return <div className="new-report-detail-style-group">
    <DesignerCheckbox checked={column.omitEmptyLines === true} onChange={omitEmptyLines => onChange({ omitEmptyLines })}>不显示空内容行</DesignerCheckbox>
    <div className="new-report-detail-parts" aria-label="列内容排列">
      {content.map((item, i) => <button key={item.id} type="button" aria-pressed={item.id === part?.id} className={`new-report-detail-part${item.visible === false ? " is-hidden" : ""}`} onClick={() => setSelectedId(item.id)}>
        <span>{i + 1}. {item.kind === "Field" ? fields.get(item.fieldPath) ?? "字段" : item.kind === "Text" ? item.text || "空文本" : partNames[item.kind]}</span>{item.visible === false && <small>隐藏</small>}
      </button>)}
    </div>
    <div className="new-report-detail-column-actions">
      {Object.entries(partNames).map(([kind, title]) => <button key={kind} type="button" className="command-button secondary" disabled={content.length >= MAX_DETAIL_PARTS} onClick={() => add(kind as ReportDetailTableCellContent["kind"])}>添加{title}</button>)}
    </div>
    {part && <>
      <label><span>编辑组合内容</span><select value={part.id} onChange={event => setSelectedId(event.target.value)}>
        {content.map((item, index) => <option key={item.id} value={item.id}>{index + 1}. {item.kind === "Field" ? fields.get(item.fieldPath) ?? item.fieldPath : item.kind === "Text" ? item.text || partNames.Text : partNames[item.kind]}</option>)}
      </select></label>
      <DesignerCheckbox checked={part.visible !== false} onChange={visible => update({ visible })}>显示此片段</DesignerCheckbox>
      <label><span>类型</span><select value={part.kind} onChange={event => {
        const kind = normalizeDetailCellPartKind(event.target.value);
        update({ kind, positionPercent: kind === "ColumnBreak" ? part.positionPercent ?? 50 : undefined, heightMm: kind === "Spacer" ? part.heightMm ?? 3 : undefined, condition: kind === "LineBreak" || kind === "ColumnBreak" ? undefined : part.condition });
      }}>
        {Object.entries(partNames).map(([kind, title]) => <option key={kind} value={kind}>{title}</option>)}
      </select></label>
      {part.kind === "Text" && <label><span>文本</span><CommitTextField multiline rows={3} value={part.text} onCommit={text => update({ text })} /></label>}
      {part.kind === "Field" && <FieldPathInput selectOnly label="字段" value={part.fieldPath} fieldGroups={fieldGroups} onChange={fieldPath => update({ fieldPath })} />}
      {part.kind === "ColumnBreak" && <NumberField label="距单元格左侧 (%)" value={part.positionPercent ?? 50} min={1} max={99} onCommit={positionPercent => update({ positionPercent })} />}
      {part.kind === "Spacer" && <NumberField label="占位行高度 (mm)" value={part.heightMm ?? 3} min={0.5} max={40} onCommit={heightMm => update({ heightMm })} />}
      {part.kind !== "LineBreak" && part.kind !== "ColumnBreak" && <>
        <DesignerCheckbox checked={Boolean(part.condition)} onChange={enabled => update({ condition: enabled ? { fieldPath: part.kind === "Field" ? part.fieldPath : column.fieldPath, operator: "HasValue", value: "" } : undefined })}>按条件显示此片段</DesignerCheckbox>
        {part.condition && <ConditionRuleProperties rule={part.condition} itemContext fieldGroups={conditionFields} onChange={patch => update({ condition: { ...part.condition!, ...patch } })} />}
      </>}
      <div className="new-report-detail-column-actions">
        <button type="button" className="command-button secondary" disabled={content.length >= MAX_DETAIL_PARTS} onClick={() => add(part.kind, true)}>复制片段</button>
        <button type="button" className="command-button secondary" disabled={index <= 0} onClick={() => onChange({ content: moveDetailCellPart(content, part.id, -1) })}>上移片段</button>
        <button type="button" className="command-button secondary" disabled={index >= content.length - 1} onClick={() => onChange({ content: moveDetailCellPart(content, part.id, 1) })}>下移片段</button>
      <button type="button" className="command-button secondary" onClick={() => onChange({ content: content.filter(current => current.id !== part.id) })}>删除片段</button>
      </div>
    </>}
  </div>;
}
