import { useMemo } from "react";
import { ArrowDown, ArrowUp, Copy, Trash2 } from "lucide-react";
import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import { addDetailTableColumn, duplicateDetailTableColumn, moveDetailTableColumn, removeDetailTableColumn, setDetailColumnContentKind, distributeDetailTableColumnWidths, REPORT_DETAIL_MAX_COLUMNS } from "./reportDesignerTableMutations.ts";
import type { ReportBlock, ReportDetailTableBlock, ReportDetailTableColumn } from "./reportDesignerSchema.ts";
import { filterDetailItemFieldGroups, normalizeAlign } from "./reportDesignerPropertiesModel.ts";
import { BorderEditor, CommitTextField, FieldPathInput, DesignerCheckbox, ColumnWidthStrip } from "./ReportDesignerPropertyControls.tsx";
import { NumberField } from "./ReportDesignerV3InspectorControls.tsx";
import { ReportDesignerDetailCompositeProperties } from "./ReportDesignerDetailCompositeProperties.tsx";
import { DeferredPropertySection } from "./ReportDesignerDeferredProperties.tsx";
import { useReportDesignerPreview } from "./ReportDesignerPreviewContext.tsx";
import { detailColumnWidths, setDetailColumnWidth } from "./reportDesignerDetailWidths.ts";

