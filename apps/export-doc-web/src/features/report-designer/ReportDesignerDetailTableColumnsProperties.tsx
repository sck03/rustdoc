import { useState } from "react";
import { ArrowDown, ArrowUp, Copy, Trash2 } from "lucide-react";
import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import {
  createDetailTableCellContent,
  createDetailTableColumn,
  duplicateDetailTableColumn,
  moveDetailTableColumn,
  removeDetailTableColumn,
} from "./reportDesignerMutations.ts";
import type { ReportBlock, ReportDetailTableBlock, ReportDetailTableCellContent, ReportDetailTableColumn } from "./reportDesignerSchema.ts";
import {
  createEmptyGroupFooterCell,
  createEmptySummaryCell,
  normalizeAlign,
  normalizeDetailCellPartKind,
  normalizeNumber,
} from "./reportDesignerPropertiesModel.ts";
import { BorderEditor, FieldPathInput, DesignerCheckbox } from "./ReportDesignerPropertyControls.tsx";

export function ReportDesignerDetailTableColumnsProperties({
  block,
  fieldGroups,
  onCommit,
}: {
  block: ReportDetailTableBlock;
  fieldGroups: ReportDesignerFieldGroup[];
  onCommit: (block: ReportBlock) => void;
}) {
  const [selectedId, setSelectedId] = useState(block.columns[0]?.id ?? "");
  const selected = block.columns.some(column => column.id === selectedId) ? selectedId : block.columns[0]?.id;
  const fields = fieldGroups.flatMap(group => group.fields);
  function updateColumn(columnId: string, update: (column: ReportDetailTableColumn) => ReportDetailTableColumn) {
    onCommit({
      ...block,
      columns: block.columns.map((column) => (column.id === columnId ? update(column) : column)),
    });
  }

  function addColumnContentPart(columnId: string, kind: ReportDetailTableCellContent["kind"]) {
    updateColumn(columnId, (column) => ({
      ...column,
      contentKind: "Composite",
      content: [...(column.content ?? []), createDetailTableCellContent(kind)],
    }));
  }

  function updateColumnContentPart(
    columnId: string,
    partId: string,
    update: (part: ReportDetailTableCellContent) => ReportDetailTableCellContent,
  ) {
    updateColumn(columnId, (column) => ({
      ...column,
      content: (column.content ?? []).map((part) => (part.id === partId ? update(part) : part)),
    }));
  }

  function removeColumnContentPart(columnId: string, partId: string) {
    updateColumn(columnId, (column) => ({
      ...column,
      content: (column.content ?? []).filter((part) => part.id !== partId),
    }));
  }

  function addColumn() {
    const column = createDetailTableColumn();
    setSelectedId(column.id);
    onCommit({
      ...block,
      columns: [...block.columns, column],
      summaryRow: block.summaryRow
        ? {
            ...block.summaryRow,
            cells: [...block.summaryRow.cells, createEmptySummaryCell(column.id)],
          }
        : undefined,
      grouping: block.grouping
        ? {
            ...block.grouping,
            footer: block.grouping.footer
              ? {
                  ...block.grouping.footer,
                  cells: [...block.grouping.footer.cells, createEmptyGroupFooterCell(column.id)],
                }
              : undefined,
          }
        : undefined,
    });
  }

  return (
    <>
      <label><span>选择要修改的列</span><select value={selected} onChange={event => setSelectedId(event.target.value)}>
        {block.columns.map((column, index) => <option key={column.id} value={column.id}>{index + 1}. {column.title || "未命名列"}</option>)}
      </select></label>
      <div className="new-report-detail-column-list">
        {block.columns.map((column, index) => (
          <div
            className="new-report-detail-column-card"
            key={column.id}
            hidden={column.id !== selected}
          >
            <div className="new-report-detail-column-title">
              <strong>{column.title || `列 ${index + 1}`}</strong>
              <div className="new-report-detail-column-actions" aria-label={`列 ${index + 1} 操作`}>
                <button
                  className="icon-button compact-icon-button"
                  type="button"
                  title="上移列" aria-label="上移列"
                  disabled={index === 0}
                  onClick={() => onCommit(moveDetailTableColumn(block, column.id, "up"))}
                >
                  <ArrowUp size={16} aria-hidden="true" />
                </button>
                <button
                  className="icon-button compact-icon-button"
                  type="button"
                  title="下移列" aria-label="下移列"
                  disabled={index >= block.columns.length - 1}
                  onClick={() => onCommit(moveDetailTableColumn(block, column.id, "down"))}
                >
                  <ArrowDown size={16} aria-hidden="true" />
                </button>
                <button
                  className="icon-button compact-icon-button"
                  type="button"
                  title="复制列" aria-label="复制列"
                  onClick={() => onCommit(duplicateDetailTableColumn(block, column.id))}
                >
                  <Copy size={16} aria-hidden="true" />
                </button>
                <button
                  className="icon-button compact-icon-button danger-icon"
                  type="button"
                  title="删除列" aria-label="删除列"
                  disabled={block.columns.length <= 1}
                  onClick={() => onCommit(removeDetailTableColumn(block, column.id))}
                >
                  <Trash2 size={16} aria-hidden="true" />
                </button>
              </div>
            </div>
            <div className="new-report-property-grid">
              <label>
                <span>标题</span>
                <input value={column.title} onChange={(event) => updateColumn(column.id, (current) => ({ ...current, title: event.target.value }))} />
              </label>
              <label>
                <span>宽度(mm)</span>
                <input type="number" min={8} max={180} step="any" value={column.widthMm} onChange={event => updateColumn(column.id, current => ({...current, widthMm: normalizeNumber(event.target.value, current.widthMm)}))} />
              </label>
              {column.contentKind !== "Composite" ? <FieldPathInput selectOnly className="new-report-property-wide" label="显示内容" value={column.fieldPath} fieldGroups={fieldGroups} onChange={fieldPath => updateColumn(column.id, current => ({...current, fieldPath}))} /> : null}
              <label><span>对齐方式</span><select value={column.align} onChange={event => updateColumn(column.id, current => ({...current, align: normalizeAlign(event.target.value)}))}><option value="Left">靠左</option><option value="Center">居中</option><option value="Right">靠右</option></select></label>
            </div>
            {column.contentKind === "Composite" ? <fieldset className="new-report-detail-style-group"><legend>显示哪些商品信息</legend>
              {(column.content ?? []).filter(part => part.kind === "Field").map(part => <DesignerCheckbox key={part.id} checked={part.visible !== false} onChange={visible => updateColumnContentPart(column.id, part.id, current => ({...current, visible}))}>{fields.find(field => field.value === part.fieldPath)?.label ?? "未识别的商品信息（请在高级设置中修正）"}</DesignerCheckbox>)}
            </fieldset> : null}
            <details onInvalidCapture={event => { event.currentTarget.open = true; }}>
              <summary>高级列设置</summary>
              <div className="new-report-property-grid">
              <label>
                <span>跨列表头</span>
                <input
                  value={column.headerGroupTitle ?? ""}
                  onChange={(event) => updateColumn(column.id, (current) => ({ ...current, headerGroupTitle: event.target.value }))}
                />
              </label>
              <label>
                <span>跨列数</span>
                <input
                  type="number"
                  min={1}
                  max={block.columns.length}
                  step={1}
                  value={column.headerGroupSpan ?? 1}
                  onChange={(event) =>
                    updateColumn(column.id, (current) => ({
                      ...current,
                      headerGroupSpan: Math.max(1, Math.floor(normalizeNumber(event.target.value, current.headerGroupSpan ?? 1))),
                    }))
                  }
                />
              </label>
              <label>
                <span>内容</span>
                <select
                  value={column.contentKind ?? "Field"}
                  onChange={(event) =>
                    updateColumn(column.id, (current) => ({
                      ...current,
                      contentKind: event.target.value === "Composite" ? "Composite" : "Field",
                      content:
                        event.target.value === "Composite" && (!current.content || current.content.length === 0)
                          ? [createDetailTableCellContent("Field")]
                          : current.content,
                    }))
                  }
                >
                  <option value="Field">一项商品信息</option>
                  <option value="Composite">多项信息组合排版</option>
                </select>
              </label>
            </div>
            {column.contentKind === "Composite" ? (
              <div className="new-report-detail-style-group">
                <DesignerCheckbox checked={column.omitEmptyLines === true} onChange={checked => updateColumn(column.id, current => ({...current,omitEmptyLines:checked}))}>不显示空内容行</DesignerCheckbox>
                <div className="new-report-detail-column-title">
                  <strong>组合内容</strong>
                  <div className="new-report-detail-column-actions">
                    <button className="command-button secondary" type="button" onClick={() => addColumnContentPart(column.id, "Text")}>
                      文本
                    </button>
                    <button className="command-button secondary" type="button" onClick={() => addColumnContentPart(column.id, "Field")}>
                      字段
                    </button>
                    <button className="command-button secondary" type="button" onClick={() => addColumnContentPart(column.id, "LineBreak")}>
                      换行
                    </button>
                    <button className="command-button secondary" type="button" onClick={() => addColumnContentPart(column.id, "ColumnBreak")}>分栏</button>
                  </div>
                </div>
                <div className="new-report-detail-column-list">
                  {(column.content ?? []).map((part, partIndex) => (
                    <div className="new-report-summary-cell-editor" key={part.id}>
                      <strong>片段 {partIndex + 1}</strong>
                      <DesignerCheckbox checked={part.visible !== false} onChange={checked => updateColumnContentPart(column.id,part.id,current=>({...current,visible:checked}))}>显示此片段</DesignerCheckbox>
                      <label>
                        <span>类型</span>
                        <select
                          value={part.kind}
                          onChange={(event) =>
                            updateColumnContentPart(column.id, part.id, (current) => ({
                              ...current,
                              kind: normalizeDetailCellPartKind(event.target.value),
                              ...(event.target.value === "ColumnBreak" ? {positionPercent: current.positionPercent ?? 50} : {}),
                            }))
                          }
                        >
                          <option value="Text">固定文本</option>
                          <option value="Field">明细字段</option>
                          <option value="LineBreak">换行</option>
                          <option value="ColumnBreak">分栏对齐</option>
                        </select>
                      </label>
                      {part.kind === "Text" ? (
                        <label>
                          <span>文本</span>
                          <input
                            value={part.text}
                            onChange={(event) =>
                              updateColumnContentPart(column.id, part.id, (current) => ({
                                ...current,
                                text: event.target.value,
                              }))
                            }
                          />
                        </label>
                      ) : null}
                      {part.kind === "ColumnBreak" ? <label><span>距单元格左侧 (%)</span><input type="number" min={1} max={99} step="any" value={part.positionPercent ?? 50} onChange={event => updateColumnContentPart(column.id, part.id, current => ({...current, positionPercent:normalizeNumber(event.target.value, 50)}))} /></label> : null}
                      {part.kind === "Field" ? (
                        <FieldPathInput
                          selectOnly
                          label="字段"
                          value={part.fieldPath}
                          fieldGroups={fieldGroups}
                          onChange={(fieldPath) =>
                            updateColumnContentPart(column.id, part.id, (current) => ({
                              ...current,
                              fieldPath,
                            }))
                          }
                        />
                      ) : null}
                      <button className="command-button secondary" type="button" onClick={() => removeColumnContentPart(column.id, part.id)}>
                        删除片段
                      </button>
                    </div>
                  ))}
                </div>
              </div>
            ) : null}
            <div className="new-report-detail-style-group">
              <div className="new-report-designer-muted">列边框覆盖</div>
              <BorderEditor border={column.border ?? block.border} onChange={(border) => updateColumn(column.id, (current) => ({ ...current, border }))} />
            </div>
            </details>
          </div>
        ))}
      </div>
      <button className="command-button secondary" type="button" onClick={addColumn}>
        新增列
      </button>
    </>
  );
}