export function ReportDesignerDetailTableColumnsProperties({ block, widthMm, fieldGroups, onCommit }: {
  block: ReportDetailTableBlock;
  widthMm: number;
  fieldGroups: ReportDesignerFieldGroup[];
  onCommit: (block: ReportBlock) => void;
}) {
  const preview = useReportDesignerPreview();
  const selectedId = preview.column?.blockId === block.id ? preview.column.columnId : "";
  const setSelectedId = (id: string) => preview.selectColumn(block.id, id);
  const column = block.columns.find(column => column.id === selectedId) ?? block.columns[0];
  const index = block.columns.indexOf(column);
  const fields = preview.labels;
  const itemFields = useMemo(() => filterDetailItemFieldGroups(fieldGroups), [fieldGroups]);
  const widths = detailColumnWidths(block, widthMm);
  function updateColumn(update: Partial<ReportDetailTableColumn>) {
    onCommit({ ...block, columns: block.columns.map(current => current.id === column.id ? { ...current, ...update } : current) });
  }
  function insert(copy: boolean, position: "before" | "after" = "after") {
    const next = copy ? duplicateDetailTableColumn(block, column.id) : addDetailTableColumn(block, column.id, position);
    if (next === block) return;
    setSelectedId(next.columns[index + (copy || position === "after" ? 1 : 0)].id);
    onCommit(next);
  }
  if (!column) return null;
  return <>
    <div className="new-report-property-readout"><span>{block.columns.length} 列</span><strong>表格 {widthMm.toFixed(1)} mm{block.sideBand ? ` · 侧栏 ${block.sideBand.widthMm} mm` : ""}</strong></div>
    <ColumnWidthStrip columns={block.columns.map((item, i) => ({ id: item.id, title: item.title || `列 ${i + 1}`, width: widths[i] }))} minWidth={Math.min(8, ...widths)} unit="mm" onResizeBoundary={(id, delta) => { const i = block.columns.findIndex(item => item.id === id); onCommit(setDetailColumnWidth(block, id, widths[i] + delta, widthMm)); }} />
    <label><span>选择要修改的列</span><select value={column.id} onChange={event => setSelectedId(event.target.value)}>
      {block.columns.map((item, index) => <option key={item.id} value={item.id}>{index + 1}. {item.title || "未命名列"} · {widths[index].toFixed(1)} mm</option>)}
    </select></label>
    <div className="new-report-detail-column-card">
      <div className="new-report-detail-column-title">
        <strong>第 {index + 1} 列</strong>
        <div className="new-report-detail-column-actions" aria-label={`列 ${index + 1} 操作`}>
          <button className="icon-button compact-icon-button" type="button" title="上移列" aria-label="上移列" disabled={index === 0} onClick={() => onCommit(moveDetailTableColumn(block, column.id, "up"))}><ArrowUp size={16} aria-hidden="true" /></button>
          <button className="icon-button compact-icon-button" type="button" title="下移列" aria-label="下移列" disabled={index === block.columns.length - 1} onClick={() => onCommit(moveDetailTableColumn(block, column.id, "down"))}><ArrowDown size={16} aria-hidden="true" /></button>
          <button className="icon-button compact-icon-button" type="button" title="复制列" aria-label="复制列" disabled={block.columns.length >= REPORT_DETAIL_MAX_COLUMNS} onClick={() => insert(true)}><Copy size={16} aria-hidden="true" /></button>
          <button className="icon-button compact-icon-button danger-icon" type="button" title="删除列" aria-label="删除列" disabled={block.columns.length <= 1 || block.entryDescriptionColumnId === column.id || Boolean(block.adjustments && [block.adjustments.descriptionColumnId, block.adjustments.amountColumnId].includes(column.id))} onClick={() => { setSelectedId(block.columns[index + 1]?.id ?? block.columns[index - 1]?.id ?? ""); onCommit(removeDetailTableColumn(block, column.id)); }}><Trash2 size={16} aria-hidden="true" /></button>
        </div>
      </div>
      {(block.entryDescriptionColumnId === column.id || (block.adjustments && [block.adjustments.descriptionColumnId, block.adjustments.amountColumnId].includes(column.id))) && <small className="new-report-detail-role-note">此列用于{[block.entryDescriptionColumnId === column.id && "录入行说明", block.adjustments?.descriptionColumnId === column.id && "费用说明", block.adjustments?.amountColumnId === column.id && "调整金额"].filter(Boolean).join("、")}。删除前在“录入行与合计”中更换对应列。</small>}
      <div className="new-report-property-grid">
        <label className="new-report-property-wide"><span>标题</span><CommitTextField multiline rows={2} value={column.title} onCommit={title => updateColumn({ title })} /></label>
        {block.columns.length > 1 ? <NumberField label="打印宽度(mm)" value={widths[index]} min={Math.min(8, ...widths)} max={widths[index] + widths[index === widths.length - 1 ? index - 1 : index + 1] - Math.min(8, ...widths)} onCommit={value => onCommit(setDetailColumnWidth(block, column.id, value, widthMm))} /> : <span>打印宽度 {widths[index].toFixed(1)} mm</span>}
        {column.contentKind !== "Composite" && <FieldPathInput selectOnly className="new-report-property-wide" label="显示内容" value={column.fieldPath} fieldGroups={itemFields} onChange={fieldPath => updateColumn({ fieldPath })} />}
        <label><span>对齐方式</span><select value={column.align} onChange={event => updateColumn({ align: normalizeAlign(event.target.value) })}><option value="Left">靠左</option><option value="Center">居中</option><option value="Right">靠右</option></select></label>
      </div>
      <small className="new-report-designer-muted">改宽度时与相邻列交换空间；其余列和唛头宽度保持。</small>
      <DesignerCheckbox checked={block.entryDescriptionColumnId === column.id} onChange={checked => onCommit({ ...block, entryDescriptionColumnId: checked ? column.id : undefined })}>用此列接收录入行文字</DesignerCheckbox>
      {column.contentKind === "Composite" && <fieldset className="new-report-detail-style-group"><legend>显示哪些商品信息</legend>
        {(column.content ?? []).filter(part => part.kind === "Field").map(part => <DesignerCheckbox key={part.id} checked={part.visible !== false} onChange={visible => updateColumn({ content: column.content?.map(current => current.id === part.id ? { ...current, visible } : current) })}>{fields.get(part.fieldPath) ?? "未识别的商品信息（请在组合排版中修正）"}</DesignerCheckbox>)}
      </fieldset>}
      <DeferredPropertySection title="组合排版">
        <label><span>内容</span><select value={column.contentKind ?? "Field"} onChange={event => updateColumn(setDetailColumnContentKind(column, event.target.value === "Composite" ? "Composite" : "Field"))}>
          <option value="Field">一项商品信息</option><option value="Composite">多项信息组合排版</option>
        </select></label>
        {column.contentKind === "Composite" && <ReportDesignerDetailCompositeProperties column={column} fieldGroups={fieldGroups} onChange={updateColumn} />}
      </DeferredPropertySection>
      <DeferredPropertySection title="跨列表头与边框">
        <label><span>跨列表头</span><CommitTextField multiline rows={2} value={column.headerGroupTitle ?? ""} onCommit={headerGroupTitle => updateColumn({ headerGroupTitle })} /></label>
        <NumberField label="跨列数" value={column.headerGroupSpan ?? 1} min={1} max={Math.min(20, block.columns.length - index)} onCommit={value => updateColumn({ headerGroupSpan: Math.floor(value) })} />
        <BorderEditor border={column.border ?? block.border} onChange={border => updateColumn({ border })} />
        {column.border && <button type="button" className="command-button secondary" onClick={() => updateColumn({ border: undefined })}>此列沿用表格边框</button>}
      </DeferredPropertySection>
    </div>
    <div className="new-report-detail-column-actions">
      <button className="command-button secondary" type="button" disabled={block.columns.length >= REPORT_DETAIL_MAX_COLUMNS} onClick={() => insert(false, "before")}>在前方插入列</button>
      <button className="command-button secondary" type="button" disabled={block.columns.length >= REPORT_DETAIL_MAX_COLUMNS} onClick={() => insert(false)}>新增列</button>
      <button className="command-button secondary" type="button" onClick={() => onCommit(distributeDetailTableColumnWidths(block))}>等分列宽</button>
    </div>
  </>;
}
